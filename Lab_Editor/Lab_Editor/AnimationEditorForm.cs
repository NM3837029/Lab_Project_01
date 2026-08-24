using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lab_Editor;

// ───────────────────────────────────────────────
//  AnimationEditorForm
//  アニメーションクリップを編集するフォーム
//  スプライトシート画像から複数フレームを切り出し、名前・フレーム範囲・再生速度・ループ有無を
//  DataGridViewの行として一覧管理できるようにする。プレビュー欄でクリップの再生確認も可能。
// ───────────────────────────────────────────────
public class AnimationEditorForm : Form
{
    // ── 公開プロパティ ──────────────────────────
    // 保存ボタン押下時に確定した編集結果（呼び出し元がDialogResult.OKを見て取得する）。
    public AnimationSet ResultSet { get; private set; } = null!;

    // ── フィールド ─────────────────────────────
    // プロジェクトのルートフォルダ（スプライト画像の絶対パス解決に使う）。
    private readonly string _projectRoot;
    // スプライト画像を格納するフォルダ（プロジェクトルート配下の"img"）。
    private readonly string _imgDir;

    private TextBox      _txtAssetId    = null!;  // 編集対象のアセットIDを入力するテキストボックス
    private DataGridView _grid          = null!;  // アニメーションクリップの一覧を編集するグリッド
    private PictureBox   _preview       = null!;  // 選択中クリップのプレビュー表示欄
    private Label        _lblFrameInfo  = null!;  // 現在のフレーム番号などの情報を表示するラベル
    private Button       _btnPreview    = null!;  // プレビュー再生/停止ボタン
    private Button       _btnAdd        = null!;  // 新規クリップ行を追加するボタン
    private Button       _btnSave       = null!;  // 編集内容を確定して閉じるボタン
    private Button       _btnCancel     = null!;  // 編集を破棄して閉じるボタン

    // アニメーション再生用
    private System.Windows.Forms.Timer _animTimer = null!;  // プレビュー再生のフレーム送りに使うタイマー
    private Bitmap?  _spriteSheet;   // 現在選択中クリップのスプライトシート全体画像（未読み込み時はnull）
    private int      _currentFrame;  // プレビュー再生中の現在フレーム番号
    private bool     _isPlaying;     // プレビュー再生中かどうか

    // 現在選択中クリップの情報（グリッドの選択行から読み込んだキャッシュ値）
    private int _frameCountX;  // スプライトシートの横方向フレーム数
    private int _frameCountY;  // スプライトシートの縦方向フレーム数
    private int _startFrame;   // 再生開始フレーム番号
    private int _endFrame;     // 再生終了フレーム番号

    // ── コンストラクタ ─────────────────────────
    // projectRoot : プロジェクトのルートパス（img フォルダの位置解決に使用）
    // animSet     : 編集対象として渡される既存のアニメーションセット（新規時は空のクリップリストを想定）
    public AnimationEditorForm(string projectRoot, AnimationSet animSet)
    {
        _projectRoot = projectRoot;
        _imgDir      = Path.Combine(projectRoot, "img");

        InitializeComponent();

        // コピーを作成して編集（渡されたanimSetを直接書き換えず、グリッドの行データとして展開する）
        _txtAssetId.Text = animSet.assetId ?? "";
        PopulateGrid(animSet.clips ?? new List<AnimationClip>());
    }

