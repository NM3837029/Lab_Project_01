using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Lab_Editor;

// ======================================================
// SoundAssignmentForm - サウンドカタログのIDを敵/ギミック/アイテム/ステージへ割り当てる専用画面
// Feature: サウンド・アセット管理の刷新
//
// SoundManagerForm(カタログ管理)では音声ファイルをID登録するだけで、そのIDを実際に
// 「敵のダメージ音」等として割り当てるUIがどこにも無かった。このフォームはその欠落を埋める。
// 敵/ギミック/アイテムはclone-and-return方式（PartsEditorForm等と同じ）で編集する。
// ======================================================
public class SoundAssignmentForm : Form
{
    public List<EnemyDef> ResultEnemies { get; private set; } = new();
    public List<GimmickDef> ResultGimmicks { get; private set; } = new();
    public List<ItemDef> ResultItems { get; private set; } = new();
    // ステージが読み込まれていない場合はnullのまま（呼び出し側はBgmIdを一切更新しない）
    public string? ResultStageBgmId { get; private set; }

    private List<EnemyDef> _enemies;
    private List<GimmickDef> _gimmicks;
    private List<ItemDef> _items;
    private readonly string? _currentStageName;

    private DataGridView _gridEnemy = null!, _gridGimmick = null!, _gridItem = null!;
    private ComboBox _cmbStageBgm = null!;
    private Panel _sectionEnemy = null!, _sectionGimmick = null!, _sectionItem = null!, _sectionStage = null!;

    private readonly HistoryManager<Snapshot> _history = new();
    private Button _btnUndo = null!, _btnRedo = null!;

    private class Snapshot
    {
        public List<EnemyDef> Enemies { get; set; } = new();
        public List<GimmickDef> Gimmicks { get; set; } = new();
        public List<ItemDef> Items { get; set; } = new();
        public string StageBgmId { get; set; } = "";
    }

    public SoundAssignmentForm(List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items,
        List<SoundDef> se, List<SoundDef> uiSe, List<SoundDef> bgm,
        string? currentStageName, string currentStageBgmId)
    {
        _enemies = CloneList(enemies);
        _gimmicks = CloneList(gimmicks);
        _items = CloneList(items);
        _currentStageName = currentStageName;

        string[] seChoices = new[] { "" }.Concat(se.Select(s => s.id)).Concat(uiSe.Select(s => s.id)).ToArray();
        string[] bgmChoices = new[] { "" }.Concat(bgm.Select(b => b.id)).ToArray();

        InitUI(seChoices, bgmChoices, currentStageBgmId);
        PushHistory();
    }

