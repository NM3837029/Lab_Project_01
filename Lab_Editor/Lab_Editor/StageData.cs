using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ===== タイル定義 =====
// マップの1マスに敷き詰める「地形タイル」1種類分の設定を表すクラス。
// tiles.json に保存されているタイル一覧の各要素がこのクラスに対応する。
public class TileDef
{
    // このタイルを一意に識別する番号。マップデータ(Map配列)にはこのidの数値が格納される。
    public int id { get; set; }
    // エディタ上に表示するためのタイルの名前（人間が読むためのラベル）。
    public string name { get; set; } = "";
    // スプライト画像を用意していない場合に代わりに塗る色（16進カラーコード）。
    public string color { get; set; } = "#CCCCCC";
    // trueの場合、プレイヤーやオブジェクトがこのタイルに乗ったり衝突したりする（地面・壁など）。
    // falseの場合はすり抜けられる（背景装飾など）。
    public bool collidable { get; set; } = true;
    // trueの場合、このタイルに触れるとプレイヤーがダメージを受けたりミスになったりする（トゲ・溶岩など）。
    public bool deadly { get; set; } = false;
    // このタイルの見た目に使うスプライト画像のファイル名（相対パス）。
    public string sprite { get; set; } = "";
    // Feature: タイルセット切り出し（表示範囲）機能 — spriteが単一の絵ではなく複数タイルをまとめた
    // タイルセット画像である場合に、そのうちどの矩形部分を表示に使うかを指定する。
    // 全て0（既定値）の場合は画像全体を使う（従来互換）。
    // srcX, srcY : タイルセット画像内での切り出し開始位置（左上座標、ピクセル単位）
    public int srcX { get; set; } = 0;
    public int srcY { get; set; } = 0;
    // srcW, srcH : 切り出す矩形の幅・高さ（ピクセル単位）
    public int srcW { get; set; } = 0;
    public int srcH { get; set; } = 0;
}

// ===== 背景レイヤー定義 (Feature 1) =====
// ステージの背景に重ねて表示する1枚の背景画像レイヤーの設定。
// 複数登録することで多重スクロール（視差効果のある背景）を実現できる。
public class BackgroundLayer
{
    // 背景画像のファイル名（相対パス）。
    public string sprite { get; set; } = "";
    // 描画順。数値が小さいほど奥（先）に描かれ、大きいほど手前に描かれる。
    public int drawOrder { get; set; } = 0;
    // スクロール速度の倍率。1.0でプレイヤー（前景）と同じ速さで動き、0に近いほどゆっくり動いて
    // 遠くにあるように見える（視差スクロール効果）。
    public float scrollRate { get; set; } = 0.3f;
    // trueの場合、画像の端まで来たら繰り返し表示してループさせる（横に長いステージ向け）。
    public bool loop { get; set; } = true;
    // 画像の初期表示位置をずらすためのオフセット（X方向）。
    public float offsetX { get; set; } = 0;
    // 画像の初期表示位置をずらすためのオフセット（Y方向）。
    public float offsetY { get; set; } = 0;
}

// ===== アニメーション定義 (Feature 2) =====
// スプライトシート（1枚の画像に複数コマが並んだもの）を使ったパラパラ漫画的アニメーションの
// 1つの動作（例:「歩く」「攻撃する」など）分の再生設定。
public class AnimationClip
{
    // このアニメーションクリップの名前（例: "Idle"=待機, "Walk"=歩行 など。コード側から名前で参照する）。
    public string name { get; set; } = "Idle";
    // このアニメーションが使うスプライトシート画像のファイル名。
    public string sprite { get; set; } = "";
    // スプライトシートの横方向のコマ数。
    public int frameCountX { get; set; } = 1;
    // スプライトシートの縦方向のコマ数。
    public int frameCountY { get; set; } = 1;
    // 再生を開始するコマ番号（0始まり、左上から横に数えた通し番号）。
    public int startFrame { get; set; } = 0;
    // 再生を終了するコマ番号（このコマまで再生したら最初に戻る、またはループを止める）。
    public int endFrame { get; set; } = 0;
    // 再生速度（1秒あたりに切り替えるコマ数）。
    public float fps { get; set; } = 8.0f;
    // trueの場合、endFrameまで再生したらstartFrameへ戻って繰り返す。falseなら最終コマで止まる。
    public bool loop { get; set; } = true;
}

// 1体のキャラクター（敵など）が持つアニメーションクリップ一式をまとめたもの。
public class AnimationSet
{
    // このアニメーションセットが対応する敵・ギミック・アイテムのID（EnemyDef.id等と対応させる）。
    public string assetId { get; set; } = "";
    // このアセットが持つアニメーションクリップの一覧（"Idle"、"Walk"など複数登録できる）。
    public List<AnimationClip> clips { get; set; } = new();
}

// ===== サウンド定義 (Feature 3) =====
// BGMや効果音（SE）1つ分の再生設定。bgm.json / se.json / ui_se.json に保存される。
public class SoundDef
{
    // このサウンドを一意に識別するID。コード側やイベントからはこのidで再生を指定する。
    public string id { get; set; } = "";
    // エディタ上に表示するためのサウンドの名前（人間が読むためのラベル）。
    public string name { get; set; } = "";
    // 実際の音声ファイル名（相対パス）。
    public string file { get; set; } = "";
    // trueの場合、曲の終わりまで再生したら最初に戻って繰り返し再生する（BGM向け）。
    public bool isLoop { get; set; } = false;
    // Feature: サウンド・アセット管理の刷新 — 0.0〜1.0の音量。C++側でDxLibの0〜255スケールへ変換して適用する。
    public float volume { get; set; } = 1.0f;
}

// ===== イベント・トリガー定義 (Feature 5) =====
// トリガー発火時、またはコモンイベント呼び出し時に実行される「1つの命令」を表す。
// 複数並べることで一連の演出・処理（メッセージ表示→ウェイト→次の命令…）を組める。
public class EventActionEntry
{
    // 実行するアクションの種類を表す文字列（例: "ShowMessage"=メッセージ表示など）。
    public string action { get; set; } = "ShowMessage";
    // アクションに渡す1つ目のパラメータ（意味はactionの種類によって異なる）。
    public string param1 { get; set; } = "";
    // アクションに渡す2つ目のパラメータ（意味はactionの種類によって異なる）。
    public string param2 { get; set; } = "";
    // このアクションを実行する前に待つ時間（秒）。演出のタイミング調整に使う。
    public float delay { get; set; } = 0;
}

