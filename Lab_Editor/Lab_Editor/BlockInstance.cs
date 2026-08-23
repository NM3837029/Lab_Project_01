namespace Lab_Editor;

// ======================================================
// BlockInstance - ブロックキャンバス上に実際に置かれた1個のブロック
// Feature: Puzzle-like Behavior Scripting (M4)
// ======================================================
// BlockDef（ブロックの「種類」を表す定義データ）を元に、実際にキャンバス上へ配置された
// 1個1個のブロックを表すクラス。Scratchのようなパズル型のビジュアルスクリプティングで、
// ブロック同士を組み合わせて挙動スクリプトを作るための土台となる。
public class BlockInstance
{
    // このブロックが「どの種類のブロックか」を表す定義データへの参照。
    // 表示名・引数の並び・見た目の形状（C型かどうか等）はすべてこのDefから決まる。
    public BlockDef Def = null!;

    // リテラル値（数値・文字列・ドロップダウン選択値）。BlockArgType.BoolSlot の引数はここには入らず ArgBlocks に入る
    public Dictionary<string, object> ArgValues = new();

    // ソケットに差し込まれたネストブロック（BoolSlot、および将来的にNumberの式ソケットにも使用）
    public Dictionary<string, BlockInstance> ArgBlocks = new();

    // C型ブロック（Forever/Repeat/If等）・ハットブロックの本体。それ以外の形状では常に空のまま
    public List<BlockInstance> Body = new();
    // IfElseのみ使用する「でなければ」側の本体
    public List<BlockInstance> Else = new();

    // ==== レイアウト結果（BlockLayout.Measure/Arrangeで毎回再計算される） ====
    // このブロックをキャンバス上のどの位置に描画するか（左上座標）。
    public int X, Y;
    // このブロック自身の描画サイズ（幅・高さ）。
    public int Width, Height;
    // C型ブロックの本体部分(Body)・IfElseの「でなければ」部分(Else)を描画するのに
    // 必要な高さ。ブロックの形状によっては使われず0のままになる。
    public int BodyHeight, ElseHeight;
    // ブロック名のラベル文字列を描画するのに必要な幅。
    public int LabelWidth;

    // 各引数欄（リテラル入力欄 or ソケット）の内容座標系での矩形。M7でヒットテスト・描画の両方の基準となる
    public Dictionary<string, Rectangle> ArgSocketRects = new();

    // BlockDef（ブロックの定義）から、実際にキャンバスへ配置できるBlockInstanceを1個生成するファクトリメソッド。
    // def : 生成元となるブロックの定義データ
    public static BlockInstance Create(BlockDef def)
    {
        // まず定義を紐付けただけの空のインスタンスを作る。
        var b = new BlockInstance { Def = def };
        // 定義に書かれている各引数について、初期値をArgValuesに登録していく。
        foreach (var arg in def.Args)
        {
            // BoolSlot（真偽値を判定する式ブロックを差し込むソケット）はリテラル値を
            // 持たない特殊な引数なので、ここでは登録せずArgBlocks側で扱う。
            if (arg.Type != BlockArgType.BoolSlot)
                b.ArgValues[arg.Name] = arg.DefaultValue;
        }
        return b;
    }

    // 表示用のラベル文字列を組み立てる（例: "〜へ移動する 速さ [Toward] [2]"）
    // 現状はどちらの分岐でもDef.DisplayNameをそのまま返すだけの実装になっている
    // （引数の値をラベル文字列に埋め込む処理は、将来的な拡張のためのプレースホルダーと思われる）。
    public string BuildLabelText()
    {
        if (Def.Args.Length == 0) return Def.DisplayName;
        return Def.DisplayName;
    }
}

// ドラッグ&ドロップで運ぶペイロード（M5）。
// パレットからの新規生成(NewFromDef)と、キャンバス内で既存ブロックを移動する場合(ExistingChain)を区別する。
// ExistingChainは「掴んだブロック本体＋そのすぐ下に連結されていた後続ブロック群」をひとつなぎで運ぶ
// （Scratchでブロックを掴むと下に繋がっているブロックも一緒に付いてくるのと同じ挙動）。
public class BlockDragPayload
{
    // パレット（ブロックの一覧）からドラッグを開始した場合に、元になった定義データが入る。
    // この場合は新規にBlockInstanceを1個生成してキャンバスに配置することになる。
    public BlockDef? NewFromDef;
    // キャンバス上の既存ブロックをドラッグした場合に、掴んだブロックとその後続ブロック群が入る。
    public List<BlockInstance>? ExistingChain;

    // パレットからの新規ドラッグ用のコンストラクタ。
    public BlockDragPayload(BlockDef def) { NewFromDef = def; }
    // キャンバス上の既存ブロック移動用のコンストラクタ。
    public BlockDragPayload(List<BlockInstance> chain) { ExistingChain = chain; }
}
