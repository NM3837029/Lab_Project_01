using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  EventEditorForm
//  EventTrigger の条件とアクションを編集するフォーム (MZ風UI)
// ───────────────────────────────────────────────
public class EventEditorForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    public EventTrigger ResultTrigger { get; private set; } = null!;

    // ── 条件型 ──────────────────────────────────
    // Feature: UI改善（提案書 EV-2）— コンボボックスには日本語ラベル(Label)を表示しつつ、
    // 保存されるデータ自体は従来通りKey(英語)を使う。ToString()をオーバーライドすることで、
    // DataSource/DisplayMemberのリフレクション（ValueTupleの要素名は実行時に反映されない）に
    // 頼らずシンプルに表示を切り替えている。
    private class ConditionOption
    {
        public string Key = "";
        public string Label = "";
        public override string ToString() => Label;
    }

    private static readonly ConditionOption[] ConditionTypes =
    {
        new() { Key = "PlayerEnter", Label = "プレイヤーが範囲に入ったとき" },
        new() { Key = "PlayerExit", Label = "プレイヤーが範囲から出たとき" },
        new() { Key = "AllEnemiesDefeated", Label = "敵を全滅させたとき" },
        new() { Key = "SwitchOn", Label = "スイッチがONになったとき" },
        new() { Key = "ItemCollected", Label = "アイテムを取得したとき" },
        new() { Key = "TimerExpired", Label = "タイマーが切れたとき" },
    };

    // ── コントロール ────────────────────────────
    private TextBox           _txtId          = null!;
    private NumericUpDown     _nudX           = null!;
    private NumericUpDown     _nudY           = null!;
    private NumericUpDown     _nudW           = null!;
    private NumericUpDown     _nudH           = null!;

    private ComboBox          _cmbCondition   = null!;
    private TextBox           _txtCondParam   = null!;
    private CheckBox          _chkOneShot     = null!;

    private ActionEditorControl _actionEditor = null!;

    private Button            _btnOk          = null!;
    private Button            _btnCancel      = null!;

    // ── コンストラクタ ─────────────────────────
    public EventEditorForm(EventTrigger trigger, AssetDefinitions assets, List<string> stageFiles)
    {
        InitializeComponent();
        _actionEditor.SetContext(assets, stageFiles);
        LoadTrigger(trigger);
    }

    // ── UI 構築 ────────────────────────────────
    private void InitializeComponent()
    {
        Text            = "イベント・トリガー編集";
        Size            = new Size(720, 600);
        Font            = new Font("Meiryo UI", 9f);
        StartPosition   = FormStartPosition.CenterParent;
        MinimizeBox     = false;
        MaximizeBox     = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        int y = 10;

        // ── Trigger ID ──────────────────────────
        AddLabel("トリガー ID:", 10, y);
        _txtId = new TextBox { Location = new Point(110, y), Width = 200 };
        Controls.Add(_txtId);
        y += 30;

        // ── 矩形 X, Y, W, H ─────────────────────
        AddLabel("X:", 10, y);
        _nudX = MakeNud(60, y);

        AddLabel("Y:", 150, y);
        _nudY = MakeNud(200, y);

        AddLabel("Width:", 300, y);
        _nudW = MakeNud(360, y);

        AddLabel("Height:", 470, y);
        _nudH = MakeNud(530, y);

        Controls.AddRange(new Control[] { _nudX, _nudY, _nudW, _nudH });
        y += 36;

        // ── 仕切り線 ────────────────────────────
        AddSeparator(y); y += 14;

        // ── 条件セクション ───────────────────────
        AddLabel("■ 実行条件", 10, y, bold: true); y += 24;

        AddLabel("条件タイプ:", 10, y);
        _cmbCondition = new ComboBox
        {
            Location     = new Point(100, y),
            Width        = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _cmbCondition.Items.AddRange(ConditionTypes);
        Controls.Add(_cmbCondition);

        AddLabel("パラメータ:", 300, y);
        _txtCondParam = new TextBox { Location = new Point(380, y), Width = 200 };
        Controls.Add(_txtCondParam);
        y += 30;

        _chkOneShot = new CheckBox
        {
            Text     = "一度だけ実行 (oneShot)",
            Location = new Point(10, y),
            Width    = 220,
        };
        Controls.Add(_chkOneShot);
        y += 34;

        // ── 仕切り線 ────────────────────────────
        AddSeparator(y); y += 14;

        // ── アクションセクション (MZ風コマンドリスト) ────
        AddLabel("■ 実行内容", 10, y, bold: true); y += 24;

        _actionEditor = new ActionEditorControl { Location = new Point(10, y) };
        Controls.Add(_actionEditor);

        y += _actionEditor.Height + 14;

        // ── 下部ボタン ──────────────────────────
        AddSeparator(y); y += 10;

        _btnOk     = MakeButton("💾 OK",    480, y, 100);
        _btnCancel = MakeButton("キャンセル", 590, y, 100);

        _btnOk.BackColor     = Color.FromArgb(70, 130, 180);
        _btnOk.ForeColor     = Color.White;
        _btnOk.FlatStyle     = FlatStyle.Flat;
        _btnCancel.FlatStyle = FlatStyle.Flat;

        _btnOk.Click     += BtnOk_Click;
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[] { _btnOk, _btnCancel });
    }

    // ── データ連携 ──────────────────────
    private void LoadTrigger(EventTrigger t)
    {
        _txtId.Text     = t.id ?? "";
        _nudX.Value     = (decimal)(t.x);
        _nudY.Value     = (decimal)(t.y);
        _nudW.Value     = (decimal)(t.width);
        _nudH.Value     = (decimal)(t.height);
        _txtCondParam.Text = t.conditionParam ?? "";
        _chkOneShot.Checked = t.oneShot;

        int condIdx = Array.FindIndex(ConditionTypes, o => o.Key == (t.condition ?? ""));
        _cmbCondition.SelectedIndex = condIdx >= 0 ? condIdx : 0;

        _actionEditor.LoadActions(t.actions);
    }

    // Feature: UI改善（提案書 CUT-3）— 保存前チェックをBehaviorScriptEditorFormと同様の考え方で
    // トリガー編集にも広げる。発動しても意味を持たない/正しく判定できない設定を検知して警告する。
    private List<string> ValidateTrigger()
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(_txtId.Text)) warnings.Add("トリガーIDが未入力です。");
        if (_nudW.Value <= 0 || _nudH.Value <= 0) warnings.Add("Width/Heightが0以下です。判定範囲が存在しないため発動しません。");

        var selectedCond = _cmbCondition.SelectedItem as ConditionOption;
        if ((selectedCond?.Key == "SwitchOn" || selectedCond?.Key == "ItemCollected") && string.IsNullOrWhiteSpace(_txtCondParam.Text))
            warnings.Add($"条件「{selectedCond.Label}」にはパラメータの指定が必要です（未入力のままだと正しく判定できません）。");

        if (_actionEditor.GetActions().Count == 0)
            warnings.Add("実行内容（アクション）が1つも設定されていません。トリガーが発動しても何も起きません。");

        return warnings;
    }

    // ── 保存 ──────────────────────────────────
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var warnings = ValidateTrigger();
        if (warnings.Count > 0)
        {
            string msg = "保存前に確認してください:\n\n" + string.Join("\n", warnings) + "\n\nこのまま保存しますか？";
            if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        ResultTrigger = new EventTrigger
        {
            id             = _txtId.Text,
            x              = (float)_nudX.Value,
            y              = (float)_nudY.Value,
            width          = (float)_nudW.Value,
            height         = (float)_nudH.Value,
            condition      = (_cmbCondition.SelectedItem as ConditionOption)?.Key ?? "",
            conditionParam = _txtCondParam.Text,
            oneShot        = _chkOneShot.Checked,
            actions        = _actionEditor.GetActions(),
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── ヘルパー ──────────────────────────────
    private void AddLabel(string text, int x, int y, bool bold = false)
    {
        var lbl = new Label
        {
            Text      = text,
            Location  = new Point(x, y + 3),
            AutoSize  = true,
            Font      = bold
                ? new Font("Meiryo UI", 9f, FontStyle.Bold)
                : new Font("Meiryo UI", 9f),
        };
        Controls.Add(lbl);
    }

    private void AddSeparator(int y)
    {
        var sep = new Panel
        {
            Location  = new Point(10, y),
            Size      = new Size(680, 1),
            BackColor = Color.Silver,
        };
        Controls.Add(sep);
    }

    private NumericUpDown MakeNud(int x, int y) =>
        new NumericUpDown
        {
            Location      = new Point(x, y),
            Width         = 80,
            DecimalPlaces = 2,
            Minimum       = -99999,
            Maximum       = 99999,
            Increment     = 1m,
        };

    private static Button MakeButton(string text, int x, int y, int w) =>
        new Button
        {
            Text     = text,
            Location = new Point(x, y),
            Size     = new Size(w, 30),
            Font     = new Font("Meiryo UI", 9f)
        };
}
