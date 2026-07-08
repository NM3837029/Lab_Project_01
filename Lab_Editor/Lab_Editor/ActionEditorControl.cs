using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  ActionEditorControl
//  EventTrigger / CommonEventDef が共通で使う「実行内容」編集 UI。
//  RPGツクールMZのプラグインコマンド風に、アクション種別ごとに
//  パラメータ欄の意味とプルダウン候補が自動で切り替わる。
// ───────────────────────────────────────────────
public class ActionEditorControl : Panel
{
    public static readonly string[] ActionTypes =
    {
        "ShowMessage", "ChangeBgm", "PlaySe", "ActivateGimmick",
        "OpenDoor", "SpawnEnemy", "SpawnItem", "SetSwitch",
        "MoveCamera", "StageClear", "GoToStage", "CallCommonEvent"
    };

    private enum ParamKind { None, FreeText, LongText, Bgm, Se, Gimmick, Enemy, Item, Stage, CommonEvent }

    private static readonly Dictionary<string, (string label1, ParamKind kind1, string label2, ParamKind kind2)> ActionMeta = new()
    {
        ["ShowMessage"]      = ("メッセージ:",      ParamKind.LongText, "話者名(任意):", ParamKind.FreeText),
        ["ChangeBgm"]        = ("BGM ID:",          ParamKind.Bgm,      "",              ParamKind.None),
        ["PlaySe"]           = ("SE ID:",           ParamKind.Se,       "",              ParamKind.None),
        ["ActivateGimmick"]  = ("ギミックID:",       ParamKind.Gimmick,  "パラメータ(任意):", ParamKind.FreeText),
        ["OpenDoor"]         = ("ギミックID(扉):",   ParamKind.Gimmick,  "",              ParamKind.None),
        ["SpawnEnemy"]       = ("敵ID:",            ParamKind.Enemy,    "座標 X,Y(任意):", ParamKind.FreeText),
        ["SpawnItem"]        = ("アイテムID:",       ParamKind.Item,     "座標 X,Y(任意):", ParamKind.FreeText),
        ["SetSwitch"]        = ("スイッチID:",       ParamKind.FreeText, "ON/OFF(既定ON):", ParamKind.FreeText),
        ["MoveCamera"]       = ("X座標:",           ParamKind.FreeText, "Y座標:",         ParamKind.FreeText),
        ["StageClear"]       = ("(パラメータなし)",  ParamKind.None,     "",              ParamKind.None),
        ["GoToStage"]        = ("ステージファイル:", ParamKind.Stage,    "",              ParamKind.None),
        ["CallCommonEvent"]  = ("コモンイベントID:", ParamKind.CommonEvent, "",           ParamKind.None),
    };

    private ListBox       _lstActions    = null!;
    private Button        _btnAddAction  = null!;
    private Button        _btnDelAction  = null!;

    private Panel         _pnlParams     = null!;
    private ComboBox      _cmbActionType = null!;
    private Label         _lblP1         = null!;
    private Label         _lblP2         = null!;
    private ComboBox      _cmbParam1     = null!;
    private Button        _btnEditText   = null!;
    private TextBox       _txtParam2     = null!;
    private NumericUpDown _nudDelay      = null!;

    private List<EventActionEntry> _actions = new();
    private bool _isUpdatingParams = false;

    private AssetDefinitions? _assets;
    private List<string> _stageFiles = new();

    public ActionEditorControl()
    {
        BuildUI();
    }

    /// <summary>プルダウン候補生成に必要な参照データを設定する</summary>
    public void SetContext(AssetDefinitions assets, List<string> stageFiles)
    {
        _assets = assets;
        _stageFiles = stageFiles;
        // 現在選択中の行があれば候補を更新
        if (_cmbActionType.SelectedItem is string act) RefreshParam1Choices(act);
    }

    public void LoadActions(List<EventActionEntry>? actions)
    {
        _actions.Clear();
        _lstActions.Items.Clear();
        foreach (var act in actions ?? new List<EventActionEntry>())
            AddActionRow(new EventActionEntry { action = act.action, param1 = act.param1, param2 = act.param2, delay = act.delay });

        if (_lstActions.Items.Count > 0) _lstActions.SelectedIndex = 0;
        else _pnlParams.Enabled = false;
    }

    public List<EventActionEntry> GetActions() => new(_actions);

