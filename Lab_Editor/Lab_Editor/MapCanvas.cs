using System.Drawing;
using System.Drawing.Drawing2D;

namespace Lab_Editor;

/// <summary>
/// タイルマップ + 配置オブジェクトのビジュアルキャンバス
/// Feature 1: 装飾レイヤー (DecoLayerBack / DecoLayerFront) 対応
/// Feature 4: TestPlay モード対応
/// Feature 5: トリガー矩形の描画・配置対応
/// </summary>
public class MapCanvas : Panel
{
    public const int TILE_SIZE = 20;    // 表示上のタイルサイズ(px)
    public const int GAME_TILE = 32;    // ゲーム内タイルサイズ(px)

    public enum EditMode
    {
        Tile,
        DecoLayerBack,   // Feature 1: 装飾後景レイヤー
        DecoLayerFront,  // Feature 1: 装飾前景レイヤー
        Enemy,
        Gimmick,
        Item,
        Select,
        PlayerStart,
        Goal,
        Trigger,         // Feature 5: トリガー矩形配置
        TestPlay,        // Feature 4: ここからプレイ（クリック位置取得）
        Eraser           // Eraser mode
    }

    // 現在の編集モード
    public EditMode CurrentMode { get; set; } = EditMode.Tile;
    public int SelectedTileId { get; set; } = 1;
    public string? SelectedAssetId { get; set; }

    // ステージデータ
    public StageData? Stage { get; set; }
    public AssetDefinitions? Assets { get; set; }

    // 選択中オブジェクト
    public object? SelectedObject { get; set; }

    // スクロール
    public int ScrollX { get; set; } = 0;
    public int ScrollY { get; set; } = 0;

    // Feature 5: トリガー矩形ドラッグ用
    private Point _triggerDragStart;
    private Rectangle _triggerDragRect;
    private bool _isTriggerDragging = false;

    // イベント
    public event EventHandler? ObjectSelected;
    public event EventHandler? StageModified;
    public event EventHandler? EditCompleted;

    /// <summary>Feature 4: ここからプレイのクリック座標（ワールド座標）</summary>
    public event EventHandler<(float wx, float wy)>? TestPlayClicked;

    /// <summary>Feature 5: トリガー矩形が確定された時のイベント</summary>
    public event EventHandler<EventTrigger>? TriggerPlaced;

    private bool isDragging = false;
    private bool isRightDrag = false;

    // Feature: 配置の重複防止 — Enemy/Gimmick/Itemモードは「クリック(ドラッグ含む)1ストロークにつき1個」に制限する。
    // タイルモードのようにドラッグ中連続で置きたいわけではないため、MouseDown〜MouseUpの間で既に1個配置済みなら
    // MouseMoveでの追加配置をスキップする。
    private bool _strokePlacedAsset = false;

    // タイル色マップ（Tile ID → Color）
    private Dictionary<int, Color> _tileColors = new()
    {
        [0] = Color.FromArgb(180, 210, 240),
        [1] = Color.FromArgb(80, 160, 60),
        [2] = Color.FromArgb(140, 100, 60),
        [3] = Color.FromArgb(204, 51, 51),
        [4] = Color.FromArgb(85, 85, 85),
    };

    // タイル画像マップ（Tile ID → Image）
    private Dictionary<int, Image> _tileImages = new();

    // タイル通行設定（Tile ID → (当たり判定あり, 即死)）MZ風の通行設定可視化用
    private Dictionary<int, (bool collidable, bool deadly)> _tileMeta = new();

    public string AssetsPath { get; set; } = "";

    // 装飾レイヤー用のやや暗い色変換（視覚的区別のため半透明表示）
    private static Color DecoColor(Color c) => Color.FromArgb(160, c);

