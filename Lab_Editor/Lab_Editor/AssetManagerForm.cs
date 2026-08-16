namespace Lab_Editor;

// UI改善（構造改修フェーズ5b）— 中身はAssetManagerPageControlに抽出済み。
// このクラスは既存の呼び出し元（Form1.cs等）が引き続き `new AssetManagerForm(...).ShowDialog()`
// で使えるよう、PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
// フェーズ5eでWorkbenchShellFormに直接AssetManagerPageControlを載せる配線に切り替わるまでは
// これが唯一の入り口。
public class AssetManagerForm : Form
{
    public AssetManagerForm(string assetsPath, AssetDefinitions assets)
    {
        Text = "アセット管理エディタ - 敵 / ギミック / アイテム";
        Size = new Size(1160, 720);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        string projectRoot = Path.GetDirectoryName(assetsPath)!;

        var page = new AssetManagerPageControl(assetsPath, assets) { Dock = DockStyle.Fill };
        page.Saved += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        // ドリルダウン系の編集要求は、モーダルFormをShowDialog()するという従来通りの動作で応じる
        // （WorkbenchShellForm経由でこのページを直接ホストする場合は、Form1側が別の応じ方を配線する）。
        page.HitboxEditRequested += (fullPath, ox, oy, w, h, onSaved) =>
        {
            using var form = new HitboxEditorForm(fullPath, ox, oy, w, h);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.HitboxOffsetX, form.HitboxOffsetY, form.HitboxWidth, form.HitboxHeight);
        };
        page.SizeEditRequested += (fullPath, curScale, onSaved) =>
        {
            using var form = new SizeEditorForm(fullPath, curScale);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultScale);
        };
        page.BehaviorScriptEditRequested += (label, initialScript, onSaved) =>
        {
            using var form = new BehaviorScriptEditorForm(label, initialScript);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultScript);
        };
        page.PartsEditRequested += (label, initialParts, baseSpritePath, onSaved) =>
        {
            using var form = new PartsEditorForm(label, initialParts, projectRoot, baseSpritePath);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultParts);
        };
        page.CommonEventEditRequested += (ev, onSaved) =>
        {
            using var form = new CommonEventEditorForm(ev, assets, page.GetStageFileNames());
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultEvent);
        };

        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
