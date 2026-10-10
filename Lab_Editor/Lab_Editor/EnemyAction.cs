namespace Lab_Editor;

// 多彩行動（type 23）の「行動」1つぶん。
//
// C++側 DrawPixel.cpp の EnemyAction と1:1で対応する（キー名・種類名も一致させること。
// ここに無いキーは、エディタで保存し直した瞬間にJSONから黙って消える）。
// 1つの行動は「予兆(warn) → 行動中(duration) → 後隙(recover)」の3段階で、
// 敵はこのリストを、順番・ランダム・重み付きのどれかで切り替えながら繰り返す。
public class EnemyAction
{
    public string kind { get; set; } = "wait";   // 種類（EnemyActionInfo.Kinds の Name）
    public float warn { get; set; } = 0f;        // 予兆の長さ（フレーム）。この間は構えるだけ
    public float duration { get; set; } = 30f;   // 行動中の長さ（フレーム。ジャンプは「空中にいられる上限」）
    public float recover { get; set; } = 0f;     // 後隙の長さ（フレーム）。行動のあとの休み
    public float speed { get; set; } = 1f;       // 歩く・突進・ジャンプの前進の速さ／弾の速さ／ズームの速さ（倍率）
    public float power { get; set; } = 1f;       // ジャンプ力（倍率）
    public int count { get; set; } = 1;          // 弾の本数（1回の斉射）
    public float angle { get; set; } = 0.35f;    // 弾の扇の半分の広がり（ラジアン）
    public float interval { get; set; } = 8f;    // 連射の間隔（フレーム）
    public int bursts { get; set; } = 1;         // 連射の回数
    public float range { get; set; } = 300f;     // 画面効果の届く距離／瞬間移動の最長距離（px）
    public float rangeMin { get; set; } = 120f;  // 瞬間移動の最短距離（px）
    public float level { get; set; } = 0.5f;     // 暗転：いちばん暗いときの明るさ／色変化：色の強さ／ズーム：揺れの大きさ
    public float colorR { get; set; } = 255f;    // 色変化：寄せる色
    public float colorG { get; set; } = 0f;
    public float colorB { get; set; } = 0f;
    public float weight { get; set; } = 1f;      // 重み付きの選び方のときの、選ばれやすさ
    public int dir { get; set; } = 0;            // 動く向き：0=プレイヤーへ 1=離れる 2=いま向いている向き
    public bool aimed { get; set; } = true;      // 弾：プレイヤーを狙うか（falseならいま向いている向きへ）

    public EnemyAction Clone() => (EnemyAction)MemberwiseClone();
}

// 行動の種類ごとの、表示名・説明・編集する項目。
public static class EnemyActionInfo
{
    public enum FieldType { Number, Choice, Flag, Color }

    public sealed class Field
    {
        public string Prop = "";      // EnemyAction のプロパティ名（色は colorR。G/B は続けて colorG / colorB）
        public string Label = "";
        public string Hint = "";
        public FieldType Type = FieldType.Number;
        public float Min, Max = 100, Step = 1;
        public int Decimals;
        public string[] Choices = Array.Empty<string>();
    }

    public sealed class Kind
    {
        public string Name = "";
        public string Label = "";
        public string Hint = "";
        public Color Color;           // 時間配分の図で使う色
        public List<Field> Fields = new();
    }

    private static Field Num(string prop, string label, float min, float max, float step, int dec, string hint = "")
        => new() { Prop = prop, Label = label, Min = min, Max = max, Step = step, Decimals = dec, Hint = hint };
    private static readonly string[] DirChoices = { "プレイヤーへ近づく向き", "プレイヤーから離れる向き", "いま向いている向き" };
    private static Field Dir(string label) => new() { Prop = "dir", Label = label, Type = FieldType.Choice, Choices = DirChoices };
    private static Field Warn(string hint) => Num("warn", "予兆の長さ(フレーム)", 0, 300, 1, 0, hint);
    private static Field Recover() => Num("recover", "後隙（休み）の長さ(フレーム)", 0, 600, 5, 0, "行動のあと、次の行動に移るまでの休み。長いほど反撃のチャンスになります。");

