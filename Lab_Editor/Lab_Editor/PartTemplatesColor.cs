using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartTemplatesColor - 「色・明るさ」をテーマにした複合パーツの型（9つ）
//
// 【なぜ作ったか】
// ゲームの画面にカラー補正パネル（明るさ・コントラスト・彩度・色相・色温度・色フィルタ）を足したので、
// それに合わせて、色や明るさそのものを見せる敵・ギミックの「型」を増やす。
// 既存の型は形や動きの型（翼・尻尾・砲身…）だったが、ここでは次の3つを使って色と明るさを作る。
//   ・パーツの color（絵に掛ける色）と blend=add（加算合成＝光る）
//   ・モーションの brightness（明るさが山なりに変わる）と hue_cycle（色相が回る）
//   ・モーションの attack / charge トリガー（攻撃・溜めに合わせて光る）
// スクリプトではなくモーションで作るので、プレビューの「状況」（攻撃・溜め）でも動きを確かめられる。
//
// 【作り方の約束】
//   ・飾りのパーツは当たり判定を中心の2×2へ縮める（敵のパーツは触れるとダメージを受けるため、
//     大きな光の輪などが当たり判定を持つと「光に触れただけで被弾」になってしまう）。
//   ・「何番目のパーツか」は焼き込む（phase を番号から求めて書き込む）。PartIndex に頼ると、
//     あとからパーツを足したときに位相が崩れる（既存の型と同じ理由）。
//   ・色は「色相（度）」と「彩度（％）」の2つの数値で選ぶ（色の輪から選ぶのと同じ）。
// ======================================================
public static partial class PartTemplates
{
    public const string CategoryColor = "色・明るさの型";

    // ── 小さな部品 ──

    // 色相（度）・彩度(0〜1)・明度(0〜1)から "#RRGGBB" を作る
    private static string HueHex(double hue, double sat, double val = 1.0)
    {
        PartColor.HsvToRgb((float)hue, (float)Math.Clamp(sat, 0, 1), (float)Math.Clamp(val, 0, 1), out float r, out float g, out float b);
        return $"#{(int)(r * 255f + 0.5f):X2}{(int)(g * 255f + 0.5f):X2}{(int)(b * 255f + 0.5f):X2}";
    }

    // 飾りのパーツ。当たり判定は中心の2×2（触れてもダメージを受けない）。
    private static PartDef Deco(string id, string sprite, double x, double y, double w, double h, int z,
                                string color, bool additive, params PartMotion[] motions)
    {
        var p = Part(id, sprite, x, y, w, h, 0, z, false, null);
        p.hitboxWidth = 2; p.hitboxHeight = 2;
        p.hitboxOffsetX = (p.width - 2) / 2; p.hitboxOffsetY = (p.height - 2) / 2;
        p.color = color; p.blend = additive ? "add" : "";
        // 細長いパーツは、縦横を別々に伸ばして描かないと、短いほうの辺の正方形になってしまう
        p.stretch = Math.Abs(w - h) > 0.5;
        p.motions = motions.ToList();
        return p;
    }

    private static PartMotion Mot(string trigger, string kind, double amount, double period = 60, double duration = 30,
                                  double phase = 0, double axis = 0, double triggerParam = 0, int easing = 0)
        => new PartMotion
        {
            trigger = trigger, kind = kind, amount = (float)amount, period = (float)Math.Max(1, period), duration = (float)Math.Max(1, duration),
            phase = (float)phase, axis = (float)axis, trigger_param = (float)triggerParam, easing = easing,
        };

    private static List<PartTemplate> ColorTemplates() => new()
    {
        GlowHalo(), RgbOrbs(), HueRing(), Beacon(), MoonPhase(), ExposureMeter(), CameraFlash(), ChromaSplit(), ColorBars(),
    };

    private static TemplateParam HueParam(string key, string label, decimal def) => Num(key, label, 0, 360, def, 5m, 0, "0=赤、60=黄、120=緑、180=水色、240=青、300=紫");
    private static TemplateParam SatParam(string key, string label, decimal def) => Num(key, label, 0, 100, def, 5m, 0, "0=白、100=あざやかな色");

