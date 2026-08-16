using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はBehaviorScriptEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new BehaviorScriptEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class BehaviorScriptEditorForm : Form
{
    public JArray ResultScript { get; private set; } = new JArray();

    public BehaviorScriptEditorForm() : this(null, null) { }

    public BehaviorScriptEditorForm(string? subjectLabel, JArray? initialScript)
    {
        bool isEditingSpecificScript = subjectLabel != null;
        Text = isEditingSpecificScript
            ? $"🧩 挙動スクリプトエディタ - {subjectLabel}"
            : "🧩 挙動スクリプトエディタ（プレビュー版）";
        Size = new Size(1000, 650);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        var page = new BehaviorScriptEditorPageControl(subjectLabel, initialScript) { Dock = DockStyle.Fill };
        page.Saved += (s, script) => { ResultScript = script; DialogResult = DialogResult.OK; Close(); };
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
