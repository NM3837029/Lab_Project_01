using Newtonsoft.Json;

namespace Lab_Editor;

// パーツの「画像の切り替え」（条件を満たしたとき、パーツの画像を別の絵に差し替える）。
//
// C++側 DrawPixel.cpp の PartSpriteRule / PartCond と1:1で対応する。JSON上のキー名・条件名は両者で完全に一致させること
// （ここに無いキーは、エディタで保存し直した瞬間にJSONから黙って消える）。
//
// 1つのパーツに、条件と画像の組を何本でも並べられる。**上から順に見て、最初に当てはまった行の画像**を使い、
// どれにも当てはまらなければ、パーツ本来の画像のまま。
//
// 【選べる条件は、そのアセットが「何をできるか」から決める】
// 敵・ギミックの型や、編集を禁止しているかどうか（拡大・回転・反転・一時停止・巻き戻し）を調べて、
// その物に起こりうる条件だけを一覧に出す。たとえば、扉の型なら「開いている／閉じている」、
// 弾を撃つ敵なら「撃つ前の溜め」、拡大を禁止してある物には「拡大されている」を出さない。
// 起こらない条件を選べてしまうと、絵が切り替わらないパーツを作ってしまうため。
public class PartSpriteRule
{
    // 条件の名前（PartSpriteRules.Conditions の name）
    public string when { get; set; } = "near";
    // 条件ごとの数値（near=距離px、hp_low=割合%、hurt/attacking=続くフレーム数、moving=速さ）
    public float param { get; set; } = 0f;
    // 切り替え先の画像（img/ からの相対パス）
    public string sprite { get; set; } = "";

    public PartSpriteRule Clone() => (PartSpriteRule)MemberwiseClone();
}

// 条件を選ぶための、持ち主（敵・ギミック・アイテム）の情報。
public sealed class PartOwnerInfo
{
    public string Kind = "";            // "enemy" / "gimmick" / "item"
    public int TypeEnum = -1;           // 敵・ギミックのタイプ番号（不明なら -1）
    public string TypeName = "";        // 画面に出す、型の短い名前
    // このアセットが禁止している編集操作（アセット管理の「○○を禁止」）。禁止してあるなら、その条件は起こらない
    public bool NoScale, NoRotate, NoFlip, NoPause, NoRewind;

    public static PartOwnerInfo Unknown() => new() { Kind = "unknown" };
}

public static class PartSpriteRules
{
    public sealed class ConditionInfo
    {
        public string Name = "";
        public string Label = "";
        public string Hint = "";
        public string? ParamLabel;      // 数値を使うなら、その名前（単位つき）
        public float ParamMin, ParamMax, ParamDefault, ParamStep = 1f;
        public int ParamDecimals;
    }

