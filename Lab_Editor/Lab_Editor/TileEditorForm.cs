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
            collidable = t.collidable, deadly = t.deadly, sprite = t.sprite,
            srcX = t.srcX, srcY = t.srcY, srcW = t.srcW, srcH = t.srcH,
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
        // Feature: タイル表示範囲調整機能 — 従来は選択中タイルの色を塗りつぶすだけだったが、
        // 実際のスプライト画像を「表示範囲」で切り出した状態でそのまま描画し、ゲーム内で
        // 実際にどう見えるかをそのまま確認できるようにする。
        pnlPreview = new Panel
        {
            Dock = DockStyle.Right,
            Width = 140,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(40, 40, 40),
        };
        pnlPreview.Paint += PnlPreview_Paint;

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

        // Feature: タイル表示範囲調整機能 — spriteがタイルセット画像の場合に、そのうちどの矩形を使うか
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcX", HeaderText = "範囲X", FillWeight = 30, ValueType = typeof(int) });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcY", HeaderText = "範囲Y", FillWeight = 30, ValueType = typeof(int) });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcW", HeaderText = "範囲W", FillWeight = 30, ValueType = typeof(int) });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcH", HeaderText = "範囲H", FillWeight = 30, ValueType = typeof(int) });

        // ボタン列: 色選択
        var colColor = new DataGridViewButtonColumn { Name = "btnColor", HeaderText = "色選択", Text = "🎨", UseColumnTextForButtonValue = true, FillWeight = 40 };
        dgv.Columns.Add(colColor);
        // ボタン列: ファイル選択
        var colFile = new DataGridViewButtonColumn { Name = "btnFile", HeaderText = "画像選択", Text = "📁", UseColumnTextForButtonValue = true, FillWeight = 40 };
        dgv.Columns.Add(colFile);
        // ボタン列: 表示範囲をドラッグで選択
        var colRegion = new DataGridViewButtonColumn { Name = "btnRegion", HeaderText = "表示範囲", Text = "🖼 範囲", UseColumnTextForButtonValue = true, FillWeight = 45 };
        dgv.Columns.Add(colRegion);

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

    private static int IntCell(DataGridViewRow row, string col) => int.TryParse(row.Cells[col].Value?.ToString(), out int v) ? v : 0;

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
                sprite = row.Cells["sprite"].Value?.ToString() ?? "",
                srcX = IntCell(row, "srcX"),
                srcY = IntCell(row, "srcY"),
                srcW = IntCell(row, "srcW"),
                srcH = IntCell(row, "srcH"),
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
            int rowIdx = dgv.Rows.Add(t.id, t.name, t.color, t.collidable, t.deadly, t.sprite, t.srcX, t.srcY, t.srcW, t.srcH, "🎨", "📁", "🖼 範囲");
            // セルの背景色
            try { dgv.Rows[rowIdx].Cells["color"].Style.BackColor = ColorTranslator.FromHtml(t.color); } catch { }
        }
    }

    private void Dgv_SelectionChanged(object? sender, EventArgs e)
    {
        pnlPreview.Invalidate();
    }

    // Feature: タイル表示範囲調整機能 — 選択中タイルのスプライトを、表示範囲(srcX/Y/W/H)で
    // 切り出した状態でそのまま描画する。範囲が未設定(0)なら画像全体を使う（従来互換）。
    private void PnlPreview_Paint(object? sender, PaintEventArgs e)
    {
        if (dgv.SelectedRows.Count == 0) return;
        var row = dgv.SelectedRows[0];
        string colorStr = row.Cells["color"].Value?.ToString() ?? "#CCCCCC";
        Color fallback;
        try { fallback = ColorTranslator.FromHtml(colorStr); } catch { fallback = Color.Gray; }

        string spritePath = row.Cells["sprite"].Value?.ToString() ?? "";
        string full = string.IsNullOrEmpty(spritePath) ? "" : Path.Combine(Path.GetDirectoryName(assetsPath)!, spritePath.Replace('/', '\\'));

        if (string.IsNullOrEmpty(full) || !File.Exists(full))
        {
            using var b = new SolidBrush(fallback);
            e.Graphics.FillRectangle(b, 4, 4, pnlPreview.Width - 8, 100);
            return;
        }

        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var img = Image.FromStream(fs);
            int sx = IntCell(row, "srcX"), sy = IntCell(row, "srcY"), sw = IntCell(row, "srcW"), sh = IntCell(row, "srcH");
            if (sw <= 0 || sh <= 0) { sx = 0; sy = 0; sw = img.Width; sh = img.Height; }
            sx = Math.Clamp(sx, 0, Math.Max(0, img.Width - 1));
            sy = Math.Clamp(sy, 0, Math.Max(0, img.Height - 1));
            sw = Math.Clamp(sw, 1, img.Width - sx);
            sh = Math.Clamp(sh, 1, img.Height - sy);

            float scale = Math.Min((float)(pnlPreview.Width - 16) / sw, 140f / sh);
            if (scale <= 0) scale = 1;
            int drawW = Math.Max(1, (int)(sw * scale));
            int drawH = Math.Max(1, (int)(sh * scale));
            int drawX = (pnlPreview.Width - drawW) / 2;
            int drawY = 8;

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(img, new Rectangle(drawX, drawY, drawW, drawH), new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
            e.Graphics.DrawRectangle(Pens.DimGray, drawX, drawY, drawW, drawH);

            using var hint = new Font(Font.FontFamily, 7.5f);
            e.Graphics.DrawString($"表示範囲:\n{sw}x{sh}px", hint, Brushes.LightGray, 6, drawY + drawH + 8);
        }
        catch
        {
            using var b = new SolidBrush(fallback);
            e.Graphics.FillRectangle(b, 4, 4, pnlPreview.Width - 8, 100);
        }
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
                pnlPreview.Invalidate();
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
                // 新しい画像を選んだ場合、表示範囲は一旦リセットして画像全体を使う状態に戻す
                row.Cells["srcX"].Value = 0;
                row.Cells["srcY"].Value = 0;
                row.Cells["srcW"].Value = 0;
                row.Cells["srcH"].Value = 0;
                pnlPreview.Invalidate();
            }
        }
        else if (dgv.Columns[e.ColumnIndex].Name == "btnRegion")
        {
            string spritePath = row.Cells["sprite"].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(spritePath))
            {
                MessageBox.Show("先に画像を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string full = Path.Combine(Path.GetDirectoryName(assetsPath)!, spritePath.Replace('/', '\\'));
            if (!File.Exists(full))
            {
                MessageBox.Show("画像ファイルが見つかりません:\n" + full, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            using var form = new TileRegionEditorForm(full, IntCell(row, "srcX"), IntCell(row, "srcY"), IntCell(row, "srcW"), IntCell(row, "srcH"));
            if (form.ShowDialog() == DialogResult.OK)
            {
                row.Cells["srcX"].Value = form.SrcX;
                row.Cells["srcY"].Value = form.SrcY;
                row.Cells["srcW"].Value = form.SrcWidth;
                row.Cells["srcH"].Value = form.SrcHeight;
                pnlPreview.Invalidate();
                PushHistory();
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
            sprite = row.Cells["sprite"].Value?.ToString() ?? "",
            srcX = IntCell(row, "srcX"),
            srcY = IntCell(row, "srcY"),
            srcW = IntCell(row, "srcW"),
            srcH = IntCell(row, "srcH"),
        };
        tiles.Add(src);
        LoadGrid();
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        PushHistory();
    }

    // Feature: UI改善（提案書 CUT-3）— IDの重複や名前未入力のまま保存すると、後から配置したステージ側で
    // 見た目や意図しないタイル参照になり気づきにくいため、保存前に警告する。
    private static List<string> ValidateTiles(List<TileDef> list)
    {
        var warnings = new List<string>();
        var dupIds = list.GroupBy(t => t.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupIds.Count > 0)
            warnings.Add($"IDが重複しています: {string.Join(", ", dupIds)}（後に定義した方だけが有効になります）。");
        var emptyNames = list.Where(t => string.IsNullOrWhiteSpace(t.name)).Select(t => t.id).ToList();
        if (emptyNames.Count > 0)
            warnings.Add($"名前が未入力のタイルがあります (ID: {string.Join(", ", emptyNames)})。");
        return warnings;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        tiles = ReadTilesFromGrid();

        var warnings = ValidateTiles(tiles);
        if (warnings.Count > 0)
        {
            string msg = "保存前に確認してください:\n\n" + string.Join("\n", warnings) + "\n\nこのまま保存しますか？";
            if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        string path = Path.Combine(assetsPath, "tiles.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(tiles, Formatting.Indented));
        MessageBox.Show("タイル定義を保存しました！", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
