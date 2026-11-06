using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Db.UI;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Db.UI;

/// <summary>接続プロファイルの操作列の表の手の種類（先頭の 9 つが製品の入口で、表の因子 Op の水準。以降は準備の手）</summary>
/// <remarks>SUT は QuickER.Db.UI の <see cref="SqlConnectionProfileStore"/>（connections.json と DPAPI の暗号ファイル）。</remarks>
internal enum ProfileStepKind
{
    /// <summary><see cref="SqlConnectionProfileStore.LoadAll"/></summary>
    LoadAll,

    /// <summary><see cref="SqlConnectionProfileStore.LoadLastUsed"/></summary>
    LoadLastUsed,

    /// <summary><see cref="SqlConnectionProfileStore.LoadPassword"/></summary>
    LoadPassword,

    /// <summary><see cref="SqlConnectionProfileStore.SaveLastUsed"/></summary>
    SaveLastUsed,

    /// <summary><see cref="SqlConnectionProfileStore.Upsert"/>（置き換え先なし）</summary>
    Upsert,

    /// <summary><see cref="SqlConnectionProfileStore.Upsert"/>（置き換え先あり）</summary>
    UpsertReplacing,

    /// <summary><see cref="SqlConnectionProfileStore.Rename"/>（置き換え先なし）</summary>
    Rename,

    /// <summary><see cref="SqlConnectionProfileStore.Rename"/>（置き換え先あり）</summary>
    RenameReplacing,

    /// <summary><see cref="SqlConnectionProfileStore.Delete"/></summary>
    Delete,

    /// <summary>製品の入口（Upsert）で初期の状態を作る（表の手として数えない）</summary>
    Seed,

    /// <summary>製品の入口（SaveLastUsed）で初期の状態を作る（表の手として数えない）</summary>
    SeedLast,

    /// <summary>connections.json へ文字列をそのまま書く（外部の書き込み）</summary>
    WriteJson,

    /// <summary>暗号ファイルを指定のバイトで差し替える（復号できない暗号ファイルを作る）</summary>
    GarbageSecret,

    /// <summary>connections.json を別のハンドルで開いたままにする</summary>
    LockJson,

    /// <summary><see cref="LockJson"/> を閉じる</summary>
    UnlockJson,

    /// <summary>暗号ファイルを別のハンドルで開いたままにする</summary>
    LockSecret,

    /// <summary><see cref="LockSecret"/> を閉じる</summary>
    UnlockSecret,

    /// <summary>暗号フォルダへ、ファイルの作成を拒否する ACL を掛ける</summary>
    DenyCreate,

    /// <summary><see cref="DenyCreate"/> の ACL を外す</summary>
    AllowCreate,

    /// <summary>何もせず、いまの状態だけ <see cref="ProfileStep.Expect"/> で確かめる</summary>
    Probe,
}

/// <summary>手の前の connections.json の状態（実際のファイルから読む）</summary>
internal enum ProfileJsonState
{
    /// <summary>ファイルが無い</summary>
    Missing,

    /// <summary>オブジェクトとして読め、null の要素が無い</summary>
    Healthy,

    /// <summary>トップレベルが配列（旧形式）</summary>
    LegacyArray,

    /// <summary>JSON として読めない</summary>
    Corrupt,

    /// <summary>profiles が null か、null の要素を含む</summary>
    NullElements,

    /// <summary>別のハンドルが開いている</summary>
    Locked,
}

/// <summary>手の対象の暗号ファイルの状態（実際のファイルから読む。対象の無い手は None）</summary>
internal enum ProfileSecretState
{
    /// <summary>ファイルが無い（対象の無い手を含む）</summary>
    None,

    /// <summary>読める（DPAPI なら復号できる）</summary>
    Readable,

    /// <summary>ファイルはあるが DPAPI で復号できない</summary>
    Unreadable,

    /// <summary>別のハンドルが開いている</summary>
    Locked,
}

/// <summary>手の前の暗号フォルダの状態</summary>
internal enum ProfileSecretFolderState
{
    /// <summary>作成を拒否していない</summary>
    Writable,

    /// <summary>ファイルの作成を拒否する ACL が掛かっている</summary>
    CreateDenied,
}

/// <summary>製品の 1 手を流す前に読んだ状態</summary>
internal readonly record struct ProfileObserved(
    ProfileStepKind Op,
    ProfileJsonState Json,
    ProfileSecretState Secret,
    ProfileSecretFolderState Folder
);

