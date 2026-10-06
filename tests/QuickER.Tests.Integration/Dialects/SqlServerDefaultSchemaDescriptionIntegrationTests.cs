using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// スキーマ修飾の無い表の説明が、表と同じく接続ユーザーの既定スキーマに付くことを実 SQL Server で検証する。
/// </summary>
/// <remarks>
/// 既定スキーマが <c>app</c> のユーザーで接続すると、<c>CREATE TABLE [Widget]</c> は <c>app.Widget</c> を作る。
/// 説明の文（<c>sp_addextendedproperty</c> / <c>sp_updateextendedproperty</c>）も既定スキーマを
/// <c>SCHEMA_NAME()</c> で求めて同じ表を指すこと（<see cref="SqlServerExtendedProperty"/>）を、DDL と同期スクリプトの
/// 両方で確かめる。Docker 必須（<see cref="SqlServerContainerFixture"/>）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerDefaultSchemaDescriptionIntegrationTests(
    SqlServerContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>既定スキーマが app のログイン名（とパスワード）</summary>
    private const string Login = "qer_app_user";

    private const string LoginPassword = "Qer_app#2026!";

    /// <summary>既定スキーマが dbo でないユーザーで、修飾の無い表の説明が DDL・同期とも既定スキーマの表に付く</summary>
    [Fact(
        DisplayName = "[Integration] SQL Server: 修飾の無い表の説明は DDL・同期とも既定スキーマの表に付く"
    )]
    public async Task Description_OfUnqualifiedTable_FollowsDefaultSchema()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await CreateAppUserAsync();

        try
        {
            // DDL: 表と説明（表・列）を既定スキーマ app へ
            var widget = Table("Widget", "widget table", "widget name");
            var ddl = new SqlServerDdlGenerator().Build(new ErDiagram { Entities = { widget } });
            await ExecuteAsAppUserAsync(ddl);

            // 同期: 新しい表の追加と説明、既存の表の説明の更新（存在確認の OBJECT_ID も既定スキーマで引く）
            var gadget = Table("Gadget", "gadget table", "gadget name");
            var items = new List<SchemaDiffItem>
            {
                new()
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = "Gadget",
                    Entity = gadget,
                    IsSelected = true,
                },
                new()
                {
                    Kind = SchemaDiffKind.SetTableDescription,
                    TableName = "Gadget",
                    Entity = gadget,
                    NewDescription = "gadget table",
                    IsSelected = true,
                },
                new()
                {
                    Kind = SchemaDiffKind.SetColumnDescription,
                    TableName = "Gadget",
                    ColumnName = "name",
                    Entity = gadget,
                    Column = gadget.Columns[1],
                    NewDescription = "gadget name",
                    IsSelected = true,
                },
                new()
                {
                    Kind = SchemaDiffKind.SetTableDescription,
                    TableName = "Widget",
                    Entity = widget,
                    NewDescription = "widget table (updated)",
                    OldDescription = "widget table",
                    IsSelected = true,
                },
            };
            var caps = new SqlServerProvider().SyncCapabilities;
            var script = new SqlServerSyncScriptBuilder().Build(
                new SyncPlanner().BuildPlan(items, caps)
            );
            var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
                AppUserSettings(),
                script,
                Ct
            );
            result.Committed.Should().BeTrue($"{result.Error}\n{script}");

            var descriptions = await ReadDescriptionsAsync();
            descriptions
                .Should()
                .BeEquivalentTo([
                    ("app", "Widget", "", "widget table (updated)"),
                    ("app", "Widget", "name", "widget name"),
                    ("app", "Gadget", "", "gadget table"),
                    ("app", "Gadget", "name", "gadget name"),
                ]);
        }
        finally
        {
            // プールに残った app ユーザーのセッションがあるとログインを落とせない
            SqlConnection.ClearAllPools();
            await fixture.ResetSchemaAsync(Ct);
            await fixture.ExecuteAsync(
                $"""
                DROP SCHEMA IF EXISTS app;
                DROP USER IF EXISTS {Login};
                IF SUSER_ID(N'{Login}') IS NOT NULL DROP LOGIN {Login};
                """,
                Ct
            );
        }
    }

    /// <summary>既定スキーマが app のユーザーを作る（app スキーマはそのユーザーが所有する）</summary>
    private Task CreateAppUserAsync() =>
        fixture.ExecuteAsync(
            $"""
            DROP SCHEMA IF EXISTS app;
            DROP USER IF EXISTS {Login};
            IF SUSER_ID(N'{Login}') IS NOT NULL DROP LOGIN {Login};
            CREATE LOGIN {Login} WITH PASSWORD = N'{LoginPassword}', CHECK_POLICY = OFF;
            CREATE USER {Login} FOR LOGIN {Login} WITH DEFAULT_SCHEMA = app;
            EXEC (N'CREATE SCHEMA app AUTHORIZATION {Login}');
            GRANT CREATE TABLE TO {Login};
            """,
            Ct
        );

    /// <summary>既定スキーマが app のユーザーの接続設定</summary>
    private DbConnectionSettings AppUserSettings()
    {
        var settings = fixture.ToDbConnectionSettings();
        settings.UserId = Login;
        settings.Password = LoginPassword;
        return settings;
    }

    /// <summary>既定スキーマが app のユーザーで SQL を実行する</summary>
    private async Task ExecuteAsAppUserAsync(string sql)
    {
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            UserID = Login,
            Password = LoginPassword,
        };
        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>ユーザー表の説明を (スキーマ, 表, 列〔表なら空〕, 説明) の組で読む</summary>
    private async Task<List<(string, string, string, string)>> ReadDescriptionsAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new SqlCommand(
            """
            SELECT SCHEMA_NAME(t.schema_id), t.name, ISNULL(c.name, N''), CAST(ep.value AS nvarchar(4000))
            FROM sys.extended_properties ep
            JOIN sys.tables t ON ep.major_id = t.object_id
            LEFT JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
            WHERE ep.class = 1 AND ep.name = N'MS_Description'
            """,
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        var rows = new List<(string, string, string, string)>();

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(
                (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            );
        }

        return rows;
    }

    /// <summary>修飾の無い名前の表（id・name の 2 列、表と name 列に説明）を作る</summary>
    private static Entity Table(string name, string description, string columnDescription) =>
        new()
        {
            TableName = name,
            Description = description,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "name",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                    Description = columnDescription,
                },
            },
        };
}
