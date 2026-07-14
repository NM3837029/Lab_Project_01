namespace Lab_Editor;

// ======================================================
// BlockDef - Scratch風ブロックエディタのブロック定義カタログ
// Feature: Puzzle-like Behavior Scripting (M4)
//
// ここに定義される各BlockDefは、BehaviorScript.h（C++側インタプリタ）が実際に解釈する
// opコードと1対1で対応する。カタログを変更する場合は必ずBehaviorScript.hの実装も
// 合わせて確認すること。
// ======================================================

// ブロックの見た目の形（Scratchの分類に対応）
public enum BlockShape
{
    Hat,       // 「〜が始まったとき」のような、スクリプトの先頭に置く丸い上部を持つブロック
    Stack,     // 上下に他のブロックと連結する通常のブロック
    CBlock,    // Forever/If等、本体(Body)を囲む「C」字型ブロック
    Reporter,  // 数値を返す丸みを帯びた（オーバル型）ブロック。ソケットに差し込んで使う
    Boolean,   // 真偽値を返す六角形ブロック。条件ソケットに差し込んで使う
}

// カテゴリ（Scratchの配色に準拠した色分け）
public enum BlockCategory
{
    Control,    // 制御（オレンジ）
    Motion,     // 動き（青）
    Sensing,    // センシング（水色）
    Combat,     // 攻撃・演出（赤系、このゲーム独自のカテゴリ）
    Variables,  // 変数（濃いオレンジ）
    Operators,  // 演算子（緑）
}

// 引数1つぶんの入力欄の種類
public enum BlockArgType
{
    Number,     // 数値をそのまま入力（リテラル）。ただし将来的にレポーターブロックを差し込めるソケットにもなる
    Text,       // 文字列入力
    Dropdown,   // 選択式（DropdownOptionsから選ぶ）
    BoolSlot,   // 真偽値ブロック(Boolean)を差し込むソケット
    HasElse,    // (内部フラグ用、UI上の入力欄ではない)
}

public class BlockArgSpec
{
    public string Name = "";           // JSON上のキー名（例: "speed"）。BehaviorScript.hのGetNumberArg等のキーと一致させる
    public string Label = "";          // エディタ上の表示ラベル（例: "速度"）
    public BlockArgType Type = BlockArgType.Number;
    public object DefaultValue = 0f;
    public string[]? DropdownOptions = null;

    public BlockArgSpec(string name, string label, BlockArgType type, object defaultValue, string[]? dropdownOptions = null)
    {
        Name = name; Label = label; Type = type; DefaultValue = defaultValue; DropdownOptions = dropdownOptions;
    }
}

public class BlockDef
{
    public string Op = "";             // BehaviorScript.hのopコード名（"hat"の場合はハット名そのもの）
    public string DisplayName = "";    // パレット上の表示名（日本語）
    public BlockCategory Category;
    public BlockShape Shape;
    public BlockArgSpec[] Args = System.Array.Empty<BlockArgSpec>();
    public bool HasBody = false;       // C字ブロック/ハットブロックが本体(body)を持つか
    public bool HasElse = false;       // IfElseのようにelseブロックも持つか

    public BlockDef(string op, string displayName, BlockCategory category, BlockShape shape,
        BlockArgSpec[]? args = null, bool hasBody = false, bool hasElse = false)
    {
        Op = op; DisplayName = displayName; Category = category; Shape = shape;
        Args = args ?? System.Array.Empty<BlockArgSpec>();
        HasBody = hasBody; HasElse = hasElse;
    }
}

public static class BlockCatalog
{
    private static readonly string[] MoveDirOptions = { "Left", "Right", "Toward", "Away" };
    private static readonly string[] VisualEffectKinds = { "brightness", "zoom" };

    // カテゴリ別の配色（Scratchの配色に準拠。Combat/Variablesはこのゲーム向けに独自選定）
    public static System.Drawing.Color CategoryColor(BlockCategory cat) => cat switch
    {
        BlockCategory.Control => System.Drawing.Color.FromArgb(255, 171, 25),   // #FFAB19
        BlockCategory.Motion => System.Drawing.Color.FromArgb(76, 151, 255),    // #4C97FF
        BlockCategory.Sensing => System.Drawing.Color.FromArgb(92, 177, 214),   // #5CB1D6
        BlockCategory.Combat => System.Drawing.Color.FromArgb(255, 102, 128),   // #FF6680
        BlockCategory.Variables => System.Drawing.Color.FromArgb(255, 140, 26), // #FF8C1A
        BlockCategory.Operators => System.Drawing.Color.FromArgb(89, 192, 89),  // #59C059
        _ => System.Drawing.Color.Gray,
    };

