using System.IO;
using QuickER.Db.UI;
using QuickER.Gui.Abstractions;
using QuickER.Provider;

namespace QuickER.Tests.Db.UI.ConnectionDialog;

/// <summary>
/// 方言の宣言（名前・表示名・既定のポート）は本物のプロバイダへ委ね、接続テストのスキーマ取込だけを偽物へ差し替えるプロバイダ
/// </summary>
/// <remarks>
/// 接続ダイアログの表は、どの方言でもこの包みを通す（本物のインポータへ行くと、接続テストが localhost の実 DB へ繋ぎに行く）。
/// 同期の実行器はこのダイアログが使わないので、呼ばれたら投げる。
/// </remarks>
internal sealed class ConnectionDialogProvider(IDatabaseProvider inner) : IDatabaseProvider
{
    /// <summary>接続テストのスキーマ取込（振る舞いを行ごとに差し替える）</summary>
    public ConnectionDialogImporter Importer { get; } = new();

    public string Name => inner.Name;

    public string DisplayName => inner.DisplayName;

    public int? DefaultPort => inner.DefaultPort;

    public ISchemaImporter SchemaImporter => Importer;

    public IColumnTypeMapper TypeMapper => inner.TypeMapper;

    public ITypeCatalog TypeCatalog => inner.TypeCatalog;

    public ISyncScriptBuilder SyncScriptBuilder => inner.SyncScriptBuilder;

    public SyncDialectCapabilities SyncCapabilities => inner.SyncCapabilities;

    public ISchemaSyncExecutor SyncExecutor =>
        throw new NotSupportedException("接続ダイアログは同期の実行器を使わない");

    public IDdlGenerator DdlGenerator => inner.DdlGenerator;

    // 実接続はしない（偽のインポータが接続文字列を無視する）ため固定値を返す
    public string BuildConnectionString(DbConnectionSettings settings)
    {
        LastSettings = settings;
        return "fake";
    }

    /// <summary>最後に接続文字列を組み立てたときの接続設定（接続テストが何の値で流れたかを見る）</summary>
    public DbConnectionSettings? LastSettings { get; private set; }
}

/// <summary>接続テストのスキーマ取込の偽物（既定は空の結果を即座に返す。止める・失敗させる・テーブル数を返すを行ごとに選ぶ）</summary>
internal sealed class ConnectionDialogImporter : ISchemaImporter
{
    private readonly List<ImportGate> _gates = [];

    /// <summary>呼ばれた回数</summary>
    public int Calls { get; private set; }

    /// <summary>次の取込の振る舞い</summary>
    public Func<Task<SchemaImportResult>> Behavior { get; set; } =
        () => Task.FromResult(new SchemaImportResult());

    /// <summary>止めた取込の門（行の終わりに組が開ける）</summary>
    public IReadOnlyList<ImportGate> Gates => _gates;

    public Task<SchemaImportResult> ImportAsync(
        string connectionString,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    )
    {
        Calls++;
        return Behavior();
    }

    /// <summary>次の取込を門で止める（始まったことを知らせ、開けられるまで待つ）</summary>
    public ImportGate Hold()
    {
        var gate = new ImportGate();
        _gates.Add(gate);
        Behavior = async () =>
        {
            gate.Started.TrySetResult();
            await gate.Release.Task.ConfigureAwait(false);
            return new SchemaImportResult();
        };
        return gate;
    }

    /// <summary>次の取込をテーブル数 <paramref name="tables"/> の結果で成功させる</summary>
    public void Succeed(int tables) =>
        Behavior = () =>
            Task.FromResult(
                new SchemaImportResult
                {
                    Entities = Enumerable
                        .Range(1, tables)
                        .Select(i => new QuickER.Model.Entity { TableName = "t" + i })
                        .ToList(),
                }
            );

    /// <summary>次の取込を例外で失敗させる</summary>
    public void Fail(Exception exception) =>
        Behavior = () => Task.FromException<SchemaImportResult>(exception);
}

/// <summary>取込を止める門</summary>
internal sealed class ImportGate
{
    /// <summary>取込が始まった</summary>
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>開けると取込が終わる</summary>
    public TaskCompletionSource Release { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>開く・保存のファイルダイアログの答えを行の途中で差し替えられる偽物</summary>
internal sealed class ConnectionDialogFiles : IFileDialogService
{
    /// <summary>開くダイアログの答え（null はキャンセル）</summary>
    public FileDialogResult? OpenResult { get; set; }

    /// <summary>保存ダイアログの答え（null はキャンセル）</summary>
    public FileDialogResult? SaveResult { get; set; }

    public FileDialogResult? PickOpenFile(string filter) => OpenResult;

    public FileDialogResult? PickSaveFile(
        string filter,
        string defaultExt,
        string? initialFileName = null,
        string? initialDirectory = null
    ) => SaveResult;

    public string? PickFolder(string title, string? initialDirectory = null) => null;
}

/// <summary>
/// 本物のストアのまま、頼まれたときだけ次の読み取りの直前に connections.json を排他で掴むストア
/// （書き込みは成功し、直後の読み直しだけが読めない状況を作る）
/// </summary>
/// <remarks>
/// 保存・名前の変更・削除は、書き込みと直後の読み直しの間に呼び出し側へ戻らないので、外から掴む隙が無い。
/// 掴むのは本物のファイルで、読み取りの結果は本物のストアの分類（Unavailable）のまま返る。
/// 掴んだハンドルは <see cref="ReleaseLock"/> で放す（組が行の終わりの見張りの前に放す）。
/// </remarks>
internal sealed class ConnectionDialogStore(string folder, bool useDpapi)
    : SqlConnectionProfileStore(folder, useDpapi)
{
    private bool _lockOnNextRead;
    private FileStream? _lock;

    /// <summary>次の <see cref="Read"/> の直前に connections.json を掴むよう頼む</summary>
    public void LockOnNextRead() => _lockOnNextRead = true;

    /// <summary>掴んだ connections.json を放す（掴んでいなければ何もしない）</summary>
    public void ReleaseLock()
    {
        _lock?.Dispose();
        _lock = null;
    }

    /// <inheritdoc />
    public override ConnectionProfilesRead Read()
    {
        if (_lockOnNextRead)
        {
            _lockOnNextRead = false;
            _lock = new FileStream(ConnectionsPath, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        return base.Read();
    }
}
