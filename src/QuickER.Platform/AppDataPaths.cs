namespace QuickER.Platform;

/// <summary>アプリ設定・作業状態と一時ファイルの置き場所の正本</summary>
public static class AppDataPaths
{
    /// <summary>置き場所のフォルダ名</summary>
    private const string FolderName = "QuickER";

    /// <summary>アプリ設定・作業状態の保存先（<c>%LOCALAPPDATA%\QuickER</c>）</summary>
    /// <remarks>
    /// ローミング（<c>%APPDATA%</c>）ではなくローカル（<c>%LOCALAPPDATA%</c>）を使う。ここに置くのは
    /// この PC に固有の作業状態（ウィンドウ状態・直近ファイル・自動保存・クラッシュログ）と DPAPI（CurrentUser）で
    /// 保護した秘密であり、ローミングプロファイルでドメイン内の他端末へ複製されると、復号できない秘密や
    /// 別端末のパスを運ぶことになるため（Windows のローミング指針どおり、機械固有のものはローカルへ置く）。
    /// </remarks>
    public static string LocalRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName
        );

    /// <summary>一時ファイル・一時フォルダの置き場所（<c>%TEMP%\QuickER</c>）</summary>
    public static string TempRoot => Path.Combine(Path.GetTempPath(), FolderName);
}
