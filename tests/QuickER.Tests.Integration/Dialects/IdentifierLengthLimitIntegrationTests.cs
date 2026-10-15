using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>識別子の長さの上限の実測で使う名前の組み立て</summary>
internal static class IdentifierLengthProbe
{
    public const string Kanji = "表";
    public const string AUmlaut = "Ä";
    public const string Emoji = "😀";

    public static string Repeat(string text, int count) =>
        string.Concat(Enumerable.Repeat(text, count));

    /// <summary>名前の種類（表・列・制約）</summary>
    public enum Kind
    {
        Table,
        Column,
        Constraint,
    }

    /// <summary>上限の計算が示す答え（通るか）</summary>
    public static bool Expected(IdentifierLengthLimit limit, Kind kind, string name) =>
        kind == Kind.Table ? limit.FitsTableName(name) : limit.Fits(name);

    /// <summary>食い違いの説明（失敗メッセージ用。長い名前は単位と長さで示す）</summary>
    public static string Describe(Kind kind, string name, bool expected, bool actual) =>
        $"{kind} [{name.Length} UTF-16 / {Encoding.UTF8.GetByteCount(name)} bytes / "
        + $"file {MySqlFileNameEncoding.EncodedLength(name)}] {Preview(name)}: "
        + $"expected {(expected ? "accepted" : "rejected")}, DB {(actual ? "accepted" : "rejected")}";

    private static string Preview(string name) =>
        name.Length <= 12 ? name : name[..6] + "…" + name[^6..];

    /// <summary>SQL を実行し、DB が断ったら false を返す</summary>
    public static async Task<bool> TryAsync(
        DbConnection connection,
        string sql,
        CancellationToken ct
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        try
        {
            await command.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }
}

/// <summary>
/// SQL Server の識別子の長さの上限（128 の UTF-16 コード単位）が実 DB と一致することを検証する統合テスト
/// </summary>
/// <remarks>上限の宣言は <see cref="IdentifierLengthLimits.SqlServer"/>。Docker 必須（<see cref="SqlServerContainerFixture"/>）。</remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerIdentifierLengthLimitIntegrationTests(
    SqlServerContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Integration] SQL Server: 上限の計算と、DB が名前を受け付けるか断るかが一致する"
    )]
    public async Task Limit_MatchesDatabase()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await using var connection = await fixture.OpenConnectionAsync(Ct);

        string[] names =
        [
            IdentifierLengthProbe.Repeat("a", 128),
            IdentifierLengthProbe.Repeat("a", 129),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Kanji, 128),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Kanji, 129),
            IdentifierLengthProbe.Repeat("a", 126) + IdentifierLengthProbe.Emoji,
            IdentifierLengthProbe.Repeat("a", 127) + IdentifierLengthProbe.Emoji,
        ];

        var mismatches = new List<string>();

        foreach (var name in names)
        {
            var q = SqlIdentifier.BracketSimple(name);
            var cases = new (IdentifierLengthProbe.Kind Kind, string Create)[]
            {
                (IdentifierLengthProbe.Kind.Table, $"CREATE TABLE {q} (id int);"),
                (IdentifierLengthProbe.Kind.Column, $"CREATE TABLE probe ({q} int);"),
                (
                    IdentifierLengthProbe.Kind.Constraint,
                    $"CREATE TABLE probe (id int NOT NULL, CONSTRAINT {q} PRIMARY KEY (id));"
                ),
            };

            foreach (var (kind, create) in cases)
            {
                var expected = IdentifierLengthProbe.Expected(SqlIdentifier.Limit, kind, name);
                var actual = await IdentifierLengthProbe.TryAsync(connection, create, Ct);

                if (actual != expected)
                {
                    mismatches.Add(IdentifierLengthProbe.Describe(kind, name, expected, actual));
                }

                await fixture.ResetSchemaAsync(Ct);
            }
        }

        mismatches.Should().BeEmpty();
    }
}

