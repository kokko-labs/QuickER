using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <b>QuickER の SQL Server 実装</b>で、Include の無い <c>Query()</c> の取得（<c>ToListAsync</c> /
/// <c>FirstOrDefaultAsync</c> / 射影のフォールバック）が FOR JSON を使わずプレーン SELECT と
/// コンパイル済みの組み立て器（<c>GetAllAsync</c> と同じ）で読むことを、実 SQL Server（Testcontainers）で固定するスイート。
/// </summary>
/// <remarks>
/// <para>
/// FOR JSON は Include の子を 1 文で同じ時点のデータとして組み立てるためのもので、Include の無い取得では
/// JSON の組み立てと解析の分だけ遅く割り当ても多い（24 列・20 万行で約 2.9 倍の時間・約 3.9 倍の割り当て）。
/// Include の無い取得を <c>GetAllAsync</c> と同じ読み方へ寄せ、Include のある取得（FOR JSON）も同じ値を読むようにしたので、
/// 次の 2 つをここで固定する:
/// </para>
/// <list type="number">
///   <item>発行する SQL（SqlClient の DiagnosticListener で捕まえる）＝Include の無い取得は <c>FOR JSON</c> を含まず、
///     Include のある取得だけが含む。Take / Skip を並び替えなしで指定したときの <c>ORDER BY (SELECT NULL)</c> の補完も実 DB で効くこと。</item>
///   <item>読んだ値＝Include の無い <c>Query()</c> は <c>GetAllAsync</c> と列ごとに同じ値・同じ <c>RowState</c>・
///     同じナビゲーションの初期状態を返す。Include のある取得（FOR JSON）のルートと子も、型の境界値と NULL で
///     <c>GetAllAsync</c>（＝格納値）と同じ値になる。</item>
/// </list>
/// <para>
/// 入力は手書きのエンティティ（<see cref="ReadPathProbeEntity"/> ほか）を <see cref="GeneratedFixtureDefinition"/> の
/// インラインランタイムへ載せた形で、表は手書きの DDL で作る（図の型では表せない <c>money</c> などの型と、
/// 「エンティティでは NOT NULL なのに DB の列は NULL を許す」食い違いを作るため）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerPlainQueryRuntimeTests(SqlServerContainerFixture fixture)
{
    /// <summary>共有コンテナのフィクスチャ</summary>
    private SqlServerContainerFixture Fixture { get; } = fixture;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>プローブ表の DDL（label はエンティティでは NOT NULL の string だが、DB の列は NULL を許す）</summary>
    private const string Ddl = """
        CREATE TABLE [plain_probes] (
            [id] int NOT NULL CONSTRAINT [PK_plain_probes] PRIMARY KEY,
            [legacy_at] datetime NULL,
            [precise_at] datetime2(7) NULL,
            [small_at] smalldatetime NULL,
            [day] date NULL,
            [clock] time(7) NULL,
            [offset_at] datetimeoffset(7) NULL,
            [money_amount] money NULL,
            [small_money_amount] smallmoney NULL,
            [exact] decimal(28,10) NULL,
            [dbl] float NULL,
            [sgl] real NULL,
            [flag] bit NULL,
            [tiny] tinyint NULL,
            [small] smallint NULL,
            [big] bigint NULL,
            [text] nvarchar(max) NULL,
            [fixed] nchar(10) NULL,
            [ansi] varchar(50) NULL,
            [uid] uniqueidentifier NULL,
            [bin] varbinary(50) NULL,
            [fixed_bin] binary(4) NULL,
            [name] varchar(50) NULL,
            [label] nvarchar(20) NULL,
            [group] nvarchar(10) NULL,
            [x]]y] nvarchar(10) NULL,
            [ratio] float NULL
        );
        CREATE TABLE [plain_probe_children] (
            [id] int NOT NULL CONSTRAINT [PK_plain_probe_children] PRIMARY KEY,
            [probe_id] int NOT NULL,
            [dbl] float NULL,
            [sgl] real NULL,
            [ratio] float NULL,
            [note] nvarchar(20) NULL
        );
        CREATE TABLE [plain_probe_tags] (
            [id] int NOT NULL CONSTRAINT [PK_plain_probe_tags] PRIMARY KEY,
            [probe_id] int NOT NULL
        );
        """;

    /// <summary>リポジトリを生成する（QuickER の SQL Server 実装を直接 new）</summary>
    private ReadPathProbeRepository CreateRepository() =>
        new(new SqlConnectionFactory(Fixture.ConnectionString));

    /// <summary>各テスト冒頭のセットアップ（スキーマ初期化＋プローブ表の作成）</summary>
    private async Task ResetAndCreateSchemaAsync()
    {
        Assert.SkipUnless(Fixture.IsAvailable, Fixture.UnavailableReason);
        await Fixture.ResetSchemaAsync(Ct);
        await Fixture.ExecuteAsync(Ddl, Ct);
    }

    /// <summary>型の境界値を入れた行・全列 NULL の行・子行を入れる</summary>
    private async Task SeedAsync()
    {
        await Fixture.ExecuteAsync(
            """
            INSERT INTO [plain_probes] VALUES
            (1, '2024-01-02T03:04:05.007', '2024-01-02T03:04:05.1234567', '2024-01-02T03:04:00', '2024-02-29',
             '23:59:59.9999999', '2024-01-02T03:04:05.1234567+09:00', 922337203685477.5807, 214748.3647,
             123456789012345678.0123456789, 0.1, 0.1, 1, 255, -32768, -9223372036854775808,
             N'日本語 "quote" \ 改行' + NCHAR(10) + N'😀', N'ab', 'ansi', '6F9619FF-8B86-D011-B42D-00C04FC964FF',
             0x00FF10, 0xDEADBEEF, 'Alice', N'label', N'g1', N'a]b', 0.30000000000000004),
            (2, '1753-01-01T00:00:00.003', '0001-01-01T00:00:00', '1900-01-01T00:00:00', '0001-01-01',
             '00:00:00', '0001-01-01T00:00:00-14:00', -922337203685477.5808, -214748.3648,
             -0.0000000001, 1.7976931348623157E+308, 3.4028235E+38, 0, 0, 32767, 9223372036854775807,
             N'', N'0123456789', '', '00000000-0000-0000-0000-000000000000', 0x, 0x00000000, 'B', N'', N'', N'', -1.7976931348623157E+308),
            (3, '9999-12-31T23:59:59.997', '9999-12-31T23:59:59.9999999', '2079-06-06T23:59:00', '9999-12-31',
             '12:34:56.0000001', '9999-12-31T23:59:59.9999999+14:00', 0.0001, 0.0001,
             0.3333333333, 0.30000000000000004, 10.0001545, 1, 1, 0, 0,
             N' lead trail ', N'x', 'y', 'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF', 0x01, 0x01020304, 'Carol', N'z', N'g3', N']', 2.2250738585072014E-308),
            (4, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
             NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
            INSERT INTO [plain_probe_children] VALUES
            (10, 1, -CAST(0 AS float), 10.0001545, 0.30000000000000004, N'n'),
            (11, 1, 2.2250738585072014E-308, 3.4028235E+38, 1.7976931348623157E+308, NULL),
            (12, 3, 0.30000000000000004, NULL, NULL, N'');
            INSERT INTO [plain_probe_tags] VALUES (20, 1);
            """,
            Ct
        );
    }

    // ==================== 発行する SQL ====================

    /// <summary>
    /// 1. Include の無い <c>ToListAsync</c> / <c>FirstOrDefaultAsync</c> / 射影のフォールバックは FOR JSON を使わず、
    /// Include のある取得だけが FOR JSON を使う。
    /// </summary>
    [Fact(
        DisplayName = "[SqlServerPlainQuery] 1: Include の無い取得は FOR JSON を使わず、Include のある取得だけが使う"
    )]
    public async Task IncludeLessFetch_DoesNotUseForJson()
    {
        await ResetAndCreateSchemaAsync();
        await SeedAsync();
        var repo = CreateRepository();

        using var capture = new SqlCommandCapture("plain_probes");

        await repo.Query().Where(x => x.Id > 1).ToListAsync(Ct);
        await repo.Query().Where(x => x.Id == 3).FirstOrDefaultAsync(Ct);
        // 列を取り出せないセレクタ（メソッドへエンティティを丸ごと渡す）＝全列を読んでからメモリ内で射影するフォールバック
        await repo.Query().ToProjectionListAsync(x => Describe(x), Ct);
        var includeLess = capture.TakeAll();

        includeLess.Should().HaveCount(3);
        includeLess.Should().AllSatisfy(sql => sql.Should().NotContain("FOR JSON"));

        await repo.Query().Include(x => x.Children).ToListAsync(Ct);
        await repo.Query().Include(x => x.Children).FirstOrDefaultAsync(Ct);
        var withInclude = capture.TakeAll();

        withInclude.Should().HaveCount(2);
        withInclude.Should().AllSatisfy(sql => sql.Should().Contain("FOR JSON PATH"));
    }

    /// <summary>
    /// 2. 並び替えなしの Take / Skip は <c>ORDER BY (SELECT NULL)</c> を補って OFFSET-FETCH で実 DB に通り、
    /// 並び替えありの窓・<c>FirstOrDefaultAsync</c> の Skip・一致なしの null も FOR JSON 経路と同じ結果になる。
    /// </summary>
    [Fact(
        DisplayName = "[SqlServerPlainQuery] 2: 並び替えなしの Take / Skip は ORDER BY (SELECT NULL) を補って通る"
    )]
    public async Task PagingWithoutOrdering_UsesOffsetFetch()
    {
        await ResetAndCreateSchemaAsync();
        await SeedAsync();
        var repo = CreateRepository();

        using var capture = new SqlCommandCapture("plain_probes");

        var window = await repo.Query().Skip(1).Take(2).ToListAsync(Ct);
        var skipOnly = await repo.Query().Skip(3).ToListAsync(Ct);
        var takeOnly = await repo.Query().Take(1).ToListAsync(Ct);
        var firstAfterSkip = await repo.Query().Skip(2).FirstOrDefaultAsync(Ct);
        var sql = capture.TakeAll();

        window.Should().HaveCount(2);
        skipOnly.Should().HaveCount(1);
        takeOnly.Should().HaveCount(1);
        firstAfterSkip.Should().NotBeNull();
        sql.Should()
            .AllSatisfy(text =>
            {
                text.Should().Contain("ORDER BY (SELECT NULL) OFFSET");
                text.Should().NotContain("FOR JSON");
            });

        // 並び替えありの窓と、一致なしの FirstOrDefaultAsync
        var ordered = await repo.Query()
            .OrderByDescending(x => x.Id)
            .Skip(1)
            .Take(2)
            .ToListAsync(Ct);
        ordered.Select(x => x.Id).Should().Equal(3, 2);
        (await repo.Query().OrderBy(x => x.Id).Skip(1).FirstOrDefaultAsync(Ct))!.Id.Should().Be(2);
        (await repo.Query().Where(x => x.Id == 99).FirstOrDefaultAsync(Ct)).Should().BeNull();
        (await repo.Query().Where(x => x.Id == 99).ToListAsync(Ct)).Should().BeEmpty();
    }

    // ==================== 読んだ値 ====================

    /// <summary>
    /// 3. Include の無い <c>Query()</c>（プレーン SELECT）・<c>GetAllAsync</c>・Include のある <c>Query()</c>（FOR JSON）の
    /// ルートと子が、型の境界値と NULL で列ごとに同じ値（＝格納値）を返し、どれも <c>RowState=Unchanged</c>、
    /// Include していないナビゲーションと子が 0 件の Include は null でない空のコレクションになる。
    /// </summary>
    /// <remarks>
    /// FOR JSON をそのまま読むと 2 か所で値がずれる（実測）。ここではそれが起きないことを名指しで確かめる。
    /// <list type="bullet">
    ///   <item>float / real：FOR JSON は float を有効数字 16 桁・real を 8 桁で書くので、<c>0.1 + 0.2</c> が 0.3 に、
    ///     double の最大値が無限大に、9 桁が要る単精度の <c>10.0001545</c> が <c>10.000154</c> になる。射影でこの 2 型の列だけ
    ///     <c>CONVERT(varchar(30), 列, 3)</c>（17 桁）にして読む。判定は <c>[SqlColumnType]</c>、無ければ CLR の型（子の列は
    ///     属性なしで、値オブジェクトの内側の double も含めて CLR の型の側を通る）。</item>
    ///   <item>エンティティでは NOT NULL の string・DB の列は NULL（<c>label</c> / <c>note</c>）：FOR JSON は NULL の列をキーごと
    ///     出さないので、読む前に SELECT 列を既定値へ戻さないと初期化子の <c>string.Empty</c> が残る。</item>
    /// </list>
    /// 旧 datetime（1/300 秒）・money・smallmoney・datetimeoffset・time・負のゼロは FOR JSON も同じ値を書く（実測）。
    /// 予約語の列名（<c>group</c>）と <c>]</c> を含む列名（<c>x]y</c>）も入れてあり、FOR JSON の別名とプレーン SELECT の
    /// 列リストの両方で引用が効くことを兼ねて確かめる（Include の無い <c>Query()</c> が FOR JSON を通らなくなったため、
    /// FOR JSON の列の別名の引用はここと Include のあるテストで見る）。
    /// </remarks>
    [Fact(
        DisplayName = "[SqlServerPlainQuery] 3: Include の有無に依らず Query() は GetAllAsync と同じ値を返す"
    )]
    public async Task Query_ReadsSameValuesAsGetAll_WithAndWithoutInclude()
    {
        await ResetAndCreateSchemaAsync();
        await SeedAsync();
        var repo = CreateRepository();

        var plain = await repo.Query().OrderBy(x => x.Id).ToListAsync(Ct);
        var all = (await repo.GetAllAsync(Ct)).OrderBy(x => x.Id).ToList();
        var forJson = await repo.Query()
            .OrderBy(x => x.Id)
            .Include(x => x.Children)
            .ToListAsync(Ct);
        var allChildren = (
            await new ReadPathProbeChildRepository(
                new SqlConnectionFactory(Fixture.ConnectionString)
            ).GetAllAsync(Ct)
        )
            .OrderBy(x => x.Id)
            .ToList();
        var forJsonChildren = forJson.SelectMany(x => x.Children).OrderBy(x => x.Id).ToList();

        plain.Should().HaveCount(4);
        plain
            .Select(Snapshot)
            .Should()
            .BeEquivalentTo(all.Select(Snapshot), o => o.WithStrictOrdering());
        forJson
            .Select(Snapshot)
            .Should()
            .BeEquivalentTo(all.Select(Snapshot), o => o.WithStrictOrdering());
        forJsonChildren.Should().HaveCount(3);
        forJsonChildren
            .Select(Snapshot)
            .Should()
            .BeEquivalentTo(allChildren.Select(Snapshot), o => o.WithStrictOrdering());

        // float / real の境界値と NULL：プレーン SELECT と FOR JSON のルートが同じ格納値を読む
        foreach (var (rows, path) in new[] { (plain, "plain"), (forJson, "FOR JSON") })
        {
            rows.Single(x => x.Id == 2).Dbl.Should().Be(double.MaxValue, path);
            rows.Single(x => x.Id == 3).Dbl.Should().Be(0.1 + 0.2, path);
            rows.Single(x => x.Id == 3).Sgl.Should().Be(10.0001545f, path);
            rows.Single(x => x.Id == 2).Sgl.Should().Be(float.MaxValue, path);
            rows.Single(x => x.Id == 1).Ratio!.Value.Should().Be(0.1 + 0.2, path);
            rows.Single(x => x.Id == 2).Ratio!.Value.Should().Be(double.MinValue, path);
            rows.Single(x => x.Id == 3).Ratio!.Value.Should().Be(2.2250738585072014E-308, path);
            rows.Single(x => x.Id == 4).Label.Should().BeNull(path);
        }

        // 子（FOR JSON・列は属性なし＝CLR の型で判定）：負のゼロ・最小の正規化数・最大値・値オブジェクト・NULL
        var negativeZero = forJsonChildren.Single(x => x.Id == 10);
        negativeZero.Dbl.Should().Be(0);
        double.IsNegative(negativeZero.Dbl!.Value).Should().BeTrue();
        negativeZero.Sgl.Should().Be(10.0001545f);
        negativeZero.Ratio!.Value.Should().Be(0.1 + 0.2);
        var extremes = forJsonChildren.Single(x => x.Id == 11);
        extremes.Dbl.Should().Be(2.2250738585072014E-308);
        extremes.Sgl.Should().Be(float.MaxValue);
        extremes.Ratio!.Value.Should().Be(double.MaxValue);
        extremes.Note.Should().BeNull();
        forJsonChildren.Single(x => x.Id == 12).Sgl.Should().BeNull();

        // RowState とナビゲーションの初期状態（後続の SaveAsync の挙動が経路で変わらないこと）
        foreach (var entity in plain.Concat(all).Concat(forJson))
        {
            entity.RowState.Should().Be(RowState.Unchanged);
            entity.Tags.Should().NotBeNull().And.BeEmpty();
        }

        forJsonChildren.Should().OnlyContain(child => child.RowState == RowState.Unchanged);

        foreach (var entity in plain.Concat(all))
        {
            entity.Children.Should().NotBeNull().And.BeEmpty();
        }

        forJson.Single(x => x.Id == 1).Children.Select(c => c.Id).Should().BeEquivalentTo([10, 11]);

        // 子が 0 件の Include も null でない空のコレクション
        forJson.Single(x => x.Id == 2).Children.Should().NotBeNull().And.BeEmpty();
        forJson.Single(x => x.Id == 4).Children.Should().NotBeNull().And.BeEmpty();

        // FirstOrDefaultAsync も同じ読み方（Include の有無とも）
        var first = await repo.Query().Where(x => x.Id == 1).FirstOrDefaultAsync(Ct);
        Snapshot(first!).Should().BeEquivalentTo(Snapshot(plain[0]));
        first!.RowState.Should().Be(RowState.Unchanged);
        var firstWithInclude = await repo.Query()
            .Where(x => x.Id == 4)
            .Include(x => x.Children)
            .FirstOrDefaultAsync(Ct);
        Snapshot(firstWithInclude!).Should().BeEquivalentTo(Snapshot(plain[3]));
    }

    /// <summary>エンティティの列の値を「プロパティ名 → 比較できる値」へ写す（バイト列は 16 進文字列）</summary>
    private static Dictionary<string, object?> Snapshot(EntityBase entity) =>
        entity
            .GetType()
            .GetProperties()
            .Where(property => property.GetCustomAttribute<ColumnAttribute>() is not null)
            .ToDictionary(
                property => property.Name,
                property =>
                    property.GetValue(entity) switch
                    {
                        byte[] bytes => Convert.ToHexString(bytes),
                        var value => value,
                    }
            );

    /// <summary>射影のフォールバックを起こすためのセレクタ本体（エンティティを丸ごと受けるので列を取り出せない）</summary>
    private static string Describe(ReadPathProbeEntity entity) => $"{entity.Id}:{entity.Text}";

    /// <summary>
    /// SqlClient が発行するコマンドの本文を DiagnosticListener で捕まえる（指定した語を含むものだけ）。
    /// </summary>
    /// <remarks>
    /// リスナーはプロセス全体に効くので、ほかのコレクションのテストが同時に発行したコマンドを拾わないよう、
    /// このスイートの表名を含むものだけを集める。
    /// </remarks>
    private sealed class SqlCommandCapture
        : IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>,
            IDisposable
    {
        /// <summary>集める対象のコマンドが含む語</summary>
        private readonly string _marker;

        /// <summary>捕まえたコマンドの本文</summary>
        private readonly List<string> _commands = [];

        /// <summary>購読の解除に使うハンドル</summary>
        private readonly List<IDisposable> _subscriptions = [];

        public SqlCommandCapture(string marker)
        {
            _marker = marker;
            _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        }

        /// <summary>捕まえたコマンドを取り出して空にする</summary>
        public List<string> TakeAll()
        {
            lock (_commands)
            {
                var taken = _commands.ToList();
                _commands.Clear();
                return taken;
            }
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "SqlClientDiagnosticListener")
            {
                lock (_subscriptions)
                {
                    _subscriptions.Add(value.Subscribe(this));
                }
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal))
            {
                return;
            }

            var command =
                value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value)
                as System.Data.Common.DbCommand;

            if (
                command?.CommandText is { } text
                && text.Contains(_marker, StringComparison.Ordinal)
            )
            {
                lock (_commands)
                {
                    _commands.Add(text);
                }
            }
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                foreach (var subscription in _subscriptions)
                {
                    subscription.Dispose();
                }

                _subscriptions.Clear();
            }
        }
    }
}

