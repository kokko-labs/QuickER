using System.IO;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.UI;
using QuickER.Platform;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>保存した API キーの入口</summary>
internal enum ApiKeyEntrance
{
    /// <summary><c>ApiKeyStore.Load</c>（読む）</summary>
    Load,

    /// <summary><c>ApiKeyStore.Save</c> に空でないキーを渡す（書く）</summary>
    Write,

    /// <summary><c>ApiKeyStore.Save</c> に空文字を渡す（消す）</summary>
    Delete,

    /// <summary>接続設定の VM で保存をオフにする（読み書きは実のストア）</summary>
    ToggleOff,

    /// <summary>接続設定の VM で保存をオフにしてからオンへ戻す（読み書きは実のストア）</summary>
    ToggleOffThenOn,
}

/// <summary>入口の前の暗号ファイルの状態</summary>
internal enum ApiKeyFileState
{
    /// <summary>フォルダはあるがファイルが無い</summary>
    Missing,

    /// <summary>保存先のフォルダが無い</summary>
    FolderMissing,

    /// <summary>この利用者の DPAPI（CurrentUser）で暗号化した正しいファイル</summary>
    Valid,

    /// <summary>壊れている（DPAPI の形でないバイト・途中で切ったファイル・0 バイト）</summary>
    Corrupt,

    /// <summary>
    /// 別の鍵の暗号（別の利用者・別の PC で暗号化したファイルを模す）：形は正しい DPAPI だが、エントロピーを付けて
    /// 暗号化したので、エントロピーなしの復号は <see cref="CryptographicException"/> になる。別の利用者そのものはテストで作れない
    /// </summary>
    OtherKeyEncrypted,

    /// <summary>別のハンドルが共有なしで開いたまま（一時的に読めない）</summary>
    Locked,
}

/// <summary>1 つの入口を流した結果</summary>
/// <param name="Loaded">読む入口の戻り値（ほかは null）</param>
/// <param name="Thrown">入口が投げた例外（投げなければ null）</param>
/// <param name="Unreadable">VM の入口の後の「読めない」の印（VM でない入口は null）</param>
/// <param name="Before">入口の前のファイルのバイト（無ければ null）</param>
/// <param name="After">入口の後のファイルのバイト（ロックを外してから読む・無ければ null）</param>
/// <param name="ReadBack">入口の後に読み直した結果（ロックを外してから）</param>
internal sealed record ApiKeyOutcome(
    SavedApiKey? Loaded,
    Exception? Thrown,
    bool? Unreadable,
    byte[]? Before,
    byte[]? After,
    SavedApiKey ReadBack
)
{
    /// <summary>ファイルのバイトが入口の前と同じ（無いままも同じとみなす）</summary>
    public bool Unchanged =>
        Before is null ? After is null : After is not null && Before.SequenceEqual(After);
}

/// <summary>保存した API キーの表の 1 ケースの組（一時フォルダの保存先・ロック・VM の設定ストア）</summary>
/// <remarks>
/// <para>
/// キーの名前は接続設定の VM の OpenAI のスロット（<see cref="Name"/>）にそろえる。VM の入口（保存のオン／オフ）が同じ暗号ファイルを
/// 読み書きするようにするため（VM の既定のプロバイダーは OpenAI）。
/// </para>
/// <para>全行に共通の表明（<see cref="Run"/> の中）：</para>
/// <list type="number">
/// <item>入口の前の状態が読めない（壊れている・別の鍵の暗号・ロック）とき、読む入口と保存のオン／オフはファイルのバイトを変えない
/// （読めないキーを空で上書きも削除もしない）。</item>
/// <item>入口の後に、保存先のフォルダへ一時ファイルの残りが無い。</item>
/// </list>
/// </remarks>
internal sealed class ApiKeyStoreHarness : IDisposable
{
    /// <summary>キーの名前（接続設定の VM の OpenAI のスロットと同じ）</summary>
    public const string Name = "OpenAiApiKey";

