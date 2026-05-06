using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace ERDesigner.Tests.Services;

/// <summary>
/// 実 SQL Server (localhost / TestDB / Windows 認証) に対してスキーマ同期を end-to-end でテストします。
/// 接続できない環境ではスキップされます。テスト用オブジェクトは <c>_erd_sync_test_</c> プレフィクスで作成し、
/// 必ず最後に DROP します。
/// </summary>
[Trait("Category", "Integration")]
public class SchemaSyncIntegrationTests : IAsyncLifetime
{
    private static readonly CancellationToken TestCancellationToken = TestContext.Current.CancellationToken;

    private static readonly SqlConnectionSettings Settings = new()
    {
        Server = "localhost",
        Database = "TestDB",
        AuthMode = SqlAuthMode.Windows,
        TrustServerCertificate = true,
    };

    private const string ParentTable = "_erd_sync_test_parent";
    private const string ChildTable = "_erd_sync_test_child";

    private bool _serverAvailable;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var conn = new SqlConnection(Settings.Build());

            await conn.OpenAsync(TestCancellationToken);
            _serverAvailable = true;
        }
        catch
        {
            _serverAvailable = false;
        }

        if (_serverAvailable)
        {
            await DropTestObjectsAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_serverAvailable)
        {
            await DropTestObjectsAsync();
        }
    }

    private static async Task DropTestObjectsAsync()
    {
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        var script =
            $@"
IF OBJECT_ID(N'{ChildTable}', N'U') IS NOT NULL DROP TABLE [{ChildTable}];
IF OBJECT_ID(N'{ParentTable}', N'U') IS NOT NULL DROP TABLE [{ParentTable}];";
        await using var cmd = new SqlCommand(script, conn);

        await cmd.ExecuteNonQueryAsync(TestCancellationToken);
    }

    [Fact(DisplayName = "[Integration] AddTable / AddColumn / AddForeignKey が実 DB に適用される")]
    public async Task FullSync_AppliesChanges()
    {
        if (!_serverAvailable)
        {
            // ローカル DB が無い環境ではスキップ扱い
            return;
        }

        // ---------- 1) ER 図側の期待状態を構築 ----------
        var parent = new Entity { TableName = ParentTable };
        parent.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        parent.Columns.Add(new Column { Name = "Name", DataType = "nvarchar(50)" });

        var child = new Entity { TableName = ChildTable };
        child.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        child.Columns.Add(new Column { Name = $"{ParentTable}_Id", DataType = "int" });

        var rel = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parent.Columns[0].Id,
            TargetColumnId = child.Columns[1].Id,
        };

        // ---------- 2) DB から現状を取得して差分計算 ----------
        var importer = new SqlServerSchemaImporter();
        var live1 = await importer.ImportAsync(Settings, TestCancellationToken);
        var diff1 = new SchemaDiffService().Compute(live1.Entities, live1.Relationships, new[] { parent, child }, new[] { rel });

        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == ParentTable);
        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == ChildTable);
        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddForeignKey);

        // ---------- 3) 実行 ----------
        var script1 = SchemaSyncScriptBuilder.Build(diff1.Items);
        var exec = new SchemaSyncExecutor();
        var result1 = await exec.ExecuteAsync(Settings, script1, TestCancellationToken);
        result1.Committed.Should().BeTrue($"スクリプト実行に失敗: {result1.Error}\nSQL:\n{script1}");

        // ---------- 4) もう一度 diff を取って空になることを確認 ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var diff2 = new SchemaDiffService().Compute(live2.Entities, live2.Relationships, new[] { parent, child }, new[] { rel });
        // ID は importer が新しい Guid を振り直すので、リレーションは「FK が DB 側に存在するか」で判定される
        diff2.Items.Where(i => i.Kind == SchemaDiffKind.AddTable).Should().BeEmpty();
        diff2.Items.Where(i => i.Kind == SchemaDiffKind.AddColumn).Should().BeEmpty();
        live2.Relationships.Should().ContainSingle();
        live2.Relationships[0].ConstraintName.Should().Be($"FK_{ChildTable}_{ParentTable}");
        live2.Relationships[0].TargetColumnId.Should().NotBeNull();

        // ---------- 5) 列追加の差分テスト ----------
        child.Columns.Add(new Column { Name = "AddedLater", DataType = "nvarchar(20)" });
        var diff3 = new SchemaDiffService().Compute(live2.Entities, live2.Relationships, new[] { parent, child }, new[] { rel });
        diff3.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName == "AddedLater");

        var script3 = SchemaSyncScriptBuilder.Build(diff3.Items.Where(i => i.Kind == SchemaDiffKind.AddColumn));
        var result3 = await exec.ExecuteAsync(Settings, script3, TestCancellationToken);
        result3.Committed.Should().BeTrue($"列追加に失敗: {result3.Error}\nSQL:\n{script3}");

        // ---------- 6) 列が実際に追加されたか sys カラムで検証 ----------
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        await using var verify = new SqlCommand($"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{ChildTable}') AND name = 'AddedLater'", conn);

        var count = (int)(await verify.ExecuteScalarAsync(TestCancellationToken))!;
        count.Should().Be(1);
    }

    [Fact(DisplayName = "[Integration] フェーズ2: AlterColumn / DropColumn / DropForeignKey / DropTable が実 DB に適用される")]
    public async Task DestructiveSync_AppliesChanges()
    {
        if (!_serverAvailable)
        {
            return;
        }

        var setup =
            $@"
CREATE TABLE [{ParentTable}] ([Id] int NOT NULL CONSTRAINT [PK_{ParentTable}] PRIMARY KEY, [Name] nvarchar(50) NULL);
CREATE TABLE [{ChildTable}] (
    [Id] int NOT NULL CONSTRAINT [PK_{ChildTable}] PRIMARY KEY,
    [{ParentTable}_Id] int NULL,
    [ToBeAltered] nvarchar(20) NULL,
    [ToBeDropped] int NULL,
    CONSTRAINT [FK_{ChildTable}_{ParentTable}] FOREIGN KEY ([{ParentTable}_Id]) REFERENCES [{ParentTable}] ([Id])
);";
        await using (var c = new SqlConnection(Settings.Build()))
        {
            await c.OpenAsync(TestCancellationToken);

            await using var cmd = new SqlCommand(setup, c);
            await cmd.ExecuteNonQueryAsync(TestCancellationToken);
        }

        // ---------- 期待状態: 子テーブルのみ残し、ToBeAltered の型を変更、ToBeDropped と FK と親テーブルを削除 ----------
        var child = new Entity { TableName = ChildTable };
        child.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        child.Columns.Add(new Column { Name = $"{ParentTable}_Id", DataType = "int" });
        child.Columns.Add(new Column { Name = "ToBeAltered", DataType = "nvarchar(100)" }); // 型を 20→100 に変更

        var importer = new SqlServerSchemaImporter();
        var live = await importer.ImportAsync(Settings, TestCancellationToken);
        var diff = new SchemaDiffService().Compute(live.Entities, live.Relationships, new[] { child }, new List<Relationship>());

        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName == "ToBeAltered");
        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName == "ToBeDropped");
        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.DropForeignKey);
        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.DropTable && i.TableName == ParentTable);

        // 既定では破壊的差分は未選択。テストでは全て選択して実行する。
        foreach (var item in diff.Items)
        {
            item.IsSelected = true;
        }

        var script = SchemaSyncScriptBuilder.Build(diff.Items);
        var exec = new SchemaSyncExecutor();
        var result = await exec.ExecuteAsync(Settings, script, TestCancellationToken);
        result.Committed.Should().BeTrue($"破壊的同期に失敗: {result.Error}\nSQL:\n{script}");

        // ---------- 検証 ----------
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        // 親テーブル DROP 済み
        await using (var v1 = new SqlCommand($"SELECT OBJECT_ID(N'{ParentTable}', N'U')", conn))
        {
            (await v1.ExecuteScalarAsync(TestCancellationToken)).Should().Be(DBNull.Value);
        }

        // 子の ToBeDropped 列が無い
        await using (var v2 = new SqlCommand($"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{ChildTable}') AND name = 'ToBeDropped'", conn))
        {
            ((int)(await v2.ExecuteScalarAsync(TestCancellationToken))!).Should().Be(0);
        }

        // ToBeAltered の最大長が 100 (=200 bytes for nvarchar) になっている
        await using (var v3 = new SqlCommand($"SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'{ChildTable}') AND name = 'ToBeAltered'", conn))
        {
            ((short)(await v3.ExecuteScalarAsync(TestCancellationToken))!).Should().Be(200);
        }
    }

    [Fact(DisplayName = "[Integration] テーブル/列の MS_Description が同期され、再 Import で取得できる")]
    public async Task DescriptionSync_RoundTrip()
    {
        if (!_serverAvailable)
        {
            return;
        }

        // ---------- 1) 期待状態 (説明付き) ----------
        var parent = new Entity { TableName = ParentTable, Description = "親テーブルの説明" };
        parent.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        parent.Columns.Add(
            new Column
            {
                Name = "Name",
                DataType = "nvarchar(50)",
                Description = "名前カラム",
            }
        );

        // ---------- 2) DB は空なので AddTable + SetTableDescription + SetColumnDescription が出る ----------
        var importer = new SqlServerSchemaImporter();
        var live1 = await importer.ImportAsync(Settings, TestCancellationToken);
        var diff1 = new SchemaDiffService().Compute(live1.Entities, live1.Relationships, new[] { parent }, new List<Relationship>());

        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddTable);
        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.SetTableDescription && i.NewDescription == "親テーブルの説明");
        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.SetColumnDescription && i.ColumnName == "Name" && i.NewDescription == "名前カラム");

        var script = SchemaSyncScriptBuilder.Build(diff1.Items);
        var exec = new SchemaSyncExecutor();
        var result = await exec.ExecuteAsync(Settings, script, TestCancellationToken);
        result.Committed.Should().BeTrue($"説明同期に失敗: {result.Error}\nSQL:\n{script}");

        // ---------- 3) 再 Import して説明が取得できることを確認 ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var imported = live2.Entities.Should().ContainSingle(e => e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)).Which;
        imported.Description.Should().Be("親テーブルの説明");
        imported.Columns.Should().ContainSingle(c => c.Name == "Name" && c.Description == "名前カラム");

        // ---------- 4) 説明を更新→ sp_updateextendedproperty 経由で反映される ----------
        // live と target でオブジェクトを分けるため、target は手で組み直す
        var updatedTarget = new Entity { TableName = imported.TableName, Description = "親テーブル(更新後)" };

        foreach (var c in imported.Columns)
        {
            updatedTarget.Columns.Add(
                new Column
                {
                    Name = c.Name,
                    DataType = c.DataType,
                    IsPrimaryKey = c.IsPrimaryKey,
                    Description = c.Name == "Name" ? "顧客名(更新後)" : c.Description,
                }
            );
        }

        var diff2 = new SchemaDiffService().Compute(live2.Entities, live2.Relationships, new[] { updatedTarget }, new List<Relationship>());
        diff2.Items.Should().Contain(i => i.Kind == SchemaDiffKind.SetTableDescription && i.NewDescription == "親テーブル(更新後)");
        diff2.Items.Should().Contain(i => i.Kind == SchemaDiffKind.SetColumnDescription && i.NewDescription == "顧客名(更新後)");

        var script2 = SchemaSyncScriptBuilder.Build(diff2.Items);
        var result2 = await exec.ExecuteAsync(Settings, script2, TestCancellationToken);
        result2.Committed.Should().BeTrue($"説明更新に失敗: {result2.Error}\nSQL:\n{script2}");

        var live3 = await importer.ImportAsync(Settings, TestCancellationToken);
        var imported2 = live3.Entities.First(e => e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase));
        imported2.Description.Should().Be("親テーブル(更新後)");
        imported2.Columns.First(c => c.Name == "Name").Description.Should().Be("顧客名(更新後)");
    }
}
