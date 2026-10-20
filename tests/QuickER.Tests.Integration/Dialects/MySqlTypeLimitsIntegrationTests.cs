using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using MySqlConnector;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// <see cref="MySqlTypeCatalog.TryFormat"/> が書き出す長さの上限が、実 MySQL の上限と一致することを固定する統合テスト。
/// </summary>
/// <remarks>
/// 上限ちょうどの表記は DDL が通って取り込み直しても同じ表記に戻り、1 つ超えた表記は MySQL が断る（だから TryFormat は
/// 書き出さない）。表は 1 列だけにする＝<c>varbinary</c> は列の上限を持たず行全体の上限（65,535 バイト）だけが効くので、
/// ほかの列と並べると上限ちょうどでも断られる（その判定は MySQL が適用時に行う）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlTypeLimitsIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>上限ちょうどの正規型と、1 つ超えた型の表記</summary>
    public static TheoryData<CanonicalTypeKind, int, string> Limits =>
        new()
        {
            { CanonicalTypeKind.FixedString, 255, "char(256)" },
            { CanonicalTypeKind.FixedBinary, 255, "binary(256)" },
            { CanonicalTypeKind.String, 16383, "varchar(16384)" },
            { CanonicalTypeKind.Binary, 65532, "varbinary(65533)" },
        };

    [Theory(
        DisplayName = "[Integration] MySQL: TryFormat が書く上限ちょうどの型は通り、1 つ超えると MySQL が断る"
    )]
    [MemberData(nameof(Limits))]
    public async Task TryFormatLimit_MatchesMySql(CanonicalTypeKind kind, int limit, string beyond)
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var catalog = new MySqlTypeCatalog();

        catalog
            .TryFormat(new CanonicalType(kind, Length: limit), out var atLimit)
            .Should()
            .BeTrue();
        catalog.TryFormat(new CanonicalType(kind, Length: limit + 1), out _).Should().BeFalse();

        await fixture.ExecuteAsync($"CREATE TABLE at_limit (c {atLimit})", Ct);

        var beyondCreate = async () =>
            await fixture.ExecuteAsync($"CREATE TABLE beyond_limit (c {beyond})", Ct);
        await beyondCreate.Should().ThrowAsync<MySqlException>($"{beyond} は MySQL が断る");

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new MySqlSchemaImporter().ImportAsync(conn, Ct);
        var column = import
            .Entities.Should()
            .ContainSingle(e => e.TableName == "at_limit")
            .Subject.Columns.Should()
            .ContainSingle()
            .Subject;

        column
            .DataType.Should()
            .Be(MySqlTypeNotation.Normalize(atLimit), "取り込み直しても同じ表記に戻る");
    }
}
