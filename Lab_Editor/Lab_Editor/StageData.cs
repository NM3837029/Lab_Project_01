using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ===== タイル定義 =====
public class TileDef
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string color { get; set; } = "#CCCCCC";
    public bool collidable { get; set; } = true;
    public bool deadly { get; set; } = false;
    public string sprite { get; set; } = "";
}

// ===== 背景レイヤー定義 (Feature 1) =====
public class BackgroundLayer
{
    public string sprite { get; set; } = "";
    public int drawOrder { get; set; } = 0;
    public float scrollRate { get; set; } = 0.3f;
    public bool loop { get; set; } = true;
    public float offsetX { get; set; } = 0;
    public float offsetY { get; set; } = 0;
}

// ===== アニメーション定義 (Feature 2) =====
public class AnimationClip
{
    public string name { get; set; } = "Idle";
    public string sprite { get; set; } = "";
    public int frameCountX { get; set; } = 1;
    public int frameCountY { get; set; } = 1;
    public int startFrame { get; set; } = 0;
    public int endFrame { get; set; } = 0;
    public float fps { get; set; } = 8.0f;
    public bool loop { get; set; } = true;
}

public class AnimationSet
{
    public string assetId { get; set; } = "";
    public List<AnimationClip> clips { get; set; } = new();
}

// ===== サウンド定義 (Feature 3) =====
public class SoundDef
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string file { get; set; } = "";
    public bool isLoop { get; set; } = false;
}

// ===== イベント・トリガー定義 (Feature 5) =====
public class EventActionEntry
{
    public string action { get; set; } = "ShowMessage";
    public string param1 { get; set; } = "";
    public string param2 { get; set; } = "";
    public float delay { get; set; } = 0;
}

// ===== コモンイベント定義 (RPGツクールMZ の「コモンイベント」相当) =====
// 複数のトリガーから CallCommonEvent アクションで共通処理を呼び出せる。
public class CommonEventDef
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public List<EventActionEntry> actions { get; set; } = new();
}

public class EventTrigger
{
    public string id { get; set; } = "";
    public float x { get; set; } = 0;
    public float y { get; set; } = 0;
    public float width { get; set; } = 64;
    public float height { get; set; } = 480;
    public string condition { get; set; } = "PlayerEnter";
    public string conditionParam { get; set; } = "";
    public bool oneShot { get; set; } = true;
    public List<EventActionEntry> actions { get; set; } = new();
}

// ===== ステージ全体のデータモデル =====
public class StageData
{
    // マップサイズ（可変）
    public int MapW { get; set; } = 80;
    public int MapH { get; set; } = 15;

    // プレイヤー開始位置
    public float PlayerStartX { get; set; } = 48.0f;
    public float PlayerStartY { get; set; } = 320.0f;

    // ゴール位置
    public float GoalX { get; set; } = -1;  // -1 = 未設定
    public float GoalY { get; set; } = -1;

    // プレイヤー能力
    public PlayerCapabilities Capabilities { get; set; } = new();

    // タイルマップ（MapH行 × MapW列）
    public int[,] Map { get; set; }

    // 装飾レイヤー (Feature 1)
    public int[,] DecoLayerBack { get; set; }    // 背景側装飾（当たり判定なし）
    public int[,] DecoLayerFront { get; set; }   // 前景側装飾（当たり判定なし）

    // 背景設定 (Feature 1)
    public List<BackgroundLayer> Backgrounds { get; set; } = new();

    // サウンド設定 (Feature 3)
    public string BgmId { get; set; } = "";

    // イベント・トリガー (Feature 5)
    public List<EventTrigger> Triggers { get; set; } = new();

    // 配置オブジェクト
    public List<PlacedEnemy> Enemies { get; set; } = new();
    public List<PlacedGimmick> Gimmicks { get; set; } = new();
    public List<PlacedItem> Items { get; set; } = new();
    public List<PlatformRect> Platforms { get; set; } = new();

    public StageData()
    {
        Map = new int[MapH, MapW];
        DecoLayerBack = new int[MapH, MapW];
        DecoLayerFront = new int[MapH, MapW];
    }

