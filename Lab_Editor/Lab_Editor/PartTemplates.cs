using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartTemplates - 複数のパーツで動く敵（複合オブジェクト）の「型」を集めたカタログ
//
// 【なぜ作ったか】
// 複数パーツの敵（芋虫の連なる胴体、砲台の動く砲身、ドッスンの黒目、ベロの舌、機械いもむしの体節）は、
// これまでアセットごとに職人が1つずつスクリプトを書いて作っていた。新しい敵を作りたいときに
// 「あの動きを別の絵で使い回したい」となっても、JSONを開いて式をコピーし、数値を直すしかなかった。
// ここでは、そうした動きを「型」として切り出し、数値（節の数・間隔・速さなど）と画像を入れるだけで
// 一式のパーツ（位置・大きさ・当たり判定・挙動スクリプト）を作れるようにする。
//
// 【型の出どころ】
//   ・既存の敵から取り出した型 …… 5つ。いまゲームに入っている敵のパーツ構成を、数値で調整できる形にした。
//                                  初期値は元の敵と同じ動きになる。
//   ・新しい型 ……………………… 6つ。羽ばたく翼・ふりふり尻尾・2連装砲身・開閉するはさみ・鉄球つきの鎖・周回する盾。
//
// 【作り方の約束】
//   ・位置は「本体の左上を原点」「パーツの左上」で指定する（ゲームの解釈と同じ）。
//     回転や周回は、本体の中心を基準に式へ書き込む（SetLocalOffsetの原点は本体の左上なので、
//     中心からの距離は『本体の幅/2 − パーツの幅/2』を足して表す）。
//   ・スクリプトの中の「何番目のパーツか」は PartIndex ではなく、生成時の番号を数値として焼き込む。
//     PartIndex は配列の並びで決まるため、あとから別のパーツを足したり並べ替えたりすると
//     体節の間隔や位相が崩れてしまう。焼き込んでおけば、並びが変わっても動きは変わらない。
//   ・スクリプトは BlockScriptSerializer が読む形式（hat / op の入れ子）と同じ JSON で作る。
// ======================================================

// 型が受け付ける数値（スライダー代わりの入力欄になる）
public class TemplateParam
{
    public string Key = "", Label = "", Hint = "";
    public decimal Min, Max, Default, Step = 1m;
    public int Decimals;
    public bool IsBool;
}

// 型が「この敵の設定にするとよい」と勧める値。敵を型から新しく作るときに、種類・HP・大きさなどへ反映される。
public class EnemySuggestion
{
    public int TypeEnum = 20;          // 敵タイプ（-1 なら、どのタイプにも合う＝指定しない）
    public string TypeNote = "";       // タイプの選び方の説明
    public int Hp = 3;
    public int Width = 32, Height = 32; // 本体の大きさ（当たり判定の大きさ）
    public bool BodyIgnoresTilt;       // 傾けても本体の絵は傾けない（砲台のように、回るのがパーツだけの敵）
    public float SegmentGap = -1f;     // 体節の間隔（体節で動くタイプ用。-1=指定しない）
    public bool ConsumePartOnAttack;   // 攻撃のとき体節を飛ばす
    public string BaseName = "";       // 新しい敵の名前の元
    public JArray? EnemyScript;        // 本体にも挙動スクリプトが要るとき（カスタムスクリプトの敵）
}

// 型に渡す値の入れ物（数値・画像・ID接頭辞・本体の大きさ）
public class TemplateValues
{
    public readonly Dictionary<string, decimal> V = new();
    public string Sprite = "";
    public string IdPrefix = "";
    public float BodyW = 32f, BodyH = 32f;
    public float F(string k) => (float)V[k];
    public double D(string k) => (double)V[k];
    public int I(string k) => (int)V[k];
    public bool B(string k) => V[k] != 0m;
}

// 1つの型
public class PartTemplate
{
    public string Id = "", Name = "", Category = "", Description = "", Origin = "";
    // 型を選んだ直後のプレビューで、動きの再生が当てにならないとき（プレイヤーとの位置関係で動く型など）の注意書き
    public string PreviewNote = "";
    public List<TemplateParam> Params = new();
    public string DefaultIdPrefix = "part";
    public Func<TemplateValues, EnemySuggestion> Suggest = _ => new EnemySuggestion();
    public Func<TemplateValues, List<PartDef>> Build = _ => new();

