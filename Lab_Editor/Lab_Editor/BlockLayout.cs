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
    // ── レイアウトに使う各種の固定サイズ定数 ──────────────
    public const int HeaderHeight = 30;   // 通常の1行ブロックの高さ
    public const int ReporterHeight = 24; // レポーター/真偽値ブロック（パレット単体表示時）の高さ
    public const int CIndent = 18;        // C型ブロックの本体の左インデント幅
    public const int CBarHeight = 14;     // C型ブロックの下部（閉じる）バーの高さ
    public const int ElseLabelHeight = 22;// IfElseの「でなければ」ラベル部分の高さ
    public const int EmptyBodyHeight = 20;// 本体が空のときに確保する最小の高さ（C字シルエットが見えるように）
    public const int MinWidth = 100;      // ブロックの最小横幅（これより短くはならない）
    public const int Padding = 8;         // ブロック左右の余白
    public const int ArgGap = 6;          // 引数欄どうしの間隔
    public const int ArgFieldWidth = 46;  // 引数の入力欄（リテラル値用）の標準横幅
    public const int ArgFieldHeight = 20; // 引数の入力欄（リテラル値用）の標準縦幅
    public const int NotchWidth = 16;     // 上端の凸/下端の凹（ジグソー結合部）の幅
    public const int NotchHeight = 5;     // 上端の凸/下端の凹（ジグソー結合部）の高さ

    // ---- Measure ----------------------------------------------------

    // 指定したブロック1つの幅・高さ（Width/Height/BodyHeight/ElseHeight）を確定する。
    // 子ブロック（本体・引数に差し込まれたブロック）から先に測ってから、自分自身のサイズを決める
    // 「子から親へ」の順序で処理する必要がある点に注意。
    // b    : サイズを確定させたい対象のブロック
    // g    : 文字列幅の計測に使うGraphicsコンテキスト
    // font : ラベル文字列の描画に使うフォント（文字幅の計測にもこれを使う）
    public static void Measure(BlockInstance b, Graphics g, Font font)
    {
        // まず本体（Body/Else）を先に測る（子から親へ、の原則）
        b.BodyHeight = MeasureSequence(b.Body, g, font);
        // 本体が空（ブロックが1つも入っていない）場合でも、C字の見た目が崩れないよう最低限の高さを確保する
        if (b.BodyHeight <= 0) b.BodyHeight = EmptyBodyHeight;

        if (b.Def.HasElse)
        {
            // IfElse等、else節を持つブロックの場合はelse側の本体も同様に測る
            b.ElseHeight = MeasureSequence(b.Else, g, font);
            if (b.ElseHeight <= 0) b.ElseHeight = EmptyBodyHeight;
        }
        else
        {
            // else節を持たないブロックはelse側の高さを0として扱う
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
                // ソケットに別のブロックが差し込まれている場合：そのブロックを再帰的に測定してから
                // 実際の幅（+間隔）を親の横幅計算に加算する
                Measure(nested, g, font);
                argsW += nested.Width + ArgGap;
            }
            else
            {
                // ソケットが空（リテラル入力欄のまま）の場合：既定の入力欄幅（+間隔）を加算する
                argsW += ArgFieldWidth + ArgGap;
            }
        }
        // 最小幅・パディング・ラベル幅・引数幅の合計のうち、大きい方を最終的な横幅として採用する
        b.Width = System.Math.Max(MinWidth, Padding * 2 + (int)textW + argsW);

        // 縦幅：形状によって異なる
        switch (b.Def.Shape)
        {
            case BlockShape.Reporter:
            case BlockShape.Boolean:
                // レポーター/真偽値ブロックは値を返すだけの1行ブロックなので、専用の低い高さを使う
                b.Height = ReporterHeight;
                break;
            case BlockShape.CBlock:
                // C型ブロック：ヘッダー＋本体＋（あれば）else節＋下部バーの合計
                b.Height = HeaderHeight + b.BodyHeight
                         + (b.Def.HasElse ? ElseLabelHeight + b.ElseHeight : 0)
                         + CBarHeight;
                break;
            case BlockShape.Hat:
                // 帽子型ブロック（イベントの起点）：本体を持つ場合はヘッダー＋本体＋下部バー、
                // 持たない場合はヘッダーのみ
                b.Height = b.Def.HasBody
                    ? HeaderHeight + b.BodyHeight + CBarHeight
                    : HeaderHeight;
                break;
            case BlockShape.Stack:
            default:
                // 通常の1行スタックブロックはヘッダーの高さのみ
                b.Height = HeaderHeight;
                break;
        }
    }

    // 縦に連結されたブロック列全体の高さを測る（各要素も再帰的にMeasureする）。
    // seq  : 縦に並んだブロックのリスト（本体やelse節の中身）
    // g    : 文字列幅の計測に使うGraphicsコンテキスト
    // font : ラベル文字列の描画に使うフォント
    // 戻り値：リスト内の全ブロックの高さを合計した値
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

    // 縦に連結されたブロック列のX/Yを確定する（親から子へ）。
    // seq : 座標を確定させたいブロックのリスト
    // x   : このリストの左端X座標
    // y   : このリストの先頭ブロックの上端Y座標
    public static void ArrangeSequence(List<BlockInstance> seq, int x, int y)
    {
        int curY = y;
        foreach (var block in seq)
        {
            // 各ブロックを左揃え・縦に積み上げる形で配置する
            block.X = x;
            block.Y = curY;
            // このブロック自身が持つ引数欄・子ブロックの座標も併せて確定させる
            ArrangeArgSockets(block);
            ArrangeChildren(block);
            // 次のブロックはこのブロックの直下から始まる
            curY += block.Height;
        }
    }

    // C型ブロック／帽子型ブロックが持つ本体（Body）とelse節（Else）の内部レイアウトを確定する。
    // それ以外の形状（Stack/Reporter/Boolean）は子を持たないため何もしない。
    private static void ArrangeChildren(BlockInstance b)
    {
        if (b.Def.Shape != BlockShape.CBlock && b.Def.Shape != BlockShape.Hat) return;

        if (b.Def.HasBody)
            // 本体はヘッダーの直下、かつインデント分だけ右にずらした位置から配置する
            ArrangeSequence(b.Body, b.X + CIndent, b.Y + HeaderHeight);

        if (b.Def.HasElse)
        {
            // else節はヘッダー＋本体＋elseラベルの高さの分だけ下にずれた位置から配置する
            int elseY = b.Y + HeaderHeight + b.BodyHeight + ElseLabelHeight;
            ArrangeSequence(b.Else, b.X + CIndent, elseY);
        }
    }

    // 各引数欄（リテラル入力欄 or ソケット）の座標を確定する。ソケットにレポーター/真偽値ブロックが
    // 差し込まれている場合は、そのブロック自身の座標もここで確定し（行内で縦センタリング）、
    // 再帰的に自分自身の引数ソケットも配置する（Add(GetVar, Const)のような多重ネストに対応）。
    private static void ArrangeArgSockets(BlockInstance b)
    {
        // 前回分の矩形情報が残らないよう、まずクリアしてから作り直す
        b.ArgSocketRects.Clear();
        if (b.Def.Args.Length == 0) return;

        // Stack/CBlock/Hatはヘッダー行（高さHeaderHeight）、Reporter/Booleanはブロック全体が1行
        int rowHeight = (b.Def.Shape == BlockShape.Reporter || b.Def.Shape == BlockShape.Boolean) ? b.Height : HeaderHeight;

        // ラベル文字列の右隣から、引数欄を順番に左から右へ並べていく
        int argX = b.X + Padding + b.LabelWidth + ArgGap;
        foreach (var arg in b.Def.Args)
        {
            // このソケットに別のブロックが差し込まれているかどうかを判定する
            bool filled = b.ArgBlocks.TryGetValue(arg.Name, out var nested);
            int w = filled ? nested!.Width : ArgFieldWidth;
            int fieldH = filled ? nested!.Height : ArgFieldHeight;
            // 行の高さに対して縦方向中央に揃うよう、Y座標にオフセットを加える
            var rect = new Rectangle(argX, b.Y + (rowHeight - fieldH) / 2, w, fieldH);
            b.ArgSocketRects[arg.Name] = rect;

            if (filled)
            {
                // 差し込まれているブロックの座標を、いま確定したソケット矩形に合わせて設定する
                nested!.X = rect.X;
                nested.Y = rect.Y;
                // そのブロック自身がさらに引数を持つ場合に備えて再帰的に配置する
                ArrangeArgSockets(nested);
            }

            // 次の引数欄はこの欄の右端＋間隔から始まる
            argX += w + ArgGap;
        }
    }
}
