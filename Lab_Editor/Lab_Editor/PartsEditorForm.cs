namespace Lab_Editor;

// UI改善（構造改修フェーズ5c）— 中身はPartsEditorPageControlに抽出済み（ウィザード群
// RodGeneratorForm/PendulumGeneratorForm/OrbitGeneratorForm とその *GroupInfo データクラスも
// PartsEditorPageControl.cs 側に移動済み）。
// このクラスは既存の呼び出し元（AssetManagerPageControl.cs等）が引き続き
// `new PartsEditorForm(...).ShowDialog()` で使えるよう、PageControlをFillで貼り付けるだけの
// 薄いラッパーとして残している。フェーズ5eでWorkbenchShellFormに直接
// PartsEditorPageControlを載せる配線に切り替わるまではこれが唯一の入り口。
public class PartsEditorForm : Form
{
    public List<PartDef> ResultParts { get; private set; } = new();

    public PartsEditorForm(string subjectLabel, List<PartDef> initialParts, string projectRoot, string baseSpritePath)
    {
        Text = $"🧩 パーツエディタ - {subjectLabel}";
        Size = new Size(1300, 820);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        var page = new PartsEditorPageControl(subjectLabel, initialParts, projectRoot, baseSpritePath) { Dock = DockStyle.Fill };
        page.Saved += (s, parts) => { ResultParts = parts; DialogResult = DialogResult.OK; Close(); };
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        // ドリルダウン系の編集要求は、モーダルFormをShowDialog()するという従来通りの動作で応じる
        // （WorkbenchShellForm経由でこのページを直接ホストする場合は、Form1側が別の応じ方を配線する）。
        page.HitboxEditRequested += (fullPath, ox, oy, w, h, onSaved) =>
        {
            using var form = new HitboxEditorForm(fullPath, ox, oy, w, h);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.HitboxOffsetX, form.HitboxOffsetY, form.HitboxWidth, form.HitboxHeight);
        };
        page.BehaviorScriptEditRequested += (label, initialScript, onSaved) =>
        {
            using var form = new BehaviorScriptEditorForm(label, initialScript);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultScript);
        };

        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