    // マップをリサイズ（既存データを保持）
    public void ResizeMap(int newW, int newH)
    {
        var newMap = new int[newH, newW];
        var newDecoBack = new int[newH, newW];
        var newDecoFront = new int[newH, newW];
        for (int r = 0; r < Math.Min(MapH, newH); r++)
            for (int c = 0; c < Math.Min(MapW, newW); c++)
            {
                newMap[r, c] = Map[r, c];
                newDecoBack[r, c] = DecoLayerBack[r, c];
                newDecoFront[r, c] = DecoLayerFront[r, c];
            }
        MapW = newW;
        MapH = newH;
        Map = newMap;
        DecoLayerBack = newDecoBack;
        DecoLayerFront = newDecoFront;
    }

    // ===== JSONファイルから読み込む =====
    public static StageData LoadFromFile(string path)
    {
        var data = new StageData();
        if (!File.Exists(path)) return data;
        try
        {
            var j = JObject.Parse(File.ReadAllText(path));

            // マップサイズ
            if (j["map_w"] != null) data.MapW = j["map_w"]!.Value<int>();
            if (j["map_h"] != null) data.MapH = j["map_h"]!.Value<int>();
            data.Map = new int[data.MapH, data.MapW];
            data.DecoLayerBack = new int[data.MapH, data.MapW];
            data.DecoLayerFront = new int[data.MapH, data.MapW];

            // プレイヤー開始位置
            if (j["player_start"] != null)
            {
                data.PlayerStartX = j["player_start"]!["x"]?.Value<float>() ?? 48.0f;
                data.PlayerStartY = j["player_start"]!["y"]?.Value<float>() ?? 320.0f;
            }

            // ゴール位置
            if (j["goal"] != null)
            {
                data.GoalX = j["goal"]!["x"]?.Value<float>() ?? -1;
                data.GoalY = j["goal"]!["y"]?.Value<float>() ?? -1;
            }

            // プレイヤー能力
            if (j["player_capabilities"] != null)
                data.Capabilities = j["player_capabilities"]!.ToObject<PlayerCapabilities>() ?? new();

            // サウンド設定 (Feature 3)
            data.BgmId = j["bgm_id"]?.Value<string>() ?? "";

            // タイルマップ（メインレイヤー）
            LoadLayerFromJson(j["map"], data.Map, data.MapH, data.MapW);

            // 装飾レイヤー (Feature 1) — 既存JSONには存在しないのでエラーにしない
            LoadLayerFromJson(j["deco_back"], data.DecoLayerBack, data.MapH, data.MapW);
            LoadLayerFromJson(j["deco_front"], data.DecoLayerFront, data.MapH, data.MapW);

            // 背景設定 (Feature 1)
            if (j["backgrounds"] is JArray bgArr)
                foreach (var bg in bgArr)
                    data.Backgrounds.Add(bg.ToObject<BackgroundLayer>() ?? new());

            // イベント・トリガー (Feature 5)
            if (j["triggers"] is JArray tArr)
                foreach (var t in tArr)
                    data.Triggers.Add(t.ToObject<EventTrigger>() ?? new());

            // 敵
            if (j["enemies"] is JArray ea)
                foreach (var e in ea)
                    data.Enemies.Add(new PlacedEnemy
                    {
                        Id = e["id"]?.Value<string>() ?? "",
                        X = e["x"]?.Value<float>() ?? 0,
                        Y = e["y"]?.Value<float>() ?? 0,
                        PatrolLeft = e["patrol_left"]?.Value<float>() ?? -1,
                        PatrolRight = e["patrol_right"]?.Value<float>() ?? -1
                    });

            // ギミック
            if (j["gimmicks"] is JArray ga)
                foreach (var g in ga)
                    data.Gimmicks.Add(new PlacedGimmick
                    {
                        Id = g["id"]?.Value<string>() ?? "",
                        X = g["x"]?.Value<float>() ?? 0,
                        Y = g["y"]?.Value<float>() ?? 0,
                        Param = g["param"]?.Value<string>() ?? ""
                    });

            // アイテム
            if (j["items"] is JArray ia)
                foreach (var i in ia)
                    data.Items.Add(new PlacedItem
                    {
                        Id = i["id"]?.Value<string>() ?? "",
                        X = i["x"]?.Value<float>() ?? 0,
                        Y = i["y"]?.Value<float>() ?? 0
                    });

            // プラットフォーム
            if (j["platforms"] is JArray pa)
                foreach (var p in pa)
                    data.Platforms.Add(new PlatformRect
                    {
                        X1 = p["x1"]?.Value<float>() ?? 0, Y1 = p["y1"]?.Value<float>() ?? 0,
                        X2 = p["x2"]?.Value<float>() ?? 0, Y2 = p["y2"]?.Value<float>() ?? 0
                    });
        }
        catch { }
        return data;
    }