/// <summary>操作列の 1 手</summary>
internal sealed record ProfileStep(ProfileStepKind Kind)
{
    /// <summary>対象のプロファイルの行の中の名前（<see cref="ProfileContext.LastKey"/> は前回接続）</summary>
    public string? Key { get; init; }

    /// <summary>置き換え先のプロファイルの行の中の名前</summary>
    public string? Replaced { get; init; }

    /// <summary>手の前にプロファイルを作って <see cref="Key"/> の名前で控える（保存済みの Id を引くときは使わない）</summary>
    public Func<ProfileContext, SqlConnectionProfile>? Make { get; init; }

    /// <summary>渡すパスワード</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Rename の新しい名前</summary>
    public string? NewName { get; init; }

    /// <summary>WriteJson の文字列</summary>
    public string? Text { get; init; }

    /// <summary>GarbageSecret のバイト</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>Lock 系の共有の指定</summary>
    public FileShare Share { get; init; } = FileShare.None;

    /// <summary>例外を投げることを期待する手（投げた例外を控える。投げない手の例外はそのまま伝える）</summary>
    public bool Throws { get; init; }

    /// <summary>元のテストの表明</summary>
    public Action<ProfileObservation>? Expect { get; init; }
}

/// <summary>操作列を流すときの状態（行ごとに 1 つ）</summary>
internal sealed class ProfileContext : IDisposable
{
    /// <summary>前回接続を指す名前（暗号ファイルは固定名 <c>last-connection.dat</c>）</summary>
    public const string LastKey = "last";

    private readonly List<(DirectoryInfo Directory, FileSystemAccessRule Rule)> _denies = [];

    /// <summary>ストアを作る</summary>
    public ProfileContext(string folder, bool useDpapi)
    {
        Folder = folder;
        UseDpapi = useDpapi;
        Store = new SqlConnectionProfileStore(folder, useDpapi);
    }

    /// <summary>保存先のフォルダ</summary>
    public string Folder { get; }

    /// <summary>DPAPI を使うか</summary>
    public bool UseDpapi { get; }

    /// <summary>流す対象のストア</summary>
    public SqlConnectionProfileStore Store { get; }

    /// <summary>行の中の名前 → プロファイル</summary>
    public Dictionary<string, SqlConnectionProfile> Profiles { get; } = new(StringComparer.Ordinal);

    /// <summary>前の手で控えた値（後の手の表明で比べる）</summary>
    public Dictionary<string, object?> Memo { get; } = new(StringComparer.Ordinal);

    /// <summary>開いたままのファイル（パス → ストリーム）</summary>
    public Dictionary<string, FileStream> Locks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ACL を掛けているか</summary>
    public bool CreateDenied => _denies.Count > 0;

    /// <summary>暗号ファイルのパス</summary>
    public string SecretPath(string key) =>
        Path.Combine(
            Store.SecretsFolder,
            key == LastKey ? "last-connection.dat" : Profiles[key].Id.ToString("N") + ".dat"
        );

    /// <summary>ファイルを別のハンドルで開いたままにする</summary>
    public void Lock(string path, FileShare share) =>
        Locks[path] = new FileStream(path, FileMode.Open, FileAccess.Read, share);

    /// <summary>開いたままのファイルを閉じる</summary>
    public void Unlock(string path)
    {
        if (Locks.Remove(path, out var stream))
        {
            stream.Dispose();
        }
    }

    /// <summary>暗号フォルダへファイルの作成の拒否を掛ける</summary>
    public void DenyCreate()
    {
        var info = new DirectoryInfo(Store.SecretsFolder);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny
        );
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        _denies.Add((info, rule));
    }

    /// <summary>ファイルの作成の拒否を外す</summary>
    public void AllowCreate()
    {
        foreach (var (info, rule) in _denies)
        {
            var security = info.GetAccessControl();
            security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }

        _denies.Clear();
    }

    /// <summary>ロックと ACL をすべて外す</summary>
    public void ReleaseAll()
    {
        foreach (var stream in Locks.Values)
        {
            stream.Dispose();
        }

        Locks.Clear();
        AllowCreate();
    }

    /// <inheritdoc />
    public void Dispose() => ReleaseAll();
}

/// <summary>1 手の後の観測</summary>
internal sealed class ProfileObservation(ProfileContext context, object? value, Exception? thrown)
{
    /// <summary>ストア</summary>
    public SqlConnectionProfileStore Store => context.Store;

    /// <summary>手の戻り値</summary>
    public object? Value => value;

