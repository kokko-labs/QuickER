using QuickER.Db.UI;
using QuickER.Extensibility;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Db.UI.ImportSync;

/// <summary>
/// 取込と同期のコマンドサービスのホストの偽物（図・未保存・世代・方言・プロバイダの解決を行ごとに置き、
/// 図を読んだ時点と置き換えた時点の世代・回数を記録する）
/// </summary>
/// <remarks>
/// 共有の <c>StubErDiagramHost</c> は図と未保存を構築後に変えられず、置き換えの回数と世代を記録しないので、
/// 表の組の中にこのダブルを置く（共有のダブルは変えない）。
/// </remarks>
internal sealed class ImportSyncHost : IErDiagramHost
{
    private int _sequence;

    /// <summary>現在の図</summary>
    public ErDiagram Diagram { get; set; } = new();

    /// <summary>未保存の変更があるか</summary>
    public bool Dirty { get; set; }

    /// <summary>図の世代（確認の中のフックで進めると「確認の間に変わった図」を作れる）</summary>
    public long Revision { get; set; }

    /// <summary>プロバイダの解決（既定は空＝初期選択・固定の方言は null）</summary>
    public DatabaseProviderRegistry Registry { get; set; } = new([]);

    /// <summary>対象の DBMS</summary>
    public string Target { get; set; } = "sqlserver";

    /// <summary>図を読んだ記録（読んだ順番の通し番号・その時点の世代・読んだ図）</summary>
    public List<(int Sequence, long Revision, ErDiagram Diagram)> Reads { get; } = [];

    /// <summary>図を置き換えた記録（置き換えた図・その時点の世代）</summary>
    public List<(ErDiagram Diagram, long Revision)> Replaced { get; } = [];

    /// <summary>次の通し番号（接続ダイアログの提示との前後を比べる）</summary>
    public int NextSequence() => ++_sequence;

    public bool IsEmpty => Diagram.Entities.Count == 0;

    public bool IsDirty => Dirty;

    public long DiagramRevision => Revision;

    public DatabaseProviderRegistry Providers => Registry;

    public string TargetDbms => Target;

    public event EventHandler<ColumnRenamedEventArgs>? ColumnRenamed
    {
        add { }
        remove { }
    }

    public event EventHandler? TargetDbmsChanged
    {
        add { }
        remove { }
    }

    public ErDiagram GetDiagram()
    {
        Reads.Add((NextSequence(), Revision, Diagram));
        return Diagram;
    }

    public void AutoArrangeNewDiagram() =>
        throw new NotSupportedException("取込と同期は自動整列を呼ばない");

    public (string Result, bool Success) ExecuteTool(string toolName, string argumentsJson) =>
        throw new NotSupportedException("取込と同期はツールを呼ばない");

    public void ReplaceQueries(IReadOnlyList<QueryDefinition> queries) =>
        throw new NotSupportedException("取込と同期はクエリだけを置き換えない");

    public void ReplaceDiagram(ErDiagram diagram) => Replaced.Add((diagram, Revision));
}

/// <summary>接続ダイアログの提示の偽物（答えを置き、呼ばれた引数と時点をすべて記録する。提示の最中の処理をフックで模す）</summary>
internal sealed class ImportSyncConnectionPresenter(ImportSyncHost host)
    : IDbConnectionDialogPresenter
{
    /// <summary>接続ダイアログの答え（null は取り消し）</summary>
    public DbConnectionDialogResult? Result { get; set; }

    /// <summary>提示の最中に走らせる処理（接続ダイアログを開いている間の図の変更を模す）</summary>
    public Action? OnShow { get; set; }

    /// <summary>呼ばれた引数と、答えを返した時点の通し番号</summary>
    public List<(
        DbConnectionDialogMode Mode,
        IDatabaseProvider? FixedProvider,
        string? Title,
        bool AllowSqliteFileCreation,
        int ReturnedAt
    )> Calls { get; } = [];

    public DbConnectionDialogResult? Show(
        DbConnectionDialogMode mode,
        IDatabaseProvider? fixedProvider = null,
        string? title = null,
        bool allowSqliteFileCreation = false
    )
    {
        OnShow?.Invoke();
        Calls.Add((mode, fixedProvider, title, allowSqliteFileCreation, host.NextSequence()));
        return Result;
    }
}