    private static void LoadLayerFromJson(JToken? token, int[,] layer, int h, int w)
    {
        if (token is not JArray arr) return;
        for (int row = 0; row < Math.Min(arr.Count, h); row++)
        {
            if (arr[row] is JArray rowArr)
                for (int col = 0; col < Math.Min(rowArr.Count, w); col++)
                    layer[row, col] = rowArr[col].Value<int>();
        }
    }

    // ===== JSONファイルに保存 =====
    public void SaveToFile(string path)
    {
        var j = new JObject
        {
            ["map_w"] = MapW,
            ["map_h"] = MapH,
            ["player_start"] = new JObject { ["x"] = PlayerStartX, ["y"] = PlayerStartY },
            ["player_capabilities"] = JToken.FromObject(Capabilities)
        };

        // ゴール
        if (GoalX >= 0)
            j["goal"] = new JObject { ["x"] = GoalX, ["y"] = GoalY };

        // サウンド設定 (Feature 3)
        if (!string.IsNullOrEmpty(BgmId))
            j["bgm_id"] = BgmId;

        // タイルマップ（メインレイヤー）
        j["map"] = LayerToJson(Map, MapH, MapW);

        // 装飾レイヤー (Feature 1) — 空でも保存
        j["deco_back"] = LayerToJson(DecoLayerBack, MapH, MapW);
        j["deco_front"] = LayerToJson(DecoLayerFront, MapH, MapW);

        // 背景設定 (Feature 1)
        j["backgrounds"] = new JArray(Backgrounds.Select(b => JToken.FromObject(b)));

        // イベント・トリガー (Feature 5)
        j["triggers"] = new JArray(Triggers.Select(t => JToken.FromObject(t)));

        // 敵
        var ea = new JArray();
        foreach (var e in Enemies)
        {
            var ej = new JObject { ["id"] = e.Id, ["x"] = e.X, ["y"] = e.Y };
            if (e.PatrolLeft >= 0) ej["patrol_left"] = e.PatrolLeft;
            if (e.PatrolRight >= 0) ej["patrol_right"] = e.PatrolRight;
            ea.Add(ej);
        }
        j["enemies"] = ea;

        // ギミック
        var ga = new JArray();
        foreach (var g in Gimmicks)
        {
            var gj = new JObject { ["id"] = g.Id, ["x"] = g.X, ["y"] = g.Y };
            if (!string.IsNullOrEmpty(g.Param)) gj["param"] = g.Param;
            ga.Add(gj);
        }
        j["gimmicks"] = ga;

        // アイテム
        j["items"] = new JArray(Items.Select(i => new JObject { ["id"] = i.Id, ["x"] = i.X, ["y"] = i.Y }));

        // プラットフォーム
        j["platforms"] = new JArray(Platforms.Select(p =>
            new JObject { ["x1"] = p.X1, ["y1"] = p.Y1, ["x2"] = p.X2, ["y2"] = p.Y2 }));

        string? dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, j.ToString(Formatting.Indented));
    }

    private static JArray LayerToJson(int[,] layer, int h, int w)
    {
        var arr = new JArray();
        for (int row = 0; row < h; row++)
        {
            var rowArr = new JArray();
            for (int col = 0; col < w; col++)
                rowArr.Add(layer[row, col]);
            arr.Add(rowArr);
        }
        return arr;
    }

    // ===== CSVからマップを生成 =====
    public static StageData LoadFromCsv(string csvPath, int specialStart = 8, int specialGoal = 9)
    {
        var data = new StageData();
        var lines = File.ReadAllLines(csvPath)
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        if (lines.Length == 0) return data;

        var rows = lines.Select(l => l.Split(',').Select(c => int.TryParse(c.Trim(), out var v) ? v : 0).ToArray()).ToArray();
        int h = rows.Length;
        int w = rows.Max(r => r.Length);

        data.MapH = h;
        data.MapW = w;
        data.Map = new int[h, w];
        data.DecoLayerBack = new int[h, w];
        data.DecoLayerFront = new int[h, w];

        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < Math.Min(rows[r].Length, w); c++)
            {
                int val = rows[r][c];
                if (val == specialStart)
                {
                    data.PlayerStartX = c * 32.0f;
                    data.PlayerStartY = r * 32.0f;
                    data.Map[r, c] = 0;
                }
                else if (val == specialGoal)
                {
                    data.GoalX = c * 32.0f;
                    data.GoalY = r * 32.0f;
                    data.Map[r, c] = 0;
                }
                else
                {
                    data.Map[r, c] = val;
                }
            }
        }
        return data;
    }

    // ===== テストプレイ用一時JSONを生成して保存 (Feature 4) =====
    public void SaveAsTestPlay(string path, float testX, float testY)
    {
        // 通常と同じ内容でPlayerStartを上書き＋test_modeフラグを付加
        var orig_sx = PlayerStartX;
        var orig_sy = PlayerStartY;
        PlayerStartX = testX;
        PlayerStartY = testY;
        SaveToFile(path);
        PlayerStartX = orig_sx;
        PlayerStartY = orig_sy;

        // test_mode フラグを追加
        var j = JObject.Parse(File.ReadAllText(path));
        j["test_mode"] = true;
        File.WriteAllText(path, j.ToString(Formatting.Indented));
    }
}

