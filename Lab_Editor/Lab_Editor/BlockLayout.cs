namespace Lab_Editor;

// ======================================================
// BlockLayout - ブロックツリーの2パスレイアウト（Measure → Arrange）
// Feature: Puzzle-like Behavior Scripting (M4)
//
// Measure: 子から親へ、各ブロックのWidth/Height/BodyHeight/ElseHeightを確定する
// Arrange: 親から子へ、各ブロックのX/Yを確定する
// ドラッグ中のリアルタイム再計算にも耐えられるよう、都度全体を計算し直す設計にしている
// （キャッシュや差分更新は行わない。ブロック数が数百に達する想定はないため十分高速）。
// ======================================================
public static class BlockLayout
{
    public const int HeaderHeight = 30;   // 通常の1行ブロックの高さ
    public const int ReporterHeight = 24; // レポーター/真偽値ブロック（パレット単体表示時）の高さ
    public const int CIndent = 18;        // C型ブロックの本体の左インデント幅
    public const int CBarHeight = 14;     // C型ブロックの下部（閉じる）バーの高さ
    public const int ElseLabelHeight = 22;// IfElseの「でなければ」ラベル部分の高さ
    public const int EmptyBodyHeight = 20;// 本体が空のときに確保する最小の高さ（C字シルエットが見えるように）
    public const int MinWidth = 100;
    public const int Padding = 8;         // ブロック左右の余白
    public const int ArgGap = 6;          // 引数欄どうしの間隔
    public const int ArgFieldWidth = 46;
    public const int ArgFieldHeight = 20;
    public const int NotchWidth = 16;     // 上端の凸/下端の凹（ジグソー結合部）の幅
    public const int NotchHeight = 5;

    // ---- Measure ----------------------------------------------------

    public static void Measure(BlockInstance b, Graphics g, Font font)
    {
        // まず本体（Body/Else）を先に測る（子から親へ、の原則）
        b.BodyHeight = MeasureSequence(b.Body, g, font);
        if (b.BodyHeight <= 0) b.BodyHeight = EmptyBodyHeight;

        if (b.Def.HasElse)
        {
            b.ElseHeight = MeasureSequence(b.Else, g, font);
            if (b.ElseHeight <= 0) b.ElseHeight = EmptyBodyHeight;
        }
        else
        {
            b.ElseHeight = 0;
        }

        // 横幅：ラベル文字列＋各引数欄の幅から見積もる
        // 引数のソケットにレポーター/真偽値ブロックが差し込まれている場合は、そのブロック自身の
        // 幅を先に確定させ（再帰的にMeasure）、親の横幅にはプレースホルダ幅ではなくその実幅を使う
        float textW = g.MeasureString(b.Def.DisplayName, font).Width;
        b.LabelWidth = (int)textW;
        int argsW = 0;
        foreach (var arg in b.Def.Args)
        {
            if (b.ArgBlocks.TryGetValue(arg.Name, out var nested))
            {
                Measure(nested, g, font);
                argsW += nested.Width + ArgGap;
            }
            else
            {
                argsW += ArgFieldWidth + ArgGap;
            }
        }
        b.Width = System.Math.Max(MinWidth, Padding * 2 + (int)textW + argsW);

        // 縦幅：形状によって異なる
        switch (b.Def.Shape)
        {
            case BlockShape.Reporter:
            case BlockShape.Boolean:
                b.Height = ReporterHeight;
                break;
            case BlockShape.CBlock:
                b.Height = HeaderHeight + b.BodyHeight
                         + (b.Def.HasElse ? ElseLabelHeight + b.ElseHeight : 0)
                         + CBarHeight;
                break;
            case BlockShape.Hat:
                b.Height = b.Def.HasBody
                    ? HeaderHeight + b.BodyHeight + CBarHeight
                    : HeaderHeight;
                break;
            case BlockShape.Stack:
            default:
                b.Height = HeaderHeight;
                break;
        }
    }

    // 縦に連結されたブロック列全体の高さを測る（各要素も再帰的にMeasureする）
    public static int MeasureSequence(List<BlockInstance> seq, Graphics g, Font font)
    {
        int total = 0;
        foreach (var block in seq)
        {
            Measure(block, g, font);
            total += block.Height;
        }
        return total;
    }

    // ---- Arrange ------------------------------------------------------

    // 縦に連結されたブロック列のX/Yを確定する（親から子へ）
    public static void ArrangeSequence(List<BlockInstance> seq, int x, int y)
    {
        int curY = y;
        foreach (var block in seq)
        {
            block.X = x;
            block.Y = curY;
            ArrangeArgSockets(block);
            ArrangeChildren(block);
            curY += block.Height;
        }
    }

    private static void ArrangeChildren(BlockInstance b)
    {
        if (b.Def.Shape != BlockShape.CBlock && b.Def.Shape != BlockShape.Hat) return;

        if (b.Def.HasBody)
            ArrangeSequence(b.Body, b.X + CIndent, b.Y + HeaderHeight);

        if (b.Def.HasElse)
        {
            int elseY = b.Y + HeaderHeight + b.BodyHeight + ElseLabelHeight;
            ArrangeSequence(b.Else, b.X + CIndent, elseY);
        }
    }

    // 各引数欄（リテラル入力欄 or ソケット）の座標を確定する。ソケットにレポーター/真偽値ブロックが
    // 差し込まれている場合は、そのブロック自身の座標もここで確定し（行内で縦センタリング）、
    // 再帰的に自分自身の引数ソケットも配置する（Add(GetVar, Const)のような多重ネストに対応）。
    private static void ArrangeArgSockets(BlockInstance b)
    {
        b.ArgSocketRects.Clear();
        if (b.Def.Args.Length == 0) return;

        // Stack/CBlock/Hatはヘッダー行（高さHeaderHeight）、Reporter/Booleanはブロック全体が1行
        int rowHeight = (b.Def.Shape == BlockShape.Reporter || b.Def.Shape == BlockShape.Boolean) ? b.Height : HeaderHeight;

        int argX = b.X + Padding + b.LabelWidth + ArgGap;
        foreach (var arg in b.Def.Args)
        {
            bool filled = b.ArgBlocks.TryGetValue(arg.Name, out var nested);
            int w = filled ? nested!.Width : ArgFieldWidth;
            int fieldH = filled ? nested!.Height : ArgFieldHeight;
            var rect = new Rectangle(argX, b.Y + (rowHeight - fieldH) / 2, w, fieldH);
            b.ArgSocketRects[arg.Name] = rect;

            if (filled)
            {
                nested!.X = rect.X;
                nested.Y = rect.Y;
                ArrangeArgSockets(nested);
            }

            argX += w + ArgGap;
        }
    }
}
