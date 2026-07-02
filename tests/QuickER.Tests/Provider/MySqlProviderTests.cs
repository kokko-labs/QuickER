using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.MySql;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="MySqlProvider"/> の結線と、レジストリに SQL Server / PostgreSQL / MySQL の 3 プロバイダが並ぶことを検証するテストクラス。
/// </summary>
public class MySqlProviderTests
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
                            DataType = "int",
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

    /// <summary>MySqlProvider が識別名・表示名・既定ポート・型カタログを公開することを検証する</summary>
    [Fact(DisplayName = "MySqlProvider は name / DisplayName / DefaultPort / 型カタログを公開する")]
    public void MySqlProvider_ExposesMetadataAndDataTypes()
    {
        var provider = new MySqlProvider();

        provider.Name.Should().Be("mysql");
        provider.DisplayName.Should().Be("MySQL");
        provider.DefaultPort.Should().Be(3306);
        provider.TypeCatalog.DataTypes.Should().NotBeEmpty();
    }

    /// <summary>プロバイダのすべての機能スロットが結線されていることを検証する</summary>
    [Fact(DisplayName = "MySqlProvider の各機能が結線されている")]
    public void MySqlProvider_WiresUpAllComponents()
    {
        var provider = new MySqlProvider();

        provider.SchemaImporter.Should().BeOfType<MySqlSchemaImporter>();
        provider.TypeMapper.Should().BeOfType<MySqlCSharpTypeMapper>();
        provider.TypeCatalog.Should().BeOfType<MySqlTypeCatalog>();
        provider.SyncScriptBuilder.Should().BeOfType<MySqlSyncScriptBuilder>();
        provider.SyncExecutor.Should().BeOfType<MySqlSchemaSyncExecutor>();
        provider.DdlGenerator.Should().BeOfType<MySqlDdlGenerator>();
    }

    /// <summary>プロバイダの型マッパが interface 経由で全カラムの型を解決することを検証する</summary>
    [Fact(DisplayName = "IColumnTypeMapper 経由で全カラムの型が解決される")]
    public void TypeMapper_ResolvesAllColumnsViaInterface()
    {
        var diagram = BuildDiagram();
        IColumnTypeMapper mapper = new MySqlProvider().TypeMapper;

        var types = mapper.ResolveColumnTypes(diagram);

        types.Should().HaveCount(2);
    }

    /// <summary>共有ファサードが MySQL プロバイダの型マッパで型解決し、コードを生成することを検証する</summary>
    [Fact(DisplayName = "DiagramCodeGenerator は MySQL プロバイダ経由で生成する")]
    public void DiagramCodeGenerator_GeneratesThroughProvider()
    {
        var diagram = BuildDiagram();
        var provider = new MySqlProvider();

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            diagram,
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().NotBeEmpty();
        result.Files[0].Content.Should().Contain("namespace Sample.Domain");
    }

    /// <summary>接続文字列に Server / Port / Database / User / ApplicationName が反映されることを検証する</summary>
    [Fact(DisplayName = "BuildConnectionString は Server/Port/Database/User/ApplicationName を反映する")]
    public void BuildConnectionString_ReflectsSettings()
    {
        var provider = new MySqlProvider();

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

        connStr.Should().Contain("Server=db.example.com");
        connStr.Should().Contain("Port=6543");
        connStr.Should().Contain("Database=shop");
        connStr.Should().Contain("User ID=app");
        connStr.Should().Contain("Application Name=QuickER");
    }

    /// <summary>ポート未指定時に既定ポート 3306 が使われることを検証する</summary>
    [Fact(DisplayName = "BuildConnectionString はポート未指定時に 3306 を用いる")]
    public void BuildConnectionString_UsesDefaultPortWhenNull()
    {
        var provider = new MySqlProvider();

        var connStr = provider.BuildConnectionString(
            new DbConnectionSettings { Host = "localhost", Database = "shop" }
        );

        connStr.Should().Contain("Port=3306");
    }

    /// <summary>レジストリに 3 プロバイダを登録し、すべてが名前で解決できることを検証する</summary>
    [Fact(DisplayName = "レジストリに sqlserver / postgresql / mysql の 3 プロバイダが並ぶ")]
    public void Registry_ContainsAllThreeProviders()
    {
        var registry = new DatabaseProviderRegistry(
            [new SqlServerProvider(), new PostgreSqlProvider(), new MySqlProvider()]
        );

        registry.All.Should().HaveCount(3);
        registry.Get("sqlserver").Should().BeOfType<SqlServerProvider>();
        registry.Get("postgresql").Should().BeOfType<PostgreSqlProvider>();
        registry.Get("mysql").Should().BeOfType<MySqlProvider>();
    }
}