    private static List<T> CloneList<T>(List<T> src) =>
        JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(src)) ?? new List<T>();

    // ── UI 構築 ────────────────────────────────
    private void InitUI(string[] seChoices, string[] bgmChoices, string currentStageBgmId)
    {
        Text = "🔊 サウンド割り当て";
        Size = new System.Drawing.Size(920, 700);
        MinimumSize = new System.Drawing.Size(700, 480);
        Font = new System.Drawing.Font("Meiryo UI", 9f);
        StartPosition = FormStartPosition.CenterParent;

        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(8, 6, 8, 0),
            Text = "サウンド管理で登録したBGM/SEのIDを、実際に鳴らしたい場面へ割り当てます。空欄は「未設定（鳴らさない）」です。",
            Font = new System.Drawing.Font(Font.FontFamily, 8f),
            ForeColor = System.Drawing.Color.DarkSlateGray,
        };

        // ── タグ絞り込み ──
        var pnlTags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var chkEnemy = new CheckBox { Text = "👾 敵SE", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkGimmick = new CheckBox { Text = "🔧 ギミックSE", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkItem = new CheckBox { Text = "💎 アイテムSE", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkStage = new CheckBox { Text = "🎮 ステージBGM", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        chkEnemy.CheckedChanged += (s, e) => _sectionEnemy.Visible = chkEnemy.Checked;
        chkGimmick.CheckedChanged += (s, e) => _sectionGimmick.Visible = chkGimmick.Checked;
        chkItem.CheckedChanged += (s, e) => _sectionItem.Visible = chkItem.Checked;
        chkStage.CheckedChanged += (s, e) => _sectionStage.Visible = chkStage.Checked;
        pnlTags.Controls.AddRange(new Control[] { chkEnemy, chkGimmick, chkItem, chkStage });

        // ── 下部ボタン ──
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 2), AutoSize = true };
        var btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = System.Drawing.Color.FromArgb(40, 167, 69), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;
        flowRight.Controls.Add(btnCancel);
        flowRight.Controls.Add(btnSave);

        var flowLeft = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 6), AutoSize = true };
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnUndo.Click += (s, e) => AssignUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnRedo.Click += (s, e) => AssignRedo();
        flowLeft.Controls.AddRange(new Control[] { _btnUndo, _btnRedo });

        pnlBottom.Controls.Add(flowRight);
        pnlBottom.Controls.Add(flowLeft);

        // ── 中央: スクロール可能な複数セクション ──
        var pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        _gridEnemy = BuildGrid(new[]
        {
            ("id", "ID", 90, true), ("name", "名前", 110, true),
            ("seSpawn", "出現音", 110, false), ("seAttack", "攻撃音", 110, false),
            ("seDamage", "被弾音", 110, false), ("seDeath", "死亡音", 110, false),
        }, seChoices);
        foreach (var en in _enemies)
            _gridEnemy.Rows.Add(en.id, en.name, en.seSpawn, en.seAttack, en.seDamage, en.seDeath);
        _gridEnemy.CellValueChanged += (s, e) => PushHistory();
        _sectionEnemy = BuildSection("👾 敵のSE割り当て", _gridEnemy, 220);

        _gridGimmick = BuildGrid(new[]
        {
            ("id", "ID", 110, true), ("name", "名前", 140, true), ("seActivate", "起動音", 140, false),
        }, seChoices);
        foreach (var gi in _gimmicks)
            _gridGimmick.Rows.Add(gi.id, gi.name, gi.seActivate);
        _gridGimmick.CellValueChanged += (s, e) => PushHistory();
        _sectionGimmick = BuildSection("🔧 ギミックのSE割り当て", _gridGimmick, 180);

        _gridItem = BuildGrid(new[]
        {
            ("id", "ID", 110, true), ("name", "名前", 140, true), ("seCollect", "取得音", 140, false),
        }, seChoices);
        foreach (var it in _items)
            _gridItem.Rows.Add(it.id, it.name, it.seCollect);
        _gridItem.CellValueChanged += (s, e) => PushHistory();
        _sectionItem = BuildSection("💎 アイテムのSE割り当て", _gridItem, 180);

        _sectionStage = BuildStageSection(bgmChoices, currentStageBgmId);

        pnlScroll.Controls.Add(_sectionItem);
        pnlScroll.Controls.Add(_sectionGimmick);
        pnlScroll.Controls.Add(_sectionEnemy);
        pnlScroll.Controls.Add(_sectionStage);

        Controls.Add(pnlScroll);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTags);
        Controls.Add(lblHint);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { AssignUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { AssignRedo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── ヘルパー: セクション（タイトル+中身、固定高さでDock=Topに積む） ──
    private Panel BuildSection(string title, Control content, int contentHeight)
    {
        const int titleHeight = 24;
        const int topMargin = 4, bottomMargin = 14;
        var section = new Panel { Dock = DockStyle.Top, Height = titleHeight + contentHeight + topMargin + bottomMargin, Padding = new Padding(4, topMargin, 4, bottomMargin) };
        var lbl = new Label { Dock = DockStyle.Top, Height = titleHeight, Text = title, Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0);
        section.Controls.Add(content);
        section.Controls.Add(lbl);
        return section;
    }

    private DataGridView BuildGrid((string name, string header, int width, bool readOnly)[] cols, string[] seChoices)
    {
        var grid = new DataGridView
        {
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersWidth = 30,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            Font = new System.Drawing.Font("Meiryo UI", 9),
        };
        // 敵/ギミック/アイテムのSEフィールドが、カタログに存在しない古いIDを保持している場合でも
        // コンボボックス列がフォーマットエラーで落ちないようにする（値はそのまま保持し、表示のみ許容）
        grid.DataError += (s, e) => e.Cancel = true;
        foreach (var (name, header, width, readOnly) in cols)
        {
            if (readOnly)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = width, ReadOnly = true, DefaultCellStyle = { BackColor = System.Drawing.Color.WhiteSmoke } });
            }
            else
            {
                var combo = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, FillWeight = width, DropDownWidth = 160 };
                combo.Items.AddRange(seChoices);
                grid.Columns.Add(combo);
            }
        }
        return grid;
    }

    private Panel BuildStageSection(string[] bgmChoices, string currentStageBgmId)
    {
        var content = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = false, Padding = new Padding(8) };
        if (_currentStageName == null)
        {
            var lbl = new Label { Text = "先にメイン画面でステージを開いてください（ステージ未選択のため、ここでは設定できません）。", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Margin = new Padding(4, 8, 4, 4) };
            content.Controls.Add(lbl);
        }
        else
        {
            var lbl = new Label { Text = $"現在のステージ「{_currentStageName}」のBGM:", AutoSize = true, Margin = new Padding(4, 8, 6, 4) };
            _cmbStageBgm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(0, 4, 4, 4) };
            _cmbStageBgm.Items.AddRange(bgmChoices);
            int idx = Array.IndexOf(bgmChoices, currentStageBgmId ?? "");
            _cmbStageBgm.SelectedIndex = idx >= 0 ? idx : 0;
            _cmbStageBgm.SelectedIndexChanged += (s, e) => PushHistory();
            content.Controls.Add(lbl);
            content.Controls.Add(_cmbStageBgm);
        }
        return BuildSection("🎮 ステージのBGM割り当て", content, 50);
    }

    // ── Undo/Redo ──────────────────────────────
    private Snapshot CaptureSnapshot() => new Snapshot
    {
        Enemies = ReadEnemiesFromGrid(),
        Gimmicks = ReadGimmicksFromGrid(),
        Items = ReadItemsFromGrid(),
        StageBgmId = _currentStageName != null ? (_cmbStageBgm.SelectedItem as string ?? "") : "",
    };

    private void RestoreSnapshot(Snapshot snap)
    {
        _enemies = snap.Enemies;
        _gimmicks = snap.Gimmicks;
        _items = snap.Items;

        _gridEnemy.Rows.Clear();
        foreach (var en in _enemies) _gridEnemy.Rows.Add(en.id, en.name, en.seSpawn, en.seAttack, en.seDamage, en.seDeath);
        _gridGimmick.Rows.Clear();
        foreach (var gi in _gimmicks) _gridGimmick.Rows.Add(gi.id, gi.name, gi.seActivate);
        _gridItem.Rows.Clear();
        foreach (var it in _items) _gridItem.Rows.Add(it.id, it.name, it.seCollect);

        if (_currentStageName != null)
        {
            int idx = _cmbStageBgm.Items.IndexOf(snap.StageBgmId);
            _cmbStageBgm.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    private void PushHistory()
    {
        _history.Push(CaptureSnapshot());
        UpdateUndoRedoButtons();
    }

    private void AssignUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        RestoreSnapshot(restored);
        UpdateUndoRedoButtons();
    }

    private void AssignRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        RestoreSnapshot(restored);
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    // ── グリッド → データモデルへの反映 ──
    private List<EnemyDef> ReadEnemiesFromGrid()
    {
        var list = CloneList(_enemies);
        for (int i = 0; i < list.Count && i < _gridEnemy.Rows.Count; i++)
        {
            var row = _gridEnemy.Rows[i];
            list[i].seSpawn = row.Cells["seSpawn"].Value?.ToString() ?? "";
            list[i].seAttack = row.Cells["seAttack"].Value?.ToString() ?? "";
            list[i].seDamage = row.Cells["seDamage"].Value?.ToString() ?? "";
            list[i].seDeath = row.Cells["seDeath"].Value?.ToString() ?? "";
        }
        return list;
    }

    private List<GimmickDef> ReadGimmicksFromGrid()
    {
        var list = CloneList(_gimmicks);
        for (int i = 0; i < list.Count && i < _gridGimmick.Rows.Count; i++)
            list[i].seActivate = _gridGimmick.Rows[i].Cells["seActivate"].Value?.ToString() ?? "";
        return list;
    }

    private List<ItemDef> ReadItemsFromGrid()
    {
        var list = CloneList(_items);
        for (int i = 0; i < list.Count && i < _gridItem.Rows.Count; i++)
            list[i].seCollect = _gridItem.Rows[i].Cells["seCollect"].Value?.ToString() ?? "";
        return list;
    }

    // ── 保存 ──────────────────────────────────
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        ResultEnemies = ReadEnemiesFromGrid();
        ResultGimmicks = ReadGimmicksFromGrid();
        ResultItems = ReadItemsFromGrid();
        ResultStageBgmId = _currentStageName != null ? (_cmbStageBgm.SelectedItem as string ?? "") : null;
        DialogResult = DialogResult.OK;
        Close();
    }
}
