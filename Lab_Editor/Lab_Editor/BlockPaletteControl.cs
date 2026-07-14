using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockPaletteControl - 全命令ブロックをカテゴリ別に一覧表示するパレット
// Feature: Puzzle-like Behavior Scripting (M4/M5)
// M5でドラッグ開始（パレット→キャンバスへのブロック生成）に対応した。
// ======================================================
public class BlockPaletteControl : Panel
{
    private readonly Font _font = new Font("Meiryo UI", 9f);
    private readonly List<(BlockCategory cat, List<BlockInstance> items)> _groups = new();

    public BlockPaletteControl()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(235, 235, 240);
        BuildItems();
    }

    // クリックされた位置にあるパレット項目のBlockDefを探し、ドラッグ操作を開始する。
    // ドロップ先(BlockCanvasControl)ではBlockDefを受け取って新規BlockInstanceを生成する。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var contentPt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        foreach (var (_, items) in _groups)
        {
            foreach (var item in items)
            {
                var rect = new Rectangle(item.X, item.Y, item.Width, item.Height);
                if (rect.Contains(contentPt))
                {
                    DoDragDrop(new BlockDragPayload(item.Def), DragDropEffects.Copy);
                    return;
                }
            }
        }
    }

    private void BuildItems()
    {
        foreach (BlockCategory cat in Enum.GetValues(typeof(BlockCategory)))
        {
            var defs = BlockCatalog.ByCategory(cat).ToList();
            if (defs.Count == 0) continue;
            _groups.Add((cat, defs.Select(BlockInstance.Create).ToList()));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        BlockRenderer.CanvasBackColor = BackColor;

        int x = 10;
        int y = 10;
        using var catFont = new Font(_font, FontStyle.Bold);

        foreach (var (cat, items) in _groups)
        {
            g.DrawString(CategoryLabel(cat), catFont, Brushes.Black, x, y);
            y += 22;
            foreach (var item in items)
            {
                BlockLayout.Measure(item, g, _font);
                item.X = x;
                item.Y = y;
                BlockRenderer.Draw(g, item, _font);
                y += item.Height + 8;
            }
            y += 16;
        }

        int neededHeight = y + 10;
        if (AutoScrollMinSize.Height != neededHeight)
            AutoScrollMinSize = new Size(230, neededHeight);
    }

    private static string CategoryLabel(BlockCategory cat) => cat switch
    {
        BlockCategory.Control => "■ 制御",
        BlockCategory.Motion => "■ 動き",
        BlockCategory.Sensing => "■ センシング",
        BlockCategory.Combat => "■ 攻撃・演出",
        BlockCategory.Variables => "■ 変数",
        BlockCategory.Operators => "■ 演算子",
        _ => cat.ToString(),
    };
}