// ===== コモンイベント定義 (RPGツクールMZ の「コモンイベント」相当) =====
// 複数のトリガーから CallCommonEvent アクションで共通処理を呼び出せる。
// 同じ処理（例: 「暗転してメッセージを出す」等）を複数箇所に個別に書かず、ここに1つ登録して
// 使い回すための仕組み。
public class CommonEventDef
{
    // このコモンイベントを一意に識別するID。CallCommonEventアクションからこのidで呼び出す。
    public string id { get; set; } = "";
    // エディタ上に表示するためのコモンイベントの名前（人間が読むためのラベル）。
    public string name { get; set; } = "";
    // このコモンイベントが呼ばれた時に順番に実行されるアクションの一覧。
    public List<EventActionEntry> actions { get; set; } = new();
}

// マップ上の特定の矩形範囲にプレイヤーが入る等の条件を満たした時に、一連のアクションを
// 発火させる「トリガー」の定義。
public class EventTrigger
{
    // このトリガーを一意に識別するID。
    public string id { get; set; } = "";
    // トリガー範囲の左上X座標。
    public float x { get; set; } = 0;
    // トリガー範囲の左上Y座標。
    public float y { get; set; } = 0;
    // トリガー範囲の幅。
    public float width { get; set; } = 64;
    // トリガー範囲の高さ。
    public float height { get; set; } = 480;
    // 発火条件の種類を表す文字列（例: "PlayerEnter"=プレイヤーがこの範囲に入った時）。
    public string condition { get; set; } = "PlayerEnter";
    // 発火条件に付随するパラメータ（条件の種類によって意味が変わる）。
    public string conditionParam { get; set; } = "";
    // trueの場合、一度発火したら以降は二度と発火しない（1回限りのイベント用）。
    public bool oneShot { get; set; } = true;
    // 発火時に順番に実行されるアクションの一覧。
    public List<EventActionEntry> actions { get; set; } = new();
}

// ===== ステージ全体のデータモデル =====
// 1つのステージ（面）を構成する全データをまとめたクラス。エディタで編集し、JSONファイルとして
// 保存する。C++側のゲーム本体はこのJSONを読み込んでステージを再現する。
public class StageData
{
    // マップサイズ（可変）。マス目（タイル）単位の横幅・縦幅。
    public int MapW { get; set; } = 80;
    public int MapH { get; set; } = 15;

    // プレイヤー開始位置（ピクセル座標）。ステージ開始時にプレイヤーがここに配置される。
    public float PlayerStartX { get; set; } = 48.0f;
    public float PlayerStartY { get; set; } = 320.0f;

    // ゴール位置（ピクセル座標）。ここにプレイヤーが到達するとステージクリアになる。
    public float GoalX { get; set; } = -1;  // -1 = 未設定
    public float GoalY { get; set; } = -1;

    // プレイヤー能力。このステージでプレイヤーがどんなアクション（2段ジャンプ等）を使えるか。
    public PlayerCapabilities Capabilities { get; set; } = new();

    // 編集ツール許可設定・編集コスト経済設定（ステージ単位）
    // このステージでプレイヤーがどの編集ツール（巻き戻し等）を使えるか、使った時のコスト消費量。
    public EditToolFlags EditTools { get; set; } = new();
    public EditCostSettings EditCost { get; set; } = new();

    // タイルマップ（MapH行 × MapW列）。各マスにTileDef.idの数値が入る、地形の本体データ。
    public int[,] Map { get; set; }

    // 装飾レイヤー (Feature 1)
    public int[,] DecoLayerBack { get; set; }    // 背景側装飾（当たり判定なし）
    public int[,] DecoLayerFront { get; set; }   // 前景側装飾（当たり判定なし）

    // 背景設定 (Feature 1) — このステージで使う多重スクロール背景レイヤーの一覧。
    public List<BackgroundLayer> Backgrounds { get; set; } = new();

    // サウンド設定 (Feature 3) — このステージで流すBGMのID（SoundDef.idを参照する）。
    public string BgmId { get; set; } = "";

    // イベント・トリガー (Feature 5) — このステージに配置されたイベントトリガーの一覧。
    public List<EventTrigger> Triggers { get; set; } = new();

    // 配置オブジェクト — このステージ上に配置された敵・ギミック・アイテム・当たり判定用矩形の一覧。
    public List<PlacedEnemy> Enemies { get; set; } = new();
    public List<PlacedGimmick> Gimmicks { get; set; } = new();
    public List<PlacedItem> Items { get; set; } = new();
    public List<PlatformRect> Platforms { get; set; } = new();

    // StageDataを新規作成した際の初期化処理。MapW/MapHの初期値に合わせて
    // タイルマップ・装飾レイヤーの二次元配列を確保しておく。
    public StageData()
    {
        Map = new int[MapH, MapW];
        DecoLayerBack = new int[MapH, MapW];
        DecoLayerFront = new int[MapH, MapW];
    }

