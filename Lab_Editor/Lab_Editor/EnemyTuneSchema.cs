namespace Lab_Editor;

// 「敵の動きを調整」画面の、項目の定義（型ごとの、グループ・日本語名・単位・範囲・既定値・説明）。
//
// 項目の中身は EnemyDef のプロパティ名（Key）で指す。値は -1 が「既定を使う」で、
// 既定値（Default）は C++側 ApplyEnemyDefaultParams（DrawPixel.cpp）の型ごとの既定値と一致させてある。
// 【重要】C++側の既定値を変えたら、ここも必ず同じ値に直すこと（画面に出る「既定」の数値と実際の動きがずれる）。
public enum TuneKind
{
    Number,   // 数値（スライダー＋入力欄）
    Choice,   // 選択肢（値は 0,1,2…。Choices の並びが値）
    Color,    // 色（Key=R の名前。G/B は Key2/Key3）
    Flag,     // ON/OFF（bool）
}

public sealed class TuneParam
{
    public string Key = "";            // EnemyDef のプロパティ名
    public string Key2 = "", Key3 = ""; // 色のときの G / B
    public string Label = "";          // 画面に出す名前
    public string Unit = "";           // 単位（px・フレーム・倍…）
    public string Hint = "";           // 何が変わるかの説明
    public TuneKind Kind = TuneKind.Number;
    public float Min, Max = 100, Step = 1, Default;
    public int Decimals;
    public string[] Choices = Array.Empty<string>(); // Choice のとき。値は添字（-1=既定）
    public int ChoiceDefault;                        // Choice の既定の添字
    public string DefaultText = "";    // 「既定」を人間の言葉で言うとき（色の「傾けで赤→緑→青」など）
    // 「既定を使う」を外した直後に入れる値。既定値そのものでは意味が無い項目（既定＝制限なし・ブレなし 等）で使う
    public float StartValue = float.NaN;
    public float FirstCustomValue => float.IsNaN(StartValue) ? Default : StartValue;
}

public sealed class TuneGroup
{
    public string Title = "";
    public string? Note;               // グループの説明
    public List<TuneParam> Params = new();
}

// 画面左のプレビューが何を描くか
public enum TunePreview
{
    Range,       // 見つける距離・効果範囲を、敵を中心にした円や帯で見せる（既定）
    Wave,        // 周期的に変わる量（浮遊・大きさ・速さ・ズーム）の波形グラフ
    Brightness,  // 暗転：距離ごとの明るさの帯
    Tint,        // 色変化：寄せる色と、距離ごとの色味
    Shot,        // 射撃：弾の向き・本数・ブレ・予兆
    Timeline,    // 溜め→行動→後隙 の時間配分
    Script,      // スクリプトで動く（調整項目なし）
}

public sealed class TuneType
{
    public int Type;
    public string Name = "";
    public string Summary = "";
    public TunePreview Preview = TunePreview.Range;
    public List<TuneGroup> Groups = new();
}

public static class EnemyTuneSchema
{
    // ---- 項目を作る小さな関数 ----
    private static TuneParam N(string key, string label, string unit, float min, float max, float step, int dec, float def, string hint)
        => new() { Key = key, Label = label, Unit = unit, Min = min, Max = max, Step = step, Decimals = dec, Default = def, Hint = hint };
    private static TuneParam Choice(string key, string label, string hint, string[] choices, int defIndex, string defText = "")
        => new() { Key = key, Label = label, Hint = hint, Kind = TuneKind.Choice, Choices = choices, ChoiceDefault = defIndex, DefaultText = defText };
    private static TuneParam Flag(string key, string label, string hint)
        => new() { Key = key, Label = label, Hint = hint, Kind = TuneKind.Flag };
    private static TuneParam S(TuneParam p, float start) { p.StartValue = start; return p; }
    private static TuneGroup G(string title, string? note, params TuneParam[] ps)
        => new() { Title = title, Note = note, Params = ps.ToList() };

    private static readonly string[] WaveChoices = { "なめらか（ゆらゆら）", "三角（一定の速さで往復）", "カクカク（端で止まる）" };

