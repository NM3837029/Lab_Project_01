namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はSizeEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new SizeEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class SizeEditorForm : Form
{
    // ダイアログがOKで閉じられたときに確定した表示スケール値を、
    // PageControlから透過的に取得するためのプロパティ。
    public float ResultScale => _page.ResultScale;

    // 実際の編集UIを持つPageControlへの参照。上記プロパティの委譲元として保持する。
    private readonly SizeEditorPageControl _page;

    // imagePath    : スケールを設定する対象の画像ファイルパス
    // initialScale : 編集開始時点での表示スケール値
    public SizeEditorForm(string imagePath, float initialScale)
    {
        // ウィンドウのタイトル・サイズを設定する。
        Text = "表示サイズ(スケール)エディタ";
        Size = new Size(600, 600);
        // Feature: UI改善 — 全コントロールが固定座標(Location)配置のため、ウィンドウを縮小すると
        // 保存/キャンセル等のボタンが表示領域外にはみ出してしまう。最小サイズを設計時のサイズに固定する。
        MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // 実際の編集UIを持つPageControlを生成し、フォーム全体を埋めるように配置する。
        _page = new SizeEditorPageControl(imagePath, initialScale) { Dock = DockStyle.Fill };
        // ページ側で「保存」が行われたら、このフォームもOKダイアログ結果として閉じる。
        _page.Saved += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        // ページ側で「キャンセル」されたら、このフォームもCancelダイアログ結果として閉じる。
        _page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        // 作成したPageControlをフォームに追加し、Enter/Escapeキーがそれぞれ
        // ページ側の主要ボタン（保存/キャンセル相当）に対応するようにする。
        Controls.Add(_page);
        AcceptButton = _page.PrimaryActionButton;
        CancelButton = _page.SecondaryActionButton;
    }
}