    // マップをリサイズ（既存データを保持）
    // newW, newH : 変更後のマップ幅・高さ（マス単位）
    // 新しいサイズの配列を用意し、元のマップと重なる範囲だけ内容をコピーする。
    // はみ出した部分（新しく増えた範囲）は既定値の0（空白）のままになる。
    public void ResizeMap(int newW, int newH)
    {
        var newMap = new int[newH, newW];
        var newDecoBack = new int[newH, newW];
        var newDecoFront = new int[newH, newW];
        // 「変更前」と「変更後」のうち小さい方の範囲までだけをコピーし、
        // 配列の範囲外アクセス（例外）を起こさないようにする。
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
    // path : 読み込むステージJSONファイルのパス
    // ファイルの内容を解析し、StageDataインスタンスへ各項目を反映して返す。
    // ファイルが存在しない場合や読み込み中に何らかのエラーが起きた場合は、
    // 例外を投げずに（可能な範囲まで反映された）デフォルト値のStageDataを返す。
    public static StageData LoadFromFile(string path)
    {
        var data = new StageData();
        if (!File.Exists(path)) return data;
        try
        {
            var j = JObject.Parse(File.ReadAllText(path));

            // マップサイズ（JSONに項目が無い場合はデフォルト値のまま据え置く）
            if (j["map_w"] != null) data.MapW = j["map_w"]!.Value<int>();
            if (j["map_h"] != null) data.MapH = j["map_h"]!.Value<int>();
            // 読み込んだサイズに合わせて配列を作り直す（コンストラクタで確保した分は破棄される）
            data.Map = new int[data.MapH, data.MapW];
            data.DecoLayerBack = new int[data.MapH, data.MapW];
            data.DecoLayerFront = new int[data.MapH, data.MapW];

            // プレイヤー開始位置。JSON側にx/yが無い場合はデフォルト値(48, 320)を使う。
            if (j["player_start"] != null)
            {
                data.PlayerStartX = j["player_start"]!["x"]?.Value<float>() ?? 48.0f;
                data.PlayerStartY = j["player_start"]!["y"]?.Value<float>() ?? 320.0f;
            }

            // ゴール位置。項目自体が無ければ「未設定」を意味する-1のままにする。
            if (j["goal"] != null)
            {
                data.GoalX = j["goal"]!["x"]?.Value<float>() ?? -1;
                data.GoalY = j["goal"]!["y"]?.Value<float>() ?? -1;
            }

            // プレイヤー能力。JSONの構造をそのままPlayerCapabilitiesクラスへ変換する。
            if (j["player_capabilities"] != null)
                data.Capabilities = j["player_capabilities"]!.ToObject<PlayerCapabilities>() ?? new();

            // 編集ツール許可設定・編集コスト経済設定。同様にJSONの構造をそのままクラスへ変換する。
            if (j["allowed_edit_tools"] != null)
                data.EditTools = j["allowed_edit_tools"]!.ToObject<EditToolFlags>() ?? new();
            if (j["edit_cost_settings"] != null)
                data.EditCost = j["edit_cost_settings"]!.ToObject<EditCostSettings>() ?? new();

            // サウンド設定 (Feature 3) — bgm_id項目が無ければ空文字列（BGMなし）として扱う。
            data.BgmId = j["bgm_id"]?.Value<string>() ?? "";

            // タイルマップ（メインレイヤー）を2次元配列へ読み込む。
            LoadLayerFromJson(j["map"], data.Map, data.MapH, data.MapW);

            // 装飾レイヤー (Feature 1) — 既存JSONには存在しないのでエラーにしない
            // （項目が無ければLoadLayerFromJson内で何もせず返るため、古いファイルを開いても壊れない）
            LoadLayerFromJson(j["deco_back"], data.DecoLayerBack, data.MapH, data.MapW);
            LoadLayerFromJson(j["deco_front"], data.DecoLayerFront, data.MapH, data.MapW);

            // 背景設定 (Feature 1) — 配列の各要素をBackgroundLayerへ変換して追加していく。
            if (j["backgrounds"] is JArray bgArr)
                foreach (var bg in bgArr)
                    data.Backgrounds.Add(bg.ToObject<BackgroundLayer>() ?? new());

            // イベント・トリガー (Feature 5) — 同様に配列の各要素をEventTriggerへ変換する。
            if (j["triggers"] is JArray tArr)
                foreach (var t in tArr)
                    data.Triggers.Add(t.ToObject<EventTrigger>() ?? new());

            // 敵 — JSON配列の各要素から必要な項目だけを取り出してPlacedEnemyを組み立てる。
            // 巡回範囲(patrol_left/right)が指定されていない場合は「巡回しない」を意味する-1にする。
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

            // ギミック — 配置座標とオプションパラメータ(param)を読み込む。
            if (j["gimmicks"] is JArray ga)
                foreach (var g in ga)
                    data.Gimmicks.Add(new PlacedGimmick
                    {
                        Id = g["id"]?.Value<string>() ?? "",
                        X = g["x"]?.Value<float>() ?? 0,
                        Y = g["y"]?.Value<float>() ?? 0,
                        Param = g["param"]?.Value<string>() ?? ""
                    });

            // アイテム — 配置座標を読み込む。
            if (j["items"] is JArray ia)
                foreach (var i in ia)
                    data.Items.Add(new PlacedItem
                    {
                        Id = i["id"]?.Value<string>() ?? "",
                        X = i["x"]?.Value<float>() ?? 0,
                        Y = i["y"]?.Value<float>() ?? 0
                    });

            // プラットフォーム — 当たり判定用の矩形（始点・終点座標）の一覧を読み込む。
            if (j["platforms"] is JArray pa)
                foreach (var p in pa)
                    data.Platforms.Add(new PlatformRect
                    {
                        X1 = p["x1"]?.Value<float>() ?? 0, Y1 = p["y1"]?.Value<float>() ?? 0,
                        X2 = p["x2"]?.Value<float>() ?? 0, Y2 = p["y2"]?.Value<float>() ?? 0
                    });
        }
        // JSONの形式が壊れている等、読み込み中に何が起きても処理を止めずに、
        // ここまでに反映できた分のStageDataをそのまま返す（アプリ全体を落とさないための安全策）。
        catch { }
        return data;
    }

    // JSONのtoken（2次元配列想定）をlayer（int[,]）へコピーするための共通処理。
    // token : JSON側の配列データ（nullや配列以外の場合は何もしない）
    // layer : コピー先の2次元配列
    // h, w  : コピー先配列の行数・列数（これを超える分は無視する）
    private static void LoadLayerFromJson(JToken? token, int[,] layer, int h, int w)
    {
        if (token is not JArray arr) return;
        // JSON側の行数とlayerの行数のうち小さい方までしか読まない（範囲外アクセス防止）
        for (int row = 0; row < Math.Min(arr.Count, h); row++)
        {
            if (arr[row] is JArray rowArr)
                // 同様に列数も小さい方までに制限する
                for (int col = 0; col < Math.Min(rowArr.Count, w); col++)
                    layer[row, col] = rowArr[col].Value<int>();
        }
    }

    // ===== JSONファイルに保存 =====
    // path : 保存先ファイルパス
    // StageDataの全項目をJObjectへ組み立て、整形済みJSONとしてファイルに書き出す。
    public void SaveToFile(string path)
    {
        var j = new JObject
        {
            ["map_w"] = MapW,
            ["map_h"] = MapH,
            ["player_start"] = new JObject { ["x"] = PlayerStartX, ["y"] = PlayerStartY },
            ["player_capabilities"] = JToken.FromObject(Capabilities),
            ["allowed_edit_tools"] = JToken.FromObject(EditTools),
            ["edit_cost_settings"] = JToken.FromObject(EditCost)
        };

        // ゴール — 未設定(-1)の場合はJSONにgoal項目自体を出力しない。
        if (GoalX >= 0)
            j["goal"] = new JObject { ["x"] = GoalX, ["y"] = GoalY };

        // サウンド設定 (Feature 3) — BGM未設定（空文字列）の場合はbgm_id項目を出力しない。
        if (!string.IsNullOrEmpty(BgmId))
            j["bgm_id"] = BgmId;

        // タイルマップ（メインレイヤー）を2次元配列からJSON配列へ変換して格納する。
        j["map"] = LayerToJson(Map, MapH, MapW);

        // 装飾レイヤー (Feature 1) — 空でも保存
        // （全マス0=何も置いていない状態でも、項目自体は必ず出力してファイル形式を統一する）
        j["deco_back"] = LayerToJson(DecoLayerBack, MapH, MapW);
        j["deco_front"] = LayerToJson(DecoLayerFront, MapH, MapW);

        // 背景設定 (Feature 1) — 各BackgroundLayerをJSONオブジェクトへ変換して配列にする。
        j["backgrounds"] = new JArray(Backgrounds.Select(b => JToken.FromObject(b)));

        // イベント・トリガー (Feature 5) — 各EventTriggerをJSONオブジェクトへ変換して配列にする。
        j["triggers"] = new JArray(Triggers.Select(t => JToken.FromObject(t)));

        // 敵 — 巡回範囲(patrol_left/right)は「設定されている場合(0以上)」のみ出力し、
        // 未設定(-1)の場合は項目自体を省略してJSONを簡潔に保つ。
        var ea = new JArray();
        foreach (var e in Enemies)
        {
            var ej = new JObject { ["id"] = e.Id, ["x"] = e.X, ["y"] = e.Y };
            if (e.PatrolLeft >= 0) ej["patrol_left"] = e.PatrolLeft;
            if (e.PatrolRight >= 0) ej["patrol_right"] = e.PatrolRight;
            ea.Add(ej);
        }
        j["enemies"] = ea;

        // ギミック — パラメータ(Param)が空でない場合のみparam項目を出力する。
        var ga = new JArray();
        foreach (var g in Gimmicks)
        {
            var gj = new JObject { ["id"] = g.Id, ["x"] = g.X, ["y"] = g.Y };
            if (!string.IsNullOrEmpty(g.Param)) gj["param"] = g.Param;
            ga.Add(gj);
        }
        j["gimmicks"] = ga;

        // アイテム — id・座標のみのシンプルな構造なのでSelectで一括変換する。
        j["items"] = new JArray(Items.Select(i => new JObject { ["id"] = i.Id, ["x"] = i.X, ["y"] = i.Y }));

        // プラットフォーム — 始点・終点座標をそのままJSONオブジェクトへ変換する。
        j["platforms"] = new JArray(Platforms.Select(p =>
            new JObject { ["x1"] = p.X1, ["y1"] = p.Y1, ["x2"] = p.X2, ["y2"] = p.Y2 }));

        // 保存先フォルダがまだ存在しない場合に備えて、書き込み前に必ず作成しておく。
        string? dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        // 人間が読みやすいようインデント付きの整形済みJSONとして書き出す。
        File.WriteAllText(path, j.ToString(Formatting.Indented));
    }

    // int[,]の2次元配列を、行ごとのJArrayを並べた入れ子のJArray（JSON上の2次元配列）へ変換する。
    // layer : 変換元の2次元配列
    // h, w  : 変換する行数・列数
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
    // csvPath     : 読み込むCSVファイルのパス（各セルがタイルidの数値であるカンマ区切りテキスト）
    // specialStart: プレイヤー開始位置を表す特別な数値（この数値のマスは実際のタイルにはせず、開始座標として扱う）
    // specialGoal : ゴール位置を表す特別な数値（同様にタイルにはせず、ゴール座標として扱う）
    // 主に他のツール（Tiledなど）からエクスポートしたCSV形式のマップデータを取り込むための機能。
    public static StageData LoadFromCsv(string csvPath, int specialStart = 8, int specialGoal = 9)
    {
        var data = new StageData();
        // 空行を除いた行だけを読み込む対象にする。
        var lines = File.ReadAllLines(csvPath)
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        if (lines.Length == 0) return data;

        // 各行をカンマで分割し、数値に変換できないセルは0（空白タイル）として扱う。
        var rows = lines.Select(l => l.Split(',').Select(c => int.TryParse(c.Trim(), out var v) ? v : 0).ToArray()).ToArray();
        int h = rows.Length;
        // 行によって列数がバラバラな場合に備え、最も長い行に合わせて幅を決める。
        int w = rows.Max(r => r.Length);

        data.MapH = h;
        data.MapW = w;
        data.Map = new int[h, w];
        data.DecoLayerBack = new int[h, w];
        data.DecoLayerFront = new int[h, w];

        for (int r = 0; r < h; r++)
        {
            // 行の長さがwより短い場合（列数が足りない行）に備え、その行の実際の長さまでしか読まない。
            for (int c = 0; c < Math.Min(rows[r].Length, w); c++)
            {
                int val = rows[r][c];
                if (val == specialStart)
                {
                    // 特別な数値＝プレイヤー開始位置のマス。タイル自体は置かず(0)、
                    // マス座標(c,r)からピクセル座標(1マス=32px換算)を求めて開始位置に設定する。
                    data.PlayerStartX = c * 32.0f;
                    data.PlayerStartY = r * 32.0f;
                    data.Map[r, c] = 0;
                }
                else if (val == specialGoal)
                {
                    // 特別な数値＝ゴール位置のマス。同様にタイルは置かず、ゴール座標として記録する。
                    data.GoalX = c * 32.0f;
                    data.GoalY = r * 32.0f;
                    data.Map[r, c] = 0;
                }
                else
                {
                    // 通常のタイル番号はそのままマップへ反映する。
                    data.Map[r, c] = val;
                }
            }
        }
        return data;
    }

    // ===== テストプレイ用一時JSONを生成して保存 (Feature 4) =====
    // path        : 一時的に書き出すJSONファイルのパス
    // testX, testY: テストプレイを開始したい座標（エディタ上でクリックした位置など）
    // エディタから「今すぐこの位置からテストプレイしたい」時に使う。本来のプレイヤー開始位置は
    // 変更せず、保存後に元へ戻すことでステージデータ自体には影響を与えない。
    public void SaveAsTestPlay(string path, float testX, float testY)
    {
        // 通常と同じ内容でPlayerStartを上書き＋test_modeフラグを付加
        // 一時的に開始位置をテスト位置へ差し替えてから保存し、保存後は元の値に戻す
        // （StageDataそのもの＝エディタ上の本編集内容は変更しないための一時退避）。
        var orig_sx = PlayerStartX;
        var orig_sy = PlayerStartY;
        PlayerStartX = testX;
        PlayerStartY = testY;
        SaveToFile(path);
        PlayerStartX = orig_sx;
        PlayerStartY = orig_sy;

        // test_mode フラグを追加
        // 保存し終えたJSONを読み直し、「これはテストプレイ用の一時ファイルである」ことを示す
        // test_modeフラグを追記して上書き保存する（ゲーム側がこのフラグを見て挙動を変える）。
        var j = JObject.Parse(File.ReadAllText(path));
        j["test_mode"] = true;
        File.WriteAllText(path, j.ToString(Formatting.Indented));
    }
}

// ===== プレイヤー能力 =====
// このステージでプレイヤーが使用できるアクション・基本パラメータの設定。
// エディタのプロパティグリッドに表示され、DisplayName/Categoryで日本語ラベル・分類が付く。
public class PlayerCapabilities
{
    // trueの場合、空中でもう一度ジャンプできる「2段ジャンプ」が可能になる。
    [System.ComponentModel.DisplayName("2段ジャンプ")]
    [System.ComponentModel.Category("アクション")]
    public bool canDoubleJump { get; set; } = false;