    /// <summary>手が投げた例外（<see cref="ProfileStep.Throws"/> の手だけ）</summary>
    public Exception? Thrown => thrown;

    /// <summary>行の中で控えた値</summary>
    public Dictionary<string, object?> Memo => context.Memo;

    /// <summary>行の中の名前のプロファイル（呼び出し側が渡したインスタンス）</summary>
    public SqlConnectionProfile Profile(string key) => context.Profiles[key];

    /// <summary>connections.json の文字列</summary>
    public string ConnectionsText => File.ReadAllText(context.Store.ConnectionsPath);

    /// <summary>connections.json の有無</summary>
    public bool ConnectionsExist => File.Exists(context.Store.ConnectionsPath);

    /// <summary>connections.json を JSON として読む</summary>
    public JsonDocument ConnectionsJson() => JsonDocument.Parse(ConnectionsText);

    /// <summary>暗号ファイルの有無</summary>
    public bool SecretExists(string key) => File.Exists(context.SecretPath(key));

    /// <summary>暗号ファイルのバイト</summary>
    public byte[] SecretBytes(string key) => File.ReadAllBytes(context.SecretPath(key));

    /// <summary>退避ファイルの有無</summary>
    public bool BackupExists => File.Exists(context.Store.CorruptBackupPath);

    /// <summary>退避ファイルの文字列</summary>
    public string BackupText => File.ReadAllText(context.Store.CorruptBackupPath);

    /// <summary>LoadAll の戻り値</summary>
    public List<SqlConnectionProfile> All => (List<SqlConnectionProfile>)value!;

    /// <summary>LoadLastUsed の戻り値</summary>
    public (SqlConnectionProfile Profile, SavedPassword Password)? Last =>
        ((SqlConnectionProfile Profile, SavedPassword Password)?)value;

    /// <summary>LoadPassword の戻り値</summary>
    public SavedPassword Password => (SavedPassword)value!;

    /// <summary>Rename の戻り値</summary>
    public bool Renamed => (bool)value!;

    /// <summary>手が接続情報ファイルを読めず中止した例外を投げたことを確かめる</summary>
    public void ShouldHaveThrownUnavailable() =>
        Thrown.Should().BeOfType<ConnectionProfileStoreUnavailableException>();
}

/// <summary>接続プロファイルの操作列のシナリオ</summary>
/// <param name="UseDpapi">ストアを DPAPI で作るか（行の属性。元のテストと同じ）</param>
/// <param name="PairsSecretsWithOwners">
/// パスワードを「pw:{接続先}」の規約で作った行の印。印のある行は、手の列の後（ロックと ACL を外した後）に
/// 「残った暗号ファイルはどれも JSON のその位置の持ち主の接続先のパスワードで、孤児が無い」を確かめる
/// </param>
/// <param name="Steps">手の列</param>
internal sealed record ProfileScenario(
    bool UseDpapi,
    bool PairsSecretsWithOwners,
    IReadOnlyList<ProfileStep> Steps
)
{
    /// <summary>一時フォルダで手の列を流し、製品の手ごとに手の前の状態を返す</summary>
    public IReadOnlyList<ProfileObserved> Run(string folder) =>
        ProfileStoreRunner.Run(folder, this);
}

/// <summary>接続プロファイルの操作列を流す</summary>
internal static class ProfileStoreRunner
{
    /// <summary>手の列を流す（表明の失敗はそのまま投げる）</summary>
    public static IReadOnlyList<ProfileObserved> Run(string folder, ProfileScenario scenario)
    {
        using var context = new ProfileContext(folder, scenario.UseDpapi);
        var observed = new List<ProfileObserved>();

        foreach (var step in scenario.Steps)
        {
            if (step.Make is not null)
            {
                context.Profiles[step.Key!] = step.Make(context);
            }

            if (OpOf(step.Kind))
            {
                observed.Add(
                    new ProfileObserved(
                        step.Kind,
                        JsonStateOf(context),
                        SecretStateOf(context, step),
                        context.CreateDenied
                            ? ProfileSecretFolderState.CreateDenied
                            : ProfileSecretFolderState.Writable
                    )
                );
            }

            object? value = null;
            Exception? thrown = null;

            try
            {
                value = Execute(context, step);
            }
            catch (Exception ex) when (step.Throws)
            {
                thrown = ex;
            }

            step.Expect?.Invoke(new ProfileObservation(context, value, thrown));
        }

        context.ReleaseAll();
        AssertNoTemporaryFiles(context);

        if (scenario.PairsSecretsWithOwners)
        {
            AssertSecretsPairWithTheirProfiles(context.Store);
        }

        return observed;
    }

