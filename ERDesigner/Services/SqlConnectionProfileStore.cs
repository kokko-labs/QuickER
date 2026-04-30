using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ERDesigner.Services;

/// <summary>
/// SQL 接続プロファイルを JSON ファイルに保存・読込するストア。
/// パスワードは Windows DPAPI (CurrentUser スコープ) で暗号化された別ファイルに保存します。
/// </summary>
public class SqlConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _folder;
    private readonly bool _useDpapi;

    /// <summary>プロファイル JSON のファイルパス。</summary>
    public string ProfilesPath => Path.Combine(_folder, "connections.json");

    /// <summary>パスワード暗号ファイルの格納フォルダ。</summary>
    public string SecretsFolder => Path.Combine(_folder, "connection-secrets");

    /// <summary>既定 (<c>%AppData%\ERDesigner</c>) のストアを生成します。</summary>
    public SqlConnectionProfileStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner"), true)
    { }

    /// <summary>テスト用にフォルダと DPAPI 利用可否を指定して生成します。</summary>
    /// <param name="folder">保存先ディレクトリ。</param>
    /// <param name="useDpapi">DPAPI を使ってパスワードを暗号化するか。<c>false</c> なら平文 (テスト用)。</param>
    public SqlConnectionProfileStore(string folder, bool useDpapi)
    {
        _folder = folder;
        _useDpapi = useDpapi;
    }

    /// <summary>すべてのプロファイルを名前順で読み込みます。</summary>
    public List<SqlConnectionProfile> LoadAll()
    {
        if (!File.Exists(ProfilesPath)) return new List<SqlConnectionProfile>();
        try
        {
            var json = File.ReadAllText(ProfilesPath);
            var list = JsonSerializer.Deserialize<List<SqlConnectionProfile>>(json, JsonOpts)
                       ?? new List<SqlConnectionProfile>();
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return new List<SqlConnectionProfile>();
        }
    }

    /// <summary>すべてのプロファイルを保存します。</summary>
    public void SaveAll(IEnumerable<SqlConnectionProfile> profiles)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(profiles.ToList(), JsonOpts);
        File.WriteAllText(ProfilesPath, json);
    }

    /// <summary>1 件のプロファイルを追加または更新し、必要ならパスワードも暗号化保存します。</summary>
    /// <param name="profile">保存対象。Id が既存と一致すれば上書き、なければ追加。</param>
    /// <param name="password">パスワード。<see cref="SqlConnectionProfile.SavePassword"/> が <c>true</c> の場合のみ保存。</param>
    public void Upsert(SqlConnectionProfile profile, string password)
    {
        var all = LoadAll();
        var idx = all.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) all[idx] = profile;
        else all.Add(profile);
        SaveAll(all);

        if (profile.SavePassword && !string.IsNullOrEmpty(password))
            SaveSecret(profile.Id, password);
        else
            DeleteSecret(profile.Id);
    }

    /// <summary>指定 ID のプロファイルとパスワードを削除します。</summary>
    public void Delete(Guid id)
    {
        var all = LoadAll();
        all.RemoveAll(p => p.Id == id);
        SaveAll(all);
        DeleteSecret(id);
    }

    /// <summary>指定 ID のプロファイルに紐づくパスワードを復号して返します (なければ空文字)。</summary>
    public string LoadPassword(Guid id)
    {
        var path = SecretPath(id);
        if (!File.Exists(path)) return string.Empty;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (_useDpapi)
            {
                var data = ProtectedData.Unprotect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void SaveSecret(Guid id, string password)
    {
        Directory.CreateDirectory(SecretsFolder);
        var raw = Encoding.UTF8.GetBytes(password);
        var bytes = _useDpapi
            ? ProtectedData.Protect(raw, optionalEntropy: null, scope: DataProtectionScope.CurrentUser)
            : raw;
        File.WriteAllBytes(SecretPath(id), bytes);
    }

    private void DeleteSecret(Guid id)
    {
        var p = SecretPath(id);
        if (File.Exists(p)) File.Delete(p);
    }

    private string SecretPath(Guid id) => Path.Combine(SecretsFolder, id.ToString("N") + ".dat");
}