    // trueの場合、高速で移動する「ダッシュ」アクションが使用可能になる。
    [System.ComponentModel.DisplayName("ダッシュ")]
    [System.ComponentModel.Category("アクション")]
    public bool canDash { get; set; } = false;

    // trueの場合、火の玉を発射する攻撃アクションが使用可能になる。
    [System.ComponentModel.DisplayName("火の玉を撃つ")]
    [System.ComponentModel.Category("アクション")]
    public bool canShootFireball { get; set; } = false;

    // trueの場合、自由に空を飛べる「飛行」アクションが使用可能になる。
    [System.ComponentModel.DisplayName("飛行")]
    [System.ComponentModel.Category("アクション")]
    public bool canFly { get; set; } = false;

    // ジャンプ時に加わる初速（負の値ほど高く跳ぶ。上方向がマイナス座標のため）。
    [System.ComponentModel.DisplayName("基本ジャンプ力")]
    [System.ComponentModel.Category("パラメータ")]
    public int baseJumpPower { get; set; } = -12;

    // プレイヤーの通常時の横移動速度。
    [System.ComponentModel.DisplayName("基本移動速度")]
    [System.ComponentModel.Category("パラメータ")]
    public float baseSpeed { get; set; } = 4.0f;
}

// ===== 編集ツール許可設定 =====
// このステージでプレイヤーがどの「編集ツール」（時間巻き戻し等、このゲーム独自のギミック操作）
// を使用できるかを、機能ごとにON/OFFで設定する。
public class EditToolFlags
{
    // trueの場合、時間を巻き戻す「巻き戻し」ツールが使用可能になる。
    [System.ComponentModel.DisplayName("巻き戻し")]
    [System.ComponentModel.Category("編集ツール")]
    public bool rewindEnabled { get; set; } = true;

