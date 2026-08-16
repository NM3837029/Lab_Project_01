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
    public event EventHandler<CommonEventDef>? Saved;
    public event EventHandler? Cancelled;
    public Button PrimaryActionButton => _btnOk;
    public Button SecondaryActionButton => _btnCancel;

    public CommonEventDef ResultEvent { get; private set; } = null!;

    private TextBox _txtId   = null!;
    private TextBox _txtName = null!;
    private ActionEditorControl _actionEditor = null!;
    private Button _btnOk = null!, _btnCancel = null!;

    public CommonEventEditorPageControl(CommonEventDef ev, AssetDefinitions assets, List<string> stageFiles)
    {
        InitializeComponent();
        _actionEditor.SetContext(assets, stageFiles);
        LoadEvent(ev);
    }

    private void InitializeComponent()
    {
        Dock            = DockStyle.Fill;
        Font            = UiTheme.Base;

        int y = 10;

        var lblId = new Label { Text = "コモンイベントID:", Location = new Point(10, y + 3), AutoSize = true };
        _txtId = new TextBox { Location = new Point(140, y), Width = 160 };

        var lblName = new Label { Text = "名前:", Location = new Point(320, y + 3), AutoSize = true };
        _txtName = new TextBox { Location = new Point(360, y), Width = 320 };

        Controls.AddRange(new Control[] { lblId, _txtId, lblName, _txtName });
        y += 34;

        Controls.Add(UiTheme.CreateSeparator(new Point(10, y), 680));
        y += 12;

        var lblActions = new Label { Text = "■ 実行内容", Location = new Point(10, y), AutoSize = true, Font = UiTheme.Bold };
        Controls.Add(lblActions);
        y += 24;

        _actionEditor = new ActionEditorControl { Location = new Point(10, y) };
        Controls.Add(_actionEditor);
        y += _actionEditor.Height + 14;

        _btnOk     = new Button { Text = "💾 OK", Location = new Point(480, y), Size = new Size(100, 30) };
        _btnCancel = new Button { Text = "キャンセル", Location = new Point(590, y), Size = new Size(100, 30) };
        UiTheme.StylePrimaryButton(_btnOk);
        UiTheme.StyleSecondaryButton(_btnCancel);
        _btnOk.Click     += BtnOk_Click;
        _btnCancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        Controls.AddRange(new Control[] { _btnOk, _btnCancel });
    }

    private void LoadEvent(CommonEventDef ev)
    {
        _txtId.Text = ev.id;
        _txtName.Text = ev.name;
        _actionEditor.LoadActions(ev.actions);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtId.Text))
        {
            MessageBox.Show("コモンイベントIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // Feature: UI改善（提案書 CUT-3）
        if (_actionEditor.GetActions().Count == 0)
        {
            if (MessageBox.Show("実行内容が1つも設定されていません。呼び出しても何も起きません。\n\nこのまま保存しますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        ResultEvent = new CommonEventDef
        {
            id = _txtId.Text.Trim(),
            name = _txtName.Text,
            actions = _actionEditor.GetActions()
        };
        Saved?.Invoke(this, ResultEvent);
    }
}
