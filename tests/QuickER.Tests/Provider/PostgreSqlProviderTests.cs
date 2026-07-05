using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="PostgreSqlProvider"/> の結線と、レジストリに SQL Server / PostgreSQL の 2 プロバイダが並ぶことを検証するテストクラス。
/// </summary>
public class PostgreSqlProviderTests
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
                            DataType = "integer",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "varchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>PostgreSqlProvider が識別名・表示名・既定ポート・型カタログを公開することを検証する</summary>
    [Fact(
        DisplayName = "PostgreSqlProvider は name / DisplayName / DefaultPort / 型カタログを公開する"
    )]
    public void PostgreSqlProvider_ExposesMetadataAndDataTypes()
    {
        var provider = new PostgreSqlProvider();

        provider.Name.Should().Be("postgresql");
        provider.DisplayName.Should().Be("PostgreSQL");
        provider.DefaultPort.Should().Be(5432);
        provider.TypeCatalog.DataTypes.Should().NotBeEmpty();
    }

    /// <summary>プロバイダのすべての機能スロットが結線されていることを検証する</summary>
    [Fact(DisplayName = "PostgreSqlProvider の各機能が結線されている")]
    public void PostgreSqlProvider_WiresUpAllComponents()
    {
        var provider = new PostgreSqlProvider();

        provider.SchemaImporter.Should().BeOfType<PostgreSqlSchemaImporter>();
        provider.TypeMapper.Should().BeOfType<PostgreSqlCSharpTypeMapper>();
        provider.TypeCatalog.Should().BeOfType<PostgreSqlTypeCatalog>();
        provider.SyncScriptBuilder.Should().BeOfType<PostgreSqlSyncScriptBuilder>();
        provider.SyncExecutor.Should().BeOfType<PostgreSqlSchemaSyncExecutor>();
        provider.DdlGenerator.Should().BeOfType<PostgreSqlDdlGenerator>();
    }

    /// <summary>プロバイダの型マッパが interface 経由で全カラムの型を解決することを検証する</summary>
    [Fact(DisplayName = "IColumnTypeMapper 経由で全カラムの型が解決される")]
    public void TypeMapper_ResolvesAllColumnsViaInterface()
    {
        var diagram = BuildDiagram();
        IColumnTypeMapper mapper = new PostgreSqlProvider().TypeMapper;

        var types = mapper.ResolveColumnTypes(diagram);

        types.Should().HaveCount(2);
    }

    /// <summary>共有ファサードが PostgreSQL プロバイダの型マッパで型解決し、コードを生成することを検証する</summary>
    [Fact(DisplayName = "DiagramCodeGenerator は PostgreSQL プロバイダ経由で生成する")]
    public void DiagramCodeGenerator_GeneratesThroughProvider()
    {
        var diagram = BuildDiagram();
        var provider = new PostgreSqlProvider();

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().NotBeEmpty();
        result.Files[0].Content.Should().Contain("namespace Sample.Domain");
    }

    /// <summary>接続文字列に Host / Port / Database / Username / ApplicationName が反映されることを検証する</summary>
    [Fact(
        DisplayName = "BuildConnectionString は Host/Port/Database/Username/ApplicationName を反映する"
    )]
    public void BuildConnectionString_ReflectsSettings()
    {
        var provider = new PostgreSqlProvider();

        var connStr = provider.BuildConnectionString(
            new DbConnectionSettings
            {
                Host = "db.example.com",
                Port = 6543,
                Database = "shop",
                UserId = "app",
                Password = "secret",
            }
        );

        connStr.Should().Contain("Host=db.example.com");
        connStr.Should().Contain("Port=6543");
        connStr.Should().Contain("Database=shop");
        connStr.Should().Contain("Username=app");
        connStr.Should().Contain("Application Name=QuickER");
    }

    /// <summary>ポート未指定時に既定ポート 5432 が使われることを検証する</summary>
    [Fact(DisplayName = "BuildConnectionString はポート未指定時に 5432 を用いる")]
    public void BuildConnectionString_UsesDefaultPortWhenNull()
    {
        var provider = new PostgreSqlProvider();

        var connStr = provider.BuildConnectionString(
            new DbConnectionSettings { Host = "localhost", Database = "shop" }
        );

        connStr.Should().Contain("Port=5432");
    }

    /// <summary>レジストリに 2 プロバイダを登録し、両方が名前で解決できることを検証する</summary>
    [Fact(DisplayName = "レジストリに sqlserver と postgresql の 2 プロバイダが並ぶ")]
    public void Registry_ContainsBothProviders()
    {
        var registry = new DatabaseProviderRegistry([
            new SqlServerProvider(),
            new PostgreSqlProvider(),
        ]);

        registry.All.Should().HaveCount(2);
        registry.Get("sqlserver").Should().BeOfType<SqlServerProvider>();
        registry.Get("postgresql").Should().BeOfType<PostgreSqlProvider>();
    }
}
