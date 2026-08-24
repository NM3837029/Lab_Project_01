using Newtonsoft.Json;

namespace Lab_Editor;

/// <summary>
/// タイル定義の追加・編集ウィンドウ。
/// ここで言う「タイル定義」とは、マップ上に敷き詰める地形タイル1種類分の設定
/// （ID・名前・色・当たり判定の有無・即死判定の有無・使用する画像とその中の表示範囲）を指す。
/// このフォームではタイル定義の一覧をDataGridViewで編集し、「保存して閉じる」を押すと
/// assets/tiles.json へ書き出す。追加・削除・編集の各操作にはUndo/Redoも効かせている。
/// </summary>
public class TileEditorForm : Form
{
    // tiles.json 等が置かれているアセットフォルダのパス（保存先の組み立てや画像パス解決の基準にする）
    private readonly string assetsPath;
    // 現在編集中のタイル定義一覧。保存時にここから tiles.json を書き出す
    private List<TileDef> tiles;

    private DataGridView dgv = null!;
    private Button btnAdd, btnSave, btnClose;
    // 選択中タイルのスプライトをそのまま描画するプレビュー領域
    private Panel pnlPreview;
    // タイルをID・名前で絞り込むための検索ボックス
    private TextBox txtSearch = null!;

    // Feature: UI改善（提案書 MP-3）— タイル定義の追加/削除/編集にもUndo/Redoを効かせる
    // 履歴はタイル一覧のスナップショット（List<TileDef>）単位で保持する
    private readonly HistoryManager<List<TileDef>> _history = new();
    private Button _btnUndo = null!, _btnRedo = null!;

    // 呼び出し元（メイン画面等）が保存後に受け取るための、編集結果のタイル一覧
    public List<TileDef> ResultTiles => tiles;

    // コンストラクタ。
    // 引数 assetsPath  : アセットフォルダのパス（画像の相対パス解決やtiles.json保存先に使う）
    // 引数 currentTiles: 編集開始時点の既存タイル定義一覧
    public TileEditorForm(string assetsPath, List<TileDef> currentTiles)
    {
        this.assetsPath = assetsPath;
        // 呼び出し元のリストを直接書き換えてしまわないよう、各要素をコピーした
        // 新しいTileDefインスタンスとして複製し、独立したリストを保持する
        tiles = currentTiles.Select(t => new TileDef
        {
            id = t.id, name = t.name, color = t.color,
            collidable = t.collidable, deadly = t.deadly, sprite = t.sprite,
            srcX = t.srcX, srcY = t.srcY, srcW = t.srcW, srcH = t.srcH,
        }).ToList();

        // UI部品を組み立て→グリッドへ反映→Undo履歴の初期状態としてプッシュ、の順で初期化する
        InitUI();
        LoadGrid();
        PushHistory();
    }

