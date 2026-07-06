using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  EventEditorForm
//  EventTrigger の条件とアクションを編集するフォーム
// ───────────────────────────────────────────────
public class EventEditorForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    public EventTrigger ResultTrigger { get; private set; } = null!;

    // ── 条件型・アクション型 ─────────────────────
    private static readonly string[] ConditionTypes =
    {
        "PlayerEnter", "PlayerExit", "AllEnemiesDefeated",
        "SwitchOn", "ItemCollected", "TimerExpired"
    };

    private static readonly string[] ActionTypes =
    {
        "ShowMessage", "ChangeBgm", "PlaySe", "ActivateGimmick",
        "OpenDoor", "SpawnEnemy", "SpawnItem",
        "MoveCamera", "StageClear", "GoToStage"
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

    private DataGridView      _gridActions    = null!;

    private Button            _btnAddAction   = null!;
    private Button            _btnDelAction   = null!;
    private Button            _btnOk          = null!;
    private Button            _btnCancel      = null!;

    private readonly EventTrigger _original;

    // ── コンストラクタ ─────────────────────────
    public EventEditorForm(EventTrigger trigger)
    {
        // コピーを編集する
        _original = trigger;
        InitializeComponent();
        LoadTrigger(trigger);
    }

    // ── UI 構築 ────────────────────────────────
    private void InitializeComponent()
    {
        Text            = "イベント・トリガー編集";
        Size            = new Size(720, 560);
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
        AddLabel("■ 条件", 10, y, bold: true); y += 24;

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

        // ── アクションセクション ──────────────────
        AddLabel("■ アクション", 10, y, bold: true); y += 24;

        _gridActions = BuildActionGrid();
        _gridActions.Location = new Point(10, y);
        _gridActions.Size     = new Size(688, 200);
        Controls.Add(_gridActions);
        y += 210;

        _btnAddAction = MakeButton("＋アクション追加", 10,  y, 150);
        _btnDelAction = MakeButton("🗑 選択行削除",   168, y, 130);
        _btnAddAction.Click += (_, _) => AddActionRow();
        _btnDelAction.Click += BtnDelAction_Click;
        Controls.AddRange(new Control[] { _btnAddAction, _btnDelAction });
        y += 38;

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

    // ── アクション DataGridView 生成 ─────────────
    private DataGridView BuildActionGrid()
    {
        var grid = new DataGridView
        {
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            RowHeadersWidth       = 30,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect           = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight   = 28,
        };
        grid.DataError += Grid_DataError;

        // action 列（ComboBox）
        var colAction = new DataGridViewComboBoxColumn
        {
            Name = "colAction", HeaderText = "action", Width = 140
        };
        colAction.Items.AddRange(ActionTypes);
        grid.Columns.Add(colAction);

        // param1 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colParam1", HeaderText = "param1", Width = 160
        });

        // param2 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colParam2", HeaderText = "param2", Width = 160
        });

        // delay 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colDelay", HeaderText = "delay", Width = 70,
            ValueType = typeof(float)
        });

        return grid;
    }

    // ── トリガー読み込み ──────────────────────
    private void LoadTrigger(EventTrigger t)
    {
        _txtId.Text     = t.id ?? "";
        _nudX.Value     = (decimal)(t.x);
        _nudY.Value     = (decimal)(t.y);
        _nudW.Value     = (decimal)(t.width);
        _nudH.Value     = (decimal)(t.height);
        _txtCondParam.Text = t.conditionParam ?? "";
        _chkOneShot.Checked = t.oneShot;

        int condIdx = Array.IndexOf(ConditionTypes, t.condition ?? "");
        _cmbCondition.SelectedIndex = condIdx >= 0 ? condIdx : 0;

        _gridActions.Rows.Clear();
        foreach (var act in t.actions ?? new List<EventActionEntry>())
            AddActionRow(act);
    }

    private void AddActionRow(EventActionEntry? act = null)
    {
        int idx = _gridActions.Rows.Add();
        var row  = _gridActions.Rows[idx];

        string actionVal = act?.action ?? ActionTypes[0];
        if (!ActionTypes.Contains(actionVal)) 
        {
            System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\warning_log.txt", $"[WARNING] EventEditorForm: Action '{actionVal}' is not valid. Auto-converted to default.\n");
            actionVal = ActionTypes[0];
        }
        row.Cells["colAction"].Value = actionVal;
        row.Cells["colParam1"].Value = act?.param1 ?? "";
        row.Cells["colParam2"].Value = act?.param2 ?? "";
        row.Cells["colDelay"].Value  = act?.delay  ?? 0f;
    }

    private void Grid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        string colName = _gridActions.Columns[e.ColumnIndex].Name;
        object val = _gridActions.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        string items = "";
        int dsCount = 0;
        if (_gridActions.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn cb)
        {
            var ds = cb.Items;
            if (ds != null && ds.Count > 0) {
                var list = new List<string>();
                foreach(var item in ds) list.Add(item.ToString() ?? "");
                items = string.Join(", ", list);
                dsCount = ds.Count;
            }
        }
        string msg = $@"[DataGridViewComboBoxCell Error]
Form: {this.Name}
DataGridView: {_gridActions.Name}
Row: {e.RowIndex}
Col: {e.ColumnIndex}
Column.Name: {colName}
Column.HeaderText: {_gridActions.Columns[e.ColumnIndex].HeaderText}
Cell.Value: '{val}'
Cell.FormattedValue: '{_gridActions.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue}'
Cell.ValueType: {_gridActions.Rows[e.RowIndex].Cells[e.ColumnIndex].ValueType}
ComboBox.Items: [{items}]
ComboBox.DataSourceCount: {dsCount}
ValueMember: {(_gridActions.Columns[e.ColumnIndex] as DataGridViewComboBoxColumn)?.ValueMember}
DisplayMember: {(_gridActions.Columns[e.ColumnIndex] as DataGridViewComboBoxColumn)?.DisplayMember}
Exception.Message: {e.Exception.Message}
Exception.StackTrace: {e.Exception.StackTrace}";

        System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\error_detail.log", msg + "\n\n");
        throw new Exception(msg, e.Exception);
    }

    // ── アクション削除 ────────────────────────
    private void BtnDelAction_Click(object? sender, EventArgs e)
    {
        if (_gridActions.SelectedRows.Count == 0) return;
        int idx = _gridActions.SelectedRows[0].Index;
        if (idx >= 0) _gridActions.Rows.RemoveAt(idx);
    }

    // ── 保存 ──────────────────────────────────
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var actions = new List<EventActionEntry>();
        foreach (DataGridViewRow row in _gridActions.Rows)
        {
            if (row.IsNewRow) continue;
            float delay = float.TryParse(row.Cells["colDelay"].Value?.ToString(), out float d) ? d : 0f;
            actions.Add(new EventActionEntry
            {
                action = row.Cells["colAction"].Value?.ToString() ?? "",
                param1 = row.Cells["colParam1"].Value?.ToString() ?? "",
                param2 = row.Cells["colParam2"].Value?.ToString() ?? "",
                delay  = delay,
            });
        }

        ResultTrigger = new EventTrigger
        {
            id             = _txtId.Text,
            x              = (float)_nudX.Value,
            y              = (float)_nudY.Value,
            width          = (float)_nudW.Value,
            height         = (float)_nudH.Value,
            condition      = _cmbCondition.SelectedItem?.ToString() ?? "",
            conditionParam = _txtCondParam.Text,
            oneShot        = _chkOneShot.Checked,
            actions        = actions,
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