/// <summary>同期のダイアログの提示の偽物（呼ばれた引数を記録する）</summary>
internal sealed class ImportSyncSchemaSyncPresenter : ISchemaSyncDialogPresenter
{
    /// <summary>呼ばれた引数</summary>
    public List<(
        IDatabaseProvider Provider,
        DbConnectionSettings Settings,
        IReadOnlyList<Entity> Entities,
        IReadOnlyList<Relationship> Relationships
    )> Calls { get; } = [];

    public void Show(
        IDatabaseProvider provider,
        DbConnectionSettings settings,
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships
    ) => Calls.Add((provider, settings, entities, relationships));
}

/// <summary>
/// 方言の宣言・型・同期スクリプトの組み立ては本物のプロバイダへ委ね、スキーマ取込と同期の実行器だけを偽物へ差し替えるプロバイダ
/// </summary>
/// <remarks>
/// 表の組は同期の実行器を必ず偽物にする（本物の実行器へ行くと、確認を承認した行で相対パスの shop.db に実 SQLite が走る）。
/// 同期スクリプトを組み立てた回数も数える。
/// </remarks>
internal sealed class ImportSyncProvider(
    IDatabaseProvider inner,
    ImportSyncImporter importer,
    ImportSyncExecutor executor
) : IDatabaseProvider
{
    private readonly CountingScriptBuilder _builder = new(inner.SyncScriptBuilder);

    /// <summary>取込のプロバイダの名前（同期の行の方言でないもの）</summary>
    public const string ImportOnlyName = "fakeimport";

    /// <summary>スキーマ取込</summary>
    public ImportSyncImporter Importer => importer;

    /// <summary>同期の実行器</summary>
    public ImportSyncExecutor Executor => executor;

    /// <summary>同期スクリプトを組み立てた回数</summary>
    public int BuildCount => _builder.Count;

    public string Name => inner.Name;

    public string DisplayName => inner.DisplayName;

    public int? DefaultPort => inner.DefaultPort;

    public ISchemaImporter SchemaImporter => importer;

    public IColumnTypeMapper TypeMapper => inner.TypeMapper;

    public ITypeCatalog TypeCatalog => inner.TypeCatalog;

    public ISyncScriptBuilder SyncScriptBuilder => _builder;

    public SyncDialectCapabilities SyncCapabilities => inner.SyncCapabilities;

    public ISchemaSyncExecutor SyncExecutor => executor;

    public IDdlGenerator DdlGenerator => inner.DdlGenerator;

    // 実接続はしない（偽のインポータが接続文字列を無視する）ため固定値を返す
    public string BuildConnectionString(DbConnectionSettings settings) => "fake";

    /// <summary>本物の組み立てへ委ねつつ回数を数える</summary>
    private sealed class CountingScriptBuilder(ISyncScriptBuilder inner) : ISyncScriptBuilder
    {
        public int Count { get; private set; }

        public string Build(SyncPlan plan)
        {
            Count++;
            return inner.Build(plan);
        }
    }
}

/// <summary>取込だけを持つ方言（DB 取込のコマンドサービスの行の取込先。宣言は持たない）</summary>
internal sealed class ImportOnlyDialect : IDatabaseProvider
{
    public string Name => ImportSyncProvider.ImportOnlyName;

    public string DisplayName => "Fake Import";

    public int? DefaultPort => null;

    public ISchemaImporter SchemaImporter =>
        throw new NotSupportedException("包み（ImportSyncProvider）の取込を使う");

    public IColumnTypeMapper TypeMapper => null!;

    public ITypeCatalog TypeCatalog => null!;

    public ISyncScriptBuilder SyncScriptBuilder => null!;

    public SyncDialectCapabilities SyncCapabilities => null!;

    public ISchemaSyncExecutor SyncExecutor => null!;

    public IDdlGenerator DdlGenerator => null!;

    public string BuildConnectionString(DbConnectionSettings settings) => "fake";
}

/// <summary>スキーマ取込の偽物（結果を差し替えられる・例外・止める・回数・タイムアウトの記録）</summary>
internal sealed class ImportSyncImporter(SchemaImportResult result) : ISchemaImporter
{
    private readonly List<TaskCompletionSource<SchemaImportResult>> _gates = [];

    /// <summary>次の取込で返す結果（同期の実行の後の「適用済みの live」を再現するために差し替える）</summary>
    public SchemaImportResult Result { get; set; } = result;

    /// <summary>次の取込の振る舞い（null なら <see cref="Result"/> を返す）</summary>
    public Func<Task<SchemaImportResult>>? Behavior { get; set; }

