using Newtonsoft.Json;

namespace Lab_Editor;

/// <summary>
/// タイル定義の追加・編集ウィンドウ
/// </summary>
public class TileEditorForm : Form
{
    private readonly string assetsPath;
    private List<TileDef> tiles;

    private DataGridView dgv = null!;
    private Button btnAdd, btnSave, btnClose;
    private Panel pnlPreview;
    private TextBox txtSearch = null!;

    // Feature: UI改善（提案書 MP-3）— タイル定義の追加/削除/編集にもUndo/Redoを効かせる
    private readonly HistoryManager<List<TileDef>> _history = new();
    private Button _btnUndo = null!, _btnRedo = null!;

    public List<TileDef> ResultTiles => tiles;

    public TileEditorForm(string assetsPath, List<TileDef> currentTiles)
    {
        this.assetsPath = assetsPath;
        tiles = currentTiles.Select(t => new TileDef
        {
            id = t.id, name = t.name, color = t.color,
            collidable = t.collidable, deadly = t.deadly, sprite = t.sprite
        }).ToList();

        InitUI();
        LoadGrid();
        PushHistory();
    }

    private void InitUI()
    {
        Text = "タイル定義エディタ";
        Size = new Size(820, 560);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // ==== 上部: 検索ボックス ====
        // Feature: レイアウト修正 — 固定座標(Location)ではなくDockベースにすることで、
        // Form.Size(タイトルバー等を含む外形)とClientSize(実際の描画領域)の取り違えによる
        // 下部見切れを構造的に起こらないようにする。
        var pnlSearch = new Panel { Dock = DockStyle.Top, Height = 30 };
        var lblSearch = new Label { Text = "🔍", Location = new Point(5, 6), Size = new Size(20, 20) };
        txtSearch = new TextBox { Location = new Point(25, 3), Size = new Size(220, 23), PlaceholderText = "ID・名前で検索..." };
        txtSearch.TextChanged += (s, e) => ApplySearchFilter();
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch });

        // ==== 右側: プレビューパネル ====
        pnlPreview = new Panel
        {
            Dock = DockStyle.Right,
            Width = 120,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // ==== 下部: ボタン（右詰めFlowLayoutPanelで自動配置、はみ出す心配がない） ====
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        btnClose = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        btnClose.Click += (s, e) => Close();
        btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;
        flowRight.Controls.Add(btnClose);
        flowRight.Controls.Add(btnSave);
        var flowLeft = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), AutoSize = true };
        btnAdd = new Button { Text = "＋ タイル追加", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnAdd.Click += BtnAdd_Click;
        var btnDel = new Button { Text = "🗑 削除", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnDel.Click += BtnDel_Click;
        var btnDuplicate = new Button { Text = "⧉ 複製", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnDuplicate.Click += BtnDuplicate_Click;
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(8, 5, 8, 5), Enabled = false };
        _btnUndo.Click += (s, e) => TilesUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(8, 5, 8, 5), Enabled = false };
        _btnRedo.Click += (s, e) => TilesRedo();
        flowLeft.Controls.AddRange(new Control[] { btnAdd, btnDel, btnDuplicate, _btnUndo, _btnRedo });
        pnlBottom.Controls.Add(flowRight);
        pnlBottom.Controls.Add(flowLeft);

        // ==== 中央: DataGridView ====
        dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9)
        };

        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "ID", ReadOnly = true, FillWeight = 30 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "名前", FillWeight = 100 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "color", HeaderText = "色(#RRGGBB)", FillWeight = 80 });
        dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "collidable", HeaderText = "当たり判定", FillWeight = 60 });
        dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "deadly", HeaderText = "即死", FillWeight = 40 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "sprite", HeaderText = "画像パス", FillWeight = 120 });

        // ボタン列: 色選択
        var colColor = new DataGridViewButtonColumn { Name = "btnColor", HeaderText = "色選択", Text = "🎨", UseColumnTextForButtonValue = true, FillWeight = 40 };
        dgv.Columns.Add(colColor);
        // ボタン列: ファイル選択
        var colFile = new DataGridViewButtonColumn { Name = "btnFile", HeaderText = "画像選択", Text = "📁", UseColumnTextForButtonValue = true, FillWeight = 40 };
        dgv.Columns.Add(colFile);

        dgv.CellContentClick += Dgv_CellContentClick;
        dgv.SelectionChanged += Dgv_SelectionChanged;
        dgv.CellValueChanged += (s, e) => PushHistory();

        Controls.Add(dgv);
        Controls.Add(pnlPreview);
        Controls.Add(pnlBottom);
        Controls.Add(pnlSearch);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { TilesUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { TilesRedo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private List<TileDef> ReadTilesFromGrid()
    {
        var result = new List<TileDef>();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (!int.TryParse(row.Cells["id"].Value?.ToString(), out int id)) continue;
            result.Add(new TileDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                color = row.Cells["color"].Value?.ToString() ?? "#CCCCCC",
                collidable = row.Cells["collidable"].Value is true,
                deadly = row.Cells["deadly"].Value is true,
                sprite = row.Cells["sprite"].Value?.ToString() ?? ""
            });
        }
        return result;
    }

    private void PushHistory()
    {
        _history.Push(ReadTilesFromGrid());
        UpdateUndoRedoButtons();
    }

    private void TilesUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        tiles = restored;
        LoadGrid();
        UpdateUndoRedoButtons();
    }

    private void TilesRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        tiles = restored;
        LoadGrid();
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    private void LoadGrid()
    {
        dgv.Rows.Clear();
        foreach (var t in tiles)
        {
            int rowIdx = dgv.Rows.Add(t.id, t.name, t.color, t.collidable, t.deadly, t.sprite, "🎨", "📁");
            // セルの背景色
            try { dgv.Rows[rowIdx].Cells["color"].Style.BackColor = ColorTranslator.FromHtml(t.color); } catch { }
        }
    }

    private void Dgv_SelectionChanged(object? sender, EventArgs e)
    {
        if (dgv.SelectedRows.Count == 0) return;
        var row = dgv.SelectedRows[0];
        string colorStr = row.Cells["color"].Value?.ToString() ?? "#CCCCCC";
        try
        {
            var c = ColorTranslator.FromHtml(colorStr);
            pnlPreview.BackColor = c;
        }
        catch { pnlPreview.BackColor = Color.Gray; }
    }

    private void Dgv_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = dgv.Rows[e.RowIndex];

        if (dgv.Columns[e.ColumnIndex].Name == "btnColor")
        {
            using var cd = new ColorDialog();
            try { cd.Color = ColorTranslator.FromHtml(row.Cells["color"].Value?.ToString() ?? "#CCCCCC"); } catch { }
            if (cd.ShowDialog() == DialogResult.OK)
            {
                string hex = $"#{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                row.Cells["color"].Value = hex;
                row.Cells["color"].Style.BackColor = cd.Color;
                pnlPreview.BackColor = cd.Color;
            }
        }
        else if (dgv.Columns[e.ColumnIndex].Name == "btnFile")
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "スプライト画像を選択" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                // プロジェクトルートからの相対パスに変換
                string rel = Path.GetRelativePath(
                    Path.GetDirectoryName(assetsPath)!,
                    ofd.FileName).Replace('\\', '/');
                row.Cells["sprite"].Value = rel;
            }
        }
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        int newId = tiles.Count > 0 ? tiles.Max(t => t.id) + 1 : 0;
        tiles.Add(new TileDef { id = newId, name = $"新タイル{newId}", color = "#888888" });
        LoadGrid();
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        PushHistory();
    }

    private void BtnDel_Click(object? sender, EventArgs e)
    {
        if (dgv.SelectedRows.Count == 0) return;
        int rowIdx = dgv.SelectedRows[0].Index;
        int id = (int)(dgv.Rows[rowIdx].Cells["id"].Value ?? 0);
        if (id == 0) { MessageBox.Show("ID=0 のタイルは削除できません", "エラー"); return; }
        tiles.RemoveAll(t => t.id == id);
        LoadGrid();
        PushHistory();
    }

    private void ApplySearchFilter()
    {
        string q = txtSearch.Text.Trim();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (string.IsNullOrEmpty(q)) { row.Visible = true; continue; }
            string id = row.Cells["id"].Value?.ToString() ?? "";
            string name = row.Cells["name"].Value?.ToString() ?? "";
            row.Visible = id.Contains(q, StringComparison.OrdinalIgnoreCase) || name.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void BtnDuplicate_Click(object? sender, EventArgs e)
    {
        if (dgv.SelectedRows.Count == 0) { MessageBox.Show("複製するタイルを選択してください。"); return; }
        var row = dgv.SelectedRows[0];
        int newId = tiles.Count > 0 ? tiles.Max(t => t.id) + 1 : 1;
        var src = new TileDef
        {
            id = newId,
            name = (row.Cells["name"].Value?.ToString() ?? "") + "のコピー",
            color = row.Cells["color"].Value?.ToString() ?? "#CCCCCC",
            collidable = row.Cells["collidable"].Value is true,
            deadly = row.Cells["deadly"].Value is true,
            sprite = row.Cells["sprite"].Value?.ToString() ?? ""
        };
        tiles.Add(src);
        LoadGrid();
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        PushHistory();
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        tiles = ReadTilesFromGrid();
        string path = Path.Combine(assetsPath, "tiles.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(tiles, Formatting.Indented));
        MessageBox.Show("タイル定義を保存しました！", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