    // タイルカラーと画像を Assets から再構築
    public void RefreshTileColors()
    {
        if (Assets == null) return;
        _tileColors.Clear();
        foreach (var img in _tileImages.Values) { img.Dispose(); }
        _tileImages.Clear();
        _tileMeta.Clear();

        foreach (var t in Assets.Tiles)
        {
            try
            {
                var c = ColorTranslator.FromHtml(t.color);
                _tileColors[t.id] = c;
            }
            catch
            {
                _tileColors[t.id] = Color.Gray;
            }

            _tileMeta[t.id] = (t.collidable, t.deadly);

            // 画像の読み込み
            if (!string.IsNullOrEmpty(t.sprite) && !string.IsNullOrEmpty(AssetsPath))
            {
                string imgPath = System.IO.Path.Combine(AssetsPath, t.sprite);
                if (System.IO.File.Exists(imgPath))
                {
                    try
                    {
                        _tileImages[t.id] = Image.FromFile(imgPath);
                    }
                    catch
                    {
                        // 読み込み失敗時は無視
                    }
                }
            }
        }
        if (!_tileColors.ContainsKey(0)) _tileColors[0] = Color.FromArgb(180, 210, 240);
        Invalidate();
    }

    public MapCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(180, 210, 240);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Stage == null) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighSpeed;

        int startCol = ScrollX / TILE_SIZE;
        int endCol = Math.Min(Stage.MapW, startCol + Width / TILE_SIZE + 2);
        int startRow = ScrollY / TILE_SIZE;
        int endRow = Math.Min(Stage.MapH, startRow + Height / TILE_SIZE + 2);

        // ==== レイヤー描画順: 装飾後景 → メイン → 装飾前景 ====

        bool isL1 = CurrentMode == EditMode.DecoLayerBack;
        bool isL2 = CurrentMode == EditMode.Tile;
        bool isL3 = CurrentMode == EditMode.DecoLayerFront;
        bool isL4 = CurrentMode == EditMode.Trigger || CurrentMode == EditMode.PlayerStart || CurrentMode == EditMode.Goal || CurrentMode == EditMode.Enemy || CurrentMode == EditMode.Gimmick || CurrentMode == EditMode.Item;
        bool isNeutral = CurrentMode == EditMode.Select || CurrentMode == EditMode.TestPlay || CurrentMode == EditMode.Eraser;

        // 1. 装飾レイヤー（後景）
        DrawLayer(g, Stage.DecoLayerBack, startRow, endRow, startCol, endCol, isDecoBefore: true, isActive: isL1 || isNeutral);

        // 2. メインタイルレイヤー
        DrawLayer(g, Stage.Map, startRow, endRow, startCol, endCol, isDecoBefore: false, isActive: isL2 || isNeutral);

        // 3. グリッド線
        using var gridPen = new Pen(Color.FromArgb(40, 0, 0, 0), 1);
        for (int row = startRow; row <= endRow; row++)
            g.DrawLine(gridPen, 0, row * TILE_SIZE - ScrollY, Width, row * TILE_SIZE - ScrollY);
        for (int col = startCol; col <= endCol; col++)
            g.DrawLine(gridPen, col * TILE_SIZE - ScrollX, 0, col * TILE_SIZE - ScrollX, Height);

        // 4. 装飾レイヤー（前景）
        DrawLayer(g, Stage.DecoLayerFront, startRow, endRow, startCol, endCol, isDecoBefore: true, isActive: isL3 || isNeutral);

        // 5. マップ外境界線
        int mapRight = Stage.MapW * TILE_SIZE - ScrollX;
        int mapBottom = Stage.MapH * TILE_SIZE - ScrollY;
        using var borderPen = new Pen(Color.FromArgb(200, 255, 100, 0), 2);
        g.DrawRectangle(borderPen, 0, 0, mapRight, mapBottom);

        // 6. プレイヤー開始位置 (P)
        DrawMarker(g, Stage.PlayerStartX, Stage.PlayerStartY, "P", Color.FromArgb(0, 200, 100));

        // 7. ゴール (G)
        if (Stage.GoalX >= 0)
            DrawMarker(g, Stage.GoalX, Stage.GoalY, "G", Color.FromArgb(255, 215, 0));

        // 7.5. 敵の巡回範囲プレビュー（MZ の移動ルートプレビュー風）
        DrawPatrolRanges(g);

        // 8. 敵（type_enumごとのアイコンで表示。テキスト表記のみでは判別しにくいため）
        foreach (var en in Stage.Enemies)
        {
            string icon = AssetIcons.ForEnemy(Assets?.Enemies.FirstOrDefault(d => d.id == en.Id)?.type_enum ?? -1);
            DrawMarker(g, en.X, en.Y, icon, SelectedObject == en ? Color.Magenta : Color.FromArgb(220, 50, 50), 11f);
        }

        // 9. ギミック
        foreach (var gi in Stage.Gimmicks)
        {
            string icon = AssetIcons.ForGimmick(Assets?.Gimmicks.FirstOrDefault(d => d.id == gi.Id)?.type_enum ?? -1);
            DrawMarker(g, gi.X, gi.Y, icon, SelectedObject == gi ? Color.Magenta : Color.FromArgb(50, 120, 220), 11f);
        }

        // 10. アイテム
        foreach (var it in Stage.Items)
        {
            string icon = AssetIcons.ForItem(Assets?.Items.FirstOrDefault(d => d.id == it.Id)?.type_enum ?? -1);
            DrawMarker(g, it.X, it.Y, icon, SelectedObject == it ? Color.Magenta : Color.FromArgb(255, 200, 0), 11f);
        }

        // 11. トリガー矩形 (Feature 5)
        if (Stage.Triggers.Count > 0)
            DrawTriggers(g);

        // 12. ドラッグ中のトリガー矩形プレビュー
        if (_isTriggerDragging && _triggerDragRect.Width > 0 && _triggerDragRect.Height > 0)
        {
            using var trigBrush = new SolidBrush(Color.FromArgb(60, 255, 140, 0));
            using var trigPen = new Pen(Color.OrangeRed, 2) { DashStyle = DashStyle.Dash };
            g.FillRectangle(trigBrush, _triggerDragRect);
            g.DrawRectangle(trigPen, _triggerDragRect);
        }

        // 13. アクティブレイヤーのインジケーター
        DrawLayerIndicator(g);
    }

    private void DrawLayer(Graphics g, int[,] layer, int startRow, int endRow, int startCol, int endCol, bool isDecoBefore, bool isActive)
    {
        for (int row = startRow; row < endRow; row++)
        {
            for (int col = startCol; col < endCol; col++)
            {
                int tileId = layer[row, col];
                if (tileId == 0) continue;
                int px = col * TILE_SIZE - ScrollX;
                int py = row * TILE_SIZE - ScrollY;

                if (_tileImages.TryGetValue(tileId, out var img))
                {
                    // スプライト描画
                    g.DrawImage(img, px, py, TILE_SIZE, TILE_SIZE);
                }
                else
                {
                    // 画像がない場合は従来の色塗りつぶし
                    var baseColor = _tileColors.TryGetValue(tileId, out var c) ? c : Color.Gray;
                    using var brush = new SolidBrush(baseColor);
                    g.FillRectangle(brush, px, py, TILE_SIZE, TILE_SIZE);
                }

                // 非アクティブレイヤーは暗くする（MZ風）
                if (!isActive)
                {
                    using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
                    g.FillRectangle(shade, px, py, TILE_SIZE, TILE_SIZE);
                }

                // 装飾レイヤーには斜線ハッチング
                if (isDecoBefore)
                {
                    using var hatch = new HatchBrush(HatchStyle.ForwardDiagonal, Color.FromArgb(30, 255, 255, 255), Color.Transparent);
                    g.FillRectangle(hatch, px, py, TILE_SIZE, TILE_SIZE);
                }

                // メインレイヤーのみ：通行設定を可視化（MZ風）
                if (!isDecoBefore && _tileMeta.TryGetValue(tileId, out var meta))
                {
                    if (meta.deadly)
                    {
                        using var deadlyPen = new Pen(Color.FromArgb(220, 255, 0, 0), 2);
                        g.DrawLine(deadlyPen, px + 2, py + 2, px + TILE_SIZE - 2, py + TILE_SIZE - 2);
                        g.DrawLine(deadlyPen, px + TILE_SIZE - 2, py + 2, px + 2, py + TILE_SIZE - 2);
                    }
                    else if (!meta.collidable)
                    {
                        using var passPen = new Pen(Color.FromArgb(200, 0, 220, 220), 1) { DashStyle = DashStyle.Dot };
                        g.DrawRectangle(passPen, px + 1, py + 1, TILE_SIZE - 2, TILE_SIZE - 2);
                    }
                }
            }
        }
    }

    private void DrawPatrolRanges(Graphics g)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        foreach (var en in Stage!.Enemies)
        {
            if (en.PatrolLeft < 0 || en.PatrolRight <= en.PatrolLeft) continue;

            int lx = (int)(en.PatrolLeft * scale) - ScrollX;
            int rx = (int)(en.PatrolRight * scale) - ScrollX;
            int ly = (int)((en.Y + GAME_TILE / 2f) * scale) - ScrollY;
            bool selected = SelectedObject == en;

            using var patrolPen = new Pen(selected ? Color.Magenta : Color.FromArgb(170, 220, 60, 60), selected ? 2.5f : 1.5f)
            { DashStyle = DashStyle.Dash };
            g.DrawLine(patrolPen, lx, ly, rx, ly);
            g.DrawLine(patrolPen, lx, ly - 5, lx, ly + 5);
            g.DrawLine(patrolPen, rx, ly - 5, rx, ly + 5);
        }
    }

    private void DrawTriggers(Graphics g)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        foreach (var t in Stage!.Triggers)
        {
            int px = (int)(t.x * scale) - ScrollX;
            int py = (int)(t.y * scale) - ScrollY;
            int pw = (int)(t.width * scale);
            int ph = (int)(t.height * scale);
            using var brush = new SolidBrush(Color.FromArgb(45, 255, 140, 0));
            using var pen = new Pen(Color.OrangeRed, 1.5f);
            g.FillRectangle(brush, px, py, pw, ph);
            g.DrawRectangle(pen, px, py, pw, ph);
            // トリガーID表示
            using var font = new Font("Meiryo UI", 6, FontStyle.Bold);
            using var tb = new SolidBrush(Color.OrangeRed);
            g.DrawString($"T:{t.id}", font, tb, px + 2, py + 2);
        }
    }

    private void DrawLayerIndicator(Graphics g)
    {
        string layerName = CurrentMode switch
        {
            EditMode.DecoLayerBack => "📌 装飾レイヤー[後景]編集中",
            EditMode.DecoLayerFront => "📌 装飾レイヤー[前景]編集中",
            EditMode.Trigger => "📌 トリガー配置中 (ドラッグで矩形)",
            EditMode.TestPlay => "📍 クリックでテストプレイ開始位置を指定",
            _ => ""
        };
        if (string.IsNullOrEmpty(layerName)) return;

        using var bgBrush = new SolidBrush(Color.FromArgb(160, 30, 30, 30));
        using var font = new Font("Meiryo UI", 8, FontStyle.Bold);
        using var tb = new SolidBrush(Color.Yellow);
        var sz = g.MeasureString(layerName, font);
        g.FillRectangle(bgBrush, 4, 4, sz.Width + 6, sz.Height + 4);
        g.DrawString(layerName, font, tb, 7, 6);
    }

    private void DrawMarker(Graphics g, float worldX, float worldY, string label, Color color, float fontSize = 7f)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        int px = (int)(worldX * scale) - ScrollX;
        int py = (int)(worldY * scale) - ScrollY;
        int sz = TILE_SIZE;
        if (px + sz < 0 || px > Width || py + sz < 0 || py > Height) return;

        using var brush = new SolidBrush(Color.FromArgb(180, color));
        g.FillRectangle(brush, px, py, sz, sz);
        using var pen = new Pen(color, 2);
        g.DrawRectangle(pen, px, py, sz, sz);
        using var font = new Font("Meiryo UI", fontSize, FontStyle.Bold);
        using var tb = new SolidBrush(Color.White);
        g.DrawString(label, font, tb, px + 1, py + 2);
    }

    // キャンバス座標 → タイル座標
    private (int col, int row) ToTile(int cx, int cy)
    {
        if (Stage == null) return (0, 0);
        int col = Math.Clamp((cx + ScrollX) / TILE_SIZE, 0, Stage.MapW - 1);
        int row = Math.Clamp((cy + ScrollY) / TILE_SIZE, 0, Stage.MapH - 1);
        return (col, row);
    }

    // キャンバス座標 → ワールド座標（グリッドスナップ）
    private (float wx, float wy) ToWorldSnapped(int cx, int cy)
    {
        float scale = (float)GAME_TILE / TILE_SIZE;
        float wx = (float)(Math.Floor((cx + ScrollX) * scale / GAME_TILE) * GAME_TILE);
        float wy = (float)(Math.Floor((cy + ScrollY) * scale / GAME_TILE) * GAME_TILE);
        return (wx, wy);
    }

    // Feature: UI改善（友人フィードバック対応）— ToTileと同様にマップ範囲へクランプする
    private (float wx, float wy) ToWorld(int cx, int cy)
    {
        if (Stage == null) return (0, 0);
        float scale = (float)GAME_TILE / TILE_SIZE;
        float wx = (cx + ScrollX) * scale;
        float wy = (cy + ScrollY) * scale;
        wx = Math.Clamp(wx, 0f, Stage.MapW * (float)GAME_TILE);
        wy = Math.Clamp(wy, 0f, Stage.MapH * (float)GAME_TILE);
        return (wx, wy);
    }

    // キャンバス座標 → スクリーン矩形
    private Rectangle ToScreenRect(float wx, float wy, float ww, float wh)
    {
        float scale = (float)TILE_SIZE / GAME_TILE;
        int px = (int)(wx * scale) - ScrollX;
        int py = (int)(wy * scale) - ScrollY;
        int pw = (int)(ww * scale);
        int ph = (int)(wh * scale);
        return new Rectangle(px, py, pw, ph);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Stage == null) return;

        // Feature 5: トリガーモードのドラッグ開始
        if (CurrentMode == EditMode.Trigger && e.Button == MouseButtons.Left)
        {
            _triggerDragStart = e.Location;
            _triggerDragRect = new Rectangle(e.X, e.Y, 0, 0);
            _isTriggerDragging = true;
            return;
        }

        if (e.Button == MouseButtons.Left) { isDragging = true; _strokePlacedAsset = false; HandleLeft(e.X, e.Y); }
        else if (e.Button == MouseButtons.Right) { isRightDrag = true; HandleRight(e.X, e.Y); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Stage == null) return;

        // Feature 5: トリガードラッグ中の矩形更新
        if (_isTriggerDragging && e.Button == MouseButtons.Left)
        {
            int x = Math.Min(_triggerDragStart.X, e.X);
            int y = Math.Min(_triggerDragStart.Y, e.Y);
            int w = Math.Abs(e.X - _triggerDragStart.X);
            int h = Math.Abs(e.Y - _triggerDragStart.Y);
            _triggerDragRect = new Rectangle(x, y, w, h);
            Invalidate();
            return;
        }

        // カーソル変更
        if (CurrentMode == EditMode.TestPlay) Cursor = Cursors.Cross;
        else if (CurrentMode == EditMode.Trigger) Cursor = Cursors.SizeNWSE;
        else Cursor = Cursors.Default;

        if (isDragging) HandleLeft(e.X, e.Y);
        else if (isRightDrag && CurrentMode == EditMode.Tile) HandleRight(e.X, e.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // Feature 5: トリガーのドラッグ確定
        if (_isTriggerDragging && e.Button == MouseButtons.Left)
        {
            _isTriggerDragging = false;
            if (_triggerDragRect.Width > 4 && _triggerDragRect.Height > 4)
            {
                var (wx1, wy1) = ToWorld(_triggerDragRect.Left, _triggerDragRect.Top);
                var (wx2, wy2) = ToWorld(_triggerDragRect.Right, _triggerDragRect.Bottom);
                var trigger = new EventTrigger
                {
                    id = $"trigger_{Stage!.Triggers.Count + 1:D3}",
                    x = wx1, y = wy1,
                    width = wx2 - wx1,
                    height = wy2 - wy1
                };
                TriggerPlaced?.Invoke(this, trigger);
            }
            _triggerDragRect = Rectangle.Empty;
            Invalidate();
            return;
        }

        bool wasEditing = isDragging || isRightDrag;
        isDragging = isRightDrag = false;
        
        if (wasEditing)
        {
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleLeft(int cx, int cy)
    {
        if (Stage == null) return;
        switch (CurrentMode)
        {
            case EditMode.Tile:
                var (col, row) = ToTile(cx, cy);
                if (Stage.Map[row, col] != SelectedTileId)
                { Stage.Map[row, col] = SelectedTileId; Fire(); }
                break;

            case EditMode.Eraser:
                var (colE, rowE) = ToTile(cx, cy);
                bool deletedTile = false;
                if (Stage.Map[rowE, colE] != 0) { Stage.Map[rowE, colE] = 0; deletedTile = true; }
                if (Stage.DecoLayerBack[rowE, colE] != 0) { Stage.DecoLayerBack[rowE, colE] = 0; deletedTile = true; }
                if (Stage.DecoLayerFront[rowE, colE] != 0) { Stage.DecoLayerFront[rowE, colE] = 0; deletedTile = true; }
                if (deletedTile) { Fire(); }
                DoDelete(cx, cy);
                break;

            case EditMode.DecoLayerBack: // Feature 1
                var (dcb, drb) = ToTile(cx, cy);
                if (Stage.DecoLayerBack[drb, dcb] != SelectedTileId)
                { Stage.DecoLayerBack[drb, dcb] = SelectedTileId; Fire(); }
                break;

            case EditMode.DecoLayerFront: // Feature 1
                var (dcf, drf) = ToTile(cx, cy);
                if (Stage.DecoLayerFront[drf, dcf] != SelectedTileId)
                { Stage.DecoLayerFront[drf, dcf] = SelectedTileId; Fire(); }
                break;

            case EditMode.Enemy:
                if (!string.IsNullOrEmpty(SelectedAssetId) && !_strokePlacedAsset)
                {
                    var (wx, wy) = ToWorldSnapped(cx, cy);
                    Stage.Enemies.Add(new PlacedEnemy { Id = SelectedAssetId, X = wx, Y = wy });
                    _strokePlacedAsset = true;
                    Fire();
                }
                break;

            case EditMode.Gimmick:
                if (!string.IsNullOrEmpty(SelectedAssetId) && !_strokePlacedAsset)
                {
                    var (wx, wy) = ToWorldSnapped(cx, cy);
                    Stage.Gimmicks.Add(new PlacedGimmick { Id = SelectedAssetId, X = wx, Y = wy });
                    _strokePlacedAsset = true;
                    Fire();
                }
                break;

            case EditMode.Item:
                if (!string.IsNullOrEmpty(SelectedAssetId) && !_strokePlacedAsset)
                {
                    var (wx, wy) = ToWorldSnapped(cx, cy);
                    Stage.Items.Add(new PlacedItem { Id = SelectedAssetId, X = wx, Y = wy });
                    _strokePlacedAsset = true;
                    Fire();
                }
                break;

            case EditMode.PlayerStart:
                var (psx, psy) = ToWorldSnapped(cx, cy);
                Stage.PlayerStartX = psx;
                Stage.PlayerStartY = psy;
                Fire();
                break;

            case EditMode.Goal:
                var (gx, gy) = ToWorldSnapped(cx, cy);
                Stage.GoalX = gx;
                Stage.GoalY = gy;
                Fire();
                break;

            case EditMode.Select:
                DoSelect(cx, cy);
                break;

            case EditMode.TestPlay: // Feature 4
                // Feature: UI改善（友人フィードバック対応）— マップ範囲外をクリックした場合は何も起きないようにする
                int tpCol = (cx + ScrollX) / TILE_SIZE;
                int tpRow = (cy + ScrollY) / TILE_SIZE;
                if (tpCol < 0 || tpCol >= Stage.MapW || tpRow < 0 || tpRow >= Stage.MapH) break;
                var (tx, ty) = ToWorld(cx, cy);
                TestPlayClicked?.Invoke(this, (tx, ty));
                break;
        }
        Invalidate();
    }

    private void HandleRight(int cx, int cy)
    {
        if (Stage == null) return;
        if (CurrentMode == EditMode.Tile)
        {
            var (col, row) = ToTile(cx, cy);
            if (Stage.Map[row, col] != 0) { Stage.Map[row, col] = 0; Fire(); Invalidate(); }
        }
        else if (CurrentMode == EditMode.DecoLayerBack) // Feature 1
        {
            var (col, row) = ToTile(cx, cy);
            if (Stage.DecoLayerBack[row, col] != 0) { Stage.DecoLayerBack[row, col] = 0; Fire(); Invalidate(); }
        }
        else if (CurrentMode == EditMode.DecoLayerFront) // Feature 1
        {
            var (col, row) = ToTile(cx, cy);
            if (Stage.DecoLayerFront[row, col] != 0) { Stage.DecoLayerFront[row, col] = 0; Fire(); Invalidate(); }
        }
        else
        {
            DoDelete(cx, cy);
        }
    }

    // Feature: 選択/削除ロジックの改善 — オブジェクトの実際のhitbox(なければGAME_TILE四方にフォールバック)で
    // 判定するようにし、複数重なっている場合はクリック位置に最も近い中心を持つものを優先する
    // （従来は敵→ギミック→アイテム→トリガーの固定順で最初に見つかったものを機械的に採用していた）。
    private (float x, float y, float w, float h) GetFootprint(object obj)
    {
        switch (obj)
        {
            case PlacedEnemy pe:
                {
                    var def = Assets?.Enemies.FirstOrDefault(d => d.id == pe.Id);
                    return def != null
                        ? (pe.X + def.hitboxOffsetX, pe.Y + def.hitboxOffsetY, def.hitboxWidth, def.hitboxHeight)
                        : (pe.X, pe.Y, GAME_TILE, GAME_TILE);
                }
            case PlacedGimmick pg:
                {
                    var def = Assets?.Gimmicks.FirstOrDefault(d => d.id == pg.Id);
                    return def != null
                        ? (pg.X + def.hitboxOffsetX, pg.Y + def.hitboxOffsetY, def.hitboxWidth, def.hitboxHeight)
                        : (pg.X, pg.Y, GAME_TILE, GAME_TILE);
                }
            case PlacedItem pi:
                {
                    var def = Assets?.Items.FirstOrDefault(d => d.id == pi.Id);
                    return def != null
                        ? (pi.X + def.hitboxOffsetX, pi.Y + def.hitboxOffsetY, def.hitboxWidth, def.hitboxHeight)
                        : (pi.X, pi.Y, GAME_TILE, GAME_TILE);
                }
            case EventTrigger t:
                return (t.x, t.y, t.width, t.height);
            default:
                return (0, 0, GAME_TILE, GAME_TILE);
        }
    }

    private void DoSelect(int cx, int cy)
    {
        if (Stage == null) return;
        var (wx, wy) = ToWorld(cx, cy);
        object? best = null;
        float bestDist = float.MaxValue;

        void Consider(object obj)
        {
            var (fx, fy, fw, fh) = GetFootprint(obj);
            if (wx < fx || wx > fx + fw || wy < fy || wy > fy + fh) return;
            float ccx = fx + fw / 2f, ccy = fy + fh / 2f;
            float dist = (wx - ccx) * (wx - ccx) + (wy - ccy) * (wy - ccy);
            if (dist < bestDist) { bestDist = dist; best = obj; }
        }

        foreach (var e in Stage.Enemies) Consider(e);
        foreach (var gi in Stage.Gimmicks) Consider(gi);
        foreach (var it in Stage.Items) Consider(it);
        foreach (var t in Stage.Triggers) Consider(t);

        SelectedObject = best;
        ObjectSelected?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void DoDelete(int cx, int cy)
    {
        if (Stage == null) return;
        var (wx, wy) = ToWorld(cx, cy);
        float bestDist = float.MaxValue;
        Action? removeBest = null;

        void Consider(object obj, Action remove)
        {
            var (fx, fy, fw, fh) = GetFootprint(obj);
            if (wx < fx || wx > fx + fw || wy < fy || wy > fy + fh) return;
            float ccx = fx + fw / 2f, ccy = fy + fh / 2f;
            float dist = (wx - ccx) * (wx - ccx) + (wy - ccy) * (wy - ccy);
            if (dist < bestDist) { bestDist = dist; removeBest = remove; }
        }

        for (int i = 0; i < Stage.Enemies.Count; i++) { int idx = i; Consider(Stage.Enemies[i], () => Stage.Enemies.RemoveAt(idx)); }
        for (int i = 0; i < Stage.Gimmicks.Count; i++) { int idx = i; Consider(Stage.Gimmicks[i], () => Stage.Gimmicks.RemoveAt(idx)); }
        for (int i = 0; i < Stage.Items.Count; i++) { int idx = i; Consider(Stage.Items[i], () => Stage.Items.RemoveAt(idx)); }
        for (int i = 0; i < Stage.Triggers.Count; i++) { int idx = i; Consider(Stage.Triggers[i], () => Stage.Triggers.RemoveAt(idx)); }

        if (removeBest != null) { removeBest(); Fire(); Invalidate(); }
    }

    private void Fire()
    {
        StageModified?.Invoke(this, EventArgs.Empty);
    }
}
