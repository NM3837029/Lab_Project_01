using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Lab_Editor;

// ======================================================
// SoundAssignmentForm - サウンドカタログのIDを敵/ギミック/アイテム/ステージへ割り当てる専用画面
// Feature: サウンド・アセット管理の刷新
//
// SoundManagerForm(カタログ管理)では音声ファイルをID登録するだけで、そのIDを実際に
// 「敵のダメージ音」等として割り当てるUIがどこにも無かった。このフォームはその欠落を埋める。
// 敵/ギミック/アイテムはclone-and-return方式（PartsEditorForm等と同じ）で編集する。
// clone-and-return方式とは：元データを直接書き換えるのではなく、まず複製（クローン）を作って
// それを編集し、最後にOK（保存）されたときだけ複製後の結果を呼び出し元に返す方式。
// これにより、キャンセルされた場合は元データが一切汚れないことが保証される。
// ======================================================
public class SoundAssignmentForm : Form
{
    // 保存確定後の敵一覧（各敵のSE割り当てを反映済み）。呼び出し元はこれを使って元データを更新する
    public List<EnemyDef> ResultEnemies { get; private set; } = new();
    // 保存確定後のギミック一覧（起動音の割り当てを反映済み）
    public List<GimmickDef> ResultGimmicks { get; private set; } = new();
    // 保存確定後のアイテム一覧（取得音の割り当てを反映済み）
    public List<ItemDef> ResultItems { get; private set; } = new();
    // ステージが読み込まれていない場合はnullのまま（呼び出し側はBgmIdを一切更新しない）
    public string? ResultStageBgmId { get; private set; }

    // 編集中の敵/ギミック/アイテムのデータ（コンストラクタで渡された元データの複製）
    private List<EnemyDef> _enemies;
    private List<GimmickDef> _gimmicks;
    private List<ItemDef> _items;
    // 現在開いているステージ名。nullの場合はステージが未選択であることを示し、
    // ステージBGMの割り当てセクションでは編集不可の案内を表示する
    private readonly string? _currentStageName;

    // 敵/ギミック/アイテムそれぞれのSE割り当てを表形式で編集するグリッド
    private DataGridView _gridEnemy = null!, _gridGimmick = null!, _gridItem = null!;
    // ステージBGMを選択するプルダウン
    private ComboBox _cmbStageBgm = null!;
    // 各カテゴリの表示/非表示を切り替えるためのセクションパネル（タグの絞り込みチェックボックスと連動）
    private Panel _sectionEnemy = null!, _sectionGimmick = null!, _sectionItem = null!, _sectionStage = null!;

    // Undo/Redo用の履歴管理。編集操作のたびにスナップショットを積み、Ctrl+Z/Ctrl+Yで巻き戻し/やり直しできるようにする
    private readonly HistoryManager<Snapshot> _history = new();
    private Button _btnUndo = null!, _btnRedo = null!;

    // Undo/Redoの1単位として保存する、ある時点でのすべての割り当て状態のスナップショット
    private class Snapshot
    {
        public List<EnemyDef> Enemies { get; set; } = new();
        public List<GimmickDef> Gimmicks { get; set; } = new();
        public List<ItemDef> Items { get; set; } = new();
        public string StageBgmId { get; set; } = "";
    }

    public SoundAssignmentForm(List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items,
        List<SoundDef> se, List<SoundDef> uiSe, List<SoundDef> bgm,
        string? currentStageName, string currentStageBgmId)
    {
        // 元データを直接編集しないよう、まず複製を作ってから編集対象とする（clone-and-return方式）
        _enemies = CloneList(enemies);
        _gimmicks = CloneList(gimmicks);
        _items = CloneList(items);
        _currentStageName = currentStageName;

        // 「未設定（鳴らさない）」を選べるよう、先頭に空文字の選択肢を追加した候補配列を作る。
        // SE候補は通常の効果音カタログとUI用効果音カタログの両方を結合したもの
        string[] seChoices = new[] { "" }.Concat(se.Select(s => s.id)).Concat(uiSe.Select(s => s.id)).ToArray();
        string[] bgmChoices = new[] { "" }.Concat(bgm.Select(b => b.id)).ToArray();

        InitUI(seChoices, bgmChoices, currentStageBgmId);
        // 初期状態を履歴の最初の1件として積んでおく（これがないと最初の変更でUndoしたときに戻る先がなくなる）
        PushHistory();
    }

