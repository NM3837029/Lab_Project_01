namespace Lab_Editor;

// ======================================================
// AssetPickerForm - 敵・ギミック・アイテムを、絵つきのカードから1つ選ぶ小さな画面
//
// 以前は、アニメーション編集の入り口が「IDの文字だけを並べたリストボックス＋OKボタン」で、
// どれがどのキャラか分からなかった。右パネルの「配置対象」と同じ、絵つきカード・検索・絞り込みで選べるようにする。
// ======================================================
public sealed class AssetPickerForm : Form
{
    private readonly PlacementPalettePanel _panel = new() { Dock = DockStyle.Fill };
    private readonly Button _ok;
    private readonly AssetThumbs _thumbs;

    // 選ばれたアセットのID（選ばずに閉じたときは null）
    public string? SelectedId { get; private set; }

    public AssetPickerForm(string title, string message, AssetDefinitions assets)
    {
        Text = title;
        Font = UiTheme.Base;
        Size = new Size(520, 640);
        MinimumSize = new Size(420, 480);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.PanelBack;

        _thumbs = new AssetThumbs(Path.Combine(AppPaths.ProjectRoot, "assets"));
        _panel.Bind(assets, _thumbs);

        var head = new Label
        {
            Dock = DockStyle.Top, Height = 50, Text = message, Padding = new Padding(14, 10, 14, 0),
            Font = UiTheme.Base, ForeColor = UiTheme.TextMain, BackColor = UiTheme.CardBack,
        };
        var foot = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = UiTheme.CardBack };
        _ok = Kit.Btn("選んで進む", () => { DialogResult = DialogResult.OK; }, primary: true);
        _ok.Enabled = false;
        var cancel = Kit.Btn("キャンセル", () => { DialogResult = DialogResult.Cancel; });
        var flow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 10, 12, 8), BackColor = UiTheme.CardBack };
        flow.Controls.Add(cancel);
        flow.Controls.Add(_ok);
        foot.Controls.Add(flow);

        _panel.AssetPicked += (kind, id) => { SelectedId = id; _ok.Enabled = true; };
        _panel.PickCleared += () => { SelectedId = null; _ok.Enabled = false; };

        Controls.Add(_panel);
        Controls.Add(foot);
        Controls.Add(head);
        AcceptButton = _ok;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _thumbs.Dispose();
        base.OnFormClosed(e);
    }
}
