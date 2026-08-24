using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockRenderer - ブロックをScratch風の見た目でGDI+描画する
// Feature: Puzzle-like Behavior Scripting (M4)
//
// 完全なジグソーパズル型の輪郭（1本の連続したGraphicsPathで凹凸を含めて描く）ではなく、
// ヘッダー/スパイン/フッターの重ね合わせと小さな凸凹グリフによる簡略表現にしている。
// 色分け・全体シルエットで十分にブロックらしく見え、実装の複雑さを抑えられるため。
//
// 座標（X, Y, Width, Height, BodyHeight, ElseHeightなど）は全てBlockLayout側で事前に計算済みの前提で、
// このクラスは受け取った座標情報をもとに「見た目を描くだけ」に責務を絞っている。
// ======================================================
public static class BlockRenderer
{
    // キャンバス（またはパレット）の背景色。下端の凹み表現をこの色で塗りつぶすことで
    // 「へこんでいるように見せる」簡易トリックに使うため、描画対象側から都度設定してもらう必要がある。
    public static Color CanvasBackColor = Color.FromArgb(245, 245, 250);

    // 1つのブロック（と、その子ブロック・差し込みブロック）を再帰的に描画するエントリーポイント。
    // b.Def.Shapeの種類に応じて描画方法を振り分け、その後Body/Else/引数ソケットの中身も再帰的に描く。
    public static void Draw(Graphics g, BlockInstance b, Font font)
    {
        // カテゴリごとに固定の色を取得し、輪郭線用にその色を少し暗くしたものも用意する
        var color = BlockCatalog.CategoryColor(b.Def.Category);
        var darker = ControlPaint.Dark(color, 0.2f);

        switch (b.Def.Shape)
        {
            case BlockShape.Reporter:
                // 値を返すだけのブロック（レポーター）：角丸の横長ピル型で描く
                DrawPillOrHexagon(g, b, color, darker, font, hexagon: false);
                break;
            case BlockShape.Boolean:
                // 真偽値を返すブロック：見分けやすいように六角形で描く
                DrawPillOrHexagon(g, b, color, darker, font, hexagon: true);
                break;
            case BlockShape.Hat:
                // イベントの開始点となるブロック（OnSpawn等）：上端が大きく丸い帽子型のコンテナで描く
                DrawContainer(g, b, color, darker, font, topRadius: 14);
                break;
            case BlockShape.CBlock:
            case BlockShape.Stack:
            default:
                // 通常のスタック型ブロック、および中に他のブロックを挟むC型ブロックは
                // どちらも同じコンテナ描画（上端の丸みだけ小さめ）で表現する
                DrawContainer(g, b, color, darker, font, topRadius: 6);
                break;
        }

        // 自分自身を描いた後、内側に持つ子ブロック列（本体側／else側）を再帰的に描画する
        foreach (var child in b.Body) Draw(g, child, font);
        foreach (var child in b.Else) Draw(g, child, font);
        // ソケットに差し込まれたレポーター/真偽値ブロックも再帰的に描画する（M7）
        foreach (var kv in b.ArgBlocks) Draw(g, kv.Value, font);
    }

