namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はSizeEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new SizeEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class SizeEditorForm : Form
{
    public float ResultScale => _page.ResultScale;

    private readonly SizeEditorPageControl _page;

    public SizeEditorForm(string imagePath, float initialScale)
    {
        Text = "表示サイズ(スケール)エディタ";
        Size = new Size(600, 600);
        // Feature: UI改善 — 全コントロールが固定座標(Location)配置のため、ウィンドウを縮小すると
        // 保存/キャンセル等のボタンが表示領域外にはみ出してしまう。最小サイズを設計時のサイズに固定する。
        MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        _page = new SizeEditorPageControl(imagePath, initialScale) { Dock = DockStyle.Fill };
        _page.Saved += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        _page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_page);
        AcceptButton = _page.PrimaryActionButton;
        CancelButton = _page.SecondaryActionButton;
    }
}