    /// <summary>製品の入口か（表の因子 Op の水準か）</summary>
    private static bool OpOf(ProfileStepKind kind) => kind <= ProfileStepKind.Delete;

    private static object? Execute(ProfileContext c, ProfileStep step)
    {
        switch (step.Kind)
        {
            case ProfileStepKind.LoadAll:
                return c.Store.LoadAll();
            case ProfileStepKind.LoadLastUsed:
                return c.Store.LoadLastUsed();
            case ProfileStepKind.LoadPassword:
                return c.Store.LoadPassword(c.Profiles[step.Key!].Id);
            case ProfileStepKind.SaveLastUsed:
            case ProfileStepKind.SeedLast:
                c.Store.SaveLastUsed(c.Profiles[step.Key!], step.Password);
                return null;
            case ProfileStepKind.Upsert:
            case ProfileStepKind.Seed:
                c.Store.Upsert(c.Profiles[step.Key!], step.Password);
                return null;
            case ProfileStepKind.UpsertReplacing:
                c.Store.Upsert(
                    c.Profiles[step.Key!],
                    step.Password,
                    replacedProfileId: c.Profiles[step.Replaced!].Id
                );
                return null;
            case ProfileStepKind.Rename:
                return c.Store.Rename(c.Profiles[step.Key!].Id, step.NewName!);
            case ProfileStepKind.RenameReplacing:
                return c.Store.Rename(
                    c.Profiles[step.Key!].Id,
                    step.NewName!,
                    replacedProfileId: c.Profiles[step.Replaced!].Id
                );
            case ProfileStepKind.Delete:
                c.Store.Delete(c.Profiles[step.Key!].Id);
                return null;
            case ProfileStepKind.WriteJson:
                File.WriteAllText(c.Store.ConnectionsPath, step.Text!);
                return null;
            case ProfileStepKind.GarbageSecret:
                File.WriteAllBytes(c.SecretPath(step.Key!), step.Bytes!);
                return null;
            case ProfileStepKind.LockJson:
                c.Lock(c.Store.ConnectionsPath, step.Share);
                return null;
            case ProfileStepKind.UnlockJson:
                c.Unlock(c.Store.ConnectionsPath);
                return null;
            case ProfileStepKind.LockSecret:
                c.Lock(c.SecretPath(step.Key!), step.Share);
                return null;
            case ProfileStepKind.UnlockSecret:
                c.Unlock(c.SecretPath(step.Key!));
                return null;
            case ProfileStepKind.DenyCreate:
                c.DenyCreate();
                return null;
            case ProfileStepKind.AllowCreate:
                c.AllowCreate();
                return null;
            default:
                return null;
        }
    }

    /// <summary>手の前の connections.json の状態をファイルから読む</summary>
    private static ProfileJsonState JsonStateOf(ProfileContext c)
    {
        var path = c.Store.ConnectionsPath;

        if (c.Locks.ContainsKey(path))
        {
            return ProfileJsonState.Locked;
        }

        if (!File.Exists(path))
        {
            return ProfileJsonState.Missing;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return ProfileJsonState.LegacyArray;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return ProfileJsonState.Corrupt;
            }

            if (root.TryGetProperty("profiles", out var profiles))
            {
                if (profiles.ValueKind == JsonValueKind.Null)
                {
                    return ProfileJsonState.NullElements;
                }

                if (
                    profiles.ValueKind == JsonValueKind.Array
                    && profiles.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Null)
                )
                {
                    return ProfileJsonState.NullElements;
                }
            }

