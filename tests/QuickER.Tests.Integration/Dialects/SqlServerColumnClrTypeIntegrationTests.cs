using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQL Server の列型について「ドライバが実際に返す CLR 型」と「<see cref="SqlServerCSharpTypeMapper"/> の解決結果」が
/// 一致することを実 DB で固定する統合テスト。
/// </summary>
/// <remarks>
/// <c>float(1)</c>〜<c>float(24)</c> は SQL Server では <c>real</c>（単精度）で、ドライバは <see cref="float"/> を返す。
/// マッパーが引数を見ずに <c>double</c> とすると、生成コードの速い読み取り（<c>GetDouble</c>）が
/// <c>InvalidCastException</c> になる。マッパーの分岐は<b>ドライバの実測値が正</b>なので、一致の検証を実 DB で行う。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerColumnClrTypeIntegrationTests(SqlServerContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>実測した CLR 型を C# のキーワード表記へ直す（マッパーの TypeName と突き合わせるため）</summary>
    private static readonly IReadOnlyDictionary<Type, string> CSharpKeywords = new Dictionary<
        Type,
        string
    >
    {
        [typeof(float)] = "float",
        [typeof(double)] = "double",
    };

    /// <summary>
    /// 浮動小数点の各表記について、マッパーの解決結果がドライバの返す CLR 型と一致し、
    /// その型の getter（GetFloat / GetDouble）で読めることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] float(1〜24) と real は float、float(25〜53) と素の float は double（ドライバの実測 CLR 型と一致する）"
    )]
    public async Task Map_FloatingPointColumns_MatchDriverClrTypes()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // 列名 → 宣言した型表記
        var declared = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["f1"] = "float(1)",
            ["f24"] = "float(24)",
            ["f25"] = "float(25)",
            ["f53"] = "float(53)",
            ["f_plain"] = "float",
            ["r"] = "real",
        };

        await fixture.ExecuteAsync(
            """
            CREATE TABLE clr_probe (
                id int NOT NULL PRIMARY KEY,
                f1 float(1) NOT NULL,
                f24 float(24) NOT NULL,
                f25 float(25) NOT NULL,
                f53 float(53) NOT NULL,
                f_plain float NOT NULL,
                r real NOT NULL
            );
            INSERT INTO clr_probe (id, f1, f24, f25, f53, f_plain, r)
                VALUES (1, 1.5, 2.5, 3.5, 4.5, 5.5, 6.5);
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT f1, f24, f25, f53, f_plain, r FROM clr_probe WHERE id = 1";
        await using var reader = await cmd.ExecuteReaderAsync(Ct);

        (await reader.ReadAsync(Ct)).Should().BeTrue();

        var mapper = new SqlServerCSharpTypeMapper();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var driverType = reader.GetFieldType(i);

            mapper
                .Map(declared[name])
                .TypeName.Should()
                .Be(CSharpKeywords[driverType], $"{name}（{declared[name]}）の C# 型");

            // 生成コードの速い読み取りと同じ getter で読める
            var read = () =>
                driverType == typeof(float) ? (object)reader.GetFloat(i) : reader.GetDouble(i);
            read.Should().NotThrow(name);
        }

        // 単精度になる表記・倍精度のままの表記を、実測値でも固定する
        reader.GetFieldType(reader.GetOrdinal("f24")).Should().Be<float>();
        reader.GetFieldType(reader.GetOrdinal("f25")).Should().Be<double>();
    }
}
