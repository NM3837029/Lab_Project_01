using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartsEditorForm - 複合オブジェクト（敵/ギミック/アイテム）のパーツ編集
// Feature: Composite Multi-Part Objects (Parts-M7)
//
// 1体の敵/ギミック/アイテムを、複数の画像パーツの組み合わせとして構成するためのエディタ。
// 左側にパーツ一覧グリッド、右側に合成プレビューキャンバス（本体の画像＋各パーツを
// ドラッグで配置できる）を配置する。当たり判定編集は既存のHitboxEditorFormを、
// 挙動スクリプト編集は既存のBehaviorScriptEditorFormをそれぞれパーツ単位でそのまま再利用する。
// ======================================================
public class PartsEditorForm : Form
{
    private readonly string projectRoot;
    private readonly string baseSpritePath;
    private readonly DataGridView dgv;
    private readonly Panel pnlComposer;
    private readonly Label lblComposerHint;
    private Image? baseSprite;
    private readonly Dictionary<PartDef, Image?> _partThumbCache = new();

    private List<PartDef> parts;
    public List<PartDef> ResultParts { get; private set; } = new();

    // ドラッグ状態（合成キャンバス上でのパーツ移動）
    private int _draggingIndex = -1;
    private Point _dragMouseStart;
    private float _dragOffsetStartX, _dragOffsetStartY;

    public PartsEditorForm(string subjectLabel, List<PartDef> initialParts, string projectRoot, string baseSpritePath)
    {
        this.projectRoot = projectRoot;
        this.baseSpritePath = baseSpritePath;
        parts = initialParts.Select(ClonePart).ToList();

        Text = $"🧩 パーツエディタ - {subjectLabel}";
        Size = new Size(980, 640);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        LoadBaseSprite();

        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = "パーツごとに画像・位置(offsetX/Y)・当たり判定・HP(0=不滅)・zOrder(奥/手前)・挙動スクリプトを設定します。右のプレビューでパーツをドラッグして位置を調整できます。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            BackColor = Color.FromArgb(230, 240, 255),
        };

