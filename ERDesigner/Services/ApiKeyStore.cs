using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ERDesigner.Services;

/// <summary>
/// API キーを Windows DPAPI (CurrentUser スコープ) で暗号化してユーザープロファイル配下へ保存するストア
/// </summary>
public static class ApiKeyStore
{
    /// <summary>暗号化ファイルの保存先フォルダ (%APPDATA%\ERDesigner)</summary>
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner");

    /// <summary>キー名に対応する保存ファイルのフルパスを返す</summary>
    private static string PathFor(string name) => Path.Combine(Folder, name + ".dat");

    /// <summary>API キーを名前付きで暗号化保存する。空文字を渡した場合は保存済みファイルを削除する</summary>
    /// <param name="name">キーの識別名 (保存ファイル名に使用)</param>
    /// <param name="apiKey">保存する API キー</param>
    public static void Save(string name, string apiKey)
    {
        Directory.CreateDirectory(Folder);
        var p = PathFor(name);

        if (string.IsNullOrEmpty(apiKey))
        {
            if (File.Exists(p))
            {
                File.Delete(p);
            }

            return;
        }

        var data = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(data, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(p, encrypted);
    }

    /// <summary>名前付きで保存された API キーを復号して返す</summary>
    /// <param name="name">キーの識別名</param>
    /// <returns>復号した API キー。未保存または復号失敗時は空文字</returns>
    public static string Load(string name)
    {
        var p = PathFor(name);

        if (!File.Exists(p))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = File.ReadAllBytes(p);
            var data = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            // 別ユーザー・別マシンで暗号化されたファイルや破損ファイルは復号できないため、キー未設定として扱う
            return string.Empty;
        }
    }
}
