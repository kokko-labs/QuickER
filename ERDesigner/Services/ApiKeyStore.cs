using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ERDesigner.Services;

/// <summary>
/// API キーを Windows DPAPI (CurrentUser スコープ) で暗号化してユーザープロファイルに保存します。
/// </summary>
public static class ApiKeyStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner");

    private static string PathFor(string name) => Path.Combine(Folder, name + ".dat");

    /// <summary>名前付きで API キーを暗号化保存します。空文字なら削除します。</summary>
    public static void Save(string name, string apiKey)
    {
        Directory.CreateDirectory(Folder);
        var p = PathFor(name);
        if (string.IsNullOrEmpty(apiKey))
        {
            if (File.Exists(p)) File.Delete(p);
            return;
        }
        var data = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(data, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(p, encrypted);
    }

    /// <summary>名前付きで保存された API キーを復号して返します。なければ空文字。</summary>
    public static string Load(string name)
    {
        var p = PathFor(name);
        if (!File.Exists(p)) return string.Empty;
        try
        {
            var encrypted = File.ReadAllBytes(p);
            var data = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return string.Empty;
        }
    }
}
