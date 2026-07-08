namespace Lab_Editor;

// プロジェクトルート・ログフォルダの解決 (どの環境でも動くように相対パスで解決する)
public static class AppPaths
{
    private static string? _projectRoot;

    public static string ProjectRoot
    {
        get
        {
            if (_projectRoot != null) return _projectRoot;
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Lab_Project_01.vcxproj")))
                {
                    _projectRoot = dir;
                    return _projectRoot;
                }
                dir = Path.GetDirectoryName(dir);
            }
            _projectRoot = AppDomain.CurrentDomain.BaseDirectory;
            return _projectRoot;
        }
    }

    public static string LogsDir
    {
        get
        {
            string dir = Path.Combine(ProjectRoot, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
