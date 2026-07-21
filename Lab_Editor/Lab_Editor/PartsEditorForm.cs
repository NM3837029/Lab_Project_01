using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartsEditorForm - 複合オブジェクト（敵/ギミック/アイテム）のパーツ編集
// Feature: Composite Multi-Part Objects (Parts-M7、UI刷新版)
//
// 1体の敵/ギミック/アイテムを、複数の画像パーツの組み合わせとして構成するためのエディタ。
//
// 【UI刷新の経緯】旧版は1つの横長DataGridViewに全項目(id/offsetX/offsetY/hp/zOrder/画像/
// ボタン×4)を詰め込んでおり、狭い固定パネル幅では列が見切れて使い物にならなかった。
// 新版では「パーツ一覧(id/hp/zOrderのみの簡易一覧)」と「選択中パーツの詳細編集パネル」を分離し、
// 全体をSplitContainer（ユーザーがドラッグで境界を調整できる）+ TableLayoutPanel/FlowLayoutPanel
// （内容に応じて自動的にサイズ・配置される）で構成することで、固定座標指定によるはみ出し・
// 見切れを構造的に起こりにくくしている。
// ======================================================
public class PartsEditorForm : Form
{
    private readonly string projectRoot;
    private readonly string baseSpritePath;
    private Image? baseSprite;
    private readonly Dictionary<PartDef, Image?> _partThumbCache = new();

    private List<PartDef> parts;
    public List<PartDef> ResultParts { get; private set; } = new();

    private int selectedIndex = -1;
    private bool _suppressEvents = false;

    // ドラッグ状態（合成キャンバス上でのパーツ移動）
    private int _draggingIndex = -1;
    private Point _dragMouseStart;
    private float _dragOffsetStartX, _dragOffsetStartY;

    // ==== コントロール ====
    private DataGridView dgvList = null!;
    private Panel pnlComposer = null!;
    private TextBox txtId = null!;
    private Label lblSpriteValue = null!;
    private NumericUpDown nudOffsetX = null!, nudOffsetY = null!, nudScale = null!, nudHp = null!, nudZOrder = null!;
    private Label lblHpHint = null!;

    public PartsEditorForm(string subjectLabel, List<PartDef> initialParts, string projectRoot, string baseSpritePath)
    {
        this.projectRoot = projectRoot;
        this.baseSpritePath = baseSpritePath;
        parts = initialParts.Select(ClonePart).ToList();

        Text = $"🧩 パーツエディタ - {subjectLabel}";
        Size = new Size(1300, 820);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        LoadBaseSprite();

        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
            Text = "左：パーツ一覧（追加/削除/選択）　中央：合成プレビュー（ドラッグで位置調整）　右：選択中パーツの詳細。境界線はドラッグで広さを調整できます。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Color.FromArgb(230, 240, 255),
        };

        var pnlBottom = BuildBottomButtons();

        // ルート: 左(一覧) | 右(プレビュー+詳細)
        var rootSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
        var pnlListSide = BuildListSide();