/// <summary>
/// PostgreSQL の識別子の長さの上限（63 バイト）が実 DB と一致することを検証する統合テスト
/// </summary>
/// <remarks>
/// PostgreSQL は上限を超える名前を断らずに黙って切り詰めるので、「名前がそのまま残ったか」で通ったかを判定する。
/// 上限の宣言は <see cref="IdentifierLengthLimits.PostgreSql"/>。Docker 必須（<see cref="PostgreSqlContainerFixture"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlIdentifierLengthLimitIntegrationTests(
    PostgreSqlContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Integration] PostgreSQL: 上限の計算と、名前が切り詰められずに残るかが一致する"
    )]
    public async Task Limit_MatchesDatabase()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await using var connection = await fixture.OpenConnectionAsync(Ct);

        string[] names =
        [
            IdentifierLengthProbe.Repeat("a", 63),
            IdentifierLengthProbe.Repeat("a", 64),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Kanji, 21),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Kanji, 22),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.AUmlaut, 31) + "a",
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.AUmlaut, 32),
            IdentifierLengthProbe.Repeat("a", 59) + IdentifierLengthProbe.Emoji,
            IdentifierLengthProbe.Repeat("a", 60) + IdentifierLengthProbe.Emoji,
        ];

        var mismatches = new List<string>();

        foreach (var name in names)
        {
            var q = PgIdentifier.QuoteSimple(name);
            var cases = new (IdentifierLengthProbe.Kind Kind, string Create, string Lookup)[]
            {
                (
                    IdentifierLengthProbe.Kind.Table,
                    $"CREATE TABLE {q} (id int);",
                    "SELECT count(*) FROM pg_class WHERE relname = @name AND relkind = 'r'"
                ),
                (
                    IdentifierLengthProbe.Kind.Column,
                    $"CREATE TABLE probe ({q} int);",
                    "SELECT count(*) FROM pg_attribute WHERE attname = @name AND attrelid = 'probe'::regclass"
                ),
                (
                    IdentifierLengthProbe.Kind.Constraint,
                    $"CREATE TABLE probe (id int NOT NULL, CONSTRAINT {q} PRIMARY KEY (id));",
                    "SELECT count(*) FROM pg_constraint WHERE conname = @name"
                ),
            };

            foreach (var (kind, create, lookup) in cases)
            {
                var expected = IdentifierLengthProbe.Expected(PgIdentifier.Limit, kind, name);
                (await IdentifierLengthProbe.TryAsync(connection, create, Ct))
                    .Should()
                    .BeTrue("PostgreSQL は長い名前を断らずに切り詰める");

                await using var command = new NpgsqlCommand(lookup, connection);
                command.Parameters.AddWithValue("name", name);
                var actual = (long)(await command.ExecuteScalarAsync(Ct))! == 1;

                if (actual != expected)
                {
                    mismatches.Add(IdentifierLengthProbe.Describe(kind, name, expected, actual));
                }

                await fixture.ResetSchemaAsync(Ct);
            }
        }

        mismatches.Should().BeEmpty();
    }
}