    // ── 1. 光る電球（ハロ） ──
    private static PartTemplate GlowHalo() => new()
    {
        Id = "glow_halo", Name = "💡 光る電球（まわりがぼんやり光る）", Category = CategoryColor,
        Description = "本体の中心に光の芯があり、そのまわりに大きさの違う光の輪が重なります。輪は加算合成（下の絵に明るさを足す）なので、ゲームで本当に光って見えます。外側の輪ほど少し遅れて明滅するので、光が脈打つように広がります。",
        DefaultIdPrefix = "halo",
        DefaultSprite = "img/光の円.png",
        Params =
        {
            Num("layers", "光の輪の数", 1, 5, 3), Num("radius", "いちばん外の輪の大きさ", 24, 160, 64), Num("core", "芯の大きさ", 6, 40, 16),
            HueParam("hue", "光の色（色相）", 50), SatParam("sat", "光の色（彩度）", 55),
            Num("glow", "明るさの変化", 0, 2, 0.6m, 0.05m, 2, "大きいほど、強く明るくなって戻ります"), Num("period", "明滅の周期（フレーム）", 30, 400, 120, 10),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます（光る飾りとして、ふわふわ浮く敵に向きます）。", Hp = 3, Width = 32, Height = 32, BaseName = "光る敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("layers");
            double R = v.D("radius"), core = v.D("core"), glow = v.D("glow"), period = v.D("period");
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            string haloColor = HueHex(v.D("hue"), v.D("sat") / 100.0, 0.55); // 加算なので、控えめな明るさの色にしておく
            // 外側から先に（zOrderが同じなら並び順で奥→手前になる）
            for (int i = n - 1; i >= 0; i--)
            {
                double size = core + (R - core) * (i + 1) / n;
                list.Add(Deco($"{v.IdPrefix}{i}", v.Sprite, cx - size / 2.0, cy - size / 2.0, size, size, 1, haloColor, true,
                    Mot("always", "brightness", glow, period, phase: i * 0.12),
                    Mot("always", "pulse", 0.06, period, phase: i * 0.12)));
            }
            list.Add(Deco($"{v.IdPrefix}_core", v.Sprite, cx - core / 2.0, cy - core / 2.0, core, core, 2, HueHex(v.D("hue"), v.D("sat") / 200.0), false,
                Mot("always", "brightness", glow * 0.6, period)));
            return list;
        },
    };

    // ── 2. 3原色の光球 ──
    private static PartTemplate RgbOrbs() => new()
    {
        Id = "rgb_orbs", Name = "🔴🟢🔵 3原色の光球（赤・緑・青が回る）", Category = CategoryColor,
        Description = "赤・緑・青の小さな光球が、本体のまわりを同じ間隔で回ります。光球は加算合成で、重なると光が混ざります。色フィルタ（赤・緑・青）と連動する敵や、カラー補正パネルの「色の輪」を題材にしたギミックの飾りに向きます。",
        DefaultIdPrefix = "orb",
        DefaultSprite = "img/光の円.png",
        Params =
        {
            Num("radius", "回る半径", 8, 100, 30), Num("size", "光球の大きさ", 8, 60, 20), Num("period", "1周する時間（フレーム）", 30, 600, 150, 10),
            Num("glow", "明るさの変化", 0, 2, 0.5m, 0.05m, 2), Flag("additive", "加算合成で光らせる", true),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 18, TypeNote = "「色調整敵」。範囲内の画面を指定の色へ寄せる敵に、3原色の光球が似合います。", Hp = 3, Width = 32, Height = 32, BaseName = "三原色の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double R = v.D("radius"), s = v.D("size"), period = v.D("period"), glow = v.D("glow");
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            string[] colors = { "#FF3030", "#30FF30", "#3030FF" };
            string[] names = { "R", "G", "B" };
            for (int i = 0; i < 3; i++)
                list.Add(Deco($"{v.IdPrefix}_{names[i]}", v.Sprite, cx - s / 2.0, cy - s / 2.0, s, s, 1, colors[i], v.B("additive"),
                    Mot("always", "orbit", R, period, phase: i / 3.0),
                    Mot("always", "brightness", glow, period / 2.0, phase: i / 3.0)));
            return list;
        },
    };

