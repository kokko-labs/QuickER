using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QuickER.AI.UI;

/// <summary>
/// API キーを Windows DPAPI (CurrentUser スコープ) で暗号化してユーザープロファイル配下へ保存するストア
/// </summary>
public static class ApiKeyStore
{
    /// <summary>暗号化ファイルの既定の保存先フォルダ (%APPDATA%\QuickER)</summary>
    private static readonly string DefaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickER"
    );

    /// <summary>キー名に対応する保存ファイルのフルパスを返す</summary>
    private static string PathFor(string name, string folder) =>
        Path.Combine(folder, name + ".dat");

    /// <summary>API キーを名前付きで暗号化保存する。空文字を渡した場合は保存済みファイルを削除する</summary>
    /// <param name="name">キーの識別名 (保存ファイル名に使用)</param>
    /// <param name="apiKey">保存する API キー</param>
    public static void Save(string name, string apiKey) => Save(name, apiKey, DefaultFolder);

    /// <summary>
    /// 保存先フォルダを指定して API キーを暗号化保存する。
    /// テストが実 %APPDATA% を汚さずに（一時フォルダへ隔離して）保存動作を検証するための版。
    /// </summary>
    /// <param name="name">キーの識別名 (保存ファイル名に使用)</param>
    /// <param name="apiKey">保存する API キー</param>
    /// <param name="folder">保存先フォルダ (存在しなければ作成する)</param>
    public static void Save(string name, string apiKey, string folder)
    {
        Directory.CreateDirectory(folder);
        var p = PathFor(name, folder);

        if (string.IsNullOrEmpty(apiKey))
        {
            if (File.Exists(p))
            {
                File.Delete(p);
            }

            return;
        }

        var data = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(
            data,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser
        );
        File.WriteAllBytes(p, encrypted);
    }

    /// <summary>名前付きで保存された API キーを復号して返す</summary>
    /// <param name="name">キーの識別名</param>
    /// <returns>復号した API キー。未保存または復号失敗時は空文字</returns>
    public static string Load(string name) => Load(name, DefaultFolder);

    /// <summary>
    /// 保存先フォルダを指定して、保存された API キーを復号して返す。
    /// テストが実 %APPDATA% を汚さずに（一時フォルダへ隔離して）復元動作を検証するための版。
    /// </summary>
    /// <param name="name">キーの識別名</param>
    /// <param name="folder">保存先フォルダ</param>
    /// <returns>復号した API キー。未保存または復号失敗時は空文字</returns>
    public static string Load(string name, string folder)
    {
        var p = PathFor(name, folder);

        if (!File.Exists(p))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = File.ReadAllBytes(p);
            var data = ProtectedData.Unprotect(
                encrypted,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            // 別ユーザー・別マシンで暗号化されたファイルや破損ファイルは復号できないため、キー未設定として扱う
            return string.Empty;
        }
    }
}
