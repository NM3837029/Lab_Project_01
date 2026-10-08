using System.Drawing.Drawing2D;

namespace Lab_Editor;

// タイトル画面の要素（ロゴ・タイトル文字・サブタイトル・メニュー）を
// ドラッグして配置するためのキャンバス。
//
// 座標系は「ゲーム内部解像度 640x480」で、ゲーム本体の game_config.json と同じ。
// キャンバスはこの 640x480 をコントロールの大きさに合わせて縮小表示するだけなので、
// ここで見た位置がそのままゲームに出る（WYSIWYG）。
//
// 手本にしたのは MapCanvas。「自由な座標へ物を置く」という構造が同じで、
// BlockCanvasControl の挿入位置計算やソケット判定はここでは要らない。
public class TitleLayoutCanvas : Panel
{
    // ゲーム内部解像度。C++側の SCREEN_WIDTH / SCREEN_HEIGHT と一致させること。
    public const float DESIGN_W = 640f;
    public const float DESIGN_H = 480f;

    // 編集対象。呼び出し側が差し替えたら SetConfig を呼ぶ。
    private GameConfig _cfg = GameConfig.CreateDefault();
    private string _projectRoot = "";

    // 現在選択されている要素のキー（"logo" / "title" / "subtitle" / "menu"）。空なら未選択。
    public string SelectedKey { get; private set; } = "";

    // 選択が変わったとき／ドラッグで座標が変わったときに発火する。
    // 右側のプロパティ欄を追従させるために使う。
    public event EventHandler? SelectionChanged;
    public event EventHandler? LayoutChanged;

    // 表示倍率とオフセット（640x480 をコントロール中央へ収める）
    private float _zoom = 1f;
    private float _offX, _offY;
    private float _userZoom = 0f; // Ctrl+ホイールで上書きした倍率。0なら自動フィット

    // ドラッグ状態
    private bool _dragging, _resizing;
    private PointF _dragStartDesign;
    private float _elemStartX, _elemStartY, _elemStartW, _elemStartH;

    // 画像キャッシュ（パス→Bitmap）。
    // Image.FromFile はファイルを掴んだままにするので使わない（MapCanvas と同じ理由）。
    private readonly Dictionary<string, Bitmap?> _imageCache = new();

    private const int HANDLE_SIZE = 7;   // リサイズハンドルの一辺(画面px)
    private const float SNAP_GRID = 8f;  // スナップの粒度(デザインpx)
    private const float SNAP_NEAR = 6f;  // 中心線・端へ吸着する距離(デザインpx)