// ===== プレイヤー能力 =====
public class PlayerCapabilities
{
    [System.ComponentModel.DisplayName("2段ジャンプ")]
    [System.ComponentModel.Category("アクション")]
    public bool canDoubleJump { get; set; } = false;

    [System.ComponentModel.DisplayName("ダッシュ")]
    [System.ComponentModel.Category("アクション")]
    public bool canDash { get; set; } = false;

    [System.ComponentModel.DisplayName("火の玉を撃つ")]
    [System.ComponentModel.Category("アクション")]
    public bool canShootFireball { get; set; } = false;

    [System.ComponentModel.DisplayName("飛行")]
    [System.ComponentModel.Category("アクション")]
    public bool canFly { get; set; } = false;

    [System.ComponentModel.DisplayName("基本ジャンプ力")]
    [System.ComponentModel.Category("パラメータ")]
    public int baseJumpPower { get; set; } = -12;

    [System.ComponentModel.DisplayName("基本移動速度")]
    [System.ComponentModel.Category("パラメータ")]
    public float baseSpeed { get; set; } = 4.0f;
}

// ===== 配置オブジェクト =====
public class PlacedEnemy
{
    [System.ComponentModel.DisplayName("ID")]
    public string Id { get; set; } = "";
    [System.ComponentModel.DisplayName("X座標")]
    public float X { get; set; }
    [System.ComponentModel.DisplayName("Y座標")]
    public float Y { get; set; }
    [System.ComponentModel.DisplayName("巡回左端")]
    public float PatrolLeft { get; set; } = -1;
    [System.ComponentModel.DisplayName("巡回右端")]
    public float PatrolRight { get; set; } = -1;
}

public class PlacedGimmick
{
    [System.ComponentModel.DisplayName("ID")]
    public string Id { get; set; } = "";
    [System.ComponentModel.DisplayName("X座標")]
    public float X { get; set; }
    [System.ComponentModel.DisplayName("Y座標")]
    public float Y { get; set; }
    [System.ComponentModel.DisplayName("パラメータ")]
    public string Param { get; set; } = "";
}

public class PlacedItem
{
    [System.ComponentModel.DisplayName("ID")]
    public string Id { get; set; } = "";
    [System.ComponentModel.DisplayName("X座標")]
    public float X { get; set; }
    [System.ComponentModel.DisplayName("Y座標")]
    public float Y { get; set; }
}

public class PlatformRect
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
}

