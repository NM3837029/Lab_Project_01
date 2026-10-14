namespace Lab_Editor;

// ======================================================
// EnemyPresets - 色・明るさに関わる敵の「設定済みプリセット」
//
// 色を寄せる敵（色変化）・画面を暗くする敵（暗転）・行動を切り替える敵（多彩行動）は、
// タイプ・範囲・色・打ち消せる条件など決める項目が多い。ここでは「こういう敵」という完成形を
// 設定済みの値と、色・明るさの複合パーツ（PartTemplatesColor.cs）つきでまとめて用意する。
// 追加後は、普通の敵と同じように、各項目・パーツ・行動を自由に直せる。
//
// 値はすべて EnemyDef の項目（-1 は「既定」）で、ここで決めた以外の項目は既定のままになる。
// ======================================================
public sealed class EnemyPreset : PresetInfo
{
    public int TypeEnum;
    public int Hp = 3, Width = 40, Height = 40;
    public string BaseName = "";
    // 付ける複合パーツの型（PartTemplates の Id。無ければパーツなし）と、その型の数値の上書き
    public string TemplateId = "";
    public Dictionary<string, decimal> TemplateParams = new();
    // 敵の定義へ、このプリセットの値を書き込む
    public Action<EnemyDef> Apply = _ => { };
}

public static class EnemyPresets
{
    public const string GroupDark = "画面を暗くする敵";
    public const string GroupTint = "画面に色をつける敵";
    public const string GroupMulti = "いろいろな行動をする敵";
    public const string GroupOther = "光る敵";
    public const string GroupScreen = "画面や物を操る敵";

    public static readonly List<EnemyPreset> All = BuildAll();

    // 色変化の敵：寄せる色(0-255)と、打ち消せるフィルタ(0=なし 1=赤 2=緑 3=青)を決める
    private static Action<EnemyDef> Dyer(float r, float g, float b, float strength, float counter, float range = 340f, float speed = 0.3f)
        => d =>
        {
            d.tintColorR = r; d.tintColorG = g; d.tintColorB = b;
            d.tintStrength = strength; d.counterFilter = counter;
            d.effectRange = range; d.triggerRange = range - 20f; d.moveSpeed = speed;
        };

    private static EnemyAction Act(string kind, Action<EnemyAction>? tweak = null)
    {
        var a = EnemyActionInfo.CreateDefault(kind);
        tweak?.Invoke(a);
        return a;
    }

