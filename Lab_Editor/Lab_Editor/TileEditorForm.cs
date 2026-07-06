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
    }

    private void InitUI()
    {
        Text = "タイル定義エディタ";
        Size = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // DataGridView
        dgv = new DataGridView
        {
            Location = new Point(5, 5),
            Size = new Size(680, 440),
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

        // プレビューパネル
        pnlPreview = new Panel
        {
            Location = new Point(695, 5),
            Size = new Size(110, 110),
            BorderStyle = BorderStyle.FixedSingle
        };

        // ボタンパネル
        btnAdd = new Button { Text = "＋ タイル追加", Location = new Point(5, 455), Size = new Size(120, 30) };
        btnAdd.Click += BtnAdd_Click;

        var btnDel = new Button { Text = "🗑 削除", Location = new Point(135, 455), Size = new Size(90, 30) };
        btnDel.Click += BtnDel_Click;

        btnSave = new Button { Text = "💾 保存して閉じる", Location = new Point(570, 455), Size = new Size(150, 30), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;

        btnClose = new Button { Text = "キャンセル", Location = new Point(460, 455), Size = new Size(100, 30) };
        btnClose.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { dgv, pnlPreview, btnAdd, btnDel, btnSave, btnClose });
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
    }

    private void BtnDel_Click(object? sender, EventArgs e)
    {
        if (dgv.SelectedRows.Count == 0) return;
        int rowIdx = dgv.SelectedRows[0].Index;
        int id = (int)(dgv.Rows[rowIdx].Cells["id"].Value ?? 0);
        if (id == 0) { MessageBox.Show("ID=0 のタイルは削除できません", "エラー"); return; }
        tiles.RemoveAll(t => t.id == id);
        LoadGrid();
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        // DataGridView → tiles リストに書き戻す
        tiles.Clear();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (!int.TryParse(row.Cells["id"].Value?.ToString(), out int id)) continue;
            tiles.Add(new TileDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                color = row.Cells["color"].Value?.ToString() ?? "#CCCCCC",
                collidable = row.Cells["collidable"].Value is true,
                deadly = row.Cells["deadly"].Value is true,
                sprite = row.Cells["sprite"].Value?.ToString() ?? ""
            });
        }
        string path = Path.Combine(assetsPath, "tiles.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(tiles, Formatting.Indented));
        MessageBox.Show("タイル定義を保存しました！", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