/// <summary>プレーン SELECT と FOR JSON の読み方を比べるためのプローブ（手書き・SQL Server の型の境界を網羅）</summary>
[Table("plain_probes")]
public sealed class ReadPathProbeEntity : EntityBase
{
    [Key]
    [Column("id")]
    [SqlColumnType(SqlDbType.Int)]
    public int Id { get; set; }

    [Column("legacy_at")]
    [SqlColumnType(SqlDbType.DateTime)]
    public DateTime? LegacyAt { get; set; }

    [Column("precise_at")]
    [SqlColumnType(SqlDbType.DateTime2)]
    public DateTime? PreciseAt { get; set; }

    [Column("small_at")]
    [SqlColumnType(SqlDbType.SmallDateTime)]
    public DateTime? SmallAt { get; set; }

    [Column("day")]
    [SqlColumnType(SqlDbType.Date)]
    public DateTime? Day { get; set; }

    [Column("clock")]
    [SqlColumnType(SqlDbType.Time)]
    public TimeSpan? Clock { get; set; }

    [Column("offset_at")]
    [SqlColumnType(SqlDbType.DateTimeOffset)]
    public DateTimeOffset? OffsetAt { get; set; }

    [Column("money_amount")]
    [SqlColumnType(SqlDbType.Money)]
    public decimal? MoneyAmount { get; set; }

