using System.Drawing.Drawing2D;

namespace Lab_Editor;

// ======================================================
// BlockCanvasControl - ブロックツリーを組み立てる作業用キャンバス
// Feature: Puzzle-like Behavior Scripting (M4/M5/M7)
//
// M5でドラッグ&ドロップ・スナップ挿入に対応した。
// ・パレットからドラッグ＝新規ブロックをその位置に生成
// ・キャンバス内の既存ブロックをドラッグ＝そのブロックと後続の連結ブロックをまとめて移動
// ・ドロップ位置の判定は「最も内側で該当するコンテナ（Body/Else/トップレベル）を再帰的に探し、
//   そのリスト内でのY座標から挿入インデックスを決める」という単純な方式にしている
//   （ジオメトリの複雑な判定は行わない）。
//
// M7で引数ソケット（式の差し込み口）の編集を仕上げた。
// ・リテラル欄（数値/文字列/ドロップダウン）はクリックでインライン編集できる
// ・レポーター/真偽値ブロックはパレット/キャンバスからドラッグして数値/真偽値ソケットへ差し込める
//   （埋まっているソケットへは直接上書きできない仕様。先に右クリックかDeleteキーで空にする）
// ・右クリックで埋まったソケットを空にできる
// ======================================================
// このクラス全体の役割：Scratch風のビジュアルスクリプティング（敵・ギミックの挙動をブロックを
// 積み木のように組み立てて定義する機能）のうち、実際にブロックを配置・並べ替え・編集する
// 「作業台」となるPanelコントロール。TopLevelに保持されたブロックの木構造がそのまま
// 編集対象のスクリプト（AST）そのものであり、マウス操作のたびに直接この木構造を書き換える。
public class BlockCanvasControl : Panel
{
    // ブロック内の文字（ラベルや数値）を描画するためのフォント。日本語表示に対応したMeiryo UIを使用する。
    private readonly Font _font = new Font("Meiryo UI", 9f);
    // このキャンバスが表示・編集している、木構造のルートとなる「ハットブロック」（OnSpawn等）の一覧。
    // 通常は複数のトップレベルブロック（イベントの種類ごと）が横に並ぶ。
    public List<BlockInstance> TopLevel = new();

    // ブロックの追加・削除・値の変更など、スクリプトの内容が変化するたびに発火するイベント。
    // 呼び出し元（親フォーム側）はこれを購読して「未保存」マーク等を更新する想定。
    public event Action? ContentChanged;

    // ── ドラッグ状態（列 = Body/Else/トップレベルからつまんだ場合） ──────
    private List<BlockInstance>? _pickedFromList;  // 掴んだ時点で属していたリスト（キャンバス内移動の場合のみ）
    private int _pickedFromIndex;                  // 掴んだ時点でそのリストの何番目にいたか（ドロップ失敗時に戻す位置）
    private List<BlockInstance>? _draggingChain;   // 現在ドラッグ中のチェーン本体
    private bool _dropHandled;                     // ドロップが成立したか（成立しなければ元の位置へ戻す）
    private HashSet<BlockInstance>? _excluded;      // ドロップ先として無効な対象（ドラッグ中のチェーン＋その子孫）

    // ── ドラッグ状態（引数ソケットからつまんだ場合。M7） ──────────────
    private BlockInstance? _pickedSocketOwner;      // ソケットを持っていた元のブロック（差し込み先の親）
    private string? _pickedSocketArgName;           // どの引数名のソケットから抜き取ったか
    private BlockInstance? _draggingSocketBlock;    // ソケットから抜き取って現在ドラッグ中のブロック本体

    // ドロップ位置プレビュー（列への挿入）
    private List<BlockInstance>? _previewList;  // 挿入先として現在ハイライトしているリスト
    private int _previewIndex = -1;             // そのリスト内での挿入予定インデックス
    private Rectangle _previewRect;             // プレビュー用の挿入ライン描画に使う矩形

    // ドロップ位置プレビュー（ソケットへの差し込み。M7）
    private BlockInstance? _previewSocketOwner;   // 差し込み先として現在ハイライトしているブロック
    private string? _previewSocketArgName;        // 差し込み先の引数名
    private Rectangle _previewSocketRect;         // プレビュー用の枠線描画に使う矩形

    // インライン編集中のリテラル欄（M7）
    private TextBox? _editBox;              // 現在表示している編集用テキストボックス（無い時はnull）
    private BlockInstance? _editingOwner;   // 編集中の値を持っているブロック
    private BlockArgSpec? _editingArg;      // 編集中の引数の定義（型情報等の参照用）

    // 現在選択中のブロック（外部からは参照のみ可能）。ハイライト表示や削除操作の対象になる。
    public BlockInstance? SelectedBlock { get; private set; }

    // Feature: UI改善（提案書 BS-5）— 複雑な組み立てで縦に長くなったキャンバスを縮小表示して
    // 全体を見渡せるようにする（Ctrl+ホイールでズーム、通常のホイールは従来通り縦スクロール）。
    // レイアウト計算・当たり判定は常に等倍(1.0)の論理座標で行い、描画とマウス座標の変換
    // (ToContentPoint)だけをズーム率で補正することで、どのズーム率でも編集操作がずれないようにしている。
    private float _viewZoom = 1.0f;

