namespace Lab_Editor;

// ======================================================
// PresetPickerForm - 「設定済みのプリセット」を選んで、画像を決めるダイアログ
//
// 【なぜ作ったか】
// 色・明るさに関わる敵やギミックは、タイプ・範囲・色・しきい値など決める項目が多く、
// 「どの値にすればどう遊べるか」を知らないと、ゼロから作るのが難しかった。
// そこで、よくある形を設定済みの状態でまとめて用意し、選ぶだけで敵・ギミックの行が追加されるようにする
// （追加したあとは、普通の敵・ギミックと同じように、各項目を自由に直せる）。
// 敵のプリセットは EnemyPresets.cs、ギミックのプリセットは GimmickPresets.cs。
// ======================================================

// 一覧に出す1件
public class PresetInfo
{
    public string Id = "";
    public string Group = "";        // 一覧の見出し
    public string Name = "";
    public string Description = "";  // 何ができるか・どう使うか
    public string Notes = "";        // 置くときの注意（任意）
}

public class PresetPickerForm : Form
{
    private readonly IReadOnlyList<PresetInfo> _presets;
    private readonly string _projectRoot;
    private ListView _list = null!;
    private Label _lblName = null!, _lblDesc = null!, _lblNotes = null!, _lblSpritePath = null!;
    private PictureBox _picSprite = null!;
    private Button _btnOk = null!;

    public int SelectedIndex { get; private set; } = -1;
    public string SpritePath { get; private set; } = "";

    public PresetPickerForm(string title, string hint, IReadOnlyList<PresetInfo> presets, string spriteLabel, string initialSprite, string projectRoot)
    {
        _presets = presets;
        _projectRoot = projectRoot;
        SpritePath = initialSprite ?? "";

        Text = title;
        Size = new Size(960, 640);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9f);

        var lblHint = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 8, 10, 0), Text = hint, ForeColor = Color.DimGray };

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None, ShowGroups = true,
        };
        _list.Columns.Add("名前", 360);
        var groups = new Dictionary<string, ListViewGroup>();
        for (int i = 0; i < presets.Count; i++)
        {
            var p = presets[i];
            if (!groups.TryGetValue(p.Group, out var g)) { g = new ListViewGroup(p.Group, HorizontalAlignment.Left); groups[p.Group] = g; _list.Groups.Add(g); }
            _list.Items.Add(new ListViewItem(p.Name) { Tag = i, Group = g });
        }
        _list.SelectedIndexChanged += (s, e) => OnSelect();
        _list.DoubleClick += (s, e) => { if (SelectedIndex >= 0) { DialogResult = DialogResult.OK; Close(); } };
        split.Panel1.Controls.Add(_list);

        var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 8, 10, 8) };
        _lblName = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 12f, FontStyle.Bold), MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 6) };
        _lblDesc = new Label { AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 0, 0, 8) };
        _lblNotes = new Label { AutoSize = true, MaximumSize = new Size(480, 0), ForeColor = Color.FromArgb(176, 112, 0), Margin = new Padding(0, 0, 0, 8) };
        right.Controls.Add(_lblName);
        right.Controls.Add(_lblDesc);
        right.Controls.Add(_lblNotes);
        if (!string.IsNullOrEmpty(spriteLabel))
        {
            right.Controls.Add(new Label { Text = spriteLabel, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 2) });
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            _picSprite = new PictureBox { Size = new Size(56, 56), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(240, 240, 240) };
            var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _lblSpritePath = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.DimGray };
            var btn = new Button { Text = "📁 画像を選ぶ…", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
            btn.Click += (s, e) => PickSprite();
            var btnNone = new Button { Text = "画像なし", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
            btnNone.Click += (s, e) => { SpritePath = ""; ShowSprite(); };
            col.Controls.Add(_lblSpritePath);
            var btnRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            btnRow.Controls.Add(btn); btnRow.Controls.Add(btnNone);
            col.Controls.Add(btnRow);
            row.Controls.Add(_picSprite); row.Controls.Add(col);
            right.Controls.Add(row);
            ShowSprite();
        }
        split.Panel2.Controls.Add(right);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        _btnOk = new Button { Text = "これを追加する", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Enabled = false };
        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(_btnOk);
        AcceptButton = _btnOk; CancelButton = btnCancel;

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(lblHint);
        Load += (s, e) => { split.SplitterDistance = 400; if (_list.Items.Count > 0) _list.Items[0].Selected = true; };
    }

    private void OnSelect()
    {
        if (_list.SelectedItems.Count == 0) { SelectedIndex = -1; _btnOk.Enabled = false; return; }
        SelectedIndex = (int)_list.SelectedItems[0].Tag!;
        var p = _presets[SelectedIndex];
        _lblName.Text = p.Name;
        _lblDesc.Text = p.Description;
        _lblNotes.Text = p.Notes;
        _lblNotes.Visible = !string.IsNullOrEmpty(p.Notes);
        _btnOk.Enabled = true;
    }

    private void PickSprite()
    {
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "画像を選択" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        // プロジェクト外のファイルを直接参照すると、あとで場所を移されたときに壊れるため、必ず img/ へコピーしてから使う
        SpritePath = ImageImportHelper.CopyIntoImgFolder(_projectRoot, ofd.FileName);
        ShowSprite();
    }

    private void ShowSprite()
    {
        if (_picSprite == null) return;
        _lblSpritePath.Text = string.IsNullOrEmpty(SpritePath) ? "(画像なし)" : SpritePath;
        _picSprite.Image?.Dispose();
        _picSprite.Image = null;
        if (string.IsNullOrEmpty(SpritePath)) return;
        string full = Path.Combine(_projectRoot, SpritePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return;
        try { using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read); _picSprite.Image = Image.FromStream(fs); } catch { }
    }
}
