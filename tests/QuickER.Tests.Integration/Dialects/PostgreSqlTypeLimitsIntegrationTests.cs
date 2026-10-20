using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Npgsql;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// <see cref="PostgreSqlTypeCatalog.TryFormat"/> が書き出す長さ・精度・スケールの上限が、実 PostgreSQL の上限と一致することを
/// 固定する統合テスト。
/// </summary>
/// <remarks>
/// 上限ちょうどの表記は DDL が通って取り込み直しても同じ表記に戻り、1 つ超えた表記は PostgreSQL が断るか、小数秒のように
/// 6 へ丸める（だから TryFormat は書き出さない）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlTypeLimitsIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>上限ちょうどの正規型と、1 つ超えた正規型</summary>
    public static TheoryData<string, CanonicalType, CanonicalType> Limits =>
        new()
        {
            {
                "numeric の精度",
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 1000, Scale: 0),
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 1001, Scale: 0)
            },
            {
                "numeric のスケールの上",
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: 1000),
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: 1001)
            },
            {
                "numeric のスケールの下",
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: -1000),
                new CanonicalType(CanonicalTypeKind.Decimal, Precision: 10, Scale: -1001)
            },
            {
                "varchar の長さ",
                new CanonicalType(CanonicalTypeKind.String, Length: 10485760),
                new CanonicalType(CanonicalTypeKind.String, Length: 10485761)
            },
            {
                "char の長さ",
                new CanonicalType(CanonicalTypeKind.FixedString, Length: 10485760),
                new CanonicalType(CanonicalTypeKind.FixedString, Length: 10485761)
            },
        };

    /// <summary>小数秒の上限ちょうどの正規型（1 つ超えると PostgreSQL は 6 へ丸める）</summary>
    public static TheoryData<CanonicalTypeKind> FractionalSecondKinds =>
        new()
        {
            CanonicalTypeKind.DateTime,
            CanonicalTypeKind.DateTimeOffset,
            CanonicalTypeKind.Time,
        };

    [Theory(
        DisplayName = "[Integration] PostgreSQL: TryFormat が書く上限ちょうどの型は通り、1 つ超えると PostgreSQL が断る"
    )]
    [MemberData(nameof(Limits))]
    public async Task TryFormatLimit_MatchesPostgreSql(
        string label,
        CanonicalType atLimit,
        CanonicalType beyond
    )
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var catalog = new PostgreSqlTypeCatalog();

        catalog.TryFormat(atLimit, out var atLimitType).Should().BeTrue(label);
        catalog.TryFormat(beyond, out _).Should().BeFalse(label);
        catalog.TryFormatParameterType(beyond, out var beyondType).Should().BeTrue(label);

        await ExecuteAsync($"CREATE TABLE at_limit (c {atLimitType})");

        var beyondCreate = async () =>
            await ExecuteAsync($"CREATE TABLE beyond_limit (c {beyondType})");
        await beyondCreate
            .Should()
            .ThrowAsync<PostgresException>($"{label}：{beyondType} は PostgreSQL が断る");

        (await ImportedTypeAsync())
            .Should()
            .Be(PostgreSqlTypeNotation.Normalize(atLimitType), label);
    }

    [Theory(
        DisplayName = "[Integration] PostgreSQL: 小数秒 6 はそのまま残り、7 は PostgreSQL が 6 へ丸める"
    )]
    [MemberData(nameof(FractionalSecondKinds))]
    public async Task FractionalSecondsLimit_MatchesPostgreSql(CanonicalTypeKind kind)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var catalog = new PostgreSqlTypeCatalog();

        catalog
            .TryFormat(new CanonicalType(kind, Precision: 6), out var atLimitType)
            .Should()
            .BeTrue();
        catalog.TryFormat(new CanonicalType(kind, Precision: 7), out _).Should().BeFalse();
        catalog
            .TryFormatParameterType(new CanonicalType(kind, Precision: 7), out var beyondType)
            .Should()
            .BeTrue();

        await ExecuteAsync($"CREATE TABLE at_limit (c {atLimitType})");
        var atLimitImported = await ImportedTypeAsync();

        atLimitImported.Should().Be(PostgreSqlTypeNotation.Normalize(atLimitType));

        await fixture.ResetSchemaAsync(Ct);
        await ExecuteAsync($"CREATE TABLE at_limit (c {beyondType})");

        (await ImportedTypeAsync())
            .Should()
            .Be(
                atLimitImported,
                $"{beyondType} は PostgreSQL が小数秒 6 へ丸める（変換の結果が元の精度と違う）"
            );
    }

    private Task ExecuteAsync(string sql) => fixture.ExecuteAsync(sql, Ct);

    private async Task<string> ImportedTypeAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        return import
            .Entities.Should()
            .ContainSingle(e => e.TableName == "at_limit")
            .Subject.Columns.Should()
            .ContainSingle()
            .Subject.DataType;
    }
}
