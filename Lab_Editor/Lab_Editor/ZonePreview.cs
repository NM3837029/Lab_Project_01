namespace Lab_Editor;

// ======================================================
// ZonePreview - 明暗ゾーン(18)・色調ゾーン(19)の「仕上がりの見本」を描く
//
// 以前は 色調R・G・B／彩度／色相／コントラスト／明転・暗転の輝度 を数字で打つだけで、
// 入った瞬間に画面がどんな色になるのかは、ゲームを起動して確かめるしかなかった。
// ここでは、白〜黒のグレーの帯と、よく出てくる色（赤・緑・青・黄・肌色・空色）の見本に、
// 入力中の値をゲームと同じ順番（色の掛け算 → 彩度 → 色相 → コントラスト）でかけた絵を作る。
//
// あくまで「目安」の見本。ゲームは画面全体の画像に GPU のフィルタをかけるので、細かな色味は少し違うことがある。
// 値が -1（未設定）のときは、ゲームの既定値（色は1倍、明転 1.6・暗転 0.35）として描く。
// ======================================================
public static class ZonePreview
{
    // 見本に並べる色（左から：赤・緑・青・黄・肌色・空色）
    private static readonly Color[] Swatches =
    {
        Color.FromArgb(220, 50, 50), Color.FromArgb(60, 180, 70), Color.FromArgb(50, 90, 220),
        Color.FromArgb(240, 210, 60), Color.FromArgb(240, 190, 150), Color.FromArgb(120, 190, 240),
    };

    // 色調ゾーンの見本。上の段＝もと、下の段＝ゾーンの中。各段は「グレーの帯」と「色の見本」。
    //   tr,tg,tb … 色の掛け算（-1＝1倍）  sat … 彩度の倍率  hue … 色相の回転（度）  contrast … -1〜1
    public static Bitmap Render(float tr, float tg, float tb, float sat, float hue, float contrast, int w, int h)
    {
        float r = tr < 0 ? 1f : tr, g = tg < 0 ? 1f : tg, b = tb < 0 ? 1f : tb;
        return Draw(w, h, new[] { "もと", "ゾーンの中" }, new Func<Color, Color>[]
        {
            c => c,
            c => Contrast(HueRotate(Saturate(Mul(c, r, g, b), sat), hue), contrast),
        });
    }

    // 明暗ゾーンの見本。上から：もと／暗転（darkLevel）／明転（brightLevel）。
    public static Bitmap RenderBright(float brightLevel, float darkLevel, int w, int h)
    {
        float bright = brightLevel < 0 ? 1.6f : brightLevel;
        float dark = darkLevel < 0 ? 0.35f : darkLevel;
        return Draw(w, h, new[] { "もと", $"暗転 ×{dark:0.00}", $"明転 ×{bright:0.00}" }, new Func<Color, Color>[]
        {
            c => c,
            c => Bright(c, dark),
            c => Bright(c, bright),
        });
    }

    // 段ごとに「名札・グレーの帯・色の見本」を描く
    private static Bitmap Draw(int w, int h, string[] names, Func<Color, Color>[] fx)
    {
        var bmp = new Bitmap(Math.Max(40, w), Math.Max(20, h));
        using var gfx = Graphics.FromImage(bmp);
        gfx.Clear(Color.White);
        int rows = names.Length;
        int rowH = bmp.Height / rows;
        const int nameW = 72;
        int gradW = (bmp.Width - nameW) / 2;
        int swW = bmp.Width - nameW - gradW;
        using var font = new Font(UiTheme.Small.FontFamily, 8f);
        for (int ri = 0; ri < rows; ri++)
        {
            int y = ri * rowH;
            gfx.DrawString(names[ri], font, Brushes.DimGray, 2, y + rowH / 2 - 7);
            // グレーの帯（黒→白を 16 段）
            const int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                int v = i * 255 / (steps - 1);
                using var br = new SolidBrush(fx[ri](Color.FromArgb(v, v, v)));
                int x0 = nameW + gradW * i / steps, x1 = nameW + gradW * (i + 1) / steps;
                gfx.FillRectangle(br, x0, y + 2, x1 - x0, rowH - 4);
            }
            // 色の見本
            for (int i = 0; i < Swatches.Length; i++)
            {
                using var br = new SolidBrush(fx[ri](Swatches[i]));
                int x0 = nameW + gradW + 4 + (swW - 4) * i / Swatches.Length, x1 = nameW + gradW + 4 + (swW - 4) * (i + 1) / Swatches.Length;
                gfx.FillRectangle(br, x0, y + 2, x1 - x0 - 2, rowH - 4);
            }
        }
        return bmp;
    }

    private static int Clamp255(float v) => (int)Math.Clamp(Math.Round(v), 0, 255);

    // 色の掛け算（ゲームの画面の色味 tint と同じ）
    private static Color Mul(Color c, float r, float g, float b) => Color.FromArgb(Clamp255(c.R * r), Clamp255(c.G * g), Clamp255(c.B * b));

    // 彩度：明るさ（輝度）を保ったまま、灰色との間を行き来する（0＝白黒、1＝そのまま、2＝倍の鮮やかさ）
    private static Color Saturate(Color c, float s)
    {
        float y = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        return Color.FromArgb(Clamp255(y + (c.R - y) * s), Clamp255(y + (c.G - y) * s), Clamp255(y + (c.B - y) * s));
    }

    // 色相：色の輪の上で deg 度回す（明るさ・鮮やかさはそのまま）
    private static Color HueRotate(Color c, float deg)
    {
        if (Math.Abs(deg) < 0.5f) return c;
        float hh = c.GetHue() + deg, ss = c.GetSaturation(), ll = c.GetBrightness();
        hh = ((hh % 360f) + 360f) % 360f;
        return FromHsl(hh, ss, ll);
    }

    // コントラスト：ゲームは「入力の範囲」を狭める（正）／広げる（負）レベル補正。目安として同じ形の直線で近づける
    private static Color Contrast(Color c, float k)
    {
        if (Math.Abs(k) < 0.01f) return c;
        float m = Math.Abs(k) * 80f; // ゲームと同じ量（|コントラスト|×80）
        float F(float v) => k > 0 ? (v - m) * 255f / Math.Max(1f, 255f - 2f * m) : m + v * (255f - 2f * m) / 255f;
        return Color.FromArgb(Clamp255(F(c.R)), Clamp255(F(c.G)), Clamp255(F(c.B)));
    }

    // 明るさ：1 より小さいと暗く、1 より大きいと白っぽく明るくなる
    private static Color Bright(Color c, float b)
    {
        if (b <= 1f) return Color.FromArgb(Clamp255(c.R * b), Clamp255(c.G * b), Clamp255(c.B * b));
        float t = Math.Min(1f, b - 1f); // 1 を越えたぶんだけ白へ寄せる
        return Color.FromArgb(Clamp255(c.R + (255 - c.R) * t * 0.6f), Clamp255(c.G + (255 - c.G) * t * 0.6f), Clamp255(c.B + (255 - c.B) * t * 0.6f));
    }

    // 色相・彩度・明度（HSL）から色を作る
    private static Color FromHsl(float h, float s, float l)
    {
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = l - c / 2f;
        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb(Clamp255((r + m) * 255f), Clamp255((g + m) * 255f), Clamp255((b + m) * 255f));
    }
}