// ===== アセット定義 =====
public class EnemyDef
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int type_enum { get; set; }
    public int hp { get; set; } = 3;
    public int width { get; set; } = 32;
    public int height { get; set; } = 32;
    public string sprite { get; set; } = "";
    // サウンド SE (Feature 3)
    public string seSpawn { get; set; } = "";
    public string seAttack { get; set; } = "";
    public string seDamage { get; set; } = "";
    public string seDeath { get; set; } = "";
    // Hitbox (Feature: Visual Hitbox Editor)
    public int hitboxOffsetX { get; set; } = 0;
    public int hitboxOffsetY { get; set; } = 0;
    public int hitboxWidth { get; set; } = 32;
    public int hitboxHeight { get; set; } = 32;
    // 表示スケール（Feature: Visual Size Editor）ゲーム内での表示サイズ倍率
    public float scale { get; set; } = 1.0f;

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // -1 (未設定) の場合、ゲーム側でそのtype_enumの従来の挙動と同じ値が自動的に補完される。
    public float moveSpeed { get; set; } = -1.0f;
    public float enragedMoveSpeed { get; set; } = -1.0f;
    public float actionInterval { get; set; } = -1.0f;
    public float jumpPowerMult { get; set; } = -1.0f;
    public float triggerRange { get; set; } = -1.0f;
    public float detectionRangeY { get; set; } = -1.0f;
    public float projectileSpeed { get; set; } = -1.0f;
    public float chargeTime { get; set; } = -1.0f;
    public float dashSpeedMult { get; set; } = -1.0f;
    public float dashDuration { get; set; } = -1.0f;
    public float cooldownTime { get; set; } = -1.0f;
    public float fallDelay { get; set; } = -1.0f;
    public float spreadAngle { get; set; } = -1.0f;
    public int spreadCount { get; set; } = -1;
    public float floatAmplitude { get; set; } = -1.0f;
    public float floatFrequency { get; set; } = -1.0f;
    public float teleportRangeMin { get; set; } = -1.0f;
    public float teleportRangeMax { get; set; } = -1.0f;
    public float shrinkFactor { get; set; } = -1.0f;
    public float shieldOnDuration { get; set; } = -1.0f;
    public float shieldOffDuration { get; set; } = -1.0f;
    public float mimicDelayFrames { get; set; } = -1.0f;
    public float sizeAmplitude { get; set; } = -1.0f;
    public float sizeFrequency { get; set; } = -1.0f;
    public float minScale { get; set; } = -1.0f;
    public float tempoFrequency { get; set; } = -1.0f;
    public float tempoMin { get; set; } = -1.0f;
    public float tempoMax { get; set; } = -1.0f;
    public float effectRange { get; set; } = -1.0f;
    public float brightnessMin { get; set; } = -1.0f;
    public float tintStrength { get; set; } = -1.0f;
    public float zoomAmplitude { get; set; } = -1.0f;
    public float zoomFrequency { get; set; } = -1.0f;
}

public class GimmickDef
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int type_enum { get; set; }
    public string sprite { get; set; } = "";
    // サウンド SE (Feature 3)
    public string seActivate { get; set; } = "";
    // Hitbox (Feature: Visual Hitbox Editor)
    public int hitboxOffsetX { get; set; } = 0;
    public int hitboxOffsetY { get; set; } = 0;
    public int hitboxWidth { get; set; } = 32;
    public int hitboxHeight { get; set; } = 32;

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // -1 (未設定) の場合、ゲーム側でそのtype_enumの従来の挙動と同じ値が自動的に補完される。
    public float rotationSpeed { get; set; } = -1.0f;
    public float sinkSpeed { get; set; } = -1.0f;
    public float maxDepthOffset { get; set; } = -1.0f;
    public float pushOutDistance { get; set; } = -1.0f;
    public float triggerWidthThreshold { get; set; } = -1.0f;
    public float travelDistance { get; set; } = -1.0f;
    public float oscillationSpeed { get; set; } = -1.0f;
    public float stepIncrement { get; set; } = -1.0f;
    public float standDelayFrames { get; set; } = -1.0f;
    public float standTolerancePx { get; set; } = -1.0f;
    public float respawnDelayFrames { get; set; } = -1.0f;
    public float radius { get; set; } = -1.0f;
    public float brightLevel { get; set; } = -1.0f;
    public float darkLevel { get; set; } = -1.0f;
    public float tintR { get; set; } = -1.0f;
    public float tintG { get; set; } = -1.0f;
    public float tintB { get; set; } = -1.0f;
    public float zoomLevel { get; set; } = -1.0f;
    public float warpOffsetPx { get; set; } = -1.0f;
}

