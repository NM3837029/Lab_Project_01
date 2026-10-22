using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lab_Editor;

// パーツ編集画面の詳細パネルに置く、「画像の切り替え」の編集欄。
//
// 選んでいるパーツの切り替え一覧（PartDef.sprite_rules）を、「条件 × 切り替え先の画像」で作る。
// 一覧の上から順に見て、最初に当てはまった行の画像が使われる（どれにも当てはまらなければ、パーツ本来の画像）。
//
// 【選べる条件】
// 持ち主（敵・ギミック・アイテム）が何をできるかを PartSpriteRules.Available が調べて、起こりうる条件だけを出す。
// このコントロール自身は PartDef を知らない。Bind で渡された List<PartSpriteRule> を直接書き換え、
// 変えたことを Changed イベントで知らせる（履歴・プレビューの更新は呼び出し側）。
public class PartSpriteRuleEditorControl : UserControl
{
    // 値が変わったとき。引数は履歴をまとめるためのキー（同じキーが続く間は1手にまとまる）
    public event Action<string>? Changed;

    private List<PartSpriteRule>? _rules;
    private PartOwnerInfo _owner = PartOwnerInfo.Unknown();
    private string _projectRoot = "";
    private bool _suppress;
    private List<PartSpriteRules.ConditionInfo> _available = new();

    private readonly Label _lblOwner = new();
    private readonly CheckedListBox _list = new();
    private readonly Button _btnAdd = new(), _btnDel = new(), _btnUp = new(), _btnDown = new();
    private readonly Panel _card = new();
    private readonly ComboBox _cboWhen = new();
    private readonly NumericUpDown _numParam = new();
    private readonly Label _lblParam = new(), _lblHint = new(), _lblEmpty = new(), _lblSpritePath = new();
    private readonly Button _btnPick = new();
    private readonly PictureBox _pic = new();
    private Panel _rowParam = null!;

