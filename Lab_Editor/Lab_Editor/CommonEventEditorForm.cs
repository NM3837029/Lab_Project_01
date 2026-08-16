namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はCommonEventEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new CommonEventEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class CommonEventEditorForm : Form
{
    public CommonEventDef ResultEvent { get; private set; } = null!;

    public CommonEventEditorForm(CommonEventDef ev, AssetDefinitions assets, List<string> stageFiles)
    {
        Text            = "コモンイベント編集";
        Size            = new Size(720, 380);
        Font            = UiTheme.Base;
        StartPosition   = FormStartPosition.CenterParent;
        UiTheme.ApplyResizableChrome(this);

        var page = new CommonEventEditorPageControl(ev, assets, stageFiles) { Dock = DockStyle.Fill };
        page.Saved += (s, result) => { ResultEvent = result; DialogResult = DialogResult.OK; Close(); };
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
