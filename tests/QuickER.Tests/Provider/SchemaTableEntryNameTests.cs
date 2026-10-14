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
            .ColumnsByName.TryGetValue("DUP", out _)
            .Should()
            .BeTrue("外部キーの参照先など綴りが宣言のまま返る引き当ては緩い照合");
    }
}