    public static readonly Kind[] Kinds =
    {
        new() { Name = "wait", Label = "待機（その場で待つ）", Hint = "その場で何もしません。行動と行動の間の「間」を作るのに使います。", Color = Color.FromArgb(90, 110, 150),
            Fields = { Num("duration", "待つ長さ(フレーム)", 1, 600, 5, 0), Recover() } },
        new() { Name = "walk", Label = "歩く", Hint = "プレイヤーへ近づく／離れる／向いている向きへ歩きます。", Color = Color.FromArgb(90, 160, 110),
            Fields = { Warn("歩き出す前の構え。"), Dir("歩く向き"), Num("speed", "歩く速さ(倍)", 0, 3, 0.05f, 2, "プレイヤーの基本の速さに対する倍率。"), Num("duration", "歩く長さ(フレーム)", 1, 600, 5, 0), Recover() } },
        new() { Name = "jump", Label = "ジャンプ", Hint = "跳んで、着地するまでが1つの行動です。", Color = Color.FromArgb(120, 200, 120),
            Fields = { Warn("跳ぶ前の屈み。"), Dir("跳ぶ向き"), Num("power", "ジャンプ力(倍)", 0.1f, 2.5f, 0.05f, 2, "プレイヤーのジャンプ力に対する倍率。"),
                       Num("speed", "前へ進む速さ(倍)", 0, 3, 0.05f, 2, "跳んでいる間の、前向きの速さ。0なら真上に跳びます。"),
                       Num("duration", "空中にいられる上限(フレーム)", 10, 300, 5, 0, "これ以上は飛びっぱなしにならない安全装置。"), Recover() } },
        new() { Name = "dash", Label = "突進", Hint = "構えてから、一直線に走ります。", Color = Color.FromArgb(230, 90, 80),
            Fields = { Warn("突進する前の溜め。長いほど読みやすくなります。"), Dir("突進する向き"), Num("speed", "突進の速さ(倍)", 0.2f, 4, 0.1f, 1),
                       Num("duration", "突進の長さ(フレーム)", 1, 300, 1, 0), Recover() } },
        new() { Name = "shoot", Label = "弾を撃つ", Hint = "本数・扇の広がり・連射・狙い方を指定して撃ちます。", Color = Color.FromArgb(240, 170, 60),
            Fields = { Warn("撃つ前の構え。"),
                       new Field { Prop = "aimed", Label = "プレイヤーを狙う", Type = FieldType.Flag, Hint = "OFFにすると、いま向いている向きへまっすぐ撃ちます。" },
                       Num("count", "弾の本数（1回に）", 1, 16, 1, 0), Num("angle", "扇の広がり(ラジアン)", 0, 3.14f, 0.05f, 2, "正面から左右にどれだけ広げるか。0.35で約20度。"),
                       Num("bursts", "連射の回数", 1, 12, 1, 0), Num("interval", "連射の間隔(フレーム)", 1, 120, 1, 0),
                       Num("speed", "弾の速さ(倍)", 0.1f, 3, 0.05f, 2), Recover() } },
        new() { Name = "teleport", Label = "瞬間移動", Hint = "プレイヤーの近くへ瞬間移動します。", Color = Color.FromArgb(200, 120, 255),
            Fields = { Warn("消える前の合図。"), Num("rangeMin", "現れる最短距離(px)", 0, 600, 5, 0), Num("range", "現れる最長距離(px)", 0, 800, 5, 0), Recover() } },
        new() { Name = "darken", Label = "暗転", Hint = "範囲内にいるあいだ、プレイヤーの画面を暗くします（近いほど強く）。", Color = Color.FromArgb(80, 80, 100),
            Fields = { Warn("暗くなり始める前の合図。"), Num("range", "効果が届く距離(px)", 20, 1000, 10, 0), Num("level", "いちばん暗いときの明るさ", 0, 1, 0.01f, 2, "0なら真っ暗、1なら変わりません。"),
                       Num("duration", "暗くしている長さ(フレーム)", 1, 600, 5, 0), Recover() } },
        new() { Name = "tint", Label = "色変化", Hint = "範囲内にいるあいだ、プレイヤーの画面を指定の色へ寄せます（近いほど強く）。", Color = Color.FromArgb(230, 120, 160),
            Fields = { Warn("色が変わり始める前の合図。"), Num("range", "効果が届く距離(px)", 20, 1000, 10, 0),
                       new Field { Prop = "colorR", Label = "寄せる色", Type = FieldType.Color },
                       Num("level", "色の強さ", 0, 1, 0.01f, 2, "0なら変わりません。"), Num("duration", "続く長さ(フレーム)", 1, 600, 5, 0), Recover() } },
        new() { Name = "zoom", Label = "ズーム", Hint = "範囲内にいるあいだ、プレイヤーの画面を拡大・縮小で揺さぶります。", Color = Color.FromArgb(120, 190, 230),
            Fields = { Warn("揺れ始める前の合図。"), Num("range", "効果が届く距離(px)", 20, 1000, 10, 0), Num("level", "揺れの大きさ", 0, 1, 0.01f, 2, "0.25なら±25％。"),
                       Num("speed", "揺れの速さ(倍)", 0.1f, 4, 0.1f, 1), Num("duration", "続く長さ(フレーム)", 1, 600, 5, 0), Recover() } },
    };

    public static Kind Get(string name) => Kinds.FirstOrDefault(k => k.Name == name) ?? Kinds[0];

    // 種類を選んだときの初期値を入れた、新しい行動
    public static EnemyAction CreateDefault(string kind)
    {
        var a = new EnemyAction { kind = kind };
        switch (kind)
        {
            case "wait": a.duration = 40; break;
            case "walk": a.warn = 5; a.duration = 60; a.speed = 0.5f; a.recover = 10; break;
            case "jump": a.warn = 15; a.duration = 80; a.power = 0.9f; a.speed = 0.6f; a.recover = 20; break;
            case "dash": a.warn = 30; a.duration = 30; a.speed = 1.2f; a.recover = 40; break;
            case "shoot": a.warn = 20; a.count = 3; a.angle = 0.3f; a.speed = 0.5f; a.recover = 30; break;
            case "teleport": a.warn = 20; a.recover = 20; break;
            case "darken": a.warn = 10; a.duration = 120; a.range = 320; a.level = 0.35f; a.recover = 30; break;
            case "tint": a.warn = 10; a.duration = 120; a.range = 320; a.level = 0.6f; a.recover = 30; break;
            case "zoom": a.warn = 10; a.duration = 120; a.range = 280; a.level = 0.25f; a.speed = 1f; a.recover = 30; break;
        }
        return a;
    }

    // 一覧の1行（「いつ・何を」を短く）
    public static string Describe(EnemyAction a)
    {
        var k = Get(a.kind);
        string label = k.Label.Split('（')[0];
        float total = a.warn + (a.kind == "shoot" ? Math.Max(1f, (a.bursts - 1) * a.interval + 1f) : a.duration) + a.recover;
        return $"{label}　（予兆{a.warn:0}→行動→休み{a.recover:0}　計 約{total:0}フレーム）";
    }
}
