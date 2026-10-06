using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqliteValueConversionFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQLite が TEXT で返す「<see cref="IConvertible"/> を実装しない CLR 型」（<see cref="TimeSpan"/> /
/// <see cref="Guid"/> / <see cref="DateTimeOffset"/>）を、生成コードが読み戻せることを実 DB で検証する。
/// </summary>
/// <remarks>
/// <para>
/// これらの型の変換を <c>Convert.ChangeType</c> だけに任せると、値の中身に関係なく必ず
/// <see cref="InvalidCastException"/> になる（SQL Server の ADO は型付きで返すため露見しなかった）。
/// 影響していた 3 経路（値オブジェクトの再ラップ・生 SQL スカラー・生 SQL 射影 DTO）を、
/// 同一の図・同一の列でまとめて押さえる。
/// </para>
/// <para>
/// Docker 不要（実ファイル DB）のため CI でも常時実行される。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteValueConversionRuntimeTests : IAsyncLifetime
{
    /// <summary>検証用の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    /// <summary>SQLite リポジトリを登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>格納・読み戻しを比較する基準値（秒未満まで持つ TimeSpan）</summary>
    private static readonly TimeSpan SampleDuration = new(1, 2, 3, 4, 500);

    /// <summary>格納・読み戻しを比較する基準値（Guid）</summary>
    private static readonly Guid SampleSession = new("2b7f0e42-9c31-4a55-8d10-6f0c1e3a7b99");

    /// <summary>格納・読み戻しを比較する基準値（オフセット付きの日時）</summary>
    private static readonly DateTimeOffset SampleOccurredAt = new(
        2026,
        8,
        16,
        10,
        20,
        30,
        TimeSpan.FromHours(9)
    );

    /// <summary>図から SQLite スキーマを作り、DI を組んで 1 行入れる</summary>
    public async ValueTask InitializeAsync()
    {
        await _sqlite.ApplyDdlAsync(SqliteValueConversionFixtureDefinition.Build(), Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_sqlite.ReadWriteCreateConnectionString)
            .BuildServiceProvider();

        await Probes().InsertAsync(NewProbe(1, "alpha"), Ct);
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>リポジトリを解決する</summary>
    private ITimeProbeRepository Probes() => _provider.GetRequiredService<ITimeProbeRepository>();

    /// <summary>生 SQL 用の Executor を組む（射影 DTO の経路はリポジトリ側では protected のため直接使う）</summary>
    private SqlExecutor Executor() =>
        new(new SqlConnectionFactory(_sqlite.ReadWriteCreateConnectionString));

    /// <summary>基準値を持つエンティティを作る</summary>
    private static TimeProbeEntity NewProbe(int id, string label) =>
        new()
        {
            ProbeId = ProbeIdValue.Create(id),
            Duration = DurationValue.Create(SampleDuration),
            SessionId = SessionIdValue.Create(SampleSession),
            OccurredAt = OccurredAtValue.Create(SampleOccurredAt),
            Label = LabelValue.Create(label),
        };

    /// <summary>読み戻したエンティティが基準値と一致することを表明する</summary>
    private static void AssertSampleValues(TimeProbeEntity? entity)
    {
        entity.Should().NotBeNull();
        entity!.Duration!.Value.Should().Be(SampleDuration);
        entity.SessionId!.Value.Should().Be(SampleSession);
        entity.OccurredAt!.Value.Should().Be(SampleOccurredAt);
    }

    /// <summary>単一行取得（式ツリー materializer のフォールバック → SetColumnValue → Wrap 経路）を検証する</summary>
    [Fact(
        DisplayName = "[SQLite/値変換] GetById が TimeSpan / Guid / DateTimeOffset の値オブジェクトを読み戻す"
    )]
    public async Task GetById_ReadsNonConvertibleValueObjects()
    {
        AssertSampleValues(await Probes().GetByIdAsync(ProbeIdValue.Create(1), Ct));
    }

    /// <summary>全件取得も同じ行マッピングを通ることを検証する</summary>
    [Fact(DisplayName = "[SQLite/値変換] GetAll が同じ列を読み戻す")]
    public async Task GetAll_ReadsNonConvertibleValueObjects()
    {
        var all = await Probes().GetAllAsync(Ct);

        AssertSampleValues(all.Should().ContainSingle().Subject);
    }

    /// <summary>Query() パイプライン（条件付き取得）も同じ行マッピングを通ることを検証する</summary>
    [Fact(DisplayName = "[SQLite/値変換] Query() の取得が同じ列を読み戻す")]
    public async Task Query_ReadsNonConvertibleValueObjects()
    {
        var label = LabelValue.Create("alpha");
        var rows = await Probes().Query().Where(probe => probe.Label == label).ToListAsync(Ct);

        AssertSampleValues(rows.Should().ContainSingle().Subject);
    }

    /// <summary>
    /// 生 SQL スカラー（<c>RawSqlMapper.ConvertSingleValue</c> 経路）が、素の CLR 型でも値オブジェクトでも
    /// 変換できることを検証する。
    /// </summary>
    [Fact(DisplayName = "[SQLite/値変換] 生 SQL スカラーが非 IConvertible 型を変換する")]
    public async Task ExecuteScalarSql_ConvertsNonConvertibleTypes()
    {
        var repository = Probes();

        (
            await repository.ExecuteScalarSqlAsync<TimeSpan>(
                "SELECT \"duration\" FROM \"time_probes\";",
                cancellationToken: Ct
            )
        )
            .Should()
            .Be(SampleDuration);
        (
            await repository.ExecuteScalarSqlAsync<Guid>(
                "SELECT \"session_id\" FROM \"time_probes\";",
                cancellationToken: Ct
            )
        )
            .Should()
            .Be(SampleSession);
        (
            await repository.ExecuteScalarSqlAsync<DateTimeOffset>(
                "SELECT \"occurred_at\" FROM \"time_probes\";",
                cancellationToken: Ct
            )
        )
            .Should()
            .Be(SampleOccurredAt);

        // 値オブジェクト形（Wrap → ResolveFactory の変換）も同じ経路で通る
        (
            await repository.ExecuteScalarSqlAsync<DurationValue>(
                "SELECT \"duration\" FROM \"time_probes\";",
                cancellationToken: Ct
            )
        )!
            .Value.Should()
            .Be(SampleDuration);
    }

    /// <summary>
    /// 生 SQL 射影 DTO（<c>RawSqlMapper.CoerceProjectionValue</c> 経路）が、素の CLR 型でも値オブジェクトでも
    /// 変換できることを検証する。
    /// </summary>
    [Fact(DisplayName = "[SQLite/値変換] 生 SQL 射影 DTO が非 IConvertible 型を変換する")]
    public async Task QueryProjectionBySql_ConvertsNonConvertibleTypes()
    {
        // 射影は結果セットの列名とプロパティ名で突き合わせるため、列に別名を付ける
        var rows = await Executor()
            .QueryProjectionBySqlAsync<ProbeProjection>(
                "SELECT \"probe_id\" AS ProbeId, \"duration\" AS Duration, \"duration\" AS DurationValueObject, "
                    + "\"session_id\" AS SessionId, \"occurred_at\" AS OccurredAt FROM \"time_probes\";",
                cancellationToken: Ct
            );

        var row = rows.Should().ContainSingle().Subject;
        row.ProbeId.Should().Be(1);
        row.Duration.Should().Be(SampleDuration);
        row.SessionId.Should().Be(SampleSession);
        row.OccurredAt.Should().Be(SampleOccurredAt);
        row.DurationValueObject!.Value.Should().Be(SampleDuration);
    }

    /// <summary>
    /// 値オブジェクト生成が有効な構成でも、値オブジェクトでない素の列を持つエンティティが
    /// 生 SQL 取得（<c>MapEntityStrict</c> → <c>SetColumnValue</c>）で読めることを検証する。
    /// </summary>
    /// <remarks>
    /// 生成器は VO 有効時に全列を VO 型にするため、この形は生成 Entity には現れない。ただし
    /// <c>ISqlExecutor.QueryBySqlAsync&lt;TEntity&gt;</c> は利用者が書いた任意の <c>EntityBase</c> 派生型を受けるため
    /// 到達可能で、<c>Wrap</c> 一択の実装では素の列に格納値（文字列）がそのまま渡って
    /// <see cref="ArgumentException"/> になる。行マッピングの規則（VO は Wrap・それ以外は方言変換）を固定する。
    /// </remarks>
    [Fact(DisplayName = "[SQLite/値変換] 値オブジェクトでない素の列も生 SQL 取得で読める")]
    public async Task QueryBySql_MapsPlainColumns_WhenValueObjectsAreEnabled()
    {
        var rows = await Executor()
            .QueryBySqlAsync<PlainProbeEntity>(
                "SELECT \"probe_id\", \"duration\", \"session_id\", \"occurred_at\", \"label\" FROM \"time_probes\";",
                cancellationToken: Ct
            );

        var row = rows.Should().ContainSingle().Subject;
        row.ProbeId.Should().Be(1);
        row.Duration.Should().Be(SampleDuration);
        row.SessionId.Should().Be(SampleSession);
        row.OccurredAt.Should().Be(SampleOccurredAt);
        row.Label.Should().Be("alpha");
    }

    /// <summary>
    /// 外部ツールが書いたオフセットつきの日時の TEXT を、どの経路で読んでも同じ値（UTC に換算・Kind=Utc）になることを検証する。
    /// GetById は値オブジェクトの速い読み取り（ドライバの GetDateTime）、Query() は再ラップ（RawValueConverter）、
    /// 素の列の生 SQL 取得は CoerceScalar、生 SQL スカラーは RawValueConverter を通る。
    /// 従来は速い読み取りだけが UTC、他は Local に換算して食い違っていた
    /// </summary>
    [Fact(
        DisplayName = "[SQLite/値変換] オフセットつきの日時の TEXT はどの経路でも同じ UTC の値に読む"
    )]
    public async Task StoredDateTimeWithOffset_ReadsTheSameOnEveryPath()
    {
        await Executor()
            .ExecuteSqlAsync(
                "UPDATE \"time_probes\" SET \"recorded_at\" = '2024-01-15 10:00:00+09:00';",
                cancellationToken: Ct
            );
        var expected = new DateTime(2024, 1, 15, 1, 0, 0, DateTimeKind.Utc);

        var byId = (await Probes().GetByIdAsync(ProbeIdValue.Create(1), Ct))!.RecordedAt!.Value;
        var label = LabelValue.Create("alpha");
        var byQuery = (await Probes().Query().Where(probe => probe.Label == label).ToListAsync(Ct))
            .Should()
            .ContainSingle()
            .Subject.RecordedAt!.Value;
        var byRawSql = (
            await Executor()
                .QueryBySqlAsync<RecordedProbeEntity>(
                    "SELECT \"probe_id\", \"session_id\", \"recorded_at\" FROM \"time_probes\";",
                    cancellationToken: Ct
                )
        )
            .Should()
            .ContainSingle()
            .Subject.RecordedAt;
        var byScalar = await Probes()
            .ExecuteScalarSqlAsync<DateTime>(
                "SELECT \"recorded_at\" FROM \"time_probes\";",
                cancellationToken: Ct
            );

        foreach (
            var (path, value) in new[]
            {
                ("GetById", byId),
                ("Query()", byQuery),
                ("生 SQL 取得", byRawSql!.Value),
                ("生 SQL スカラー", byScalar),
            }
        )
        {
            value.Should().Be(expected, path);
            value.Kind.Should().Be(DateTimeKind.Utc, path);
        }
    }

    /// <summary>
    /// 16 バイトの BLOB で格納された Guid を、どの経路で読んでも同じ値になることを検証する。
    /// 従来は速い読み取り（GetGuid）だけが読め、Query() と素の列の生 SQL 取得は変換に失敗していた
    /// </summary>
    [Fact(DisplayName = "[SQLite/値変換] BLOB で格納された Guid はどの経路でも読める")]
    public async Task StoredGuidBlob_ReadsTheSameOnEveryPath()
    {
        await Executor()
            .ExecuteSqlAsync(
                "UPDATE \"time_probes\" SET \"session_id\" = @session;",
                new { session = SampleSession.ToByteArray() },
                Ct
            );

        var byId = (await Probes().GetByIdAsync(ProbeIdValue.Create(1), Ct))!.SessionId!.Value;
        var label = LabelValue.Create("alpha");
        var byQuery = (await Probes().Query().Where(probe => probe.Label == label).ToListAsync(Ct))
            .Should()
            .ContainSingle()
            .Subject.SessionId!.Value;
        var byRawSql = (
            await Executor()
                .QueryBySqlAsync<RecordedProbeEntity>(
                    "SELECT \"probe_id\", \"session_id\", \"recorded_at\" FROM \"time_probes\";",
                    cancellationToken: Ct
                )
        )
            .Should()
            .ContainSingle()
            .Subject.SessionId;

        byId.Should().Be(SampleSession, "GetById");
        byQuery.Should().Be(SampleSession, "Query()");
        byRawSql.Should().Be(SampleSession, "生 SQL 取得");
    }

    /// <summary>
    /// 手書きのエンティティの DateOnly / TimeOnly の列（SQLite は TEXT で格納する）を、リポジトリの GetAll と
    /// 生 SQL 取得の両方で読めることを検証する。どちらも IConvertible を実装しないため、
    /// Convert.ChangeType に任せると値の中身に関係なく失敗する
    /// </summary>
    [Fact(DisplayName = "[SQLite/値変換] 手書きエンティティの DateOnly / TimeOnly の列を読める")]
    public async Task HandwrittenDateOnlyAndTimeOnlyColumns_AreRead()
    {
        var executor = Executor();
        await executor.ExecuteSqlAsync(
            "CREATE TABLE \"day_probes\" (\"probe_id\" INTEGER NOT NULL PRIMARY KEY, \"day\" TEXT NOT NULL, \"clock\" TEXT NULL);",
            cancellationToken: Ct
        );
        await executor.ExecuteSqlAsync(
            "INSERT INTO \"day_probes\" VALUES (1, '2024-01-15', '10:30:15.5000000');",
            cancellationToken: Ct
        );

        var repository = new DayProbeRepository(
            new SqlConnectionFactory(_sqlite.ReadWriteCreateConnectionString)
        );
        var byRepository = (await repository.GetAllAsync(Ct)).Should().ContainSingle().Subject;
        var byRawSql = (
            await executor.QueryBySqlAsync<DayProbeEntity>(
                "SELECT \"probe_id\", \"day\", \"clock\" FROM \"day_probes\";",
                cancellationToken: Ct
            )
        )
            .Should()
            .ContainSingle()
            .Subject;

        foreach (var (path, row) in new[] { ("GetAll", byRepository), ("生 SQL 取得", byRawSql) })
        {
            row.Day.Should().Be(new DateOnly(2024, 1, 15), path);
            row.Clock.Should().Be(new TimeOnly(10, 30, 15, 500), path);
        }
    }

    /// <summary>
    /// 値オブジェクトの検証に通らない格納値（最大長 50 の列に 60 文字）を読むと、どの経路でも列名を名指しした
    /// InvalidOperationException になることを検証する。速い読み取り（GetById / GetAll）は Create を直接呼び、
    /// 従来は値オブジェクトの型しか分からない例外がそのまま出ていた
    /// </summary>
    [Fact(
        DisplayName = "[SQLite/値変換] 値オブジェクトが拒む格納値はどの経路でも列名つきの例外になる"
    )]
    public async Task StoredValueRejectedByValueObject_NamesTheColumnOnEveryPath()
    {
        await Executor()
            .ExecuteSqlAsync(
                "UPDATE \"time_probes\" SET \"label\" = @label;",
                new { label = new string('x', 60) },
                Ct
            );

        var reads = new (string Path, Func<Task> Read)[]
        {
            ("GetById", async () => await Probes().GetByIdAsync(ProbeIdValue.Create(1), Ct)),
            ("GetAll", async () => await Probes().GetAllAsync(Ct)),
            (
                "Query()",
                async () =>
                    await Probes()
                        .Query()
                        .Where(probe => probe.ProbeId == ProbeIdValue.Create(1))
                        .ToListAsync(Ct)
            ),
        };

        foreach (var (path, read) in reads)
        {
            var failure = (
                await read.Should().ThrowExactlyAsync<InvalidOperationException>(path)
            ).Which;
            failure
                .Message.Should()
                .Contain("'label'", path)
                .And.Contain("TimeProbeEntity.Label", path);
            failure.InnerException.Should().BeOfType<ValueObjectValidationException>(path);
        }
    }

    /// <summary>生 SQL 射影の受け皿 DTO（素の CLR 型で受ける）</summary>
    private sealed class ProbeProjection
    {
        /// <summary>probe_id 列（SQLite は INTEGER＝long で返すため縮小変換も兼ねる）</summary>
        public int ProbeId { get; set; }

        /// <summary>duration 列</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>duration 列を値オブジェクトで受ける（射影の VO 経路）</summary>
        public DurationValue? DurationValueObject { get; set; }

        /// <summary>session_id 列</summary>
        public Guid SessionId { get; set; }

        /// <summary>occurred_at 列</summary>
        public DateTimeOffset OccurredAt { get; set; }
    }
}

/// <summary>
/// 手書きの「値オブジェクトを使わない」エンティティ（生 SQL 取得で <c>time_probes</c> を素の CLR 型として受ける）。
/// </summary>
/// <remarks>
/// 生成 Entity（<c>TimeProbeEntity</c>）は VO 型だが、<c>ISqlExecutor.QueryBySqlAsync&lt;TEntity&gt;</c> は
/// 利用者が定義した任意の <c>EntityBase</c> 派生型を受け付ける。行マッピングが「VO は Wrap・それ以外は方言変換」で
/// 分岐していることを、この型で確かめる。
/// </remarks>
[Table("time_probes")]
public sealed class PlainProbeEntity : EntityBase
{
    /// <summary>probe_id 列</summary>
    [Key]
    [Column("probe_id")]
    public int ProbeId { get; set; }

    /// <summary>duration 列</summary>
    [Column("duration")]
    public TimeSpan Duration { get; set; }

    /// <summary>session_id 列</summary>
    [Column("session_id")]
    public Guid SessionId { get; set; }

    /// <summary>occurred_at 列</summary>
    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>label 列</summary>
    [Column("label")]
    public string? Label { get; set; }
}

/// <summary>
/// 手書きの、<c>time_probes</c> の一部の列を素の CLR 型で受けるエンティティ（生 SQL 取得の CoerceScalar の経路を通す）。
/// </summary>
[Table("time_probes")]
public sealed class RecordedProbeEntity : EntityBase
{
    /// <summary>probe_id 列</summary>
    [Key]
    [Column("probe_id")]
    public int ProbeId { get; set; }

    /// <summary>session_id 列</summary>
    [Column("session_id")]
    public Guid SessionId { get; set; }

    /// <summary>recorded_at 列</summary>
    [Column("recorded_at")]
    public DateTime? RecordedAt { get; set; }
}

/// <summary>手書きの、DateOnly / TimeOnly の列を持つエンティティ（型マッパーはこの 2 型を出さないので手書きでだけ現れる）。</summary>
[Table("day_probes")]
public sealed class DayProbeEntity : EntityBase
{
    /// <summary>probe_id 列</summary>
    [Key]
    [Column("probe_id")]
    public int ProbeId { get; set; }

    /// <summary>day 列</summary>
    [Column("day")]
    public DateOnly Day { get; set; }

    /// <summary>clock 列</summary>
    [Column("clock")]
    public TimeOnly? Clock { get; set; }
}

/// <summary>手書きエンティティ <see cref="DayProbeEntity"/> を生成ランタイムの SQLite エンジンで読むリポジトリ。</summary>
public sealed class DayProbeRepository(ISqlConnectionFactory connectionFactory)
    : SqliteRepository<DayProbeEntity, int>(connectionFactory);
