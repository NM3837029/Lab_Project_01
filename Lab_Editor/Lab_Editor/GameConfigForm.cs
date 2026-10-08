namespace Lab_Editor;

// ゲーム設定ページ(GameConfigPageControl)を単独ウィンドウとして開くための薄いラッパー。
// 他のエディタ画面（SizeEditorForm 等）と同じ形にしてあるので、
// 将来 WorkbenchShell のページ遷移へ載せ替えるときも中身をそのまま使える。
public class GameConfigForm : Form
{
    private readonly GameConfigPageControl _page;

    public GameConfigForm(string projectRoot, string assetsPath, string stagesPath,
                          AssetDefinitions assets, GameConfig cfg)
    {
        Text = "ゲーム設定（タイトル画面・ステージ一覧）";
        Size = new Size(1180, 760);
        // 配置キャンバスとプロパティ欄が並ぶので、小さくしすぎると操作できなくなる
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        _page = new GameConfigPageControl(projectRoot, assetsPath, stagesPath, assets, cfg) { Dock = DockStyle.Fill };
        _page.Saved += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        _page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(_page);
        AcceptButton = _page.PrimaryActionButton;
        CancelButton = _page.SecondaryActionButton;
    }
}
