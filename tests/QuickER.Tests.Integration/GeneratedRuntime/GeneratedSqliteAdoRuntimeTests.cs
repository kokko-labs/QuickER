using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedSqliteFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

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

    /// <summary>列判定用のプローブ（プロパティ名 A/B がそのまま列名になる）</summary>
    private sealed class ProbeRow
    {
        public string A { get; set; } = string.Empty;
        public string B { get; set; } = string.Empty;
    }

    /// <summary>
    /// 追加（QuickER の SQLite のみ）: 翻訳器が「値の位置に列参照」を置いた式へ生成する条件文字列
    /// （列同士の LIKE 系・等値）が、実 SQLite に対して意図どおりの意味論（部分一致・ワイルドカードの
    /// リテラル扱い・引数列 NULL は不一致・StartsWith/EndsWith/Equals/IgnoreCase）で判定されることを検証する。
    /// </summary>
    /// <remarks>
    /// <see cref="DateParts_StrftimeFragments_ReturnCorrectIntegersOnRealData"/> と同型のアプローチで、
    /// 2 文字列列（A/B）の一時テーブルを用意し、<b>翻訳器の <c>ToCondition</c> が実際に生成した条件文字列</b>を
    /// <c>SELECT COUNT(*) FROM ... WHERE {condition}</c> に埋めて実行する。式は SQLite フィクスチャの
    /// 翻訳器（二重引用符・<c>||</c> 連結）を通す。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite] 追加: 翻訳器が生成する列同士の LIKE 系・等値条件が実 DB で正しい意味論を返す"
    )]
    public async Task ColumnArgumentConditions_HaveCorrectSemanticsOnRealData()
    {
        await ResetAndCreateSchemaAsync();

        // 2 文字列列（A/B。ProbeRow のプロパティ名と一致）を持つ検証専用テーブルを用意する
        await using (var conn = new SqliteConnection(ConnectionString))
        {
            await conn.OpenAsync(Ct);
            await using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE \"probe\" (\"A\" TEXT, \"B\" TEXT);";
            await create.ExecuteNonQueryAsync(Ct);
        }

        var executor = CreateSqlExecutor();

        // 翻訳器（SQLite 方言）が述語本体から生成する条件文字列を取り出す
        static string Condition(Expression<Func<ProbeRow, bool>> predicate) =>
            SqlExpressionTranslator.ToCondition(predicate.Body, new List<SqlQueryParameter>());

        // 単一行を差し替え、条件に一致するかを COUNT で確かめる（引数列 B は NULL も渡せるよう分岐する）
        async Task<int> MatchesAsync(string condition, string a, string? b)
        {
            await executor.ExecuteSqlAsync("DELETE FROM \"probe\"", null, Ct);

            if (b is null)
            {
                await executor.ExecuteSqlAsync(
                    "INSERT INTO \"probe\" (\"A\", \"B\") VALUES (@a, NULL)",
                    new { a },
                    Ct
                );
            }
            else
            {
                await executor.ExecuteSqlAsync(
                    "INSERT INTO \"probe\" (\"A\", \"B\") VALUES (@a, @b)",
                    new { a, b },
                    Ct
                );
            }

            return await executor.ExecuteScalarSqlAsync<int>(
                $"SELECT COUNT(*) FROM \"probe\" WHERE {condition}",
                null,
                Ct
            );
        }

        // --- Contains（部分一致・ワイルドカードのリテラル扱い・引数列 NULL は不一致） ---
        var contains = Condition(p => p.A.Contains(p.B));
        (await MatchesAsync(contains, "foobar", "oob")).Should().Be(1, "B の値が A に含まれる");
        (await MatchesAsync(contains, "x10%y", "10%"))
            .Should()
            .Be(1, "% はリテラル扱いなので A に含まれる");
        (await MatchesAsync(contains, "10a", "10%"))
            .Should()
            .Be(0, "% がワイルドカードなら誤って一致してしまう");
        (await MatchesAsync(contains, "xa_cy", "a_c"))
            .Should()
            .Be(1, "_ はリテラル扱いなので A に含まれる");
        (await MatchesAsync(contains, "xabcy", "a_c"))
            .Should()
            .Be(0, "_ がワイルドカードなら誤って一致してしまう");
        (await MatchesAsync(contains, "hello", null)).Should().Be(0, "引数列が NULL の行は不一致");

        // --- StartsWith / EndsWith ---
        var startsWith = Condition(p => p.A.StartsWith(p.B));
        (await MatchesAsync(startsWith, "abcdef", "abc")).Should().Be(1);
        (await MatchesAsync(startsWith, "xabc", "abc")).Should().Be(0);

        var endsWith = Condition(p => p.A.EndsWith(p.B));
        (await MatchesAsync(endsWith, "abcdef", "def")).Should().Be(1);
        (await MatchesAsync(endsWith, "defx", "def")).Should().Be(0);

        // --- Equals / Equals(IgnoreCase） ---
        var equals = Condition(p => p.A.Equals(p.B));
        (await MatchesAsync(equals, "same", "same")).Should().Be(1);
        (await MatchesAsync(equals, "a", "b")).Should().Be(0);

        var equalsIgnoreCase = Condition(p => p.A.Equals(p.B, StringComparison.OrdinalIgnoreCase));
        (await MatchesAsync(equalsIgnoreCase, "ABC", "abc"))
            .Should()
            .Be(1, "IgnoreCase は両辺を LOWER で畳むため一致する");
    }

    /// <summary>
    /// 追加（QuickER の SQLite のみ）: 自列参照の Equals（<c>o.Memo.Equals(o.Memo)</c>）が、QuickER 版の
    /// SQL null 意味論（<c>[memo] = [memo]</c> ＝ NULL 行は不一致）で非 NULL 行のみ返すことを検証する。
    /// </summary>
    /// <remarks>
    /// <b>ADO 専用の理由</b>: EF Core は <c>Equals</c> を C# の null 等価（両辺 NULL を等しいとみなす）で翻訳し、
    /// <c>[memo] = [memo] OR ([memo] IS NULL AND [memo] IS NULL)</c> 相当を生成するため NULL 行も含めてしまう。
    /// QuickER 版は素の <c>[memo] = [memo]</c>（<c>NULL = NULL</c> は NULL＝不一致）なので NULL 行を除外する。
    /// この差は両バックエンドで観測結果が割れるため、パリティ基底ではなく ADO 専用テストとして置く
    /// （自列 Contains は左辺列も NULL となり両者一致するため基底 <c>Where_SelfColumnContains_ReturnsNonNullRows</c> に置く）。
    /// </remarks>
    [Fact(
        DisplayName = "[SQLite] 追加: 自列参照の Equals は QuickER の SQL null 意味論で非 NULL 行のみ返す（ADO 専用）"
    )]
    public async Task SelfColumnEquals_ExcludesNullRows()
    {
        await ResetAndCreateSchemaAsync();

        var customers = CreateCustomerRepository();
        var orders = CreateOrderRepository();

        await customers.InsertAsync(NewCustomer(1, "Alice"), Ct);
        await orders.InsertAsync(NewOrder(10, 1, 10m, "abc"), Ct);
        await orders.InsertAsync(NewOrder(11, 1, 20m, "xyz"), Ct);
        await orders.InsertAsync(NewOrder(12, 1, 30m, memo: null), Ct); // memo が NULL の行

        // 自列 Equals: [memo] = [memo] ＝ 非 NULL の全行が一致し、NULL 行（NULL = NULL）は不一致
        var selfEquals = await orders.Query().Where(o => o.Memo!.Equals(o.Memo!)).ToListAsync(Ct);
        selfEquals.Select(o => o.OrderId.Value).Should().BeEquivalentTo([10, 11]);
    }

    /// <summary>
    /// 追加（QuickER の SQLite のみ）: VO の検証に合わない値が DB に入っていたときの読み取り例外が、
    /// 「どの列で失敗したか」を含む <see cref="InvalidOperationException"/> になることを検証する。
    /// </summary>
    /// <remarks>
    /// VO の再構築は <c>SqlValueObjectActivator</c> が型ごとに 1 回だけコンパイルする式木デリゲート経由の
    /// <c>Create</c> 呼び出しで、デリゲート呼び出しは <c>TargetInvocationException</c> に包まないため、
    /// 検証失敗はそのままの例外として出る（リフレクション <c>Invoke</c> なら包まれて列も型も分からなくなる）。
    /// これを行マッピング側が列名・プロパティ名を添えて包み直す（元の例外は InnerException に残る）。
    /// SQLite は宣言長を強制しないため、生 SQL で 50 文字上限を超える名前を書き込んで再現する。
    /// </remarks>
    [Fact(DisplayName = "[SQLite] 追加: VO 検証に合わない DB 値の読み取り例外に列名が含まれる")]
    public async Task ValueObjectRestoreFailure_NamesTheColumn()
    {
        await ResetAndCreateSchemaAsync();

        var executor = CreateSqlExecutor();

        // name は nvarchar(50)＝VO の上限 50 文字。SQLite は長さを強制しないのでそのまま書き込める
        await executor.ExecuteSqlAsync(
            "INSERT INTO \"customers\" (\"customer_id\", \"name\", \"balance\") VALUES (1, @name, NULL)",
            new { name = new string('x', 60) },
            Ct
        );

        var repo = CreateCustomerRepository();
        var read = async () => await repo.QueryBySqlAsync("SELECT * FROM \"customers\"", null, Ct);

        var thrown = await read.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("name", "失敗した列名が示される");
        thrown.Which.Message.Should().Contain("NameValue", "対象の VO 型も示される");
        thrown
            .Which.InnerException.Should()
            .NotBeNull()
            .And.NotBeOfType<System.Reflection.TargetInvocationException>(
                "式木デリゲート経由なのでリフレクションの包装が挟まらない"
            );
    }

    public override void Dispose()
    {
        _provider?.Dispose();
        base.Dispose();
    }
}
