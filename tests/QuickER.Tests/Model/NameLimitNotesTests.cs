using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// 図の名前のうち対象 DBMS が受け付けないものを見つけ、変更の前後の違反を比べて注意文にする（<see cref="NameLimitNotes"/>）ことを検証するテストクラス
/// </summary>
public class NameLimitNotesTests
{
    private static readonly string Emoji = "x" + char.ConvertFromUtf32(0x1F600);

    private static ErDiagram Diagram(string dbms, string tableName, string columnName)
    {
        var entity = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column { Name = columnName, DataType = "int" },
            },
        };
        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_ok", ColumnIds = { entity.Columns[0].Id } }
        );

        return new ErDiagram { TargetDbms = dbms, Entities = { entity } };
    }

    [Fact(DisplayName = "Find: 場所と理由つきで、テーブル名・カラム名・制約名の違反を返す")]
    public void Find_ReportsEachKind()
    {
        var diagram = Diagram("postgresql", new string('t', 64), new string('c', 64));
        diagram.Entities[0].UniqueConstraints[0].Name = new string('u', 64);
        diagram.Relationships.Add(new Relationship { ConstraintName = new string('f', 64) });

        var found = NameLimitNotes.Find(diagram);

        found.Should().HaveCount(4);
        found
            .Should()
            .Contain(line => line.StartsWith("table '") && line.Contains("63 UTF-8 bytes"));
        found.Should().Contain(line => line.StartsWith("column of '"));
        found.Should().Contain(line => line.StartsWith("unique constraint of '"));
        found.Should().Contain(line => line.StartsWith("foreign key '"));
    }

    [Fact(DisplayName = "Find: 上限内の名前・上限なしの対象 DBMS では何も返さない")]
    public void Find_WithinLimit_ReturnsNothing()
    {
        NameLimitNotes.Find(Diagram("postgresql", "orders", "id")).Should().BeEmpty();
        NameLimitNotes.Find(Diagram("sqlite", new string('t', 500), "id")).Should().BeEmpty();
    }

    [Fact(DisplayName = "Find: MySQL では BMP の外の文字を含む名前を返し、PostgreSQL では返さない")]
    public void Find_Unrepresentable_OnlyOnMySql()
    {
        NameLimitNotes.Find(Diagram("mysql", "orders", Emoji)).Should().ContainSingle();
        NameLimitNotes.Find(Diagram("postgresql", "orders", Emoji)).Should().BeEmpty();
    }

    [Fact(DisplayName = "Describe: 変更で新しく生じた違反だけを注意文にし、無ければ null")]
    public void Describe_OnlyNewViolations()
    {
        var before = new[] { "table 'a' is longer" };
        var after = new[] { "table 'a' is longer", "table 'b' is longer" };

        var note = NameLimitNotes.Describe(before, after, "postgresql");

        note.Should()
            .StartWith("Note:")
            .And.Contain("table 'b' is longer")
            .And.NotContain("table 'a'");
        NameLimitNotes.Describe(after, after, "postgresql").Should().BeNull();
        NameLimitNotes.Describe(after, before, "postgresql").Should().BeNull();
    }
}