    // ── UI 構築 ────────────────────────────────
    // フォーム上の全コントロール（テキストボックス・グリッド・プレビュー・ボタン等）を生成し配置する。
    // WinFormsデザイナを使わず手書きで構築しているため、座標・サイズ指定がすべて明示的に書かれている。
    private void InitializeComponent()
    {
        Text            = "アニメーションエディタ";
        Size            = new Size(900, 540);
        Font            = UiTheme.Base;
        StartPosition   = FormStartPosition.CenterParent;
        // 共通テーマ設定：リサイズ可能な枠やタイトルバーの見た目を統一する。
        UiTheme.ApplyResizableChrome(this);

        // ── 上部：assetId ───────────────────────
        var lblAsset = new Label
        {
            Text     = "assetId:",
            Location = new Point(10, 12),
            AutoSize = true,
        };
        _txtAssetId = new TextBox
        {
            Location = new Point(70, 10),
            Width    = 220,
        };
        Controls.Add(lblAsset);
        Controls.Add(_txtAssetId);

        // ── 右パネル（プレビュー） ───────────────
        var pnlRight = new Panel
        {
            Location  = new Point(638, 40),
            Size      = new Size(240, 420),
        };

        // 選択中クリップのフレーム画像を表示するプレビューボックス（黒背景でズーム表示）。
        _preview = new PictureBox
        {
            Location  = new Point(10, 10),
            Size      = new Size(180, 180),
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
        };

        // 現在のフレーム番号・フレーム数を表示するラベル。
        _lblFrameInfo = new Label
        {
            Location  = new Point(10, 198),
            Size      = new Size(200, 40),
            Text      = "フレーム情報",
            ForeColor = Color.DimGray,
        };

        // プレビュー再生/停止を切り替えるトグルボタン。
        _btnPreview = new Button
        {
            Text     = "▶ プレビュー",
            Location = new Point(10, 246),
            Size     = new Size(120, 30),
        };
        _btnPreview.Click += BtnPreview_Click;

        pnlRight.Controls.AddRange(new Control[] { _preview, _lblFrameInfo, _btnPreview });
        Controls.Add(pnlRight);

        // ── DataGridView ────────────────────────
        // クリップ一覧を編集するメインのグリッド。列構成はBuildGrid()で定義する。
        _grid = BuildGrid();
        _grid.Location = new Point(10, 40);
        _grid.Size     = new Size(618, 390);
        Controls.Add(_grid);

        // ── タイマー ────────────────────────────
        // プレビュー再生時のフレーム送りに使うタイマー。間隔はクリップのfps値に応じて選択時に再設定される。
        _animTimer          = new System.Windows.Forms.Timer();
        _animTimer.Interval = 100;
        _animTimer.Tick    += AnimTimer_Tick;

        // ── 下部ボタン ──────────────────────────
        _btnAdd    = MakeButton("＋クリップ追加",    10,  470, 130);
        _btnSave   = MakeButton("💾 保存して閉じる", 620, 470, 160);
        _btnCancel = MakeButton("キャンセル",         788, 470, 100);

        // 保存ボタンは強調表示、キャンセルボタンは控えめな配色にする（共通UIテーマ適用）。
        UiTheme.StylePrimaryButton(_btnSave);
        UiTheme.StyleSecondaryButton(_btnCancel);

        _btnAdd.Click    += (_, _) => AddEmptyRow();
        _btnSave.Click   += BtnSave_Click;
        _btnCancel.Click += (_, _) =>
        {
            // キャンセル時はプレビュー再生を止めてから、変更を保存せずに閉じる。
            StopPreview();
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(new Control[] { _btnAdd, _btnSave, _btnCancel });
    }

    // ── DataGridView 生成 ──────────────────────
    // クリップ一覧グリッドの列構成・表示設定を組み立てて返す。
    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            AutoSizeColumnsMode   = DataGridViewAutoSizeColumnsMode.None,
            AllowUserToAddRows    = false,
            AllowUserToDeleteRows = false,
            RowHeadersWidth       = 30,
            SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect           = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight   = 28,
        };
        // セルの型変換エラー（不正な数値入力など）が発生してもダイアログを出さずに無視する。
        grid.DataError += (_, e) => e.Cancel = true;