    /// <summary>前から保存してあるキー</summary>
    public const string SavedKey = "sk-saved-0123";

    /// <summary>
    /// 書く入口で渡すキー（記号と ASCII でない文字を含む＝UTF-8 の符号化と暗号の往復で文字が変わらないことも見る）
    /// </summary>
    public const string NewKey = "sk-test-abc123!@#-é鍵";

    /// <summary>別の鍵の暗号のエントロピー</summary>
    private static readonly byte[] OtherKeyEntropy = "another-user"u8.ToArray();

    private readonly TestTempFolder _temp = new();
    private FileStream? _lock;

    /// <summary>暗号ファイルの保存先（作るのは行の準備かストア）</summary>
    public string Folder => Path.Combine(_temp.Path, "keys");

    /// <summary>暗号ファイルのパス</summary>
    public string FilePath => Path.Combine(Folder, Name + ".dat");

    /// <summary>流した（入口, 入口の前の状態）</summary>
    public List<(ApiKeyEntrance Entrance, ApiKeyFileState State)> Observed { get; } = [];

    /// <summary>入口の前のファイルを作る</summary>
    /// <param name="state">状態</param>
    /// <param name="corrupt">壊れているときのバイト（null なら DPAPI の形でない 4 バイト）</param>
    public void Prepare(ApiKeyFileState state, byte[]? corrupt = null)
    {
        if (state == ApiKeyFileState.FolderMissing)
        {
            return;
        }

        Directory.CreateDirectory(Folder);

        switch (state)
        {
            case ApiKeyFileState.Valid:
            case ApiKeyFileState.Locked:
                File.WriteAllBytes(FilePath, Protect(SavedKey, null));
                break;

            case ApiKeyFileState.Corrupt:
                File.WriteAllBytes(FilePath, corrupt ?? [1, 2, 3, 4]);
                break;

            case ApiKeyFileState.OtherKeyEncrypted:
                File.WriteAllBytes(FilePath, Protect(SavedKey, OtherKeyEntropy));
                break;
        }

        if (state == ApiKeyFileState.Locked)
        {
            _lock = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
    }

    /// <summary>この利用者の DPAPI で暗号化した正しいファイルを途中で切ったバイト</summary>
    public static byte[] TruncatedValid()
    {
        var full = Protect(SavedKey, null);
        return full[..(full.Length / 2)];
    }

    /// <summary>入口を流し、結果を返す（全行に共通の表明もここで行う）</summary>
    /// <param name="entrance">入口</param>
    /// <param name="key">書く入口のキー（ほかは使わない）</param>
    public ApiKeyOutcome Run(ApiKeyEntrance entrance, string key = NewKey)
    {
        var state = StateOf();
        Observed.Add((entrance, state));
        var before = ReadBytes();
        SavedApiKey? loaded = null;
        Exception? thrown = null;
        bool? unreadable = null;

        try
        {
            switch (entrance)
            {
                case ApiKeyEntrance.Load:
                    loaded = ApiKeyStore.Load(Name, Folder);
                    break;

                case ApiKeyEntrance.Write:
                    ApiKeyStore.Save(Name, key, Folder);
                    break;

                case ApiKeyEntrance.Delete:
                    ApiKeyStore.Save(Name, string.Empty, Folder);
                    break;

                default:
                    var vm = CreateViewModel();
                    vm.LoadSettings();
                    vm.Initialize();
                    vm.SaveApiKey = false;

                    if (entrance == ApiKeyEntrance.ToggleOffThenOn)
                    {
                        vm.SaveApiKey = true;
                    }

                    unreadable = vm.IsSavedApiKeyUnreadable;
                    break;
            }
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Unlock();
        var outcome = new ApiKeyOutcome(
            loaded,
            thrown,
            unreadable,
            before,
            ReadBytes(),
            ApiKeyStore.Load(Name, Folder)
        );

        if (
            state
                is ApiKeyFileState.Corrupt
                    or ApiKeyFileState.OtherKeyEncrypted
                    or ApiKeyFileState.Locked
            && entrance
                is ApiKeyEntrance.Load
                    or ApiKeyEntrance.ToggleOff
                    or ApiKeyEntrance.ToggleOffThenOn
        )
        {
            outcome
                .Unchanged.Should()
                .BeTrue(
                    $"共通の表明 1：読めないキー（{state}）を {entrance} が上書きも削除もしない"
                );
        }

        if (Directory.Exists(Folder))
        {
            Directory
                .EnumerateFiles(Folder)
                .Select(Path.GetFileName)
                .Where(name => AtomicFile.IsTemporaryFileName(name!))
                .Should()
                .BeEmpty("共通の表明 2：一時ファイルを残さない");
        }

        return outcome;
    }

    /// <summary>接続設定の VM を作る（API キーの読み書きは実のストアの保存先指定の版）</summary>
    private ChatConnectionSettingsViewModel CreateViewModel() =>
        new(
            AiDialogKind.AiChat,
            settingsStore: new AiSettingsStore(Path.Combine(_temp.Path, "settings")),
            codexConfigReader: () => new CodexConfigToml(),
            apiKeyLoader: name => ApiKeyStore.Load(name, Folder),
            apiKeySaver: (name, value) => ApiKeyStore.Save(name, value, Folder)
        );

    /// <summary>入口の前の状態をファイルから読む（製品の関数を使わない）</summary>
    private ApiKeyFileState StateOf()
    {
        if (!Directory.Exists(Folder))
        {
            return ApiKeyFileState.FolderMissing;
        }

        if (_lock is not null)
        {
            return ApiKeyFileState.Locked;
        }

        if (!File.Exists(FilePath))
        {
            return ApiKeyFileState.Missing;
        }

        var bytes = File.ReadAllBytes(FilePath);

        if (TryUnprotect(bytes, null))
        {
            return ApiKeyFileState.Valid;
        }

        return TryUnprotect(bytes, OtherKeyEntropy)
            ? ApiKeyFileState.OtherKeyEncrypted
            : ApiKeyFileState.Corrupt;
    }

    /// <summary>ファイルのバイト（無ければ null。ロック中は外してから呼ぶ）</summary>
    private byte[]? ReadBytes()
    {
        if (_lock is not null)
        {
            _lock.Position = 0;
            using var copy = new MemoryStream();
            _lock.CopyTo(copy);
            return copy.ToArray();
        }

        return File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;
    }

    private void Unlock()
    {
        _lock?.Dispose();
        _lock = null;
    }

    private static byte[] Protect(string key, byte[]? entropy) =>
        ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key),
            entropy,
            DataProtectionScope.CurrentUser
        );

