using AwesomeAssertions;
using QuickER.Model;
using QuickER.PostgreSql;
using QuickER.Provider;

namespace QuickER.Tests.PostgreSql;

/// <summary><see cref="PostgreSqlSyncScriptBuilder"/> が差分から生成する PostgreSQL DDL の内容と出力順序を検証するテストクラス</summary>
public class PostgreSqlSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new PostgreSqlSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities())
        );

    /// <summary>AddTable が主キー制約を含む CREATE TABLE 文を生成し、GO を使わないことを検証する</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を含み GO を使わない")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
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

        sql.Should().Contain("CREATE TABLE \"customer\"");
        sql.Should().Contain("\"id\" integer NOT NULL");
        sql.Should().Contain("CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\")");
        sql.Should().NotContain("GO");
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

        sql.Should()
            .Contain("ALTER TABLE \"customer\" ADD COLUMN \"email\" varchar(200) NOT NULL;");
    }

    /// <summary>AlterColumn が型変更（TYPE）と NULL 制約（SET NOT NULL）を別文で生成することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は TYPE と SET NOT NULL を別文で生成する")]
    public void AlterColumn_GeneratesTypeAndNotNull()
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

        sql.Should().Contain("ALTER TABLE \"customer\" ALTER COLUMN \"name\" TYPE varchar(100);");
        sql.Should().Contain("ALTER TABLE \"customer\" ALTER COLUMN \"name\" SET NOT NULL;");
    }

    /// <summary>NULL 許容へ変更する AlterColumn が DROP NOT NULL を生成することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は NULL 許容化で DROP NOT NULL を生成する")]
    public void AlterColumn_NullableGeneratesDropNotNull()
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

        sql.Should().Contain("ALTER TABLE \"customer\" ALTER COLUMN \"note\" DROP NOT NULL;");
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

        sql.Should().Contain("ALTER TABLE \"customer\" DROP COLUMN \"old\";");
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

        sql.Should().Contain("DROP TABLE \"customer\";");
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
                DataType = "integer",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "order" };
        order.Columns.Add(new Column { Name = "customer_id", DataType = "integer" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = customer.Columns[0].Id,
            ConstraintName = "FK_order_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetDefault,
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

        sql.Should().Contain("ALTER TABLE \"order\" ADD CONSTRAINT \"FK_order_customer\"");
        sql.Should().Contain("FOREIGN KEY (\"customer_id\") REFERENCES \"customer\" (\"id\")");
        sql.Should().Contain("ON DELETE CASCADE");
        sql.Should().Contain("ON UPDATE SET DEFAULT");
    }

    /// <summary>DropForeignKey が制約名判明時に DROP CONSTRAINT IF EXISTS を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名があれば DROP CONSTRAINT IF EXISTS を生成する")]
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

        sql.Should()
            .Contain("ALTER TABLE \"order\" DROP CONSTRAINT IF EXISTS \"FK_order_customer\";");
    }

    /// <summary>制約名不明時に DO ブロックでカタログ逆引き削除を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名不明時に DO ブロックで逆引き削除する")]
    public void DropForeignKey_UsesDoBlockWhenNameUnknown()
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

        sql.Should().Contain("DO $$");
        sql.Should().Contain("pg_constraint");
        sql.Should().Contain("child.relname = 'order'");
        sql.Should().Contain("parent.relname = 'customer'");
    }

    /// <summary>SetTableDescription が COMMENT ON TABLE を生成することを検証する</summary>
    [Fact(DisplayName = "SetTableDescription は COMMENT ON TABLE を生成する")]
    public void SetTableDescription_EmitsCommentOnTable()
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

        sql.Should().Contain("COMMENT ON TABLE \"customer\" IS '顧客マスタ';");
    }

    /// <summary>SetColumnDescription が COMMENT ON COLUMN を生成することを検証する</summary>
    [Fact(DisplayName = "SetColumnDescription は COMMENT ON COLUMN を生成する")]
    public void SetColumnDescription_EmitsCommentOnColumn()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetColumnDescription,
                TableName = "customer",
                ColumnName = "name",
                NewDescription = "顧客名",
                IsSelected = true,
            }
        );

        sql.Should().Contain("COMMENT ON COLUMN \"customer\".\"name\" IS '顧客名';");
    }

    /// <summary>新しい説明が空の場合に COMMENT ON ... IS NULL（削除）が生成されることを検証する</summary>
    [Fact(DisplayName = "説明が空なら COMMENT ON ... IS NULL を生成する")]
    public void EmptyDescription_EmitsCommentIsNull()
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

        sql.Should().Contain("COMMENT ON TABLE \"customer\" IS NULL;");
    }

    /// <summary>説明内の単一引用符が二重化エスケープされることを検証する</summary>
    [Fact(DisplayName = "説明内の ' がエスケープされる")]
    public void Description_EscapesSingleQuote()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.SetTableDescription,
                TableName = "customer",
                NewDescription = "O'Brien の顧客",
                IsSelected = true,
            }
        );

        sql.Should().Contain("IS 'O''Brien の顧客';");
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
                DataType = "integer",
                IsPrimaryKey = true,
            }
        );
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
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
                Column = new Column { Name = "customer_id", DataType = "integer" },
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
        var iAdd = sql.IndexOf("ADD COLUMN \"customer_id\"", StringComparison.Ordinal);
        var iFk = sql.IndexOf("ADD CONSTRAINT", StringComparison.Ordinal);
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
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
                DataType = "text",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "integer",
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

    /// <summary>主キー変更が DO ブロックの動的 DROP と新主キーの ADD CONSTRAINT を生成することを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は DO ブロックの DROP と複合 PK の ADD を生成する")]
    public void AlterPrimaryKey_DropsExistingAndAddsComposite()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id", "line_no")));

        // 旧主キーの制約名は差分に無いため pg_constraint から逆引きして DO ブロックで外す
        sql.Should().Contain("DO $$");
        sql.Should().Contain("FROM pg_constraint con");
        sql.Should().Contain("WHERE con.contype = 'p' AND tbl.relname = 'orders'");
        sql.Should().Contain("AND tbl.relnamespace = 'public'::regnamespace;");
        sql.Should().Contain("IF pk_name IS NOT NULL THEN");
        sql.Should()
            .Contain("EXECUTE 'ALTER TABLE \"orders\" DROP CONSTRAINT \"' || pk_name || '\"';");
        // 新主キーは列定義順の複合キーとして CREATE TABLE と同じ制約名規則で付与する
        sql.Should()
            .Contain(
                "ALTER TABLE \"orders\" ADD CONSTRAINT \"PK_orders\" PRIMARY KEY (\"order_id\", \"line_no\");"
            );
    }

    /// <summary>主キーが無いテーブルへの主キー付与でも DROP が無害な形（存在時のみ実行）で出ることを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 付与のみでも DROP を no-op 形で出す")]
    public void AlterPrimaryKey_AddOnly_EmitsGuardedDrop()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer", "id")));

        sql.Should().Contain("IF pk_name IS NOT NULL THEN");
        sql.Should()
            .Contain(
                "ALTER TABLE \"customer\" ADD CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\");"
            );
    }

    /// <summary>主キーの解除のみ（新主キー列ゼロ）では付与文が出ないことを検証する</summary>
    [Fact(DisplayName = "AlterPrimaryKey は PK 解除のみなら ADD を出さない")]
    public void AlterPrimaryKey_DropOnly_OmitsAdd()
    {
        var sql = Build(AlterPk("customer", PkTarget("customer")));

        sql.Should().Contain("IF pk_name IS NOT NULL THEN");
        sql.Should().NotContain("ADD CONSTRAINT");
        sql.Should().NotContain("PRIMARY KEY (");
    }
}