    // 共通の項目
    private static TuneParam MoveSpeed(float def, string label = "歩く・近づく速さ")
        => N("moveSpeed", label, "倍", 0, 3, 0.05f, 2, def, "プレイヤーの基本の速さに対する倍率。1なら同じ速さ、0.5なら半分。");
    private static TuneParam TriggerRange(float def, string label = "プレイヤーに気づく距離")
        => N("triggerRange", label, "px", 0, 800, 10, 0, def, "プレイヤーがこの距離（横）に入ると動き出します。0にすると気づきません。");
    private static TuneParam ProjSpeed(float def)
        => N("projectileSpeed", "弾の速さ", "倍", 0.1f, 3, 0.05f, 2, def, "弾の基本の速さに対する倍率。大きいほど避けにくくなります。");
    private static TuneParam FfAttack()
        => N("fastForwardAttackMult", "早送り中の連射の速さ", "倍", 1, 6, 0.1f, 1, 2.2f, "プレイヤーが早送りしている間、撃つ間隔がこの倍率で詰まります。");

    private static TuneParam[] WarnParams() => new[]
    {
        N("chargeWarnFrames", "撃つ前の予兆の長さ", "フレーム", 0, 90, 1, 0, 20, "撃つ直前、この長さのあいだ「そろそろ撃つ」の合図（画面のズーム）が出ます。長いほど読みやすくなります。"),
        N("chargeWarnZoom", "予兆の画面ズーム", "", 0, 0.3f, 0.01f, 2, 0.06f, "予兆のとき画面がどれだけズームインするか。0なら合図なし。"),
    };

    public static readonly List<TuneType> Types = Build();

    public static TuneType Get(int type) => Types.FirstOrDefault(t => t.Type == type) ?? new TuneType { Type = type, Name = "（不明な型）", Preview = TunePreview.Script };

    // 共通：どの型にも出す、編集に対する反応のロック
    public static List<TuneGroup> CommonGroups() => new()
    {
        G("共通の設定", "どの敵にも共通の設定です。",
            Flag("ignorePause", "一時停止を無視して動き続ける", "ONにすると、プレイヤーが時間を止めても動き続けます（幽霊のような敵向け）。"),
            Flag("bodyIgnoresTilt", "傾けても本体の絵は傾けない", "ONにすると、回しても動くのはパーツだけで、本体の絵は傾きません（砲台の台座など）。")),
        G("プレイヤーの編集を禁止する", "ONにした操作は、この敵に対してはできなくなります。",
            Flag("noScale", "拡大縮小を禁止", "拡大・縮小ができなくなります。"),
            Flag("noRotate", "回転を禁止", "傾けることができなくなります。"),
            Flag("noMove", "移動を禁止", "ドラッグで動かせなくなります。"),
            Flag("noFlip", "向き反転を禁止", "向きを反転できなくなります。"),
            Flag("noPause", "一時停止を禁止", "この敵だけを止めることができなくなります。"),
            Flag("noRewind", "巻き戻しを禁止", "この敵だけを巻き戻せなくなります。"),
            Flag("noSpeed", "速度変更を禁止", "この敵だけの速さを変えられなくなります。")),
    };

