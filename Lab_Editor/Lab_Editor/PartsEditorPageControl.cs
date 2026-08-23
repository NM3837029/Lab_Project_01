using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartsEditorPageControl - 複合オブジェクト（敵/ギミック/アイテム）のパーツ編集
// Feature: Composite Multi-Part Objects (Parts-M7、UI刷新版)
// 構造改修フェーズ5cでForm(PartsEditorForm)からUserControlへ抽出。
//
// 1体の敵/ギミック/アイテムを、複数の画像パーツの組み合わせとして構成するためのエディタ。
//
// 【UI刷新の経緯】旧版は1つの横長DataGridViewに全項目(id/offsetX/offsetY/hp/zOrder/画像/
// ボタン×4)を詰め込んでおり、狭い固定パネル幅では列が見切れて使い物にならなかった。
// 新版では「パーツ一覧(id/hp/zOrderのみの簡易一覧)」と「選択中パーツの詳細編集パネル」を分離し、
// 全体をSplitContainer（ユーザーがドラッグで境界を調整できる）+ TableLayoutPanel/FlowLayoutPanel
// （内容に応じて自動的にサイズ・配置される）で構成することで、固定座標指定によるはみ出し・
// 見切れを構造的に起こりにくくしている。
// ======================================================
public class PartsEditorPageControl : UserControl
{
    // UserControlにはDialogResult/Close()というプロパティ・メソッドが存在しない（これらはFormクラス専用）。
    // そのため「保存されたこと」「キャンセルされたこと」を呼び出し元(親ページ)に伝える手段として、
    // イベントを自前で用意している。呼び出し元はSaved/Cancelledを購読し、発火したタイミングで
    // 画面遷移（前のページに戻るなど）を行う。
    public event EventHandler<List<PartDef>>? Saved;
    public event EventHandler? Cancelled;
    // 下部のOK/キャンセルボタンを外部（シェル側のフッターなど）からも参照できるように公開している。
    public Button PrimaryActionButton => _btnOk;
    public Button SecondaryActionButton => _btnCancel;
    private Button _btnOk = null!, _btnCancel = null!;

    // 「当たり判定編集」「挙動スクリプト編集」は、このページの中では完結せず別ページへ一時的に
    // 移動して編集させる（ドリルダウン）。その要求をイベントとして親に伝える。
    // AssetManagerPageControlも同じ設計を採用している（詳細はPageEditRequests.csを参照）。
    public event HitboxEditRequestHandler? HitboxEditRequested;
    public event BehaviorScriptEditRequestHandler? BehaviorScriptEditRequested;

    // プロジェクトのルートフォルダ（画像の相対パスを絶対パスに解決するために使う）
    private readonly string projectRoot;
    // 編集対象の敵/ギミック本体が持つ基準スプライト（合成プレビューの中心に薄く表示する目印用）
    private readonly string baseSpritePath;
    private Image? baseSprite;
    // パーツごとのサムネイル画像のキャッシュ。毎回ファイルを読み直すと重いため、一度読み込んだら
    // PartDefインスタンスをキーにして保持しておく（画像が変わった時はInvalidatePartThumbで明示的に破棄する）。
    private readonly Dictionary<PartDef, Image?> _partThumbCache = new();

    // 編集中のパーツ一覧本体。コンストラクタで渡された初期値をクローンして持つため、
    // キャンセルされても呼び出し元の元データは書き換わらない。
    private List<PartDef> parts;
    // OKが押された後、確定した編集結果を呼び出し元が読み取るための公開プロパティ。
    public List<PartDef> ResultParts { get; private set; } = new();

    // 現在選択中のパーツのインデックス（未選択時は-1）
    private int selectedIndex = -1;
    // 詳細パネルの値をコードから設定している最中に、その変更をユーザー操作と誤認して
    // 余計なイベント処理（Undo履歴の記録など）が走らないようにするためのフラグ。
    private bool _suppressEvents = false;

    // ドラッグ状態（合成キャンバス上でのパーツ移動）
    // どのパーツをドラッグ中か（-1はドラッグしていない状態）
    private int _draggingIndex = -1;
    // ドラッグ開始時のマウス座標（差分を計算する基準点）
    private Point _dragMouseStart;
    // ドラッグ開始時点でのパーツのoffsetX/offsetY（マウス移動量をこの値に加算していく）
    private float _dragOffsetStartX, _dragOffsetStartY;

    // Feature: UI改善（提案書 PT-1）— 挙動スクリプトによる動きをその場で確認する再生プレビュー
    // 再生中に一定間隔でTickイベントを発生させ、経過時間(_previewTime)を進めるタイマー
    private System.Windows.Forms.Timer? _previewTimer;
    // 再生プレビューの現在時刻（スクリプト評価に渡すTime値。フレーム数相当のカウンタ）
    private float _previewTime = 0f;
    // 再生中かどうか（true=再生中。ドラッグでの位置調整は再生中は無効にする）
    private bool _isPlaying = false;
    private Button _btnPlayToggle = null!;

    // Feature: UI改善（提案書 CUT-1）— パーツ編集画面自体のUndo/Redo（マップ編集限定だった仕組みの拡張）
    // パーツ一覧のスナップショットを積み重ねて保持する履歴管理オブジェクト
    private readonly HistoryManager<List<PartDef>> _history = new();
    private Button _btnUndo = null!, _btnRedo = null!;

    // Feature: UI改善（提案書 PT-6）— 「このスクリプトを全パーツへ」適用後、どのパーツが変わったか
    // 一目で分かるよう、対象パーツの枠を一瞬光らせる
    // 現在ハイライト表示すべきパーツの集合（描画時にこの集合に含まれるパーツだけ緑枠を追加で描く）
    private readonly HashSet<PartDef> _highlightedParts = new();
    // ハイライトを一定時間後に自動的に消すためのワンショットタイマー
    private System.Windows.Forms.Timer? _highlightTimer;

    // ==== コントロール ====
    // パーツの簡易一覧を表示するグリッド（左側パネル）
    private DataGridView dgvList = null!;
    // 合成プレビューを描画するキャンバス（中央パネル）
    private Panel pnlComposer = null!;
    // 以下、選択中パーツの詳細編集パネル（右下）で使う各入力コントロール
    private TextBox txtId = null!;
    private Label lblSpriteValue = null!;
    private NumericUpDown nudOffsetX = null!, nudOffsetY = null!, nudScale = null!, nudHp = null!, nudZOrder = null!;
    private Label lblHpHint = null!;

    // コンストラクタ。
    // subjectLabel   : 編集対象（敵/ギミック/アイテムの名前など）を表す文字列。将来的な見出し表示用に受け取っている。
    // initialParts   : 編集開始時点でのパーツ一覧。ここではクローンして保持するため、このリスト自体は変更されない。
    // projectRoot    : プロジェクトのルートフォルダ。画像パスの解決に使う。
    // baseSpritePath : 本体の基準スプライトのパス。合成プレビューの中心目印として表示する。
    public PartsEditorPageControl(string subjectLabel, List<PartDef> initialParts, string projectRoot, string baseSpritePath)
    {
        this.projectRoot = projectRoot;
        this.baseSpritePath = baseSpritePath;
        // 渡されたパーツ一覧をそのまま参照すると、キャンセルしても呼び出し元のデータが
        // 書き換わってしまう恐れがあるため、1件ずつクローンして独立したリストとして保持する。
        parts = initialParts.Select(ClonePart).ToList();

        // ページ全体を親コンテナいっぱいに広げ、共通のUIテーマフォントを適用する。
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        // 本体の基準スプライト画像を読み込んでおく（合成プレビューの中心に表示するため）。
        LoadBaseSprite();

        // 画面上部に表示する操作ヒントのラベル。
        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 40,
            Text = "左：パーツ一覧（追加/削除/選択）　中央：合成プレビュー（ドラッグで位置調整）　右：選択中パーツの詳細。境界線はドラッグで広さを調整できます。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            BackColor = Color.FromArgb(230, 240, 255),
        };

        // 下部の「OK/キャンセル」「Undo/Redo」ボタン群を組み立てる。
        var pnlBottom = BuildBottomButtons();

        // ルート: 左(一覧) | 右(プレビュー+詳細)
        // 画面全体をユーザーがドラッグで比率調整できるSplitContainerで左右に分割する。
        var rootSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
        var pnlListSide = BuildListSide();

