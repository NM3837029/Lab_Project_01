using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Lab_Editor;

// ======================================================
// DeployForm - 配布用パッケージ（zip）を作る画面
//
// 配布の仕組み自体は前から tools\build_dist.ps1 にあったが、入口が
// リポジトリ直下の build_dist.bat をダブルクリックする方法しか無かった。
// エディタで作ったステージを渡したいときに、わざわざエクスプローラを開いて
// バッチを探す必要があり、「エディタの中に配布の入口が無い」状態だった。
// この画面はメニューの「ファイル」から開く、その入口にあたる。
//
// 中身を作り直しているわけではなく、既存のスクリプトをそのまま呼ぶ薄い層にしてある。
// 手順を二重に持つと、片方だけ直したときに配布物の中身が食い違うため。
// ======================================================
public class DeployForm : Form
{
    // 配布スクリプトを走らせるときの作業フォルダ（＝リポジトリルート）
    private readonly string _root;
    // 呼び出す配布スクリプト tools\build_dist.ps1 の絶対パス
    private readonly string _scriptPath;
    // 出来上がったものが置かれるフォルダ dist\ の絶対パス
    private readonly string _distDir;

    private readonly TextBox _txtVersion = new();
    private readonly CheckBox _chkSkipBuild = new();
    private readonly TextBox _txtLog = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnRun = new();
    private readonly Button _btnOpenDist = new();
    private readonly Button _btnClose = new();

    // 実行中のPowerShellプロセス。実行中でなければnull。
    // 画面を閉じるときに動いていたら道連れにしないよう、参照を持っておく。
    private Process? _proc;

    // バージョン文字列はzipのファイル名にそのまま入る。
    // 「\」「/」「..」のようなものを弾いておかないと、意図しない場所へ書き出す指定が通ってしまう。
    // 数字・英字・ドット・ハイフン・アンダースコアだけに限る。
    private static readonly Regex VersionPattern = new(@"^[0-9A-Za-z._-]{1,32}$", RegexOptions.Compiled);

    public DeployForm(string projectRoot)
    {
        _root = projectRoot;
        _scriptPath = Path.Combine(_root, "tools", "build_dist.ps1");
        _distDir = Path.Combine(_root, "dist");

        Text = "📦 配布用パッケージを作る";
        Size = new Size(720, 560);
        StartPosition = FormStartPosition.CenterParent;
        Font = UiTheme.Base;

        // ── 上部：説明と設定 ──────────────────────────────
        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(12, 10, 12, 0) };

        var lblHead = new Label
        {
            Text = "遊ぶ人に渡すためのzipを作ります。",
            Font = UiTheme.Heading,
            Location = new Point(12, 8),
            AutoSize = true,
        };

        // 何が起きるのかを先に書いておく。
        // ビルドから始まるので数十秒かかることがあり、押した後で「固まった」と思われやすい。
        var lblDesc = new Label
        {
            Text = "Release|x64 をビルドし、exe と assets / img / sound / se を dist\\LabProject01\\ に集めて\n"
                 + "dist\\LabProject01_v<バージョン>.zip に固めます。ビルドから始めると1分ほどかかることがあります。\n"
                 + "編集中のステージは、この画面を開いた時点で保存済みです。",
            Location = new Point(12, 32),
            Size = new Size(660, 56),
            ForeColor = Color.DimGray,
        };

        var lblVer = UiTheme.CreateLabel("バージョン:", new Point(12, 94), bold: true);
        _txtVersion.Location = new Point(96, 91);
        _txtVersion.Width = 120;
        _txtVersion.Font = UiTheme.Base;
        _txtVersion.Text = GuessNextVersion();

        var lblVerHint = new Label
        {
            Text = "zipのファイル名に付きます（例: 0.2.0）",
            Location = new Point(224, 94),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Font = UiTheme.Small,
        };

        _chkSkipBuild.Text = "ビルドし直さず、既にある Release のexeでzipだけ作る";
        _chkSkipBuild.Location = new Point(12, 120);
        _chkSkipBuild.AutoSize = true;

        var lblSkipHint = new Label
        {
            Text = "ステージやアセットだけ直したときに使うと速く済みます（C++側を直したときは外してください）。",
            Location = new Point(30, 142),
            AutoSize = true,
            ForeColor = Color.DimGray,
            Font = UiTheme.Small,
        };