            return ProfileJsonState.Healthy;
        }
        catch (JsonException)
        {
            return ProfileJsonState.Corrupt;
        }
    }

    /// <summary>手の対象の暗号ファイルの状態をファイルから読む（対象の暗号ファイルを持たない手は None）</summary>
    private static ProfileSecretState SecretStateOf(ProfileContext c, ProfileStep step)
    {
        var key = step.Kind switch
        {
            ProfileStepKind.LoadAll => null,
            ProfileStepKind.LoadLastUsed or ProfileStepKind.SaveLastUsed => ProfileContext.LastKey,
            ProfileStepKind.UpsertReplacing or ProfileStepKind.RenameReplacing => step.Replaced,
            _ => step.Key,
        };

        if (key is null)
        {
            return ProfileSecretState.None;
        }

        var path = c.SecretPath(key);

        if (c.Locks.ContainsKey(path))
        {
            return ProfileSecretState.Locked;
        }

        if (!File.Exists(path))
        {
            return ProfileSecretState.None;
        }

        if (!c.UseDpapi)
        {
            return ProfileSecretState.Readable;
        }

        try
        {
            ProtectedData.Unprotect(
                File.ReadAllBytes(path),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );
            return ProfileSecretState.Readable;
        }
        catch (CryptographicException)
        {
            return ProfileSecretState.Unreadable;
        }
    }

    /// <summary>全行に共通：connections.json のフォルダにも暗号フォルダにも一時ファイルの残りが無い</summary>
    private static void AssertNoTemporaryFiles(ProfileContext c)
    {
        foreach (var directory in new[] { c.Folder, c.Store.SecretsFolder })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            Directory
                .GetFiles(directory, "*.tmp")
                .Should()
                .BeEmpty($"保存は成功しても失敗しても一時ファイルを残さない（{directory}）");
        }
    }

    /// <summary>
    /// 残った暗号ファイルがどれも、いま JSON にあるその位置のプロファイルの接続先のパスワード（pw:{接続先}）であること
    /// （孤児の暗号ファイルも無いこと）を確かめる
    /// </summary>
    private static void AssertSecretsPairWithTheirProfiles(SqlConnectionProfileStore store)
    {
        var owners = store
            .LoadAll()
            .ToDictionary(
                p => p.Id.ToString("N") + ".dat",
                p => p.Server,
                StringComparer.OrdinalIgnoreCase
            );

        if (store.LoadLastUsed() is { } lastUsed)
        {
            owners["last-connection.dat"] = lastUsed.Profile.Server;
        }

        if (!Directory.Exists(store.SecretsFolder))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(store.SecretsFolder))
        {
            var name = Path.GetFileName(file);
            owners
                .Should()
                .ContainKey(name, $"暗号ファイル {name} の持ち主のプロファイルが無い（孤児）");
            File.ReadAllText(file)
                .Should()
                .Be(
                    ProfileStoreRows.Pw(owners[name]),
                    $"{name} は接続先 {owners[name]} のパスワードでなければならない"
                );
        }
    }
}

/// <summary>
/// 接続プロファイルの保存の操作列の表（QuickER.Db.UI の SqlConnectionProfileStore＝connections.json と DPAPI の暗号ファイルを見る）
/// </summary>
/// <remarks>
/// 行は元の単発のテストの手の順と表明をそのまま持つ。主題ごとの行は <c>ProfileStoreRows.{主題}.cs</c> の private メソッドが返し、
/// <see cref="AllRows"/> に足す。手の前の状態（connections.json・手の対象の暗号ファイル・暗号フォルダ）は実際のファイルから読んで数える。
/// </remarks>
internal static partial class ProfileStoreRows
{
    /// <summary>表</summary>
    public static ScenarioTable<ProfileScenario> Table =>
        new(
            EngineIds.OsBoundary,
            [.. BasicRows(), .. LegacyRows(), .. TlsRows(), .. UnreadableRows(), .. PairingRows()]
        );

    /// <summary>パスワードを接続先から決める規約（印を付けた行の暗号ファイルの中身）</summary>
    public static string Pw(string server) => "pw:" + server;

    private static ScenarioRow<ProfileScenario> Row(
        string id,
        string description,
        params ProfileStep[] steps
    ) => new(id, null, description, new ProfileScenario(false, false, steps));

    /// <summary>DPAPI で暗号化して保存する行（実 DPAPI の Protect / Unprotect を通す）</summary>
    private static ScenarioRow<ProfileScenario> DpapiRow(
        string id,
        string description,
        params ProfileStep[] steps
    ) => new(id, null, description, new ProfileScenario(true, false, steps));

    /// <summary>パスワードを「pw:{接続先}」で作った行（手の列の後に暗号ファイルと持ち主の組を確かめる）</summary>
    private static ScenarioRow<ProfileScenario> PairedRow(
        string id,
        string description,
        params ProfileStep[] steps
    ) => new(id, null, description, new ProfileScenario(false, true, steps));

    // ---------------- プロファイルの作り方 ----------------

    private static Func<ProfileContext, SqlConnectionProfile> Prof(
        string name = "",
        string server = "",
        string database = "",
        bool save = false,
        Action<SqlConnectionProfile>? tweak = null
    ) =>
        _ =>
        {
            var profile = new SqlConnectionProfile
            {
                Name = name,
                Server = server,
                Database = database,
                SavePassword = save,
            };
            tweak?.Invoke(profile);
            return profile;
        };