    // ── UI 構築 ────────────────────────────────
    private void BuildUI()
    {
        Size = new Size(680, 210);

        _lstActions = new ListBox
        {
            Location = new Point(0, 0),
            Size = new Size(380, 210),
            Font = new Font("Meiryo UI", 10f)
        };
        _lstActions.SelectedIndexChanged += LstActions_SelectedIndexChanged;

        _pnlParams = new Panel
        {
            Location = new Point(390, 0),
            Size = new Size(290, 210),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 245, 250)
        };

        var lblTitle = new Label { Text = "コマンド詳細", Location = new Point(10, 8), Font = new Font("Meiryo UI", 9f, FontStyle.Bold), AutoSize = true };

        var lblAct = new Label { Text = "アクション:", Location = new Point(10, 38), AutoSize = true };
        _cmbActionType = new ComboBox { Location = new Point(90, 36), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbActionType.Items.AddRange(ActionTypes);
        _cmbActionType.SelectedIndexChanged += ActionType_Changed;

        _lblP1 = new Label { Text = "Param 1:", Location = new Point(10, 73), AutoSize = true };
        _cmbParam1 = new ComboBox { Location = new Point(10, 90), Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
        _cmbParam1.TextChanged += Param_Changed;
        _btnEditText = new Button { Text = "✎", Location = new Point(216, 89), Size = new Size(28, 24), Visible = false };
        _btnEditText.Click += BtnEditText_Click;

        _lblP2 = new Label { Text = "Param 2:", Location = new Point(10, 122), AutoSize = true };
        _txtParam2 = new TextBox { Location = new Point(10, 139), Width = 234 };
        _txtParam2.TextChanged += Param_Changed;

        var lblDly = new Label { Text = "Delay(秒):", Location = new Point(10, 172), AutoSize = true };
        _nudDelay = new NumericUpDown { Location = new Point(90, 170), Width = 90, DecimalPlaces = 2, Increment = 0.1m, Maximum = 100 };
        _nudDelay.ValueChanged += Param_Changed;

        _pnlParams.Controls.AddRange(new Control[]
        {
            lblTitle, lblAct, _cmbActionType,
            _lblP1, _cmbParam1, _btnEditText,
            _lblP2, _txtParam2,
            lblDly, _nudDelay
        });
        _pnlParams.Enabled = false;

        _btnAddAction = new Button { Text = "＋追加", Location = new Point(0, 216), Size = new Size(100, 30) };
        _btnDelAction = new Button { Text = "🗑 削除", Location = new Point(110, 216), Size = new Size(100, 30) };
        _btnAddAction.Click += (_, _) => AddActionRow(new EventActionEntry());
        _btnDelAction.Click += BtnDelAction_Click;

        Controls.AddRange(new Control[] { _lstActions, _pnlParams, _btnAddAction, _btnDelAction });
        Height = 250;
    }

    // ── 行操作 ────────────────────────────────
    private void AddActionRow(EventActionEntry act)
    {
        if (!ActionTypes.Contains(act.action)) act.action = ActionTypes[0];
        _actions.Add(act);
        _lstActions.Items.Add(GetActionDisplayText(act));
        _lstActions.SelectedIndex = _lstActions.Items.Count - 1;
    }

    private string GetActionDisplayText(EventActionEntry act)
    {
        string p = "";
        if (!string.IsNullOrEmpty(act.param1)) p += $" {Truncate(act.param1, 24)}";
        if (!string.IsNullOrEmpty(act.param2)) p += $", {act.param2}";
        if (act.delay > 0) p += $" [Delay: {act.delay}s]";
        return $"◆ {act.action}{p}";
    }

    private static string Truncate(string s, int len) => s.Length <= len ? s : s.Substring(0, len) + "…";

    private void LstActions_SelectedIndexChanged(object? sender, EventArgs e)
    {
        int idx = _lstActions.SelectedIndex;
        if (idx >= 0 && idx < _actions.Count)
        {
            _pnlParams.Enabled = true;
            _isUpdatingParams = true;

            var act = _actions[idx];
            int aIdx = Array.IndexOf(ActionTypes, act.action);
            _cmbActionType.SelectedIndex = aIdx >= 0 ? aIdx : 0;
            RefreshParam1Choices(act.action);
            _cmbParam1.Text = act.param1;
            _txtParam2.Text = act.param2;
            _nudDelay.Value = (decimal)act.delay;

            ApplyFieldMeta(act.action);
            _isUpdatingParams = false;
        }
        else
        {
            _pnlParams.Enabled = false;
        }
    }

    private void ActionType_Changed(object? sender, EventArgs e)
    {
        if (_cmbActionType.SelectedItem is not string action) return;
        RefreshParam1Choices(action);
        ApplyFieldMeta(action);
        Param_Changed(sender, e);
    }

    // アクション種別に応じてラベル文言・有効/無効・✎ボタンの表示を切り替える（MZのプラグインコマンドUI風）
    private void ApplyFieldMeta(string action)
    {
        if (!ActionMeta.TryGetValue(action, out var meta)) meta = ("Param 1:", ParamKind.FreeText, "Param 2:", ParamKind.FreeText);

        _lblP1.Text = meta.label1;
        _cmbParam1.Enabled = meta.kind1 != ParamKind.None;
        _btnEditText.Visible = meta.kind1 == ParamKind.LongText;

        bool hasP2 = meta.kind2 != ParamKind.None;
        _lblP2.Text = meta.label2;
        _lblP2.Visible = hasP2;
        _txtParam2.Visible = hasP2;
    }

    // BGM/SE/敵/ギミック/アイテム/ステージ/コモンイベントのID候補をプルダウンに反映
    private void RefreshParam1Choices(string action)
    {
        if (!ActionMeta.TryGetValue(action, out var meta)) return;
        string current = _cmbParam1.Text;
        _cmbParam1.Items.Clear();

        IEnumerable<string>? ids = meta.kind1 switch
        {
            ParamKind.Bgm => _assets?.Bgm.Select(x => x.id),
            ParamKind.Se => _assets?.Se.Select(x => x.id).Concat(_assets.UiSe.Select(x => x.id)),
            ParamKind.Gimmick => _assets?.Gimmicks.Select(x => x.id),
            ParamKind.Enemy => _assets?.Enemies.Select(x => x.id),
            ParamKind.Item => _assets?.Items.Select(x => x.id),
            ParamKind.Stage => _stageFiles,
            ParamKind.CommonEvent => _assets?.CommonEvents.Select(x => x.id),
            _ => null
        };
        if (ids != null) _cmbParam1.Items.AddRange(ids.ToArray<object>());
        _cmbParam1.Text = current;
    }

    private void BtnEditText_Click(object? sender, EventArgs e)
    {
        using var dlg = new LongTextEditForm(_cmbParam1.Text);
        if (dlg.ShowDialog() == DialogResult.OK)
            _cmbParam1.Text = dlg.ResultText;
    }

    private void Param_Changed(object? sender, EventArgs e)
    {
        if (_isUpdatingParams || _lstActions.SelectedIndex < 0) return;

        int idx = _lstActions.SelectedIndex;
        var act = _actions[idx];

        act.action = _cmbActionType.SelectedItem?.ToString() ?? ActionTypes[0];
        act.param1 = _cmbParam1.Text;
        act.param2 = _txtParam2.Text;
        act.delay = (float)_nudDelay.Value;

        _lstActions.Items[idx] = GetActionDisplayText(act);
    }

    private void BtnDelAction_Click(object? sender, EventArgs e)
    {
        int idx = _lstActions.SelectedIndex;
        if (idx < 0) return;
        _actions.RemoveAt(idx);
        _lstActions.Items.RemoveAt(idx);
        if (_lstActions.Items.Count > 0)
            _lstActions.SelectedIndex = Math.Min(idx, _lstActions.Items.Count - 1);
        else
            _pnlParams.Enabled = false;
    }
}

// 長文（メッセージ等）を快適に編集するための簡易ダイアログ
internal class LongTextEditForm : Form
{
    public string ResultText { get; private set; }

    public LongTextEditForm(string initial)
    {
        ResultText = initial;
        Text = "メッセージ編集";
        Size = new Size(420, 300);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var txt = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(10, 10),
            Size = new Size(384, 200),
            Text = initial,
            Font = new Font("Meiryo UI", 10f)
        };

        var btnOk = new Button { Text = "OK", Location = new Point(214, 220), Size = new Size(90, 30), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "キャンセル", Location = new Point(310, 220), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) => { ResultText = txt.Text; };

        Controls.AddRange(new Control[] { txt, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