    public PartSpriteRuleEditorControl()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = UiTheme.Base;
        Margin = new Padding(0);

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0),
        };
        Controls.Add(flow);

        var intro = new Label
        {
            Text = "条件を満たしたとき、パーツの画像を別の絵に切り替えます。上の行から順に見て、最初に当てはまった行の画像を使います。どれにも当てはまらなければ、元の画像のままです。",
            AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 3),
        };
        flow.Controls.Add(intro);

        // 持ち主が何をできるか（これを基に、選べる条件が決まる）
        _lblOwner.AutoSize = true;
        _lblOwner.MaximumSize = new Size(300, 0);
        _lblOwner.ForeColor = Color.FromArgb(20, 90, 140);
        _lblOwner.Margin = new Padding(0, 0, 0, 4);
        flow.Controls.Add(_lblOwner);

        _list.Width = 296; _list.Height = 78; _list.CheckOnClick = false; _list.IntegralHeight = false;
        _list.ItemCheck += (s, e) => { /* 切り替えの一覧は、無効にする機能は持たない（行を消す）ので、チェックは常にオン */ };
        _list.MouseDown += (s, e) =>
        {
            int i = _list.IndexFromPoint(e.Location);
            if (i >= 0) _list.SelectedIndex = i;
        };
        _list.SelectedIndexChanged += (s, e) => { if (!_suppress) ShowCard(); };
        flow.Controls.Add(_list);

        var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 3, 0, 3) };
        StyleBtn(_btnAdd, "＋ 追加", 72, (s, e) => AddRule());
        StyleBtn(_btnDel, "削除", 52, (s, e) => DeleteRule());
        StyleBtn(_btnUp, "▲", 34, (s, e) => MoveRule(-1));
        StyleBtn(_btnDown, "▼", 34, (s, e) => MoveRule(1));
        foreach (var b in new[] { _btnAdd, _btnDel, _btnUp, _btnDown }) bar.Controls.Add(b);
        flow.Controls.Add(bar);

        _lblEmpty.Text = "切り替えはまだありません。「＋ 追加」で作ります。";
        _lblEmpty.AutoSize = true; _lblEmpty.ForeColor = Color.Gray; _lblEmpty.Margin = new Padding(0, 0, 0, 3);
        flow.Controls.Add(_lblEmpty);

        // ---- 選んだ行の編集カード ----
        _card.Width = 296; _card.AutoSize = true; _card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _card.BorderStyle = BorderStyle.FixedSingle; _card.Padding = new Padding(6); _card.Margin = new Padding(0, 0, 0, 6);
        var cf = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0),
        };
        _card.Controls.Add(cf);

        cf.Controls.Add(new Label { Text = "いつ（条件）", AutoSize = true, Font = UiTheme.Small, Margin = new Padding(0, 0, 0, 1) });
        _cboWhen.DropDownStyle = ComboBoxStyle.DropDownList; _cboWhen.Width = 278;
        _cboWhen.SelectedIndexChanged += (s, e) => OnWhenChanged();
        cf.Controls.Add(_cboWhen);
        _lblHint.AutoSize = true; _lblHint.MaximumSize = new Size(278, 0); _lblHint.ForeColor = Color.DimGray;
        _lblHint.Font = UiTheme.Small; _lblHint.Margin = new Padding(0, 1, 0, 4);
        cf.Controls.Add(_lblHint);

        _rowParam = new Panel { Width = 278, Height = 44, Margin = new Padding(0, 0, 0, 2) };
        _lblParam.AutoSize = true; _lblParam.Font = UiTheme.Small; _lblParam.Location = new Point(0, 0);
        _numParam.Location = new Point(0, 18); _numParam.Width = 110;
        _numParam.ValueChanged += (s, e) =>
        {
            if (_suppress || Current() is not { } r) return;
            r.param = (float)_numParam.Value;
            RefreshListItem(_list.SelectedIndex);
            Changed?.Invoke("sprite-rule-param");
        };
        _rowParam.Controls.Add(_lblParam); _rowParam.Controls.Add(_numParam);
        cf.Controls.Add(_rowParam);

        cf.Controls.Add(new Label { Text = "切り替え先の画像", AutoSize = true, Font = UiTheme.Small, Margin = new Padding(0, 2, 0, 1) });
        var rowImg = new Panel { Width = 278, Height = 64, Margin = new Padding(0) };
        _pic.Location = new Point(0, 0); _pic.Size = new Size(60, 60); _pic.SizeMode = PictureBoxSizeMode.Zoom;
        _pic.BorderStyle = BorderStyle.FixedSingle; _pic.BackColor = Color.FromArgb(235, 235, 235);
        _lblSpritePath.Location = new Point(66, 2); _lblSpritePath.Size = new Size(210, 28); _lblSpritePath.Font = UiTheme.Small;
        _btnPick.Text = "画像を選ぶ..."; _btnPick.Location = new Point(66, 32); _btnPick.Size = new Size(100, 26);
        _btnPick.Click += (s, e) => PickImage();
        rowImg.Controls.Add(_pic); rowImg.Controls.Add(_lblSpritePath); rowImg.Controls.Add(_btnPick);
        cf.Controls.Add(rowImg);
        flow.Controls.Add(_card);
    }

    private static void StyleBtn(Button b, string text, int width, EventHandler click)
    {
        b.Text = text; b.Width = width; b.Height = 26; b.Margin = new Padding(0, 0, 4, 0); b.Click += click;
    }

    // 選んでいるパーツの一覧へ向ける。rules が null なら未選択（何も出さない）。
    //   owner       … 持ち主の情報（選べる条件を決める）
    //   projectRoot … 画像のコピー先（img フォルダ）と、サムネイル表示の基準
    public void Bind(List<PartSpriteRule>? rules, PartOwnerInfo owner, string projectRoot)
    {
        _suppress = true;
        _rules = rules;
        _owner = owner;
        _projectRoot = projectRoot;
        _available = PartSpriteRules.Available(owner);
        _lblOwner.Text = PartSpriteRules.Describe(owner);
        Enabled = rules != null;
        RefreshList(0);
        _suppress = false;
        ShowCard();
    }

    private PartSpriteRule? Current()
        => _rules != null && _list.SelectedIndex >= 0 && _list.SelectedIndex < _rules.Count ? _rules[_list.SelectedIndex] : null;

    private void RefreshList(int select)
    {
        _list.Items.Clear();
        if (_rules != null)
            foreach (var r in _rules)
            {
                bool ok = _available.Any(c => c.Name == r.when);
                _list.Items.Add(PartSpriteRules.DescribeRule(r) + (ok ? "" : "  ⚠"), true);
            }
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Clamp(select, 0, _list.Items.Count - 1);
        _lblEmpty.Visible = _rules != null && _rules.Count == 0;
        _list.Visible = _rules != null && _rules.Count > 0;
    }

    private void RefreshListItem(int i)
    {
        if (_rules == null || i < 0 || i >= _rules.Count) return;
        bool ok = _available.Any(c => c.Name == _rules[i].when);
        _suppress = true;
        _list.Items[i] = PartSpriteRules.DescribeRule(_rules[i]) + (ok ? "" : "  ⚠");
        _list.SetItemChecked(i, true);
        _suppress = false;
    }

    // 選んでいる行の内容を、編集カードへ映す
    private void ShowCard()
    {
        var r = Current();
        _card.Visible = r != null;
        bool has = r != null;
        _btnDel.Enabled = has;
        _btnUp.Enabled = has && _list.SelectedIndex > 0;
        _btnDown.Enabled = has && _rules != null && _list.SelectedIndex < _rules.Count - 1;
        _btnAdd.Enabled = _rules != null;
        if (r == null) return;

        _suppress = true;
        // 条件の選択肢：起こりうる条件。いまの値がそれに無い（別の型で作った）ときは、注意つきで足す
        _cboWhen.Items.Clear();
        foreach (var c in _available) _cboWhen.Items.Add(c.Label);
        int idx = _available.FindIndex(c => c.Name == r.when);
        if (idx < 0)
        {
            _cboWhen.Items.Add(PartSpriteRules.LabelOf(r.when) + "（この物では起こりません）");
            idx = _cboWhen.Items.Count - 1;
        }
        _cboWhen.SelectedIndex = idx;
        ShowWhenDetails(r);
        _suppress = false;
        ShowSprite(r.sprite);
    }

    private void ShowWhenDetails(PartSpriteRule r)
    {
        var info = PartSpriteRules.Find(r.when);
        _lblHint.Text = info?.Hint ?? "";
        bool usesParam = info?.ParamLabel != null;
        _rowParam.Visible = usesParam;
        if (usesParam && info != null)
        {
            _lblParam.Text = info.ParamLabel;
            _numParam.Minimum = (decimal)info.ParamMin; _numParam.Maximum = (decimal)info.ParamMax;
            _numParam.DecimalPlaces = info.ParamDecimals; _numParam.Increment = (decimal)info.ParamStep;
            _numParam.Value = (decimal)Math.Clamp(r.param, info.ParamMin, info.ParamMax);
        }
    }

    private void OnWhenChanged()
    {
        if (_suppress || Current() is not { } r) return;
        if (_cboWhen.SelectedIndex < 0 || _cboWhen.SelectedIndex >= _available.Count) return; // 注意つきの行を選び直したときは何もしない
        var c = _available[_cboWhen.SelectedIndex];
        r.when = c.Name;
        r.param = c.ParamLabel != null ? c.ParamDefault : 0f;
        _suppress = true;
        ShowWhenDetails(r);
        _suppress = false;
        RefreshListItem(_list.SelectedIndex);
        Changed?.Invoke("sprite-rule-when");
    }

    private void AddRule()
    {
        if (_rules == null || _available.Count == 0) return;
        // 最初に出る条件のうち、「近い」以外のほうを先に勧める（「近い」はどの物でも選べて、最初の例になりにくい）
        var c = _available.Count > 1 ? _available[1] : _available[0];
        var r = new PartSpriteRule { when = c.Name, param = c.ParamLabel != null ? c.ParamDefault : 0f, sprite = "" };
        _rules.Add(r);
        RefreshList(_rules.Count - 1);
        ShowCard();
        Changed?.Invoke("sprite-rule-add");
    }

    private void DeleteRule()
    {
        if (_rules == null) return;
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _rules.Count) return;
        _rules.RemoveAt(i);
        RefreshList(Math.Min(i, _rules.Count - 1));
        ShowCard();
        Changed?.Invoke("sprite-rule-del");
    }

    private void MoveRule(int d)
    {
        if (_rules == null) return;
        int i = _list.SelectedIndex, j = i + d;
        if (i < 0 || j < 0 || j >= _rules.Count) return;
        (_rules[i], _rules[j]) = (_rules[j], _rules[i]);
        RefreshList(j);
        ShowCard();
        Changed?.Invoke("sprite-rule-move");
    }

    // 画像を選び、プロジェクトの img フォルダへコピーして、「img/ファイル名」の相対パスを設定する（背景設定と同じ作り）
    private void PickImage()
    {
        if (Current() is not { } r) return;
        using var dlg = new OpenFileDialog { Title = "切り替え先の画像を選択", Filter = "画像ファイル|*.png;*.jpg;*.bmp;*.gif|すべて|*.*" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            string imgDir = Path.Combine(_projectRoot, "img");
            Directory.CreateDirectory(imgDir);
            string dest = Path.Combine(imgDir, Path.GetFileName(dlg.FileName));
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);
            r.sprite = "img/" + Path.GetFileName(dlg.FileName);
            ShowSprite(r.sprite);
            RefreshListItem(_list.SelectedIndex);
            Changed?.Invoke("sprite-rule-image");
        }
        catch (Exception ex)
        {
            MessageBox.Show("画像のコピーに失敗しました:" + Environment.NewLine + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSprite(string rel)
    {
        _lblSpritePath.Text = string.IsNullOrEmpty(rel) ? "（まだ選んでいません）" : rel;
        _pic.Image?.Dispose();
        _pic.Image = null;
        if (string.IsNullOrEmpty(rel)) return;
        string p = Path.Combine(_projectRoot, rel);
        if (!File.Exists(p)) return;
        try
        {
            // ファイルを掴んだままにするとゲームが同じ画像を読めなくなるので、読み切ってから Bitmap にする
            using var fs = new FileStream(p, FileMode.Open, FileAccess.Read);
            _pic.Image = new Bitmap(Image.FromStream(fs));
        }
        catch { /* 読めなければ、サムネイルを出さないだけ */ }
    }
}