    private static bool TryUnprotect(byte[] bytes, byte[]? entropy)
    {
        try
        {
            ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Unlock();
        _temp.Dispose();
    }
}

/// <summary>保存した API キーの表の行の入力</summary>
/// <param name="Entrance">入口</param>
/// <param name="State">入口の前の状態（行の準備が作る状態。数えるのは実際に読んだ状態）</param>
/// <param name="Corrupt">壊れているときのバイト（null なら 4 バイト）</param>
/// <param name="Expect">結果の表明</param>
internal sealed record ApiKeyCase(
    ApiKeyEntrance Entrance,
    ApiKeyFileState State,
    byte[]? Corrupt,
    Action<ApiKeyOutcome, ApiKeyStoreHarness> Expect
);

/// <summary>保存した API キーの表の行</summary>
/// <remarks>
/// <para>
/// 期待は行ごとに手で書く（製品の関数を期待に使わない）。読めないファイル（壊れている・別の鍵の暗号・ロック）を、
/// 読む入口と保存のオン／オフが上書きも削除もしないことは、全行に共通の表明（<see cref="ApiKeyStoreHarness.Run"/>）が見る。
/// </para>
/// <para>
/// ストアの書く・消す入口は、読めるかに依らず上書き・削除する（ストアの契約は「空なら消す」）。読めないキーを守るのは
/// 接続設定の VM の入口（読めない間は、新しいキーが入力されるまで保存の関数を呼ばない）で、その行は
/// このファイルの <c>toggle-*</c> の行と、偽のストアで操作の列を流す <c>ConnectionSettingsTests</c> の行
/// <c>failure-unreadable-api-key-is-neither-overwritten-nor-deleted</c> にある。
/// </para>
/// </remarks>
internal static class ApiKeyStoreRows
{
    /// <summary>書く入口で渡すキー（ストアの既定のキーと同じ）</summary>
    private const string NewKey = ApiKeyStoreHarness.NewKey;

