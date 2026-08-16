namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はHitboxEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new HitboxEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class HitboxEditorForm : Form
{
    public int HitboxOffsetX => _page.HitboxOffsetX;
    public int HitboxOffsetY => _page.HitboxOffsetY;
    public int HitboxWidth => _page.HitboxWidth;
    public int HitboxHeight => _page.HitboxHeight;

    private readonly HitboxEditorPageControl _page;

    public HitboxEditorForm(string imagePath, int ox, int oy, int w, int h)
    {
        Text = "当たり判定(Hitbox)エディタ";
        Size = new Size(600, 600);
        // Feature: UI改善 — 全コントロールが固定座標(Location)配置のため、ウィンドウを縮小すると
        // 保存/キャンセルボタンが表示領域外にはみ出してしまう。最小サイズを設計時のサイズに固定し、
        // それより縮小できないようにすることではみ出しを防ぐ。
        MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        _page = new HitboxEditorPageControl(imagePath, ox, oy, w, h) { Dock = DockStyle.Fill };
        _page.Saved += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        _page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_page);
        AcceptButton = _page.PrimaryActionButton;
        CancelButton = _page.SecondaryActionButton;
    }
}