        // ===== 左: パーツ一覧グリッド =====
        dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 8.5f),
            RowHeadersWidth = 25
        };
        dgv.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name="id", HeaderText="パーツID", FillWeight=90 },
            new DataGridViewTextBoxColumn { Name="offsetX", HeaderText="offsetX", FillWeight=55 },
            new DataGridViewTextBoxColumn { Name="offsetY", HeaderText="offsetY", FillWeight=55 },
            new DataGridViewTextBoxColumn { Name="hp", HeaderText="HP", FillWeight=40 },
            new DataGridViewTextBoxColumn { Name="zOrder", HeaderText="zOrder", FillWeight=45 },
            new DataGridViewTextBoxColumn { Name="sprite", HeaderText="画像", FillWeight=100, ReadOnly=true },
            new DataGridViewButtonColumn { Name="btnSprite", HeaderText="📁", Text="📁", UseColumnTextForButtonValue=true, FillWeight=30 },
            new DataGridViewButtonColumn { Name="btnHitbox", HeaderText="🎯", Text="🎯", UseColumnTextForButtonValue=true, FillWeight=30 },
            new DataGridViewButtonColumn { Name="btnScript", HeaderText="📝", Text="📝", UseColumnTextForButtonValue=true, FillWeight=30 },
            new DataGridViewButtonColumn { Name="btnDel", HeaderText="🗑", Text="🗑", UseColumnTextForButtonValue=true, FillWeight=30 },
        });
        dgv.CellContentClick += Dgv_CellContentClick;
        dgv.CellValueChanged += Dgv_CellValueChanged;
        dgv.CurrentCellDirtyStateChanged += (s, e) => { if (dgv.IsCurrentCellDirty) dgv.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        dgv.SelectionChanged += (s, e) => pnlComposer.Invalidate();

        var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 470 };
        var lblGridTitle = new Label { Dock = DockStyle.Top, Height = 22, Text = "📋 パーツ一覧", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 3, 0, 0) };
        var pnlGridButtons = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        var btnAddPart = new Button { Text = "＋ パーツ追加", Location = new Point(4, 3), Size = new Size(110, 26) };
        btnAddPart.Click += (s, e) => AddPart();
        var btnApplyScriptToAll = new Button { Text = "🧩 全パーツに同じスクリプトを適用", Location = new Point(118, 3), Size = new Size(220, 26) };
        btnApplyScriptToAll.Click += (s, e) => ApplyScriptToAllParts();
        pnlGridButtons.Controls.AddRange(new Control[] { btnAddPart, btnApplyScriptToAll });
        pnlLeft.Controls.Add(dgv);
        pnlLeft.Controls.Add(pnlGridButtons);
        pnlLeft.Controls.Add(lblGridTitle);

        // ===== 右: 合成プレビューキャンバス =====
        lblComposerHint = new Label { Dock = DockStyle.Top, Height = 22, Text = "🖼 合成プレビュー（パーツをドラッグして配置）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 3, 0, 0) };
        pnlComposer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(50, 50, 55) };
        pnlComposer.Paint += PnlComposer_Paint;
        pnlComposer.MouseDown += PnlComposer_MouseDown;
        pnlComposer.MouseMove += PnlComposer_MouseMove;
        pnlComposer.MouseUp += (s, e) => _draggingIndex = -1;
        var pnlRight = new Panel { Dock = DockStyle.Fill };
        pnlRight.Controls.Add(pnlComposer);
        pnlRight.Controls.Add(lblComposerHint);

        // ===== 下部ボタン =====
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        var btnOk = new Button { Text = "💾 OK", DialogResult = DialogResult.OK, Location = new Point(770, 4), Size = new Size(90, 30), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(870, 4), Size = new Size(90, 30) };
        btnOk.Click += (s, e) => { CommitGridToParts(); ResultParts = parts; };
        pnlBottom.Controls.AddRange(new Control[] { btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(pnlRight);
        Controls.Add(pnlLeft);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);

        RefreshGrid();
    }

    private static PartDef ClonePart(PartDef p) => new PartDef
    {
        id = p.id,
        sprite = p.sprite,
        offsetX = p.offsetX,
        offsetY = p.offsetY,
        width = p.width,
        height = p.height,
        hitboxOffsetX = p.hitboxOffsetX,
        hitboxOffsetY = p.hitboxOffsetY,
        hitboxWidth = p.hitboxWidth,
        hitboxHeight = p.hitboxHeight,
        scale = p.scale,
        hp = p.hp,
        zOrder = p.zOrder,
        script = (JArray)p.script.DeepClone(),
    };

    private void LoadBaseSprite()
    {
        if (string.IsNullOrEmpty(baseSpritePath)) return;
        string full = Path.Combine(projectRoot, baseSpritePath.Replace('/', '\\'));
        if (!File.Exists(full)) return;
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            baseSprite = Image.FromStream(fs);
        }
        catch { baseSprite = null; }
    }

    private Image? GetPartThumb(PartDef p)
    {
        if (_partThumbCache.TryGetValue(p, out var cached)) return cached;
        Image? img = null;
        if (!string.IsNullOrEmpty(p.sprite))
        {
            string full = Path.Combine(projectRoot, p.sprite.Replace('/', '\\'));
            if (File.Exists(full))
            {
                try
                {
                    using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                    img = Image.FromStream(fs);
                }
                catch { img = null; }
            }
        }
        _partThumbCache[p] = img;
        return img;
    }

    private void InvalidatePartThumb(PartDef p)
    {
        if (_partThumbCache.TryGetValue(p, out var old)) { old?.Dispose(); _partThumbCache.Remove(p); }
    }

    // ==== グリッド ⇔ partsモデルの同期 ====

    private void RefreshGrid()
    {
        dgv.CellValueChanged -= Dgv_CellValueChanged;
        dgv.Rows.Clear();
        foreach (var p in parts)
            dgv.Rows.Add(p.id, p.offsetX, p.offsetY, p.hp, p.zOrder, p.sprite, "📁", "🎯", "📝", "🗑");
        dgv.CellValueChanged += Dgv_CellValueChanged;
        pnlComposer.Invalidate();
    }

    // OKボタン押下時に、グリッドの数値セル（offsetX/Y, hp, zOrder, id）を最終的にpartsへ反映する
    private void CommitGridToParts()
    {
        for (int i = 0; i < parts.Count && i < dgv.Rows.Count; i++)
        {
            var row = dgv.Rows[i];
            parts[i].id = row.Cells["id"].Value?.ToString() ?? parts[i].id;
            parts[i].offsetX = FloatCell(row, "offsetX", parts[i].offsetX);
            parts[i].offsetY = FloatCell(row, "offsetY", parts[i].offsetY);
            parts[i].hp = IntCell(row, "hp", parts[i].hp);
            parts[i].zOrder = IntCell(row, "zOrder", parts[i].zOrder);
        }
    }

    private void Dgv_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= parts.Count) return;
        string colName = dgv.Columns[e.ColumnIndex].Name;
        var row = dgv.Rows[e.RowIndex];
        var p = parts[e.RowIndex];
        switch (colName)
        {
            case "id": p.id = row.Cells["id"].Value?.ToString() ?? p.id; break;
            case "offsetX": p.offsetX = FloatCell(row, "offsetX", p.offsetX); break;
            case "offsetY": p.offsetY = FloatCell(row, "offsetY", p.offsetY); break;
            case "hp": p.hp = IntCell(row, "hp", p.hp); break;
            case "zOrder": p.zOrder = IntCell(row, "zOrder", p.zOrder); break;
        }
        pnlComposer.Invalidate();
    }

    private static int IntCell(DataGridViewRow row, string col, int def)
        => int.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;
    private static float FloatCell(DataGridViewRow row, string col, float def)
        => float.TryParse(row.Cells[col].Value?.ToString(), out var v) ? v : def;

    private void Dgv_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= parts.Count) return;
        CommitGridToParts();
        string colName = dgv.Columns[e.ColumnIndex].Name;
        var p = parts[e.RowIndex];

        if (colName == "btnSprite")
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "パーツ画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            string relPath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            p.sprite = relPath;
            InvalidatePartThumb(p);
            RefreshGrid();
        }
        else if (colName == "btnHitbox")
        {
            string full = string.IsNullOrEmpty(p.sprite) ? "" : Path.Combine(projectRoot, p.sprite.Replace('/', '\\'));
            using var form = new HitboxEditorForm(full, p.hitboxOffsetX, p.hitboxOffsetY, p.hitboxWidth, p.hitboxHeight);
            if (form.ShowDialog() == DialogResult.OK)
            {
                p.hitboxOffsetX = form.HitboxOffsetX;
                p.hitboxOffsetY = form.HitboxOffsetY;
                p.hitboxWidth = form.HitboxWidth;
                p.hitboxHeight = form.HitboxHeight;
            }
        }
        else if (colName == "btnScript")
        {
            using var form = new BehaviorScriptEditorForm($"パーツ: {p.id}", p.script);
            if (form.ShowDialog() == DialogResult.OK) p.script = form.ResultScript;
        }
        else if (colName == "btnDel")
        {
            if (MessageBox.Show("このパーツを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                InvalidatePartThumb(p);
                parts.RemoveAt(e.RowIndex);
                RefreshGrid();
            }
        }
    }

    private void AddPart()
    {
        CommitGridToParts();
        string baseId = "part";
        int n = 1;
        var existing = new HashSet<string>(parts.Select(p => p.id));
        string newId;
        do { newId = $"{baseId}{n}"; n++; } while (existing.Contains(newId));
        parts.Add(new PartDef { id = newId, offsetX = 0, offsetY = 0 });
        RefreshGrid();
    }

    private void ApplyScriptToAllParts()
    {
        CommitGridToParts();
        if (parts.Count == 0) { MessageBox.Show("パーツがありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (dgv.SelectedRows.Count == 0) { MessageBox.Show("コピー元にするパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        int srcIdx = dgv.SelectedRows[0].Index;
        var srcScript = (JArray)parts[srcIdx].script.DeepClone();
        foreach (var p in parts) p.script = (JArray)srcScript.DeepClone();
        MessageBox.Show($"「{parts[srcIdx].id}」のスクリプトを全{parts.Count}パーツに適用しました。\n（PartIndexレポーターを使えば、同じスクリプトのままパーツごとに異なる位相・動きを表現できます）",
            "適用完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ==== 合成プレビューキャンバス ====

    private Rectangle GetBaseDrawRect()
    {
        if (baseSprite == null) return new Rectangle(0, 0, pnlComposer.Width, pnlComposer.Height);
        float scale = Math.Min((float)pnlComposer.Width * 0.6f / baseSprite.Width, (float)pnlComposer.Height * 0.6f / baseSprite.Height);
        if (scale > 10) scale = 10;
        int drawW = (int)(baseSprite.Width * scale);
        int drawH = (int)(baseSprite.Height * scale);
        int drawX = (pnlComposer.Width - drawW) / 2;
        int drawY = (pnlComposer.Height - drawH) / 2;
        return new Rectangle(drawX, drawY, drawW, drawH);
    }

    // offsetX/Y(ワールド座標系。本体の左上が原点)を、合成キャンバスのスクリーン座標へ変換する
    private PointF WorldToScreen(Rectangle baseRect, float scale, float ox, float oy)
        => new PointF(baseRect.X + ox * scale, baseRect.Y + oy * scale);

    private void PnlComposer_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var baseRect = GetBaseDrawRect();
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;

        if (baseSprite != null) e.Graphics.DrawImage(baseSprite, baseRect);
        e.Graphics.DrawRectangle(Pens.DimGray, baseRect);

        int selected = dgv.SelectedRows.Count > 0 ? dgv.SelectedRows[0].Index : -1;

        // zOrder順（奥から手前）に描画してプレビューの前後関係を実際の描画と一致させる
        var order = Enumerable.Range(0, parts.Count).OrderBy(i => parts[i].zOrder).ToList();
        foreach (int i in order)
        {
            var p = parts[i];
            var pos = WorldToScreen(baseRect, scale, p.offsetX, p.offsetY);
            var thumb = GetPartThumb(p);
            int pw = Math.Max((int)((thumb?.Width ?? 24) * scale * p.scale), 10);
            int ph = Math.Max((int)((thumb?.Height ?? 24) * scale * p.scale), 10);
            var rect = new Rectangle((int)pos.X - pw / 2, (int)pos.Y - ph / 2, pw, ph);

            if (thumb != null) e.Graphics.DrawImage(thumb, rect);
            else
            {
                using var b = new SolidBrush(Color.FromArgb(160, 255, 140, 0));
                e.Graphics.FillEllipse(b, rect);
            }
            using var pen = new Pen(i == selected ? Color.Yellow : Color.FromArgb(200, 255, 255, 255), i == selected ? 2.5f : 1.2f);
            e.Graphics.DrawRectangle(pen, rect);
            e.Graphics.DrawString(p.id, new Font(Font.FontFamily, 7.5f), Brushes.White, rect.X, rect.Bottom + 1);
        }
    }

    private int FindPartMarkerAt(Point pt, out Rectangle hitRect)
    {
        var baseRect = GetBaseDrawRect();
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;
        var order = Enumerable.Range(0, parts.Count).OrderByDescending(i => parts[i].zOrder).ToList();
        foreach (int i in order)
        {
            var p = parts[i];
            var pos = WorldToScreen(baseRect, scale, p.offsetX, p.offsetY);
            var thumb = GetPartThumb(p);
            int pw = Math.Max((int)((thumb?.Width ?? 24) * scale * p.scale), 10);
            int ph = Math.Max((int)((thumb?.Height ?? 24) * scale * p.scale), 10);
            var rect = new Rectangle((int)pos.X - pw / 2, (int)pos.Y - ph / 2, pw, ph);
            if (rect.Contains(pt)) { hitRect = rect; return i; }
        }
        hitRect = Rectangle.Empty;
        return -1;
    }

    private void PnlComposer_MouseDown(object? sender, MouseEventArgs e)
    {
        int idx = FindPartMarkerAt(e.Location, out _);
        if (idx < 0) return;
        CommitGridToParts();
        _draggingIndex = idx;
        _dragMouseStart = e.Location;
        _dragOffsetStartX = parts[idx].offsetX;
        _dragOffsetStartY = parts[idx].offsetY;
        dgv.ClearSelection();
        dgv.Rows[idx].Selected = true;
    }

    private void PnlComposer_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingIndex < 0) return;
        var baseRect = GetBaseDrawRect();
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;
        if (scale <= 0) return;
        float dx = (e.Location.X - _dragMouseStart.X) / scale;
        float dy = (e.Location.Y - _dragMouseStart.Y) / scale;
        parts[_draggingIndex].offsetX = _dragOffsetStartX + dx;
        parts[_draggingIndex].offsetY = _dragOffsetStartY + dy;
        dgv.Rows[_draggingIndex].Cells["offsetX"].Value = parts[_draggingIndex].offsetX;
        dgv.Rows[_draggingIndex].Cells["offsetY"].Value = parts[_draggingIndex].offsetY;
        pnlComposer.Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        baseSprite?.Dispose();
        foreach (var img in _partThumbCache.Values) img?.Dispose();
    }
}