    /// <summary>ストアの書く・消す入口が読めないファイルを上書き・削除してよい理由（行の説明に添える）</summary>
    private const string StoreContract =
        "ストアの契約どおり。読めないキーを守るのは VM の入口（toggle-* の行・ConnectionSettingsTests の行 failure-unreadable-api-key-is-neither-overwritten-nor-deleted）";

    /// <summary>行の表</summary>
    public static ScenarioTable<ApiKeyCase> Table =>
        new(EngineIds.Ai, [.. LoadRows(), .. WriteRows(), .. DeleteRows(), .. ToggleRows()]);

    /// <summary>読む入口</summary>
    private static IEnumerable<ScenarioRow<ApiKeyCase>> LoadRows() =>
        [
            Row(
                "load-missing-is-missing",
                "ファイルが無ければ保存されていないを返す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.Missing,
                (o, _) => o.Loaded.Should().Be(SavedApiKey.Missing)
            ),
            Row(
                "load-folder-missing-is-missing-and-creates-nothing",
                "保存先のフォルダが無ければ保存されていないを返し、フォルダを作らない",
                ApiKeyEntrance.Load,
                ApiKeyFileState.FolderMissing,
                (o, h) =>
                {
                    o.Loaded.Should().Be(SavedApiKey.Missing);
                    Directory.Exists(h.Folder).Should().BeFalse();
                }
            ),
            Row(
                "load-valid-returns-key",
                "この利用者の暗号ファイルは読めたとしてキーを返す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.Valid,
                (o, _) =>
                {
                    o.Loaded.Should()
                        .Be(new SavedApiKey(SavedApiKeyStatus.Loaded, ApiKeyStoreHarness.SavedKey));
                    o.Unchanged.Should().BeTrue();
                }
            ),
            Row(
                "load-corrupt-four-bytes-is-unreadable-and-keeps-file",
                "復号できない暗号ファイル（DPAPI の形でない 4 バイト）は、保存されていないではなく読めないとして返し、ファイルは残す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.Corrupt,
                UnreadableLoad
            ),
            Row(
                "load-corrupt-truncated-is-unreadable-and-keeps-file",
                "途中で切れた暗号ファイルは読めないとして返し、ファイルは残す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.Corrupt,
                UnreadableLoad,
                ApiKeyStoreHarness.TruncatedValid()
            ),
            Row(
                "load-corrupt-empty-is-unreadable-and-keeps-file",
                "0 バイトの暗号ファイルは読めないとして返し、ファイルは残す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.Corrupt,
                UnreadableLoad,
                []
            ),
            Row(
                "load-other-key-is-unreadable-and-keeps-file",
                "別の鍵の暗号（別の利用者・別の PC で暗号化したもの）は読めないとして返し、ファイルは残す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.OtherKeyEncrypted,
                UnreadableLoad
            ),
            Row(
                "load-locked-is-unreadable-and-keeps-file",
                "一時的なロックで開けないファイルは読めないとして返し、ファイルは残す",
                ApiKeyEntrance.Load,
                ApiKeyFileState.Locked,
                UnreadableLoad
            ),
        ];

