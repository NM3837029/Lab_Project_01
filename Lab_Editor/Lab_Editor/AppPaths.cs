namespace Lab_Editor;

// プロジェクトルート・ログフォルダの解決 (どの環境でも動くように相対パスで解決する)
// 実行ファイルの場所からフォルダを1階層ずつ遡り、プロジェクトのルートフォルダ
// （Lab_Project_01.vcxprojが置かれている場所）を自動的に探し出すユーティリティクラス。
// 開発者ごとにプロジェクトの配置場所が異なっていても、絶対パスをハードコードせずに
// 正しいルートフォルダを見つけられるようにするための仕組み。
public static class AppPaths
{
    // 一度見つけたプロジェクトルートをキャッシュしておくためのフィールド。
    // 毎回フォルダを遡って探索するのは無駄なので、2回目以降はこの値をそのまま返す。
    private static string? _projectRoot;

    // プロジェクトのルートフォルダの絶対パスを取得するプロパティ。
    public static string ProjectRoot
    {
        get
        {
            // 既に探索済みであれば、キャッシュされた値をそのまま返す。
            if (_projectRoot != null) return _projectRoot;
            // 実行ファイル（.exe）が置かれているフォルダを探索の出発点にする。
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            // 現在のフォルダから親フォルダへと1階層ずつ遡りながら、
            // プロジェクトファイル(Lab_Project_01.vcxproj)が見つかるまで探し続ける。
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Lab_Project_01.vcxproj")))
                {
                    // 見つかったフォルダをプロジェクトルートとしてキャッシュし、返す。
                    _projectRoot = dir;
                    return _projectRoot;
                }
                // 見つからなければ、さらに1階層上の親フォルダに移動する。
                dir = Path.GetDirectoryName(dir);
            }
            // ルートまで遡ってもプロジェクトファイルが見つからなかった場合は、
            // やむを得ず実行ファイルのフォルダをそのままプロジェクトルート扱いにする。
            _projectRoot = AppDomain.CurrentDomain.BaseDirectory;
            return _projectRoot;
        }
    }

    // ログファイルを保存するためのフォルダの絶対パスを取得するプロパティ。
    public static string LogsDir
    {
        get
        {
            // プロジェクトルート直下の"logs"フォルダをログ保存先とする。
            string dir = Path.Combine(ProjectRoot, "logs");
            // フォルダがまだ存在しない場合は作成する（既に存在する場合は何もしない）。
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
