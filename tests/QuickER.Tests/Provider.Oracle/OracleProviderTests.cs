using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// <see cref="OracleProvider"/> が図を通して型を解決し、コード生成と接続文字列を組めることを検証するテストクラス。
/// 結線・メタデータ・レジストリは方言の型の表の <c>Provider_IsWired</c>（oracle）が固定する。
/// </summary>
public class OracleProviderTests
{
    private static ErDiagram BuildDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "NUMBER(10)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "VARCHAR2(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>プロバイダの型マッパが interface 経由で全カラムの型を解決することを検証する</summary>
    [Fact(DisplayName = "IColumnTypeMapper 経由で全カラムの型が解決される")]
    public void TypeMapper_ResolvesAllColumnsViaInterface()
    {
        var diagram = BuildDiagram();
        IColumnTypeMapper mapper = new OracleProvider().TypeMapper;

        var types = mapper.ResolveColumnTypes(diagram);

        types.Should().HaveCount(2);
    }

    /// <summary>共有ファサードが Oracle プロバイダの型マッパで型解決し、コードを生成することを検証する</summary>
    [Fact(DisplayName = "DiagramCodeGenerator は Oracle プロバイダ経由で生成する")]
    public void DiagramCodeGenerator_GeneratesThroughProvider()
    {
        var diagram = BuildDiagram();
        var provider = new OracleProvider();

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().NotBeEmpty();
        result.Files[0].Content.Should().Contain("namespace Sample.Domain");
    }

    /// <summary>接続文字列に EZConnect 形式の DataSource / User Id が反映されることを検証する</summary>
    [Fact(DisplayName = "BuildConnectionString は EZConnect 形式で DataSource/User を反映する")]
    public void BuildConnectionString_ReflectsSettings()
    {
        var provider = new OracleProvider();

        var connStr = provider.BuildConnectionString(
            new DbConnectionSettings
            {
                Host = "db.example.com",
                Port = 1600,
                Database = "ORCL",
                ServiceName = "FREEPDB1",
                UserId = "app",
                Password = "secret",
            }
        );

        // ServiceName が非空ならそれを使う
        connStr.Should().Contain("db.example.com:1600/FREEPDB1");
        connStr.Should().Contain("app");
    }

    /// <summary>ServiceName が空なら Database をサービス名に用い、ポート未指定なら 1521 を使うことを検証する</summary>
    [Fact(
        DisplayName = "BuildConnectionString はサービス名未指定時に Database、ポート未指定時に 1521 を用いる"
    )]
    public void BuildConnectionString_FallsBackToDatabaseAndDefaultPort()
    {
        var provider = new OracleProvider();

        var connStr = provider.BuildConnectionString(
            new DbConnectionSettings
            {
                Host = "localhost",
                Database = "XEPDB1",
                UserId = "app",
                Password = "secret",
            }
        );

        connStr.Should().Contain("localhost:1521/XEPDB1");
    }
}
