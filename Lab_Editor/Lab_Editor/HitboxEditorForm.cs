namespace Lab_Editor;

// UI改善（構造改修フェーズ5d）— 中身はHitboxEditorPageControlに抽出済み。
// 既存の呼び出し元が引き続き `new HitboxEditorForm(...).ShowDialog()` で使えるよう、
// PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
public class HitboxEditorForm : Form
{
    // 以下4つは、編集結果（確定した当たり判定の座標・サイズ）をPageControlから
    // そのまま透過的に取得するためのプロパティ。
    public int HitboxOffsetX => _page.HitboxOffsetX;
    public int HitboxOffsetY => _page.HitboxOffsetY;
    public int HitboxWidth => _page.HitboxWidth;
    public int HitboxHeight => _page.HitboxHeight;

    // 実際の編集UIを持つPageControlへの参照。上記プロパティの委譲元として保持する。
    private readonly HitboxEditorPageControl _page;

    // imagePath : 当たり判定を設定する対象の画像ファイルパス
    // ox, oy    : 編集開始時点での当たり判定オフセット（X, Y）
    // w, h      : 編集開始時点での当たり判定サイズ（幅, 高さ）
    public HitboxEditorForm(string imagePath, int ox, int oy, int w, int h)
    {
        // ウィンドウのタイトル・サイズを設定する。
        Text = "当たり判定(Hitbox)エディタ";
        Size = new Size(600, 600);
        // Feature: UI改善 — 全コントロールが固定座標(Location)配置のため、ウィンドウを縮小すると
        // 保存/キャンセルボタンが表示領域外にはみ出してしまう。最小サイズを設計時のサイズに固定し、
        // それより縮小できないようにすることではみ出しを防ぐ。
        MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // 実際の編集UIを持つPageControlを生成し、フォーム全体を埋めるように配置する。
        _page = new HitboxEditorPageControl(imagePath, ox, oy, w, h) { Dock = DockStyle.Fill };
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