    // 指定した矩形領域の四隅を、指定した半径で丸めたGraphicsPathを生成する共通ヘルパー。
    // ブロックのヘッダー・フッター・ピル形状などあらゆる角丸矩形の描画で共有して使われる。
    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2; // 直径（角を丸めるための円弧のバウンディングボックスのサイズ）
        // 矩形自体が小さすぎて角丸の直径が矩形の幅/高さを超えてしまう場合は、直径を矩形のサイズに合わせて縮める
        // （超えたままにすると弧が崩れて見た目がおかしくなるため）
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        // 左上→右上→右下→左下の順に90度ずつの円弧をつなげて、角丸矩形の輪郭を1本のパスとして構築する
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); // 始点と終点を結んでパスを閉じる
        return path;
    }

    // ヘッダー(+本体を囲むC字)を描く。Stack/Hat/CBlock全形状に対応
    // b.Def.HasBodyがtrueの場合（If/Forever等のC型ブロック）は、ヘッダーの下にスパイン（左の縦棒）・
    // 必要であれば「でなければ」ラベル、そして最後にフッター（下端を閉じる帯）まで一続きに描画する。
    // HasBodyがfalseの単純なブロック（Stack型で中身を持たないもの）はヘッダーだけを描いて終了する。
    private static void DrawContainer(Graphics g, BlockInstance b, Color color, Color darker, Font font, int topRadius)
    {
        using var brush = new SolidBrush(color); // ブロック本体の塗りつぶし色（カテゴリ色）
        using var pen = new Pen(darker, 1.4f);    // 輪郭線用のペン（本体色を少し暗くした色）

        // ヘッダー部分（ブロック名やラベル・引数欄が表示される、常に存在する帯）の矩形
        var headerRect = new Rectangle(b.X, b.Y, b.Width, BlockLayout.HeaderHeight);
        bool isContainer = b.Def.HasBody; // このブロックが中に他のブロックを挟む「C字」構造を持つかどうか

        // ヘッダーの背景を角丸矩形で塗りつぶし、輪郭線を描く
        using (var headerPath = RoundedRect(headerRect, topRadius))
        {
            g.FillPath(brush, headerPath);
            g.DrawPath(pen, headerPath);
        }
        DrawConnectorNotches(g, b, headerRect); // 上端の凸/下端の凹の接続グリフを描く
        DrawLabel(g, b, headerRect, font);       // ブロック名の文字列と引数欄を描く

        if (!isContainer) return; // 単純なブロックはヘッダーのみで描画完了

        // スパイン（左の縦棒）：C字の左側の縦の帯。この右側に子ブロックが積み上げられる想定の余白となる。
        var spineRect = new Rectangle(b.X, b.Y + BlockLayout.HeaderHeight, BlockLayout.CIndent, b.BodyHeight);
        g.FillRectangle(brush, spineRect);
        g.DrawLine(pen, spineRect.X, spineRect.Y, spineRect.X, spineRect.Bottom);

        // ヘッダーと本体（Body）を描き終えた直後のY座標。ここから「でなければ」部やフッターを続けて描く。
        int afterBody = b.Y + BlockLayout.HeaderHeight + b.BodyHeight;

        if (b.Def.HasElse)
        {
            // 「でなければ」ラベルバー：IfElseブロックのelse側の開始位置を示す帯
            var elseLabelRect = new Rectangle(b.X, afterBody, b.Width, BlockLayout.ElseLabelHeight);
            g.FillRectangle(brush, elseLabelRect);
            using var elseFont = new Font(font, FontStyle.Bold);
            g.DrawString("でなければ", elseFont, Brushes.White, elseLabelRect.X + BlockLayout.Padding, elseLabelRect.Y + 2);

            // else側のスパイン（else側に積まれる子ブロックのための左側の縦帯）
            var elseSpineRect = new Rectangle(b.X, afterBody + BlockLayout.ElseLabelHeight, BlockLayout.CIndent, b.ElseHeight);
            g.FillRectangle(brush, elseSpineRect);

            afterBody = afterBody + BlockLayout.ElseLabelHeight + b.ElseHeight;
        }

        // フッター（下部の閉じるバー）：C字の輪郭を下側で閉じる帯。ここでC型ブロックの範囲が視覚的に完結する。
        var footerRect = new Rectangle(b.X, afterBody, b.Width, BlockLayout.CBarHeight);
        using var footerPath = RoundedRect(footerRect, topRadius);
        g.FillPath(brush, footerPath);
        g.DrawPath(pen, footerPath);
    }

    // 上端の凸（前のブロックと繋がる部分）と下端の凹（次のブロックが繋がる部分）の簡易グリフ。
    // 本物のジグソーパズルのような輪郭一体型の凹凸ではなく、小さな四角形を重ねて描くだけの簡略表現。
    private static void DrawConnectorNotches(Graphics g, BlockInstance b, Rectangle headerRect)
    {
        int nx = headerRect.X + 16; // 凸/凹グリフのX位置（ヘッダー左端から16pxの位置に固定）
        var color = BlockCatalog.CategoryColor(b.Def.Category);

        // 上端の凸（Hat形状は連結の起点＝一番上に来るブロックなので、上に繋がる相手がおらず描かない）
        if (b.Def.Shape != BlockShape.Hat)
        {
            using var brush = new SolidBrush(color);
            // ヘッダーの上端からわずかに突き出す小さな矩形を、ブロックと同じ色で塗って「凸」に見せる
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

    // ヘッダー内にブロック名（DisplayName）と、埋まっていない引数ソケットのプレースホルダを描画する。
    private static void DrawLabel(Graphics g, BlockInstance b, Rectangle headerRect, Font font)
    {
        // ブロック名のテキストをヘッダーの左端（余白Padding分）、縦方向は中央に描画する
        int tx = headerRect.X + BlockLayout.Padding;
        int ty = headerRect.Y + (BlockLayout.HeaderHeight - font.Height) / 2;
        g.DrawString(b.Def.DisplayName, font, Brushes.White, tx, ty);

        // 引数欄の座標はBlockLayout.ArrangeArgSocketsが確定済み（M7）。ここでは描画のみ行う。
        // ソケットが埋まっている場合は何も描かない（差し込まれたブロック自体はDraw()の再帰で別途描画される）。
        foreach (var arg in b.Def.Args)
        {
            // このブロック定義に対応するソケット矩形が見つからない場合はレイアウト未確定なのでスキップ
            if (!b.ArgSocketRects.TryGetValue(arg.Name, out var fieldRect)) continue;
            bool filled = b.ArgBlocks.ContainsKey(arg.Name); // 他のブロックが既に差し込まれているか
            if (filled) continue; // 差し込み済みなら、そのブロック自体がDraw()の再帰で描かれるのでここでは何もしない

            if (arg.Type == BlockArgType.BoolSlot)
            {
                // 真偽値ソケット：差し込まれていなければ、半透明の白い六角形を「空のへこみ」として表示する
                using var slotBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
                g.FillPolygon(slotBrush, HexagonPoints(fieldRect));
                continue;
            }

            // 数値・文字列など通常の引数：白い矩形の入力欄と、現在設定されている値をテキストとして描画する
            using var fieldBrush = new SolidBrush(Color.White);
            g.FillRectangle(fieldBrush, fieldRect);
            g.DrawRectangle(Pens.Gray, fieldRect);
            string valText = b.ArgValues.TryGetValue(arg.Name, out var v) ? v?.ToString() ?? "" : "";
            using var smallFont = new Font(font.FontFamily, font.Size * 0.85f);
            g.DrawString(valText, smallFont, Brushes.Black, fieldRect.X + 3, fieldRect.Y + 2);
        }
    }

    // レポーター（値を返す）ブロックと真偽値ブロックの見た目を描画する。
    // hexagon=trueなら六角形（真偽値用）、falseなら左右が丸い横長のピル形状（数値レポーター用）で描く。
    private static void DrawPillOrHexagon(Graphics g, BlockInstance b, Color color, Color darker, Font font, bool hexagon)
    {
        var rect = new Rectangle(b.X, b.Y, b.Width, b.Height);
        using var brush = new SolidBrush(color);
        using var pen = new Pen(darker, 1.2f);

        if (hexagon)
        {
            // 真偽値ブロック：六角形の頂点を計算して塗りつぶし＋輪郭線を描く
            var pts = HexagonPoints(rect);
            g.FillPolygon(brush, pts);
            g.DrawPolygon(pen, pts);
        }
        else
        {
            // レポーターブロック：高さの半分を半径にすることで、左右の端が完全な半円になる「ピル形状」にする
            using var path = RoundedRect(rect, rect.Height / 2);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        // ブロック名を中央揃えで描画する（少し小さめのフォントサイズを使う）
        using var smallFont = new Font(font.FontFamily, font.Size * 0.9f);
        var textSize = g.MeasureString(b.Def.DisplayName, smallFont);
        g.DrawString(b.Def.DisplayName, smallFont, Brushes.White,
            rect.X + (rect.Width - textSize.Width) / 2, rect.Y + (rect.Height - textSize.Height) / 2);
    }

    // 指定した矩形を元に、左右の端が三角形に尖った六角形（横長の宝石型）の頂点座標6点を計算して返す。
    // 真偽値ブロックの本体形状や、真偽値ソケットの空きプレースホルダ形状の両方で使い回される。
    private static Point[] HexagonPoints(Rectangle r)
    {
        int cut = r.Height / 2; // 左右の斜め辺の切り欠き幅（高さの半分＝正三角形に近い角度になる）
        return new[]
        {
            new Point(r.X + cut, r.Y),             // 左上
            new Point(r.Right - cut, r.Y),         // 右上
            new Point(r.Right, r.Y + r.Height / 2), // 右端の頂点（最も右に尖っている点）
            new Point(r.Right - cut, r.Bottom),     // 右下
            new Point(r.X + cut, r.Bottom),         // 左下
            new Point(r.X, r.Y + r.Height / 2),     // 左端の頂点（最も左に尖っている点）
        };
    }
}