    // 初期値の入った TemplateValues を作る
    public TemplateValues NewValues(float bodyW, float bodyH, string sprite)
    {
        var v = new TemplateValues { BodyW = bodyW, BodyH = bodyH, Sprite = sprite, IdPrefix = DefaultIdPrefix };
        foreach (var p in Params) v.V[p.Key] = p.Default;
        return v;
    }
}

public static class PartTemplates
{
    public const string CategoryExisting = "既存の敵から取り出した型";
    public const string CategoryNew = "新しい型";

    public static readonly List<PartTemplate> All = BuildAll();

    // ==== スクリプトを組み立てる小さな部品 ====
    private const double D2R = Math.PI / 180.0;
    private static JToken V(double d) => new JValue(d);
    private static JObject Op(string op) => new JObject { ["op"] = op };
    private static JObject Bin(string op, JToken a, JToken b) => new JObject { ["op"] = op, ["a"] = a, ["b"] = b };
    private static JObject Add(JToken a, JToken b) => Bin("Add", a, b);
    private static JObject Sub(JToken a, JToken b) => Bin("Sub", a, b);
    private static JObject Mul(JToken a, JToken b) => Bin("Mul", a, b);
    private static JObject Sin(JToken a) => new JObject { ["op"] = "Sin", ["a"] = a };
    private static JObject Cos(JToken a) => new JObject { ["op"] = "Cos", ["a"] = a };
    private static JObject TimeOp() => Op("Time");
    private static JObject Dir() => Op("ParentDirection");     // 本体が右向きなら+1、左向きなら-1
    private static JObject Tilt() => Op("ParentTilt");         // 本体の傾き（編集ツールで回したぶん）
    private static JObject Aim() => Op("ParentAim");           // 本体が今狙っている向き
    private static JObject ToPlayer() => Op("DirectionToPlayer");
    private static JObject Wait(int frames) => new JObject { ["op"] = "Wait", ["frames"] = frames };
    private static JObject SetOffset(JToken dx, JToken dy) => new JObject { ["op"] = "SetLocalOffset", ["dx"] = dx, ["dy"] = dy };
    private static JObject SetAngle(JToken a) => new JObject { ["op"] = "SetAngle", ["angle"] = a };
    private static JObject GetVar(string name) => new JObject { ["op"] = "GetVar", ["name"] = name };

