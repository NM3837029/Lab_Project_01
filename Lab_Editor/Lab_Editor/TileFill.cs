using System;
using System.Collections.Generic;

namespace Lab_Editor;

// タイル配列の上の矩形範囲（タイル座標）。タイルの選択範囲として使う。
// Col/Row は左上のマス、Cols/Rows はマスの数（どちらも1以上）。
public readonly record struct TileRect(int Col, int Row, int Cols, int Rows)
{
    public int Right => Col + Cols - 1;   // 右端のマス（含む）
    public int Bottom => Row + Rows - 1;  // 下端のマス（含む）

    public bool Contains(int col, int row) => col >= Col && col <= Right && row >= Row && row <= Bottom;

    // 2つのマス（ドラッグの始点と終点など）を両端とする矩形を、マップの範囲内に収めて作る。
    // 始点と終点はどちらが左上でもよい。
    public static TileRect FromCorners(int c1, int r1, int c2, int r2, int mapW, int mapH)
    {
        int left = Math.Clamp(Math.Min(c1, c2), 0, Math.Max(0, mapW - 1));
        int top = Math.Clamp(Math.Min(r1, r2), 0, Math.Max(0, mapH - 1));
        int right = Math.Clamp(Math.Max(c1, c2), 0, Math.Max(0, mapW - 1));
        int bottom = Math.Clamp(Math.Max(r1, r2), 0, Math.Max(0, mapH - 1));
        return new TileRect(left, top, right - left + 1, bottom - top + 1);
    }

    // マップの大きさが変わったとき（リサイズ・別ステージ）に、はみ出さないよう切り詰める。範囲外なら null
    public TileRect? ClampTo(int mapW, int mapH)
    {
        int left = Math.Max(0, Col), top = Math.Max(0, Row);
        int right = Math.Min(mapW - 1, Right), bottom = Math.Min(mapH - 1, Bottom);
        if (right < left || bottom < top) return null;
        return new TileRect(left, top, right - left + 1, bottom - top + 1);
    }
}

// タイルの塗りつぶし。画面に依存しない計算だけを持つ（Lab_Editor の MapCanvas から使い、単体でも検証できるようにしてある）。
// layer は [行, 列] の2次元配列（StageData.Map / DecoLayerBack / DecoLayerFront と同じ）。
public static class TileFill
{
    // 矩形範囲を、すべて tileId で埋める。変わったマスの数を返す。
    // 範囲がマップからはみ出す分は無視する（切り詰める）。
    public static int FillRect(int[,] layer, TileRect rect, int tileId)
    {
        int rows = layer.GetLength(0), cols = layer.GetLength(1);
        var r = rect.ClampTo(cols, rows);
        if (r == null) return 0;
        int changed = 0;
        for (int row = r.Value.Row; row <= r.Value.Bottom; row++)
            for (int col = r.Value.Col; col <= r.Value.Right; col++)
                if (layer[row, col] != tileId) { layer[row, col] = tileId; changed++; }
        return changed;
    }

    // バケツ塗り。(col,row) と同じタイルで上下左右につながっている範囲を、tileId で埋める。変わったマスの数を返す。
    // limit が指定されていれば、その矩形の中だけで塗る（つながりも、その中でだけ数える）。
    // 始点が limit の外なら何もしない。始点がすでに tileId なら何もしない（無限に塗り直さないため）。
    // 再帰ではなく自前のスタックで走査する（広いマップで呼び出しの深さが溢れないように）。
    public static int FloodFill(int[,] layer, int col, int row, int tileId, TileRect? limit = null)
    {
        int rows = layer.GetLength(0), cols = layer.GetLength(1);
        if (col < 0 || col >= cols || row < 0 || row >= rows) return 0;
        if (limit.HasValue && !limit.Value.Contains(col, row)) return 0;
        int target = layer[row, col];
        if (target == tileId) return 0;

        int minC = 0, maxC = cols - 1, minR = 0, maxR = rows - 1;
        if (limit.HasValue)
        {
            minC = Math.Max(minC, limit.Value.Col); maxC = Math.Min(maxC, limit.Value.Right);
            minR = Math.Max(minR, limit.Value.Row); maxR = Math.Min(maxR, limit.Value.Bottom);
        }

        int changed = 0;
        var stack = new Stack<(int c, int r)>();
        stack.Push((col, row));
        while (stack.Count > 0)
        {
            var (c, r) = stack.Pop();
            if (c < minC || c > maxC || r < minR || r > maxR) continue;
            if (layer[r, c] != target) continue; // すでに塗った／別のタイル
            layer[r, c] = tileId;
            changed++;
            stack.Push((c + 1, r)); stack.Push((c - 1, r));
            stack.Push((c, r + 1)); stack.Push((c, r - 1));
        }
        return changed;
    }
}
