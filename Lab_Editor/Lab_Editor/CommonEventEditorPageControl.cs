using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  CommonEventEditorPageControl
//  RPGツクールMZの「コモンイベント」相当：ID/名前 + 実行内容(アクション列)を編集する。
//  複数のトリガーから CallCommonEvent アクションで呼び出される。
//  構造改修フェーズ5dでForm(CommonEventEditorForm)からUserControlへ抽出。
// ───────────────────────────────────────────────
public class CommonEventEditorPageControl : UserControl
{
    // 保存ボタンが押されたとき、確定したCommonEventDefを添えて発生するイベント
    public event EventHandler<CommonEventDef>? Saved;
    // キャンセルボタンが押されたときに発生するイベント
    public event EventHandler? Cancelled;
    // シェル側がAcceptButtonとして割り当てるための、OKボタンへの参照
    public Button PrimaryActionButton => _btnOk;
    // シェル側がCancelButtonとして割り当てるための、キャンセルボタンへの参照
    public Button SecondaryActionButton => _btnCancel;

    // 保存確定後の結果として呼び出し元へ渡すコモンイベント定義
    public CommonEventDef ResultEvent { get; private set; } = null!;

    // コモンイベントIDを入力するテキストボックス
    private TextBox _txtId   = null!;
    // コモンイベント名を入力するテキストボックス
    private TextBox _txtName = null!;
    // 実行内容（アクション列）を編集するための共通コントロール
    private ActionEditorControl _actionEditor = null!;
    // OK／キャンセルの各ボタン
    private Button _btnOk = null!, _btnCancel = null!;

    // コンストラクタ。
    // ev         : 編集対象となる既存のコモンイベント定義（新規作成の場合も空の定義が渡される）
    // assets     : アクション編集で参照するアセット一覧（敵/ギミック/アイテム等の選択肢に使う）
    // stageFiles : アクション編集で参照するステージファイル一覧（「ステージ切替」等の選択肢に使う）
    public CommonEventEditorPageControl(CommonEventDef ev, AssetDefinitions assets, List<string> stageFiles)
    {
        InitializeComponent();
        // アクションエディタに、選択肢として使うアセット・ステージ一覧を渡す
        _actionEditor.SetContext(assets, stageFiles);
        // 渡された既存データを画面に反映する
        LoadEvent(ev);
    }

    // 画面上の各コントロールを生成・配置する初期化処理。
    private void InitializeComponent()
    {
        Dock            = DockStyle.Fill;
        Font            = UiTheme.Base;

        // 各行の縦方向の配置に使う現在のY座標（下に向かって積み上げていく）
        int y = 10;

        // コモンイベントIDの入力欄
        var lblId = new Label { Text = "コモンイベントID:", Location = new Point(10, y + 3), AutoSize = true };
        _txtId = new TextBox { Location = new Point(140, y), Width = 160 };

        // コモンイベント名の入力欄
        var lblName = new Label { Text = "名前:", Location = new Point(320, y + 3), AutoSize = true };
        _txtName = new TextBox { Location = new Point(360, y), Width = 320 };

        Controls.AddRange(new Control[] { lblId, _txtId, lblName, _txtName });
        y += 34;

        // ID/名前欄と実行内容欄を区切る仕切り線
        Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 680));
        y += 12;

        // 「実行内容」セクションの見出し
        var lblActions = new Label { Text = "■ 実行内容", Location = new Point(10, y), AutoSize = true, Font = UiTheme.Bold };
        Controls.Add(lblActions);
        y += 24;

        // アクション列（実行内容）を編集するエディタ本体
        _actionEditor = new ActionEditorControl { Location = new Point(10, y) };
        Controls.Add(_actionEditor);
        y += _actionEditor.Height + 14;

        // OK／キャンセルの各ボタンを配置し、共通テーマの装飾を適用する
        _btnOk     = new Button { Text = "💾 OK", Location = new Point(480, y), Size = new Size(100, 30) };
        _btnCancel = new Button { Text = "キャンセル", Location = new Point(590, y), Size = new Size(100, 30) };
        UiTheme.StylePrimaryButton(_btnOk);
        UiTheme.StyleSecondaryButton(_btnCancel);
        _btnOk.Click     += BtnOk_Click;
        _btnCancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        Controls.AddRange(new Control[] { _btnOk, _btnCancel });
    }

    // 引数で渡された既存のコモンイベント定義を各入力欄に反映する。
    private void LoadEvent(CommonEventDef ev)
    {
        _txtId.Text = ev.id;
        _txtName.Text = ev.name;
        _actionEditor.LoadActions(ev.actions);
    }

    // OKボタン押下時の処理。入力内容を検証してから、結果を確定してSavedイベントを発火する。
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        // IDが未入力の場合は保存させず、警告メッセージを表示して処理を中断する
        if (string.IsNullOrWhiteSpace(_txtId.Text))
        {
            MessageBox.Show("コモンイベントIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // Feature: UI改善（提案書 CUT-3）
        // 実行内容が1つも設定されていない場合、呼び出しても何も起きない「空のイベント」になってしまうため、
        // ユーザーに確認を取ってから保存する（意図的な仮登録の可能性もあるため保存自体は禁止しない）。
        if (_actionEditor.GetActions().Count == 0)
        {
            if (MessageBox.Show("実行内容が1つも設定されていません。呼び出しても何も起きません。\n\nこのまま保存しますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        // 入力内容から最終的なコモンイベント定義を組み立てて、呼び出し元へ通知する
        ResultEvent = new CommonEventDef
        {
            id = _txtId.Text.Trim(),
            name = _txtName.Text,
            actions = _actionEditor.GetActions()
        };
        Saved?.Invoke(this, ResultEvent);
    }
}