    public TitleLayoutCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        // 矢印キーで1pxずつ動かせるようにフォーカスを受け取れる状態にしておく
        TabStop = true;
        BackColor = Color.FromArgb(70, 72, 78);
    }

    public void SetConfig(GameConfig cfg, string projectRoot)
    {
        _cfg = cfg;
        _projectRoot = projectRoot;
        _imageCache.Clear();
        Invalidate();
    }

    public TitleElement? Selected =>
        _cfg.title_screen.elements.FirstOrDefault(e => e.key == SelectedKey);

    public void SelectKey(string key)
    {
        SelectedKey = key;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // ---- 座標変換 ----------------------------------------------------

    private void RecalcView()
    {
        float fit = Math.Min(ClientSize.Width / DESIGN_W, ClientSize.Height / DESIGN_H);
        if (fit <= 0f) fit = 0.1f;
        _zoom = (_userZoom > 0f) ? _userZoom : fit;
        _offX = (ClientSize.Width - DESIGN_W * _zoom) / 2f;
        _offY = (ClientSize.Height - DESIGN_H * _zoom) / 2f;
    }

    private PointF ToDesign(Point p) => new((p.X - _offX) / _zoom, (p.Y - _offY) / _zoom);
    private float SX(float dx) => _offX + dx * _zoom;
    private float SY(float dy) => _offY + dy * _zoom;
    private float SL(float d) => d * _zoom;

    // ---- 要素の矩形（デザイン座標）------------------------------------

    // 要素の「掴める矩形」を返す。
    // text は実際の描画幅から、image と menu は w/h から求める。
    private RectangleF ElementRect(TitleElement e, Graphics g)
    {
        if (e.type == "menu")
        {
            int n = Math.Max(1, _cfg.title_screen.menu_items.Count);
            float h = n * e.item_h + (n - 1) * e.gap;
            float l = (e.align == "center") ? e.x - e.w / 2f : e.x;
            return new RectangleF(l, e.y, e.w, h);
        }
        if (e.type == "image")
        {
            float l = (e.align == "center") ? e.x - e.w / 2f : e.x;
            float t = (e.align == "center") ? e.y - e.h / 2f : e.y;
            return new RectangleF(l, t, e.w, e.h);
        }
        // text: フォントの実測幅で矩形を作る
        using var f = DesignFont(e.font_size);
        SizeF sz = g.MeasureString(string.IsNullOrEmpty(e.text) ? " " : e.text, f);
        float tw = sz.Width / _zoom;
        float th = sz.Height / _zoom;
        float lx = (e.align == "center") ? e.x - tw / 2f : e.x;
        return new RectangleF(lx, e.y, tw, th);
    }

    // デザイン上のフォントサイズを、現在の表示倍率に合わせた実フォントへ変換する
    private Font DesignFont(int designSize)
    {
        float px = Math.Max(4f, designSize * _zoom);
        return new Font("Meiryo UI", px, GraphicsUnit.Pixel);
    }

    private bool CanResize(TitleElement e) => e.type == "image" || e.type == "menu";

    // ---- 描画 --------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        RecalcView();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        var screen = new RectangleF(SX(0), SY(0), SL(DESIGN_W), SL(DESIGN_H));

        // 画面の下地
        using (var b = new SolidBrush(ToColor(_cfg.theme.backdrop)))
            g.FillRectangle(b, screen);

        // 背景画像（ゲーム側はウィンドウ全面へ引き伸ばすが、
        // ここでは640x480の枠に合わせて見せる。位置合わせの用途には十分）
        var bg = LoadImage(_cfg.title_screen.background_image);
        if (bg != null) g.DrawImage(bg, screen);

        // 画面の端が分かるように枠と中心線を引く
        using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
        {
            g.DrawLine(pen, SX(DESIGN_W / 2f), SY(0), SX(DESIGN_W / 2f), SY(DESIGN_H));
            g.DrawLine(pen, SX(0), SY(DESIGN_H / 2f), SX(DESIGN_W), SY(DESIGN_H / 2f));
        }
        using (var pen = new Pen(Color.White, 2f)) g.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);

        // 要素
        foreach (var el in _cfg.title_screen.elements)
        {
            if (!el.visible) continue;
            DrawElement(g, el);
        }

        // 選択枠とハンドル
        var sel = Selected;
        if (sel != null && sel.visible)
        {
            var r = ElementRect(sel, g);
            var sr = new RectangleF(SX(r.X), SY(r.Y), SL(r.Width), SL(r.Height));
            using var pen = new Pen(Color.OrangeRed, 1.6f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, sr.X, sr.Y, sr.Width, sr.Height);
            if (CanResize(sel))
            {
                using var hb = new SolidBrush(Color.OrangeRed);
                g.FillRectangle(hb, sr.Right - HANDLE_SIZE, sr.Bottom - HANDLE_SIZE, HANDLE_SIZE, HANDLE_SIZE);
            }
        }

        // 使い方の案内（キャンバスは初見だと何ができるか分からないため）
        using var hint = new Font("Meiryo UI", 8.5f);
        using var hb2 = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        g.DrawString("ドラッグで移動 / 矢印キーで1px / Shift+矢印で10px / Shift押しながらでスナップ無効 / Ctrl+ホイールで拡大",
                     hint, hb2, 6, ClientSize.Height - 20);
    }

    private void DrawElement(Graphics g, TitleElement el)
    {
        if (el.type == "image")
        {
            var r = ElementRect(el, g);
            var sr = new RectangleF(SX(r.X), SY(r.Y), SL(r.Width), SL(r.Height));
            var img = LoadImage(el.image);
            if (img != null) g.DrawImage(img, sr);
            else
            {
                // 画像が未設定でも「ここに何かある」ことが分かるように枠で示す
                using var p = new Pen(Color.FromArgb(160, 255, 255, 255), 1f) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(p, sr.X, sr.Y, sr.Width, sr.Height);
                using var f2 = new Font("Meiryo UI", 8f);
                using var b2 = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                g.DrawString("(画像なし)", f2, b2, sr.X + 4, sr.Y + 4);
            }
            return;
        }

        if (el.type == "menu")
        {
            // 【重要】このレイアウト式は C++ 側（DrawPixel.cpp の MetaMenuItemRect）にも
            // 同じものがある。片方だけ変えると「エディタで見た位置」と「実際に出る位置」が
            // ズレるので、変更するときは必ず両方を直すこと。
            //   align=="center" のとき:
            //     left = x - w/2,  top = y + i*(item_h + gap),  width = w,  height = item_h
            var items = _cfg.title_screen.menu_items;
            using var f = DesignFont(el.font_size);
            for (int i = 0; i < Math.Max(1, items.Count); i++)
            {
                float l = (el.align == "center") ? el.x - el.w / 2f : el.x;
                float t = el.y + i * (el.item_h + el.gap);
                var sr = new RectangleF(SX(l), SY(t), SL(el.w), SL(el.item_h));
                using (var b = new SolidBrush(Color.FromArgb(235, 252, 246, 236))) g.FillRectangle(b, sr);
                using (var p = new Pen(ToColor(_cfg.theme.ink_accent), 1.5f)) g.DrawRectangle(p, sr.X, sr.Y, sr.Width, sr.Height);
                string label = (i < items.Count) ? items[i].label : "(項目なし)";
                using var tb = new SolidBrush(ToColor(_cfg.theme.ink));
                var sz = g.MeasureString(label, f);
                g.DrawString(label, f, tb, sr.X + (sr.Width - sz.Width) / 2f,
                             sr.Y + (sr.Height - sz.Height) / 2f);
            }
            return;
        }

        // text
        using (var f = DesignFont(el.font_size))
        using (var b = new SolidBrush(RoleColor(el.color_role)))
        {
            string txt = string.IsNullOrEmpty(el.text) ? "(文字なし)" : el.text;
            var sz = g.MeasureString(txt, f);
            float lx = (el.align == "center") ? SX(el.x) - sz.Width / 2f : SX(el.x);
            // 縁取りの雰囲気だけ再現する（ゲーム側は DX_FONTTYPE_ANTIALIASING_EDGE_4X4）
            if (el.edge)
            {
                using var eb = new SolidBrush(Color.FromArgb(190, 255, 255, 255));
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0) g.DrawString(txt, f, eb, lx + dx, SY(el.y) + dy);
            }
            g.DrawString(txt, f, b, lx, SY(el.y));
        }
    }

    private Color RoleColor(string role) => role switch
    {
        "sub" => ToColor(_cfg.theme.ink_sub),
        "accent" => ToColor(_cfg.theme.ink_accent),
        _ => ToColor(_cfg.theme.ink),
    };

    private static Color ToColor(int[] rgb) =>
        (rgb != null && rgb.Length >= 3) ? Color.FromArgb(rgb[0], rgb[1], rgb[2]) : Color.Black;

    private Bitmap? LoadImage(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return null;
        if (_imageCache.TryGetValue(relPath, out var cached)) return cached;

        Bitmap? bmp = null;
        try
        {
            string full = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));
            if (File.Exists(full))
            {
                // Image.FromFile はファイルを掴んだままにするため、ゲーム本体が
                // 同じ画像を読めなくなる。一旦ストリームから読み切ってロックを残さない。
                using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                bmp = new Bitmap(Image.FromStream(fs));
            }
        }
        catch { bmp = null; }

        _imageCache[relPath] = bmp;
        return bmp;
    }

    // 画像を差し替えたときにキャッシュを捨てる
    public void InvalidateImageCache()
    {
        foreach (var kv in _imageCache) kv.Value?.Dispose();
        _imageCache.Clear();
        Invalidate();
    }

    // ---- マウス ------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // 矢印キーでの微調整を受け取れるようにする
        if (e.Button != MouseButtons.Left) return;

        RecalcView();
        var d = ToDesign(e.Location);
        using var g = CreateGraphics();

        // 選択中の要素のリサイズハンドルを先に見る（要素の重なりより優先）
        var sel = Selected;
        if (sel != null && sel.visible && CanResize(sel))
        {
            var r = ElementRect(sel, g);
            var handle = new RectangleF(SX(r.Right) - HANDLE_SIZE, SY(r.Bottom) - HANDLE_SIZE,
                                        HANDLE_SIZE + 2, HANDLE_SIZE + 2);
            if (handle.Contains(e.Location))
            {
                _resizing = true;
                _dragStartDesign = d;
                _elemStartW = sel.w; _elemStartH = sel.h;
                return;
            }
        }

        // 手前（配列の後ろ）から順にヒットテストする
        for (int i = _cfg.title_screen.elements.Count - 1; i >= 0; i--)
        {
            var el = _cfg.title_screen.elements[i];
            if (!el.visible) continue;
            if (ElementRect(el, g).Contains(d))
            {
                SelectKey(el.key);
                _dragging = true;
                _dragStartDesign = d;
                _elemStartX = el.x; _elemStartY = el.y;
                return;
            }
        }
        SelectKey(""); // 何も無い場所をクリックしたら選択解除
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var sel = Selected;
        if (sel == null) return;
        if (!_dragging && !_resizing) return;

        RecalcView();
        var d = ToDesign(e.Location);
        bool noSnap = (ModifierKeys & Keys.Shift) != 0;

        if (_resizing)
        {
            sel.w = Math.Max(16f, _elemStartW + (d.X - _dragStartDesign.X));
            if (sel.type == "image") sel.h = Math.Max(16f, _elemStartH + (d.Y - _dragStartDesign.Y));
            if (!noSnap) { sel.w = Snap(sel.w); if (sel.type == "image") sel.h = Snap(sel.h); }
        }
        else
        {
            float nx = _elemStartX + (d.X - _dragStartDesign.X);
            float ny = _elemStartY + (d.Y - _dragStartDesign.Y);
            if (!noSnap)
            {
                nx = Snap(nx); ny = Snap(ny);
                // 画面中央と端へ吸着させる。中央揃えのレイアウトを作りやすくするため。
                if (Math.Abs(nx - DESIGN_W / 2f) < SNAP_NEAR) nx = DESIGN_W / 2f;
                if (Math.Abs(nx) < SNAP_NEAR) nx = 0f;
                if (Math.Abs(nx - DESIGN_W) < SNAP_NEAR) nx = DESIGN_W;
                if (Math.Abs(ny) < SNAP_NEAR) ny = 0f;
                if (Math.Abs(ny - DESIGN_H) < SNAP_NEAR) ny = DESIGN_H;
            }
            sel.x = nx; sel.y = ny;
        }
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging || _resizing)
        {
            _dragging = _resizing = false;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // Ctrl+ホイールで拡大縮小（1px単位の微調整をしたいときのため）。
        // 通常のホイールは何もしない（スクロールが要るほど広いキャンバスではない）。
        if ((ModifierKeys & Keys.Control) == 0) return;
        RecalcView();
        float cur = _zoom;
        float next = cur * (e.Delta > 0 ? 1.15f : 1f / 1.15f);
        _userZoom = Math.Clamp(next, 0.3f, 4f);
        Invalidate();
    }

    // ダブルクリックで自動フィットへ戻す
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        _userZoom = 0f;
        Invalidate();
    }

    private static float Snap(float v) => (float)Math.Round(v / SNAP_GRID) * SNAP_GRID;

    // ---- キーボード --------------------------------------------------

    protected override bool IsInputKey(Keys keyData)
    {
        // 矢印キーをコントロール間のフォーカス移動ではなく自分で受け取る
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var sel = Selected;
        if (sel == null) return;

        float step = e.Shift ? 10f : 1f;
        bool moved = true;
        switch (e.KeyCode)
        {
            case Keys.Left: sel.x -= step; break;
            case Keys.Right: sel.x += step; break;
            case Keys.Up: sel.y -= step; break;
            case Keys.Down: sel.y += step; break;
            case Keys.Delete: sel.visible = false; break; // 要素は消さず「非表示」にする
            default: moved = false; break;
        }
        if (moved)
        {
            e.Handled = true;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
}
