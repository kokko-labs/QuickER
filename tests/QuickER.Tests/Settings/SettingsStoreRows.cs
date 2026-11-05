using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using QuickER.Settings;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Settings;

/// <summary>設定の操作列の表の手の種類</summary>
internal enum SettingsOpKind
{
    /// <summary>設定ファイルへ文字列をそのまま書く（外部の書き込み）</summary>
    WriteFile,

    /// <summary><see cref="JsonSettingsStore{TSettings}.Load"/></summary>
    Load,

    /// <summary><see cref="JsonSettingsStore{TSettings}.Save"/>（例外も控える）</summary>
    Save,

    /// <summary><see cref="JsonSettingsStore{TSettings}.SaveTo"/>（一時フォルダの別のファイルへ）</summary>
    SaveTo,

    /// <summary><see cref="JsonSettingsStore{TSettings}.TryLoadFrom"/>（一時フォルダのファイルか設定ファイル）</summary>
    TryLoadFrom,

    /// <summary>設定ファイルを別のハンドルで FileShare.None のまま開く（読めない状態を作る）</summary>
    LockFile,

    /// <summary><see cref="LockFile"/> を閉じる</summary>
    UnlockFile,

    /// <summary>退避先（.corrupt）にフォルダを置く（退避の失敗を起こす）</summary>
    BlockBackup,

    /// <summary><see cref="BlockBackup"/> のフォルダを消す</summary>
    UnblockBackup,
}

/// <summary>手の前の設定ファイルの状態（網羅の計数に使う）</summary>
internal enum SettingsFileState
{
    /// <summary>ファイルが無い</summary>
    Missing,

    /// <summary>JSON として読める</summary>
    Healthy,

    /// <summary>JSON として読めない</summary>
    Corrupt,

    /// <summary>別のハンドルが排他で開いている</summary>
    Locked,
}

/// <summary>1 手の後の観測</summary>
/// <typeparam name="T">設定の型</typeparam>
/// <remarks><see cref="Memo"/> は 1 行の手の列で共有する控え（前の手で控えた値を後の手の表明で比べる）。</remarks>
internal sealed record SettingsObservation<T>(
    JsonSettingsStore<T> Store,
    string Folder,
    T? Loaded,
    bool? Saved,
    Exception? Thrown,
    T? TryLoaded,
    Dictionary<string, object?> Memo
)
    where T : class, new()
{
    /// <summary>設定ファイルの文字列（無ければ null。開けなければ例外）</summary>
    public string? FileText =>
        File.Exists(Store.SettingsPath) ? File.ReadAllText(Store.SettingsPath) : null;

    /// <summary>設定ファイルを JSON のオブジェクトとして読む</summary>
    public JsonObject FileJson => JsonNode.Parse(File.ReadAllText(Store.SettingsPath))!.AsObject();

    /// <summary>退避先のファイルがあるか</summary>
    public bool BackupExists => File.Exists(Store.CorruptBackupPath);

    /// <summary>退避先の文字列</summary>
    public string BackupText => File.ReadAllText(Store.CorruptBackupPath);

    /// <summary>一時フォルダの中のファイルのパス</summary>
    public string PathOf(string name) => Path.Combine(Folder, name);
}

/// <summary>設定の操作列の 1 手</summary>
/// <typeparam name="T">設定の型</typeparam>
internal sealed record SettingsStep<T>(SettingsOpKind Kind)
    where T : class, new()
{
    /// <summary>WriteFile の文字列</summary>
    public string? Text { get; init; }

    /// <summary>SaveTo・TryLoadFrom のファイル名（一時フォルダの中。TryLoadFrom で null なら設定ファイル）</summary>
    public string? Name { get; init; }

    /// <summary>Save・SaveTo で書く設定を作る（null なら直前に読んだ設定を書く）</summary>
    public Func<T>? Make { get; init; }

    /// <summary>Save の前に直前に読んだ設定を書き換える（Make が null のときだけ）</summary>
    public Action<T>? Mutate { get; init; }

    /// <summary>元のテストの表明</summary>
    public Action<SettingsObservation<T>>? Expect { get; init; }
}

/// <summary>設定の操作列のシナリオ（設定の型を隠して表に載せる）</summary>
/// <param name="SettingsType">設定の型の名前</param>
/// <param name="Run">一時フォルダで手の列を流し、製品の手ごとに（手の種類・手の前のファイルの状態）を返す</param>
internal sealed record SettingsScenario(
    string SettingsType,
    Func<string, IReadOnlyList<(SettingsOpKind Op, SettingsFileState State)>> Run
);

/// <summary>設定の操作列を流す</summary>
internal static class SettingsStoreRunner
{
    /// <summary>設定のファイル名（元のテストと同じ）</summary>
    public const string FileName = "probe-settings.json";