    // trueの場合、時間の流れを止める「一時停止」ツールが使用可能になる。
    [System.ComponentModel.DisplayName("一時停止")]
    [System.ComponentModel.Category("編集ツール")]
    public bool pauseEnabled { get; set; } = true;

    // trueの場合、時間の流れを速める「早送り」ツールが使用可能になる。
    [System.ComponentModel.DisplayName("早送り")]
    [System.ComponentModel.Category("編集ツール")]
    public bool fastForwardEnabled { get; set; } = true;

    // trueの場合、ズーム・明暗・色味を変える「画面エフェクト」ツールが使用可能になる。
    [System.ComponentModel.DisplayName("画面エフェクト(ズーム/明暗/色)")]
    [System.ComponentModel.Category("編集ツール")]
    public bool screenEffectEnabled { get; set; } = true;

    // trueの場合、個々の敵やギミックを個別に編集・操作する機能が使用可能になる。
    [System.ComponentModel.DisplayName("個別オブジェクト編集")]
    [System.ComponentModel.Category("編集ツール")]
    public bool objectEditEnabled { get; set; } = true;
}

// ===== 編集コスト経済設定 =====
// 編集ツール（巻き戻し・一時停止等）を使うたびに消費される「コスト」ゲージの設定。
// コストが尽きるとツールが使えなくなり、時間経過で自然回復する、というリソース管理の仕組み。
public class EditCostSettings
{
    // コストゲージの最大値。
    [System.ComponentModel.DisplayName("最大値")]
    [System.ComponentModel.Category("コスト")]
    public float maxCost { get; set; } = 100.0f;

    // 何もツールを使っていない時に1秒あたり自然回復するコスト量。
    [System.ComponentModel.DisplayName("自然回復/秒")]
    [System.ComponentModel.Category("コスト")]
    public float regenPerSec { get; set; } = 6.0f;

    // 「巻き戻し」ツールを使用中、1秒あたりに消費されるコスト量。
    [System.ComponentModel.DisplayName("巻き戻し消費/秒")]
    [System.ComponentModel.Category("コスト")]
    public float drainRewindPerSec { get; set; } = 18.0f;

    // 「一時停止」ツールを使用中、1秒あたりに消費されるコスト量。
    [System.ComponentModel.DisplayName("一時停止消費/秒")]
    [System.ComponentModel.Category("コスト")]
    public float drainPausePerSec { get; set; } = 4.0f;

    // 「早送り」ツールを使用中、1秒あたりに消費されるコスト量。
    [System.ComponentModel.DisplayName("早送り消費/秒")]
    [System.ComponentModel.Category("コスト")]
    public float drainFastForwardPerSec { get; set; } = 10.0f;

    // 「画面エフェクト」ツールを使用中、1秒あたりに消費されるコスト量。
    [System.ComponentModel.DisplayName("画面エフェクト消費/秒")]
    [System.ComponentModel.Category("コスト")]
    public float drainScreenEffectPerSec { get; set; } = 8.0f;

