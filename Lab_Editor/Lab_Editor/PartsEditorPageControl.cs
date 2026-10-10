using Newtonsoft.Json.Linq;

namespace Lab_Editor;

// ======================================================
// PartsEditorPageControl - 複合オブジェクト（敵/ギミック/アイテム）のパーツ編集
// Feature: Composite Multi-Part Objects (Parts-M7、UI刷新版)
// 構造改修フェーズ5cでForm(PartsEditorForm)からUserControlへ抽出。
//
// 1体の敵/ギミック/アイテムを、複数の画像パーツの組み合わせとして構成するためのエディタ。
//
// 【2026-10 UI全面改修】使いづらさの原因と、その直し方：
//   ・合成プレビューが「本体の画像を画面の半分に収める」固定の倍率で、大きな合成体は左右がはみ出して見えず、
//     小さなパーツは点にしか見えなかった。拡大縮小(ホイール)・移動(中ボタン/Space+ドラッグ)・全体表示を付けた
//   ・位置合わせは数値入力か、1px単位のドラッグしかなかった。グリッド(8px)とスナップ、矢印キーの微調整、
//     複数選択(Ctrl+クリック/範囲選択)と整列・等間隔を付けた
//   ・大きさ(幅・高さ)は画面から変えられなかった。選んだパーツの右下につまみを出して、ドラッグで変えられるようにした
//   ・機能のボタンが左下に11個詰め込まれ、操作のたびにメッセージボックスが出ていた。
//     上のツールバーへ種類ごとに整理し、結果は下のステータス欄に出す（止まらない）
//   ・詳細パネルがプレビューの下にあり、縦に長くて見切れていた。右側へ移し、項目をグループに分けた
//   ・値を1つ変えるたびに取り消しの履歴が1件積まれていた。同じ項目の連続した変更は1手にまとめる
//   ・複製・読み込みで deadly（触れるとダメージ）が消えていた不具合を直した
// 全体は SplitContainer（境界をドラッグで調整できる）+ TableLayoutPanel/FlowLayoutPanel で構成し、
// 固定座標指定によるはみ出しを構造的に起こりにくくしている。
//
// ファイルは3つに分けてある：
//   PartsEditorPageControl.cs        … 部品・一覧・ツールバー・詳細パネル・編集操作（このファイル）
//   PartsEditorPageControl.View.cs   … 合成プレビュー（描画・ズーム・ドラッグ・つまみ・キー操作）
//   PartTemplates.cs / PartTemplatePickerForm.cs … テンプレート（型にはまった複合パーツの一式）
// ======================================================
public partial class PartsEditorPageControl : UserControl
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
    // 本体の論理サイズ（当たり判定の幅・高さ）。0なら未指定で、画像の原寸を基準にする。
    private readonly float baseLogicalW;
    private readonly float baseLogicalH;
    // パーツごとのサムネイル画像のキャッシュ。毎回ファイルを読み直すと重いため、一度読み込んだら
    // PartDefインスタンスをキーにして保持しておく（画像が変わった時はInvalidatePartThumbで明示的に破棄する）。
    private readonly Dictionary<PartDef, Image?> _partThumbCache = new();

    // 編集中のパーツ一覧本体。コンストラクタで渡された初期値をクローンして持つため、
    // キャンセルされても呼び出し元の元データは書き換わらない。
    private List<PartDef> parts;
    // OKが押された後、確定した編集結果を呼び出し元が読み取るための公開プロパティ。
    public List<PartDef> ResultParts { get; private set; } = new();

    // 現在「主に」選択しているパーツのインデックス（未選択時は-1）。詳細パネルはこのパーツの値を表示する。
    private int selectedIndex = -1;
    // 選択しているパーツ全部のインデックス（主選択を含む）。複数選択のとき、移動・複製・削除・整列などは全員に効く。
    private readonly HashSet<int> _selected = new();
    // 詳細パネルの値をコードから設定している最中に、その変更をユーザー操作と誤認して
    // 余計なイベント処理（Undo履歴の記録など）が走らないようにするためのフラグ。
    private bool _suppressEvents = false;
    // 一覧グリッドの選択を、コード側から書き換えている最中か（SelectionChangedの再入防止）
    private bool _syncingList = false;

    // Feature: UI改善（提案書 PT-1）— 挙動スクリプトによる動きをその場で確認する再生プレビュー
    // 再生中に一定間隔でTickイベントを発生させ、経過時間(_previewTime)を進めるタイマー
    private System.Windows.Forms.Timer? _previewTimer;
    // 再生プレビューの現在時刻（スクリプト評価に渡すTime値。フレーム数相当のカウンタ）
    private float _previewTime = 0f;
    // 再生中かどうか（true=再生中。ドラッグでの位置調整は再生中は無効にする）
    private bool _isPlaying = false;
    private Button _btnPlayToggle = null!;
    // パーツのモーション（PartDef.motions）の編集欄と、プレビューで「いま起きていること」の模擬
    private PartMotionEditorControl _motionEditor = null!;
    private readonly PartMotionEvaluator.SimState _sim = new();
    private ComboBox _cboSim = null!;

    // Feature: UI改善（提案書 CUT-1）— パーツ編集画面自体のUndo/Redo（マップ編集限定だった仕組みの拡張）
    // パーツ一覧のスナップショットを積み重ねて保持する履歴管理オブジェクト
    private readonly HistoryManager<List<PartDef>> _history = new();
    private ToolStripButton _tbUndo = null!, _tbRedo = null!;
    // 同じ項目を続けて変えたとき（数値の矢印を押し続ける・文字を打つ等）を1手にまとめるための記録
    private string? _lastHistoryKey;
    private DateTime _lastHistoryTime = DateTime.MinValue;

    // Feature: UI改善（提案書 PT-6）— 「このスクリプトを全パーツへ」適用後、どのパーツが変わったか
    // 一目で分かるよう、対象パーツの枠を一瞬光らせる
    // 現在ハイライト表示すべきパーツの集合（描画時にこの集合に含まれるパーツだけ緑枠を追加で描く）
    private readonly HashSet<PartDef> _highlightedParts = new();
    // ハイライトを一定時間後に自動的に消すためのワンショットタイマー
    private System.Windows.Forms.Timer? _highlightTimer;

    // ==== コントロール ====
    // ツールバー（編集操作の入口）
    private ToolStrip _toolbar = null!;
    // パーツの簡易一覧を表示するグリッド（左側パネル）
    private DataGridView dgvList = null!;
    // 合成プレビューを描画するキャンバス（中央パネル）
    private CanvasPanel pnlComposer = null!;
    // 下端の状態表示。左に操作のヒントや結果（トースト）、右に座標・倍率を出す
    private Label _lblStatus = null!, _lblCoords = null!;
    private System.Windows.Forms.Timer? _toastTimer;
    private const string DefaultStatusText = "ホイール＝拡大縮小　中ボタン／Space＋ドラッグ＝移動　Ctrl＋クリック・範囲ドラッグ＝複数選択　矢印キー＝1px（Shift＝8px）　Delete＝削除　Ctrl＋D＝複製";
    // 以下、選択中パーツの詳細編集パネル（右）で使う各入力コントロール
    private TextBox txtId = null!;
    private PictureBox picSprite = null!;
    private Label lblSpriteValue = null!;
    private NumericUpDown nudOffsetX = null!, nudOffsetY = null!, nudWidth = null!, nudHeight = null!, nudScale = null!, nudHp = null!, nudZOrder = null!;
    private CheckBox chkDeadly = null!;
    private Label lblHpHint = null!, lblSelectionInfo = null!, lblScriptInfo = null!, lblHitboxInfo = null!;
    // 複数選択のとき共通の項目だけを編集できることを伝える見出し
    private Label lblMultiNote = null!;

    // コンストラクタ。
    // subjectLabel   : 編集対象（敵/ギミック/アイテムの名前など）を表す文字列。将来的な見出し表示用に受け取っている。
    // initialParts   : 編集開始時点でのパーツ一覧。ここではクローンして保持するため、このリスト自体は変更されない。
    // projectRoot    : プロジェクトのルートフォルダ。画像パスの解決に使う。
    // baseSpritePath : 本体の基準スプライトのパス。合成プレビューの中心目印として表示する。
    // baseLogicalW/H : 本体の論理サイズ（当たり判定の幅・高さ）。0なら画像の原寸で代用する。
    //                  詳細は WorldScale のコメントを参照。
    public PartsEditorPageControl(string subjectLabel, List<PartDef> initialParts, string projectRoot, string baseSpritePath, float baseLogicalW = 0f, float baseLogicalH = 0f)
    {
        this.projectRoot = projectRoot;
        this.baseSpritePath = baseSpritePath;
        this.baseLogicalW = baseLogicalW;
        this.baseLogicalH = baseLogicalH;
        // 渡されたパーツ一覧をそのまま参照すると、キャンセルしても呼び出し元のデータが
        // 書き換わってしまう恐れがあるため、1件ずつクローンして独立したリストとして保持する。
        parts = initialParts.Select(ClonePart).ToList();

        // ページ全体を親コンテナいっぱいに広げ、共通のUIテーマフォントを適用する。
        Dock = DockStyle.Fill;
        Font = UiTheme.Base;

        // 本体の基準スプライト画像を読み込んでおく（合成プレビューの中心に表示するため）。
        LoadBaseSprite();

        // 上：ツールバー ／ 下：状態表示＋OK・キャンセル ／ 中：左(一覧) | 中央(プレビュー) | 右(詳細)
        _toolbar = BuildToolbar();
        var pnlBottom = BuildBottomButtons();

        // 3分割。左右の境界はドラッグで調整できる。
        // 左(一覧)｜右(プレビュー＋詳細) をまず分け、右側をさらに 中央(プレビュー)｜右端(詳細) に分ける。
        var rootSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
        var pnlListSide = BuildListSide();
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6 };
        var pnlComposerSide = BuildComposerSide();
        var pnlDetailSide = BuildDetailSide();

        // 各パーツをコントロールツリーに配置していく。追加順は「Fill → Bottom → Top」にして、
        // Dockの重なり順（後から追加したものが外側）を意図どおりにする。
        Controls.Add(rootSplit);
        Controls.Add(pnlBottom);
        Controls.Add(_toolbar);

        rootSplit.Panel1.Controls.Add(pnlListSide);
        rootSplit.Panel2.Controls.Add(rightSplit);
        rightSplit.Panel1.Controls.Add(pnlComposerSide);
        rightSplit.Panel2.Controls.Add(pnlDetailSide);

        // SplitterDistanceはコントロールがDockされ実サイズが確定してから設定する（先に設定すると例外/無視されることがある）。
        // UserControlにはShownがないため、同じ目的で「親に配置されて実サイズが確定した後」に一度だけ
        // 発火するLoadイベントを使う。
        //
        // 分割位置の設定は SplitLayout.Apply 経由で行う。以前は直接代入していたため、シェルへ組み込まれる
        // 瞬間にウィンドウ（親）が小さいと「SplitterDistance は Panel1MinSize と Panel2MinSize の間でなければ
        // なりません」の例外になり、パーツ編集を開けなかった。幅が足りないときは設定を見送り、
        // 大きさが変わったとき（SizeChanged）に、設定できるまで再挑戦する。
        bool splitsApplied = false;
        void ApplySplits()
        {
            // 左(一覧)は狭め、右(詳細)は読みやすい幅、残りの広いところを合成プレビューに使う。
            // 左右の最小幅は、プレビューが潰れない程度（一覧180・詳細260・プレビュー200）にとどめる。
            bool a = SplitLayout.Apply(rootSplit, 180, 200 + 260 + 6, Math.Max(210, (int)(ClientSize.Width * 0.17)));
            bool b = SplitLayout.Apply(rightSplit, 200, 260, Math.Max(300, rightSplit.Width - 340));
            if (a && b) splitsApplied = true;
        }
        SizeChanged += (s, e) => { if (!splitsApplied && IsHandleCreated) ApplySplits(); };
        Load += (s, e) =>
        {
            ApplySplits();
            // 最初の1回だけ、全パーツが収まる倍率・位置にする
            FitView();
            // 詳細パネルは最初の選択を反映しておく（コンストラクタの時点ではグリッドの選択イベントが届かないことがある）
            LoadDetailFromSelection();
        };

        // 初期状態を履歴に1件積んでおく（これがないとUndoで「何もない状態」へ戻れなくなる）。
        PushHistory();
        // 一覧グリッドと詳細パネルを最新状態に合わせて表示する。
        RefreshList();
        UpdateToolbarState();
        SetStatus(DefaultStatusText);
    }

    // キーボードショートカットをこのページ内で捕まえて処理する。
    // WinFormsの標準ではメニューやツールバーのショートカットキーとして登録しないと拾えないため、
    // ProcessCmdKeyをオーバーライドして直接キー入力を検知している。
    //   Ctrl+Z/Ctrl+Y … 取り消し／やり直し（どこにフォーカスがあっても）
    //   Ctrl+D        … 複製
    //   Ctrl+A / Delete / 矢印 … プレビューか一覧にフォーカスがあるときだけ（入力欄で文字を編集中は奪わない）
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { PartsUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { PartsRedo(); return true; }
        if (keyData == (Keys.Control | Keys.D)) { DuplicateSelected(); return true; }
        bool onCanvas = pnlComposer.ContainsFocus;
        bool onList = dgvList.ContainsFocus;
        if (onCanvas || onList)
        {
            if (keyData == (Keys.Control | Keys.A)) { SelectAllParts(); return true; }
            if (keyData == Keys.Delete) { DeleteSelected(); return true; }
            if (keyData == Keys.Escape) { SetSelection(Array.Empty<int>(), -1); return true; }
        }
        // 矢印キーはプレビューにフォーカスがあるときだけパーツを動かす（一覧では、行の移動に使うため）
        if (onCanvas && (keyData & (Keys.Control | Keys.Alt)) == 0)
        {
            int amount = (keyData & Keys.Shift) != 0 ? 8 : 1;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: NudgeSelected(-amount, 0); return true;
                case Keys.Right: NudgeSelected(amount, 0); return true;
                case Keys.Up: NudgeSelected(0, -amount); return true;
                case Keys.Down: NudgeSelected(0, amount); return true;
            }
        }
        // 該当しないキーは基底クラスの処理に委ねる（他のショートカットやフォーカス移動を妨げないため）。
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ==== 取り消し／やり直し ====

    // このメソッド1つを、パーツ一覧を実際に変更する全ての操作（追加/削除/並び替え/複製/反転複製/
    // 各ウィザードの生成・編集/詳細パネルの値確定/キャンバスのドラッグ確定/画像・当たり判定・
    // スクリプトの変更確定）の末尾から呼ぶことで、Undo/Redoの対象を機能追加のたびに
    // 個別配線し直さずに済むようにしている。
    //
    // coalesceKey … 同じ項目の連続した変更（例：offsetXの矢印を押し続ける）を1手にまとめるための名前。
    //               0.7秒以内に同じ名前で呼ばれたら、新しく積まずに直前の履歴を上書きする。
    //               以前は値が1つ変わるたびに履歴が1件ずつ積まれ、10回戻してもほとんど動かなかった。
    private void PushHistory(string? coalesceKey = null)
    {
        // 現在のパーツ一覧をディープクローンしてから履歴スタックに積む。
        // クローンしない場合、後から同じPartDefインスタンスを書き換えると過去の履歴まで
        // 一緒に変わってしまい、Undo/Redoが正しく機能しなくなる。
        var snapshot = parts.Select(ClonePart).ToList();
        var now = DateTime.UtcNow;
        if (coalesceKey != null && coalesceKey == _lastHistoryKey && (now - _lastHistoryTime).TotalMilliseconds < 700 && _history.CanUndo)
            _history.ReplaceCurrent(snapshot);
        else
            _history.Push(snapshot);
        _lastHistoryKey = coalesceKey;
        _lastHistoryTime = now;
        // ボタンの有効/無効状態（これ以上戻れない/進めない場合はグレーアウト）を更新する。
        UpdateUndoRedoButtons();
    }

    // 「元に戻す」処理。履歴スタックから1つ前の状態を取り出し、現在のパーツ一覧を置き換える。
    private void PartsUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        ApplyRestoredParts(restored);
    }

    // 「やり直す」処理。PartsUndoの逆方向で、履歴スタックから1つ先の状態を取り出す。
    private void PartsRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        ApplyRestoredParts(restored);
    }

    // 履歴から取り出した状態へ丸ごと置き換え、選択・一覧・プレビューを合わせる。
    private void ApplyRestoredParts(List<PartDef> restored)
    {
        _lastHistoryKey = null; // 取り消し／やり直しをまたいで、まとめ書きを続けない
        foreach (var img in _partThumbCache.Values) img?.Dispose();
        _partThumbCache.Clear();
        parts = restored;
        // 選択中インデックスが復元後の件数を超えていたら、範囲内に収まるよう補正する。
        _selected.RemoveWhere(i => i >= parts.Count);
        selectedIndex = Math.Clamp(selectedIndex, -1, parts.Count - 1);
        if (selectedIndex >= 0) _selected.Add(selectedIndex);
        RefreshList();
        UpdateUndoRedoButtons();
    }

    // Undo/Redoボタンの有効・無効状態を、履歴管理オブジェクトの現在の状態に合わせて更新する。
    private void UpdateUndoRedoButtons()
    {
        // コンストラクタの初期化順序によっては、ボタンがまだ生成されていない段階で
        // PushHistoryが呼ばれる可能性があるため、nullチェックしてから触る。
        if (_tbUndo == null! || _tbRedo == null!) return;
        _tbUndo.Enabled = _history.CanUndo;
        _tbRedo.Enabled = _history.CanRedo;
    }

    // PartDefの全フィールドを1つずつコピーして独立したインスタンスを作る（ディープクローン）。
    // scriptフィールドはJArray（参照型・入れ子構造）なので、単純代入だと元と同じオブジェクトを
    // 共有してしまう。DeepClone()を使うことで、複製後にスクリプトを書き換えても元のパーツに
    // 影響しないようにしている。
    //
    // 【不具合の修正】deadly（触れるとダメージ）が抜けていたため、エディタでパーツを開いて保存し直すだけで
    // 回転する棘の輪などの「危険なはず」のパーツが、素通りできる飾りに戻っていた。
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
        deadly = p.deadly,
        script = (JArray)p.script.DeepClone(),
        motions = p.motions.Select(m => m.Clone()).ToList(),
    };

    // ==== 状態表示（メッセージボックスの代わり） ====

    // 下端の状態欄へ文字を出す。操作のたびに止まるメッセージボックスを出すと流れが切れるので、
    // 結果は黙ってここへ出し、数秒たったら操作ヒントへ戻す。
    //   warn … 注意（赤っぽい色）にする
    private void Toast(string message, bool warn = false)
    {
        SetStatus(message, warn ? Color.FromArgb(180, 40, 40) : Color.FromArgb(20, 110, 60));
        _toastTimer?.Stop();
        _toastTimer ??= new System.Windows.Forms.Timer { Interval = 5000 };
        _toastTimer.Tick -= OnToastTimerTick;
        _toastTimer.Tick += OnToastTimerTick;
        _toastTimer.Start();
    }
    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        SetStatus(DefaultStatusText);
    }
    private void SetStatus(string text, Color? color = null)
    {
        if (_lblStatus == null!) return;
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color ?? Color.DimGray;
    }

    // ==== 選択 ====

    // 選択を丸ごと置き換える（indices=選択するパーツ全部、primary=詳細パネルに出す主選択。-1なら未選択）。
    // 一覧・プレビュー・詳細・ツールバーの有効/無効をすべて合わせる。選択が変わる操作は必ずここを通す。
    private void SetSelection(IEnumerable<int> indices, int primary)
    {
        _selected.Clear();
        foreach (int i in indices) if (i >= 0 && i < parts.Count) _selected.Add(i);
        if (primary < 0 || !_selected.Contains(primary)) primary = _selected.Count > 0 ? _selected.Min() : -1;
        selectedIndex = primary;
        SyncListSelection();
        LoadDetailFromSelection();
        UpdateToolbarState();
        pnlComposer.Invalidate();
    }
    private void SelectOnly(int idx) => SetSelection(idx >= 0 ? new[] { idx } : Array.Empty<int>(), idx);
    private void SelectAllParts() => SetSelection(Enumerable.Range(0, parts.Count), parts.Count > 0 ? 0 : -1);

    // 一覧グリッドの選択表示を、_selected に合わせる
    private void SyncListSelection()
    {
        _syncingList = true;
        try
        {
            dgvList.ClearSelection();
            foreach (int i in _selected) if (i < dgvList.Rows.Count) dgvList.Rows[i].Selected = true;
            if (selectedIndex >= 0 && selectedIndex < dgvList.Rows.Count)
            {
                // CurrentCell を主選択へ寄せる（キーボード操作と表示のずれを防ぐ）。選択状態は変えない
                try { dgvList.CurrentCell = dgvList.Rows[selectedIndex].Cells["id"]; } catch { }
                dgvList.Rows[selectedIndex].Selected = true;
            }
        }
        finally { _syncingList = false; }
    }

    // 選択に応じて、ツールバーの各ボタンを使える／使えないにする
    private void UpdateToolbarState()
    {
        if (_toolbar == null!) return;
        bool any = _selected.Count > 0;
        bool multi = _selected.Count >= 2;
        foreach (var it in _selectionDependentItems) it.Enabled = any;
        foreach (var it in _multiDependentItems) it.Enabled = multi;
        UpdateSelectionInfo();
    }

    // ==== ツールバー ====
    private readonly List<ToolStripItem> _selectionDependentItems = new();
    private readonly List<ToolStripItem> _multiDependentItems = new();

    private ToolStrip BuildToolbar()
    {
        var ts = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(6, 3, 6, 3),
            Font = UiTheme.Base,
        };
        ToolStripButton Btn(string text, string tip, EventHandler onClick, bool needsSelection = false, bool needsMulti = false)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text, Margin = new Padding(1, 1, 1, 2) };
            b.Click += onClick;
            if (needsSelection) _selectionDependentItems.Add(b);
            if (needsMulti) _multiDependentItems.Add(b);
            return b;
        }

        // ── パーツを増やす・減らす ──
        ts.Items.Add(Btn("＋ 追加", "新しいパーツを1つ追加します", (s, e) => AddPart()));
        ts.Items.Add(Btn("⧉ 複製", "選んだパーツを複製します（Ctrl＋D）", (s, e) => DuplicateSelected(), needsSelection: true));
        ts.Items.Add(Btn("🪞 反転複製", "選んだパーツを左右反転して複製します（動きのスクリプトも鏡写しになります）", (s, e) => MirrorSelected(), needsSelection: true));
        ts.Items.Add(Btn("🗑 削除", "選んだパーツを削除します（Ctrl＋Zで戻せます）", (s, e) => DeleteSelected(), needsSelection: true));
        ts.Items.Add(new ToolStripSeparator());

        // ── 重なり順・整列 ──
        ts.Items.Add(Btn("⬆ 手前へ", "zOrderを＋1します（大きいほど手前に描かれます）", (s, e) => ChangeZOrder(+1), needsSelection: true));
        ts.Items.Add(Btn("⬇ 奥へ", "zOrderを－1します", (s, e) => ChangeZOrder(-1), needsSelection: true));
        var align = new ToolStripDropDownButton("⇔ 整列") { ToolTipText = "選んだパーツをそろえます（2つ以上を選んだとき）", DisplayStyle = ToolStripItemDisplayStyle.Text };
        void AlignItem(string text, Action act) { var mi = new ToolStripMenuItem(text); mi.Click += (s, e) => act(); align.DropDownItems.Add(mi); }
        AlignItem("左にそろえる", () => AlignSelected(AlignKind.Left));
        AlignItem("横の中心にそろえる", () => AlignSelected(AlignKind.CenterX));
        AlignItem("右にそろえる", () => AlignSelected(AlignKind.Right));
        align.DropDownItems.Add(new ToolStripSeparator());
        AlignItem("上にそろえる", () => AlignSelected(AlignKind.Top));
        AlignItem("縦の中心にそろえる", () => AlignSelected(AlignKind.CenterY));
        AlignItem("下にそろえる", () => AlignSelected(AlignKind.Bottom));
        align.DropDownItems.Add(new ToolStripSeparator());
        AlignItem("横に等間隔にならべる", () => AlignSelected(AlignKind.DistributeX));
        AlignItem("縦に等間隔にならべる", () => AlignSelected(AlignKind.DistributeY));
        _multiDependentItems.Add(align);
        ts.Items.Add(align);
        ts.Items.Add(new ToolStripSeparator());

        // ── テンプレートと動き ──
        var tpl = Btn("✨ テンプレートから追加…", "多節体・砲身・目・舌・翼など、型にはまった複合パーツを一式で追加します", (s, e) => OpenTemplatePicker());
        tpl.BackColor = Color.FromArgb(255, 244, 214);
        ts.Items.Add(tpl);
        var motion = new ToolStripDropDownButton("🌀 動きを追加") { ToolTipText = "回転する棒・振り子・公転を、数値を入れるだけで作ります", DisplayStyle = ToolStripItemDisplayStyle.Text };
        void MotionItem(string text, Action act) { var mi = new ToolStripMenuItem(text); mi.Click += (s, e) => act(); motion.DropDownItems.Add(mi); }
        MotionItem("🌀 回転する棒として配置…", OpenRodGenerator);
        MotionItem("🕰 振り子として配置…", OpenPendulumGenerator);
        MotionItem("🛰 公転として配置…", OpenOrbitGenerator);
        motion.DropDownItems.Add(new ToolStripSeparator());
        MotionItem("🔧 選んだ動きのパターンを編集…", OpenMotionEditor);
        ts.Items.Add(motion);
        ts.Items.Add(Btn("🧩 スクリプトを全パーツへ", "主に選んだパーツの挙動スクリプトを、全パーツへコピーします", (s, e) => ApplyScriptToAllParts(), needsSelection: true));

        // ── 取り消し（右寄せ） ──
        _tbRedo = new ToolStripButton("↪ やり直す") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Ctrl＋Y", DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
        _tbRedo.Click += (s, e) => PartsRedo();
        _tbUndo = new ToolStripButton("↩ 元に戻す") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Ctrl＋Z", DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
        _tbUndo.Click += (s, e) => PartsUndo();
        ts.Items.Add(_tbRedo);
        ts.Items.Add(_tbUndo);
        return ts;
    }

    // ==== 左: パーツ一覧 ====

    // 左側パネル（パーツ一覧グリッドと並べ替えボタン）を組み立てて返す。
    // 追加・複製・削除などの操作はツールバーへ移したので、ここは「選ぶ」「並べる」だけにしてある。
    private Panel BuildListSide()
    {
        var pnl = new Panel { Dock = DockStyle.Fill };
        var lblTitle = new Label { Dock = DockStyle.Top, Height = 26, Text = "📋 パーツ一覧", Font = new Font(Font, FontStyle.Bold), Padding = new Padding(4, 6, 0, 0) };

        // パーツ一覧を表示するグリッド本体の設定。
        dgvList = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 8.5f),
            RowHeadersVisible = false,
            ScrollBars = ScrollBars.Vertical,
            RowTemplate = { Height = 30 },
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
        };
        // Feature: UI改善（提案書 PT-4）— 一覧上でどの画像のパーツか一目で分かるよう、サムネイル列を追加する。
        var colThumb = new DataGridViewImageColumn { Name = "thumb", HeaderText = "", FillWeight = 24, ImageLayout = DataGridViewImageCellLayout.Zoom };
        colThumb.DefaultCellStyle.NullValue = null;
        dgvList.Columns.AddRange(new DataGridViewColumn[]
        {
            colThumb,
            new DataGridViewTextBoxColumn { Name = "id", HeaderText = "パーツID", FillWeight = 56, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "hp", HeaderText = "HP", FillWeight = 16, ReadOnly = true },
            new DataGridViewTextBoxColumn { Name = "zOrder", HeaderText = "Z", FillWeight = 16, ReadOnly = true },
        });
        // 一覧グリッドで選択行が変わったら、選択を取り込み、詳細パネルと
        // 合成プレビュー（選択中パーツの枠を黄色く強調表示するため）を最新状態に合わせる。
        dgvList.SelectionChanged += (s, e) =>
        {
            if (_syncingList) return;
            var rows = dgvList.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).Where(i => i >= 0 && i < parts.Count).ToList();
            int primary = dgvList.CurrentRow != null && rows.Contains(dgvList.CurrentRow.Index) ? dgvList.CurrentRow.Index : (rows.Count > 0 ? rows.Min() : -1);
            _selected.Clear();
            foreach (int i in rows) _selected.Add(i);
            selectedIndex = primary;
            LoadDetailFromSelection();
            UpdateToolbarState();
            pnlComposer.Invalidate();
        };
        dgvList.ContextMenuStrip = BuildPartContextMenu();
        dgvList.CellMouseDown += (s, e) =>
        {
            // 右クリックしたとき、まだ選んでいない行なら、その行だけを選ぶ（右クリックメニューが対象を取り違えないように）
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !_selected.Contains(e.RowIndex)) SelectOnly(e.RowIndex);
        };

        // 並べ替えボタン（描画順・重なり順の基準にもなる並び）。
        var flowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(2),
        };
        var btnUp = new Button { Text = "▲ 上へ", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
        btnUp.Click += (s, e) => MoveSelectedPart(-1);
        var btnDown = new Button { Text = "▼ 下へ", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
        btnDown.Click += (s, e) => MoveSelectedPart(1);
        flowButtons.Controls.AddRange(new Control[] { btnUp, btnDown });

        pnl.Controls.Add(dgvList);
        pnl.Controls.Add(flowButtons);
        pnl.Controls.Add(lblTitle);
        return pnl;
    }

    // 右クリックメニュー（一覧・プレビュー共通）。ツールバーを探しに行かなくても、その場で主な操作ができる。
    private ContextMenuStrip BuildPartContextMenu()
    {
        var cms = new ContextMenuStrip();
        void Item(string text, Action act) { var mi = new ToolStripMenuItem(text); mi.Click += (s, e) => act(); cms.Items.Add(mi); }
        Item("⧉ 複製　Ctrl+D", DuplicateSelected);
        Item("🪞 反転複製", MirrorSelected);
        Item("🗑 削除　Delete", DeleteSelected);
        cms.Items.Add(new ToolStripSeparator());
        Item("⬆ 手前へ", () => ChangeZOrder(+1));
        Item("⬇ 奥へ", () => ChangeZOrder(-1));
        cms.Items.Add(new ToolStripSeparator());
        Item("📁 画像を選ぶ…", PickSpriteForSelected);
        Item("📝 挙動スクリプトを編集…", EditScriptForSelected);
        Item("🧩 スクリプトを全パーツへ", ApplyScriptToAllParts);
        cms.Opening += (s, e) => { if (_selected.Count == 0) e.Cancel = true; };
        return cms;
    }

    // パーツ一覧グリッドの中身を、現在のpartsリストの内容で全面的に描き直す。
    // 一覧に変更を加える操作（追加/削除/並び替え等）は、最後に必ずこのメソッドを呼んで
    // 画面表示を最新状態に同期させている。
    private void RefreshList()
    {
        // 再描画後もできるだけ同じ行の選択状態を保つため、現在の選択を退避しておく。
        _selected.RemoveWhere(i => i < 0 || i >= parts.Count);
        _syncingList = true;
        try
        {
            dgvList.Rows.Clear();
            // 各パーツについて、サムネイル画像・ID・HP・zOrderの4列分の行を追加する。
            foreach (var p in parts) dgvList.Rows.Add(GetPartThumb(p), p.id, p.hp, p.zOrder);
        }
        finally { _syncingList = false; }
        if (parts.Count > 0)
        {
            // 選択が空なら先頭(0)を選択する（開いた直後に詳細パネルが空のままにならないように）。
            if (_selected.Count == 0) _selected.Add(Math.Clamp(selectedIndex < 0 ? 0 : selectedIndex, 0, parts.Count - 1));
            if (selectedIndex < 0 || !_selected.Contains(selectedIndex)) selectedIndex = _selected.Min();
        }
        else
        {
            // パーツが1つもない場合は「未選択」状態にする。
            selectedIndex = -1;
            _selected.Clear();
        }
        SyncListSelection();
        // 選択状態が変わった可能性があるため、詳細パネルと合成プレビューも合わせて更新する。
        LoadDetailFromSelection();
        UpdateToolbarState();
        pnlComposer.Invalidate();
    }

    // 一覧の1行ぶんだけ、HPとzOrderの表示を最新にする（詳細パネルの入力中に全面更新すると選択が乱れるため）
    private void RefreshListCells()
    {
        for (int i = 0; i < parts.Count && i < dgvList.Rows.Count; i++)
        {
            dgvList.Rows[i].Cells["id"].Value = parts[i].id;
            dgvList.Rows[i].Cells["hp"].Value = parts[i].hp;
            dgvList.Rows[i].Cells["zOrder"].Value = parts[i].zOrder;
        }
    }

    // ==== パーツを増やす・減らす・並べ替える ====

    // 新規パーツを1つ追加する。
    private void AddPart()
    {
        // "part1", "part2", ... のように、既存IDと重複しない連番のIDを自動生成する。
        string newId = MakeUniquePartId("part", startAt: 1);
        // 位置は本体の中心に置く（原点(0,0)＝本体の左上のままだと、プレビューの端に隠れて見失いやすいため）。
        var (bw, bh) = BodySize();
        var np = new PartDef { id = newId, offsetX = MathF.Round(bw / 2f - 12f), offsetY = MathF.Round(bh / 2f - 12f), width = 24, height = 24 };
        parts.Add(np);
        RefreshList();
        SelectOnly(parts.Count - 1);
        PushHistory();
        Toast($"パーツ「{newId}」を追加しました。画像は右の「📁 選ぶ」から設定できます。");
    }

    // 選択中のパーツ（複数なら全部）を削除する。
    // 確認ダイアログは出さない（Ctrl＋Z で戻せるため。毎回止まるより、間違えたら戻すほうが速い）。
    private void DeleteSelected()
    {
        if (_selected.Count == 0) { Toast("削除するパーツを選んでください。", warn: true); return; }
        var idxs = _selected.OrderByDescending(i => i).ToList();
        int first = idxs.Last();
        foreach (int i in idxs)
        {
            // サムネイルキャッシュに残ったままだとメモリリークになるため、削除前に破棄しておく。
            InvalidatePartThumb(parts[i]);
            parts.RemoveAt(i);
        }
        _selected.Clear();
        selectedIndex = -1;
        RefreshList();
        if (parts.Count > 0) SelectOnly(Math.Min(first, parts.Count - 1));
        PushHistory();
        Toast($"{idxs.Count}個のパーツを削除しました（Ctrl＋Zで戻せます）。");
    }

    // 選択中のパーツを一覧内で上下に1つ移動する（並び順を入れ替える）。
    // dir : -1で上へ、+1で下へ移動する。複数選択のときは主選択だけを動かす。
    private void MoveSelectedPart(int dir)
    {
        if (selectedIndex < 0 || selectedIndex >= parts.Count) return;
        int newIdx = selectedIndex + dir;
        // 移動先が一覧の範囲外（先頭より上、末尾より下）になる場合は何もしない。
        if (newIdx < 0 || newIdx >= parts.Count) return;
        // タプルの分解代入を使い、選択中パーツと移動先のパーツの位置を入れ替える。
        (parts[selectedIndex], parts[newIdx]) = (parts[newIdx], parts[selectedIndex]);
        RefreshList();
        SelectOnly(newIdx);
        PushHistory();
    }

    // 選択中のパーツの zOrder を delta だけ変える（大きいほど手前）。
    private void ChangeZOrder(int delta)
    {
        if (_selected.Count == 0) return;
        foreach (int i in _selected) parts[i].zOrder = Math.Clamp(parts[i].zOrder + delta, -100, 100);
        RefreshListCells();
        LoadDetailFromSelection();
        pnlComposer.Invalidate();
        PushHistory("zOrder");
    }

    // 選択中のパーツを (dx, dy) だけ動かす（矢印キー）。
    private void NudgeSelected(int dx, int dy)
    {
        if (_selected.Count == 0 || _isPlaying) return;
        foreach (int i in _selected) { parts[i].offsetX += dx; parts[i].offsetY += dy; }
        LoadDetailFromSelection();
        pnlComposer.Invalidate();
        PushHistory("nudge");
    }

    // 選択中パーツの挙動スクリプトを、丸ごと複製して全パーツに上書き適用する。
    // 例えば「回転する棒」のように、複数パーツが同じスクリプトを共有しつつPartIndexで
    // 個々の見た目を変える、という構成をまとめて設定したいときに使う。
    private void ApplyScriptToAllParts()
    {
        if (parts.Count == 0) { Toast("パーツがありません。", warn: true); return; }
        if (selectedIndex < 0) { Toast("コピー元にするパーツを選んでください。", warn: true); return; }
        // コピー元のスクリプトを1回だけディープクローンして取得する。
        var srcScript = (JArray)parts[selectedIndex].script.DeepClone();
        // 各パーツへは、それぞれ独立したインスタンスになるよう毎回改めてディープクローンして代入する
        // （同じJArrayインスタンスを複数パーツで共有すると、1パーツの編集が他パーツにも影響してしまうため）。
        foreach (var p in parts) p.script = (JArray)srcScript.DeepClone();
        PushHistory();
        LoadDetailFromSelection();

        // 適用結果が視覚的に分かるよう、全パーツの枠を一瞬（900ミリ秒）緑色に光らせる演出。
        _highlightedParts.Clear();
        foreach (var p in parts) _highlightedParts.Add(p);
        pnlComposer.Invalidate();
        // 前回のハイライト用タイマーが動いていれば止めてから、新しいワンショットタイマーを開始する。
        _highlightTimer?.Stop();
        _highlightTimer = new System.Windows.Forms.Timer { Interval = 900 };
        _highlightTimer.Tick += (s, e) => { _highlightedParts.Clear(); _highlightTimer!.Stop(); pnlComposer.Invalidate(); };
        _highlightTimer.Start();

        Toast($"「{parts[selectedIndex].id}」のスクリプトを全{parts.Count}パーツに適用しました（PartIndexを使えば、同じスクリプトでもパーツごとに位相・動きを変えられます）。");
    }

    // 選択中のパーツ（複数なら全部）を複製する。画像/当たり判定/スクリプトを全てコピーし、位置だけ少しずらす。
    private void DuplicateSelected()
    {
        if (_selected.Count == 0) { Toast("複製したいパーツを選んでください。", warn: true); return; }
        var newIdx = new List<int>();
        // 後ろから挿入すると、前のパーツの添字がずれない
        foreach (int i in _selected.OrderByDescending(i => i))
        {
            var copy = ClonePart(parts[i]);
            // IDは元と同じままだと一覧上で区別できなくなるため、"_copy"を付けた重複しないIDに変更する。
            copy.id = MakeUniquePartId(parts[i].id + "_copy");
            // 複製直後に元パーツと完全に重なって見えなくならないよう、少しだけ位置をずらす。
            copy.offsetX += 16;
            copy.offsetY += 16;
            parts.Insert(i + 1, copy);
        }
        // 挿入後の添字を数え直す（元の選択より前に挿入された数だけ後ろへずれる）
        var orig = _selected.OrderBy(i => i).ToList();
        for (int k = 0; k < orig.Count; k++) newIdx.Add(orig[k] + 1 + k);
        RefreshList();
        SetSelection(newIdx, newIdx.Count > 0 ? newIdx[0] : -1);
        PushHistory();
        Toast($"{newIdx.Count}個のパーツを複製しました。");
    }

    // baseIdを基準に、現在のパーツ一覧内で重複しないIDを生成する。
    // baseId自体が未使用ならそのまま返し、既に使われていれば "baseId2", "baseId3", ... の
    // ように末尾へ連番を付けて重複しなくなるまで探す。startAt を与えたときは、最初から連番を付ける。
    private string MakeUniquePartId(string baseId, int startAt = 0)
    {
        var existing = new HashSet<string>(parts.Select(p => p.id));
        if (startAt == 0)
        {
            if (!existing.Contains(baseId)) return baseId;
            int m = 2;
            string id2;
            do { id2 = $"{baseId}{m}"; m++; } while (existing.Contains(id2));
            return id2;
        }
        int n = startAt;
        string id;
        do { id = $"{baseId}{n}"; n++; } while (existing.Contains(id));
        return id;
    }

    // 主に選んでいるパーツの動き（モーション）の一覧を、同時に選んでいる他のパーツへコピーする。
    // 「パーツ番号ごとのずれ」を使っておけば、コピーした同じ動きが少しずつずれて動く。
    private void CopyMotionsToOthers()
    {
        if (selectedIndex < 0 || selectedIndex >= parts.Count) return;
        var others = _selected.Where(i => i != selectedIndex).ToList();
        if (others.Count == 0) { Toast("コピー先がありません。Ctrl＋クリックで、コピーしたいパーツも一緒に選んでください。", warn: true); return; }
        var src = parts[selectedIndex].motions;
        foreach (int i in others) parts[i].motions = src.Select(m => m.Clone()).ToList();
        PushHistory("motion-copy");
        RefreshListCells();
        pnlComposer.Invalidate();
        Toast($"動き（{src.Count}個）を、他の{others.Count}個のパーツへコピーしました。");
    }

    // Feature: UI改善（提案書 PT-2）— 選択中パーツ（複数なら全部）を左右反転して複製する。棘やツノなど左右対称の装飾を
    // 片側だけ作ってワンクリックでもう片方を得られるようにする。静的なoffsetXだけでなく、
    // 挙動スクリプト内のSetLocalOffset(dx)/SetLocalOffsetPolar(angle)も再帰的に反転させるため、
    // 回転する棒/振り子/公転のいずれで生成したパーツでも正しく鏡写しになる。
    private void MirrorSelected()
    {
        if (_selected.Count == 0) { Toast("反転複製したいパーツを選んでください。", warn: true); return; }
        // 左右反転の軸は「本体の中心」。本体の左右の幅の中央に対して、パーツの左右を入れ替える。
        var (bw, _) = BodySize();
        var newIdx = new List<int>();
        var orig = _selected.OrderBy(i => i).ToList();
        foreach (int i in orig.AsEnumerable().Reverse())
        {
            var src = parts[i];
            var copy = ClonePart(src);
            // IDの命名規則: 元IDが"_L"で終わっていれば"_R"に、"_R"で終わっていれば"_L"に付け替える
            // （左右のペアだと分かりやすくするため）。どちらでもなければ単純に"_mirror"を付ける。
            string mirroredBaseId =
                src.id.EndsWith("_L", StringComparison.Ordinal) ? src.id[..^2] + "_R" :
                src.id.EndsWith("_R", StringComparison.Ordinal) ? src.id[..^2] + "_L" :
                src.id + "_mirror";
            copy.id = MakeUniquePartId(mirroredBaseId);
            // 静的な位置を、本体の中心線に対して左右反転する。
            // 以前は原点(本体の左上)に対して offsetX の符号を反転していたため、本体の右側にあるパーツが
            // 本体の外の左側（見えない場所）へ飛んでいた。パーツの「左上」を基準に、幅ぶんを考慮する。
            float w = EffectivePartSize(src).w;
            copy.offsetX = bw - src.offsetX - w;
            copy.hitboxOffsetX = -copy.hitboxOffsetX;
            // 静的な値だけでなく、挙動スクリプト内で動的に位置を決めている部分（SetLocalOffset系）も
            // 再帰的に反転させる。これにより回転する棒/振り子/公転のいずれで生成したパーツでも、
            // 動きまで含めて正しく鏡写しになる。
            MirrorScriptHorizontalInPlace(copy.script);
            // モーションも鏡写しにする。動く向きは左右を入れ替え、首振り・回転は向きを逆にする
            // （円運動・プレイヤーを向く、は左右対称に作ってあるのでそのまま）。
            foreach (var mo in copy.motions)
            {
                mo.axis = 180f - mo.axis;
                if (mo.kind is "swing" or "spin") mo.amount = -mo.amount;
            }
            parts.Insert(i + 1, copy);
        }
        for (int k = 0; k < orig.Count; k++) newIdx.Add(orig[k] + 1 + k);
        RefreshList();
        SetSelection(newIdx, newIdx.Count > 0 ? newIdx[0] : -1);
        PushHistory();
        Toast($"{newIdx.Count}個のパーツを左右反転して複製しました。");
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

    // ==== 整列・等間隔 ====
    private enum AlignKind { Left, CenterX, Right, Top, CenterY, Bottom, DistributeX, DistributeY }

    // 選んだパーツ（2つ以上）をそろえる。基準は「選んだ全員をまとめて囲む箱」。
    // 等間隔は、両端の2つを動かさず、間のパーツを同じ間隔になるよう並べ直す（並びは現在の位置の順）。
    private void AlignSelected(AlignKind kind)
    {
        var idxs = _selected.OrderBy(i => i).ToList();
        if (idxs.Count < 2) { Toast("2つ以上のパーツを選んでください（Ctrl＋クリックか、範囲ドラッグ）。", warn: true); return; }
        var rects = idxs.ToDictionary(i => i, i => { var s = EffectivePartSize(parts[i]); return new RectangleF(parts[i].offsetX, parts[i].offsetY, s.w, s.h); });
        float minX = rects.Values.Min(r => r.Left), maxX = rects.Values.Max(r => r.Right);
        float minY = rects.Values.Min(r => r.Top), maxY = rects.Values.Max(r => r.Bottom);
        switch (kind)
        {
            case AlignKind.Left: foreach (int i in idxs) parts[i].offsetX = minX; break;
            case AlignKind.Right: foreach (int i in idxs) parts[i].offsetX = maxX - rects[i].Width; break;
            case AlignKind.CenterX: foreach (int i in idxs) parts[i].offsetX = (minX + maxX) / 2f - rects[i].Width / 2f; break;
            case AlignKind.Top: foreach (int i in idxs) parts[i].offsetY = minY; break;
            case AlignKind.Bottom: foreach (int i in idxs) parts[i].offsetY = maxY - rects[i].Height; break;
            case AlignKind.CenterY: foreach (int i in idxs) parts[i].offsetY = (minY + maxY) / 2f - rects[i].Height / 2f; break;
            case AlignKind.DistributeX:
            {
                var order = idxs.OrderBy(i => rects[i].Left).ToList();
                float total = order.Sum(i => rects[i].Width);
                float gap = ((maxX - minX) - total) / (order.Count - 1);
                float x = minX;
                foreach (int i in order) { parts[i].offsetX = x; x += rects[i].Width + gap; }
                break;
            }
            case AlignKind.DistributeY:
            {
                var order = idxs.OrderBy(i => rects[i].Top).ToList();
                float total = order.Sum(i => rects[i].Height);
                float gap = ((maxY - minY) - total) / (order.Count - 1);
                float y = minY;
                foreach (int i in order) { parts[i].offsetY = y; y += rects[i].Height + gap; }
                break;
            }
        }
        LoadDetailFromSelection();
        pnlComposer.Invalidate();
        PushHistory();
        Toast($"{idxs.Count}個のパーツをそろえました。");
    }

    // ==== 右: 選択中パーツの詳細編集パネル ====

    // 右側のパネル（選択中パーツの詳細編集フォーム）を組み立てて返す。
    // 項目を「基本・位置・大きさ・性質・動き」のグループに分け、全体を縦スクロールにしてある。
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
            Padding = new Padding(6, 4, 6, 8),
        };

        var lblTitle = new Label { AutoSize = true, Text = "🔧 選択中パーツの詳細", Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 4, 0, 2) };
        lblSelectionInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 2), MaximumSize = new Size(300, 0) };
        lblMultiNote = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 90, 0), Margin = new Padding(0, 0, 0, 2), MaximumSize = new Size(300, 0), Visible = false };
        flow.Controls.Add(lblTitle);
        flow.Controls.Add(lblSelectionInfo);
        flow.Controls.Add(lblMultiNote);

        // 見出し（グループ名）を1つ作るローカル関数
        Label Heading(string text) => new Label
        {
            Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(40, 80, 140),
            Margin = new Padding(0, 10, 0, 2),
        };
        // ラベル＋入力コントロールを縦に並べた表を作るローカル関数
        TableLayoutPanel NewTable()
        {
            var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0), Padding = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }
        void AddRow(TableLayoutPanel table, string label, Control control)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 3, 3) };
            control.Margin = new Padding(3, 3, 3, 3);
            table.Controls.Add(lbl, 0, r);
            table.Controls.Add(control, 1, r);
        }

        // ── 基本：ID と 画像 ──
        flow.Controls.Add(Heading("基本"));
        var tBasic = NewTable();
        txtId = new TextBox { Width = 170 };
        // 入力中はリアルタイムでID・一覧・プレビューに反映するが、履歴には文字ごとに積まない（まとめて1手）。
        txtId.TextChanged += (s, e) => { if (!_suppressEvents && selectedIndex >= 0) { parts[selectedIndex].id = txtId.Text; RefreshListCells(); pnlComposer.Invalidate(); PushHistory("id"); } };
        AddRow(tBasic, "パーツID", txtId);

        var spritePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        picSprite = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(240, 240, 240), Margin = new Padding(0, 0, 6, 0) };
        var spriteRight = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        lblSpriteValue = new Label { Text = "(画像なし)", AutoSize = true, MaximumSize = new Size(170, 0), ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 2) };
        var btnPickSprite = new Button { Text = "📁 画像を選ぶ", AutoSize = true, Padding = new Padding(4, 2, 4, 2), Margin = new Padding(0) };
        btnPickSprite.Click += (s, e) => PickSpriteForSelected();
        spriteRight.Controls.AddRange(new Control[] { lblSpriteValue, btnPickSprite });
        spritePanel.Controls.AddRange(new Control[] { picSprite, spriteRight });
        AddRow(tBasic, "画像", spritePanel);
        flow.Controls.Add(tBasic);

        // ── 位置 ──
        flow.Controls.Add(Heading("位置（本体の左上が 0,0）"));
        var tPos = NewTable();
        nudOffsetX = MakeNumeric(-4000, 4000, 1);
        nudOffsetX.ValueChanged += (s, e) => EditPrimary("offsetX", p => p.offsetX = (float)nudOffsetX.Value);
        AddRow(tPos, "横 (offsetX)", nudOffsetX);
        nudOffsetY = MakeNumeric(-4000, 4000, 1);
        nudOffsetY.ValueChanged += (s, e) => EditPrimary("offsetY", p => p.offsetY = (float)nudOffsetY.Value);
        AddRow(tPos, "縦 (offsetY)", nudOffsetY);
        flow.Controls.Add(tPos);

        // ── 大きさ ──
        flow.Controls.Add(Heading("大きさ"));
        var tSize = NewTable();
        nudWidth = MakeNumeric(0, 2000, 0);
        nudWidth.ValueChanged += (s, e) => EditPrimary("width", p => p.width = (int)nudWidth.Value);
        AddRow(tSize, "幅", nudWidth);
        nudHeight = MakeNumeric(0, 2000, 0);
        nudHeight.ValueChanged += (s, e) => EditPrimary("height", p => p.height = (int)nudHeight.Value);
        AddRow(tSize, "高さ", nudHeight);
        var btnFitImage = new Button { Text = "画像の大きさに合わせる", AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
        btnFitImage.Click += (s, e) => FitSizeToImage();
        AddRow(tSize, "", btnFitImage);
        var lblSizeHint = new Label { Text = "0 = 画像の原寸。プレビューのつまみ（右下）でも変えられます", AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7.5f), MaximumSize = new Size(190, 0) };
        AddRow(tSize, "", lblSizeHint);
        nudScale = MakeNumeric(0.1m, 10m, 2, 1m);
        nudScale.Increment = 0.1m;
        nudScale.ValueChanged += (s, e) => EditSelected("scale", p => p.scale = (float)nudScale.Value);
        AddRow(tSize, "表示スケール", nudScale);
        flow.Controls.Add(tSize);

        // ── 性質 ──
        flow.Controls.Add(Heading("性質"));
        var tProp = NewTable();
        nudHp = MakeNumeric(0, 999, 0);
        nudHp.ValueChanged += (s, e) => { EditSelected("hp", p => p.hp = (int)nudHp.Value); UpdateHpHint(); };
        AddRow(tProp, "HP", nudHp);
        // HPの意味（0=不滅か、何発で壊れるか）を文章で説明する補助ラベル。
        lblHpHint = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7.5f), MaximumSize = new Size(190, 0) };
        AddRow(tProp, "", lblHpHint);
        chkDeadly = new CheckBox { Text = "触れるとダメージ", AutoSize = true };
        chkDeadly.CheckedChanged += (s, e) => EditSelected("deadly", p => p.deadly = chkDeadly.Checked);
        AddRow(tProp, "接触", chkDeadly);
        nudZOrder = MakeNumeric(-100, 100, 0);
        nudZOrder.ValueChanged += (s, e) => EditSelected("zOrder", p => p.zOrder = (int)nudZOrder.Value);
        AddRow(tProp, "重なり (zOrder)", nudZOrder);
        var lblZHint = new Label { Text = "マイナス＝本体より奥、プラス＝手前", AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7.5f) };
        AddRow(tProp, "", lblZHint);
        flow.Controls.Add(tProp);

        // ── 動きと当たり判定 ──
        flow.Controls.Add(Heading("動きと当たり判定"));
        lblScriptInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 2), MaximumSize = new Size(300, 0) };
        flow.Controls.Add(lblScriptInfo);
        var flowScript = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0) };
        var btnScript = new Button { Text = "📝 挙動スクリプトを編集…", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
        btnScript.Click += (s, e) => EditScriptForSelected();
        flowScript.Controls.Add(btnScript);
        flow.Controls.Add(flowScript);
        lblHitboxInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 0, 2), MaximumSize = new Size(300, 0) };
        flow.Controls.Add(lblHitboxInfo);
        var btnHitbox = new Button { Text = "🎯 当たり判定を編集…", AutoSize = true, Padding = new Padding(6, 3, 6, 3) };
        btnHitbox.Click += (s, e) => EditHitboxForSelected();
        flow.Controls.Add(btnHitbox);

        // ── 動き（モーション）──
        // 「いつ」「どんな動き」を一覧とスライダーで作る。スクリプトとは独立で、併用できる。
        // プレビューの「状況を試す」で、攻撃した・溜めている等の状況を作って確かめられる。
        flow.Controls.Add(Heading("動き（モーション）"));
        _motionEditor = new PartMotionEditorControl();
        _motionEditor.Changed += key =>
        {
            if (_suppressEvents) return;
            UpdateSelectionInfo();
            pnlComposer.Invalidate();
            PushHistory(key);
            // 動きを作ったら、すぐ確かめられるよう再生を始める（止めたままだと変化が見えない）
            if (!_isPlaying && selectedIndex >= 0 && parts[selectedIndex].motions.Count > 0) TogglePreviewPlayback();
        };
        _motionEditor.CopyToOthersRequested += CopyMotionsToOthers;
        flow.Controls.Add(_motionEditor);

        pnl.Controls.Add(flow);
        return pnl;
    }

    // NumericUpDownコントロールを、最小値・最大値・小数桁数・初期値を指定して1行で生成するヘルパー。
    // 同じような設定を何度も書かずに済ませるためのショートカット。
    private static NumericUpDown MakeNumeric(decimal min, decimal max, int decimals, decimal value = 0)
        => new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value, Width = 110 };

    // 詳細パネルの入力から、主選択のパーツ1つだけを書き換える（位置・大きさ・ID）。
    private void EditPrimary(string key, Action<PartDef> apply)
    {
        if (_suppressEvents || selectedIndex < 0) return;
        apply(parts[selectedIndex]);
        RefreshListCells();
        UpdateSelectionInfo();
        pnlComposer.Invalidate();
        PushHistory(key);
    }

    // 詳細パネルの入力から、選んだパーツ全部を書き換える（HP・zOrder・スケール・接触ダメージ）。
    // 複数選択のとき、同じ値をまとめて設定できる。
    private void EditSelected(string key, Action<PartDef> apply)
    {
        if (_suppressEvents || _selected.Count == 0) return;
        foreach (int i in _selected) apply(parts[i]);
        RefreshListCells();
        UpdateSelectionInfo();
        pnlComposer.Invalidate();
        PushHistory(key);
    }

    // 選択中パーツの値を、詳細パネルの各入力コントロールに反映する。
    // 一覧やキャンバスでの選択が変わるたびに呼ばれ、右パネルの表示をその場で選ばれたパーツの内容に合わせる。
    private void LoadDetailFromSelection()
    {
        if (txtId == null!) return;
        bool hasSel = selectedIndex >= 0 && selectedIndex < parts.Count;
        // ここでコントロールの値を設定すると各種ValueChanged/TextChangedイベントが発火してしまうが、
        // これはユーザー操作ではなく「表示の同期」のためだけなので、履歴に積んだり二重更新したりしないよう
        // 一時的にイベント処理を抑制するフラグを立てる。
        _suppressEvents = true;
        try
        {
            if (hasSel)
            {
                var p = parts[selectedIndex];
                txtId.Text = p.id;
                lblSpriteValue.Text = string.IsNullOrEmpty(p.sprite) ? "(画像なし)" : p.sprite;
                picSprite.Image = GetPartThumb(p);
                // NumericUpDownのMinimum/Maximumを超える値を設定しようとすると例外になるため、
                // Math.Clampで必ず範囲内に収めてから代入する（パーツ側の値が想定外に大きい場合の保険）。
                nudOffsetX.Value = (decimal)Math.Clamp(p.offsetX, (float)nudOffsetX.Minimum, (float)nudOffsetX.Maximum);
                nudOffsetY.Value = (decimal)Math.Clamp(p.offsetY, (float)nudOffsetY.Minimum, (float)nudOffsetY.Maximum);
                nudWidth.Value = Math.Clamp(p.width, (int)nudWidth.Minimum, (int)nudWidth.Maximum);
                nudHeight.Value = Math.Clamp(p.height, (int)nudHeight.Minimum, (int)nudHeight.Maximum);
                nudScale.Value = (decimal)Math.Clamp(p.scale, (float)nudScale.Minimum, (float)nudScale.Maximum);
                nudHp.Value = Math.Clamp(p.hp, (int)nudHp.Minimum, (int)nudHp.Maximum);
                nudZOrder.Value = Math.Clamp(p.zOrder, (int)nudZOrder.Minimum, (int)nudZOrder.Maximum);
                chkDeadly.Checked = p.deadly;
                UpdateHpHint();
                lblScriptInfo.Text = ScriptSummary(p);
                lblHitboxInfo.Text = $"当たり判定：位置({p.hitboxOffsetX}, {p.hitboxOffsetY})　大きさ {p.hitboxWidth}×{p.hitboxHeight}";
            }
            else
            {
                // パーツが選択されていない場合は、全項目を初期値表示にリセットする。
                txtId.Text = "";
                lblSpriteValue.Text = "(パーツ未選択)";
                picSprite.Image = null;
                nudOffsetX.Value = 0; nudOffsetY.Value = 0; nudWidth.Value = 0; nudHeight.Value = 0; nudScale.Value = 1; nudHp.Value = 0; nudZOrder.Value = 0;
                chkDeadly.Checked = false;
                lblHpHint.Text = "";
                lblScriptInfo.Text = "";
                lblHitboxInfo.Text = "";
            }
            // 動き（モーション）の編集欄を、選んでいるパーツの一覧へ向ける
            _motionEditor?.Bind(hasSel ? parts[selectedIndex].motions : null);
            // 未選択の間は編集しても意味がないため、全ての入力コントロールを無効化してユーザーに
            // 「まずパーツを選んでください」という状態を視覚的に伝える。
            foreach (Control c in new Control[] { txtId, nudOffsetX, nudOffsetY, nudWidth, nudHeight, nudScale, nudHp, nudZOrder, chkDeadly }) c.Enabled = hasSel;
            // 位置・大きさ・IDは主選択だけに効く。複数選択のときは、そのことを見出しで伝える。
            bool multi = _selected.Count >= 2;
            lblMultiNote.Visible = multi;
            lblMultiNote.Text = multi ? $"{_selected.Count}個を選択中。スケール・HP・接触・zOrderは全員に、ID・位置・大きさは主に選んだ1つだけに効きます。" : "";
            UpdateSelectionInfo();
        }
        finally { _suppressEvents = false; }
    }

    // 詳細パネル上部の、選択中パーツの要約（名前・位置・大きさ）を更新する
    private void UpdateSelectionInfo()
    {
        if (lblSelectionInfo == null!) return;
        if (selectedIndex < 0 || selectedIndex >= parts.Count) { lblSelectionInfo.Text = parts.Count == 0 ? "パーツがありません。ツールバーの「＋ 追加」か「✨ テンプレート」から作れます。" : "パーツを選んでください。"; return; }
        var p = parts[selectedIndex];
        var s = EffectivePartSize(p);
        lblSelectionInfo.Text = $"{p.id}　位置({p.offsetX:0.#}, {p.offsetY:0.#})　大きさ {s.w:0.#}×{s.h:0.#}";
    }

    // パーツのスクリプトの要約（あり/なしと、命令の数）。スクリプト編集画面を開かなくても、動くパーツかどうかが分かる。
    private static string ScriptSummary(PartDef p)
    {
        int n = CountOps(p.script);
        return n == 0 ? "📝 挙動スクリプト：なし（本体にただ追従します）" : $"📝 挙動スクリプト：あり（命令 {n} 個）";
    }
    private static int CountOps(JToken? t)
    {
        if (t == null) return 0;
        int n = 0;
        if (t is JObject o)
        {
            if (o["op"] != null) n++;
            foreach (var prop in o.Properties()) n += CountOps(prop.Value);
        }
        else if (t is JArray a) foreach (var x in a) n += CountOps(x);
        return n;
    }

    // 現在のHP入力値に応じて、その意味を説明する補助テキストを更新する。
    // HP=0は「常時存在する破壊不能な障害物」、それ以外は「弾を何発当てれば壊れるか」を表す。
    private void UpdateHpHint()
    {
        int hp = (int)nudHp.Value;
        lblHpHint.Text = hp == 0 ? "0 = 破壊不能な常在ハザード" : $"{hp} = 弾{hp}発で破壊可能（本体のHP/生死には影響しません）";
    }

    // 表示サイズ（論理）を、画像の原寸に合わせる。幅・高さが0（＝原寸）のままだと、つまみで大きさを変えにくいので、
    // 一度ここで実寸の数値にしておくと、そこから整数で調整できる。
    private void FitSizeToImage()
    {
        if (selectedIndex < 0) return;
        var p = parts[selectedIndex];
        var thumb = GetPartThumb(p);
        if (thumb == null) { Toast("画像が設定されていません。", warn: true); return; }
        p.width = thumb.Width; p.height = thumb.Height;
        LoadDetailFromSelection();
        pnlComposer.Invalidate();
        PushHistory();
        Toast($"大きさを画像の原寸（{thumb.Width}×{thumb.Height}）にしました。");
    }

    // 選択中パーツ（複数なら全部）の画像ファイルをファイル選択ダイアログで選ばせ、プロジェクトのimg/フォルダへ
    // コピーした上でパーツに設定する。
    private void PickSpriteForSelected()
    {
        if (selectedIndex < 0) { Toast("先にパーツを選んでください。", warn: true); return; }
        using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "パーツ画像を選択" };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        // プロジェクト外のファイルを直接参照すると後で場所を移動された時に壊れるため、
        // 必ずimg/フォルダにコピーしてから、その相対パスをパーツに記録する。
        string relPath = ImageImportHelper.CopyIntoImgFolder(projectRoot, ofd.FileName);
        foreach (int i in _selected)
        {
            var p = parts[i];
            p.sprite = relPath;
            // 画像を差し替えたので、古いサムネイルキャッシュは無効化して次回描画時に再読み込みさせる。
            InvalidatePartThumb(p);
        }
        RefreshList();
        PushHistory();
        Toast(_selected.Count >= 2 ? $"{_selected.Count}個のパーツの画像を変えました。" : "画像を変えました。");
    }

    // 「当たり判定を編集」ボタンの処理。このページ内では編集せず、専用の当たり判定編集画面へ
    // ドリルダウンするようイベントで要求する。編集完了時のコールバックでパーツに値を書き戻す。
    private void EditHitboxForSelected()
    {
        if (selectedIndex < 0) { Toast("先にパーツを選んでください。", warn: true); return; }
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
            LoadDetailFromSelection();
            pnlComposer.Invalidate();
            PushHistory();
        });
    }

    // 「挙動スクリプトを編集」ボタンの処理。当たり判定編集と同様、専用のスクリプト編集画面へ
    // ドリルダウンし、確定されたスクリプトをパーツに書き戻す。
    private void EditScriptForSelected()
    {
        if (selectedIndex < 0) { Toast("先にパーツを選んでください。", warn: true); return; }
        var p = parts[selectedIndex];
        BehaviorScriptEditRequested?.Invoke($"パーツ: {p.id}", p.script, script => { p.script = script; LoadDetailFromSelection(); PushHistory(); });
    }

    // ==== テンプレート ====

    // 「✨ テンプレートから追加」。テンプレートの一覧から選び、数値を決めると、複合パーツ一式がここへ追加される。
    // 実装は PartTemplatePickerForm（テンプレートの選択・パラメータ入力・プレビュー）に任せる。
    private void OpenTemplatePicker()
    {
        var (bw, bh) = BodySize();
        using var dlg = new PartTemplatePickerForm(projectRoot, baseSpritePath, bw, bh, parts.Count);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK || dlg.ResultParts.Count == 0) return;
        AddGeneratedParts(dlg.ResultParts, dlg.ReplaceExisting, $"テンプレート「{dlg.ResultTemplateName}」");
        if (!string.IsNullOrEmpty(dlg.ResultNote)) Toast($"{dlg.ResultParts.Count}個のパーツを追加しました。{dlg.ResultNote}");
    }

    // 生成したパーツ一式を追加する（テンプレート・各種ジェネレータで共通）。
    //   replaceExisting … true なら、いまあるパーツを全部消してから入れ替える
    // IDは、いまあるパーツと重ならないよう付け直す。追加したパーツ全員を選択状態にする。
    private void AddGeneratedParts(List<PartDef> newParts, bool replaceExisting, string label)
    {
        if (replaceExisting)
        {
            foreach (var p in parts) InvalidatePartThumb(p);
            parts.Clear();
        }
        var existing = new HashSet<string>(parts.Select(p => p.id));
        foreach (var np in newParts)
        {
            string id = np.id;
            int n = 2;
            while (existing.Contains(id)) { id = $"{np.id}_{n}"; n++; }
            np.id = id;
            existing.Add(id);
        }
        int startIdx = parts.Count;
        parts.AddRange(newParts);
        RefreshList();
        SetSelection(Enumerable.Range(startIdx, newParts.Count), startIdx);
        FitView();
        PushHistory();
        Toast($"{label}から{newParts.Count}個のパーツを{(replaceExisting ? "置き換えて" : "")}追加しました（▶ 再生で動きを確認できます）。");
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
        AddGeneratedParts(newParts, false, "「回転する棒」");
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
        AddGeneratedParts(newParts, false, "「回転する棒」（更新）");
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
        AddGeneratedParts(newParts, false, "「振り子」");
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
        AddGeneratedParts(newParts, false, "「振り子」（更新）");
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
        AddGeneratedParts(newParts, false, "「公転」");
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
        AddGeneratedParts(newParts, false, "「公転」（更新）");
    }

    // 選択中のパーツが「回転する棒」「振り子」「公転」のいずれかとして認識できるかを順に試し、
    // 一致した種類の編集ダイアログを開く（種類ごとにボタンを分けず、利用者は用途を意識しなくてよい）
    private void OpenMotionEditor()
    {
        if (selectedIndex < 0) { Toast("編集したい動きのパーツをどれか1つ選んでください。", warn: true); return; }

        var rodGroup = DetectRodGroup(selectedIndex);
        if (rodGroup != null) { EditRodGroup(rodGroup); return; }

        var pendulumGroup = DetectPendulumGroup(selectedIndex);
        if (pendulumGroup != null) { EditPendulumGroup(pendulumGroup); return; }

        var orbitGroup = DetectOrbitGroup(selectedIndex);
        if (orbitGroup != null) { EditOrbitGroup(orbitGroup); return; }

        Toast("選んだパーツは「回転する棒」「振り子」「公転」のいずれとしても認識できませんでした（「🌀 動きを追加」から作ったパーツだけ、ここで編集できます）。", warn: true);
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
    public float Amplitude; // 振れ幅（ラジアン単位。基準角度からどれだけ左右に振れるかを表す）
    public float BaseAngle; // 基準角度（ラジアン単位。振り子が静止しているときの中心となる向き。90度=真下）
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