    private static List<TuneType> Build()
    {
        var list = new List<TuneType>();
        void T(int type, string name, string summary, TunePreview prev, params TuneGroup[] groups)
            => list.Add(new TuneType { Type = type, Name = name, Summary = summary, Preview = prev, Groups = groups.ToList() });

        T(0, "巡回", "決まった範囲を行き来します。", TunePreview.Range,
            G("動き", null, MoveSpeed(0.4f, "歩く速さ")));
        T(1, "ジャンプ", "一定の間隔で跳びはねます。", TunePreview.Timeline,
            G("ジャンプ", null,
                N("actionInterval", "ジャンプの間隔", "フレーム", 10, 400, 5, 0, 90, "着地してから、次に跳ぶまでの時間（60フレーム＝約1秒）。"),
                N("jumpPowerMult", "ジャンプ力", "倍", 0.1f, 2, 0.05f, 2, 0.7f, "プレイヤーのジャンプ力に対する倍率。")));
        T(2, "固定砲", "動かず、プレイヤーをいつも狙って撃ちます。", TunePreview.Shot,
            G("撃ち方", null,
                N("actionInterval", "撃つ間隔", "フレーム", 10, 400, 5, 0, 120, "弾を撃つ間隔（60フレーム＝約1秒）。"), ProjSpeed(0.6f), FfAttack()),
            G("予兆とねらい", "撃つ前の合図と、狙いのブレです。",
                WarnParams().Concat(new[]
                {
                    S(N("aimJitter", "狙いのブレ", "ラジアン", 0, 0.8f, 0.01f, 2, 0, "撃つたびに、狙いがこの範囲でランダムにずれます。0ならいつも正確。0.17で約10度。"), 0.15f),
                }).ToArray()));
        T(3, "巡回して撃つ", "巡回しながら、見つけたプレイヤーを撃ちます。", TunePreview.Shot,
            G("見つける範囲", null,
                TriggerRange(300, "見つける距離（横）"),
                N("detectionRangeY", "見つける距離（縦）", "px", 0, 600, 10, 0, 100, "縦方向にどれだけ離れていても見つけられるか。")),
            G("動きと撃ち方", null,
                MoveSpeed(0.5f, "巡回の速さ"),
                N("cooldownTime", "撃つ間隔", "フレーム", 10, 400, 5, 0, 60, "弾を撃つ間隔（60フレーム＝約1秒）。"), ProjSpeed(0.5f), FfAttack()),
            G("予兆", "撃つ前の合図です。", WarnParams()));
        T(4, "歩いてくる", "プレイヤーに向かって歩きます。", TunePreview.Range,
            G("動き", null, MoveSpeed(0.35f, "歩く速さ"), TriggerRange(300, "気づく距離")));
        T(5, "追っかけてくる", "プレイヤーを追って、壁も飛び越えます。", TunePreview.Range,
            G("動き", null, MoveSpeed(0.55f, "走る速さ"), TriggerRange(300, "気づく距離"),
                N("jumpPowerMult", "壁を飛び越えるジャンプ力", "倍", 0.1f, 2, 0.05f, 2, 0.8f, "壁にぶつかったとき跳ぶ高さ。")));
        T(6, "突進", "近づくと、溜めてから一直線に突進します。", TunePreview.Timeline,
            G("見つける", null, TriggerRange(260, "突進を始める距離")),
            G("突進の流れ", "溜め → 突進 → 休み の順に繰り返します。",
                N("chargeTime", "溜めの長さ", "フレーム", 0, 200, 1, 0, 30, "突進する前に、構えている時間。長いほど読みやすくなります。"),
                N("dashSpeedMult", "突進の速さ", "倍", 0.2f, 5, 0.1f, 1, 1.5f, "突進の速さの倍率。"),
                N("dashDuration", "突進の長さ", "フレーム", 1, 200, 1, 0, 40, "突進している時間。速さと掛けると、進む距離になります。"),
                N("cooldownTime", "突進後の休み", "フレーム", 0, 300, 5, 0, 70, "突進のあと、次に動き出すまでの時間。")));
        T(7, "落下", "真下を通ると、予兆のあとに落ちてきます。", TunePreview.Timeline,
            G("見つける", null, TriggerRange(24, "真下とみなす幅")),
            G("落下の流れ", "予兆 → 落下 → 休み → もとの高さへ戻る の順です。",
                N("fallDelay", "落ちる前の予兆", "フレーム", 0, 120, 1, 0, 10, "見つけてから落ち始めるまでの時間（影が大きくなる）。"),
                N("cooldownTime", "着地後の休み", "フレーム", 0, 400, 5, 0, 120, "着地してから、戻り始めるまでの時間。"),
                N("riseSpeed", "元の高さへ戻る速さ", "px/フレーム", 0, 10, 0.1f, 1, 0, "0なら一瞬で戻ります。")),
            G("着地の迫力", null,
                N("shockwaveRadius", "着地の衝撃波の半径", "px", 0, 300, 5, 0, 60, "着地したとき、壊せるブロックを割る範囲。"),
                N("diagonalFallSpeed", "斜めに落ちるときの横の速さ", "px/フレーム", 0, 10, 0.1f, 1, 2.5f, "向きを反転すると斜めに落ちます。その横方向の速さ。"),
                N("fastForwardJitter", "早送り中の左右のブレ", "px", 0, 100, 1, 0, 30, "早送りしている間、落ちる位置が左右にずれる幅。")));
        T(8, "拡散弾", "弾を扇状（または全方向）にばらまきます。", TunePreview.Shot,
            G("撃ち方", null,
                N("actionInterval", "撃つ間隔", "フレーム", 10, 400, 5, 0, 150, "弾を撃つ間隔。"),
                N("spreadCount", "弾の本数", "本", 1, 24, 1, 0, 3, "一度に撃つ弾の数。"),
                N("spreadAngle", "扇の広がり", "ラジアン", 0, 3.14f, 0.05f, 2, 0.35f, "正面から左右にどれだけ広げるか。0.35で約20度。"),
                ProjSpeed(0.5f),
                Flag("radialFire", "全方向に撃つ", "ONにすると、扇ではなく円形に等間隔で撃ちます。")),
            G("回転", null,
                N("spreadRotationStep", "撃つたびに回す角度", "ラジアン", 0, 1.5f, 0.02f, 2, 0, "撃つたびに向きがこの角度ずつ回ります（うずまき状になる）。")));
        T(9, "照準弾", "撃つ瞬間のプレイヤーの位置を狙います。", TunePreview.Shot,
            G("撃ち方", null,
                N("actionInterval", "撃つ間隔", "フレーム", 10, 400, 5, 0, 130, "弾を撃つ間隔。"), ProjSpeed(0.55f),
                S(N("aimJitter", "狙いのブレ", "ラジアン", 0, 0.8f, 0.01f, 2, 0, "撃つたびに、狙いがこの範囲でランダムにずれます。0ならいつも正確。"), 0.15f)));
        T(10, "浮遊", "ふわふわ浮かびながら近づきます。", TunePreview.Wave,
            G("浮かび方", null,
                N("floatAmplitude", "上下のふれ幅", "px", 0, 200, 1, 0, 40, "どれだけ上下に浮き沈みするか。"),
                N("floatFrequency", "浮き沈みの速さ", "", 0.005f, 0.4f, 0.005f, 3, 0.05f, "大きいほど速く上下します。"),
                Choice("waveShape", "浮き沈みの形", "上下の動きの形。", WaveChoices, 0, "なめらか")),
            G("近づき方", null, MoveSpeed(0.2f, "近づく速さ"), TriggerRange(300),
                S(N("verticalTrackSpeed", "プレイヤーの高さに合わせる速さ", "px/フレーム", 0, 6, 0.1f, 1, 0, "0なら高さは変えません。大きいと、上下に逃げても追ってきます。"), 1.0f)));
        T(11, "瞬間移動", "プレイヤーの近くへ瞬間移動します。", TunePreview.Range,
            G("瞬間移動", null,
                N("actionInterval", "移動の間隔", "フレーム", 10, 600, 5, 0, 180, "瞬間移動する間隔。"),
                N("teleportRangeMin", "移動先の最短距離", "px", 0, 600, 5, 0, 120, "プレイヤーからこの距離より近くには現れません。"),
                N("teleportRangeMax", "移動先の最長距離", "px", 0, 800, 5, 0, 220, "プレイヤーからこの距離より遠くには現れません。")));
        T(12, "縮んで復活", "倒されると縮んで復活し、速くなります。", TunePreview.Range,
            G("動き", null, MoveSpeed(0.35f, "通常の速さ"),
                N("enragedMoveSpeed", "復活後の速さ", "倍", 0, 3, 0.05f, 2, 0.9f, "一度倒されて縮んだあとの速さ。"), TriggerRange(300)),
            G("縮み方", null, N("shrinkFactor", "縮む大きさ", "倍", 0.1f, 1, 0.05f, 2, 0.6f, "復活したときの大きさ。0.6なら6割の大きさ。")));
        T(13, "シールド", "一定のあいだ、攻撃が効かなくなります。", TunePreview.Timeline,
            G("シールドの周期", "無敵の時間と、攻撃が効く時間を交互に繰り返します。",
                N("shieldOnDuration", "無敵の長さ", "フレーム", 1, 600, 5, 0, 90, "弾が効かない時間。"),
                N("shieldOffDuration", "攻撃が効く長さ", "フレーム", 1, 600, 5, 0, 150, "弾が効く時間。")),
            G("動き", null, MoveSpeed(0.3f), TriggerRange(300)));
        T(14, "まぼろし", "プレイヤーの過去の動きを追いかけます。", TunePreview.Range,
            G("追いかけ方", null,
                N("mimicDelayFrames", "どれだけ遅れて真似るか", "フレーム", 10, 600, 5, 0, 90, "プレイヤーの動きを、この時間だけ遅れてなぞります。")));
        T(15, "大きさが変わる", "大きくなったり小さくなったりしながら近づきます。", TunePreview.Wave,
            G("大きさの変わり方", null,
                N("sizeAmplitude", "変わる幅", "", 0, 2, 0.05f, 2, 0.5f, "1に対して、±この割合だけ大きさが変わります。"),
                N("sizeFrequency", "変わる速さ", "", 0.005f, 0.4f, 0.005f, 3, 0.04f, "大きいほど速く変わります。"),
                N("minScale", "いちばん小さい大きさ", "倍", 0.1f, 1, 0.05f, 2, 0.4f, "これより小さくはなりません。"),
                S(N("maxScale", "いちばん大きい大きさ", "倍", 1, 5, 0.05f, 2, 0, "これより大きくはなりません。「既定」は制限なし。"), 1.5f),
                Choice("waveShape", "変わり方の形", "大きさの変わり方の形。", WaveChoices, 0, "なめらか")),
            G("動き", null, MoveSpeed(0.25f), TriggerRange(300)));
        T(16, "速さが変わる", "速くなったり遅くなったりしながら近づきます。", TunePreview.Wave,
            G("速さの変わり方", null,
                N("tempoMin", "いちばん遅いとき", "倍", 0, 3, 0.05f, 2, 0.3f, "速さの倍率の最小。"),
                N("tempoMax", "いちばん速いとき", "倍", 0, 4, 0.05f, 2, 1.6f, "速さの倍率の最大。"),
                N("tempoFrequency", "変わる速さ", "", 0.005f, 0.4f, 0.005f, 3, 0.05f, "大きいほど、速い・遅いの切り替えが細かくなります。"),
                Choice("waveShape", "変わり方の形", "速さの変わり方の形。", WaveChoices, 0, "なめらか")),
            G("動き", null, MoveSpeed(0.4f, "基準の速さ"), TriggerRange(300)));
        T(17, "暗転", "近づくと、画面が暗くなります。", TunePreview.Brightness,
            G("暗くする", null,
                N("effectRange", "効果が届く距離", "px", 20, 800, 10, 0, 320, "この距離まで近づくと暗くなり始めます。近いほど強く暗くなります。"),
                N("brightnessMin", "いちばん暗いときの明るさ", "", 0, 1, 0.01f, 2, 0.35f, "真横まで近づいたときの画面の明るさ。0なら真っ暗、1なら変わりません。")),
            G("反転・打ち消し", "プレイヤーの編集ツールとの関係です。",
                N("brightenMax", "反転したときの最大の明るさ", "", 1, 3, 0.05f, 2, 1.7f, "向きを反転すると、暗くするかわりに、この明るさまで明るくします。"),
                N("counterBrightness", "打ち消せる明るさ", "", 1, 3, 0.05f, 2, 1.3f, "プレイヤー自身が画面をこの明るさより明るくしている間は、暗転が効きません。")),
            G("動き", null, MoveSpeed(0.3f), TriggerRange(300)));
        T(18, "色変化", "近づくと、画面の色合いが変わります。", TunePreview.Tint,
            G("色を変える", null,
                N("effectRange", "効果が届く距離", "px", 20, 800, 10, 0, 320, "この距離まで近づくと色が変わり始めます。近いほど強く変わります。"),
                new TuneParam { Key = "tintColorR", Key2 = "tintColorG", Key3 = "tintColorB", Kind = TuneKind.Color, Label = "画面を寄せる色",
                    Hint = "画面がこの色へ寄っていきます。指定しないと、傾けるたびに赤→緑→青と切り替わります（ゲーム内の操作）。", DefaultText = "傾けで 赤→緑→青" },
                N("tintStrength", "色の強さ", "", 0, 1, 0.01f, 2, 0.6f, "真横まで近づいたとき、どれだけ指定の色に寄るか。0なら変わりません。")),
            G("打ち消し", "プレイヤーの色フィルタ（Tキー）との関係です。",
                Choice("counterFilter", "打ち消せる色フィルタ", "プレイヤーがこの色のフィルタを掛けている間は、色変化が効きません。",
                    new[] { "打ち消せない", "赤", "緑", "青" }, 1, "寄せる色と同じ（自動）")),
            G("動き", null, MoveSpeed(0.3f), TriggerRange(300)));
        T(19, "ズーム撹乱", "近づくと、画面が拡大・縮小を繰り返します。", TunePreview.Wave,
            G("ズームの揺れ", null,
                N("effectRange", "効果が届く距離", "px", 20, 800, 10, 0, 280, "この距離に入ると画面が揺れます。"),
                N("zoomAmplitude", "揺れの大きさ", "", 0, 1, 0.01f, 2, 0.25f, "画面がどれだけ拡大・縮小するか。0.25なら±25％。"),
                N("zoomFrequency", "揺れの速さ", "", 0.005f, 0.5f, 0.005f, 3, 0.08f, "大きいほど速く揺れます。"),
                Choice("waveShape", "揺れの形", "揺れ方の形。", WaveChoices, 0, "なめらか")));
        T(20, "カスタムスクリプト", "ブロックのスクリプトで動きを作る敵です。", TunePreview.Script);
        T(21, "飛びかかり", "地面を這って近づき、溜めてから飛びかかります。", TunePreview.Timeline,
            G("近づく", null, MoveSpeed(0.7f, "這う速さ"), TriggerRange(200, "飛びかかる距離")),
            G("飛びかかりの流れ", "追う → 溜め → 飛ぶ → 着地後 の順に繰り返します。",
                N("chargeTime", "溜めの長さ", "フレーム", 0, 200, 1, 0, 22, "飛ぶ前に、構えている時間。"),
                N("jumpPowerMult", "ジャンプ力", "倍", 0.1f, 3, 0.05f, 2, 1.0f, "飛び出しの高さ。"),
                N("dashSpeedMult", "前へ飛ぶ勢い", "倍", 0, 3, 0.05f, 2, 0.5f, "飛び出すときの前向きの速さ。"),
                N("dashDuration", "空中にいられる上限", "フレーム", 10, 400, 5, 0, 120, "これ以上は飛びっぱなしにならない安全装置。"),
                N("cooldownTime", "着地後の硬直", "フレーム", 0, 300, 5, 0, 45, "着地してから、また動き出すまでの時間。")));
        T(22, "はねまわる節足", "頭が跳ね回り、胴体の節がついてきます。", TunePreview.Range,
            G("頭の動き", null,
                N("bounceSpeed", "飛ぶ速さ", "px/フレーム", 0.2f, 12, 0.1f, 1, 3, "頭が壁や床で跳ね返りながら進む速さ。"),
                N("bounceRandomness", "跳ね返りの乱れ", "度", 0, 90, 1, 0, 25, "跳ね返るたびに、角度がこの範囲でランダムにずれます。")),
            G("胴体", null,
                N("segmentGap", "節と節の間隔", "px", 4, 80, 1, 0, 22, "胴体の節の間隔。大きいほど長く伸びます。")));
        return list;
    }
}
