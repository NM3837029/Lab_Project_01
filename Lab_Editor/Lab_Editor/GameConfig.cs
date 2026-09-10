using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// assets/game_config.json のデータモデル。
//
// 【なぜ AssetDefinitions に混ぜないのか】
// AssetDefinitions.SaveToFolder は呼ばれるたびに enemies.json 等9本を全部書き直す。
// そこに game_config.json を混ぜると「敵の当たり判定を直そうとしてアセット管理を
// 保存しただけなのに、タイトル画面のレイアウトも書き直された」が起きる。独立させておく。
//
// 【なぜ全クラスに [JsonExtensionData] を付けるのか】
// StageData.cs の EnemyDef に「C++側と1:1で対応させること。ここに無いキーは
// エディタで保存し直した瞬間に黙って消える」という警告が残っている。これは実際に
// 踏んだ地雷で、同じ形をここでも作らないための手当て。
// _extra が未知のキーを丸ごと拾って保存時に書き戻すので、C++側だけが解釈する
// キーを後から足しても、エディタを通しただけで消えることがない。
public class GameConfig
{
    public int version { get; set; } = 1;

    // ウィンドウのタイトルバーに出す文字列（C++側が SetMainWindowText へ渡す）
    public string window_title { get; set; } = "Lab Project 01";

    // false にするとタイトル画面を出さず、従来どおり起動即プレイになる。
    // 何か問題が起きたときに設定だけで元へ戻せるようにするための逃げ道。
    public bool title_enabled { get; set; } = true;

    public ThemeColors theme { get; set; } = new();
    public TitleScreenConfig title_screen { get; set; } = new();
    public StageSelectConfig stage_select { get; set; } = new();
    public List<StageEntry> stages { get; set; } = new();
    public ResultConfig result { get; set; } = new();

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();

    public static string PathFor(string assetsPath) => Path.Combine(assetsPath, "game_config.json");

    // 読み込む。ファイルが無い・壊れている場合は既定値を返す（例外は投げない）。
    public static GameConfig Load(string assetsPath)
    {
        string p = PathFor(assetsPath);
        if (!File.Exists(p)) return CreateDefault();
        try
        {
            var cfg = JsonConvert.DeserializeObject<GameConfig>(File.ReadAllText(p));
            return cfg ?? CreateDefault();
        }
        catch { return CreateDefault(); }
    }

