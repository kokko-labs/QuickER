using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// MySQL の列型について「ドライバが実際に返す CLR 型」と「<see cref="MySqlCSharpTypeMapper"/> の解決結果」が
/// 一致することを実 DB で固定する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// <c>bit(n&gt;1)</c> と <c>year</c> は従来「未知の型 → string」のフォールバックへ落ちており、生成コードが
/// 読み出し（<c>GetFieldValue&lt;string&gt;</c> 相当）で <c>InvalidCastException</c> になっていた。
/// マッパーの分岐は<b>ドライバの実測値が正</b>なので、一致の検証を実 DB で行う。
/// </para>
/// <para>
/// <c>bit(1)</c> だけは例外で、ドライバは <c>ulong</c> を返すが QuickER は <c>tinyint(1)</c> と対の
/// 真偽値慣習で <c>bool</c> へ寄せる（意図的な非対称。その宣言も併せて固定する）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlColumnClrTypeIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>実測した CLR 型を C# のキーワード表記へ直す（マッパーの TypeName と突き合わせるため）</summary>
    private static readonly IReadOnlyDictionary<Type, string> CSharpKeywords = new Dictionary<
        Type,
        string
    >
    {
        [typeof(bool)] = "bool",
        [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "short",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
        [typeof(string)] = "string",
        [typeof(byte[])] = "byte[]",
        [typeof(DateTime)] = "DateTime",
        [typeof(TimeSpan)] = "TimeSpan",
    };

    /// <summary>
    /// <c>bit(n&gt;1)</c> / <c>year</c> のマッパー解決結果が、ドライバが返す CLR 型と一致する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] bit(n>1) は ulong、year は int（ドライバの実測 CLR 型と一致する）"
    )]
    public async Task Map_BitFieldAndYear_MatchDriverClrTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE clr_probe (
                id int NOT NULL PRIMARY KEY,
                flag bit(1) NOT NULL,
                flags8 bit(8) NOT NULL,
                flags64 bit(64) NOT NULL,
                built year NOT NULL,
                built_opt year NULL
            ) ENGINE=InnoDB;
            INSERT INTO clr_probe (id, flag, flags8, flags64, built, built_opt)
                VALUES (1, b'1', b'10101010', b'1111', 2026, NULL);
            """,
            Ct
        );

        // 1) 図としての取込（COLUMN_TYPE をそのまま型表記に採る）
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new MySqlSchemaImporter().ImportAsync(conn, Ct);
        var entity = import.Entities.Should().ContainSingle().Subject;
        var declaredTypes = entity.Columns.ToDictionary(c => c.Name, c => c.DataType);

        declaredTypes["flags8"].Should().Be("bit(8)");
        declaredTypes["built"].Should().Be("year");

        // 2) ドライバが実際に返す CLR 型
        var driverTypes = new Dictionary<string, Type>(StringComparer.Ordinal);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT flag, flags8, flags64, built, built_opt FROM clr_probe WHERE id = 1";
            await using var reader = await cmd.ExecuteReaderAsync(Ct);

            (await reader.ReadAsync(Ct)).Should().BeTrue();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                driverTypes[reader.GetName(i)] = reader.GetFieldType(i);
            }
        }

        driverTypes["flags8"].Should().Be<ulong>();
        driverTypes["built"].Should().Be<int>();

        // 3) マッパーの解決結果がドライバの CLR 型と一致する（bit(1) だけは真偽値慣習で意図的に外れる）
        var mapper = new MySqlCSharpTypeMapper();

        foreach (var columnName in new[] { "flags8", "flags64", "built", "built_opt" })
        {
            mapper
                .Map(declaredTypes[columnName])
                .TypeName.Should()
                .Be(
                    CSharpKeywords[driverTypes[columnName]],
                    $"{columnName}（{declaredTypes[columnName]}）はドライバが返す CLR 型と同じ C# 型へ解決されること"
                );
        }

        // bit(1) はドライバの ulong に対し bool（tinyint(1) と対の真偽値慣習・意図的な非対称）
        driverTypes["flag"].Should().Be<ulong>();
        mapper.Map(declaredTypes["flag"]).TypeName.Should().Be("bool");
    }

    /// <summary>
    /// 取り込んだ図から生成した Entity で、NULL 許容の <c>year</c> / <c>bit(8)</c> が
    /// <c>int?</c> / <c>ulong?</c>（NULL 許容の値型）になる。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] NULL 許容の bit(8) / year は生成 Entity で ulong? / int? になる"
    )]
    public async Task Generate_NullableBitAndYear_EmitNullableValueTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE clr_nullable (
                id int NOT NULL PRIMARY KEY,
                flags8 bit(8) NULL,
                built year NULL
            ) ENGINE=InnoDB;
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var import = await new MySqlSchemaImporter().ImportAsync(conn, Ct);
        var diagram = new ErDiagram();

        foreach (var entity in import.Entities)
        {
            diagram.Entities.Add(entity);
        }

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            MySqlCSharpTypeMapper.ResolveColumnTypes(diagram),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCoreRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = string.Join("\n", result.Files.Select(file => file.Content));

        content.Should().Contain("public ulong? Flags8 { get; set; }");
        content.Should().Contain("public int? Built { get; set; }");
    }
}
