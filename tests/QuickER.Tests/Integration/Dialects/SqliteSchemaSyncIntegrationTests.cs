using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
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
    /// 型変更＋列削除の再構築で、行データ・FK・インデックス・トリガー・テーブルレベル UNIQUE が温存されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 型変更＋列削除の再構築で行データ・FK・索引・トリガー・UNIQUE を温存"
    )]
    public async Task Rebuild_PreservesDataAndAuxiliaryObjects()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"category\" (\"id\" INTEGER NOT NULL, \"name\" TEXT NULL, CONSTRAINT \"PK_category\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"product\" ("
                + "\"id\" INTEGER NOT NULL, "
                + "\"category_id\" INTEGER NOT NULL, "
                + "\"sku\" TEXT NOT NULL, "
                + "\"note\" TEXT NULL, "
                + "\"legacy_col\" INTEGER NULL, "
                + "CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"), "
                + "UNIQUE (\"sku\"), "
                + "CONSTRAINT \"FK_product_category\" FOREIGN KEY (\"category_id\") REFERENCES \"category\" (\"id\") ON DELETE CASCADE"
                + ");",
            "CREATE INDEX \"idx_product_note\" ON \"product\" (\"note\");",
            "CREATE TRIGGER \"trg_product_ai\" AFTER INSERT ON \"product\" BEGIN UPDATE \"category\" SET \"name\" = \"name\"; END;",
            "INSERT INTO \"category\" (\"id\", \"name\") VALUES (1, 'cat1');",
            "INSERT INTO \"product\" (\"id\", \"category_id\", \"sku\", \"note\", \"legacy_col\") VALUES (10, 1, 'SKU-10', 'hello', 99);",
            "INSERT INTO \"product\" (\"id\", \"category_id\", \"sku\", \"note\", \"legacy_col\") VALUES (11, 1, 'SKU-11', 'world', 100);"
        );

        // 目標: product.note を VARCHAR(200) へ、legacy_col を削除（FK・UNIQUE・索引・トリガーは維持）
        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var product = entities.Single(e => e.TableName == "product");
                product.Columns.Single(c => c.Name == "note").DataType = "VARCHAR(200)";
                product.Columns.RemoveAll(c => c.Name == "legacy_col");
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // ---------- 再取込して構造を検証 ----------
        var reimported = await ImportAsync(db, provider);
        var importedProduct = reimported.Entities.Single(e => e.TableName == "product");

        // 列削除・型変更の反映
        importedProduct.Columns.Select(c => c.Name).Should().NotContain("legacy_col");
        importedProduct.Columns.Single(c => c.Name == "note").DataType.Should().Be("VARCHAR(200)");

        // FK 温存（参照アクション込み）
        var fk = reimported.Relationships.Should().ContainSingle().Which;
        fk.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);

        // インデックス・トリガー・UNIQUE の温存（補助オブジェクトとして再取込される）
        reimported
            .AuxiliaryObjects.Should()
            .Contain(a =>
                a.Kind == SchemaAuxiliaryObjectKind.Index && a.Name == "idx_product_note"
            );
        reimported
            .AuxiliaryObjects.Should()
            .Contain(a =>
                a.Kind == SchemaAuxiliaryObjectKind.Trigger && a.Name == "trg_product_ai"
            );
        reimported
            .AuxiliaryObjects.Should()
            .Contain(a =>
                a.Kind == SchemaAuxiliaryObjectKind.UniqueConstraint
                && a.Columns.Count == 1
                && a.Columns[0] == "sku"
            );

        // ---------- 行データの温存 ----------
        var rows = await QueryProductRowsAsync(db);
        rows.Should()
            .BeEquivalentTo(
                new (long, long, string, string?)[]
                {
                    (10, 1, "SKU-10", "hello"),
                    (11, 1, "SKU-11", "world"),
                }
            );
    }

    /// <summary>整合的なデータに対する FK 追加の再構築が成功し、FK が張られることを検証する</summary>
    [Fact(DisplayName = "[Integration] SQLite 同期: FK 追加の再構築が成功する（整合データ）")]
    public async Task Rebuild_AddForeignKey_Succeeds()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"category\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_category\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"product\" (\"id\" INTEGER NOT NULL, \"category_id\" INTEGER NOT NULL, CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"category\" (\"id\") VALUES (1);",
            "INSERT INTO \"product\" (\"id\", \"category_id\") VALUES (10, 1);"
        );

        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                // 目標に FK product.category_id -> category.id を追加する
                var category = entities.Single(e => e.TableName == "category");
                var product = entities.Single(e => e.TableName == "product");
                relationships.Add(
                    new Relationship
                    {
                        SourceEntityId = category.Id,
                        TargetEntityId = product.Id,
                        Type = RelationshipType.OneToMany,
                        SourceColumnId = category.Columns.Single(c => c.Name == "id").Id,
                        TargetColumnId = product.Columns.Single(c => c.Name == "category_id").Id,
                    }
                );
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // FK が張られたことを PRAGMA foreign_key_list で確認する
        var fkCount = await QueryForeignKeyCountAsync(db, "product");
        fkCount.Should().Be(1);
    }

    /// <summary>FK 削除の再構築で、FK が外れることを検証する</summary>
    [Fact(DisplayName = "[Integration] SQLite 同期: FK 削除の再構築で FK が外れる")]
    public async Task Rebuild_DropForeignKey_RemovesIt()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"category\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_category\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"product\" ("
                + "\"id\" INTEGER NOT NULL, \"category_id\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"FK_product_category\" FOREIGN KEY (\"category_id\") REFERENCES \"category\" (\"id\")"
                + ");",
            "INSERT INTO \"category\" (\"id\") VALUES (1);",
            "INSERT INTO \"product\" (\"id\", \"category_id\") VALUES (10, 1);"
        );

        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                // 目標から FK を取り除く
                relationships.Clear();
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        var fkCount = await QueryForeignKeyCountAsync(db, "product");
        fkCount.Should().Be(0);
    }

    /// <summary>既存 NULL 行がある列を NOT NULL 化する再構築が実行時に失敗し、DB が無変更でロールバックされることを検証する</summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 既存 NULL がある列の NOT NULL 化はロールバックされ DB は無変更"
    )]
    public async Task Rebuild_NotNullWithExistingNull_RollsBack()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"product\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"product\" (\"id\", \"note\") VALUES (10, NULL);"
        );

        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var note = entities
                    .Single(e => e.TableName == "product")
                    .Columns.Single(c => c.Name == "note");
                note.IsNullable = false; // 既存 NULL 行があるため NOT NULL 化は失敗するはず
                return (entities, relationships);
            }
        );

        // 実行時エラーでロールバックされる
        result.Committed.Should().BeFalse();
        result.Error.Should().NotBeNull();

        // DB は無変更: note は依然 nullable で、NULL 行が残っている
        var reimported = await ImportAsync(db, provider);
        reimported
            .Entities.Single(e => e.TableName == "product")
            .Columns.Single(c => c.Name == "note")
            .IsNullable.Should()
            .BeTrue();

        var rows = await QueryProductRowsAsync(db, includeCategoryAndSku: false);
        rows.Should().ContainSingle(r => r.Item1 == 10 && r.Item4 == null);
    }

    /// <summary>
    /// 孤立データがある状態で FK を追加する再構築が <c>foreign_key_check</c> で検出され、ロールバックされることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 孤立データへの FK 追加は foreign_key_check で失敗しロールバック"
    )]
    public async Task Rebuild_ForeignKeyViolation_RollsBack()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"category\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_category\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"product\" (\"id\" INTEGER NOT NULL, \"category_id\" INTEGER NOT NULL, CONSTRAINT \"PK_product\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"category\" (\"id\") VALUES (1);",
            // category_id=999 は category に存在しない孤立データ
            "INSERT INTO \"product\" (\"id\", \"category_id\") VALUES (10, 999);"
        );

        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var category = entities.Single(e => e.TableName == "category");
                var product = entities.Single(e => e.TableName == "product");
                relationships.Add(
                    new Relationship
                    {
                        SourceEntityId = category.Id,
                        TargetEntityId = product.Id,
                        Type = RelationshipType.OneToMany,
                        SourceColumnId = category.Columns.Single(c => c.Name == "id").Id,
                        TargetColumnId = product.Columns.Single(c => c.Name == "category_id").Id,
                    }
                );
                return (entities, relationships);
            }
        );

        // foreign_key_check で違反が検出されロールバック・違反テーブル名がエラーに含まれる
        result.Committed.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Should().Contain("product");

        // DB は無変更: FK は張られていない
        var fkCount = await QueryForeignKeyCountAsync(db, "product");
        fkCount.Should().Be(0);
    }

    /// <summary>並び替え＋真ん中への列追加を 1 回の同期で反映し、列順とデータが正しく保たれることを検証する</summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 並び替え＋真ん中への列追加を 1 回で反映し列順・データを保つ"
    )]
    public async Task Rebuild_ColumnReorderWithInsertion_AppliesInOneSync()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"item\" ("
                + "\"id\" INTEGER NOT NULL, \"a\" INTEGER NULL, \"b\" INTEGER NULL, "
                + "CONSTRAINT \"PK_item\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"item\" (\"id\", \"a\", \"b\") VALUES (1, 10, 20);",
            "INSERT INTO \"item\" (\"id\", \"a\", \"b\") VALUES (2, 30, 40);"
        );

        // 目標列順: id, new_col（真ん中へ追加）, b, a（a と b を入れ替え）
        var (result, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var item = entities.Single(e => e.TableName == "item");
                var id = item.Columns.Single(c => c.Name == "id");
                var a = item.Columns.Single(c => c.Name == "a");
                var b = item.Columns.Single(c => c.Name == "b");
                var newCol = new Column
                {
                    Name = "new_col",
                    DataType = "TEXT",
                    IsNullable = true,
                };
                item.Columns.Clear();
                item.Columns.Add(id);
                item.Columns.Add(newCol);
                item.Columns.Add(b);
                item.Columns.Add(a);
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // 列順が id, new_col, b, a に反映される
        var order = await QueryColumnOrderAsync(db, "item");
        order.Should().Equal("id", "new_col", "b", "a");

        // データは温存され、追加列は NULL
        var rows = await QueryItemRowsAsync(db);
        rows.Should()
            .BeEquivalentTo(
                new (long, long, long, string?)[] { (1, 10, 20, null), (2, 30, 40, null) }
            );
    }

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
                        SourceColumnId = category.Columns.Single(c => c.Name == "id").Id,
                        TargetColumnId = product.Columns.Single(c => c.Name == "category_id").Id,
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

    // ---------------- ヘルパー ----------------

    /// <summary>PRAGMA table_info でテーブルの列順（宣言順）を取得する</summary>
    private static async Task<List<string>> QueryColumnOrderAsync(
        SqliteTempDatabase db,
        string table
    )
    {
        var names = new List<string>();

        await using var conn = new SqliteConnection(NonPooledReadOnlyConnectionString(db));
        await conn.OpenAsync(Ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await cmd.ExecuteReaderAsync(Ct).ConfigureAwait(false);

        while (await reader.ReadAsync(Ct).ConfigureAwait(false))
        {
            // 列は cid / name / type / ...。name は 2 列目
            names.Add(reader.GetString(1));
        }

        return names;
    }

    /// <summary>item テーブルの行（id, a, b, new_col）を取得する（検証用）</summary>
    private static async Task<List<(long, long, long, string?)>> QueryItemRowsAsync(
        SqliteTempDatabase db
    )
    {
        var rows = new List<(long, long, long, string?)>();

        await using var conn = new SqliteConnection(NonPooledReadOnlyConnectionString(db));
        await conn.OpenAsync(Ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"id\", \"a\", \"b\", \"new_col\" FROM \"item\" ORDER BY \"id\";";
        await using var reader = await cmd.ExecuteReaderAsync(Ct).ConfigureAwait(false);

        while (await reader.ReadAsync(Ct).ConfigureAwait(false))
        {
            rows.Add(
                (
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)
                )
            );
        }

        return rows;
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
            .SchemaImporter.ImportAsync(db.ReadOnlyConnectionString, Ct)
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
            SourceColumnId = r.SourceColumnId,
            TargetColumnId = r.TargetColumnId,
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

    /// <summary>product テーブルの行を取得する（検証用）</summary>
    private static async Task<List<(long, long, string, string?)>> QueryProductRowsAsync(
        SqliteTempDatabase db,
        bool includeCategoryAndSku = true
    )
    {
        var rows = new List<(long, long, string, string?)>();

        await using var conn = new SqliteConnection(NonPooledReadOnlyConnectionString(db));
        await conn.OpenAsync(Ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = includeCategoryAndSku
            ? "SELECT \"id\", \"category_id\", \"sku\", \"note\" FROM \"product\" ORDER BY \"id\";"
            : "SELECT \"id\", 0, '', \"note\" FROM \"product\" ORDER BY \"id\";";
        await using var reader = await cmd.ExecuteReaderAsync(Ct).ConfigureAwait(false);

        while (await reader.ReadAsync(Ct).ConfigureAwait(false))
        {
            rows.Add(
                (
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)
                )
            );
        }

        return rows;
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
