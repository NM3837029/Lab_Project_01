using System.Diagnostics;

namespace Lab_Editor;

public partial class Form1 : Form
{
    private readonly string projectRoot = @"C:\Users\naots\Documents\OriginalGame\C++\Lab_Project_01";
    private readonly string assetsPath;
    private readonly string stagesPath;
    private readonly string exePath;
    private readonly string testPlayJsonPath;  // Feature 4

    private AssetDefinitions assets = new();
    private StageData? currentStage;
    private string currentStageFile = "";
    private bool _loading = false;

    private HashSet<Keys> pressedKeys = new();

    public Form1()
    {
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
                Size = new Size(90, 28),
                BackColor = c,
                ForeColor = c.GetBrightness() < 0.5f ? Color.White : Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Meiryo UI", 7),
                TextAlign = ContentAlignment.MiddleLeft
            };
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

    // ===== ツールバー =====
    private void rbTool_CheckedChanged(object? sender, EventArgs e)
    {
        if (rbTileMode.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Tile;
        else if (rbErase.Checked)
        {
            // 消去モード: 現在のレイヤーに合わせて消去
            if (mapCanvas.CurrentMode == MapCanvas.EditMode.DecoLayerBack) { mapCanvas.SelectedTileId = 0; }
            else if (mapCanvas.CurrentMode == MapCanvas.EditMode.DecoLayerFront) { mapCanvas.SelectedTileId = 0; }
            else { mapCanvas.CurrentMode = MapCanvas.EditMode.Tile; mapCanvas.SelectedTileId = 0; }
        }
        else if (rbDecoBack.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.DecoLayerBack; }   // Feature 1
        else if (rbDecoFront.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.DecoLayerFront; } // Feature 1
        else if (rbSelect.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Select;
        else if (rbPlayerStart.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.PlayerStart;
        else if (rbGoal.Checked) mapCanvas.CurrentMode = MapCanvas.EditMode.Goal;
        else if (rbTrigger.Checked) { mapCanvas.CurrentMode = MapCanvas.EditMode.Trigger; }           // Feature 5
        UpdateLayerInfo();
    }

    private void UpdateLayerInfo()
    {
        lblLayerInfo.Text = mapCanvas.CurrentMode switch
        {
            MapCanvas.EditMode.DecoLayerBack => "🌿 後景装飾レイヤー編集中",
            MapCanvas.EditMode.DecoLayerFront => "🌸 前景装飾レイヤー編集中",
            MapCanvas.EditMode.Trigger => "⚡ トリガー配置（ドラッグで矩形）",
            MapCanvas.EditMode.TestPlay => "📍 クリックでテストプレイ開始位置を指定",
            _ => ""
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

    private void mapCanvas_StageModified(object? sender, EventArgs e)
        => SaveCurrentStage();

    // Feature 4: ここからプレイ — マップクリックでテストプレイ開始位置確定
    private void mapCanvas_TestPlayClicked(object? sender, (float wx, float wy) pos)
    {
        if (currentStage == null) return;
        // テストプレイモードを解除して通常モードへ戻す
        rbTileMode.Checked = true;
        mapCanvas.CurrentMode = MapCanvas.EditMode.Tile;
        UpdateLayerInfo();

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

    private void OpenTriggerEditor(EventTrigger trigger, bool isNew = false)
    {
        var form = new EventEditorForm(trigger);
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
    private void btnTestPlay_Click(object? sender, EventArgs e)
    {
        if (currentStage == null) { MessageBox.Show("ステージを選択してください。"); return; }
        mapCanvas.CurrentMode = MapCanvas.EditMode.TestPlay;
        UpdateLayerInfo();
        MessageBox.Show("マップ上でテストプレイを開始したい位置をクリックしてください。\n\n右クリックでキャンセル。", "ここからプレイ",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        var form = new SoundManagerForm(projectRoot, assets.Bgm, assets.Se);
        if (form.ShowDialog() == DialogResult.OK)
        {
            assets.Bgm = form.ResultBgm;
            assets.Se = form.ResultSe;
            assets.SaveToFolder(assetsPath);
            // ステージのBGM選択UIを更新（必要に応じて）
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
