using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockRenderer - ブロックをScratch風の見た目でGDI+描画する
// Feature: Puzzle-like Behavior Scripting (M4)
//
// 完全なジグソーパズル型の輪郭（1本の連続したGraphicsPathで凹凸を含めて描く）ではなく、
// ヘッダー/スパイン/フッターの重ね合わせと小さな凸凹グリフによる簡略表現にしている。
// 色分け・全体シルエットで十分にブロックらしく見え、実装の複雑さを抑えられるため。
// ======================================================
public static class BlockRenderer
{
    public static Color CanvasBackColor = Color.FromArgb(245, 245, 250);

    public static void Draw(Graphics g, BlockInstance b, Font font)
    {
        var color = BlockCatalog.CategoryColor(b.Def.Category);
        var darker = ControlPaint.Dark(color, 0.2f);

        switch (b.Def.Shape)
        {
            case BlockShape.Reporter:
                DrawPillOrHexagon(g, b, color, darker, font, hexagon: false);
                break;
            case BlockShape.Boolean:
                DrawPillOrHexagon(g, b, color, darker, font, hexagon: true);
                break;
            case BlockShape.Hat:
                DrawContainer(g, b, color, darker, font, topRadius: 14);
                break;
            case BlockShape.CBlock:
            case BlockShape.Stack:
            default:
                DrawContainer(g, b, color, darker, font, topRadius: 6);
                break;
        }

        foreach (var child in b.Body) Draw(g, child, font);
        foreach (var child in b.Else) Draw(g, child, font);
        // ソケットに差し込まれたレポーター/真偽値ブロックも再帰的に描画する（M7）
        foreach (var kv in b.ArgBlocks) Draw(g, kv.Value, font);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ヘッダー(+本体を囲むC字)を描く。Stack/Hat/CBlock全形状に対応
    private static void DrawContainer(Graphics g, BlockInstance b, Color color, Color darker, Font font, int topRadius)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(darker, 1.4f);

        var headerRect = new Rectangle(b.X, b.Y, b.Width, BlockLayout.HeaderHeight);
        bool isContainer = b.Def.HasBody; // C字に囲む必要があるか

        using (var headerPath = RoundedRect(headerRect, topRadius))
        {
            g.FillPath(brush, headerPath);
            g.DrawPath(pen, headerPath);
        }
        DrawConnectorNotches(g, b, headerRect);
        DrawLabel(g, b, headerRect, font);

        if (!isContainer) return;

        // スパイン（左の縦棒）
        var spineRect = new Rectangle(b.X, b.Y + BlockLayout.HeaderHeight, BlockLayout.CIndent, b.BodyHeight);
        g.FillRectangle(brush, spineRect);
        g.DrawLine(pen, spineRect.X, spineRect.Y, spineRect.X, spineRect.Bottom);

        int afterBody = b.Y + BlockLayout.HeaderHeight + b.BodyHeight;

        if (b.Def.HasElse)
        {
            // 「でなければ」ラベルバー
            var elseLabelRect = new Rectangle(b.X, afterBody, b.Width, BlockLayout.ElseLabelHeight);
            g.FillRectangle(brush, elseLabelRect);
            using var elseFont = new Font(font, FontStyle.Bold);
            g.DrawString("でなければ", elseFont, Brushes.White, elseLabelRect.X + BlockLayout.Padding, elseLabelRect.Y + 2);

            var elseSpineRect = new Rectangle(b.X, afterBody + BlockLayout.ElseLabelHeight, BlockLayout.CIndent, b.ElseHeight);
            g.FillRectangle(brush, elseSpineRect);

            afterBody = afterBody + BlockLayout.ElseLabelHeight + b.ElseHeight;
        }

        // フッター（下部の閉じるバー）
        var footerRect = new Rectangle(b.X, afterBody, b.Width, BlockLayout.CBarHeight);
        using var footerPath = RoundedRect(footerRect, topRadius);
        g.FillPath(brush, footerPath);
        g.DrawPath(pen, footerPath);
    }

