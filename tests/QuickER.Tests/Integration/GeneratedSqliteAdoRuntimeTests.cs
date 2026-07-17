using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqliteFixture;

namespace QuickER.Tests.Integration;

/// <summary>
/// SQLite 方言ランタイムスイートを<b>QuickER の <c>SqliteRepository</c> 版</b>で実行する派生。
/// リポジトリ・エグゼキュータは実運用と同じ DI 経路（<c>AddGeneratedSqliteRepositories(connectionString)</c> →
/// <see cref="ServiceProvider"/> から解決）で取得する。接続は一時ファイル DB の書き込み可能接続文字列。
/// </summary>
/// <remarks>
/// これは Phase T の「SQLite QuickER 版 Repository の方言ランタイムテスト」に相当する。基底
/// <see cref="GeneratedSqliteRuntimeTestsBase"/> の全シナリオ（CRUD・式木・ページング・Include マルチクエリ・
/// 生 SQL・グラフ保存・BulkInsert・削除カスケード）を、SQLite 固有の実行経路（プレーン SELECT＋DataReader・
/// LIMIT/OFFSET・IncludeLoader・AddWithValue・1Tx INSERT ループ）で流す。
/// </remarks>
public sealed class GeneratedSqliteAdoRuntimeTests : GeneratedSqliteRuntimeTestsBase
{
    /// <summary>QuickER の SQLite リポジトリ群を登録した DI コンテナ（接続文字列は基底の一時 DB）</summary>
    private ServiceProvider? _provider;

    /// <summary>AddGeneratedSqliteRepositories → QuickER の SqliteRepository の DI 経路でリポジトリ群を解決する</summary>
    private ServiceProvider Provider() =>
        _provider ??= new ServiceCollection()
            .AddGeneratedSqliteRepositories(ConnectionString)
            .BuildServiceProvider();

    protected override ICustomerRepository CreateCustomerRepository() =>
        Provider().GetRequiredService<ICustomerRepository>();

    protected override IOrderRepository CreateOrderRepository() =>
        Provider().GetRequiredService<IOrderRepository>();

    protected override ISqlExecutor CreateSqlExecutor() =>
        Provider().GetRequiredService<ISqlExecutor>();

    /// <summary>
    /// 追加（QuickER の SQLite のみ）: 式木トランスレータが日付部品（Year/Month/Day/Hour/Minute/Second/DayOfYear）へ
    /// 生成する strftime フラグメントが、実 SQLite の ISO8601 TEXT に対して正しい整数値を返すことを検証する。
    /// </summary>
    /// <remarks>
    /// <b>制約と代替検証</b>: 第3フィクスチャの入力は方言可搬な図（<see cref="SqlitePortableFixtureDefinition"/>）で、
    /// DateTime 列を持たない。そのため式木クエリ API（<c>Where(x =&gt; x.Col.Year == ...)</c>）から翻訳器を通す
    /// 実データ検証ができない。翻訳器が DateTime 列参照に対して生成する SQL フラグメントそのもの
    /// （<c>CAST(strftime('%Y', "col") AS INTEGER)</c> など。テンプレート <c>CSharpRuntime/*.scriban</c> の
    /// <c>TryGetDatePart</c> と一致）を、DateTime 列を持つ一時テーブルへ ISO8601 TEXT を格納したうえで
    /// <c>ExecuteScalarSqlAsync&lt;int&gt;</c> で実行し、部品の実整数値を検証する。フラグメントの生成側（式木からの
    /// 吐き分け）は <c>SqliteRepositoryDialectTests</c>（生成テキストに <c>strftime(</c> が現れること）と
    /// Roslyn コンパイル検証が守る。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite] 追加: 式木の日付部品が生成する strftime フラグメントが実 ISO8601 TEXT で正しい整数を返す"
    )]
    public async Task DateParts_StrftimeFragments_ReturnCorrectIntegersOnRealData()
    {
        await ResetAndCreateSchemaAsync();

        // DateTime 列（ISO8601 TEXT）を持つ検証専用テーブルを用意する（EF Core Sqlite の DateTime 格納規約と同じ表記）
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);
            await using var create = conn.CreateCommand();
            create.CommandText =
                "CREATE TABLE \"events\" (\"event_id\" INTEGER PRIMARY KEY, \"occurred_at\" TEXT NOT NULL);"
                + "INSERT INTO \"events\" (\"event_id\", \"occurred_at\") VALUES (1, '2026-07-05 13:47:09');";
            await create.ExecuteNonQueryAsync(Ct);
        }

        var executor = CreateSqlExecutor();

        // 翻訳器が生成する各日付部品フラグメント（列名 occurred_at）を検証する
        async Task<int> PartAsync(string fragment) =>
            await executor.ExecuteScalarSqlAsync<int>(
                $"SELECT {fragment} FROM \"events\" WHERE \"event_id\" = 1",
                null,
                Ct
            );

        (await PartAsync("CAST(strftime('%Y', \"occurred_at\") AS INTEGER)")).Should().Be(2026);
        (await PartAsync("CAST(strftime('%m', \"occurred_at\") AS INTEGER)")).Should().Be(7);
        (await PartAsync("CAST(strftime('%d', \"occurred_at\") AS INTEGER)")).Should().Be(5);
        (await PartAsync("CAST(strftime('%H', \"occurred_at\") AS INTEGER)")).Should().Be(13);
        (await PartAsync("CAST(strftime('%M', \"occurred_at\") AS INTEGER)")).Should().Be(47);
        (await PartAsync("CAST(strftime('%S', \"occurred_at\") AS INTEGER)")).Should().Be(9);
        // 2026-07-05 は年初から 186 日目
        (await PartAsync("CAST(strftime('%j', \"occurred_at\") AS INTEGER)"))
            .Should()
            .Be(186);
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
