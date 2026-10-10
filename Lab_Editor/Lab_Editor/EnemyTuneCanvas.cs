using System.Drawing.Drawing2D;

namespace Lab_Editor;

// 「敵の動きを調整」画面の左側のプレビュー。
//
// 数値を変えると、その敵が「どう動くか・何がどれだけ変わるか」を絵で見せる。
// 型ごとに描くものが違う（TunePreview）：
//   Range      … 見つける距離・効果範囲などを、敵を中心にした円で見せる。プレイヤー（青い丸）をドラッグして距離を変えられる
//   Wave       … 周期的に変わる量（浮遊・大きさ・速さ・ズーム）の波形グラフと、いまの位置
//   Brightness … 暗転：敵からの距離ごとの画面の明るさ
//   Tint       … 色変化：寄せる色と、距離ごとの画面の色味
//   Shot       … 射撃：弾の向き・本数・ブレ、予兆の長さ
//   Timeline   … 溜め→行動→後隙 などの、時間の配分
//
// 式はゲーム本体（DrawPixel.cpp）の同じ処理と同じ形で書いてある。
public sealed class EnemyTuneCanvas : Panel
{
    // 実際の値（既定を使うものは既定値に置き換え済み）を返す関数。色が未指定のときは -1
    public Func<string, float> Eff = _ => 0f;
    public TuneType? TypeDef;
    // プレイヤーまでの距離（px）。ドラッグで変える
    public float PlayerDist = 180f;

    private float _time;                 // アニメーションの時計（フレーム）
    private bool _dragging;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    private static readonly Font FSmall = new("Meiryo UI", 8.5f);
    private static readonly Font FNormal = new("Meiryo UI", 9.5f);
    private static readonly Font FBold = new("Meiryo UI", 10f, FontStyle.Bold);

