using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はBehaviorScriptEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new BehaviorScriptEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class BehaviorScriptEditorForm : Form
{
    // ダイアログがOKで閉じられたときに、確定した挙動スクリプト(JSON配列)をここに格納する。
    public JArray ResultScript { get; private set; } = new JArray();

    // 引数なしコンストラクタ。特定の対象を編集するのではなく、
    // プレビュー版（対象未指定）としてエディタを開く場合に使う。
    public BehaviorScriptEditorForm() : this(null, null) { }

    // subjectLabel  : 編集対象を示すラベル文字列（タイトル表示用。未指定ならプレビュー版扱い）
    // initialScript : 編集開始時点での挙動スクリプト(JSON配列)
    public BehaviorScriptEditorForm(string? subjectLabel, JArray? initialScript)
    {
        // subjectLabelが指定されているかどうかで、特定対象の編集なのか
        // プレビュー版なのかを判定する。
        bool isEditingSpecificScript = subjectLabel != null;
        // 判定結果に応じてウィンドウタイトルを切り替える。
        Text = isEditingSpecificScript
            ? $"🧩 挙動スクリプトエディタ - {subjectLabel}"
            : "🧩 挙動スクリプトエディタ（プレビュー版）";
        // ウィンドウのサイズ・最小サイズ・表示位置・フォントを設定する。
        Size = new Size(1000, 650);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // 実際の編集UIを持つPageControlを生成し、フォーム全体を埋めるように配置する。
        var page = new BehaviorScriptEditorPageControl(subjectLabel, initialScript) { Dock = DockStyle.Fill };
        // ページ側で「保存」が行われたら、確定したスクリプトを受け取ってフォームをOKで閉じる。
        page.Saved += (s, script) => { ResultScript = script; DialogResult = DialogResult.OK; Close(); };
        // ページ側で「キャンセル」されたら、このフォームもCancelダイアログ結果として閉じる。
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        // 作成したPageControlをフォームに追加し、Enter/Escapeキーがそれぞれ
        // ページ側の主要ボタン（保存/キャンセル相当）に対応するようにする。
        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
