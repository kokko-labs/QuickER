using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>実 SQL Server に対しスキーマ同期を end-to-end で検証する統合テストクラス</summary>
/// <remarks>
/// localhost / TestDB / Windows 認証へ接続できない環境では各テストをスキップする
/// テスト用オブジェクトは <c>_erd_sync_test_</c> プレフィクスで作成し、前後で必ず DROP する
/// </remarks>
[Trait("Category", "Integration")]
public class SqlServerSchemaSyncIntegrationTests : IAsyncLifetime
{
    /// <summary>テスト全体で共有するキャンセルトークン</summary>
    private static readonly CancellationToken TestCancellationToken = TestContext
        .Current
        .CancellationToken;

    /// <summary>テスト対象 DB への接続設定</summary>
    private static readonly SqlConnectionSettings Settings = new()
    {
        Server = "localhost",
        Database = "TestDB",
        AuthMode = SqlAuthMode.Windows,
        TrustServerCertificate = true,
    };

    /// <summary>親テーブル名（FK の参照先）</summary>
    private const string ParentTable = "_erd_sync_test_parent";

    /// <summary>子テーブル名（FK の保有側）</summary>
    private const string ChildTable = "_erd_sync_test_child";

    /// <summary>主キー変更の検証に使う単独テーブル名（参照 FK を持たない）</summary>
    private const string ItemTable = "_erd_sync_test_item";

    /// <summary>テスト DB へ接続可能かどうか（不可ならテストをスキップする）</summary>
    private bool _serverAvailable;

    /// <summary>接続可否を判定し、可能ならテスト用オブジェクトを事前に削除する</summary>
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

    /// <summary>テスト終了時にテスト用オブジェクトを削除する</summary>
    public async ValueTask DisposeAsync()
    {
        if (_serverAvailable)
        {
            await DropTestObjectsAsync();
        }
    }

    /// <summary>テスト用の親子テーブルを依存順に DROP する</summary>
    private static async Task DropTestObjectsAsync()
    {
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        var script =
            $@"
IF OBJECT_ID(N'{ChildTable}', N'U') IS NOT NULL DROP TABLE [{ChildTable}];
IF OBJECT_ID(N'{ParentTable}', N'U') IS NOT NULL DROP TABLE [{ParentTable}];
IF OBJECT_ID(N'{ItemTable}', N'U') IS NOT NULL DROP TABLE [{ItemTable}];";
        await using var cmd = new SqlCommand(script, conn);

        await cmd.ExecuteNonQueryAsync(TestCancellationToken);
    }

    /// <summary>テーブル追加・列追加・外部キー追加が実 DB へ適用されることを検証する</summary>
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
        var diff1 = new SchemaDiffService().Compute(
            live1.Entities,
            live1.Relationships,
            new[] { parent, child },
            new[] { rel }
        );

