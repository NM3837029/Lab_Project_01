using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartTemplatesScreen - 「画面や物を操る敵」の見た目の型（3つ）
//
// 【なぜ作ったか】
// 2026-10-15 の素材一新で、これまでの型（翼・尻尾・砲身・目…）では作れない動きの敵が入った。
//   ・ズーム操作をしてくる敵 …… 4方向の矢印が、中心へ向かって脈打つ（寄せている合図）
//   ・回転させる敵 ……………… 頭（輪）だけが、画像の中心ではなく「軸」を中心にぐるぐる回る
//   ・サイズを変える敵 ………… 4方向の矢印の十字を持っていて、投げる前に震える
// それぞれを、絵と数値を入れるだけで作れる型にする。敵のタイプ（19・24・25）と組み合わせて使う。
//
// 【作り方の約束】（PartTemplates.cs と同じ）
//   ・位置は「本体の左上を原点」「パーツの左上」で指定する。
//   ・飾りのパーツは当たり判定を2×2へ縮める（触れてもダメージを受けない）。
//   ・軸まわりの回転は、パーツの左上を「(I − Rot(φ))·v」だけずらして作る。
//     v は「パーツの中心から軸までのベクトル」。こうすると、軸の位置が動かないまま、絵が軸を中心に回る。
// ======================================================
public static partial class PartTemplates
{
    public const string CategoryScreen = "画面や物を操る敵の型";

    private static List<PartTemplate> ScreenTemplates() => new()
    {
        ZoomArrows(), PivotSpinner(), MountedEyes(), ThrowCross(),
    };