    public static readonly List<BlockDef> All = new()
    {
        // ── Control: hat blocks ─────────────────────────────
        new BlockDef("OnSpawn",  "🚩 出現したとき",   BlockCategory.Control, BlockShape.Hat, hasBody: true),
        new BlockDef("OnDamaged","💥 ダメージを受けたとき", BlockCategory.Control, BlockShape.Hat, hasBody: true),
        new BlockDef("OnDeath",  "☠ 倒されたとき",    BlockCategory.Control, BlockShape.Hat, hasBody: true),

        // ── Control: C-blocks / stack ────────────────────────
        new BlockDef("Forever", "ずっと", BlockCategory.Control, BlockShape.CBlock, hasBody: true),
        new BlockDef("Repeat", "〜回繰り返す", BlockCategory.Control, BlockShape.CBlock,
            new[] { new BlockArgSpec("count", "回数", BlockArgType.Number, 10f) }, hasBody: true),
        new BlockDef("RepeatUntil", "〜になるまで繰り返す", BlockCategory.Control, BlockShape.CBlock,
            new[] { new BlockArgSpec("cond", "条件", BlockArgType.BoolSlot, null!) }, hasBody: true),
        new BlockDef("If", "もし〜なら", BlockCategory.Control, BlockShape.CBlock,
            new[] { new BlockArgSpec("cond", "条件", BlockArgType.BoolSlot, null!) }, hasBody: true),
        new BlockDef("IfElse", "もし〜なら / でなければ", BlockCategory.Control, BlockShape.CBlock,
            new[] { new BlockArgSpec("cond", "条件", BlockArgType.BoolSlot, null!) }, hasBody: true, hasElse: true),
        new BlockDef("Wait", "〜フレーム待つ", BlockCategory.Control, BlockShape.Stack,
            new[] { new BlockArgSpec("frames", "フレーム数", BlockArgType.Number, 30f) }),
        new BlockDef("WaitUntil", "〜になるまで待つ", BlockCategory.Control, BlockShape.Stack,
            new[] { new BlockArgSpec("cond", "条件", BlockArgType.BoolSlot, null!) }),

        // ── Motion ───────────────────────────────────────────
        new BlockDef("MoveDirection", "〜へ移動する 速さ", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("dir", "方向", BlockArgType.Dropdown, "Toward", MoveDirOptions),
                new BlockArgSpec("speed", "速さ", BlockArgType.Number, 2f),
            }),
        new BlockDef("ApplyImpulse", "速度を設定 vx/vy", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("vx", "vx", BlockArgType.Number, 0f),
                new BlockArgSpec("vy", "vy", BlockArgType.Number, 0f),
            }),
        new BlockDef("SetPosition", "座標を指定 x/y", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("x", "x", BlockArgType.Number, 0f),
                new BlockArgSpec("y", "y", BlockArgType.Number, 0f),
            }),
        new BlockDef("OffsetPosition", "座標を相対移動 dx/dy", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("dx", "dx", BlockArgType.Number, 0f),
                new BlockArgSpec("dy", "dy", BlockArgType.Number, 0f),
            }),
        new BlockDef("FaceTowards", "プレイヤーの方を向く", BlockCategory.Motion, BlockShape.Stack),
        new BlockDef("Oscillate", "min〜maxで振動 周期(フレーム)", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("min", "最小", BlockArgType.Number, 0f),
                new BlockArgSpec("max", "最大", BlockArgType.Number, 100f),
                new BlockArgSpec("periodFrames", "周期(フレーム)", BlockArgType.Number, 60f),
            }),

        // ── Combat / gameplay ────────────────────────────────
        new BlockDef("Shoot", "角度/速さ/威力で弾を撃つ", BlockCategory.Combat, BlockShape.Stack,
            new[] {
                new BlockArgSpec("angle", "角度(rad)", BlockArgType.Number, 0f),
                new BlockArgSpec("speed", "速さ", BlockArgType.Number, 6f),
                new BlockArgSpec("damage", "威力", BlockArgType.Number, 1f),
            }),
        new BlockDef("ShootAtPlayer", "プレイヤーへ自動照準で弾を撃つ", BlockCategory.Combat, BlockShape.Stack,
            new[] {
                new BlockArgSpec("speed", "速さ", BlockArgType.Number, 6f),
                new BlockArgSpec("damage", "威力", BlockArgType.Number, 1f),
            }),
        new BlockDef("SetInvincible", "無敵状態にする/解除する", BlockCategory.Combat, BlockShape.Stack,
            new[] { new BlockArgSpec("on", "無敵", BlockArgType.Dropdown, "true", new[] { "true", "false" }) }),
        new BlockDef("SetScale", "表示スケールを変更", BlockCategory.Combat, BlockShape.Stack,
            new[] { new BlockArgSpec("scale", "スケール", BlockArgType.Number, 1f) }),
        new BlockDef("SetVisualEffect", "画面演出をかける", BlockCategory.Combat, BlockShape.Stack,
            new[] {
                new BlockArgSpec("kind", "種類", BlockArgType.Dropdown, "brightness", VisualEffectKinds),
                new BlockArgSpec("intensity", "強さ", BlockArgType.Number, 1f),
            }),
        new BlockDef("PlaySound", "効果音を再生", BlockCategory.Combat, BlockShape.Stack,
            new[] { new BlockArgSpec("slot", "SE ID", BlockArgType.Text, "") }),

        // ── Variables ────────────────────────────────────────
        new BlockDef("SetVar", "変数を〜にする", BlockCategory.Variables, BlockShape.Stack,
            new[] {
                new BlockArgSpec("name", "変数名", BlockArgType.Text, "myVar"),
                new BlockArgSpec("value", "値", BlockArgType.Number, 0f),
            }),
        new BlockDef("ChangeVar", "変数を〜だけ変える", BlockCategory.Variables, BlockShape.Stack,
            new[] {
                new BlockArgSpec("name", "変数名", BlockArgType.Text, "myVar"),
                new BlockArgSpec("value", "値", BlockArgType.Number, 1f),
            }),
        new BlockDef("GetVar", "変数の値", BlockCategory.Variables, BlockShape.Reporter,
            new[] { new BlockArgSpec("name", "変数名", BlockArgType.Text, "myVar") }),

        // ── Sensing: reporters (数値) ────────────────────────
        new BlockDef("SelfX", "自分のX座標", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("SelfY", "自分のY座標", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("PlayerX", "プレイヤーのX座標", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("PlayerY", "プレイヤーのY座標", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("DistanceToPlayer", "プレイヤーまでの距離", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("DirectionToPlayer", "プレイヤーへの角度(rad)", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("Random", "min〜maxの乱数", BlockCategory.Sensing, BlockShape.Reporter,
            new[] {
                new BlockArgSpec("min", "最小", BlockArgType.Number, 0f),
                new BlockArgSpec("max", "最大", BlockArgType.Number, 1f),
            }),

        // ── Sensing: booleans (真偽値) ───────────────────────
        new BlockDef("IsGrounded", "地面に接地している", BlockCategory.Sensing, BlockShape.Boolean),
        new BlockDef("IsWallAhead", "進行方向に壁がある", BlockCategory.Sensing, BlockShape.Boolean),
        new BlockDef("IsGroundAhead", "進行方向の足元に地面がある", BlockCategory.Sensing, BlockShape.Boolean),

        // ── Operators: reporters ─────────────────────────────
        new BlockDef("Const", "数値", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("value", "値", BlockArgType.Number, 0f) }),
        new BlockDef("Add", "〜 + 〜", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 0f) }),
        new BlockDef("Sub", "〜 − 〜", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 0f) }),
        new BlockDef("Mul", "〜 × 〜", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 0f) }),
        new BlockDef("Div", "〜 ÷ 〜", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 1f) }),

        // ── Operators: booleans ──────────────────────────────
        new BlockDef("Gt", "〜 > 〜", BlockCategory.Operators, BlockShape.Boolean,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 0f) }),
        new BlockDef("Lt", "〜 < 〜", BlockCategory.Operators, BlockShape.Boolean,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 0f) }),
        new BlockDef("Eq", "〜 = 〜", BlockCategory.Operators, BlockShape.Boolean,
            new[] { new BlockArgSpec("a", "a", BlockArgType.Number, 0f), new BlockArgSpec("b", "b", BlockArgType.Number, 0f) }),
        new BlockDef("And", "〜 かつ 〜", BlockCategory.Operators, BlockShape.Boolean,
            new[] { new BlockArgSpec("a", "a", BlockArgType.BoolSlot, null!), new BlockArgSpec("b", "b", BlockArgType.BoolSlot, null!) }),
        new BlockDef("Or", "〜 または 〜", BlockCategory.Operators, BlockShape.Boolean,
            new[] { new BlockArgSpec("a", "a", BlockArgType.BoolSlot, null!), new BlockArgSpec("b", "b", BlockArgType.BoolSlot, null!) }),
        new BlockDef("Not", "〜ではない", BlockCategory.Operators, BlockShape.Boolean,
            new[] { new BlockArgSpec("a", "a", BlockArgType.BoolSlot, null!) }),
    };

    public static BlockDef? Find(string op) => All.Find(b => b.Op == op);

    public static IEnumerable<BlockDef> ByCategory(BlockCategory cat) => All.Where(b => b.Category == cat);
}
