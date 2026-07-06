using Newtonsoft.Json;

namespace Lab_Editor;

/// <summary>
/// アセット管理 - 敵・ギミック・アイテムの総合編集ウィンドウ
/// ・画像プレビュー付き
/// ・ファイルダイアログでスプライト選択（imgフォルダへ自動コピー）
/// ・敵の巡回範囲・HP・サイズ等フル編集
/// ・type_enumごとのパラメータ説明
/// ・ギミック・アイテムもフル編集
/// </summary>
public partial class AssetManagerForm : Form
{
    private readonly string assetsPath;
    private readonly string projectRoot;
    private AssetDefinitions assets;

    private TabControl tabControl = null!;
    private DataGridView dgvEnemies = null!, dgvGimmicks = null!, dgvItems = null!;
    private PictureBox pbPreview = null!;
    private Label lblPreviewPath = null!;
    private Button btnSave = null!, btnClose = null!;
    private RichTextBox rtbTypeHint = null!;

    // type_enum の説明
    private static readonly (int type, string desc, string detail)[] EnemyTypes =
    {
        (0, "0 = 巡回 (Patrol)", "左右にpatrolLeft～patrolRightの範囲で巡回します。\npatrol_left/patrol_rightをステージJSON配置時に指定可能。"),
        (1, "1 = ジャンプ (Jumper)", "その場で定期的にジャンプします。重力が適用されます。"),
        (2, "2 = 固定砲台 (Stationary)", "プレイヤーに向いて定期的に弾を撃ちます。移動しません。"),
        (3, "3 = 巡回砲台 (Patrol+Shoot)", "近づいたプレイヤーを攻撃し、それ以外は巡回します。"),
    };
    private static readonly (int type, string desc)[] GimmickTypes =
    {
        (0, "0 = ポータル"),
        (1, "1 = 回転橋(自動)"),
        (2, "2 = 回転橋(手動)"),
        (3, "3 = 破壊ブロック"),
        (4, "4 = 落下リフト"),
        (5, "5 = 反射鏡"),
        (6, "6 = 重量スイッチ"),
        (7, "7 = スケールボックス"),
        (8, "8 = ゲート扉"),
        (9, "9 = 棘床"),
        (10, "10 = スケール地面"),
        (11, "11 = ちくわブロック"),
        (12, "12 = 時間フィールド"),
        (13, "13 = 食らいギミック"),
    };
    private static readonly (int type, string desc)[] ItemTypes =
    {
        (0, "0 = なし"),
        (1, "1 = コイン"),
        (2, "2 = 回復アイテム"),
    };

    public AssetManagerForm(string assetsPath, AssetDefinitions assets)
    {
        this.assetsPath = assetsPath;
        this.projectRoot = Path.GetDirectoryName(assetsPath)!;
        this.assets = assets;
        InitUI();
        LoadData();
    }