    [Column("small_money_amount")]
    [SqlColumnType(SqlDbType.SmallMoney)]
    public decimal? SmallMoneyAmount { get; set; }

    [Column("exact")]
    [SqlColumnType(SqlDbType.Decimal, Precision = 28, Scale = 10)]
    public decimal? Exact { get; set; }

    [Column("dbl")]
    [SqlColumnType(SqlDbType.Float)]
    public double? Dbl { get; set; }

    [Column("sgl")]
    [SqlColumnType(SqlDbType.Real)]
    public float? Sgl { get; set; }

    [Column("flag")]
    [SqlColumnType(SqlDbType.Bit)]
    public bool? Flag { get; set; }

    [Column("tiny")]
    [SqlColumnType(SqlDbType.TinyInt)]
    public byte? Tiny { get; set; }

    [Column("small")]
    [SqlColumnType(SqlDbType.SmallInt)]
    public short? Small { get; set; }

    [Column("big")]
    [SqlColumnType(SqlDbType.BigInt)]
    public long? Big { get; set; }

    [Column("text")]
    [SqlColumnType(SqlDbType.NVarChar, Size = -1)]
    public string? Text { get; set; }

    [Column("fixed")]
    [SqlColumnType(SqlDbType.NChar, Size = 10)]
    public string? Fixed { get; set; }

