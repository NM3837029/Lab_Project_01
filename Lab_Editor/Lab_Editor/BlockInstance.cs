namespace Lab_Editor;

// ======================================================
// BlockInstance - ブロックキャンバス上に実際に置かれた1個のブロック
// Feature: Puzzle-like Behavior Scripting (M4)
// ======================================================
public class BlockInstance
{
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
    public int X, Y;
    public int Width, Height;
    public int BodyHeight, ElseHeight;
    public int LabelWidth;

    // 各引数欄（リテラル入力欄 or ソケット）の内容座標系での矩形。M7でヒットテスト・描画の両方の基準となる
    public Dictionary<string, Rectangle> ArgSocketRects = new();

    public static BlockInstance Create(BlockDef def)
    {
        var b = new BlockInstance { Def = def };
        foreach (var arg in def.Args)
        {
            if (arg.Type != BlockArgType.BoolSlot)
                b.ArgValues[arg.Name] = arg.DefaultValue;
        }
        return b;
    }

    // 表示用のラベル文字列を組み立てる（例: "〜へ移動する 速さ [Toward] [2]"）
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
    public BlockDef? NewFromDef;
    public List<BlockInstance>? ExistingChain;

    public BlockDragPayload(BlockDef def) { NewFromDef = def; }
    public BlockDragPayload(List<BlockInstance> chain) { ExistingChain = chain; }
}