    // ── 3. 色相環 ──
    private static PartTemplate HueRing() => new()
    {
        Id = "hue_ring", Name = "🌈 色相環（虹色の輪が回る）", Category = CategoryColor,
        Description = "色相がひとつずつずれた光球が輪になって、本体のまわりを回ります。「色が虹色に回る」をONにすると、輪の色も少しずつ変わり続けます。カラー補正パネルの「色相」スライダーの見本のような型です。",
        DefaultIdPrefix = "hue",
        DefaultSprite = "img/白い円.png",
        Params =
        {
            Num("count", "光球の数", 3, 12, 8), Num("radius", "輪の半径", 10, 100, 32), Num("size", "光球の大きさ", 6, 40, 14),
            Num("period", "1周する時間（フレーム）", 60, 900, 240, 10), SatParam("sat", "色の鮮やかさ", 90), Flag("cycle", "色が虹色に回り続ける", true),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 10, TypeNote = "「浮遊敵」。重力を受けず浮かぶ敵に、虹色の輪が似合います。", Hp = 2, Width = 32, Height = 32, BaseName = "虹の輪の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("count");
            double R = v.D("radius"), s = v.D("size"), period = v.D("period"), sat = v.D("sat") / 100.0;
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            for (int i = 0; i < n; i++)
            {
                var motions = new List<PartMotion> { Mot("always", "orbit", R, period, phase: i / (double)n) };
                // 色が回るときは、色相のモーションが色を作る（静的な色も掛けると、違う色同士の積になって暗くなる）
                if (v.B("cycle")) motions.Add(Mot("always", "hue_cycle", sat, period, phase: i / (double)n));
                list.Add(Deco($"{v.IdPrefix}{i}", v.Sprite, cx - s / 2.0, cy - s / 2.0, s, s, 1, v.B("cycle") ? "" : HueHex(360.0 * i / n, sat), false, motions.ToArray()));
            }
            return list;
        },
    };

    // ── 4. 回転灯 ──
    private static PartTemplate Beacon() => new()
    {
        Id = "beacon", Name = "🚨 回転灯（光の帯が回る）", Category = CategoryColor,
        Description = "本体の中心で、細長い光の帯が回り続けます。帯は加算合成で光ります。「逆回りの帯も重ねる」をONにすると、逆向きに回るもう1本が加わって、光が交差します。警報・探照灯・灯台のような敵やギミックに向きます。",
        DefaultIdPrefix = "beam",
        DefaultSprite = "img/白い四角.png",
        Params =
        {
            Num("length", "帯の長さ", 20, 200, 70), Num("width", "帯の太さ", 3, 40, 10), Num("period", "1周する時間（フレーム）", 30, 600, 90, 10),
            HueParam("hue", "光の色（色相）", 0), SatParam("sat", "光の色（彩度）", 90), Num("glow", "明るさの変化", 0, 2, 0.5m, 0.05m, 2),
            Flag("twin", "逆回りの帯も重ねる", true),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます（固定砲台やギミックの飾りに向きます）。", Hp = 3, Width = 32, Height = 32, BaseName = "回転灯の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double L = v.D("length"), w = v.D("width"), period = v.D("period"), glow = v.D("glow");
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            string color = HueHex(v.D("hue"), v.D("sat") / 100.0, 0.8);
            list.Add(Deco($"{v.IdPrefix}0", v.Sprite, cx - L / 2.0, cy - w / 2.0, L, w, 1, color, true,
                Mot("always", "spin", 360, period), Mot("always", "brightness", glow, period / 2.0)));
            if (v.B("twin"))
                list.Add(Deco($"{v.IdPrefix}1", v.Sprite, cx - L / 2.0, cy - w / 2.0, L, w, 1, color, true,
                    Mot("always", "spin", -360, period * 1.5), Mot("always", "brightness", glow, period / 2.0, phase: 0.5)));
            // 中心の灯りは丸く（標準の画像が四角いときだけ、白い円の絵に替える。自分で選んだ画像ならそのまま使う）
            list.Add(Deco($"{v.IdPrefix}_lamp", v.Sprite == "img/白い四角.png" ? "img/白い円.png" : v.Sprite, cx - w, cy - w, w * 2, w * 2, 2, HueHex(v.D("hue"), v.D("sat") / 100.0 * 0.6), false,
                Mot("always", "brightness", glow, period / 2.0)));
            return list;
        },
    };

    // ── 5. 月の満ち欠け ──
    private static PartTemplate MoonPhase() => new()
    {
        Id = "moon_phase", Name = "🌓 月の満ち欠け（明るい面が入れ替わる）", Category = CategoryColor,
        Description = "明るい円の上を、暗い円がゆっくり横切ります。新月から満月、また新月へと、明暗がゆっくり入れ替わります。明るさを操る敵（暗くしたり明るくしたりする敵）の見た目に向きます。",
        DefaultIdPrefix = "moon",
        DefaultSprite = "img/白い円.png",
        Params =
        {
            Num("size", "月の大きさ", 12, 100, 36), Num("period", "満ち欠けの周期（フレーム）", 120, 1200, 480, 20),
            HueParam("hue", "月の色（色相）", 55), SatParam("sat", "月の色（彩度）", 30),
            Num("shade", "影の暗さ", 0, 100, 85, 5m, 0, "100で真っ暗"), Num("reach", "影が動く距離（月の大きさに対する倍率）", 0.5m, 2.5m, 1.3m, 0.1m, 1),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 17, TypeNote = "「明るさ操作敵」。範囲内の画面を暗くする敵に、満ち欠けする月が似合います。", Hp = 3, Width = 36, Height = 36, BaseName = "月の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double s = v.D("size"), period = v.D("period"), reach = v.D("reach");
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            double shadeVal = 1.0 - v.D("shade") / 100.0;
            list.Add(Deco($"{v.IdPrefix}_light", v.Sprite, cx - s / 2.0, cy - s / 2.0, s, s, 1, HueHex(v.D("hue"), v.D("sat") / 100.0), false,
                Mot("always", "brightness", 0.25, period / 4.0)));
            // 影：月の上から横へ動いて戻る（動き始めは月を覆っている）
            list.Add(Deco($"{v.IdPrefix}_shade", v.Sprite, cx - s / 2.0, cy - s / 2.0, s, s, 2, HueHex(240, 0.5, 0.05 + shadeVal * 0.25), false,
                Mot("always", "slide", s * reach, period, axis: 0)));
            return list;
        },
    };

