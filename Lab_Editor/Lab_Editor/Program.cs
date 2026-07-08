namespace Lab_Editor;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        string logPath = Path.Combine(AppPaths.LogsDir, "error_log.txt");
        Application.ThreadException += (s, e) =>
        {
            System.IO.File.AppendAllText(logPath, e.Exception.ToString() + "\n");
            MessageBox.Show("Error logged to " + logPath);
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.IO.File.AppendAllText(logPath, e.ExceptionObject.ToString() + "\n");
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}