    /// <summary>書く入口</summary>
    private static IEnumerable<ScenarioRow<ApiKeyCase>> WriteRows() =>
        [
            Row(
                "write-missing-then-load-round-trips",
                "保存した API キーを読むと同じキーが返る",
                ApiKeyEntrance.Write,
                ApiKeyFileState.Missing,
                Written
            ),
            Row(
                "write-folder-missing-creates-folder-and-round-trips",
                "保存先のフォルダが無ければ作って保存する",
                ApiKeyEntrance.Write,
                ApiKeyFileState.FolderMissing,
                Written
            ),
            Row(
                "write-valid-overwrites",
                "保存済みのキーを新しいキーで上書きする",
                ApiKeyEntrance.Write,
                ApiKeyFileState.Valid,
                Written
            ),
            Row(
                "write-corrupt-overwrites-by-store-contract",
                "壊れたファイルも新しいキーで上書きする（" + StoreContract + "）",
                ApiKeyEntrance.Write,
                ApiKeyFileState.Corrupt,
                Written
            ),
            Row(
                "write-other-key-overwrites-by-store-contract",
                "別の鍵の暗号も新しいキーで上書きする（" + StoreContract + "）",
                ApiKeyEntrance.Write,
                ApiKeyFileState.OtherKeyEncrypted,
                Written
            ),
            Row(
                "write-locked-throws-and-keeps-file",
                "ロックで書けないときは入出力の例外を投げ、ファイルを変えない（VM は投げた保存を次の確定点でやり直す）",
                ApiKeyEntrance.Write,
                ApiKeyFileState.Locked,
                FailedOnLock
            ),
        ];

    /// <summary>消す入口</summary>
    private static IEnumerable<ScenarioRow<ApiKeyCase>> DeleteRows() =>
        [
            Row(
                "delete-missing-leaves-nothing",
                "ファイルが無ければ何もせず、保存されていないのまま",
                ApiKeyEntrance.Delete,
                ApiKeyFileState.Missing,
                Deleted
            ),
            Row(
                "delete-folder-missing-leaves-no-file",
                "保存先のフォルダが無くても投げず、ファイルは無いまま",
                ApiKeyEntrance.Delete,
                ApiKeyFileState.FolderMissing,
                Deleted
            ),
            Row(
                "delete-valid-removes-file-and-load-is-missing",
                "空文字を保存すると削除され、読むと保存されていないを返す",
                ApiKeyEntrance.Delete,
                ApiKeyFileState.Valid,
                Deleted
            ),
            Row(
                "delete-corrupt-removes-by-store-contract",
                "壊れたファイルも空文字の保存で消す（" + StoreContract + "）",
                ApiKeyEntrance.Delete,
                ApiKeyFileState.Corrupt,
                Deleted
            ),
            Row(
                "delete-other-key-removes-by-store-contract",
                "別の鍵の暗号も空文字の保存で消す（" + StoreContract + "）",
                ApiKeyEntrance.Delete,
                ApiKeyFileState.OtherKeyEncrypted,
                Deleted
            ),
            Row(
                "delete-locked-throws-and-keeps-file",
                "ロックで消せないときは入出力の例外を投げ、ファイルを変えない",
                ApiKeyEntrance.Delete,
                ApiKeyFileState.Locked,
                FailedOnLock
            ),
        ];