    // コンストラクタ：Panelとしての描画・スクロール・ドロップ受け入れの基本設定を行い、
    // 動作確認用のサンプルブロック（デモツリー）を最初から組み立てておく。
    public BlockCanvasControl()
    {
        DoubleBuffered = true;   // ちらつき防止のため、裏バッファに描画してから一括で画面に出す
        AutoScroll = true;       // ブロックがコントロールの表示範囲より大きくなったら自動でスクロールバーを出す
        BackColor = Color.White;
        AllowDrop = true;        // ドラッグ&ドロップの受け入れを許可する（これが無いとOnDragXxxが呼ばれない）
        TabStop = true;          // Tabキーでフォーカスを受け取れるようにする（キー操作を有効にするため）
        BuildDemoTree();
    }

    // マウスホイール操作のハンドラ。
    // Ctrlキーを押しながらの場合のみズーム操作として扱い、押していなければ通常のスクロール動作
    // （base.OnMouseWheelによる縦スクロール）に任せる。
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (ModifierKeys != Keys.Control) { base.OnMouseWheel(e); return; }
        // ホイールを奥に回したら+0.1、手前に回したら-0.1し、0.4〜1.5倍の範囲に収める。
        _viewZoom = Math.Clamp(_viewZoom + (e.Delta > 0 ? 0.1f : -0.1f), 0.4f, 1.5f);
        Invalidate(); // ズーム率が変わったので再描画を要求する
    }

    // Feature: Puzzle-like Behavior Scripting (M6) — 特定の敵/ギミックのscriptを編集する際に、
    // デモツリーを破棄して実データ（または空）に差し替える
    // topLevel : 編集対象として読み込む、実際のスクリプトのトップレベルブロック一覧
    public void LoadProgram(List<BlockInstance> topLevel)
    {
        TopLevel = topLevel;
        SelectedBlock = null;   // 別のスクリプトに切り替わるので選択状態をクリアする
        ClearPreview();         // 前のドロップ操作のプレビュー状態が残らないようにクリアする
        Invalidate();
    }

    // Forever{ MoveDirection(Toward,2); Wait(30); Shoot(angle=DirectionToPlayer,6,1) } を
    // OnSpawnハットの下にネストした初期サンプル（動作確認・ドラッグ練習用に最初から置いてある）
    // コンストラクタから呼ばれ、TopLevelへ「出現時に、プレイヤーへ向かって移動→待機→
    // プレイヤー方向へ弾を撃つ、を繰り返す」という見本のブロック構造を組み立てて登録する。
    private void BuildDemoTree()
    {
        // OnSpawn（出現時に実行される）ハットブロックを作る
        var onSpawn = BlockInstance.Create(BlockCatalog.Find("OnSpawn")!);
        // Forever（中身を無限に繰り返す）ブロックを作る
        var forever = BlockInstance.Create(BlockCatalog.Find("Forever")!);

        // 「プレイヤーの方向へ、速度2で移動する」ブロックを作り、引数を設定する
        var move = BlockInstance.Create(BlockCatalog.Find("MoveDirection")!);
        move.ArgValues["dir"] = "Toward";
        move.ArgValues["speed"] = 2f;

        // 「30フレーム待機する」ブロックを作る
        var wait = BlockInstance.Create(BlockCatalog.Find("Wait")!);
        wait.ArgValues["frames"] = 30f;

        // 「速度6・威力1で弾を発射する」ブロックを作り、角度の引数ソケットには
        // 「プレイヤーへの方向」を返すレポーターブロックを差し込む
        var shoot = BlockInstance.Create(BlockCatalog.Find("Shoot")!);
        shoot.ArgValues["speed"] = 6f;
        shoot.ArgValues["damage"] = 1f;
        shoot.ArgBlocks["angle"] = BlockInstance.Create(BlockCatalog.Find("DirectionToPlayer")!);

        // Foreverの中身（Body）へ、移動→待機→発射の順で並べる
        forever.Body.Add(move);
        forever.Body.Add(wait);
        forever.Body.Add(shoot);
        // OnSpawnの中身へForeverブロックを1つだけ入れる
        onSpawn.Body.Add(forever);

        TopLevel.Add(onSpawn);
    }

    // ==== レイアウト ====================================================

    // 各トップレベルブロック（ハットとその子孫全体）のサイズ・位置を計算し直す。
    // g : サイズ測定（文字列幅など）に使うGraphicsオブジェクト
    // 複数のトップレベルブロックは横に並べて配置し、1つ分の配置が終わるたびに
    // その幅+40pxぶん右へずらして次のブロックの開始X座標にする。
    private void EnsureLayout(Graphics g)
    {
        int x = 20;
        foreach (var root in TopLevel)
        {
            BlockLayout.Measure(root, g, _font);
            BlockLayout.ArrangeSequence(new List<BlockInstance> { root }, x, 20);
            x += root.Width + 40;
        }
    }

    // ==== 描画 ==========================================================

    // このコントロールの描画処理本体。ブロック本体・選択中ブロックの枠・ドロップ位置プレビューを描く。
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; // 線や角丸を滑らかに描画する
        // AutoScrollによるスクロール位置のぶんだけ描画原点をずらす（スクロールしても正しい位置に描くため）
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        BlockRenderer.CanvasBackColor = BackColor;

        EnsureLayout(g); // レイアウト計算は常に等倍座標で行う（当たり判定と一致させるため）

        if (_viewZoom != 1.0f) g.ScaleTransform(_viewZoom, _viewZoom); // 見た目だけをズーム

        // トップレベルの各ブロック（とその子孫全体）を順番に描画する
        foreach (var root in TopLevel)
            BlockRenderer.Draw(g, root, _font);

        // 選択中のブロックがあれば、その周りに点線の金色の枠を描いて選択状態を示す
        if (SelectedBlock != null)
        {
            // レポーター/真偽値ブロック（式として使う小さな部品）は本体全体の高さを、
            // それ以外（通常の命令ブロック）はヘッダー部分だけの高さをハイライト範囲にする
            bool isSocketShape = SelectedBlock.Def.Shape == BlockShape.Reporter || SelectedBlock.Def.Shape == BlockShape.Boolean;
            int hlHeight = isSocketShape ? SelectedBlock.Height : BlockLayout.HeaderHeight;
            using var selPen = new Pen(Color.Gold, 2.5f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(selPen, SelectedBlock.X - 2, SelectedBlock.Y - 2, SelectedBlock.Width + 4, hlHeight + 4);
        }

        // ドラッグ中で、列（Body/Else/トップレベル）への挿入位置が決まっていれば、
        // その挿入位置を示すピンク色の横線を描く
        if (_previewList != null && _previewIndex >= 0)
        {
            using var previewPen = new Pen(Color.DeepPink, 3f);
            // 挿入位置に既存ブロックがあればそのY座標、リストの末尾に挿入する場合は事前計算済みのY座標を使う
            int py = _previewIndex < _previewList.Count ? _previewList[_previewIndex].Y : _previewRect.Y;
            int px = _previewRect.X;
            int pw = System.Math.Max(_previewRect.Width, 60);
            g.DrawLine(previewPen, px, py, px + pw, py);
        }

        // ドラッグ中で、引数ソケットへの差し込み先が決まっていれば、そのソケットをピンク色の枠で囲む
        if (_previewSocketOwner != null)
        {
            using var socketPen = new Pen(Color.DeepPink, 3f);
            var r = _previewSocketRect;
            r.Inflate(3, 3); // 枠をソケットの矩形より少し大きくして見やすくする
            g.DrawRectangle(socketPen, r);
        }
    }

    // ==== 既存ブロックの選択・ドラッグ開始（キャンバス内） ==============

    // マウスボタンが押された時の処理。左クリックでの選択・ドラッグ開始と、右クリックでの
    // ソケットの中身削除の両方をここで扱う。
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // クリックでキーボード操作（Delete等）を受け付けられるようにフォーカスを移す
        // 左クリック・右クリック以外（中クリック等）は何もしない
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

        // 画面（クライアント）座標を、スクロール・ズームを考慮した論理座標に変換する
        var pt = ToContentPoint(e.Location);

        // 引数ソケット（リテラル欄 or 差し込み済みブロック）にヒットしたか、最優先で調べる
        var argHit = FindArgSocketAt(TopLevel, pt);
        if (argHit != null)
        {
            var (owner, argName, filled) = argHit.Value;

            if (e.Button == MouseButtons.Right)
            {
                // 右クリック：埋まったソケットを空にする（M7）
                if (filled)
                {
                    // ソケットから差し込まれていたブロックを取り除く（ブロック自体は破棄され、木構造から外れる）
                    var removed = owner.ArgBlocks[argName];
                    owner.ArgBlocks.Remove(argName);
                    // 取り除いたブロックが選択中だった場合は選択を解除する（選択先が消えてしまうため）
                    if (SelectedBlock == removed) SelectedBlock = null;
                    Invalidate();
                    ContentChanged?.Invoke();
                }
                return;
            }

            // 左クリック
            if (filled)
            {
                // 差し込まれているブロックをつまんで再ドラッグする（他のソケットへ移動、またはDelete/右クリックでの削除に備える）
                // 一旦ソケットから抜き取り、ドラッグ操作としてOSのDoDragDropに処理を渡す
                var nested = owner.ArgBlocks[argName];
                owner.ArgBlocks.Remove(argName);
                SelectedBlock = nested;
                Invalidate();

                // ドロップが成立しなかった場合に元へ戻せるよう、抜き取り元の情報を保持しておく
                _pickedSocketOwner = owner;
                _pickedSocketArgName = argName;
                _draggingSocketBlock = nested;
                _dropHandled = false;
                // ドラッグ中のブロック自身とその子孫（さらにネストされたソケットの中身）を、
                // 自分自身の中へドロップできないようにする除外集合として収集する
                _excluded = new HashSet<BlockInstance>();
                CollectDescendants(nested, _excluded);

                // OSのドラッグ&ドロップ処理を開始する（この呼び出しはドロップが完了するかキャンセルされるまでブロックする）
                DoDragDrop(new BlockDragPayload(new List<BlockInstance> { nested }), DragDropEffects.Move);

                // ドロップが成立しなかった（キャンバス外に離した等）場合は、抜き取り元のソケットへ戻す
                if (!_dropHandled && _pickedSocketOwner != null && _pickedSocketArgName != null && _draggingSocketBlock != null)
                {
                    _pickedSocketOwner.ArgBlocks[_pickedSocketArgName] = _draggingSocketBlock;
                }
                // ドラッグ状態の後片付け
                _pickedSocketOwner = null;
                _pickedSocketArgName = null;
                _draggingSocketBlock = null;
                _excluded = null;
                ClearPreview();
                Invalidate();
                ContentChanged?.Invoke();
            }
            else
            {
                // 空のリテラル欄：インライン編集を開く（真偽値ソケットは差し込み専用なので何もしない）
                var arg = System.Array.Find(owner.Def.Args, a => a.Name == argName);
                if (arg != null && arg.Type != BlockArgType.BoolSlot)
                {
                    OpenLiteralEditor(owner, arg, owner.ArgSocketRects[argName]);
                }
            }
            return;
        }

        // 引数ソケット以外の場所への右クリックは何もしない（左クリックの処理のみ以降で行う）
        if (e.Button != MouseButtons.Left) return;

        // クリック位置にある通常ブロック（命令ブロック）を、それが属するリストと添字の形で探す
        var hit = FindChainAt(TopLevel, pt);
        if (hit == null) { SelectedBlock = null; Invalidate(); return; }

        var (list, index) = hit.Value;
        SelectedBlock = list[index];
        Invalidate();

        // 掴んだブロック以降（下に連結されている分）をまとめてチェーンとして切り離す
        // これにより「1個だけ動かす」ではなく「そのブロックから下を全部まとめて移動する」という
        // Scratch風の直感的な操作を実現している
        var chain = list.GetRange(index, list.Count - index);
        list.RemoveRange(index, list.Count - index);

        // ドロップが成立しなかった場合に元の位置へ戻せるよう、切り離し元の情報を保持しておく
        _pickedFromList = list;
        _pickedFromIndex = index;
        _draggingChain = chain;
        _dropHandled = false;
        // チェーン中の各ブロックとその子孫全てを、ドロップ先として無効な集合として収集する
        _excluded = new HashSet<BlockInstance>();
        foreach (var c in chain) CollectDescendants(c, _excluded);

        Invalidate();
        // OSのドラッグ&ドロップ処理を開始する
        DoDragDrop(new BlockDragPayload(chain), DragDropEffects.Move);

        // ドロップが成立しなかった場合は元の位置へ戻す（データを失わないようにする安全策）
        if (!_dropHandled && _pickedFromList != null)
        {
            _pickedFromList.InsertRange(System.Math.Min(_pickedFromIndex, _pickedFromList.Count), chain);
        }
        // ドラッグ状態の後片付け
        _pickedFromList = null;
        _draggingChain = null;
        _excluded = null;
        ClearPreview();
        Invalidate();
        ContentChanged?.Invoke();
    }

    // キー入力のハンドラ。選択中のブロックがある状態でDeleteキーを押すと、
    // そのブロックを木構造全体（Body/Else/引数ソケットのどこにあっても）から取り除く。
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete && SelectedBlock != null)
        {
            if (RemoveFromTree(TopLevel, SelectedBlock))
            {
                SelectedBlock = null;
                Invalidate();
                ContentChanged?.Invoke();
            }
        }
    }

    // ==== インライン編集（リテラル欄。M7） ================================

    // 変数名を扱う（自由入力の名前を持つ）ブロックのOp名一覧。
    // これらのブロックの"name"引数だけ、既存の変数名をオートコンプリート候補として提示する対象になる。
    private static readonly HashSet<string> VariableArgOps = new() { "SetVar", "ChangeVar", "GetVar" };

    // 現在のスクリプト全体（ネストした引数ソケットの中も含む）を辿り、既に使われている変数名を収集する
    // 戻り値：これまでにSetVar/ChangeVar/GetVarで使われた変数名の一覧（重複なし）
    private List<string> CollectKnownVariableNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        // 1つのブロックを調べ、変数名を持っていれば収集したうえで、
        // そのブロックが持つ子ブロック（引数ソケットの中身・Body・Else）も再帰的に辿る
        void Visit(BlockInstance b)
        {
            if (VariableArgOps.Contains(b.Def.Op) && b.ArgValues.TryGetValue("name", out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
                names.Add(s);
            foreach (var nested in b.ArgBlocks.Values) Visit(nested);
            if (b.Def.HasBody) foreach (var c in b.Body) Visit(c);
            if (b.Def.HasElse) foreach (var c in b.Else) Visit(c);
        }
        // 全てのトップレベルハットブロックから探索を開始する
        foreach (var hat in TopLevel) Visit(hat);
        return names.ToList();
    }

    // リテラル欄（数値/文字列/ドロップダウン）のインライン編集を開始する。
    // owner       : 編集対象の値を持っているブロック
    // arg         : 編集対象の引数の定義（型情報を含む）
    // contentRect : クリックされたリテラル欄の矩形（論理座標）。編集用コントロールの表示位置に使う
    private void OpenLiteralEditor(BlockInstance owner, BlockArgSpec arg, Rectangle contentRect)
    {
        // 既に別の編集ボックスが開いていた場合は、その内容を確定させずに閉じてから新しい編集を始める
        CloseLiteralEditor(commit: false);

        if (arg.Type == BlockArgType.Dropdown)
        {
            // ドロップダウン型の引数は、テキストボックスではなく選択肢一覧の右クリックメニュー風の
            // ContextMenuStripを使って選ばせる
            using var menu = new ContextMenuStrip();
            foreach (var opt in arg.DropdownOptions ?? System.Array.Empty<string>())
            {
                // ループ変数optをそのままラムダ式内で使うとクロージャの問題が起きるため、
                // ローカル変数へ複製してからイベントハンドラに渡す
                string capturedOpt = opt;
                var item = new ToolStripMenuItem(opt);
                item.Click += (s, e) =>
                {
                    owner.ArgValues[arg.Name] = capturedOpt;
                    Invalidate();
                    ContentChanged?.Invoke();
                };
                menu.Items.Add(item);
            }
            // 論理座標(contentRect)を実際の画面座標に変換してメニューを表示する
            var screenPt = PointToScreen(new Point(contentRect.X + AutoScrollPosition.X, contentRect.Bottom + AutoScrollPosition.Y));
            menu.Show(screenPt);
            return;
        }

        // 数値・文字列型の引数は、その場にテキストボックスを重ねて表示し、直接文字入力させる
        var rect = new Rectangle(contentRect.X + AutoScrollPosition.X, contentRect.Y + AutoScrollPosition.Y,
            System.Math.Max(contentRect.Width, 40), contentRect.Height);
        _editBox = new TextBox
        {
            Location = rect.Location,
            Size = rect.Size,
            // 既存の値があればそれを初期表示テキストにする。無ければ空欄から始める
            Text = owner.ArgValues.TryGetValue(arg.Name, out var v) ? v?.ToString() ?? "" : "",
            BorderStyle = BorderStyle.FixedSingle,
        };
        // Feature: UI改善（提案書 BS-2）— SetVar/ChangeVar/GetVarの変数名は自由入力のため、
        // 綴りミスに気づけず「別の変数」として扱われてしまう。既に使われている変数名を
        // ネイティブのオートコンプリートで候補提示する。
        if (arg.Name == "name" && VariableArgOps.Contains(owner.Def.Op))
        {
            var src = new AutoCompleteStringCollection();
            src.AddRange(CollectKnownVariableNames().ToArray());
            _editBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _editBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _editBox.AutoCompleteCustomSource = src;
        }
        _editingOwner = owner;
        _editingArg = arg;
        // Enterキーで確定、Escapeキーでキャンセルする
        _editBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { CloseLiteralEditor(true); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CloseLiteralEditor(false); e.Handled = true; }
        };
        // テキストボックスからフォーカスが外れた場合（他の場所をクリックした等）も、
        // 入力内容を確定として扱って閉じる
        _editBox.LostFocus += (s, e) => CloseLiteralEditor(true);
        Controls.Add(_editBox);
        _editBox.BringToFront();
        _editBox.Focus();
        _editBox.SelectAll(); // 既存の値を全選択状態にして、そのまま上書き入力しやすくする
    }

    // 開いているリテラル編集用テキストボックスを閉じる。
    // commit : trueなら入力内容を実際の値として確定する。falseなら破棄してキャンセルする
    private void CloseLiteralEditor(bool commit)
    {
        if (_editBox == null) return;
        var box = _editBox;
        _editBox = null; // LostFocus再入防止のため先にnull化してからControls.Removeする

        if (commit && _editingOwner != null && _editingArg != null)
        {
            string text = box.Text;
            if (_editingArg.Type == BlockArgType.Number)
            {
                // 数値型の場合は変換できた時だけ反映する（不正な文字列を入力した場合は元の値のまま据え置く）
                if (float.TryParse(text, out var f)) _editingOwner.ArgValues[_editingArg.Name] = f;
            }
            else
            {
                // 文字列型はそのまま反映する
                _editingOwner.ArgValues[_editingArg.Name] = text;
            }
            ContentChanged?.Invoke();
        }

        Controls.Remove(box);
        box.Dispose();
        _editingOwner = null;
        _editingArg = null;
        Invalidate();
    }

    // ==== ドロップ受け入れ（パレットからの新規、またはキャンバス内移動） ====

    // ドラッグ中のマウスがこのコントロールの領域に入った時に呼ばれる。
    // 運んでいるデータがBlockDragPayload（このエディタ独自のブロック運搬用データ）であれば
    // ドロップを受け入れ可能とする。
    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        if (drgevent.Data?.GetDataPresent(typeof(BlockDragPayload)) == true)
            drgevent.Effect = DragDropEffects.Move | DragDropEffects.Copy;
    }

    // ドラッグ中のマウスがこのコントロール上を移動するたびに呼ばれる。
    // 現在のマウス位置に応じて、挿入先（列 or ソケット）のプレビュー表示を更新する。
    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        if (drgevent.Data?.GetData(typeof(BlockDragPayload)) is not BlockDragPayload payload) { return; }
        // パレットからの新規生成ならCopy、キャンバス内の既存ブロック移動ならMoveとしてカーソル表示を変える
        drgevent.Effect = payload.NewFromDef != null ? DragDropEffects.Copy : DragDropEffects.Move;

        // マウスの画面座標を論理座標へ変換し、最新のレイアウトを再計算しておく
        // （ドラッグ中に他の操作でレイアウトが変わっている可能性があるため）
        var pt = ToContentPoint(PointToClient(new Point(drgevent.X, drgevent.Y)));
        using var g = CreateGraphics();
        EnsureLayout(g);

        var draggedShape = PayloadShape(payload);
        if (draggedShape == BlockShape.Reporter || draggedShape == BlockShape.Boolean)
        {
            // 運んでいるのが「式」として使うブロック（レポーター/真偽値）の場合は、
            // 挿入先として型の合う引数ソケットを探してプレビュー表示する
            var target = FindSocketTargetDeep(TopLevel, pt, _excluded ?? new HashSet<BlockInstance>(), draggedShape.Value);
            _previewSocketOwner = target?.owner;
            _previewSocketArgName = target?.argName;
            _previewSocketRect = target?.rect ?? Rectangle.Empty;
            ClearSequencePreview(); // 列への挿入プレビューは表示しない（同時には出さない）
        }
        else
        {
            // 通常の命令ブロックの場合は、挿入先の列（Body/Else/トップレベル）と挿入位置を探してプレビュー表示する
            var (list, index, containerX, containerWidth) = FindDropTargetDeep(TopLevel, pt, _excluded ?? new HashSet<BlockInstance>(),
                new Rectangle(20, 20, System.Math.Max(ClientSize.Width - 40, 200), int.MaxValue / 2));
            _previewList = list;
            _previewIndex = index;
            _previewRect = new Rectangle(containerX, 0, containerWidth, 0);
            ClearSocketPreview(); // ソケットへの差し込みプレビューは表示しない
        }
        Invalidate();
    }

    // ドラッグ中のマウスがこのコントロールの領域から出た時に呼ばれる。
    // プレビュー表示をクリアして、挿入先が無いことを示す。
    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        ClearPreview();
        Invalidate();
    }

    // このコントロール上でドロップが実行された時の処理。プレビューで示していた挿入先へ
    // 実際にブロックを追加・移動する。
    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (drgevent.Data?.GetData(typeof(BlockDragPayload)) is not BlockDragPayload payload) return;

        var draggedShape = PayloadShape(payload);
        if (draggedShape == BlockShape.Reporter || draggedShape == BlockShape.Boolean)
        {
            // 引数ソケットへの差し込みドロップ
            if (_previewSocketOwner != null && _previewSocketArgName != null)
            {
                BlockInstance toPlace;
                if (payload.NewFromDef != null)
                {
                    // パレットから運んできた場合は、その定義から新しいブロックインスタンスを生成する
                    toPlace = BlockInstance.Create(payload.NewFromDef);
                }
                else if (payload.ExistingChain != null)
                {
                    // キャンバス内の既存ブロックを運んできた場合は、そのインスタンスをそのまま使う
                    toPlace = payload.ExistingChain[0];
                    _dropHandled = true; // 元の位置へ戻す処理を行わせないようにする
                }
                else { ClearPreview(); return; }

                _previewSocketOwner.ArgBlocks[_previewSocketArgName] = toPlace;
                SelectedBlock = toPlace;
            }
            ClearPreview();
            Invalidate();
            ContentChanged?.Invoke();
            return;
        }

        // 列（Body/Else/トップレベル）への挿入ドロップ。挿入先が決まっていなければ何もしない
        if (_previewList == null || _previewIndex < 0) { ClearPreview(); return; }

        if (payload.NewFromDef != null)
        {
            // パレットからの新規ドロップ：定義から新しいブロックを1つ生成して挿入する
            var fresh = BlockInstance.Create(payload.NewFromDef);
            _previewList.Insert(System.Math.Min(_previewIndex, _previewList.Count), fresh);
            SelectedBlock = fresh;
        }
        else if (payload.ExistingChain != null)
        {
            // キャンバス内移動：つまんでいたチェーン（ブロック本体+後続の連結ブロック）をまとめて挿入する
            _previewList.InsertRange(System.Math.Min(_previewIndex, _previewList.Count), payload.ExistingChain);
            _dropHandled = true; // 元の位置へ戻す処理を行わせないようにする
            SelectedBlock = payload.ExistingChain[0];
        }

        ClearPreview();
        Invalidate();
        ContentChanged?.Invoke();
    }

    // ドラッグ中のペイロードが表す形状（Reporter/Boolean/通常命令など）を取得する。
    // 新規生成（NewFromDef）なら定義から、既存ブロック移動（ExistingChain）ならその先頭ブロックの定義から判定する。
    private static BlockShape? PayloadShape(BlockDragPayload payload)
        => payload.NewFromDef?.Shape ?? payload.ExistingChain?[0].Def.Shape;

    // 列への挿入プレビュー・ソケットへの差し込みプレビューの両方をまとめてクリアする。
    private void ClearPreview()
    {
        ClearSequencePreview();
        ClearSocketPreview();
    }

    // 列（Body/Else/トップレベル）への挿入プレビュー状態だけをクリアする。
    private void ClearSequencePreview()
    {
        _previewList = null;
        _previewIndex = -1;
    }

    // 引数ソケットへの差し込みプレビュー状態だけをクリアする。
    private void ClearSocketPreview()
    {
        _previewSocketOwner = null;
        _previewSocketArgName = null;
    }

    // ==== ヒットテスト ====================================================

    // 画面上のクライアント座標（マウスイベントの座標）を、スクロール位置とズーム率を差し引いた
    // 「論理座標」（レイアウト計算・当たり判定で使う座標系）に変換する。
    private Point ToContentPoint(Point clientPt) => new(
        (int)((clientPt.X - AutoScrollPosition.X) / _viewZoom),
        (int)((clientPt.Y - AutoScrollPosition.Y) / _viewZoom));

    // キャンバス内の既存ブロックをMouseDownでつまむための検索（Body/Elseも再帰的に見る）
    // list, pt : 検索対象のブロック列と、判定したい論理座標
    // 戻り値：ヒットしたブロックが属するリストと、そのリスト内での添字。見つからなければnull
    // 先に子（Body/Else）を再帰的に調べることで、入れ子の内側にあるブロックを優先してヒットさせる。
    private (List<BlockInstance> list, int index)? FindChainAt(List<BlockInstance> list, Point pt)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (b.Def.HasBody)
            {
                var inner = FindChainAt(b.Body, pt);
                if (inner != null) return inner;
            }
            if (b.Def.HasElse)
            {
                var inner = FindChainAt(b.Else, pt);
                if (inner != null) return inner;
            }
            // ヘッダー部分（ブロック本体の見出し行）の矩形とヒットテストする
            var headerRect = new Rectangle(b.X, b.Y, b.Width, BlockLayout.HeaderHeight);
            if (headerRect.Contains(pt)) return (list, i);
        }
        return null;
    }

    // 引数ソケット（リテラル欄 or 差し込み済みブロック）のヒットテスト。深いネストほど優先して返す（M7）
    // list, pt : 検索対象のブロック列と、判定したい論理座標
    // 戻り値：ヒットしたソケットの所有ブロック・引数名・埋まっているかどうか。見つからなければnull
    private (BlockInstance owner, string argName, bool filled)? FindArgSocketAt(List<BlockInstance> list, Point pt)
    {
        foreach (var b in list)
        {
            var hit = FindArgSocketAtBlock(b, pt);
            if (hit != null) return hit;
        }
        return null;
    }

    // 1つのブロックbとその子孫（Body/Else/差し込み済みソケットの中身）を再帰的に辿り、
    // ptにヒットする引数ソケットを探す。より深いネストのソケットを優先して見つける。
    private (BlockInstance owner, string argName, bool filled)? FindArgSocketAtBlock(BlockInstance b, Point pt)
    {
        // まずBody/Elseの中身（ネストされた命令ブロック列）を先に調べる
        foreach (var c in b.Body)
        {
            var inner = FindArgSocketAtBlock(c, pt);
            if (inner != null) return inner;
        }
        foreach (var c in b.Else)
        {
            var inner = FindArgSocketAtBlock(c, pt);
            if (inner != null) return inner;
        }

        // 次にこのブロック自身が持つ各引数ソケットを調べる
        foreach (var arg in b.Def.Args)
        {
            if (!b.ArgSocketRects.TryGetValue(arg.Name, out var rect)) continue;
            bool filled = b.ArgBlocks.TryGetValue(arg.Name, out var nested);
            if (filled)
            {
                // ソケットが埋まっている場合、まずその差し込まれたブロックの内部（さらに深いソケット）を優先して調べる
                var inner = FindArgSocketAtBlock(nested!, pt);
                if (inner != null) return inner;
            }
            if (rect.Contains(pt)) return (b, arg.Name, filled);
        }
        return null;
    }

    // ドロップ可能な「空いている」引数ソケットの検索。型（Number⇔Reporter、BoolSlot⇔Boolean）が
    // 一致するソケットのみを対象にする。埋まっているソケットへは直接ドロップできない（M7）
    // list, pt, excluded, draggedShape : 検索対象の列、判定座標、除外対象、運んでいるブロックの形状
    // 戻り値：差し込み先のブロック・引数名・その矩形。見つからなければnull
    private (BlockInstance owner, string argName, Rectangle rect)? FindSocketTargetDeep(
        List<BlockInstance> list, Point pt, HashSet<BlockInstance> excluded, BlockShape draggedShape)
    {
        foreach (var b in list)
        {
            var hit = FindSocketTargetDeepBlock(b, pt, excluded, draggedShape);
            if (hit != null) return hit;
        }
        return null;
    }

    // 1つのブロックbとその子孫を再帰的に辿り、ptにヒットしてかつ型が一致する「空いている」
    // 引数ソケットを探す。
    private (BlockInstance owner, string argName, Rectangle rect)? FindSocketTargetDeepBlock(
        BlockInstance b, Point pt, HashSet<BlockInstance> excluded, BlockShape draggedShape)
    {
        // ドラッグ中のブロック自身やその子孫へは差し込めない（自己参照を防ぐ）
        if (excluded.Contains(b)) return null;

        foreach (var c in b.Body)
        {
            var inner = FindSocketTargetDeepBlock(c, pt, excluded, draggedShape);
            if (inner != null) return inner;
        }
        foreach (var c in b.Else)
        {
            var inner = FindSocketTargetDeepBlock(c, pt, excluded, draggedShape);
            if (inner != null) return inner;
        }

        foreach (var arg in b.Def.Args)
        {
            if (!b.ArgSocketRects.TryGetValue(arg.Name, out var rect)) continue;
            bool filled = b.ArgBlocks.TryGetValue(arg.Name, out var nested);
            if (filled)
            {
                // 既に埋まっているソケットでも、その中に差し込まれたブロックのさらに内部にある
                // 空いているソケットへは差し込める可能性があるため、除外対象でなければ再帰的に調べる
                if (excluded.Contains(nested!)) continue;
                var inner = FindSocketTargetDeepBlock(nested!, pt, excluded, draggedShape);
                if (inner != null) return inner;
                continue; // 埋まっているソケット自体は差し込み先にならない
            }

            // 引数の型と運んでいるブロックの形状が対応している場合のみ差し込み可能とする
            // （Number型のソケットにはReporterブロック、BoolSlot型のソケットにはBooleanブロックのみ）
            bool compatible = (arg.Type == BlockArgType.Number && draggedShape == BlockShape.Reporter)
                            || (arg.Type == BlockArgType.BoolSlot && draggedShape == BlockShape.Boolean);
            if (compatible && rect.Contains(pt)) return (b, arg.Name, rect);
        }
        return null;
    }

    // 子孫（Body/Else/引数ソケットの中身すべて）を再帰的に集める。ドラッグ中のチェーン/ブロックを
    // 自分自身の中にドロップできないようにするための除外集合づくりに使う
    // b   : 起点となるブロック（このブロック自身も結果setに含まれる）
    // set : 収集結果を格納する集合（呼び出し側で用意したものへ追加していく）
    private static void CollectDescendants(BlockInstance b, HashSet<BlockInstance> set)
    {
        set.Add(b);
        foreach (var c in b.Body) CollectDescendants(c, set);
        foreach (var c in b.Else) CollectDescendants(c, set);
        foreach (var kv in b.ArgBlocks) CollectDescendants(kv.Value, set);
    }

    // 最も内側で該当するコンテナ（Body/Else/トップレベル）を再帰的に探し、その中でのY座標から
    // 挿入インデックスを決める。深い階層のコンテナが優先される。
    // list, pt, excluded : 検索対象の列、判定座標、除外対象
    // containerBounds    : listが表示されている領域の矩形（プレビュー描画用のX座標・幅の算出に使う）
    // 戻り値：挿入先のリスト、挿入インデックス、プレビュー線を描くためのX座標・幅
    private (List<BlockInstance> list, int index, int containerX, int containerWidth) FindDropTargetDeep(
        List<BlockInstance> list, Point pt, HashSet<BlockInstance> excluded, Rectangle containerBounds)
    {
        foreach (var b in list)
        {
            if (excluded.Contains(b)) continue;

            if (b.Def.HasBody)
            {
                // このブロックのBody（中に入れ子で命令を並べる領域）の矩形を計算し、
                // マウス位置がその中にあれば、さらに深い階層として再帰的に探索する
                var bodyRect = new Rectangle(b.X + BlockLayout.CIndent, b.Y + BlockLayout.HeaderHeight,
                    System.Math.Max(b.Width - BlockLayout.CIndent, 40), b.BodyHeight);
                if (bodyRect.Contains(pt))
                    return FindDropTargetDeep(b.Body, pt, excluded, bodyRect);
            }
            if (b.Def.HasElse)
            {
                // Else領域についても同様に、マウス位置がその中にあれば再帰的に探索する
                int elseY = b.Y + BlockLayout.HeaderHeight + b.BodyHeight + BlockLayout.ElseLabelHeight;
                var elseRect = new Rectangle(b.X + BlockLayout.CIndent, elseY,
                    System.Math.Max(b.Width - BlockLayout.CIndent, 40), b.ElseHeight);
                if (elseRect.Contains(pt))
                    return FindDropTargetDeep(b.Else, pt, excluded, elseRect);
            }
        }

        // このコンテナ自身が挿入先。Y座標に最も近い挿入位置（既存ブロックの中央より上か下か）を求める
        // 各ブロックの中央のY座標より上にマウスがあれば、そのブロックの手前に挿入する位置とする。
        // どのブロックの中央よりも下だった場合は、リストの末尾（list.Count）に挿入する。
        int index = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            if (excluded.Contains(list[i])) continue;
            int mid = list[i].Y + list[i].Height / 2;
            if (pt.Y < mid) { index = i; break; }
        }
        return (list, index, containerBounds.X, containerBounds.Width);
    }

    // ドラッグ操作を伴わない、外部（保存処理等）からの木構造からの除去、およびDeleteキーに使う。
    // Body/Elseのシーケンスだけでなく、引数ソケット（ArgBlocks）に差し込まれたブロックも対象にする（M7）
    // list   : 検索・削除対象のブロック列
    // target : 木構造全体から取り除きたいブロック
    // 戻り値：削除に成功したかどうか
    private static bool RemoveFromTree(List<BlockInstance> list, BlockInstance target)
    {
        // まずこの階層のリスト自体に対象が含まれていれば、そこから直接取り除いて終了する
        if (list.Remove(target)) return true;
        foreach (var b in list)
        {
            // 各ブロックのBody・Else（ネストした命令列）を再帰的に調べる
            if (RemoveFromTree(b.Body, target)) return true;
            if (RemoveFromTree(b.Else, target)) return true;
            // 各ブロックの引数ソケット（ArgBlocks）に差し込まれているブロックも調べる
            if (RemoveFromArgBlocks(b, target)) return true;
        }
        return false;
    }

    // ownerが持つ引数ソケット（ArgBlocks）の中からtargetを探して取り除く。
    // owner  : 引数ソケットを持つブロック
    // target : 取り除きたいブロック
    // 戻り値：削除に成功したかどうか
    private static bool RemoveFromArgBlocks(BlockInstance owner, BlockInstance target)
    {
        // まずownerの直接の引数ソケットの中にtargetそのものが差し込まれていないか調べる
        string? foundKey = null;
        foreach (var kv in owner.ArgBlocks)
        {
            if (kv.Value == target) { foundKey = kv.Key; break; }
        }
        if (foundKey != null) { owner.ArgBlocks.Remove(foundKey); return true; }

        // 直接は見つからなかった場合、差し込まれている各ブロックのさらに内部
        // （そのブロックのBody/Else、そのまた引数ソケット）を再帰的に調べる
        foreach (var kv in owner.ArgBlocks)
        {
            if (RemoveFromTree(kv.Value.Body, target)) return true;
            if (RemoveFromTree(kv.Value.Else, target)) return true;
            if (RemoveFromArgBlocks(kv.Value, target)) return true;
        }
        return false;
    }
}