    [Column("ansi")]
    [SqlColumnType(SqlDbType.VarChar, Size = 50)]
    public string? Ansi { get; set; }

    [Column("uid")]
    [SqlColumnType(SqlDbType.UniqueIdentifier)]
    public Guid? Uid { get; set; }

    [Column("bin")]
    [SqlColumnType(SqlDbType.VarBinary, Size = 50)]
    public byte[]? Bin { get; set; }

    [Column("fixed_bin")]
    [SqlColumnType(SqlDbType.Binary, Size = 4)]
    public byte[]? FixedBin { get; set; }

    /// <summary>値オブジェクトの列（GeneratedFixture の NameValue＝varchar(50)）</summary>
    [Column("name")]
    [SqlColumnType(SqlDbType.VarChar, Size = 50)]
    public NameValue? Name { get; set; }

    /// <summary>エンティティでは NOT NULL・DB の列は NULL を許す（取込の後に DB 側が変わった形）</summary>
    [Column("label")]
    [SqlColumnType(SqlDbType.NVarChar, Size = 20)]
    public string Label { get; set; } = string.Empty;

    /// <summary>予約語の列名（FOR JSON の別名・プレーン SELECT の列リストの引用を通す）</summary>
    [Column("group")]
    [SqlColumnType(SqlDbType.NVarChar, Size = 10)]
    public string? Group { get; set; }

