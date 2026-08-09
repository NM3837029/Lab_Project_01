using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockPaletteControl - 全命令ブロックをカテゴリ別に一覧表示するパレット
// Feature: Puzzle-like Behavior Scripting (M4/M5)
// M5でドラッグ開始（パレット→キャンバスへのブロック生成）に対応した。
// ======================================================
public class BlockPaletteControl : Panel
{
    private readonly Font _font = new Font("Meiryo UI", 9f);
    private readonly List<(BlockCategory cat, List<BlockInstance> items)> _groups = new();

    // Feature: UI改善（提案書 BS-1）— ブロック名だけでは初めて見る人には意味が分かりにくいため、
    // マウスホバー時に「何をするブロックか」を平易な言葉で説明するツールチップを表示する。
    private readonly ToolTip _tooltip = new ToolTip { InitialDelay = 400, ReshowDelay = 150, AutoPopDelay = 20000 };
    private BlockInstance? _hoveredItem;

    // Feature: UI改善（提案書 CUT-6）— 約55種のブロックをスクロールして探す負担を減らすため、
    // 名前(表示名/内部op名)で絞り込める検索を追加する。
    private string _filterQuery = "";

    public void SetFilter(string query)
    {
        _filterQuery = query?.Trim() ?? "";
        Invalidate();
    }

