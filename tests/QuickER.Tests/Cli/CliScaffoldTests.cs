using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Cli.Resources;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using Xunit;

namespace QuickER.Tests.Cli;

/// <summary>
/// CLI の <c>scaffold</c>（DB 直結 → コード生成）の取込と検証順序、および設定の出力先の導出を検証する。
/// </summary>
/// <remarks>
/// 取込そのものは偽のスキーマインポータで置き換え、実 DB に依らずに
/// 「中断と失敗の区別」「組み立てる図の対象 DBMS」を確かめる。
/// </remarks>
public class CliScaffoldTests
{
    /// <summary>
    /// 利用者のトークンが取り消された状態の中断は、取込の失敗として表示せず伝える（呼び出し側が 130 を返す）
    /// </summary>
    [Fact(DisplayName = "scaffold の取込は利用者の中断を失敗にせず伝える")]
    public async Task ImportDiagram_UserCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var provider = new FakeImportProvider((_, ct) => throw new OperationCanceledException(ct));
        var stderr = new StringWriter();

        var act = () => CliApp.ImportDiagramAsync(provider, "Data Source=x", 60, stderr, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stderr.ToString().Should().BeEmpty("中断は取込の失敗として表示しない");
    }

    /// <summary>
    /// 利用者のトークンが取り消されていない <see cref="OperationCanceledException"/>（ドライバや内部の時間切れ）は、
    /// 中断に化けさせず取込の失敗（null と文言）として扱う
    /// </summary>
    [Fact(
        DisplayName = "scaffold の取込は利用者の中断でない OperationCanceledException を失敗として扱う"
    )]
    public async Task ImportDiagram_CancellationWithoutUserToken_IsReportedAsFailure()
    {
        var provider = new FakeImportProvider(
            (_, _) => throw new TaskCanceledException("driver timeout")
        );
        var stderr = new StringWriter();

        var diagram = await CliApp.ImportDiagramAsync(
            provider,
            "Data Source=x",
            60,
            stderr,
            CancellationToken.None
        );

        diagram.Should().BeNull();
        stderr
            .ToString()
            .Should()
            .Contain(string.Format(Strings.Cli_SchemaImportFailed, "driver timeout"));
    }

    /// <summary>取り込んだ図の対象 DBMS は、取込に使ったプロバイダの方言になる（reverse と同じ）</summary>
    [Fact(DisplayName = "scaffold が組み立てる図の対象 DBMS は取込のプロバイダの方言")]
    public async Task ImportDiagram_SetsTargetDbmsToProvider()
    {
        var entity = new Entity { TableName = "Customer" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
            }
        );
        var provider = new FakeImportProvider(
            (_, _) => Task.FromResult(new SchemaImportResult { Entities = [entity] })
        );

        var diagram = await CliApp.ImportDiagramAsync(
            provider,
            "Data Source=x",
            60,
            new StringWriter(),
            CancellationToken.None
        );

        diagram.Should().NotBeNull();
        diagram!.TargetDbms.Should().Be(SqliteProvider.ProviderName);
        diagram.Entities.Should().ContainSingle();
    }

    /// <summary>
    /// 設定ファイルの誤りは、DB への取込より前に報告する（取込を終えてから設定の誤りに気づかない）
    /// </summary>
    /// <remarks>
    /// 接続は必ず失敗する（存在しないフォルダの読み取り専用 DB）。順序が逆だと取込の失敗で先に止まり、
    /// 設定ファイルの不在は報告されない。
    /// </remarks>
    [Fact(DisplayName = "scaffold は設定ファイルの誤りを DB 取込より前に報告する")]
    public async Task Scaffold_InvalidConfig_IsReportedBeforeImport()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERCliScaffold",
            Guid.NewGuid().ToString("N")
        );
        var missingConfig = Path.Combine(root, "missing-quicker.json");
        var database = Path.Combine(root, "no-such-folder", "x.db");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await CliApp.InvokeAsync(
            [
                "scaffold",
                "--provider",
                "sqlite",
                "--connection",
                $"Data Source={database};Mode=ReadOnly",
                "--out",
                Path.Combine(root, "out"),
                "--config",
                missingConfig,
            ],
            stdout,
            stderr
        );

        exit.Should().Be(1);
        stderr
            .ToString()
            .Should()
            .Contain(string.Format(Strings.Cli_ConfigFileNotFound, missingConfig));
        var importFailedPrefix = Strings.Cli_SchemaImportFailed.Split("{0}")[0];
        stderr.ToString().Should().NotContain(importFailedPrefix, "取込は設定の検証より後");
    }

    /// <summary>
    /// OutputPath からの導出は、値が null の別綴りキーを残さず正準キー 1 つで設定する
    /// </summary>
    [Fact(DisplayName = "OutputFileName の導出は綴り違いの二重キーを残さない")]
    public void DeriveOutputFileName_ReplacesNullAlternateSpelling()
    {
        var node = new JsonObject { ["outputFileName"] = null, ["OutputPath"] = "out/Shop.g.cs" };

        GenerationConfigLoader.DeriveOutputFileName(node);

        var keys = new List<string>();

        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, "OutputFileName", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(pair.Key);
            }
        }

        keys.Should().Equal("OutputFileName");
        node["OutputFileName"]!.GetValue<string>().Should().Be("Shop.g.cs");
    }

    /// <summary>取込だけを差し替えた SQLite プロバイダ（他のメンバーは実物へ委譲する）</summary>
    private sealed class FakeImportProvider(
        Func<string, CancellationToken, Task<SchemaImportResult>> import
    ) : IDatabaseProvider
    {
        private readonly SqliteProvider _inner = new();

        public string Name => _inner.Name;

        public string DisplayName => _inner.DisplayName;

        public int? DefaultPort => _inner.DefaultPort;

        public ISchemaImporter SchemaImporter { get; } = new FakeSchemaImporter(import);

        public IColumnTypeMapper TypeMapper => _inner.TypeMapper;

        public ITypeCatalog TypeCatalog => _inner.TypeCatalog;

        public ISyncScriptBuilder SyncScriptBuilder => _inner.SyncScriptBuilder;

        public SyncDialectCapabilities SyncCapabilities => _inner.SyncCapabilities;

        public ISchemaSyncExecutor SyncExecutor => _inner.SyncExecutor;

        public IDdlGenerator DdlGenerator => _inner.DdlGenerator;

        public string BuildConnectionString(DbConnectionSettings settings) =>
            _inner.BuildConnectionString(settings);
    }

    /// <summary>与えた処理で取込を行う偽のスキーマインポータ</summary>
    private sealed class FakeSchemaImporter(
        Func<string, CancellationToken, Task<SchemaImportResult>> import
    ) : ISchemaImporter
    {
        public Task<SchemaImportResult> ImportAsync(
            string connectionString,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken = default
        ) => import(connectionString, cancellationToken);
    }
}