    /// <summary>閉じ角括弧を含む列名（FOR JSON の別名・プレーン SELECT の列リストで <c>]</c> を二重にする）</summary>
    [Column("x]y")]
    [SqlColumnType(SqlDbType.NVarChar, Size = 10)]
    public string? XY { get; set; }

    /// <summary>double を包む値オブジェクトの列（float・<c>[SqlColumnType]</c> あり）</summary>
    [Column("ratio")]
    [SqlColumnType(SqlDbType.Float)]
    public ProbeRatioValue? Ratio { get; set; }

    [NavigationReference(
        "plain_probes",
        "id",
        "plain_probe_children",
        "probe_id",
        true,
        false,
        false
    )]
    public ICollection<ReadPathProbeChildEntity> Children { get; set; } =
        new List<ReadPathProbeChildEntity>();

    [NavigationReference("plain_probes", "id", "plain_probe_tags", "probe_id", true, false, false)]
    public ICollection<ReadPathProbeTagEntity> Tags { get; set; } =
        new List<ReadPathProbeTagEntity>();
}

/// <summary>プローブの子（Include して FOR JSON 経路を通すため）</summary>
[Table("plain_probe_children")]
public sealed class ReadPathProbeChildEntity : EntityBase
{
    [Key]
    [Column("id")]
    [SqlColumnType(SqlDbType.Int)]
    public int Id { get; set; }