    // ── 1. 脈打つ矢印（ズーム操作の敵） ──
    private static PartTemplate ZoomArrows() => new()
    {
        Id = "zoom_arrows", Name = "🔍 脈打つ矢印（画面を寄せている合図）", Category = CategoryScreen,
        Origin = "enemy_zoom（ズーム操作をしてくる敵）",
        Description = "本体の中心に、4方向の矢印が重なります。ふだんはゆっくり脈打ち、画面を寄せているあいだ（行動中）は速く大きく脈打ちます。矢印の絵は、中心に向かって寄る形の絵を選んでください。",
        DefaultIdPrefix = "arrows",
        DefaultSprite = "img/ズーム操作をしてくる敵ズーム表現部分.png",
        Params =
        {
            Num("size", "矢印の大きさ", 16, 160, 56), Num("idle", "ふだんの脈打ち", 0, 0.5m, 0.1m, 0.05m, 2, "大きくなる割合（0.1なら10％）"),
            Num("idlePeriod", "ふだんの周期（フレーム）", 20, 300, 90, 10), Num("active", "寄せているときの脈打ち", 0, 1, 0.25m, 0.05m, 2),
            Num("activePeriod", "寄せているときの周期（フレーム）", 10, 200, 36, 2),
        },
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 19, TypeNote = "「ズーム固定」。近づくと、この敵を中心に画面が少しずつ寄っていき、この敵を倒すまで戻りません。",
            Hp = 4, Width = 46, Height = 46, BaseName = "ズーム固定の敵",
        },
        Build = v =>
        {
            double s = v.D("size");
            double cx = v.BodyW / 2.0 - s / 2.0, cy = v.BodyH / 2.0 - s / 2.0;
            // 矢印は本体の後ろ（z=-1）。脈打ちは「ふだん」と「行動中」の2つを重ねる
            var p = Deco($"{v.IdPrefix}", v.Sprite, cx, cy, s, s, -1, "", false,
                Mot("always", "pulse", v.D("idle"), v.D("idlePeriod")),
                Mot("active", "pulse", v.D("active"), v.D("activePeriod")));
            p.color = "";
            return new List<PartDef> { p };
        },
    };

    // ── 2. 軸まわりで回る部品（回転させる敵の頭） ──
    private static PartTemplate PivotSpinner() => new()
    {
        Id = "pivot_spinner", Name = "🌀 軸まわりで回る部品（頭・輪・ファン）", Category = CategoryScreen,
        Origin = "enemy_kaiten（回転させる敵）",
        Description = "1つの部品が、絵の中心ではなく「軸」を中心にぐるぐる回ります。軸は、部品の絵の中の位置（％）で決めます。たとえば、絵の上のほうに丸い頭がある絵なら、頭の中心を軸にすれば、頭だけがその場で回ります。",
        DefaultIdPrefix = "spinner",
        DefaultSprite = "img/回転させる敵回転する頭部.png",
        Params =
        {
            Num("size", "部品の大きさ", 12, 160, 46), Num("speed", "回る速さ（度/秒）", -720, 720, 140, 10, 0, "プラスで時計回り、マイナスで反時計回り"),
            Num("pivotX", "軸の位置・横（％）", 0, 100, 52, 1, 0, "絵の左端を0、右端を100として、回転の中心になる位置"),
            Num("pivotY", "軸の位置・縦（％）", 0, 100, 34, 1, 0, "絵の上端を0、下端を100"),
            Num("offsetX", "本体の中心からのずれ・横", -60, 60, 0), Num("offsetY", "本体の中心からのずれ・縦", -60, 60, 0),
        },
        PreviewNote = "回る向きと速さは、プレビューでそのまま確かめられます。",
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 24, TypeNote = "「回転させる」。近くにいるプレイヤー・ほかの敵・ギミック・アイテムを、ゆっくり回し続けます。",
            Hp = 3, Width = 46, Height = 46, BaseName = "回転させる敵",
        },
        Build = v =>
        {
            double s = v.D("size");
            double cx = v.BodyW / 2.0 + v.D("offsetX") - s / 2.0, cy = v.BodyH / 2.0 + v.D("offsetY") - s / 2.0; // 止まっているときの左上
            // 部品の中心から軸までのベクトル（部品の大きさ s を単位にした割合から）
            double vx = (v.D("pivotX") - 50.0) / 100.0 * s, vy = (v.D("pivotY") - 50.0) / 100.0 * s;
            // 角度 φ = 時間 × 速さ（度/秒 → ラジアン/フレーム）
            JToken phi = Mul(TimeOp(), V(v.D("speed") * D2R / 60.0));
            JToken lx = Add(V(cx), Sub(V(vx), Sub(Mul(V(vx), Cos(phi)), Mul(V(vy), Sin(phi)))));
            JToken ly = Add(V(cy), Sub(V(vy), Add(Mul(V(vx), Sin(phi)), Mul(V(vy), Cos(phi)))));
            var p = Part(v.IdPrefix, v.Sprite, cx, cy, s, s, 0, 1, false, Loop(SetAngle(phi), SetOffset(lx, ly)));
            p.hitboxWidth = 2; p.hitboxHeight = 2; p.hitboxOffsetX = (p.width - 2) / 2; p.hitboxOffsetY = (p.height - 2) / 2;
            return new List<PartDef> { p };
        },
    };

    // ── 2b. 回る部品に載った黒目 ──
    private static PartTemplate MountedEyes() => new()
    {
        Id = "mounted_eyes", Name = "👁 回る頭に載った黒目（プレイヤーを見ながら一緒に回る）", Category = CategoryScreen,
        Origin = "enemy_kaiten（回転させる敵の黒目）",
        Description = "「軸まわりで回る部品」（回る頭など）の目の穴に載った黒目です。頭と同じ軸・同じ速さで一緒に回りながら、プレイヤーのいる向きへ少しずれます。頭だけが回って、黒目が穴から取り残されるのを防ぎます。軸・速さ・ずれは、載せる部品の値と同じにしてください。",
        DefaultIdPrefix = "pupils",
        DefaultSprite = "img/回転させる敵目.png",
        Params =
        {
            Num("size", "黒目の絵の大きさ", 12, 160, 46, 1, 0, "載せる部品（頭）と同じ大きさにすると、絵がぴったり重なります"),
            Num("speed", "回る速さ（度/秒）", -720, 720, 140, 10, 0, "載せる部品の速さと同じにする"),
            Num("pivotX", "軸の位置・横（％）", 0, 100, 52, 1, 0, "載せる部品の軸と同じにする"),
            Num("pivotY", "軸の位置・縦（％）", 0, 100, 34, 1, 0, "載せる部品の軸と同じにする"),
            Num("offsetX", "本体の中心からのずれ・横", -60, 60, 0), Num("offsetY", "本体の中心からのずれ・縦", -60, 60, 0),
            Num("reach", "プレイヤーのほうへずれる幅（px）", 0, 14, 3, 0.5m, 1),
        },
        PreviewNote = "プレイヤーのいる向きは、プレビューでは左右に振って見せています。",
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 24, TypeNote = "「回転させる」。近くにいるプレイヤー・ほかの敵・ギミック・アイテムを、ゆっくり回し続けます。",
            Hp = 3, Width = 46, Height = 46, BaseName = "回転させる敵",
        },
        Build = v =>
        {
            double s = v.D("size");
            double cx = v.BodyW / 2.0 + v.D("offsetX") - s / 2.0, cy = v.BodyH / 2.0 + v.D("offsetY") - s / 2.0;
            double vx = (v.D("pivotX") - 50.0) / 100.0 * s, vy = (v.D("pivotY") - 50.0) / 100.0 * s;
            JToken phi = Mul(TimeOp(), V(v.D("speed") * D2R / 60.0));
            JToken a = Sub(ToPlayer(), Tilt()); // 親が傾いていても、プレイヤーの方向を向く
            JToken lx = Add(Add(V(cx), Sub(V(vx), Sub(Mul(V(vx), Cos(phi)), Mul(V(vy), Sin(phi))))), Mul(Cos(a), V(v.D("reach"))));
            JToken ly = Add(Add(V(cy), Sub(V(vy), Add(Mul(V(vx), Sin(phi)), Mul(V(vy), Cos(phi))))), Mul(Sin(a), V(v.D("reach"))));
            var p = Part(v.IdPrefix, v.Sprite, cx, cy, s, s, 0, 2, false, Loop(SetAngle(phi), SetOffset(lx, ly)));
            p.hitboxWidth = 2; p.hitboxHeight = 2; p.hitboxOffsetX = (p.width - 2) / 2; p.hitboxOffsetY = (p.height - 2) / 2;
            return new List<PartDef> { p };
        },
    };

    // ── 3. 持っている十字（サイズを変える敵） ──
    private static PartTemplate ThrowCross() => new()
    {
        Id = "throw_cross", Name = "✚ 持っている十字（投げる前に震える）", Category = CategoryScreen,
        Origin = "enemy_size（範囲サイズ調整の敵）",
        Description = "本体の後ろに、4方向の矢印の十字を持たせます。ふだんはゆっくり脈打ち、投げる前の予兆（溜め）のあいだは、ぶるぶる震えます。実際に投げる物と範囲は、敵のタイプ「範囲サイズ変更」が作ります。",
        DefaultIdPrefix = "cross",
        DefaultSprite = "img/投げた場所の特定範囲のサイズ調整を行う敵投げるもの.png",
        Params =
        {
            Num("size", "十字の大きさ", 16, 200, 69), Num("shake", "予兆の震え（px）", 0, 12, 3, 0.5m, 1),
            Num("pulse", "ふだんの脈打ち", 0, 0.3m, 0.05m, 0.01m, 2), Num("offsetY", "本体の中心からの高さのずれ", -60, 60, 0),
        },
        Suggest = v => new EnemySuggestion
        {
            TypeEnum = 25, TypeNote = "「範囲サイズ変更」。近づくと警告音のあと十字を投げ、着地した範囲の中の物を、投げるたびに交互に拡大・縮小します。",
            Hp = 3, Width = 34, Height = 34, BaseName = "範囲サイズ調整の敵",
        },
        Build = v =>
        {
            double s = v.D("size");
            double cx = v.BodyW / 2.0 - s / 2.0, cy = v.BodyH / 2.0 + v.D("offsetY") - s / 2.0;
            var p = Deco(v.IdPrefix, v.Sprite, cx, cy, s, s, -1, "", false,
                Mot("charge", "shake", v.D("shake")),
                Mot("always", "pulse", v.D("pulse"), 120));
            p.color = "";
            return new List<PartDef> { p };
        },
    };
}
