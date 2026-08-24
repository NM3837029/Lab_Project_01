using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockPaletteControl - 全命令ブロックをカテゴリ別に一覧表示するパレット
// Feature: Puzzle-like Behavior Scripting (M4/M5)
// M5でドラッグ開始（パレット→キャンバスへのブロック生成）に対応した。
//
// カテゴリ（制御/動き/センシング/攻撃・演出/変数/演算子）ごとにブロックをグループ化して
// 縦に並べて表示するだけのシンプルなパネル。実際の見た目の描画はBlockRenderer.Drawに委譲しており、
// このクラス自身はレイアウト計算・マウス操作（クリックでドラッグ開始／ホバーでツールチップ）・
// 検索フィルタの3つの責務のみを持つ。
// ======================================================
public class BlockPaletteControl : Panel
{
    // パレット上の文字を描画する際に使うフォント。ブロック本体の描画自体はBlockRenderer側で行うが、
    // カテゴリ見出しやブロックラベルの計測（BlockLayout.Measure）にもこのフォントを使い回す。
    private readonly Font _font = new Font("Meiryo UI", 9f);

    // カテゴリごとに「そのカテゴリに属するブロックのインスタンス一覧」をまとめたリスト。
    // BuildItems()で一度だけ構築し、以降はOnPaint/OnMouseDown/OnMouseMoveなどで読み取り専用に使う。
    // BlockInstanceはパレット上での表示用インスタンスであり、実際にキャンバスへドラッグされた際は
    // 別途新規のBlockInstanceが生成される（パレット側のインスタンスはあくまで見本）。
    private readonly List<(BlockCategory cat, List<BlockInstance> items)> _groups = new();

    // Feature: UI改善（提案書 BS-1）— ブロック名だけでは初めて見る人には意味が分かりにくいため、
    // マウスホバー時に「何をするブロックか」を平易な言葉で説明するツールチップを表示する。
    // 表示までの遅延(ms)・再表示までの間隔(ms)・自動で消えるまでの時間(ms)を指定したツールチップ。
    // InitialDelayを400msにすることで、単にマウスを通過させただけでは出ず、少し止まったときだけ出るようにしている。
    private readonly ToolTip _tooltip = new ToolTip { InitialDelay = 400, ReshowDelay = 150, AutoPopDelay = 20000 };

    // 直前のOnMouseMoveでマウスカーソルの下にあったブロック項目（無ければnull）。
    // 同じ項目に留まっている間はツールチップの再表示処理をスキップするために使う。
    private BlockInstance? _hoveredItem;

    // Feature: UI改善（提案書 CUT-6）— 約55種のブロックをスクロールして探す負担を減らすため、
    // 名前(表示名/内部op名)で絞り込める検索を追加する。
    // 検索欄に入力された絞り込みキーワード。空文字なら全件表示（フィルタなし）。
    private string _filterQuery = "";

    // 外部（検索用テキストボックス側）から呼び出され、絞り込みキーワードを更新して再描画する。
    // query : 検索欄に入力された文字列。前後の空白は除去し、nullの場合は空文字として扱う。
    public void SetFilter(string query)
    {
        _filterQuery = query?.Trim() ?? "";
        Invalidate(); // フィルタが変わったのでパレット全体を再描画して表示件数を更新する
    }

