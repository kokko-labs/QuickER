using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 取込の作業用エントリ <see cref="SchemaTableEntry"/> の名前の扱い（<see cref="SchemaNames"/> の規則で衝突を判定し、
/// 衝突で捨てた列の行が採った列へ混ざらない）のテストクラス
/// </summary>
public sealed class SchemaTableEntryNameTests
{
    [Theory(
        DisplayName = "テーブルの辞書の比較器は前後の空白と大文字小文字を区別せず、角括弧の内側の空白も除く"
    )]
    [InlineData("Orders", " orders ", true)]
    [InlineData("[dbo].[Orders]", "[dbo].[Orders ]", true)]
    [InlineData("[dbo].[Orders]", "[ DBO ].[orders]", true)]
    [InlineData("[dbo].[Orders]", "[dbo].[Order]", false)]
    [InlineData("[dbo].[Orders]", "[sales].[Orders]", false)]
    public void KeyComparer_FollowsSchemaNameRule(string left, string right, bool expected)
    {
        SchemaTableEntry.KeyComparer.Equals(left, right).Should().Be(expected);

        if (expected)
        {
            SchemaTableEntry
                .KeyComparer.GetHashCode(left)
                .Should()
                .Be(SchemaTableEntry.KeyComparer.GetHashCode(right));
        }
    }

    [Fact(
        DisplayName = "同じ名前の列は後着を採らず ColumnNameCollision で告げ、捨てた列の行は採った列へ引き当てない"
    )]
    public void TryBeginColumn_RejectsSameNameAndExactLookupIgnoresDropped()
    {
        var entry = new SchemaTableEntry
        {
            Key = "t",
            Entity = new Entity { TableName = "t" },
        };
        var warnings = new List<SchemaImportWarning>();

        entry.TryBeginColumn("Dup", warnings).Should().BeTrue();
        entry.AddColumn(new Column { Name = "Dup" }, "Dup");

        entry.TryBeginColumn("dup ", warnings).Should().BeFalse();

        var warning = warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.ColumnNameCollision);
        warning.TableName.Should().Be("t");
        warning.Subject.Should().Be("dup ");
        warning.Detail.Should().Be("Dup");

        entry.TryGetColumnExact("Dup", out var kept).Should().BeTrue();
        kept!.Name.Should().Be("Dup");
        entry
            .TryGetColumnExact("dup ", out _)
            .Should()
            .BeFalse("捨てた列の行は採った列へ混ざってはいけない");
        entry
            .TryFindColumnName((name, _) => name == "dup ", out _)
            .Should()
            .BeFalse("捨てた列は条件で探しても見つからない");
        entry
            .TryFindColumnName(
                (name, _) => string.Equals(name, "DUP", StringComparison.OrdinalIgnoreCase),
                out var catalogName
            )
            .Should()
            .BeTrue();
        catalogName.Should().Be("Dup", "宣言の綴りは採用した列のカタログ上の名前へ直す");
    }

    /// <summary>親 p（id・Dup・捨てた dup）と子 t（id・Dup・捨てた dup・r）を、列の衝突を通して組み立てる</summary>
    private static Dictionary<string, SchemaTableEntry> BuildCollidedTables()
    {
        var warnings = new List<SchemaImportWarning>();
        var tables = new Dictionary<string, SchemaTableEntry>(SchemaTableEntry.KeyComparer);

        foreach (
            var (tableName, columns) in new[]
            {
                ("p", new[] { "id", "Dup", "dup" }),
                ("t", new[] { "id", "Dup", "dup", "r" }),
            }
        )
        {
            var entry = new SchemaTableEntry
            {
                Key = tableName,
                Entity = new Entity { TableName = tableName },
            };

            foreach (var columnName in columns)
            {
                if (entry.TryBeginColumn(columnName, warnings))
                {
                    entry.AddColumn(new Column { Name = columnName, DataType = "int" }, columnName);
                }
            }

            tables[tableName] = entry;
        }

        return tables;
    }

    [Fact(
        DisplayName = "一意制約の構成列は綴りまで一致で引き、捨てた列の制約を採った列へ付け替えない"
    )]
    public void UniqueConstraintAttach_DoesNotMoveConstraintOfDroppedColumn()
    {
        var tables = BuildCollidedTables();
        var builder = new UniqueConstraintImportBuilder();
        builder.Add("p", "uq_dropped", "dup", "uq_dropped");
        builder.Add("p", "uq_kept", "Dup", "uq_kept");

        UniqueConstraintImportBuilder.Attach(tables, builder.Build());

        var p = tables["p"].Entity;
        p.UniqueConstraints.Should()
            .ContainSingle()
            .Which.Name.Should()
            .Be("uq_kept", "捨てた列の一意制約は作らない");
        p.UniqueConstraints[0].ColumnIds.Should().Equal(p.Columns.Single(c => c.Name == "Dup").Id);
    }

    [Fact(
        DisplayName = "外部キーの構成列は両端とも綴りまで一致で引き、捨てた列の外部キーを採った列へ付け替えない"
    )]
    public void ForeignKeyBuild_DoesNotMoveForeignKeyOfDroppedColumn()
    {
        var tables = BuildCollidedTables();
        var builder = new ForeignKeyRelationshipBuilder();
        builder.Add(
            "fk_dropped_child",
            "t",
            "dup",
            "p",
            "id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "fk_dropped_parent",
            "t",
            "r",
            "p",
            "dup",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );
        builder.Add(
            "fk_kept",
            "t",
            "Dup",
            "p",
            "id",
            ForeignKeyReferentialAction.NoAction,
            ForeignKeyReferentialAction.NoAction
        );

        var relationships = builder.Build(tables);

        var t = tables["t"].Entity;
        relationships
            .Should()
            .ContainSingle("捨てた列の外部キーは、子の側でも親の側でもリレーションにしない")
            .Which.ConstraintName.Should()
            .Be("fk_kept");
        t.Columns.Where(c => c.IsForeignKey).Select(c => c.Name).Should().Equal("Dup");
    }
}
