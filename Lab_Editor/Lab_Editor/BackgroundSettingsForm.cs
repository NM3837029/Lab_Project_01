using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  BackgroundSettingsForm
//  ステージの視差背景レイヤーを設定するフォーム
// ───────────────────────────────────────────────
public class BackgroundSettingsForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    public List<BackgroundLayer> ResultLayers { get; private set; } = new();

    // ── フィールド ─────────────────────────────
    private readonly string _projectRoot;
    private readonly string _imgDir;

    private DataGridView _grid       = null!;
    private PictureBox   _preview    = null!;
    private Button       _btnAdd     = null!;
    private Button       _btnSave    = null!;
    private Button       _btnCancel  = null!;

    // ── コンストラクタ ─────────────────────────
    public BackgroundSettingsForm(string projectRoot, List<BackgroundLayer> layers)
    {
        _projectRoot = projectRoot;
        _imgDir      = Path.Combine(projectRoot, "img");

        InitializeComponent();
        PopulateGrid(layers);
    }

    // ── UI 構築 ────────────────────────────────
    private void InitializeComponent()
    {
        Text            = "背景レイヤー設定";
        Size            = new Size(860, 500);
        Font            = new Font("Meiryo UI", 9f);
        StartPosition   = FormStartPosition.CenterParent;
        MinimizeBox     = false;
        MaximizeBox     = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        // ── 右パネル（プレビュー） ───────────────
        var pnlRight = new Panel
        {
            Dock  = DockStyle.Right,
            Width = 220,
        };

        var lblPreview = new Label
        {
            Text     = "スプライトプレビュー",
            Dock     = DockStyle.Top,
            Height   = 24,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        _preview = new PictureBox
        {
            Size      = new Size(200, 200),
            Location  = new Point(10, 30),
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        pnlRight.Controls.Add(lblPreview);
        pnlRight.Controls.Add(_preview);

        // ── DataGridView ────────────────────────
        _grid = new DataGridView
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

        // drawOrder 列
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colOrder", HeaderText = "drawOrder", Width = 78,
            ValueType = typeof(int)
        });

        // sprite 列（読み取り専用）
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colSprite", HeaderText = "sprite", Width = 160,
            ReadOnly = true,
            DefaultCellStyle = { BackColor = Color.WhiteSmoke }
        });

        // scrollRate 列
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colScrollRate", HeaderText = "scrollRate", Width = 80,
            ValueType = typeof(float)
        });

        // loop チェックボックス列
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "colLoop", HeaderText = "loop", Width = 50
        });

        // offsetX 列
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colOffsetX", HeaderText = "offsetX", Width = 70,
            ValueType = typeof(float)
        });

        // offsetY 列
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colOffsetY", HeaderText = "offsetY", Width = 70,
            ValueType = typeof(float)
        });

        // ボタン列：📁選択
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "colBtnFile", HeaderText = "", Width = 64,
            Text = "📁選択", UseColumnTextForButtonValue = true
        });

        // ボタン列：🗑削除
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "colBtnDel", HeaderText = "", Width = 60,
            Text = "🗑削除", UseColumnTextForButtonValue = true
        });

        _grid.CellContentClick     += Grid_CellContentClick;
        _grid.SelectionChanged     += Grid_SelectionChanged;
        _grid.DataError            += (_, e) => e.Cancel = true;

        // ── 下部パネル ──────────────────────────
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };

        _btnAdd    = MakeButton("＋背景追加",       12,  6, 110);
        _btnSave   = MakeButton("💾 保存して閉じる", 530, 6, 160);
        _btnCancel = MakeButton("キャンセル",        700, 6, 100);

        _btnSave.BackColor   = Color.FromArgb(70, 130, 180);
        _btnSave.ForeColor   = Color.White;
        _btnSave.FlatStyle   = FlatStyle.Flat;
        _btnCancel.FlatStyle = FlatStyle.Flat;

        _btnAdd.Click    += (_, _) => AddEmptyRow();
        _btnSave.Click   += BtnSave_Click;
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        pnlBottom.Controls.AddRange(new Control[] { _btnAdd, _btnSave, _btnCancel });

        Controls.Add(_grid);
        Controls.Add(pnlRight);
        Controls.Add(pnlBottom);
    }

    // ── データ流し込み ────────────────────────
    private void PopulateGrid(List<BackgroundLayer> layers)
    {
        _grid.Rows.Clear();
        foreach (var l in layers)
            AddRow(l);
    }

    private void AddRow(BackgroundLayer? layer = null)
    {
        int idx = _grid.Rows.Add();
        var row  = _grid.Rows[idx];

        row.Cells["colOrder"].Value      = layer?.drawOrder  ?? 0;
        row.Cells["colSprite"].Value     = layer?.sprite     ?? "";
        row.Cells["colScrollRate"].Value = layer?.scrollRate ?? 0.5f;
        row.Cells["colLoop"].Value       = layer?.loop       ?? false;
        row.Cells["colOffsetX"].Value    = layer?.offsetX    ?? 0f;
        row.Cells["colOffsetY"].Value    = layer?.offsetY    ?? 0f;
    }

    private void AddEmptyRow() => AddRow(null);

    // ── セルボタンクリック ─────────────────────
    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "colBtnFile")
        {
            SelectImageFile(e.RowIndex);
        }
        else if (colName == "colBtnDel")
        {
            _grid.Rows.RemoveAt(e.RowIndex);
            _preview.Image = null;
        }
    }

    // ── ファイル選択 ──────────────────────────
    private void SelectImageFile(int rowIndex)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "画像ファイルを選択",
            Filter = "画像ファイル|*.png;*.jpg;*.bmp;*.gif|すべて|*.*"
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            Directory.CreateDirectory(_imgDir);
            string dest = Path.Combine(_imgDir, Path.GetFileName(dlg.FileName));

            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);

            string relPath = "img/" + Path.GetFileName(dlg.FileName);
            _grid.Rows[rowIndex].Cells["colSprite"].Value = relPath;

            ShowPreview(relPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 選択行変更→プレビュー更新 ─────────────
    private void Grid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0) return;
        string sprite = _grid.SelectedRows[0].Cells["colSprite"].Value?.ToString() ?? "";
        ShowPreview(sprite);
    }

    private void ShowPreview(string relPath)
    {
        if (string.IsNullOrEmpty(relPath))
        {
            _preview.Image = null;
            return;
        }

        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));

        if (!File.Exists(fullPath))
        {
            _preview.Image = null;
            return;
        }

        try
        {
            var bmp = new Bitmap(fullPath);
            var old = _preview.Image;
            _preview.Image = bmp;
            old?.Dispose();
        }
        catch
        {
            _preview.Image = null;
        }
    }

    // ── 保存 ──────────────────────────────────
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        ResultLayers.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;

            float TryFloat(string colName, float fallback = 0f)
            {
                return float.TryParse(row.Cells[colName].Value?.ToString(), out float v) ? v : fallback;
            }
            int TryInt(string colName, int fallback = 0)
            {
                return int.TryParse(row.Cells[colName].Value?.ToString(), out int v) ? v : fallback;
            }

            ResultLayers.Add(new BackgroundLayer
            {
                drawOrder  = TryInt("colOrder"),
                sprite     = row.Cells["colSprite"].Value?.ToString()       ?? "",
                scrollRate = TryFloat("colScrollRate", 0.5f),
                loop       = row.Cells["colLoop"].Value is bool b && b,
                offsetX    = TryFloat("colOffsetX"),
                offsetY    = TryFloat("colOffsetY"),
            });
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── ヘルパー ──────────────────────────────
    private static Button MakeButton(string text, int x, int y, int w) =>
        new Button
        {
            Text     = text,
            Location = new Point(x, y),
            Size     = new Size(w, 30),
            Font     = new Font("Meiryo UI", 9f)
        };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