    // 上端の凸（前のブロックと繋がる部分）と下端の凹（次のブロックが繋がる部分）の簡易グリフ
    private static void DrawConnectorNotches(Graphics g, BlockInstance b, Rectangle headerRect)
    {
        int nx = headerRect.X + 16;
        var color = BlockCatalog.CategoryColor(b.Def.Category);

        // 上端の凸（Hat形状は開始点なので描かない）
        if (b.Def.Shape != BlockShape.Hat)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, nx, headerRect.Y - BlockLayout.NotchHeight, BlockLayout.NotchWidth, BlockLayout.NotchHeight + 1);
        }

        // 下端の凹（キャンバス背景色で塗って「へこみ」に見せる簡易表現）
        using var bgBrush = new SolidBrush(CanvasBackColor);
        int bottomY = headerRect.Y + BlockLayout.HeaderHeight - BlockLayout.NotchHeight;
        // Body/Elseを持つ場合は下端の凹はフッターバーの下端に描く（ヘッダー直下ではない）ため、
        // コンテナ形状の凹表示はDrawContainer側の最終フッターでは省略し、単純ブロックのみここで描く
        if (!b.Def.HasBody)
        {
            g.FillRectangle(bgBrush, nx, bottomY, BlockLayout.NotchWidth, BlockLayout.NotchHeight + 1);
        }
    }

    private static void DrawLabel(Graphics g, BlockInstance b, Rectangle headerRect, Font font)
    {
        int tx = headerRect.X + BlockLayout.Padding;
        int ty = headerRect.Y + (BlockLayout.HeaderHeight - font.Height) / 2;
        g.DrawString(b.Def.DisplayName, font, Brushes.White, tx, ty);

        // 引数欄の座標はBlockLayout.ArrangeArgSocketsが確定済み（M7）。ここでは描画のみ行う。
        // ソケットが埋まっている場合は何も描かない（差し込まれたブロック自体はDraw()の再帰で別途描画される）。
        foreach (var arg in b.Def.Args)
        {
            if (!b.ArgSocketRects.TryGetValue(arg.Name, out var fieldRect)) continue;
            bool filled = b.ArgBlocks.ContainsKey(arg.Name);
            if (filled) continue;

            if (arg.Type == BlockArgType.BoolSlot)
            {
                // 真偽値ソケット：差し込まれていなければ空のへこみプレースホルダを表示
                using var slotBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
                g.FillPolygon(slotBrush, HexagonPoints(fieldRect));
                continue;
            }

            using var fieldBrush = new SolidBrush(Color.White);
            g.FillRectangle(fieldBrush, fieldRect);
            g.DrawRectangle(Pens.Gray, fieldRect);
            string valText = b.ArgValues.TryGetValue(arg.Name, out var v) ? v?.ToString() ?? "" : "";
            using var smallFont = new Font(font.FontFamily, font.Size * 0.85f);
            g.DrawString(valText, smallFont, Brushes.Black, fieldRect.X + 3, fieldRect.Y + 2);
        }
    }

    private static void DrawPillOrHexagon(Graphics g, BlockInstance b, Color color, Color darker, Font font, bool hexagon)
    {
        var rect = new Rectangle(b.X, b.Y, b.Width, b.Height);
        using var brush = new SolidBrush(color);
        using var pen = new Pen(darker, 1.2f);

        if (hexagon)
        {
            var pts = HexagonPoints(rect);
            g.FillPolygon(brush, pts);
            g.DrawPolygon(pen, pts);
        }
        else
        {
            using var path = RoundedRect(rect, rect.Height / 2);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        using var smallFont = new Font(font.FontFamily, font.Size * 0.9f);
        var textSize = g.MeasureString(b.Def.DisplayName, smallFont);
        g.DrawString(b.Def.DisplayName, smallFont, Brushes.White,
            rect.X + (rect.Width - textSize.Width) / 2, rect.Y + (rect.Height - textSize.Height) / 2);
    }

    private static Point[] HexagonPoints(Rectangle r)
    {
        int cut = r.Height / 2;
        return new[]
        {
            new Point(r.X + cut, r.Y),
            new Point(r.Right - cut, r.Y),
            new Point(r.Right, r.Y + r.Height / 2),
            new Point(r.Right - cut, r.Bottom),
            new Point(r.X + cut, r.Bottom),
            new Point(r.X, r.Y + r.Height / 2),
        };
    }
}
