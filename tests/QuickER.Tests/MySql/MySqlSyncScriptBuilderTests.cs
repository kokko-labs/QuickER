using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Provider;

namespace QuickER.Tests.MySql;

/// <summary><see cref="MySqlSyncScriptBuilder"/> が差分から生成する MySQL DDL の内容と出力順序を検証するテストクラス</summary>
public class MySqlSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new MySqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities())
        );

    /// <summary>AddTable が主キー制約を含む CREATE TABLE 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含む")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        e.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "varchar(50)",
                IsNullable = true,
            }
        );

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "customer",
                Entity = e,
                IsSelected = true,
            }
        );

        sql.Should().Contain("CREATE TABLE `customer`");
        sql.Should().Contain("`id` int NOT NULL");
        sql.Should().Contain("CONSTRAINT `PK_customer` PRIMARY KEY (`id`)");
    }

    /// <summary>AddColumn が ALTER TABLE ... ADD COLUMN 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddColumn は ALTER TABLE ADD COLUMN を生成する")]
    public void AddColumn_GeneratesAlterAddColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "customer",
                ColumnName = "email",
                Column = new Column
                {
                    Name = "email",
                    DataType = "varchar(200)",
                    IsNullable = false,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` ADD COLUMN `email` varchar(200) NOT NULL;");
    }

    /// <summary>AlterColumn が MODIFY COLUMN で型と NULL 制約を再指定することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は MODIFY COLUMN で型と NOT NULL を再指定する")]
    public void AlterColumn_GeneratesModifyColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "name",
                Column = new Column
                {
                    Name = "name",
                    DataType = "varchar(100)",
                    IsNullable = false,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` MODIFY COLUMN `name` varchar(100) NOT NULL;");
    }

    /// <summary>AlterColumn は説明がある場合 COMMENT を含めて既存コメントの消失を防ぐことを検証する</summary>
    [Fact(DisplayName = "AlterColumn は説明があれば COMMENT を含める")]
    public void AlterColumn_WithDescription_IncludesComment()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "name",
                Column = new Column
                {
                    Name = "name",
                    DataType = "varchar(100)",
                    IsNullable = false,
                    Description = "顧客名",
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("MODIFY COLUMN `name` varchar(100) NOT NULL COMMENT '顧客名';");
    }

    /// <summary>NULL 許容へ変更する AlterColumn が NULL を再指定することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は NULL 許容化で NULL を再指定する")]
    public void AlterColumn_NullableGeneratesNull()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "customer",
                ColumnName = "note",
                Column = new Column
                {
                    Name = "note",
                    DataType = "text",
                    IsNullable = true,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` MODIFY COLUMN `note` text NULL;");
    }

    /// <summary>DropColumn が ALTER TABLE ... DROP COLUMN 文を生成することを検証する</summary>
    [Fact(DisplayName = "DropColumn は DROP COLUMN を生成する")]
    public void DropColumn_GeneratesDropColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropColumn,
                TableName = "customer",
                ColumnName = "old",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` DROP COLUMN `old`;");
    }

    /// <summary>DropTable が DROP TABLE 文を生成することを検証する</summary>
    [Fact(DisplayName = "DropTable は DROP TABLE を生成する")]
    public void DropTable_GeneratesDropTable()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropTable,
                TableName = "customer",
                IsSelected = true,
            }
        );

        sql.Should().Contain("DROP TABLE `customer`;");
    }

    /// <summary>AddForeignKey が FK 制約追加文と参照アクションを生成することを検証する</summary>
    [Fact(DisplayName = "AddForeignKey は ADD CONSTRAINT FOREIGN KEY と参照アクションを生成する")]
    public void AddForeignKey_GeneratesConstraint()
    {
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "order" };
        order.Columns.Add(new Column { Name = "customer_id", DataType = "int" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = customer.Columns[0].Id,
            ConstraintName = "FK_order_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetNull,
        };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "order",
                ColumnName = "customer_id",
                ParentEntity = customer,
                ChildEntity = order,
                Relationship = rel,
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `order` ADD CONSTRAINT `FK_order_customer`");
        sql.Should().Contain("FOREIGN KEY (`customer_id`) REFERENCES `customer` (`id`)");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET NULL");
    }

    /// <summary>DropForeignKey が制約名判明時に DROP FOREIGN KEY を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名があれば DROP FOREIGN KEY を生成する")]
    public void DropForeignKey_UsesConstraintNameWhenAvailable()
    {
        var customer = new Entity { TableName = "customer" };
        var order = new Entity { TableName = "order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = customer,
                ChildEntity = order,
                ForeignKeyName = "FK_order_customer",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `order` DROP FOREIGN KEY `FK_order_customer`;");
    }

    /// <summary>制約名不明時にプリペアド動的 SQL でカタログ逆引き削除を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名不明時にプリペアド動的 SQL で逆引き削除する")]
    public void DropForeignKey_UsesPreparedStatementWhenNameUnknown()
    {
        var customer = new Entity { TableName = "customer" };
        var order = new Entity { TableName = "order" };

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "order",
                ParentEntity = customer,
                ChildEntity = order,
                IsSelected = true,
            }
        );

        sql.Should().Contain("information_schema.REFERENTIAL_CONSTRAINTS");
        sql.Should().Contain("INTO @fk");
        sql.Should().Contain("rc.TABLE_NAME = 'order'");
        sql.Should().Contain("rc.REFERENCED_TABLE_NAME = 'customer'");
        sql.Should().Contain("SET @sql = IF(@fk IS NULL, 'DO 0'");
        sql.Should().Contain("PREPARE stmt FROM @sql;");
        sql.Should().Contain("EXECUTE stmt;");
        sql.Should().Contain("DEALLOCATE PREPARE stmt;");
    }

    /// <summary>SetTableDescription が ALTER TABLE ... COMMENT を生成することを検証する</summary>
    [Fact(DisplayName = "SetTableDescription は ALTER TABLE COMMENT を生成する")]
    public void SetTableDescription_EmitsAlterTableComment()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = "顧客マスタ",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` COMMENT = '顧客マスタ';");
    }

    /// <summary>SetColumnDescription が MODIFY COLUMN による完全再指定で COMMENT を設定することを検証する</summary>
    [Fact(DisplayName = "SetColumnDescription は MODIFY COLUMN で型・NULL・COMMENT を再指定する")]
    public void SetColumnDescription_EmitsModifyColumn()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "varchar(50)",
                IsNullable = false,
            }
        );

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = "customer",
                ColumnName = "name",
                Entity = e,
                NewDescription = "顧客名",
                IsSelected = true,
            }
        );

        sql.Should()
            .Contain(
                "ALTER TABLE `customer` MODIFY COLUMN `name` varchar(50) NOT NULL COMMENT '顧客名';"
            );
    }

    /// <summary>テーブル説明が空の場合に空文字 COMMENT（削除）が生成されることを検証する</summary>
    [Fact(DisplayName = "テーブル説明が空なら空文字 COMMENT を生成する")]
    public void EmptyTableDescription_EmitsEmptyComment()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = "",
                OldDescription = "古い",
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE `customer` COMMENT = '';");
    }

    /// <summary>説明内の単一引用符とバックスラッシュがエスケープされることを検証する</summary>
    [Fact(DisplayName = "説明内の ' と \\ がエスケープされる")]
    public void Description_EscapesSingleQuoteAndBackslash()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = @"O'Brien\path",
                IsSelected = true,
            }
        );

        sql.Should().Contain(@"COMMENT = 'O''Brien\\path';");
    }

    /// <summary>RebuildTable は情報表示専用で SQL を生成しないことを検証する</summary>
    [Fact(DisplayName = "RebuildTable は SQL を生成しない")]
    public void RebuildTable_GeneratesNothing()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.RebuildTable,
                TableName = "customer",
                IsSelected = true,
            }
        );

        sql.Should().NotContain("customer");
    }

    /// <summary>未選択の差分項目がスクリプトへ出力されないことを検証する</summary>
    [Fact(DisplayName = "選択されていない項目はスクリプトに含まれない")]
    public void Unselected_Excluded()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "customer",
                ColumnName = "email",
                Column = new Column { Name = "email", DataType = "varchar(200)" },
                IsSelected = false,
            }
        );

        sql.Should().NotContain("email");
    }

    /// <summary>依存関係を満たすよう CREATE → ADD COLUMN → ADD CONSTRAINT の順で出力されることを検証する</summary>
    [Fact(DisplayName = "実行順序: AddTable → AddColumn → AddForeignKey")]
    public void Order_AddTable_Then_AddColumn_Then_Fk()
    {
        var e = new Entity { TableName = "t" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
            }
        );

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddForeignKey,
                TableName = "t",
                ColumnName = "customer_id",
                ParentEntity = customer,
                ChildEntity = e,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "t",
                ColumnName = "customer_id",
                Column = new Column { Name = "customer_id", DataType = "int" },
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "t",
                Entity = e,
                IsSelected = true,
            }
        );

        var iCreate = sql.IndexOf("CREATE TABLE", StringComparison.Ordinal);
        var iAdd = sql.IndexOf("ADD COLUMN `customer_id`", StringComparison.Ordinal);
        var iFk = sql.IndexOf("ADD CONSTRAINT", StringComparison.Ordinal);
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
    }

    // ---------------- 列順変更 (MODIFY ... AFTER) ----------------

    /// <summary>指定名・順の列（int NULL）を持つエンティティを組み立てる</summary>
    private static Entity ReorderTable(string name, params string[] cols)
    {
        var e = new Entity { TableName = name };

        foreach (var c in cols)
        {
            e.Columns.Add(
                new Column
                {
                    Name = c,
                    DataType = "int",
                    IsNullable = true,
                }
            );
        }

        return e;
    }

    /// <summary>Native ケーパビリティ＋live 土台で ReorderColumns から MySQL スクリプトを生成する</summary>
    private static string BuildReorder(params (Entity Live, Entity Target)[] tables)
    {
        var items = tables
            .Select(t => new SchemaDiffItem
            {
                Kind = SchemaDiffKind.ReorderColumns,
                TableName = t.Live.TableName,
                Entity = t.Target,
                IsSelected = true,
            })
            .ToArray();
        var context = new SyncPlanContext { LiveEntities = tables.Select(t => t.Live).ToArray() };
        var plan = new SyncPlanner().BuildPlan(
            items,
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native },
            context
        );
        return new MySqlSyncScriptBuilder().Build(plan);
    }

    /// <summary>列順変更が見出しと MODIFY COLUMN ... AFTER を生成することを検証する</summary>
    [Fact(DisplayName = "ReorderColumns は MODIFY COLUMN ... AFTER と見出しを生成する")]
    public void Reorder_GeneratesModifyAfterWithHeading()
    {
        // live: id,a,b,c → target: id,c,a,b（c を id の直後へ）
        var sql = BuildReorder(
            (ReorderTable("t", "id", "a", "b", "c"), ReorderTable("t", "id", "c", "a", "b"))
        );

        sql.Should().Contain("-- ===== ReorderColumns: t =====");
        sql.Should().Contain("ALTER TABLE `t` MODIFY COLUMN `c` int NULL AFTER `id`;");
    }

    /// <summary>先頭へ動かす列が FIRST を生成することを検証する</summary>
    [Fact(DisplayName = "ReorderColumns は先頭移動で FIRST を生成する")]
    public void Reorder_MoveToFront_GeneratesFirst()
    {
        // live: a,b,c → target: c,a,b（c を先頭へ）
        var sql = BuildReorder(
            (ReorderTable("t", "a", "b", "c"), ReorderTable("t", "c", "a", "b"))
        );

        sql.Should().Contain("ALTER TABLE `t` MODIFY COLUMN `c` int NULL FIRST;");
        sql.Should().NotContain("AFTER");
    }

    /// <summary>複数テーブルの列順変更がテーブルごとの見出しで出力されることを検証する</summary>
    [Fact(DisplayName = "ReorderColumns は複数テーブルをテーブルごとに出力する")]
    public void Reorder_MultipleTables_EmitsPerTableHeadings()
    {
        var sql = BuildReorder(
            (ReorderTable("t1", "id", "a", "b", "c"), ReorderTable("t1", "id", "c", "a", "b")),
            (ReorderTable("t2", "x", "y", "z"), ReorderTable("t2", "z", "x", "y"))
        );

        sql.Should().Contain("-- ===== ReorderColumns: t1 =====");
        sql.Should().Contain("-- ===== ReorderColumns: t2 =====");
        sql.Should().Contain("ALTER TABLE `t1` MODIFY COLUMN `c` int NULL AFTER `id`;");
        sql.Should().Contain("ALTER TABLE `t2` MODIFY COLUMN `z` int NULL FIRST;");
    }

    // ---------------- 主キー変更（AlterPrimaryKey） ----------------

    /// <summary>指定の主キー列を持つ target エンティティを組み立てる</summary>
    private static Entity PkTarget(string table, params string[] pkColumns)
    {
        var e = new Entity { TableName = table };
        e.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = "varchar(50)",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                }
            );
        }

        return e;
    }

    /// <summary>主キー変更の差分項目を生成する（Entity＝新しい主キー構成の源）</summary>
    private static SchemaDiffItem AlterPk(string table, Entity target) =>
        new()
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = table,
            Entity = target,
            IsSelected = true,
        };

    /// <summary>主キー変更が存在確認付きの動的 DROP と ADD PRIMARY KEY を生成することを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は存在確認付き DROP と複合 PK の ADD を生成する")]
    public void AlterPrimaryKey_DropsExistingAndAddsComposite()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id", "line_no")));

        // 主キーが無いテーブルでは DROP PRIMARY KEY がエラーになるため存在確認してから動的 SQL で外す
        sql.Should().Contain("SET @pk = NULL;");
        sql.Should().Contain("FROM information_schema.TABLE_CONSTRAINTS tc");
        sql.Should()
            .Contain(
                "WHERE tc.CONSTRAINT_SCHEMA = DATABASE() AND tc.TABLE_NAME = 'orders' "
                    + "AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY' LIMIT 1;"
            );
        sql.Should()
            .Contain(
                "SET @sql = IF(@pk IS NULL, 'DO 0', 'ALTER TABLE `orders` DROP PRIMARY KEY');"
            );
        sql.Should().Contain("PREPARE stmt FROM @sql;");
        // MySQL の主キー名は PRIMARY 固定のため CONSTRAINT 名は指定しない
        sql.Should().Contain("ALTER TABLE `orders` ADD PRIMARY KEY (`order_id`, `line_no`);");
        sql.Should().NotContain("ADD CONSTRAINT");
    }

    /// <summary>主キーが無いテーブルへの主キー付与でも DROP が無害な形（DO 0 へ分岐）で出ることを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す")]
    public void AlterPrimaryKey_AddOnly_EmitsGuardedDrop()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer", "id")));

        sql.Should().Contain("IF(@pk IS NULL, 'DO 0'");
        sql.Should().Contain("ALTER TABLE `customer` ADD PRIMARY KEY (`id`);");
    }

    /// <summary>主キーの解除のみ（新主キー列ゼロ）では付与文が出ないことを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 解除のみなら ADD を出さない")]
    public void AlterPrimaryKey_DropOnly_OmitsAdd()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer")));

        sql.Should().Contain("IF(@pk IS NULL, 'DO 0'");
        sql.Should().NotContain("ADD PRIMARY KEY");
    }
}