        // 右側をさらに 上(プレビュー) / 下(詳細) に分割
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6 };
        var pnlComposerSide = BuildComposerSide();
        var pnlDetailSide = BuildDetailSide();

        // 各パーツをコントロールツリーに配置していく。
        Controls.Add(rootSplit);
        Controls.Add(pnlBottom);
        Controls.Add(lblHint);

        rootSplit.Panel1.Controls.Add(pnlListSide);
        rootSplit.Panel2.Controls.Add(rightSplit);
        rightSplit.Panel1.Controls.Add(pnlComposerSide);
        rightSplit.Panel2.Controls.Add(pnlDetailSide);

        // SplitterDistanceはコントロールがDockされ実サイズが確定してから設定する（先に設定すると例外/無視されることがある）。
        // UserControlにはShownがないため、同じ目的で「親に配置されて実サイズが確定した後」に一度だけ
        // 発火するLoadイベントを使う。
        Load += (s, e) =>
        {
            // 左側パネルの幅は「画面幅の24%」を基準にしつつ、狭くなりすぎないよう最低280pxを確保する。
            rootSplit.SplitterDistance = Math.Max(280, (int)(ClientSize.Width * 0.24));
            // 右側上段（プレビュー）の高さは「右側全体の55%」を基準にしつつ、最低300pxを確保する。
            rightSplit.SplitterDistance = Math.Max(300, (int)(rightSplit.Height * 0.55));
        };

        // 初期状態を履歴に1件積んでおく（これがないとUndoで「何もない状態」へ戻れなくなる）。
        PushHistory();
        // 一覧グリッドと詳細パネルを最新状態に合わせて表示する。
        RefreshList();
    }

    // キーボードショートカット（Ctrl+Z=元に戻す、Ctrl+Y=やり直す）をこのページ内で捕まえて処理する。
    // WinFormsの標準ではメニューやツールバーのショートカットキーとして登録しないと拾えないため、
    // ProcessCmdKeyをオーバーライドして直接キー入力を検知している。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { PartsUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { PartsRedo(); return true; }
        // 該当しないキーは基底クラスの処理に委ねる（他のショートカットやフォーカス移動を妨げないため）。
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // このメソッド1つを、パーツ一覧を実際に変更する全ての操作（追加/削除/並び替え/複製/反転複製/
    // 各ウィザードの生成・編集/詳細パネルの値確定/キャンバスのドラッグ確定/画像・当たり判定・
    // スクリプトの変更確定）の末尾から呼ぶことで、Undo/Redoの対象を機能追加のたびに
    // 個別配線し直さずに済むようにしている。
    private void PushHistory()
    {
        // 現在のパーツ一覧をディープクローンしてから履歴スタックに積む。
        // クローンしない場合、後から同じPartDefインスタンスを書き換えると過去の履歴まで
        // 一緒に変わってしまい、Undo/Redoが正しく機能しなくなる。
        _history.Push(parts.Select(ClonePart).ToList());
        // ボタンの有効/無効状態（これ以上戻れない/進めない場合はグレーアウト）を更新する。
        UpdateUndoRedoButtons();
    }

    // 「元に戻す」処理。履歴スタックから1つ前の状態を取り出し、現在のパーツ一覧を置き換える。
    private void PartsUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        parts = restored;
        // 選択中インデックスが復元後の件数を超えていたら、範囲内に収まるよう補正する。
        selectedIndex = Math.Clamp(selectedIndex, -1, parts.Count - 1);
        // 一覧・プレビュー・詳細パネルを復元後の内容で再描画する。
        RefreshList();
        UpdateUndoRedoButtons();
    }

    // 「やり直す」処理。PartsUndoの逆方向で、履歴スタックから1つ先の状態を取り出す。
    private void PartsRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        parts = restored;
        selectedIndex = Math.Clamp(selectedIndex, -1, parts.Count - 1);
        RefreshList();
        UpdateUndoRedoButtons();
    }

    // Undo/Redoボタンの有効・無効状態を、履歴管理オブジェクトの現在の状態に合わせて更新する。
    private void UpdateUndoRedoButtons()
    {
        // コンストラクタの初期化順序によっては、ボタンがまだ生成されていない段階で
        // PushHistoryが呼ばれる可能性があるため、nullチェックしてから触る。
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    // PartDefの全フィールドを1つずつコピーして独立したインスタンスを作る（ディープクローン）。
    // scriptフィールドはJArray（参照型・入れ子構造）なので、単純代入だと元と同じオブジェクトを
    // 共有してしまう。DeepClone()を使うことで、複製後にスクリプトを書き換えても元のパーツに
    // 影響しないようにしている。
    private static PartDef ClonePart(PartDef p) => new PartDef
    {
        id = p.id,
        sprite = p.sprite,
        offsetX = p.offsetX,
        offsetY = p.offsetY,
        width = p.width,
        height = p.height,
        hitboxOffsetX = p.hitboxOffsetX,
        hitboxOffsetY = p.hitboxOffsetY,
        hitboxWidth = p.hitboxWidth,
        hitboxHeight = p.hitboxHeight,
        scale = p.scale,
        hp = p.hp,
        zOrder = p.zOrder,
        script = (JArray)p.script.DeepClone(),
    };

    // ==== 左: パーツ一覧 ====

    // 左側パネル（パーツ一覧グリッドと操作ボタン群）を組み立てて返す。
    private Panel BuildListSide()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Dock = DockStyle.Top, Height = 24, Text = "📋 パーツ一覧", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 4, 0, 0) };

        // パーツ一覧を表示するグリッド本体の設定。
        dgvList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 8.5f),
            RowHeadersWidth = 20,
            ScrollBars = ScrollBars.Vertical,
            RowTemplate = { Height = 30 },
        };
        // Feature: UI改善（提案書 PT-4）— 一覧上でどの画像のパーツか一目で分かるよう、サムネイル列を追加する。
        var colThumb = new DataGridViewImageColumn { Name = "thumb", HeaderText = "", FillWeight = 22, ImageLayout = DataGridViewImageCellLayout.Zoom };
        colThumb.DefaultCellStyle.NullValue = null;
        dgvList.Columns.AddRange(new DataGridViewColumn[]
        {
            colThumb,
            new DataGridViewTextBoxColumn { Name = "id", HeaderText = "パーツID", FillWeight = 55, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "hp", HeaderText = "HP", FillWeight = 18, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "zOrder", HeaderText = "Z", FillWeight = 18, ReadOnly = true },
        });
        // 一覧グリッドで選択行が変わったら、選択インデックスを更新し、詳細パネルと
        // 合成プレビュー（選択中パーツの枠を黄色く強調表示するため）を最新状態に合わせる。
        dgvList.SelectionChanged += (s, e) =>
        {
            selectedIndex = dgvList.SelectedRows.Count > 0 ? dgvList.SelectedRows[0].Index : -1;
            LoadDetailFromSelection();
            pnlComposer.Invalidate();
        };

        // Feature: UI改善 — ボタン数が多く折り返し(WrapContents)が発生しうるため、固定Heightだと
        // 折り返した行がパネルからはみ出して見えなくなっていた。AutoSizeにして必要な行数ぶん
        // 高さが自動的に伸びるようにする（幅はDock=Bottomにより親の幅に追従する）。
        var flowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(2),
        };
        // 新規パーツを1つ追加するボタン。
        var btnAdd = new Button { Text = "＋ 追加", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnAdd.Click += (s, e) => AddPart();
        // 選択中のパーツを削除するボタン。
        var btnDel = new Button { Text = "🗑 削除", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnDel.Click += (s, e) => DeleteSelectedPart();
        // 選択中のパーツを一覧内で1つ上（描画順・重なり順の基準にもなる並び）に移動するボタン。
        var btnUp = new Button { Text = "▲ 上へ", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnUp.Click += (s, e) => MoveSelectedPart(-1);
        // 選択中のパーツを一覧内で1つ下に移動するボタン。
        var btnDown = new Button { Text = "▼ 下へ", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnDown.Click += (s, e) => MoveSelectedPart(1);
        // 選択中パーツの挙動スクリプトを、他の全パーツへコピーして一括適用するボタン。
        var btnApplyScript = new Button { Text = "🧩 このスクリプトを全パーツへ", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnApplyScript.Click += (s, e) => ApplyScriptToAllParts();
        // Feature: UI改善（提案書 PT-3）— よく似たパーツをゼロから作り直さずに済むよう、複製ボタンを追加。
        var btnDuplicate = new Button { Text = "⧉ 複製", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnDuplicate.Click += (s, e) => DuplicatePart();
        // Feature: UI改善（提案書 PT-2）— 左右対称の敵/ギミック（棘やツノなど）を素早く作れるよう、反転複製ボタンを追加。
        var btnMirror = new Button { Text = "🪞 反転複製", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnMirror.Click += (s, e) => MirrorSelectedPart();
        var btnRod = new Button { Text = "🌀 回転する棒として配置...", AutoSize = true, Padding = new Padding(6, 4, 6, 4), BackColor = Color.FromArgb(255, 244, 214) };
        btnRod.Click += (s, e) => OpenRodGenerator();
        // Feature: UI改善（提案書 PT-2）— ウィザードの拡充。振り子・公転の2種類を追加し、動きを持つ複合パーツを
        // プログラミング知識なしで組み立てられるようにする。
        var btnPendulum = new Button { Text = "🕰 振り子として配置...", AutoSize = true, Padding = new Padding(6, 4, 6, 4), BackColor = Color.FromArgb(255, 244, 214) };
        btnPendulum.Click += (s, e) => OpenPendulumGenerator();
        var btnOrbit = new Button { Text = "🛰 公転として配置...", AutoSize = true, Padding = new Padding(6, 4, 6, 4), BackColor = Color.FromArgb(255, 244, 214) };
        btnOrbit.Click += (s, e) => OpenOrbitGenerator();
        // 回転する棒/振り子/公転のいずれで作られたパーツかを自動判別して編集する統一ボタン（種類ごとにボタンを分けない）
        var btnEditMotion = new Button { Text = "🔧 動きのパターンを編集...", AutoSize = true, Padding = new Padding(6, 4, 6, 4), BackColor = Color.FromArgb(255, 244, 214) };
        btnEditMotion.Click += (s, e) => OpenMotionEditor();
        flowButtons.Controls.AddRange(new Control[] { btnAdd, btnDel, btnUp, btnDown, btnDuplicate, btnMirror, btnApplyScript, btnRod, btnPendulum, btnOrbit, btnEditMotion });

        pnl.Controls.Add(dgvList);
        pnl.Controls.Add(flowButtons);
        pnl.Controls.Add(lblTitle);
        return pnl;
    }

    // パーツ一覧グリッドの中身を、現在のpartsリストの内容で全面的に描き直す。
    // 一覧に変更を加える操作（追加/削除/並び替え等）は、最後に必ずこのメソッドを呼んで
    // 画面表示を最新状態に同期させている。
    private void RefreshList()
    {
        // 再描画後もできるだけ同じ行の選択状態を保つため、現在の選択インデックスを退避しておく。
        int keepSelected = selectedIndex;
        dgvList.Rows.Clear();
        // 各パーツについて、サムネイル画像・ID・HP・zOrderの4列分の行を追加する。
        foreach (var p in parts) dgvList.Rows.Add(GetPartThumb(p), p.id, p.hp, p.zOrder);
        if (parts.Count > 0)
        {
            // 退避しておいた選択インデックスが範囲内であればそれを、そうでなければ先頭(0)を選択する。
            int idx = Math.Clamp(keepSelected < 0 ? 0 : keepSelected, 0, parts.Count - 1);
            dgvList.ClearSelection();
            dgvList.Rows[idx].Selected = true;
            selectedIndex = idx;
        }
        else
        {
            // パーツが1つもない場合は「未選択」状態にする。
            selectedIndex = -1;
        }
        // 選択状態が変わった可能性があるため、詳細パネルと合成プレビューも合わせて更新する。
        LoadDetailFromSelection();
        pnlComposer.Invalidate();
    }

    // 新規パーツを1つ追加する。
    private void AddPart()
    {
        // "part1", "part2", ... のように、既存IDと重複しない連番のIDを自動生成する。
        string baseId = "part";
        int n = 1;
        var existing = new HashSet<string>(parts.Select(p => p.id));
        string newId;
        do { newId = $"{baseId}{n}"; n++; } while (existing.Contains(newId));
        // 位置は原点(0,0)のまま追加する。あとでユーザーがドラッグや数値入力で調整する想定。
        parts.Add(new PartDef { id = newId, offsetX = 0, offsetY = 0 });
        // 追加したパーツを自動的に選択状態にし、すぐに詳細編集に移れるようにする。
        selectedIndex = parts.Count - 1;
        RefreshList();
        PushHistory();
    }

    // 選択中のパーツを、確認ダイアログを挟んで削除する。
    private void DeleteSelectedPart()
    {
        if (selectedIndex < 0 || selectedIndex >= parts.Count) { MessageBox.Show("削除するパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        // 誤操作による削除を防ぐため、必ず確認ダイアログを挟む。
        if (MessageBox.Show($"パーツ「{parts[selectedIndex].id}」を削除しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        // サムネイルキャッシュに残ったままだとメモリリークになるため、削除前に破棄しておく。
        InvalidatePartThumb(parts[selectedIndex]);
        parts.RemoveAt(selectedIndex);
        // 削除後の件数を超えないよう選択インデックスを補正する（末尾のパーツを削除した場合など）。
        selectedIndex = Math.Min(selectedIndex, parts.Count - 1);
        RefreshList();
        PushHistory();
    }

    // 選択中のパーツを一覧内で上下に1つ移動する（並び順を入れ替える）。
    // dir : -1で上へ、+1で下へ移動する。
    private void MoveSelectedPart(int dir)
    {
        if (selectedIndex < 0 || selectedIndex >= parts.Count) return;
        int newIdx = selectedIndex + dir;
        // 移動先が一覧の範囲外（先頭より上、末尾より下）になる場合は何もしない。
        if (newIdx < 0 || newIdx >= parts.Count) return;
        // タプルの分解代入を使い、選択中パーツと移動先のパーツの位置を入れ替える。
        (parts[selectedIndex], parts[newIdx]) = (parts[newIdx], parts[selectedIndex]);
        selectedIndex = newIdx;
        RefreshList();
        PushHistory();
    }

    // 選択中パーツの挙動スクリプトを、丸ごと複製して全パーツに上書き適用する。
    // 例えば「回転する棒」のように、複数パーツが同じスクリプトを共有しつつPartIndexで
    // 個々の見た目を変える、という構成をまとめて設定したいときに使う。
    private void ApplyScriptToAllParts()
    {
        if (parts.Count == 0) { MessageBox.Show("パーツがありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (selectedIndex < 0) { MessageBox.Show("コピー元にするパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        // コピー元のスクリプトを1回だけディープクローンして取得する。
        var srcScript = (JArray)parts[selectedIndex].script.DeepClone();
        // 各パーツへは、それぞれ独立したインスタンスになるよう毎回改めてディープクローンして代入する
        // （同じJArrayインスタンスを複数パーツで共有すると、1パーツの編集が他パーツにも影響してしまうため）。
        foreach (var p in parts) p.script = (JArray)srcScript.DeepClone();
        PushHistory();

        // 適用結果が視覚的に分かるよう、全パーツの枠を一瞬（900ミリ秒）緑色に光らせる演出。
        _highlightedParts.Clear();
        foreach (var p in parts) _highlightedParts.Add(p);
        pnlComposer.Invalidate();
        // 前回のハイライト用タイマーが動いていれば止めてから、新しいワンショットタイマーを開始する。
        _highlightTimer?.Stop();
        _highlightTimer = new System.Windows.Forms.Timer { Interval = 900 };
        _highlightTimer.Tick += (s, e) => { _highlightedParts.Clear(); _highlightTimer!.Stop(); pnlComposer.Invalidate(); };
        _highlightTimer.Start();

        MessageBox.Show($"「{parts[selectedIndex].id}」のスクリプトを全{parts.Count}パーツに適用しました。\n（PartIndexレポーターを使えば、同じスクリプトのままパーツごとに異なる位相・動きを表現できます）",
            "適用完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Feature: UI改善（提案書 PT-3）— 選択中パーツをそのまま複製する（画像/当たり判定/スクリプトを全てコピー、位置だけ少しずらす）
    private void DuplicatePart()
    {
        if (selectedIndex < 0) { MessageBox.Show("複製したいパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var src = parts[selectedIndex];
        // 画像/当たり判定/スクリプトなど全フィールドをまるごとコピーする。
        var copy = ClonePart(src);
        // IDは元と同じままだと一覧上で区別できなくなるため、"_copy"を付けた重複しないIDに変更する。
        copy.id = MakeUniquePartId(src.id + "_copy");
        // 複製直後に元パーツと完全に重なって見えなくならないよう、少しだけ位置をずらす。
        copy.offsetX += 16;
        copy.offsetY += 16;
        // 元パーツのすぐ下（一覧上で隣）に挿入する。
        parts.Insert(selectedIndex + 1, copy);
        selectedIndex += 1;
        RefreshList();
        PushHistory();
    }

    // baseIdを基準に、現在のパーツ一覧内で重複しないIDを生成する。
    // baseId自体が未使用ならそのまま返し、既に使われていれば "baseId2", "baseId3", ... の
    // ように末尾へ連番を付けて重複しなくなるまで探す。
    private string MakeUniquePartId(string baseId)
    {
        var existing = new HashSet<string>(parts.Select(p => p.id));
        if (!existing.Contains(baseId)) return baseId;
        int n = 2;
        string id;
        do { id = $"{baseId}{n}"; n++; } while (existing.Contains(id));
        return id;
    }

    // Feature: UI改善（提案書 PT-2）— 選択中パーツを左右反転して複製する。棘やツノなど左右対称の装飾を
    // 片側だけ作ってワンクリックでもう片方を得られるようにする。静的なoffsetXだけでなく、
    // 挙動スクリプト内のSetLocalOffset(dx)/SetLocalOffsetPolar(angle)も再帰的に反転させるため、
    // 回転する棒/振り子/公転のいずれで生成したパーツでも正しく鏡写しになる。
    private void MirrorSelectedPart()
    {
        if (selectedIndex < 0) { MessageBox.Show("反転複製したいパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var src = parts[selectedIndex];
        var copy = ClonePart(src);
        // IDの命名規則: 元IDが"_L"で終わっていれば"_R"に、"_R"で終わっていれば"_L"に付け替える
        // （左右のペアだと分かりやすくするため）。どちらでもなければ単純に"_mirror"を付ける。
        string mirroredBaseId =
            src.id.EndsWith("_L", StringComparison.Ordinal) ? src.id[..^2] + "_R" :
            src.id.EndsWith("_R", StringComparison.Ordinal) ? src.id[..^2] + "_L" :
            src.id + "_mirror";
        copy.id = MakeUniquePartId(mirroredBaseId);
        // 静的な位置（offsetX）と当たり判定のオフセットを左右反転する。
        copy.offsetX = -copy.offsetX;
        copy.hitboxOffsetX = -copy.hitboxOffsetX;
        // 静的な値だけでなく、挙動スクリプト内で動的に位置を決めている部分（SetLocalOffset系）も
        // 再帰的に反転させる。これにより回転する棒/振り子/公転のいずれで生成したパーツでも、
        // 動きまで含めて正しく鏡写しになる。
        MirrorScriptHorizontalInPlace(copy.script);
        parts.Insert(selectedIndex + 1, copy);
        selectedIndex += 1;
        RefreshList();
        PushHistory();
        MessageBox.Show($"「{src.id}」を左右反転して「{copy.id}」として複製しました。",
            "反転複製完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // 挙動スクリプトの最上位（各"hat"ブロック）をたどり、その本体(body)を反転処理にかける。
    private static void MirrorScriptHorizontalInPlace(JArray script)
    {
        foreach (var tok in script)
        {
            if (tok is not JObject hat) continue;
            if (hat["body"] is JArray body) MirrorSequence(body);
        }
    }

    // 命令列を1つずつ調べ、位置を横方向に決めている命令（SetLocalOffset/SetLocalOffsetPolar）を
    // 見つけたら、その式を書き換えて左右反転させる。制御構文（Forever/Repeat/If等）の中身も
    // 再帰的に処理することで、ネストした命令列の奥深くにある位置指定も漏れなく反転する。
    private static void MirrorSequence(JArray seq)
    {
        foreach (var tok in seq)
        {
            if (tok is not JObject node) continue;
            string op = node["op"]?.ToString() ?? "";
            switch (op)
            {
                case "SetLocalOffset":
                    // dx（横方向の距離）に -1 を掛ける式でラップし、常に元の値と符号が逆になるようにする。
                    if (node["dx"] is JToken dx) node["dx"] = new JObject { ["op"] = "Mul", ["a"] = -1, ["b"] = dx.DeepClone() };
                    break;
                case "SetLocalOffsetPolar":
                    // 極座標（角度＋半径）の場合は、角度を「π - 元の角度」に置き換えることで
                    // 横方向（X軸）を軸にした鏡写しの角度になる。
                    if (node["angle"] is JToken angle) node["angle"] = new JObject { ["op"] = "Sub", ["a"] = Math.PI, ["b"] = angle.DeepClone() };
                    break;
                case "Forever":
                case "Repeat":
                case "RepeatUntil":
                    // ループ構文の中身にも同じ反転処理を再帰適用する。
                    if (node["body"] is JArray innerBody) MirrorSequence(innerBody);
                    break;
                case "If":
                case "IfElse":
                    // 分岐構文は then節・else節の両方を反転対象にする。
                    if (node["body"] is JArray thenBody) MirrorSequence(thenBody);
                    if (node["else"] is JArray elseBody) MirrorSequence(elseBody);
                    break;
            }
        }
    }

    // ==== 中央: 合成プレビューキャンバス ====

    // 中央パネル（合成プレビューのキャンバスと再生バー）を組み立てて返す。
    private Panel BuildComposerSide()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Dock = DockStyle.Top, Height = 24, Text = "🖼 合成プレビュー（パーツをドラッグして配置）", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 4, 0, 0) };

        // Feature: UI改善（提案書 PT-1）— 保存してゲームを起動しなくても、その場で動きを確認できる再生バー
        var pnlPlayback = new Panel { Dock = DockStyle.Top, Height = 30 };
        var flowPlayback = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4, 2, 4, 2) };
        // 再生/停止を切り替えるトグルボタン。
        _btnPlayToggle = new Button { Text = "▶ 再生", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        _btnPlayToggle.Click += (s, e) => TogglePreviewPlayback();
        // 経過時間を0に巻き戻すボタン（再生を止めずに巻き戻すことも可能）。
        var btnResetTime = new Button { Text = "⏮ 0に戻す", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        btnResetTime.Click += (s, e) => { _previewTime = 0f; pnlComposer.Invalidate(); };
        var lblPreviewHint = new Label
        {
            AutoSize = true,
            Text = "挙動スクリプトの動きをここで確認できます（停止中はドラッグで配置換えできます）",
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, 7.5f),
            Margin = new Padding(10, 8, 0, 0),
        };
        flowPlayback.Controls.AddRange(new Control[] { _btnPlayToggle, btnResetTime, lblPreviewHint });
        pnlPlayback.Controls.Add(flowPlayback);

        // 合成プレビューを実際に描画するキャンバス。背景をダークグレーにして、
        // 明るい色のパーツ画像が見やすいようにしている。
        pnlComposer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(50, 50, 55) };
        pnlComposer.Paint += PnlComposer_Paint;
        pnlComposer.MouseDown += PnlComposer_MouseDown;
        pnlComposer.MouseMove += PnlComposer_MouseMove;
        // マウスを離した時点でドラッグ中だった場合のみ、その配置変更を履歴に1件記録する
        // （ドラッグ中の1フレームごとに記録すると履歴が大量に積まれてしまうため、確定時のみ記録する）。
        pnlComposer.MouseUp += (s, e) => { if (_draggingIndex >= 0) PushHistory(); _draggingIndex = -1; };
        // パネルのサイズが変わったら（ウィンドウリサイズ・スプリッター調整など）再描画する。
        pnlComposer.Resize += (s, e) => pnlComposer.Invalidate();

        pnl.Controls.Add(pnlComposer);
        pnl.Controls.Add(lblTitle);
        pnl.Controls.Add(pnlPlayback);
        return pnl;
    }

    // 再生バーの「▶ 再生」/「⏸ 停止」ボタンが押されたときに、再生状態を反転させる。
    private void TogglePreviewPlayback()
    {
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            _btnPlayToggle.Text = "⏸ 停止";
            // タイマーが未作成であれば、ここで初めて生成する（初回再生時に1度だけ作られる）。
            if (_previewTimer == null)
            {
                _previewTimer = new System.Windows.Forms.Timer { Interval = 16 }; // 実機と同じ約60fps相当でTimeを進める
                // Tickのたびに経過時間を1フレーム分進めて、キャンバスを再描画する。
                _previewTimer.Tick += (s, e) => { _previewTime += 1.0f; pnlComposer.Invalidate(); };
            }
            _previewTimer.Start();
        }
        else
        {
            _btnPlayToggle.Text = "▶ 再生";
            _previewTimer?.Stop();
        }
        pnlComposer.Invalidate();
    }

    // 本体の基準スプライト画像をファイルから読み込む。合成プレビューの中心に薄く表示し、
    // 各パーツの位置関係を把握しやすくするための目印として使う。
    private void LoadBaseSprite()
    {
        if (string.IsNullOrEmpty(baseSpritePath)) return;
        string full = Path.Combine(projectRoot, baseSpritePath.Replace('/', '\\'));
        if (!File.Exists(full)) return;
        try
        {
            // 他プロセス（ゲーム本体等）が同じファイルを開いていても読み込めるよう、
            // 共有読み取り(FileShare.Read)モードでストリームを開いてから画像化する。
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            baseSprite = Image.FromStream(fs);
        }
        catch { baseSprite = null; } // 画像が壊れている等で読み込みに失敗しても、致命的エラーにはせず「画像なし」として続行する
    }

    // 指定パーツのサムネイル画像を取得する。一度読み込んだ画像はキャッシュされ、
    // 同じパーツについて2回目以降はファイルI/Oを行わずキャッシュから即座に返す。
    private Image? GetPartThumb(PartDef p)
    {
        if (_partThumbCache.TryGetValue(p, out var cached)) return cached;
        Image? img = null;
        if (!string.IsNullOrEmpty(p.sprite))
        {
            string full = Path.Combine(projectRoot, p.sprite.Replace('/', '\\'));
            if (File.Exists(full))
            {
                try
                {
                    using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                    img = Image.FromStream(fs);
                }
                catch { img = null; } // 読み込み失敗時はサムネイルなし（後述の描画側でオレンジの丸に代替表示される）
            }
        }
        // 画像が見つからなかった場合も含めて、結果（nullでも）をキャッシュしておくことで
        // 存在しない画像パスに対して毎回無駄なファイルアクセスを繰り返さないようにする。
        _partThumbCache[p] = img;
        return img;
    }

    // 指定パーツのサムネイルキャッシュを破棄する。画像を差し替えた時や、パーツ自体を削除する時に
    // 呼び出し、古い画像リソースをDisposeしてメモリを解放する。
    private void InvalidatePartThumb(PartDef p)
    {
        if (_partThumbCache.TryGetValue(p, out var old)) { old?.Dispose(); _partThumbCache.Remove(p); }
    }

    // 合成プレビューキャンバス内で、本体の基準スプライトを描画すべき矩形（位置とサイズ）を計算する。
    // キャンバスの中央に、幅・高さそれぞれの50%に収まるようアスペクト比を保ったまま拡大縮小する。
    private Rectangle GetBaseDrawRect()
    {
        // キャンバスがまだ実サイズを持っていない（レイアウト確定前）場合は、ダミーの極小矩形を返す。
        if (pnlComposer.Width <= 0 || pnlComposer.Height <= 0) return new Rectangle(0, 0, 1, 1);
        // 基準スプライトが存在しない場合は、中央に小さな点（8x8）だけを描く位置を返す。
        if (baseSprite == null) return new Rectangle(pnlComposer.Width / 2 - 4, pnlComposer.Height / 2 - 4, 8, 8);
        // 横方向・縦方向それぞれで「キャンバスの半分に収まる倍率」を計算し、小さい方（＝両方に収まる倍率）を採用する。
        float scale = Math.Min((float)pnlComposer.Width * 0.5f / baseSprite.Width, (float)pnlComposer.Height * 0.5f / baseSprite.Height);
        // 極端に小さい画像を拡大しすぎてぼやけないよう、倍率の上限を10倍に制限する。
        if (scale > 10) scale = 10;
        // 万一マイナスや0になった場合は等倍にフォールバックする（安全策）。
        if (scale <= 0) scale = 1;
        int drawW = Math.Max((int)(baseSprite.Width * scale), 1);
        int drawH = Math.Max((int)(baseSprite.Height * scale), 1);
        // キャンバスの中央に配置されるよう、左上座標を計算する。
        int drawX = (pnlComposer.Width - drawW) / 2;
        int drawY = (pnlComposer.Height - drawH) / 2;
        return new Rectangle(drawX, drawY, drawW, drawH);
    }

    // ワールド座標（パーツのoffsetX/offsetY、本体中心を原点とする論理座標）を、
    // 画面上の実ピクセル座標に変換する。baseRectの左上を原点とし、scale倍して加算する。
    private PointF WorldToScreen(Rectangle baseRect, float scale, float ox, float oy)
        => new PointF(baseRect.X + ox * scale, baseRect.Y + oy * scale);

    // 合成プレビューキャンバスの描画本体。毎フレーム（Invalidateされるたび）呼び出され、
    // 「本体の基準スプライト → 各パーツ（zOrder順）→ 再生中の場合の情報表示 → パーツ0件時の案内」
    // の順に描き重ねていく。
    private void PnlComposer_Paint(object? sender, PaintEventArgs e)
    {
        // ドット絵をぼかさずくっきり拡大表示するため、補間モードを最近傍法にする。
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var baseRect = GetBaseDrawRect();
        // 基準スプライトの実サイズに対する表示倍率。以降、全パーツの座標変換にもこの倍率を使う。
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;

        // 本体の基準スプライトを薄く表示し、外枠と原点（水色の点）を描いて位置の目安にする。
        if (baseSprite != null) e.Graphics.DrawImage(baseSprite, baseRect);
        e.Graphics.DrawRectangle(Pens.DimGray, baseRect);
        using var originBrush = new SolidBrush(Color.FromArgb(200, 0, 255, 255));
        e.Graphics.FillEllipse(originBrush, baseRect.X - 3, baseRect.Y - 3, 6, 6);

        // zOrderの小さい順（奥から手前へ）に描画することで、値が大きいパーツが上に重なって見えるようにする。
        var order = Enumerable.Range(0, parts.Count).OrderBy(i => parts[i].zOrder).ToList();
        foreach (int i in order)
        {
            var p = parts[i];
            // 通常時（停止中）はパーツに設定された固定のoffsetX/offsetYをそのまま使う。
            float ox = p.offsetX, oy = p.offsetY, angleRad = 0f;
            if (_isPlaying)
            {
                // 再生中は挙動スクリプトを現在時刻(_previewTime)とパーツ番号(i=PartIndex)で評価し、
                // スクリプトが位置や角度を動的に変えている場合はその評価結果で上書きする。
                var pose = ScriptPreviewEvaluator.Evaluate(p.script, _previewTime, i);
                if (pose.HasOffset) { ox = pose.OffsetX; oy = pose.OffsetY; }
                if (pose.HasAngle) angleRad = pose.Angle;
            }
            // ワールド座標を画面座標に変換し、サムネイル画像のサイズ（画像自体のサイズ×表示倍率×パーツのscale値）から
            // 描画矩形を中心合わせで計算する。
            var pos = WorldToScreen(baseRect, scale, ox, oy);
            var thumb = GetPartThumb(p);
            int pw = Math.Max((int)((thumb?.Width ?? 24) * scale * p.scale), 10);
            int ph = Math.Max((int)((thumb?.Height ?? 24) * scale * p.scale), 10);
            var rect = new Rectangle((int)pos.X - pw / 2, (int)pos.Y - ph / 2, pw, ph);

            // 回転角度がある場合は、パーツの中心を軸に回転描画するため一時的に座標系を
            // 「中心へ平行移動→回転→元に戻す」という変換にしてから描き、描画後に元の座標系へ復元する。
            var savedState = angleRad != 0f ? e.Graphics.Save() : null;
            if (angleRad != 0f)
            {
                e.Graphics.TranslateTransform(pos.X, pos.Y);
                e.Graphics.RotateTransform(angleRad * 180f / MathF.PI);
                e.Graphics.TranslateTransform(-pos.X, -pos.Y);
            }
            // サムネイル画像があればそれを描画し、なければ「画像未設定」を表すオレンジ色の丸で代替表示する。
            if (thumb != null) e.Graphics.DrawImage(thumb, rect);
            else
            {
                using var b = new SolidBrush(Color.FromArgb(160, 255, 140, 0));
                e.Graphics.FillEllipse(b, rect);
            }
            if (savedState != null) e.Graphics.Restore(savedState);

            // 「このスクリプトを全パーツへ」適用直後などにハイライト対象になっているパーツは、
            // 通常の枠の外側にもう一段太い緑の枠を追加で描いて目立たせる。
            if (_highlightedParts.Contains(p))
            {
                using var glowPen = new Pen(Color.Lime, 3f);
                e.Graphics.DrawRectangle(glowPen, rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
            }
            // 選択中のパーツは黄色の太枠、それ以外は白っぽい細枠で囲む。
            using var pen = new Pen(i == selectedIndex ? Color.Yellow : Color.FromArgb(200, 255, 255, 255), i == selectedIndex ? 2.5f : 1.2f);
            e.Graphics.DrawRectangle(pen, rect);
            // パーツの直下にIDを小さな文字で表示し、どのマーカーがどのパーツかを一覧と対応づけやすくする。
            using var smallFont = new Font(Font.FontFamily, 7.5f);
            e.Graphics.DrawString(p.id, smallFont, Brushes.White, rect.X, rect.Bottom + 1);
        }

        // 再生中であることと現在の経過時間を、キャンバス左上に文字で表示する。
        if (_isPlaying)
        {
            using var playFont = new Font(Font.FontFamily, 8f, FontStyle.Bold);
            e.Graphics.DrawString($"再生中... t={_previewTime:0}", playFont, Brushes.LightGreen, 6, 6);
        }

        // パーツが1つもない場合は、空のキャンバスのままだと何をすればいいか分からないため、
        // 操作方法を案内するメッセージを表示する。
        if (parts.Count == 0)
        {
            using var f = new Font(Font.FontFamily, 10f);
            var msg = "パーツがありません。左の「＋ 追加」または「🌀 回転する棒として配置...」から作成してください。";
            var sz = e.Graphics.MeasureString(msg, f, pnlComposer.Width - 20);
            e.Graphics.DrawString(msg, f, Brushes.LightGray, new RectangleF(10, 10, pnlComposer.Width - 20, sz.Height + 10));
        }
    }

    // 画面座標ptの位置にあるパーツマーカーを探し、そのパーツのインデックスを返す（見つからなければ-1）。
    // zOrderが大きい（手前に描かれている）パーツから優先的に判定することで、パーツ同士が重なっている
    // 場合でも「見た目上、一番手前にあるもの」がクリックされたと判定されるようにしている。
    private int FindPartMarkerAt(Point pt)
    {
        var baseRect = GetBaseDrawRect();
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;
        var order = Enumerable.Range(0, parts.Count).OrderByDescending(i => parts[i].zOrder).ToList();
        foreach (int i in order)
        {
            var p = parts[i];
            var pos = WorldToScreen(baseRect, scale, p.offsetX, p.offsetY);
            var thumb = GetPartThumb(p);
            int pw = Math.Max((int)((thumb?.Width ?? 24) * scale * p.scale), 10);
            int ph = Math.Max((int)((thumb?.Height ?? 24) * scale * p.scale), 10);
            var rect = new Rectangle((int)pos.X - pw / 2, (int)pos.Y - ph / 2, pw, ph);
            if (rect.Contains(pt)) return i;
        }
        return -1;
    }

    // キャンバス上でマウスボタンが押された時の処理。クリックされた位置にパーツがあれば、
    // そのパーツを選択状態にしつつドラッグ開始の準備（開始座標とその時点でのoffset値を記録）を行う。
    private void PnlComposer_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_isPlaying) return; // 再生中は「今どこにいるか」が動的に変わるため、配置換えは停止してから行う
        int idx = FindPartMarkerAt(e.Location);
        if (idx < 0) return; // パーツのない場所をクリックした場合は何もしない（ドラッグ開始しない）
        _draggingIndex = idx;
        _dragMouseStart = e.Location;
        _dragOffsetStartX = parts[idx].offsetX;
        _dragOffsetStartY = parts[idx].offsetY;
        // クリックしたパーツを一覧側でも選択状態にし、詳細パネルにも反映させる（一覧とキャンバスの選択状態を同期させる）。
        selectedIndex = idx;
        dgvList.ClearSelection();
        if (idx < dgvList.Rows.Count) dgvList.Rows[idx].Selected = true;
        LoadDetailFromSelection();
    }

    // マウスがドラッグされている間、ドラッグ中のパーツの位置をマウス移動量に応じて更新し続ける。
    private void PnlComposer_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingIndex < 0) return; // ドラッグ中でなければ何もしない
        var baseRect = GetBaseDrawRect();
        float scale = baseSprite != null ? (float)baseRect.Width / baseSprite.Width : 1.0f;
        if (scale <= 0) return;
        // 画面上のマウス移動量（ピクセル）を、表示倍率で割ってワールド座標系での移動量に変換する。
        float dx = (e.Location.X - _dragMouseStart.X) / scale;
        float dy = (e.Location.Y - _dragMouseStart.Y) / scale;
        // ドラッグ開始時点のoffset値に移動量を加算する（開始位置からの差分方式にすることで、
        // ドラッグ中に微小なずれが累積することなく正確に追従する）。
        parts[_draggingIndex].offsetX = _dragOffsetStartX + dx;
        parts[_draggingIndex].offsetY = _dragOffsetStartY + dy;
        // ドラッグ中のパーツが選択中パーツと同じであれば、詳細パネルの数値表示もリアルタイムに更新する。
        if (_draggingIndex == selectedIndex) LoadDetailFromSelection();
        pnlComposer.Invalidate();
    }

    // ==== 右下: 選択中パーツの詳細編集パネル ====

    // 右下パネル（選択中パーツの詳細編集フォーム）を組み立てて返す。
    private Panel BuildDetailSide()
    {
        // AutoScroll付きのPanel＋TopDown方向のFlowLayoutPanelで縦積みにすることで、
        // 「Dock=Topを複数並べた時の重なり順の曖昧さ」を避け、追加した順番どおりに必ず
        // 上から並ぶようにしている。ウィンドウが小さくても内容が入り切らない場合は
        // 自動的に縦スクロールバーが出るため、項目が見切れることはない。
        var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var lblTitle = new Label { AutoSize = true, Text = "🔧 選択中パーツの詳細", Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 4, 0, 4) };

        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(6),
            Margin = new Padding(0),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ラベル＋入力コントロールを1行としてtableに追加するローカル関数。
        // 呼び出すたびに行を1つ増やし、左列にラベル・右列にコントロールを配置する。
        void AddRow(string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(lbl, 0, r);
            table.Controls.Add(control, 1, r);
        }

        // --- パーツID ---
        txtId = new TextBox { Width = 180 };
        // 入力中はリアルタイムでID・一覧・プレビューに反映するが、履歴には積まない（下記コメント参照）。
        txtId.TextChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].id = txtId.Text; dgvList.Rows[selectedIndex].Cells["id"].Value = txtId.Text; pnlComposer.Invalidate(); } };
        // IDはキー入力のたびに履歴を積むと1文字ごとにUndo項目が増えてしまうため、確定タイミング(フォーカスを外した時)でのみ記録する
        txtId.Leave += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) PushHistory(); };
        AddRow("パーツID", txtId);

        // --- 画像（スプライト） ---
        var spritePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        lblSpriteValue = new Label { Text = "(画像なし)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3), MaximumSize = new Size(220, 0) };
        var btnPickSprite = new Button { Text = "📁 画像選択", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnPickSprite.Click += (s, e) => PickSpriteForSelected();
        spritePanel.Controls.AddRange(new Control[] { lblSpriteValue, btnPickSprite });
        AddRow("画像", spritePanel);

        // --- 横位置(offsetX) --- 値を変更するたびに即座にプレビューへ反映し、履歴にも記録する。
        nudOffsetX = MakeNumeric(-4000, 4000, 0);
        nudOffsetX.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].offsetX = (float)nudOffsetX.Value; pnlComposer.Invalidate(); PushHistory(); } };
        AddRow("offsetX(横位置)", nudOffsetX);

        // --- 縦位置(offsetY) ---
        nudOffsetY = MakeNumeric(-4000, 4000, 0);
        nudOffsetY.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].offsetY = (float)nudOffsetY.Value; pnlComposer.Invalidate(); PushHistory(); } };
        AddRow("offsetY(縦位置)", nudOffsetY);

        // --- 表示スケール（画像の拡大縮小率。1.0が等倍） ---
        nudScale = MakeNumeric(0.1m, 10m, 2, 1m);
        nudScale.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].scale = (float)nudScale.Value; pnlComposer.Invalidate(); PushHistory(); } };
        AddRow("表示スケール", nudScale);

        // --- HP（このパーツ単体の耐久力。本体のHP/生死とは別枠） ---
        nudHp = MakeNumeric(0, 999, 0);
        nudHp.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].hp = (int)nudHp.Value; dgvList.Rows[selectedIndex].Cells["hp"].Value = (int)nudHp.Value; UpdateHpHint(); PushHistory(); } };
        AddRow("HP", nudHp);

        // HPの意味（0=不滅か、何発で壊れるか）を文章で説明する補助ラベル。ラベル列は空にして値だけ全幅で表示する。
        lblHpHint = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7.5f), Anchor = AnchorStyles.Left };
        AddRow("", lblHpHint);

        // --- zOrder（描画の重なり順。値が大きいほど手前に表示される） ---
        nudZOrder = MakeNumeric(-100, 100, 0);
        nudZOrder.ValueChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].zOrder = (int)nudZOrder.Value; dgvList.Rows[selectedIndex].Cells["zOrder"].Value = (int)nudZOrder.Value; pnlComposer.Invalidate(); PushHistory(); } };
        AddRow("zOrder(奥-/手前+)", nudZOrder);

        // --- ドリルダウン系の編集ボタン（当たり判定・挙動スクリプトは別画面で編集する） ---
        var flowActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(6), Margin = new Padding(0) };
        var btnHitbox = new Button { Text = "🎯 当たり判定を編集", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnHitbox.Click += (s, e) => EditHitboxForSelected();
        var btnScript = new Button { Text = "📝 挙動スクリプトを編集", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        btnScript.Click += (s, e) => EditScriptForSelected();
        flowActions.Controls.AddRange(new Control[] { btnHitbox, btnScript });

        flow.Controls.Add(lblTitle);
        flow.Controls.Add(table);
        flow.Controls.Add(flowActions);
        pnl.Controls.Add(flow);
        return pnl;
    }

    // NumericUpDownコントロールを、最小値・最大値・小数桁数・初期値を指定して1行で生成するヘルパー。
    // 同じような設定を何度も書かずに済ませるためのショートカット。
    private static NumericUpDown MakeNumeric(decimal min, decimal max, int decimals, decimal value = 0)
        => new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value, Width = 120 };

    // 選択中パーツ(parts[selectedIndex])の値を、詳細パネルの各入力コントロールに反映する。
    // 一覧やキャンバスでの選択が変わるたびに呼ばれ、右下パネルの表示をその場で選ばれたパーツの内容に合わせる。
    private void LoadDetailFromSelection()
    {
        bool hasSel = selectedIndex >= 0 && selectedIndex < parts.Count;
        // ここでコントロールの値を設定すると各種ValueChanged/TextChangedイベントが発火してしまうが、
        // これはユーザー操作ではなく「表示の同期」のためだけなので、履歴に積んだり二重更新したりしないよう
        // 一時的にイベント処理を抑制するフラグを立てる。
        _suppressEvents = true;
        if (hasSel)
        {
            var p = parts[selectedIndex];
            txtId.Text = p.id;
            lblSpriteValue.Text = string.IsNullOrEmpty(p.sprite) ? "(画像なし)" : p.sprite;
            // NumericUpDownのMinimum/Maximumを超える値を設定しようとすると例外になるため、
            // Math.Clampで必ず範囲内に収めてから代入する（パーツ側の値が想定外に大きい場合の保険）。
            nudOffsetX.Value = (decimal)Math.Clamp(p.offsetX, (float)nudOffsetX.Minimum, (float)nudOffsetX.Maximum);
            nudOffsetY.Value = (decimal)Math.Clamp(p.offsetY, (float)nudOffsetY.Minimum, (float)nudOffsetY.Maximum);
            nudScale.Value = (decimal)Math.Clamp(p.scale, (float)nudScale.Minimum, (float)nudScale.Maximum);
            nudHp.Value = Math.Clamp(p.hp, (int)nudHp.Minimum, (int)nudHp.Maximum);
            nudZOrder.Value = Math.Clamp(p.zOrder, (int)nudZOrder.Minimum, (int)nudZOrder.Maximum);
            UpdateHpHint();
        }
        else
        {
            // パーツが選択されていない場合は、全項目を初期値表示にリセットする。
            txtId.Text = "";
            lblSpriteValue.Text = "(パーツ未選択)";
            nudOffsetX.Value = 0; nudOffsetY.Value = 0; nudScale.Value = 1; nudHp.Value = 0; nudZOrder.Value = 0;
            lblHpHint.Text = "";
        }
        // 未選択の間は編集しても意味がないため、全ての入力コントロールを無効化してユーザーに
        // 「まずパーツを選んでください」という状態を視覚的に伝える。
        txtId.Enabled = lblSpriteValue.Enabled = nudOffsetX.Enabled = nudOffsetY.Enabled = nudScale.Enabled = nudHp.Enabled = nudZOrder.Enabled = hasSel;
        _suppressEvents = false;
    }

    // 現在のHP入力値に応じて、その意味を説明する補助テキストを更新する。
    // HP=0は「常時存在する破壊不能な障害物」、それ以外は「弾を何発当てれば壊れるか」を表す。
    private void UpdateHpHint()
    {
        int hp = (int)nudHp.Value;
        lblHpHint.Text = hp == 0 ? "0 = 破壊不能な常在ハザード" : $"{hp} = 弾{hp}発で破壊可能（本体のHP/生死には影響しません）";
    }

    // 選択中パーツの画像ファイルをファイル選択ダイアログで選ばせ、プロジェクトのimg/フォルダへ
    // コピーした上でパーツに設定する。
    private void PickSpriteForSelected()
    {
        if (selectedIndex < 0) { MessageBox.Show("先にパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "パーツ画像を選択" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        // プロジェクト外のファイルを直接参照すると後で場所を移動された時に壊れるため、
        // 必ずimg/フォルダにコピーしてから、その相対パスをパーツに記録する。
        string relPath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
        var p = parts[selectedIndex];
        p.sprite = relPath;
        // 画像を差し替えたので、古いサムネイルキャッシュは無効化して次回描画時に再読み込みさせる。
        InvalidatePartThumb(p);
        lblSpriteValue.Text = relPath;
        pnlComposer.Invalidate();
        PushHistory();
    }

    // 「当たり判定を編集」ボタンの処理。このページ内では編集せず、専用の当たり判定編集画面へ
    // ドリルダウンするようイベントで要求する。編集完了時のコールバックでパーツに値を書き戻す。
    private void EditHitboxForSelected()
    {
        if (selectedIndex < 0) { MessageBox.Show("先にパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var p = parts[selectedIndex];
        string full = string.IsNullOrEmpty(p.sprite) ? "" : Path.Combine(projectRoot, p.sprite.Replace('/', '\\'));
        // 現在の当たり判定の値を渡して編集画面を開いてもらい、確定されたらコールバックで
        // 新しい値を受け取ってパーツに反映し、履歴に記録する。
        HitboxEditRequested?.Invoke(full, p.hitboxOffsetX, p.hitboxOffsetY, p.hitboxWidth, p.hitboxHeight, (ox, oy, w, h) =>
        {
            p.hitboxOffsetX = ox;
            p.hitboxOffsetY = oy;
            p.hitboxWidth = w;
            p.hitboxHeight = h;
            PushHistory();
        });
    }

    // 「挙動スクリプトを編集」ボタンの処理。当たり判定編集と同様、専用のスクリプト編集画面へ
    // ドリルダウンし、確定されたスクリプトをパーツに書き戻す。
    private void EditScriptForSelected()
    {
        if (selectedIndex < 0) { MessageBox.Show("先にパーツを選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var p = parts[selectedIndex];
        BehaviorScriptEditRequested?.Invoke($"パーツ: {p.id}", p.script, script => { p.script = script; PushHistory(); });
    }

    // ==== 🌀 回転する棒として配置（ジェネレータ）／🔧 既存の棒を編集 ====

    // フォームの入力値から、指定した開始インデックス(全体配列内での通し番号)を基準にパーツ一式を生成する。
    // 新規作成でも既存の棒の再生成でも、この1メソッドを共有する。
    private List<PartDef> GenerateRodParts(RodGeneratorForm form, int startIndexForPhase)
    {
        var existing = new HashSet<string>(parts.Select(p => p.id));
        var newParts = new List<PartDef>();
        for (int i = 0; i < form.Count; i++)
        {
            string baseId = form.IdPrefix;
            string id = $"{baseId}{i}";
            int n = 1;
            while (existing.Contains(id)) { id = $"{baseId}{i}_{n}"; n++; }
            existing.Add(id);

            var pd = new PartDef
            {
                id = id,
                sprite = form.SpritePath,
                width = form.PartSize,
                height = form.PartSize,
                hitboxWidth = form.PartSize,
                hitboxHeight = form.PartSize,
                hp = form.Hp,
                zOrder = form.ZOrder,
            };
            // 「同じ角度・パーツごとに異なる半径」で並べることで、球が一直線に並んで回転する棒（ファイアバー等）を再現する。
            // 半径は (このパーツの全体配列インデックス+1) * 間隔 として、既存パーツを含めた通し番号で計算する。
            int overallIndex = startIndexForPhase + i;
            pd.offsetX = form.Spacing * (overallIndex + 1);
            pd.offsetY = 0;
            var angleExpr = new JObject { ["op"] = "Mul", ["a"] = new JObject { ["op"] = "Time" }, ["b"] = form.Speed };
            var radiusExpr = new JObject
            {
                ["op"] = "Mul",
                ["a"] = new JObject { ["op"] = "Add", ["a"] = new JObject { ["op"] = "PartIndex" }, ["b"] = 1 },
                ["b"] = form.Spacing,
            };
            var body = new JArray {
                new JObject {
                    ["op"] = "Forever",
                    ["body"] = new JArray {
                        new JObject { ["op"] = "SetLocalOffsetPolar", ["angle"] = angleExpr, ["radius"] = radiusExpr },
                        new JObject { ["op"] = "Wait", ["frames"] = 1 },
                    }
                }
            };
            pd.script = new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = body } };
            newParts.Add(pd);
        }
        return newParts;
    }

    private void OpenRodGenerator()
    {
        using var form = new RodGeneratorForm(projectRoot);
        if (form.ShowDialog() != DialogResult.OK) return;

        var newParts = GenerateRodParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        PushHistory();
        MessageBox.Show($"{newParts.Count}個のパーツを「回転する棒」として配置しました。\n（半径はパーツの並び順(PartIndex)に応じて自動的に増えていくため、全て同じスクリプトを共有しても一直線に並んで回転します）",
            "生成完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // 選択中のパーツが「🌀 回転する棒として配置」で作られた棒の一部かどうかを判定し、
    // 同じ棒に属する他のパーツ（idの接頭辞が同じ かつ 同じ速さ・間隔のSetLocalOffsetPolarスクリプトを持つ）をまとめて検出する。
    private RodGroupInfo? DetectRodGroup(int fromIndex)
    {
        if (fromIndex < 0 || fromIndex >= parts.Count) return null;
        var seed = parts[fromIndex];
        if (!RodGroupInfo.TryParseRodScript(seed.script, out float speed, out float spacing)) return null;

        string id = seed.id;
        int cut = id.Length;
        while (cut > 0 && char.IsDigit(id[cut - 1])) cut--;
        string prefix = id.Substring(0, cut);
        if (string.IsNullOrEmpty(prefix)) return null;

        var info = new RodGroupInfo { IdPrefix = prefix, Spacing = spacing, Speed = speed, Hp = seed.hp, ZOrder = seed.zOrder, PartSize = seed.width > 0 ? seed.width : 12, SpritePath = seed.sprite };
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (!p.id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!RodGroupInfo.TryParseRodScript(p.script, out float s2, out float sp2)) continue;
            if (Math.Abs(s2 - speed) > 0.0001f || Math.Abs(sp2 - spacing) > 0.0001f) continue;
            info.Indices.Add(i);
        }
        return info.Indices.Count > 0 ? info : null;
    }

    private void EditRodGroup(RodGroupInfo group)
    {
        using var form = new RodGeneratorForm(projectRoot, group);
        if (form.ShowDialog() != DialogResult.OK) return;

        // 既存メンバーを削除してから同じパラメータ体系で再生成する（並び順は末尾に移動する）
        foreach (var idx in group.Indices.OrderByDescending(i => i)) parts.RemoveAt(idx);
        var newParts = GenerateRodParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        PushHistory();
        MessageBox.Show($"「回転する棒」を{newParts.Count}個のパーツに更新しました。\n（更新後はパーツ一覧の末尾に移動しています）",
            "更新完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ==== 🕰 振り子として配置（ジェネレータ）/編集 ====
    // Feature: UI改善（提案書 PT-2）— 回転する棒と同じ「同じスクリプトを共有し、PartIndexで半径だけ変える」
    // 仕組みを流用し、角度を Time*速さ ではなく 基準角度 + sin(Time*速さ)*振れ幅 にすることで、
    // 一直線に並んだまま行ったり来たり揺れる振り子（吊り橋の重り・シャンデリア等）を表現する。

    private List<PartDef> GeneratePendulumParts(PendulumGeneratorForm form, int startIndexForPhase)
    {
        var existing = new HashSet<string>(parts.Select(p => p.id));
        var newParts = new List<PartDef>();
        for (int i = 0; i < form.Count; i++)
        {
            string baseId = form.IdPrefix;
            string id = $"{baseId}{i}";
            int n = 1;
            while (existing.Contains(id)) { id = $"{baseId}{i}_{n}"; n++; }
            existing.Add(id);

            var pd = new PartDef
            {
                id = id,
                sprite = form.SpritePath,
                width = form.PartSize,
                height = form.PartSize,
                hitboxWidth = form.PartSize,
                hitboxHeight = form.PartSize,
                hp = form.Hp,
                zOrder = form.ZOrder,
            };
            int overallIndex = startIndexForPhase + i;
            // t=0時点の静止姿勢に近い値を初期値としておく（実際の位置は再生開始と同時にスクリプトが上書きする）
            float restRadius = form.Spacing * (overallIndex + 1);
            pd.offsetX = MathF.Cos(form.BaseAngleRad) * restRadius;
            pd.offsetY = MathF.Sin(form.BaseAngleRad) * restRadius;

            var swingExpr = new JObject
            {
                ["op"] = "Mul",
                ["a"] = new JObject { ["op"] = "Sin", ["a"] = new JObject { ["op"] = "Mul", ["a"] = new JObject { ["op"] = "Time" }, ["b"] = form.Speed } },
                ["b"] = form.AmplitudeRad,
            };
            var angleExpr = new JObject { ["op"] = "Add", ["a"] = form.BaseAngleRad, ["b"] = swingExpr };
            var radiusExpr = new JObject
            {
                ["op"] = "Mul",
                ["a"] = new JObject { ["op"] = "Add", ["a"] = new JObject { ["op"] = "PartIndex" }, ["b"] = 1 },
                ["b"] = form.Spacing,
            };
            var body = new JArray {
                new JObject {
                    ["op"] = "Forever",
                    ["body"] = new JArray {
                        new JObject { ["op"] = "SetLocalOffsetPolar", ["angle"] = angleExpr, ["radius"] = radiusExpr },
                        new JObject { ["op"] = "Wait", ["frames"] = 1 },
                    }
                }
            };
            pd.script = new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = body } };
            newParts.Add(pd);
        }
        return newParts;
    }

    private void OpenPendulumGenerator()
    {
        using var form = new PendulumGeneratorForm(projectRoot);
        if (form.ShowDialog() != DialogResult.OK) return;

        var newParts = GeneratePendulumParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        PushHistory();
        MessageBox.Show($"{newParts.Count}個のパーツを「振り子」として配置しました。",
            "生成完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private PendulumGroupInfo? DetectPendulumGroup(int fromIndex)
    {
        if (fromIndex < 0 || fromIndex >= parts.Count) return null;
        var seed = parts[fromIndex];
        if (!PendulumGroupInfo.TryParsePendulumScript(seed.script, out float baseAngle, out float amplitude, out float speed, out float spacing)) return null;

        string id = seed.id;
        int cut = id.Length;
        while (cut > 0 && char.IsDigit(id[cut - 1])) cut--;
        string prefix = id.Substring(0, cut);
        if (string.IsNullOrEmpty(prefix)) return null;

        var info = new PendulumGroupInfo { IdPrefix = prefix, Spacing = spacing, Speed = speed, Amplitude = amplitude, BaseAngle = baseAngle, Hp = seed.hp, ZOrder = seed.zOrder, PartSize = seed.width > 0 ? seed.width : 12, SpritePath = seed.sprite };
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (!p.id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!PendulumGroupInfo.TryParsePendulumScript(p.script, out float ba2, out float am2, out float sp2, out float spc2)) continue;
            if (Math.Abs(ba2 - baseAngle) > 0.0001f || Math.Abs(am2 - amplitude) > 0.0001f || Math.Abs(sp2 - speed) > 0.0001f || Math.Abs(spc2 - spacing) > 0.0001f) continue;
            info.Indices.Add(i);
        }
        return info.Indices.Count > 0 ? info : null;
    }

    private void EditPendulumGroup(PendulumGroupInfo group)
    {
        using var form = new PendulumGeneratorForm(projectRoot, group);
        if (form.ShowDialog() != DialogResult.OK) return;

        foreach (var idx in group.Indices.OrderByDescending(i => i)) parts.RemoveAt(idx);
        var newParts = GeneratePendulumParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        PushHistory();
        MessageBox.Show($"「振り子」を{newParts.Count}個のパーツに更新しました。\n（更新後はパーツ一覧の末尾に移動しています）",
            "更新完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ==== 🛰 公転として配置（ジェネレータ）/編集 ====
    // Feature: UI改善（提案書 PT-2）— 複数のパーツが同じ半径の円周上を、PartIndexに応じた位相差を
    // 保ったまま回り続ける（衛星/取り巻きのような動き）。回転する棒との違いは「半径が一定で
    // 角度がPartIndexごとにずれる」点（回転する棒は逆に「角度が共通で半径がPartIndexごとにずれる」）。

    private List<PartDef> GenerateOrbitParts(OrbitGeneratorForm form, int startIndexForPhase)
    {
        var existing = new HashSet<string>(parts.Select(p => p.id));
        var newParts = new List<PartDef>();
        float phaseStep = (2f * MathF.PI) / Math.Max(form.Count, 1);
        for (int i = 0; i < form.Count; i++)
        {
            string baseId = form.IdPrefix;
            string id = $"{baseId}{i}";
            int n = 1;
            while (existing.Contains(id)) { id = $"{baseId}{i}_{n}"; n++; }
            existing.Add(id);

            var pd = new PartDef
            {
                id = id,
                sprite = form.SpritePath,
                width = form.PartSize,
                height = form.PartSize,
                hitboxWidth = form.PartSize,
                hitboxHeight = form.PartSize,
                hp = form.Hp,
                zOrder = form.ZOrder,
            };
            int overallIndex = startIndexForPhase + i;
            float restAngle = overallIndex * phaseStep; // t=0時点の静止姿勢（各パーツごとに位相がずれる）
            pd.offsetX = MathF.Cos(restAngle) * form.Radius;
            pd.offsetY = MathF.Sin(restAngle) * form.Radius;

            var angleExpr = new JObject
            {
                ["op"] = "Add",
                ["a"] = new JObject { ["op"] = "Mul", ["a"] = new JObject { ["op"] = "Time" }, ["b"] = form.Speed },
                ["b"] = new JObject { ["op"] = "Mul", ["a"] = new JObject { ["op"] = "PartIndex" }, ["b"] = phaseStep },
            };
            var body = new JArray {
                new JObject {
                    ["op"] = "Forever",
                    ["body"] = new JArray {
                        new JObject { ["op"] = "SetLocalOffsetPolar", ["angle"] = angleExpr, ["radius"] = (double)form.Radius },
                        new JObject { ["op"] = "Wait", ["frames"] = 1 },
                    }
                }
            };
            pd.script = new JArray { new JObject { ["hat"] = "OnSpawn", ["body"] = body } };
            newParts.Add(pd);
        }
        return newParts;
    }

    private void OpenOrbitGenerator()
    {
        using var form = new OrbitGeneratorForm(projectRoot);
        if (form.ShowDialog() != DialogResult.OK) return;

        var newParts = GenerateOrbitParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        PushHistory();
        MessageBox.Show($"{newParts.Count}個のパーツを「公転」として配置しました。\n（PartIndexに応じて位相がずれるため、同じスクリプトを共有しても円周上に等間隔で並んで回り続けます）",
            "生成完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private OrbitGroupInfo? DetectOrbitGroup(int fromIndex)
    {
        if (fromIndex < 0 || fromIndex >= parts.Count) return null;
        var seed = parts[fromIndex];
        if (!OrbitGroupInfo.TryParseOrbitScript(seed.script, out float speed, out float phaseStep, out float radius)) return null;

        string id = seed.id;
        int cut = id.Length;
        while (cut > 0 && char.IsDigit(id[cut - 1])) cut--;
        string prefix = id.Substring(0, cut);
        if (string.IsNullOrEmpty(prefix)) return null;

        var info = new OrbitGroupInfo { IdPrefix = prefix, Speed = speed, PhaseStep = phaseStep, Radius = radius, Hp = seed.hp, ZOrder = seed.zOrder, PartSize = seed.width > 0 ? seed.width : 12, SpritePath = seed.sprite };
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            if (!p.id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!OrbitGroupInfo.TryParseOrbitScript(p.script, out float sp2, out float ph2, out float r2)) continue;
            if (Math.Abs(sp2 - speed) > 0.0001f || Math.Abs(ph2 - phaseStep) > 0.0001f || Math.Abs(r2 - radius) > 0.0001f) continue;
            info.Indices.Add(i);
        }
        return info.Indices.Count > 0 ? info : null;
    }

    private void EditOrbitGroup(OrbitGroupInfo group)
    {
        using var form = new OrbitGeneratorForm(projectRoot, group);
        if (form.ShowDialog() != DialogResult.OK) return;

        foreach (var idx in group.Indices.OrderByDescending(i => i)) parts.RemoveAt(idx);
        var newParts = GenerateOrbitParts(form, parts.Count);
        parts.AddRange(newParts);
        selectedIndex = parts.Count - newParts.Count;
        RefreshList();
        PushHistory();
        MessageBox.Show($"「公転」を{newParts.Count}個のパーツに更新しました。\n（更新後はパーツ一覧の末尾に移動しています）",
            "更新完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // 選択中のパーツが「回転する棒」「振り子」「公転」のいずれかとして認識できるかを順に試し、
    // 一致した種類の編集ダイアログを開く（種類ごとにボタンを分けず、利用者は用途を意識しなくてよい）
    private void OpenMotionEditor()
    {
        if (selectedIndex < 0) { MessageBox.Show("編集したい動きのパーツをどれか1つ選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var rodGroup = DetectRodGroup(selectedIndex);
        if (rodGroup != null) { EditRodGroup(rodGroup); return; }

        var pendulumGroup = DetectPendulumGroup(selectedIndex);
        if (pendulumGroup != null) { EditPendulumGroup(pendulumGroup); return; }

        var orbitGroup = DetectOrbitGroup(selectedIndex);
        if (orbitGroup != null) { EditOrbitGroup(orbitGroup); return; }

        MessageBox.Show("選択中のパーツは「回転する棒」「振り子」「公転」のいずれとしても認識できませんでした。\n（対応するウィザードから生成したパーツのみ、この機能で編集できます）", "認識できません", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ==== 下部ボタン ====

    private Panel BuildBottomButtons()
    {
        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        _btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        _btnCancel.Click += (s, e) => Cancelled?.Invoke(this, EventArgs.Empty);
        // Feature: UI改善（提案書 CUT-3）— パーツIDが重複していると一覧上で区別できなくなるため保存前に警告する。
        _btnOk = new Button { Text = "💾 OK", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _btnOk.Click += (s, e) =>
        {
            var dupIds = parts.GroupBy(p => p.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupIds.Count > 0)
            {
                string msg = $"パーツIDが重複しています: {string.Join(", ", dupIds)}\n\nこのまま保存しますか？";
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            ResultParts = parts;
            Saved?.Invoke(this, parts);
        };
        // RightToLeftのFlowLayoutPanelは追加順が右から並ぶため、先にCancelを追加すると右端になる。OKを一番右(先頭追加)にしたいため逆順で追加する。
        flow.Controls.Add(_btnCancel);
        flow.Controls.Add(_btnOk);

        // Feature: UI改善（提案書 CUT-1）— このパーツエディタ内での操作ミスを、ダイアログ全体の
        // キャンセルに頼らず1手ずつ戻せるようにするUndo/Redo（Ctrl+Z/Ctrl+Yでも操作可能）。
        var flowHistory = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), AutoSize = true };
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(8, 5, 8, 5), Enabled = false };
        _btnUndo.Click += (s, e) => PartsUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(8, 5, 8, 5), Enabled = false };
        _btnRedo.Click += (s, e) => PartsRedo();
        flowHistory.Controls.AddRange(new Control[] { _btnUndo, _btnRedo });

        pnl.Controls.Add(flow);
        pnl.Controls.Add(flowHistory);
        return pnl;
    }

    // UserControlにはOnFormClosedがないため、Dispose(bool)でタイマー/画像を確実に解放する。
    // シェルのGoBack()はページをControlsから外した直後に必ずDispose()を呼ぶため、
    // モーダル表示だった頃のOnFormClosedと同じタイミングで後始末できる。
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewTimer?.Stop();
            _previewTimer?.Dispose();
            _highlightTimer?.Stop();
            _highlightTimer?.Dispose();
            baseSprite?.Dispose();
            foreach (var img in _partThumbCache.Values) img?.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ======================================================
// RodGeneratorForm - 「🌀 回転する棒として配置」の設定ダイアログ
// Feature: Composite Multi-Part Objects (Parts-Fix4)
// ======================================================
public class RodGeneratorForm : Form
{
    private readonly string projectRoot;
    private NumericUpDown nudCount = null!, nudSpacing = null!, nudSpeed = null!, nudHp = null!, nudZOrder = null!, nudSize = null!;
    private CheckBox chkReverse = null!;
    private TextBox txtIdPrefix = null!;
    private Label lblSprite = null!;
    private string spritePath = "";

    public int Count => (int)nudCount.Value;
    public float Spacing => (float)nudSpacing.Value;
    public float Speed => (float)nudSpeed.Value * (chkReverse.Checked ? -1f : 1f);
    public int Hp => (int)nudHp.Value;
    public int ZOrder => (int)nudZOrder.Value;
    public int PartSize => (int)nudSize.Value;
    public string IdPrefix => string.IsNullOrWhiteSpace(txtIdPrefix.Text) ? "rod" : txtIdPrefix.Text.Trim();
    public string SpritePath => spritePath;

    // initialを渡すと「既存の棒を編集」モードになり、現在の値が入った状態で開く（値の意味はRodGroupInfo参照）
    public RodGeneratorForm(string projectRoot, RodGroupInfo? initial = null)
    {
        this.projectRoot = projectRoot;
        bool isEdit = initial != null;
        Text = isEdit ? "🔧 回転する棒を編集" : "🌀 回転する棒として配置";
        Size = new Size(480, 460);
        MinimumSize = new Size(420, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblExplain = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(8),
            Text = isEdit
                ? "現在の「回転する棒」のパラメータを変更して更新します。球の数を増減させたり、回転速度・向き・間隔（伸び縮み）を調整できます。"
                : "マリオのファイアバーのように、中心から一直線に並んだ球が棒状に回転するパーツ一式を自動生成します。\n" +
                  "「□○○○○○」のように、球が同じ角度・異なる半径で並ぶため、常に一直線のまま回転します。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        // Dock=Topにして内容量に応じた高さだけ確保する（Dock=Fillだと親のAutoScrollより先に
        // 強制的にクライアント領域いっぱいに引き伸ばされてしまい、はみ出た分がスクロールされず
        // 見切れる原因になるため、必ずTop+AutoSizeの組み合わせにする）
        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, r);
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(control, 1, r);
        }

        nudCount = new NumericUpDown { Minimum = 1, Maximum = 40, Value = initial?.Indices.Count ?? 5, Width = 100 };
        AddRow("球の数（増減で伸縮）", nudCount);

        nudSpacing = new NumericUpDown { Minimum = 2, Maximum = 500, Value = (decimal)(initial?.Spacing ?? 14f), Width = 100 };
        AddRow("間隔(px)", nudSpacing);

        nudSpeed = new NumericUpDown { Minimum = 0.001m, Maximum = 1m, DecimalPlaces = 3, Increment = 0.005m, Value = (decimal)Math.Abs(initial?.Speed ?? 0.04f), Width = 100 };
        AddRow("回転速度", nudSpeed);

        chkReverse = new CheckBox { Text = "逆回転（反時計回り）にする", AutoSize = true, Checked = (initial?.Speed ?? 0f) < 0f };
        AddRow("回転方向", chkReverse);

        nudSize = new NumericUpDown { Minimum = 2, Maximum = 200, Value = initial?.PartSize ?? 12, Width = 100 };
        AddRow("球の表示/当たり判定サイズ(px)", nudSize);

        nudHp = new NumericUpDown { Minimum = 0, Maximum = 999, Value = initial?.Hp ?? 0, Width = 100 };
        AddRow("HP(0=不滅)", nudHp);

        nudZOrder = new NumericUpDown { Minimum = -100, Maximum = 100, Value = initial?.ZOrder ?? 1, Width = 100 };
        AddRow("zOrder", nudZOrder);

        txtIdPrefix = new TextBox { Text = initial?.IdPrefix ?? "rod", Width = 150 };
        AddRow("パーツID接頭辞", txtIdPrefix);

        spritePath = initial?.SpritePath ?? "";
        var spritePanel = new FlowLayoutPanel { AutoSize = true };
        lblSprite = new Label { Text = string.IsNullOrEmpty(spritePath) ? "(画像なし)" : spritePath, AutoSize = true, MaximumSize = new Size(220, 0), Margin = new Padding(3, 6, 6, 3) };
        var btnPick = new Button { Text = "📁 画像選択", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnPick.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "球の画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            spritePath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            lblSprite.Text = spritePath;
        };
        spritePanel.Controls.AddRange(new Control[] { lblSprite, btnPick });
        AddRow("球の画像", spritePanel);

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = isEdit ? "更新" : "生成", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        pnlBottom.Controls.Add(flow);

        var pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        pnlScroll.Controls.Add(table);

        Controls.Add(pnlScroll);
        Controls.Add(pnlBottom);
        Controls.Add(lblExplain);
    }
}

// ======================================================
// RodGroupInfo - 「回転する棒」として検出済みのパーツ群の共有パラメータ
// Feature: Composite Multi-Part Objects (Parts-Fix5)
// ======================================================
public class RodGroupInfo
{
    public List<int> Indices = new(); // parts配列内でのインデックス（棒を構成する全パーツ）
    public string IdPrefix = "rod";
    public float Spacing;
    public float Speed; // 符号が回転方向（負=逆回転）
    public int Hp;
    public int ZOrder;
    public int PartSize;
    public string SpritePath = "";

    // scriptが「SetLocalOffsetPolar(angle:Mul(Time,speed), radius:Mul(Add(PartIndex,1),spacing))」の
    // 形になっているかを判定し、speed/spacingを取り出す。ジェネレータが生成した形と完全一致する場合のみtrue。
    public static bool TryParseRodScript(JArray script, out float speed, out float spacing)
    {
        speed = 0; spacing = 0;
        try
        {
            var hat = script.FirstOrDefault(t => t["hat"]?.ToString() == "OnSpawn") as JObject;
            var body = hat?["body"] as JArray;
            var forever = body?.FirstOrDefault(t => t["op"]?.ToString() == "Forever") as JObject;
            var fbody = forever?["body"] as JArray;
            var setOp = fbody?.FirstOrDefault(t => t["op"]?.ToString() == "SetLocalOffsetPolar") as JObject;
            if (setOp == null) return false;
            var angle = setOp["angle"] as JObject;
            var radius = setOp["radius"] as JObject;
            if (angle?["op"]?.ToString() != "Mul") return false;
            if (radius?["op"]?.ToString() != "Mul") return false;
            speed = angle["b"]!.Value<float>();
            spacing = radius["b"]!.Value<float>();
            return true;
        }
        catch { return false; }
    }
}

// ======================================================
// PendulumGeneratorForm - 「🕰 振り子として配置」の設定ダイアログ
// Feature: UI改善（提案書 PT-2）
// ======================================================
public class PendulumGeneratorForm : Form
{
    private readonly string projectRoot;
    private NumericUpDown nudCount = null!, nudSpacing = null!, nudAmplitudeDeg = null!, nudSpeed = null!, nudBaseAngleDeg = null!, nudHp = null!, nudZOrder = null!, nudSize = null!;
    private TextBox txtIdPrefix = null!;
    private Label lblSprite = null!;
    private string spritePath = "";

    public int Count => (int)nudCount.Value;
    public float Spacing => (float)nudSpacing.Value;
    public float Speed => (float)nudSpeed.Value;
    public float AmplitudeRad => (float)((double)nudAmplitudeDeg.Value * Math.PI / 180.0);
    public float BaseAngleRad => (float)((double)nudBaseAngleDeg.Value * Math.PI / 180.0);
    public int Hp => (int)nudHp.Value;
    public int ZOrder => (int)nudZOrder.Value;
    public int PartSize => (int)nudSize.Value;
    public string IdPrefix => string.IsNullOrWhiteSpace(txtIdPrefix.Text) ? "pendulum" : txtIdPrefix.Text.Trim();
    public string SpritePath => spritePath;

    public PendulumGeneratorForm(string projectRoot, PendulumGroupInfo? initial = null)
    {
        this.projectRoot = projectRoot;
        bool isEdit = initial != null;
        Text = isEdit ? "🔧 振り子を編集" : "🕰 振り子として配置";
        Size = new Size(480, 480);
        MinimumSize = new Size(420, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblExplain = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(8),
            Text = isEdit
                ? "現在の「振り子」のパラメータを変更して更新します。長さ・振れ幅・速さ・向きを調整できます。"
                : "中心から一直線に並んだ球が、基準角度を中心に左右（または上下）へ振り子のように揺れる\n" +
                  "パーツ一式を自動生成します。吊り橋の重りやシャンデリアなどに使えます。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, r);
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(control, 1, r);
        }

        double initBaseAngleDeg = (initial?.BaseAngle ?? MathF.PI / 2f) * 180.0 / Math.PI;
        double initAmplitudeDeg = (initial?.Amplitude ?? MathF.PI / 6f) * 180.0 / Math.PI;

        nudCount = new NumericUpDown { Minimum = 1, Maximum = 40, Value = initial?.Indices.Count ?? 5, Width = 100 };
        AddRow("球の数（増減で伸縮）", nudCount);

        nudSpacing = new NumericUpDown { Minimum = 2, Maximum = 500, Value = (decimal)(initial?.Spacing ?? 14f), Width = 100 };
        AddRow("間隔(px)＝振り子の長さ", nudSpacing);

        nudBaseAngleDeg = new NumericUpDown { Minimum = -180, Maximum = 180, Value = (decimal)Math.Clamp(initBaseAngleDeg, -180, 180), Width = 100 };
        AddRow("基準角度(度)　90=真下", nudBaseAngleDeg);

        nudAmplitudeDeg = new NumericUpDown { Minimum = 1, Maximum = 179, Value = (decimal)Math.Clamp(initAmplitudeDeg, 1, 179), Width = 100 };
        AddRow("振れ幅(度)", nudAmplitudeDeg);

        nudSpeed = new NumericUpDown { Minimum = 0.001m, Maximum = 1m, DecimalPlaces = 3, Increment = 0.005m, Value = (decimal)Math.Abs(initial?.Speed ?? 0.03f), Width = 100 };
        AddRow("揺れる速さ", nudSpeed);

        nudSize = new NumericUpDown { Minimum = 2, Maximum = 200, Value = initial?.PartSize ?? 12, Width = 100 };
        AddRow("球の表示/当たり判定サイズ(px)", nudSize);

        nudHp = new NumericUpDown { Minimum = 0, Maximum = 999, Value = initial?.Hp ?? 0, Width = 100 };
        AddRow("HP(0=不滅)", nudHp);

        nudZOrder = new NumericUpDown { Minimum = -100, Maximum = 100, Value = initial?.ZOrder ?? 1, Width = 100 };
        AddRow("zOrder", nudZOrder);

        txtIdPrefix = new TextBox { Text = initial?.IdPrefix ?? "pendulum", Width = 150 };
        AddRow("パーツID接頭辞", txtIdPrefix);

        spritePath = initial?.SpritePath ?? "";
        var spritePanel = new FlowLayoutPanel { AutoSize = true };
        lblSprite = new Label { Text = string.IsNullOrEmpty(spritePath) ? "(画像なし)" : spritePath, AutoSize = true, MaximumSize = new Size(220, 0), Margin = new Padding(3, 6, 6, 3) };
        var btnPick = new Button { Text = "📁 画像選択", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnPick.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "球の画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            spritePath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            lblSprite.Text = spritePath;
        };
        spritePanel.Controls.AddRange(new Control[] { lblSprite, btnPick });
        AddRow("球の画像", spritePanel);

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = isEdit ? "更新" : "生成", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        pnlBottom.Controls.Add(flow);

        var pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        pnlScroll.Controls.Add(table);

        Controls.Add(pnlScroll);
        Controls.Add(pnlBottom);
        Controls.Add(lblExplain);
    }
}

// ======================================================
// PendulumGroupInfo - 「振り子」として検出済みのパーツ群の共有パラメータ
// Feature: UI改善（提案書 PT-2）
// ======================================================
public class PendulumGroupInfo
{
    public List<int> Indices = new();
    public string IdPrefix = "pendulum";
    public float Spacing;
    public float Speed;
    public float Amplitude; // ラジアン
    public float BaseAngle; // ラジアン
    public int Hp;
    public int ZOrder;
    public int PartSize;
    public string SpritePath = "";

    // scriptが「SetLocalOffsetPolar(angle:Add(baseAngle,Mul(Sin(Mul(Time,speed)),amplitude)), radius:Mul(Add(PartIndex,1),spacing))」
    // の形になっているかを判定する。回転する棒(角度がMul)・公転(angle.aがMul)とは形が異なるため誤検出しない。
    public static bool TryParsePendulumScript(JArray script, out float baseAngle, out float amplitude, out float speed, out float spacing)
    {
        baseAngle = 0; amplitude = 0; speed = 0; spacing = 0;
        try
        {
            var hat = script.FirstOrDefault(t => t["hat"]?.ToString() == "OnSpawn") as JObject;
            var body = hat?["body"] as JArray;
            var forever = body?.FirstOrDefault(t => t["op"]?.ToString() == "Forever") as JObject;
            var fbody = forever?["body"] as JArray;
            var setOp = fbody?.FirstOrDefault(t => t["op"]?.ToString() == "SetLocalOffsetPolar") as JObject;
            if (setOp == null) return false;
            var angle = setOp["angle"] as JObject;
            var radius = setOp["radius"] as JObject;
            if (angle?["op"]?.ToString() != "Add") return false;
            if (radius?["op"]?.ToString() != "Mul") return false;
            baseAngle = angle["a"]!.Value<float>(); // Orbit(angle.aがMulのJObject)ならここで例外→falseになる
            var swing = angle["b"] as JObject;
            if (swing?["op"]?.ToString() != "Mul") return false;
            var sin = swing["a"] as JObject;
            if (sin?["op"]?.ToString() != "Sin") return false;
            var innerMul = sin["a"] as JObject;
            if (innerMul?["op"]?.ToString() != "Mul") return false;
            speed = innerMul["b"]!.Value<float>();
            amplitude = swing["b"]!.Value<float>();
            spacing = radius["b"]!.Value<float>();
            return true;
        }
        catch { return false; }
    }
}

// ======================================================
// OrbitGeneratorForm - 「🛰 公転として配置」の設定ダイアログ
// Feature: UI改善（提案書 PT-2）
// ======================================================
public class OrbitGeneratorForm : Form
{
    private readonly string projectRoot;
    private NumericUpDown nudCount = null!, nudRadius = null!, nudSpeed = null!, nudHp = null!, nudZOrder = null!, nudSize = null!;
    private CheckBox chkReverse = null!;
    private TextBox txtIdPrefix = null!;
    private Label lblSprite = null!;
    private string spritePath = "";

    public int Count => (int)nudCount.Value;
    public float Radius => (float)nudRadius.Value;
    public float Speed => (float)nudSpeed.Value * (chkReverse.Checked ? -1f : 1f);
    public int Hp => (int)nudHp.Value;
    public int ZOrder => (int)nudZOrder.Value;
    public int PartSize => (int)nudSize.Value;
    public string IdPrefix => string.IsNullOrWhiteSpace(txtIdPrefix.Text) ? "orbit" : txtIdPrefix.Text.Trim();
    public string SpritePath => spritePath;

    public OrbitGeneratorForm(string projectRoot, OrbitGroupInfo? initial = null)
    {
        this.projectRoot = projectRoot;
        bool isEdit = initial != null;
        Text = isEdit ? "🔧 公転を編集" : "🛰 公転として配置";
        Size = new Size(480, 460);
        MinimumSize = new Size(420, 400);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        var lblExplain = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(8),
            Text = isEdit
                ? "現在の「公転」のパラメータを変更して更新します。数・半径・速さ・向きを調整できます。"
                : "同じ半径の円周上を、等間隔に並んだまま回り続けるパーツ一式を自動生成します。\n" +
                  "衛星や取り巻きのような動きに使えます（回転する棒と違い、パーツどうしの間隔は円周上の弧の長さになります）。",
            Font = new Font(Font.FontFamily, 8f),
            ForeColor = Color.DarkSlateGray,
        };

        var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, r);
            control.Margin = new Padding(3, 4, 3, 4);
            table.Controls.Add(control, 1, r);
        }

        nudCount = new NumericUpDown { Minimum = 1, Maximum = 40, Value = initial?.Indices.Count ?? 3, Width = 100 };
        AddRow("衛星の数", nudCount);

        nudRadius = new NumericUpDown { Minimum = 2, Maximum = 1000, Value = (decimal)(initial?.Radius ?? 60f), Width = 100 };
        AddRow("公転半径(px)", nudRadius);

        nudSpeed = new NumericUpDown { Minimum = 0.001m, Maximum = 1m, DecimalPlaces = 3, Increment = 0.005m, Value = (decimal)Math.Abs(initial?.Speed ?? 0.04f), Width = 100 };
        AddRow("回転速度", nudSpeed);

        chkReverse = new CheckBox { Text = "逆回転（反時計回り）にする", AutoSize = true, Checked = (initial?.Speed ?? 0f) < 0f };
        AddRow("回転方向", chkReverse);

        nudSize = new NumericUpDown { Minimum = 2, Maximum = 200, Value = initial?.PartSize ?? 12, Width = 100 };
        AddRow("衛星の表示/当たり判定サイズ(px)", nudSize);

        nudHp = new NumericUpDown { Minimum = 0, Maximum = 999, Value = initial?.Hp ?? 0, Width = 100 };
        AddRow("HP(0=不滅)", nudHp);

        nudZOrder = new NumericUpDown { Minimum = -100, Maximum = 100, Value = initial?.ZOrder ?? 1, Width = 100 };
        AddRow("zOrder", nudZOrder);

        txtIdPrefix = new TextBox { Text = initial?.IdPrefix ?? "orbit", Width = 150 };
        AddRow("パーツID接頭辞", txtIdPrefix);

        spritePath = initial?.SpritePath ?? "";
        var spritePanel = new FlowLayoutPanel { AutoSize = true };
        lblSprite = new Label { Text = string.IsNullOrEmpty(spritePath) ? "(画像なし)" : spritePath, AutoSize = true, MaximumSize = new Size(220, 0), Margin = new Padding(3, 6, 6, 3) };
        var btnPick = new Button { Text = "📁 画像選択", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnPick.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "衛星の画像を選択" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            spritePath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
            lblSprite.Text = spritePath;
        };
        spritePanel.Controls.AddRange(new Control[] { lblSprite, btnPick });
        AddRow("衛星の画像", spritePanel);

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var btnCancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        var btnOk = new Button { Text = isEdit ? "更新" : "生成", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        flow.Controls.Add(btnCancel);
        flow.Controls.Add(btnOk);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        pnlBottom.Controls.Add(flow);

        var pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        pnlScroll.Controls.Add(table);

        Controls.Add(pnlScroll);
        Controls.Add(pnlBottom);
        Controls.Add(lblExplain);
    }
}

// ======================================================
// OrbitGroupInfo - 「公転」として検出済みのパーツ群の共有パラメータ
// Feature: UI改善（提案書 PT-2）
// ======================================================
public class OrbitGroupInfo
{
    public List<int> Indices = new();
    public string IdPrefix = "orbit";
    public float Speed;
    public float PhaseStep; // ラジアン。パーツ間の位相差(2π/個数)がベイクされた値
    public float Radius;
    public int Hp;
    public int ZOrder;
    public int PartSize;
    public string SpritePath = "";

    // scriptが「SetLocalOffsetPolar(angle:Add(Mul(Time,speed),Mul(PartIndex,phaseStep)), radius:<定数>)」
    // の形になっているかを判定する。半径が単純な数値であることを要求し、回転する棒/振り子と区別する。
    public static bool TryParseOrbitScript(JArray script, out float speed, out float phaseStep, out float radius)
    {
        speed = 0; phaseStep = 0; radius = 0;
        try
        {
            var hat = script.FirstOrDefault(t => t["hat"]?.ToString() == "OnSpawn") as JObject;
            var body = hat?["body"] as JArray;
            var forever = body?.FirstOrDefault(t => t["op"]?.ToString() == "Forever") as JObject;
            var fbody = forever?["body"] as JArray;
            var setOp = fbody?.FirstOrDefault(t => t["op"]?.ToString() == "SetLocalOffsetPolar") as JObject;
            if (setOp == null) return false;
            var angle = setOp["angle"] as JObject;
            var radiusTok = setOp["radius"];
            if (angle?["op"]?.ToString() != "Add") return false;
            var timeTerm = angle["a"] as JObject;
            var phaseTerm = angle["b"] as JObject;
            if (timeTerm?["op"]?.ToString() != "Mul") return false;
            if (phaseTerm?["op"]?.ToString() != "Mul") return false;
            if (radiusTok == null || radiusTok.Type == JTokenType.Object) return false; // 半径が式(回転する棒/振り子)なら対象外
            speed = timeTerm["b"]!.Value<float>();
            phaseStep = phaseTerm["b"]!.Value<float>();
            radius = radiusTok.Value<float>();
            return true;
        }
        catch { return false; }
    }
}

// Feature: Composite Multi-Part Objects (Parts-M7)
// 画像をimg/フォルダへコピーする既存処理（AssetManagerFormのbtnSprite分岐）を共通化したヘルパー。
// 同名かつ内容が異なるファイルを誤って上書きしないよう、内容が違う場合は連番を付けて別名保存する。
public static class ImageImportHelper
{
    public static string CopyIntoImgFolder(string projectRoot, string sourceFile)
    {
        string imgDir = Path.Combine(projectRoot, "img");
        Directory.CreateDirectory(imgDir);

        string fileName = Path.GetFileName(sourceFile);
        string destPath = Path.Combine(imgDir, fileName);

        if (destPath.Equals(sourceFile, StringComparison.OrdinalIgnoreCase))
            return "img/" + fileName;

        if (File.Exists(destPath) && !FilesHaveSameContent(sourceFile, destPath))
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int n = 2;
            do
            {
                fileName = $"{nameNoExt}_{n}{ext}";
                destPath = Path.Combine(imgDir, fileName);
                n++;
            } while (File.Exists(destPath) && !FilesHaveSameContent(sourceFile, destPath));
        }

        if (!File.Exists(destPath)) File.Copy(sourceFile, destPath, overwrite: false);
        return "img/" + fileName;
    }

    private static bool FilesHaveSameContent(string pathA, string pathB)
    {
        try
        {
            var infoA = new FileInfo(pathA);
            var infoB = new FileInfo(pathB);
            if (infoA.Length != infoB.Length) return false;
            using var a = File.OpenRead(pathA);
            using var b = File.OpenRead(pathB);
            int ba, bb;
            do
            {
                ba = a.ReadByte();
                bb = b.ReadByte();
                if (ba != bb) return false;
            } while (ba != -1);
            return true;
        }
        catch { return false; }
    }
}