    /// <summary>呼ばれた回数</summary>
    public int Calls { get; private set; }

    /// <summary>直近の取込のタイムアウト</summary>
    public int? LastCommandTimeoutSeconds { get; private set; }

    /// <summary>直近の取込が例外で失敗したか</summary>
    public bool LastFailed { get; private set; }

    /// <summary>止めた取込の門（行の終わりに組が開ける）</summary>
    public IReadOnlyList<TaskCompletionSource<SchemaImportResult>> Gates => _gates;

    public async Task<SchemaImportResult> ImportAsync(
        string connectionString,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        Calls++;
        LastCommandTimeoutSeconds = commandTimeoutSeconds;
        LastFailed = false;

        try
        {
            return Behavior is { } behavior ? await behavior().ConfigureAwait(true) : Result;
        }
        catch
        {
            LastFailed = true;
            throw;
        }
    }

    /// <summary>次の取込から例外で失敗させる</summary>
    public void Fail(string message) =>
        Behavior = () => throw new InvalidOperationException(message);

    /// <summary>次の取込を門で止める（開けると <paramref name="released"/> を返す）</summary>
    public TaskCompletionSource<SchemaImportResult> Hold()
    {
        var gate = new TaskCompletionSource<SchemaImportResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _gates.Add(gate);
        Behavior = () => gate.Task;
        return gate;
    }
}

/// <summary>同期の実行器の結果</summary>
internal enum ExecutorOutcome
{
    /// <summary>まだ実行していない</summary>
    None,

    /// <summary>コミットした</summary>
    Committed,

    /// <summary>失敗を返した</summary>
    Failed,

    /// <summary>例外で抜けた</summary>
    Threw,
}

/// <summary>同期の実行器の偽物（既定は即座にコミット。失敗・例外・止めるを行ごとに選ぶ。実行の副作用＝部分適用をコールバックで再現できる）</summary>
internal sealed class ImportSyncExecutor : ISchemaSyncExecutor
{
    private readonly List<(TaskCompletionSource Started, TaskCompletionSource Release)> _gates = [];
    private Func<Task<SchemaSyncResult>> _behavior = () =>
        Task.FromResult(new SchemaSyncResult { Committed = true });

    /// <summary>実行された回数</summary>
    public int Calls { get; private set; }

    /// <summary>直近の実行の結果</summary>
    public ExecutorOutcome LastOutcome { get; private set; }

    /// <summary>止めた実行の門（行の終わりに組が開ける）</summary>
    public IReadOnlyList<(TaskCompletionSource Started, TaskCompletionSource Release)> Gates =>
        _gates;

    public async Task<SchemaSyncResult> ExecuteAsync(
        DbConnectionSettings settings,
        string script,
        CancellationToken ct = default
    )
    {
        Calls++;

        try
        {
            var result = await _behavior().ConfigureAwait(true);
            LastOutcome = result.Committed ? ExecutorOutcome.Committed : ExecutorOutcome.Failed;
            return result;
        }
        catch
        {
            LastOutcome = ExecutorOutcome.Threw;
            throw;
        }
    }

    /// <summary>失敗（コミットしない）を返す。実行の副作用を <paramref name="onExecute"/> で再現する</summary>
    public void Fail(string error, Action? onExecute = null) =>
        _behavior = () =>
        {
            onExecute?.Invoke();
            return Task.FromResult(new SchemaSyncResult { Committed = false, Error = error });
        };

    /// <summary>例外で抜ける。実行の副作用を <paramref name="onExecute"/> で再現する</summary>
    public void Throw(string message, Action? onExecute = null) =>
        _behavior = () =>
        {
            onExecute?.Invoke();
            throw new InvalidOperationException(message);
        };

    /// <summary>実行の開始を知らせ、開けられるまで止める（実行の最中を作る門）</summary>
    /// <remarks>
    /// 観測を「実行の最中」へ確実に置くための門。コールバックを継続で走らせる形だと、
    /// <c>AsyncRelayCommand</c> が <c>ExecutionTask</c> を代入するのと継続の実行が競合し、観測が実行前に見えることがある。
    /// </remarks>
    public (TaskCompletionSource Started, TaskCompletionSource Release) Hold()
    {
        var gate = (
            Started: new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            Release: new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );
        _gates.Add(gate);
        _behavior = async () =>
        {
            gate.Started.TrySetResult();
            await gate.Release.Task.ConfigureAwait(false);
            return new SchemaSyncResult { Committed = true };
        };
        return gate;
    }
}
