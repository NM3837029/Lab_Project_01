using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  AnimationEditorForm
//  アニメーションクリップを編集するフォーム
// ───────────────────────────────────────────────
public class AnimationEditorForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    public AnimationSet ResultSet { get; private set; } = null!;

    // ── フィールド ─────────────────────────────
    private readonly string _projectRoot;
    private readonly string _imgDir;

    private TextBox      _txtAssetId    = null!;
    private DataGridView _grid          = null!;
    private PictureBox   _preview       = null!;
    private Label        _lblFrameInfo  = null!;
    private Button       _btnPreview    = null!;
    private Button       _btnAdd        = null!;
    private Button       _btnSave       = null!;
    private Button       _btnCancel     = null!;

    // アニメーション再生用
    private System.Windows.Forms.Timer _animTimer = null!;
    private Bitmap?  _spriteSheet;
    private int      _currentFrame;
    private bool     _isPlaying;

    // 現在選択中クリップの情報
    private int _frameCountX;
    private int _frameCountY;
    private int _startFrame;
    private int _endFrame;

    // ── コンストラクタ ─────────────────────────
    public AnimationEditorForm(string projectRoot, AnimationSet animSet)
    {
        _projectRoot = projectRoot;
        _imgDir      = Path.Combine(projectRoot, "img");

        InitializeComponent();

        // コピーを作成して編集
        _txtAssetId.Text = animSet.assetId ?? "";
        PopulateGrid(animSet.clips ?? new List<AnimationClip>());
    }

    // ── UI 構築 ────────────────────────────────
    private void InitializeComponent()
    {
        Text            = "アニメーションエディタ";
        Size            = new Size(900, 540);
        Font            = new Font("Meiryo UI", 9f);
        StartPosition   = FormStartPosition.CenterParent;
        MinimizeBox     = false;
        MaximizeBox     = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        // ── 上部：assetId ───────────────────────
        var lblAsset = new Label
        {
            Text     = "assetId:",
            Location = new Point(10, 12),
            AutoSize = true,
        };
        _txtAssetId = new TextBox
        {
            Location = new Point(70, 10),
            Width    = 220,
        };
        Controls.Add(lblAsset);
        Controls.Add(_txtAssetId);

        // ── 右パネル（プレビュー） ───────────────
        var pnlRight = new Panel
        {
            Location  = new Point(638, 40),
            Size      = new Size(240, 420),
        };

        _preview = new PictureBox
        {
            Location  = new Point(10, 10),
            Size      = new Size(180, 180),
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        _lblFrameInfo = new Label
        {
            Location  = new Point(10, 198),
            Size      = new Size(200, 40),
            Text      = "フレーム情報",
            ForeColor = Color.DimGray,
        };

        _btnPreview = new Button
        {
            Text     = "▶ プレビュー",
            Location = new Point(10, 246),
            Size     = new Size(120, 30),
        };
        _btnPreview.Click += BtnPreview_Click;

        pnlRight.Controls.AddRange(new Control[] { _preview, _lblFrameInfo, _btnPreview });
        Controls.Add(pnlRight);

        // ── DataGridView ────────────────────────
        _grid = BuildGrid();
        _grid.Location = new Point(10, 40);
        _grid.Size     = new Size(618, 390);
        Controls.Add(_grid);

        // ── タイマー ────────────────────────────
        _animTimer          = new System.Windows.Forms.Timer();
        _animTimer.Interval = 100;
        _animTimer.Tick    += AnimTimer_Tick;

        // ── 下部ボタン ──────────────────────────
        _btnAdd    = MakeButton("＋クリップ追加",    10,  470, 130);
        _btnSave   = MakeButton("💾 保存して閉じる", 620, 470, 160);
        _btnCancel = MakeButton("キャンセル",         788, 470, 100);

        _btnSave.BackColor   = Color.FromArgb(70, 130, 180);
        _btnSave.ForeColor   = Color.White;
        _btnSave.FlatStyle   = FlatStyle.Flat;
        _btnCancel.FlatStyle = FlatStyle.Flat;

        _btnAdd.Click    += (_, _) => AddEmptyRow();
        _btnSave.Click   += BtnSave_Click;
        _btnCancel.Click += (_, _) =>
        {
            StopPreview();
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(new Control[] { _btnAdd, _btnSave, _btnCancel });
    }

    // ── DataGridView 生成 ──────────────────────
    private DataGridView BuildGrid()
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
        grid.DataError += (_, e) => e.Cancel = true;

        // name 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colName", HeaderText = "name", Width = 100
        });

        // sprite 列（読み取り専用）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colSprite", HeaderText = "sprite", Width = 140,
            ReadOnly = true,
            DefaultCellStyle = { BackColor = Color.WhiteSmoke }
        });

        // frameCountX 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFCX", HeaderText = "FCX", Width = 44,
            ValueType = typeof(int)
        });

        // frameCountY 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFCY", HeaderText = "FCY", Width = 44,
            ValueType = typeof(int)
        });

        // startFrame 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colStart", HeaderText = "start", Width = 46,
            ValueType = typeof(int)
        });

        // endFrame 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colEnd", HeaderText = "end", Width = 46,
            ValueType = typeof(int)
        });

        // fps 列
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFps", HeaderText = "fps", Width = 46,
            ValueType = typeof(float)
        });

        // loop チェックボックス列
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "colLoop", HeaderText = "loop", Width = 46
        });

        // ボタン列：📁選択
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "colBtnFile", HeaderText = "", Width = 64,
            Text = "📁選択", UseColumnTextForButtonValue = true
        });

        grid.CellContentClick += Grid_CellContentClick;
        grid.SelectionChanged  += Grid_SelectionChanged;

        return grid;
    }

    // ── データ流し込み ────────────────────────
    private void PopulateGrid(List<AnimationClip> clips)
    {
        _grid.Rows.Clear();
        foreach (var c in clips)
            AddRow(c);
    }

    private void AddRow(AnimationClip? clip = null)
    {
        int idx = _grid.Rows.Add();
        var row  = _grid.Rows[idx];

        row.Cells["colName"].Value   = clip?.name        ?? "";
        row.Cells["colSprite"].Value = clip?.sprite      ?? "";
        row.Cells["colFCX"].Value    = clip?.frameCountX ?? 1;
        row.Cells["colFCY"].Value    = clip?.frameCountY ?? 1;
        row.Cells["colStart"].Value  = clip?.startFrame  ?? 0;
        row.Cells["colEnd"].Value    = clip?.endFrame    ?? 0;
        row.Cells["colFps"].Value    = clip?.fps         ?? 12f;
        row.Cells["colLoop"].Value   = clip?.loop        ?? true;
    }

    private void AddEmptyRow() => AddRow(null);

    // ── セルボタンクリック ─────────────────────
    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name == "colBtnFile")
            SelectSpriteFile(e.RowIndex);
    }

    // ── ファイル選択 ──────────────────────────
    private void SelectSpriteFile(int rowIndex)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "スプライトシートを選択",
            Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*"
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
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 選択行変更 ────────────────────────────
    private void Grid_SelectionChanged(object? sender, EventArgs e)
    {
        StopPreview();
        LoadSelectedClip();
    }

    private void LoadSelectedClip()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var row = _grid.SelectedRows[0];

        string sprite = row.Cells["colSprite"].Value?.ToString() ?? "";
        _frameCountX  = TryGetInt(row, "colFCX",   1);
        _frameCountY  = TryGetInt(row, "colFCY",   1);
        _startFrame   = TryGetInt(row, "colStart",  0);
        _endFrame     = TryGetInt(row, "colEnd",    0);
        float fps     = TryGetFloat(row, "colFps", 12f);

        _animTimer.Interval = fps > 0 ? (int)(1000f / fps) : 100;
        _currentFrame = _startFrame;

        LoadSpriteSheet(sprite);
        DrawFrame();
        UpdateFrameLabel();
    }

    private void LoadSpriteSheet(string relPath)
    {
        _spriteSheet?.Dispose();
        _spriteSheet = null;

        if (string.IsNullOrEmpty(relPath)) return;

        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));
        if (!File.Exists(fullPath)) return;

        try
        {
            _spriteSheet = new Bitmap(fullPath);
        }
        catch
        {
            _spriteSheet = null;
        }
    }

    // ── フレーム描画 ──────────────────────────
    private void DrawFrame()
    {
        if (_spriteSheet == null || _frameCountX <= 0 || _frameCountY <= 0)
        {
            _preview.Image = null;
            return;
        }

        int cellW = _spriteSheet.Width  / _frameCountX;
        int cellH = _spriteSheet.Height / _frameCountY;

        int col = _currentFrame % _frameCountX;
        int row = _currentFrame / _frameCountX;

        if (row >= _frameCountY)
        {
            _preview.Image = null;
            return;
        }

        var srcRect = new Rectangle(col * cellW, row * cellH, cellW, cellH);
        var bmp     = new Bitmap(cellW, cellH);
        using (var g = Graphics.FromImage(bmp))
            g.DrawImage(_spriteSheet, new Rectangle(0, 0, cellW, cellH), srcRect, GraphicsUnit.Pixel);

        var old = _preview.Image;
        _preview.Image = bmp;
        old?.Dispose();
    }

    private void UpdateFrameLabel()
    {
        _lblFrameInfo.Text =
            $"フレーム: {_currentFrame} / {_endFrame}\n" +
            $"FCX:{_frameCountX} FCY:{_frameCountY}";
    }

    // ── タイマー Tick ─────────────────────────
    private void AnimTimer_Tick(object? sender, EventArgs e)
    {
        if (_endFrame <= _startFrame)
        {
            StopPreview();
            return;
        }

        _currentFrame++;
        if (_currentFrame > _endFrame)
            _currentFrame = _startFrame;

        DrawFrame();
        UpdateFrameLabel();
    }

    // ── プレビュー開始/停止 ───────────────────
    private void BtnPreview_Click(object? sender, EventArgs e)
    {
        if (_isPlaying)
        {
            StopPreview();
        }
        else
        {
            if (_spriteSheet == null)
            {
                MessageBox.Show("スプライトが設定されていません。",
                    "プレビュー", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _isPlaying = true;
            _btnPreview.Text = "⏹ 停止";
            _currentFrame = _startFrame;
            _animTimer.Start();
        }
    }

    private void StopPreview()
    {
        _animTimer.Stop();
        _isPlaying       = false;
        _btnPreview.Text = "▶ プレビュー";
    }

    // ── 保存 ──────────────────────────────────
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        StopPreview();

        var clips = new List<AnimationClip>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            clips.Add(new AnimationClip
            {
                name        = row.Cells["colName"].Value?.ToString()   ?? "",
                sprite      = row.Cells["colSprite"].Value?.ToString() ?? "",
                frameCountX = TryGetInt(row, "colFCX",   1),
                frameCountY = TryGetInt(row, "colFCY",   1),
                startFrame  = TryGetInt(row, "colStart",  0),
                endFrame    = TryGetInt(row, "colEnd",    0),
                fps         = TryGetFloat(row, "colFps", 12f),
                loop        = row.Cells["colLoop"].Value is bool b && b,
            });
        }

        ResultSet = new AnimationSet
        {
            assetId = _txtAssetId.Text,
            clips   = clips,
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── ヘルパー ──────────────────────────────
    private static int TryGetInt(DataGridViewRow row, string col, int fallback)
    {
        return int.TryParse(row.Cells[col].Value?.ToString(), out int v) ? v : fallback;
    }

    private static float TryGetFloat(DataGridViewRow row, string col, float fallback)
    {
        return float.TryParse(row.Cells[col].Value?.ToString(), out float v) ? v : fallback;
    }

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
        StopPreview();
        _spriteSheet?.Dispose();
        _animTimer.Dispose();
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
