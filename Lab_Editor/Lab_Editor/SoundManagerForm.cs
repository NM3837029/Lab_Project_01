using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  SoundManagerForm
//  BGM / SE / UI音の定義を管理するエディタフォーム
//  Feature: サウンド・アセット管理の刷新 — タブ廃止→タグ絞り込み1グリッド構成に作り直し
//  （AssetManagerForm.csで確立済みのパターンを踏襲）。音量列・複製・削除時の参照チェック・
//  SE/UI音間の重複検証・検索・Undo/Redoを追加した。
// ───────────────────────────────────────────────
public class SoundManagerForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    public List<SoundDef> ResultBgm { get; private set; } = new();
    public List<SoundDef> ResultSe { get; private set; } = new();
    public List<SoundDef> ResultUiSe { get; private set; } = new();

    // ── 内部データモデル（BGM/SE/UI音を1つのグリッドで扱うため、カテゴリを行ごとに保持する） ──
    private class SoundRow
    {
        public string Category { get; set; } = "BGM"; // "BGM" | "SE" | "UI"
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public bool IsLoop { get; set; } = false;
        public float Volume { get; set; } = 1.0f;
    }

    // ── フィールド ─────────────────────────────
    private readonly string _projectRoot;
    private readonly string _soundsDir;

    // 削除時の参照チェック用（読み取り専用）
    private readonly List<EnemyDef> _enemies;
    private readonly List<GimmickDef> _gimmicks;
    private readonly List<ItemDef> _items;
    private readonly List<CommonEventDef> _commonEvents;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private CheckBox _chkTagBgm = null!, _chkTagSe = null!, _chkTagUi = null!;
    private Button _btnUndo = null!, _btnRedo = null!;

    private SoundPlayer? _currentPlayer;

    private readonly HistoryManager<List<SoundRow>> _history = new();

    // ── コンストラクタ ─────────────────────────
    public SoundManagerForm(string projectRoot, List<SoundDef> bgm, List<SoundDef> se, List<SoundDef> uiSe,
        List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items, List<CommonEventDef> commonEvents)
    {
        _projectRoot = projectRoot;
        _soundsDir = Path.Combine(projectRoot, "sound");
        _enemies = enemies;
        _gimmicks = gimmicks;
        _items = items;
        _commonEvents = commonEvents;

        InitUI();

        var rows = new List<SoundRow>();
        rows.AddRange(bgm.Select(d => ToRow("BGM", d)));
        rows.AddRange(se.Select(d => ToRow("SE", d)));
        rows.AddRange(uiSe.Select(d => ToRow("UI", d)));
        PopulateGrid(rows);
        PushHistory();
    }

    private static SoundRow ToRow(string category, SoundDef d) => new SoundRow
    {
        Category = category, Id = d.id, Name = d.name, File = d.file, IsLoop = d.isLoop, Volume = d.volume
    };

    // ── UI 構築 ────────────────────────────────
    private void InitUI()
    {
        Text = "サウンド管理エディタ";
        Size = new System.Drawing.Size(900, 620);
        MinimumSize = new System.Drawing.Size(700, 460);
        Font = new System.Drawing.Font("Meiryo UI", 9f);
        StartPosition = FormStartPosition.CenterParent;

        // ── 上部: 検索欄 + タグ絞り込み ──
        var pnlTop = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        var pnlSearch = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var lblSearch = new Label { Text = "🔍", AutoSize = true, Margin = new Padding(2, 6, 0, 0) };
        _txtSearch = new TextBox { Width = 220, Margin = new Padding(4, 3, 0, 0), PlaceholderText = "ID・名前で検索..." };
        _txtSearch.TextChanged += (s, e) => ApplyFilter();
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, _txtSearch });

        var pnlTags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 4) };
        _chkTagBgm = new CheckBox { Text = "🎵 BGM", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        _chkTagSe = new CheckBox { Text = "🔊 SE (効果音)", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        _chkTagUi = new CheckBox { Text = "🔔 UI音", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        _chkTagBgm.CheckedChanged += (s, e) => ApplyFilter();
        _chkTagSe.CheckedChanged += (s, e) => ApplyFilter();
        _chkTagUi.CheckedChanged += (s, e) => ApplyFilter();
        pnlTags.Controls.AddRange(new Control[] { _chkTagBgm, _chkTagSe, _chkTagUi });

        pnlTop.Controls.Add(pnlTags);
        pnlTop.Controls.Add(pnlSearch);

        // ── 下部: ボタン群 ──
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 2), AutoSize = true };
        var btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = System.Drawing.Color.FromArgb(40, 167, 69), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;
        flowRight.Controls.Add(btnCancel);
        flowRight.Controls.Add(btnSave);

        var flowLeft = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 6), AutoSize = true, WrapContents = true };
        var btnAddBgm = new Button { Text = "＋追加 (BGM)", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddBgm.Click += (s, e) => AddRow("BGM");
        var btnAddSe = new Button { Text = "＋追加 (SE)", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddSe.Click += (s, e) => AddRow("SE");
        var btnAddUi = new Button { Text = "＋追加 (UI音)", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddUi.Click += (s, e) => AddRow("UI");
        var btnDuplicate = new Button { Text = "⧉ 複製", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnDuplicate.Click += (s, e) => DuplicateSelected();
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnUndo.Click += (s, e) => SoundUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnRedo.Click += (s, e) => SoundRedo();
        flowLeft.Controls.AddRange(new Control[] { btnAddBgm, btnAddSe, btnAddUi, btnDuplicate, _btnUndo, _btnRedo });

        pnlBottom.Controls.Add(flowRight);
        pnlBottom.Controls.Add(flowLeft);

        // ── 中央: グリッド ──
        _grid = BuildGrid();

        Controls.Add(_grid);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTop);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { SoundUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { SoundRedo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── DataGridView 生成 ──────────────────────
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
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

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", HeaderText = "id", FillWeight = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "name", FillWeight = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colFile", HeaderText = "file", FillWeight = 170, ReadOnly = true, DefaultCellStyle = { BackColor = System.Drawing.Color.WhiteSmoke } });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colLoop", HeaderText = "ループ(BGM用)", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colVolume", HeaderText = "音量(0.00-1.00)", FillWeight = 70, ValueType = typeof(float) });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "colBtnFile", HeaderText = "", Text = "📁選択", UseColumnTextForButtonValue = true, FillWeight = 55 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "colBtnPlay", HeaderText = "", Text = "▶試聴", UseColumnTextForButtonValue = true, FillWeight = 50 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "colBtnDel", HeaderText = "", Text = "🗑削除", UseColumnTextForButtonValue = true, FillWeight = 50 });

        grid.CellContentClick += Grid_CellContentClick;
        grid.CellValueChanged += (s, e) => PushHistory();
        return grid;
    }

    // ── グリッド ⇔ SoundRow ────────────────────
    private void PopulateGrid(List<SoundRow> rows)
    {
        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            int idx = _grid.Rows.Add(r.Id, r.Name, r.File, r.IsLoop, r.Volume, "📁選択", "▶試聴", "🗑削除");
            _grid.Rows[idx].Tag = r.Category;
        }
        ApplyFilter();
    }

    private List<SoundRow> ReadRowsFromGrid()
    {
        var list = new List<SoundRow>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string id = row.Cells["colId"].Value?.ToString() ?? "";
            string file = row.Cells["colFile"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(file)) continue; // 未使用の空行
            if (!float.TryParse(row.Cells["colVolume"].Value?.ToString(), out float vol)) vol = 1.0f;
            list.Add(new SoundRow
            {
                Category = row.Tag as string ?? "SE",
                Id = id,
                Name = row.Cells["colName"].Value?.ToString() ?? "",
                File = file,
                IsLoop = row.Cells["colLoop"].Value is true,
                Volume = Math.Clamp(vol, 0f, 1f),
            });
        }
        return list;
    }

    private void ApplyFilter()
    {
        string q = _txtSearch.Text.Trim();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string category = row.Tag as string ?? "SE";
            bool tagOk = category switch
            {
                "BGM" => _chkTagBgm.Checked,
                "UI" => _chkTagUi.Checked,
                _ => _chkTagSe.Checked,
            };
            bool searchOk = string.IsNullOrEmpty(q)
                || (row.Cells["colId"].Value?.ToString() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (row.Cells["colName"].Value?.ToString() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
            row.Visible = tagOk && searchOk;
        }
    }

    private void AddRow(string category)
    {
        int idx = _grid.Rows.Add("", "", "", category == "BGM", 1.0f, "📁選択", "▶試聴", "🗑削除");
        _grid.Rows[idx].Tag = category;
        ApplyFilter();
        _grid.ClearSelection();
        _grid.Rows[idx].Selected = true;
        PushHistory();
    }

    private void DuplicateSelected()
    {
        if (_grid.SelectedRows.Count == 0) { MessageBox.Show("複製したい行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var src = _grid.SelectedRows[0];
        string category = src.Tag as string ?? "SE";
        string baseId = (src.Cells["colId"].Value?.ToString() ?? "") + "_copy";
        var existingIds = new HashSet<string>(_grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => r.Cells["colId"].Value?.ToString() ?? ""));
        string newId = baseId;
        int n = 2;
        while (existingIds.Contains(newId)) { newId = $"{baseId}{n}"; n++; }

        int idx = _grid.Rows.Add(newId, src.Cells["colName"].Value, src.Cells["colFile"].Value, src.Cells["colLoop"].Value, src.Cells["colVolume"].Value, "📁選択", "▶試聴", "🗑削除");
        _grid.Rows[idx].Tag = category;
        ApplyFilter();
        _grid.ClearSelection();
        _grid.Rows[idx].Selected = true;
        PushHistory();
    }

    // ── Undo/Redo ──────────────────────────────
    private void PushHistory()
    {
        _history.Push(ReadRowsFromGrid());
        UpdateUndoRedoButtons();
    }

    private void SoundUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        PopulateGrid(restored);
        UpdateUndoRedoButtons();
    }

    private void SoundRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        PopulateGrid(restored);
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    // ── セルボタンクリック ─────────────────────
    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        string colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "colBtnFile")
        {
            SelectSoundFile(row);
        }
        else if (colName == "colBtnPlay")
        {
            PlayPreview(row);
        }
        else if (colName == "colBtnDel")
        {
            DeleteRow(row);
        }
    }

    private void SelectSoundFile(DataGridViewRow row)
    {
        using var dlg = new OpenFileDialog { Title = "音声ファイルを選択", Filter = "音声ファイル|*.wav;*.ogg;*.mp3|すべて|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            Directory.CreateDirectory(_soundsDir);
            string dest = Path.Combine(_soundsDir, Path.GetFileName(dlg.FileName));
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);

            row.Cells["colFile"].Value = "sound/" + Path.GetFileName(dlg.FileName);
            PushHistory();
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 試聴はWAVのみ対応（System.Media.SoundPlayerの制約。mp3/ogg再生には新規の音声ライブラリ依存が
    // 必要になるため、今回のスコープでは追加しない）。音量フィールドは試聴には反映されない
    // （SoundPlayerに音量APIが無いため）。
    private void PlayPreview(DataGridViewRow row)
    {
        string relPath = row.Cells["colFile"].Value?.ToString() ?? "";
        if (string.IsNullOrEmpty(relPath)) return;

        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));
        if (!File.Exists(fullPath))
        {
            MessageBox.Show("ファイルが見つかりません:\n" + fullPath, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (Path.GetExtension(fullPath).ToLowerInvariant() != ".wav")
        {
            MessageBox.Show("WAVファイルのみプレビュー可能です", "プレビュー不可", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _currentPlayer?.Stop();
            _currentPlayer?.Dispose();
            _currentPlayer = new SoundPlayer(fullPath);
            _currentPlayer.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show("再生に失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Feature: サウンド・アセット管理の刷新 — 削除しようとしているIDが敵/ギミック/アイテム/
    // コモンイベントのどこかで参照されていれば警告する（削除で無音になる参照切れを防ぐ）。
    // 注: ステージごとのイベントトリガー（EventTrigger）はステージファイルを個別に読み込む必要が
    // あり、このダイアログの通常の呼び出し経路では対象外としている（既知の制限）。
    private void DeleteRow(DataGridViewRow row)
    {
        string id = row.Cells["colId"].Value?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(id))
        {
            var refs = FindReferences(id);
            if (refs.Count > 0)
            {
                string msg = $"ID「{id}」は以下から参照されています。削除すると無音になります:\n\n" +
                    string.Join("\n", refs.Take(10)) + (refs.Count > 10 ? $"\n…他{refs.Count - 10}件" : "") +
                    "\n\nこのまま削除しますか？";
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
        }
        _grid.Rows.Remove(row);
        PushHistory();
    }

    private List<string> FindReferences(string id)
    {
        var refs = new List<string>();
        foreach (var en in _enemies)
        {
            if (en.seSpawn == id) refs.Add($"敵「{en.id}」の出現音");
            if (en.seAttack == id) refs.Add($"敵「{en.id}」の攻撃音");
            if (en.seDamage == id) refs.Add($"敵「{en.id}」の被弾音");
            if (en.seDeath == id) refs.Add($"敵「{en.id}」の死亡音");
        }
        foreach (var gi in _gimmicks)
        {
            if (gi.seActivate == id) refs.Add($"ギミック「{gi.id}」の起動音");
        }
        foreach (var it in _items)
        {
            if (it.seCollect == id) refs.Add($"アイテム「{it.id}」の取得音");
        }
        foreach (var ce in _commonEvents)
        {
            foreach (var act in ce.actions)
            {
                if ((act.action == "ChangeBgm" || act.action == "PlaySe") && act.param1 == id)
                    refs.Add($"コモンイベント「{ce.id}」の{act.action}アクション");
            }
        }
        return refs;
    }

    // ── 保存 ──────────────────────────────────
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var rows = ReadRowsFromGrid();
        var bgm = rows.Where(r => r.Category == "BGM").ToList();
        var se = rows.Where(r => r.Category == "SE").ToList();
        var uiSe = rows.Where(r => r.Category == "UI").ToList();

        string? error = ValidateCategory("BGM", bgm) ?? ValidateCategory("SE", se) ?? ValidateCategory("UI音", uiSe);
        if (error == null)
        {
            // Feature: サウンド・アセット管理の刷新 — SEとUI音はエンジン側で同じseMapに統合されるため、
            // カテゴリ間でもID重複があれば警告する（片方が上書きされ再生できなくなる実バグを防ぐ）
            var seIds = new HashSet<string>(se.Where(d => !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id));
            var crossDup = uiSe.Select(d => d.Id).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && seIds.Contains(id));
            if (crossDup != null)
                error = $"ID「{crossDup}」がSEとUI音の両方に存在します。\nゲーム内ではSEとUI音は同じ名前空間で管理されるため、どちらか一方のIDを変更してください。";
        }
        if (error != null)
        {
            MessageBox.Show(error, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ResultBgm = bgm.Select(ToDef).ToList();
        ResultSe = se.Select(ToDef).ToList();
        ResultUiSe = uiSe.Select(ToDef).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private static SoundDef ToDef(SoundRow r) => new SoundDef { id = r.Id, name = r.Name, file = r.File, isLoop = r.IsLoop, volume = r.Volume };

    private static string? ValidateCategory(string categoryLabel, List<SoundRow> defs)
    {
        var seenIds = new HashSet<string>();
        foreach (var d in defs)
        {
            if (string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.File))
                return $"[{categoryLabel}] ファイル「{d.File}」にIDが設定されていません。\nIDが空のままだとゲーム側から一切参照できず、無効なデータとして保存されます。";
            if (!string.IsNullOrWhiteSpace(d.Id) && string.IsNullOrWhiteSpace(d.File))
                return $"[{categoryLabel}] ID「{d.Id}」にファイルが設定されていません。\n先に「📁選択」でファイルを指定してください。";
            if (!string.IsNullOrWhiteSpace(d.Id) && !seenIds.Add(d.Id))
                return $"[{categoryLabel}] ID「{d.Id}」が重複しています。\nIDは一意である必要があります。";
        }
        return null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _currentPlayer?.Stop();
        _currentPlayer?.Dispose();
        base.OnFormClosed(e);
    }
}
