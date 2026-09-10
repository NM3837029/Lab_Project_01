using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ゲーム全体の設定（assets/game_config.json）を編集するページ。
//
// タブ3枚の構成:
//   1. タイトル画面 … 配置キャンバス＋選択中の要素のプロパティ
//   2. ステージ一覧 … 表示名・並び順・サムネイル・解放条件
//   3. その他       … ウィンドウタイトル / テーマ色 / リザルト文言 / セーブデータ削除
//
// 他のページと同じく Saved / Cancelled と Primary/SecondaryActionButton を公開し、
// 薄いラッパー(GameConfigForm)からも WorkbenchShell からも同じように使える形にしてある。
public class GameConfigPageControl : UserControl
{
    public event EventHandler? Saved;
    public event EventHandler? Cancelled;
    public Button PrimaryActionButton => _btnSave;
    public Button SecondaryActionButton => _btnCancel;

    private readonly string _projectRoot;
    private readonly string _assetsPath;
    private readonly string _stagesPath;
    private readonly AssetDefinitions _assets;
    private GameConfig _cfg;

    private Button _btnSave = null!, _btnCancel = null!;
    private TitleLayoutCanvas _canvas = null!;
    private SplitContainer _split = null!;
    private DataGridView _dgvStages = null!, _dgvMenu = null!;

    // 要素プロパティ欄
    private ComboBox _cboElement = null!;
    private CheckBox _chkVisible = null!;
    private TextBox _txtText = null!, _txtImage = null!;
    private NumericUpDown _numX = null!, _numY = null!, _numW = null!, _numH = null!;
    private NumericUpDown _numFont = null!, _numItemH = null!, _numGap = null!;
    private ComboBox _cboRole = null!;
    private CheckBox _chkEdge = null!;
    private TextBox _txtTitleBg = null!;
    private ComboBox _cboTitleBgm = null!;

    // その他タブ
    private TextBox _txtWindowTitle = null!;
    private CheckBox _chkTitleEnabled = null!;
    private TextBox _txtNext = null!, _txtRetry = null!, _txtSelect = null!, _txtVictory = null!, _txtGameover = null!;
    private TextBox _txtHeading = null!, _txtBack = null!, _txtLocked = null!, _txtSelectBg = null!;
    private readonly Dictionary<string, Button> _colorButtons = new();

    // プロパティ欄からキャンバスへ書き戻すときの再入防止。
    // キャンバス→欄→キャンバス…と往復して無限ループになるのを止める。
    private bool _suppress;

    public GameConfigPageControl(string projectRoot, string assetsPath, string stagesPath,
                                 AssetDefinitions assets, GameConfig cfg)
    {
        _projectRoot = projectRoot;
        _assetsPath = assetsPath;
        _stagesPath = stagesPath;
        _assets = assets;
        _cfg = cfg;

        // 要素が1つも無い設定（手書きで作られた等）でも編集できるよう、既定の4要素を補う
        EnsureElements();

        BuildUi();
        LoadIntoUi();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // SplitterDistance はコントロールが実サイズを持ってからでないと効かない
        // （コンストラクタ内で指定すると内部で丸められ、プロパティ欄が潰れる）。
        // Panel2MinSize も同じ理由でここで設定する。SplitContainer の既定幅は150pxしかなく、
        // コンストラクタ時点で270を指定すると「SplitterDistance が範囲外」で例外になる。
        try
        {
            if (_split.Width > 420)
            {
                _split.Panel2MinSize = 270;
                int want = _split.Width - 300;
                if (want > _split.Panel1MinSize && want < _split.Width - _split.Panel2MinSize)
                    _split.SplitterDistance = want;
            }
        }
        catch { /* 極端に狭いウィンドウでは設定できないが、既定の分割位置のままで支障はない */ }
    }

    // key が欠けている要素を既定値で補う。
    // C++側は key で引くだけなので、無い要素は「描かれない」ではなく「編集できない」問題になる。
    private void EnsureElements()
    {
        var def = GameConfig.CreateDefault().title_screen.elements;
        foreach (var d in def)
        {
            if (!_cfg.title_screen.elements.Any(e => e.key == d.key))
                _cfg.title_screen.elements.Add(d);
        }
    }

    // ================= UI 構築 =================