    /// <summary>接続設定の VM の保存のオン／オフ（読み書きは実のストア）</summary>
    private static IEnumerable<ScenarioRow<ApiKeyCase>> ToggleRows()
    {
        foreach (var entrance in new[] { ApiKeyEntrance.ToggleOff, ApiKeyEntrance.ToggleOffThenOn })
        {
            var name = entrance == ApiKeyEntrance.ToggleOff ? "toggle-off" : "toggle-off-then-on";
            var what =
                entrance == ApiKeyEntrance.ToggleOff
                    ? "保存をオフにする"
                    : "保存をオフにしてからオンへ戻す";

            yield return Row(
                $"{name}-missing-leaves-no-file",
                $"保存されていないときに{what}と、ファイルは無いまま",
                entrance,
                ApiKeyFileState.Missing,
                NoFileAfterToggle
            );
            yield return Row(
                $"{name}-folder-missing-leaves-no-file",
                $"保存先のフォルダが無いときに{what}と、ファイルは無いまま",
                entrance,
                ApiKeyFileState.FolderMissing,
                NoFileAfterToggle
            );
            yield return entrance == ApiKeyEntrance.ToggleOff
                ? Row(
                    $"{name}-valid-removes-file",
                    "読めたキーがあるときに保存をオフにすると、保存済みのキーを消す",
                    entrance,
                    ApiKeyFileState.Valid,
                    (o, _) =>
                    {
                        o.Unreadable.Should().BeFalse();
                        o.After.Should().BeNull();
                        o.ReadBack.Should().Be(SavedApiKey.Missing);
                    }
                )
                : Row(
                    $"{name}-valid-saves-key-again",
                    "読めたキーがあるときに保存をオフにしてからオンへ戻すと、同じキーを保存し直す",
                    entrance,
                    ApiKeyFileState.Valid,
                    (o, _) =>
                    {
                        o.Unreadable.Should().BeFalse();
                        o.ReadBack.Should()
                            .Be(
                                new SavedApiKey(
                                    SavedApiKeyStatus.Loaded,
                                    ApiKeyStoreHarness.SavedKey
                                )
                            );
                    }
                );

            foreach (
                var (state, label) in new[]
                {
                    (ApiKeyFileState.Corrupt, "corrupt"),
                    (ApiKeyFileState.OtherKeyEncrypted, "other-key"),
                    (ApiKeyFileState.Locked, "locked"),
                }
            )
            {
                yield return Row(
                    $"{name}-{label}-keeps-unreadable-file",
                    $"読めないキー（{state}）があるときに{what}も、暗号ファイルを上書きも削除もせず、読めないの印を残す",
                    entrance,
                    state,
                    (o, _) =>
                    {
                        o.Unreadable.Should().BeTrue();
                        o.Unchanged.Should().BeTrue();
                    }
                );
            }
        }
    }

    private static void UnreadableLoad(ApiKeyOutcome o, ApiKeyStoreHarness _)
    {
        o.Loaded.Should().Be(SavedApiKey.Unreadable);
        o.After.Should().NotBeNull("読めないファイルは残す");
        o.Unchanged.Should().BeTrue();
    }

    private static void Written(ApiKeyOutcome o, ApiKeyStoreHarness _)
    {
        o.Thrown.Should().BeNull();
        o.ReadBack.Should().Be(new SavedApiKey(SavedApiKeyStatus.Loaded, NewKey));
    }

    private static void Deleted(ApiKeyOutcome o, ApiKeyStoreHarness _)
    {
        o.Thrown.Should().BeNull();
        o.After.Should().BeNull();
        o.ReadBack.Should().Be(SavedApiKey.Missing);
    }

    /// <summary>ロックで書けない・消せない（VM が受け止める型＝入出力・アクセス拒否の例外を投げ、ファイルを変えない）</summary>
    private static void FailedOnLock(ApiKeyOutcome o, ApiKeyStoreHarness _)
    {
        o.Thrown.Should()
            .Match<Exception>(ex => ex is IOException || ex is UnauthorizedAccessException);
        o.Unchanged.Should().BeTrue();
        o.ReadBack.Should()
            .Be(new SavedApiKey(SavedApiKeyStatus.Loaded, ApiKeyStoreHarness.SavedKey));
    }

    private static void NoFileAfterToggle(ApiKeyOutcome o, ApiKeyStoreHarness _)
    {
        o.Thrown.Should().BeNull();
        o.Unreadable.Should().BeFalse();
        o.After.Should().BeNull();
        o.ReadBack.Should().Be(SavedApiKey.Missing);
    }

    private static ScenarioRow<ApiKeyCase> Row(
        string id,
        string description,
        ApiKeyEntrance entrance,
        ApiKeyFileState state,
        Action<ApiKeyOutcome, ApiKeyStoreHarness> expect,
        byte[]? corrupt = null
    ) => new(id, null, description, new ApiKeyCase(entrance, state, corrupt, expect));
}
