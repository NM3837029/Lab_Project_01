using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// EditorKit - 各エディタ画面で使い回す「画面の部品」
//
// 背景設定・サウンド管理・サウンド割り当て・アニメーション・タイル・アセットワークベンチの
// 6画面はどれも、もとは「表（DataGridView）＋保存して閉じるボタン」だった。
// これを「左に画像つきの一覧、右に日本語の項目名のついた詳細フォーム」へ作り替えるため、
// 共通で要る部品（見出し・入力欄・スライダー・色・画像表示・画面の枠）をここへまとめる。
// ======================================================
public static class Kit
{
    // ボタンを作る。primary=主要な操作（青）、danger=消す操作（赤い文字）。
    public static Button Btn(string text, Action onClick, bool primary = false, bool danger = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(0, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            Font = primary ? UiTheme.Bold : UiTheme.Base,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? UiTheme.Accent : UiTheme.CardBorder;
        b.BackColor = primary ? UiTheme.Accent : UiTheme.CardBack;
        b.ForeColor = primary ? Color.White : danger ? Color.Firebrick : UiTheme.TextMain;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 100, 180) : UiTheme.CardHover;
        b.Click += (s, e) => onClick();
        return b;
    }

    // 区切りの見出し（太字＋その下に細い線）。
    public static Control Heading(string text)
    {
        var p = new Panel { Height = 30, Margin = new Padding(0, 10, 0, 2) };
        var l = new Label { Text = text, Dock = DockStyle.Fill, Font = UiTheme.Bold, ForeColor = UiTheme.TextMain, TextAlign = ContentAlignment.BottomLeft };
        var line = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.CardBorder };
        p.Controls.Add(l);
        p.Controls.Add(line);
        return p;
    }

    // 補足の説明文（灰色の小さな字、折り返しあり）。幅は FieldStack が合わせる。
    public static Label Hint(string text) => new()
    {
        Text = text, AutoSize = false, Height = 20, ForeColor = UiTheme.TextMuted, Font = UiTheme.Small,
        Margin = new Padding(0, 0, 0, 4), Tag = "hint",
    };

    // 「項目名（上）＋入力欄（下）＋補足（任意）」を1つにまとめる。
    // width を指定すると、その幅に固定する（2つ並べたいとき）。指定しなければ、並べた領域の幅いっぱいに広がる。
    public static Control Field(string label, Control input, string? hint = null, int width = 0)
        => new FieldBox(label, input, hint, width);

    // 数値入力欄を作る。
    public static NumericUpDown Num(decimal min, decimal max, int decimals = 0, decimal step = 1, int width = 100) => new()
    {
        Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step, Width = width, Font = UiTheme.Base,
        ThousandsSeparator = false,
    };

    // 値が範囲内なら代入、範囲外なら端へ丸めて代入する（NumericUpDown は範囲外の代入で例外になるため）。
    public static void SetNum(NumericUpDown n, decimal v) => n.Value = Math.Clamp(v, n.Minimum, n.Maximum);

    // 画像ファイルを読み込む。ゲーム本体が同じ絵を読めなくならないよう、メモリへ読み切ってからファイルを離す。
    public static Bitmap? LoadBitmap(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return null;
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var src = Image.FromStream(fs);
            return new Bitmap(src);
        }
        catch { return null; }
    }

    // 色の文字列（#RRGGBB）を Color に。読めなければ null。
    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return null;
        try { return ColorTranslator.FromHtml(hex); } catch { return null; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // 種類の色の丸の上に絵文字を1つ描いた小さな絵（絵ファイルを持たない物の、一覧の目印用）。
    public static Bitmap Emoji(string emoji, Color tint, int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using (var br = new SolidBrush(Color.FromArgb(40, tint))) g.FillEllipse(br, 1, 1, size - 2, size - 2);
        using var f = new Font("Segoe UI Emoji", Math.Max(6f, size * 0.42f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(emoji, f, Brushes.Black, new RectangleF(0, 0, size, size + 1), sf);
        return bmp;
    }

    // WAVファイルの長さ（秒）。読めなければ null。ヘッダーの「1秒あたりのバイト数」と「データ量」から求める。
    public static double? WavSeconds(string path)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return null;
            using var br = new BinaryReader(File.OpenRead(path));
            if (new string(br.ReadChars(4)) != "RIFF") return null;
            br.ReadInt32();
            if (new string(br.ReadChars(4)) != "WAVE") return null;
            int byteRate = 0;
            while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
            {
                string id = new string(br.ReadChars(4));
                int size = br.ReadInt32();
                if (id == "fmt ") { br.ReadInt16(); br.ReadInt16(); br.ReadInt32(); byteRate = br.ReadInt32(); br.BaseStream.Seek(size - 12, SeekOrigin.Current); }
                else if (id == "data") return byteRate > 0 ? (double)size / byteRate : null;
                else br.BaseStream.Seek(size, SeekOrigin.Current);
            }
        }
        catch { }
        return null;
    }

    // 画像を、縦横比を保って size×size の中央へ収めた小さな絵（一覧のサムネイル用）。
    public static Bitmap Thumb(Image? src, int size, Color? fallback = null)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        if (src == null)
        {
            if (fallback is Color c)
            {
                using var br = new SolidBrush(c);
                g.FillRectangle(br, 1, 1, size - 2, size - 2);
            }
            return bmp;
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        float k = Math.Min((size - 2f) / src.Width, (size - 2f) / src.Height);
        float w = src.Width * k, h = src.Height * k;
        g.DrawImage(src, (size - w) / 2f, (size - h) / 2f, w, h);
        return bmp;
    }
}