    public EnemyTuneCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(34, 36, 42);
        _timer.Tick += (s, e) => { _time += 2f; Invalidate(); };
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    // ================= 操作 =================

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _dragging = true; SetPlayerFromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) SetPlayerFromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; }

    private float _pxScale = 1f;      // 1px(ゲーム) あたりの画面px
    private float _originX;           // 敵の画面X
    private void SetPlayerFromMouse(int mx)
    {
        if (_pxScale <= 0.0001f) return;
        PlayerDist = Math.Clamp((mx - _originX) / _pxScale, 0f, 1200f);
        Invalidate();
    }

    // ================= 描画 =================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if (TypeDef == null) return;
        switch (TypeDef.Preview)
        {
            case TunePreview.Wave: DrawWave(g); break;
            case TunePreview.Brightness: DrawBrightness(g); break;
            case TunePreview.Tint: DrawTint(g); break;
            case TunePreview.Shot: DrawShot(g); break;
            case TunePreview.Timeline: DrawTimeline(g); break;
            case TunePreview.Script: DrawMessage(g, "この敵は、ブロックのスクリプトで動きを作ります。\n調整する数値はありません。\n（アセット管理の「挙動スクリプトを編集」から編集できます）"); break;
            default: DrawRange(g); break;
        }
    }

    private void Text(Graphics g, string s, float x, float y, Color c, Font? f = null, bool center = false)
    {
        using var br = new SolidBrush(c);
        var sf = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near };
        g.DrawString(s, f ?? FNormal, br, x, y, sf);
    }

    private void DrawMessage(Graphics g, string msg)
    {
        Text(g, msg, Width / 2f, Height / 2f - 30, Color.Silver, FNormal, true);
    }

    private void Title(Graphics g, string title, string sub)
    {
        Text(g, title, 12, 8, Color.White, FBold);
        Text(g, sub, 12, 28, Color.FromArgb(170, 170, 180), FSmall);
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // ---- 範囲（距離）----
    private static readonly (string key, string label, Color color)[] RangeKeys =
    {
        ("triggerRange", "気づく距離", Color.FromArgb(110, 170, 255)),
        ("effectRange", "効果が届く距離", Color.FromArgb(255, 170, 80)),
        ("teleportRangeMax", "瞬間移動の最長距離", Color.FromArgb(200, 120, 255)),
        ("teleportRangeMin", "瞬間移動の最短距離", Color.FromArgb(160, 100, 220)),
        ("shockwaveRadius", "着地の衝撃波", Color.FromArgb(255, 110, 110)),
    };

    private void DrawRange(Graphics g)
    {
        Title(g, "距離のイメージ", "青い丸（プレイヤー）をドラッグして、距離を変えて確かめられます。");
        float cy = Height / 2f + 10;
        var rings = RangeKeys.Where(r => TypeDef!.Groups.SelectMany(gr => gr.Params).Any(p => p.Key == r.key)).Select(r => (r.label, r.color, v: Eff(r.key))).Where(r => r.v > 0).ToList();
        float maxPx = Math.Max(160f, Math.Max(PlayerDist, rings.Count > 0 ? rings.Max(r => r.v) : 0f));
        _originX = Width * 0.25f;
        _pxScale = (Width - _originX - 24) / (maxPx * 1.05f);
        float ey = cy;
        // 地面
        using (var ground = new SolidBrush(Color.FromArgb(60, 64, 74))) g.FillRectangle(ground, 0, ey + 22, Width, Height);
        float lineY = 62;
        foreach (var r in rings)
        {
            float rad = r.v * _pxScale;
            using var pen = new Pen(r.color, 1.6f) { DashStyle = DashStyle.Dash };
            g.DrawEllipse(pen, _originX - rad, ey - rad, rad * 2, rad * 2);
            // 説明を左上に並べる（距離の凡例）
            using var sw = new SolidBrush(r.color); g.FillRectangle(sw, 12, lineY + 3, 10, 10);
            bool inside = PlayerDist <= r.v;
            Text(g, $"{r.label}  {r.v:0} px  →  プレイヤーは{(inside ? "範囲の中" : "範囲の外")}", 28, lineY, inside ? Color.White : Color.Gray, FSmall);
            lineY += 17;
        }
        // 敵
        using (var eb = new SolidBrush(Color.FromArgb(220, 80, 80))) g.FillEllipse(eb, _originX - 14, ey - 14, 28, 28);
        Text(g, "敵", _originX, ey - 38, Color.White, FSmall, true);
        // プレイヤー
        float px = _originX + PlayerDist * _pxScale;
        using (var pb = new SolidBrush(Color.FromArgb(90, 150, 255))) g.FillEllipse(pb, px - 10, ey - 10, 20, 20);
        Text(g, $"プレイヤー  {PlayerDist:0} px", px, ey + 14, Color.FromArgb(150, 190, 255), FSmall, true);
        // 速さの目安
        float ms = Eff("moveSpeed");
        if (ms > 0 && TypeDef!.Groups.SelectMany(gr => gr.Params).Any(p => p.Key == "moveSpeed"))
            Text(g, $"歩く速さ：1秒に約 {ms * 4f * 60f:0} px（プレイヤーが基本の速さで歩くとき、その {ms:0.##} 倍）", 12, Height - 22, Color.FromArgb(170, 220, 170), FSmall);
    }

    // ---- 波形 ----
    private void DrawWave(Graphics g)
    {
        string t = TypeDef!.Type switch { 10 => "浮き沈み（上下の位置）", 15 => "大きさの変化", 16 => "速さの変化", _ => "画面のズームの揺れ" };
        Title(g, t, "横が時間（フレーム）。緑の丸が「いま」の値です。");
        float freq = TypeDef.Type switch { 10 => Eff("floatFrequency"), 15 => Eff("sizeFrequency"), 16 => Eff("tempoFrequency"), _ => Eff("zoomFrequency") };
        if (freq <= 0.0001f) freq = 0.05f;
        float shape = Eff("waveShape");
        float period = 6.2831853f / freq;
        float span = Math.Max(240f, period * 2.2f);
        var area = new RectangleF(54, 70, Width - 74, Height - 150);
        // 値の範囲と、値を作る関数
        float lo, hi, mid; Func<float, float> val; string unit;
        switch (TypeDef.Type)
        {
            case 10: { float a = Eff("floatAmplitude"); lo = -a; hi = a; mid = 0; unit = "px"; val = tt => a * Wave(tt * freq, shape); break; }
            case 15:
            {
                float a = Eff("sizeAmplitude"), mn = Eff("minScale"), mx = Eff("maxScale") > 0 ? Eff("maxScale") : 99f;
                val = tt => Math.Min(mx, Math.Max(mn, 1f + a * Wave(tt * freq, shape)));
                lo = Math.Max(mn, 1f - a); hi = Math.Min(mx, 1f + a); mid = 1f; unit = "倍"; break;
            }
            case 16:
            {
                float mn = Eff("tempoMin"), mx = Eff("tempoMax");
                val = tt => mn + (mx - mn) * (0.5f + 0.5f * Wave(tt * freq, shape));
                lo = mn; hi = mx; mid = (mn + mx) / 2f; unit = "倍"; break;
            }
            default: { float a = Eff("zoomAmplitude"); val = tt => 1f + a * Wave(tt * freq, shape); lo = 1f - a; hi = 1f + a; mid = 1f; unit = "倍"; break; }
        }
        if (hi - lo < 0.0001f) { hi = lo + 1f; }
        float pad = (hi - lo) * 0.15f;
        float vmin = lo - pad, vmax = hi + pad;
        float Y(float v) => area.Bottom - (v - vmin) / (vmax - vmin) * area.Height;
        using (var fr = new Pen(Color.FromArgb(70, 74, 86))) g.DrawRectangle(fr, area.X, area.Y, area.Width, area.Height);
        using (var mp = new Pen(Color.FromArgb(70, 74, 86)) { DashStyle = DashStyle.Dot }) g.DrawLine(mp, area.X, Y(mid), area.Right, Y(mid));
        Text(g, $"{hi:0.##}{unit}", 4, Y(hi) - 7, Color.FromArgb(200, 200, 120), FSmall);
        Text(g, $"{lo:0.##}{unit}", 4, Y(lo) - 7, Color.FromArgb(200, 200, 120), FSmall);
        var pts = new List<PointF>();
        for (int i = 0; i <= 200; i++) { float tt = span * i / 200f; pts.Add(new PointF(area.X + area.Width * i / 200f, Y(val(tt)))); }
        using (var pen = new Pen(Color.FromArgb(110, 190, 255), 2f)) g.DrawLines(pen, pts.ToArray());
        float now = _time % span;
        float nx = area.X + area.Width * now / span, ny = Y(val(now));
        using (var gb = new SolidBrush(Color.FromArgb(120, 230, 140))) g.FillEllipse(gb, nx - 5, ny - 5, 10, 10);
        Text(g, $"いまの値： {val(now):0.##}{unit}", 12, Height - 70, Color.FromArgb(120, 230, 140), FNormal);
        Text(g, $"1往復： {period:0} フレーム（約 {period / 60f:0.0} 秒）", 12, Height - 48, Color.FromArgb(190, 190, 200), FSmall);
        if (TypeDef.Type == 15 && Eff("maxScale") > 0) Text(g, "上限（いちばん大きい大きさ）で頭打ちになります。", 12, Height - 28, Color.FromArgb(200, 200, 120), FSmall);
    }

    // 波形（ゲームの EnemyWave と同じ。x はラジアン）
    private static float Wave(float x, float shapeParam)
    {
        int shape = shapeParam > 0.5f ? (int)(shapeParam + 0.5f) : 0;
        if (shape == 0) return MathF.Sin(x);
        return PartMotionEvaluator.Wave(x / 6.2831853f, shape);
    }

    // ---- 暗転 ----
    private void DrawBrightness(Graphics g)
    {
        Title(g, "暗転：敵からの距離と、画面の明るさ", "左が敵のすぐそば、右が効果の端です。青い丸（プレイヤー）をドラッグできます。");
        float range = Math.Max(1f, Eff("effectRange")), bmin = Eff("brightnessMin"), bmax = Eff("brightenMax"), counter = Eff("counterBrightness");
        _originX = 40; _pxScale = (Width - 80) / range;
        DrawGradientBar(g, 40, 70, Width - 80, 30, "ふつうの暗転", d => Math.Clamp(1f - (1f - d / range) * (1f - bmin), 0f, 1f), range, true, f => BrightColor(f));
        DrawGradientBar(g, 40, 140, Width - 80, 30, "向きを反転したとき（明るくなる）", d => 1f + (1f - d / range) * (bmax - 1f), range, true, f => BrightColor(f));
        float pd = Math.Min(PlayerDist, range * 1.2f);
        float px = 40 + pd * _pxScale;
        using (var pb = new SolidBrush(Color.FromArgb(90, 150, 255))) g.FillEllipse(pb, px - 8, 98, 16, 16);
        float cur = PlayerDist >= range ? 1f : 1f - (1f - PlayerDist / range) * (1f - bmin);
        Text(g, $"プレイヤーが敵から {PlayerDist:0} px のとき、画面の明るさは {cur:0.00}" + (PlayerDist >= range ? "（効果の外なので変わりません）" : ""), 12, 190, Color.White, FNormal);
        // 見本の画面
        var scene = new Rectangle(40, 224, Math.Min(Width - 80, 300), Math.Min(Height - 270, 170));
        if (scene.Height > 30) DrawMockScreen(g, scene, Color.FromArgb(255, 255, 255), cur);
        Text(g, $"明るさ {bmin:0.00} が、いちばん暗いとき。{counter:0.00} より明るい画面（Cキーで明転）にすると、暗転を打ち消せます。", 12, Height - 24, Color.FromArgb(190, 190, 200), FSmall);
    }

    private static Color BrightColor(float f) { int v = (int)Math.Clamp(f * 130f, 0f, 255f); return Color.FromArgb(v, v, v); }

    private void DrawGradientBar(Graphics g, float x, float y, float w, float h, string label, Func<float, float> f, float range, bool showNumbers, Func<float, Color> colorOf)
    {
        Text(g, label, x, y - 18, Color.FromArgb(200, 200, 210), FSmall);
        int n = (int)w;
        for (int i = 0; i < n; i++)
        {
            float d = range * i / n;
            using var br = new SolidBrush(colorOf(f(d)));
            g.FillRectangle(br, x + i, y, 2, h);
        }
        using var pen = new Pen(Color.FromArgb(120, 124, 136)); g.DrawRectangle(pen, x, y, w, h);
        if (showNumbers)
        {
            Text(g, $"{f(0):0.00}", x, y + h + 1, Color.Silver, FSmall);
            Text(g, $"{f(range):0.00}", x + w - 28, y + h + 1, Color.Silver, FSmall);
        }
    }

    // ゲーム画面の見本（空・地面・キャラ）に、明るさ・色味を掛けて見せる
    private void DrawMockScreen(Graphics g, Rectangle r, Color tint, float brightness)
    {
        Color Mul(Color c) => Color.FromArgb(
            (int)Math.Clamp(c.R * tint.R / 255f * brightness, 0, 255), (int)Math.Clamp(c.G * tint.G / 255f * brightness, 0, 255), (int)Math.Clamp(c.B * tint.B / 255f * brightness, 0, 255));
        using (var sky = new SolidBrush(Mul(Color.FromArgb(70, 90, 150)))) g.FillRectangle(sky, r);
        using (var gr = new SolidBrush(Mul(Color.FromArgb(150, 110, 90)))) g.FillRectangle(gr, r.X, r.Bottom - r.Height / 4, r.Width, r.Height / 4);
        using (var pl = new SolidBrush(Mul(Color.FromArgb(240, 240, 120)))) g.FillEllipse(pl, r.X + r.Width * 0.2f, r.Bottom - r.Height / 4 - 24, 24, 24);
        using (var en = new SolidBrush(Mul(Color.FromArgb(220, 80, 80)))) g.FillEllipse(en, r.X + r.Width * 0.7f, r.Bottom - r.Height / 4 - 28, 28, 28);
        using var pen = new Pen(Color.FromArgb(120, 124, 136)); g.DrawRectangle(pen, r);
    }

    // ---- 色変化 ----
    private void DrawTint(Graphics g)
    {
        Title(g, "色変化：画面が寄っていく色", "青い丸（プレイヤー）をドラッグすると、距離ごとの画面の色味が見られます。");
        float range = Math.Max(1f, Eff("effectRange")), strength = Eff("tintStrength");
        float cr = Eff("tintColorR"), cg = Eff("tintColorG"), cb = Eff("tintColorB");
        bool custom = cr >= 0 && cg >= 0 && cb >= 0;
        _originX = 40; _pxScale = (Width - 80) / range;
        // 寄せる色の見本
        if (custom)
        {
            using (var sw = new SolidBrush(Color.FromArgb((int)cr, (int)cg, (int)cb))) g.FillRectangle(sw, 12, 62, 38, 26);
            Text(g, $"寄せる色  R{cr:0} G{cg:0} B{cb:0}", 58, 66, Color.White, FNormal);
        }
        else
        {
            Text(g, "寄せる色：指定なし（ゲーム内で敵を傾けるたびに 赤 → 緑 → 青）", 12, 66, Color.White, FNormal);
            Color[] prim = { Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255) };
            for (int i = 0; i < 3; i++) { using var sw = new SolidBrush(prim[i]); g.FillRectangle(sw, 12 + i * 34, 90, 28, 18); }
            cr = 255; cg = 0; cb = 0; // 見本は赤で描く
            Text(g, "↓ 見本（赤のとき）", 120, 91, Color.Silver, FSmall);
        }
        float tr = cr / 255f, tg = cg / 255f, tb = cb / 255f;
        Func<float, Color> tintAt = d =>
        {
            float t = Math.Clamp(1f - d / range, 0f, 1f);
            float R = 1f - t * strength * (1f - tr), G = 1f - t * strength * (1f - tg), B = 1f - t * strength * (1f - tb);
            return Color.FromArgb((int)(255 * R), (int)(255 * G), (int)(255 * B));
        };
        DrawGradientBar(g, 40, 148, Width - 80, 30, "敵からの距離ごとの色味（右へ行くほど遠い）", d => d, range, false, d => { var t = tintAt(d); return Color.FromArgb((int)(240 * t.R / 255f), (int)(236 * t.G / 255f), (int)(224 * t.B / 255f)); });
        float pd = Math.Min(PlayerDist, range * 1.2f);
        float px = 40 + pd * _pxScale;
        using (var pb = new SolidBrush(Color.FromArgb(90, 150, 255))) g.FillEllipse(pb, px - 8, 186, 16, 16);
        var cur = PlayerDist >= range ? Color.White : tintAt(PlayerDist);
        Text(g, $"プレイヤーが {PlayerDist:0} px のとき" + (PlayerDist >= range ? "（効果の外）" : ""), 12, 208, Color.White, FNormal);
        var scene = new Rectangle(40, 236, Math.Min(Width - 80, 300), Math.Min(Height - 285, 160));
        if (scene.Height > 30) DrawMockScreen(g, scene, cur, 1f);
        string ctr = Eff("counterFilter") switch { < 0 => "寄せる色と同じ色", 0 => "打ち消せない", 1 => "赤", 2 => "緑", _ => "青" };
        Text(g, $"打ち消せる色フィルタ（Tキー）： {ctr}", 12, Height - 24, Color.FromArgb(190, 190, 200), FSmall);
    }

    // ---- 射撃 ----
    private void DrawShot(Graphics g)
    {
        Title(g, "撃ち方のイメージ", "左が敵。弾の向き・本数・狙いのブレを真上から見た図です。");
        float cx = 80, cy = Height / 2f - 10;
        float jitter = Eff("aimJitter"); if (jitter < 0) jitter = 0;
        bool hasSpread = TypeDef!.Groups.SelectMany(gr => gr.Params).Any(p => p.Key == "spreadCount");
        float len = Math.Min(Width - 140, 360);
        using (var eb = new SolidBrush(Color.FromArgb(220, 80, 80))) g.FillEllipse(eb, cx - 14, cy - 14, 28, 28);
        if (hasSpread)
        {
            int count = Math.Max(1, (int)Eff("spreadCount"));
            float ang = Eff("spreadAngle");
            bool radial = Eff("radialFire") > 0.5f;
            float step = Eff("spreadRotationStep");
            for (int i = 0; i < count; i++)
            {
                float a = radial ? (6.2831853f * i / count + _time * 0.0f) : (count == 1 ? 0 : -ang + 2f * ang * i / (count - 1));
                if (radial) a += (_time / 120f) * step * 6f; // 回転の見本
                float rl = radial ? len * 0.55f : len;
                using var pen = new Pen(Color.FromArgb(255, 200, 90), 2f);
                float ex = cx + MathF.Cos(a) * rl, ey = cy + MathF.Sin(a) * rl;
                g.DrawLine(pen, cx, cy, ex, ey);
                using var db = new SolidBrush(Color.FromArgb(255, 210, 110)); g.FillEllipse(db, ex - 4, ey - 4, 8, 8);
            }
            Text(g, radial ? $"全方向 {count} 本" + (step > 0 ? "（撃つたびに少しずつ回る）" : "") : $"扇状 {count} 本（正面から左右へ ±{ang * 57.3f:0}度）", 12, 62, Color.White, FNormal);
        }
        else
        {
            // 単発：プレイヤーへ向かう線と、ブレの扇
            float px = cx + len * 0.85f, py = cy - 30;
            float baseA = MathF.Atan2(py - cy, px - cx);
            if (jitter > 0.001f)
            {
                using var cone = new SolidBrush(Color.FromArgb(50, 255, 200, 90));
                g.FillPie(cone, cx - len, cy - len, len * 2, len * 2, (baseA - jitter) * 57.2958f, jitter * 2 * 57.2958f);
            }
            using (var pen = new Pen(Color.FromArgb(255, 200, 90), 2f)) g.DrawLine(pen, cx, cy, px, py);
            using (var pb = new SolidBrush(Color.FromArgb(90, 150, 255))) g.FillEllipse(pb, px - 9, py - 9, 18, 18);
            Text(g, jitter > 0.001f ? $"狙いのブレ ±{jitter * 57.3f:0.#}度（薄いオレンジの範囲のどこかへ飛びます）" : "狙いのブレなし（いつも正確に狙います）", 12, 62, Color.White, FNormal);
        }
        // 間隔と予兆のバー
        float interval = Eff("actionInterval") > 0 ? Eff("actionInterval") : (Eff("cooldownTime") > 0 ? Eff("cooldownTime") : 0);
        float warn = Eff("chargeWarnFrames");
        if (interval > 0)
        {
            float bx = 12, by = Height - 56, bw = Width - 24;
            using (var bb = new SolidBrush(Color.FromArgb(70, 74, 86))) g.FillRectangle(bb, bx, by, bw, 16);
            if (TypeDef.Groups.SelectMany(gr => gr.Params).Any(p => p.Key == "chargeWarnFrames") && warn > 0)
            {
                float ww = Math.Min(bw, bw * warn / interval);
                using var wb = new SolidBrush(Color.FromArgb(230, 190, 60)); g.FillRectangle(wb, bx + bw - ww, by, ww, 16);
            }
            float t = (_time % interval) / interval;
            using (var cb = new SolidBrush(Color.FromArgb(120, 230, 140))) g.FillRectangle(cb, bx + bw * t - 2, by - 3, 4, 22);
            Text(g, $"撃つ間隔 {interval:0} フレーム（約 {interval / 60f:0.0} 秒）" + (warn > 0 && TypeDef.Groups.SelectMany(gr => gr.Params).Any(p => p.Key == "chargeWarnFrames") ? $"　黄色＝撃つ前の予兆 {warn:0} フレーム" : ""), 12, by + 20, Color.FromArgb(190, 190, 200), FSmall);
        }
    }

    // ---- 時間の配分 ----
    private void DrawTimeline(Graphics g)
    {
        Title(g, "行動の流れ（時間の配分）", "帯の長さが、かかる時間の割合です。緑の線が「いま」。");
        var segs = new List<(string name, float frames, Color color)>();
        switch (TypeDef!.Type)
        {
            case 1: segs.Add(("待つ", Eff("actionInterval"), Color.FromArgb(90, 110, 150))); segs.Add(("跳ぶ", 40, Color.FromArgb(120, 200, 120))); break;
            case 6:
                segs.Add(("溜め", Eff("chargeTime"), Color.FromArgb(230, 190, 60)));
                segs.Add(("突進", Eff("dashDuration"), Color.FromArgb(230, 90, 80)));
                segs.Add(("休み", Eff("cooldownTime"), Color.FromArgb(90, 110, 150))); break;
            case 7:
                segs.Add(("予兆", Eff("fallDelay") + 26, Color.FromArgb(230, 190, 60)));
                segs.Add(("落下", 30, Color.FromArgb(230, 90, 80)));
                segs.Add(("休み", Eff("cooldownTime"), Color.FromArgb(90, 110, 150))); break;
            case 13:
                segs.Add(("無敵", Eff("shieldOnDuration"), Color.FromArgb(230, 200, 80)));
                segs.Add(("攻撃が効く", Eff("shieldOffDuration"), Color.FromArgb(90, 150, 220))); break;
            case 21:
                segs.Add(("這って近づく", 60, Color.FromArgb(90, 150, 110)));
                segs.Add(("溜め", Eff("chargeTime"), Color.FromArgb(230, 190, 60)));
                segs.Add(("飛ぶ（上限）", Eff("dashDuration"), Color.FromArgb(230, 90, 80)));
                segs.Add(("着地後", Eff("cooldownTime"), Color.FromArgb(90, 110, 150))); break;
        }
        float total = Math.Max(1f, segs.Sum(s => s.frames));
        float x0 = 20, y0 = 90, w = Width - 40, h = 44;
        float x = x0;
        foreach (var s in segs)
        {
            float sw = w * s.frames / total;
            using (var br = new SolidBrush(s.color)) g.FillRectangle(br, x, y0, Math.Max(sw, 1), h);
            using (var pen = new Pen(Color.FromArgb(34, 36, 42), 2)) g.DrawRectangle(pen, x, y0, Math.Max(sw, 1), h);
            if (sw > 36) Text(g, s.name, x + sw / 2, y0 + 5, Color.Black, FNormal, true);
            if (sw > 36) Text(g, $"{s.frames:0}f", x + sw / 2, y0 + 24, Color.FromArgb(40, 40, 40), FSmall, true);
            x += sw;
        }
        float now = x0 + w * ((_time % total) / total);
        using (var cb = new SolidBrush(Color.FromArgb(120, 230, 140))) g.FillRectangle(cb, now - 2, y0 - 8, 4, h + 16);
        Text(g, $"1回まわるのに {total:0} フレーム（約 {total / 60f:0.0} 秒）", 20, y0 + h + 18, Color.White, FNormal);
        int yy = (int)(y0 + h + 48);
        foreach (var s in segs) { Text(g, $"・{s.name}： {s.frames:0} フレーム（約 {s.frames / 60f:0.0} 秒）", 20, yy, Color.FromArgb(190, 190, 200), FSmall); yy += 18; }
        if (TypeDef.Type is 6 or 21) Text(g, "※ 溜めが長いほど、プレイヤーは動きを読んで避けやすくなります。", 20, Height - 26, Color.FromArgb(200, 200, 120), FSmall);
    }
}
