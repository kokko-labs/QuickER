using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQLite の DB 同期（テーブル再構築方式）をエンドツーエンドで検証する統合テスト。
/// live DB 構築 → 取込 → 差分計算（capabilities 付き）→ 計画（context 付き）→ スクリプト生成 → 実行の一連を通す。
/// </summary>
/// <remarks>
/// SQLite はインプロセス（Microsoft.Data.Sqlite）のため Docker / Testcontainers を使わず、CI でも常時実行される。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteSchemaSyncIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// 未作成パス → 新規空 DB 作成 → 取込（テーブル 0）→ テーブル・FK ありの図との差分同期で、
    /// 新規ファイルにスキーマが作られ行の挿入までできることを検証する（新規作成同期先のエンドツーエンド）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 未作成ファイルを新規作成し空 DB へスキーマを同期できる"
    )]
    public async Task CreateNewFile_ThenSyncSchema_BuildsSchemaAndAcceptsRows()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        // まだファイルは存在しない（取込・同期の既存ガードがブロックする状態）
        System.IO.File.Exists(db.FilePath).Should().BeFalse();

        // 明示的なユーザー操作（新規作成ボタン）相当で空 DB を作成する
        SqliteDatabaseFile.CreateEmpty(db.FilePath);
        System.IO.File.Exists(db.FilePath).Should().BeTrue();

        // 取込するとテーブルは 0 件（空 DB）
        var live = await ImportAsync(db, provider);
        live.Entities.Should().BeEmpty();

        // 目標: category と product（FK product.category_id -> category.id）を新規追加する
        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var category = new Entity
                {
                    TableName = "category",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                };
                var product = new Entity
                {
                    TableName = "product",
                    Columns =
                    {
                        new Column
                        {
                            Name = "id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "category_id",
                            DataType = "INTEGER",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "name",
                            DataType = "TEXT",
                            IsNullable = true,
                        },
                    },
                };
                entities.Add(category);
                entities.Add(product);
                relationships.Add(
                    new Relationship
                    {
                        SourceEntityId = category.Id,
                        TargetEntityId = product.Id,
                        Type = RelationshipType.OneToMany,
                        ColumnPairs =
                        [
                            new(
                                category.Columns.Single(c => c.Name == "id").Id,
                                product.Columns.Single(c => c.Name == "category_id").Id
                            ),
                        ],
                    }
                );
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // 新規ファイルに 2 テーブルが作られ、product に FK が張られている
        var reimported = await ImportAsync(db, provider);
        reimported
            .Entities.Select(e => e.TableName)
            .Should()
            .BeEquivalentTo(new[] { "category", "product" });
        (await QueryForeignKeyCountAsync(db, "product")).Should().Be(1);

        // 行の挿入までできる（親→子の順で FK 整合）
        await RunStatementsAsync(
            db,
            "INSERT INTO \"category\" (\"id\") VALUES (1);",
            "INSERT INTO \"product\" (\"id\", \"category_id\", \"name\") VALUES (10, 1, 'apple');"
        );

        var inserted = await QueryScalarLongAsync(
            db,
            "SELECT \"id\" FROM \"product\" WHERE \"category_id\" = 1;"
        );
        inserted.Should().Be(10);
    }

    /// <summary>単一の long スカラーを取得する（新規作成同期の行挿入検証用）</summary>
    private static async Task<long> QueryScalarLongAsync(SqliteTempDatabase db, string sql)
    {
        await using var conn = new SqliteConnection(NonPooledReadOnlyConnectionString(db));
        await conn.OpenAsync(Ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(Ct).ConfigureAwait(false);
        return System.Convert.ToInt64(value);
    }

    // ---------------- 接続の設定 ----------------

    /// <summary>
    /// 同期の後に、実行器と同じ接続文字列で開いた接続（プールから戻った接続）の外部キー検査と
    /// RENAME の動作が、同期前と同じ設定に戻っていることを検証する。
    /// </summary>
    /// <remarks>
    /// 実行器はトランザクション外で <c>foreign_keys=OFF</c> と <c>legacy_alter_table=ON</c> を設定する。
    /// Microsoft.Data.Sqlite は接続をプールし、同梱の e_sqlite3 は既定で外部キー検査が ON なので、
    /// 戻さないと同じ接続文字列で後から開いた接続が外部キー検査なしのまま使われる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 同期の後にプールから戻った接続の外部キー検査と RENAME の動作は元のまま"
    )]
    public async Task AfterSync_PooledConnectionPragmasAreRestored()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"pr_item\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_pr_item\" PRIMARY KEY (\"id\"));"
        );

        // 実行器と同じ接続文字列（ReadWrite・プールあり）
        var executorConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = db.FilePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ConnectionString;

        var before = await ReadPragmasAsync(executorConnectionString);

        var (result, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "pr_item")
                    .Columns.Single(c => c.Name == "note")
                    .DataType = "VARCHAR(10)",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );
        result.Committed.Should().BeTrue(result.Error);

        var after = await ReadPragmasAsync(executorConnectionString);

        // 同梱の e_sqlite3 は既定で外部キー検査が ON（1）・旧来の RENAME は OFF（0）
        before.Should().Be((1L, 0L));
        after.Should().Be(before);

        static async Task<(long ForeignKeys, long LegacyAlterTable)> ReadPragmasAsync(
            string connectionString
        )
        {
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(Ct);
            await using var fk = conn.CreateCommand();
            fk.CommandText = "PRAGMA foreign_keys;";
            var foreignKeys = (long)(await fk.ExecuteScalarAsync(Ct))!;
            await using var legacy = conn.CreateCommand();
            legacy.CommandText = "PRAGMA legacy_alter_table;";
            var legacyAlterTable = (long)(await legacy.ExecuteScalarAsync(Ct))!;
            return (foreignKeys, legacyAlterTable);
        }
    }

    /// <summary>
    /// GUI と同じく live と別の Guid 空間の図を組み立て、選んだ差分だけで計画→スクリプト生成→実行までを通す。
    /// </summary>
    /// <remarks>
    /// <see cref="RunSyncAsync"/> は live を <c>Clone(preserveId: true)</c> で図にするため、列 ID が live と
    /// 一致して「図側の列を合成に持ち込む」不具合が表に出ない。こちらは <c>Clone(preserveId: false)</c> で
    /// 図を作り、リレーションの列ペアも新しい ID へ張り替える。
    /// </remarks>
    private static async Task<(
        SchemaSyncResult Result,
        string Script,
        SyncPlan Plan
    )> RunSyncWithFreshIdsAsync(
        SqliteTempDatabase db,
        SqliteProvider provider,
        System.Action<List<Entity>, List<Relationship>> editTarget,
        System.Func<SchemaDiffItem, bool> select
    )
    {
        var live = await ImportAsync(db, provider);

        var entityIdMap = new Dictionary<System.Guid, System.Guid>();
        var columnIdMap = new Dictionary<System.Guid, System.Guid>();
        var targetEntities = new List<Entity>();

        foreach (var entity in live.Entities)
        {
            var clone = entity.Clone(preserveId: false);
            entityIdMap[entity.Id] = clone.Id;

            for (var i = 0; i < entity.Columns.Count; i++)
            {
                columnIdMap[entity.Columns[i].Id] = clone.Columns[i].Id;
            }

            targetEntities.Add(clone);
        }

        var targetRelationships = live
            .Relationships.Select(r => new Relationship
            {
                SourceEntityId = entityIdMap[r.SourceEntityId],
                TargetEntityId = entityIdMap[r.TargetEntityId],
                Type = r.Type,
                ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone(columnIdMap))],
                ConstraintName = r.ConstraintName,
                OnDelete = r.OnDelete,
                OnUpdate = r.OnUpdate,
            })
            .ToList();

        editTarget(targetEntities, targetRelationships);

        var capabilities = provider.SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            targetEntities,
            targetRelationships,
            capabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = select(item);
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            capabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
                AuxiliaryObjects = live.AuxiliaryObjects,
                TableCreateSql = live.TableCreateSql,
            }
        );
        var script = provider.SyncScriptBuilder.Build(plan);

        var result = await provider
            .SyncExecutor.ExecuteAsync(
                new DbConnectionSettings { FilePath = db.FilePath },
                script,
                Ct
            )
            .ConfigureAwait(false);

        return (result, script, plan);
    }

    /// <summary>live DB を取り込み、目標スキーマを組み立て、差分計算→計画→スクリプト生成→実行までを通す</summary>
    private static async Task<(SchemaSyncResult Result, string Script)> RunSyncAsync(
        SqliteTempDatabase db,
        SqliteProvider provider,
        System.Func<
            List<Entity>,
            List<Relationship>,
            (List<Entity>, List<Relationship>)
        > buildTarget
    )
    {
        var live = await ImportAsync(db, provider);

        // 目標は live の深いコピー（ID 維持）を土台に変換する（構造以外は live と一致＝最小差分になる）
        var targetEntities = live.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        var targetRelationships = live.Relationships.Select(CloneRelationship).ToList();
        var (finalEntities, finalRelationships) = buildTarget(targetEntities, targetRelationships);

        var capabilities = provider.SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            finalEntities,
            finalRelationships,
            capabilities
        );

        // 統合テストでは検出された差分をすべて選択する
        foreach (var item in diff.Items)
        {
            item.IsSelected = true;
        }

        var context = new SyncPlanContext
        {
            LiveEntities = live.Entities,
            LiveRelationships = live.Relationships,
            AuxiliaryObjects = live.AuxiliaryObjects,
            TableCreateSql = live.TableCreateSql,
        };
        var plan = new SyncPlanner().BuildPlan(diff.Items, capabilities, context);
        var script = provider.SyncScriptBuilder.Build(plan);

        var result = await provider
            .SyncExecutor.ExecuteAsync(
                new DbConnectionSettings { FilePath = db.FilePath },
                script,
                Ct
            )
            .ConfigureAwait(false);

        return (result, script);
    }

    /// <summary>取込専用（ReadOnly）接続でスキーマ・補助オブジェクトを取り込む</summary>
    private static async Task<SchemaImportResult> ImportAsync(
        SqliteTempDatabase db,
        SqliteProvider provider
    )
    {
        return await provider
            .SchemaImporter.ImportAsync(
                db.ReadOnlyConnectionString,
                DbCommands.DefaultTimeoutSeconds,
                Ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>Relationship の浅いコピー（差分計算に必要なフィールドのみ複製する）</summary>
    private static Relationship CloneRelationship(Relationship r) =>
        new()
        {
            Id = r.Id,
            SourceEntityId = r.SourceEntityId,
            TargetEntityId = r.TargetEntityId,
            Type = r.Type,
            ColumnPairs = [.. r.ColumnPairs.Select(p => p.Clone())],
            ConstraintName = r.ConstraintName,
            OnDelete = r.OnDelete,
            OnUpdate = r.OnUpdate,
        };

    /// <summary>与えられた文を 1 つずつ書き込み可能接続で実行する（トリガー等の複合文を分割せず保つ）</summary>
    private static async Task RunStatementsAsync(SqliteTempDatabase db, params string[] statements)
    {
        await using var conn = new SqliteConnection(db.ReadWriteCreateConnectionString);
        await conn.OpenAsync(Ct).ConfigureAwait(false);

        await using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(Ct).ConfigureAwait(false);
        }

        foreach (var statement in statements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync(Ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// プールを無効にした ReadOnly 接続文字列。プールされた接続の SQLite スキーマキャッシュが同期直後に
    /// 古いままになり、生の PRAGMA が旧スキーマを見る事象を避ける（検証を毎回フレッシュな接続で行う）。
    /// </summary>
    private static string NonPooledReadOnlyConnectionString(SqliteTempDatabase db) =>
        new SqliteConnectionStringBuilder(db.ReadOnlyConnectionString)
        {
            Pooling = false,
        }.ConnectionString;

    /// <summary>指定テーブルの外部キー本数を PRAGMA foreign_key_list で数える</summary>
    private static async Task<int> QueryForeignKeyCountAsync(SqliteTempDatabase db, string table)
    {
        var ids = new HashSet<long>();

        await using var conn = new SqliteConnection(NonPooledReadOnlyConnectionString(db));
        await conn.OpenAsync(Ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync(Ct).ConfigureAwait(false);

        while (await reader.ReadAsync(Ct).ConfigureAwait(false))
        {
            // 列は id / seq / ...。id ごとに 1 本の FK（複合列は同一 id）
            ids.Add(reader.GetInt64(0));
        }

        return ids.Count;
    }
}
