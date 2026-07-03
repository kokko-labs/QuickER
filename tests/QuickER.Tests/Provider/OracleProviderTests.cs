using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="OracleProvider"/> の結線と、レジストリに SQL Server / PostgreSQL / Oracle の 3 プロバイダが並ぶことを検証するテストクラス。
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

    /// <summary>OracleProvider が識別名・表示名・既定ポート・型カタログを公開することを検証する</summary>
    [Fact(
        DisplayName = "OracleProvider は name / DisplayName / DefaultPort / 型カタログを公開する"
    )]
    public void OracleProvider_ExposesMetadataAndDataTypes()
    {
        var provider = new OracleProvider();

        provider.Name.Should().Be("oracle");
        provider.DisplayName.Should().Be("Oracle");
        provider.DefaultPort.Should().Be(1521);
        provider.TypeCatalog.DataTypes.Should().NotBeEmpty();
    }

    /// <summary>プロバイダのすべての機能スロットが結線されていることを検証する</summary>
    [Fact(DisplayName = "OracleProvider の各機能が結線されている")]
    public void OracleProvider_WiresUpAllComponents()
    {
        var provider = new OracleProvider();

        provider.SchemaImporter.Should().BeOfType<OracleSchemaImporter>();
        provider.TypeMapper.Should().BeOfType<OracleCSharpTypeMapper>();
        provider.TypeCatalog.Should().BeOfType<OracleTypeCatalog>();
        provider.SyncScriptBuilder.Should().BeOfType<OracleSyncScriptBuilder>();
        provider.SyncExecutor.Should().BeOfType<OracleSchemaSyncExecutor>();
        provider.DdlGenerator.Should().BeOfType<OracleDdlGenerator>();
    }

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
            diagram,
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().NotBeEmpty();
        result.Files[0].Content.Should().Contain("namespace Sample.Domain");
    }

    /// <summary>NUMBER(10) の PK が C# の int として生成されることを検証する（型マッパの主要規則）</summary>
    [Fact(DisplayName = "TypeMapper は NUMBER(10) を int、VARCHAR2 を string に解決する")]
    public void TypeMapper_MapsNumberAndVarchar()
    {
        var mapper = new OracleCSharpTypeMapper();

        mapper.Map("NUMBER(10)").TypeName.Should().Be("int");
        mapper.Map("NUMBER(1)").TypeName.Should().Be("bool");
        mapper.Map("NUMBER(19)").TypeName.Should().Be("long");
        mapper.Map("NUMBER(10,2)").TypeName.Should().Be("decimal");
        mapper.Map("VARCHAR2(100)").TypeName.Should().Be("string");
        mapper.Map("VARCHAR2(100)").MaxLength.Should().Be(100);
        mapper.Map("RAW(16)").TypeName.Should().Be("byte[]");
        mapper.Map("TIMESTAMP WITH TIME ZONE").TypeName.Should().Be("DateTimeOffset");
        mapper.Map("DATE").TypeName.Should().Be("DateTime");
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

    /// <summary>レジストリに 3 プロバイダを登録し、すべてが名前で解決できることを検証する</summary>
    [Fact(DisplayName = "レジストリに sqlserver / postgresql / oracle の 3 プロバイダが並ぶ")]
    public void Registry_ContainsThreeProviders()
    {
        var registry = new DatabaseProviderRegistry([
            new SqlServerProvider(),
            new PostgreSqlProvider(),
            new OracleProvider(),
        ]);

        registry.All.Should().HaveCount(3);
        registry.Get("sqlserver").Should().BeOfType<SqlServerProvider>();
        registry.Get("postgresql").Should().BeOfType<PostgreSqlProvider>();
        registry.Get("oracle").Should().BeOfType<OracleProvider>();
    }
}