// ───────────────────────────────────────────────
// FieldBox - 「項目名＋入力欄＋補足」を縦に並べた1まとまり
// 自動の大きさ調整（AutoSize）は「中身の最小幅」に縮んでしまい、領域の幅に広がってくれないので、
// 幅が決まるたびに自前で上から並べ直して、高さを求める。
// ───────────────────────────────────────────────
public sealed class FieldBox : Panel
{
    private readonly Label _label;
    private readonly Control _input;
    private readonly Label? _hint;
    private readonly bool _stretch;
    public int FixedWidth { get; }

    public FieldBox(string label, Control input, string? hint, int fixedWidth)
    {
        FixedWidth = fixedWidth;
        Margin = new Padding(0, 4, 0, 4);
        _input = input;
        // 数値欄・ボタン類は、幅いっぱいに引き伸ばさず、もとの幅で置く。
        _stretch = input is not NumericUpDown && input is not Button && input is not CheckBox;
        _label = new Label { Text = label, AutoSize = false, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted };
        Controls.Add(_label);
        Controls.Add(input);
        if (hint != null)
        {
            _hint = new Label { Text = hint, AutoSize = false, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted };
            Controls.Add(_hint);
        }
        if (fixedWidth > 0) Width = fixedWidth;
        Relayout();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Relayout();
    }