    private void InitUI()
    {
        Text = "アセット管理エディタ - 敵 / ギミック / アイテム";
        Size = new Size(1100, 620);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        tabControl = new TabControl { Location = new Point(5, 5), Size = new Size(820, 550) };

        // ===== 右サイドパネル =====
        var pnlRight = new Panel { Location = new Point(835, 5), Size = new Size(250, 550), BorderStyle = BorderStyle.FixedSingle };

        var lblPrev = new Label { Text = "🖼 スプライトプレビュー", Location = new Point(5, 5), Size = new Size(240, 20), Font = new Font("Meiryo UI", 9, FontStyle.Bold) };
        pbPreview = new PictureBox { Location = new Point(5, 28), Size = new Size(238, 180), BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
        lblPreviewPath = new Label { Location = new Point(5, 212), Size = new Size(238, 40), Font = new Font("Meiryo UI", 7), ForeColor = Color.Gray, Text = "(選択なし)" };

        var lblTypeHintTitle = new Label { Text = "📋 タイプ説明", Location = new Point(5, 258), Size = new Size(238, 20), Font = new Font("Meiryo UI", 9, FontStyle.Bold) };
        rtbTypeHint = new RichTextBox
        {
            Location = new Point(5, 280),
            Size = new Size(238, 265),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Meiryo UI", 8),
            BackColor = Color.FromArgb(250, 250, 250),
            BorderStyle = BorderStyle.None
        };

        pnlRight.Controls.AddRange(new Control[] { lblPrev, pbPreview, lblPreviewPath, lblTypeHintTitle, rtbTypeHint });

        // ===== 敵タブ =====
        var tabEnemies = new TabPage("👾 敵 (Enemies)");
        dgvEnemies = CreateEnemyGrid();
        tabEnemies.Controls.Add(dgvEnemies);

        // ===== ギミックタブ =====
        var tabGimmicks = new TabPage("🔧 ギミック (Gimmicks)");
        dgvGimmicks = CreateGimmickGrid();
        tabGimmicks.Controls.Add(dgvGimmicks);

        // ===== アイテムタブ =====
        var tabItems = new TabPage("💎 アイテム (Items)");
        dgvItems = CreateItemGrid();
        tabItems.Controls.Add(dgvItems);

        tabControl.TabPages.AddRange(new TabPage[] { tabEnemies, tabGimmicks, tabItems });
        tabControl.SelectedIndexChanged += (s, e) => UpdateTypeHint();

        // ===== 下部ボタン =====
        var pnlBottom = new Panel { Location = new Point(5, 558), Size = new Size(1082, 40) };

        btnSave = new Button
        {
            Text = "💾 保存して閉じる",
            Location = new Point(720, 5), Size = new Size(160, 30),
            BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Font = new Font("Meiryo UI", 10, FontStyle.Bold)
        };
        btnSave.Click += BtnSave_Click;

        btnClose = new Button { Text = "キャンセル", Location = new Point(600, 5), Size = new Size(110, 30) };
        btnClose.Click += (s, e) => Close();

        var btnAddEnemy = new Button { Text = "＋ 敵追加", Location = new Point(5, 5), Size = new Size(100, 30) };
        btnAddEnemy.Click += (s, e) => AddRow(dgvEnemies, GetDefaultEnemyRow());

        var btnAddGimmick = new Button { Text = "＋ ギミック追加", Location = new Point(110, 5), Size = new Size(120, 30) };
        btnAddGimmick.Click += (s, e) => AddRow(dgvGimmicks, GetDefaultGimmickRow());

        var btnAddItem = new Button { Text = "＋ アイテム追加", Location = new Point(235, 5), Size = new Size(120, 30) };
        btnAddItem.Click += (s, e) => AddRow(dgvItems, GetDefaultItemRow());

        pnlBottom.Controls.AddRange(new Control[] { btnAddEnemy, btnAddGimmick, btnAddItem, btnClose, btnSave });

        Controls.AddRange(new Control[] { tabControl, pnlRight, pnlBottom });
        UpdateTypeHint();
    }

    private DataGridView CreateEnemyGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id",       HeaderText="ID",       FillWeight=80 },
            new DataGridViewTextBoxColumn { Name="name",     HeaderText="名前",     FillWeight=100 },
            new DataGridViewComboBoxColumn
            {
                Name="type_enum", HeaderText="タイプ", FillWeight=100,
                DataSource = EnemyTypes.Select(t => t.desc).ToArray(),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            },
            new DataGridViewTextBoxColumn { Name="hp",     HeaderText="HP",        FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="width",  HeaderText="幅px",       FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="height", HeaderText="高さpx",     FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="sprite", HeaderText="画像パス",   FillWeight=160, ReadOnly=true },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択",  Text="📁", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",   Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });

        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => UpdatePreview(dgv);
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        return dgv;
    }

    private DataGridView CreateGimmickGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id",       HeaderText="ID",      FillWeight=80 },
            new DataGridViewTextBoxColumn { Name="name",     HeaderText="名前",    FillWeight=120 },
            new DataGridViewComboBoxColumn
            {
                Name="type_enum", HeaderText="タイプ", FillWeight=120,
                DataSource = GimmickTypes.Select(t => t.desc).ToArray(),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            },
            new DataGridViewTextBoxColumn { Name="sprite", HeaderText="画像パス", FillWeight=200, ReadOnly=true },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択", Text="📁", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",  Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });

        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => UpdatePreview(dgv);
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    private DataGridView CreateItemGrid()
    {
        var dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9),
            RowHeadersWidth = 25
        };

        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id",           HeaderText="ID",        FillWeight=80 },
            new DataGridViewTextBoxColumn { Name="name",         HeaderText="名前",      FillWeight=100 },
            new DataGridViewComboBoxColumn
            {
                Name="type_enum", HeaderText="タイプ", FillWeight=100,
                DataSource = ItemTypes.Select(t => t.desc).ToArray(),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
            },
            new DataGridViewTextBoxColumn { Name="sprite",        HeaderText="画像パス",  FillWeight=180, ReadOnly=true },
            new DataGridViewTextBoxColumn { Name="grant_ability", HeaderText="付与能力",  FillWeight=100 },
            new DataGridViewButtonColumn  { Name="btnSprite", HeaderText="📁選択", Text="📁", UseColumnTextForButtonValue=true, FillWeight=35 },
            new DataGridViewButtonColumn  { Name="btnDel",    HeaderText="🗑削除",  Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });

        dgv.CellContentClick += (s, e) => HandleGridButton(dgv, e);
        dgv.SelectionChanged += (s, e) => UpdatePreview(dgv);
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.DataError += (s, e) => HandleDataError(dgv, e);
        return dgv;
    }

    // ===== 行操作 =====
    private void HandleGridButton(DataGridView dgv, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        string colName = dgv.Columns[e.ColumnIndex].Name;

        if (colName == "btnSprite")
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "スプライト画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            // imgフォルダへコピー
            string imgDir = Path.Combine(projectRoot, "img");
            Directory.CreateDirectory(imgDir);
            string destPath = Path.Combine(imgDir, Path.GetFileName(ofd.FileName));
            if (!destPath.Equals(ofd.FileName, StringComparison.OrdinalIgnoreCase))
                File.Copy(ofd.FileName, destPath, overwrite: true);

            // 相対パスとして保存（ゲームはimg/xxxx.pngを使用）
            string relPath = "img/" + Path.GetFileName(ofd.FileName);
            dgv.Rows[e.RowIndex].Cells["sprite"].Value = relPath;
            ShowPreview(destPath);
            lblPreviewPath.Text = relPath;
        }
        else if (colName == "btnDel")
        {
            if (MessageBox.Show("この行を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                dgv.Rows.RemoveAt(e.RowIndex);
        }
    }

    private void AddRow(DataGridView dgv, object[] values)
    {
        dgv.Rows.Add(values);
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        dgv.FirstDisplayedScrollingRowIndex = dgv.Rows.Count - 1;
    }

    private void HandleDataError(DataGridView dgv, DataGridViewDataErrorEventArgs e)
    {
        string colName = dgv.Columns[e.ColumnIndex].Name;
        object val = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        string items = "";
        int dsCount = 0;
        if (dgv.Columns[e.ColumnIndex] is DataGridViewComboBoxColumn cb)
        {
            var ds = cb.DataSource as string[];
            if (ds != null) {
                items = string.Join(", ", ds);
                dsCount = ds.Length;
            }
        }
        string msg = $@"[DataGridViewComboBoxCell Error]
Form: {this.Name}
DataGridView: {dgv.Name}
Row: {e.RowIndex}
Col: {e.ColumnIndex}
Column.Name: {colName}
Column.HeaderText: {dgv.Columns[e.ColumnIndex].HeaderText}
Cell.Value: '{val}'
Cell.FormattedValue: '{dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue}'
Cell.ValueType: {dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].ValueType}
ComboBox.Items: [{items}]
ComboBox.DataSourceCount: {dsCount}
ValueMember: {(dgv.Columns[e.ColumnIndex] as DataGridViewComboBoxColumn)?.ValueMember}
DisplayMember: {(dgv.Columns[e.ColumnIndex] as DataGridViewComboBoxColumn)?.DisplayMember}
Exception.Message: {e.Exception.Message}
Exception.StackTrace: {e.Exception.StackTrace}";

        System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\error_detail.log", msg + "\n\n");
        throw new Exception(msg, e.Exception);
    }

    private object[] GetDefaultEnemyRow()
    {
        string newId = $"enemy_{assets.Enemies.Count + dgvEnemies.Rows.Count + 1}";
        return new object[] { newId, "新敵", EnemyTypes[0].desc, 3, 32, 32, "", "画像選択", "削除" };
    }

    private object[] GetDefaultGimmickRow()
    {
        string newId = $"gimmick_{assets.Gimmicks.Count + dgvGimmicks.Rows.Count + 1}";
        return new object[] { newId, "新しいギミック", GimmickTypes[0].desc, "", "📁", "🗑" };
    }

    private object[] GetDefaultItemRow()
    {
        string newId = $"item_{assets.Items.Count + dgvItems.Rows.Count + 1}";
        return new object[] { newId, "新しいアイテム", ItemTypes[0].desc, "", "", "📁", "🗑" };
    }

    // ===== プレビュー更新 =====
    private void UpdatePreview(DataGridView dgv)
    {
        if (dgv.SelectedRows.Count == 0) return;
        var row = dgv.SelectedRows[0];
        if (!dgv.Columns.Contains("sprite")) return;
        string sp = row.Cells["sprite"].Value?.ToString() ?? "";
        if (string.IsNullOrEmpty(sp)) { pbPreview.Image = null; lblPreviewPath.Text = "(画像なし)"; return; }
        string fullPath = Path.Combine(projectRoot, sp.Replace('/', '\\'));
        ShowPreview(fullPath);
        lblPreviewPath.Text = sp;
    }

    private void ShowPreview(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // ファイルロックを避けるためにStreamで読む
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                pbPreview.Image = Image.FromStream(fs);
            }
            else
            {
                pbPreview.Image = null;
                lblPreviewPath.Text = "⚠ ファイルが見つかりません";
            }
        }
        catch { pbPreview.Image = null; }
    }

    private void UpdateTypeHint()
    {
        rtbTypeHint.Clear();
        switch (tabControl.SelectedIndex)
        {
            case 0:
                rtbTypeHint.AppendText("【敵タイプ一覧】\n\n");
                foreach (var (type, desc, detail) in EnemyTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkBlue;
                    rtbTypeHint.AppendText(desc + "\n");
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 7.5f);
                    rtbTypeHint.SelectionColor = Color.DarkGray;
                    rtbTypeHint.AppendText(detail + "\n\n");
                }
                break;
            case 1:
                rtbTypeHint.AppendText("【ギミックタイプ一覧】\n\n");
                foreach (var (type, desc) in GimmickTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkGreen;
                    rtbTypeHint.AppendText(desc + "\n");
                }
                break;
            case 2:
                rtbTypeHint.AppendText("【アイテムタイプ一覧】\n\n");
                foreach (var (type, desc) in ItemTypes)
                {
                    rtbTypeHint.SelectionFont = new Font("Meiryo UI", 8, FontStyle.Bold);
                    rtbTypeHint.SelectionColor = Color.DarkRed;
                    rtbTypeHint.AppendText(desc + "\n");
                }
                rtbTypeHint.AppendText("\n【grant_ability フィールド】\n");
                rtbTypeHint.AppendText("取得時にプレイヤーに付与する能力名を入力。\n例: canDoubleJump, canDash, canShootFireball\n");
                break;
        }
    }

    // ===== データ読み込み =====
    private void LoadData()
    {
        dgvEnemies.Rows.Clear();
        foreach (var e in assets.Enemies)
        {
            string typeLabel = EnemyTypes.FirstOrDefault(t => t.type == e.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !EnemyTypes.Any(t => t.desc == typeLabel)) 
            {
                System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\warning_log.txt", $"[WARNING] AssetManagerForm: Enemy ID '{e.id}' has invalid type_enum '{e.type_enum}'. Auto-converted to default.\n");
                typeLabel = EnemyTypes[0].desc;
            }
            dgvEnemies.Rows.Add(e.id, e.name, typeLabel, e.hp, e.width, e.height, e.sprite, "📁", "🗑");
        }

        dgvGimmicks.Rows.Clear();
        foreach (var g in assets.Gimmicks)
        {
            string typeLabel = GimmickTypes.FirstOrDefault(t => t.type == g.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !GimmickTypes.Any(t => t.desc == typeLabel)) 
            {
                System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\warning_log.txt", $"[WARNING] AssetManagerForm: Gimmick ID '{g.id}' has invalid type_enum '{g.type_enum}'. Auto-converted to default.\n");
                typeLabel = GimmickTypes[0].desc;
            }
            dgvGimmicks.Rows.Add(g.id, g.name, typeLabel, g.sprite, "📁", "🗑");
        }

        dgvItems.Rows.Clear();
        foreach (var i in assets.Items)
        {
            string typeLabel = ItemTypes.FirstOrDefault(t => t.type == i.type_enum).desc;
            if (string.IsNullOrEmpty(typeLabel) || !ItemTypes.Any(t => t.desc == typeLabel)) 
            {
                System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\warning_log.txt", $"[WARNING] AssetManagerForm: Item ID '{i.id}' has invalid type_enum '{i.type_enum}'. Auto-converted to default.\n");
                typeLabel = ItemTypes[0].desc;
            }
            dgvItems.Rows.Add(i.id, i.name, typeLabel, i.sprite, i.grant_ability, "📁", "🗑");
        }
    }

    // ===== 保存 =====
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        assets.Enemies = ReadEnemies();
        assets.Gimmicks = ReadGimmicks();
        assets.Items = ReadItems();
        assets.SaveToFolder(assetsPath);
        MessageBox.Show("アセット定義を保存しました！\n\n※画像はimgフォルダへコピー済みです。\nゲームを再ビルドすると新しいスプライトが反映されます。",
            "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }

    private List<EnemyDef> ReadEnemies()
    {
        var list = new List<EnemyDef>();
        foreach (DataGridViewRow row in dgvEnemies.Rows)
        {
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;

            // type_enum: コンボボックスの選択インデックスから取得
            int typeIdx = 0;
            string typeStr = row.Cells["type_enum"].Value?.ToString() ?? "";
            for (int i = 0; i < EnemyTypes.Length; i++)
                if (EnemyTypes[i].desc.Split('=')[1].Trim().Split(' ')[0] == typeStr ||
                    typeStr == i.ToString() || EnemyTypes[i].desc.Contains(typeStr)) { typeIdx = i; break; }
            // ComboBoxのインデックスで取得試み
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            list.Add(new EnemyDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                type_enum = typeIdx,
                hp = IntCell(row, "hp", 3),
                width = IntCell(row, "width", 32),
                height = IntCell(row, "height", 32),
                sprite = row.Cells["sprite"].Value?.ToString() ?? ""
            });
        }
        return list;
    }

    private List<GimmickDef> ReadGimmicks()
    {
        var list = new List<GimmickDef>();
        foreach (DataGridViewRow row in dgvGimmicks.Rows)
        {
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;

            int typeIdx = 0;
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            list.Add(new GimmickDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                type_enum = typeIdx,
                sprite = row.Cells["sprite"].Value?.ToString() ?? ""
            });
        }
        return list;
    }

    private List<ItemDef> ReadItems()
    {
        var list = new List<ItemDef>();
        foreach (DataGridViewRow row in dgvItems.Rows)
        {
            if (row.IsNewRow) continue;
            string? id = row.Cells["id"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;

            int typeIdx = 0;
            if (row.Cells["type_enum"] is DataGridViewComboBoxCell combo)
            {
                var vals = (string[]?)combo.DataSource;
                if (vals != null)
                {
                    int foundIdx = Array.IndexOf(vals, combo.Value?.ToString() ?? "");
                    if (foundIdx >= 0) typeIdx = foundIdx;
                }
            }

            list.Add(new ItemDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                type_enum = typeIdx,
                sprite = row.Cells["sprite"].Value?.ToString() ?? "",
                grant_ability = row.Cells["grant_ability"].Value?.ToString() ?? ""
            });
        }
        return list;
    }

    private static int IntCell(DataGridViewRow row, string col, int def = 0)
        => int.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;
}