    // ── 6. 露出メーター ──
    private static PartTemplate ExposureMeter() => new()
    {
        Id = "exposure_meter", Name = "🎚 露出メーター（針が暗い〜明るいを振れる）", Category = CategoryColor,
        Description = "左が暗い側（青）、右が明るい側（黄）の目盛りの上を、白い針が行き来します。カラー補正パネルの「明るさ」スライダーの見本のような型で、明るさに関わるギミックの案内板や、明暗で動く敵の飾りに向きます。",
        DefaultIdPrefix = "meter",
        DefaultSprite = "img/白い四角.png",
        Params =
        {
            Num("width", "目盛りの幅", 24, 160, 64), Num("height", "目盛りの高さ", 4, 30, 10), Num("period", "針が往復する時間（フレーム）", 60, 900, 240, 10),
            Num("gap", "本体との間隔", -40, 40, 6), Flag("above", "本体の上に出す（OFFで下）", true),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます。", Hp = 3, Width = 32, Height = 32, BaseName = "明るさ計の敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double W = v.D("width"), H = v.D("height"), period = v.D("period"), gap = v.D("gap");
            double cx = v.BodyW / 2.0;
            double y = v.B("above") ? -H - gap : v.BodyH + gap;
            list.Add(Deco($"{v.IdPrefix}_dark", v.Sprite, cx - W / 2.0, y, W / 2.0, H, 1, "#3050B0", false));
            list.Add(Deco($"{v.IdPrefix}_light", v.Sprite, cx, y, W / 2.0, H, 1, "#FFD060", false));
            double nw = Math.Max(2, H / 3.0), nh = H + 6;
            list.Add(Deco($"{v.IdPrefix}_needle", v.Sprite, cx - nw / 2.0, y - 3, nw, nh, 2, "#FFFFFF", false,
                Mot("always", "sway", W / 2.0 - nw / 2.0, period, axis: 0)));
            return list;
        },
    };

    // ── 7. フラッシュ ──
    private static PartTemplate CameraFlash() => new()
    {
        Id = "camera_flash", Name = "📸 フラッシュ（溜めて、撃つと光る）", Category = CategoryColor,
        Description = "本体の中心に大きな光があり、ふだんはほとんど見えません。攻撃の前の「溜め」の間は光が明滅しながら強まり、攻撃した瞬間にぱっと閃光します。弾を撃つ敵・突進する敵と組み合わせると、攻撃の予告と合図になります。",
        DefaultIdPrefix = "flash",
        DefaultSprite = "img/光の円.png",
        PreviewNote = "プレビューは、240フレームで1周する疑似の「溜め→攻撃」で動かしています。実際の敵では、その敵の溜めと攻撃に合わせて光ります。",
        Params =
        {
            Num("size", "光の大きさ", 16, 200, 90), HueParam("hue", "光の色（色相）", 50), SatParam("sat", "光の色（彩度）", 10),
            Num("strength", "閃光の強さ", 1, 8, 5, 0.5m, 1, "大きいほど真っ白に飛びます"), Num("flashLen", "閃光の長さ（フレーム）", 6, 60, 18),
            Num("chargeGlow", "溜めている間の明るさ", 0, 4, 1.5m, 0.1m, 1), Num("chargeSpeed", "溜めの明滅の周期（フレーム）", 8, 80, 24),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = 9, TypeNote = "「照準弾」。溜めてから狙い撃つ敵に、閃光が似合います（「敵の動きを調整」で予兆を長くするとよく見えます）。", Hp = 3, Width = 36, Height = 36, BaseName = "フラッシュ砲台" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double s = v.D("size");
            double cx = v.BodyW / 2.0, cy = v.BodyH / 2.0;
            // ふだんは暗い色（加算なのでほぼ見えない）。明るさのモーションで、溜め中と攻撃の瞬間だけ強く光る
            string dim = HueHex(v.D("hue"), v.D("sat") / 100.0, 0.12);
            list.Add(Deco($"{v.IdPrefix}0", v.Sprite, cx - s / 2.0, cy - s / 2.0, s, s, 1, dim, true,
                Mot("charge", "brightness", v.D("chargeGlow"), v.D("chargeSpeed")),
                Mot("attack", "brightness", v.D("strength"), 30, v.D("flashLen")),
                Mot("attack", "pulse", 0.35, 30, v.D("flashLen"))));
            return list;
        },
    };

