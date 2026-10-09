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

        // インデックス・トリガーの温存（補助オブジェクトとして再取込される）
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

        // UNIQUE の温存。一意制約は補助オブジェクトではなく意味モデル（Entity.UniqueConstraints）が正本になった
        var reimportedUnique = importedProduct.UniqueConstraints.Should().ContainSingle().Which;
        reimportedUnique
            .ColumnIds.Select(id => importedProduct.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("sku");

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

    /// <summary>
    /// 主キーの付け替え（id → code）がテーブル再構築へ畳まれ、行データが温存されることを検証する。
    /// </summary>
    /// <remarks>
    /// SQLite は主キー変更を逐次 DDL で表現できないため、<see cref="SyncPlanner"/> が再構築計画へ畳む。
    /// 再構築はデータ移送を伴うため、行が失われないことの確認が特に重要になる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 主キーを id から code へ付け替える再構築でデータを温存"
    )]
    public async Task Rebuild_AlterPrimaryKey_MovesPrimaryKeyAndKeepsRows()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"pk_item\" ("
                + "\"code\" TEXT NOT NULL, \"id\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_pk_item\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-001', 1);",
            "INSERT INTO \"pk_item\" (\"code\", \"id\") VALUES ('A-002', 2);"
        );

        // 目標: 列構成・型は変えず、主キーだけ id から code へ移す
        var (result, script) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var item = entities.Single(e => e.TableName == "pk_item");
                item.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                item.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // 主キー変更が再構築へ畳まれていること（逐次 DDL のセクションには出ない）
        script.Should().Contain("-- ===== RebuildTable: pk_item =====");

        // 再取込: 主キーが code へ移っている
        var reimported = await ImportAsync(db, provider);
        var item2 = reimported.Entities.Single(e => e.TableName == "pk_item");
        item2.Columns.Single(c => c.Name == "code").IsPrimaryKey.Should().BeTrue();
        item2.Columns.Single(c => c.Name == "id").IsPrimaryKey.Should().BeFalse();

        // 行データは失われない（再構築のデータ移送が効いている）
        var rows = await QueryPkItemRowsAsync(db);
        rows.Should().Equal(("A-001", 1L), ("A-002", 2L));
    }

    /// <summary>一意制約の追加・削除がテーブル再構築へ畳まれ、実 DB へ反映されることを検証する</summary>
    /// <remarks>
    /// SQLite は <c>ADD / DROP CONSTRAINT</c> を持たないため、一意制約の変更もテーブル再構築で実現する。
    /// 追加と削除を同時に指定し、1 回の再構築で両方が成立することを確認する。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 一意制約の追加・削除がテーブル再構築で反映される"
    )]
    public async Task Rebuild_UniqueConstraintAddAndDrop()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"uq_item\" ("
                + "\"id\" INTEGER NOT NULL, "
                + "\"code\" TEXT NOT NULL, "
                + "\"legacy\" TEXT NULL, "
                + "CONSTRAINT \"PK_uq_item\" PRIMARY KEY (\"id\"), "
                + "UNIQUE (\"legacy\")"
                + ");",
            "INSERT INTO \"uq_item\" (\"id\", \"code\", \"legacy\") VALUES (1, 'C-1', 'L-1');"
        );

        // 目標: legacy の一意制約を外し、code へ一意制約を張る
        var (result, script) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var item = entities.Single(e => e.TableName == "uq_item");
                item.UniqueConstraints.Clear();
                item.UniqueConstraints.Add(
                    new UniqueConstraint
                    {
                        ColumnIds = [item.Columns.Single(c => c.Name == "code").Id],
                    }
                );
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // 一意制約の変更だけでテーブル再構築が起き、名前付きの UNIQUE 行が出力される
        script.Should().Contain("-- ===== RebuildTable: uq_item =====");
        script.Should().Contain("CONSTRAINT \"UQ_uq_item_code\" UNIQUE (\"code\")");

        // ---------- 再取込: code に一意制約が張られ legacy のものは消えている ----------
        var reimported = await ImportAsync(db, provider);
        var item2 = reimported.Entities.Single(e => e.TableName == "uq_item");
        var unique = item2.UniqueConstraints.Should().ContainSingle().Which;
        unique
            .ColumnIds.Select(id => item2.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("code");

        // ---------- 行データの温存 ----------
        (await QueryScalarLongAsync(db, "SELECT COUNT(*) FROM \"uq_item\";"))
            .Should()
            .Be(1);
    }

    // ---------------- 複合外部キー ----------------

    /// <summary>複合外部キーを持つ子テーブルの再構築で、外部キーが構成列を保ったまま残ることを検証する</summary>
    /// <remarks>
    /// <b>劣化時代はブロックされていた経路</b>。意味モデルが複合外部キーの列対応を保てなかった頃は、
    /// 再構築すると単列外部キーへ作り替えられる（成功して静かに壊れる）ため、このテーブルの再構築だけを
    /// 計画から落としていた。列ペアが正本になった今は、合成後の <c>CREATE TABLE</c> が全構成列の
    /// <c>FOREIGN KEY</c> 句を出力するため、通常どおり同期できる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 複合外部キーの子テーブルも再構築でき構成列が失われない"
    )]
    public async Task Rebuild_CompositeForeignKeyChildTable_PreservesAllColumnPairs()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"cfk_parent\" ("
                + "\"a\" INTEGER NOT NULL, \"b\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));",
            "CREATE TABLE \"cfk_child\" ("
                + "\"id\" INTEGER NOT NULL, "
                + "\"a_ref\" INTEGER NOT NULL, "
                + "\"b_ref\" INTEGER NOT NULL, "
                + "\"note\" TEXT NULL, "
                + "CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"FK_cfk_child_cfk_parent\" FOREIGN KEY (\"a_ref\", \"b_ref\") "
                + "REFERENCES \"cfk_parent\" (\"a\", \"b\"));",
            "INSERT INTO \"cfk_parent\" (\"a\", \"b\") VALUES (1, 2);",
            "INSERT INTO \"cfk_child\" (\"id\", \"a_ref\", \"b_ref\", \"note\") "
                + "VALUES (10, 1, 2, 'hello');"
        );

        // 目標: 複合外部キーに関与しない note の型だけを変える（＝子テーブルの再構築が要る）
        var (result, script) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var child = entities.Single(e => e.TableName == "cfk_child");
                child.Columns.Single(c => c.Name == "note").DataType = "VARCHAR(200)";
                return (entities, relationships);
            }
        );

        result.Committed.Should().BeTrue(result.Error);

        // 再構築が起き、合成後の CREATE TABLE が全構成列の FOREIGN KEY 句を出力する
        script.Should().Contain("-- ===== RebuildTable: cfk_child =====");
        script
            .Should()
            .Contain("FOREIGN KEY (\"a_ref\", \"b_ref\") REFERENCES \"cfk_parent\" (\"a\", \"b\")");

        // ---------- 再取込: 型変更が反映され、複合外部キーは構成列を保つ ----------
        var reimported = await ImportAsync(db, provider);
        var child2 = reimported.Entities.Single(e => e.TableName == "cfk_child");
        child2.Columns.Single(c => c.Name == "note").DataType.Should().Be("VARCHAR(200)");
        AssertCompositeForeignKey(reimported);

        // 外部キーは 1 本のまま（単列 FK 2 本へ分裂していない）
        (await QueryForeignKeyCountAsync(db, "cfk_child"))
            .Should()
            .Be(1);

        // 行データは失われない（再構築のデータ移送が効いている）
        (await QueryScalarLongAsync(db, "SELECT COUNT(*) FROM \"cfk_child\";"))
            .Should()
            .Be(1);
    }

    /// <summary>図へ足した複合外部キーが、テーブル再構築を通じて実 DB へ反映されることを検証する</summary>
    /// <remarks>
    /// SQLite は <c>ALTER TABLE ... ADD CONSTRAINT</c> を持たないため、外部キーの追加も再構築で実現する。
    /// 追加後に外して元へ戻るところまで往復させる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 複合外部キーの追加・削除がテーブル再構築で反映される"
    )]
    public async Task Rebuild_CompositeForeignKeyAddAndDrop()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"cfk_parent\" ("
                + "\"a\" INTEGER NOT NULL, \"b\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_cfk_parent\" PRIMARY KEY (\"a\", \"b\"));",
            "CREATE TABLE \"cfk_child\" ("
                + "\"id\" INTEGER NOT NULL, "
                + "\"a_ref\" INTEGER NOT NULL, "
                + "\"b_ref\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_cfk_child\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"cfk_parent\" (\"a\", \"b\") VALUES (1, 2);",
            "INSERT INTO \"cfk_child\" (\"id\", \"a_ref\", \"b_ref\") VALUES (10, 1, 2);"
        );

        // ========== 追加 ==========
        var (addResult, addScript) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var parent = entities.Single(e => e.TableName == "cfk_parent");
                var child = entities.Single(e => e.TableName == "cfk_child");
                relationships.Add(BuildCompositeRelationship(parent, child));
                return (entities, relationships);
            }
        );

        addResult.Committed.Should().BeTrue(addResult.Error);
        addScript
            .Should()
            .Contain("FOREIGN KEY (\"a_ref\", \"b_ref\") REFERENCES \"cfk_parent\" (\"a\", \"b\")");

        var afterAdd = await ImportAsync(db, provider);
        AssertCompositeForeignKey(afterAdd);
        (await QueryScalarLongAsync(db, "SELECT COUNT(*) FROM \"cfk_child\";")).Should().Be(1);

        // ========== 削除 ==========
        var (dropResult, _) = await RunSyncAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                relationships.Clear();
                return (entities, relationships);
            }
        );

        dropResult.Committed.Should().BeTrue(dropResult.Error);
        (await ImportAsync(db, provider)).Relationships.Should().BeEmpty();
        (await QueryForeignKeyCountAsync(db, "cfk_child")).Should().Be(0);
    }

    /// <summary>(a, b) → (a_ref, b_ref) の複合外部キーを表すリレーションを組み立てる</summary>
    private static Relationship BuildCompositeRelationship(Entity parent, Entity child) =>
        new()
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ColumnPairs =
            [
                new(
                    parent.Columns.Single(c => c.Name == "a").Id,
                    child.Columns.Single(c => c.Name == "a_ref").Id
                ),
                new(
                    parent.Columns.Single(c => c.Name == "b").Id,
                    child.Columns.Single(c => c.Name == "b_ref").Id
                ),
            ],
            ConstraintName = "FK_cfk_child_cfk_parent",
        };

    /// <summary>取込結果に、構成列を 2 組とも保った複合外部キーが 1 本だけあることを検証する</summary>
    private static void AssertCompositeForeignKey(SchemaImportResult live)
    {
        var parent = live.Entities.Single(e => e.TableName == "cfk_parent");
        var child = live.Entities.Single(e => e.TableName == "cfk_child");
        var rel = live.Relationships.Should().ContainSingle().Which;

        rel.ColumnPairs.Select(p =>
                (
                    parent.Columns.Single(c => c.Id == p.SourceColumnId).Name,
                    child.Columns.Single(c => c.Id == p.TargetColumnId).Name
                )
            )
            .Should()
            .Equal(("a", "a_ref"), ("b", "b_ref"));
    }

    /// <summary>主キー変更の検証用テーブルの行を取得する（データ温存の確認用）</summary>
    private static async Task<List<(string Code, long Id)>> QueryPkItemRowsAsync(
        SqliteTempDatabase db
    )
    {
        var rows = new List<(string Code, long Id)>();

        await using var conn = new SqliteConnection(NonPooledReadOnlyConnectionString(db));
        await conn.OpenAsync(Ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"code\", \"id\" FROM \"pk_item\" ORDER BY \"id\";";
        await using var reader = await cmd.ExecuteReaderAsync(Ct).ConfigureAwait(false);

        while (await reader.ReadAsync(Ct).ConfigureAwait(false))
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
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

    /// <summary>
    /// 再構築で失われる列レベル属性（AUTOINCREMENT / DEFAULT）が実行前に警告され、
    /// 実際に適用後は失われる（現状固定）ことを検証する。
    /// </summary>
    /// <remarks>
    /// 再構築の CREATE TABLE は意味モデルから組み立て直すため、モデルが持たない属性は再現されない。
    /// 暗黙の rowid 別名（INTEGER 主キー）は保たれるので、消えるのは明示 AUTOINCREMENT と DEFAULT のほう。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 再構築で失われる列属性を警告し、実際に失われる"
    )]
    public async Task Rebuild_WarnsAndDropsColumnAttributes()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"item\" ("
                + "\"id\" INTEGER PRIMARY KEY AUTOINCREMENT, "
                + "\"qty\" INTEGER NOT NULL DEFAULT 7, "
                + "\"note\" TEXT NULL"
                + ");",
            "INSERT INTO \"item\" (\"qty\", \"note\") VALUES (1, 'a');"
        );

        // ---------- 計画の警告を確認する（実行前に利用者へ提示される材料） ----------
        var live = await ImportAsync(db, provider);
        live.TableCreateSql.Should().ContainKey("item");

        var target = live.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        target.Single(e => e.TableName == "item").Columns.Single(c => c.Name == "note").DataType =
            "VARCHAR(50)";

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            target,
            [],
            provider.SyncCapabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = true;
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
                AuxiliaryObjects = live.AuxiliaryObjects,
                TableCreateSql = live.TableCreateSql,
            }
        );

        var warning = plan
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
            .Which;
        warning.TableName.Should().Be("item");
        warning.Detail.Should().Be("AUTOINCREMENT, DEFAULT");

        // ---------- 実行し、警告どおり属性が失われることを固定する ----------
        var result = await provider.SyncExecutor.ExecuteAsync(
            new DbConnectionSettings { FilePath = db.FilePath },
            provider.SyncScriptBuilder.Build(plan),
            Ct
        );
        result.Committed.Should().BeTrue(result.Error);

        var reimported = await ImportAsync(db, provider);
        var rebuiltDdl = reimported.TableCreateSql["item"];

        // 型変更は反映され、AUTOINCREMENT / DEFAULT は消えている（現状固定）
        rebuiltDdl.Should().Contain("VARCHAR(50)");
        rebuiltDdl.Should().NotContain("AUTOINCREMENT");
        rebuiltDdl.Should().NotContain("DEFAULT");
        TableRebuildAttributeDetector.Detect(rebuiltDdl).Should().BeEmpty();

        // 行データは温存される（喪失するのは属性だけ）
        await using var conn = new SqliteConnection(db.ReadOnlyConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"item\";";
        var count = (long)(await cmd.ExecuteScalarAsync(Ct))!;
        count.Should().Be(1);
    }

    /// <summary>
    /// 列にもテーブルにも属さない属性（括弧の後ろのテーブルオプション・外部キーの遅延指定・競合解決句）も
    /// 再構築で失われるため、列属性と同じ警告に載り、実際に失われる（現状固定）ことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: WITHOUT ROWID・STRICT・DEFERRABLE・ON CONFLICT も再構築で失われることを警告する"
    )]
    public async Task Rebuild_WarnsTableOptionsAndConstraintClausesLost()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"p\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_p\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"w\" ("
                + "\"id\" INTEGER NOT NULL, "
                + "\"p_id\" INTEGER NULL, "
                + "\"note\" TEXT NOT NULL ON CONFLICT ABORT, "
                + "PRIMARY KEY (\"id\"), "
                + "FOREIGN KEY (\"p_id\") REFERENCES \"p\" (\"id\") DEFERRABLE INITIALLY DEFERRED"
                + ") WITHOUT ROWID, STRICT;",
            "INSERT INTO \"p\" (\"id\") VALUES (1);",
            "INSERT INTO \"w\" (\"id\", \"p_id\", \"note\") VALUES (1, 1, 'a');"
        );

        var (result, _, plan) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "w")
                    .Columns.Single(c => c.Name == "note")
                    .DataType = "INT",
            _ => true
        );

        var warning = plan
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.TableRebuildDropsColumnAttribute)
            .Which;
        warning.TableName.Should().Be("w");
        warning.Detail.Should().Be("ON CONFLICT, DEFERRABLE, WITHOUT ROWID, STRICT");

        // 警告どおり、再構築後の定義からは 4 つとも消えている（現状固定）
        result.Committed.Should().BeTrue(result.Error);
        var rebuiltDdl = (await ImportAsync(db, provider)).TableCreateSql["w"];
        rebuiltDdl.Should().NotContain("WITHOUT ROWID");
        rebuiltDdl.Should().NotContain("STRICT");
        rebuiltDdl.Should().NotContain("DEFERRABLE");
        rebuiltDdl.Should().NotContain("ON CONFLICT");
    }

    // ---------------- 別 Guid 空間の図（GUI と同じ）での再構築の合成 ----------------

    /// <summary>
    /// 列の型だけを変える再構築で、その列を構成列に持つ一意制約が消えないことを検証する。
    /// </summary>
    /// <remarks>
    /// GUI の図は live と別の Guid 空間にある。合成が live の列を図側の列（別 Guid）へ差し替えると、
    /// live の一意制約の構成列 ID が解決できなくなり、再構築後の表から一意制約が黙って消えていた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 別 Guid の図で列の型を変えても一意制約は再構築後に残る"
    )]
    public async Task Rebuild_AlterColumnWithFreshIds_KeepsUniqueConstraint()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"uq_keep\" ("
                + "\"id\" INTEGER NOT NULL, \"sku\" TEXT NOT NULL, "
                + "CONSTRAINT \"PK_uq_keep\" PRIMARY KEY (\"id\"), UNIQUE (\"sku\"));",
            "INSERT INTO \"uq_keep\" (\"id\", \"sku\") VALUES (1, 'A');"
        );

        var (result, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "uq_keep")
                    .Columns.Single(c => c.Name == "sku")
                    .DataType = "VARCHAR(50)",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        result.Committed.Should().BeTrue(result.Error);

        var item = (await ImportAsync(db, provider)).Entities.Single(e => e.TableName == "uq_keep");
        item.Columns.Single(c => c.Name == "sku").DataType.Should().Be("VARCHAR(50)");
        var unique = item.UniqueConstraints.Should().ContainSingle().Which;
        unique
            .ColumnIds.Select(id => item.Columns.Single(c => c.Id == id).Name)
            .Should()
            .Equal("sku");
    }

    /// <summary>複合主キーの構成列の型を変える再構築で、主キーの列順が変わらないことを検証する</summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 別 Guid の図で複合主キーの列の型を変えても主キーの順序は変わらない"
    )]
    public async Task Rebuild_AlterColumnWithFreshIds_KeepsCompositePrimaryKeyOrder()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"pk_order\" ("
                + "\"a\" TEXT NOT NULL, \"b\" TEXT NOT NULL, \"v\" TEXT NULL, "
                + "CONSTRAINT \"PK_pk_order\" PRIMARY KEY (\"a\", \"b\"));",
            "INSERT INTO \"pk_order\" (\"a\", \"b\", \"v\") VALUES ('x', 'y', 'z');"
        );

        var (result, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "pk_order")
                    .Columns.Single(c => c.Name == "a")
                    .DataType = "VARCHAR(10)",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        result.Committed.Should().BeTrue(result.Error);

        var table = (await ImportAsync(db, provider)).Entities.Single(e =>
            e.TableName == "pk_order"
        );
        table.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("a", "b");
    }

    /// <summary>
    /// 主キー変更（AlterPrimaryKey）を選ばずに列の型だけを変える再構築で、主キーが変わらないことを検証する。
    /// </summary>
    /// <remarks>
    /// 図側では型を変える列を主キーにもしているが、主キーの変更は選んでいない。
    /// 合成が図側の列の主キー指定を持ち込むと、選んでいない主キー変更が適用されていた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 主キー変更を選ばずに列の型だけを変えても主キーは変わらない"
    )]
    public async Task Rebuild_AlterColumnWithoutPrimaryKeySelection_KeepsPrimaryKey()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"pk_keep\" ("
                + "\"id\" INTEGER NOT NULL, \"code\" TEXT NOT NULL, "
                + "CONSTRAINT \"PK_pk_keep\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"pk_keep\" (\"id\", \"code\") VALUES (1, 'A');"
        );

        var (result, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
            {
                var code = entities
                    .Single(e => e.TableName == "pk_keep")
                    .Columns.Single(c => c.Name == "code");
                code.DataType = "VARCHAR(20)";
                code.IsPrimaryKey = true;
            },
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        result.Committed.Should().BeTrue(result.Error);

        var table = (await ImportAsync(db, provider)).Entities.Single(e =>
            e.TableName == "pk_keep"
        );
        table.Columns.Single(c => c.Name == "code").DataType.Should().Be("VARCHAR(20)");
        table.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("id");
    }

    /// <summary>
    /// 主キー変更を選ばずに、図で主キーにした列を追加する再構築で、主キーが変わらないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 主キー変更を選ばずに主キー指定の列を追加しても主キーは変わらない"
    )]
    public async Task Rebuild_AddPrimaryKeyColumnWithoutPrimaryKeySelection_KeepsPrimaryKey()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"pk_add\" ("
                + "\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, "
                + "CONSTRAINT \"PK_pk_add\" PRIMARY KEY (\"id\"));"
        );

        var (result, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
            {
                var table = entities.Single(e => e.TableName == "pk_add");
                table.Columns.Single(c => c.Name == "note").DataType = "VARCHAR(10)";
                table.Columns.Add(
                    new Column
                    {
                        Name = "k2",
                        DataType = "TEXT",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    }
                );
            },
            item => item.Kind is SchemaDiffKind.AlterColumn or SchemaDiffKind.AddColumn
        );

        result.Committed.Should().BeTrue(result.Error);

        var table = (await ImportAsync(db, provider)).Entities.Single(e => e.TableName == "pk_add");
        table.Columns.Select(c => c.Name).Should().Contain("k2");
        table.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("id");
    }

    /// <summary>
    /// 参照される側の主キーを付け替える再構築で、参照先が候補キーでなくなる外部キーを警告することを検証する。
    /// </summary>
    /// <remarks>
    /// 逐次 DDL の方言と同じ判定（同期後の主キー・一意制約と被参照列が完全一致するか）を再構築でも行う。
    /// 実行は SQLite 自身が foreign key mismatch で拒否してロールバックする（安全側）が、事前に知らせる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 参照される主キーの付け替えで候補キーを失う外部キーを警告する"
    )]
    public async Task Rebuild_AlterReferencedPrimaryKey_WarnsCandidateKeyLoss()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"ck_parent\" ("
                + "\"id\" INTEGER NOT NULL, \"code\" TEXT NOT NULL, "
                + "CONSTRAINT \"PK_ck_parent\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"ck_child\" ("
                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_ck_child\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"FK_ck_child_ck_parent\" FOREIGN KEY (\"parent_id\") "
                + "REFERENCES \"ck_parent\" (\"id\"));",
            "INSERT INTO \"ck_parent\" (\"id\", \"code\") VALUES (1, 'A');",
            "INSERT INTO \"ck_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
        );

        var (result, _, plan) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
            {
                var parent = entities.Single(e => e.TableName == "ck_parent");
                parent.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
                parent.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
                parent.PrimaryKeyColumnIds = [];
            },
            item => item.Kind == SchemaDiffKind.AlterPrimaryKey
        );

        var warning = plan
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey)
            .Which;
        warning.TableName.Should().Be("ck_child");

        // SQLite は外部キー名を保存しないため、取込は id 付きの名前を合成する（FK_ck_child_ck_parent_0）
        warning.Detail.Should().StartWith("FK_ck_child_ck_parent");

        // 警告どおり実行は拒否され、ロールバックされる（主キーは元のまま）
        result.Committed.Should().BeFalse();
        (await ImportAsync(db, provider))
            .Entities.Single(e => e.TableName == "ck_parent")
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name)
            .Should()
            .Equal("id");
    }

    /// <summary>
    /// 外部キーの参照アクションだけを変える同期は 1 つの外部キー変更になり、再構築後の子表には
    /// 新しい定義の外部キーが 1 本だけ残ることを検証する。
    /// </summary>
    /// <remarks>
    /// SQLite は外部キー名を保存しないため、削除と追加の 2 項目のまま追加だけを選ぶと、再構築の合成
    /// （live − 削除 ＋ 追加）で旧と新の外部キーが同じ列に 2 本並んでいた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 参照アクションの変更は外部キー変更として 1 本に作り直される"
    )]
    public async Task Rebuild_AlterForeignKey_ReplacesForeignKey()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"fa_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_fa_parent\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"fa_child\" ("
                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_fa_child\" PRIMARY KEY (\"id\"), "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"fa_parent\" (\"id\"));",
            "INSERT INTO \"fa_parent\" (\"id\") VALUES (1);",
            "INSERT INTO \"fa_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
        );

        var (result, _, plan) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (_, relationships) => relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade,
            item => item.Kind == SchemaDiffKind.AlterForeignKey
        );

        result.Committed.Should().BeTrue(result.Error);
        plan.Rebuilds.Should().ContainSingle().Which.ForeignKeys.Should().ContainSingle();

        var fk = (await ImportAsync(db, provider)).Relationships.Should().ContainSingle().Which;
        fk.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        (await QueryForeignKeyCountAsync(db, "fa_child")).Should().Be(1);
    }

    /// <summary>
    /// 参照される親の表だけを消すとき、外部キーの削除（既定で未選択）を選ばなくても、子の表を作り直して
    /// 外部キーを外すことを検証する。子に行があれば <c>foreign_key_check</c> で失敗し、行が無ければ
    /// 存在しない表を指す外部キーが黙って残っていた（取込が範囲外の外部キーとして告げる）。
    /// </summary>
    [Theory(
        DisplayName = "[Integration] SQLite 同期: 親の表だけを消しても、子の表を作り直して外部キーを外す"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DropParentTableOnly_RebuildsChildWithoutForeignKey(bool childHasRows)
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"vp_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_vp_parent\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"vp_child\" ("
                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_vp_child\" PRIMARY KEY (\"id\"), "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"vp_parent\" (\"id\"));",
            "INSERT INTO \"vp_parent\" (\"id\") VALUES (1);"
        );

        if (childHasRows)
        {
            await RunStatementsAsync(
                db,
                "INSERT INTO \"vp_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
            );
        }

        var (result, script, plan) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                entities.RemoveAll(e => e.TableName == "vp_parent");
                relationships.Clear();
            },
            item => item.Kind == SchemaDiffKind.DropTable
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        plan.Rebuilds.Should().ContainSingle().Which.TableName.Should().Be("vp_child");
        var live = await ImportAsync(db, provider);
        live.Warnings.Should().BeEmpty();
        live.Entities.Select(e => e.TableName).Should().Equal("vp_child");
        (await QueryForeignKeyCountAsync(db, "vp_child")).Should().Be(0);
        (await QueryScalarLongAsync(db, "SELECT COUNT(*) FROM \"vp_child\""))
            .Should()
            .Be(childHasRows ? 1 : 0);
    }

    /// <summary>
    /// 新しい列を主キーにする変更で、その列の追加を選ばずに主キーの変更だけを選ぶと、主キーの変更を外して名指しし
    /// 表を変えないことを検証する（以前は再構築が無い列を読み飛ばし、部分的な主キーのまま成功していた）。
    /// 列の追加も選べば、新しい主キーがそのまま付く。
    /// </summary>
    [Theory(
        DisplayName = "[Integration] SQLite 同期: 新しい列を主キーにする変更は、列の追加を選ばなければ外して警告する"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlterPrimaryKeyOnNewColumn_RequiresAddColumn(bool selectAddColumn)
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"pk_part\" (\"a\" INTEGER NOT NULL, \"b\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_pk_part\" PRIMARY KEY (\"a\"));"
        );

        var (result, script, plan) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
            {
                var table = entities.Single(e => e.TableName == "pk_part");
                var c = new Column
                {
                    Name = "c",
                    DataType = "INTEGER",
                    IsNullable = false,
                };
                table.Columns.Add(c);
                table.Columns.Single(x => x.Name == "a").IsPrimaryKey = false;
                table.Columns.Single(x => x.Name == "b").IsPrimaryKey = true;
                c.IsPrimaryKey = true;
                table.PrimaryKeyColumnIds = [table.Columns.Single(x => x.Name == "b").Id, c.Id];
            },
            item =>
                item.Kind == SchemaDiffKind.AlterPrimaryKey
                || (selectAddColumn && item.Kind == SchemaDiffKind.AddColumn)
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var key = (await ImportAsync(db, provider))
            .Entities.Single()
            .GetPrimaryKeyColumnsInOrder()
            .Select(c => c.Name);

        if (selectAddColumn)
        {
            plan.Warnings.Should().BeEmpty();
            key.Should().Equal("b", "c");
        }
        else
        {
            plan.Warnings.Should()
                .ContainSingle()
                .Which.Detail.Should()
                .Be("PRIMARY KEY (pk_part.c)");
            key.Should().Equal("a");
        }
    }

    /// <summary>
    /// 選んでいない新しい表を参照する外部キーは、計画から外して名指しすることを検証する（以前は再構築が存在しない表を
    /// 参照する外部キーを作って成功扱いになり、その表への INSERT / DELETE が no such table で全部失敗した）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 選んでいない新しい表を参照する外部キーは外して警告する"
    )]
    public async Task AddForeignKeyToUnselectedNewTable_IsSkippedWithWarning()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"np_child\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_np_child\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"np_child\" (\"id\") VALUES (1);"
        );

        var (result, script, plan) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var parent = new Entity
                {
                    TableName = "np_parent",
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
                entities.Add(parent);
                var child = entities.Single(e => e.TableName == "np_child");
                var reference = new Column { Name = "parent_id", DataType = "INTEGER" };
                child.Columns.Add(reference);
                relationships.Add(
                    new Relationship
                    {
                        SourceEntityId = parent.Id,
                        TargetEntityId = child.Id,
                        ColumnPairs = [new(parent.Columns[0].Id, reference.Id)],
                    }
                );
            },
            item => item.Kind is SchemaDiffKind.AddColumn or SchemaDiffKind.AddForeignKey
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        plan.Warnings.Should()
            .ContainSingle()
            .Which.Detail.Should()
            .Be("FK_np_child_np_parent (np_parent)");
        var live = await ImportAsync(db, provider);
        live.Warnings.Should().BeEmpty();
        live.Entities.Select(e => e.TableName).Should().Equal("np_child");
        (await QueryForeignKeyCountAsync(db, "np_child")).Should().Be(0);
    }

    /// <summary>
    /// 同じ列ペアに外部キーが 2 本あるとき、片方の参照アクションだけを変える同期が、変えた 1 本を作り直し
    /// もう 1 本をそのまま残すことを検証する（再構築の合成が同じ列ペアの外部キーを 2 本とも外していた）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 同じ列ペアの外部キー 2 本のうち 1 本だけ変えても、もう 1 本は残る"
    )]
    public async Task Rebuild_AlterOneOfDuplicatedForeignKeys_KeepsTheOther()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"dp_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_dp_parent\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"dp_child\" ("
                + "\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NOT NULL, "
                + "CONSTRAINT \"PK_dp_child\" PRIMARY KEY (\"id\"), "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"dp_parent\" (\"id\"), "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"dp_parent\" (\"id\") ON DELETE CASCADE);",
            "INSERT INTO \"dp_parent\" (\"id\") VALUES (1);",
            "INSERT INTO \"dp_child\" (\"id\", \"parent_id\") VALUES (1, 1);"
        );

        var (result, script, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (_, relationships) =>
                relationships
                    .Single(r => r.OnDelete == ForeignKeyReferentialAction.NoAction)
                    .OnUpdate = ForeignKeyReferentialAction.Cascade,
            item => item.Kind == SchemaDiffKind.AlterForeignKey
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        var actions = (await ImportAsync(db, provider))
            .Relationships.Select(r => (r.OnDelete, r.OnUpdate))
            .ToList();
        actions
            .Should()
            .BeEquivalentTo([
                (ForeignKeyReferentialAction.NoAction, ForeignKeyReferentialAction.Cascade),
                (ForeignKeyReferentialAction.Cascade, ForeignKeyReferentialAction.NoAction),
            ]);
    }

    /// <summary>
    /// 主キーと同じ列集合の一意制約を図に持つと、SQLite の差分が選べない案内項目になり、
    /// 同期しても同じ案内が出るだけで選べる差分が繰り返し出ないことを検証する
    /// （SQLite はその一意制約の索引を作らず、取り込んでも現れない）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 主キーと同じ列集合の一意制約は案内項目になり、同期しても差分が繰り返し出ない"
    )]
    public async Task UniqueConstraintOnPrimaryKey_IsGuideOnlyAndDoesNotRepeat()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"uk_item\" (\"id\" INTEGER NOT NULL, \"name\" TEXT, CONSTRAINT \"PK_uk_item\" PRIMARY KEY (\"id\"));"
        );

        static void AddUniqueOnPrimaryKey(List<Entity> entities, List<Relationship> _)
        {
            var item = entities.Single(e => e.TableName == "uk_item");
            item.UniqueConstraints.Add(
                new UniqueConstraint { Name = "UQ_uk_item_id", ColumnIds = [item.Columns[0].Id] }
            );
        }

        for (var round = 0; round < 2; round++)
        {
            var items = new List<SchemaDiffItem>();
            var (result, script, plan) = await RunSyncWithFreshIdsAsync(
                db,
                provider,
                AddUniqueOnPrimaryKey,
                item =>
                {
                    items.Add(item);
                    return item.IsSelectable;
                }
            );

            items.Should().ContainSingle();
            items[0].Kind.Should().Be(SchemaDiffKind.AddUniqueConstraint);
            items[0].IsSelectable.Should().BeFalse();
            plan.Rebuilds.Should().BeEmpty();
            result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        }

        (await ImportAsync(db, provider)).Entities.Single().UniqueConstraints.Should().BeEmpty();
    }

    // ---------------- ビュー・既存の外部キー違反と再構築 ----------------

    /// <summary>
    /// 作り直す表を参照するビュー（と、もともと壊れている無関係なビュー）があっても再構築が成功し、
    /// ビューは作り直した表を指し続けることを検証する。
    /// </summary>
    /// <remarks>
    /// SQLite 3.26 以降の RENAME はスキーマ全体を検証し直すため、旧い表を消した時点で「無い表を参照するビュー」になり
    /// RENAME が失敗していた（もともと壊れたビューが 1 つあるだけでも同じ）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 作り直す表を参照するビューがあっても再構築が成功しビューは新しい表を指す"
    )]
    public async Task Rebuild_WithDependentView_Succeeds()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"vw_item\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_vw_item\" PRIMARY KEY (\"id\"));",
            "INSERT INTO \"vw_item\" (\"id\", \"note\") VALUES (1, 'a');",
            "CREATE VIEW \"vw_notes\" AS SELECT \"id\", \"note\" FROM \"vw_item\";",
            "CREATE VIEW \"vw_pre_broken\" AS SELECT \"x\" FROM \"no_such_table\";",
            // 作り直す表へ書き込む、別の表のトリガー
            "CREATE TABLE \"vw_log\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_vw_log\" PRIMARY KEY (\"id\"));",
            "CREATE TRIGGER \"trg_vw_log\" AFTER INSERT ON \"vw_log\" BEGIN "
                + "INSERT INTO \"vw_item\" (\"id\", \"note\") VALUES (NEW.\"id\" + 100, 'from trigger'); END;"
        );

        var (result, script, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "vw_item")
                    .Columns.Single(c => c.Name == "note")
                    .DataType = "VARCHAR(50)",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );

        result.Committed.Should().BeTrue($"{result.Error}{System.Environment.NewLine}{script}");
        (await QueryScalarLongAsync(db, "SELECT COUNT(*) FROM \"vw_notes\" WHERE \"note\" = 'a';"))
            .Should()
            .Be(1);

        // 別の表のトリガーも、作り直した表へ書き込める
        await RunStatementsAsync(db, "INSERT INTO \"vw_log\" (\"id\") VALUES (1);");
        (
            await QueryScalarLongAsync(
                db,
                "SELECT COUNT(*) FROM \"vw_notes\" WHERE \"note\" = 'from trigger';"
            )
        )
            .Should()
            .Be(1);
        (await ImportAsync(db, provider))
            .Entities.Single(e => e.TableName == "vw_item")
            .Columns.Single(c => c.Name == "note")
            .DataType.Should()
            .Be("VARCHAR(50)");
    }

    /// <summary>作り直しでビューが壊れる（消した列を参照する）同期は、ビューを名指ししてロールバックすることを検証する</summary>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 再構築でビューが壊れるならビューを名指ししてロールバックする"
    )]
    public async Task Rebuild_BreakingView_RollsBack()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"vw_item\" (\"id\" INTEGER NOT NULL, \"note\" TEXT NULL, CONSTRAINT \"PK_vw_item\" PRIMARY KEY (\"id\"));",
            "CREATE VIEW \"vw_notes\" AS SELECT \"id\", \"note\" FROM \"vw_item\";"
        );

        var (result, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "vw_item")
                    .Columns.RemoveAll(c => c.Name == "note"),
            item => item.Kind == SchemaDiffKind.DropColumn
        );

        result.Committed.Should().BeFalse();
        result.Error.Should().Contain("vw_notes");
        (await ImportAsync(db, provider))
            .Entities.Single(e => e.TableName == "vw_item")
            .Columns.Select(c => c.Name)
            .Should()
            .Contain("note");
    }

    /// <summary>
    /// 親の無い子行（同期前から在る外部キー違反）を抱えた DB でも、無関係な同期・その子表の作り直しは成功し、
    /// 同期で違反が増えるときだけ失敗することを検証する。
    /// </summary>
    /// <remarks>
    /// SQLite は既定で外部キーを検査しないため、こうした DB は珍しくない。同期後の foreign_key_check を
    /// DB 全体の結果で判定していたため、無関係な列追加まで失敗していた。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] SQLite 同期: 既存の外部キー違反があっても無関係な同期は成功し、違反が増えるときだけ失敗する"
    )]
    public async Task ExistingForeignKeyViolations_OnlyNewOnesFail()
    {
        using var db = SqliteTempDatabase.Create();
        var provider = new SqliteProvider();

        await RunStatementsAsync(
            db,
            "CREATE TABLE \"ov_parent\" (\"id\" INTEGER NOT NULL, CONSTRAINT \"PK_ov_parent\" PRIMARY KEY (\"id\"));",
            "CREATE TABLE \"ov_child\" (\"id\" INTEGER NOT NULL, \"parent_id\" INTEGER NULL, \"note\" TEXT NULL, "
                + "CONSTRAINT \"PK_ov_child\" PRIMARY KEY (\"id\"), "
                + "FOREIGN KEY (\"parent_id\") REFERENCES \"ov_parent\" (\"id\"));",
            "CREATE TABLE \"ov_other\" (\"id\" INTEGER NOT NULL, \"ref\" INTEGER NULL, CONSTRAINT \"PK_ov_other\" PRIMARY KEY (\"id\"));",
            // 外部キーを検査しない状態で入れた親の無い子行（同期前から在る違反）
            "PRAGMA foreign_keys = OFF;",
            "INSERT INTO \"ov_child\" (\"id\", \"parent_id\") VALUES (1, 999);",
            "INSERT INTO \"ov_other\" (\"id\", \"ref\") VALUES (1, 999);"
        );

        // 無関係な表への列追加
        var (addResult, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "ov_parent")
                    .Columns.Add(new Column { Name = "memo", DataType = "TEXT" }),
            item => item.Kind == SchemaDiffKind.AddColumn
        );
        addResult.Committed.Should().BeTrue(addResult.Error);

        // 違反を抱えた子表そのものの作り直し（違反の件数は変わらない）
        var (rebuildResult, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, _) =>
                entities
                    .Single(e => e.TableName == "ov_child")
                    .Columns.Single(c => c.Name == "note")
                    .DataType = "VARCHAR(20)",
            item => item.Kind == SchemaDiffKind.AlterColumn
        );
        rebuildResult.Committed.Should().BeTrue(rebuildResult.Error);

        // 新しい外部キーで違反が増える同期は失敗する
        var (newFkResult, _, _) = await RunSyncWithFreshIdsAsync(
            db,
            provider,
            (entities, relationships) =>
            {
                var parent = entities.Single(e => e.TableName == "ov_parent");
                var other = entities.Single(e => e.TableName == "ov_other");
                relationships.Add(
                    new Relationship
                    {
                        SourceEntityId = parent.Id,
                        TargetEntityId = other.Id,
                        ColumnPairs = [new(parent.Columns[0].Id, other.Columns[1].Id)],
                    }
                );
            },
            item => item.Kind == SchemaDiffKind.AddForeignKey
        );
        newFkResult.Committed.Should().BeFalse();
        newFkResult.Error.Should().Contain("ov_other");
    }

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
