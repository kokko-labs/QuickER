using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickER.Db.UI;

/// <summary>connections.json のルート（登録プロファイル一覧＋前回接続）</summary>
public sealed class SqlConnectionData
{
    /// <summary>登録済みの接続プロファイル一覧</summary>
    public List<SqlConnectionProfile> Profiles { get; set; } = new();

    /// <summary>前回使用した接続情報（未使用時は null）</summary>
    public SqlConnectionProfile? LastUsed { get; set; }
}

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

    /// <summary>接続情報 JSON（プロファイル一覧＋前回接続）のファイルパス</summary>
    public string ConnectionsPath => Path.Combine(_folder, "connections.json");

    /// <summary>パスワード暗号ファイルの格納フォルダ</summary>
    public string SecretsFolder => Path.Combine(_folder, "connection-secrets");

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

    /// <summary>接続情報（プロファイル一覧＋前回接続）を読み込む</summary>
    /// <remarks>
    /// ファイル無し・解析失敗（旧配列形式など新形式として読めない JSON を含む）時は空の
    /// <see cref="SqlConnectionData"/> へフォールバックする
    /// </remarks>
    private SqlConnectionData LoadData()
    {
        if (!File.Exists(ConnectionsPath))
        {
            return new SqlConnectionData();
        }

        try
        {
            var json = File.ReadAllText(ConnectionsPath);
            return JsonSerializer.Deserialize<SqlConnectionData>(json, JsonOpts)
                ?? new SqlConnectionData();
        }
        catch
        {
            // 破損 JSON・旧配列形式等で UI を妨げないよう空データへフォールバックする
            return new SqlConnectionData();
        }
    }

    /// <summary>接続情報（プロファイル一覧＋前回接続）を保存する</summary>
    private void SaveData(SqlConnectionData data)
    {
        Directory.CreateDirectory(_folder);
        var json = JsonSerializer.Serialize(data, JsonOpts);
        WriteAtomic(ConnectionsPath, json);
    }

    /// <summary>JSON 文字列を原子的に（書き込み途中の中断で既存ファイルを壊さずに）書き込む</summary>
    /// <remarks>
    /// <c>QuickER.Settings.JsonSettingsStore.WriteAtomic</c> と同型の実装（tmp へ全量書き切ってから
    /// 本体へ置換）。素の <c>File.WriteAllText</c> は既存ファイルを切り詰めてから書くため、途中で
    /// プロセスが落ちると connections.json が破損した JSON になり、読込側（<see cref="LoadData"/>）が
    /// 空データへフォールバックした状態のまま次の read-modify-write 保存（SaveAll / SaveLastUsed）で
    /// 他のプロファイル・前回接続情報が巻き添えで消失する。QuickER.Db.UI は QuickER.Settings への
    /// プロジェクト参照を持たないため、新規参照を足さずここへ複製している
    /// </remarks>
    /// <param name="path">書き込み先の絶対パス</param>
    /// <param name="json">書き込む JSON 文字列</param>
    private static void WriteAtomic(string path, string json)
    {
        // 同時保存・同名衝突を避けるため GUID を挟んだ一時ファイル名にする
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
                {
                    // クラウド同期フォルダ等、File.Replace が使えない環境向けのフォールバック
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            // 正常終了時は置換/移動済みで存在しない。例外発生時のみ tmp の残骸を掃除する
            // （掃除自体の失敗で元の例外を握り潰さないよう黙殺する）
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // tmp の残骸は次回保存時に上書きされるため無害
            }
        }
    }

    /// <summary>すべてのプロファイルを名前順で読み込む（読み取り失敗時は空一覧を返す）</summary>
    public List<SqlConnectionProfile> LoadAll() =>
        LoadData().Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>前回使用した接続情報を読み込む</summary>
    /// <remarks>パスワードは <see cref="SqlConnectionProfile.SavePassword"/> が有効な場合のみ復号して返す</remarks>
    public (SqlConnectionProfile Profile, string Password)? LoadLastUsed()
    {
        var profile = LoadData().LastUsed;

        if (profile is null)
        {
            return null;
        }

        var password = profile.SavePassword ? LoadSecret(LastConnectionSecretPath()) : string.Empty;
        return (profile, password);
    }

    /// <summary>すべてのプロファイルを保存する（前回接続情報は温存する）</summary>
    public void SaveAll(IEnumerable<SqlConnectionProfile> profiles)
    {
        // read-modify-write で Profiles のみ差し替え、LastUsed を消さない
        var data = LoadData();
        data.Profiles = profiles.ToList();
        SaveData(data);
    }

    /// <summary>前回使用した接続情報を保存する</summary>
    /// <remarks>登録済みプロファイル一覧とは別セクションで管理し、次回ダイアログ表示時の初期値復元に用いる</remarks>
    public void SaveLastUsed(SqlConnectionProfile profile, string password)
    {
        // read-modify-write で LastUsed のみ差し替え、Profiles を消さない
        var data = LoadData();
        data.LastUsed = profile;
        SaveData(data);

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