/// <summary>
/// MySQL の識別子の長さの上限（64 文字と、表の名前のファイル名での 251 バイト）と、ファイル名の符号化の移植
/// （<see cref="MySqlFileNameEncoding"/>）が実 DB と一致することを検証する統合テスト
/// </summary>
/// <remarks>上限の宣言は <see cref="IdentifierLengthLimits.MySql"/>。Docker 必須（<see cref="MySqlContainerFixture"/>）。</remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlIdentifierLengthLimitIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Integration] MySQL: 上限の計算と、DB が名前を受け付けるか断るかが一致する"
    )]
    public async Task Limit_MatchesDatabase()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await using var connection = await fixture.OpenConnectionAsync(Ct);

        var k = IdentifierLengthProbe.Kanji;
        var a = IdentifierLengthProbe.AUmlaut;
        string[] names =
        [
            IdentifierLengthProbe.Repeat("a", 64),
            IdentifierLengthProbe.Repeat("a", 65),
            IdentifierLengthProbe.Repeat(k, 50),
            IdentifierLengthProbe.Repeat(k, 51),
            IdentifierLengthProbe.Repeat(k, 64),
            IdentifierLengthProbe.Repeat(k, 65),
            IdentifierLengthProbe.Repeat(a, 64),
            IdentifierLengthProbe.Repeat(a, 65),
            IdentifierLengthProbe.Repeat("#", 50),
            IdentifierLengthProbe.Repeat("#", 51),
            "a" + IdentifierLengthProbe.Repeat("#", 50),
            "aa" + IdentifierLengthProbe.Repeat("#", 50),
            a + a + IdentifierLengthProbe.Repeat(k, 49),
            a + a + a + IdentifierLengthProbe.Repeat(k, 49),
            IdentifierLengthProbe.Repeat("Ａ", 64),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Emoji, 50),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Emoji, 51),
        ];

        var mismatches = new List<string>();

        foreach (var name in names)
        {
            var q = MySqlIdentifier.QuoteSimple(name);
            var cases = new (IdentifierLengthProbe.Kind Kind, string Create)[]
            {
                (IdentifierLengthProbe.Kind.Table, $"CREATE TABLE {q} (id int);"),
                (IdentifierLengthProbe.Kind.Column, $"CREATE TABLE probe ({q} int);"),
                (
                    IdentifierLengthProbe.Kind.Constraint,
                    $"CREATE TABLE probe (id int NOT NULL, CONSTRAINT {q} UNIQUE (id));"
                ),
            };

            foreach (var (kind, create) in cases)
            {
                var expected = IdentifierLengthProbe.Expected(MySqlIdentifier.Limit, kind, name);
                var actual = await IdentifierLengthProbe.TryAsync(connection, create, Ct);

                if (actual != expected)
                {
                    mismatches.Add(IdentifierLengthProbe.Describe(kind, name, expected, actual));
                }

                await fixture.ResetSchemaAsync(Ct);
            }
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>
    /// 移植した範囲の表（5 つの表の全符号位置と、その前後の文字）で、計算したファイル名の長さが
    /// MySQL の作るファイル名（<c>information_schema.INNODB_TABLES</c> の名前）と一致することを検証する
    /// </summary>
    /// <remarks>
    /// 同じ表へ入れる文字は、計算上の長さ（3 か 5）が同じものだけにする（食い違いが表の中で打ち消し合わないため）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] MySQL: ファイル名の符号化の長さが、移植した表の全符号位置で実 DB と一致する"
    )]
    public async Task FileNameEncoding_MatchesDatabaseForEveryTabledCodePoint()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // MySQL のソースの 5 つの表が覆う区間（表に載らない＝5 バイトの文字も区間の中にある）と、区間の外の代表
        (int First, int Last)[] spans =
        [
            (0x0080, 0x05FF),
            (0x1E00, 0x1FFF),
            (0x2160, 0x2180),
            (0x24B0, 0x24EF),
            (0xFF20, 0xFF5F),
            (0x3040, 0x3045),
            (0x8868, 0x8868),
        ];
        var codePoints = spans
            .SelectMany(s => Enumerable.Range(s.First, s.Last - s.First + 1))
            .Where(c => !char.IsControl((char)c))
            .ToList();

        var groups = codePoints
            .GroupBy(MySqlFileNameEncoding.EncodedLength)
            .SelectMany(g => g.Chunk(40).Select(chunk => (Length: g.Key, Chars: chunk)))
            .ToList();
        var database = new MySqlConnectionStringBuilder(fixture.ConnectionString).Database;
        var expectedByTable = new Dictionary<string, (int Encoded, string Chars)>();

        for (var i = 0; i < groups.Count; i++)
        {
            var prefix = $"g{i}_";
            var chars = string.Concat(groups[i].Chars.Select(char.ConvertFromUtf32));
            var tableName = prefix + chars;
            expectedByTable[prefix] = (MySqlFileNameEncoding.EncodedLength(tableName), chars);
            await fixture.ExecuteAsync(
                $"CREATE TABLE {MySqlIdentifier.QuoteSimple(tableName)} (id int);",
                Ct
            );
        }

        var actualByTable = new Dictionary<string, int>();
        await using (var root = new MySqlConnection(fixture.RootConnectionString))
        {
            await root.OpenAsync(Ct);
            await using var command = new MySqlCommand(
                "SELECT NAME FROM information_schema.INNODB_TABLES WHERE NAME LIKE @prefix",
                root
            );
            command.Parameters.AddWithValue("prefix", database + "/g%");
            await using var reader = await command.ExecuteReaderAsync(Ct);

            while (await reader.ReadAsync(Ct))
            {
                var fileName = reader.GetString(0)[(database.Length + 1)..];
                var prefix = fileName[..(fileName.IndexOf('_') + 1)];
                actualByTable[prefix] = fileName.Length;
            }
        }

        actualByTable.Keys.Should().BeEquivalentTo(expectedByTable.Keys);
        var mismatches = expectedByTable
            .Where(e => actualByTable[e.Key] != e.Value.Encoded)
            .Select(e =>
                $"{e.Key} expected {e.Value.Encoded}, DB {actualByTable[e.Key]}: "
                + string.Join(" ", e.Value.Chars.EnumerateRunes().Select(r => $"U+{r.Value:X4}"))
            )
            .ToList();

        mismatches.Should().BeEmpty();
        await fixture.ResetSchemaAsync(Ct);
    }

    /// <summary>
    /// BMP の外の文字（サロゲート対）は断られずに <c>?</c> へ置き換えられる（ファイル名の長さを <c>?</c> と同じ 5 と数える根拠）
    /// </summary>
    [Fact(DisplayName = "[Integration] MySQL: BMP の外の文字は断られずに ? へ置き換えられる")]
    public async Task SupplementaryCharacter_IsReplacedWithQuestionMark()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            $"CREATE TABLE {MySqlIdentifier.QuoteSimple("a" + IdentifierLengthProbe.Emoji + "b")} (id int);",
            Ct
        );

        await using var connection = await fixture.OpenConnectionAsync(Ct);
        await using var command = new MySqlCommand(
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()",
            connection
        );
        var tableName = (string)(await command.ExecuteScalarAsync(Ct))!;

        tableName.Should().Be("a?b");
        await fixture.ResetSchemaAsync(Ct);
    }
}

