using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  CommonEventEditorForm
//  RPGツクールMZの「コモンイベント」相当：ID/名前 + 実行内容(アクション列)を編集する。
//  複数のトリガーから CallCommonEvent アクションで呼び出される。
// ───────────────────────────────────────────────
public class CommonEventEditorForm : Form
{
    public CommonEventDef ResultEvent { get; private set; } = null!;

    private TextBox _txtId   = null!;
    private TextBox _txtName = null!;
    private ActionEditorControl _actionEditor = null!;
    private Button _btnOk = null!, _btnCancel = null!;

    public CommonEventEditorForm(CommonEventDef ev, AssetDefinitions assets, List<string> stageFiles)
    {
        InitializeComponent();
        _actionEditor.SetContext(assets, stageFiles);
        LoadEvent(ev);
    }

    private void InitializeComponent()
    {
        Text            = "コモンイベント編集";
        Size            = new Size(720, 380);
        Font            = new Font("Meiryo UI", 9f);
        StartPosition   = FormStartPosition.CenterParent;
        MinimizeBox     = false;
        MaximizeBox     = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        int y = 10;

        var lblId = new Label { Text = "コモンイベントID:", Location = new Point(10, y + 3), AutoSize = true };
        _txtId = new TextBox { Location = new Point(140, y), Width = 160 };

        var lblName = new Label { Text = "名前:", Location = new Point(320, y + 3), AutoSize = true };
        _txtName = new TextBox { Location = new Point(360, y), Width = 320 };

        Controls.AddRange(new Control[] { lblId, _txtId, lblName, _txtName });
        y += 34;

        var sep = new Panel { Location = new Point(10, y), Size = new Size(680, 1), BackColor = Color.Silver };
        Controls.Add(sep);
        y += 12;

        var lblActions = new Label { Text = "■ 実行内容", Location = new Point(10, y), AutoSize = true, Font = new Font("Meiryo UI", 9f, FontStyle.Bold) };
        Controls.Add(lblActions);
        y += 24;

        _actionEditor = new ActionEditorControl { Location = new Point(10, y) };
        Controls.Add(_actionEditor);
        y += _actionEditor.Height + 14;

        _btnOk     = new Button { Text = "💾 OK", Location = new Point(480, y), Size = new Size(100, 30), BackColor = Color.FromArgb(70, 130, 180), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnCancel = new Button { Text = "キャンセル", Location = new Point(590, y), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat };
        _btnOk.Click     += BtnOk_Click;
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
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
        DialogResult = DialogResult.OK;
        Close();
    }
}
