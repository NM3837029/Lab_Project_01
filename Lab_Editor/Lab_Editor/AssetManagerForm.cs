namespace Lab_Editor;

// UI改善（構造改修フェーズ5b）— 中身はAssetManagerPageControlに抽出済み。
// このクラスは既存の呼び出し元（Form1.cs等）が引き続き `new AssetManagerForm(...).ShowDialog()`
// で使えるよう、PageControlをFillで貼り付けるだけの薄いラッパーとして残している。
// フェーズ5eでWorkbenchShellFormに直接AssetManagerPageControlを載せる配線に切り替わるまでは
// これが唯一の入り口。
public class AssetManagerForm : Form
{
    // assetsPath : アセット定義ファイル（JSON等）へのパス
    // assets     : 読み込み済みのアセット定義データ（敵・ギミック・アイテムなど）
    public AssetManagerForm(string assetsPath, AssetDefinitions assets)
    {
        // ウィンドウのタイトル・サイズ・最小サイズ・表示位置・フォントを設定する。
        Text = "アセット管理エディタ - 敵 / ギミック / アイテム";
        Size = new Size(1160, 720);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // アセット定義ファイルが置かれているフォルダを、プロジェクトルートとして取得する。
        // パーツエディタなど、相対パス解決が必要な子フォームに渡すために使う。
        string projectRoot = Path.GetDirectoryName(assetsPath)!;

        // 実際の編集UIを持つPageControlを生成し、フォーム全体を埋めるように配置する。
        var page = new AssetManagerPageControl(assetsPath, assets) { Dock = DockStyle.Fill };
        // ページ側で「保存」が行われたら、このフォームもOKダイアログ結果として閉じる。
        page.Saved += (s, e) => { DialogResult = DialogResult.OK; Close(); };
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
        // 表示サイズ(スケール)の編集要求 → SizeEditorFormをモーダル表示し、OKなら結果をコールバックで返す。
        page.SizeEditRequested += (fullPath, curScale, onSaved) =>
        {
            using var form = new SizeEditorForm(fullPath, curScale);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultScale);
        };
        // 挙動スクリプトの編集要求 → BehaviorScriptEditorFormをモーダル表示し、OKなら結果をコールバックで返す。
        page.BehaviorScriptEditRequested += (label, initialScript, onSaved) =>
        {
            using var form = new BehaviorScriptEditorForm(label, initialScript);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultScript);
        };
        // パーツ構成の編集要求 → PartsEditorFormをモーダル表示し、OKなら結果をコールバックで返す。
        page.PartsEditRequested += (label, initialParts, baseSpritePath, onSaved) =>
        {
            using var form = new PartsEditorForm(label, initialParts, projectRoot, baseSpritePath);
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultParts);
        };
        // コモンイベントの編集要求 → CommonEventEditorFormをモーダル表示し、OKなら結果をコールバックで返す。
        page.CommonEventEditRequested += (ev, onSaved) =>
        {
            using var form = new CommonEventEditorForm(ev, assets, page.GetStageFileNames());
            if (form.ShowDialog(this) == DialogResult.OK) onSaved(form.ResultEvent);
        };

        // 作成したPageControlをフォームに追加し、Enter/Escapeキーがそれぞれ
        // ページ側の主要ボタン（保存/キャンセル相当）に対応するようにする。
        Controls.Add(page);
        AcceptButton = page.PrimaryActionButton;
        CancelButton = page.SecondaryActionButton;
    }
}