        // name 列（クリップ名。任意の文字列）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colName", HeaderText = "name", Width = 100
        });

        // sprite 列（スプライトシートの相対パス。読み取り専用。📁選択ボタン経由でのみ変更可能）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colSprite", HeaderText = "sprite", Width = 140,
            ReadOnly = true,
            DefaultCellStyle = { BackColor = Color.WhiteSmoke }
        });

        // frameCountX 列（スプライトシートを横方向に何分割するか）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFCX", HeaderText = "FCX", Width = 44,
            ValueType = typeof(int)
        });

        // frameCountY 列（スプライトシートを縦方向に何分割するか）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFCY", HeaderText = "FCY", Width = 44,
            ValueType = typeof(int)
        });

        // startFrame 列（再生開始フレーム番号。0始まりの通し番号）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colStart", HeaderText = "start", Width = 46,
            ValueType = typeof(int)
        });

        // endFrame 列（再生終了フレーム番号）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colEnd", HeaderText = "end", Width = 46,
            ValueType = typeof(int)
        });

        // fps 列（再生速度。1秒あたりのフレーム数）
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colFps", HeaderText = "fps", Width = 46,
            ValueType = typeof(float)
        });

        // loop チェックボックス列（endFrameに達したらstartFrameへ戻ってループ再生するかどうか）
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "colLoop", HeaderText = "loop", Width = 46
        });

        // ボタン列：📁選択（クリックでスプライトシートのファイル選択ダイアログを開く）
        grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "colBtnFile", HeaderText = "", Width = 64,
            Text = "📁選択", UseColumnTextForButtonValue = true
        });

        grid.CellContentClick += Grid_CellContentClick;
        grid.SelectionChanged  += Grid_SelectionChanged;

        return grid;
    }

    // ── データ流し込み ────────────────────────
    // 渡されたクリップ一覧をグリッドに反映する（既存行はすべてクリアしてから追加し直す）。
    private void PopulateGrid(List<AnimationClip> clips)
    {
        _grid.Rows.Clear();
        foreach (var c in clips)
            AddRow(c);
    }

    // グリッドに1行追加する。clipがnullの場合は既定値による空の新規行を追加する。
    private void AddRow(AnimationClip? clip = null)
    {
        int idx = _grid.Rows.Add();
        var row  = _grid.Rows[idx];

        // 各セルへclipの値（またはnullの場合は既定値）を設定する。
        row.Cells["colName"].Value   = clip?.name        ?? "";
        row.Cells["colSprite"].Value = clip?.sprite      ?? "";
        row.Cells["colFCX"].Value    = clip?.frameCountX ?? 1;
        row.Cells["colFCY"].Value    = clip?.frameCountY ?? 1;
        row.Cells["colStart"].Value  = clip?.startFrame  ?? 0;
        row.Cells["colEnd"].Value    = clip?.endFrame    ?? 0;
        row.Cells["colFps"].Value    = clip?.fps         ?? 12f;
        row.Cells["colLoop"].Value   = clip?.loop        ?? true;
    }

    // 「＋クリップ追加」ボタンから呼ばれる、空の新規クリップ行を追加するショートカット。
    private void AddEmptyRow() => AddRow(null);

    // ── セルボタンクリック ─────────────────────
    // グリッド内のボタン列（📁選択）がクリックされた時のハンドラ。
    private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        // ヘッダー行のクリック（RowIndex < 0）は対象外。
        if (e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name == "colBtnFile")
            SelectSpriteFile(e.RowIndex);
    }

    // ── ファイル選択 ──────────────────────────
    // 指定した行のスプライトシート画像をファイル選択ダイアログから選ばせ、プロジェクトのimgフォルダへコピーする。
    // rowIndex : 対象となるグリッドの行インデックス
    private void SelectSpriteFile(int rowIndex)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "スプライトシートを選択",
            Filter = "画像ファイル|*.png;*.jpg;*.bmp|すべて|*.*"
        };

        // ユーザーがキャンセルした場合は何もせず終了。
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            // imgフォルダが存在しない場合は作成する（存在していてもエラーにはならない）。
            Directory.CreateDirectory(_imgDir);
            string dest = Path.Combine(_imgDir, Path.GetFileName(dlg.FileName));

            // 選択したファイルが既にimgフォルダ内にある場合はコピー不要（同一パスへの上書きコピーを避ける）。
            if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, dest, overwrite: true);

            // グリッドにはプロジェクトルートからの相対パス（"img/xxx.png"形式）を保存する。
            string relPath = "img/" + Path.GetFileName(dlg.FileName);
            _grid.Rows[rowIndex].Cells["colSprite"].Value = relPath;
        }
        catch (Exception ex)
        {
            // コピー失敗時（アクセス権限がない等）はエラーメッセージを表示するのみで処理を中断する。
            MessageBox.Show("ファイルコピーに失敗しました:\n" + ex.Message,
                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 選択行変更 ────────────────────────────
    // グリッドの選択行が変わった時、プレビューを選択中クリップの内容に切り替える。
    private void Grid_SelectionChanged(object? sender, EventArgs e)
    {
        // 前のクリップの再生を止めてから、新しい選択行の情報を読み込む。
        StopPreview();
        LoadSelectedClip();
        // Feature: UI改善（提案書 AN-2）— クリップを選んだ瞬間に自動再生し、都度ボタンを押す手間を無くす
        if (_spriteSheet != null) StartPreview();
    }

    // 現在グリッドで選択されている行の情報を読み込み、プレビュー用のフィールドに反映する。
    private void LoadSelectedClip()
    {
        // 選択行がない場合は何もしない。
        if (_grid.SelectedRows.Count == 0) return;
        var row = _grid.SelectedRows[0];

        string sprite = row.Cells["colSprite"].Value?.ToString() ?? "";
        _frameCountX  = TryGetInt(row, "colFCX",   1);
        _frameCountY  = TryGetInt(row, "colFCY",   1);
        _startFrame   = TryGetInt(row, "colStart",  0);
        _endFrame     = TryGetInt(row, "colEnd",    0);
        float fps     = TryGetFloat(row, "colFps", 12f);

        // fpsから1フレームあたりのタイマー間隔(ms)を逆算する。fpsが0以下の不正値なら既定の100msにフォールバック。
        _animTimer.Interval = fps > 0 ? (int)(1000f / fps) : 100;
        // 再生位置を開始フレームにリセットする。
        _currentFrame = _startFrame;

        LoadSpriteSheet(sprite);
        DrawFrame();
        UpdateFrameLabel();
    }

    // 指定された相対パスからスプライトシート画像を読み込み、_spriteSheetフィールドにセットする。
    // relPath : プロジェクトルートからの相対パス（例: "img/enemy1.png"）
    private void LoadSpriteSheet(string relPath)
    {
        // 前の画像リソースを確実に解放してから読み込み直す（メモリリーク防止）。
        _spriteSheet?.Dispose();
        _spriteSheet = null;

        // パスが空であれば画像なしの状態で終了。
        if (string.IsNullOrEmpty(relPath)) return;

        string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', '\\'));
        // ファイルが実際に存在しない場合は読み込まずに終了（存在しないパスが指定されているクリップの保護）。
        if (!File.Exists(fullPath)) return;

        try
        {
            _spriteSheet = new Bitmap(fullPath);
        }
        catch
        {
            // 画像として読み込めない不正なファイルの場合はnullのままにしておく。
            _spriteSheet = null;
        }
    }

    // ── フレーム描画 ──────────────────────────
    // 現在の_currentFrameに対応する矩形をスプライトシートから切り出し、プレビューに表示する。
    private void DrawFrame()
    {
        // スプライトシートが未読み込み、またはフレーム分割数が不正な場合はプレビューを空にする。
        if (_spriteSheet == null || _frameCountX <= 0 || _frameCountY <= 0)
        {
            _preview.Image = null;
            return;
        }

        // スプライトシート全体のサイズを分割数で割って、1フレームあたりのセルサイズを求める。
        int cellW = _spriteSheet.Width  / _frameCountX;
        int cellH = _spriteSheet.Height / _frameCountY;

        // 通し番号のフレームインデックスを、横方向の列番号・縦方向の行番号に変換する。
        int col = _currentFrame % _frameCountX;
        int row = _currentFrame / _frameCountX;

        // 行番号が縦方向の分割数を超える場合（フレーム番号が範囲外）は表示せず終了。
        if (row >= _frameCountY)
        {
            _preview.Image = null;
            return;
        }

        // スプライトシート内の該当セル矩形を、同サイズの新しいビットマップへ切り出してコピーする。
        var srcRect = new Rectangle(col * cellW, row * cellH, cellW, cellH);
        var bmp     = new Bitmap(cellW, cellH);
        using (var g = Graphics.FromImage(bmp))
            g.DrawImage(_spriteSheet, new Rectangle(0, 0, cellW, cellH), srcRect, GraphicsUnit.Pixel);

        // 古い画像を保持してから差し替え、差し替え後に古い方を破棄する（表示の一瞬の欠落を防ぐ順序）。
        var old = _preview.Image;
        _preview.Image = bmp;
        old?.Dispose();
    }

    // 現在のフレーム番号・フレーム分割情報をラベルに反映する。
    private void UpdateFrameLabel()
    {
        _lblFrameInfo.Text =
            $"フレーム: {_currentFrame} / {_endFrame}\n" +
            $"FCX:{_frameCountX} FCY:{_frameCountY}";
    }

    // ── タイマー Tick ─────────────────────────
    // アニメーションタイマーが一定間隔で発火するたびに呼ばれ、フレームを1つ進めて再描画する。
    private void AnimTimer_Tick(object? sender, EventArgs e)
    {
        // 終了フレームが開始フレーム以下（=再生範囲が無効）の場合は再生を止める。
        if (_endFrame <= _startFrame)
        {
            StopPreview();
            return;
        }

        _currentFrame++;
        // 終了フレームを超えたら開始フレームへ戻ってループ再生する。
        if (_currentFrame > _endFrame)
            _currentFrame = _startFrame;

        DrawFrame();
        UpdateFrameLabel();
    }

    // ── プレビュー開始/停止 ───────────────────
    // プレビューボタン押下時のハンドラ。再生中なら停止、停止中なら再生を開始するトグル動作。
    private void BtnPreview_Click(object? sender, EventArgs e)
    {
        if (_isPlaying)
        {
            StopPreview();
        }
        else
        {
            // スプライトシートが未設定の場合は再生できないため、案内メッセージを出して終了する。
            if (_spriteSheet == null)
            {
                MessageBox.Show("スプライトが設定されていません。",
                    "プレビュー", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            StartPreview();
        }
    }

    // プレビュー再生を開始する（タイマーを起動し、ボタン表示を「停止」に切り替える）。
    private void StartPreview()
    {
        if (_spriteSheet == null) return;
        _isPlaying = true;
        _btnPreview.Text = "⏹ 停止";
        // 再生開始時は必ず開始フレームから始める。
        _currentFrame = _startFrame;
        _animTimer.Start();
    }

    // プレビュー再生を停止する（タイマーを止め、ボタン表示を「再生」に戻す）。
    private void StopPreview()
    {
        _animTimer.Stop();
        _isPlaying       = false;
        _btnPreview.Text = "▶ プレビュー";
    }

    // ── 保存 ──────────────────────────────────
    // 保存ボタン押下時のハンドラ。グリッドの全行からAnimationClipのリストを組み立て、ResultSetとして確定する。
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        StopPreview();

        var clips = new List<AnimationClip>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            // DataGridViewが自動生成する「新規行プレースホルダ」はデータではないためスキップする。
            if (row.IsNewRow) continue;
            clips.Add(new AnimationClip
            {
                name        = row.Cells["colName"].Value?.ToString()   ?? "",
                sprite      = row.Cells["colSprite"].Value?.ToString() ?? "",
                frameCountX = TryGetInt(row, "colFCX",   1),
                frameCountY = TryGetInt(row, "colFCY",   1),
                startFrame  = TryGetInt(row, "colStart",  0),
                endFrame    = TryGetInt(row, "colEnd",    0),
                fps         = TryGetFloat(row, "colFps", 12f),
                loop        = row.Cells["colLoop"].Value is bool b && b,
            });
        }

        // 入力されたassetIdと組み立てたクリップ一覧をまとめて結果として確定する。
        ResultSet = new AnimationSet
        {
            assetId = _txtAssetId.Text,
            clips   = clips,
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    // ── ヘルパー ──────────────────────────────
    // 指定したセルの値を整数として解析する。解析に失敗した場合はfallback値を返す。
    private static int TryGetInt(DataGridViewRow row, string col, int fallback)
    {
        return int.TryParse(row.Cells[col].Value?.ToString(), out int v) ? v : fallback;
    }

    // 指定したセルの値を浮動小数点数として解析する。解析に失敗した場合はfallback値を返す。
    private static float TryGetFloat(DataGridViewRow row, string col, float fallback)
    {
        return float.TryParse(row.Cells[col].Value?.ToString(), out float v) ? v : fallback;
    }

    // 共通スタイルのボタンを生成するショートカット（位置・サイズのみ指定し、見た目はUiThemeに委ねる）。
    private static Button MakeButton(string text, int x, int y, int w) =>
        UiTheme.CreateButton(text, new Point(x, y), new Size(w, 30));

    // フォームが閉じられる際、再生中のプレビューを止めて画像リソース・タイマーを確実に解放する。
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopPreview();
        _spriteSheet?.Dispose();
        _animTimer.Dispose();
        _preview.Image?.Dispose();
        base.OnFormClosed(e);
    }
}