        // 右側をさらに 上(プレビュー) / 下(詳細) に分割
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6 };
        var pnlComposerSide = BuildComposerSide();
        var pnlDetailSide = BuildDetailSide();

        Controls.Add(rootSplit);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);

        rootSplit.Panel1.Controls.Add(pnlListSide);
        rootSplit.Panel2.Controls.Add(rightSplit);
        rightSplit.Panel1.Controls.Add(pnlComposerSide);
        rightSplit.Panel2.Controls.Add(pnlDetailSide);

        // SplitterDistanceはコントロールがDockされ実サイズが確定してから設定する（先に設定すると例外/無視されることがある）
        Shown += (s, e) =>
        {
            rootSplit.SplitterDistance = Math.Max(280, (int)(ClientSize.Width * 0.24));
            rightSplit.SplitterDistance = Math.Max(300, (int)(rightSplit.Height * 0.55));
        };

        RefreshList();
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

    // ==== 左: パーツ一覧 ====

    private Panel BuildListSide()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Dock = DockStyle.Top, Height = 24, Text = "📋 パーツ一覧", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 4, 0, 0) };

        dgvList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 8.5f),
            RowHeadersWidth = 20,
            ScrollBars = ScrollBars.Vertical,
        };
        dgvList.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "id", HeaderText = "パーツID", FillWeight = 60, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "hp", HeaderText = "HP", FillWeight = 20, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "zOrder", HeaderText = "Z", FillWeight = 20, ReadOnly = true },
        });
        dgvList.SelectionChanged += (s, e) =>
        {
            selectedIndex = dgvList.SelectedRows.Count > 0 ? dgvList.SelectedRows[0].Index : -1;
            LoadDetailFromSelection();
            pnlComposer.Invalidate();
        };

        // Feature: UI改善 — ボタン数が多く折り返し(WrapContents)が発生しうるため、固定Heightだと
        // 折り返した行がパネルからはみ出して見えなくなっていた。AutoSizeにして必要な行数ぶん
        // 高さが自動的に伸びるようにする（幅はDock=Bottomにより親の幅に追従する）。
        var flowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(2),
        };
        var btnAdd = new Button { Text = "＋ 追加", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnAdd.Click += (s, e) => AddPart();
        var btnDel = new Button { Text = "🗑 削除", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnDel.Click += (s, e) => DeleteSelectedPart();
        var btnUp = new Button { Text = "▲ 上へ", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnUp.Click += (s, e) => MoveSelectedPart(-1);
        var btnDown = new Button { Text = "▼ 下へ", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnDown.Click += (s, e) => MoveSelectedPart(1);
        var btnApplyScript = new Button { Text = "🧩 このスクリプトを全パーツへ", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnApplyScript.Click += (s, e) => ApplyScriptToAllParts();
        var btnRod = new Button { Text = "🌀 回転する棒として配置...", AutoSize = true, Padding = new Padding(6, 4, 6, 4), BackColor = Color.FromArgb(255, 244, 214) };
        btnRod.Click += (s, e) => OpenRodGenerator();
        var btnEditRod = new Button { Text = "🔧 回転する棒を編集...", AutoSize = true, Padding = new Padding(6, 4, 6, 4), BackColor = Color.FromArgb(255, 244, 214) };
        btnEditRod.Click += (s, e) => OpenRodEditor();
        flowButtons.Controls.AddRange(new Control[] { btnAdd, btnDel, btnUp, btnDown, btnApplyScript, btnRod, btnEditRod });

        pnl.Controls.Add(dgvList);
        pnl.Controls.Add(flowButtons);
        pnl.Controls.Add(lblTitle);
        return pnl;
    }

    private void RefreshList()
    {
        int keepSelected = selectedIndex;
        dgvList.Rows.Clear();
        foreach (var p in parts) dgvList.Rows.Add(p.id, p.hp, p.zOrder);
        if (parts.Count > 0)
        {
            int idx = Math.Clamp(keepSelected < 0 ? 0 : keepSelected, 0, parts.Count - 1);
            dgvList.ClearSelection();
            dgvList.Rows[idx].Selected = true;
            selectedIndex = idx;
        }
        else
        {
            selectedIndex = -1;
        }
        LoadDetailFromSelection();
        pnlComposer.Invalidate();
    }

    private void AddPart()
    {
        string baseId = "part";
        int n = 1;
        var existing = new HashSet<string>(parts.Select(p => p.id));
        string newId;
        do { newId = $"{baseId}{n}"; n++; } while (existing.Contains(newId));
        parts.Add(new PartDef { id = newId, offsetX = 0, offsetY = 0 });
        selectedIndex = parts.Count - 1;
        RefreshList();
    }

    private void DeleteSelectedPart()
    {
        if (selectedIndex < 0 || selectedIndex >= parts.Count) { MessageBox.Show("削除するパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"パーツ「{parts[selectedIndex].id}」を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        InvalidatePartThumb(parts[selectedIndex]);
        parts.RemoveAt(selectedIndex);
        selectedIndex = Math.Min(selectedIndex, parts.Count - 1);
        RefreshList();
    }

    private void MoveSelectedPart(int dir)
    {
        if (selectedIndex < 0 || selectedIndex >= parts.Count) return;
        int newIdx = selectedIndex + dir;
        if (newIdx < 0 || newIdx >= parts.Count) return;
        (parts[selectedIndex], parts[newIdx]) = (parts[newIdx], parts[selectedIndex]);
        selectedIndex = newIdx;
        RefreshList();
    }

    private void ApplyScriptToAllParts()
    {
        if (parts.Count == 0) { MessageBox.Show("パーツがありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (selectedIndex < 0) { MessageBox.Show("コピー元にするパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var srcScript = (JArray)parts[selectedIndex].script.DeepClone();
        foreach (var p in parts) p.script = (JArray)srcScript.DeepClone();
        MessageBox.Show($"「{parts[selectedIndex].id}」のスクリプトを全{parts.Count}パーツに適用しました。\n（PartIndexレポーターを使えば、同じスクリプトのままパーツごとに異なる位相・動きを表現できます）",
            "適用完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ==== 中央: 合成プレビューキャンバス ====

    private Panel BuildComposerSide()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Dock = DockStyle.Top, Height = 24, Text = "🖼 合成プレビュー（パーツをドラッグして配置）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 4, 0, 0) };
        pnlComposer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(50, 50, 55) };
        pnlComposer.Paint += PnlComposer_Paint;
        pnlComposer.MouseDown += PnlComposer_MouseDown;
        pnlComposer.MouseMove += PnlComposer_MouseMove;
        pnlComposer.MouseUp += (s, e) => _draggingIndex = -1;
        pnlComposer.Resize += (s, e) => pnlComposer.Invalidate();
        pnl.Controls.Add(pnlComposer);
        pnl.Controls.Add(lblTitle);
        return pnl;
    }

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

    private Rectangle GetBaseDrawRect()
    {
        if (pnlComposer.Width <= 0 || pnlComposer.Height <= 0) return new Rectangle(0, 0, 1, 1);
        if (baseSprite == null) return new Rectangle(pnlComposer.Width / 2 - 4, pnlComposer.Height / 2 - 4, 8, 8);
        float scale = Math.Min((float)pnlComposer.Width * 0.5f / baseSprite.Width, (float)pnlComposer.Height * 0.5f / baseSprite.Height);
        if (scale > 10) scale = 10;
        if (scale <= 0) scale = 1;
        int drawW = Math.Max((int)(baseSprite.Width * scale), 1);
        int drawH = Math.Max((int)(baseSprite.Height * scale), 1);
        int drawX = (pnlComposer.Width - drawW) / 2;
        int drawY = (pnlComposer.Height - drawH) / 2;
        return new Rectangle(drawX, drawY, drawW, drawH);
    }

    private PointF WorldToScreen(Rectangle baseRect, float scale, float ox, float oy)
        => new PointF(baseRect.X + ox * scale, baseRect.Y + oy * scale);

    private void PnlComposer_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var baseRect = GetBaseDrawRect();
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;

        if (baseSprite != null) e.Graphics.DrawImage(baseSprite, baseRect);
        e.Graphics.DrawRectangle(Pens.DimGray, baseRect);
        using var originBrush = new SolidBrush(Color.FromArgb(200, 0, 255, 255));
        e.Graphics.FillEllipse(originBrush, baseRect.X - 3, baseRect.Y - 3, 6, 6);

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
            using var pen = new Pen(i == selectedIndex ? Color.Yellow : Color.FromArgb(200, 255, 255, 255), i == selectedIndex ? 2.5f : 1.2f);
            e.Graphics.DrawRectangle(pen, rect);
            using var smallFont = new Font(Font.FontFamily, 7.5f);
            e.Graphics.DrawString(p.id, smallFont, Brushes.White, rect.X, rect.Bottom + 1);
        }

        if (parts.Count == 0)
        {
            using var f = new Font(Font.FontFamily, 10f);
            var msg = "パーツがありません。左の「＋ 追加」または「🌀 回転する棒として配置...」から作成してください。";
            var sz = e.Graphics.MeasureString(msg, f, pnlComposer.Width - 20);
            e.Graphics.DrawString(msg, f, Brushes.LightGray, new RectangleF(10, 10, pnlComposer.Width - 20, sz.Height + 10));
        }
    }

    private int FindPartMarkerAt(Point pt)
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
            if (rect.Contains(pt)) return i;
        }
        return -1;
    }

    private void PnlComposer_MouseDown(object? sender, MouseEventArgs e)
    {
        int idx = FindPartMarkerAt(e.Location);
        if (idx < 0) return;
        _draggingIndex = idx;
        _dragMouseStart = e.Location;
        _dragOffsetStartX = parts[idx].offsetX;
        _dragOffsetStartY = parts[idx].offsetY;
        selectedIndex = idx;
        dgvList.ClearSelection();
        if (idx < dgvList.Rows.Count) dgvList.Rows[idx].Selected = true;
        LoadDetailFromSelection();
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
        if (_draggingIndex == selectedIndex) LoadDetailFromSelection();
        pnlComposer.Invalidate();
    }

    // ==== 右下: 選択中パーツの詳細編集パネル ====

    private Panel BuildDetailSide()
    {
        // AutoScroll付きのPanel＋TopDown方向のFlowLayoutPanelで縦積みにすることで、
        // 「Dock=Topを複数並べた時の重なり順の曖昧さ」を避け、追加した順番どおりに必ず
        // 上から並ぶようにしている。ウィンドウが小さくても内容が入り切らない場合は
        // 自動的に縦スクロールバーが出るため、項目が見切れることはない。
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var lblTitle = new Label { AutoSize = true, Text = "🔧 選択中パーツの詳細", Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 4, 0, 4) };

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(6),
            Margin = new Padding(0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(lbl, 0, r);
            table.Controls.Add(control, 1, r);
        }

        txtId = new TextBox { Width = 180 };
        txtId.TextChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].id = txtId.Text; dgvList.Rows[selectedIndex].Cells["id"].Value = txtId.Text; pnlComposer.Invalidate(); } };
        AddRow("パーツID", txtId);

        var spritePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        lblSpriteValue = new Label { Text = "(画像なし)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3), MaximumSize = new Size(220, 0) };
        var btnPickSprite = new Button { Text = "📁 画像選択", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnPickSprite.Click += (s, e) => PickSpriteForSelected();
        spritePanel.Controls.AddRange(new Control[] { lblSpriteValue, btnPickSprite });
        AddRow("画像", spritePanel);

        nudOffsetX = MakeNumeric(-4000, 4000, 0);
        nudOffsetX.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].offsetX = (float)nudOffsetX.Value; pnlComposer.Invalidate(); } };
        AddRow("offsetX(横位置)", nudOffsetX);

        nudOffsetY = MakeNumeric(-4000, 4000, 0);
        nudOffsetY.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].offsetY = (float)nudOffsetY.Value; pnlComposer.Invalidate(); } };
        AddRow("offsetY(縦位置)", nudOffsetY);

        nudScale = MakeNumeric(0.1m, 10m, 2, 1m);
        nudScale.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].scale = (float)nudScale.Value; pnlComposer.Invalidate(); } };
        AddRow("表示スケール", nudScale);

        nudHp = MakeNumeric(0, 999, 0);
        nudHp.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].hp = (int)nudHp.Value; dgvList.Rows[selectedIndex].Cells["hp"].Value = (int)nudHp.Value; UpdateHpHint(); } };
        AddRow("HP", nudHp);

        lblHpHint = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7.5f), Anchor = AnchorStyles.Left };
        AddRow("", lblHpHint);

        nudZOrder = MakeNumeric(-100, 100, 0);
        nudZOrder.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].zOrder = (int)nudZOrder.Value; dgvList.Rows[selectedIndex].Cells["zOrder"].Value = (int)nudZOrder.Value; pnlComposer.Invalidate(); } };
        AddRow("zOrder(奥-/手前+)", nudZOrder);

        var flowActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(6), Margin = new Padding(0) };
        var btnHitbox = new Button { Text = "🎯 当たり判定を編集", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnHitbox.Click += (s, e) => EditHitboxForSelected();
        var btnScript = new Button { Text = "📝 挙動スクリプトを編集", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnScript.Click += (s, e) => EditScriptForSelected();
        flowActions.Controls.AddRange(new Control[] { btnHitbox, btnScript });

        flow.Controls.Add(lblTitle);
        flow.Controls.Add(table);
        flow.Controls.Add(flowActions);
        pnl.Controls.Add(flow);
        return pnl;
    }

    private static NumericUpDown MakeNumeric(decimal min, decimal max, int decimals, decimal value = 0)
        => new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value, Width = 120 };

    private void LoadDetailFromSelection()
    {
        bool hasSel = selectedIndex >= 0 && selectedIndex < parts.Count;
        _suppressEvents = true;
        if (hasSel)
        {
            var p = parts[selectedIndex];
            txtId.Text = p.id;
            lblSpriteValue.Text = string.IsNullOrEmpty(p.sprite) ? "(画像なし)" : p.sprite;
            nudOffsetX.Value = (decimal)Math.Clamp(p.offsetX, (float)nudOffsetX.Minimum, (float)nudOffsetX.Maximum);
            nudOffsetY.Value = (decimal)Math.Clamp(p.offsetY, (float)nudOffsetY.Minimum, (float)nudOffsetY.Maximum);
            nudScale.Value = (decimal)Math.Clamp(p.scale, (float)nudScale.Minimum, (float)nudScale.Maximum);
            nudHp.Value = Math.Clamp(p.hp, (int)nudHp.Minimum, (int)nudHp.Maximum);
            nudZOrder.Value = Math.Clamp(p.zOrder, (int)nudZOrder.Minimum, (int)nudZOrder.Maximum);
            UpdateHpHint();
        }
        else
        {
            txtId.Text = "";
            lblSpriteValue.Text = "(パーツ未選択)";
            nudOffsetX.Value = 0; nudOffsetY.Value = 0; nudScale.Value = 1; nudHp.Value = 0; nudZOrder.Value = 0;
            lblHpHint.Text = "";
        }
        txtId.Enabled = lblSpriteValue.Enabled = nudOffsetX.Enabled = nudOffsetY.Enabled = nudScale.Enabled = nudHp.Enabled = nudZOrder.Enabled = hasSel;
        _suppressEvents = false;
    }

    private void UpdateHpHint()
    {
        int hp = (int)nudHp.Value;
        lblHpHint.Text = hp == 0 ? "0 = 破壊不能な常在ハザード" : $"{hp} = 弾{hp}発で破壊可能（本体のHP/生死には影響しません）";
    }

    private void PickSpriteForSelected()
    {
        if (selectedIndex < 0) { MessageBox.Show("先にパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "パーツ画像を選択" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        string relPath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
        var p = parts[selectedIndex];
        p.sprite = relPath;
        InvalidatePartThumb(p);
        lblSpriteValue.Text = relPath;
        pnlComposer.Invalidate();
    }

    private void EditHitboxForSelected()
    {
        if (selectedIndex < 0) { MessageBox.Show("先にパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var p = parts[selectedIndex];
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

    private void EditScriptForSelected()
    {
        if (selectedIndex < 0) { MessageBox.Show("先にパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var p = parts[selectedIndex];
        using var form = new BehaviorScriptEditorForm($"パーツ: {p.id}", p.script);
        if (form.ShowDialog() == DialogResult.OK) p.script = form.ResultScript;
    }

    // ==== 🌀 回転する棒として配置（ジェネレータ）／🔧 既存の棒を編集 ====

    // フォームの入力値から、指定した開始インデックス(全体配列内での通し番号)を基準にパーツ一式を生成する。
    // 新規作成でも既存の棒の再生成でも、この1メソッドを共有する。
    private List<PartDef> GenerateRodParts(RodGeneratorForm form, int startIndexForPhase)
    {
        var existing = new HashSet<string>(parts.Select(p => p.id));
        var newParts = new List<PartDef>();
        for (int i = 0; i < form.Count; i++)
        {
            string baseId = form.IdPrefix;
            string id = $"{baseId}{i}";
            int n = 1;
            while (existing.Contains(id)) { id = $"{baseId}{i}_{n}"; n++; }
            existing.Add(id);

            var pd = new PartDef
            {
                id = id,
                sprite = form.SpritePath,
                width = form.PartSize,
                height = form.PartSize,
                hitboxWidth = form.PartSize,
                hitboxHeight = form.PartSize,
                hp = form.Hp,
                zOrder = form.ZOrder,
            };
            // 「同じ角度・パーツごとに異なる半径」で並べることで、球が一直線に並んで回転する棒（ファイアバー等）を再現する。
            // 半径は (このパーツの全体配列インデックス+1) * 間隔 として、既存パーツを含めた通し番号で計算する。
            int overallIndex = startIndexForPhase + i;
            pd.offsetX = form.Spacing * (overallIndex + 1);
            pd.offsetY = 0;
            var angleExpr = new JObject { ["op"] = "Mul", ["a"] = new JObject { ["op"] = "Time" }, ["b"] = form.Speed };
            var radiusExpr = new JObject
            {
                ["op"] = "Mul",
                ["a"] = new JObject { ["op"] = "Add", ["a"] = new JObject { ["op"] = "PartIndex" }, ["b"] = 1 },
                ["b"] = form.Spacing,
            };
            var body = new JArray {
                new JObject {
                    ["op"] = "Forever",
                    ["body"] = new JArray {
                        new JObject { ["op"] = "SetLocalOffsetPolar", ["angle"] = angleExpr, ["radius"] = radiusExpr },
                        new JObject { ["op"] = "Wait", ["frames"] = 1 },
                    }
                }
            };
            pd.script = new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = body } };
            newParts.Add(pd);
        }
        return newParts;
    }

    private void OpenRodGenerator()
    {
        using var form = new RodGeneratorForm(projectRoot);
        if (form.ShowDialog() != DialogResult.OK) return;

        var newParts = GenerateRodParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        MessageBox.Show($"{newParts.Count}個のパーツを「回転する棒」として配置しました。\n（半径はパーツの並び順(PartIndex)に応じて自動的に増えていくため、全て同じスクリプトを共有しても一直線に並んで回転します）",
            "生成完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // 選択中のパーツが「🌀 回転する棒として配置」で作られた棒の一部かどうかを判定し、
    // 同じ棒に属する他のパーツ（idの接頭辞が同じ かつ 同じ速さ・間隔のSetLocalOffsetPolarスクリプトを持つ）をまとめて検出する。
    private RodGroupInfo? DetectRodGroup(int fromIndex)
    {
        if (fromIndex < 0 || fromIndex >= parts.Count) return null;
        var seed = parts[fromIndex];
        if (!RodGroupInfo.TryParseRodScript(seed.script, out float speed, out float spacing)) return null;

        string id = seed.id;
        int cut = id.Length;
        while (cut > 0 && char.IsDigit(id[cut - 1])) cut--;
        string prefix = id.Substring(0, cut);
        if (string.IsNullOrEmpty(prefix)) return null;

        var info = new RodGroupInfo { IdPrefix = prefix, Spacing = spacing, Speed = speed, Hp = seed.hp, ZOrder = seed.zOrder, PartSize = seed.width > 0 ? seed.width : 12, SpritePath = seed.sprite };
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (!p.id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!RodGroupInfo.TryParseRodScript(p.script, out float s2, out float sp2)) continue;
            if (Math.Abs(s2 - speed) > 0.0001f || Math.Abs(sp2 - spacing) > 0.0001f) continue;
            info.Indices.Add(i);
        }
        return info.Indices.Count > 0 ? info : null;
    }

    private void OpenRodEditor()
    {
        if (selectedIndex < 0) { MessageBox.Show("編集したい「回転する棒」のパーツをどれか1つ選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var group = DetectRodGroup(selectedIndex);
        if (group == null)
        {
            MessageBox.Show("選択中のパーツは「回転する棒」として認識できませんでした。\n（「🌀 回転する棒として配置」で作成したパーツのみ、この機能で編集できます）", "認識できません", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new RodGeneratorForm(projectRoot, group);
        if (form.ShowDialog() != DialogResult.OK) return;

        // 既存メンバーを削除してから同じパラメータ体系で再生成する（並び順は末尾に移動する）
        foreach (var idx in group.Indices.OrderByDescending(i => i)) parts.RemoveAt(idx);
        var newParts = GenerateRodParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        MessageBox.Show($"「回転する棒」を{newParts.Count}個のパーツに更新しました。\n（更新後はパーツ一覧の末尾に移動しています）",
            "更新完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ==== 下部ボタン ====

    private Panel BuildBottomButtons()
    {
        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = "💾 OK", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnOk.Click += (s, e) => { ResultParts = parts; };
        // RightToLeftのFlowLayoutPanelは追加順が右から並ぶため、先にCancelを追加すると右端になる。OKを一番右(先頭追加)にしたいため逆順で追加する。
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        pnl.Controls.Add(flow);
        return pnl;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        baseSprite?.Dispose();
        foreach (var img in _partThumbCache.Values) img?.Dispose();
    }
}

// ======================================================
// RodGeneratorForm - 「🌀 回転する棒として配置」の設定ダイアログ
// Feature: Composite Multi-Part Objects (Parts-Fix4)
// ======================================================
public class RodGeneratorForm : Form
{
    private readonly string projectRoot;
    private NumericUpDown nudCount = null!, nudSpacing = null!, nudSpeed = null!, nudHp = null!, nudZOrder = null!, nudSize = null!;
    private CheckBox chkReverse = null!;
    private TextBox txtIdPrefix = null!;
    private Label lblSprite = null!;
    private string spritePath = "";

    public int Count => (int)nudCount.Value;
    public float Spacing => (float)nudSpacing.Value;
    public float Speed => (float)nudSpeed.Value * (chkReverse.Checked ? -1f : 1f);
    public int Hp => (int)nudHp.Value;
    public int ZOrder => (int)nudZOrder.Value;
    public int PartSize => (int)nudSize.Value;
    public string IdPrefix => string.IsNullOrWhiteSpace(txtIdPrefix.Text) ? "rod" : txtIdPrefix.Text.Trim();
    public string SpritePath => spritePath;

    // initialを渡すと「既存の棒を編集」モードになり、現在の値が入った状態で開く（値の意味はRodGroupInfo参照）
    public RodGeneratorForm(string projectRoot, RodGroupInfo? initial = null)
    {
        this.projectRoot = projectRoot;
        bool isEdit = initial != null;
        Text = isEdit ? "🔧 回転する棒を編集" : "🌀 回転する棒として配置";
        Size = new Size(480, 460);
        MinimumSize = new Size(420, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblExplain = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(8),
            Text = isEdit
                ? "現在の「回転する棒」のパラメータを変更して更新します。球の数を増減させたり、回転速度・向き・間隔（伸び縮み）を調整できます。"
                : "マリオのファイアバーのように、中心から一直線に並んだ球が棒状に回転するパーツ一式を自動生成します。\n" +
                  "「□○○○○○」のように、球が同じ角度・異なる半径で並ぶため、常に一直線のまま回転します。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        // Dock=Topにして内容量に応じた高さだけ確保する（Dock=Fillだと親のAutoScrollより先に
        // 強制的にクライアント領域いっぱいに引き伸ばされてしまい、はみ出た分がスクロールされず
        // 見切れる原因になるため、必ずTop+AutoSizeの組み合わせにする）
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, r);
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(control, 1, r);
        }

        nudCount = new NumericUpDown { Minimum = 1, Maximum = 40, Value = initial?.Indices.Count ?? 5, Width = 100 };
        AddRow("球の数（増減で伸縮）", nudCount);

        nudSpacing = new NumericUpDown { Minimum = 2, Maximum = 500, Value = (decimal)(initial?.Spacing ?? 14f), Width = 100 };
        AddRow("間隔(px)", nudSpacing);

        nudSpeed = new NumericUpDown { Minimum = 0.001m, Maximum = 1m, DecimalPlaces = 3, Increment = 0.005m, Value = (decimal)Math.Abs(initial?.Speed ?? 0.04f), Width = 100 };
        AddRow("回転速度", nudSpeed);

        chkReverse = new CheckBox { Text = "逆回転（反時計回り）にする", AutoSize = true, Checked = (initial?.Speed ?? 0f) < 0f };
        AddRow("回転方向", chkReverse);

        nudSize = new NumericUpDown { Minimum = 2, Maximum = 200, Value = initial?.PartSize ?? 12, Width = 100 };
        AddRow("球の表示/当たり判定サイズ(px)", nudSize);

        nudHp = new NumericUpDown { Minimum = 0, Maximum = 999, Value = initial?.Hp ?? 0, Width = 100 };
        AddRow("HP(0=不滅)", nudHp);

        nudZOrder = new NumericUpDown { Minimum = -100, Maximum = 100, Value = initial?.ZOrder ?? 1, Width = 100 };
        AddRow("zOrder", nudZOrder);

        txtIdPrefix = new TextBox { Text = initial?.IdPrefix ?? "rod", Width = 150 };
        AddRow("パーツID接頭辞", txtIdPrefix);

        spritePath = initial?.SpritePath ?? "";
        var spritePanel = new FlowLayoutPanel { AutoSize = true };
        lblSprite = new Label { Text = string.IsNullOrEmpty(spritePath) ? "(画像なし)" : spritePath, AutoSize = true, MaximumSize = new Size(220, 0), Margin = new Padding(3, 6, 6, 3) };
        var btnPick = new Button { Text = "📁 画像選択", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnPick.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "球の画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            spritePath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            lblSprite.Text = spritePath;
        };
        spritePanel.Controls.AddRange(new Control[] { lblSprite, btnPick });
        AddRow("球の画像", spritePanel);

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = isEdit ? "更新" : "生成", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        pnlBottom.Controls.Add(flow);

        var pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        pnlScroll.Controls.Add(table);

        Controls.Add(pnlScroll);
        Controls.Add(pnlBottom);
        Controls.Add(lblExplain);
    }
}

// ======================================================
// RodGroupInfo - 「回転する棒」として検出済みのパーツ群の共有パラメータ
// Feature: Composite Multi-Part Objects (Parts-Fix5)
// ======================================================
public class RodGroupInfo
{
    public List<int> Indices = new(); // parts配列内でのインデックス（棒を構成する全パーツ）
    public string IdPrefix = "rod";
    public float Spacing;
    public float Speed; // 符号が回転方向（負=逆回転）
    public int Hp;
    public int ZOrder;
    public int PartSize;
    public string SpritePath = "";

    // scriptが「SetLocalOffsetPolar(angle:Mul(Time,speed), radius:Mul(Add(PartIndex,1),spacing))」の
    // 形になっているかを判定し、speed/spacingを取り出す。ジェネレータが生成した形と完全一致する場合のみtrue。
    public static bool TryParseRodScript(JArray script, out float speed, out float spacing)
    {
        speed = 0; spacing = 0;
        try
        {
            var hat = script.FirstOrDefault(t => t["hat"]?.ToString() == "OnSpawn") as JObject;
            var body = hat?["body"] as JArray;
            var forever = body?.FirstOrDefault(t => t["op"]?.ToString() == "Forever") as JObject;
            var fbody = forever?["body"] as JArray;
            var setOp = fbody?.FirstOrDefault(t => t["op"]?.ToString() == "SetLocalOffsetPolar") as JObject;
            if (setOp == null) return false;
            var angle = setOp["angle"] as JObject;
            var radius = setOp["radius"] as JObject;
            if (angle?["op"]?.ToString() != "Mul") return false;
            if (radius?["op"]?.ToString() != "Mul") return false;
            speed = angle["b"]!.Value<float>();
            spacing = radius["b"]!.Value<float>();
            return true;
        }
        catch { return false; }
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
