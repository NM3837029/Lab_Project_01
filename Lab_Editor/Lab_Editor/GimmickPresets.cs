namespace Lab_Editor;

// ======================================================
// GimmickPresets - 色・明るさに関わるステージギミックの「設定済みプリセット」
//
// カラー補正パネルの値（明るさ・コントラスト・彩度・色相・色温度・色フィルタ）で動くギミックを、
// 「暗いと開く扉」「白黒のときだけ現れる足場」「セピア色のゾーン」のような完成形でまとめて用意する。
// 補正スイッチ(26)・補正ロック足場(27)・色調ゾーン(19)・明暗ゾーン(18)の、数値を決めた版にあたる。
//
// 「スイッチと扉のセット」は、スイッチとゲート扉の2つのギミック定義が追加される。
// ステージに置くときは、1組だけなら param は空のままで連動する（ゲート扉は、param が空なら最初に見つかった
// スイッチと連動する）。2組以上置くときは、組ごとに、スイッチと扉の param へ同じ文字（A、B…）を入れる。
// ======================================================
public sealed class GimmickRowSpec
{
    public string IdPrefix = "";
    public string Name = "";
    public int Type;
    public int Width = 32, Height = 32;
    public bool UsesUserSprite;           // ダイアログで選んだ画像を使う（ゲート扉）
    public Action<GimmickDef> Apply = _ => { };
}

public sealed class GimmickPreset : PresetInfo
{
    public List<GimmickRowSpec> Rows = new();
    public bool NeedsSprite => Rows.Any(r => r.UsesUserSprite);
}

public static class GimmickPresets
{
    public const string GroupDoor = "スイッチと扉のセット";
    public const string GroupLock = "補正ロック足場";
    public const string GroupZone = "色調・明暗ゾーン";

    public static readonly List<GimmickPreset> All = BuildAll();

    // 扉の画像の候補（最初に見つかったものを標準にする）。無ければダイアログで選んでもらう。
    public static readonly string[] GateSpriteCandidates = { "img/扉.png" }; // 旧素材（img/old）は参照しない

    private static GimmickRowSpec Switch(string idPrefix, string name, int channel, float lo, float hi)
        => new()
        {
            IdPrefix = idPrefix, Name = name, Type = 26, Width = 64, Height = 16,
            Apply = d => { d.gradeChannel = channel; d.gradeMin = lo; d.gradeMax = hi; d.seActivate = "se_size_change"; },
        };

    private static GimmickRowSpec Gate()
        => new()
        {
            IdPrefix = "gim_grade_gate_", Name = "ゲート扉（補正スイッチ用）", Type = 8, Width = 32, Height = 160, UsesUserSprite = true,
            Apply = d => { d.seActivate = "se_size_change"; },
        };

    private static GimmickRowSpec Lock(string idPrefix, string name, int channel, float lo, float hi)
        => new()
        {
            IdPrefix = idPrefix, Name = name, Type = 27, Width = 96, Height = 32,
            Apply = d => { d.gradeChannel = channel; d.gradeMin = lo; d.gradeMax = hi; },
        };

    private static GimmickRowSpec Zone(string idPrefix, string name, float tr, float tg, float tb, float sat, float hue, float contrast)
        => new()
        {
            IdPrefix = idPrefix, Name = name, Type = 19, Width = 200, Height = 200,
            Apply = d => { d.tintR = tr; d.tintG = tg; d.tintB = tb; d.zoneSat = sat; d.zoneHue = hue; d.zoneContrast = contrast; },
        };

    // スイッチと扉のセット1件分
    private static GimmickPreset DoorSet(string id, string name, string how, int channel, float lo, float hi, string sw)
        => new()
        {
            Id = id, Group = GroupDoor, Name = name,
            Description = $"カラー補正パネルで「{how}」にしている間だけスイッチが入り、扉が開きます。スイッチと扉の2つが定義として追加されます。スイッチの上には条件が文字で出て、入っている間は緑に変わります。",
            Notes = "ステージに置くとき：1組だけなら param は空のままで連動します。2組以上のときは、スイッチと扉に同じ param（A、B…）を入れてください。",
            Rows = { Switch(sw, $"補正スイッチ：{how}", channel, lo, hi), Gate() },
        };

