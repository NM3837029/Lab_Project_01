namespace Lab_Editor;

static class Program
{
    /// <summary>
    /// アプリケーションのエントリーポイント（プログラム起動時に最初に呼び出される関数）。
    /// </summary>
    [STAThread]
    static void Main()
    {
        // ログファイルの保存先パスを組み立てる（AppPaths.LogsDirが存在確認・作成まで行ってくれる）。
        string logPath = Path.Combine(AppPaths.LogsDir, "error_log.txt");
        // UIスレッド上で捕捉されなかった例外が発生した場合のハンドラを登録する。
        // 例外の内容をログファイルに追記し、ユーザーにもメッセージボックスで通知する。
        Application.ThreadException += (s, e) =>
        {
            System.IO.File.AppendAllText(logPath, e.Exception.ToString() + "\n");
            MessageBox.Show("Error logged to " + logPath);
        };
        // Windows Formsの例外処理モードを「捕捉する」設定にする。
        // これによりThreadExceptionハンドラが確実に呼ばれるようになる。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        // UIスレッド以外（バックグラウンドスレッド等）で発生した、
        // どこにも捕捉されなかった例外を記録するためのハンドラを登録する。
        // こちらはアプリが強制終了する直前に呼ばれるため、ログ記録のみ行う。
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.IO.File.AppendAllText(logPath, e.ExceptionObject.ToString() + "\n");
        };

        // Windows Formsアプリケーションの既定設定（DPI対応やビジュアルスタイル等）を初期化する。
        ApplicationConfiguration.Initialize();
        // メインフォーム(Form1)を生成して表示し、アプリケーションのメッセージループを開始する。
        Application.Run(new Form1());
    }
}