    // 色フィルタを1回切り替えるたびに消費される固定コスト量（時間ではなく1回ごとの消費）。
    [System.ComponentModel.DisplayName("色フィルタ切替")]
    [System.ComponentModel.Category("コスト(単発)")]
    public float flatColorCycle { get; set; } = 5.0f;

    // メニューを1回開閉するたびに消費される固定コスト量。
    [System.ComponentModel.DisplayName("メニュートグル")]
    [System.ComponentModel.Category("コスト(単発)")]
    public float flatMenuToggle { get; set; } = 8.0f;

    // 速度を1回変更するたびに消費される固定コスト量。
    [System.ComponentModel.DisplayName("速度変更")]
    [System.ComponentModel.Category("コスト(単発)")]
    public float flatSpeedChange { get; set; } = 6.0f;

    // 向きを1回反転させるたびに消費される固定コスト量。
    [System.ComponentModel.DisplayName("向き反転")]
    [System.ComponentModel.Category("コスト(単発)")]
    public float flatDirectionFlip { get; set; } = 4.0f;

    // 「すべてリセット」操作を1回行うたびに消費される固定コスト量。
    [System.ComponentModel.DisplayName("すべてリセット")]
    [System.ComponentModel.Category("コスト(単発)")]
    public float flatResetAll { get; set; } = 10.0f;
}

// ===== 配置オブジェクト =====
// マップ上に実際に1体配置された敵1体分の情報。EnemyDef（敵の「種類」の定義）とは別に、
// 「どの種類の敵を、どこに、どんな巡回範囲で置くか」を表す。
public class PlacedEnemy
{
    // どの敵の種類(EnemyDef.id)を配置するかを表すID。
    [System.ComponentModel.DisplayName("ID")]
    public string Id { get; set; } = "";
    // 配置するX座標。
    [System.ComponentModel.DisplayName("X座標")]
    public float X { get; set; }
    // 配置するY座標。
    [System.ComponentModel.DisplayName("Y座標")]
    public float Y { get; set; }
    // 巡回行動をする敵の場合の、巡回範囲の左端座標。-1は「未設定（巡回しない）」を意味する。
    [System.ComponentModel.DisplayName("巡回左端")]
    public float PatrolLeft { get; set; } = -1;
    // 巡回行動をする敵の場合の、巡回範囲の右端座標。-1は「未設定（巡回しない）」を意味する。
    [System.ComponentModel.DisplayName("巡回右端")]
    public float PatrolRight { get; set; } = -1;
}

// マップ上に実際に1体配置されたギミック1個分の情報。
public class PlacedGimmick
{
    // どのギミックの種類(GimmickDef.id)を配置するかを表すID。
    [System.ComponentModel.DisplayName("ID")]
    public string Id { get; set; } = "";
    // 配置するX座標。
    [System.ComponentModel.DisplayName("X座標")]
    public float X { get; set; }
    // 配置するY座標。
    [System.ComponentModel.DisplayName("Y座標")]
    public float Y { get; set; }
    // このギミック固有の追加パラメータ（ギミックの種類によって意味が変わる文字列）。
    [System.ComponentModel.DisplayName("パラメータ")]
    public string Param { get; set; } = "";
}

// マップ上に実際に1個配置されたアイテム1個分の情報。
public class PlacedItem
{
    // どのアイテムの種類(ItemDef.id)を配置するかを表すID。
    [System.ComponentModel.DisplayName("ID")]
    public string Id { get; set; } = "";
    // 配置するX座標。
    [System.ComponentModel.DisplayName("X座標")]
    public float X { get; set; }
    // 配置するY座標。
    [System.ComponentModel.DisplayName("Y座標")]
    public float Y { get; set; }
}

// 当たり判定用の矩形1個分の情報（足場・壁など、見た目のタイルとは別に判定だけを持たせたい場合に使う）。
// 始点(X1,Y1)から終点(X2,Y2)までを結ぶ矩形・線分として扱われる。
public class PlatformRect
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
}