    private static List<EnemyPreset> BuildAll() => new()
    {
        // ───────────── 画面を暗くする敵（タイプ17）─────────────
        new EnemyPreset
        {
            Id = "night_watch", Group = GroupDark, Name = "🌑 夜の番人（ほぼ暗闇にする）",
            Description = "近づくと画面をほとんど真っ暗にします。画面の明るさを 115% 以上にすると打ち消せるので、カラー補正パネルの「明るさ」を少し上げるのが答えです。月が満ち欠けする飾りつき。",
            Notes = "打ち消す条件は「明るさ 115% 以上」。敵の動きを調整（🎛）で「打ち消せる明るさ」を変えられます。",
            TypeEnum = 17, Hp = 3, Width = 40, Height = 40, BaseName = "夜の番人", TemplateId = "moon_phase",
            Apply = d => { d.moveSpeed = 0.25f; d.effectRange = 400f; d.brightnessMin = 0.2f; d.triggerRange = 340f; d.counterBrightness = 1.15f; },
        },
        new EnemyPreset
        {
            Id = "dark_lord", Group = GroupDark, Name = "🕳 真っ暗闇の主（強い暗転）",
            Description = "ほぼ何も見えなくなるほど暗くします（明るさ5%）。打ち消すには画面の明るさを 145% まで上げる必要があるので、編集コストをたくさん使います。範囲は狭く、動きはゆっくりです。",
            Notes = "打ち消す条件は「明るさ 145% 以上」。難しめのステージ向けです。",
            TypeEnum = 17, Hp = 4, Width = 44, Height = 44, BaseName = "暗闇の主", TemplateId = "glow_halo",
            TemplateParams = { ["hue"] = 260m, ["sat"] = 50m, ["glow"] = 0.3m, ["radius"] = 70m },
            Apply = d => { d.moveSpeed = 0.15f; d.effectRange = 260f; d.brightnessMin = 0.05f; d.triggerRange = 240f; d.counterBrightness = 1.45f; },
        },
        new EnemyPreset
        {
            Id = "shy_dimmer", Group = GroupDark, Name = "🌘 おだやかな暗がり（入門向け）",
            Description = "画面を少し暗くするだけの、やさしい敵です（明るさ40%まで）。明るさを 110% にするだけで打ち消せます。明るさスライダーの使い方を覚える最初の敵に向きます。目盛りの飾りつき。",
            TypeEnum = 17, Hp = 2, Width = 36, Height = 36, BaseName = "暗がりの敵", TemplateId = "exposure_meter",
            Apply = d => { d.moveSpeed = 0.4f; d.effectRange = 300f; d.brightnessMin = 0.4f; d.triggerRange = 280f; d.counterBrightness = 1.1f; },
        },

        // ───────────── 画面に色をつける敵（タイプ18）─────────────
        new EnemyPreset
        {
            Id = "red_dyer", Group = GroupTint, Name = "🔴 赤の染め屋（画面を赤く染める）",
            Description = "近づくと画面を赤く染めます。色の輪を「赤」にして赤フィルタをかけると打ち消せます（T キーや、パネルの「赤」ボタンでも）。",
            Notes = "打ち消せるのは「赤フィルタ」。",
            TypeEnum = 18, Hp = 3, Width = 40, Height = 40, BaseName = "赤の染め屋", TemplateId = "glow_halo",
            TemplateParams = { ["hue"] = 0m, ["sat"] = 80m },
            Apply = Dyer(255, 40, 40, 0.75f, 1),
        },
        new EnemyPreset
        {
            Id = "green_dyer", Group = GroupTint, Name = "🟢 緑の染め屋（画面を緑に染める）",
            Description = "近づくと画面を緑に染めます。色の輪を「緑」にすると打ち消せます。",
            Notes = "打ち消せるのは「緑フィルタ」。",
            TypeEnum = 18, Hp = 3, Width = 40, Height = 40, BaseName = "緑の染め屋", TemplateId = "glow_halo",
            TemplateParams = { ["hue"] = 120m, ["sat"] = 80m },
            Apply = Dyer(40, 255, 40, 0.75f, 2),
        },
        new EnemyPreset
        {
            Id = "blue_dyer", Group = GroupTint, Name = "🔵 青の染め屋（画面を青に染める）",
            Description = "近づくと画面を青に染めます。色の輪を「青」にすると打ち消せます。",
            Notes = "打ち消せるのは「青フィルタ」。",
            TypeEnum = 18, Hp = 3, Width = 40, Height = 40, BaseName = "青の染め屋", TemplateId = "glow_halo",
            TemplateParams = { ["hue"] = 230m, ["sat"] = 80m },
            Apply = Dyer(60, 100, 255, 0.75f, 3),
        },
        new EnemyPreset
        {
            Id = "purple_dyer", Group = GroupTint, Name = "🟣 紫の染め屋（青で打ち消せる）",
            Description = "近づくと画面を紫に染めます。紫は赤と青の混ざった色なので、打ち消せるのは「青フィルタ」だけにしてあります。色の輪の意味を知るためのひっかけの敵です。",
            Notes = "打ち消せるのは「青フィルタ」（赤では打ち消せません）。",
            TypeEnum = 18, Hp = 3, Width = 40, Height = 40, BaseName = "紫の染め屋", TemplateId = "glow_halo",
            TemplateParams = { ["hue"] = 280m, ["sat"] = 70m },
            Apply = Dyer(200, 60, 255, 0.7f, 3),
        },
        new EnemyPreset
        {
            Id = "rgb_dyer", Group = GroupTint, Name = "🔴🟢🔵 三原色の染め屋（傾けると色が変わる）",
            Description = "赤・緑・青の光球がまわる染め屋です。色は特に指定せず、ふだんは赤で染めます。プレイヤーが傾けると、染める色が赤→緑→青と切り替わります（打ち消す色も一緒に変わります）。",
            Notes = "染める色を指定していないので、「敵の動きを調整」で色を決めると固定の色になります。",
            TypeEnum = 18, Hp = 4, Width = 40, Height = 40, BaseName = "三原色の染め屋", TemplateId = "rgb_orbs",
            Apply = d => { d.tintStrength = 0.65f; d.effectRange = 340f; d.triggerRange = 320f; d.moveSpeed = 0.3f; },
        },

        // ───────────── いろいろな行動をする敵（タイプ23 多彩行動）─────────────
        new EnemyPreset
        {
            Id = "color_mage", Group = GroupMulti, Name = "🎭 色彩の魔術師（色・暗さ・彩度を次々変える）",
            Description = "赤く染める → 暗くする → 白黒にする → 青く染める → 色相をずらす、を順番に繰り返します。間に少し歩いて近づきます。カラー補正パネルのスライダーを全部使わされる、総合テストのような敵です。虹色の輪の飾りつき。",
            Notes = "行動の長さ・強さは「🎛 敵の動きを調整」の行動リストで直せます。",
            TypeEnum = 23, Hp = 6, Width = 44, Height = 44, BaseName = "色彩の魔術師", TemplateId = "hue_ring",
            Apply = d =>
            {
                d.actionMode = "sequence";
                d.actions = new List<EnemyAction>
                {
                    Act("tint", a => { a.warn = 25; a.duration = 120; a.range = 340; a.level = 0.65f; a.colorR = 255; a.colorG = 50; a.colorB = 50; a.recover = 25; }),
                    Act("walk", a => { a.warn = 0; a.duration = 50; a.speed = 0.45f; a.recover = 5; }),
                    Act("darken", a => { a.warn = 20; a.duration = 100; a.range = 300; a.level = 0.3f; a.recover = 25; }),
                    Act("walk", a => { a.warn = 0; a.duration = 50; a.speed = 0.45f; a.recover = 5; }),
                    Act("grade", a => { a.warn = 20; a.duration = 140; a.range = 320; a.level = 0.0f; a.hue = 0; a.contrast = 0.3f; a.recover = 25; }),
                    Act("tint", a => { a.warn = 25; a.duration = 120; a.range = 340; a.level = 0.65f; a.colorR = 60; a.colorG = 100; a.colorB = 255; a.recover = 25; }),
                    Act("grade", a => { a.warn = 20; a.duration = 120; a.range = 320; a.level = 1.4f; a.hue = 150; a.contrast = 0; a.recover = 40; }),
                };
            },
        },
        new EnemyPreset
        {
            Id = "glitch_ghost", Group = GroupMulti, Name = "📺 ノイズの怪人（色がぶれる・突進してくる）",
            Description = "色相をぐるりとずらして画面をおかしくし、そのあと突進してきます。白黒でコントラストの強い画面にしてから弾を撃つこともあります。体は赤・緑・青にずれて見えます（色ずれの飾り）。",
            Notes = "パーツの画像は本体の絵を使います。本体の画像を選んでください。",
            TypeEnum = 23, Hp = 5, Width = 40, Height = 40, BaseName = "ノイズの怪人", TemplateId = "chroma_split",
            Apply = d =>
            {
                d.actionMode = "random";
                d.actions = new List<EnemyAction>
                {
                    Act("grade", a => { a.warn = 15; a.duration = 90; a.range = 340; a.level = 1.6f; a.hue = 180; a.contrast = 0.4f; a.recover = 20; }),
                    Act("dash", a => { a.warn = 30; a.duration = 25; a.speed = 1.2f; a.recover = 40; }),
                    Act("grade", a => { a.warn = 15; a.duration = 80; a.range = 340; a.level = 0.0f; a.hue = 0; a.contrast = 0.6f; a.recover = 20; }),
                    Act("shoot", a => { a.warn = 20; a.count = 3; a.angle = 0.3f; a.speed = 0.5f; a.recover = 30; }),
                    Act("wait", a => { a.duration = 40; }),
                };
            },
        },

        // ───────────── 画面や物を操る敵（タイプ19・24・25）─────────────
        new EnemyPreset
        {
            Id = "zoom_lock", Group = GroupScreen, Name = "🔍 ズーム固定の敵（倒すまで画面が寄ったまま）",
            Description = "近づくと、この敵を中心に画面が少しずつ寄っていきます。離れても戻らず、この敵を倒したときだけ解けます。寄りきるまで3秒。プレイヤーが画面の外に出ないよう、映す範囲は自動でずれます。矢印が脈打つ飾りつき。",
            Notes = "プレイヤーのZキーのズームでは打ち消せません。倒す方法を用意しておいてください（弾が撃てる能力など）。",
            TypeEnum = 19, Hp = 4, Width = 46, Height = 46, BaseName = "ズーム固定の敵", TemplateId = "zoom_arrows",
            Apply = d => { d.effectRange = 300f; d.zoomAmplitude = 1.0f; d.zoomLockFrames = 180f; },
        },
        new EnemyPreset
        {
            Id = "spinner", Group = GroupScreen, Name = "🌀 回転させる敵（近くの物を回し続ける）",
            Description = "近くにいるプレイヤー・ほかの敵・ギミック・アイテムを、ゆっくり回し続けます。回った角度は残るので、編集の「回転」か巻き戻しで直します。頭の輪が、その場でぐるぐる回る飾りつき。",
            Notes = "回す相手と速さは「🎛 敵の動きを調整」で変えられます。「回転を禁止」された物は回りません。",
            TypeEnum = 24, Hp = 3, Width = 46, Height = 46, BaseName = "回転させる敵", TemplateId = "pivot_spinner",
            Apply = d => { d.effectRange = 240f; d.rotateSpeed = 60f; },
        },
        new EnemyPreset
        {
            Id = "size_thrower", Group = GroupScreen, Name = "✚ 範囲サイズ調整の敵（投げた先で拡大⇔縮小）",
            Description = "近づくと、警告音のあと4方向の矢印の十字を投げます。着地した場所に円形の範囲ができ、中の物の大きさを、投げるたびに交互に拡大・縮小します。投げる前は、持っている十字が震えます。",
            Notes = "変わった大きさは、範囲が消えても残ります（編集の「拡大縮小」か巻き戻しで直せます）。範囲の大きさ・残る時間・倍率は「🎛 敵の動きを調整」で変えられます。",
            TypeEnum = 25, Hp = 3, Width = 34, Height = 34, BaseName = "範囲サイズ調整の敵", TemplateId = "throw_cross",
            Apply = d => { d.triggerRange = 340f; d.actionInterval = 300f; d.chargeTime = 40f; d.effectRange = 110f; d.zoneFrames = 360f; d.sizeFactor = 1.8f; },
        },

        // ───────────── 光る敵 ─────────────
        new EnemyPreset
        {
            Id = "flash_turret", Group = GroupOther, Name = "📸 フラッシュ砲台（溜めて、光って、撃つ）",
            Description = "狙いを定めて撃つ砲台です。撃つ前に画面が少しズームして（予兆）、体が明滅しながら強く光り、撃った瞬間にぱっと閃光します。予兆が長めなので、撃つタイミングが見切りやすくなっています。",
            Notes = "予兆の長さ（45フレーム）とズーム量は「🎛 敵の動きを調整」で変えられます。",
            TypeEnum = 9, Hp = 3, Width = 36, Height = 36, BaseName = "フラッシュ砲台", TemplateId = "camera_flash",
            Apply = d => { d.chargeWarnFrames = 45f; d.chargeWarnZoom = 0.1f; },
        },
        new EnemyPreset
        {
            Id = "rainbow_floater", Group = GroupOther, Name = "🌈 虹の輪の浮遊敵（ふわふわ近づく）",
            Description = "重力を受けず、ふわふわ浮かんでゆっくり近づく敵です。まわりを虹色の光球の輪が回っていて、色もゆっくり変わります。見た目重視の飾りの敵として使えます。",
            TypeEnum = 10, Hp = 2, Width = 32, Height = 32, BaseName = "虹の輪の敵", TemplateId = "hue_ring",
        },
    };
}
