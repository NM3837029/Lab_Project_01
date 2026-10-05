using System.Drawing.Drawing2D;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartTemplatePickerForm - 複合パーツの「型」を選んで、数値と画像を決めるダイアログ
//
// 左：型の一覧（「既存の敵から取り出した型」と「新しい型」に分かれる）
// 中：選んだ型の説明・画像・数値（型ごとに自動で入力欄が並ぶ）
// 右：作られるパーツのプレビュー（▶ 動かす で、スクリプトの動きも確かめられる）
//
// 2通りの使い方がある。
//   ・パーツ編集画面から開く（既定）  … 選んだ型のパーツ一式を、編集中の敵・ギミックへ追加する（または置き換える）
//   ・敵を型から新しく作る（createEnemyMode）… アセット管理から開き、パーツだけでなく、敵のタイプ・HP・大きさ・
//                                              本体の絵まで、型が勧める設定で新しい敵を1体作る
// ======================================================
public class PartTemplatePickerForm : Form
{
    // ── 結果 ──
    public List<PartDef> ResultParts { get; private set; } = new();
    public bool ReplaceExisting { get; private set; }
    public string ResultTemplateName { get; private set; } = "";
    // 追加したあとにユーザーへ伝えたいこと（おすすめの敵タイプなど）。空なら何も伝えない。
    public string ResultNote { get; private set; } = "";
    // 敵を新しく作るときに必要な情報
    public PartTemplate? SelectedTemplate { get; private set; }
    public EnemySuggestion? Suggestion { get; private set; }
    public string BodySpritePath { get; private set; } = "";
    public string PartSpritePath { get; private set; } = "";

    private readonly string _projectRoot;
    private readonly bool _createEnemyMode;
    private readonly int _existingCount;
    private readonly float _paramBodyW, _paramBodyH;

    private ListView _list = null!;
    private Label _lblName = null!, _lblDesc = null!, _lblOrigin = null!, _lblSuggest = null!, _lblPreviewNote = null!;
    private PictureBox _picPart = null!, _picBody = null!;
    private Label _lblPartPath = null!, _lblBodyPath = null!;
    private TextBox _txtPrefix = null!;
    private TableLayoutPanel _tblParams = null!;
    private CheckBox _chkReplace = null!;
    private Panel _pnlPreview = null!;
    private Button _btnPlay = null!, _btnOk = null!;
    private System.Windows.Forms.Timer _timer = null!;
    private bool _playing;
    private float _time;

    private PartTemplate? _tpl;
    private TemplateValues _values = new();
    private List<PartDef> _preview = new();
    private string _bodySprite = "", _partSprite = "";
    private readonly Dictionary<string, Image?> _imgCache = new();
    private bool _building;                 // 入力欄を作っている最中（変更イベントを無視する）
    private string? _buildError;

    // ── 初期化 ──
    //   baseSpritePath … 編集中の本体の絵（パーツ編集画面から開いたとき）。プレビューの本体と、パーツの既定の絵に使う。
    //   bodyW/bodyH    … 本体の大きさ（当たり判定の大きさ）。敵を新しく作るときは、型が勧める大きさで上書きされる。
    public PartTemplatePickerForm(string projectRoot, string baseSpritePath, float bodyW, float bodyH, int existingCount, bool createEnemyMode = false)
    {
        _projectRoot = projectRoot;
        _createEnemyMode = createEnemyMode;
        _existingCount = existingCount;
        _paramBodyW = bodyW > 0 ? bodyW : 32f;
        _paramBodyH = bodyH > 0 ? bodyH : 32f;
        _bodySprite = baseSpritePath ?? "";
        _partSprite = createEnemyMode ? "" : (baseSpritePath ?? "");

        Text = createEnemyMode ? "テンプレートから敵を作る" : "テンプレートから複合パーツを追加";
        Size = new Size(1180, 740);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;
        UiTheme.ApplyResizableChrome(this);

        BuildLayout();
        // 動きのプレビュー用タイマー（約60fps）。「▶ 動かす」を押している間だけ動く。
        _timer = new System.Windows.Forms.Timer { Interval = 16 };
        _timer.Tick += (s, e) => { _time += 1f; _pnlPreview.Invalidate(); };

        // 最初の型を選んでおく
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;
    }