public class ItemDef
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public int type_enum { get; set; }
    public string sprite { get; set; } = "";
    public string grant_ability { get; set; } = "";
    // サウンド SE (Feature 3)
    public string seCollect { get; set; } = "";
    // Hitbox (Feature: Visual Hitbox Editor)
    public int hitboxOffsetX { get; set; } = 0;
    public int hitboxOffsetY { get; set; } = 0;
    public int hitboxWidth { get; set; } = 32;
    public int hitboxHeight { get; set; } = 32;
}

public class AssetDefinitions
{
    public List<EnemyDef> Enemies { get; set; } = new();
    public List<GimmickDef> Gimmicks { get; set; } = new();
    public List<ItemDef> Items { get; set; } = new();
    public List<TileDef> Tiles { get; set; } = new();
    // サウンド定義 (Feature 3)
    public List<SoundDef> Bgm { get; set; } = new();
    public List<SoundDef> Se { get; set; } = new();
    // UI音（ポーズ・早送り切替など）。再生時はSEと同じ名前空間で PlaySe(id) される。
    public List<SoundDef> UiSe { get; set; } = new();
    // アニメーション定義 (Feature 2)
    public List<AnimationSet> Animations { get; set; } = new();

    // コモンイベント定義 (RPGツクールMZ風)
    public List<CommonEventDef> CommonEvents { get; set; } = new();

    public static AssetDefinitions LoadFromFolder(string assetsPath)
    {
        var defs = new AssetDefinitions();
        defs.Enemies = LoadJson<EnemyDef>(Path.Combine(assetsPath, "enemies.json")) ?? defs.Enemies;
        defs.Gimmicks = LoadJson<GimmickDef>(Path.Combine(assetsPath, "gimmicks.json")) ?? defs.Gimmicks;
        defs.Items = LoadJson<ItemDef>(Path.Combine(assetsPath, "items.json")) ?? defs.Items;
        defs.Tiles = LoadJson<TileDef>(Path.Combine(assetsPath, "tiles.json")) ?? defs.Tiles;
        defs.Bgm = LoadJson<SoundDef>(Path.Combine(assetsPath, "bgm.json")) ?? defs.Bgm;
        defs.Se = LoadJson<SoundDef>(Path.Combine(assetsPath, "se.json")) ?? defs.Se;
        defs.UiSe = LoadJson<SoundDef>(Path.Combine(assetsPath, "ui_se.json")) ?? defs.UiSe;
        defs.Animations = LoadJson<AnimationSet>(Path.Combine(assetsPath, "animations.json")) ?? defs.Animations;
        defs.CommonEvents = LoadJson<CommonEventDef>(Path.Combine(assetsPath, "common_events.json")) ?? defs.CommonEvents;
        return defs;
    }

    private static List<T>? LoadJson<T>(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(path)); } catch { return null; }
    }

    public void SaveToFolder(string assetsPath)
    {
        File.WriteAllText(Path.Combine(assetsPath, "enemies.json"), JsonConvert.SerializeObject(Enemies, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "gimmicks.json"), JsonConvert.SerializeObject(Gimmicks, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "items.json"), JsonConvert.SerializeObject(Items, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "tiles.json"), JsonConvert.SerializeObject(Tiles, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "bgm.json"), JsonConvert.SerializeObject(Bgm, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "se.json"), JsonConvert.SerializeObject(Se, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "ui_se.json"), JsonConvert.SerializeObject(UiSe, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "animations.json"), JsonConvert.SerializeObject(Animations, Formatting.Indented));
        File.WriteAllText(Path.Combine(assetsPath, "common_events.json"), JsonConvert.SerializeObject(CommonEvents, Formatting.Indented));
    }
}
