using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickER.Services;

/// <summary>SQL 接続プロファイルを JSON ファイルへ保存・読込するストア</summary>
/// <remarks>
/// パスワードはプロファイル本体とは分離し、Windows DPAPI（CurrentUser スコープ）で
/// 暗号化した別ファイルへ保存する
/// </remarks>
public class SqlConnectionProfileStore
{
    /// <summary>JSON シリアライズ設定（インデント付与・プロパティ名は camelCase）</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>各ファイルの保存先フォルダ</summary>
    private readonly string _folder;

    /// <summary>パスワードを DPAPI で暗号化するかどうか（テストでは平文保存のため false）</summary>
    private readonly bool _useDpapi;

    /// <summary>プロファイル一覧 JSON のファイルパス</summary>
    public string ProfilesPath => Path.Combine(_folder, "connections.json");

    /// <summary>パスワード暗号ファイルの格納フォルダ</summary>
    public string SecretsFolder => Path.Combine(_folder, "connection-secrets");

    /// <summary>前回接続情報 JSON のファイルパス</summary>
    public string LastConnectionPath => Path.Combine(_folder, "last-connection.json");

    /// <summary>既定（<c>%AppData%\QuickER</c>・DPAPI 有効）のストアを生成する</summary>
    public SqlConnectionProfileStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QuickER"
            ),
            true
        ) { }

    /// <summary>保存先フォルダと DPAPI 利用可否を指定してストアを生成する（テスト用）</summary>
    /// <param name="folder">保存先ディレクトリ</param>
    /// <param name="useDpapi">DPAPI でパスワードを暗号化するか <c>false</c> なら平文保存（テスト用）</param>
    public SqlConnectionProfileStore(string folder, bool useDpapi)
    {
        _folder = folder;
        _useDpapi = useDpapi;
    }

    /// <summary>すべてのプロファイルを名前順で読み込む（読み取り失敗時は空一覧を返す）</summary>
    public List<SqlConnectionProfile> LoadAll()
    {
        if (!File.Exists(ProfilesPath))
        {
            return new List<SqlConnectionProfile>();
        }

        try
        {
            var json = File.ReadAllText(ProfilesPath);
            var list =
                JsonSerializer.Deserialize<List<SqlConnectionProfile>>(json, JsonOpts)
                ?? new List<SqlConnectionProfile>();
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            // 破損 JSON 等で UI を妨げないよう空一覧へフォールバックする
            return new List<SqlConnectionProfile>();
        }
    }

    /// <summary>前回使用した接続情報を読み込む</summary>
    /// <remarks>パスワードは <see cref="SqlConnectionProfile.SavePassword"/> が有効な場合のみ復号して返す</remarks>
    public (SqlConnectionProfile Profile, string Password)? LoadLastUsed()
    {
        if (!File.Exists(LastConnectionPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(LastConnectionPath);
            var profile = JsonSerializer.Deserialize<SqlConnectionProfile>(json, JsonOpts);

            if (profile is null)
            {
                return null;
            }

            var password = profile.SavePassword
                ? LoadSecret(LastConnectionSecretPath())
                : string.Empty;
            return (profile, password);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>すべてのプロファイルを保存する</summary>
    public void SaveAll(IEnumerable<SqlConnectionProfile> profiles)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(profiles.ToList(), JsonOpts);
        File.WriteAllText(ProfilesPath, json);
    }

    /// <summary>前回使用した接続情報を保存する</summary>
    /// <remarks>登録済みプロファイルとは別管理とし、次回ダイアログ表示時の初期値復元に用いる</remarks>
    public void SaveLastUsed(SqlConnectionProfile profile, string password)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(profile, JsonOpts);
        File.WriteAllText(LastConnectionPath, json);

        // パスワード保存が無効化された場合は残存する暗号ファイルを確実に削除する
        if (profile.SavePassword && !string.IsNullOrEmpty(password))
        {
            SaveSecret(LastConnectionSecretPath(), password);
        }
        else
        {
            DeleteSecret(LastConnectionSecretPath());
        }
    }

    /// <summary>プロファイルを 1 件追加または更新し、必要に応じてパスワードを暗号化保存する</summary>
    /// <param name="profile">保存対象 Id が既存と一致すれば上書き、なければ追加する</param>
    /// <param name="password">パスワード <see cref="SqlConnectionProfile.SavePassword"/> が <c>true</c> の場合のみ保存する</param>
    public void Upsert(SqlConnectionProfile profile, string password)
    {
        var all = LoadAll();
        var idx = all.FindIndex(p => p.Id == profile.Id);

        if (idx >= 0)
        {
            all[idx] = profile;
        }
        else
        {
            all.Add(profile);
        }

        SaveAll(all);

        // 保存無効・空パスワード時は残存する暗号ファイルを削除する
        if (profile.SavePassword && !string.IsNullOrEmpty(password))
        {
            SaveSecret(profile.Id, password);
        }
        else
        {
            DeleteSecret(profile.Id);
        }
    }

    /// <summary>指定 ID のプロファイルと暗号化パスワードを削除する</summary>
    public void Delete(Guid id)
    {
        var all = LoadAll();
        all.RemoveAll(p => p.Id == id);
        SaveAll(all);
        DeleteSecret(id);
    }

    /// <summary>指定 ID のプロファイルに紐づくパスワードを復号して返す（無ければ空文字）</summary>
    public string LoadPassword(Guid id)
    {
        var path = SecretPath(id);
        return LoadSecret(path);
    }

    /// <summary>暗号ファイルを復号してパスワード文字列を返す（DPAPI 無効時は平文として読む）</summary>
    private string LoadSecret(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);

            if (_useDpapi)
            {
                var data = ProtectedData.Unprotect(
                    bytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(data);
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // 別ユーザーで暗号化された等で復号失敗した場合は空文字を返す
            return string.Empty;
        }
    }

    /// <summary>プロファイル ID に対応する暗号ファイルへパスワードを保存する</summary>
    private void SaveSecret(Guid id, string password) => SaveSecret(SecretPath(id), password);

    /// <summary>指定パスへパスワードを保存する（DPAPI 有効時は暗号化する）</summary>
    private void SaveSecret(string path, string password)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var raw = Encoding.UTF8.GetBytes(password);
        var bytes = _useDpapi
            ? ProtectedData.Protect(
                raw,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            )
            : raw;
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>プロファイル ID に対応する暗号ファイルを削除する</summary>
    private void DeleteSecret(Guid id) => DeleteSecret(SecretPath(id));

    /// <summary>指定パスの暗号ファイルが存在すれば削除する</summary>
    private void DeleteSecret(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>プロファイル ID から暗号ファイルのパスを生成する</summary>
    private string SecretPath(Guid id) => Path.Combine(SecretsFolder, id.ToString("N") + ".dat");

    /// <summary>前回接続情報用の暗号ファイルのパスを生成する</summary>
    private string LastConnectionSecretPath() => Path.Combine(SecretsFolder, "last-connection.dat");
}
