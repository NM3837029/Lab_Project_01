using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  SoundManagerForm
//  BGM（背景音楽）・SE（効果音）・UI音（ボタン操作音などUI用の効果音）の定義を
//  一括管理するためのエディタフォーム。
//
//  Feature: サウンド・アセット管理の刷新
//  以前はBGM/SE/UI音をそれぞれ別タブに分けて表示していたが、今回はタブを廃止し、
//  「タグ（チェックボックス）による絞り込み」＋「1つの共通グリッド」という構成に作り直した。
//  これはAssetManagerForm.cs（敵・アイテム等を管理する別のエディタ）で既に確立されている
//  パターンを踏襲したもの。加えて以下の機能を追加している。
//    ・音量列（0.00〜1.00の範囲で指定）
//    ・行の複製機能
//    ・削除時に他データ（敵/ギミック/アイテム/コモンイベント）からの参照チェック
//    ・SEとUI音の間でのID重複検証（内部的に同じ名前空間を共有するため）
//    ・IDや名前による検索絞り込み
//    ・Undo/Redo（元に戻す・やり直す）機能
// ───────────────────────────────────────────────
public class SoundManagerForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    // 保存ボタンが押された後、呼び出し元（このフォームを開いた側）が結果を受け取るためのプロパティ。
    // カテゴリごとに分けたリストとして公開する。
    public List<SoundDef> ResultBgm { get; private set; } = new();
    public List<SoundDef> ResultSe { get; private set; } = new();
    public List<SoundDef> ResultUiSe { get; private set; } = new();

    // ── 内部データモデル ────────────────────────
    // BGM/SE/UI音の3種類を1つのDataGridViewで同時に扱うため、
    // 各行がどのカテゴリに属するかをこのクラスで保持しておく（グリッドの行のTagにも同じ値を入れる）。
    private class SoundRow
    {
        public string Category { get; set; } = "BGM"; // "BGM" | "SE" | "UI" のいずれか
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public bool IsLoop { get; set; } = false;
        public float Volume { get; set; } = 1.0f;
    }

    // ── フィールド ─────────────────────────────
    private readonly string _projectRoot; // プロジェクトのルートフォルダ（音声ファイルの絶対パス組み立てに使う）
    private readonly string _soundsDir;   // 音声ファイルのコピー先フォルダ（<プロジェクトルート>/sound）

    // 削除時の参照チェック用（このフォーム側では変更しない読み取り専用データ）。
    // サウンドIDを削除しようとしたとき、これらのデータのどこかから参照されていないかを調べるために使う。
    private readonly List<EnemyDef> _enemies;
    private readonly List<GimmickDef> _gimmicks;
    private readonly List<ItemDef> _items;
    private readonly List<CommonEventDef> _commonEvents;

    private DataGridView _grid = null!;          // BGM/SE/UI音をまとめて表示・編集するメインのグリッド
    private TextBox _txtSearch = null!;           // ID・名前で絞り込むための検索ボックス
    private CheckBox _chkTagBgm = null!, _chkTagSe = null!, _chkTagUi = null!; // カテゴリ別の表示ON/OFFを切り替えるチェックボックス
    private Button _btnUndo = null!, _btnRedo = null!; // 元に戻す／やり直すボタン（有効・無効を履歴の状態に応じて切り替える）

    private SoundPlayer? _currentPlayer; // 試聴中の音声を再生するプレイヤー（次の試聴開始時やフォーム終了時に停止・破棄する）

    // 編集履歴（Undo/Redo）を管理するオブジェクト。グリッドの内容をスナップショットとして積んでいく。
    private readonly HistoryManager<List<SoundRow>> _history = new();

    // ── コンストラクタ ─────────────────────────
    // projectRoot   : プロジェクトのルートフォルダパス
    // bgm/se/uiSe   : 呼び出し元（親フォーム）から渡される、編集前の各カテゴリのサウンド定義一覧
    // enemies/gimmicks/items/commonEvents : 削除時の参照チェックに使う既存データ（読み取り専用）
    public SoundManagerForm(string projectRoot, List<SoundDef> bgm, List<SoundDef> se, List<SoundDef> uiSe,
        List<EnemyDef> enemies, List<GimmickDef> gimmicks, List<ItemDef> items, List<CommonEventDef> commonEvents)
    {
        _projectRoot = projectRoot;
        _soundsDir = Path.Combine(projectRoot, "sound");
        _enemies = enemies;
        _gimmicks = gimmicks;
        _items = items;
        _commonEvents = commonEvents;

        InitUI();

        // 3つのカテゴリのリストを、カテゴリ情報付きの1つのSoundRowリストにまとめてからグリッドへ流し込む。
        var rows = new List<SoundRow>();
        rows.AddRange(bgm.Select(d => ToRow("BGM", d)));
        rows.AddRange(se.Select(d => ToRow("SE", d)));
        rows.AddRange(uiSe.Select(d => ToRow("UI", d)));
        PopulateGrid(rows);
        // 初期状態を履歴の最初の1件として積んでおく（これによりUndoで「編集前の初期状態」まで戻れるようになる）。
        PushHistory();
    }

    // SoundDef（保存用のデータ構造）をSoundRow（グリッド編集用の内部データ構造）に変換するヘルパー。
    private static SoundRow ToRow(string category, SoundDef d) => new SoundRow
    {
        Category = category, Id = d.id, Name = d.name, File = d.file, IsLoop = d.isLoop, Volume = d.volume
    };

    // ── UI 構築 ────────────────────────────────
    // フォーム全体のレイアウトを組み立てる。上部に検索欄とタグ絞り込み、下部にボタン群、
    // 中央にメイングリッドを配置する。
    private void InitUI()
    {
        Text = "サウンド管理エディタ";
        Size = new System.Drawing.Size(900, 620);
        MinimumSize = new System.Drawing.Size(700, 460);
        Font = new System.Drawing.Font("Meiryo UI", 9f);
        StartPosition = FormStartPosition.CenterParent;

        // ── 上部: 検索欄 + タグ絞り込み ──
        var pnlTop = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        // 検索ボックス行。虫眼鏡アイコンのラベルとテキストボックスを横に並べる。
        var pnlSearch = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        var lblSearch = new Label { Text = "🔍", AutoSize = true, Margin = new Padding(2, 6, 0, 0) };
        _txtSearch = new TextBox { Width = 220, Margin = new Padding(4, 3, 0, 0), PlaceholderText = "ID・名前で検索..." };
        // テキストが変更されるたびに絞り込みを再適用する。
        _txtSearch.TextChanged += (s, e) => ApplyFilter();
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, _txtSearch });

        // カテゴリ絞り込み用のトグルボタン（チェックボックスをボタン風の見た目にしている）。
        // 初期状態は全てチェック済み＝全カテゴリを表示する状態にしておく。
        var pnlTags = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 4) };
        _chkTagBgm = new CheckBox { Text = "🎵 BGM", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        _chkTagSe = new CheckBox { Text = "🔊 SE (効果音)", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        _chkTagUi = new CheckBox { Text = "🔔 UI音", Appearance = Appearance.Button, Checked = true, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 4, 0) };
        // チェック状態が変わるたびに絞り込みを再適用する。
        _chkTagBgm.CheckedChanged += (s, e) => ApplyFilter();
        _chkTagSe.CheckedChanged += (s, e) => ApplyFilter();
        _chkTagUi.CheckedChanged += (s, e) => ApplyFilter();
        pnlTags.Controls.AddRange(new Control[] { _chkTagBgm, _chkTagSe, _chkTagUi });

        pnlTop.Controls.Add(pnlTags);
        pnlTop.Controls.Add(pnlSearch);

        // ── 下部: ボタン群 ──
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        // 右側: キャンセル・保存ボタン（右詰めで並べる）。
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 2), AutoSize = true };
        var btnCancel = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        // キャンセル時は変更を破棄してダイアログを閉じる（DialogResult.Cancelを返す）。
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 6, 10, 6), BackColor = System.Drawing.Color.FromArgb(40, 167, 69), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;
        flowRight.Controls.Add(btnCancel);
        flowRight.Controls.Add(btnSave);

        // 左側: 追加・複製・Undo/Redoボタン（左詰めで並べる。折り返しを許可し、ウィンドウが狭くても崩れないようにする）。
        var flowLeft = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 6, 8, 6), AutoSize = true, WrapContents = true };
        var btnAddBgm = new Button { Text = "＋追加 (BGM)", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddBgm.Click += (s, e) => AddRow("BGM");
        var btnAddSe = new Button { Text = "＋追加 (SE)", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddSe.Click += (s, e) => AddRow("SE");
        var btnAddUi = new Button { Text = "＋追加 (UI音)", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnAddUi.Click += (s, e) => AddRow("UI");
        var btnDuplicate = new Button { Text = "⧉ 複製", AutoSize = true, Padding = new Padding(6, 5, 6, 5) };
        btnDuplicate.Click += (s, e) => DuplicateSelected();
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnUndo.Click += (s, e) => SoundUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(6, 5, 6, 5), Enabled = false };
        _btnRedo.Click += (s, e) => SoundRedo();
        flowLeft.Controls.AddRange(new Control[] { btnAddBgm, btnAddSe, btnAddUi, btnDuplicate, _btnUndo, _btnRedo });

        pnlBottom.Controls.Add(flowRight);
        pnlBottom.Controls.Add(flowLeft);

        // ── 中央: グリッド ──
        _grid = BuildGrid();

        // WinFormsのDockレイアウトは「後から追加したコントロールほど内側に配置される」性質があるため、
        // Fill（中央いっぱいに広がる）指定のグリッドを最初に追加し、Top/Bottomのパネルはその後に追加する。
        Controls.Add(_grid);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTop);
    }

    // フォーム上でのキー入力をアプリ全体のショートカットとして横取りする。
    // Ctrl+Z＝元に戻す、Ctrl+Y＝やり直す。どのコントロールにフォーカスがあっても効くようにするための実装。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { SoundUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { SoundRedo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── DataGridView 生成 ──────────────────────
    // BGM/SE/UI音を共通の列で表示するためのグリッドを組み立てて返す。
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, // 各列の幅をFillWeightの比率で自動調整する
            AllowUserToAddRows = false,    // グリッド最下部の「新規行」入力は使わない（専用の追加ボタンを使う）
            AllowUserToDeleteRows = false, // 行削除もキー操作ではなく専用の削除ボタン経由にする（誤操作防止のため）
            RowHeadersWidth = 30,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            Font = new System.Drawing.Font("Meiryo UI", 9),
        };

        // 各列の定義。FillWeightは列同士の幅の比率を表す値で、グリッド全体の幅に対する割合として自動計算される。
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", HeaderText = "id", FillWeight = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "name", FillWeight = 130 });
        // ファイルパスは直接テキスト編集させず、必ず「📁選択」ボタン経由で設定させるため読み取り専用にする。
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colFile", HeaderText = "file", FillWeight = 170, ReadOnly = true, DefaultCellStyle = { BackColor = System.Drawing.Color.WhiteSmoke } });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colLoop", HeaderText = "ループ(BGM用)", FillWeight = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colVolume", HeaderText = "音量(0.00-1.00)", FillWeight = 70, ValueType = typeof(float) });
        // 各行に埋め込む操作ボタン列（ファイル選択・試聴・削除）。
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "colBtnFile", HeaderText = "", Text = "📁選択", UseColumnTextForButtonValue = true, FillWeight = 55 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "colBtnPlay", HeaderText = "", Text = "▶試聴", UseColumnTextForButtonValue = true, FillWeight = 50 });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "colBtnDel", HeaderText = "", Text = "🗑削除", UseColumnTextForButtonValue = true, FillWeight = 50 });

        // ボタン列がクリックされたときの処理をまとめて1箇所で受け取る。
        grid.CellContentClick += Grid_CellContentClick;
        // セルの値が変わるたびに履歴（Undo/Redo用）へ現在の状態を積む。
        grid.CellValueChanged += (s, e) => PushHistory();
        return grid;
    }

    // ── グリッド ⇔ SoundRow ────────────────────
    // SoundRowのリストからグリッドの行を作り直す（Undo/Redoで状態を復元するときにも使う共通処理）。
    private void PopulateGrid(List<SoundRow> rows)
    {
        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            int idx = _grid.Rows.Add(r.Id, r.Name, r.File, r.IsLoop, r.Volume, "📁選択", "▶試聴", "🗑削除");
            // カテゴリ情報は表示用の列を持たないため、行のTagプロパティに保持しておく。
            _grid.Rows[idx].Tag = r.Category;
        }
        // 行を作り直した後は検索・タグ絞り込みの状態を再適用する。
        ApplyFilter();
    }

    // 現在のグリッドの内容をSoundRowのリストとして読み出す（保存時・履歴保存時の両方で使う共通処理）。
    private List<SoundRow> ReadRowsFromGrid()
    {
        var list = new List<SoundRow>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string id = row.Cells["colId"].Value?.ToString() ?? "";
            string file = row.Cells["colFile"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(file)) continue; // 未使用の空行
            // 音量欄がうまく数値変換できない場合はデフォルトの1.0（最大音量）にフォールバックする。
            if (!float.TryParse(row.Cells["colVolume"].Value?.ToString(), out float vol)) vol = 1.0f;
            list.Add(new SoundRow
            {
                Category = row.Tag as string ?? "SE",
                Id = id,
                Name = row.Cells["colName"].Value?.ToString() ?? "",
                File = file,
                IsLoop = row.Cells["colLoop"].Value is true,
                // 音量は必ず0.0〜1.0の範囲に収める（範囲外の値が入力されてもゲーム側で問題を起こさないようにする）。
                Volume = Math.Clamp(vol, 0f, 1f),
            });
        }
        return list;
    }

    // 検索テキストとカテゴリ（タグ）チェックボックスの状態に応じて、各行の表示/非表示を切り替える。
    private void ApplyFilter()
    {
        string q = _txtSearch.Text.Trim();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            string category = row.Tag as string ?? "SE";
            // カテゴリごとに対応するチェックボックスがONになっているかを判定する。
            bool tagOk = category switch
            {
                "BGM" => _chkTagBgm.Checked,
                "UI" => _chkTagUi.Checked,
                _ => _chkTagSe.Checked,
            };
            // 検索欄が空なら常に一致とみなす。そうでなければID・名前のどちらかに部分一致するか調べる（大文字小文字は区別しない）。
            bool searchOk = string.IsNullOrEmpty(q)
                || (row.Cells["colId"].Value?.ToString() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (row.Cells["colName"].Value?.ToString() ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
            row.Visible = tagOk && searchOk;
        }
    }

    // 指定カテゴリの空行を1件追加し、選択状態にする。
    private void AddRow(string category)
    {
        // BGMカテゴリの場合のみループ再生をデフォルトでONにしておく（BGMは基本的にループ再生される想定のため）。
        int idx = _grid.Rows.Add("", "", "", category == "BGM", 1.0f, "📁選択", "▶試聴", "🗑削除");
        _grid.Rows[idx].Tag = category;
        ApplyFilter();
        _grid.ClearSelection();
        _grid.Rows[idx].Selected = true;
        PushHistory();
    }

    // 選択中の行を複製する。IDが重複しないよう "_copy" を付与し、それでも重なる場合は連番を付けて回避する。
    private void DuplicateSelected()
    {
        if (_grid.SelectedRows.Count == 0) { MessageBox.Show("複製したい行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var src = _grid.SelectedRows[0];
        string category = src.Tag as string ?? "SE";
        string baseId = (src.Cells["colId"].Value?.ToString() ?? "") + "_copy";
        // 現在グリッドに存在する全IDを集めておき、重複しない新しいIDを探す。
        var existingIds = new HashSet<string>(_grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => r.Cells["colId"].Value?.ToString() ?? ""));
        string newId = baseId;
        int n = 2;
        while (existingIds.Contains(newId)) { newId = $"{baseId}{n}"; n++; }

        int idx = _grid.Rows.Add(newId, src.Cells["colName"].Value, src.Cells["colFile"].Value, src.Cells["colLoop"].Value, src.Cells["colVolume"].Value, "📁選択", "▶試聴", "🗑削除");
        _grid.Rows[idx].Tag = category;
        ApplyFilter();
        _grid.ClearSelection();
        _grid.Rows[idx].Selected = true;
        PushHistory();
    }

    // ── Undo/Redo ──────────────────────────────
    // 現在のグリッドの状態を履歴スタックに積む。セル編集・行の追加/削除/複製などの操作の直後に必ず呼び出す。
    private void PushHistory()
    {
        _history.Push(ReadRowsFromGrid());
        UpdateUndoRedoButtons();
    }

    // 1つ前の状態に戻す。
    private void SoundUndo()
    {
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        PopulateGrid(restored);
        UpdateUndoRedoButtons();
    }

    // Undoで戻した操作をやり直す（1つ先の状態に進める）。
    private void SoundRedo()
    {
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        PopulateGrid(restored);
        UpdateUndoRedoButtons();
    }

    // Undo/Redoボタンの有効・無効状態を、現在の履歴の位置に合わせて更新する。
    private void UpdateUndoRedoButtons()
    {
        // フォーム初期化中（ボタンがまだ生成されていない段階）に呼ばれた場合は何もしない。
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    // ── セルボタンクリック ─────────────────────
    // グリッド内の埋め込みボタン（ファイル選択／試聴／削除）が押されたときに呼ばれる共通ハンドラ。
    // クリックされた列名で処理を振り分ける。
    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return; // ヘッダー部分のクリックは無視する
        var row = _grid.Rows[e.RowIndex];
        string colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "colBtnFile")
        {
            SelectSoundFile(row);
        }
        else if (colName == "colBtnPlay")
        {
            PlayPreview(row);
        }
        else if (colName == "colBtnDel")
        {
            DeleteRow(row);
        }
    }

    // 音声ファイルを選択し、プロジェクトのsoundフォルダにコピーしたうえで、そのファイルをこの行に紐づける。
    private void SelectSoundFile(DataGridViewRow row)
    {
        using var dlg = new OpenFileDialog { Title = "音声ファイルを選択", Filter = "音声ファイル|*.wav;*.ogg;*.mp3|すべて|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            // sound フォルダがまだ存在しない場合に備えて作成しておく（既に存在していてもエラーにはならない）。
            Directory.CreateDirectory(_soundsDir);
            string dest = Path.Combine(_soundsDir, Path.GetFileName(dlg.FileName));
            // 選択したファイルが既にコピー先と同じ場所にある場合は、無駄なコピーを避ける。
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);

            // グリッドにはプロジェクトルートからの相対パス（"sound/xxx.wav" 形式）で保存する。
            row.Cells["colFile"].Value = "sound/" + Path.GetFileName(dlg.FileName);
            PushHistory();
        }
        catch (Exception ex)
        {
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 試聴はWAVのみ対応している。これはSystem.Media.SoundPlayerというWindows標準クラスの制約によるもので、
    // mp3/oggを再生するには新たに外部の音声ライブラリへの依存を追加する必要があるため、
    // 今回の改修スコープでは対応を見送っている。
    // なお、音量欄で設定した値は試聴再生には反映されない（SoundPlayerに音量を指定するAPIが存在しないため）。
    // 実際の音量はあくまでゲーム本体（C++側）で再生されたときに適用される。
    private void PlayPreview(DataGridViewRow row)
    {
        string relPath = row.Cells["colFile"].Value?.ToString() ?? "";
        if (string.IsNullOrEmpty(relPath)) return;

        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));
        if (!File.Exists(fullPath))
        {
            MessageBox.Show("ファイルが見つかりません:\n" + fullPath, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // WAV以外の拡張子は試聴不可としてユーザーに知らせる（上記の制約による）。
        if (Path.GetExtension(fullPath).ToLowerInvariant() != ".wav")
        {
            MessageBox.Show("WAVファイルのみプレビュー可能です", "プレビュー不可", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // 前回再生していた音声が残っていれば停止・破棄してから、新しい音声を再生する。
            _currentPlayer?.Stop();
            _currentPlayer?.Dispose();
            _currentPlayer = new SoundPlayer(fullPath);
            _currentPlayer.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show("再生に失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Feature: サウンド・アセット管理の刷新
    // 行を削除しようとしたとき、そのIDが敵・ギミック・アイテム・コモンイベントのどこかで
    // 参照されていないかを事前にチェックし、参照されていれば警告する。
    // これにより、削除した結果として「本来鳴るはずの音が鳴らなくなる」という参照切れバグを未然に防ぐ。
    //
    // 注意（既知の制限）: ステージごとのイベントトリガー（EventTrigger）で使われているサウンドは、
    // このチェックの対象に含まれていない。ステージファイルは個別に読み込む必要があり、
    // このダイアログの通常の呼び出し経路ではステージデータにアクセスできないためである。
    private void DeleteRow(DataGridViewRow row)
    {
        string id = row.Cells["colId"].Value?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(id))
        {
            var refs = FindReferences(id);
            if (refs.Count > 0)
            {
                // 参照元を最大10件まで列挙し、それを超える場合は残り件数をまとめて表示する。
                string msg = $"ID「{id}」は以下から参照されています。削除すると無音になります:\n\n" +
                    string.Join("\n", refs.Take(10)) + (refs.Count > 10 ? $"\n…他{refs.Count - 10}件" : "") +
                    "\n\nこのまま削除しますか？";
                if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
        }
        _grid.Rows.Remove(row);
        PushHistory();
    }

    // 指定したサウンドIDが、敵・ギミック・アイテム・コモンイベントのどこで使われているかを列挙する。
    // 戻り値は「〇〇「xxx」の△△音」のような、ユーザーに見せてそのまま理解できる説明文のリスト。
    private List<string> FindReferences(string id)
    {
        var refs = new List<string>();
        foreach (var en in _enemies)
        {
            if (en.seSpawn == id) refs.Add($"敵「{en.id}」の出現音");
            if (en.seAttack == id) refs.Add($"敵「{en.id}」の攻撃音");
            if (en.seDamage == id) refs.Add($"敵「{en.id}」の被弾音");
            if (en.seDeath == id) refs.Add($"敵「{en.id}」の死亡音");
        }
        foreach (var gi in _gimmicks)
        {
            if (gi.seActivate == id) refs.Add($"ギミック「{gi.id}」の起動音");
        }
        foreach (var it in _items)
        {
            if (it.seCollect == id) refs.Add($"アイテム「{it.id}」の取得音");
        }
        foreach (var ce in _commonEvents)
        {
            foreach (var act in ce.actions)
            {
                // コモンイベントのアクションのうち、BGM切り替え(ChangeBgm)またはSE再生(PlaySe)で
                // このIDをパラメータに指定しているものを探す。
                if ((act.action == "ChangeBgm" || act.action == "PlaySe") && act.param1 == id)
                    refs.Add($"コモンイベント「{ce.id}」の{act.action}アクション");
            }
        }
        return refs;
    }

    // ── 保存 ──────────────────────────────────
    // 「保存して閉じる」ボタンが押されたときの処理。入力内容を検証したうえで結果プロパティに反映し、
    // ダイアログをOKで閉じる。
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var rows = ReadRowsFromGrid();
        var bgm = rows.Where(r => r.Category == "BGM").ToList();
        var se = rows.Where(r => r.Category == "SE").ToList();
        var uiSe = rows.Where(r => r.Category == "UI").ToList();

        // まず各カテゴリ内での基本的な入力チェック（ID未設定・ファイル未設定・ID重複）を行う。
        string? error = ValidateCategory("BGM", bgm) ?? ValidateCategory("SE", se) ?? ValidateCategory("UI音", uiSe);
        if (error == null)
        {
            // Feature: サウンド・アセット管理の刷新
            // SEとUI音は、ゲームエンジン側では同じseMap（1つの辞書）に統合して格納される仕様のため、
            // カテゴリをまたいでID重複があると片方のデータがもう片方に上書きされ、実際に再生できなくなる
            // という実害のあるバグに繋がる。そのため保存前にカテゴリ間の重複もチェックする。
            var seIds = new HashSet<string>(se.Where(d => !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id));
            var crossDup = uiSe.Select(d => d.Id).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && seIds.Contains(id));
            if (crossDup != null)
                error = $"ID「{crossDup}」がSEとUI音の両方に存在します。\nゲーム内ではSEとUI音は同じ名前空間で管理されるため、どちらか一方のIDを変更してください。";
        }
        if (error != null)
        {
            MessageBox.Show(error, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 検証を通過したデータを公開プロパティにセットし、呼び出し元が受け取れるようにする。
        ResultBgm = bgm.Select(ToDef).ToList();
        ResultSe = se.Select(ToDef).ToList();
        ResultUiSe = uiSe.Select(ToDef).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    // SoundRow（グリッド編集用）をSoundDef（保存用データ構造）へ変換するヘルパー。
    private static SoundDef ToDef(SoundRow r) => new SoundDef { id = r.Id, name = r.Name, file = r.File, isLoop = r.IsLoop, volume = r.Volume };

    // 1カテゴリ分のデータについて、保存前の入力チェックを行う。
    // 問題があればエラーメッセージ（画面表示用の文字列）を返し、問題なければnullを返す。
    private static string? ValidateCategory(string categoryLabel, List<SoundRow> defs)
    {
        var seenIds = new HashSet<string>();
        foreach (var d in defs)
        {
            // ファイルは設定されているのにIDが空、というパターンは保存後にゲーム側から参照できなくなるため禁止する。
            if (string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.File))
                return $"[{categoryLabel}] ファイル「{d.File}」にIDが設定されていません。\nIDが空のままだとゲーム側から一切参照できず、無効なデータとして保存されます。";
            // 逆にIDはあるのにファイルが空、というパターンも不完全なデータとして禁止する。
            if (!string.IsNullOrWhiteSpace(d.Id) && string.IsNullOrWhiteSpace(d.File))
                return $"[{categoryLabel}] ID「{d.Id}」にファイルが設定されていません。\n先に「📁選択」でファイルを指定してください。";
            // 同一カテゴリ内でのID重複チェック（HashSet.Addは追加できなかった＝重複していたときfalseを返す性質を利用している）。
            if (!string.IsNullOrWhiteSpace(d.Id) && !seenIds.Add(d.Id))
                return $"[{categoryLabel}] ID「{d.Id}」が重複しています。\nIDは一意である必要があります。";
        }
        return null;
    }

    // フォームが閉じられるときに、再生中の試聴音声を確実に停止・破棄する
    // （音声が鳴り続けたままフォームだけ閉じてしまうことを防ぐ）。
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _currentPlayer?.Stop();
        _currentPlayer?.Dispose();
        base.OnFormClosed(e);
    }
}