    // すべての条件（C++ の PartCond の名前と同じ）。どれを出すかは Available が決める
    public static readonly ConditionInfo[] Conditions =
    {
        new() { Name = "near",     Label = "プレイヤーが近いとき", Hint = "プレイヤーが指定の距離以内にいる間、画像を切り替えます。",
                ParamLabel = "距離(px)", ParamMin = 8, ParamMax = 800, ParamDefault = 160, ParamStep = 8 },
        new() { Name = "flipped",  Label = "向きを反転されているとき", Hint = "ゲーム内の編集（ダブルクリック等）で、向きを反転されている間。" },
        new() { Name = "tilted",   Label = "傾けられているとき", Hint = "ゲーム内の編集（Ctrl+ホイール等）で、傾けられている間。" },
        new() { Name = "enlarged", Label = "拡大されているとき", Hint = "ゲーム内の編集で、置いたときより大きくされている間。" },
        new() { Name = "shrunk",   Label = "縮小されているとき", Hint = "ゲーム内の編集で、置いたときより小さくされている間。" },
        new() { Name = "paused",   Label = "個別に一時停止されているとき", Hint = "この物だけを一時停止されている間。" },
        new() { Name = "rewinding",Label = "個別に巻き戻し中のとき", Hint = "この物だけを巻き戻している間。" },
        new() { Name = "hp_low",   Label = "HPが少ないとき", Hint = "HPが、最大HPの指定の割合以下になっている間。",
                ParamLabel = "割合(%)", ParamMin = 1, ParamMax = 100, ParamDefault = 50, ParamStep = 5 },
        new() { Name = "hurt",     Label = "弾を受けた直後", Hint = "弾を受けてから、指定のフレームの間（0なら20フレーム）。",
                ParamLabel = "続くフレーム数", ParamMin = 0, ParamMax = 300, ParamDefault = 20, ParamStep = 5 },
        new() { Name = "attacking",Label = "攻撃した直後", Hint = "弾を撃つ・突進する・飛びかかるなどをしてから、指定のフレームの間（0なら20フレーム）。",
                ParamLabel = "続くフレーム数", ParamMin = 0, ParamMax = 300, ParamDefault = 20, ParamStep = 5 },
        new() { Name = "charge",   Label = "溜めているとき（予兆）", Hint = "突進の溜め・落下の予兆・射撃の直前など、「これから行動する」間。" },
        new() { Name = "active",   Label = "行動中", Hint = "突進中・落下中・飛びかかりの飛行中・効果を出している間。" },
        new() { Name = "moving",   Label = "動いているとき", Hint = "歩く・飛ぶ・落ちるなど、動いている間（指定の速さ以上）。",
                ParamLabel = "速さ(px/フレーム)", ParamMin = 0.05f, ParamMax = 20, ParamDefault = 0.3f, ParamStep = 0.05f, ParamDecimals = 2 },
        new() { Name = "airborne", Label = "空中にいるとき", Hint = "地面に立っておらず、跳んでいる・落ちている間。" },
        new() { Name = "facing_left",  Label = "左を向いているとき", Hint = "敵が左を向いている間。" },
        new() { Name = "facing_right", Label = "右を向いているとき", Hint = "敵が右を向いている間。" },
        new() { Name = "effect_on",Label = "効果の範囲にプレイヤーがいるとき", Hint = "画面を暗くする・色を変える・ズームを固定する・回す・大きさを変える敵が、効果を出せる範囲にプレイヤーがいる間。" },
        new() { Name = "gate_open",   Label = "開いているとき（通れる）", Hint = "扉・ゲートが開いて、通れる間。" },
        new() { Name = "gate_closed", Label = "閉じているとき（通れない）", Hint = "扉・ゲートが閉じて、通れない間。" },
        new() { Name = "pressed",  Label = "スイッチが入っているとき", Hint = "重量スイッチ・補正スイッチが、押されて（条件を満たして）いる間。" },
        new() { Name = "solid",    Label = "足場が実体化しているとき", Hint = "色・明暗・補正ロックの足場が、条件を満たして、乗れる間。" },
        new() { Name = "activated",Label = "発動済みのとき", Hint = "チェックポイントに触れて、復帰地点になっている間。" },
    };

    public static ConditionInfo? Find(string name) => Conditions.FirstOrDefault(c => c.Name == name);
    public static string LabelOf(string name) => Find(name)?.Label ?? name;

    // ---- 敵・ギミックの型ごとの「できること」 ----
    // タイプ番号は C++ の EnemyType / GimmickType と同じ（DrawPixel.cpp。AssetManagerPageControl の一覧とも一致）。
    private static readonly HashSet<int> EnemyCharge = new() { 2, 3, 6, 7, 8, 9, 21, 23, 25 };   // 溜め・予兆の段階を持つ
    private static readonly HashSet<int> EnemyActive = new() { 6, 7, 19, 21, 23, 24 };           // 行動中の段階を持つ
    private static readonly HashSet<int> EnemyAttack = new() { 2, 3, 6, 7, 8, 9, 11, 21, 22, 23, 25 }; // 攻撃・突進・瞬間移動などをする
    private static readonly HashSet<int> EnemyEffect = new() { 17, 18, 19, 24, 25 };             // 範囲で効果を出す
    private static readonly HashSet<int> EnemyStationary = new() { 2, 8, 9, 19, 24, 25 };        // その場から動かない
    private static readonly HashSet<int> EnemyFlying = new() { 10, 14, 22 };                     // 浮く・飛ぶ・すり抜ける（地面に立たない）
    private static readonly HashSet<int> GimmickDoor = new() { 8, 16, 28 };                      // ゲート扉・早送りゲート・逆行ゲート
    private static readonly HashSet<int> GimmickSwitch = new() { 6, 26 };                        // 重量スイッチ・補正スイッチ
    private static readonly HashSet<int> GimmickLock = new() { 22, 23, 27 };                     // 色・明暗・補正ロック足場
    private static readonly HashSet<int> GimmickAngleAiOwned = new() { 1, 11, 24 };              // 角度をAIが使う（傾け編集が効かない）

