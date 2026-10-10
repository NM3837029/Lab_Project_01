using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// パーツのモーション（パーツごとの「いつ・どんな動き」）。
//
// C++側 DrawPixel.cpp の PartMotion / PartMotionTrigger / PartMotionKind と1:1で対応する。
// JSON上のキー名・種類名・トリガー名は両者で完全に一致させること
// （ここに無いキーは、エディタで保存し直した瞬間にJSONから黙って消える）。
//
// 【なぜスクリプトではなく別の定義なのか】
// 「パーツごとに違う動き」「敵が攻撃したときだけ動く」を、ブロックを組まずに一覧とスライダーで作れるようにするため。
// 結果は見た目と当たり判定への上乗せだけで、スクリプトとは独立に併用できる。
public class PartMotion
{
    // いつ動くか（PartMotionInfo.Triggers の name）
    public string trigger { get; set; } = "always";
    // near=距離(px)、moving=速さのしきい値
    public float trigger_param { get; set; } = 0f;
    // どんな動きか（PartMotionInfo.Kinds の name）
    public string kind { get; set; } = "sway";
    // 大きさ（種類ごとに px / 度 / 倍率）
    public float amount { get; set; } = 8f;
    // 1周期のフレーム数（状態系のトリガーで使う）
    public float period { get; set; } = 60f;
    // 位相のずれ（0〜1 ＝ 1周期ぶん）
    public float phase { get; set; } = 0f;
    // パーツの番号ごとに足す位相（0〜1）。同じ動きを少しずつずらして並べるのに使う
    public float phase_by_index { get; set; } = 0f;
    // 1回の長さ（フレーム。イベント系のトリガーで使う）
    public float duration { get; set; } = 30f;
    // 始まるまでの遅れ（フレーム）
    public float delay { get; set; } = 0f;
    // 波の形：0=なめらか 1=三角 2=カクカク
    public int easing { get; set; } = 0;
    // 動く向き（度。0=右、90=下。親の向きに合わせて親と一緒に回る）
    public float axis { get; set; } = 0f;
    public bool enabled { get; set; } = true;

    public PartMotion Clone() => (PartMotion)MemberwiseClone();
}

// 種類・トリガーの表示名と、スライダーの範囲などの画面用情報。
public static class PartMotionInfo
{
    public sealed class TriggerInfo
    {
        public string Name = "";     // JSONの名前
        public string Label = "";    // 画面に出す名前
        public string Hint = "";     // 説明
        public bool IsEvent;         // イベント系（その瞬間から duration フレームだけ動く）か、状態系（その間ずっと動く）か
        public string? ParamLabel;   // trigger_param の名前（無ければ値を使わない）
        public float ParamMin, ParamMax, ParamDefault, ParamStep;
        public bool EnemyOnly;       // 敵のパーツでしか起きない（ギミック・アイテムでは動かない）
    }

    public sealed class KindInfo
    {
        public string Name = "";
        public string Label = "";
        public string Hint = "";
        public string AmountLabel = "";   // 「大きさ」の名前（単位つき）
        public float AmountMin, AmountMax, AmountDefault, AmountStep;
        public int AmountDecimals;
        public bool UsesAxis;             // 動く向きを使うか
        public bool UsesEasing;           // 波の形を使うか
        public bool UsesPeriod = true;    // 周期を使うか（顔を向ける等は使わない）
    }

    // 並びは C++ の enum と一致させる（順番に意味がある）
    public static readonly TriggerInfo[] Triggers =
    {
        new() { Name = "always",  Label = "いつも",                 Hint = "常に動き続けます。" },
        new() { Name = "attack",  Label = "攻撃したとき",           Hint = "弾を撃つ・突進や飛びかかりに入る・体当たりした瞬間から、長さのぶんだけ動きます。", IsEvent = true, EnemyOnly = true },
        new() { Name = "jump",    Label = "ジャンプしたとき",       Hint = "ジャンプ（飛びかかりを含む）した瞬間から、長さのぶんだけ動きます。", IsEvent = true, EnemyOnly = true },
        new() { Name = "charge",  Label = "溜めているとき",         Hint = "突進の溜め・落下の予兆・射撃の直前など、「これから行動する」間だけ動きます。", EnemyOnly = true },
        new() { Name = "active",  Label = "行動中",                 Hint = "突進中・落下中・飛びかかりの飛行中だけ動きます。", EnemyOnly = true },
        new() { Name = "hurt",    Label = "弾を受けたとき",         Hint = "弾を受けた瞬間から、長さのぶんだけ動きます。", IsEvent = true, EnemyOnly = true },
        new() { Name = "near",    Label = "プレイヤーが近いとき",   Hint = "プレイヤーが指定の距離以内にいる間だけ動きます。",
                ParamLabel = "距離(px)", ParamMin = 8, ParamMax = 800, ParamDefault = 160, ParamStep = 8 },
        new() { Name = "moving",  Label = "動いているとき",         Hint = "歩く・飛ぶ・落ちるなど、本体が動いている間だけ動きます。",
                ParamLabel = "速さ(px/フレーム)", ParamMin = 0.05f, ParamMax = 20, ParamDefault = 0.3f, ParamStep = 0.05f, EnemyOnly = true },
        new() { Name = "flipped", Label = "向きを反転されているとき", Hint = "編集ツールで向きを反転されている間だけ動きます。", EnemyOnly = true },
        new() { Name = "tilted",  Label = "傾けられているとき",     Hint = "編集ツールで傾けられている間だけ動きます。", EnemyOnly = true },
    };

