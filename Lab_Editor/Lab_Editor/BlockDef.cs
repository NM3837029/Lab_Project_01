namespace Lab_Editor;

// ======================================================
// BlockDef - Scratch風ブロックエディタで使用する「ブロックの定義カタログ」
// Feature: Puzzle-like Behavior Scripting (M4)
//
// このファイルでは、ブロックエディタのパレットに並べる命令ブロック（ハット/スタック/
// C字/レポーター/真偽値の各種）を、種類ごとに1つずつBlockDefとして定義している。
// ここに定義される各BlockDefのOp（op名）は、BehaviorScript.h（C++側のスクリプト
// インタプリタ）が実際に解釈するopコード文字列と1対1で対応している。
// そのため、このカタログにブロックを追加・変更・削除した場合は、必ずBehaviorScript.h
// 側の実装（対応するopの処理）も合わせて修正・確認すること。片方だけ変更すると、
// エディタ上では組み立てられるのにゲーム内では正しく動かない、という不具合になる。
// ======================================================

// ブロックの見た目の形を表す列挙型（Scratchにおけるブロック分類にほぼ対応している）。
// 見た目の形によって、BlockRenderer側の描画方法や、他のブロックと接続できるかどうかの
// ルールが変わる。
public enum BlockShape
{
    Hat,       // 「〜が始まったとき」のような、スクリプトの先頭に置く丸い上部を持つブロック。他のブロックの下には接続できない
    Stack,     // 上下に他のブロックと連結する通常の命令ブロック。最も基本的な形
    CBlock,    // Forever/If等、本体(Body)のブロック列を囲む「C」字型ブロック。中に他のブロックを入れ子にできる
    Reporter,  // 数値を返す丸みを帯びた（オーバル型）ブロック。他のブロックの数値入力ソケットに差し込んで使う
    Boolean,   // 真偽値(true/false)を返す六角形ブロック。If等の条件ソケットに差し込んで使う
}

// ブロックのカテゴリを表す列挙型。パレット上でのグループ分けと、ブロックの配色に使われる
// （配色はScratchの配色ルールに準拠させている）。
public enum BlockCategory
{
    Control,    // 制御系のブロック（繰り返し・条件分岐・待機など）。配色はオレンジ
    Motion,     // 動き系のブロック（移動・座標操作など）。配色は青
    Sensing,    // センシング系のブロック（座標取得・距離判定など）。配色は水色
    Combat,     // 攻撃・演出系のブロック（弾を撃つ・無敵にする等）。このゲーム独自に追加したカテゴリで、配色は赤系
    Variables,  // 変数の読み書きを行うブロック。配色は濃いオレンジ
    Operators,  // 四則演算・比較・論理演算などの演算子ブロック。配色は緑
}

// ブロックが持つ引数（入力欄）1つぶんの種類を表す列挙型。BlockArgSpec.Typeに指定して使う。
public enum BlockArgType
{
    Number,     // 数値をそのまま入力するリテラル欄。現状は直接数字を打ち込むが、将来的にはレポーターブロックを差し込めるソケットにも拡張する想定
    Text,       // 文字列を入力する欄（変数名やSE IDなど）
    Dropdown,   // あらかじめ用意した選択肢(DropdownOptions)の中から1つを選ぶ欄
    BoolSlot,   // 真偽値を返すブロック(BlockShape.Boolean)を差し込むためのソケット
    HasElse,    // UI上に表示される入力欄ではなく、内部的なフラグとして扱うための値（入力欄の種類としては使用しない）
}

// ブロックが持つ引数（入力欄）1つぶんの仕様を表すクラス。
// BlockDefのArgs配列の要素として使われ、パレット/キャンバス上で入力欄を1つ生成するための
// 情報（内部名・表示名・種類・初期値・選択肢）をまとめて保持する。
public class BlockArgSpec
{
    public string Name = "";           // JSON上のキー名（例: "speed"）。BehaviorScript.hのGetNumberArg等のキーと一致させる
    public string Label = "";          // エディタ上の表示ラベル（例: "速度"）。日本語で分かりやすい名前を付ける
    public BlockArgType Type = BlockArgType.Number;   // 入力欄の種類（数値/文字列/選択式/真偽値ソケットなど）
    public object DefaultValue = 0f;                  // ブロックを新規生成したときの初期値
    public string[]? DropdownOptions = null;          // Type=Dropdownのときに選択肢として表示する文字列一覧（それ以外の種類では未使用）

    // BlockArgSpecを生成するコンストラクタ。
    // name           : JSON上のキー名
    // label          : エディタ上の表示ラベル
    // type           : 入力欄の種類
    // defaultValue   : 初期値
    // dropdownOptions: ドロップダウン選択肢（省略可。Dropdown以外の種類では不要）
    public BlockArgSpec(string name, string label, BlockArgType type, object defaultValue, string[]? dropdownOptions = null)
    {
        Name = name; Label = label; Type = type; DefaultValue = defaultValue; DropdownOptions = dropdownOptions;
    }
}

