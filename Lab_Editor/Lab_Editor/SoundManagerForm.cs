using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  SoundManagerForm
//  BGM / SE の定義を管理するエディタフォーム
// ───────────────────────────────────────────────
public class SoundManagerForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    public List<SoundDef> ResultBgm { get; private set; } = new();
    public List<SoundDef> ResultSe  { get; private set; } = new();
    public List<SoundDef> ResultUiSe { get; private set; } = new();

    // ── フィールド ─────────────────────────────
    private readonly string _projectRoot;
    private readonly string _soundsDir;

    private TabControl    _tabControl  = null!;
    private TabPage       _tabBgm      = null!;
    private TabPage       _tabSe       = null!;
    private TabPage       _tabUi       = null!;
    private DataGridView  _gridBgm     = null!;
    private DataGridView  _gridSe      = null!;
    private DataGridView  _gridUi      = null!;

    private Button _btnAddBgm    = null!;
    private Button _btnAddSe     = null!;
    private Button _btnAddUi     = null!;
    private Button _btnSave      = null!;
    private Button _btnCancel    = null!;

    private SoundPlayer? _currentPlayer;

    // ── コンストラクタ ─────────────────────────
    public SoundManagerForm(string projectRoot, List<SoundDef> bgm, List<SoundDef> se, List<SoundDef> uiSe)
    {
        _projectRoot = projectRoot;
        _soundsDir   = Path.Combine(projectRoot, "sound");

        InitializeComponent();
        PopulateGrid(_gridBgm, bgm, isBgm: true);
        PopulateGrid(_gridSe,  se,  isBgm: false);
        PopulateGrid(_gridUi,  uiSe, isBgm: false);
    }

    // ── UI 構築 ────────────────────────────────
    private void InitializeComponent()
    {
        Text            = "サウンド管理エディタ";
        Size            = new System.Drawing.Size(780, 520);
        Font            = new System.Drawing.Font("Meiryo UI", 9f);
        StartPosition   = FormStartPosition.CenterParent;
        MinimizeBox     = false;
        MaximizeBox     = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        // ── TabControl ──────────────────────────
        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabBgm     = new TabPage("🎵 BGM");
        _tabSe      = new TabPage("🔊 SE (効果音)");
        _tabUi      = new TabPage("🔔 UI音");

        _gridBgm = BuildGrid(isBgm: true);
        _gridSe  = BuildGrid(isBgm: false);
        _gridUi  = BuildGrid(isBgm: false);

        _tabBgm.Controls.Add(_gridBgm);
        _tabSe.Controls.Add(_gridSe);
        _tabUi.Controls.Add(_gridUi);
        _tabControl.TabPages.Add(_tabBgm);
        _tabControl.TabPages.Add(_tabSe);
        _tabControl.TabPages.Add(_tabUi);

        // ── Bottom Panel ────────────────────────
        var pnlBottom = new Panel
        {
            Dock   = DockStyle.Bottom,
            Height = 44,
        };

        _btnAddBgm = MakeButton("＋追加 (BGM)", 12,  6, 110);
        _btnAddSe  = MakeButton("＋追加 (SE)",  132, 6, 110);
        _btnAddUi  = MakeButton("＋追加 (UI音)", 252, 6, 110);
        _btnSave   = MakeButton("💾 保存して閉じる", 480, 6, 160);
        _btnCancel = MakeButton("キャンセル",    648, 6, 100);

        _btnSave.BackColor   = System.Drawing.Color.FromArgb(70, 130, 180);
        _btnSave.ForeColor   = System.Drawing.Color.White;
        _btnSave.FlatStyle   = FlatStyle.Flat;
        _btnCancel.FlatStyle = FlatStyle.Flat;

        _btnAddBgm.Click += (_, _) => AddRow(_gridBgm, isBgm: true);
        _btnAddSe.Click  += (_, _) => AddRow(_gridSe,  isBgm: false);
        _btnAddUi.Click  += (_, _) => AddRow(_gridUi,  isBgm: false);
        _btnSave.Click   += BtnSave_Click;
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        pnlBottom.Controls.AddRange(new Control[]
            { _btnAddBgm, _btnAddSe, _btnAddUi, _btnSave, _btnCancel });

        Controls.Add(_tabControl);
        Controls.Add(pnlBottom);
    }

    // ── DataGridView 生成 ──────────────────────
    private DataGridView BuildGrid(bool isBgm)
    {
        var grid = new DataGridView
        {
            Dock                  = DockStyle.Fill,
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            RowHeadersWidth       = 30,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect           = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight   = 28,
        };

        // id 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colId", HeaderText = "id", Width = 110,
            DataPropertyName = "id"
        });

        // name 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colName", HeaderText = "name", Width = 140,
            DataPropertyName = "name"
        });

        // file 列 (読み取り専用)
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFile", HeaderText = "file", Width = 180,
            ReadOnly = true, DefaultCellStyle = { BackColor = System.Drawing.Color.WhiteSmoke }
        });

        // BGM のみ isLoop チェックボックス列
        if (isBgm)
        {
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "colLoop", HeaderText = "isLoop", Width = 60
            });
        }

        // ボタン列：📁選択
        var colFile = new DataGridViewButtonColumn
        {
            Name = "colBtnFile", HeaderText = "", Width = 64,
            Text = "📁選択", UseColumnTextForButtonValue = true
        };
        grid.Columns.Add(colFile);

        // ボタン列：▶試聴
        var colPlay = new DataGridViewButtonColumn
        {
            Name = "colBtnPlay", HeaderText = "", Width = 60,
            Text = "▶試聴", UseColumnTextForButtonValue = true
        };
        grid.Columns.Add(colPlay);

        // ボタン列：🗑削除
        var colDel = new DataGridViewButtonColumn
        {
            Name = "colBtnDel", HeaderText = "", Width = 60,
            Text = "🗑削除", UseColumnTextForButtonValue = true
        };
        grid.Columns.Add(colDel);

        grid.CellContentClick += (s, e) => Grid_CellContentClick(grid, e, isBgm);

        return grid;
    }

    // ── グリッドにデータを流し込む ─────────────
    private void PopulateGrid(DataGridView grid, List<SoundDef> defs, bool isBgm)
    {
        grid.Rows.Clear();
        foreach (var d in defs)
            AddRow(grid, isBgm, d);
    }

    private void AddRow(DataGridView grid, bool isBgm, SoundDef? def = null)
    {
        int idx = grid.Rows.Add();
        var row  = grid.Rows[idx];

        row.Cells["colId"].Value   = def?.id   ?? "";
        row.Cells["colName"].Value = def?.name  ?? "";
        row.Cells["colFile"].Value = def?.file  ?? "";

        if (isBgm)
            row.Cells["colLoop"].Value = def?.isLoop ?? true;
    }

    // ── セルボタンクリック ─────────────────────
    private void Grid_CellContentClick(DataGridView grid, DataGridViewCellEventArgs e, bool isBgm)
    {
        if (e.RowIndex < 0) return;
        var colName = grid.Columns[e.ColumnIndex].Name;

        if (colName == "colBtnFile")
        {
            SelectSoundFile(grid, e.RowIndex);
        }
        else if (colName == "colBtnPlay")
        {
            PlayPreview(grid, e.RowIndex);
        }
        else if (colName == "colBtnDel")
        {
            grid.Rows.RemoveAt(e.RowIndex);
        }
    }

    // ── ファイル選択 ──────────────────────────
    private void SelectSoundFile(DataGridView grid, int rowIndex)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "音声ファイルを選択",
            Filter = "音声ファイル|*.wav;*.ogg;*.mp3|すべて|*.*"
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            Directory.CreateDirectory(_soundsDir);
            string dest = Path.Combine(_soundsDir, Path.GetFileName(dlg.FileName));

            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);

            string relPath = "sound/" + Path.GetFileName(dlg.FileName);
            grid.Rows[rowIndex].Cells["colFile"].Value = relPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 試聴 ──────────────────────────────────
    private void PlayPreview(DataGridView grid, int rowIndex)
    {
        string relPath = grid.Rows[rowIndex].Cells["colFile"].Value?.ToString() ?? "";
        if (string.IsNullOrEmpty(relPath)) return;

        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));

        if (!File.Exists(fullPath))
        {
            MessageBox.Show("ファイルが見つかりません:\n" + fullPath,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext != ".wav")
        {
            MessageBox.Show("WAVファイルのみプレビュー可能です",
                "プレビュー不可", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show("再生に失敗しました:\n" + ex.Message,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 保存 ──────────────────────────────────
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var bgm  = ExtractSoundDefs(_gridBgm, isBgm: true);
        var se   = ExtractSoundDefs(_gridSe,  isBgm: false);
        var uiSe = ExtractSoundDefs(_gridUi,  isBgm: false);

        string? error = ValidateSoundDefs("BGM", bgm)
                     ?? ValidateSoundDefs("SE", se)
                     ?? ValidateSoundDefs("UI音", uiSe);
        if (error != null)
        {
            MessageBox.Show(error, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ResultBgm  = bgm;
        ResultSe   = se;
        ResultUiSe = uiSe;
        DialogResult = DialogResult.OK;
        Close();
    }

    // id/file が片方だけ空、または同一カテゴリ内でidが重複している行がないか検証する。
    // 両方空の行（未入力のまま追加しただけの行）はExtractSoundDefsの時点で除外済みなのでここでは扱わない。
    private static string? ValidateSoundDefs(string categoryLabel, List<SoundDef> defs)
    {
        var seenIds = new HashSet<string>();
        foreach (var d in defs)
        {
            if (string.IsNullOrWhiteSpace(d.id) && !string.IsNullOrWhiteSpace(d.file))
                return $"[{categoryLabel}] ファイル「{d.file}」にIDが設定されていません。\nIDが空のままだとゲーム側から一切参照できず、無効なデータとして保存されます。";
            if (!string.IsNullOrWhiteSpace(d.id) && string.IsNullOrWhiteSpace(d.file))
                return $"[{categoryLabel}] ID「{d.id}」にファイルが設定されていません。\n先に「📁選択」でファイルを指定してください。";
            if (!string.IsNullOrWhiteSpace(d.id) && !seenIds.Add(d.id))
                return $"[{categoryLabel}] ID「{d.id}」が重複しています。\nIDは一意である必要があります。";
        }
        return null;
    }

    private List<SoundDef> ExtractSoundDefs(DataGridView grid, bool isBgm)
    {
        var list = new List<SoundDef>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var def = new SoundDef
            {
                id     = row.Cells["colId"].Value?.ToString()   ?? "",
                name   = row.Cells["colName"].Value?.ToString() ?? "",
                file   = row.Cells["colFile"].Value?.ToString() ?? "",
                isLoop = isBgm
                    ? (row.Cells["colLoop"].Value is bool b && b)
                    : false
            };
            // id・fileが両方とも未入力の行は、未使用の空行として無視する
            if (string.IsNullOrWhiteSpace(def.id) && string.IsNullOrWhiteSpace(def.file)) continue;
            list.Add(def);
        }
        return list;
    }

    // ── ヘルパー ──────────────────────────────
    private static Button MakeButton(string text, int x, int y, int w) =>
        new Button { Text = text, Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(w, 30), Font = new System.Drawing.Font("Meiryo UI", 9f) };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _currentPlayer?.Stop();
        _currentPlayer?.Dispose();
        base.OnFormClosed(e);
    }
}
