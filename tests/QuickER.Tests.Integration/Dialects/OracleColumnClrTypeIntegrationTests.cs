using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Provider.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// Oracle の列型について「ドライバが実際に返す CLR 型」と「<see cref="OracleCSharpTypeMapper"/> の解決結果」が
/// 一致することを実 DB で固定する統合テスト。
/// </summary>
/// <remarks>
/// ANSI の <c>INTEGER</c> / <c>SMALLINT</c> は Oracle では <c>NUMBER(*,0)</c>（精度なし・スケール 0）の列になり、
/// ODP.NET は <c>decimal</c> で返す。図に書いた表記（<c>INTEGER</c>）と取り込んだ表記（<c>NUMBER(*,0)</c>）の
/// どちらからも、同じ <c>decimal</c> へ解決されることを確かめる。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleColumnClrTypeIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact(
        DisplayName = "[Integration] Oracle: INTEGER / SMALLINT は decimal（ドライバの実測 CLR 型と一致する）"
    )]
    public async Task Map_AnsiIntegers_MatchDriverClrTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE CLR_PROBE (
                ID NUMBER(10) NOT NULL PRIMARY KEY,
                I INTEGER NOT NULL,
                S SMALLINT NOT NULL
            );
            INSERT INTO CLR_PROBE (ID, I, S) VALUES (1, 12345678901234567890, 7);
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new OracleSchemaImporter().ImportAsync(conn, Ct);
        var declaredTypes = import
            .Entities.Should()
            .ContainSingle()
            .Subject.Columns.ToDictionary(c => c.Name, c => c.DataType);

        declaredTypes["I"].Should().Be("NUMBER(*,0)");
        declaredTypes["S"].Should().Be("NUMBER(*,0)");

        var driverTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT I, S FROM CLR_PROBE WHERE ID = 1";
            await using var reader = await cmd.ExecuteReaderAsync(Ct);

            (await reader.ReadAsync(Ct)).Should().BeTrue();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                driverTypes[reader.GetName(i)] = reader.GetFieldType(i);
            }
        }

        driverTypes["I"].Should().Be<decimal>();
        driverTypes["S"].Should().Be<decimal>();

        var mapper = new OracleCSharpTypeMapper();

        foreach (
            var declared in new[] { "INTEGER", "SMALLINT", declaredTypes["I"], declaredTypes["S"] }
        )
        {
            var mapped = mapper.Map(declared);

            mapped.TypeName.Should().Be("decimal", declared);
            mapped.IsFallbackType.Should().BeFalse(declared);
        }
    }
}