    private static GimmickPreset LockPlat(string id, string name, string how, int channel, float lo, float hi, string prefix)
        => new()
        {
            Id = id, Group = GroupLock, Name = name,
            Description = $"「{how}」のときだけ実体化する足場です。条件が合っていない間は、半透明のゴーストで位置だけ見えます。足場の上に条件が文字で出ます。向きを反転する編集で、条件が裏返ります。",
            Rows = { Lock(prefix, $"補正ロック足場：{how}", channel, lo, hi) },
        };

    private static GimmickPreset ZonePreset(string id, string name, string description, GimmickRowSpec row)
        => new() { Id = id, Group = GroupZone, Name = name, Description = description, Notes = "範囲（大きさ）は、一覧の📏ボタンで変えられます。ゲーム中は、拡大縮小で範囲が、速度の編集で効果の強さが変わります。", Rows = { row } };

    private static List<GimmickPreset> BuildAll() => new()
    {
        // ───────────── スイッチと扉のセット ─────────────
        DoorSet("door_dark", "🌑 暗いと開く扉", "暗い（明るさ60%未満）", 0, -999f, 0.6f, "gim_grade_sw_dark_"),
        DoorSet("door_bright", "☀ 明るいと開く扉", "明るい（明るさ130%より上）", 0, 1.3f, 999f, "gim_grade_sw_bright_"),
        DoorSet("door_mono", "⬜ 白黒にすると開く扉", "白黒（彩度20%以下）", 2, -999f, 0.2f, "gim_grade_sw_mono_"),
        DoorSet("door_vivid", "🌈 あざやかにすると開く扉", "あざやか（彩度150%以上）", 2, 1.5f, 999f, "gim_grade_sw_vivid_"),
        DoorSet("door_red", "🔴 赤フィルタで開く扉", "赤フィルタ", 5, 1f, 1f, "gim_grade_sw_red_"),
        DoorSet("door_green", "🟢 緑フィルタで開く扉", "緑フィルタ", 5, 2f, 2f, "gim_grade_sw_green_"),
        DoorSet("door_blue", "🔵 青フィルタで開く扉", "青フィルタ", 5, 3f, 3f, "gim_grade_sw_blue_"),
        DoorSet("door_contrast", "🔆 コントラストを上げると開く扉", "コントラストを上げた（+50以上）", 1, 0.5f, 999f, "gim_grade_sw_contrast_"),
        DoorSet("door_warm", "🌅 暖色にすると開く扉", "暖色にした（色温度+40以上）", 4, 0.4f, 999f, "gim_grade_sw_warm_"),
        DoorSet("door_cool", "🧊 寒色にすると開く扉", "寒色にした（色温度-40以下）", 4, -999f, -0.4f, "gim_grade_sw_cool_"),
        DoorSet("door_hue", "🔄 色相をずらすと開く扉", "色相を+60〜+180度ずらした", 3, 60f, 180f, "gim_grade_sw_hue_"),

        // ───────────── 補正ロック足場 ─────────────
        LockPlat("lock_dark", "⬛ 暗いときだけ現れる足場", "暗い（明るさ60%未満）", 0, -999f, 0.6f, "gim_grade_lock_dark_"),
        LockPlat("lock_bright", "⬜ 明るいときだけ現れる足場", "明るい（明るさ130%より上）", 0, 1.3f, 999f, "gim_grade_lock_bright_"),
        LockPlat("lock_mono", "⚪ 白黒のときだけ現れる足場", "白黒（彩度20%以下）", 2, -999f, 0.2f, "gim_grade_lock_mono_"),
        LockPlat("lock_vivid", "🌈 あざやかなときだけ現れる足場", "あざやか（彩度150%以上）", 2, 1.5f, 999f, "gim_grade_lock_vivid_"),
        LockPlat("lock_contrast", "🔆 コントラストが高いときだけ現れる足場", "コントラストが高い（+50以上）", 1, 0.5f, 999f, "gim_grade_lock_contrast_"),
        LockPlat("lock_warm", "🌅 暖色のときだけ現れる足場", "暖色（色温度+40以上）", 4, 0.4f, 999f, "gim_grade_lock_warm_"),
        LockPlat("lock_cool", "🧊 寒色のときだけ現れる足場", "寒色（色温度-40以下）", 4, -999f, -0.4f, "gim_grade_lock_cool_"),
        LockPlat("lock_hue", "🔄 色相がずれているときだけ現れる足場", "色相が+60〜+180度ずれている", 3, 60f, 180f, "gim_grade_lock_hue_"),
        LockPlat("lock_zoomin", "🔍 拡大中だけ現れる足場", "拡大中（Zキー、1.15倍以上）", 6, 1.15f, 999f, "gim_grade_lock_zoomin_"),
        LockPlat("lock_zoomout", "🔭 縮小中だけ現れる足場", "縮小中（Vキー、0.95倍以下）", 7, -999f, 0.95f, "gim_grade_lock_zoomout_"),

        // ───────────── 色調・明暗ゾーン ─────────────
        ZonePreset("zone_mono", "⚫ モノクロゾーン（白黒になる）", "範囲に入っている間、画面が白黒になります。「白黒のときだけ現れる足場」や「白黒にすると開く扉」と組み合わせて、プレイヤーが自分で操作しなくても条件を満たす通路を作れます。",
            Zone("gim_zone_mono_", "モノクロゾーン", 1f, 1f, 1f, 0f, 0f, 0f)),
        ZonePreset("zone_sepia", "🟤 セピアゾーン（古い写真の色）", "範囲に入っている間、画面が茶色がかった古い写真のような色になります（彩度を下げて、黄色〜茶色を乗せます）。演出向けです。",
            Zone("gim_zone_sepia_", "セピアゾーン", 1f, 0.88f, 0.68f, 0.45f, 0f, 0.1f)),
        ZonePreset("zone_hue", "🔁 色相ずらしゾーン（色が入れ替わる）", "範囲に入っている間、画面の色相が120度ずれます（赤→緑、緑→青…）。色が入れ替わるので、色の手がかりが読みにくくなる意地悪なゾーンです。",
            Zone("gim_zone_hue_", "色相ずらしゾーン", 1f, 1f, 1f, 1f, 120f, 0f)),
        ZonePreset("zone_contrast", "🔆 ハイコントラストゾーン（くっきり）", "範囲に入っている間、明暗がくっきりして、色もあざやかになります（コントラスト+60、彩度130%）。",
            Zone("gim_zone_contrast_", "ハイコントラストゾーン", 1f, 1f, 1f, 1.3f, 0f, 0.6f)),
        ZonePreset("zone_hazy", "🌫 ぼんやりゾーン（色もうすくなる）", "範囲に入っている間、明暗の差が小さくなり、色もうすくなります（コントラスト-50、彩度60%）。遠くがかすんで見える演出です。",
            Zone("gim_zone_hazy_", "ぼんやりゾーン", 1f, 1f, 1f, 0.6f, 0f, -0.5f)),
        ZonePreset("zone_negative", "🎞 色反転ゾーン（色相が反対になる）", "範囲に入っている間、色相が180度回って反対の色になり、彩度とコントラストも上がります。ネガフィルムのような不思議な見た目です。",
            Zone("gim_zone_negative_", "色反転ゾーン", 1f, 1f, 1f, 1.4f, 180f, 0.3f)),
        new GimmickPreset
        {
            Id = "zone_night", Group = GroupZone, Name = "🌙 夜ゾーン（暗くなる。反転するとまぶしい）",
            Description = "範囲に入っている間、画面が暗くなります（明るさ30%）。「暗いと開く扉」「暗いときだけ現れる足場」と組み合わせると、夜の通路を作れます。",
            Notes = "ゲーム中に向きを反転する編集（ダブルクリック）をすると、逆に明るく（明るさ170%）なります。",
            Rows = { new GimmickRowSpec { IdPrefix = "gim_zone_night_", Name = "夜ゾーン（反転でまぶしく）", Type = 18, Width = 200, Height = 200, Apply = d => { d.darkLevel = 0.3f; d.brightLevel = 1.7f; } } },
        },
    };

    // 既存の画像から、扉の標準の画像を探す
    public static string FindGateSprite(string projectRoot)
    {
        foreach (var c in GateSpriteCandidates)
            if (File.Exists(Path.Combine(projectRoot, c.Replace('/', Path.DirectorySeparatorChar)))) return c;
        return "";
    }
}