    private bool _busy;
    private void Relayout()
    {
        if (_busy || Width < 20) return;
        _busy = true;
        int w = Width, y = 0;
        var lf = TextRenderer.MeasureText(_label.Text, _label.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        _label.SetBounds(0, y, w, lf.Height + 2);
        y += _label.Height + 2;
        int ih = _input.Height;
        _input.SetBounds(0, y, _stretch ? w : Math.Min(_input.Width, w), ih);
        y += ih;
        if (_hint != null)
        {
            var hf = TextRenderer.MeasureText(_hint.Text, _hint.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            _hint.SetBounds(0, y + 3, w, hf.Height + 2);
            y += 3 + _hint.Height;
        }
        if (Height != y) Height = y;
        _busy = false;
    }
}

// ───────────────────────────────────────────────
// FieldStack - 縦に項目を積むスクロール領域
// 追加した項目の幅を、領域の幅（スクロールバーを除く）に自動で合わせる。
// ───────────────────────────────────────────────
public sealed class FieldStack : FlowLayoutPanel
{
    public FieldStack()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoScroll = true;
        Padding = new Padding(14, 6, 14, 14);
        BackColor = UiTheme.CardBack;
        Font = UiTheme.Base;
    }

    public void AddItem(Control c) { Controls.Add(c); FitWidth(c); }
    public void ClearItems() { foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); } }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        foreach (Control c in Controls) FitWidth(c);
        base.OnLayout(levent);
    }

    private void FitWidth(Control c)
    {
        int w = Math.Max(120, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
        if (c is Label { Tag: "hint" } l)
        {
            // 補足文は幅に合わせて折り返し、必要な高さを測って決める。
            var size = TextRenderer.MeasureText(l.Text, l.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            l.Width = w; l.Height = size.Height + 4;
        }
        else if (c is FieldBox { FixedWidth: > 0 }) { }
        else c.Width = w;
    }

    // 内容を差し替えたあと、いちばん上から見せる（直前の項目へ勝手にスクロールしてしまうのを防ぐ）。
    public void ScrollToTop() { AutoScrollPosition = new Point(0, 0); }
}

// ───────────────────────────────────────────────
// SliderField - つまみと数値入力を連動させた入力欄
// つまみを動かすと数値が、数値を打つとつまみが追従する。左右に意味の説明を添えられる。
// ───────────────────────────────────────────────
public sealed class SliderField : UserControl
{
    private readonly TrackBar _track = new() { TickStyle = TickStyle.None, AutoSize = false, Height = 28 };
    private readonly NumericUpDown _num;
    private readonly double _min, _max, _step;
    private bool _sync;

    public event EventHandler? ValueChanged;

    public double Value
    {
        get => (double)_num.Value;
        set
        {
            _sync = true;
            _num.Value = (decimal)Math.Clamp(value, _min, _max);
            _track.Value = Math.Clamp((int)Math.Round((double)_num.Value / _step), _track.Minimum, _track.Maximum);
            _sync = false;
        }
    }

    public SliderField(double min, double max, double step, int decimals, string leftCaption = "", string rightCaption = "")
    {
        _min = min; _max = max; _step = step;
        Height = 62;
        Font = UiTheme.Base;
        _track.Minimum = (int)Math.Round(min / step);
        _track.Maximum = (int)Math.Round(max / step);
        _track.Dock = DockStyle.None;
        _num = Kit.Num((decimal)min, (decimal)max, decimals, (decimal)step, 74);

        var caps = new Panel { Dock = DockStyle.Bottom, Height = 18 };
        caps.Controls.Add(new Label { Text = leftCaption, AutoSize = true, Dock = DockStyle.Left, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted });
        caps.Controls.Add(new Label { Text = rightCaption, AutoSize = true, Dock = DockStyle.Right, Font = UiTheme.Small, ForeColor = UiTheme.TextMuted });

        var row = new Panel { Dock = DockStyle.Top, Height = 32 };
        _num.Dock = DockStyle.Right;
        _track.Dock = DockStyle.Fill;
        row.Controls.Add(_track);
        row.Controls.Add(_num);

        Controls.Add(caps);
        Controls.Add(row);

        _track.ValueChanged += (s, e) =>
        {
            if (_sync) return;
            _sync = true; _num.Value = (decimal)Math.Clamp(_track.Value * _step, _min, _max); _sync = false;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        _num.ValueChanged += (s, e) =>
        {
            if (_sync) return;
            _sync = true; _track.Value = Math.Clamp((int)Math.Round((double)_num.Value / _step), _track.Minimum, _track.Maximum); _sync = false;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
    }
}

// ───────────────────────────────────────────────
// ColorField - 色の見本・#RRGGBB・色選択ボタン・解除ボタン
// ───────────────────────────────────────────────
public sealed class ColorField : UserControl
{
    private readonly Panel _swatch = new() { Width = 36, Dock = DockStyle.Left, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand };
    private readonly TextBox _txt = new() { Width = 84, Font = UiTheme.Base, MaxLength = 7 };
    private bool _sync;

    public event EventHandler? ValueChanged;

    // "#RRGGBB"。色なしは空文字。
    public string Value
    {
        get => _txt.Text.Trim();
        set { _sync = true; _txt.Text = value ?? ""; Refresh2(); _sync = false; }
    }

    public ColorField()
    {
        Height = 38;
        var btnPick = Kit.Btn("🎨 選ぶ", Pick);
        var btnClear = Kit.Btn("✕ なし", () => { Value = ""; ValueChanged?.Invoke(this, EventArgs.Empty); });
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(6, 0, 0, 0) };
        _txt.Margin = new Padding(0, 3, 6, 0);
        btnPick.Margin = new Padding(0, 0, 6, 0);
        flow.Controls.Add(_txt); flow.Controls.Add(btnPick); flow.Controls.Add(btnClear);
        Controls.Add(flow);
        Controls.Add(_swatch);
        _swatch.Click += (s, e) => Pick();
        _txt.TextChanged += (s, e) => { Refresh2(); if (!_sync) ValueChanged?.Invoke(this, EventArgs.Empty); };
    }

    private void Refresh2()
    {
        var c = Kit.ParseHex(_txt.Text.Trim());
        _swatch.BackColor = c ?? Color.White;
        _swatch.BackgroundImage = null;
    }

    private void Pick()
    {
        using var dlg = new ColorDialog { FullOpen = true };
        var cur = Kit.ParseHex(_txt.Text.Trim());
        if (cur is Color c) dlg.Color = c;
        if (dlg.ShowDialog() == DialogResult.OK) { _txt.Text = Kit.ToHex(dlg.Color); }
    }
}

// ───────────────────────────────────────────────
// ImageViewport - 画像を市松模様の上へ、縦横比を保って大きく出す表示欄
// ドット絵がにじまないよう最近傍補間で拡大する。重ね描き（枠・格子など）を差し込める。
// ───────────────────────────────────────────────
public sealed class ImageViewport : Control
{
    private Image? _image;
    public Image? Image { get => _image; set { _image = value; Invalidate(); } }
    public string Placeholder { get; set; } = "画像なし";
    // 画像を描いたあとに重ねて描く処理。引数は（描画先, 画像が描かれた矩形, 画像1pxあたりの画面px）。
    public Action<Graphics, RectangleF, float>? Overlay { get; set; }
    // 画像が描かれた矩形（Overlay・クリック位置の換算に使う）。
    public RectangleF ImageRect { get; private set; }
    public float PixelScale { get; private set; } = 1f;

    public ImageViewport()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 160;
        BackColor = Color.FromArgb(52, 56, 62);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_image == null)
        {
            TextRenderer.DrawText(g, Placeholder, UiTheme.Base, ClientRectangle, Color.FromArgb(170, 176, 184),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            ImageRect = RectangleF.Empty;
            return;
        }
        float m = 8;
        float k = Math.Min((Width - m * 2) / _image.Width, (Height - m * 2) / _image.Height);
        if (k <= 0) return;
        // 整数倍にできるときは整数倍にして、ドットの太さをそろえる。
        if (k >= 1) k = (float)Math.Floor(k);
        float w = _image.Width * k, h = _image.Height * k;
        var r = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
        ImageRect = r; PixelScale = k;
        // 市松模様（透明部分が分かるように）
        using (var light = new SolidBrush(Color.FromArgb(78, 82, 90)))
        using (var dark = new SolidBrush(Color.FromArgb(64, 68, 75)))
        {
            g.FillRectangle(dark, r);
            int cell = 8;
            for (float y = r.Top, yi = 0; y < r.Bottom; y += cell, yi++)
                for (float x = r.Left, xi = 0; x < r.Right; x += cell, xi++)
                    if (((int)xi + (int)yi) % 2 == 0)
                        g.FillRectangle(light, x, y, Math.Min(cell, r.Right - x), Math.Min(cell, r.Bottom - y));
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_image, r);
        Overlay?.Invoke(g, r, k);
    }
}

// ───────────────────────────────────────────────
// EditorFormBase - 6つのエディタ画面に共通の「枠」
//
//   ┌ 見出し（画面名・ひとこと説明）            [元に戻す][やり直す] ┐
//   │ 本体（Body）                                                    │
//   └ [左の操作ボタン…]                      [キャンセル][保存して閉じる] ┘
//
// ・キャンセル時、変更があれば「破棄しますか？」と確認する（うっかり閉じて失う事故を防ぐ）
// ・Ctrl+Z / Ctrl+Y は、子クラスが InitUndo で渡した処理に流す
// 保存の中身（結果を各 Result プロパティへ入れて DialogResult.OK で閉じる）は子クラスが OnSave で行う。
// ───────────────────────────────────────────────
public abstract class EditorFormBase : Form
{
    protected readonly Panel Body = new() { Dock = DockStyle.Fill, BackColor = UiTheme.PanelBack };
    protected readonly FlowLayoutPanel LeftBar = new() { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Padding = new Padding(14, 12, 0, 8), BackColor = UiTheme.CardBack };
    private readonly Label _title = new() { Font = new Font("Meiryo UI", 13f, FontStyle.Bold), AutoSize = true, ForeColor = UiTheme.TextMain };
    private readonly Label _sub = new() { Font = UiTheme.Small, AutoSize = true, ForeColor = UiTheme.TextMuted };
    private Button? _btnUndo, _btnRedo;
    private Action? _undo, _redo;
    private readonly Panel _head = new() { Dock = DockStyle.Top, Height = 58, BackColor = UiTheme.CardBack };
    private readonly Panel _foot = new() { Dock = DockStyle.Bottom, Height = 56, BackColor = UiTheme.CardBack };
    private readonly FlowLayoutPanel _headRight = new() { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 14, 14, 0), BackColor = UiTheme.CardBack };
    private bool _dirty;

    // 子クラスが「何か変えた」と知らせる（キャンセル確認に使う）。
    protected void MarkDirty() => _dirty = true;
    protected bool IsDirty => _dirty;

    protected EditorFormBase(string title, string subtitle, Size size, Size minSize)
    {
        Text = title;
        Font = UiTheme.Base;
        StartPosition = FormStartPosition.CenterParent;
        Size = size;
        MinimumSize = minSize;
        BackColor = UiTheme.PanelBack;

        _title.Text = title; _sub.Text = subtitle;
        _title.Location = new Point(16, 8);
        _sub.Location = new Point(18, 36);
        _head.Controls.Add(_title);
        _head.Controls.Add(_sub);
        _head.Controls.Add(_headRight);
        var headLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.CardBorder };
        _head.Controls.Add(headLine);

        var btnSave = Kit.Btn("💾  保存して閉じる", () => OnSave(), primary: true);
        btnSave.Padding = new Padding(12, 6, 12, 6);
        btnSave.Margin = new Padding(0, 0, 0, 0);
        var btnCancel = Kit.Btn("キャンセル", Cancel);
        btnCancel.Padding = new Padding(12, 6, 12, 6);
        btnCancel.Margin = new Padding(0, 0, 10, 0);
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 12, 14, 8), BackColor = UiTheme.CardBack };
        right.Controls.Add(btnCancel);
        right.Controls.Add(btnSave);
        var footLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.CardBorder };
        _foot.Controls.Add(right);
        _foot.Controls.Add(LeftBar);
        _foot.Controls.Add(footLine);

        // Dock の決まり：Fill を先に、あとから足したものが外側。
        Controls.Add(Body);
        Controls.Add(_foot);
        Controls.Add(_head);
    }

    // Undo/Redo の配線。見出しの右にボタンを出す。
    protected void InitUndo(Action undo, Action redo)
    {
        _undo = undo; _redo = redo;
        _btnUndo = Kit.Btn("↩ 元に戻す", undo); _btnUndo.Enabled = false;
        _btnRedo = Kit.Btn("↪ やり直す", redo); _btnRedo.Enabled = false;
        _headRight.Controls.Add(_btnUndo);
        _headRight.Controls.Add(_btnRedo);
    }

    protected void UpdateUndoState(bool canUndo, bool canRedo)
    {
        if (_btnUndo != null) _btnUndo.Enabled = canUndo;
        if (_btnRedo != null) _btnRedo.Enabled = canRedo;
    }

    // 保存して閉じる（子クラスが実装する）。
    protected abstract void OnSave();

    private void Cancel()
    {
        if (_dirty && MessageBox.Show("変更したところがあります。保存せずに閉じますか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    // ×ボタンで閉じられたときも、変更があれば確認する。
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || DialogResult == DialogResult.OK || DialogResult == DialogResult.Cancel || !_dirty) return;
        if (e.CloseReason != CloseReason.UserClosing) return;
        if (MessageBox.Show("変更したところがあります。保存せずに閉じますか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 文字入力中の Ctrl+Z は入力欄自身の取り消しに任せる。
        bool typing = ActiveControl is TextBoxBase;
        if (!typing && _undo != null && keyData == (Keys.Control | Keys.Z)) { _undo(); return true; }
        if (!typing && _redo != null && keyData == (Keys.Control | Keys.Y)) { _redo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // 左（一覧）｜右（詳細）の分割を、画面が出たあとで安全に配置する。
    protected static void SplitWhenReady(SplitContainer sc, int leftMin, int rightMin, int leftWidth)
    {
        void Apply() => SplitLayout.Apply(sc, leftMin, rightMin, leftWidth);
        sc.HandleCreated += (s, e) => Apply();
        sc.SizeChanged += (s, e) => { if (sc.Panel1.Width < leftMin || sc.Panel1MinSize == 0) Apply(); };
    }
}