    public void Save(string assetsPath)
    {
        // stages の order は「リストの並び順」を正とし、保存のたびに 0..N-1 へ振り直す。
        // 手で編集した order が重複していると、C++側の「1つ前のステージをクリアしたら解放」
        // という判定が不定になるため。
        for (int i = 0; i < stages.Count; i++) stages[i].order = i;

        File.WriteAllText(PathFor(assetsPath), JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    // game_config.json がまだ無いプロジェクト向けの初期値。
    // C++側 GameConfig.h の既定値と揃えてある。
    public static GameConfig CreateDefault()
    {
        var cfg = new GameConfig();
        cfg.title_screen.elements = new List<TitleElement>
        {
            new() { key = "logo",     type = "image", visible = false, x = 320, y = 110, w = 360, h = 120 },
            new() { key = "title",    type = "text",  visible = true, text = "Lab Project 01",
                    x = 320, y = 92,  font_size = 52, align = "center", color_role = "accent", edge = true },
            new() { key = "subtitle", type = "text",  visible = true, text = "",
                    x = 320, y = 162, font_size = 20, align = "center", color_role = "ink", edge = true },
            new() { key = "menu",     type = "menu",  visible = true,
                    x = 320, y = 240, w = 260, item_h = 46, gap = 12, font_size = 22, align = "center" },
        };
        cfg.title_screen.menu_items = new List<MenuItemDef>
        {
            new() { label = "はじめる",   action = "stage_select" },
            new() { label = "つづきから", action = "continue" },
            new() { label = "おわる",     action = "quit" },
        };
        return cfg;
    }
}

public class ThemeColors
{
    // 色は [R, G, B] の配列で持つ（C++側もこの形で読む）
    public int[] ink { get; set; } = { 32, 34, 40 };
    public int[] ink_sub { get; set; } = { 112, 116, 126 };
    public int[] ink_accent { get; set; } = { 20, 84, 132 };
    public int[] backdrop { get; set; } = { 246, 240, 228 };

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

public class TitleScreenConfig
{
    public string background_image { get; set; } = "";
    public string bgm_id { get; set; } = "";
    public List<TitleElement> elements { get; set; } = new();
    public List<MenuItemDef> menu_items { get; set; } = new();

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

// タイトル画面に置く要素1つぶん。
// key は "logo" / "title" / "subtitle" / "menu" の固定4種で、追加・削除はしない。
// 座標は全て「ゲーム内部解像度 640x480」で持つ（配置キャンバスもこの座標系）。
public class TitleElement
{
    public string key { get; set; } = "";
    public string type { get; set; } = "text";   // "text" | "image" | "menu"
    public bool visible { get; set; } = true;
    public string text { get; set; } = "";
    public string image { get; set; } = "";
    // align=="center" のとき x は中心座標（実行時の中央寄せと一致させるため）
    public float x { get; set; }
    public float y { get; set; }
    public float w { get; set; }
    public float h { get; set; }
    public int font_size { get; set; } = 20;
    public string align { get; set; } = "center";
    public string color_role { get; set; } = "ink"; // "ink" | "sub" | "accent"
    public bool edge { get; set; } = true;          // 背景画像の上でも読めるよう縁取りを付けるか
    // type=="menu" のときだけ使う
    public float item_h { get; set; } = 46f;
    public float gap { get; set; } = 12f;

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

public class MenuItemDef
{
    public string label { get; set; } = "";
    // "stage_select"（ステージ選択へ）/ "continue"（最後に遊んだステージから）/ "quit"（終了）
    public string action { get; set; } = "stage_select";

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

public class StageSelectConfig
{
    public string background_image { get; set; } = "";
    public string bgm_id { get; set; } = "";
    public string heading { get; set; } = "STAGE SELECT";
    public float heading_x { get; set; } = 320f;
    public float heading_y { get; set; } = 40f;
    public int heading_font_size { get; set; } = 32;
    public GridConfig grid { get; set; } = new();
    public string back_label { get; set; } = "BACK";
    public string locked_label { get; set; } = "? ? ?";

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

public class GridConfig
{
    public float x { get; set; } = 64f;
    public float y { get; set; } = 92f;
    public int cols { get; set; } = 3;
    public float cell_w { get; set; } = 160f;
    public float cell_h { get; set; } = 116f;
    public float gap_x { get; set; } = 16f;
    public float gap_y { get; set; } = 16f;

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

public class StageEntry
{
    // assets/stages/ 配下のファイル名。セーブデータのキーでもあるので、
    // 一度公開したステージのファイル名を変えるとクリア記録が引き継がれない点に注意。
    public string file { get; set; } = "";
    public string name { get; set; } = "";
    public string thumbnail { get; set; } = "";
    public int order { get; set; }
    // "always"（常に選べる）/ "prev_clear"（1つ前をクリアしたら）/ "require"（指定ステージをクリアしたら）
    public string unlock { get; set; } = "always";
    public List<string> require { get; set; } = new();
    // そのステージのアイテム総数。セレクト画面の「3 / 7」表示に使う。
    // 起動時に全ステージJSONをパースせずに済むよう、エディタ側が数えて書き込む。
    public int item_total { get; set; }

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}

public class ResultConfig
{
    public string next_label { get; set; } = "NEXT STAGE";
    public string retry_label { get; set; } = "RETRY";
    public string select_label { get; set; } = "STAGE SELECT";
    public string victory_text { get; set; } = "VICTORY!";
    public string gameover_text { get; set; } = "GAME OVER";

    [JsonExtensionData] public IDictionary<string, JToken> _extra { get; set; } = new Dictionary<string, JToken>();
}