        diff1
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == ParentTable);
        diff1
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AddTable && i.TableName == ChildTable);
        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddForeignKey);

        // ---------- 3) 実行 ----------
        var script1 = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff1.Items, new SyncDialectCapabilities())
        );
        var exec = new SqlServerSchemaSyncExecutor();
        var result1 = await exec.ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script1,
            TestCancellationToken
        );
        result1
            .Committed.Should()
            .BeTrue($"スクリプト実行に失敗: {result1.Error}\nSQL:\n{script1}");

        // ---------- 4) もう一度 diff を取って空になることを確認 ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var diff2 = new SchemaDiffService().Compute(
            live2.Entities,
            live2.Relationships,
            new[] { parent, child },
            new[] { rel }
        );
        // ID は importer が新しい Guid を振り直すので、リレーションは「FK が DB 側に存在するか」で判定される
        diff2.Items.Where(i => i.Kind == SchemaDiffKind.AddTable).Should().BeEmpty();
        diff2.Items.Where(i => i.Kind == SchemaDiffKind.AddColumn).Should().BeEmpty();
        live2.Relationships.Should().ContainSingle();
        live2.Relationships[0].ConstraintName.Should().Be($"FK_{ChildTable}_{ParentTable}");
        live2.Relationships[0].TargetColumnId.Should().NotBeNull();
        live2.Relationships[0].OnDelete.Should().Be(ForeignKeyReferentialAction.NoAction);
        live2.Relationships[0].OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);

        // ---------- 5) 列追加の差分テスト ----------
        child.Columns.Add(new Column { Name = "AddedLater", DataType = "nvarchar(20)" });
        var diff3 = new SchemaDiffService().Compute(
            live2.Entities,
            live2.Relationships,
            new[] { parent, child },
            new[] { rel }
        );
        diff3
            .Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName == "AddedLater");

        var script3 = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(
                diff3.Items.Where(i => i.Kind == SchemaDiffKind.AddColumn),
                new SyncDialectCapabilities()
            )
        );
        var result3 = await exec.ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script3,
            TestCancellationToken
        );
        result3.Committed.Should().BeTrue($"列追加に失敗: {result3.Error}\nSQL:\n{script3}");

        // ---------- 6) 列が実際に追加されたか sys カラムで検証 ----------
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        await using var verify = new SqlCommand(
            $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{ChildTable}') AND name = 'AddedLater'",
            conn
        );

        var count = (int)(await verify.ExecuteScalarAsync(TestCancellationToken))!;
        count.Should().Be(1);
    }

    /// <summary>列変更・列削除・外部キー削除・テーブル削除の破壊的変更が実 DB へ適用されることを検証する</summary>
    [Fact(
        DisplayName = "[Integration] フェーズ2: AlterColumn / DropColumn / DropForeignKey / DropTable が実 DB に適用される"
    )]
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
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            new[] { child },
            new List<Relationship>()
        );

        diff.Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName == "ToBeAltered");
        diff.Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.DropColumn && i.ColumnName == "ToBeDropped");
        diff.Items.Should().Contain(i => i.Kind == SchemaDiffKind.DropForeignKey);
        diff.Items.Should()
            .Contain(i => i.Kind == SchemaDiffKind.DropTable && i.TableName == ParentTable);

        // 既定では破壊的差分は未選択。テストでは全て選択して実行する。
        foreach (var item in diff.Items)
        {
            item.IsSelected = true;
        }

        var script = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff.Items, new SyncDialectCapabilities())
        );
        var exec = new SqlServerSchemaSyncExecutor();
        var result = await exec.ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script,
            TestCancellationToken
        );
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
        await using (
            var v2 = new SqlCommand(
                $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{ChildTable}') AND name = 'ToBeDropped'",
                conn
            )
        )
        {
            ((int)(await v2.ExecuteScalarAsync(TestCancellationToken))!).Should().Be(0);
        }

        // ToBeAltered の最大長が 100 (=200 bytes for nvarchar) になっている
        await using (
            var v3 = new SqlCommand(
                $"SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'{ChildTable}') AND name = 'ToBeAltered'",
                conn
            )
        )
        {
            ((short)(await v3.ExecuteScalarAsync(TestCancellationToken))!).Should().Be(200);
        }
    }

    /// <summary>テーブル・列の MS_Description が同期され、再インポートで取得できることを検証する</summary>
    [Fact(
        DisplayName = "[Integration] テーブル/列の MS_Description が同期され、再 Import で取得できる"
    )]
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
        var diff1 = new SchemaDiffService().Compute(
            live1.Entities,
            live1.Relationships,
            new[] { parent },
            new List<Relationship>()
        );

        diff1.Items.Should().Contain(i => i.Kind == SchemaDiffKind.AddTable);
        diff1
            .Items.Should()
            .Contain(i =>
                i.Kind == SchemaDiffKind.SetTableDescription
                && i.NewDescription == "親テーブルの説明"
            );
        diff1
            .Items.Should()
            .Contain(i =>
                i.Kind == SchemaDiffKind.SetColumnDescription
                && i.ColumnName == "Name"
                && i.NewDescription == "名前カラム"
            );

        var script = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff1.Items, new SyncDialectCapabilities())
        );
        var exec = new SqlServerSchemaSyncExecutor();
        var result = await exec.ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script,
            TestCancellationToken
        );
        result.Committed.Should().BeTrue($"説明同期に失敗: {result.Error}\nSQL:\n{script}");

        // ---------- 3) 再 Import して説明が取得できることを確認 ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var imported = live2
            .Entities.Should()
            .ContainSingle(e =>
                e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)
            )
            .Which;
        imported.Description.Should().Be("親テーブルの説明");
        imported
            .Columns.Should()
            .ContainSingle(c => c.Name == "Name" && c.Description == "名前カラム");

        // ---------- 4) 説明を更新→ sp_updateextendedproperty 経由で反映される ----------
        // live と target でオブジェクトを分けるため、target は手で組み直す
        var updatedTarget = new Entity
        {
            TableName = imported.TableName,
            Description = "親テーブル(更新後)",
        };

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

        var diff2 = new SchemaDiffService().Compute(
            live2.Entities,
            live2.Relationships,
            new[] { updatedTarget },
            new List<Relationship>()
        );
        diff2
            .Items.Should()
            .Contain(i =>
                i.Kind == SchemaDiffKind.SetTableDescription
                && i.NewDescription == "親テーブル(更新後)"
            );
        diff2
            .Items.Should()
            .Contain(i =>
                i.Kind == SchemaDiffKind.SetColumnDescription
                && i.NewDescription == "顧客名(更新後)"
            );

        var script2 = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff2.Items, new SyncDialectCapabilities())
        );
        var result2 = await exec.ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script2,
            TestCancellationToken
        );
        result2.Committed.Should().BeTrue($"説明更新に失敗: {result2.Error}\nSQL:\n{script2}");

        var live3 = await importer.ImportAsync(Settings, TestCancellationToken);
        var imported2 = live3.Entities.First(e =>
            e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)
        );
        imported2.Description.Should().Be("親テーブル(更新後)");
        imported2.Columns.First(c => c.Name == "Name").Description.Should().Be("顧客名(更新後)");
    }

    /// <summary>主キーの付け替え（Id → Code）が実 DB へ適用され、行データが温存されることを検証する</summary>
    /// <remarks>参照してくる FK が無いテーブルで、主キー変更単体の往復を確認する</remarks>
    [Fact(
        DisplayName = "[Integration] AlterPrimaryKey: 主キーを Id から Code へ付け替えてもデータが温存される"
    )]
    public async Task PrimaryKeySync_MovesPrimaryKeyToAnotherColumn()
    {
        if (!_serverAvailable)
        {
            return;
        }

        await RunScriptAsync(
            $@"
CREATE TABLE [{ItemTable}] (
    [Code] nvarchar(20) NOT NULL,
    [Id] int NOT NULL,
    CONSTRAINT [PK_{ItemTable}] PRIMARY KEY ([Id])
);
INSERT INTO [{ItemTable}] ([Code], [Id]) VALUES (N'A-001', 1);
INSERT INTO [{ItemTable}] ([Code], [Id]) VALUES (N'A-002', 2);"
        );

        var importer = new SqlServerSchemaImporter();
        var live = await importer.ImportAsync(Settings, TestCancellationToken);
        var liveItem = live.Entities.Single(e =>
            e.TableName.EndsWith(ItemTable, StringComparison.OrdinalIgnoreCase)
        );

        // 目標: 列構成・型は変えず、主キーだけ Id から Code へ移す
        var target = liveItem.Clone(preserveId: true);
        target.Columns.Single(c => c.Name == "Id").IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "Code").IsPrimaryKey = true;

        var capabilities = new SqlServerProvider().SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            new[] { target },
            new List<Relationship>(),
            capabilities
        );

        // 主キー変更は既定で未選択のため、対象項目だけを明示的に選択する
        var alterPk = diff
            .Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey
                && i.TableName.EndsWith(ItemTable, StringComparison.OrdinalIgnoreCase)
            )
            .Which;
        alterPk.IsSelected = true;

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        var script = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff.Items, capabilities, context)
        );
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script,
            TestCancellationToken
        );
        result.Committed.Should().BeTrue($"主キー変更に失敗: {result.Error}\nSQL:\n{script}");

        // ---------- 再取込: 主キーが Code へ移っている ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var item2 = live2.Entities.Single(e =>
            e.TableName.EndsWith(ItemTable, StringComparison.OrdinalIgnoreCase)
        );
        item2.Columns.Single(c => c.Name == "Code").IsPrimaryKey.Should().BeTrue();
        item2.Columns.Single(c => c.Name == "Id").IsPrimaryKey.Should().BeFalse();

        // ---------- 行データは失われない ----------
        var rows = await QueryItemRowsAsync();
        rows.Should().Equal(("A-001", 1), ("A-002", 2));
    }

    /// <summary>
    /// FK に参加している列の定義変更で、依存 FK が自動 DROP → 再 ADD され同期が成功することを検証する。
    /// </summary>
    /// <remarks>
    /// SQL Server は FOREIGN KEY 制約に参加している列の <c>ALTER COLUMN</c> を拒否する（Msg 5074）。
    /// <see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> により
    /// <see cref="SyncPlanner"/> が FK の DROP と再 ADD を注入することで、従来失敗していたこのケースが通る。
    /// 型（長さ）を変えると再 ADD 時に参照先と型が一致せず失敗するため、ここでは NULL 許容のみを変更する。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] AlterColumn: FK 参加列の定義変更で依存 FK が自動 DROP → 再 ADD される"
    )]
    public async Task AlterColumn_OnForeignKeyColumn_RebuildsDependentForeignKey()
    {
        if (!_serverAvailable)
        {
            return;
        }

        await RunScriptAsync(
            $@"
CREATE TABLE [{ParentTable}] (
    [Code] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_{ParentTable}] PRIMARY KEY ([Code])
);
CREATE TABLE [{ChildTable}] (
    [Id] int NOT NULL,
    [ParentCode] nvarchar(20) NULL,
    CONSTRAINT [PK_{ChildTable}] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_{ChildTable}_{ParentTable}] FOREIGN KEY ([ParentCode]) REFERENCES [{ParentTable}] ([Code])
);
INSERT INTO [{ParentTable}] ([Code]) VALUES (N'P-1');
INSERT INTO [{ChildTable}] ([Id], [ParentCode]) VALUES (1, N'P-1');"
        );

        var importer = new SqlServerSchemaImporter();
        var live = await importer.ImportAsync(Settings, TestCancellationToken);
        var liveParent = live.Entities.Single(e =>
            e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)
        );
        var liveChild = live.Entities.Single(e =>
            e.TableName.EndsWith(ChildTable, StringComparison.OrdinalIgnoreCase)
        );
        var liveFk = live.Relationships.Single(r => r.TargetEntityId == liveChild.Id);

        // 目標: FK は図上維持したまま、FK 参加列 ParentCode を NULL 許容から NOT NULL へ変更する
        var parentTarget = liveParent.Clone(preserveId: true);
        var childTarget = liveChild.Clone(preserveId: true);
        childTarget.Columns.Single(c => c.Name == "ParentCode").IsNullable = false;

        var relKeep = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentTarget.Columns.Single(c => c.Name == "Code").Id,
            TargetColumnId = childTarget.Columns.Single(c => c.Name == "ParentCode").Id,
            ConstraintName = liveFk.ConstraintName,
        };

        var capabilities = new SqlServerProvider().SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            new[] { parentTarget, childTarget },
            new[] { relKeep },
            capabilities
        );

        // FK 差分は出ない（図上は維持）ことと、列定義変更が 1 件出ることを確認する
        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.DropForeignKey
                && i.TableName.EndsWith(ChildTable, StringComparison.OrdinalIgnoreCase)
            );
        var alterColumn = diff
            .Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName == "ParentCode"
            )
            .Which;
        alterColumn.IsSelected = true;

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        var plan = new SyncPlanner().BuildPlan(diff.Items, capabilities, context);

        // プランナーが依存 FK の DROP と再 ADD を注入していること
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.DropForeignKey);
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddForeignKey);

        var script = new SqlServerSyncScriptBuilder().Build(plan);
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script,
            TestCancellationToken
        );
        result.Committed.Should().BeTrue($"FK 参加列の変更に失敗: {result.Error}\nSQL:\n{script}");

        // ---------- 再取込: 列定義が変わり、FK は存続している ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var child2 = live2.Entities.Single(e =>
            e.TableName.EndsWith(ChildTable, StringComparison.OrdinalIgnoreCase)
        );
        child2.Columns.Single(c => c.Name == "ParentCode").IsNullable.Should().BeFalse();
        live2
            .Relationships.Should()
            .Contain(r => r.ConstraintName == liveFk.ConstraintName, "FK は再 ADD されているはず");

        // 行データも失われない
        (await QueryScalarIntAsync($"SELECT COUNT(*) FROM [{ChildTable}];"))
            .Should()
            .Be(1);
    }

    /// <summary>
    /// 被参照列が候補キーでなくなる主キー変更は、自動再 ADD される FK が実行時に失敗し、
    /// トランザクションごとロールバックされることを検証する（既知の限界の現状固定）。
    /// </summary>
    /// <remarks>
    /// 親の主キーを Id から Code へ移すと、Id を参照している子の FK は再 ADD できない（参照先が候補キーでなくなるため）。
    /// この場合にサイレントな破壊（FK だけ消えて主キーが変わる）が起きず、DB が元の状態のまま残ることを確認する。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] AlterPrimaryKey: 被参照列が候補キーでなくなる変更は失敗しロールバックされる"
    )]
    public async Task PrimaryKeyChange_BreakingDependentForeignKey_RollsBack()
    {
        if (!_serverAvailable)
        {
            return;
        }

        await RunScriptAsync(
            $@"
CREATE TABLE [{ParentTable}] (
    [Id] int NOT NULL,
    [Code] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_{ParentTable}] PRIMARY KEY ([Id])
);
CREATE TABLE [{ChildTable}] (
    [Id] int NOT NULL,
    [ParentId] int NULL,
    CONSTRAINT [PK_{ChildTable}] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_{ChildTable}_{ParentTable}] FOREIGN KEY ([ParentId]) REFERENCES [{ParentTable}] ([Id])
);
INSERT INTO [{ParentTable}] ([Id], [Code]) VALUES (1, N'P-1');
INSERT INTO [{ChildTable}] ([Id], [ParentId]) VALUES (1, 1);"
        );

        var importer = new SqlServerSchemaImporter();
        var live = await importer.ImportAsync(Settings, TestCancellationToken);
        var liveParent = live.Entities.Single(e =>
            e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)
        );
        var liveChild = live.Entities.Single(e =>
            e.TableName.EndsWith(ChildTable, StringComparison.OrdinalIgnoreCase)
        );
        var liveFk = live.Relationships.Single(r => r.TargetEntityId == liveChild.Id);

        // 目標: 親の主キーを Id から Code へ移す（子の FK は Id を参照したまま維持）
        var parentTarget = liveParent.Clone(preserveId: true);
        parentTarget.Columns.Single(c => c.Name == "Id").IsPrimaryKey = false;
        parentTarget.Columns.Single(c => c.Name == "Code").IsPrimaryKey = true;
        var childTarget = liveChild.Clone(preserveId: true);

        var relKeep = new Relationship
        {
            SourceEntityId = parentTarget.Id,
            TargetEntityId = childTarget.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = parentTarget.Columns.Single(c => c.Name == "Id").Id,
            TargetColumnId = childTarget.Columns.Single(c => c.Name == "ParentId").Id,
            ConstraintName = liveFk.ConstraintName,
        };

        var capabilities = new SqlServerProvider().SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            new[] { parentTarget, childTarget },
            new[] { relKeep },
            capabilities
        );

        var alterPk = diff
            .Items.Should()
            .ContainSingle(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey
                && i.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)
            )
            .Which;
        alterPk.IsSelected = true;

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
        };
        var script = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(diff.Items, capabilities, context)
        );
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script,
            TestCancellationToken
        );

        // 再 ADD できない FK があるため実行は失敗し、エラーが報告される
        result
            .Committed.Should()
            .BeFalse($"再 ADD できない FK があるため失敗するはず\nSQL:\n{script}");
        result.Error.Should().NotBeNullOrEmpty();

        // ---------- SQL Server は単一トランザクションのため DB は元のまま ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var parent2 = live2.Entities.Single(e =>
            e.TableName.EndsWith(ParentTable, StringComparison.OrdinalIgnoreCase)
        );
        parent2.Columns.Single(c => c.Name == "Id").IsPrimaryKey.Should().BeTrue();
        parent2.Columns.Single(c => c.Name == "Code").IsPrimaryKey.Should().BeFalse();
        live2
            .Relationships.Should()
            .Contain(r => r.ConstraintName == liveFk.ConstraintName, "FK も元のまま残るはず");
        (await QueryScalarIntAsync($"SELECT COUNT(*) FROM [{ChildTable}];")).Should().Be(1);
    }

    /// <summary>図に足した一意制約が実 DB へ追加され、外した一意制約が実 DB から消えることを検証する</summary>
    [Fact(DisplayName = "[Integration] UniqueConstraint: 追加・削除が実 DB へ反映される")]
    public async Task UniqueConstraintSync_AddsAndDrops()
    {
        if (!_serverAvailable)
        {
            return;
        }

        await RunScriptAsync(
            $@"
CREATE TABLE [{ItemTable}] (
    [Code] nvarchar(20) NOT NULL,
    [Id] int NOT NULL,
    CONSTRAINT [PK_{ItemTable}] PRIMARY KEY ([Id])
);
INSERT INTO [{ItemTable}] ([Code], [Id]) VALUES (N'A-001', 1);"
        );

        var importer = new SqlServerSchemaImporter();
        var capabilities = new SqlServerProvider().SyncCapabilities;

        // ---------- 1) 一意制約の追加 ----------
        var live = await importer.ImportAsync(Settings, TestCancellationToken);
        var liveItem = SingleItemEntity(live);
        var target = liveItem.Clone(preserveId: true);
        target.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [target.Columns.Single(c => c.Name == "Code").Id] }
        );

        var addDiff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            new[] { target },
            new List<Relationship>(),
            capabilities
        );
        var add = addDiff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AddUniqueConstraint)
            .Which;
        // 追加は既定で選択される（制約を増やすだけで既存定義を壊さないため）
        add.IsSelected.Should().BeTrue();

        var addScript = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(addDiff.Items, capabilities, PlanContext(live))
        );
        var addResult = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            addScript,
            TestCancellationToken
        );
        addResult
            .Committed.Should()
            .BeTrue($"一意制約の追加に失敗: {addResult.Error}\nSQL:\n{addScript}");

        (await QueryScalarIntAsync(UniqueConstraintCountSql)).Should().Be(1);

        // ---------- 2) 一意制約の削除（取込 → 図から外す） ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var liveItem2 = SingleItemEntity(live2);
        liveItem2.UniqueConstraints.Should().ContainSingle();

        var target2 = liveItem2.Clone(preserveId: true);
        target2.UniqueConstraints.Clear();

        var dropDiff = new SchemaDiffService().Compute(
            live2.Entities,
            live2.Relationships,
            new[] { target2 },
            new List<Relationship>(),
            capabilities
        );
        var drop = dropDiff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.DropUniqueConstraint)
            .Which;
        // 削除は破壊的のため既定では未選択＝明示的に選ぶ
        drop.IsSelected.Should().BeFalse();
        drop.IsSelected = true;

        var dropScript = new SqlServerSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(dropDiff.Items, capabilities, PlanContext(live2))
        );
        var dropResult = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            dropScript,
            TestCancellationToken
        );
        dropResult
            .Committed.Should()
            .BeTrue($"一意制約の削除に失敗: {dropResult.Error}\nSQL:\n{dropScript}");

        (await QueryScalarIntAsync(UniqueConstraintCountSql)).Should().Be(0);
    }

    /// <summary>
    /// 一意制約に参加している列の定義変更で、制約が自動 DROP → 再 ADD され同期が成功することを検証する。
    /// </summary>
    /// <remarks>
    /// SQL Server は UNIQUE 制約に参加している列の <c>ALTER COLUMN</c> を拒否する（FK と同じ Msg 5074）。
    /// <see cref="SyncPlanner"/> が一意制約の DROP と再 ADD を注入することで、このケースが通る。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] AlterColumn: UNIQUE 構成列の定義変更で制約が自動 DROP → 再 ADD される"
    )]
    public async Task AlterColumn_OnUniqueConstraintColumn_RebuildsUniqueConstraint()
    {
        if (!_serverAvailable)
        {
            return;
        }

        await RunScriptAsync(
            $@"
CREATE TABLE [{ItemTable}] (
    [Code] nvarchar(20) NOT NULL,
    [Id] int NOT NULL,
    CONSTRAINT [PK_{ItemTable}] PRIMARY KEY ([Id]),
    CONSTRAINT [UQ_{ItemTable}_Code] UNIQUE ([Code])
);
INSERT INTO [{ItemTable}] ([Code], [Id]) VALUES (N'A-001', 1);"
        );

        var importer = new SqlServerSchemaImporter();
        var capabilities = new SqlServerProvider().SyncCapabilities;
        var live = await importer.ImportAsync(Settings, TestCancellationToken);
        var liveItem = SingleItemEntity(live);
        liveItem.UniqueConstraints.Should().ContainSingle();

        // 目標: 一意制約はそのままに、その構成列 Code の長さを 20 → 40 へ広げる
        var target = liveItem.Clone(preserveId: true);
        target.Columns.Single(c => c.Name == "Code").DataType = "nvarchar(40)";

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            new[] { target },
            new List<Relationship>(),
            capabilities
        );

        // 一意制約は図と DB で一致しているため差分は出ず、列定義変更だけが出る
        diff.Items.Should()
            .NotContain(i =>
                i.Kind == SchemaDiffKind.AddUniqueConstraint
                || i.Kind == SchemaDiffKind.DropUniqueConstraint
            );
        var alterColumn = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterColumn && i.ColumnName == "Code")
            .Which;
        alterColumn.IsSelected = true;

        var plan = new SyncPlanner().BuildPlan(diff.Items, capabilities, PlanContext(live));

        // プランナーが一意制約の DROP と再 ADD を注入していること
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.DropUniqueConstraint);
        plan.Sections.Should().Contain(s => s.Kind == SchemaDiffKind.AddUniqueConstraint);

        var script = new SqlServerSyncScriptBuilder().Build(plan);
        var result = await new SqlServerSchemaSyncExecutor().ExecuteAsync(
            Settings.ToDbConnectionSettings(),
            script,
            TestCancellationToken
        );
        result
            .Committed.Should()
            .BeTrue($"UNIQUE 構成列の変更に失敗: {result.Error}\nSQL:\n{script}");

        // ---------- 再取込: 列定義が変わり、一意制約は存続している ----------
        var live2 = await importer.ImportAsync(Settings, TestCancellationToken);
        var item2 = SingleItemEntity(live2);
        item2.Columns.Single(c => c.Name == "Code").DataType.Should().Be("nvarchar(40)");
        item2.UniqueConstraints.Should().ContainSingle();
        (await QueryScalarIntAsync(UniqueConstraintCountSql)).Should().Be(1);
    }

    // ---------------- ヘルパー ----------------

    /// <summary>検証用テーブルに張られた UNIQUE 制約の数を数える SQL</summary>
    private static readonly string UniqueConstraintCountSql =
        "SELECT COUNT(*) FROM sys.key_constraints "
        + $"WHERE type = 'UQ' AND parent_object_id = OBJECT_ID(N'{ItemTable}');";

    /// <summary>取込結果から検証用テーブルのエンティティを取り出す</summary>
    private static Entity SingleItemEntity(SqlServerSchemaImporter.SchemaResult result) =>
        result.Entities.Single(e =>
            e.TableName.EndsWith(ItemTable, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>取込結果から計画用の live 入力を組み立てる</summary>
    private static SyncPlanContext PlanContext(SqlServerSchemaImporter.SchemaResult result) =>
        new() { LiveEntities = result.Entities, LiveRelationships = result.Relationships };

    /// <summary>セットアップ用の T-SQL をそのまま実行する</summary>
    private static async Task RunScriptAsync(string sql)
    {
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestCancellationToken);
    }

    /// <summary>単一の int スカラーを取得する（件数検証用）</summary>
    private static async Task<int> QueryScalarIntAsync(string sql)
    {
        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        await using var cmd = new SqlCommand(sql, conn);
        return (int)(await cmd.ExecuteScalarAsync(TestCancellationToken))!;
    }

    /// <summary>主キー変更の検証用テーブルの行を取得する（データ温存の確認用）</summary>
    private static async Task<List<(string Code, int Id)>> QueryItemRowsAsync()
    {
        var rows = new List<(string Code, int Id)>();

        await using var conn = new SqlConnection(Settings.Build());

        await conn.OpenAsync(TestCancellationToken);

        await using var cmd = new SqlCommand(
            $"SELECT [Code], [Id] FROM [{ItemTable}] ORDER BY [Id];",
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync(TestCancellationToken);

        while (await reader.ReadAsync(TestCancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return rows;
    }
}