    // 「毎フレーム、これらの命令を実行し続ける」スクリプト（OnSpawn → Forever → 命令… → Wait 1）
    private static JArray Loop(params JObject[] body)
    {
        var inner = new JArray();
        foreach (var b in body) inner.Add(b);
        inner.Add(Wait(1));
        return new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = new JArray { new JObject { ["op"] = "Forever", ["body"] = inner } } } };
    }

    // パーツを1つ作る。当たり判定は表示の8割を中央に置く（端のドットに引っかからないように）。
    private static PartDef Part(string id, string sprite, double x, double y, double w, double h, int hp, int z, bool deadly, JArray? script)
    {
        int iw = Math.Max(2, (int)Math.Round(w)), ih = Math.Max(2, (int)Math.Round(h));
        int hbw = Math.Max(2, (int)Math.Round(iw * 0.8)), hbh = Math.Max(2, (int)Math.Round(ih * 0.8));
        return new PartDef
        {
            id = id, sprite = sprite,
            offsetX = (float)Math.Round(x, 1), offsetY = (float)Math.Round(y, 1),
            width = iw, height = ih,
            hitboxOffsetX = (iw - hbw) / 2, hitboxOffsetY = (ih - hbh) / 2, hitboxWidth = hbw, hitboxHeight = hbh,
            scale = 1f, hp = hp, zOrder = z, deadly = deadly,
            script = script ?? new JArray(),
        };
    }

    private static TemplateParam Num(string key, string label, decimal min, decimal max, decimal def, decimal step = 1m, int dec = 0, string hint = "")
        => new TemplateParam { Key = key, Label = label, Min = min, Max = max, Default = def, Step = step, Decimals = dec, Hint = hint };
    private static TemplateParam Flag(string key, string label, bool def, string hint = "")
        => new TemplateParam { Key = key, Label = label, IsBool = true, Default = def ? 1m : 0m, Hint = hint };

    // ==== 型の一覧 ====
    private static List<PartTemplate> BuildAll() => new()
    {
        // ───────────── 既存の敵から取り出した型 ─────────────
        SegmentedBody(), SpringChain(), AimBarrel(), WatchingEyes(), TongueChain(),
        // ───────────── 新しい型 ─────────────
        Wings(), TailWag(), TwinBarrels(), Claws(), MorningStar(), ShieldOrbiter(),
    };

    // ── 1. 多節体（敵: enemy_imomushi） ──
    // 体節が本体の後ろに連なり、各節が自分の番号ぶんずれた波を描いてくねる。
    // 横は「後ろへ gap × (番号+1)」に cos の揺れ、縦は基準 + sin の揺れ。元の敵と同じ式。
    private static PartTemplate SegmentedBody() => new()
    {
        Id = "segmented_body", Name = "🐛 多節体（うねる胴体）", Category = CategoryExisting, Origin = "enemy_imomushi（いもむし）",
        Description = "本体の後ろに体節が連なり、波打つようにくねりながら付いてきます。各体節が自分の番号ぶんずれた波を描くので、全体がなめらかにうねります。初期値は「いもむし」と同じ動きです。",
        DefaultIdPrefix = "body",
        Params =
        {
            Num("count", "体節の数", 2, 16, 7), Num("gap", "体節の間隔", 6, 48, 16),
            Num("size", "体節の大きさ", 8, 64, 24),
            Num("waveX", "横のうねり", 0, 16, 3.5m, 0.5m, 1), Num("waveY", "縦のうねり", 0, 24, 8, 0.5m, 1),
            Num("speed", "うねる速さ", 0.05m, 1.0m, 0.3m, 0.05m, 2, "大きいほど速く波打ちます"),
            Num("phase", "節ごとの波のずれ", 0, 2, 0.9m, 0.05m, 2, "大きいほど波が細かくなります"),
            Num("baseY", "縦の位置", -40, 40, 6),
        },
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 21, TypeNote = "「飛びかかり」。普段は地上を追い、近づくと体を縮めて飛びかかります（元のいもむしと同じ）。",
            Hp = 5, Width = 34, Height = 34, ConsumePartOnAttack = true, BaseName = "いもむし",
        },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("count");
            double gap = v.D("gap"), size = v.D("size"), ax = v.D("waveX"), ay = v.D("waveY"), sp = v.D("speed"), ph = v.D("phase"), by = v.D("baseY");
            for (int i = 0; i < n; i++)
            {
                // 波の位相 = 時間 × 速さ − 節の番号 × ずれ
                JToken w = Sub(Mul(TimeOp(), V(sp)), V(i * ph));
                var dx = Add(Mul(Dir(), V(-gap * (i + 1))), Mul(Cos(w), V(ax)));
                var dy = Add(V(by), Mul(Sin(w), V(ay)));
                list.Add(Part($"{v.IdPrefix}{i}", v.Sprite, -gap * (i + 1), by, size, size, 0, -1, false, Loop(SetOffset(dx, dy))));
            }
            return list;
        },
    };

    // ── 2. ばね式の体節（敵: enemy_kikai_imomushi） ──
    // 体節はスクリプトを持たず、ただ並べるだけ。動かすのは敵タイプ側（体節が前の節に追従する）。
    private static PartTemplate SpringChain() => new()
    {
        Id = "spring_chain", Name = "⛓ 連なる体節（敵タイプが動かす）", Category = CategoryExisting, Origin = "enemy_kikai_imomushi（機械いもむし）",
        Description = "体節を後ろへ一列に並べるだけの型です。動きはスクリプトではなく、敵タイプ「跳ね回る節足敵」が決めます（各節が前の節に引っ張られて付いてくる）。節の間隔は敵の設定「segmentGap」と合わせます。",
        DefaultIdPrefix = "body",
        Params = { Num("count", "体節の数", 2, 12, 6), Num("gap", "体節の間隔", 8, 48, 22), Num("size", "体節の大きさ", 10, 60, 26) },
        PreviewNote = "動きはゲームの敵タイプが決めるため、プレビューでは並びだけが見えます。",
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 22, TypeNote = "「跳ね回る節足敵」。重力を受けず、壁や床で跳ね返りながら飛び続けます。体節の間隔も自動で設定されます。",
            Hp = 3, Width = 34, Height = 34, SegmentGap = (float)v.D("gap"), BaseName = "機械いもむし",
        },
        Build = v =>
        {
            var list = new List<PartDef>();
            double gap = v.D("gap"), size = v.D("size");
            for (int i = 0; i < v.I("count"); i++)
                list.Add(Part($"{v.IdPrefix}{i}", v.Sprite, -gap * (i + 1), 0, size, size, 0, -1, false, null));
            return list;
        },
    };

    // ── 3. 照準砲身（敵: enemy_houdai） ──
    // 砲身が、本体が今狙っている向き(ParentAim)へ回る。絵と弾の向きが必ず一致する。
    private static PartTemplate AimBarrel() => new()
    {
        Id = "aim_barrel", Name = "🔫 照準砲身（狙う向きへ回る）", Category = CategoryExisting, Origin = "enemy_houdai（砲台）",
        Description = "砲身が、本体の狙っている向きへ回ります。本体の絵（台座）は動かず、回るのは砲身だけです。砲台を傾けたときも、砲身は傾けた向きを指し続けます。",
        DefaultIdPrefix = "barrel",
        Params =
        {
            Num("size", "砲身の大きさ", 12, 64, 28), Num("radius", "砲身の中心までの距離", 0, 40, 10, 1, 0, "本体の中心から、砲身の中心までの距離"),
            Flag("cancelTilt", "本体を傾けても砲身は狙いを向く", true, "OFFにすると、本体を回したぶん砲身も一緒に回ります"),
        },
        PreviewNote = "狙う向きは、プレビューでは左右に振って見せています。",
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 9, TypeNote = "「照準弾」。撃つ瞬間のプレイヤーの位置へ正確に狙います。台座は傾かず、砲身だけが回ります。",
            Hp = 3, Width = 36, Height = 36, BodyIgnoresTilt = true, BaseName = "砲台",
        },
        Build = v =>
        {
            double s = v.D("size"), r = v.D("radius");
            double cx = v.BodyW / 2.0 - s / 2.0, cy = v.BodyH / 2.0 - s / 2.0; // 砲身の左上（本体の中心に置いたとき）
            JToken aim = v.B("cancelTilt") ? Sub(Aim(), Tilt()) : Aim();
            var dx = Add(V(cx), Mul(Cos(aim), V(r)));
            var dy = Add(V(cy), Mul(Sin(aim), V(r)));
            // 絵は上向きが基準なので、狙いの向きへ +90度 して合わせる
            JToken ang = v.B("cancelTilt") ? Sub(Add(Aim(), V(Math.PI / 2)), Tilt()) : Add(Aim(), V(Math.PI / 2));
            return new List<PartDef> { Part(v.IdPrefix, v.Sprite, cx, cy - r, s, s, 0, -1, false, Loop(SetOffset(dx, dy), SetAngle(ang))) };
        },
    };

    // ── 4. プレイヤーを見る目（敵: enemy_dossun の黒目） ──
    private static PartTemplate WatchingEyes() => new()
    {
        Id = "watching_eyes", Name = "👀 プレイヤーを見る目（黒目）", Category = CategoryExisting, Origin = "enemy_dossun（ドッスン）",
        Description = "黒目がプレイヤーの方向へ寄ります。本体を傾けても、黒目はプレイヤーのほうを向き続けます。目の数・間隔・動く範囲を決められます。どの敵タイプにも使えます。",
        DefaultIdPrefix = "pupil",
        Params =
        {
            Num("eyes", "目の数", 1, 4, 2), Num("spacing", "目と目の間隔", 0, 80, 18), Num("size", "黒目の大きさ", 4, 48, 12),
            Num("reach", "黒目が動く範囲", 0, 14, 3, 0.5m, 1), Num("height", "目の高さ（％）", 0, 100, 40, 5, 0, "本体の上から何％のところに目を置くか"),
        },
        PreviewNote = "プレイヤーのいる向きは、プレビューでは左右に振って見せています。",
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます。", Hp = 3, Width = 48, Height = 48, BaseName = "目玉の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("eyes");
            double s = v.D("size"), sp = v.D("spacing"), rc = v.D("reach");
            double cy = v.BodyH * v.D("height") / 100.0 - s / 2.0;
            for (int e = 0; e < n; e++)
            {
                double cx = v.BodyW / 2.0 + (e - (n - 1) / 2.0) * sp - s / 2.0;
                JToken a = Sub(ToPlayer(), Tilt()); // 親が傾いていても、プレイヤーの方向を向く
                var dx = Add(V(cx), Mul(Cos(a), V(rc)));
                var dy = Add(V(cy), Mul(Sin(a), V(rc)));
                list.Add(Part($"{v.IdPrefix}{e}", v.Sprite, cx, cy, s, s, 0, 1, false, Loop(SetOffset(dx, dy))));
            }
            return list;
        },
    };

    // ── 5. のびる舌（敵: enemy_bero） ──
    private static PartTemplate TongueChain() => new()
    {
        Id = "tongue_chain", Name = "👅 のびる舌（待ち伏せ）", Category = CategoryExisting, Origin = "enemy_bero（ベロ）",
        Description = "プレイヤーが近づくと、舌の体節がプレイヤーの方向へ伸び、少し待ってから縮みます。伸びきる速さ・届く距離・反応する距離・休む時間を決められます。本体を反転させた間は伸びません。",
        DefaultIdPrefix = "tongue",
        Params =
        {
            Num("segs", "舌の体節の数", 1, 8, 3), Num("reach", "届く距離", 20, 160, 58), Num("trigger", "反応する距離", 60, 400, 170),
            Num("step", "伸びる速さ", 1, 20, 6, 1, 0, "1フレームに何px伸びるか"), Num("hold", "伸ばしたまま待つ（フレーム）", 0, 120, 18),
            Num("rest", "縮んだあと休む（フレーム）", 0, 180, 45),
            Num("w", "体節の幅", 6, 48, 18), Num("h", "体節の高さ", 4, 32, 10),
            Num("mouthX", "口の位置 X", -20, 80, 8), Num("mouthY", "口の位置 Y", -20, 80, 12),
        },
        PreviewNote = "舌は、プレイヤーが近づいたときだけ伸びます。プレビューでは縮んだ状態で見えます。",
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 20, TypeNote = "「カスタムスクリプト」。本体はプレイヤーのほうを向くだけで動かず、舌のパーツが動きます（本体用のスクリプトも一緒に作られます）。",
            Hp = 3, Width = 36, Height = 36, BaseName = "舌の敵",
            EnemyScript = new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = new JArray { new JObject { ["op"] = "Forever", ["body"] = new JArray {
                Op("FaceTowards"), new JObject { ["op"] = "ApplyImpulse", ["vx"] = 0.0 }, Wait(1) } } } } },
        },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("segs");
            double reach = v.D("reach"), trigger = v.D("trigger"), step = v.D("step"), mx = v.D("mouthX"), my = v.D("mouthY");
            for (int i = 0; i < n; i++)
            {
                double frac = (i + 1) / (double)n; // 先の節ほど遠くまで伸びる
                // 口から、プレイヤーの方向へ r × frac だけ離れた位置
                JObject Pos() => SetOffset(
                    Add(V(mx), Mul(Cos(ToPlayer()), Mul(GetVar("r"), V(frac)))),
                    Add(V(my), Mul(Sin(ToPlayer()), Mul(GetVar("r"), V(frac)))));
                var extend = new JObject { ["op"] = "RepeatUntil", ["cond"] = Bin("Gt", GetVar("r"), V(reach)),
                    ["body"] = new JArray { new JObject { ["op"] = "ChangeVar", ["name"] = "r", ["value"] = step }, Pos(), Wait(1) } };
                var retract = new JObject { ["op"] = "RepeatUntil", ["cond"] = Bin("Lt", GetVar("r"), V(12)),
                    ["body"] = new JArray { new JObject { ["op"] = "ChangeVar", ["name"] = "r", ["value"] = -5.0 }, Pos(), Wait(1) } };
                var cond = Bin("And", Bin("Lt", Op("DistanceToPlayer"), V(trigger)), new JObject { ["op"] = "Not", ["a"] = Op("EditFlipped") });
                var attack = new JArray { new JObject { ["op"] = "SetVar", ["name"] = "r", ["value"] = 10.0 }, extend, Wait(v.I("hold")), retract, Wait(v.I("rest")) };
                var idle = new JArray { SetOffset(V(mx), V(my)), Wait(1) };
                var body = new JArray { new JObject { ["op"] = "Forever", ["body"] = new JArray { new JObject { ["op"] = "IfElse", ["cond"] = cond, ["body"] = attack, ["else"] = idle } } } };
                var script = new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = body } };
                list.Add(Part($"{v.IdPrefix}{i}", v.Sprite, mx, my, v.D("w"), v.D("h"), 0, 1, false, script));
            }
            return list;
        },
    };

    // ── 6. はばたく翼 ──
    private static PartTemplate Wings() => new()
    {
        Id = "wings", Name = "🪽 はばたく翼（左右対称）", Category = CategoryNew,
        Description = "本体の両脇に翼が付き、付け根を支点に上下へはばたきます。左右は鏡写しに動きます。浮遊する敵・鳥・虫に向きます。",
        DefaultIdPrefix = "wing",
        Params =
        {
            Num("size", "翼の大きさ", 12, 80, 32), Num("reach", "付け根から翼の中心まで", 4, 60, 20),
            Num("amp", "はばたく幅（度）", 5, 80, 35), Num("rest", "休めの角度（度）", 0, 80, 20, 5, 0, "翼を広げたとき、水平から上へ何度開くか"),
            Num("speed", "はばたく速さ", 0.05m, 0.6m, 0.2m, 0.05m, 2), Num("hinge", "付け根の間隔（中心から）", 0, 40, 10),
            Num("hingeY", "付け根の高さ（中心から）", -40, 40, -4),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 10, TypeNote = "「浮遊敵」。重力を受けず、上下に揺れながらゆっくり近づきます。", Hp = 2, Width = 32, Height = 32, BaseName = "はばたく敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double s = v.D("size"), R = v.D("reach"), amp = v.D("amp") * D2R, rest = v.D("rest") * D2R, sp = v.D("speed");
            double hx0 = v.BodyW / 2.0, hy = v.BodyH / 2.0 + v.D("hingeY");
            foreach (int side in new[] { -1, 1 })
            {
                JToken a = Add(V(rest), Mul(Sin(Mul(TimeOp(), V(sp))), V(amp)));      // 翼の上げ角
                double hx = hx0 + side * v.D("hinge");
                var dx = Add(V(hx - s / 2.0), Mul(Mul(Cos(a), V(R)), V(side)));        // 付け根から横へ
                var dy = Sub(V(hy - s / 2.0), Mul(Sin(a), V(R)));                       // 上げ角ぶん上へ
                var ang = Mul(a, V(-side));                                             // 右の翼は反時計回りに起きる
                list.Add(Part($"{v.IdPrefix}_{(side < 0 ? "L" : "R")}", v.Sprite, hx - s / 2.0 + side * R * Math.Cos(rest), hy - s / 2.0 - R * Math.Sin(rest), s, s, 0, -1, false, Loop(SetOffset(dx, dy), SetAngle(ang))));
            }
            return list;
        },
    };

    // ── 7. ふりふり尻尾 ──
    private static PartTemplate TailWag() => new()
    {
        Id = "tail_wag", Name = "🐕 ふりふり尻尾（先ほど大きく振れる）", Category = CategoryNew,
        Description = "本体の後ろに尻尾の体節が付き、先へ行くほど大きく、少し遅れて振れます（鞭のようなしなり）。先へ向かって体節が細くなります。多節体との違いは、振れ幅が根元から先へ増えていくことです。",
        DefaultIdPrefix = "tail",
        Params =
        {
            Num("count", "体節の数", 2, 10, 5), Num("gap", "体節の間隔", 6, 32, 14), Num("amp", "先端の振れ幅（度）", 5, 90, 40),
            Num("speed", "振る速さ", 0.03m, 0.5m, 0.12m, 0.01m, 2), Num("lag", "節ごとの遅れ", 0, 1.5m, 0.5m, 0.1m, 1),
            Num("root", "根元の大きさ", 8, 48, 20), Num("taper", "先細り（1で一定）", 0.5m, 1.0m, 0.85m, 0.05m, 2), Num("baseY", "縦の位置", -20, 40, 6),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます（歩く・追いかける敵に向きます）。", Hp = 3, Width = 32, Height = 32, BaseName = "尻尾の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("count");
            double gap = v.D("gap"), amp = v.D("amp") * D2R, sp = v.D("speed"), lag = v.D("lag"), root = v.D("root"), taper = v.D("taper"), by = v.D("baseY");
            for (int i = 0; i < n; i++)
            {
                double size = Math.Max(4, root * Math.Pow(taper, i));
                // 振れ角 = sin(時間×速さ − 番号×遅れ) × 先端の振れ幅 × (番号+1)/節の数
                JToken w = Mul(Sin(Sub(Mul(TimeOp(), V(sp)), V(i * lag))), V(amp * (i + 1) / n));
                var dx = Mul(Mul(Dir(), V(-gap * (i + 1))), Cos(w));                    // 後ろへ、振れ角ぶん縮む
                var dy = Add(V(by), Mul(V(gap * (i + 1)), Sin(w)));                     // 振れ角ぶん上下へ
                list.Add(Part($"{v.IdPrefix}{i}", v.Sprite, -gap * (i + 1), by, size, size, 0, -1, false, Loop(SetOffset(dx, dy))));
            }
            return list;
        },
    };

    // ── 8. 2連装の砲身 ──
    private static PartTemplate TwinBarrels() => new()
    {
        Id = "twin_barrels", Name = "🔫🔫 2連装の砲身", Category = CategoryNew,
        Description = "砲身が2本、左右に並んでプレイヤーを狙います。2本は少し外側へ開く角度を付けられます。弾はこれまでどおり本体から出ますが、見た目が2連装になります。",
        DefaultIdPrefix = "barrel",
        Params =
        {
            Num("size", "砲身の大きさ", 12, 64, 24), Num("radius", "砲身の中心までの距離", 0, 40, 10), Num("side", "左右の間隔（中心から）", 0, 40, 9),
            Num("spread", "外へ開く角度（度）", -20, 30, 6), Flag("cancelTilt", "本体を傾けても砲身は狙いを向く", true),
        },
        PreviewNote = "狙う向きは、プレビューでは左右に振って見せています。",
        Suggest = v => new EnemySuggestion { TypeEnum = 9, TypeNote = "「照準弾」。台座は傾かず、砲身だけが回ります。", Hp = 3, Width = 36, Height = 36, BodyIgnoresTilt = true, BaseName = "2連装砲台" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double s = v.D("size"), r = v.D("radius"), sg = v.D("side"), sp = v.D("spread") * D2R;
            foreach (int side in new[] { -1, 1 })
            {
                JToken aim = Add(Aim(), V(side * sp));
                if (v.B("cancelTilt")) aim = Sub(aim, Tilt());
                double cx = v.BodyW / 2.0 + side * sg - s / 2.0, cy = v.BodyH / 2.0 - s / 2.0;
                var dx = Add(V(cx), Mul(Cos(aim), V(r)));
                var dy = Add(V(cy), Mul(Sin(aim), V(r)));
                list.Add(Part($"{v.IdPrefix}_{(side < 0 ? "L" : "R")}", v.Sprite, cx, cy - r, s, s, 0, -1, false, Loop(SetOffset(dx, dy), SetAngle(Add(aim, V(Math.PI / 2))))));
            }
            return list;
        },
    };

    // ── 9. 開閉するはさみ ──
    private static PartTemplate Claws() => new()
    {
        Id = "claws", Name = "🦀 開閉するはさみ", Category = CategoryNew,
        Description = "本体の正面に、上下2本の爪が付いて開いたり閉じたりします。爪は触れるとダメージを受けるハザードです。向きを反転させると、爪も反対側へ付きます。",
        DefaultIdPrefix = "claw",
        Params =
        {
            Num("size", "爪の大きさ", 10, 60, 22), Num("arm", "付け根から爪の中心まで", 4, 50, 16), Num("open", "開く角度（度）", 10, 80, 40),
            Num("speed", "開閉の速さ", 0.02m, 0.4m, 0.08m, 0.02m, 2), Num("hingeY", "上下の付け根の間隔", 0, 24, 5),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 0, TypeNote = "「巡回」。左右に歩き、向いている側の正面ではさみが開閉します。", Hp = 4, Width = 36, Height = 28, BaseName = "はさみの敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double s = v.D("size"), L = v.D("arm"), open = v.D("open") * D2R, sp = v.D("speed"), hy = v.D("hingeY");
            foreach (int sy in new[] { -1, 1 })
            {
                // 開き具合 0〜1 を時間で往復させ、角度にする
                JToken th = Mul(Add(Mul(Sin(Mul(TimeOp(), V(sp))), V(0.5)), V(0.5)), V(open));
                // 正面（向いている側の端）を付け根にして、上下へ角度ぶん振る
                var dx = Add(V(v.BodyW / 2.0 - s / 2.0), Mul(Dir(), Add(V(v.BodyW / 2.0), Mul(Cos(th), V(L)))));
                var dy = Add(V(v.BodyH / 2.0 - s / 2.0 + sy * hy), Mul(Mul(V(sy), Sin(th)), V(L)));
                var ang = Mul(Mul(Dir(), V(sy)), th);
                list.Add(Part($"{v.IdPrefix}_{(sy < 0 ? "U" : "D")}", v.Sprite, v.BodyW + L - s / 2.0, v.BodyH / 2.0 - s / 2.0 + sy * hy, s, s, 0, 1, true, Loop(SetOffset(dx, dy), SetAngle(ang))));
            }
            return list;
        },
    };

    // ── 10. 鉄球つきの鎖（モーニングスター） ──
    private static PartTemplate MorningStar() => new()
    {
        Id = "morning_star", Name = "⚔ 鉄球つきの鎖（モーニングスター）", Category = CategoryNew,
        Description = "本体の上の支点から鎖が垂れ、先の鉄球が振り子のように振れます。鎖は見た目だけで、鉄球に触れるとダメージを受けます。",
        DefaultIdPrefix = "chain",
        Params =
        {
            Num("links", "鎖の数", 1, 10, 4), Num("gap", "鎖の間隔", 6, 30, 12), Num("link", "鎖の大きさ", 4, 24, 10), Num("ball", "鉄球の大きさ", 16, 80, 32),
            Num("amp", "振れ幅（度）", 10, 120, 60), Num("speed", "振れる速さ", 0.02m, 0.3m, 0.06m, 0.01m, 2),
            Num("pivotY", "支点の高さ（本体の上端から）", -20, 40, 0),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 0, TypeNote = "「巡回」。左右に歩きながら、頭上で鉄球を振り回します。", Hp = 4, Width = 32, Height = 32, BaseName = "鉄球の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("links");
            double gap = v.D("gap"), link = v.D("link"), ball = v.D("ball"), amp = v.D("amp") * D2R, sp = v.D("speed");
            double px = v.BodyW / 2.0, py = v.D("pivotY");
            // 真下(π/2)を中心に、左右へ振れる
            JToken a = Add(V(Math.PI / 2), Mul(Sin(Mul(TimeOp(), V(sp))), V(amp)));
            PartDef At(string id, double r, double size, bool deadly, int z)
            {
                var dx = Add(V(px - size / 2.0), Mul(Cos(a), V(r)));
                var dy = Add(V(py - size / 2.0), Mul(Sin(a), V(r)));
                return Part(id, v.Sprite, px - size / 2.0, py + r - size / 2.0, size, size, 0, z, deadly, Loop(SetOffset(dx, dy)));
            }
            for (int i = 0; i < n; i++) list.Add(At($"{v.IdPrefix}{i}", gap * (i + 1), link, false, 0));
            list.Add(At("ball", gap * (n + 1) + ball / 2.0, ball, true, 1));
            return list;
        },
    };

    // ── 11. 周回する盾と弱点コア ──
    private static PartTemplate ShieldOrbiter() => new()
    {
        Id = "shield_orbiter", Name = "🛡 周回する盾と弱点コア", Category = CategoryNew,
        Description = "本体の中心に弱点のコアがあり、そのまわりを盾が回ります。盾は壊せず、触れるとダメージを受けます。コアは弾で壊せて、ゆっくり脈打ちます。",
        DefaultIdPrefix = "plate",
        Params =
        {
            Num("plates", "盾の数", 1, 8, 2), Num("radius", "回る半径", 8, 80, 28), Num("size", "盾の大きさ", 8, 60, 20),
            Num("speed", "回る速さ", -0.3m, 0.3m, 0.06m, 0.01m, 2, "マイナスで逆回り"), Num("core", "コアの大きさ", 6, 40, 14),
            Num("coreHp", "コアのHP", 1, 20, 3), Num("pulse", "コアの脈打ち", 0, 6, 2, 0.5m, 1),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 13, TypeNote = "「シールド」。一定間隔で無敵になり、弾を無効にします。", Hp = 3, Width = 36, Height = 36, BaseName = "盾の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("plates");
            double R = v.D("radius"), s = v.D("size"), sp = v.D("speed"), cs = v.D("core"), pulse = v.D("pulse");
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            // コア：中心で、ゆっくり上下に脈打つ
            list.Add(Part("core", v.Sprite, cx - cs / 2.0, cy - cs / 2.0, cs, cs, v.I("coreHp"), 1, false,
                Loop(SetOffset(V(cx - cs / 2.0), Add(V(cy - cs / 2.0), Mul(Sin(Mul(TimeOp(), V(0.1))), V(pulse)))))));
            for (int i = 0; i < n; i++)
            {
                JToken a = Add(V(2 * Math.PI * i / n), Mul(TimeOp(), V(sp)));
                var dx = Add(V(cx - s / 2.0), Mul(Cos(a), V(R)));
                var dy = Add(V(cy - s / 2.0), Mul(Sin(a), V(R)));
                double a0 = 2 * Math.PI * i / n;
                list.Add(Part($"{v.IdPrefix}{i}", v.Sprite, cx - s / 2.0 + Math.Cos(a0) * R, cy - s / 2.0 + Math.Sin(a0) * R, s, s, 0, 1, true, Loop(SetOffset(dx, dy))));
            }
            return list;
        },
    };
}