    // この持ち主に起こりうる条件を、画面に出す順で返す。
    public static List<ConditionInfo> Available(PartOwnerInfo o)
    {
        var names = new List<string>();
        void Add(string n) { if (!names.Contains(n)) names.Add(n); }

        Add("near"); // プレイヤーとの距離は、どの物でも測れる
        bool known = o.Kind == "enemy" || o.Kind == "gimmick" || o.Kind == "item";
        if (!known)
        {
            // 持ち主が分からないときは、全部を選べるようにする（絞り込めないので）
            foreach (var c in Conditions) Add(c.Name);
        }
        else
        {
            // ゲーム内の編集：禁止していなければ、起こりうる（アイテムは、編集ツールで選べないので出さない）
            if (o.Kind != "item")
            {
                if (!o.NoFlip) Add("flipped");
                bool angleAi = o.Kind == "gimmick" && GimmickAngleAiOwned.Contains(o.TypeEnum);
                if (!o.NoRotate && !angleAi) Add("tilted");
                if (!o.NoScale) { Add("enlarged"); Add("shrunk"); }
                if (!o.NoPause) Add("paused");
                if (!o.NoRewind) Add("rewinding");
            }
            if (o.Kind == "enemy")
            {
                int t = o.TypeEnum;
                Add("hp_low"); Add("hurt");
                if (EnemyAttack.Contains(t) || t == 20) Add("attacking");   // 20=カスタムスクリプトは、何をするか決まっていないので全部出す
                if (EnemyCharge.Contains(t) || t == 20) Add("charge");
                if (EnemyActive.Contains(t) || t == 20) Add("active");
                if (!EnemyStationary.Contains(t)) Add("moving");
                if (!EnemyStationary.Contains(t) && !EnemyFlying.Contains(t)) Add("airborne");
                if (t != 22) { Add("facing_left"); Add("facing_right"); }   // 22=跳ね回る節足敵は、向きを角度で持つ
                if (EnemyEffect.Contains(t) || t == 20) Add("effect_on");
            }
            else if (o.Kind == "gimmick")
            {
                int t = o.TypeEnum;
                if (GimmickDoor.Contains(t)) { Add("gate_open"); Add("gate_closed"); }
                if (GimmickSwitch.Contains(t)) Add("pressed");
                if (GimmickLock.Contains(t)) Add("solid");
                if (t == 25) Add("activated");
            }
        }
        return Conditions.Where(c => names.Contains(c.Name)).ToList();
    }

    // 「この持ち主は〜ができるので、これらの条件を選べます」という説明（パーツ編集の入力欄の見出し）
    public static string Describe(PartOwnerInfo o)
    {
        string who = o.Kind switch { "enemy" => "この敵", "gimmick" => "このギミック", "item" => "このアイテム", _ => "この物" };
        if (o.Kind == "unknown") return "持ち主の種類が分からないため、すべての条件を選べます。";
        var abilities = new List<string>();
        if (o.Kind == "enemy")
        {
            int t = o.TypeEnum;
            if (EnemyAttack.Contains(t)) abilities.Add("攻撃する");
            if (EnemyCharge.Contains(t)) abilities.Add("溜める（予兆）");
            if (EnemyEffect.Contains(t)) abilities.Add("範囲で効果を出す");
            if (!EnemyStationary.Contains(t)) abilities.Add(EnemyFlying.Contains(t) ? "飛ぶ" : "動く");
            abilities.Add("弾を受ける");
        }
        else if (o.Kind == "gimmick")
        {
            int t = o.TypeEnum;
            if (GimmickDoor.Contains(t)) abilities.Add("開閉する");
            if (GimmickSwitch.Contains(t)) abilities.Add("スイッチになる");
            if (GimmickLock.Contains(t)) abilities.Add("条件で実体化する");
            if (t == 25) abilities.Add("復帰地点になる");
        }
        var denied = new List<string>();
        if (o.Kind != "item")
        {
            if (o.NoFlip) denied.Add("反転");
            if (o.NoRotate || (o.Kind == "gimmick" && GimmickAngleAiOwned.Contains(o.TypeEnum))) denied.Add("傾け");
            if (o.NoScale) denied.Add("拡大縮小");
            if (o.NoPause) denied.Add("一時停止");
            if (o.NoRewind) denied.Add("巻き戻し");
        }
        string head = string.IsNullOrEmpty(o.TypeName) ? who : $"{who}（{o.TypeName}）";
        string s = head + (abilities.Count > 0 ? "は、" + string.Join("・", abilities) + "ことができます。" : "の条件を選べます。");
        if (denied.Count > 0) s += $" 編集の{string.Join("・", denied)}はできない設定なので、その条件は出していません。";
        return s;
    }

    // 一覧に出す1行の説明：「条件 → 画像名」
    public static string DescribeRule(PartSpriteRule r)
    {
        var c = Find(r.when);
        string when = c?.Label ?? r.when;
        if (c?.ParamLabel != null) when += $"（{r.param:0.##}）";
        string img = string.IsNullOrEmpty(r.sprite) ? "（画像未選択）" : Path.GetFileNameWithoutExtension(r.sprite);
        return $"{when} → {img}";
    }
}
