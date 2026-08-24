using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  BackgroundSettingsForm
//  ステージの視差背景レイヤーを設定するフォーム
//  「背景レイヤー」とは、遠景・中景・近景のようにカメラ移動に対して異なる速度で
//  スクロールする画像の層のことで、drawOrder（重ね順）・sprite（画像パス）・
//  scrollRate（カメラに対するスクロール速度の比率）・loop（横方向に繰り返すか）・
//  offsetX/offsetY（初期表示位置のずらし量）の各パラメータをDataGridViewで一覧編集し、
//  右側のパララックスプレビューで実際の見え方を確認しながら調整できるようにしている。
// ───────────────────────────────────────────────
public class BackgroundSettingsForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    // 「保存して閉じる」が押された時点でのレイヤー一覧。呼び出し元はShowDialog()が
    // DialogResult.OKで返ってきた後にこのプロパティを読み取り、ステージデータへ反映する。
    public List<BackgroundLayer> ResultLayers { get; private set; } = new();

    // ── フィールド ─────────────────────────────
    // プロジェクトのルートフォルダ（背景画像の相対パスを解決する際の基準になる）
    private readonly string _projectRoot;
    // 背景画像をコピーして格納する "img" サブフォルダのフルパス
    private readonly string _imgDir;

    // レイヤー一覧を表示・編集するためのグリッド
    private DataGridView _grid       = null!;
    // 選択中の行に対応するスプライト画像を表示するプレビュー用ピクチャーボックス
    private PictureBox   _preview    = null!;
    private Button       _btnAdd     = null!;
    private Button       _btnSave    = null!;
    private Button       _btnCancel  = null!;

    // Feature: UI改善（提案書 SD-2）— scrollRateの数値だけでは奥行きの出方が分からないため、
    // 全レイヤーを合成して自動スクロールさせるミニプレビューを追加する。
    // 以下の4つはそのミニプレビュー機能専用のフィールド。
    // 実際にカメラが動いているように見せるための描画専用パネル（Paintイベントで自前描画する）
    private Panel _parallaxPreview = null!;
    // プレビューを一定間隔で再描画してアニメーションさせるためのタイマー
    private System.Windows.Forms.Timer? _parallaxTimer;
    // プレビュー内で「カメラがどこまで進んだか」を表す仮想的なX座標（実ゲームのカメラとは別物）
    private float _simCameraX = 0f;
    // プレビュー描画のたびにファイルから画像を読み込み直すと重いため、
    // 一度読み込んだ画像をパスをキーにキャッシュしておく辞書
    private readonly Dictionary<string, Image?> _parallaxImgCache = new();

    // ── コンストラクタ ─────────────────────────
    // 引数 projectRoot : プロジェクトのルートフォルダ（画像パスの解決や保存先の基準に使う）
    // 引数 layers      : 編集対象として渡される既存の背景レイヤー一覧（呼び出し元の現在の設定）
    public BackgroundSettingsForm(string projectRoot, List<BackgroundLayer> layers)
    {
        _projectRoot = projectRoot;
        _imgDir      = Path.Combine(projectRoot, "img");

        // UI部品を組み立ててから、渡された既存レイヤーをグリッドへ流し込む
        InitializeComponent();
        PopulateGrid(layers);
    }

    // ── UI 構築 ────────────────────────────────
    // フォーム全体のレイアウト（グリッド・右側プレビュー・下部ボタン）を一括で組み立てる。
    // コンストラクタから一度だけ呼ばれる初期化処理。
    private void InitializeComponent()
    {
        Text            = "背景レイヤー設定";
        Size            = new Size(860, 500);
        Font            = UiTheme.Base;
        StartPosition   = FormStartPosition.CenterParent;
        // ウィンドウのリサイズ挙動や外観をアプリ共通のテーマに合わせる（UiTheme側で定義）
        UiTheme.ApplyResizableChrome(this);

        // ── 右パネル（プレビュー） ───────────────
        // 静止画プレビューとパララックス（自動スクロール）プレビューをまとめて置く領域
        var pnlRight = new Panel
        {
            Dock  = DockStyle.Right,
            Width = 220,
        };

        // 「スプライトプレビュー」の見出しラベル
        var lblPreview = new Label
        {
            Text     = "スプライトプレビュー",
            Dock     = DockStyle.Top,
            Height   = 24,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        // 選択中の行のスプライト画像をそのまま表示する静止画プレビュー
        _preview = new PictureBox
        {
            Size      = new Size(200, 200),
            Location  = new Point(10, 30),
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // 「パララックスプレビュー」の見出しラベル
        var lblParallax = new Label
        {
            Text = "🎬 パララックスプレビュー（自動スクロール）",
            Location = new Point(10, 238),
            Size = new Size(200, 16),
            Font = UiTheme.Small,
        };
        // 全レイヤーを合成して自動スクロールさせる、自前描画のプレビュー領域。
        // BackColorは空を模した水色にしており、レイヤー画像が無い部分の背景として見える。
        _parallaxPreview = new Panel
        {
            Location = new Point(10, 256),
            Size = new Size(200, 130),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(140, 190, 230),
        };
        // 実際の描画処理はParallaxPreview_Paintに委譲する
        _parallaxPreview.Paint += ParallaxPreview_Paint;

        // 30ms間隔（約33fps相当）でカメラ位置を進め、プレビューパネルの再描画を要求するタイマー。
        // フォーム生成と同時に開始し、常にアニメーションし続ける。
        _parallaxTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _parallaxTimer.Tick += (s, e) => { _simCameraX += 1.5f; _parallaxPreview.Invalidate(); };
        _parallaxTimer.Start();

        // 右パネルへ、見出し・静止画プレビュー・パララックス見出し・パララックスプレビューの順に追加する
        pnlRight.Controls.Add(lblPreview);
        pnlRight.Controls.Add(_preview);
        pnlRight.Controls.Add(lblParallax);
        pnlRight.Controls.Add(_parallaxPreview);

        // ── DataGridView ────────────────────────
        // 背景レイヤーの一覧を行として表示・編集するメイングリッド
        _grid = new DataGridView
        {
            Dock                  = DockStyle.Fill,
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
            // 新規行の自動追加・行削除はグリッド標準の機能ではなく、
            // 専用ボタン(＋背景追加／🗑削除)経由でのみ行わせるため両方とも無効化する
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            RowHeadersWidth       = 30,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect           = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight   = 28,
        };

        // drawOrder 列（レイヤーの重ね順。数値が小さいほど奥、大きいほど手前に描画される想定）
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colOrder", HeaderText = "drawOrder", Width = 78,
            ValueType = typeof(int)
        });

        // sprite 列（読み取り専用。値は「📁選択」ボタン経由でのみ設定され、直接入力はさせない）
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colSprite", HeaderText = "sprite", Width = 160,
            ReadOnly = true,
            DefaultCellStyle = { BackColor = Color.WhiteSmoke }
        });

        // scrollRate 列（カメラ移動量に対するこのレイヤーのスクロール速度の比率。
        // 1.0で前景と同じ速度、0に近いほど遠景のようにゆっくり動く）
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colScrollRate", HeaderText = "scrollRate", Width = 80,
            ValueType = typeof(float)
        });

        // loop チェックボックス列（trueなら画像を横方向に敷き詰めて繰り返し表示する）
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "colLoop", HeaderText = "loop", Width = 50
        });

        // offsetX 列（画像の初期表示位置を横方向にずらすオフセット量）
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colOffsetX", HeaderText = "offsetX", Width = 70,
            ValueType = typeof(float)
        });

        // offsetY 列（画像の初期表示位置を縦方向にずらすオフセット量）
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colOffsetY", HeaderText = "offsetY", Width = 70,
            ValueType = typeof(float)
        });

        // ボタン列：📁選択（クリックした行に対して画像ファイル選択ダイアログを開く）
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "colBtnFile", HeaderText = "", Width = 64,
            Text = "📁選択", UseColumnTextForButtonValue = true
        });

        // ボタン列：🗑削除（クリックした行をグリッドから削除する）
        _grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "colBtnDel", HeaderText = "", Width = 60,
            Text = "🗑削除", UseColumnTextForButtonValue = true
        });

        // 各列のボタンクリック・行選択変更・不正な値入力（DataError）に対するイベントハンドラを登録する
        _grid.CellContentClick     += Grid_CellContentClick;
        _grid.SelectionChanged     += Grid_SelectionChanged;
        // 型に合わない値が入力された場合でも例外で落ちないよう、エラーを握りつぶす（e.Cancel=true）
        _grid.DataError            += (_, e) => e.Cancel = true;

        // ── 下部パネル ──────────────────────────
        // 「＋背景追加」「💾 保存して閉じる」「キャンセル」の3ボタンを配置する領域
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };

        _btnAdd    = MakeButton("＋背景追加",       12,  6, 110);
        _btnSave   = MakeButton("💾 保存して閉じる", 530, 6, 160);
        _btnCancel = MakeButton("キャンセル",        700, 6, 100);

        // 保存ボタンは「主要な操作」として緑系の強調スタイルを、
        // キャンセルボタンは「副次的な操作」としてフラットのみのスタイルを適用する
        UiTheme.StylePrimaryButton(_btnSave);
        UiTheme.StyleSecondaryButton(_btnCancel);

        // 各ボタンのクリック時の動作を登録する
        _btnAdd.Click    += (_, _) => AddEmptyRow();
        _btnSave.Click   += BtnSave_Click;
        // キャンセル時はDialogResultをCancelにしてから閉じる（呼び出し元はResultLayersを見ない）
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        pnlBottom.Controls.AddRange(new Control[] { _btnAdd, _btnSave, _btnCancel });

        // グリッドをDock.Fillで先に追加し、右パネル・下部パネルをその後に追加することで、
        // 右パネルと下部パネルが優先的に確保され、残りをグリッドが埋める配置になる
        Controls.Add(_grid);
        Controls.Add(pnlRight);
        Controls.Add(pnlBottom);
    }

    // ── データ流し込み ────────────────────────
    // 引数 layers : 表示したい背景レイヤーの一覧
    // グリッドを一旦空にしてから、渡されたレイヤーを1件ずつ行として追加し直す。
    private void PopulateGrid(List<BackgroundLayer> layers)
    {
        _grid.Rows.Clear();
        foreach (var l in layers)
            AddRow(l);
    }

    // グリッドに1行追加する。
    // 引数 layer : 行の初期値として使うレイヤーデータ。nullの場合は「＋背景追加」ボタンから
    //              呼ばれた新規行の追加であり、各項目にデフォルト値（drawOrder=0, scrollRate=0.5等）を設定する。
    private void AddRow(BackgroundLayer? layer = null)
    {
        int idx = _grid.Rows.Add();
        var row  = _grid.Rows[idx];

        // layerがnullの場合は各プロパティのデフォルト値（?? の右側）を使う
        row.Cells["colOrder"].Value      = layer?.drawOrder  ?? 0;
        row.Cells["colSprite"].Value     = layer?.sprite     ?? "";
        row.Cells["colScrollRate"].Value = layer?.scrollRate ?? 0.5f;
        row.Cells["colLoop"].Value       = layer?.loop       ?? false;
        row.Cells["colOffsetX"].Value    = layer?.offsetX    ?? 0f;
        row.Cells["colOffsetY"].Value    = layer?.offsetY    ?? 0f;
    }

    // 「＋背景追加」ボタン用のショートカット。引数なしでAddRowを呼び、空の新規行を1つ追加する。
    private void AddEmptyRow() => AddRow(null);

    // ── セルボタンクリック ─────────────────────
    // グリッド内のボタン列（📁選択／🗑削除）がクリックされたときの処理を振り分ける。
    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        // 列ヘッダー部分のクリックはRowIndexが-1になるため無視する
        if (e.RowIndex < 0) return;
        var colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "colBtnFile")
        {
            // 画像ファイル選択ダイアログを開き、選ばれた画像をこの行に設定する
            SelectImageFile(e.RowIndex);
        }
        else if (colName == "colBtnDel")
        {
            // 対象行をグリッドから削除し、削除した行が選択中だった場合に備えて
            // 静止画プレビューもクリアしておく
            _grid.Rows.RemoveAt(e.RowIndex);
            _preview.Image = null;
        }
    }

    // ── ファイル選択 ──────────────────────────
    // 画像ファイル選択ダイアログを開き、選択された画像をプロジェクトのimgフォルダへコピーした上で、
    // 指定行のsprite列にプロジェクトルートからの相対パスを設定する。
    // 引数 rowIndex : 画像を設定する対象行のインデックス
    private void SelectImageFile(int rowIndex)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "画像ファイルを選択",
            Filter = "画像ファイル|*.png;*.jpg;*.bmp;*.gif|すべて|*.*"
        };

        // ダイアログがキャンセルされた場合は何もせず終了する
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            // imgフォルダがまだ存在しない場合に備えて、コピー前に必ず作成しておく
            Directory.CreateDirectory(_imgDir);
            string dest = Path.Combine(_imgDir, Path.GetFileName(dlg.FileName));

            // 選択されたファイルが既にimgフォルダ内の同じファイルである場合はコピー不要。
            // それ以外はimgフォルダへ上書きコピーし、プロジェクト内で完結するようにする。
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);

            // 保存されるJSON等で使われる「img/ファイル名」形式の相対パスを組み立てて設定する
            string relPath = "img/" + Path.GetFileName(dlg.FileName);
            _grid.Rows[rowIndex].Cells["colSprite"].Value = relPath;

            // 設定した画像を右側の静止画プレビューにも反映する
            ShowPreview(relPath);
        }
        catch (Exception ex)
        {
            // ファイルコピーに失敗した場合（アクセス権限がない等）はエラーメッセージを表示するのみで、
            // グリッドの内容には影響を与えない
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 選択行変更→プレビュー更新 ─────────────
    // グリッドの選択行が変わるたびに、その行のスプライト画像を静止画プレビューに反映する。
    private void Grid_SelectionChanged(object? sender, EventArgs e)
    {
        // 選択されている行が無い場合（全解除など）は何もしない
        if (_grid.SelectedRows.Count == 0) return;
        string sprite = _grid.SelectedRows[0].Cells["colSprite"].Value?.ToString() ?? "";
        ShowPreview(sprite);
    }

    // 指定された相対パスの画像を読み込み、静止画プレビュー(_preview)に表示する。
    // 引数 relPath : プロジェクトルートからの相対パス（例: "img/sky.png"）
    private void ShowPreview(string relPath)
    {
        // パスが空の場合（未設定の行など）はプレビューをクリアする
        if (string.IsNullOrEmpty(relPath))
        {
            _preview.Image = null;
            return;
        }

        // "/" 区切りの相対パスをWindowsのパス区切り"\"に変換してからフルパスを組み立てる
        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));

        // ファイルが実在しない場合（移動・削除された等）もプレビューをクリアする
        if (!File.Exists(fullPath))
        {
            _preview.Image = null;
            return;
        }

        try
        {
            var bmp = new Bitmap(fullPath);
            // 差し替え前の古い画像を先に変数へ退避してから新しい画像を設定し、
            // その後で古い画像を破棄することで、表示が途切れる隙をなくしつつメモリリークも防ぐ
            var old = _preview.Image;
            _preview.Image = bmp;
            old?.Dispose();
        }
        catch
        {
            // 画像として読み込めない壊れたファイル等の場合は、プレビューを空にするだけで済ませる
            _preview.Image = null;
        }
    }

    // ── パララックスプレビュー ─────────────────
    // _parallaxPreviewパネルのPaintイベントで呼ばれる、全レイヤー合成のカスタム描画処理。
    // 現在のグリッドの内容から各レイヤーを読み取り、_simCameraXに基づいてスクロールさせながら
    // drawOrderの昇順（奥から手前）で重ねて描画することで、実際の視差スクロールの見た目を再現する。
    private void ParallaxPreview_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;

        // 新規行のプレースホルダ行(IsNewRow)を除いた行だけを対象にする
        var rows = new List<DataGridViewRow>();
        foreach (DataGridViewRow row in _grid.Rows) if (!row.IsNewRow) rows.Add(row);
        // drawOrderの小さい順（奥にあるレイヤーから先）に並べ替えることで、
        // 後から描画される手前のレイヤーが正しく上に重なるようにする
        rows.Sort((a, b) => GetInt(a, "colOrder").CompareTo(GetInt(b, "colOrder")));

        foreach (var row in rows)
        {
            // 画像パスが未設定の行は描画対象から除外する
            string sprite = row.Cells["colSprite"].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(sprite)) continue;
            // キャッシュ経由で画像を取得。読み込みに失敗している場合(null)もスキップする
            var img = LoadParallaxImage(sprite);
            if (img == null) continue;

            float scrollRate = GetFloat(row, "colScrollRate", 0.5f);
            bool loop = row.Cells["colLoop"].Value is bool lb && lb;
            float offsetX = GetFloat(row, "colOffsetX", 0f);
            float offsetY = GetFloat(row, "colOffsetY", 0f);

            // 画像の高さをプレビューパネルの高さに合わせて拡大縮小率を決め、幅もその比率で揃える
            float scale = (float)_parallaxPreview.Height / img.Height;
            int drawW = Math.Max(1, (int)(img.Width * scale));
            int drawH = _parallaxPreview.Height;

            // 0.3f はプレビュー画面内で動きが分かりやすくなるよう調整した表示用の速度係数（実機の速度そのものではない）
            // シミュレーション上のカメラ位置(_simCameraX)にscrollRateを掛けて「このレイヤーがどれだけ動くか」を求め、
            // offsetXを加算した上で表示スケール・速度係数を掛けて実際の描画上の移動量(scrollPx)に変換する
            float scrollPx = (_simCameraX * scrollRate + offsetX) * scale * 0.3f;
            // 描画開始位置。drawW（1枚分の幅）で割った余りを使うことで、
            // ループ描画時に画像がタイル状に無限スクロールしているように見せる
            int baseX = -(int)(scrollPx % drawW);
            // 余りが正の値になった場合、画像の先頭がパネル内部から始まってしまい
            // 左端に隙間ができるため、1枚分左にずらして隙間が出ないようにする
            if (baseX > 0) baseX -= drawW;

            if (loop)
            {
                // loop=trueの場合は、パネル幅を覆い尽くすまで画像を横に並べて繰り返し描画する
                for (int x = baseX; x < _parallaxPreview.Width; x += drawW)
                    g.DrawImage(img, x, (int)(offsetY * scale), drawW, drawH);
            }
            else
            {
                // loop=falseの場合は繰り返さず、計算済みの位置に1枚だけ描画する
                g.DrawImage(img, baseX, (int)(offsetY * scale), drawW, drawH);
            }
        }
    }

    // パララックスプレビュー用に画像をキャッシュ付きで読み込む。
    // 引数 relPath : プロジェクトルートからの相対パス
    // 戻り値       : 読み込めた場合はImage、パスが存在しない・読み込みに失敗した場合はnull
    private Image? LoadParallaxImage(string relPath)
    {
        // 既に一度読み込み済み（失敗してnullが入っている場合も含む）ならキャッシュをそのまま返す
        if (_parallaxImgCache.TryGetValue(relPath, out var cached)) return cached;
        string full = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));
        Image? img = null;
        if (File.Exists(full)) { try { img = Image.FromFile(full); } catch { img = null; } }
        // 成功・失敗にかかわらず結果をキャッシュしておくことで、
        // 存在しないファイルを毎フレーム探しに行くような無駄を避ける
        _parallaxImgCache[relPath] = img;
        return img;
    }

    // セルの文字列値をintとして読み取るヘルパー。変換できない場合は0を返す。
    private static int GetInt(DataGridViewRow row, string col) => int.TryParse(row.Cells[col].Value?.ToString(), out int v) ? v : 0;
    // セルの文字列値をfloatとして読み取るヘルパー。変換できない場合はfallbackを返す。
    private static float GetFloat(DataGridViewRow row, string col, float fallback) => float.TryParse(row.Cells[col].Value?.ToString(), out float v) ? v : fallback;

    // ── 保存 ──────────────────────────────────
    // 「💾 保存して閉じる」ボタンのクリック処理。
    // グリッドの現在の内容をBackgroundLayerのリストに変換してResultLayersへ格納し、
    // DialogResult.OKを設定してフォームを閉じる。呼び出し元はこの後ResultLayersを読み取る。
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        ResultLayers.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            // グリッド末尾の「新規行入力用プレースホルダ」は実データではないためスキップする
            if (row.IsNewRow) continue;

            // 指定列の値をfloatとして取り出すローカル関数。変換できない場合はfallbackを返す
            float TryFloat(string colName, float fallback = 0f)
            {
                return float.TryParse(row.Cells[colName].Value?.ToString(), out float v) ? v : fallback;
            }
            // 指定列の値をintとして取り出すローカル関数。変換できない場合はfallbackを返す
            int TryInt(string colName, int fallback = 0)
            {
                return int.TryParse(row.Cells[colName].Value?.ToString(), out int v) ? v : fallback;
            }

            // 各セルの値からBackgroundLayerを1件組み立ててResultLayersに追加する
            ResultLayers.Add(new BackgroundLayer
            {
                drawOrder  = TryInt("colOrder"),
                sprite     = row.Cells["colSprite"].Value?.ToString()       ?? "",
                scrollRate = TryFloat("colScrollRate", 0.5f),
                loop       = row.Cells["colLoop"].Value is bool b && b,
                offsetX    = TryFloat("colOffsetX"),
                offsetY    = TryFloat("colOffsetY"),
            });
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── ヘルパー ──────────────────────────────
    // UiTheme.CreateButtonへ座標・サイズをまとめて渡すだけの簡易ラッパー。
    // 呼び出し側の記述を(text, x, y, width)という短い形にするための糖衣関数。
    private static Button MakeButton(string text, int x, int y, int w) =>
        UiTheme.CreateButton(text, new Point(x, y), new Size(w, 30));

    // フォームが閉じられた後始末。画像やタイマーなどの管理外リソースを確実に解放する。
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 静止画プレビューに残っている画像を破棄する
        _preview.Image?.Dispose();
        // パララックスアニメーション用タイマーを停止・破棄する（止め忘れるとフォーム破棄後も動き続けてしまう）
        _parallaxTimer?.Stop();
        _parallaxTimer?.Dispose();
        // パララックスプレビュー用に読み込んでキャッシュしていた画像も、まとめて破棄する
        foreach (var img in _parallaxImgCache.Values) img?.Dispose();
        base.OnFormClosed(e);
    }
}