// Feature: Composite Multi-Part Objects (Parts-M7)
// 画像をimg/フォルダへコピーする既存処理（AssetManagerFormのbtnSprite分岐）を共通化したヘルパー。
// 同名かつ内容が異なるファイルを誤って上書きしないよう、内容が違う場合は連番を付けて別名保存する。
public static class ImageImportHelper
{
    public static string CopyIntoImgFolder(string projectRoot, string sourceFile)
    {
        string imgDir = Path.Combine(projectRoot, "img");
        Directory.CreateDirectory(imgDir);

        string fileName = Path.GetFileName(sourceFile);
        string destPath = Path.Combine(imgDir, fileName);

        if (destPath.Equals(sourceFile, StringComparison.OrdinalIgnoreCase))
            return "img/" + fileName;

        if (File.Exists(destPath) && !FilesHaveSameContent(sourceFile, destPath))
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int n = 2;
            do
            {
                fileName = $"{nameNoExt}_{n}{ext}";
                destPath = Path.Combine(imgDir, fileName);
                n++;
            } while (File.Exists(destPath) && !FilesHaveSameContent(sourceFile, destPath));
        }

        if (!File.Exists(destPath)) File.Copy(sourceFile, destPath, overwrite: false);
        return "img/" + fileName;
    }

    private static bool FilesHaveSameContent(string pathA, string pathB)
    {
        try
        {
            var infoA = new FileInfo(pathA);
            var infoB = new FileInfo(pathB);
            if (infoA.Length != infoB.Length) return false;
            using var a = File.OpenRead(pathA);
            using var b = File.OpenRead(pathB);
            int ba, bb;
            do
            {
                ba = a.ReadByte();
                bb = b.ReadByte();
                if (ba != bb) return false;
            } while (ba != -1);
            return true;
        }
        catch { return false; }
    }
}