    private bool MatchesFilter(BlockInstance item) =>
        string.IsNullOrEmpty(_filterQuery) ||
        item.Def.DisplayName.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase) ||
        item.Def.Op.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["OnSpawn"] = "この敵/ギミック/パーツが出現した瞬間に1回だけ実行される「開始地点」です。動きの設定はここから始めます。",
        ["OnDamaged"] = "プレイヤーの弾などでダメージを受けた瞬間に実行されます。被弾時の演出（点滅など）に使います。",
        ["OnDeath"] = "HPが0になって倒された瞬間に実行されます。爆発演出やアイテムのドロップなどに使います。",
        ["Forever"] = "中に入れたブロックを、ゲームが続く限りずっと繰り返します。「常に動き続ける処理」の外枠として使います。",
        ["Repeat"] = "中に入れたブロックを、指定した回数だけ繰り返して実行します。",
        ["RepeatUntil"] = "指定した条件が真になるまで、中のブロックを繰り返し実行します。",
        ["If"] = "指定した条件が真のときだけ、中のブロックを実行します。",
        ["IfElse"] = "条件が真なら上側、偽なら「でなければ」側のブロックを実行します。二択の分岐に使います。",
        ["Wait"] = "指定したフレーム数（1秒=60フレーム）だけ、次の処理に進むのを待ちます。",
        ["WaitUntil"] = "指定した条件が真になるまで、次の処理に進むのを待ちます。",

        ["MoveDirection"] = "指定した方向（左/右/プレイヤー方向/プレイヤーと逆方向）へ、指定した速さで移動します。Foreverの中で使うのが基本です。",
        ["ApplyImpulse"] = "横方向・縦方向の速度を直接指定します。マイナスの縦速度で上方向に動きます（ジャンプの再現などに）。",
        ["SetPosition"] = "座標を指定した位置へ直接ワープさせます。",
        ["OffsetPosition"] = "現在位置から指定した分だけ相対的に移動します。",
        ["FaceTowards"] = "プレイヤーがいる方向へ向きを変えます（見た目の左右反転などに影響します）。",
        ["Oscillate"] = "Y座標を最小値〜最大値の間で、指定した周期(フレーム数)でゆっくり往復させます。動く足場に使います。min/maxは絶対座標（画面上の実際の高さ）です。",
        ["SetLocalOffset"] = "複合パーツ専用：本体（親）から見た相対位置(dx,dy)を指定します。",
        ["SetLocalOffsetPolar"] = "複合パーツ専用：本体（親）を中心とした角度と半径で相対位置を指定します。角度をTimeで変化させると回転する動きになります。",
        ["SetAngle"] = "見た目の回転角（ラジアン）を指定します。当たり判定の形自体は回転しません。",

        ["Shoot"] = "指定した角度・速さ・威力で弾を1発発射します。",
        ["ShootAtPlayer"] = "プレイヤーがいる方向へ自動で狙いを定めて弾を1発発射します。",
        ["SetInvincible"] = "無敵状態のON/OFFを切り替えます。ONの間はダメージを受けません。",
        ["SetScale"] = "表示サイズの倍率を変更します（1=等倍、2=2倍の大きさ）。",
        ["SetVisualEffect"] = "画面全体に明るさ変化やズームなどの演出をかけます。",
        ["PlaySound"] = "指定したSE(効果音)IDのサウンドを再生します。",

        ["SetVar"] = "指定した名前の変数の値を、指定した値に設定します。",
        ["ChangeVar"] = "指定した名前の変数の値に、指定した数だけ加算（マイナスの値なら減算）します。",
        ["GetVar"] = "指定した名前の変数の、現在の値を取り出します（他のブロックの数値欄にはめ込んで使います）。",

        ["SelfX"] = "自分自身の現在のX座標（横位置）です。",
        ["SelfY"] = "自分自身の現在のY座標（縦位置）です。",
        ["PlayerX"] = "プレイヤーの現在のX座標です。",
        ["PlayerY"] = "プレイヤーの現在のY座標です。",
        ["DistanceToPlayer"] = "自分からプレイヤーまでの距離(px)です。「近づいたら◯◯する」といった条件に使えます。",
        ["DirectionToPlayer"] = "自分から見たプレイヤーの方向（ラジアン）です。狙い撃ちなどに使えます。",
        ["Random"] = "指定した最小値〜最大値の間からランダムな数値を1つ選びます。",
        ["Time"] = "ゲーム開始からの経過フレーム数です。Sin/Cosや掛け算と組み合わせると、回転・振動のような周期的な動きを作れます。",
        ["ParentX"] = "複合パーツ専用：自分の本体（親オブジェクト）の現在のX座標です。",
        ["ParentY"] = "複合パーツ専用：自分の本体（親オブジェクト）の現在のY座標です。",
        ["PartIndex"] = "複合パーツ専用：自分が何番目のパーツか（0始まりの通し番号）です。同じスクリプトを全パーツで共有しつつ、この値でパーツごとに異なる動きをさせられます。",

        ["IsGrounded"] = "現在、地面に接地しているかどうか（true/false）です。",
        ["IsWallAhead"] = "進行方向のすぐ先に壁があるかどうかです。パトロールの折り返し判定などに使えます。",
        ["IsGroundAhead"] = "進行方向の足元に地面があるかどうかです。崖から落ちない敵の判定などに使えます。",

        ["Const"] = "決まった1つの数値です。他のブロックの数値欄にそのまま数字を入れる代わりに使います。",
        ["Add"] = "2つの数を足し算した結果です。",
        ["Sub"] = "2つの数を引き算した結果です（a−b）。",
        ["Mul"] = "2つの数を掛け算した結果です。",
        ["Div"] = "2つの数を割り算した結果です（a÷b）。",
        ["Sin"] = "サイン（正弦）の計算結果です。Timeと組み合わせると-1〜1の間を滑らかに往復する値になり、揺れや波打つ動きに使えます。",
        ["Cos"] = "コサイン（余弦）の計算結果です。Sinと90度ずれた波形で、回転運動のX/Y座標の計算によく使います。",

        ["Gt"] = "aがbより大きいかどうか（真偽値）です。",
        ["Lt"] = "aがbより小さいかどうか（真偽値）です。",
        ["Eq"] = "aとbが等しいかどうか（真偽値）です。",
        ["And"] = "2つの条件が両方とも真の場合にのみ真になります。",
        ["Or"] = "2つの条件のどちらか一方でも真なら真になります。",
        ["Not"] = "条件の真偽を反転させます。",
    };

    public BlockPaletteControl()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(235, 235, 240);
        BuildItems();
    }

    // クリックされた位置にあるパレット項目のBlockDefを探し、ドラッグ操作を開始する。
    // ドロップ先(BlockCanvasControl)ではBlockDefを受け取って新規BlockInstanceを生成する。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var contentPt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        foreach (var (_, items) in _groups)
        {
            foreach (var item in items)
            {
                if (!MatchesFilter(item)) continue;
                var rect = new Rectangle(item.X, item.Y, item.Width, item.Height);
                if (rect.Contains(contentPt))
                {
                    DoDragDrop(new BlockDragPayload(item.Def), DragDropEffects.Copy);
                    return;
                }
            }
        }
    }

    private BlockInstance? FindItemAt(Point contentPt)
    {
        foreach (var (_, items) in _groups)
        {
            foreach (var item in items)
            {
                if (!MatchesFilter(item)) continue;
                var rect = new Rectangle(item.X, item.Y, item.Width, item.Height);
                if (rect.Contains(contentPt)) return item;
            }
        }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var contentPt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        var found = FindItemAt(contentPt);
        if (ReferenceEquals(found, _hoveredItem)) return;
        _hoveredItem = found;
        if (found != null && Descriptions.TryGetValue(found.Def.Op, out var desc))
            _tooltip.Show(desc, this, e.X + 16, e.Y + 16, 20000);
        else
            _tooltip.Hide(this);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredItem = null;
        _tooltip.Hide(this);
    }

    private void BuildItems()
    {
        foreach (BlockCategory cat in Enum.GetValues(typeof(BlockCategory)))
        {
            var defs = BlockCatalog.ByCategory(cat).ToList();
            if (defs.Count == 0) continue;
            _groups.Add((cat, defs.Select(BlockInstance.Create).ToList()));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        BlockRenderer.CanvasBackColor = BackColor;

        int x = 10;
        int y = 10;
        using var catFont = new Font(_font, FontStyle.Bold);

        bool anyVisible = false;
        foreach (var (cat, items) in _groups)
        {
            var visible = items.Where(MatchesFilter).ToList();
            if (visible.Count == 0) continue;
            anyVisible = true;

            g.DrawString(CategoryLabel(cat), catFont, Brushes.Black, x, y);
            y += 22;
            foreach (var item in visible)
            {
                BlockLayout.Measure(item, g, _font);
                item.X = x;
                item.Y = y;
                BlockRenderer.Draw(g, item, _font);
                y += item.Height + 8;
            }
            y += 16;
        }

        if (!anyVisible && !string.IsNullOrEmpty(_filterQuery))
            g.DrawString($"「{_filterQuery}」に一致するブロックがありません", _font, Brushes.Gray, x, y);

        int neededHeight = y + 10;
        if (AutoScrollMinSize.Height != neededHeight)
            AutoScrollMinSize = new Size(230, neededHeight);
    }

    private static string CategoryLabel(BlockCategory cat) => cat switch
    {
        BlockCategory.Control => "■ 制御",
        BlockCategory.Motion => "■ 動き",
        BlockCategory.Sensing => "■ センシング",
        BlockCategory.Combat => "■ 攻撃・演出",
        BlockCategory.Variables => "■ 変数",
        BlockCategory.Operators => "■ 演算子",
        _ => cat.ToString(),
    };
}