/// <summary>
/// Oracle の識別子の長さの上限（128 バイト）が実 DB と一致することを検証する統合テスト
/// </summary>
/// <remarks>上限の宣言は <see cref="IdentifierLengthLimits.Oracle"/>。Docker 必須（<see cref="OracleContainerFixture"/>）。</remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleIdentifierLengthLimitIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Integration] Oracle: 上限の計算と、DB が名前を受け付けるか断るかが一致する"
    )]
    public async Task Limit_MatchesDatabase()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await using var connection = await fixture.OpenConnectionAsync(Ct);

        string[] names =
        [
            IdentifierLengthProbe.Repeat("a", 128),
            IdentifierLengthProbe.Repeat("a", 129),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Kanji, 42),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.Kanji, 43),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.AUmlaut, 64),
            IdentifierLengthProbe.Repeat(IdentifierLengthProbe.AUmlaut, 64) + "a",
            IdentifierLengthProbe.Repeat("a", 124) + IdentifierLengthProbe.Emoji,
            IdentifierLengthProbe.Repeat("a", 125) + IdentifierLengthProbe.Emoji,
        ];

        var mismatches = new List<string>();

        foreach (var name in names)
        {
            var q = OracleIdentifier.QuoteSimple(name);
            var cases = new (IdentifierLengthProbe.Kind Kind, string Create)[]
            {
                (IdentifierLengthProbe.Kind.Table, $"CREATE TABLE {q} (id NUMBER(10))"),
                (IdentifierLengthProbe.Kind.Column, $"CREATE TABLE probe ({q} NUMBER(10))"),
                (
                    IdentifierLengthProbe.Kind.Constraint,
                    $"CREATE TABLE probe (id NUMBER(10) NOT NULL, CONSTRAINT {q} PRIMARY KEY (id))"
                ),
            };

            foreach (var (kind, create) in cases)
            {
                var expected = IdentifierLengthProbe.Expected(OracleIdentifier.Limit, kind, name);
                var actual = await IdentifierLengthProbe.TryAsync(connection, create, Ct);

                if (actual != expected)
                {
                    mismatches.Add(IdentifierLengthProbe.Describe(kind, name, expected, actual));
                }

                await fixture.ResetSchemaAsync(Ct);
            }
        }

        mismatches.Should().BeEmpty();
    }
}
