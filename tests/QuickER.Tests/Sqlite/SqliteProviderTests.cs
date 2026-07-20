using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Sqlite;

/// <summary>
/// <see cref="SqliteProvider"/> の結線・メタデータ・接続文字列・同期スタブを検証するテストクラス。
/// </summary>
public class SqliteProviderTests
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
                            DataType = "INT",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "NVARCHAR(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>SqliteProvider が識別名・表示名・既定ポート（null）・型カタログを公開することを検証する</summary>
    [Fact(
        DisplayName = "SqliteProvider は name / DisplayName / DefaultPort / 型カタログを公開する"
    )]
    public void SqliteProvider_ExposesMetadataAndDataTypes()
    {
        var provider = new SqliteProvider();

        provider.Name.Should().Be("sqlite");
        provider.DisplayName.Should().Be("SQLite");
        provider.DefaultPort.Should().BeNull();
        provider.TypeCatalog.DataTypes.Should().NotBeEmpty();
    }

    /// <summary>プロバイダのすべての機能スロットが結線されていることを検証する</summary>
    [Fact(DisplayName = "SqliteProvider の各機能が結線されている")]
    public void SqliteProvider_WiresUpAllComponents()
    {
        var provider = new SqliteProvider();

        provider.SchemaImporter.Should().BeOfType<SqliteSchemaImporter>();
        provider.TypeMapper.Should().BeOfType<SqliteCSharpTypeMapper>();
        provider.TypeCatalog.Should().BeOfType<SqliteTypeCatalog>();
        provider.SyncScriptBuilder.Should().BeOfType<SqliteSyncScriptBuilder>();
        provider.SyncExecutor.Should().BeOfType<SqliteSchemaSyncExecutor>();
        provider.DdlGenerator.Should().BeOfType<SqliteDdlGenerator>();
    }

    /// <summary>プロバイダの型マッパが interface 経由で全カラムの型を解決することを検証する</summary>
    [Fact(DisplayName = "IColumnTypeMapper 経由で全カラムの型が解決される")]
    public void TypeMapper_ResolvesAllColumnsViaInterface()
    {
        var diagram = BuildDiagram();
        IColumnTypeMapper mapper = new SqliteProvider().TypeMapper;

        var types = mapper.ResolveColumnTypes(diagram);

        types.Should().HaveCount(2);
    }

    /// <summary>共有ファサードが SQLite プロバイダの型マッパで型解決し、コードを生成することを検証する</summary>
    [Fact(DisplayName = "DiagramCodeGenerator は SQLite プロバイダ経由で生成する")]
    public void DiagramCodeGenerator_GeneratesThroughProvider()
    {
        var diagram = BuildDiagram();
        var provider = new SqliteProvider();

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

    /// <summary>接続文字列が FilePath を DataSource に、Mode=ReadOnly を含めて構築されることを検証する</summary>
    [Fact(
        DisplayName = "BuildConnectionString は FilePath → DataSource / Mode=ReadOnly を反映する"
    )]
    public void BuildConnectionString_UsesFilePathAndReadOnlyMode()
    {
        var provider = new SqliteProvider();

        var connStr = provider.BuildConnectionString(
            new DbConnectionSettings { FilePath = @"C:\data\shop.db" }
        );

        connStr.Should().Contain(@"C:\data\shop.db");
        connStr.Should().Contain("Mode=ReadOnly");
    }

    /// <summary>同期スクリプト生成が計画から SQLite スクリプト（PRAGMA ラップ・CREATE TABLE）を生成することを検証する</summary>
    [Fact(DisplayName = "SyncScriptBuilder は計画から SQLite スクリプトを生成する")]
    public void SyncScriptBuilder_ProducesScript()
    {
        var provider = new SqliteProvider();

        var entity = new Entity
        {
            TableName = "t",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "INT",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };

        // 新規テーブル追加（CreateOnly）は live スキーマ不要のため空の context で計画できる
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = "t",
                    Entity = entity,
                    IsSelected = true,
                },
            ],
            provider.SyncCapabilities,
            new SyncPlanContext()
        );

        var script = provider.SyncScriptBuilder.Build(plan);

        script.Should().Contain("PRAGMA foreign_keys=OFF;");
        script.Should().Contain("CREATE TABLE \"t\"");
        script.Should().Contain("PRAGMA foreign_key_check;");
    }

    /// <summary>空スクリプトの同期実行が例外を投げず、no-op として成功扱いになることを検証する</summary>
    [Fact(DisplayName = "SyncExecutor は空スクリプトを no-op として成功扱いする")]
    public async Task SyncExecutor_ExecutesEmptyScriptAsCommitted()
    {
        var provider = new SqliteProvider();

        var result = await provider.SyncExecutor.ExecuteAsync(
            new DbConnectionSettings(),
            "",
            TestContext.Current.CancellationToken
        );

        result.Committed.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    /// <summary>レジストリに SQL Server と SQLite を登録し、両方が名前で解決できることを検証する</summary>
    [Fact(DisplayName = "レジストリに sqlserver と sqlite の 2 プロバイダが並ぶ")]
    public void Registry_ContainsBothProviders()
    {
        var registry = new DatabaseProviderRegistry([
            new SqlServerProvider(),
            new SqliteProvider(),
        ]);

        registry.All.Should().HaveCount(2);
        registry.Get("sqlserver").Should().BeOfType<SqlServerProvider>();
        registry.Get("sqlite").Should().BeOfType<SqliteProvider>();
    }
}
