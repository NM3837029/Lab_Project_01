namespace Lab_Editor;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.ThreadException += (s, e) => 
        {
            System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\error_log.txt", e.Exception.ToString() + "\n");
            MessageBox.Show("Error logged to error_log.txt");
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.IO.File.AppendAllText("C:\\Users\\naots\\Documents\\OriginalGame\\error_log.txt", e.ExceptionObject.ToString() + "\n");
        };

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }    
}