    /// <summary>手の列を流す（表明の失敗はそのまま投げる）</summary>
    public static IReadOnlyList<(SettingsOpKind Op, SettingsFileState State)> Run<T>(
        string folder,
        IReadOnlyList<SettingsStep<T>> steps
    )
        where T : class, new()
    {
        var store = new JsonSettingsStore<T>(FileName, folder);
        var observed = new List<(SettingsOpKind, SettingsFileState)>();
        var memo = new Dictionary<string, object?>(StringComparer.Ordinal);
        FileStream? lockStream = null;
        T? lastLoaded = null;

        try
        {
            foreach (var step in steps)
            {
                var state = StateOf(store, lockStream is not null);
                bool? saved = null;
                Exception? thrown = null;
                T? loaded = null;
                T? tryLoaded = null;

                switch (step.Kind)
                {
                    case SettingsOpKind.WriteFile:
                        File.WriteAllText(store.SettingsPath, step.Text!);
                        break;
                    case SettingsOpKind.Load:
                        loaded = lastLoaded = store.Load();
                        break;
                    case SettingsOpKind.Save:
                        var toSave = step.Make?.Invoke() ?? Mutated(lastLoaded, step);

                        try
                        {
                            saved = store.Save(toSave);
                        }
                        catch (Exception ex)
                        {
                            thrown = ex;
                        }

                        break;
                    case SettingsOpKind.SaveTo:
                        store.SaveTo(Path.Combine(folder, step.Name!), step.Make!.Invoke());
                        break;
                    case SettingsOpKind.TryLoadFrom:
                        tryLoaded = store.TryLoadFrom(
                            step.Name is null ? store.SettingsPath : Path.Combine(folder, step.Name)
                        );
                        break;
                    case SettingsOpKind.LockFile:
                        lockStream = new FileStream(
                            store.SettingsPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.None
                        );
                        break;
                    case SettingsOpKind.UnlockFile:
                        lockStream?.Dispose();
                        lockStream = null;
                        break;
                    case SettingsOpKind.BlockBackup:
                        Directory.CreateDirectory(store.CorruptBackupPath);
                        break;
                    case SettingsOpKind.UnblockBackup:
                        Directory.Delete(store.CorruptBackupPath);
                        break;
                }

                if (
                    step.Kind
                    is SettingsOpKind.Load
                        or SettingsOpKind.Save
                        or SettingsOpKind.SaveTo
                        or SettingsOpKind.TryLoadFrom
                )
                {
                    observed.Add((step.Kind, state));
                }

                step.Expect?.Invoke(
                    new SettingsObservation<T>(
                        store,
                        folder,
                        loaded,
                        saved,
                        thrown,
                        tryLoaded,
                        memo
                    )
                );
            }
        }
        finally
        {
            lockStream?.Dispose();
        }

        return observed;
    }

    private static T Mutated<T>(T? lastLoaded, SettingsStep<T> step)
        where T : class, new()
    {
        var settings =
            lastLoaded
            ?? throw new InvalidOperationException(
                "Save の前に Load が無い（Make か Load が要る）"
            );
        step.Mutate?.Invoke(settings);
        return settings;
    }

    private static SettingsFileState StateOf<T>(JsonSettingsStore<T> store, bool locked)
        where T : class, new()
    {
        if (locked)
        {
            return SettingsFileState.Locked;
        }

        if (!File.Exists(store.SettingsPath))
        {
            return SettingsFileState.Missing;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(store.SettingsPath));
            return SettingsFileState.Healthy;
        }
        catch (JsonException)
        {
            return SettingsFileState.Corrupt;
        }
    }
}

/// <summary>
/// 設定 JSON ストアの操作列の表（未知のキーの引き継ぎ・読めないときは書かない・壊れたファイルの退避・null と PascalCase の読み）
/// </summary>
/// <remarks>
/// 行は元の単発のテストの手の順と表明をそのまま持つ。主題ごとの行は <c>SettingsStoreRows.{主題}.cs</c> の private メソッドが返し、
/// <see cref="AllRows"/> に足す。設定の型（<see cref="ProbeSettings"/> ほか）はこのクラスに置く。
/// </remarks>
internal static partial class SettingsStoreRows
{
    /// <summary>表</summary>
    public static ScenarioTable<SettingsScenario> Table => new(EngineIds.DocumentFile, AllRows());

    private static IEnumerable<ScenarioRow<SettingsScenario>> AllRows() =>
        [.. UnavailableRows(), .. RecoveryRows(), .. UnknownKeyRows(), .. NullAndCaseRows()];

    /// <summary>行を作る</summary>
    private static ScenarioRow<SettingsScenario> Row<T>(
        string id,
        string description,
        params SettingsStep<T>[] steps
    )
        where T : class, new() =>
        new(
            id,
            null,
            description,
            new SettingsScenario(typeof(T).Name, folder => SettingsStoreRunner.Run(folder, steps))
        );

    /// <summary>手を作る</summary>
    private static SettingsStep<T> Step<T>(SettingsOpKind kind)
        where T : class, new() => new(kind);

    /// <summary>言語だけを持つ設定（元のテストの ProbeSettings）</summary>
    public sealed class ProbeSettings
    {
        public string Language { get; set; } = string.Empty;
    }

    /// <summary>入れ子のオブジェクトと辞書を持つ設定（元のテストの NestedProbeSettings）</summary>
    public sealed class NestedProbeSettings
    {
        public string Language { get; set; } = string.Empty;

        public ViewProbeSettings View { get; set; } = new();

        public Dictionary<string, string> Tags { get; set; } = new();
    }

    /// <summary>入れ子の設定（元のテストの ViewProbeSettings）</summary>
    public sealed class ViewProbeSettings
    {
        public bool IsCompact { get; set; }
    }

    /// <summary>null を許す・許さないプロパティを持つ設定（元のテストの NullProbeSettings）</summary>
    public sealed class NullProbeSettings
    {
        public string Language { get; set; } = "en";

        public string? Note { get; set; } = "note";

        public List<string> Dialects { get; set; } = ["sqlserver"];

        public ViewProbeSettings View { get; set; } = new() { IsCompact = true };
    }
}
