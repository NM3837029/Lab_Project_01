using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Lab_Editor;

// 「ゲーム設定」の「UIデザイン」タブ。
//
// ゲーム画面に出るウィンドウ（左上のコイン・ゲージの枠、メッセージ、ボタン、円形メニュー、編集パネル、
// ポーズメニュー、ステージセレクトのマス）の見た目を、コードを書き換えずに変えるための画面。
//
// 【なぜ必要か】
// これらの枠は img/UIウィンドウ.png の固定の絵と、C++に直書きされた数値・色で描かれていて、
// 見た目を変える手段がエディタに無かった。game_config.json の ui_style / theme へ出し、
// ここで編集できるようにした（C++側の GameConfig.h の UiStyle と対応する）。
//
// 【構成】
//   左 … 実際のゲーム画面と同じ並びで、枠のサンプルを描くプレビュー。クリックすると、その枠の「枠ごとの設定」へ飛ぶ
//   右 … 枠の素材と切り方／文字・ゲージの色／メッセージの置き方／枠ごとの設定（素材・色合わせ・不透明度・太さ）
//
// プレビューは C++側の DrawUiWindow（9スライス・色合わせ・不透明度）と同じ式で描くので、
// ここで見えている形がゲームでもそのまま出る。
public class UiDesignPanel : UserControl
{
    private readonly GameConfig _cfg;
    private readonly string _projectRoot;
    private readonly PreviewCanvas _preview;
    private readonly Action? _onChanged;

    private TextBox _txtFrame = null!;
    private NumericUpDown _numSrcPad = null!, _numSrcSlice = null!, _numDestSlice = null!;
    private NumericUpDown _numMsgX = null!, _numMsgB = null!, _numMsgH = null!;
    private ComboBox _cboKind = null!;
    private TextBox _txtKindImage = null!;
    private Button _btnKindTint = null!;
    private NumericUpDown _numKindOpacity = null!, _numKindSlice = null!;
    private readonly List<Action> _refreshers = new(); // 色ボタンの色を値へ合わせ直す処理
    private bool _suppress;

    // テーマ色の変更をタイトル画面のキャンバスなど他のタブへ伝えたいときに呼ぶ
    public UiDesignPanel(GameConfig cfg, string projectRoot, Action? onChanged = null)
    {
        _cfg = cfg;
        _projectRoot = projectRoot;
        _onChanged = onChanged;
        Font = UiTheme.Base;
        Dock = DockStyle.Fill;

        _preview = new PreviewCanvas(cfg, projectRoot) { Dock = DockStyle.Fill };
        _preview.KindPicked += kind => { _cboKind.SelectedIndex = kind; };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
        };
        split.Panel1.Controls.Add(_preview);
        split.Panel2.Controls.Add(BuildSide());
        Controls.Add(split);

        // SplitterDistance は実サイズを持ってからでないと効かない（他のタブと同じ理由）
        HandleCreated += (s, e) =>
        {
            try
            {
                if (split.Width > 700)
                {
                    split.Panel2MinSize = 380;
                    int want = split.Width - 420;
                    if (want > split.Panel1MinSize && want < split.Width - split.Panel2MinSize) split.SplitterDistance = want;
                }
            }
            catch { /* 狭いウィンドウでは既定の分割位置のままで支障はない */ }
        };