    /// <summary>行の中の名前の既存のプロファイルを書き換えて、同じインスタンスを渡す</summary>
    private static Func<ProfileContext, SqlConnectionProfile> Edit(
        string key,
        Action<SqlConnectionProfile> change
    ) =>
        context =>
        {
            var profile = context.Profiles[key];
            change(profile);
            return profile;
        };

    /// <summary>保存しない・暗号ファイルを持たない、Id だけのプロファイル（未登録の Id を作る）</summary>
    private static Func<ProfileContext, SqlConnectionProfile> Unregistered() =>
        _ => new SqlConnectionProfile();

    // ---------------- 手の作り方 ----------------

    private static ProfileStep Seed(
        string key,
        Func<ProfileContext, SqlConnectionProfile> make,
        string password = ""
    ) =>
        new(ProfileStepKind.Seed)
        {
            Key = key,
            Make = make,
            Password = password,
        };

    private static ProfileStep SeedLast(
        Func<ProfileContext, SqlConnectionProfile> make,
        string password = ""
    ) =>
        new(ProfileStepKind.SeedLast)
        {
            Key = ProfileContext.LastKey,
            Make = make,
            Password = password,
        };

    private static ProfileStep Upsert(
        string key,
        Func<ProfileContext, SqlConnectionProfile> make,
        string password = "",
        string? replaced = null,
        bool throws = false,
        Action<ProfileObservation>? expect = null
    ) =>
        new(replaced is null ? ProfileStepKind.Upsert : ProfileStepKind.UpsertReplacing)
        {
            Key = key,
            Make = make,
            Password = password,
            Replaced = replaced,
            Throws = throws,
            Expect = expect,
        };

    private static ProfileStep SaveLast(
        Func<ProfileContext, SqlConnectionProfile> make,
        string password = "",
        bool throws = false,
        Action<ProfileObservation>? expect = null
    ) =>
        new(ProfileStepKind.SaveLastUsed)
        {
            Key = ProfileContext.LastKey,
            Make = make,
            Password = password,
            Throws = throws,
            Expect = expect,
        };

    private static ProfileStep LoadAll(Action<ProfileObservation>? expect = null) =>
        new(ProfileStepKind.LoadAll) { Expect = expect };

    private static ProfileStep LoadLast(Action<ProfileObservation>? expect = null) =>
        new(ProfileStepKind.LoadLastUsed) { Expect = expect };

    private static ProfileStep LoadPassword(
        string key,
        Action<ProfileObservation>? expect = null,
        Func<ProfileContext, SqlConnectionProfile>? make = null
    ) =>
        new(ProfileStepKind.LoadPassword)
        {
            Key = key,
            Make = make,
            Expect = expect,
        };

    private static ProfileStep Rename(
        string key,
        string newName,
        string? replaced = null,
        bool throws = false,
        Func<ProfileContext, SqlConnectionProfile>? make = null,
        Action<ProfileObservation>? expect = null
    ) =>
        new(replaced is null ? ProfileStepKind.Rename : ProfileStepKind.RenameReplacing)
        {
            Key = key,
            NewName = newName,
            Replaced = replaced,
            Throws = throws,
            Make = make,
            Expect = expect,
        };

    private static ProfileStep Delete(
        string key,
        bool throws = false,
        Action<ProfileObservation>? expect = null
    ) =>
        new(ProfileStepKind.Delete)
        {
            Key = key,
            Throws = throws,
            Expect = expect,
        };

    private static ProfileStep WriteJson(string text) =>
        new(ProfileStepKind.WriteJson) { Text = text };

    private static ProfileStep GarbageSecret(string key, byte[] bytes) =>
        new(ProfileStepKind.GarbageSecret) { Key = key, Bytes = bytes };

    private static ProfileStep LockJson(FileShare share) =>
        new(ProfileStepKind.LockJson) { Share = share };

    private static ProfileStep UnlockJson() => new(ProfileStepKind.UnlockJson);

    private static ProfileStep LockSecret(string key, FileShare share) =>
        new(ProfileStepKind.LockSecret) { Key = key, Share = share };

    private static ProfileStep DenyCreate() => new(ProfileStepKind.DenyCreate);

    private static ProfileStep Probe(Action<ProfileObservation> expect) =>
        new(ProfileStepKind.Probe) { Expect = expect };
}