    // ── 画面の組み立て ──
    private void BuildLayout()
    {
        // 下：OK/キャンセル
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 48 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        _btnOk = new Button { Text = _createEnemyMode ? "この型で敵を作る" : "この型で追加", AutoSize = true, Padding = new Padding(12, 5, 12, 5), BackColor = UiTheme.PrimaryButtonBack, ForeColor = UiTheme.PrimaryButtonFore, FlatStyle = FlatStyle.Flat };
        _btnOk.Click += (s, e) => Commit();
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(_btnOk);
        var lblHint = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), ForeColor = Color.DimGray,
            Text = _createEnemyMode
                ? "型を選び、画像と数値を決めると、タイプ・HP・大きさ・パーツ・スクリプトの入った新しい敵が1体できます。あとで自由に直せます。"
                : "型を選び、画像と数値を決めて「この型で追加」を押すと、パーツ一式が編集中のオブジェクトへ追加されます。追加後も、各パーツを自由に直せます。",
        };
        pnlBottom.Controls.Add(lblHint);
        pnlBottom.Controls.Add(flow);
        AcceptButton = _btnOk;
        CancelButton = btnCancel;

        var rootSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
        Controls.Add(rootSplit);
        Controls.Add(pnlBottom);
        rootSplit.Panel2.Controls.Add(rightSplit);

        // 左：型の一覧
        var pnlLeft = new Panel { Dock = DockStyle.Fill };
        var lblList = new Label { Dock = DockStyle.Top, Height = 26, Text = "📚 型の一覧", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 6, 0, 0) };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None, ShowGroups = true,
        };
        _list.Columns.Add("型", 260);
        var gExisting = new ListViewGroup(PartTemplates.CategoryExisting, HorizontalAlignment.Left);
        var gNew = new ListViewGroup(PartTemplates.CategoryNew, HorizontalAlignment.Left);
        _list.Groups.AddRange(new[] { gExisting, gNew });
        foreach (var t in PartTemplates.All)
            _list.Items.Add(new ListViewItem(t.Name) { Tag = t, Group = t.Category == PartTemplates.CategoryExisting ? gExisting : gNew });
        _list.SelectedIndexChanged += (s, e) => { if (_list.SelectedItems.Count > 0) SelectTemplate((PartTemplate)_list.SelectedItems[0].Tag); };
        pnlLeft.Controls.Add(_list);
        pnlLeft.Controls.Add(lblList);
        rootSplit.Panel1.Controls.Add(pnlLeft);

        // 中：説明と設定
        var pnlMid = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var flowMid = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(8, 6, 8, 12) };
        _lblName = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 4) };
        _lblDesc = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 4) };
        _lblOrigin = new Label { AutoSize = true, ForeColor = Color.FromArgb(40, 100, 160), MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 2) };
        _lblSuggest = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 90, 0), MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 6) };
        flowMid.Controls.AddRange(new Control[] { _lblName, _lblDesc, _lblOrigin, _lblSuggest });

        // 画像
        flowMid.Controls.Add(Heading("画像"));
        flowMid.Controls.Add(SpriteRow("パーツの画像", out _picPart, out _lblPartPath, () => PickSprite(isBody: false)));
        if (_createEnemyMode) flowMid.Controls.Add(SpriteRow("本体の画像", out _picBody, out _lblBodyPath, () => PickSprite(isBody: true)));
        else { _picBody = new PictureBox(); _lblBodyPath = new Label(); }

        // 数値
        flowMid.Controls.Add(Heading("数値"));
        _tblParams = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0) };
        _tblParams.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        _tblParams.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        flowMid.Controls.Add(_tblParams);

        // パーツのID接頭辞と、置き換え
        flowMid.Controls.Add(Heading("追加のしかた"));
        var rowPrefix = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        rowPrefix.Controls.Add(new Label { Text = "パーツIDの頭文字", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        _txtPrefix = new TextBox { Width = 120 };
        _txtPrefix.TextChanged += (s, e) => { if (_building) return; _values.IdPrefix = string.IsNullOrWhiteSpace(_txtPrefix.Text) ? "part" : _txtPrefix.Text.Trim(); RebuildPreview(); };
        rowPrefix.Controls.Add(_txtPrefix);
        flowMid.Controls.Add(rowPrefix);
        _chkReplace = new CheckBox { Text = "いまあるパーツを全部消して、置き換える", AutoSize = true, Visible = !_createEnemyMode && _existingCount > 0, Margin = new Padding(0, 6, 0, 0) };
        flowMid.Controls.Add(_chkReplace);

        pnlMid.Controls.Add(flowMid);
        rightSplit.Panel1.Controls.Add(pnlMid);

        // 右：プレビュー
        var pnlRight = new Panel { Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 3, 4, 1) };
        _btnPlay = new Button { Text = "▶ 動かす", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2) };
        _btnPlay.Click += (s, e) => TogglePlay();
        bar.Controls.Add(_btnPlay);
        _lblPreviewNote = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 7, 0, 0), MaximumSize = new Size(380, 0) };
        bar.Controls.Add(_lblPreviewNote);
        _pnlPreview = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(44, 46, 52) };
        _pnlPreview.Paint += PreviewPaint;
        _pnlPreview.Resize += (s, e) => _pnlPreview.Invalidate();
        pnlRight.Controls.Add(_pnlPreview);
        pnlRight.Controls.Add(bar);
        rightSplit.Panel2.Controls.Add(pnlRight);

        Shown += (s, e) =>
        {
            rootSplit.Panel1MinSize = 200;
            rootSplit.SplitterDistance = 270;
            rightSplit.SplitterDistance = Math.Max(380, (int)(rightSplit.Width * 0.48));
        };
    }

    private sealed class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

    private Label Heading(string text) => new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(40, 80, 140), Margin = new Padding(0, 10, 0, 3) };

    // 画像の選択欄（サムネイル・パス・ボタン）を1行作る
    private Control SpriteRow(string label, out PictureBox pic, out Label path, Action onPick)
    {
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Width = 90, Margin = new Padding(0, 16, 6, 0) });
        pic = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(240, 240, 240), Margin = new Padding(0, 0, 6, 0) };
        var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        path = new Label { Text = "(画像なし)", AutoSize = true, MaximumSize = new Size(220, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 2) };
        var btn = new Button { Text = "📁 選ぶ…", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 2, 4, 2), Margin = new Padding(0) };
        btn.Click += (s, e) => onPick();
        right.Controls.Add(path);
        right.Controls.Add(btn);
        row.Controls.Add(pic);
        row.Controls.Add(right);
        return row;
    }

    // ── 型の選択 ──
    private void SelectTemplate(PartTemplate t)
    {
        _tpl = t;
        // 敵を新しく作るときは、型が勧める本体の大きさでプレビューする
        var firstValues = t.NewValues(_paramBodyW, _paramBodyH, _partSprite);
        float bw = _paramBodyW, bh = _paramBodyH;
        if (_createEnemyMode) { var sg = t.Suggest(firstValues); bw = sg.Width; bh = sg.Height; }
        _values = t.NewValues(bw, bh, _partSprite);

        _building = true;
        _lblName.Text = t.Name;
        _lblDesc.Text = t.Description;
        _lblOrigin.Text = string.IsNullOrEmpty(t.Origin) ? "" : $"元になった敵：{t.Origin}";
        _lblOrigin.Visible = !string.IsNullOrEmpty(t.Origin);
        _lblPreviewNote.Text = t.PreviewNote;
        _txtPrefix.Text = t.DefaultIdPrefix;

        // 数値の入力欄を、型の定義から作り直す
        _tblParams.SuspendLayout();
        _tblParams.Controls.Clear();
        _tblParams.RowStyles.Clear();
        _tblParams.RowCount = 0;
        foreach (var p in t.Params)
        {
            int r = _tblParams.RowCount;
            _tblParams.RowCount = r + 1;
            _tblParams.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { Text = p.Label, AutoSize = true, MaximumSize = new Size(185, 0), TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 3, 3) };
            Control input;
            var key = p.Key;
            if (p.IsBool)
            {
                var chk = new CheckBox { Checked = p.Default != 0m, AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
                chk.CheckedChanged += (s, e) => { if (_building) return; _values.V[key] = chk.Checked ? 1m : 0m; RebuildPreview(); };
                input = chk;
            }
            else
            {
                var nud = new NumericUpDown { Minimum = p.Min, Maximum = p.Max, DecimalPlaces = p.Decimals, Increment = p.Step, Value = Math.Clamp(p.Default, p.Min, p.Max), Width = 90, Margin = new Padding(3, 3, 3, 3) };
                nud.ValueChanged += (s, e) => { if (_building) return; _values.V[key] = nud.Value; RebuildPreview(); };
                input = nud;
            }
            _tblParams.Controls.Add(lbl, 0, r);
            _tblParams.Controls.Add(input, 1, r);
            if (!string.IsNullOrEmpty(p.Hint))
            {
                int r2 = _tblParams.RowCount;
                _tblParams.RowCount = r2 + 1;
                _tblParams.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var hint = new Label { Text = p.Hint, AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7.5f), MaximumSize = new Size(260, 0), Margin = new Padding(3, 0, 0, 2) };
                _tblParams.Controls.Add(hint, 1, r2);
            }
        }
        _tblParams.ResumeLayout();
        UpdateSpriteViews();
        _building = false;

        UpdateSuggestionLabel();
        _time = 0f;
        RebuildPreview();
    }

    // 敵を作るモードでは、勧めるタイプなどを説明する。パーツ追加モードでも、組み合わせるとよい敵タイプを伝える。
    private void UpdateSuggestionLabel()
    {
        if (_tpl == null) return;
        var sg = _tpl.Suggest(_values);
        string note = string.IsNullOrEmpty(sg.TypeNote) ? "" : $"おすすめの敵タイプ：{(sg.TypeEnum >= 0 ? $"{sg.TypeEnum}　" : "")}{sg.TypeNote}";
        _lblSuggest.Text = note;
        _lblSuggest.Visible = note.Length > 0;
    }

    private void RebuildPreview()
    {
        if (_tpl == null) return;
        try { _preview = _tpl.Build(_values); _buildError = null; }
        catch (Exception ex) { _preview = new(); _buildError = ex.Message; }
        UpdateSuggestionLabel();
        _pnlPreview.Invalidate();
    }

    // ── 画像 ──
    private void PickSprite(bool isBody)
    {
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = isBody ? "本体の画像を選択" : "パーツの画像を選択" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        // プロジェクト外のファイルを直接参照すると、あとで場所を移されたときに壊れるため、必ず img/ へコピーしてから使う。
        string rel = ImageImportHelper.CopyIntoImgFolder(_projectRoot, ofd.FileName);
        if (isBody) _bodySprite = rel;
        else { _partSprite = rel; _values.Sprite = rel; }
        UpdateSpriteViews();
        RebuildPreview();
    }

    private void UpdateSpriteViews()
    {
        _picPart.Image = LoadImage(_partSprite);
        _lblPartPath.Text = string.IsNullOrEmpty(_partSprite) ? "(画像なし：オレンジの印で表示)" : _partSprite;
        if (_createEnemyMode)
        {
            _picBody.Image = LoadImage(_bodySprite);
            _lblBodyPath.Text = string.IsNullOrEmpty(_bodySprite) ? "(画像なし)" : _bodySprite;
        }
    }

    private Image? LoadImage(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return null;
        if (_imgCache.TryGetValue(rel, out var c)) return c;
        Image? img = null;
        string full = Path.Combine(_projectRoot, rel.Replace('/', '\\'));
        if (File.Exists(full))
        {
            try { using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read); img = Image.FromStream(fs); }
            catch { img = null; }
        }
        _imgCache[rel] = img;
        return img;
    }

    // ── プレビュー ──
    private void TogglePlay()
    {
        _playing = !_playing;
        _btnPlay.Text = _playing ? "⏸ 止める" : "▶ 動かす";
        if (_playing) _timer.Start(); else _timer.Stop();
        _pnlPreview.Invalidate();
    }

    private static readonly Color[] Palette =
    {
        Color.FromArgb(200, 255, 150, 60), Color.FromArgb(200, 90, 190, 255), Color.FromArgb(200, 120, 220, 120),
        Color.FromArgb(200, 240, 120, 200), Color.FromArgb(200, 230, 220, 90), Color.FromArgb(200, 170, 140, 255),
    };

    private void PreviewPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        int cw = _pnlPreview.Width, ch = _pnlPreview.Height;
        if (_buildError != null)
        {
            g.DrawString("この数値では作れません: " + _buildError, Font, Brushes.OrangeRed, 10, 10);
            return;
        }
        float bw = _values.BodyW, bh = _values.BodyH;

        // 表示範囲：本体と、全パーツの位置（初期位置と、再生中の動きの両方）が収まるようにする
        float minX = 0, minY = 0, maxX = bw, maxY = bh;
        for (int i = 0; i < _preview.Count; i++)
        {
            var p = _preview[i];
            float w = (p.width > 0 ? p.width : 24) * p.scale, h = (p.height > 0 ? p.height : 24) * p.scale;
            void Take(float x, float y) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x + w); maxY = Math.Max(maxY, y + h); }
            Take(p.offsetX, p.offsetY);
            // 動く型は、いくつかの時刻の姿勢も範囲に含める（再生を始めても画面から出ないように）
            foreach (float t in new[] { 0f, 40f, 80f, 120f, 160f, 200f, 240f })
            {
                var pose = ScriptPreviewEvaluator.Evaluate(p.script, t, i);
                if (pose.HasOffset) Take(pose.OffsetX, pose.OffsetY);
            }
        }
        float ww = Math.Max(maxX - minX, 8f), hh = Math.Max(maxY - minY, 8f);
        const float margin = 28f;
        float zoom = Math.Clamp(Math.Min((cw - margin * 2) / ww, (ch - margin * 2) / hh), 0.5f, 12f);
        float panX = cw / 2f - (minX + ww / 2f) * zoom, panY = ch / 2f - (minY + hh / 2f) * zoom;
        PointF W(float x, float y) => new(panX + x * zoom, panY + y * zoom);

        // グリッド（32px）
        using (var grid = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
        {
            for (float x = MathF.Floor(minX / 32f - 1) * 32f; x <= maxX + 32; x += 32) { var s = W(x, 0); g.DrawLine(grid, s.X, 0, s.X, ch); }
            for (float y = MathF.Floor(minY / 32f - 1) * 32f; y <= maxY + 32; y += 32) { var s = W(0, y); g.DrawLine(grid, 0, s.Y, cw, s.Y); }
        }

        // 本体（点線の枠と、薄い絵）
        var tl = W(0, 0); var br = W(bw, bh);
        var bodyRect = new RectangleF(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        var bodyImg = LoadImage(_bodySprite);
        if (bodyImg != null)
        {
            using var attr = new System.Drawing.Imaging.ImageAttributes();
            attr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f });
            g.DrawImage(bodyImg, Rectangle.Round(bodyRect), 0, 0, bodyImg.Width, bodyImg.Height, GraphicsUnit.Pixel, attr);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(40, 130, 190, 255));
            g.FillRectangle(fill, bodyRect);
        }
        using (var bp = new Pen(Color.FromArgb(180, 130, 190, 255), 1f) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(bp, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);
        using var small = new Font(Font.FontFamily, 7.5f);
        g.DrawString($"本体 {bw:0.#}×{bh:0.#}", small, Brushes.LightSkyBlue, bodyRect.X, bodyRect.Bottom + 2);

        // パーツ（zOrderの小さい順）
        var order = Enumerable.Range(0, _preview.Count).OrderBy(i => _preview[i].zOrder).ToList();
        foreach (int i in order)
        {
            var p = _preview[i];
            float ox = p.offsetX, oy = p.offsetY, ang = 0f;
            if (_playing)
            {
                var pose = ScriptPreviewEvaluator.Evaluate(p.script, _time, i);
                if (pose.HasOffset) { ox = pose.OffsetX; oy = pose.OffsetY; }
                if (pose.HasAngle) ang = pose.Angle;
            }
            float w = (p.width > 0 ? p.width : 24) * p.scale, h = (p.height > 0 ? p.height : 24) * p.scale;
            var s0 = W(ox, oy);
            var rect = new RectangleF(s0.X, s0.Y, Math.Max(w * zoom, 4f), Math.Max(h * zoom, 4f));
            var st = ang != 0f ? g.Save() : null;
            if (ang != 0f)
            {
                float px = rect.X + rect.Width / 2f, py = rect.Y + rect.Height / 2f;
                g.TranslateTransform(px, py); g.RotateTransform(ang * 180f / MathF.PI); g.TranslateTransform(-px, -py);
            }
            var img = LoadImage(p.sprite);
            if (img != null) g.DrawImage(img, rect);
            else { using var b = new SolidBrush(Palette[i % Palette.Length]); g.FillRectangle(b, rect); }
            if (st != null) g.Restore(st);
            // 枠：壊せるパーツは緑、触れるとダメージは赤、そのほかは白
            Color frame = p.hp > 0 ? Color.LimeGreen : (p.deadly ? Color.FromArgb(230, 255, 90, 90) : Color.FromArgb(150, 255, 255, 255));
            using var pen = new Pen(frame, 1f);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }
        // 凡例
        g.DrawString($"パーツ {_preview.Count}個　（緑の枠＝弾で壊せる／赤の枠＝触れるとダメージ）", small, Brushes.Silver, 8, ch - 18);
    }

    // ── 確定 ──
    private void Commit()
    {
        if (_tpl == null) return;
        try
        {
            ResultParts = _tpl.Build(_values);
        }
        catch (Exception ex)
        {
            MessageBox.Show("この数値ではパーツを作れませんでした。\n" + ex.Message, "作成できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SelectedTemplate = _tpl;
        Suggestion = _tpl.Suggest(_values);
        ResultTemplateName = _tpl.Name;
        ReplaceExisting = _chkReplace.Visible && _chkReplace.Checked;
        BodySpritePath = _bodySprite;
        PartSpritePath = _partSprite;
        ResultNote = string.IsNullOrEmpty(Suggestion.TypeNote) ? "" : $"（おすすめの敵タイプ：{Suggestion.TypeNote}）";
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer?.Stop();
        _timer?.Dispose();
        foreach (var img in _imgCache.Values) img?.Dispose();
        base.OnFormClosed(e);
    }
}