        LoadValues();
    }

    // ================= 右側の操作欄 =================

    private Control BuildSide()
    {
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        int y = 8;
        const int W = 380;

        // ---- 枠の素材 ----
        p.Controls.Add(UiTheme.CreateLabel("全体の枠の素材（9スライス）", new Point(10, y), true)); y += 22;
        _txtFrame = new TextBox { Location = new Point(10, y), Width = 210, ReadOnly = true };
        var btnPick = UiTheme.CreateButton("参照", new Point(226, y - 1), new Size(56, 24));
        btnPick.Click += (s, e) => PickImage(rel => { _cfg.ui_style.frame_image = rel; _txtFrame.Text = rel; });
        var btnNone = UiTheme.CreateButton("素材なし", new Point(286, y - 1), new Size(84, 24));
        btnNone.Click += (s, e) => { _cfg.ui_style.frame_image = ""; _txtFrame.Text = ""; Changed(true); };
        p.Controls.Add(_txtFrame); p.Controls.Add(btnPick); p.Controls.Add(btnNone); y += 30;

        var btnDefImg = UiTheme.CreateButton("既定の素材に戻す", new Point(10, y), new Size(130, 24));
        btnDefImg.Click += (s, e) => { _cfg.ui_style.frame_image = "img/UIウィンドウ.png"; _txtFrame.Text = _cfg.ui_style.frame_image; Changed(true); };
        p.Controls.Add(btnDefImg); y += 32;

        _numSrcPad = AddNum(p, "素材の余白を切り落とす(px)", ref y, 0, 400, v => _cfg.ui_style.src_pad = v);
        _numSrcSlice = AddNum(p, "素材の縁の太さ(px)", ref y, 1, 400, v => _cfg.ui_style.src_slice = v);
        _numDestSlice = AddNum(p, "画面上の縁の太さ(px)", ref y, 2, 100, v => _cfg.ui_style.dest_slice = v);
        var note = UiTheme.CreateLabel("四隅は伸ばさず、辺と中央だけを伸ばして描きます。\n余白・縁の太さは素材の絵に合わせて調整してください。", new Point(10, y));
        note.ForeColor = Color.DimGray; note.Font = UiTheme.Small;
        p.Controls.Add(note); y += 38;

        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), W)); y += 12;

        // ---- 素材が無いときの単色枠 ----
        p.Controls.Add(UiTheme.CreateLabel("素材なしのときの単色の枠", new Point(10, y), true)); y += 22;
        AddColor(p, "塗り", ref y, () => _cfg.ui_style.fallback_fill, v => _cfg.ui_style.fallback_fill = v);
        AddColor(p, "縁", ref y, () => _cfg.ui_style.fallback_edge, v => _cfg.ui_style.fallback_edge = v);
        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), W)); y += 12;

        // ---- 文字の色 ----
        p.Controls.Add(UiTheme.CreateLabel("文字の色", new Point(10, y), true)); y += 22;
        AddColor(p, "標準", ref y, () => _cfg.theme.ink, v => _cfg.theme.ink = v, themeChanged: true);
        AddColor(p, "うすい（補助・無効）", ref y, () => _cfg.theme.ink_sub, v => _cfg.theme.ink_sub = v, themeChanged: true);
        AddColor(p, "強調", ref y, () => _cfg.theme.ink_accent, v => _cfg.theme.ink_accent = v, themeChanged: true);
        AddColor(p, "警告・削除", ref y, () => _cfg.ui_style.ink_warn, v => _cfg.ui_style.ink_warn = v);
        AddColor(p, "正常・有効", ref y, () => _cfg.ui_style.ink_ok, v => _cfg.ui_style.ink_ok = v);
        var note2 = UiTheme.CreateLabel("標準・うすい・強調は、タイトル／セレクト画面の文字色と共通です。", new Point(10, y));
        note2.ForeColor = Color.DimGray; note2.Font = UiTheme.Small;
        p.Controls.Add(note2); y += 24;
        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), W)); y += 12;

        // ---- ゲージの色 ----
        p.Controls.Add(UiTheme.CreateLabel("編集コストゲージの色", new Point(10, y), true)); y += 22;
        AddColor(p, "中身", ref y, () => _cfg.ui_style.gauge_fill, v => _cfg.ui_style.gauge_fill = v);
        AddColor(p, "残りわずか（点滅）", ref y, () => _cfg.ui_style.gauge_low, v => _cfg.ui_style.gauge_low = v);
        AddColor(p, "器（背景）", ref y, () => _cfg.ui_style.gauge_back, v => _cfg.ui_style.gauge_back = v);
        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), W)); y += 12;

        // ---- メッセージの置き方 ----
        p.Controls.Add(UiTheme.CreateLabel("メッセージウィンドウの置き方", new Point(10, y), true)); y += 22;
        _numMsgX = AddNum(p, "左右の余白(px)", ref y, 0, 300, v => _cfg.ui_style.message.margin_x = v);
        _numMsgB = AddNum(p, "下の余白(px)", ref y, 0, 400, v => _cfg.ui_style.message.margin_bottom = v);
        _numMsgH = AddNum(p, "高さ（1行のとき）(px)", ref y, 40, 400, v => _cfg.ui_style.message.height = v);
        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), W)); y += 12;

        // ---- 枠ごとの設定 ----
        p.Controls.Add(UiTheme.CreateLabel("枠ごとの設定（左のプレビューをクリックしても選べます）", new Point(10, y), true)); y += 24;
        _cboKind = new ComboBox { Location = new Point(10, y), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboKind.Items.AddRange(UiWindowsConfig.Labels);
        _cboKind.SelectedIndexChanged += (s, e) => { LoadKind(); _preview.Selected = _cboKind.SelectedIndex; };
        p.Controls.Add(_cboKind); y += 32;

        p.Controls.Add(UiTheme.CreateLabel("素材（空＝全体と同じ）", new Point(10, y + 4)));
        y += 24;
        _txtKindImage = new TextBox { Location = new Point(10, y), Width = 210, ReadOnly = true };
        var btnKPick = UiTheme.CreateButton("参照", new Point(226, y - 1), new Size(56, 24));
        btnKPick.Click += (s, e) => PickImage(rel => { Kind().image = rel; _txtKindImage.Text = rel; });
        var btnKClear = UiTheme.CreateButton("全体と同じ", new Point(286, y - 1), new Size(84, 24));
        btnKClear.Click += (s, e) => { Kind().image = ""; _txtKindImage.Text = ""; Changed(true); };
        p.Controls.Add(_txtKindImage); p.Controls.Add(btnKPick); p.Controls.Add(btnKClear); y += 32;

        p.Controls.Add(UiTheme.CreateLabel("色合わせ（乗算）", new Point(10, y + 4)));
        _btnKindTint = new Button { Location = new Point(190, y), Size = new Size(60, 24), FlatStyle = FlatStyle.Flat };
        _btnKindTint.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = _btnKindTint.BackColor, FullOpen = true };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            Kind().tint = new[] { (int)dlg.Color.R, (int)dlg.Color.G, (int)dlg.Color.B };
            _btnKindTint.BackColor = dlg.Color;
            Changed();
        };
        var btnWhite = UiTheme.CreateButton("白に戻す", new Point(256, y - 1), new Size(70, 24));
        btnWhite.Click += (s, e) => { Kind().tint = new[] { 255, 255, 255 }; _btnKindTint.BackColor = Color.White; Changed(); };
        p.Controls.Add(_btnKindTint); p.Controls.Add(btnWhite); y += 30;

        p.Controls.Add(UiTheme.CreateLabel("不透明度(%)", new Point(10, y + 4)));
        _numKindOpacity = UiTheme.CreateNumericUpDown(new Point(190, y), 80, 0, 100, 0);
        _numKindOpacity.ValueChanged += (s, e) => { if (!_suppress) { Kind().opacity = (int)_numKindOpacity.Value; Changed(); } };
        p.Controls.Add(_numKindOpacity); y += 30;

        p.Controls.Add(UiTheme.CreateLabel("縁の太さ(px)（-1＝全体と同じ）", new Point(10, y + 4)));
        _numKindSlice = UiTheme.CreateNumericUpDown(new Point(250, y), 80, -1, 100, 0);
        _numKindSlice.ValueChanged += (s, e) => { if (!_suppress) { Kind().dest_slice = (int)_numKindSlice.Value; Changed(); } };
        p.Controls.Add(_numKindSlice); y += 34;

        var btnKReset = UiTheme.CreateButton("この枠の設定を全体に合わせる", new Point(10, y), new Size(200, 26));
        btnKReset.Click += (s, e) =>
        {
            var k = Kind();
            k.image = ""; k.tint = new[] { 255, 255, 255 }; k.opacity = 100; k.dest_slice = -1;
            LoadKind(); Changed(true);
        };
        p.Controls.Add(btnKReset); y += 40;

        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), W)); y += 12;
        var btnAllDef = UiTheme.CreateButton("UIデザインをすべて既定に戻す", new Point(10, y), new Size(220, 28));
        btnAllDef.Click += (s, e) =>
        {
            if (MessageBox.Show("ウィンドウの素材・色・置き方を、すべて初期の見た目へ戻します。\n（文字色のテーマ色も初期値に戻ります）\nよろしいですか？",
                                "UIデザインを既定に戻す", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var def = new UiStyleConfig();
            var keep = _cfg.ui_style._extra;
            _cfg.ui_style = def; def._extra = keep;
            var th = new ThemeColors();
            _cfg.theme.ink = th.ink; _cfg.theme.ink_sub = th.ink_sub; _cfg.theme.ink_accent = th.ink_accent;
            LoadValues(); Changed(true, themeChanged: true);
        };
        p.Controls.Add(btnAllDef); y += 40;

        return p;
    }

    private NumericUpDown AddNum(Panel p, string label, ref int y, int min, int max, Action<int> set)
    {
        p.Controls.Add(UiTheme.CreateLabel(label, new Point(10, y + 4)));
        var n = UiTheme.CreateNumericUpDown(new Point(250, y), 80, min, max, 0);
        n.ValueChanged += (s, e) => { if (!_suppress) { set((int)n.Value); Changed(); } };
        p.Controls.Add(n);
        y += 28;
        return n;
    }

    private void AddColor(Panel p, string label, ref int y, Func<int[]> get, Action<int[]> set, bool themeChanged = false)
    {
        p.Controls.Add(UiTheme.CreateLabel(label, new Point(10, y + 4)));
        var btn = new Button { Location = new Point(250, y), Size = new Size(60, 24), FlatStyle = FlatStyle.Flat };
        btn.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = btn.BackColor, FullOpen = true };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            set(new[] { (int)dlg.Color.R, (int)dlg.Color.G, (int)dlg.Color.B });
            btn.BackColor = dlg.Color;
            Changed(false, themeChanged);
        };
        _refreshers.Add(() => btn.BackColor = ToColor(get()));
        p.Controls.Add(btn);
        y += 28;
    }

    // ================= 値の出し入れ =================

    private UiWindowStyleConfig Kind() => _cfg.ui_style.windows.Get(Math.Max(0, _cboKind.SelectedIndex));

    private void LoadValues()
    {
        _suppress = true;
        var u = _cfg.ui_style;
        _txtFrame.Text = u.frame_image;
        _numSrcPad.Value = Clamp(u.src_pad, _numSrcPad);
        _numSrcSlice.Value = Clamp(u.src_slice, _numSrcSlice);
        _numDestSlice.Value = Clamp(u.dest_slice, _numDestSlice);
        _numMsgX.Value = Clamp(u.message.margin_x, _numMsgX);
        _numMsgB.Value = Clamp(u.message.margin_bottom, _numMsgB);
        _numMsgH.Value = Clamp(u.message.height, _numMsgH);
        foreach (var r in _refreshers) r();
        if (_cboKind.SelectedIndex < 0) _cboKind.SelectedIndex = 0;
        _suppress = false;
        LoadKind();
        _preview.Invalidate();
    }

    private void LoadKind()
    {
        _suppress = true;
        var k = Kind();
        _txtKindImage.Text = k.image;
        _btnKindTint.BackColor = ToColor(k.tint);
        _numKindOpacity.Value = Clamp(k.opacity, _numKindOpacity);
        _numKindSlice.Value = Clamp(k.dest_slice, _numKindSlice);
        _suppress = false;
    }

    private static decimal Clamp(int v, NumericUpDown n) => Math.Max(n.Minimum, Math.Min(n.Maximum, v));

    // 値が変わったとき。clearImages … 素材の指定が変わったのでプレビューの画像キャッシュを捨てる
    private void Changed(bool clearImages = false, bool themeChanged = false)
    {
        if (clearImages) _preview.ClearImageCache();
        _preview.Invalidate();
        if (themeChanged) _onChanged?.Invoke();
    }

    private void PickImage(Action<string> onPicked)
    {
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "ウィンドウ枠の画像を選択" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        string rel = ImageImportHelper.CopyIntoImgFolder(_projectRoot, ofd.FileName);
        onPicked(rel);
        Changed(true);
    }

    private static Color ToColor(int[]? rgb) =>
        rgb != null && rgb.Length >= 3
            ? Color.FromArgb(Math.Clamp(rgb[0], 0, 255), Math.Clamp(rgb[1], 0, 255), Math.Clamp(rgb[2], 0, 255))
            : Color.Black;

    // ================= プレビュー =================

    // ゲーム画面（640x480）と同じ並びで、枠のサンプルを描く。描き方は C++の DrawUiWindow と同じ。
    private sealed class PreviewCanvas : Panel
    {
        private readonly GameConfig _cfg;
        private readonly string _root;
        private readonly Dictionary<string, Bitmap?> _images = new();
        private readonly List<(Rectangle rect, int kind)> _hits = new(); // 描いた順（あとのものが手前）

        public int Selected { get => _selected; set { _selected = value; Invalidate(); } }
        private int _selected;
        public event Action<int>? KindPicked;

        public PreviewCanvas(GameConfig cfg, string root)
        {
            _cfg = cfg;
            _root = root;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(40, 44, 60);
        }

        public void ClearImageCache()
        {
            foreach (var b in _images.Values) b?.Dispose();
            _images.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearImageCache();
            base.Dispose(disposing);
        }

        private Bitmap? LoadImage(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            if (_images.TryGetValue(rel, out var cached)) return cached;
            Bitmap? bmp = null;
            try
            {
                string path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    // ファイルを掴みっぱなしにしないよう、読み込んだらコピーして元は閉じる
                    using var tmp = Image.FromFile(path);
                    bmp = new Bitmap(tmp);
                }
            }
            catch { bmp = null; }
            _images[rel] = bmp;
            return bmp;
        }

        private float _scale = 1f;
        private PointF _origin;

        private PointF ToDesign(Point p) => new((p.X - _origin.X) / _scale, (p.Y - _origin.Y) / _scale);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var d = ToDesign(e.Location);
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].rect.Contains((int)d.X, (int)d.Y)) { KindPicked?.Invoke(_hits[i].kind); return; }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            _scale = Math.Min(ClientSize.Width / 640f, ClientSize.Height / 480f);
            if (_scale <= 0.05f) return;
            _origin = new PointF((ClientSize.Width - 640 * _scale) / 2f, (ClientSize.Height - 480 * _scale) / 2f);
            g.TranslateTransform(_origin.X, _origin.Y);
            g.ScaleTransform(_scale, _scale);
            _hits.Clear();

            // ゲーム画面の代わりの背景（暗い空と地面）。枠の色合わせ・不透明度が分かるよう、絵のある背景にしてある
            using (var sky = new LinearGradientBrush(new Rectangle(0, 0, 640, 480), Color.FromArgb(34, 38, 70), Color.FromArgb(70, 60, 96), 90f))
                g.FillRectangle(sky, 0, 0, 640, 480);
            using (var ground = new SolidBrush(Color.FromArgb(150, 120, 110)))
                g.FillRectangle(ground, 0, 330, 640, 150);
            using (var star = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                for (int i = 0; i < 24; i++) g.FillRectangle(star, (i * 97) % 640, 20 + (i * 53) % 250, 2, 2);
            g.SetClip(new Rectangle(0, 0, 640, 480));

            using var font = new Font("Meiryo UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var fontBig = new Font("Meiryo UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var fontSmall = new Font("Meiryo UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
            var t = _cfg.theme;
            var u = _cfg.ui_style;
            Color ink = ToColor(t.ink), sub = ToColor(t.ink_sub), accent = ToColor(t.ink_accent);
            Color warn = ToColor(u.ink_warn), ok = ToColor(u.ink_ok);

            void Text(string s, float x, float y, Color c, Font? f = null)
            {
                using var br = new SolidBrush(c);
                g.DrawString(s, f ?? font, br, x, y, StringFormat.GenericTypographic);
            }
            void Hit(Rectangle r, int kind) => _hits.Add((r, kind));

            // 1) コイン・ゲージの枠（kind 0）
            var osd = new Rectangle(8, 8, 188, 76);
            DrawWindow(g, osd, 0); Hit(osd, 0);
            Text("0 / 16", 56, 18, ink, fontBig);
            Text("コイン", 14, 18, sub);
            var gaugeBack = new Rectangle(54, 50, 132, 20);
            using (var br = new SolidBrush(ToColor(u.gauge_back))) g.FillRectangle(br, gaugeBack);
            using (var br = new SolidBrush(ToColor(u.gauge_fill))) g.FillRectangle(br, 54, 50, (int)(132 * 0.7), 20);
            using (var pn = new Pen(accent)) g.DrawRectangle(pn, gaugeBack);
            Text("112 / 160", 60, 52, Color.White);

            // 2) 編集パネル（kind 4）
            var panel = new Rectangle(8, 96, 150, 150);
            DrawWindow(g, panel, 4); Hit(panel, 4);
            Text("STAGE EDITOR", 22, 108, accent);
            Text("PAUSED", 22, 130, warn);
            Text("Rewind [R]", 22, 158, ink); Text("ON", 118, 158, ok);
            Text("Pause [SP]", 22, 178, ink); Text("ON", 118, 178, ok);
            Text("Cut [Ctrl]", 22, 198, sub); Text("--", 118, 198, sub);

            // 3) ポーズメニュー（kind 5）と、中のボタン（kind 2）
            var menu = new Rectangle(176, 96, 252, 224);
            DrawWindow(g, menu, 5); Hit(menu, 5);
            Text("ポーズ", 202, 110, accent);
            string[] labels = { "つづける", "もういちど", "ゲームをおわる" };
            for (int i = 0; i < 3; i++)
            {
                var b = new Rectangle(206, 138 + i * 52, 192, 40);
                DrawWindow(g, b, 2, i == 0 ? 1f : 0.84f); Hit(b, 2);
                var sz = g.MeasureString(labels[i], font, 400, StringFormat.GenericTypographic);
                Text(labels[i], b.X + (b.Width - sz.Width) / 2, b.Y + 11, i == 0 ? accent : ink);
            }

            // 4) ステージセレクトのマス（kind 6）
            var cell = new Rectangle(444, 96, 180, 108);
            DrawWindow(g, cell, 6); Hit(cell, 6);
            Text("STAGE 1", 462, 112, ink, fontBig);
            Text("CLEAR  3 / 7", 462, 142, accent);
            Text("ステージの名前", 462, 168, sub);

            // 5) 円形メニューの項目（kind 3）。選択中・通常・押せない、の3状態
            (string label, string subText, float bright, Color c)[] radial =
            {
                ("巻き戻し", "OFF -3", 1f, accent),
                ("一時停止", "OFF -3", 0.84f, ink),
                ("リセット", "-10",    0.59f, sub),
            };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rectangle(458, 222 + i * 52, 104, 42);
                DrawWindow(g, r, 3, radial[i].bright); Hit(r, 3);
                Text(radial[i].label, r.X + 14, r.Y + 3, radial[i].c);
                Text(radial[i].subText, r.X + 14, r.Y + 24, sub, fontSmall);
            }

            // 6) メッセージウィンドウ（kind 1）。置き方は設定の余白・高さに従う
            int mx = u.message.margin_x, mb = u.message.margin_bottom, mh = Math.Max(40, u.message.height);
            var msg = new Rectangle(mx, 480 - mb - mh, 640 - mx * 2, mh);
            DrawWindow(g, msg, 1); Hit(msg, 1);
            Text("ナビ", msg.X + 18, msg.Y + 14, accent);
            Text("Rキーで時間を巻き戻せます。試してみましょう。", msg.X + 18, msg.Y + 38, ink);
            Text("[ENTER] to close", msg.Right - 130, msg.Bottom - 26, sub);

            // 選択中の枠を点線で囲む（どの枠の設定を編集しているかが分かるように）
            g.ResetClip();
            using var sel = new Pen(Color.FromArgb(255, 220, 90), 2f) { DashStyle = DashStyle.Dash };
            foreach (var h in _hits.Where(h => h.kind == _selected))
            {
                var r = h.rect; r.Inflate(3, 3);
                g.DrawRectangle(sel, r);
            }
        }

        // C++側 DrawUiWindow と同じ式の9スライス描画。bright は呼び出し側の明るさ（ホバーで暗くする等）
        private void DrawWindow(Graphics g, Rectangle r, int kind, float bright = 1f)
        {
            var u = _cfg.ui_style;
            var ws = u.windows.Get(kind);
            string path = !string.IsNullOrEmpty(ws.image) ? ws.image : u.frame_image;
            var img = LoadImage(path);

            float tr = ws.tint[0] / 255f * bright, tg = ws.tint[1] / 255f * bright, tb = ws.tint[2] / 255f * bright;
            float op = Math.Clamp(ws.opacity, 0, 100) / 100f;

            if (img == null)
            {
                // 素材なし（または読めない）→ 単色の枠
                Color f = ToColor(u.fallback_fill), ed = ToColor(u.fallback_edge);
                int a = (int)(op * 255);
                using (var br = new SolidBrush(Color.FromArgb(a, (int)(f.R * tr), (int)(f.G * tg), (int)(f.B * tb)))) g.FillRectangle(br, r);
                using (var pn = new Pen(Color.FromArgb(a, (int)(ed.R * tr), (int)(ed.G * tg), (int)(ed.B * tb)))) g.DrawRectangle(pn, r);
                return;
            }

            var cm = new ColorMatrix(new[]
            {
                new[] { Math.Min(tr, 1f), 0f, 0f, 0f, 0f },
                new[] { 0f, Math.Min(tg, 1f), 0f, 0f, 0f },
                new[] { 0f, 0f, Math.Min(tb, 1f), 0f, 0f },
                new[] { 0f, 0f, 0f, op, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f },
            });
            using var ia = new ImageAttributes();
            ia.SetColorMatrix(cm);
            ia.SetWrapMode(WrapMode.TileFlipXY); // 切り出した境界で隣の絵が混ざらないようにする

            int iw = img.Width, ih = img.Height;
            int ds = ws.dest_slice >= 0 ? ws.dest_slice : u.dest_slice;
            int limit = Math.Min(r.Width, r.Height) / 3;
            if (ds > limit) ds = limit;
            if (ds < 2) ds = 2;

            int pad = u.src_pad;
            if (pad * 2 >= iw - 2) pad = (iw - 2) / 2;
            if (pad * 2 >= ih - 2) pad = (ih - 2) / 2;
            if (pad < 0) pad = 0;
            int spanW = iw - pad * 2, spanH = ih - pad * 2;
            int ss = u.src_slice;
            if (ss > spanW / 2 - 1) ss = spanW / 2 - 1;
            if (ss > spanH / 2 - 1) ss = spanH / 2 - 1;
            if (ss < 1) ss = 1;
            int smW = spanW - ss * 2, smH = spanH - ss * 2;
            int sx0 = pad, sx1 = sx0 + ss, sx2 = sx1 + smW;
            int sy0 = pad, sy1 = sy0 + ss, sy2 = sy1 + smH;
            int x1 = r.X, y1 = r.Y, x2 = r.Right, y2 = r.Bottom;
            int dx1 = x1 + ds, dx2 = x2 - ds, dy1 = y1 + ds, dy2 = y2 - ds;

            void Part(int dxa, int dya, int dxb, int dyb, int sx, int sy, int sw, int sh)
            {
                if (dxb <= dxa || dyb <= dya || sw <= 0 || sh <= 0) return;
                g.DrawImage(img, new Rectangle(dxa, dya, dxb - dxa, dyb - dya), sx, sy, sw, sh, GraphicsUnit.Pixel, ia);
            }
            // 四隅 → 上下の辺 → 左右の辺 → 中央（C++側と同じ順）
            Part(x1, y1, dx1, dy1, sx0, sy0, ss, ss);
            Part(dx2, y1, x2, dy1, sx2, sy0, ss, ss);
            Part(x1, dy2, dx1, y2, sx0, sy2, ss, ss);
            Part(dx2, dy2, x2, y2, sx2, sy2, ss, ss);
            Part(dx1, y1, dx2, dy1, sx1, sy0, smW, ss);
            Part(dx1, dy2, dx2, y2, sx1, sy2, smW, ss);
            Part(x1, dy1, dx1, dy2, sx0, sy1, ss, smH);
            Part(dx2, dy1, x2, dy2, sx2, sy1, ss, smH);
            Part(dx1, dy1, dx2, dy2, sx1, sy1, smW, smH);
        }

        private static Color ToColor(int[]? rgb) =>
            rgb != null && rgb.Length >= 3
                ? Color.FromArgb(Math.Clamp(rgb[0], 0, 255), Math.Clamp(rgb[1], 0, 255), Math.Clamp(rgb[2], 0, 255))
                : Color.Black;
    }
}