    // JSONを介したシリアライズ/デシリアライズにより、リストの完全なディープコピーを作るヘルパー。
    // 参照を共有しない独立したコピーが必要な場面（元データの保護、Undo用スナップショットの保存）で使う。
    private static List<T> CloneList<T>(List<T> src) =>
        JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(src)) ?? new List<T>();

    // ── UI 構築 ────────────────────────────────
    private void InitUI(string[] seChoices, string[] bgmChoices, string currentStageBgmId)
    {
        Text = "🔊 サウンド割り当て";
        Size = new System.Drawing.Size(920, 700);
        MinimumSize = new System.Drawing.Size(700, 480);
        Font = new System.Drawing.Font("Meiryo UI", 9f);
        StartPosition = FormStartPosition.CenterParent;

        // 画面上部：この画面の使い方を説明する案内文
        var lblHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Padding = new Padding(8, 6, 8, 0),
            Text = "サウンド管理で登録したBGM/SEのIDを、実際に鳴らしたい場面へ割り当てます。空欄は「未設定（鳴らさない）」です。",
            Font = new System.Drawing.Font(Font.FontFamily, 8f),
            ForeColor = System.Drawing.Color.DarkSlateGray,
        };

        // ── タグ絞り込み ──
        // カテゴリ（敵/ギミック/アイテム/ステージ）ごとのトグルボタン。
        // チェックを外すとそのカテゴリのセクションが非表示になり、編集したい項目だけに画面を絞り込める
        var pnlTags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var chkEnemy = new CheckBox { Text = "👾 敵SE", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkGimmick = new CheckBox { Text = "🔧 ギミックSE", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkItem = new CheckBox { Text = "💎 アイテムSE", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        var chkStage = new CheckBox { Text = "🎮 ステージBGM", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        chkEnemy.CheckedChanged += (s, e) => _sectionEnemy.Visible = chkEnemy.Checked;
        chkGimmick.CheckedChanged += (s, e) => _sectionGimmick.Visible = chkGimmick.Checked;
        chkItem.CheckedChanged += (s, e) => _sectionItem.Visible = chkItem.Checked;
        chkStage.CheckedChanged += (s, e) => _sectionStage.Visible = chkStage.Checked;
        pnlTags.Controls.AddRange(new Control[] { chkEnemy, chkGimmick, chkItem, chkStage });

        // ── 下部ボタン ──
        // 画面下部にキャンセル/保存ボタンとUndo/Redoボタンをまとめて配置するエリア
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        // 右寄せ：キャンセル・保存ボタン（保存が最も右＝目立つ位置に来るよう右から順に追加）
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 2), AutoSize = true };
        var btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        // キャンセル時は結果を確定せずCancelを返して閉じる（呼び出し元は変更を反映しない）
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = System.Drawing.Color.FromArgb(40, 167, 69), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;
        flowRight.Controls.Add(btnCancel);
        flowRight.Controls.Add(btnSave);

        // 左寄せ：Undo/Redoボタン
        var flowLeft = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 6), AutoSize = true };
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnUndo.Click += (s, e) => AssignUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnRedo.Click += (s, e) => AssignRedo();
        flowLeft.Controls.AddRange(new Control[] { _btnUndo, _btnRedo });

        pnlBottom.Controls.Add(flowRight);
        pnlBottom.Controls.Add(flowLeft);

        // ── 中央: スクロール可能な複数セクション ──
        // 敵/ギミック/アイテム/ステージの各セクションを縦に並べ、画面に収まらない分はスクロールで見られるようにする
        var pnlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        // 敵のSE割り当てグリッド：ID/名前は読み取り専用、出現音・攻撃音・被弾音・死亡音は編集可能な列
        _gridEnemy = BuildGrid(new[]
        {
            ("id", "ID", 90, true), ("name", "名前", 110, true),
            ("seSpawn", "出現音", 110, false), ("seAttack", "攻撃音", 110, false),
            ("seDamage", "被弾音", 110, false), ("seDeath", "死亡音", 110, false),
        }, seChoices);
        foreach (var en in _enemies)
            _gridEnemy.Rows.Add(en.id, en.name, en.seSpawn, en.seAttack, en.seDamage, en.seDeath);
        // セルの値が変わるたびに履歴へスナップショットを積み、Undo/Redoの対象にする
        _gridEnemy.CellValueChanged += (s, e) => PushHistory();
        _sectionEnemy = BuildSection("👾 敵のSE割り当て", _gridEnemy, 220);

        // ギミックのSE割り当てグリッド：起動音のみ編集可能
        _gridGimmick = BuildGrid(new[]
        {
            ("id", "ID", 110, true), ("name", "名前", 140, true), ("seActivate", "起動音", 140, false),
        }, seChoices);
        foreach (var gi in _gimmicks)
            _gridGimmick.Rows.Add(gi.id, gi.name, gi.seActivate);
        _gridGimmick.CellValueChanged += (s, e) => PushHistory();
        _sectionGimmick = BuildSection("🔧 ギミックのSE割り当て", _gridGimmick, 180);

        // アイテムのSE割り当てグリッド：取得音のみ編集可能
        _gridItem = BuildGrid(new[]
        {
            ("id", "ID", 110, true), ("name", "名前", 140, true), ("seCollect", "取得音", 140, false),
        }, seChoices);
        foreach (var it in _items)
            _gridItem.Rows.Add(it.id, it.name, it.seCollect);
        _gridItem.CellValueChanged += (s, e) => PushHistory();
        _sectionItem = BuildSection("💎 アイテムのSE割り当て", _gridItem, 180);

        _sectionStage = BuildStageSection(bgmChoices, currentStageBgmId);

        // 表示順（上から）：アイテム→ギミック→敵→ステージ の順にスクロールパネルへ積む
        pnlScroll.Controls.Add(_sectionItem);
        pnlScroll.Controls.Add(_sectionGimmick);
        pnlScroll.Controls.Add(_sectionEnemy);
        pnlScroll.Controls.Add(_sectionStage);

        Controls.Add(pnlScroll);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTags);
        Controls.Add(lblHint);
    }

    // フォーム全体でCtrl+Z（元に戻す）とCtrl+Y（やり直す）のショートカットキーを受け付ける。
    // グリッドなど個別コントロールにフォーカスがあっても、フォームレベルでこのキー入力を捕まえて処理する。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { AssignUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { AssignRedo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── ヘルパー: セクション（タイトル+中身、固定高さでDock=Topに積む） ──
    // タイトルラベルと中身のコントロールをまとめた1つのセクションパネルを作る。
    // 複数のセクションをDockStyle.Topで縦に積み重ねられるよう、高さを固定値として計算する。
    private Panel BuildSection(string title, Control content, int contentHeight)
    {
        const int titleHeight = 24;
        const int topMargin = 4, bottomMargin = 14;
        var section = new Panel { Dock = DockStyle.Top, Height = titleHeight + contentHeight + topMargin + bottomMargin, Padding = new Padding(4, topMargin, 4, bottomMargin) };
        var lbl = new Label { Dock = DockStyle.Top, Height = titleHeight, Text = title, Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
        content.Dock = DockStyle.Fill;
        content.Margin = new Padding(0);
        section.Controls.Add(content);
        section.Controls.Add(lbl);
        return section;
    }

    // 敵/ギミック/アイテムの割り当てグリッドを共通ロジックで組み立てるヘルパー。
    // cols: (列名, 見出し, 幅の割合, 読み取り専用か) のタプル配列。
    // readOnly=falseの列はSE選択用のコンボボックス列として作成される。
    private DataGridView BuildGrid((string name, string header, int width, bool readOnly)[] cols, string[] seChoices)
    {
        var grid = new DataGridView
        {
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersWidth = 30,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            Font = new System.Drawing.Font("Meiryo UI", 9),
        };
        // 敵/ギミック/アイテムのSEフィールドが、カタログに存在しない古いIDを保持している場合でも
        // コンボボックス列がフォーマットエラーで落ちないようにする（値はそのまま保持し、表示のみ許容）
        grid.DataError += (s, e) => e.Cancel = true;
        foreach (var (name, header, width, readOnly) in cols)
        {
            if (readOnly)
            {
                // ID/名前などの参照専用列は、編集不可のテキスト列として作り、背景色を薄灰色にして区別する
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = width, ReadOnly = true, DefaultCellStyle = { BackColor = System.Drawing.Color.WhiteSmoke } });
            }
            else
            {
                // SE割り当て用の列はプルダウンから選択できるコンボボックス列として作る
                var combo = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, FillWeight = width, DropDownWidth = 160 };
                combo.Items.AddRange(seChoices);
                grid.Columns.Add(combo);
            }
        }
        return grid;
    }

    // ステージBGM割り当てセクションを組み立てる。
    // ステージが開かれていない場合は編集不可の案内メッセージを、開かれている場合はBGM選択プルダウンを表示する。
    private Panel BuildStageSection(string[] bgmChoices, string currentStageBgmId)
    {
        var content = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = false, Padding = new Padding(8) };
        if (_currentStageName == null)
        {
            // ステージ未選択の場合：ここでは何も編集できないことを利用者に伝える案内文のみ表示する
            var lbl = new Label { Text = "先にメイン画面でステージを開いてください（ステージ未選択のため、ここでは設定できません）。", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Margin = new Padding(4, 8, 4, 4) };
            content.Controls.Add(lbl);
        }
        else
        {
            // ステージが開かれている場合：現在のBGM割り当てをプルダウンの初期選択として反映する
            var lbl = new Label { Text = $"現在のステージ「{_currentStageName}」のBGM:", AutoSize = true, Margin = new Padding(4, 8, 6, 4) };
            _cmbStageBgm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(0, 4, 4, 4) };
            _cmbStageBgm.Items.AddRange(bgmChoices);
            // 現在のBGM IDが候補の中に見つからない場合は先頭（＝未設定）を選択する
            int idx = Array.IndexOf(bgmChoices, currentStageBgmId ?? "");
            _cmbStageBgm.SelectedIndex = idx >= 0 ? idx : 0;
            _cmbStageBgm.SelectedIndexChanged += (s, e) => PushHistory();
            content.Controls.Add(lbl);
            content.Controls.Add(_cmbStageBgm);
        }
        return BuildSection("🎮 ステージのBGM割り当て", content, 50);
    }

    // ── Undo/Redo ──────────────────────────────
    // 現在の画面状態（各グリッドとステージBGMプルダウンの内容）をスナップショットとして切り出す
    private Snapshot CaptureSnapshot() => new Snapshot
    {
        Enemies = ReadEnemiesFromGrid(),
        Gimmicks = ReadGimmicksFromGrid(),
        Items = ReadItemsFromGrid(),
        StageBgmId = _currentStageName != null ? (_cmbStageBgm.SelectedItem as string ?? "") : "",
    };

    // 履歴から取り出したスナップショットの内容を、内部データとグリッド表示の両方に復元する。
    // グリッドはいったん全行クリアしてから、復元後のデータで作り直す。
    private void RestoreSnapshot(Snapshot snap)
    {
        _enemies = snap.Enemies;
        _gimmicks = snap.Gimmicks;
        _items = snap.Items;

        _gridEnemy.Rows.Clear();
        foreach (var en in _enemies) _gridEnemy.Rows.Add(en.id, en.name, en.seSpawn, en.seAttack, en.seDamage, en.seDeath);
        _gridGimmick.Rows.Clear();
        foreach (var gi in _gimmicks) _gridGimmick.Rows.Add(gi.id, gi.name, gi.seActivate);
        _gridItem.Rows.Clear();
        foreach (var it in _items) _gridItem.Rows.Add(it.id, it.name, it.seCollect);

        // ステージが選択されている場合のみ、BGMプルダウンの選択状態も復元する
        if (_currentStageName != null)
        {
            int idx = _cmbStageBgm.Items.IndexOf(snap.StageBgmId);
            _cmbStageBgm.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    // 現在の状態を履歴に1件積み、Undo/Redoボタンの有効/無効状態を最新化する。
    // 何らかの編集操作（セル変更・BGM変更）が行われるたびに呼ばれる。
    private void PushHistory()
    {
        _history.Push(CaptureSnapshot());
        UpdateUndoRedoButtons();
    }

    // 元に戻す（Ctrl+Z）。戻せる履歴がなければ何もしない。
    private void AssignUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        RestoreSnapshot(restored);
        UpdateUndoRedoButtons();
    }

    // やり直す（Ctrl+Y）。やり直せる履歴がなければ何もしない。
    private void AssignRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        RestoreSnapshot(restored);
        UpdateUndoRedoButtons();
    }

    // Undo/Redoボタンの有効/無効を、現在の履歴の状態（これ以上戻せる/進めるか）に合わせて更新する。
    // ボタンがまだ生成されていない（コンストラクタ処理中など）タイミングで呼ばれた場合は何もしない。
    private void UpdateUndoRedoButtons()
    {
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    // ── グリッド → データモデルへの反映 ──
    // 敵グリッドの各行から現在の入力値を読み取り、複製した敵データへ書き戻す。
    // 行数がデータ数より少ない場合に備え、両方の件数の小さい方までしかループしない。
    private List<EnemyDef> ReadEnemiesFromGrid()
    {
        var list = CloneList(_enemies);
        for (int i = 0; i < list.Count && i < _gridEnemy.Rows.Count; i++)
        {
            var row = _gridEnemy.Rows[i];
            list[i].seSpawn = row.Cells["seSpawn"].Value?.ToString() ?? "";
            list[i].seAttack = row.Cells["seAttack"].Value?.ToString() ?? "";
            list[i].seDamage = row.Cells["seDamage"].Value?.ToString() ?? "";
            list[i].seDeath = row.Cells["seDeath"].Value?.ToString() ?? "";
        }
        return list;
    }

    // ギミックグリッドの各行から起動音の入力値を読み取り、複製したギミックデータへ書き戻す。
    private List<GimmickDef> ReadGimmicksFromGrid()
    {
        var list = CloneList(_gimmicks);
        for (int i = 0; i < list.Count && i < _gridGimmick.Rows.Count; i++)
            list[i].seActivate = _gridGimmick.Rows[i].Cells["seActivate"].Value?.ToString() ?? "";
        return list;
    }

    // アイテムグリッドの各行から取得音の入力値を読み取り、複製したアイテムデータへ書き戻す。
    private List<ItemDef> ReadItemsFromGrid()
    {
        var list = CloneList(_items);
        for (int i = 0; i < list.Count && i < _gridItem.Rows.Count; i++)
            list[i].seCollect = _gridItem.Rows[i].Cells["seCollect"].Value?.ToString() ?? "";
        return list;
    }

    // ── 保存 ──────────────────────────────────
    // 保存ボタンが押されたときの処理。各グリッドの最新内容をResult系プロパティへ確定し、
    // DialogResult.OKを設定してフォームを閉じる（呼び出し元はこのOK/Resultプロパティを見て反映する）。
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        ResultEnemies = ReadEnemiesFromGrid();
        ResultGimmicks = ReadGimmicksFromGrid();
        ResultItems = ReadItemsFromGrid();
        // ステージが選択されていない場合はnullのままにし、呼び出し側にBGM未変更であることを伝える
        ResultStageBgmId = _currentStageName != null ? (_cmbStageBgm.SelectedItem as string ?? "") : null;
        DialogResult = DialogResult.OK;
        Close();
    }
}