    // ── 8. 色ずれ（色収差） ──
    private static PartTemplate ChromaSplit() => new()
    {
        Id = "chroma_split", Name = "🌈 色ずれ（赤・緑・青がずれて見える）", Category = CategoryColor,
        Description = "本体と同じ大きさの絵が、赤・緑・青の3枚に分かれて、少しずつずれながら重なります（レンズの色収差・グリッチの見た目）。3枚は加算合成なので、ずれていない部分は元の色に戻ります。パーツの画像には、本体と同じ絵を選んでください。",
        DefaultIdPrefix = "split",
        UsesBodySprite = true,
        Params =
        {
            Num("shift", "ずれる幅", 1, 16, 4, 0.5m, 1), Num("period", "ずれが往復する時間（フレーム）", 20, 400, 90, 10),
            Num("jitter", "ふるえ（ざらつき）", 0, 6, 1, 0.5m, 1),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます（本体と同じ絵をパーツの画像に選びます）。", Hp = 3, Width = 32, Height = 32, BaseName = "色ずれの敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            double shift = v.D("shift"), period = v.D("period"), jitter = v.D("jitter");
            string[] colors = { "#FF0000", "#00FF00", "#0000FF" };
            string[] names = { "R", "G", "B" };
            for (int i = 0; i < 3; i++)
            {
                var motions = new List<PartMotion> { Mot("always", "sway", shift, period, phase: i / 3.0, axis: i * 120.0) };
                if (jitter > 0) motions.Add(Mot("always", "shake", jitter));
                list.Add(Deco($"{v.IdPrefix}_{names[i]}", v.Sprite, 0, 0, v.BodyW, v.BodyH, 1, colors[i], true, motions.ToArray()));
            }
            return list;
        },
    };

    // ── 9. カラーバー ──
    private static PartTemplate ColorBars() => new()
    {
        Id = "color_bars", Name = "📺 カラーバー（テレビの色帯が明滅する）", Category = CategoryColor,
        Description = "テレビの調整画面のような色の帯（白・黄・水色・緑・紫・赤・青）が横に並び、順番に明るくなって消えるのを繰り返します（波のように）。色や明るさを題材にした敵・ギミックの看板に向きます。",
        DefaultIdPrefix = "bar",
        DefaultSprite = "img/白い四角.png",
        Params =
        {
            Num("bars", "帯の数", 3, 7, 7), Num("width", "帯1本の幅", 3, 24, 8), Num("height", "帯の高さ", 8, 80, 28), Num("gap", "帯の間隔", 0, 6, 1),
            Num("glow", "明滅の強さ", 0, 2, 0.8m, 0.05m, 2), Num("period", "明滅の周期（フレーム）", 30, 400, 90, 10), Flag("wave", "波のように順番に光らせる", true),
        },
        Suggest = v => new EnemySuggestion { TypeEnum = -1, TypeNote = "どの敵タイプとも組み合わせられます。", Hp = 3, Width = 40, Height = 32, BaseName = "カラーバーの敵" },
        Build = v =>
        {
            var list = new List<PartDef>();
            int n = v.I("bars");
            double w = v.D("width"), h = v.D("height"), gap = v.D("gap"), glow = v.D("glow"), period = v.D("period");
            double total = n * w + (n - 1) * gap;
            double x0 = v.BodyW / 2.0 - total / 2.0, y0 = v.BodyH / 2.0 - h / 2.0;
            string[] colors = { "#FFFFFF", "#FFFF00", "#00FFFF", "#00FF00", "#FF00FF", "#FF0000", "#0000FF" };
            for (int i = 0; i < n; i++)
                list.Add(Deco($"{v.IdPrefix}{i}", v.Sprite, x0 + i * (w + gap), y0, w, h, 1, colors[i], false,
                    Mot("always", "brightness", glow, period, phase: v.B("wave") ? i / (double)n : 0.0)));
            return list;
        },
    };
}
