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
    // ダイアログがOKで閉じられたときに、確定したパーツ構成一覧をここに格納する。
    public List<PartDef> ResultParts { get; private set; } = new();

    // subjectLabel   : 編集対象を示すラベル文字列（タイトル表示用）
    // initialParts   : 編集開始時点でのパーツ構成一覧
    // projectRoot    : 画像パスなどを相対解決するためのプロジェクトルートパス
    // baseSpritePath : ベースとなるスプライト画像のパス
    public PartsEditorForm(string subjectLabel, List<PartDef> initialParts, string projectRoot, string baseSpritePath)
    {
        // ウィンドウのタイトル・サイズ・最小サイズ・表示位置・フォントを設定する。
        Text = $"🧩 パーツエディタ - {subjectLabel}";
        Size = new Size(1300, 820);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // 実際の編集UIを持つPageControlを生成し、フォーム全体を埋めるように配置する。
        var page = new PartsEditorPageControl(subjectLabel, initialParts, projectRoot, baseSpritePath) { Dock = DockStyle.Fill };
        // ページ側で「保存」が行われたら、確定したパーツ一覧を受け取ってフォームをOKで閉じる。
        page.Saved += (s, parts) => { ResultParts = parts; DialogResult = DialogResult.OK; Close(); };
        // ページ側で「キャンセル」されたら、このフォームもCancelダイアログ結果として閉じる。
        page.Cancelled += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        // ドリルダウン系の編集要求は、モーダルFormをShowDialog()するという従来通りの動作で応じる
        // （WorkbenchShellForm経由でこのページを直接ホストする場合は、Form1側が別の応じ方を配線する）。
        // 当たり判定(Hitbox)の編集要求 → HitboxEditorFormをモーダル表示し、OKなら結果をコールバックで返す。
        page.HitboxEditRequested += (fullPath, ox, oy, w, h, onSaved) =>
        {
            using var form = new HitboxEditorForm(fullPath, ox, oy, w, h);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.HitboxOffsetX, form.HitboxOffsetY, form.HitboxWidth, form.HitboxHeight);
        };
        // 挙動スクリプトの編集要求 → BehaviorScriptEditorFormをモーダル表示し、OKなら結果をコールバックで返す。
        page.BehaviorScriptEditRequested += (label, initialScript, onSaved) =>
        {
            using var form = new BehaviorScriptEditorForm(label, initialScript);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultScript);
        };

        // 作成したPageControlをフォームに追加し、Enter/Escapeキーがそれぞれ
        // ページ側の主要ボタン（保存/キャンセル相当）に対応するようにする。
        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
