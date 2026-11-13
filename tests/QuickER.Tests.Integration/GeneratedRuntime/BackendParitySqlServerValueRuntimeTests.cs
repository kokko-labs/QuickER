using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using VP = QuickER.Tests.GeneratedBackendParitySqlServerFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の値の往復（SQL Server の系統）：値の形をそろえた値を、QuickER 版 SQL Server と EF Core 版 SQL Server の
/// 書く経路（InsertAsync・UpdateAsync・SaveAsync の挿入と更新・BulkInsertAsync）で書いて読み戻し、docs の約束の写し
/// （<see cref="BackendParitySqlServerValueExpectations"/>）と、2 つの実装先どうしの読み戻しを比べる。
/// </summary>
/// <remarks>
/// <para>
/// 図は <c>BackendParitySqlServerFixture</c>（値オブジェクトなし。範囲の外の値もパラメータの束縛と SqlBulkCopy まで届く）。
/// DDL は SQL Server が作れる図（<c>BuildForSqlServerDdl</c>）から作る。読む経路は GetById・Include の無い Query と、最後に 1 回の
/// customers からの Include（QuickER 版は FOR JSON）。除外する無制限バイナリ列は生の SELECT で読む。
/// </para>
/// <para>
/// 断られた書き込みは、断られたことだけを比べる（例外の型と文は経路で違い、比べない）。断られたら何も残らないこと（BulkInsert は
/// 1 行でも断られればどの行も残らないこと・更新は元の値のままであること）を確かめる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
[RuntimeCell(RuntimeFeatures.BackendParity, RuntimeTarget.AdoSqlServer)]
[RuntimeCell(RuntimeFeatures.BackendParity, RuntimeTarget.EfCoreSqlServer)]
public sealed class BackendParitySqlServerValueRuntimeTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 乱数の種（<c>QUICKER_PARITY_SQLSERVER_VALUE_SEED</c> が 1 つの種、<c>QUICKER_PARITY_SQLSERVER_VALUE_SEEDS</c> が数・既定 8・
    /// 夜間 200・1 から）
    /// </summary>
    public static SeedSettings Seeds { get; } =
        new(
            "QUICKER_PARITY_SQLSERVER_VALUE_SEEDS",
            8,
            200,
            singleVariable: "QUICKER_PARITY_SQLSERVER_VALUE_SEED"
        );

    /// <summary>乱数の種の範囲</summary>
    private const string RandomScope = BackendParitySqlServerValueDimensions.Scope;

    /// <summary>今わかっている違い（範囲は <see cref="RandomScope"/>）</summary>
    private static readonly KnownFailureSet KnownSet = new(RandomScope, []);

    /// <summary>違いのあった種をいくつまで流して報告するか</summary>
    private const int MaxFailedSeeds = 3;

    /// <summary>決まった形の網羅の失敗をいくつまで並べるか</summary>
    private const int MaxListedProblems = 80;

    /// <summary>顧客（value_probes の外部キーの親・Include の根）の主キー</summary>
    private const int CustomerId = 1;

    [Fact(
        DisplayName = "[Parity/SqlServer] 値の往復の決まった形は、QuickER 版と EF Core 版のどの書く経路でも docs の約束どおりに読み戻せる"
    )]
    public async Task FixedCases_MatchDocsOnBothBackends()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var scenario = BackendParitySqlServerValueGenerator.Fixed();
        var coverage = new CoverageCounter(BackendParitySqlServerValueDimensions.Table);
        var problems = new List<string>();
        var observed = new Dictionary<SqlServerValueBackend, Dictionary<string, string>>();
        var watch = Stopwatch.StartNew();

        foreach (var backend in Enum.GetValues<SqlServerValueBackend>())
        {
            await using var context = await ValueContext.CreateAsync(fixture, backend);
            var results = new Dictionary<string, string>(StringComparer.Ordinal);
            await RunAsync(context, scenario, coverage, results, problems);
            await CheckIncludeAsync(context, scenario, results, problems);
            observed[backend] = results;
        }

        CompareBackends(observed, problems);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"値の往復の決まった形：書き込み {scenario.Writes.Count} 回 × 実装先 2 つ・{watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} 秒"
        );

        // 網羅の宣言の誤りは値の結果より先に赤にする
        var report = coverage.Report();
        report.WriteSummary();
        report.AssertGaps(
            BackendParitySqlServerValueDimensions.Gaps,
            BackendParitySqlServerValueDimensions.Scope
        );
        problems
            .Should()
            .BeEmpty(
                $"{problems.Count} 件の食い違い（先頭 {MaxListedProblems} 件）:\n  "
                    + string.Join("\n  ", problems.Take(MaxListedProblems))
            );
    }

    [Fact(
        DisplayName = "[Parity/SqlServer] 値の往復の乱数の行は、QuickER 版と EF Core 版のどの書く経路でも docs の約束どおりに読み戻せる"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public async Task RandomSeeds_MatchDocsOnBothBackends()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);

        var plan = SeedSource.Resolve(Seeds);
        plan.WriteWarnings();
        var scenarios = plan.Seeds.ToDictionary(
            seed => seed,
            BackendParitySqlServerValueGenerator.Create
        );
        var coverage = new CoverageCounter(BackendParitySqlServerValueDimensions.Table);
        var observed = scenarios.Keys.ToDictionary(
            seed => seed,
            _ => new Dictionary<SqlServerValueBackend, Dictionary<string, string>>()
        );
        var problems = scenarios.Keys.ToDictionary(seed => seed, _ => new List<string>());
        var watch = Stopwatch.StartNew();

        foreach (var backend in Enum.GetValues<SqlServerValueBackend>())
        {
            await using var context = await ValueContext.CreateAsync(fixture, backend);

            foreach (var (seed, scenario) in scenarios)
            {
                var results = new Dictionary<string, string>(StringComparer.Ordinal);
                await RunAsync(context, scenario, coverage, results, problems[seed]);
                observed[seed][backend] = results;
            }

            var all = new SqlServerValueScenario(
                "all",
                [.. scenarios.Values.SelectMany(s => s.Writes)]
            );
            var merged = observed
                .Values.SelectMany(byBackend => byBackend[backend])
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var includeProblems = new List<string>();
            await CheckIncludeAsync(context, all, merged, includeProblems);

            foreach (var problem in includeProblems)
            {
                problems[SeedOf(problem, scenarios)].Add(problem);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"値の往復の乱数の行：種 {plan.Seeds.Count} 個 × 実装先 2 つ・{watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} 秒"
        );

        var engineReport = new EngineReport(RandomScope, MaxFailedSeeds);

        foreach (var (seed, scenario) in scenarios)
        {
            CompareBackends(observed[seed], problems[seed]);
            var failures = problems[seed]
                .Select(problem => new EngineFailure(
                    RandomScope,
                    problem.Split(' ', 2)[0],
                    $"{Describe(scenario)}\n  {problem}",
                    seed
                ))
                .ToList();
            var known = KnownSet.Classify(failures, plan, scope => scope == RandomScope);

            foreach (var failure in known.Unknown)
            {
                engineReport.Add(failure, plan.ReproductionFor(seed));
            }
        }

        // 網羅しない組の照合は夜間の既定の種の数でだけ行う（普段の 8 種では書く経路 × 断られる形の組が欠ける）
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary();

        if (plan.Profile == EngineProfile.Nightly)
        {
            coverageReport.AssertGaps(
                BackendParitySqlServerValueDimensions.Gaps,
                BackendParitySqlServerValueDimensions.Scope,
                plan
            );
        }

        engineReport.Failures.Should().BeEmpty(engineReport.Render());
    }

    /// <summary>シナリオの書き込みを順に流し、期待と照らして、読み戻した値の表記を結果へ積む</summary>
    private static async Task RunAsync(
        ValueContext context,
        SqlServerValueScenario scenario,
        CoverageCounter coverage,
        Dictionary<string, string> results,
        List<string> problems
    )
    {
        foreach (var write in scenario.Writes)
        {
            BackendParitySqlServerValueDimensions.Observe(coverage, context.Backend, write);
            var label = $"{context.Backend} {write.Path} {Describe(write)}";
            var refused = write.Rows.Any(row =>
                row.Cells.Any(cell =>
                    BackendParitySqlServerValueExpectations.IsRefused(cell.Column, cell.Value)
                )
            );
            Exception? thrown;

            try
            {
                thrown = await context.WriteAsync(write);
            }
            catch (Exception ex)
            {
                problems.Add(
                    $"setup {label}：前提の書き込みが失敗した：{ex.GetType().Name}: {FirstLine(ex)}"
                );
                continue;
            }

            if (thrown is not null && !refused)
            {
                problems.Add(
                    $"refused-unexpectedly {label}：断られた：{thrown.GetType().Name}/{Innermost(thrown).GetType().Name}: {FirstLine(Innermost(thrown))}"
                );
            }
            else if (thrown is null && refused)
            {
                problems.Add($"not-refused {label}：断られるはずの書き込みが通った");
            }

            foreach (var row in write.Rows)
            {
                await CheckRowAsync(
                    context,
                    write,
                    row,
                    thrown is not null,
                    label,
                    results,
                    problems
                );
            }
        }
    }

    /// <summary>1 行を GetById・Query で読み戻して照らす（断られたなら何も残っていないこと）</summary>
    private static async Task CheckRowAsync(
        ValueContext context,
        SqlServerValueWrite write,
        SqlServerValueRow row,
        bool failed,
        string label,
        Dictionary<string, string> results,
        List<string> problems
    )
    {
        var byId = await context.Probes.GetByIdAsync(row.Id, Ct);
        var queried = (
            await context.Probes.Query().Where(p => p.ProbeId == row.Id).ToListAsync(Ct)
        ).SingleOrDefault();
        var updates = write.Path is SqlServerValuePath.Update or SqlServerValuePath.SaveUpdate;

        if (failed)
        {
            if (!updates && byId is not null)
            {
                problems.Add($"residue {label}：断られた書き込みの行 {row.Id} が残った");
            }

            if (updates && byId is not null)
            {
                foreach (
                    var cell in row.Cells.Where(cell => cell.Column != SqlServerValueColumn.Payload)
                )
                {
                    if (Read(byId, cell.Column) is { } left)
                    {
                        problems.Add(
                            $"residue {label}：断られた更新の行 {row.Id} の {cell.Column} が {BackendParitySqlServerValueExpectations.Format(left)} になった"
                        );
                    }
                }
            }

            return;
        }

        if (byId is null || queried is null)
        {
            problems.Add(
                $"missing {label}：行 {row.Id} を読み戻せない（GetById {byId is not null}・Query {queried is not null}）"
            );
            return;
        }

        foreach (var cell in row.Cells)
        {
            var read =
                cell.Column == SqlServerValueColumn.Payload
                    ? await context.ReadPayloadAsync(row.Id)
                    : Read(byId, cell.Column);
            var viaQuery =
                cell.Column == SqlServerValueColumn.Payload ? read : Read(queried, cell.Column);
            var key = Key(row, cell);
            results[key] = BackendParitySqlServerValueExpectations.Format(read);

            // 断られるはずの値が通ったことは not-refused で名指し済み（期待の値は無いので照らさない）
            if (BackendParitySqlServerValueExpectations.IsRefused(cell.Column, cell.Value))
            {
                continue;
            }

            if (
                BackendParitySqlServerValueExpectations.Check(cell.Column, cell.Value, read) is
                { } problem
            )
            {
                problems.Add(
                    $"value {label}：行 {row.Id} の {cell.Column}（{cell.Shape}）：{problem}"
                );
            }

            if (BackendParitySqlServerValueExpectations.Format(viaQuery) != results[key])
            {
                problems.Add(
                    $"read-paths-differ {label}：行 {row.Id} の {cell.Column}：GetById {results[key]}・Query {BackendParitySqlServerValueExpectations.Format(viaQuery)}"
                );
            }
        }
    }

    /// <summary>customers から Include で読み（QuickER 版は FOR JSON）、GetById で読んだ値と同じか確かめる</summary>
    private static async Task CheckIncludeAsync(
        ValueContext context,
        SqlServerValueScenario scenario,
        IReadOnlyDictionary<string, string> results,
        List<string> problems
    )
    {
        var customer = await context
            .Customers.Query()
            .Where(c => c.CustomerId == CustomerId)
            .Include(c => c.ValueProbes)
            .FirstOrDefaultAsync(Ct);
        var included = customer!.ValueProbes.ToDictionary(p => p.ProbeId);

        foreach (var row in scenario.Writes.SelectMany(write => write.Rows))
        {
            foreach (
                var cell in row.Cells.Where(cell => cell.Column != SqlServerValueColumn.Payload)
            )
            {
                if (!results.TryGetValue(Key(row, cell), out var expected))
                {
                    continue;
                }

                var actual = included.TryGetValue(row.Id, out var probe)
                    ? BackendParitySqlServerValueExpectations.Format(Read(probe, cell.Column))
                    : "(行が無い)";

                if (actual != expected)
                {
                    problems.Add(
                        $"include {context.Backend}：行 {row.Id} の {cell.Column}（{cell.Shape}）：GetById {expected}・Include {actual}"
                    );
                }
            }
        }
    }

    /// <summary>2 つの実装先の読み戻しが同じか（片方だけにある値も食い違い）</summary>
    private static void CompareBackends(
        IReadOnlyDictionary<SqlServerValueBackend, Dictionary<string, string>> observed,
        List<string> problems
    )
    {
        var quickEr = observed[SqlServerValueBackend.QuickEr];
        var efCore = observed[SqlServerValueBackend.EfCore];

        foreach (var key in quickEr.Keys.Union(efCore.Keys).Order(StringComparer.Ordinal))
        {
            var left = quickEr.GetValueOrDefault(key, "(読み戻していない)");
            var right = efCore.GetValueOrDefault(key, "(読み戻していない)");

            if (left != right)
            {
                problems.Add($"backends-differ {key}：QuickER 版 {left}・EF Core 版 {right}");
            }
        }
    }

    /// <summary>結果の鍵（行・列。経路と行は主キーで区別する）</summary>
    private static string Key(SqlServerValueRow row, SqlServerValueCell cell) =>
        $"{row.Id}/{cell.Column}";

    /// <summary>失敗の説明の行の主キーから種を引く（Include の失敗を種へ戻す）</summary>
    private static int SeedOf(
        string problem,
        IReadOnlyDictionary<int, SqlServerValueScenario> scenarios
    )
    {
        foreach (var (seed, scenario) in scenarios)
        {
            if (
                scenario
                    .Writes.SelectMany(w => w.Rows)
                    .Any(row => problem.Contains($"行 {row.Id} ", StringComparison.Ordinal))
            )
            {
                return seed;
            }
        }

        return scenarios.Keys.First();
    }

    private static object? Read(VP.ValueProbeEntity entity, SqlServerValueColumn column) =>
        column switch
        {
            SqlServerValueColumn.At2 => entity.At2,
            SqlServerValueColumn.At => entity.At,
            SqlServerValueColumn.Clock => entity.Clock,
            SqlServerValueColumn.AtOffset => entity.AtOffset,
            SqlServerValueColumn.AtOffset2 => entity.AtOffset2,
            SqlServerValueColumn.Day => entity.Day,
            SqlServerValueColumn.Amount => entity.Amount,
            SqlServerValueColumn.Ansi => entity.Ansi,
            SqlServerValueColumn.Code => entity.Code,
            SqlServerValueColumn.Label => entity.Label,
            SqlServerValueColumn.Tag => entity.Tag,
            SqlServerValueColumn.Payload => entity.Payload,
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };

    private static void Write(VP.ValueProbeEntity entity, SqlServerValueCell cell)
    {
        switch (cell.Column)
        {
            case SqlServerValueColumn.At2:
                entity.At2 = (DateTime)cell.Value;
                break;
            case SqlServerValueColumn.At:
                entity.At = (DateTime)cell.Value;
                break;
            case SqlServerValueColumn.Clock:
                entity.Clock = (TimeSpan)cell.Value;
                break;
            case SqlServerValueColumn.AtOffset:
                entity.AtOffset = (DateTimeOffset)cell.Value;
                break;
            case SqlServerValueColumn.AtOffset2:
                entity.AtOffset2 = (DateTimeOffset)cell.Value;
                break;
            case SqlServerValueColumn.Day:
                entity.Day = (DateTime)cell.Value;
                break;
            case SqlServerValueColumn.Amount:
                entity.Amount = (decimal)cell.Value;
                break;
            case SqlServerValueColumn.Ansi:
                entity.Ansi = (string)cell.Value;
                break;
            case SqlServerValueColumn.Code:
                entity.Code = (string)cell.Value;
                break;
            case SqlServerValueColumn.Label:
                entity.Label = (string)cell.Value;
                break;
            case SqlServerValueColumn.Tag:
                entity.Tag = (byte[])cell.Value;
                break;
            case SqlServerValueColumn.Payload:
                entity.Payload = (byte[])cell.Value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cell), cell.Column, null);
        }
    }

    private static string Describe(SqlServerValueScenario scenario) =>
        $"{scenario.Name}:\n  "
        + string.Join("\n  ", scenario.Writes.Select(write => $"{write.Path} {Describe(write)}"));

    private static string Describe(SqlServerValueWrite write) =>
        string.Join(
            " ",
            write.Rows.Select(row =>
                $"[{row.Id}: "
                + string.Join(
                    ", ",
                    row.Cells.Select(cell =>
                        $"{cell.Column}={cell.Shape}:{BackendParitySqlServerValueExpectations.Format(cell.Value)}"
                    )
                )
                + "]"
            )
        );

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    private static string FirstLine(Exception exception) => exception.Message.Split('\n')[0].Trim();

    /// <summary>1 つの実装先の DB・リポジトリ（作るときにスキーマを作り直し、顧客を 1 行入れる）</summary>
    private sealed class ValueContext : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _connectionString;

        private ValueContext(
            SqlServerValueBackend backend,
            ServiceProvider provider,
            string connectionString
        )
        {
            Backend = backend;
            _provider = provider;
            _connectionString = connectionString;
        }

        public SqlServerValueBackend Backend { get; }

        public VP.IValueProbeRepository Probes =>
            _provider.GetRequiredService<VP.IValueProbeRepository>();

        public VP.ICustomerRepository Customers =>
            _provider.GetRequiredService<VP.ICustomerRepository>();

        public static async Task<ValueContext> CreateAsync(
            SqlServerContainerFixture fixture,
            SqlServerValueBackend backend
        )
        {
            await fixture.ResetSchemaAsync(Ct);
            await fixture.ApplyDdlAsync(
                VP.BackendParitySqlServerFixtureDefinition.BuildForSqlServerDdl(),
                Ct
            );
            var services = new ServiceCollection();
            var provider = (
                backend == SqlServerValueBackend.QuickEr
                    ? VP.GeneratedSqlServerRepositoryServiceCollectionExtensions.AddGeneratedSqlServerRepositories(
                        services,
                        fixture.ConnectionString
                    )
                    : VP.GeneratedEfCoreRepositoryServiceCollectionExtensions.AddGeneratedEfCoreRepositories(
                        services,
                        options =>
                            options
                                .UseSqlServer(fixture.ConnectionString)
                                .IgnoreServiceProviderCacheLimit()
                    )
            ).BuildServiceProvider();
            var context = new ValueContext(backend, provider, fixture.ConnectionString);
            await context.Customers.InsertAsync(
                new VP.CustomerEntity
                {
                    CustomerId = CustomerId,
                    Name = "values",
                    Code = "values",
                },
                Ct
            );

            return context;
        }

        /// <summary>書き込みを 1 回流す（断られたらその例外、通れば null。前提の書き込みの失敗はそのまま投げる）</summary>
        public async Task<Exception?> WriteAsync(SqlServerValueWrite write)
        {
            var probes = Probes;
            VP.ValueProbeEntity? loaded = null;

            if (write.Path is SqlServerValuePath.Update or SqlServerValuePath.SaveUpdate)
            {
                var row = write.Rows.Single();
                await probes.InsertAsync(New(row.Id), Ct);
                loaded =
                    await probes.GetByIdAsync(row.Id, Ct)
                    ?? throw new InvalidOperationException($"前提の行 {row.Id} を読めない");

                foreach (var cell in row.Cells)
                {
                    Write(loaded, cell);
                }
            }

            try
            {
                switch (write.Path)
                {
                    case SqlServerValuePath.Insert:
                        await probes.InsertAsync(Build(write.Rows.Single()), Ct);
                        break;
                    case SqlServerValuePath.Update:
                        await probes.UpdateAsync(loaded!, cancellationToken: Ct);
                        break;
                    case SqlServerValuePath.SaveInsert:
                        var added = Build(write.Rows.Single());
                        added.RowState = VP.RowState.Added;
                        await probes.SaveAsync(added, cancellationToken: Ct);
                        break;
                    case SqlServerValuePath.SaveUpdate:
                        loaded!.MarkUpdated();
                        await probes.SaveAsync(loaded, cancellationToken: Ct);
                        break;
                    case SqlServerValuePath.Bulk:
                        await probes.BulkInsertAsync([.. write.Rows.Select(Build)], Ct);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(write), write.Path, null);
                }

                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ex;
            }
        }

        /// <summary>除外する無制限バイナリ列を生の SELECT で読む</summary>
        public async Task<byte[]?> ReadPayloadAsync(int id)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT [payload] FROM [value_probes] WHERE [probe_id] = @id";
            command.Parameters.Add(
                new SqlParameter("@id", System.Data.SqlDbType.Int) { Value = id }
            );
            var value = await command.ExecuteScalarAsync(Ct);

            return value as byte[];
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        private static VP.ValueProbeEntity New(int id) =>
            new() { ProbeId = id, CustomerId = CustomerId };

        private static VP.ValueProbeEntity Build(SqlServerValueRow row)
        {
            var entity = New(row.Id);

            foreach (var cell in row.Cells)
            {
                Write(entity, cell);
            }

            return entity;
        }
    }
}
