using System.Diagnostics;

namespace Lab_Editor;

public partial class Form1 : Form
{
    private readonly string projectRoot;
    private readonly string assetsPath;
    private readonly string stagesPath;
    private readonly string exePath;
    private readonly string testPlayJsonPath;  // Feature 4

    private AssetDefinitions assets = new();
    private StageData? currentStage;
    private string currentStageFile = "";
    private bool _loading = false;
    private HistoryManager historyMgr = new HistoryManager();

    private HashSet<Keys> pressedKeys = new();

    // イベントリスト(Layer 4)の各行 ↔ 実オブジェクトの対応。"START"/"GOAL" は特別扱い。
    private List<object?> _placedEventRefs = new();

    public Form1()
    {
        projectRoot = AppPaths.ProjectRoot;
        assetsPath = Path.Combine(projectRoot, "assets");
        stagesPath = Path.Combine(assetsPath, "stages");
        exePath = Path.Combine(projectRoot, "x64", "Debug", "Lab_Project_01.exe");
        testPlayJsonPath = Path.Combine(stagesPath, "_test_play.json"); // Feature 4
        InitializeComponent();
        mapCanvas.AssetsPath = assetsPath;
        KeyPreview = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        pressedKeys.Add(e.KeyCode);
        if (pressedKeys.Contains(Keys.Up) && pressedKeys.Contains(Keys.Down) &&
            pressedKeys.Contains(Keys.Left) && pressedKeys.Contains(Keys.Right))
        { pressedKeys.Clear(); LaunchGame(); }
        
        if (e.Control && e.KeyCode == Keys.Z) Undo();
        if (e.Control && e.KeyCode == Keys.Y) Redo();
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressedKeys.Remove(e.KeyCode); }

    // ===== 初期化 =====
    private void Form1_Load(object? sender, EventArgs e)
    {
        if (!Directory.Exists(stagesPath)) Directory.CreateDirectory(stagesPath);
        // sound フォルダを事前作成 (Feature 3)
        Directory.CreateDirectory(Path.Combine(projectRoot, "sound"));
        assets = AssetDefinitions.LoadFromFolder(assetsPath);
        RefreshPalette();
        RefreshStageList();
        if (Directory.GetFiles(stagesPath, "*.json").Length == 0) CreateNewStage("stage_01");
        UpdateStatus();
        ShowFirstRunGuideIfNeeded();
    }

    // Feature: UI改善（提案書 MW-1）— 初めて開いたときだけ使い方ガイドを自動表示する。
    // 一度表示したらマーカーファイルを作り、以後は「ヘルプ」メニューから任意で開く形に切り替える。
    private void ShowFirstRunGuideIfNeeded()
    {
        string marker = Path.Combine(AppPaths.LogsDir, "first_run_guide_shown.flag");
        if (File.Exists(marker)) return;
        try { File.WriteAllText(marker, DateTime.Now.ToString("o")); } catch { }
        new HelpForm().ShowDialog(this);
    }

    // ===== ステージリスト =====
    private void RefreshStageList()
    {
        lstStages.Items.Clear();
        if (Directory.Exists(stagesPath))
            foreach (var f in Directory.GetFiles(stagesPath, "*.json"))
            {
                var name = Path.GetFileName(f);
                if (name != "_test_play.json") // Feature 4: テスト用JSONは表示しない
                    lstStages.Items.Add(name);
            }
        if (lstStages.Items.Count > 0 && lstStages.SelectedIndex < 0)
            lstStages.SelectedIndex = 0;
    }