    // 指定したブロック項目が現在の検索キーワードに一致するかどうかを判定する。
    // 表示名（日本語のラベル）と内部op名（英語の識別子）のどちらかに部分一致すればヒットとする。
    // キーワードが空の場合は常にtrue（絞り込みなし＝全件表示）を返す。
    private bool MatchesFilter(BlockInstance item) =>
        string.IsNullOrEmpty(_filterQuery) ||
        item.Def.DisplayName.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase) ||
        item.Def.Op.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase);

    // 各ブロックのop名をキーとして、そのブロックが「何をするものか」を平易な日本語で説明する辞書。
    // マウスホバー時にツールチップとして表示される（OnMouseMove参照）。
    // ブロック名（DisplayName）だけでは初見のユーザーには意味が伝わりにくいため、
    // 動作の詳細・使いどころ・注意点（座標系や単位など）まで含めて説明文を用意している。
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

    // コンストラクタ：パネルの基本設定を行い、カテゴリ別のブロック一覧を構築する。
    public BlockPaletteControl()
    {
        DoubleBuffered = true; // 再描画時のちらつきを防止する（頻繁にInvalidateされるため必須）
        AutoScroll = true;     // ブロック数が多くパネルに収まらない場合に自動でスクロールバーを出す
        BackColor = Color.FromArgb(235, 235, 240); // パレット全体の背景色（薄いグレー）
        BuildItems(); // BlockCatalogから全ブロック定義を取得し、カテゴリごとにグループ化しておく
    }

    // クリックされた位置にあるパレット項目のBlockDefを探し、ドラッグ操作を開始する。
    // ドロップ先(BlockCanvasControl)ではBlockDefを受け取って新規BlockInstanceを生成する。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return; // ドラッグ開始は左クリックのみ対応

        // AutoScrollPosition分を差し引いて、スクロールしていても正しい「コンテンツ座標」に変換する
        // （AutoScrollPositionは通常マイナス値なので、引き算することでスクロール量を打ち消す）
        var contentPt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        foreach (var (_, items) in _groups)
        {
            foreach (var item in items)
            {
                if (!MatchesFilter(item)) continue; // 検索フィルタで非表示になっている項目はクリック判定の対象外
                var rect = new Rectangle(item.X, item.Y, item.Width, item.Height);
                if (rect.Contains(contentPt))
                {
                    // クリック位置に一致するブロックが見つかったので、そのBlockDef（ブロックの定義情報）を
                    // ペイロードとしてドラッグ操作を開始する。DragDropEffects.Copyは「元のパレット項目は
                    // 消えず、ドロップ先に複製が作られる」という挙動を表す。
                    DoDragDrop(new BlockDragPayload(item.Def), DragDropEffects.Copy);
                    return; // 最初に見つかった1件だけを対象にするのでここで終了
                }
            }
        }
    }

    // 指定したコンテンツ座標（スクロール量を差し引いた座標）の位置にあるブロック項目を探して返す。
    // 見つからない場合はnullを返す。OnMouseMoveでのホバー判定に使用する。
    private BlockInstance? FindItemAt(Point contentPt)
    {
        foreach (var (_, items) in _groups)
        {
            foreach (var item in items)
            {
                if (!MatchesFilter(item)) continue; // 非表示（フィルタで隠れている）項目はホバー対象外
                var rect = new Rectangle(item.X, item.Y, item.Width, item.Height);
                if (rect.Contains(contentPt)) return item;
            }
        }
        return null;
    }

    // マウス移動時に呼ばれ、カーソル直下のブロックが変わったらツールチップの表示内容を更新する。
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var contentPt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
        var found = FindItemAt(contentPt);
        // 直前のフレームと同じ項目の上にいる場合は何もしない（毎フレームツールチップを出し直すと
        // ちらつきや再表示ディレイのリセットが発生してしまうため、変化があった時だけ処理する）
        if (ReferenceEquals(found, _hoveredItem)) return;
        _hoveredItem = found;
        // ホバー中の項目に説明文が登録されていればマウス位置の少し右下にツールチップを表示し、
        // 登録が無い、またはカーソルが何もない場所に移動した場合はツールチップを隠す。
        if (found != null && Descriptions.TryGetValue(found.Def.Op, out var desc))
            _tooltip.Show(desc, this, e.X + 16, e.Y + 16, 20000);
        else
            _tooltip.Hide(this);
    }

    // マウスカーソルがパネル外へ出たときにホバー状態とツールチップをリセットする。
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredItem = null;
        _tooltip.Hide(this);
    }

    // BlockCatalogに登録されている全ブロック定義を、カテゴリ（Enum値）ごとに走査して取得し、
    // それぞれをパレット表示用のBlockInstanceに変換してから_groupsへ積み上げる。
    // コンストラクタから一度だけ呼ばれ、以降パレットの内容が動的に変わることはない。
    private void BuildItems()
    {
        foreach (BlockCategory cat in Enum.GetValues(typeof(BlockCategory)))
        {
            var defs = BlockCatalog.ByCategory(cat).ToList();
            if (defs.Count == 0) continue; // そのカテゴリに属するブロックが1つも無ければグループ自体を作らない
            _groups.Add((cat, defs.Select(BlockInstance.Create).ToList()));
        }
    }

    // パレット全体の描画処理。カテゴリ見出しを描いた後、各カテゴリに属するブロックを縦に並べて描画する。
    // 検索フィルタで絞り込まれている場合は一致する項目のみを描画し、1件もヒットしなければ
    // 「一致するブロックがありません」というメッセージを表示する。
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; // ブロックの丸み・斜め線をなめらかに描画する
        // AutoScrollPositionの分だけ描画原点をずらすことで、スクロールに合わせて内容全体が
        // 一緒に移動しているように見せる（座標計算自体はスクロールしていない前提で行える）
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        // BlockRendererは下端の凹み表現を「背景色で塗りつぶす」ことで実現しているため、
        // パレットの背景色をBlockRenderer側にも伝えておく必要がある。
        BlockRenderer.CanvasBackColor = BackColor;

        int x = 10; // 各ブロック・見出しの左端X座標（余白10px）
        int y = 10; // 描画中の現在のY座標（描くたびに下へ進めていく）
        using var catFont = new Font(_font, FontStyle.Bold); // カテゴリ見出し用の太字フォント

        bool anyVisible = false; // 検索フィルタを通過した項目が1つでもあったかどうか
        foreach (var (cat, items) in _groups)
        {
            var visible = items.Where(MatchesFilter).ToList(); // このカテゴリ内で検索条件に一致する項目だけ抽出
            if (visible.Count == 0) continue; // 一致する項目が無いカテゴリは見出しごと表示しない
            anyVisible = true;

            // カテゴリ見出し（例：「■ 制御」）を描画してから次の行へ進める
            g.DrawString(CategoryLabel(cat), catFont, Brushes.Black, x, y);
            y += 22;
            foreach (var item in visible)
            {
                // ブロックのテキスト量に応じた実際のサイズ（幅・高さ）を計測し、item.Width/Heightへ反映する
                BlockLayout.Measure(item, g, _font);
                item.X = x;
                item.Y = y;
                BlockRenderer.Draw(g, item, _font); // 計測結果をもとにブロックの見た目を実際に描画する
                y += item.Height + 8; // 次のブロックとの間隔として8pxのマージンを空ける
            }
            y += 16; // カテゴリの区切りとして、次のカテゴリ見出しとの間に16pxの余白を追加する
        }

        // 検索キーワードが入力されているのに1件もヒットしなかった場合、その旨をグレー文字で案内する
        if (!anyVisible && !string.IsNullOrEmpty(_filterQuery))
            g.DrawString($"「{_filterQuery}」に一致するブロックがありません", _font, Brushes.Gray, x, y);

        // 描画し終えた内容の全高さに合わせて、スクロール可能な領域のサイズを更新する。
        // 値が変わっていないのに毎回設定するとちらつきの原因になるため、変化がある時だけ更新する。
        int neededHeight = y + 10;
        if (AutoScrollMinSize.Height != neededHeight)
            AutoScrollMinSize = new Size(230, neededHeight);
    }

    // カテゴリのEnum値を、見出しとして表示する日本語ラベル文字列に変換する。
    // 未知のカテゴリ（switch式に定義が無いもの）が来た場合は、Enum名をそのまま文字列化してフォールバックする。
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