    private void BuildUi()
    {
        Font = UiTheme.Base;

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = UiTheme.Base };
        tabs.TabPages.Add(BuildTitleTab());
        tabs.TabPages.Add(BuildStagesTab());
        tabs.TabPages.Add(BuildMiscTab());

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = UiTheme.PanelBackLight };
        _btnSave = new Button { Text = "保存", Size = new Size(110, 30), Location = new Point(0, 8), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _btnCancel = new Button { Text = "キャンセル", Size = new Size(110, 30), Location = new Point(0, 8), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        UiTheme.StylePrimaryButton(_btnSave);
        UiTheme.StyleSecondaryButton(_btnCancel);
        _btnSave.Click += (s, e) => DoSave();
        _btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);
        bottom.Controls.Add(_btnSave);
        bottom.Controls.Add(_btnCancel);
        bottom.Resize += (s, e) =>
        {
            _btnSave.Left = bottom.ClientSize.Width - 240;
            _btnCancel.Left = bottom.ClientSize.Width - 120;
        };

        // 【重要】Dock=Fill の子を先に、Dock=Bottom を後から Add する。
        // 逆にすると Fill が Bottom の領域まで食ってボタンが隠れる（このコードベース共通の規約）。
        Controls.Add(tabs);
        Controls.Add(bottom);
    }

    private TabPage BuildTitleTab()
    {
        var page = new TabPage("タイトル画面");

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            // ウィンドウを広げてもプロパティ欄の幅は変えず、キャンバス側だけを広げる
            FixedPanel = FixedPanel.Panel2,
        };
        var split = _split;

        _canvas = new TitleLayoutCanvas { Dock = DockStyle.Fill };
        _canvas.SetConfig(_cfg, _projectRoot);
        _canvas.SelectionChanged += (s, e) => { SyncElementPanel(); };
        _canvas.LayoutChanged += (s, e) => { SyncElementPanel(); };
        split.Panel1.Controls.Add(_canvas);

        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        int y = 6;

        right.Controls.Add(UiTheme.CreateLabel("編集する要素", new Point(8, y), true)); y += 22;
        _cboElement = new ComboBox { Location = new Point(8, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboElement.Items.AddRange(new object[] { "ロゴ画像 (logo)", "タイトル文字 (title)", "サブタイトル (subtitle)", "メニュー (menu)" });
        _cboElement.SelectedIndexChanged += (s, e) =>
        {
            string[] keys = { "logo", "title", "subtitle", "menu" };
            if (_cboElement.SelectedIndex >= 0) _canvas.SelectKey(keys[_cboElement.SelectedIndex]);
        };
        right.Controls.Add(_cboElement); y += 30;

        _chkVisible = new CheckBox { Text = "この要素を表示する", Location = new Point(8, y), Width = 240 };
        _chkVisible.CheckedChanged += (s, e) => ApplyFromPanel(el => el.visible = _chkVisible.Checked);
        right.Controls.Add(_chkVisible); y += 26;

        right.Controls.Add(UiTheme.CreateLabel("文字", new Point(8, y))); y += 18;
        _txtText = new TextBox { Location = new Point(8, y), Width = 240 };
        _txtText.TextChanged += (s, e) => ApplyFromPanel(el => el.text = _txtText.Text);
        right.Controls.Add(_txtText); y += 28;

        right.Controls.Add(UiTheme.CreateLabel("画像 (ロゴ用)", new Point(8, y))); y += 18;
        _txtImage = new TextBox { Location = new Point(8, y), Width = 168, ReadOnly = true };
        var btnImg = UiTheme.CreateButton("参照", new Point(180, y - 1), new Size(68, 24));
        btnImg.Click += (s, e) => PickImageInto(_txtImage, rel => ApplyFromPanel(el => el.image = rel));
        right.Controls.Add(_txtImage); right.Controls.Add(btnImg); y += 30;

        right.Controls.Add(UiTheme.CreateLabel("位置・大きさ (画面は640x480)", new Point(8, y), true)); y += 20;
        right.Controls.Add(UiTheme.CreateLabel("X", new Point(8, y + 4)));
        _numX = MakeNum(new Point(28, y), 0, 640, v => ApplyFromPanel(el => el.x = v));
        right.Controls.Add(_numX);
        right.Controls.Add(UiTheme.CreateLabel("Y", new Point(128, y + 4)));
        _numY = MakeNum(new Point(148, y), 0, 480, v => ApplyFromPanel(el => el.y = v));
        right.Controls.Add(_numY); y += 28;

        right.Controls.Add(UiTheme.CreateLabel("幅", new Point(8, y + 4)));
        _numW = MakeNum(new Point(28, y), 0, 640, v => ApplyFromPanel(el => el.w = v));
        right.Controls.Add(_numW);
        right.Controls.Add(UiTheme.CreateLabel("高", new Point(128, y + 4)));
        _numH = MakeNum(new Point(148, y), 0, 480, v => ApplyFromPanel(el => el.h = v));
        right.Controls.Add(_numH); y += 30;

        right.Controls.Add(UiTheme.CreateLabel("文字サイズ", new Point(8, y + 4)));
        _numFont = MakeNum(new Point(88, y), 6, 120, v => ApplyFromPanel(el => el.font_size = (int)v));
        right.Controls.Add(_numFont); y += 28;

        right.Controls.Add(UiTheme.CreateLabel("文字色", new Point(8, y + 4)));
        _cboRole = new ComboBox { Location = new Point(88, y), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboRole.Items.AddRange(new object[] { "標準 (ink)", "うすい (sub)", "強調 (accent)" });
        _cboRole.SelectedIndexChanged += (s, e) =>
        {
            string[] roles = { "ink", "sub", "accent" };
            if (_cboRole.SelectedIndex >= 0) ApplyFromPanel(el => el.color_role = roles[_cboRole.SelectedIndex]);
        };
        right.Controls.Add(_cboRole); y += 28;

        _chkEdge = new CheckBox { Text = "縁取りを付ける（背景の上でも読める）", Location = new Point(8, y), Width = 250 };
        _chkEdge.CheckedChanged += (s, e) => ApplyFromPanel(el => el.edge = _chkEdge.Checked);
        right.Controls.Add(_chkEdge); y += 30;

        right.Controls.Add(UiTheme.CreateLabel("メニュー専用", new Point(8, y), true)); y += 20;
        right.Controls.Add(UiTheme.CreateLabel("項目の高さ", new Point(8, y + 4)));
        _numItemH = MakeNum(new Point(88, y), 10, 200, v => ApplyFromPanel(el => el.item_h = v));
        right.Controls.Add(_numItemH);
        right.Controls.Add(UiTheme.CreateLabel("すき間", new Point(168, y + 4)));
        _numGap = MakeNum(new Point(218, y), 0, 100, v => ApplyFromPanel(el => el.gap = v));
        right.Controls.Add(_numGap); y += 32;

        right.Controls.Add(UiTheme.CreateSeparator(new Point(8, y), 250)); y += 12;

        right.Controls.Add(UiTheme.CreateLabel("メニュー項目", new Point(8, y), true)); y += 20;
        _dgvMenu = new DataGridView
        {
            Location = new Point(8, y), Size = new Size(250, 116),
            AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter, Font = UiTheme.Base,
        };
        _dgvMenu.Columns.Add(new DataGridViewTextBoxColumn { Name = "label", HeaderText = "文言", FillWeight = 120 });
        var colAct = new DataGridViewComboBoxColumn { Name = "action", HeaderText = "動作", FillWeight = 110 };
        colAct.Items.AddRange(new object[] { "ステージ選択へ", "つづきから", "終了する" });
        _dgvMenu.Columns.Add(colAct);
        _dgvMenu.CellValueChanged += (s, e) => { if (!_suppress) { ReadMenuGrid(); _canvas.Invalidate(); } };
        _dgvMenu.CurrentCellDirtyStateChanged += (s, e) =>
        { if (_dgvMenu.IsCurrentCellDirty) _dgvMenu.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _dgvMenu.UserDeletedRow += (s, e) => { ReadMenuGrid(); _canvas.Invalidate(); };
        right.Controls.Add(_dgvMenu); y += 126;

        right.Controls.Add(UiTheme.CreateSeparator(new Point(8, y), 250)); y += 12;

        right.Controls.Add(UiTheme.CreateLabel("背景画像", new Point(8, y))); y += 18;
        _txtTitleBg = new TextBox { Location = new Point(8, y), Width = 168, ReadOnly = true };
        var btnBg = UiTheme.CreateButton("参照", new Point(180, y - 1), new Size(68, 24));
        btnBg.Click += (s, e) => PickImageInto(_txtTitleBg, rel =>
        { _cfg.title_screen.background_image = rel; _canvas.InvalidateImageCache(); });
        right.Controls.Add(_txtTitleBg); right.Controls.Add(btnBg); y += 30;

        right.Controls.Add(UiTheme.CreateLabel("BGM", new Point(8, y))); y += 18;
        _cboTitleBgm = new ComboBox { Location = new Point(8, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        FillBgmCombo(_cboTitleBgm);
        _cboTitleBgm.SelectedIndexChanged += (s, e) =>
        { if (!_suppress) _cfg.title_screen.bgm_id = BgmIdFromCombo(_cboTitleBgm); };
        right.Controls.Add(_cboTitleBgm);

        split.Panel2.Controls.Add(right);
        page.Controls.Add(split);
        return page;
    }

    private TabPage BuildStagesTab()
    {
        var page = new TabPage("ステージ一覧");

        _dgvStages = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EditMode = DataGridViewEditMode.EditOnEnter, Font = UiTheme.Base,
        };

        var colFile = new DataGridViewComboBoxColumn { Name = "file", HeaderText = "ステージファイル", FillWeight = 130 };
        foreach (var f in ListUserStages()) colFile.Items.Add(f);
        _dgvStages.Columns.Add(colFile);
        _dgvStages.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "表示名", FillWeight = 130 });
        _dgvStages.Columns.Add(new DataGridViewTextBoxColumn { Name = "thumbnail", HeaderText = "サムネイル", FillWeight = 120, ReadOnly = true });
        var colUnlock = new DataGridViewComboBoxColumn { Name = "unlock", HeaderText = "解放条件", FillWeight = 120 };
        colUnlock.Items.AddRange(new object[] { "常に選べる", "前のステージをクリア", "指定ステージをクリア" });
        _dgvStages.Columns.Add(colUnlock);
        _dgvStages.Columns.Add(new DataGridViewTextBoxColumn { Name = "require", HeaderText = "必要ステージ(,区切り)", FillWeight = 130 });
        _dgvStages.Columns.Add(new DataGridViewTextBoxColumn { Name = "item_total", HeaderText = "アイテム数", FillWeight = 70, ReadOnly = true });

        _dgvStages.CurrentCellDirtyStateChanged += (s, e) =>
        { if (_dgvStages.IsCurrentCellDirty) _dgvStages.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        // ファイルを選び直したら、そのステージのアイテム数を数え直して埋める。
        // セレクト画面の「3 / 7」表示のためにゲーム側が必要とする値で、
        // エディタが知っている情報なのでエディタが書く（起動時に全ステージを開かせない）。
        _dgvStages.CellValueChanged += (s, e) =>
        {
            if (_suppress || e.RowIndex < 0) return;
            if (_dgvStages.Columns[e.ColumnIndex].Name == "file") RefreshItemTotal(e.RowIndex);
        };
        // DataGridViewのComboBox列は、DataSourceに無い値を入れるとDataErrorを投げる。
        // 実害が無いので握りつぶす（既存の AssetManagerPageControl も同じ扱い）。
        _dgvStages.DataError += (s, e) => { e.ThrowException = false; };

        var bar = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = UiTheme.PanelBackLight };
        var btnUp = UiTheme.CreateButton("▲ 上へ", new Point(8, 6), new Size(80, 26));
        var btnDown = UiTheme.CreateButton("▼ 下へ", new Point(94, 6), new Size(80, 26));
        var btnThumb = UiTheme.CreateButton("サムネイルを選ぶ", new Point(184, 6), new Size(140, 26));
        var lblHint = UiTheme.CreateLabel("並び順がそのまま「前のステージ」の順序になります", new Point(336, 11));
        btnUp.Click += (s, e) => MoveStageRow(-1);
        btnDown.Click += (s, e) => MoveStageRow(+1);
        btnThumb.Click += (s, e) => PickThumbnailForCurrentRow();
        bar.Controls.AddRange(new Control[] { btnUp, btnDown, btnThumb, lblHint });

        // Fill を先に、Top を後から
        page.Controls.Add(_dgvStages);
        page.Controls.Add(bar);
        return page;
    }

    private TabPage BuildMiscTab()
    {
        var page = new TabPage("その他");
        var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        int y = 10;

        p.Controls.Add(UiTheme.CreateLabel("ウィンドウのタイトル", new Point(10, y), true)); y += 20;
        _txtWindowTitle = new TextBox { Location = new Point(10, y), Width = 360 };
        p.Controls.Add(_txtWindowTitle); y += 30;

        _chkTitleEnabled = new CheckBox { Text = "タイトル画面を使う（外すと起動して即プレイになります）", Location = new Point(10, y), Width = 420 };
        p.Controls.Add(_chkTitleEnabled); y += 32;

        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 420)); y += 14;

        p.Controls.Add(UiTheme.CreateLabel("ステージセレクト画面", new Point(10, y), true)); y += 20;
        p.Controls.Add(UiTheme.CreateLabel("見出し", new Point(10, y + 4)));
        _txtHeading = new TextBox { Location = new Point(90, y), Width = 280 };
        p.Controls.Add(_txtHeading); y += 28;
        p.Controls.Add(UiTheme.CreateLabel("もどる", new Point(10, y + 4)));
        _txtBack = new TextBox { Location = new Point(90, y), Width = 110 };
        p.Controls.Add(_txtBack); y += 28;
        p.Controls.Add(UiTheme.CreateLabel("未解放の表示", new Point(10, y + 4)));
        _txtLocked = new TextBox { Location = new Point(90, y), Width = 110 };
        p.Controls.Add(_txtLocked); y += 28;
        p.Controls.Add(UiTheme.CreateLabel("背景画像", new Point(10, y + 4)));
        _txtSelectBg = new TextBox { Location = new Point(90, y), Width = 210, ReadOnly = true };
        var btnSelBg = UiTheme.CreateButton("参照", new Point(306, y - 1), new Size(64, 24));
        btnSelBg.Click += (s, e) => PickImageInto(_txtSelectBg, rel => _cfg.stage_select.background_image = rel);
        p.Controls.Add(_txtSelectBg); p.Controls.Add(btnSelBg); y += 34;

        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 420)); y += 14;

        p.Controls.Add(UiTheme.CreateLabel("リザルト画面の文言", new Point(10, y), true)); y += 20;
        _txtVictory = AddLabeledText(p, "クリア時", ref y);
        _txtGameover = AddLabeledText(p, "ゲームオーバー", ref y);
        _txtNext = AddLabeledText(p, "つぎへ", ref y);
        _txtRetry = AddLabeledText(p, "もういちど", ref y);
        _txtSelect = AddLabeledText(p, "セレクトへ", ref y);
        y += 6;

        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 420)); y += 14;

        p.Controls.Add(UiTheme.CreateLabel("テーマ色", new Point(10, y), true)); y += 22;
        AddColorPicker(p, "標準の文字", "ink", ref y);
        AddColorPicker(p, "うすい文字", "ink_sub", ref y);
        AddColorPicker(p, "強調の文字", "ink_accent", ref y);
        AddColorPicker(p, "背景の下地", "backdrop", ref y);
        y += 8;

        p.Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 420)); y += 14;

        p.Controls.Add(UiTheme.CreateLabel("セーブデータ", new Point(10, y), true)); y += 20;
        string savePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabProject01", "save.json");
        p.Controls.Add(UiTheme.CreateLabel(savePath, new Point(10, y))); y += 22;
        var btnDelSave = UiTheme.CreateButton("セーブデータを削除", new Point(10, y), new Size(160, 28));
        // 解放条件を作ったら「ちゃんとロックされているか」を確かめるために毎回消したくなる。
        // %LOCALAPPDATA% を手で開くのは面倒なのでボタンにしてある。
        btnDelSave.Click += (s, e) =>
        {
            if (!File.Exists(savePath))
            { MessageBox.Show("セーブデータはまだありません。", "セーブデータ", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show("クリア記録を全部消します。よろしいですか？", "セーブデータの削除",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { File.Delete(savePath); MessageBox.Show("削除しました。", "セーブデータ"); }
            catch (Exception ex) { MessageBox.Show("削除できませんでした:\n" + ex.Message, "エラー"); }
        };
        p.Controls.Add(btnDelSave);

        page.Controls.Add(p);
        return page;
    }

    private TextBox AddLabeledText(Panel p, string label, ref int y)
    {
        p.Controls.Add(UiTheme.CreateLabel(label, new Point(10, y + 4)));
        var tb = new TextBox { Location = new Point(120, y), Width = 250 };
        p.Controls.Add(tb);
        y += 28;
        return tb;
    }

    private void AddColorPicker(Panel p, string label, string key, ref int y)
    {
        p.Controls.Add(UiTheme.CreateLabel(label, new Point(10, y + 4)));
        var btn = new Button { Location = new Point(120, y), Size = new Size(60, 24), Text = "" };
        btn.FlatStyle = FlatStyle.Flat;
        btn.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = btn.BackColor, FullOpen = true };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            btn.BackColor = dlg.Color;
            int[] rgb = { dlg.Color.R, dlg.Color.G, dlg.Color.B };
            switch (key)
            {
                case "ink": _cfg.theme.ink = rgb; break;
                case "ink_sub": _cfg.theme.ink_sub = rgb; break;
                case "ink_accent": _cfg.theme.ink_accent = rgb; break;
                case "backdrop": _cfg.theme.backdrop = rgb; break;
            }
            _canvas.Invalidate();
        };
        _colorButtons[key] = btn;
        p.Controls.Add(btn);
        y += 30;
    }

    private NumericUpDown MakeNum(Point loc, int min, int max, Action<float> onChanged)
    {
        var n = UiTheme.CreateNumericUpDown(loc, 80, min, max, 0);
        n.ValueChanged += (s, e) => { if (!_suppress) onChanged((float)n.Value); };
        return n;
    }

    // ================= 値の出し入れ =================

    private void LoadIntoUi()
    {
        _suppress = true;

        _txtWindowTitle.Text = _cfg.window_title;
        _chkTitleEnabled.Checked = _cfg.title_enabled;
        _txtTitleBg.Text = _cfg.title_screen.background_image;
        SelectBgmInCombo(_cboTitleBgm, _cfg.title_screen.bgm_id);

        _txtHeading.Text = _cfg.stage_select.heading;
        _txtBack.Text = _cfg.stage_select.back_label;
        _txtLocked.Text = _cfg.stage_select.locked_label;
        _txtSelectBg.Text = _cfg.stage_select.background_image;

        _txtVictory.Text = _cfg.result.victory_text;
        _txtGameover.Text = _cfg.result.gameover_text;
        _txtNext.Text = _cfg.result.next_label;
        _txtRetry.Text = _cfg.result.retry_label;
        _txtSelect.Text = _cfg.result.select_label;

        SetColorButton("ink", _cfg.theme.ink);
        SetColorButton("ink_sub", _cfg.theme.ink_sub);
        SetColorButton("ink_accent", _cfg.theme.ink_accent);
        SetColorButton("backdrop", _cfg.theme.backdrop);

        // メニュー項目
        _dgvMenu.Rows.Clear();
        foreach (var m in _cfg.title_screen.menu_items)
            _dgvMenu.Rows.Add(m.label, ActionToLabel(m.action));

        // ステージ一覧
        _dgvStages.Rows.Clear();
        foreach (var s in _cfg.stages)
            _dgvStages.Rows.Add(s.file, s.name, s.thumbnail, UnlockToLabel(s.unlock),
                                string.Join(",", s.require), s.item_total);

        _suppress = false;

        _cboElement.SelectedIndex = 1; // タイトル文字を最初に選んでおく
        _canvas.SelectKey("title");
        SyncElementPanel();
    }

    private void SetColorButton(string key, int[] rgb)
    {
        if (_colorButtons.TryGetValue(key, out var b) && rgb != null && rgb.Length >= 3)
            b.BackColor = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
    }

    // キャンバス側の選択・座標を右の欄へ反映する
    private void SyncElementPanel()
    {
        var el = _canvas.Selected;
        _suppress = true;
        bool has = el != null;
        foreach (Control c in new Control[] { _chkVisible, _txtText, _txtImage, _numX, _numY, _numW, _numH,
                                              _numFont, _cboRole, _chkEdge, _numItemH, _numGap })
            c.Enabled = has;

        if (el != null)
        {
            string[] keys = { "logo", "title", "subtitle", "menu" };
            int idx = Array.IndexOf(keys, el.key);
            if (idx >= 0 && _cboElement.SelectedIndex != idx) _cboElement.SelectedIndex = idx;

            _chkVisible.Checked = el.visible;
            _txtText.Text = el.text;
            _txtImage.Text = el.image;
            _numX.Value = Clamp(_numX, el.x);
            _numY.Value = Clamp(_numY, el.y);
            _numW.Value = Clamp(_numW, el.w);
            _numH.Value = Clamp(_numH, el.h);
            _numFont.Value = Clamp(_numFont, el.font_size);
            _numItemH.Value = Clamp(_numItemH, el.item_h);
            _numGap.Value = Clamp(_numGap, el.gap);
            _cboRole.SelectedIndex = el.color_role switch { "sub" => 1, "accent" => 2, _ => 0 };
            _chkEdge.Checked = el.edge;

            // 型ごとに意味のある欄だけ触れるようにする
            bool isText = el.type == "text";
            bool isImage = el.type == "image";
            bool isMenu = el.type == "menu";
            _txtText.Enabled = isText;
            _txtImage.Enabled = isImage;
            _numW.Enabled = isImage || isMenu;
            _numH.Enabled = isImage;
            _numItemH.Enabled = isMenu;
            _numGap.Enabled = isMenu;
            _cboRole.Enabled = isText;
            _chkEdge.Enabled = isText;
        }
        _suppress = false;
    }

    private static decimal Clamp(NumericUpDown n, float v)
    {
        decimal d = (decimal)v;
        if (d < n.Minimum) d = n.Minimum;
        if (d > n.Maximum) d = n.Maximum;
        return d;
    }

    // 右の欄からキャンバスへ書き戻す
    private void ApplyFromPanel(Action<TitleElement> apply)
    {
        if (_suppress) return;
        var el = _canvas.Selected;
        if (el == null) return;
        apply(el);
        if (el.type == "image") _canvas.InvalidateImageCache();
        _canvas.Invalidate();
    }

    private void ReadMenuGrid()
    {
        var list = new List<MenuItemDef>();
        foreach (DataGridViewRow row in _dgvMenu.Rows)
        {
            if (row.IsNewRow) continue;
            string label = row.Cells["label"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(label)) continue;
            list.Add(new MenuItemDef { label = label, action = LabelToAction(row.Cells["action"].Value?.ToString() ?? "") });
        }
        _cfg.title_screen.menu_items = list;
    }

    private static string ActionToLabel(string a) => a switch
    {
        "continue" => "つづきから",
        "quit" => "終了する",
        _ => "ステージ選択へ",
    };
    private static string LabelToAction(string l) => l switch
    {
        "つづきから" => "continue",
        "終了する" => "quit",
        _ => "stage_select",
    };
    private static string UnlockToLabel(string u) => u switch
    {
        "prev_clear" => "前のステージをクリア",
        "require" => "指定ステージをクリア",
        _ => "常に選べる",
    };
    private static string LabelToUnlock(string l) => l switch
    {
        "前のステージをクリア" => "prev_clear",
        "指定ステージをクリア" => "require",
        _ => "always",
    };

    // ================= ステージ一覧の操作 =================

    // assets/stages/ を列挙する。_test_play.json は
    // エディタの「ここからプレイ」が書き出す一時ファイルなので必ず除く
    // （Form1.RefreshStageList と同じルール）。
    private List<string> ListUserStages()
    {
        var list = new List<string>();
        if (!Directory.Exists(_stagesPath)) return list;
        foreach (var f in Directory.GetFiles(_stagesPath, "*.json"))
        {
            var name = Path.GetFileName(f);
            if (name == "_test_play.json") continue;
            list.Add(name);
        }
        return list;
    }

    private void RefreshItemTotal(int rowIndex)
    {
        string file = _dgvStages.Rows[rowIndex].Cells["file"].Value?.ToString() ?? "";
        int total = 0;
        if (!string.IsNullOrEmpty(file))
        {
            string p = Path.Combine(_stagesPath, file);
            if (File.Exists(p))
            {
                try { total = StageData.LoadFromFile(p).Items.Count; } catch { total = 0; }
            }
        }
        _suppress = true;
        _dgvStages.Rows[rowIndex].Cells["item_total"].Value = total;
        _suppress = false;
    }

    private void MoveStageRow(int delta)
    {
        var cur = _dgvStages.CurrentRow;
        if (cur == null || cur.IsNewRow) return;
        int i = cur.Index;
        int j = i + delta;
        if (j < 0 || j >= _dgvStages.Rows.Count || _dgvStages.Rows[j].IsNewRow) return;

        // DataGridViewは行の入れ替えAPIが無いので、値を交換する
        for (int c = 0; c < _dgvStages.Columns.Count; c++)
        {
            var tmp = _dgvStages.Rows[i].Cells[c].Value;
            _dgvStages.Rows[i].Cells[c].Value = _dgvStages.Rows[j].Cells[c].Value;
            _dgvStages.Rows[j].Cells[c].Value = tmp;
        }
        _dgvStages.CurrentCell = _dgvStages.Rows[j].Cells[Math.Max(0, _dgvStages.CurrentCell?.ColumnIndex ?? 0)];
    }

    private void PickThumbnailForCurrentRow()
    {
        var cur = _dgvStages.CurrentRow;
        if (cur == null || cur.IsNewRow)
        { MessageBox.Show("先に行を選んでください。", "サムネイル"); return; }
        PickImageInto(null, rel => { cur.Cells["thumbnail"].Value = rel; });
    }

    // 画像を選ばせて img/ へ取り込み、"img/ファイル名" を返す。
    // 直接パスを保存すると別PCで開けなくなるので、必ずプロジェクト内へコピーする。
    private void PickImageInto(TextBox? target, Action<string> onPicked)
    {
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "画像を選択" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        string rel = ImageImportHelper.CopyIntoImgFolder(_projectRoot, ofd.FileName);
        if (target != null) target.Text = rel;
        onPicked(rel);
        _canvas.InvalidateImageCache();
    }

    private void FillBgmCombo(ComboBox cbo)
    {
        cbo.Items.Add("(なし)");
        foreach (var b in _assets.Bgm) cbo.Items.Add(b.id);
    }
    private static string BgmIdFromCombo(ComboBox cbo)
        => (cbo.SelectedIndex <= 0) ? "" : (cbo.SelectedItem?.ToString() ?? "");
    private static void SelectBgmInCombo(ComboBox cbo, string id)
    {
        if (string.IsNullOrEmpty(id)) { cbo.SelectedIndex = 0; return; }
        int i = cbo.Items.IndexOf(id);
        cbo.SelectedIndex = (i >= 0) ? i : 0;
    }

    // ================= 保存 =================

    private void DoSave()
    {
        _cfg.window_title = _txtWindowTitle.Text;
        _cfg.title_enabled = _chkTitleEnabled.Checked;
        _cfg.title_screen.background_image = _txtTitleBg.Text;
        _cfg.title_screen.bgm_id = BgmIdFromCombo(_cboTitleBgm);

        _cfg.stage_select.heading = _txtHeading.Text;
        _cfg.stage_select.back_label = _txtBack.Text;
        _cfg.stage_select.locked_label = _txtLocked.Text;
        _cfg.stage_select.background_image = _txtSelectBg.Text;

        _cfg.result.victory_text = _txtVictory.Text;
        _cfg.result.gameover_text = _txtGameover.Text;
        _cfg.result.next_label = _txtNext.Text;
        _cfg.result.retry_label = _txtRetry.Text;
        _cfg.result.select_label = _txtSelect.Text;

        ReadMenuGrid();

        // ステージ一覧。既存エントリを土台にして上書きし、
        // C++側だけが解釈するキー(_extra)を取りこぼさないようにする。
        var old = _cfg.stages.ToDictionary(s => s.file, s => s);
        var list = new List<StageEntry>();
        foreach (DataGridViewRow row in _dgvStages.Rows)
        {
            if (row.IsNewRow) continue;
            string file = row.Cells["file"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(file)) continue;

            var entry = old.TryGetValue(file, out var prev) ? prev : new StageEntry();
            entry.file = file;
            entry.name = row.Cells["name"].Value?.ToString() ?? file;
            entry.thumbnail = row.Cells["thumbnail"].Value?.ToString() ?? "";
            entry.unlock = LabelToUnlock(row.Cells["unlock"].Value?.ToString() ?? "");
            entry.require = (row.Cells["require"].Value?.ToString() ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            int.TryParse(row.Cells["item_total"].Value?.ToString(), out int it);
            entry.item_total = it;
            list.Add(entry);
        }
        _cfg.stages = list;

        // 同じステージを二重に載せると「前のステージをクリア」の判定が読みにくくなるので警告する
        var dup = list.GroupBy(s => s.file).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dup.Count > 0)
        {
            MessageBox.Show("同じステージが複数回登録されています:\n  " + string.Join("\n  ", dup)
                            + "\n\n解放条件の判定が意図しない結果になる可能性があります。",
                            "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        try
        {
            _cfg.Save(_assetsPath);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存できませんでした:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