// 1種類の命令ブロックの定義（見た目・カテゴリ・引数・本体の有無など）をまとめたクラス。
// BlockCatalog.All に列挙されている各要素がこのBlockDefのインスタンスであり、
// パレットに表示するブロックの種類そのものを表す（実際にキャンバスに置かれた
// ブロック1個1個の状態はBlockInstance側が別途保持する）。
public class BlockDef
{
    public string Op = "";             // BehaviorScript.hのopコード名（"hat"の場合はハット名そのもの）。C++側の解釈と対応させる必須のキー
    public string DisplayName = "";    // パレット/キャンバス上に表示される名前（日本語。絵文字付きのものもある）
    public BlockCategory Category;     // このブロックが属するカテゴリ（配色とパレット上のグループ分けに使われる）
    public BlockShape Shape;           // このブロックの見た目の形（Hat/Stack/CBlock/Reporter/Boolean）
    public BlockArgSpec[] Args = System.Array.Empty<BlockArgSpec>();  // このブロックが持つ引数（入力欄）の一覧。引数がなければ空配列
    public bool HasBody = false;       // C字ブロック/ハットブロックが、内側に入れ子のブロック列(本体/body)を持つかどうか
    public bool HasElse = false;       // IfElseのように、本体(body)とは別に「でなければ」側のブロック列(else)も持つかどうか

    // BlockDefを生成するコンストラクタ。
    // op          : BehaviorScript.h側のopコード名
    // displayName : パレット上の表示名
    // category    : ブロックのカテゴリ（色分け用）
    // shape       : ブロックの見た目の形
    // args        : 引数の一覧（省略時は引数なし）
    // hasBody     : 本体(body)を持つブロックかどうか（省略時はfalse）
    // hasElse     : 「でなければ」側(else)も持つブロックかどうか（省略時はfalse）
    public BlockDef(string op, string displayName, BlockCategory category, BlockShape shape,
        BlockArgSpec[]? args = null, bool hasBody = false, bool hasElse = false)
    {
        Op = op; DisplayName = displayName; Category = category; Shape = shape;
        Args = args ?? System.Array.Empty<BlockArgSpec>();
        HasBody = hasBody; HasElse = hasElse;
    }
}

// このゲームで使えるすべてのブロック定義をまとめたカタログ（静的クラス）。
// パレット表示・キャンバスへの新規ブロック生成・カテゴリ色の取得など、
// ブロックエディタ全体がこのクラスを通じてブロック定義にアクセスする。
public static class BlockCatalog
{
    // MoveDirection ブロックの「方向」ドロップダウンで選べる選択肢一覧
    private static readonly string[] MoveDirOptions = { "Left", "Right", "Toward", "Away" };
    // SetVisualEffect ブロックの「種類」ドロップダウンで選べる選択肢一覧
    private static readonly string[] VisualEffectKinds = { "brightness", "zoom" };

    // カテゴリごとの配色を返す。Scratchの配色ルールに準拠させつつ、
    // Combat（攻撃・演出）とVariables（変数）はこのゲーム向けに独自に色を選定している。
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

    // このゲームで使用できる全ブロックの定義一覧。
    // パレットにはBlockCategoryごとにグループ化されて表示される（BuildItems参照）。
    // 新しいブロックを追加する場合は、ここに1行追加するのと合わせて、必ず
    // BehaviorScript.h側にも対応するopの処理を実装すること。
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
        // Feature: Composite Multi-Part Objects (Parts-M2) — パーツ(部品)の位置・角度を制御する
        new BlockDef("SetLocalOffset", "親からの相対位置を設定 dx/dy", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("dx", "dx", BlockArgType.Number, 0f),
                new BlockArgSpec("dy", "dy", BlockArgType.Number, 0f),
            }),
        new BlockDef("SetLocalOffsetPolar", "親から角度/半径で相対位置を設定", BlockCategory.Motion, BlockShape.Stack,
            new[] {
                new BlockArgSpec("angle", "角度(rad)", BlockArgType.Number, 0f),
                new BlockArgSpec("radius", "半径", BlockArgType.Number, 0f),
            }),
        new BlockDef("SetAngle", "見た目の回転角を設定(rad)", BlockCategory.Motion, BlockShape.Stack,
            new[] { new BlockArgSpec("angle", "角度(rad)", BlockArgType.Number, 0f) }),

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
        // Feature: Composite Multi-Part Objects (Parts-M2)
        new BlockDef("Time", "経過フレーム数(全体時計)", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("ParentX", "親(本体)のX座標", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("ParentY", "親(本体)のY座標", BlockCategory.Sensing, BlockShape.Reporter),
        new BlockDef("PartIndex", "自分のパーツ番号(0始まり)", BlockCategory.Sensing, BlockShape.Reporter),

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
        // Feature: Composite Multi-Part Objects (Parts-M2) — 汎用の三角関数（回転・周回運動の自作に使う）
        new BlockDef("Sin", "sin(〜) [ラジアン]", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("a", "角度(rad)", BlockArgType.Number, 0f) }),
        new BlockDef("Cos", "cos(〜) [ラジアン]", BlockCategory.Operators, BlockShape.Reporter,
            new[] { new BlockArgSpec("a", "角度(rad)", BlockArgType.Number, 0f) }),

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

    // 指定したopコード名に一致するBlockDefを探して返す。見つからなければnull。
    public static BlockDef? Find(string op) => All.Find(b => b.Op == op);

    // 指定したカテゴリに属するBlockDefだけを絞り込んで返す（パレットのカテゴリ別表示に使用）。
    public static IEnumerable<BlockDef> ByCategory(BlockCategory cat) => All.Where(b => b.Category == cat);
}