    [Column("probe_id")]
    [SqlColumnType(SqlDbType.Int)]
    public int ProbeId { get; set; }

    // 以下の列は [SqlColumnType] を持たない＝FOR JSON の射影は CLR の型で float / real の列を見分ける

    [Column("dbl")]
    public double? Dbl { get; set; }

    [Column("sgl")]
    public float? Sgl { get; set; }

    /// <summary>double を包む値オブジェクトの列（属性なし＝値オブジェクトの内側の型で判定する）</summary>
    [Column("ratio")]
    public ProbeRatioValue? Ratio { get; set; }

    /// <summary>エンティティでは NOT NULL・DB の列は NULL を許す</summary>
    [Column("note")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>プローブのもう 1 つの子（Include しないナビゲーションの初期状態を見るため）</summary>
[Table("plain_probe_tags")]
public sealed class ReadPathProbeTagEntity : EntityBase
{
    [Key]
    [Column("id")]
    [SqlColumnType(SqlDbType.Int)]
    public int Id { get; set; }

    [Column("probe_id")]
    [SqlColumnType(SqlDbType.Int)]
    public int ProbeId { get; set; }
}

/// <summary>プローブのリポジトリ（QuickER の SQL Server 実装）</summary>
public sealed class ReadPathProbeRepository(ISqlConnectionFactory connectionFactory)
    : SqlServerRepository<ReadPathProbeEntity, int>(connectionFactory);

/// <summary>プローブの子のリポジトリ（子の格納値を GetAllAsync で読むため）</summary>
public sealed class ReadPathProbeChildRepository(ISqlConnectionFactory connectionFactory)
    : SqlServerRepository<ReadPathProbeChildEntity, int>(connectionFactory);

/// <summary>double を包む手書きの値オブジェクト（float 列の読み取りを値オブジェクト越しに確かめるため）</summary>
public sealed class ProbeRatioValue
    : ValueObjectOrderedBase<ProbeRatioValue, double>,
        IValueObject<ProbeRatioValue, double>
{
    private ProbeRatioValue(double value)
        : base(value) { }

    static ProbeRatioValue IValueObject<ProbeRatioValue, double>.New(double value) => new(value);
}