        pnlTop.Controls.AddRange(new Control[] { lblHead, lblDesc, lblVer, _txtVersion, lblVerHint, _chkSkipBuild, lblSkipHint });

        // ── 下部：ボタンと状態表示 ────────────────────────
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 8, 12, 8) };

        _btnRun.Text = "▶ 作成する";
        _btnRun.Size = new Size(120, 30);
        _btnRun.Location = new Point(12, 8);
        UiTheme.StylePrimaryButton(_btnRun);
        _btnRun.Click += async (_, _) => await RunAsync();

        _btnOpenDist.Text = "📂 dist を開く";
        _btnOpenDist.Size = new Size(120, 30);
        _btnOpenDist.Location = new Point(140, 8);
        UiTheme.StyleSecondaryButton(_btnOpenDist);
        _btnOpenDist.Enabled = Directory.Exists(_distDir);
        _btnOpenDist.Click += (_, _) => OpenDistFolder();

        _lblStatus.Location = new Point(272, 14);
        _lblStatus.AutoSize = true;
        _lblStatus.Text = "";

        _btnClose.Text = "閉じる";
        _btnClose.Size = new Size(90, 30);
        _btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnClose.Location = new Point(pnlBottom.Width - 102, 8);
        UiTheme.StyleSecondaryButton(_btnClose);
        _btnClose.Click += (_, _) => Close();

        pnlBottom.Controls.AddRange(new Control[] { _btnRun, _btnOpenDist, _lblStatus, _btnClose });

        // ── 中央：スクリプトの出力をそのまま流す欄 ────────
        // 失敗したときに何が起きたかを、この画面の中だけで読めるようにする
        // （bat をダブルクリックしたときの黒い画面と同じ内容が出る）。
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Both;
        _txtLog.WordWrap = false;
        _txtLog.Dock = DockStyle.Fill;
        _txtLog.BackColor = Color.FromArgb(28, 28, 28);
        _txtLog.ForeColor = Color.Gainsboro;
        _txtLog.Font = new Font("Consolas", 9f);
        var pnlLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
        pnlLog.Controls.Add(_txtLog);

        // Dock=Fill を先に、Top/Bottom を後から足す（このコードベースの並び順の規約）
        Controls.Add(pnlLog);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);

        CancelButton = _btnClose;

        // スクリプトが無ければ何もできないので、開いた時点で理由を出しておく
        if (!File.Exists(_scriptPath))
        {
            _btnRun.Enabled = false;
            AppendLog($"配布スクリプトが見つかりません:\r\n  {_scriptPath}\r\n");
            _lblStatus.Text = "スクリプトが見つかりません";
            _lblStatus.ForeColor = Color.Firebrick;
        }
    }

    // 既にある zip から次に使いそうなバージョンを推測して初期値にする。
    // 毎回同じ既定値だと、作り直すたびに前のzipを黙って上書きしてしまうため。
    private string GuessNextVersion()
    {
        try
        {
            if (!Directory.Exists(_distDir)) return "0.1.0";
            var rx = new Regex(@"^LabProject01_v(?<v>.+)\.zip$", RegexOptions.IgnoreCase);
            // 「最後に作ったzip」を基準にする（ファイル名の並び順ではなく作成時刻で選ぶ）
            var latest = Directory.GetFiles(_distDir, "LabProject01_v*.zip")
                                  .OrderByDescending(File.GetLastWriteTime)
                                  .FirstOrDefault();
            if (latest == null) return "0.1.0";
            var m = rx.Match(Path.GetFileName(latest));
            if (!m.Success) return "0.1.0";
            string v = m.Groups["v"].Value;
            // 末尾の数字を1つ繰り上げる（0.1.0 → 0.1.1）。数字で終わらない形なら手を加えない。
            var tail = Regex.Match(v, @"(\d+)$");
            if (!tail.Success) return v;
            int n = int.Parse(tail.Groups[1].Value) + 1;
            return v.Substring(0, tail.Index) + n;
        }
        catch
        {
            // 推測に失敗しても配布そのものは行えるので、既定値へ落とす
            return "0.1.0";
        }
    }

    // 「作成する」を押したときの本体。PowerShellで配布スクリプトを走らせ、出力をそのまま流す。
    private async Task RunAsync()
    {
        string version = _txtVersion.Text.Trim();
        if (!VersionPattern.IsMatch(version))
        {
            MessageBox.Show(this,
                "バージョンには、数字・英字・ドット・ハイフン・アンダースコアだけを使ってください。\n（zipのファイル名にそのまま入ります）",
                "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtVersion.Focus();
            return;
        }

        string zipPath = Path.Combine(_distDir, $"LabProject01_v{version}.zip");
        if (File.Exists(zipPath))
        {
            var ans = MessageBox.Show(this,
                $"同じ名前のzipが既にあります。上書きしますか？\n\n{zipPath}",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes) return;
        }

        _txtLog.Clear();
        SetRunning(true);

        // PowerShell を -File ではなく -Command で呼ぶ。
        // -File だと出力がその環境の既定コードページで返り、日本語が化けることがある。
        // 子側の [Console]::OutputEncoding をUTF-8に揃え、こちら側も同じ指定で読む。
        string script = _scriptPath.Replace("'", "''"); // PowerShellの単引用符内のエスケープ
        string command = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; "
                       + $"& '{script}' -Version '{version}'"
                       + (_chkSkipBuild.Checked ? " -SkipBuild" : "");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        try
        {
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLogSafe(e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendLogSafe(e.Data); };
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            await _proc.WaitForExitAsync();

            int code = _proc.ExitCode;
            if (code == 0)
            {
                _lblStatus.Text = "完了しました";
                _lblStatus.ForeColor = Color.SeaGreen;
                _btnOpenDist.Enabled = Directory.Exists(_distDir);
                if (File.Exists(zipPath))
                {
                    double mb = Math.Round(new FileInfo(zipPath).Length / 1024.0 / 1024.0, 1);
                    AppendLog($"\r\n→ {zipPath} ({mb} MB)\r\n");
                }
            }
            else
            {
                _lblStatus.Text = $"失敗しました (終了コード {code})";
                _lblStatus.ForeColor = Color.Firebrick;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\r\n起動に失敗しました: {ex.Message}\r\n");
            _lblStatus.Text = "起動に失敗しました";
            _lblStatus.ForeColor = Color.Firebrick;
        }
        finally
        {
            _proc?.Dispose();
            _proc = null;
            SetRunning(false);
        }
    }

    // 実行中かどうかで操作できる項目を切り替える。
    // 走っている最中に二重起動されると、同じ出力先を2つのプロセスが作り直して壊し合うため。
    private void SetRunning(bool running)
    {
        _btnRun.Enabled = !running;
        _txtVersion.Enabled = !running;
        _chkSkipBuild.Enabled = !running;
        UseWaitCursor = running;
        if (running)
        {
            _lblStatus.Text = "作成しています...";
            _lblStatus.ForeColor = Color.DarkSlateBlue;
        }
    }

    // 出力を1行追記する（末尾へ自動スクロール）。
    private void AppendLog(string text)
    {
        _txtLog.AppendText(text);
    }

    // プロセスの出力イベントはUIスレッド以外から来るので、必ず貼り替えてから触る。
    private void AppendLogSafe(string line)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) BeginInvoke(new Action(() => AppendLog(line + "\r\n")));
            else AppendLog(line + "\r\n");
        }
        catch (ObjectDisposedException)
        {
            // 実行中に画面が閉じられた場合。出力の行き先が無いだけなので無視してよい。
        }
    }

    // 出来上がったものが置かれるフォルダをエクスプローラで開く。
    private void OpenDistFolder()
    {
        try
        {
            Directory.CreateDirectory(_distDir);
            Process.Start(new ProcessStartInfo { FileName = _distDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"フォルダを開けませんでした:\n{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 作成中に閉じようとしたときは確認する。
    // 途中で打ち切ると dist\LabProject01\ が作りかけのまま残り、
    // 「zipは古いのにフォルダだけ新しい」という分かりにくい状態になるため。
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_proc != null && !_proc.HasExited)
        {
            var ans = MessageBox.Show(this,
                "配布パッケージを作成中です。中断して閉じますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ans != DialogResult.Yes) { e.Cancel = true; return; }
            try { _proc.Kill(entireProcessTree: true); } catch { /* 既に終了していれば何もしなくてよい */ }
        }
        base.OnFormClosing(e);
    }
}