    private void lstStages_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstStages.SelectedItem == null) return;
        currentStageFile = lstStages.SelectedItem.ToString() ?? "";
        LoadCurrentStage();
    }

    private void LoadCurrentStage()
    {
        if (string.IsNullOrEmpty(currentStageFile)) return;
        _loading = true;
        try
        {
            currentStage = StageData.LoadFromFile(Path.Combine(stagesPath, currentStageFile));
            historyMgr.Clear();
            historyMgr.Push(currentStage);

            mapCanvas.Stage = currentStage;
            mapCanvas.Assets = assets;
            mapCanvas.RefreshTileColors();
            mapCanvas.ScrollX = 0;
            mapCanvas.ScrollY = 0;
            hScrollMap.Value = 0;
            vScrollMap.Value = 0;
            UpdateScrollBars();
            mapCanvas.Invalidate();

            numStartX.Value = Math.Clamp((decimal)currentStage.PlayerStartX, 0, 9999);
            numStartY.Value = Math.Clamp((decimal)currentStage.PlayerStartY, 0, 9999);
            chkDoubleJump.Checked = currentStage.Capabilities.canDoubleJump;
            chkDash.Checked = currentStage.Capabilities.canDash;
            chkFireball.Checked = currentStage.Capabilities.canShootFireball;
            chkFly.Checked = currentStage.Capabilities.canFly;
            numJumpPower.Value = Math.Clamp(currentStage.Capabilities.baseJumpPower, -30, 0);
            numSpeed.Value = (decimal)Math.Clamp(currentStage.Capabilities.baseSpeed, 0.5f, 20f);

            numMapW.Value = currentStage.MapW;
            numMapH.Value = currentStage.MapH;

            lblCurrentStage.Text = $"編集中: {currentStageFile}  ({currentStage.MapW}×{currentStage.MapH}) | 敵:{currentStage.Enemies.Count} ギミック:{currentStage.Gimmicks.Count} トリガー:{currentStage.Triggers.Count}";
            RefreshPlacedEvents();
        }
        finally { _loading = false; }
    }

    // ===== スクロールバー =====
    private void UpdateScrollBars()
    {
        if (currentStage == null) return;
        int maxX = Math.Max(0, currentStage.MapW * MapCanvas.TILE_SIZE - mapCanvas.Width + 50);
        int maxY = Math.Max(0, currentStage.MapH * MapCanvas.TILE_SIZE - mapCanvas.Height + 50);
        hScrollMap.Maximum = maxX + hScrollMap.LargeChange;
        vScrollMap.Maximum = maxY + vScrollMap.LargeChange;
        hScrollMap.Value = Math.Min(hScrollMap.Value, maxX);
        vScrollMap.Value = Math.Min(vScrollMap.Value, maxY);
    }

    private void hScrollMap_Scroll(object? sender, ScrollEventArgs e)
    { mapCanvas.ScrollX = e.NewValue; mapCanvas.Invalidate(); }

    private void vScrollMap_Scroll(object? sender, ScrollEventArgs e)
    { mapCanvas.ScrollY = e.NewValue; mapCanvas.Invalidate(); }

    // ===== パレット（タイル・敵・ギミック・アイテム）=====
    private void RefreshPalette()
    {
        flpTiles.Controls.Clear();
        foreach (var t in assets.Tiles)
        {
            Color c;
            try { c = ColorTranslator.FromHtml(t.color); } catch { c = Color.Gray; }
            var btn = new Button
            {
                Text = $"{t.id}:{t.name}",
                Tag = t.id,
                Size = new Size(104, 34),
                BackColor = c,
                ForeColor = c.GetBrightness() < 0.5f ? Color.White : Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Meiryo UI", 7),
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 3, 0),
            };
            // Feature: UI改善（提案書 MP-1）— タイルボタンが単色の四角のみで見た目が分からなかったため、
            // スプライトが設定されていれば実際の画像を小さいアイコンとして添える（無ければ従来通り色のみ）。
            var icon = LoadTileIcon(t.sprite, 24);
            if (icon != null)
            {
                btn.Image = icon;
                btn.ImageAlign = ContentAlignment.MiddleLeft;
                btn.TextImageRelation = TextImageRelation.ImageBeforeText;
            }
            btn.Click += TileBtn_Click;
            flpTiles.Controls.Add(btn);
        }

        lstEnemies.Items.Clear();
        foreach (var e in assets.Enemies) lstEnemies.Items.Add($"{e.name}");

        lstGimmicks.Items.Clear();
        foreach (var g in assets.Gimmicks) lstGimmicks.Items.Add($"{g.name}");

        lstItems.Items.Clear();
        foreach (var i in assets.Items) lstItems.Items.Add($"{i.name}");
    }

    private static Image? LoadTileIcon(string spritePath, int size)
    {
        if (string.IsNullOrEmpty(spritePath)) return null;
        string full = Path.Combine(AppPaths.ProjectRoot, spritePath.Replace('/', '\\'));
        if (!File.Exists(full)) return null;
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var src = Image.FromStream(fs);
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.DrawImage(src, 0, 0, size, size);
            return bmp;
        }
        catch { return null; }
    }

    private void TileBtn_Click(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            // 現在アクティブなレイヤーモードを保持しつつタイル選択
            if (mapCanvas.CurrentMode != MapCanvas.EditMode.DecoLayerBack &&
                mapCanvas.CurrentMode != MapCanvas.EditMode.DecoLayerFront)
                mapCanvas.CurrentMode = MapCanvas.EditMode.Tile;
            mapCanvas.SelectedTileId = id;
            foreach (Control c in flpTiles.Controls)
                if (c is Button b) b.FlatStyle = b == btn ? FlatStyle.Standard : FlatStyle.Flat;
        }
    }

    // Feature: UI改善（友人フィードバック対応）— 行・列数を数値指定して矩形範囲を一括でタイル埋めする
    private void BtnBulkFill_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("先にステージを選択してください。"); return; }

        int[,] targetLayer = mapCanvas.CurrentMode switch
        {
            MapCanvas.EditMode.DecoLayerBack => currentStage.DecoLayerBack,
            MapCanvas.EditMode.DecoLayerFront => currentStage.DecoLayerFront,
            _ => currentStage.Map
        };

        using var form = new BulkTileFillForm(currentStage.MapW, currentStage.MapH);
        if (form.ShowDialog() != DialogResult.OK) return;

        int rEnd = Math.Min(currentStage.MapH, form.StartRow + form.RowCount);
        int cEnd = Math.Min(currentStage.MapW, form.StartCol + form.ColCount);
        for (int r = form.StartRow; r < rEnd; r++)
            for (int c = form.StartCol; c < cEnd; c++)
                targetLayer[r, c] = mapCanvas.SelectedTileId;

        mapCanvas.Invalidate();
        SaveCurrentStage();
    }

    private void lstEnemies_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstEnemies.SelectedIndex >= 0 && lstEnemies.SelectedIndex < assets.Enemies.Count)
        {
            mapCanvas.CurrentMode = MapCanvas.EditMode.Enemy;
            mapCanvas.SelectedAssetId = assets.Enemies[lstEnemies.SelectedIndex].id;
            lstGimmicks.ClearSelected(); lstItems.ClearSelected();
            UpdateLayerInfo();
        }
    }

    private void lstGimmicks_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstGimmicks.SelectedIndex >= 0 && lstGimmicks.SelectedIndex < assets.Gimmicks.Count)
        {
            mapCanvas.CurrentMode = MapCanvas.EditMode.Gimmick;
            mapCanvas.SelectedAssetId = assets.Gimmicks[lstGimmicks.SelectedIndex].id;
            lstEnemies.ClearSelected(); lstItems.ClearSelected();
            UpdateLayerInfo();
        }
    }

    private void lstItems_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstItems.SelectedIndex >= 0 && lstItems.SelectedIndex < assets.Items.Count)
        {
            mapCanvas.CurrentMode = MapCanvas.EditMode.Item;
            mapCanvas.SelectedAssetId = assets.Items[lstItems.SelectedIndex].id;
            lstEnemies.ClearSelected(); lstGimmicks.ClearSelected();
            UpdateLayerInfo();
        }
    }

    // ===== Undo / Redo =====
    private void Undo()
    {
        if (historyMgr.CanUndo)
        {
            currentStage = historyMgr.Undo();
            mapCanvas.Stage = currentStage;
            mapCanvas.Invalidate();
            SaveCurrentStage();
            RefreshPlacedEvents();
        }
    }

    private void Redo()
    {
        if (historyMgr.CanRedo)
        {
            currentStage = historyMgr.Redo();
            mapCanvas.Stage = currentStage;
            mapCanvas.Invalidate();
            SaveCurrentStage();
            RefreshPlacedEvents();
        }
    }

    // ===== ツールバー・レイヤー =====
    private void TsbLayer_Click(object? sender, EventArgs e)
    {
        tsbLayer1.Checked = sender == tsbLayer1;
        tsbLayer2.Checked = sender == tsbLayer2;
        tsbLayer3.Checked = sender == tsbLayer3;
        tsbLayer4.Checked = sender == tsbLayer4;

        if (tsbLayer1.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.DecoLayerBack; tabRight.SelectedTab = tabTilePalette; }
        else if (tsbLayer2.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.Tile; tabRight.SelectedTab = tabTilePalette; }
        else if (tsbLayer3.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.DecoLayerFront; tabRight.SelectedTab = tabTilePalette; }
        else if (tsbLayer4.Checked) 
        { 
            // イベントモード時はイベント配置パレットを表示
            tabRight.SelectedTab = tabEventPalette; 
            UpdateEventModeSubTool();
        }
        UpdateLayerInfo();
        mapCanvas.Invalidate();
    }

    private void TsbTool_Click(object? sender, EventArgs e)
    {
        tsbPen.Checked = sender == tsbPen;
        tsbEraser.Checked = sender == tsbEraser;
        tsbSelect.Checked = sender == tsbSelect;

        if (tsbEraser.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Eraser;
        else if (tsbSelect.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Select;
        else if (tsbPen.Checked) 
        {
            // ペンに戻す際、現在のレイヤーに合わせたモードに戻す
            TsbLayer_Click(tsbLayer1.Checked ? tsbLayer1 : (tsbLayer2.Checked ? tsbLayer2 : (tsbLayer3.Checked ? tsbLayer3 : tsbLayer4)), EventArgs.Empty);
        }
        UpdateLayerInfo();
    }

    private void EventModeItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (tsbLayer4.Checked)
            UpdateEventModeSubTool();
    }

    private void UpdateEventModeSubTool()
    {
        if (rbTrigger.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Trigger;
        else if (rbPlayerStart.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.PlayerStart;
        else if (rbGoal.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Goal;
        else if (lstEnemies.SelectedIndex >= 0) mapCanvas.CurrentMode = MapCanvas.EditMode.Enemy;
        else if (lstGimmicks.SelectedIndex >= 0) mapCanvas.CurrentMode = MapCanvas.EditMode.Gimmick;
        else if (lstItems.SelectedIndex >= 0) mapCanvas.CurrentMode = MapCanvas.EditMode.Item;
        else mapCanvas.CurrentMode = MapCanvas.EditMode.Select; // デフォルト
        UpdateLayerInfo();
    }

    // ===== 配置済みイベントリスト (Layer 4) =====
    private void RefreshPlacedEvents()
    {
        lstPlacedEvents.Items.Clear();
        _placedEventRefs.Clear();
        if (currentStage == null) return;

        lstPlacedEvents.Items.Add($"🚶 Start ({currentStage.PlayerStartX}, {currentStage.PlayerStartY})");
        _placedEventRefs.Add("START");

        if (currentStage.GoalX >= 0)
        {
            lstPlacedEvents.Items.Add($"🏁 Goal ({currentStage.GoalX}, {currentStage.GoalY})");
            _placedEventRefs.Add("GOAL");
        }

        foreach (var t in currentStage.Triggers) { lstPlacedEvents.Items.Add($"⚡ {t.id} ({t.x},{t.y})"); _placedEventRefs.Add(t); }
        foreach (var e in currentStage.Enemies) { lstPlacedEvents.Items.Add($"👾 {e.Id} ({e.X},{e.Y})"); _placedEventRefs.Add(e); }
        foreach (var g in currentStage.Gimmicks) { lstPlacedEvents.Items.Add($"🔧 {g.Id} ({g.X},{g.Y})"); _placedEventRefs.Add(g); }
        foreach (var i in currentStage.Items) { lstPlacedEvents.Items.Add($"💎 {i.Id} ({i.X},{i.Y})"); _placedEventRefs.Add(i); }
    }

    private void LstPlacedEvents_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // リストで選択したらキャンバス上でも選択状態にする処理をここに入れる（拡張用）
    }

    // イベントリスト(Layer 4)をダブルクリックすると、該当オブジェクトへキャンバスをジャンプさせて選択する
    // (MZの「リスト選択で該当イベントの編集画面に直接ジャンプ」に相当)。トリガーは編集ダイアログも開く。
    private void LstPlacedEvents_DoubleClick(object? sender, EventArgs e)
    {
        int idx = lstPlacedEvents.SelectedIndex;
        if (currentStage == null || idx < 0 || idx >= _placedEventRefs.Count) return;

        switch (_placedEventRefs[idx])
        {
            case "START":
                SelectAndScrollTo(null, currentStage.PlayerStartX, currentStage.PlayerStartY);
                break;
            case "GOAL":
                if (currentStage.GoalX >= 0) SelectAndScrollTo(null, currentStage.GoalX, currentStage.GoalY);
                break;
            case EventTrigger t:
                SelectAndScrollTo(t, t.x, t.y);
                OpenTriggerEditor(t);
                break;
            case PlacedEnemy en:
                SelectAndScrollTo(en, en.X, en.Y);
                break;
            case PlacedGimmick g:
                SelectAndScrollTo(g, g.X, g.Y);
                break;
            case PlacedItem it:
                SelectAndScrollTo(it, it.X, it.Y);
                break;
        }
    }

    // キャンバス上の指定オブジェクトへスクロールして選択状態にする
    private void SelectAndScrollTo(object? target, float worldX, float worldY)
    {
        if (currentStage == null) return;
        float scale = (float)MapCanvas.TILE_SIZE / MapCanvas.GAME_TILE;
        int px = (int)(worldX * scale);
        int py = (int)(worldY * scale);

        int maxX = Math.Max(0, currentStage.MapW * MapCanvas.TILE_SIZE - mapCanvas.Width + 50);
        int maxY = Math.Max(0, currentStage.MapH * MapCanvas.TILE_SIZE - mapCanvas.Height + 50);
        int scrollX = Math.Clamp(px - mapCanvas.Width / 2, 0, maxX);
        int scrollY = Math.Clamp(py - mapCanvas.Height / 2, 0, maxY);

        mapCanvas.SelectedObject = target;
        mapCanvas.ScrollX = scrollX;
        mapCanvas.ScrollY = scrollY;
        UpdateScrollBars();
        hScrollMap.Value = Math.Min(scrollX, hScrollMap.Maximum);
        vScrollMap.Value = Math.Min(scrollY, vScrollMap.Maximum);
        propertyGrid.SelectedObject = target;
        mapCanvas.Invalidate();
    }

    private void UpdateLayerInfo()
    {
        lblLayerInfo.Text = mapCanvas.CurrentMode switch
        {
            MapCanvas.EditMode.DecoLayerBack => "Layer 1: 遠景レイヤー編集中",
            MapCanvas.EditMode.Tile => "Layer 2: メイン（地形）編集中",
            MapCanvas.EditMode.DecoLayerFront => "Layer 3: 近景レイヤー編集中",
            MapCanvas.EditMode.Trigger => "Layer 4: トリガー配置（ドラッグで矩形）",
            MapCanvas.EditMode.PlayerStart => "Layer 4: 開始位置設定",
            MapCanvas.EditMode.Goal => "Layer 4: ゴール設定",
            MapCanvas.EditMode.Enemy => "Layer 4: 敵の配置",
            MapCanvas.EditMode.Gimmick => "Layer 4: ギミックの配置",
            MapCanvas.EditMode.Item => "Layer 4: アイテムの配置",
            MapCanvas.EditMode.TestPlay => "📍 クリックでテストプレイ開始位置を指定",
            _ => "選択 / 消去 モード"
        };
    }

    // ===== プロパティ =====
    private void mapCanvas_ObjectSelected(object? sender, EventArgs e)
    {
        propertyGrid.SelectedObject = mapCanvas.SelectedObject;
        // トリガーが選択された場合は編集フォームを開く (Feature 5)
        if (mapCanvas.SelectedObject is EventTrigger trigger)
        {
            OpenTriggerEditor(trigger);
        }
    }

    // StageModified はドラッグ中のタイル1マスごとに何度も発火するため、
    // ここでは即時保存せずキャンバスの再描画のみ行う（保存はストローク完了時の EditCompleted にまとめる）。
    private void mapCanvas_StageModified(object? sender, EventArgs e) { }

    private void mapCanvas_EditCompleted(object? sender, EventArgs e)
    {
        if (currentStage != null)
        {
            SaveCurrentStage();
            historyMgr.Push(currentStage);
        }
    }

    // Feature 4: ここからプレイ （マップクリックでテストプレイ開始位置確定）
    private void mapCanvas_TestPlayClicked(object? sender, (float wx, float wy) pos)
    {
        if (currentStage == null) return;
        // テストプレイモードを解除して通常モードへ戻す
        tsbLayer2.Checked = true;
        TsbLayer_Click(tsbLayer2, EventArgs.Empty);

        // 一時JSONを生成して起動
        try
        {
            SaveCurrentStage(); // まず保存
            currentStage.SaveAsTestPlay(testPlayJsonPath, pos.wx, pos.wy);
            LaunchGameWithFile("_test_play.json");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"テストプレイの準備に失敗しました:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Feature 5: トリガー矩形が確定された時
    private void mapCanvas_TriggerPlaced(object? sender, EventTrigger trigger)
    {
        // イベントエディタを開く
        OpenTriggerEditor(trigger, isNew: true);
    }

    private List<string> GetStageFileNames()
    {
        if (!Directory.Exists(stagesPath)) return new List<string>();
        return Directory.GetFiles(stagesPath, "*.json")
            .Select(Path.GetFileName)
            .Where(n => n != null && n != "_test_play.json")
            .Select(n => n!)
            .ToList();
    }

    private void OpenTriggerEditor(EventTrigger trigger, bool isNew = false)
    {
        var form = new EventEditorForm(trigger, assets, GetStageFileNames());
        if (form.ShowDialog() == DialogResult.OK)
        {
            if (isNew && currentStage != null)
            {
                currentStage.Triggers.Add(form.ResultTrigger);
            }
            else if (!isNew && currentStage != null)
            {
                // 既存トリガーを更新
                var idx = currentStage.Triggers.FindIndex(t => t.id == trigger.id);
                if (idx >= 0) currentStage.Triggers[idx] = form.ResultTrigger;
            }
            SaveCurrentStage();
            mapCanvas.Invalidate();
        }
        else if (!isNew)
        {
            // キャンセル時 — トリガー削除確認
            if (MessageBox.Show("このトリガーを削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                currentStage?.Triggers.RemoveAll(t => t.id == trigger.id);
                SaveCurrentStage();
                mapCanvas.Invalidate();
            }
        }
    }

    // ===== プレイヤー設定変更 =====
    private void PlayerSetting_Changed(object? sender, EventArgs e)
    {
        if (_loading || currentStage == null) return;
        SaveCurrentStage();
    }

    // ===== 保存 =====
    private void SaveCurrentStage()
    {
        if (currentStage == null || string.IsNullOrEmpty(currentStageFile)) return;

        currentStage.PlayerStartX = (float)numStartX.Value;
        currentStage.PlayerStartY = (float)numStartY.Value;
        currentStage.Capabilities.canDoubleJump = chkDoubleJump.Checked;
        currentStage.Capabilities.canDash = chkDash.Checked;
        currentStage.Capabilities.canShootFireball = chkFireball.Checked;
        currentStage.Capabilities.canFly = chkFly.Checked;
        currentStage.Capabilities.baseJumpPower = (int)numJumpPower.Value;
        currentStage.Capabilities.baseSpeed = (float)numSpeed.Value;

        currentStage.SaveToFile(Path.Combine(stagesPath, currentStageFile));
    }

    private void btnSave_Click(object? sender, EventArgs e)
    {
        SaveCurrentStage();
        MessageBox.Show("ステージを保存しました！", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ===== ステージサイズ変更 =====
    private void btnResize_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) return;
        int newW = (int)numMapW.Value;
        int newH = (int)numMapH.Value;
        if (newW == currentStage.MapW && newH == currentStage.MapH) return;

        var res = MessageBox.Show(
            $"ステージサイズを {currentStage.MapW}×{currentStage.MapH} → {newW}×{newH} に変更します。\n範囲外のデータは失われます。続けますか？",
            "サイズ変更確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (res != DialogResult.Yes) return;

        currentStage.ResizeMap(newW, newH);
        UpdateScrollBars();
        mapCanvas.Invalidate();
        lblCurrentStage.Text = $"編集中: {currentStageFile}  ({newW}×{newH})";
        SaveCurrentStage();
    }

    // ===== 新規・削除 =====
    private void btnCreateStage_Click(object? sender, EventArgs e)
    {
        string name = txtNewStage.Text.Trim();
        if (string.IsNullOrEmpty(name)) { MessageBox.Show("名前を入力してください"); return; }
        CreateNewStage(name);
        txtNewStage.Text = "";
    }

    private void CreateNewStage(string name)
    {
        if (!name.EndsWith(".json")) name += ".json";
        currentStageFile = name;
        currentStage = new StageData();
        SaveCurrentStage();
        RefreshStageList();
        for (int i = 0; i < lstStages.Items.Count; i++)
            if (lstStages.Items[i].ToString() == name) { lstStages.SelectedIndex = i; break; }
    }

    private void btnDeleteStage_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(currentStageFile)) return;
        if (MessageBox.Show($"「{currentStageFile}」を削除しますか？", "確認",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            string path = Path.Combine(stagesPath, currentStageFile);
            if (File.Exists(path)) File.Delete(path);
            currentStageFile = ""; currentStage = null;
            RefreshStageList();
        }
    }

    // ===== プレイテスト =====
    private void btnPlay_Click(object? sender, EventArgs e) => LaunchGame();

    private void LaunchGame()
    {
        SaveCurrentStage();
        LaunchGameWithFile(currentStageFile);
    }

    private void LaunchGameWithFile(string stageFileName)
    {
        if (!File.Exists(exePath))
        { MessageBox.Show($"ゲーム実行ファイルが見つかりません:\n{exePath}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        if (string.IsNullOrEmpty(stageFileName))
        { MessageBox.Show("ステージを選択してください。", "選択エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try { Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = projectRoot, Arguments = stageFileName }); }
        catch (Exception ex) { MessageBox.Show($"起動失敗:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // Feature 4: ここからプレイ
    // Feature: UI改善（友人フィードバック対応）— 確認モーダルポップアップはうざいとの指摘のため削除。
    // 案内は既存のlblLayerInfo（UpdateLayerInfo内で「📍 クリックでテストプレイ開始位置を指定」を表示）に一本化する。
    private void btnTestPlay_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("ステージを選択してください。"); return; }
        mapCanvas.CurrentMode = MapCanvas.EditMode.TestPlay;
        UpdateLayerInfo();
    }

    // Feature 1: 背景設定
    private void btnBgSettings_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("ステージを選択してください。"); return; }
        var form = new BackgroundSettingsForm(projectRoot, currentStage.Backgrounds);
        if (form.ShowDialog() == DialogResult.OK)
        {
            currentStage.Backgrounds = form.ResultLayers;
            SaveCurrentStage();
        }
    }

    // Feature 3: サウンド管理
    private void btnSoundMgr_Click(object? sender, EventArgs e)
    {
        var form = new SoundManagerForm(projectRoot, assets.Bgm, assets.Se, assets.UiSe,
            assets.Enemies, assets.Gimmicks, assets.Items, assets.CommonEvents);
        if (form.ShowDialog() == DialogResult.OK)
        {
            assets.Bgm = form.ResultBgm;
            assets.Se = form.ResultSe;
            assets.UiSe = form.ResultUiSe;
            assets.SaveToFolder(assetsPath);
        }
    }

    // Feature: サウンド・アセット管理の刷新 — カタログのBGM/SE IDを敵/ギミック/アイテム/現在のステージへ割り当てる
    private void btnSoundAssign_Click(object? sender, EventArgs e)
    {
        string? stageName = currentStage != null && !string.IsNullOrEmpty(currentStageFile) ? currentStageFile : null;
        string stageBgmId = currentStage?.BgmId ?? "";
        var form = new SoundAssignmentForm(assets.Enemies, assets.Gimmicks, assets.Items,
            assets.Se, assets.UiSe, assets.Bgm, stageName, stageBgmId);
        if (form.ShowDialog() == DialogResult.OK)
        {
            assets.Enemies = form.ResultEnemies;
            assets.Gimmicks = form.ResultGimmicks;
            assets.Items = form.ResultItems;
            assets.SaveToFolder(assetsPath);

            if (form.ResultStageBgmId != null && currentStage != null)
            {
                currentStage.BgmId = form.ResultStageBgmId;
                SaveCurrentStage();
            }
        }
    }

    // Feature 2: アニメーションエディタ
    private void btnAnimEditor_Click(object? sender, EventArgs e)
    {
        // アセットIDを選択させるためのリスト
        var ids = assets.Enemies.Select(x => x.id)
            .Concat(assets.Gimmicks.Select(x => x.id))
            .Concat(assets.Items.Select(x => x.id))
            .ToList();

        if (ids.Count == 0) { MessageBox.Show("アセットが登録されていません。先にアセット管理でアセットを追加してください。"); return; }

        // 簡易選択ダイアログ
        string? selectedId = null;
        using var picker = new Form
        {
            Text = "アニメーション編集するアセットを選択", Size = new Size(360, 200),
            StartPosition = FormStartPosition.CenterParent
        };
        var lb = new ListBox { Dock = DockStyle.Fill };
        ids.ForEach(id => lb.Items.Add(id));
        var btnOk = new Button { Text = "OK", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
        picker.Controls.AddRange(new Control[] { lb, btnOk });
        picker.AcceptButton = btnOk;
        if (picker.ShowDialog() == DialogResult.OK && lb.SelectedItem != null)
            selectedId = lb.SelectedItem.ToString();

        if (string.IsNullOrEmpty(selectedId)) return;

        var existing = assets.Animations.FirstOrDefault(a => a.assetId == selectedId) ?? new AnimationSet { assetId = selectedId };
        var form = new AnimationEditorForm(projectRoot, existing);
        if (form.ShowDialog() == DialogResult.OK)
        {
            var idx = assets.Animations.FindIndex(a => a.assetId == selectedId);
            if (idx >= 0) assets.Animations[idx] = form.ResultSet;
            else assets.Animations.Add(form.ResultSet);
            assets.SaveToFolder(assetsPath);
        }
    }

    // ===== アセット管理 =====
    private void btnAssetManager_Click(object? sender, EventArgs e)
    {
        var form = new AssetManagerForm(assetsPath, assets);
        if (form.ShowDialog() == DialogResult.OK)
        { assets = AssetDefinitions.LoadFromFolder(assetsPath); RefreshPalette(); if (currentStage != null) { mapCanvas.Assets = assets; mapCanvas.RefreshTileColors(); } }
    }

    // ===== タイルエディタ =====
    private void btnTileEditor_Click(object? sender, EventArgs e)
    {
        var form = new TileEditorForm(assetsPath, assets.Tiles);
        if (form.ShowDialog() == DialogResult.OK)
        { assets = AssetDefinitions.LoadFromFolder(assetsPath); RefreshPalette(); if (currentStage != null) { mapCanvas.Assets = assets; mapCanvas.RefreshTileColors(); } }
    }

    // ===== CSV インポート =====
    private void btnImportCsv_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "CSVファイル|*.csv|すべて|*.*", Title = "CSVからステージを生成" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        try
        {
            var data = StageData.LoadFromCsv(ofd.FileName);
            string baseName = Path.GetFileNameWithoutExtension(ofd.FileName) + ".json";
            string savePath = Path.Combine(stagesPath, baseName);
            data.SaveToFile(savePath);

            MessageBox.Show($"CSVからステージ「{baseName}」を生成しました！\nサイズ: {data.MapW}×{data.MapH}", "生成完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshStageList();
            for (int i = 0; i < lstStages.Items.Count; i++)
                if (lstStages.Items[i].ToString() == baseName) { lstStages.SelectedIndex = i; break; }
        }
        catch (Exception ex)
        { MessageBox.Show($"CSVの読み込みに失敗しました:\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void UpdateStatus()
    {
        if (File.Exists(exePath)) { lblStatus.Text = "✅ ゲームエンジン検出済み"; lblStatus.ForeColor = Color.Green; }
        else { lblStatus.Text = "⚠ ゲームのビルドが見つかりません"; lblStatus.ForeColor = Color.Red; }
    }
}