// ===== アセット定義 =====
// 敵の「種類」1つ分の定義（見た目・耐久力・行動パラメータなど）。PlacedEnemyはこの定義を
// 「どこに置くか」だけを持ち、実際の性能はここに集約されている。enemies.jsonに保存される。
public class EnemyDef
{
    // この敵の種類を一意に識別するID。PlacedEnemy.Idから参照される。
    public string id { get; set; } = "";
    // エディタ上に表示するための敵の名前。
    public string name { get; set; } = "";
    // 敵の挙動の種類を表す列挙値（C++側のenumと対応する数値。挙動ロジックの分岐に使われる）。
    public int type_enum { get; set; }
    // 敵の耐久力（この回数だけダメージを受けると倒される）。
    public int hp { get; set; } = 3;
    // 敵の表示上の幅（ピクセル）。
    public int width { get; set; } = 32;
    // 敵の表示上の高さ（ピクセル）。
    public int height { get; set; } = 32;
    // この敵の見た目に使うスプライト画像のファイル名。
    public string sprite { get; set; } = "";
    // サウンド SE (Feature 3)
    // 各状況で再生する効果音のID（SoundDef.idを参照する。空文字列なら再生しない）。
    public string seSpawn { get; set; } = "";   // 出現時
    public string seAttack { get; set; } = "";  // 攻撃時
    public string seDamage { get; set; } = "";  // 被ダメージ時
    public string seDeath { get; set; } = "";   // 撃破時
    // Hitbox (Feature: Visual Hitbox Editor)
    // 見た目の画像(width/height)と当たり判定の大きさ・位置がずれる場合に調整するための値。
    public int hitboxOffsetX { get; set; } = 0;   // 当たり判定のX方向オフセット（画像左上からのずれ）
    public int hitboxOffsetY { get; set; } = 0;   // 当たり判定のY方向オフセット
    public int hitboxWidth { get; set; } = 32;    // 当たり判定の幅
    public int hitboxHeight { get; set; } = 32;   // 当たり判定の高さ
    // 表示スケール（Feature: Visual Size Editor）ゲーム内での表示サイズ倍率
    public float scale { get; set; } = 1.0f;

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // -1 (未設定) の場合、ゲーム側でそのtype_enumの従来の挙動と同じ値が自動的に補完される。
    // つまりここに並ぶ各パラメータは「type_enumごとの標準的な挙動を上書きしたい時だけ」
    // 数値を設定する、任意のチューニング項目である。
    public float moveSpeed { get; set; } = -1.0f;           // 通常時の移動速度
    public float enragedMoveSpeed { get; set; } = -1.0f;     // 怒り状態（強化状態）時の移動速度
    public float actionInterval { get; set; } = -1.0f;       // 行動（攻撃等）の間隔
    public float jumpPowerMult { get; set; } = -1.0f;        // ジャンプ力に掛ける倍率
    public float triggerRange { get; set; } = -1.0f;         // プレイヤーを検知・反応する範囲
    public float detectionRangeY { get; set; } = -1.0f;      // 縦方向の検知範囲
    public float projectileSpeed { get; set; } = -1.0f;      // 発射する弾（飛び道具）の速度
    public float chargeTime { get; set; } = -1.0f;           // 攻撃前のチャージ（溜め）時間
    public float dashSpeedMult { get; set; } = -1.0f;        // ダッシュ行動時の速度倍率
    public float dashDuration { get; set; } = -1.0f;         // ダッシュ行動の継続時間
    public float cooldownTime { get; set; } = -1.0f;         // 行動後のクールダウン（再行動までの待ち）時間
    public float fallDelay { get; set; } = -1.0f;            // 落下し始めるまでの遅延時間
    public float spreadAngle { get; set; } = -1.0f;          // 弾を複数発射する際の拡散角度
    public int spreadCount { get; set; } = -1;               // 拡散発射する弾の本数
    public float floatAmplitude { get; set; } = -1.0f;       // 上下に浮遊する動きの振幅
    public float floatFrequency { get; set; } = -1.0f;       // 上下に浮遊する動きの周期（速さ）
    public float teleportRangeMin { get; set; } = -1.0f;     // テレポートする距離の最小値
    public float teleportRangeMax { get; set; } = -1.0f;     // テレポートする距離の最大値
    public float shrinkFactor { get; set; } = -1.0f;         // 縮小する際の倍率
    public float shieldOnDuration { get; set; } = -1.0f;     // シールドが有効になっている継続時間
    public float shieldOffDuration { get; set; } = -1.0f;    // シールドが無効になっている継続時間
    public float mimicDelayFrames { get; set; } = -1.0f;     // ミミック（模倣）行動の遅延フレーム数
    public float sizeAmplitude { get; set; } = -1.0f;        // サイズが変化する動きの振幅
    public float sizeFrequency { get; set; } = -1.0f;        // サイズが変化する動きの周期（速さ）
    public float minScale { get; set; } = -1.0f;             // サイズ変化時の最小スケール
    public float tempoFrequency { get; set; } = -1.0f;       // テンポ（周期的な変化）の周波数
    public float tempoMin { get; set; } = -1.0f;             // テンポ変化の最小値
    public float tempoMax { get; set; } = -1.0f;             // テンポ変化の最大値
    public float effectRange { get; set; } = -1.0f;          // 効果が及ぶ範囲
    public float brightnessMin { get; set; } = -1.0f;        // 明るさ変化の最小値
    public float tintStrength { get; set; } = -1.0f;         // 色味（ティント）を掛ける強さ
    public float zoomAmplitude { get; set; } = -1.0f;        // ズーム演出の振幅
    public float zoomFrequency { get; set; } = -1.0f;        // ズーム演出の周期（速さ）

    // ==== 敵の動き大幅改良プラン Phase 1 ====
    public float shockwaveRadius { get; set; } = -1.0f;          // 衝撃波（着地時等）の効果範囲半径
    public float fastForwardJitter { get; set; } = -1.0f;        // 早送り中に加えるランダムな揺らぎの大きさ
    public float fastForwardAttackMult { get; set; } = -1.0f;    // 早送り中の攻撃頻度・威力に掛ける倍率
    public float diagonalFallSpeed { get; set; } = -1.0f;        // 斜め方向に落下する際の速度

    // Feature: Puzzle-like Behavior Scripting (M2/M6) — type_enum==20(ENEMY_CUSTOM_SCRIPT)の時に使うJSON ASTブロック配列
    // BlockCanvasControlで組み立てたビジュアルスクリプト（ブロックの木構造）をJSON化して保持する。
    public JArray script { get; set; } = new JArray();

    // Feature: Composite Multi-Part Objects (Parts-M7)
    // 1体の敵を複数の画像パーツの組み合わせで構成したい場合のパーツ一覧（空なら単一画像のまま）。
    public List<PartDef> parts { get; set; } = new();
}

// ギミックの「種類」1つ分の定義。PlacedGimmickはこの定義を「どこに置くか」だけを持つ。
public class GimmickDef
{
    // このギミックの種類を一意に識別するID。PlacedGimmick.Idから参照される。
    public string id { get; set; } = "";
    // エディタ上に表示するためのギミックの名前。
    public string name { get; set; } = "";
    // ギミックの挙動の種類を表す列挙値（C++側のenumと対応する数値）。
    public int type_enum { get; set; }
    // このギミックの見た目に使うスプライト画像のファイル名。
    public string sprite { get; set; } = "";
    // サウンド SE (Feature 3)
    // ギミックが作動した際に再生する効果音のID（SoundDef.idを参照する）。
    public string seActivate { get; set; } = "";
    // Hitbox (Feature: Visual Hitbox Editor)
    // 見た目の画像と当たり判定の大きさ・位置がずれる場合に調整するための値。
    public int hitboxOffsetX { get; set; } = 0;
    public int hitboxOffsetY { get; set; } = 0;
    public int hitboxWidth { get; set; } = 32;
    public int hitboxHeight { get; set; } = 32;