    public static readonly KindInfo[] Kinds =
    {
        new() { Name = "sway",  Label = "揺れ（行き来）", Hint = "指定の向きへ、まっすぐ行き来します。",
                AmountLabel = "ふれ幅(px)", AmountMin = 0, AmountMax = 200, AmountDefault = 6, AmountStep = 1, AmountDecimals = 1, UsesAxis = true, UsesEasing = true },
        new() { Name = "swing", Label = "首振り（左右に回る）", Hint = "パーツ自身の中心で、左右に回ります。",
                AmountLabel = "振れ角(度)", AmountMin = 0, AmountMax = 180, AmountDefault = 20, AmountStep = 1, AmountDecimals = 0, UsesEasing = true },
        new() { Name = "spin",  Label = "回転（回り続ける）", Hint = "1周期で指定の角度ぶん回り続けます。360なら1周期で1回転。",
                AmountLabel = "1周期で回る角度(度)", AmountMin = -1440, AmountMax = 1440, AmountDefault = 360, AmountStep = 15, AmountDecimals = 0 },
        new() { Name = "orbit", Label = "円運動（くるくる回る）", Hint = "元の位置を中心に、小さな円を描きます。",
                AmountLabel = "半径(px)", AmountMin = 0, AmountMax = 200, AmountDefault = 8, AmountStep = 1, AmountDecimals = 1 },
        new() { Name = "pulse", Label = "ふくらみ（脈打つ）", Hint = "大きくなって戻ります。0.3なら最大1.3倍。マイナスなら縮みます。",
                AmountLabel = "ふくらみ(倍率の増分)", AmountMin = -0.9f, AmountMax = 3, AmountDefault = 0.3f, AmountStep = 0.05f, AmountDecimals = 2 },
        new() { Name = "slide", Label = "スライド（動いて戻る）", Hint = "指定の向きへなめらかに動いて、元の位置へ戻ります。",
                AmountLabel = "動く距離(px)", AmountMin = -200, AmountMax = 200, AmountDefault = 10, AmountStep = 1, AmountDecimals = 1, UsesAxis = true },
        new() { Name = "thrust", Label = "突き（素早く出てゆっくり戻る）", Hint = "指定の向きへ素早く突き出し、ゆっくり戻ります。攻撃の動きに向いています。",
                AmountLabel = "突き出す距離(px)", AmountMin = -200, AmountMax = 200, AmountDefault = 14, AmountStep = 1, AmountDecimals = 1, UsesAxis = true },
        new() { Name = "blink", Label = "点滅（薄くなる）", Hint = "周期の後半で薄くなります。",
                AmountLabel = "薄くする量(0〜1)", AmountMin = 0, AmountMax = 1, AmountDefault = 0.7f, AmountStep = 0.05f, AmountDecimals = 2 },
        new() { Name = "shake", Label = "震え（ガタガタ）", Hint = "毎フレーム、ランダムな向きへ小刻みにずれます。",
                AmountLabel = "ふるえ幅(px)", AmountMin = 0, AmountMax = 60, AmountDefault = 2, AmountStep = 0.5f, AmountDecimals = 1, UsesPeriod = false },
        new() { Name = "face_player", Label = "プレイヤーを向く", Hint = "パーツがプレイヤーのほうを向きます。絵が右向きに描かれているなら、補正は0度です。",
                AmountLabel = "向きの補正(度)", AmountMin = -180, AmountMax = 180, AmountDefault = 0, AmountStep = 5, AmountDecimals = 0, UsesPeriod = false },
    };

    public static TriggerInfo Trigger(string name) => Triggers.FirstOrDefault(t => t.Name == name) ?? Triggers[0];
    public static KindInfo Kind(string name) => Kinds.FirstOrDefault(k => k.Name == name) ?? Kinds[0];
    public static int TriggerIndex(string name) { int i = Array.FindIndex(Triggers, t => t.Name == name); return i < 0 ? 0 : i; }
    public static int KindIndex(string name) { int i = Array.FindIndex(Kinds, k => k.Name == name); return i < 0 ? 0 : i; }

    // 種類を選んだときの初期値を入れた、新しいモーション
    public static PartMotion CreateDefault(string kind)
    {
        var ki = Kind(kind);
        var m = new PartMotion { kind = ki.Name, amount = ki.AmountDefault };
        if (ki.Name == "thrust" || ki.Name == "slide") { m.trigger = "attack"; m.duration = 30; }
        else if (ki.Name == "spin") m.period = 90;
        else if (ki.Name == "face_player") m.trigger = "near";
        if (m.trigger == "near") { m.trigger_param = Trigger("near").ParamDefault; }
        return m;
    }

    // 「いつ・どんな動き」を日本語1行にまとめる（一覧の表示用）
    public static string Describe(PartMotion m)
    {
        var t = Trigger(m.trigger);
        var k = Kind(m.kind);
        string when = t.Label;
        if (t.ParamLabel != null) when += $"（{m.trigger_param:0.##}）";
        return $"{when} → {k.Label.Split('（')[0]}";
    }
}
