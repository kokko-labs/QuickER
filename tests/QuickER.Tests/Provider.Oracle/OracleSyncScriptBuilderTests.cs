using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider.Oracle;

/// <summary>
/// <see cref="OracleSyncScriptBuilder"/> が差分から生成する Oracle DDL のうち、実 DB で流しても観測できない文字列の形
/// （「/」区切り規約・実方言の差分からは届かない分岐）を検証するテストクラス
/// </summary>
/// <remarks>
/// 実 DB で流して結果を観測できる文の形は、同期計画の期待の行（<c>SyncPlanExpectationRows.RealOracle.cs</c> の
/// <c>realdb-oracle-script-*</c> など）が実 Oracle で流して見る。
/// </remarks>
public class OracleSyncScriptBuilderTests
{
    private static string Build(params SchemaDiffItem[] items) =>
        new OracleSyncScriptBuilder().Build(
            new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral)
        );

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

    /// <summary>変更前の列があっても、NULL 許容が変わるときは句を付けることを検証する（主キーの列は NOT NULL とみなす）</summary>
    [Fact(DisplayName = "AlterColumn は NULL 許容が変わるときだけ句を付ける")]
    public void AlterColumn_NullabilityChange_KeepsClause()
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
                    DataType = "VARCHAR2(50)",
                    IsNullable = true,
                },
                OldColumn = new Column
                {
                    Name = "name",
                    DataType = "VARCHAR2(50)",
                    IsNullable = true,
                    IsPrimaryKey = true,
                },
                IsSelected = true,
            }
        );

        sql.Should().Contain("ALTER TABLE \"customer\" MODIFY (\"name\" VARCHAR2(50) NULL);");
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
        sql.Should().Contain("FROM all_constraints c");
        sql.Should().Contain("constraint_type = 'R'");
        sql.Should()
            .Contain(
                "c.owner = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AND c.table_name = 'order'"
            );
        sql.Should()
            .Contain(
                "r.owner = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AND r.table_name = 'customer'"
            );
        sql.Should().Contain("EXECUTE IMMEDIATE");
        sql.Should().Contain("WHEN NO_DATA_FOUND THEN NULL");
        sql.Should().Contain("END;");
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

    // ---------------- 文の区切り（SQL*Plus と同じ）----------------

    /// <summary>通常の文は ; で終わり、後ろに「/」行を置かないことを検証する</summary>
    /// <remarks>SQL*Plus の「/」は直前の文をもう一度実行するため、通常の文の後ろに置くと同じ文が 2 回走る</remarks>
    [Fact(DisplayName = "通常の文は ; で終わり「/」行を置かない（SQL*Plus で 2 回実行させない）")]
    public void PlainStatements_AreNotFollowedBySlashLine()
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

        // 通常の文だけなので「/」のみの行は 1 本も無く、各文は ; で終わる
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        lines.Count(l => l.Trim() == "/").Should().Be(0);
        lines.Should().Contain(");").And.Contain("DROP TABLE \"u\";");
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

    // ---------------- 主キー変更（AlterPrimaryKey） ----------------

    /// <summary>指定の主キー列を持つ target エンティティを組み立てる</summary>
    private static Entity PkTarget(string table, params string[] pkColumns)
    {
        var e = new Entity { TableName = table };
        e.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = "VARCHAR2(50)",
                IsNullable = true,
            }
        );

        foreach (var name in pkColumns)
        {
            e.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = "NUMBER(10)",
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

    /// <summary>
    /// 主キー変更の「/」のみの行は、旧主キーを外す PL/SQL ブロックの後ろの 1 本だけで、新主キーの ADD は ; で終わることを検証する
    /// </summary>
    [Fact(DisplayName = "AlterPrimaryKey は PL/SQL ブロックの DROP と複合 PK の ADD を生成する")]
    public void AlterPrimaryKey_DropsExistingAndAddsComposite()
    {
        var sql = Build(AlterPk("orders", PkTarget("orders", "order_id", "line_no")));

        // 「/」のみの行は DROP（PL/SQL ブロック）の後ろの 1 本だけ。ADD は ; で終わる
        var slashLines = sql.Replace("\r\n", "\n").Split('\n').Count(l => l.Trim() == "/");
        slashLines.Should().Be(1);
    }

    // ---------------- 動的 SQL のリテラルエスケープ ----------------

    /// <summary>
    /// スキーマで修飾した表名の逆引きは、そのスキーマを所有者として絞り、外部キーの子と親の所有者は別々に決まることを検証する
    /// （所有者で絞らないと、自スキーマの同じ名前の表の制約を引いた）。
    /// </summary>
    [Fact(
        DisplayName = "逆引き: 修飾した表名はそのスキーマで、修飾しない表名は現在のスキーマで所有者を絞る"
    )]
    public void RuntimeLookup_QualifiedTableName_IsScopedByOwner()
    {
        var sql = Build(
            AlterPk("OTHER.T", PkTarget("OTHER.T", "CODE")),
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.DropForeignKey,
                TableName = "OTHER.C",
                ParentEntity = new Entity { TableName = "P" },
                ChildEntity = new Entity { TableName = "OTHER.C" },
                IsSelected = true,
            }
        );

        sql.Should()
            .Contain("WHERE c.constraint_type = 'P' AND c.owner = 'OTHER' AND c.table_name = 'T';");
        sql.Should().Contain("c.owner = 'OTHER' AND c.table_name = 'C'");
        sql.Should()
            .Contain("r.owner = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AND r.table_name = 'P'");
        sql.Should().NotContain("user_constraints");
    }

    /// <summary>逆引きの所有者のリテラルも、スキーマ名の <c>'</c> を二重化することを検証する</summary>
    [Fact(DisplayName = "逆引き: 所有者のリテラルはスキーマ名の ' を二重化する")]
    public void RuntimeLookup_OwnerLiteral_IsEscaped()
    {
        var sql = Build(AlterPk("o'ther.T", PkTarget("o'ther.T", "CODE")));

        sql.Should().Contain("c.owner = 'o''ther' AND c.table_name = 'T'");
        sql.Should().NotContain("c.owner = 'o'ther'");
    }

    /// <summary>制約名不明の外部キー削除でも、PL/SQL ブロックのテーブル名がリテラル用にエスケープされることを検証する</summary>
    [Fact(DisplayName = "DropForeignKey の PL/SQL ブロックはテーブル名の ' を二重化する")]
    public void DropForeignKey_TableNameWithQuote_IsEscapedInDynamicSql()
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = "o'rders",
            ParentEntity = new Entity { TableName = "customer" },
            ChildEntity = new Entity { TableName = "o'rders" },
            IsSelected = true,
        };

        var sql = Build(item);

        sql.Should()
            .Contain(
                "EXECUTE IMMEDIATE 'ALTER TABLE \"o''rders\" DROP CONSTRAINT \"' || v_name || '\"';"
            );
        sql.Should().NotContain("EXECUTE IMMEDIATE 'ALTER TABLE \"o'rders\"");
    }
}
