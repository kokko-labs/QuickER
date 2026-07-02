using System.Linq;
using FluentAssertions;
using QuickER.Model;
using QuickER.Oracle;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary><see cref="OracleSyncScriptBuilder"/> が差分から生成する Oracle DDL の内容・出力順序・「/」区切り規約を検証するテストクラス</summary>
public class OracleSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new OracleSyncScriptBuilder().Build(items);

    /// <summary>AddTable が主キー制約を含む CREATE TABLE 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddTable は CREATE TABLE と PK を生成する")]
    public void AddTable_GeneratesCreate()
    {
        var e = new Entity { TableName = "customer" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        e.Columns.Add(
            new Column
            {
                Name = "name",
                DataType = "VARCHAR2(50)",
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
        sql.Should().Contain("\"id\" NUMBER(10) NOT NULL");
        sql.Should().Contain("CONSTRAINT \"PK_customer\" PRIMARY KEY (\"id\")");
    }

    /// <summary>AddColumn が ALTER TABLE ... ADD 文を生成することを検証する</summary>
    [Fact(DisplayName = "AddColumn は ALTER TABLE ADD (...) を生成する")]
    public void AddColumn_GeneratesAlterAdd()
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
                    DataType = "VARCHAR2(200)",
                    IsNullable = false,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE \"customer\" ADD (\"email\" VARCHAR2(200) NOT NULL);");
    }

    /// <summary>NULL 許容の AddColumn は NULL 句を付けないことを検証する（Oracle は NULL が既定）</summary>
    [Fact(DisplayName = "AddColumn（NULL 許容）は NULL 句を付けない")]
    public void AddColumn_Nullable_OmitsNullClause()
    {
        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddColumn,
                TableName = "customer",
                ColumnName = "note",
                Column = new Column
                {
                    Name = "note",
                    DataType = "VARCHAR2(200)",
                    IsNullable = true,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE \"customer\" ADD (\"note\" VARCHAR2(200));");
    }

    /// <summary>AlterColumn が MODIFY 文（型＋NOT NULL）を生成することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は MODIFY で型と NOT NULL を出す")]
    public void AlterColumn_GeneratesModifyNotNull()
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
                    DataType = "VARCHAR2(100)",
                    IsNullable = false,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE \"customer\" MODIFY (\"name\" VARCHAR2(100) NOT NULL);");
    }

    /// <summary>NULL 許容化する AlterColumn が MODIFY ... NULL を明示することを検証する</summary>
    [Fact(DisplayName = "AlterColumn は NULL 許容化で MODIFY ... NULL を明示する")]
    public void AlterColumn_NullableGeneratesModifyNull()
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
                    DataType = "VARCHAR2(200)",
                    IsNullable = true,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE \"customer\" MODIFY (\"note\" VARCHAR2(200) NULL);");
    }

    /// <summary>DropColumn が DROP COLUMN 文を生成することを検証する</summary>
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

    /// <summary>AddForeignKey が FK 制約追加文と ON DELETE 句を生成し、ON UPDATE を出さないことを検証する</summary>
    [Fact(DisplayName = "AddForeignKey は ON DELETE を出し ON UPDATE は出さず注意コメントを付す")]
    public void AddForeignKey_GeneratesConstraint_NoOnUpdate()
    {
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
                IsPrimaryKey = true,
            }
        );
        var order = new Entity { TableName = "order" };
        order.Columns.Add(new Column { Name = "customer_id", DataType = "NUMBER(10)" });
        var rel = new Relationship
        {
            SourceEntityId = customer.Id,
            TargetEntityId = order.Id,
            Type = RelationshipType.OneToMany,
            SourceColumnId = customer.Columns[0].Id,
            ConstraintName = "FK_order_customer",
            OnDelete = ForeignKeyReferentialAction.Cascade,
            OnUpdate = ForeignKeyReferentialAction.SetDefault, // Oracle では無視される
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
        // 注意コメントには "ON UPDATE" が含まれるが、SQL の句としては出力しない
        sql.Should().NotContain("REFERENCES \"customer\" (\"id\") ON DELETE CASCADE ON UPDATE");
        sql.Should().Contain("-- 注: Oracle は ON UPDATE をサポートしないため無視");
    }

    /// <summary>DropForeignKey が制約名判明時に DROP CONSTRAINT を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名があれば DROP CONSTRAINT を生成する")]
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

        sql.Should().Contain("ALTER TABLE \"order\" DROP CONSTRAINT \"FK_order_customer\";");
    }

    /// <summary>制約名不明時に PL/SQL 無名ブロックでカタログ逆引き削除を生成することを検証する</summary>
    [Fact(DisplayName = "DropForeignKey は制約名不明時に PL/SQL ブロックで逆引き削除する")]
    public void DropForeignKey_UsesPlSqlBlockWhenNameUnknown()
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

        sql.Should().Contain("DECLARE");
        sql.Should().Contain("user_constraints");
        sql.Should().Contain("constraint_type = 'R'");
        sql.Should().Contain("c.table_name = 'order'");
        sql.Should().Contain("r.table_name = 'customer'");
        sql.Should().Contain("EXECUTE IMMEDIATE");
        sql.Should().Contain("WHEN NO_DATA_FOUND THEN NULL");
        sql.Should().Contain("END;");
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

    /// <summary>説明が空なら COMMENT ON ... IS '' （空文字での削除）が生成されることを検証する</summary>
    [Fact(DisplayName = "説明が空なら COMMENT ON ... IS '' を生成する（Oracle は IS NULL 不可）")]
    public void EmptyDescription_EmitsCommentIsEmptyString()
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

        sql.Should().Contain("COMMENT ON TABLE \"customer\" IS '';");
        sql.Should().NotContain("IS NULL");
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
                Column = new Column { Name = "email", DataType = "VARCHAR2(200)" },
                IsSelected = false,
            }
        );

        sql.Should().NotContain("email");
    }

    // ---------------- 「/」区切り規約 ----------------

    /// <summary>各文が「/」のみの行で区切られることを検証する</summary>
    [Fact(DisplayName = "各文は「/」のみの行で区切られる（SQL*Plus 流儀）")]
    public void Statements_AreSeparatedBySlashLine()
    {
        var e = new Entity { TableName = "t" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
                IsPrimaryKey = true,
            }
        );

        var sql = Build(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AddTable,
                TableName = "t",
                Entity = e,
                IsSelected = true,
            },
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropTable,
                TableName = "u",
                IsSelected = true,
            }
        );

        // 「/」のみの行が 2 本（各文の後）現れる
        var slashLines = sql.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim() == "/");
        slashLines.Should().Be(2);
    }

    /// <summary>PL/SQL ブロックが END; の後に「/」行を伴うことを検証する</summary>
    [Fact(DisplayName = "PL/SQL ブロックは END; の後に「/」行を持つ")]
    public void PlSqlBlock_IsFollowedBySlashLine()
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

        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var endIndex = System.Array.FindIndex(lines, l => l.Trim() == "END;");
        endIndex.Should().BeGreaterThan(-1);
        // END; の直後の非空行が「/」であること
        lines[endIndex + 1].Trim().Should().Be("/");
    }

    /// <summary>依存関係を満たすよう AddTable → AddColumn → AddForeignKey の順で出力されることを検証する</summary>
    [Fact(DisplayName = "実行順序: AddTable → AddColumn → AddForeignKey")]
    public void Order_AddTable_Then_AddColumn_Then_Fk()
    {
        var e = new Entity { TableName = "t" };
        e.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
                IsPrimaryKey = true,
            }
        );
        var customer = new Entity { TableName = "customer" };
        customer.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "NUMBER(10)",
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
                Column = new Column { Name = "customer_id", DataType = "NUMBER(10)" },
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

        var iCreate = sql.IndexOf("CREATE TABLE", System.StringComparison.Ordinal);
        var iAdd = sql.IndexOf("ADD (\"customer_id\"", System.StringComparison.Ordinal);
        var iFk = sql.IndexOf("ADD CONSTRAINT", System.StringComparison.Ordinal);
        iCreate.Should().BeGreaterThan(-1);
        iAdd.Should().BeGreaterThan(iCreate);
        iFk.Should().BeGreaterThan(iAdd);
    }
}
