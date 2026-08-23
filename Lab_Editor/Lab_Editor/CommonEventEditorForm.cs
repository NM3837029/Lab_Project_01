namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はCommonEventEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new CommonEventEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class CommonEventEditorForm : Form
{
    // ダイアログがOKで閉じられたときに、確定したコモンイベントの定義をここに格納する。
    public CommonEventDef ResultEvent { get; private set; } = null!;

    // ev         : 編集対象となるコモンイベントの定義データ
    // assets     : アセット定義一式（イベント条件・アクションの選択肢を組み立てるために使う）
    // stageFiles : 選択肢に表示するステージファイル名の一覧
    public CommonEventEditorForm(CommonEventDef ev, AssetDefinitions assets, List<string> stageFiles)
    {
        // ウィンドウのタイトル・サイズ・フォント・表示位置を設定する。
        Text            = "コモンイベント編集";
        Size            = new Size(720, 380);
        Font            = UiTheme.Base;
        StartPosition   = FormStartPosition.CenterParent;
        // ウィンドウ枠をリサイズ可能なスタイルに揃える（最大化ボタン等の見た目を統一する共通処理）。
        UiTheme.ApplyResizableChrome(this);

        // 実際の編集UIを持つPageControlを生成し、フォーム全体を埋めるように配置する。
        var page = new CommonEventEditorPageControl(ev, assets, stageFiles) { Dock = DockStyle.Fill };
        // ページ側で「保存」が行われたら、確定したイベント定義を受け取ってフォームをOKで閉じる。
        page.Saved += (s, result) => { ResultEvent = result; DialogResult = DialogResult.OK; Close(); };
        // ページ側で「キャンセル」されたら、このフォームもCancelダイアログ結果として閉じる。
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        // 作成したPageControlをフォームに追加し、Enter/Escapeキーがそれぞれ
        // ページ側の主要ボタン（保存/キャンセル相当）に対応するようにする。
        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