    // フォーム全体のUI（検索ボックス・プレビュー・下部ボタン群・DataGridView）を組み立てる初期化処理。
    // コンストラクタから一度だけ呼ばれる。
    private void InitUI()
    {
        Text = "タイル定義エディタ";
        Size = new Size(820, 560);
        MinimumSize = new Size(600, 420);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Meiryo UI", 9);

        // ==== 上部: 検索ボックス ====
        // Feature: レイアウト修正 — 固定座標(Location)ではなくDockベースにすることで、
        // Form.Size(タイトルバー等を含む外形)とClientSize(実際の描画領域)の取り違えによる
        // 下部見切れを構造的に起こらないようにする。
        var pnlSearch = new Panel { Dock = DockStyle.Top, Height = 30 };
        var lblSearch = new Label { Text = "🔍", Location = new Point(5, 6), Size = new Size(20, 20) };
        // テキストが変わるたびに絞り込みを再適用する（ApplySearchFilterはID・名前の部分一致で判定する）
        txtSearch = new TextBox { Location = new Point(25, 3), Size = new Size(220, 23), PlaceholderText = "ID・名前で検索..." };
        txtSearch.TextChanged += (s, e) => ApplySearchFilter();
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch });

        // ==== 右側: プレビューパネル ====
        // Feature: タイル表示範囲調整機能 — 従来は選択中タイルの色を塗りつぶすだけだったが、
        // 実際のスプライト画像を「表示範囲」で切り出した状態でそのまま描画し、ゲーム内で
        // 実際にどう見えるかをそのまま確認できるようにする。
        pnlPreview = new Panel
        {
            Dock = DockStyle.Right,
            Width = 140,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(40, 40, 40),
        };
        // 実際の描画処理はPnlPreview_Paintに委譲する（選択行が変わるたびにInvalidateで再描画を促す）
        pnlPreview.Paint += PnlPreview_Paint;

        // ==== 下部: ボタン（右詰めFlowLayoutPanelで自動配置、はみ出す心配がない） ====
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        // 右側に「キャンセル」「保存して閉じる」を右詰めで配置するFlowLayoutPanel
        var flowRight = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        btnClose = new Button { Text = "キャンセル", AutoSize = true, Padding = new Padding(10, 5, 10, 5) };
        // キャンセル時はDialogResultを設定せず単純にウィンドウを閉じる（変更は破棄される）
        btnClose.Click += (s, e) => Close();
        btnSave = new Button { Text = "💾 保存して閉じる", AutoSize = true, Padding = new Padding(10, 5, 10, 5), BackColor = Color.FromArgb(40, 167, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.Click += BtnSave_Click;
        flowRight.Controls.Add(btnClose);
        flowRight.Controls.Add(btnSave);
        // 左側に「追加」「削除」「複製」「元に戻す」「やり直す」を左詰めで配置するFlowLayoutPanel
        var flowLeft = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8), AutoSize = true };
        btnAdd = new Button { Text = "＋ タイル追加", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnAdd.Click += BtnAdd_Click;
        var btnDel = new Button { Text = "🗑 削除", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnDel.Click += BtnDel_Click;
        var btnDuplicate = new Button { Text = "⧉ 複製", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
        btnDuplicate.Click += BtnDuplicate_Click;
        // Undo/Redoボタンは履歴が無い状態では押せないよう、初期状態はEnabled=falseにしておく
        // （実際の有効/無効切り替えはUpdateUndoRedoButtonsが担当する）
        _btnUndo = new Button { Text = "↩ 元に戻す (Ctrl+Z)", AutoSize = true, Padding = new Padding(8, 5, 8, 5), Enabled = false };
        _btnUndo.Click += (s, e) => TilesUndo();
        _btnRedo = new Button { Text = "↪ やり直す (Ctrl+Y)", AutoSize = true, Padding = new Padding(8, 5, 8, 5), Enabled = false };
        _btnRedo.Click += (s, e) => TilesRedo();
        flowLeft.Controls.AddRange(new Control[] { btnAdd, btnDel, btnDuplicate, _btnUndo, _btnRedo });
        pnlBottom.Controls.Add(flowRight);
        pnlBottom.Controls.Add(flowLeft);

        // ==== 中央: DataGridView ====
        // タイル定義一覧のメイン編集グリッド。列幅は自動でパネル幅一杯に広がる(Fill)ようにしている
        dgv = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Meiryo UI", 9)
        };

        // ID列は自動採番される値であり、ユーザーが直接編集すると重複や不整合の原因になるため読み取り専用にする
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "ID", ReadOnly = true, FillWeight = 30 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "名前", FillWeight = 100 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "color", HeaderText = "色(#RRGGBB)", FillWeight = 80 });
        dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "collidable", HeaderText = "当たり判定", FillWeight = 60 });
        dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "deadly", HeaderText = "即死", FillWeight = 40 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "sprite", HeaderText = "画像パス", FillWeight = 120 });

        // Feature: タイル表示範囲調整機能 — spriteがタイルセット画像の場合に、そのうちどの矩形を使うか
        // （画像内の切り出し開始位置X/Y、切り出し幅W、切り出し高さH）を保持する4列。
        // 値が0（未設定）の場合は画像全体を使う扱いになる（PnlPreview_Paint等を参照）。
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcX", HeaderText = "範囲X", FillWeight = 30, ValueType = typeof(int) });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcY", HeaderText = "範囲Y", FillWeight = 30, ValueType = typeof(int) });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcW", HeaderText = "範囲W", FillWeight = 30, ValueType = typeof(int) });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "srcH", HeaderText = "範囲H", FillWeight = 30, ValueType = typeof(int) });

        // ボタン列: 色選択（クリックした行に対してColorDialogを開き、colorセルへ反映する）
        var colColor = new DataGridViewButtonColumn { Name = "btnColor", HeaderText = "色選択", Text = "🎨", UseColumnTextForButtonValue = true, FillWeight = 40 };
        dgv.Columns.Add(colColor);
        // ボタン列: ファイル選択（クリックした行に対して画像選択ダイアログを開き、spriteセルへ反映する）
        var colFile = new DataGridViewButtonColumn { Name = "btnFile", HeaderText = "画像選択", Text = "📁", UseColumnTextForButtonValue = true, FillWeight = 40 };
        dgv.Columns.Add(colFile);
        // ボタン列: 表示範囲をドラッグで選択（TileRegionEditorFormを開き、srcX/Y/W/Hをまとめて設定する）
        var colRegion = new DataGridViewButtonColumn { Name = "btnRegion", HeaderText = "表示範囲", Text = "🖼 範囲", UseColumnTextForButtonValue = true, FillWeight = 45 };
        dgv.Columns.Add(colRegion);

        // 各種イベントハンドラを登録する：ボタン列クリック、選択行変更（→プレビュー更新）、
        // セル値変更のたびにUndo履歴へスナップショットを積む
        dgv.CellContentClick += Dgv_CellContentClick;
        dgv.SelectionChanged += Dgv_SelectionChanged;
        dgv.CellValueChanged += (s, e) => PushHistory();

        // グリッドをDock.Fillで先に追加し、プレビュー・下部ボタン・検索ボックスをその後に追加することで、
        // 各Dock固定領域が優先確保され、残り領域をグリッドが埋める配置になる
        Controls.Add(dgv);
        Controls.Add(pnlPreview);
        Controls.Add(pnlBottom);
        Controls.Add(pnlSearch);
    }

    // フォーム全体でCtrl+Z/Ctrl+Yのショートカットキーを受け取れるようにするオーバーライド。
    // グリッドやテキストボックスにフォーカスがあっても、このフォーム内であれば常にUndo/Redoが効くようにする。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { TilesUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { TilesRedo(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // セルの文字列値をintとして読み取るヘルパー。変換できない場合は0を返す。
    private static int IntCell(DataGridViewRow row, string col) => int.TryParse(row.Cells[col].Value?.ToString(), out int v) ? v : 0;

    // 現在のグリッドの表示内容から、TileDefのリストを組み立てて返す。
    // Undo履歴へのスナップショット保存や、保存(BtnSave_Click)時のデータ取得に使う共通処理。
    private List<TileDef> ReadTilesFromGrid()
    {
        var result = new List<TileDef>();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            // IDが数値として読み取れない行（新規行のプレースホルダ等）はスキップする
            if (!int.TryParse(row.Cells["id"].Value?.ToString(), out int id)) continue;
            result.Add(new TileDef
            {
                id = id,
                name = row.Cells["name"].Value?.ToString() ?? "",
                color = row.Cells["color"].Value?.ToString() ?? "#CCCCCC",
                collidable = row.Cells["collidable"].Value is true,
                deadly = row.Cells["deadly"].Value is true,
                sprite = row.Cells["sprite"].Value?.ToString() ?? "",
                srcX = IntCell(row, "srcX"),
                srcY = IntCell(row, "srcY"),
                srcW = IntCell(row, "srcW"),
                srcH = IntCell(row, "srcH"),
            });
        }
        return result;
    }

    // 現在のグリッド内容をスナップショットとしてUndo履歴に積み、Undo/Redoボタンの有効状態を更新する。
    // タイルの追加・削除・複製・セル値変更など、内容が変わる操作のたびに呼び出す。
    private void PushHistory()
    {
        _history.Push(ReadTilesFromGrid());
        UpdateUndoRedoButtons();
    }

    // 直前の状態に戻す（Ctrl+Zまたは「↩ 元に戻す」ボタン）。
    private void TilesUndo()
    {
        // 戻せる履歴が無ければ何もしない
        if (!_history.CanUndo) return;
        var restored = _history.Undo();
        if (restored == null) return;
        tiles = restored;
        LoadGrid();
        UpdateUndoRedoButtons();
    }

    // Undoで戻した操作をやり直す（Ctrl+Yまたは「↪ やり直す」ボタン）。
    private void TilesRedo()
    {
        // やり直せる履歴が無ければ何もしない
        if (!_history.CanRedo) return;
        var restored = _history.Redo();
        if (restored == null) return;
        tiles = restored;
        LoadGrid();
        UpdateUndoRedoButtons();
    }

    // Undo/Redoボタンの有効・無効を、現在の履歴の状態(_history.CanUndo/CanRedo)に合わせて更新する。
    private void UpdateUndoRedoButtons()
    {
        // ボタン生成前（InitUIの途中）に呼ばれる可能性があるための安全確認
        if (_btnUndo == null! || _btnRedo == null!) return;
        _btnUndo.Enabled = _history.CanUndo;
        _btnRedo.Enabled = _history.CanRedo;
    }

    // 現在の tiles フィールドの内容を使ってグリッドを全行再構築する。
    // Undo/Redoやタイル追加・削除・複製など、tilesの中身が変わった後は必ずこれを呼んで画面に反映する。
    private void LoadGrid()
    {
        dgv.Rows.Clear();
        foreach (var t in tiles)
        {
            int rowIdx = dgv.Rows.Add(t.id, t.name, t.color, t.collidable, t.deadly, t.sprite, t.srcX, t.srcY, t.srcW, t.srcH, "🎨", "📁", "🖼 範囲");
            // セルの背景色を、そのタイルの色(#RRGGBB)そのままに塗ることで、一覧を見ただけで色がわかるようにする。
            // 不正な色文字列が入っていた場合に例外で落ちないよう、失敗時は何もしない
            try { dgv.Rows[rowIdx].Cells["color"].Style.BackColor = ColorTranslator.FromHtml(t.color); } catch { }
        }
    }

    // 選択行が変わったら、右側プレビューパネルの再描画を要求する（実際の描画はPnlPreview_Paintで行う）。
    private void Dgv_SelectionChanged(object? sender, EventArgs e)
    {
        pnlPreview.Invalidate();
    }

    // Feature: タイル表示範囲調整機能 — 選択中タイルのスプライトを、表示範囲(srcX/Y/W/H)で
    // 切り出した状態でそのまま描画する。範囲が未設定(0)なら画像全体を使う（従来互換）。
    private void PnlPreview_Paint(object? sender, PaintEventArgs e)
    {
        // 選択中の行が無ければ描画するものが無いので何もしない
        if (dgv.SelectedRows.Count == 0) return;
        var row = dgv.SelectedRows[0];
        string colorStr = row.Cells["color"].Value?.ToString() ?? "#CCCCCC";
        Color fallback;
        // 色文字列が不正な場合はグレーを代替色として使う
        try { fallback = ColorTranslator.FromHtml(colorStr); } catch { fallback = Color.Gray; }

        string spritePath = row.Cells["sprite"].Value?.ToString() ?? "";
        // アセットフォルダ（assetsPathの親ディレクトリ）を基準に、画像の相対パスをフルパスへ変換する
        string full = string.IsNullOrEmpty(spritePath) ? "" : Path.Combine(Path.GetDirectoryName(assetsPath)!, spritePath.Replace('/', '\\'));

        // 画像パスが無い、またはファイルが存在しない場合は、代わりにタイル色の四角形を塗りつぶして表示する
        if (string.IsNullOrEmpty(full) || !File.Exists(full))
        {
            using var b = new SolidBrush(fallback);
            e.Graphics.FillRectangle(b, 4, 4, pnlPreview.Width - 8, 100);
            return;
        }

        try
        {
            // ファイルを読み取り専用で開いて画像として読み込む（他プロセスがロックしていても読めるようFileShare.Readを指定）
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var img = Image.FromStream(fs);
            int sx = IntCell(row, "srcX"), sy = IntCell(row, "srcY"), sw = IntCell(row, "srcW"), sh = IntCell(row, "srcH");
            // 表示範囲の幅または高さが0以下（＝未設定）の場合は、画像全体を使う従来互換の挙動にする
            if (sw <= 0 || sh <= 0) { sx = 0; sy = 0; sw = img.Width; sh = img.Height; }
            // 表示範囲が画像の実際のサイズをはみ出さないよう、各値を画像サイズの範囲内にクランプ（丸め込み）する
            sx = Math.Clamp(sx, 0, Math.Max(0, img.Width - 1));
            sy = Math.Clamp(sy, 0, Math.Max(0, img.Height - 1));
            sw = Math.Clamp(sw, 1, img.Width - sx);
            sh = Math.Clamp(sh, 1, img.Height - sy);

            // プレビューパネルの幅・高さに収まるよう、幅基準・高さ基準それぞれの縮小率のうち小さい方を採用する
            float scale = Math.Min((float)(pnlPreview.Width - 16) / sw, 140f / sh);
            if (scale <= 0) scale = 1;
            int drawW = Math.Max(1, (int)(sw * scale));
            int drawH = Math.Max(1, (int)(sh * scale));
            // 横方向は中央寄せ、縦方向は上から8pxの位置に描画する
            int drawX = (pnlPreview.Width - drawW) / 2;
            int drawY = 8;

            // ドット絵の輪郭がぼやけないよう、拡大縮小の補間方式を最近傍（ニアレストネイバー）にする
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            // 元画像のうち(sx, sy, sw, sh)の矩形部分だけを、プレビュー上の(drawX, drawY, drawW, drawH)へ描画する
            e.Graphics.DrawImage(img, new Rectangle(drawX, drawY, drawW, drawH), new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
            // 表示範囲の境界が分かるよう、描画した画像の周囲に枠線を引く
            e.Graphics.DrawRectangle(Pens.DimGray, drawX, drawY, drawW, drawH);

            // 画像の下に、実際に切り出しているピクセルサイズを小さな文字で表示する（デバッグ・確認用の補助情報）
            using var hint = new Font(Font.FontFamily, 7.5f);
            e.Graphics.DrawString($"表示範囲:\n{sw}x{sh}px", hint, Brushes.LightGray, 6, drawY + drawH + 8);
        }
        catch
        {
            // 画像の読み込みや描画に失敗した場合も、タイル色の四角形をフォールバック表示する
            using var b = new SolidBrush(fallback);
            e.Graphics.FillRectangle(b, 4, 4, pnlPreview.Width - 8, 100);
        }
    }

    // グリッド内のボタン列（色選択／ファイル選択／表示範囲）がクリックされたときの処理を振り分ける。
    private void Dgv_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        // 列ヘッダー部分のクリックはRowIndexが-1になるため無視する
        if (e.RowIndex < 0) return;
        var row = dgv.Rows[e.RowIndex];

        if (dgv.Columns[e.ColumnIndex].Name == "btnColor")
        {
            // 現在の色をダイアログの初期選択色として表示する（不正な文字列の場合は既定色のまま）
            using var cd = new ColorDialog();
            try { cd.Color = ColorTranslator.FromHtml(row.Cells["color"].Value?.ToString() ?? "#CCCCCC"); } catch { }
            if (cd.ShowDialog() == DialogResult.OK)
            {
                // 選ばれた色を#RRGGBB形式の文字列に変換してセルへ設定し、セルの背景色にも反映する
                string hex = $"#{cd.Color.R:X2}{cd.Color.G:X2}{cd.Color.B:X2}";
                row.Cells["color"].Value = hex;
                row.Cells["color"].Style.BackColor = cd.Color;
                pnlPreview.Invalidate();
            }
        }
        else if (dgv.Columns[e.ColumnIndex].Name == "btnFile")
        {
            using var ofd = new OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*", Title = "スプライト画像を選択" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                // プロジェクトルートからの相対パスに変換
                // （assetsPathの親ディレクトリを基準に、選ばれたファイルの相対パスを"/"区切りで組み立てる）
                string rel = Path.GetRelativePath(
                    Path.GetDirectoryName(assetsPath)!,
                    ofd.FileName).Replace('\\', '/');
                row.Cells["sprite"].Value = rel;
                // 新しい画像を選んだ場合、表示範囲は一旦リセットして画像全体を使う状態に戻す
                row.Cells["srcX"].Value = 0;
                row.Cells["srcY"].Value = 0;
                row.Cells["srcW"].Value = 0;
                row.Cells["srcH"].Value = 0;
                pnlPreview.Invalidate();
            }
        }
        else if (dgv.Columns[e.ColumnIndex].Name == "btnRegion")
        {
            string spritePath = row.Cells["sprite"].Value?.ToString() ?? "";
            // 画像が未選択の状態で「表示範囲」を押された場合は、切り出す対象が無い旨を案内して抜ける
            if (string.IsNullOrEmpty(spritePath))
            {
                MessageBox.Show("先に画像を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string full = Path.Combine(Path.GetDirectoryName(assetsPath)!, spritePath.Replace('/', '\\'));
            // 参照先の画像ファイルが見つからない場合はエラーを表示して抜ける
            if (!File.Exists(full))
            {
                MessageBox.Show("画像ファイルが見つかりません:\n" + full, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // 現在の表示範囲を初期値として渡し、ドラッグ操作で範囲を選び直せる専用エディタを開く
            using var form = new TileRegionEditorForm(full, IntCell(row, "srcX"), IntCell(row, "srcY"), IntCell(row, "srcW"), IntCell(row, "srcH"));
            if (form.ShowDialog() == DialogResult.OK)
            {
                // エディタで確定された範囲をセルへ反映し、プレビューを更新した上で履歴にも積む
                row.Cells["srcX"].Value = form.SrcX;
                row.Cells["srcY"].Value = form.SrcY;
                row.Cells["srcW"].Value = form.SrcWidth;
                row.Cells["srcH"].Value = form.SrcHeight;
                pnlPreview.Invalidate();
                PushHistory();
            }
        }
    }

    // 「＋ タイル追加」ボタンの処理。既存の最大ID+1を新規IDとして、仮の名前・色を持つタイルを1件追加する。
    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        // タイルが1件も無い場合はID=0から開始し、それ以外は既存の最大ID+1を使う（IDの重複を避けるため）
        int newId = tiles.Count > 0 ? tiles.Max(t => t.id) + 1 : 0;
        tiles.Add(new TileDef { id = newId, name = $"新タイル{newId}", color = "#888888" });
        LoadGrid();
        // 追加した行（末尾）をそのまま選択状態にし、続けて編集しやすくする
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        PushHistory();
    }

    // 「🗑 削除」ボタンの処理。選択中のタイルをtilesから取り除く。
    private void BtnDel_Click(object? sender, EventArgs e)
    {
        // 選択行が無ければ削除対象が無いので何もしない
        if (dgv.SelectedRows.Count == 0) return;
        int rowIdx = dgv.SelectedRows[0].Index;
        int id = (int)(dgv.Rows[rowIdx].Cells["id"].Value ?? 0);
        // ID=0のタイルはゲーム側で「未配置（何もない）」等の特別な意味を持つ既定タイルであるため、削除を禁止する
        if (id == 0) { MessageBox.Show("ID=0 のタイルは削除できません", "エラー"); return; }
        tiles.RemoveAll(t => t.id == id);
        LoadGrid();
        PushHistory();
    }

    // 検索ボックスの入力内容(txtSearch.Text)を使って、ID・名前が部分一致しない行を非表示にする絞り込み処理。
    private void ApplySearchFilter()
    {
        string q = txtSearch.Text.Trim();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            // 検索語が空の場合は絞り込みをせず、すべての行を表示する
            if (string.IsNullOrEmpty(q)) { row.Visible = true; continue; }
            string id = row.Cells["id"].Value?.ToString() ?? "";
            string name = row.Cells["name"].Value?.ToString() ?? "";
            // 大文字小文字を区別せず、IDまたは名前のどちらかに検索語が含まれていれば表示する
            row.Visible = id.Contains(q, StringComparison.OrdinalIgnoreCase) || name.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 「⧉ 複製」ボタンの処理。選択中のタイルと同じ内容（IDのみ新規採番）のタイルをもう1件追加する。
    private void BtnDuplicate_Click(object? sender, EventArgs e)
    {
        // 選択行が無ければ複製元が無いので、その旨を案内して抜ける
        if (dgv.SelectedRows.Count == 0) { MessageBox.Show("複製するタイルを選択してください。"); return; }
        var row = dgv.SelectedRows[0];
        // 既存の最大ID+1を新規IDとする（タイルが1件も無い状況はここでは起こり得ないが、念のため1から開始）
        int newId = tiles.Count > 0 ? tiles.Max(t => t.id) + 1 : 1;
        var src = new TileDef
        {
            id = newId,
            // 元のタイルと区別しやすいよう、名前の末尾に「のコピー」を付与する
            name = (row.Cells["name"].Value?.ToString() ?? "") + "のコピー",
            color = row.Cells["color"].Value?.ToString() ?? "#CCCCCC",
            collidable = row.Cells["collidable"].Value is true,
            deadly = row.Cells["deadly"].Value is true,
            sprite = row.Cells["sprite"].Value?.ToString() ?? "",
            srcX = IntCell(row, "srcX"),
            srcY = IntCell(row, "srcY"),
            srcW = IntCell(row, "srcW"),
            srcH = IntCell(row, "srcH"),
        };
        tiles.Add(src);
        LoadGrid();
        // 複製後の新しい行（末尾）を選択状態にし、続けて名前や色を調整しやすくする
        dgv.Rows[dgv.Rows.Count - 1].Selected = true;
        PushHistory();
    }

    // Feature: UI改善（提案書 CUT-3）— IDの重複や名前未入力のまま保存すると、後から配置したステージ側で
    // 見た目や意図しないタイル参照になり気づきにくいため、保存前に警告する。
    // 引数 list  : 検証対象のタイル定義一覧
    // 戻り値     : 見つかった問題点を説明する警告メッセージの一覧（問題が無ければ空リスト）
    private static List<string> ValidateTiles(List<TileDef> list)
    {
        var warnings = new List<string>();
        // 同じIDを持つタイルが複数存在していないかをチェックする
        var dupIds = list.GroupBy(t => t.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupIds.Count > 0)
            warnings.Add($"IDが重複しています: {string.Join(", ", dupIds)}（後に定義した方だけが有効になります）。");
        // 名前が空白または未入力のタイルが無いかをチェックする
        var emptyNames = list.Where(t => string.IsNullOrWhiteSpace(t.name)).Select(t => t.id).ToList();
        if (emptyNames.Count > 0)
            warnings.Add($"名前が未入力のタイルがあります (ID: {string.Join(", ", emptyNames)})。");
        return warnings;
    }

    // 「💾 保存して閉じる」ボタンの処理。
    // グリッドの内容をtilesへ反映→バリデーション→問題があれば確認ダイアログ→
    // assets/tiles.json へJSONとして書き出す、という一連の保存処理を行う。
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        tiles = ReadTilesFromGrid();

        var warnings = ValidateTiles(tiles);
        if (warnings.Count > 0)
        {
            // ID重複や名前未入力があった場合は、内容を保存前に確認してもらい、
            // 「いいえ」が選ばれた場合は保存処理を中断してフォームを閉じない
            string msg = "保存前に確認してください:\n\n" + string.Join("\n", warnings) + "\n\nこのまま保存しますか？";
            if (MessageBox.Show(msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        // アセットフォルダ直下のtiles.jsonへ、整形済み(Formatting.Indented)のJSONとして上書き保存する
        string path = Path.Combine(assetsPath, "tiles.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(tiles, Formatting.Indented));
        MessageBox.Show("タイル定義を保存しました！", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DialogResult = DialogResult.OK;
        Close();
    }
}
