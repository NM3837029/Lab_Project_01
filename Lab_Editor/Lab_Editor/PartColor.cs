using System.Drawing.Imaging;

namespace Lab_Editor;

// パーツの色（"#RRGGBB"）と、色・不透明度を画像に掛けるための小さな道具。
//
// ゲーム側（DrawPixel.cpp の DrawPartsPass）は「色の倍率を SetDrawBright で掛け、255を超える分は加算合成で足す」。
// プレビュー（GDI+）は ColorMatrix の対角に倍率を入れれば同じ見た目になる（1を超えると白へ飛ぶ）。
// 加算合成は GDI+ では再現できないので、プレビューでは「少し薄くして重ねる」近似にしてある。
public static class PartColor
{
    // "#RRGGBB" を 0〜1 の倍率に直す。空・不正なら (1,1,1)=絵のまま。戻り値は「色が指定されていたか」
    public static bool TryParse(string? hex, out float r, out float g, out float b)
    {
        r = g = b = 1f;
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
        if (!int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
        r = ((rgb >> 16) & 0xFF) / 255f; g = ((rgb >> 8) & 0xFF) / 255f; b = (rgb & 0xFF) / 255f;
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color ToColor(string? hex)
        => TryParse(hex, out float r, out float g, out float b) ? Color.FromArgb((int)(r * 255f + 0.5f), (int)(g * 255f + 0.5f), (int)(b * 255f + 0.5f)) : Color.Empty;

    // 色相(度)・彩度・明度 → RGB(0〜1)。ゲーム側 ColorGrade.h の HsvToRgb と同じ式
    public static void HsvToRgb(float hDeg, float s, float v, out float r, out float g, out float b)
    {
        float h = hDeg % 360f; if (h < 0f) h += 360f;
        h /= 60f;
        int i = (int)h; float f = h - i;
        float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }

    // 色の倍率と不透明度を掛ける設定。何も変えないなら null（呼び出し側はそのまま描く）。使い終わったら Dispose すること。
    public static ImageAttributes? MakeAttributes(float r, float g, float b, float alpha)
    {
        if (Math.Abs(r - 1f) < 0.001f && Math.Abs(g - 1f) < 0.001f && Math.Abs(b - 1f) < 0.001f && alpha > 0.999f) return null;
        var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { r, 0f, 0f, 0f, 0f }, new[] { 0f, g, 0f, 0f, 0f }, new[] { 0f, 0f, b, 0f, 0f },
            new[] { 0f, 0f, 0f, Math.Clamp(alpha, 0f, 1f), 0f }, new[] { 0f, 0f, 0f, 0f, 1f },
        }));
        return ia;
    }
}