    // ==== Feature: Configurable Behavior Parameters (M1) ====
    // -1 (未設定) の場合、ゲーム側でそのtype_enumの従来の挙動と同じ値が自動的に補完される。
    public float rotationSpeed { get; set; } = -1.0f;            // 回転速度
    public float sinkSpeed { get; set; } = -1.0f;                 // 沈み込む速度（足場が沈むギミック等）
    public float maxDepthOffset { get; set; } = -1.0f;            // 沈み込める最大の深さ
    public float pushOutDistance { get; set; } = -1.0f;           // プレイヤーを押し出す距離
    public float triggerWidthThreshold { get; set; } = -1.0f;     // 作動判定の横幅のしきい値
    public float travelDistance { get; set; } = -1.0f;            // 移動する距離（動く床等）
    public float oscillationSpeed { get; set; } = -1.0f;          // 往復運動の速度
    public float stepIncrement { get; set; } = -1.0f;             // 1ステップあたりの変化量
    public float standDelayFrames { get; set; } = -1.0f;          // プレイヤーが乗ってから作動するまでの遅延フレーム数
    public float standTolerancePx { get; set; } = -1.0f;          // 「乗っている」と判定する許容ピクセル数
    public float respawnDelayFrames { get; set; } = -1.0f;        // 再出現（リスポーン）までの遅延フレーム数
    public float radius { get; set; } = -1.0f;                    // 効果範囲の半径
    public float brightLevel { get; set; } = -1.0f;               // 明るさ演出の明るい側のレベル
    public float darkLevel { get; set; } = -1.0f;                 // 明るさ演出の暗い側のレベル
    public float tintR { get; set; } = -1.0f;                     // 色味（ティント）の赤成分
    public float tintG { get; set; } = -1.0f;                     // 色味（ティント）の緑成分
    public float tintB { get; set; } = -1.0f;                     // 色味（ティント）の青成分
    public float zoomLevel { get; set; } = -1.0f;                 // ズーム演出の倍率
    public float warpOffsetPx { get; set; } = -1.0f;              // ワープ（瞬間移動）させる距離（ピクセル）

    // Feature: Puzzle-like Behavior Scripting (M2/M6) — type_enum==24(GIMMICK_CUSTOM_SCRIPT)の時に使うJSON ASTブロック配列
    public JArray script { get; set; } = new JArray();

    // Feature: Composite Multi-Part Objects (Parts-M7)
    // 1つのギミックを複数の画像パーツの組み合わせで構成したい場合のパーツ一覧。
    public List<PartDef> parts { get; set; } = new();
}

// アイテムの「種類」1つ分の定義。PlacedItemはこの定義を「どこに置くか」だけを持つ。
public class ItemDef
{
    // このアイテムの種類を一意に識別するID。PlacedItem.Idから参照される。
    public string id { get; set; } = "";
    // エディタ上に表示するためのアイテムの名前。
    public string name { get; set; } = "";
    // アイテムの種類を表す列挙値（C++側のenumと対応する数値）。
    public int type_enum { get; set; }
    // このアイテムの見た目に使うスプライト画像のファイル名。
    public string sprite { get; set; } = "";
    // このアイテムを取得した時にプレイヤーへ付与する能力を表す文字列
    // （PlayerCapabilitiesのプロパティ名等と対応させて、取得時に該当フラグをONにする想定）。
    public string grant_ability { get; set; } = "";
    // サウンド SE (Feature 3)
    // アイテムを取得した際に再生する効果音のID。
    public string seCollect { get; set; } = "";
    // Hitbox (Feature: Visual Hitbox Editor)
    // 見た目の画像と当たり判定の大きさ・位置がずれる場合に調整するための値。
    public int hitboxOffsetX { get; set; } = 0;
    public int hitboxOffsetY { get; set; } = 0;
    public int hitboxWidth { get; set; } = 32;
    public int hitboxHeight { get; set; } = 32;

    // Feature: Composite Multi-Part Objects (Parts-M7)
    // 1つのアイテムを複数の画像パーツの組み合わせで構成したい場合のパーツ一覧。
    public List<PartDef> parts { get; set; } = new();
}

// ===== 複合オブジェクトのパーツ定義 (Feature: Composite Multi-Part Objects) =====
// 敵/ギミック/アイテムを、複数の画像パーツの組み合わせとして構成するためのテンプレート。
// C++側 DrawPixel.cpp の PartDef 構造体と1:1で対応する（フィールド名も一致させること）。
public class PartDef
{
    // このパーツを一意に識別するID。
    public string id { get; set; } = "";
    // このパーツの見た目に使うスプライト画像のファイル名。
    public string sprite { get; set; } = "";
    // 親オブジェクトの基準位置からのオフセット（X方向）。パーツの配置位置を決める。
    public float offsetX { get; set; } = 0f;
    // 親オブジェクトの基準位置からのオフセット（Y方向）。
    public float offsetY { get; set; } = 0f;
    // このパーツの表示幅（ピクセル）。
    public int width { get; set; } = 0;
    // このパーツの表示高さ（ピクセル）。
    public int height { get; set; } = 0;
    // このパーツ単体の当たり判定のオフセット・サイズ（親とは別に個別に持てる）。
    public int hitboxOffsetX { get; set; } = 0;
    public int hitboxOffsetY { get; set; } = 0;
    public int hitboxWidth { get; set; } = 32;
    public int hitboxHeight { get; set; } = 32;
    // このパーツの表示サイズ倍率。
    public float scale { get; set; } = 1.0f;
    // 0=破壊不能(常在ハザード) / 1以上=個別に破壊可能
    public int hp { get; set; } = 0;
    // 負=親より奥に描画、正=親より手前
    public int zOrder { get; set; } = 0;
    // このパーツ専用のBehaviorScript（OnSpawn/OnDamaged/OnDeath）
    // このパーツだけに適用される、ビジュアルスクリプトのJSON AST（ブロック構造）。
    public JArray script { get; set; } = new JArray();
}

// アセット（敵・ギミック・アイテム・タイル・サウンド・アニメーション・コモンイベント）の
// 全定義データを1つにまとめたコンテナ。エディタ起動時にAssetsフォルダから読み込み、
// 保存時にはこの内容を各JSONファイルへ書き戻す。
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

    // assetsPath : Assetsフォルダのパス
    // フォルダ内の各JSONファイル（enemies.json等）を読み込み、1つのAssetDefinitionsにまとめて返す。
    // 個々のファイルが存在しない・読み込みに失敗した場合はそのリストを空のまま（デフォルト）にする。
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

    // 指定パスのJSONファイルを読み込み、型Tのリストへ変換する共通処理。
    // path : 読み込むJSONファイルのパス
    // ファイルが存在しない場合や読み込み・変換に失敗した場合はnullを返す
    // （呼び出し側で ?? によりデフォルトのリストへフォールバックする）。
    private static List<T>? LoadJson<T>(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonConvert.DeserializeObject<List<T>>(File.ReadAllText(path)); } catch { return null; }
    }

    // assetsPath : 書き出し先のAssetsフォルダのパス
    // 保持している全アセット定義を、それぞれ対応するJSONファイルへ整形済みの形で書き出す。
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
