using AwesomeAssertions;
using QuickER.Dialects;
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

    /// <remarks>
    /// 残す理由（P3-6）：PostgreSQL の上限の文言（63 UTF-8 bytes）・違反の件数・場所の前置きの形。両ホストのツールが 4 種の名前の違反を
    /// 告げることは ToolScenarioTests の行 <c>name-limit-*</c>（SQL Server の上限）が持つが、この表明を外すと弱くなるので元の表明をすべて残す
    /// </remarks>
    [Fact(DisplayName = "Find: 場所と理由つきで、テーブル名・カラム名・制約名の違反を返す")]
    public void Find_ReportsEachKind()
    {
        var diagram = Diagram("postgresql", new string('t', 64), new string('c', 64));
        diagram.Entities[0].UniqueConstraints[0].Name = new string('u', 64);
        diagram.Relationships.Add(new Relationship { ConstraintName = new string('f', 64) });

        var found = NameLimitNotes.Find(
            diagram,
            IdentifierLengthLimits.ForDbms(diagram.TargetDbms)
        );

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
        NameLimitNotes
            .Find(Diagram("postgresql", "orders", "id"), IdentifierLengthLimits.PostgreSql)
            .Should()
            .BeEmpty();
        NameLimitNotes
            .Find(Diagram("sqlite", new string('t', 500), "id"), IdentifierLengthLimits.Sqlite)
            .Should()
            .BeEmpty();
    }

    /// <summary>
    /// 方言ごとの判定（表せない文字・表の名前だけに掛かるファイル名の上限）が、名前の種類ごとに正しく掛かることを検証する
    /// </summary>
    /// <param name="row">
    /// 行（mysql-emoji-column / postgresql-emoji-column / mysql-file-name-table-only）
    /// </param>
    /// <remarks>
    /// MySQL の表の名前は 64 文字の上限に加えて、ファイル名へ符号化した長さ（251 バイト）にも掛かる。
    /// 記号 <c>-</c> は 1 字 5 バイトに符号化されるので、51 字は文字数の上限の内でファイル名の上限を超える。
    /// 同じ名前をカラム名・一意制約名・外部キー名に付けても違反にならない（表の名前だけの上限）。
    /// </remarks>
    [Theory(
        DisplayName = "Find: MySQL では BMP の外の文字を含む名前と、ファイル名の上限を超える表の名前だけを返す"
    )]
    [InlineData("mysql-emoji-column")]
    [InlineData("postgresql-emoji-column")]
    [InlineData("mysql-file-name-table-only")]
    public void Find_Unrepresentable_OnlyOnMySql(string row)
    {
        var dashes = new string('-', 51);
        var (diagram, limit, expected) = row switch
        {
            "mysql-emoji-column" => (
                Diagram("mysql", "orders", Emoji),
                IdentifierLengthLimits.MySql,
                new[] { "column of 'orders':" }
            ),
            "postgresql-emoji-column" => (
                Diagram("postgresql", "orders", Emoji),
                IdentifierLengthLimits.PostgreSql,
                Array.Empty<string>()
            ),
            "mysql-file-name-table-only" => (
                Diagram("mysql", dashes, dashes),
                IdentifierLengthLimits.MySql,
                new[] { "table '" }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        };

        if (row == "mysql-file-name-table-only")
        {
            diagram.Entities[0].UniqueConstraints[0].Name = dashes;
            diagram.Relationships.Add(new Relationship { ConstraintName = dashes });
        }

        var found = NameLimitNotes.Find(diagram, limit);

        found.Should().HaveCount(expected.Length);

        foreach (var (line, prefix) in found.Zip(expected))
        {
            line.Should().StartWith(prefix);
        }
    }

    /// <remarks>
    /// 残す理由（P3-6）：注意文の前置き・新しい違反だけの本文・違反が減ったときと同じときの null。以前からの違反を繰り返さないことは
    /// ToolScenarioTests の行 <c>name-limit-within-limit-adds-no-note-and-existing-violation-is-not-repeated</c> が持つが、この表明を外すと
    /// 弱くなるので元の表明をすべて残す
    /// </remarks>
    [Fact(DisplayName = "Describe: 変更で新しく生じた違反だけを注意文にし、無ければ null")]
    public void Describe_OnlyNewViolations()
    {
        var before = new[] { "table 'a' is longer" };
        var after = new[] { "table 'a' is longer", "table 'b' is longer", "table 'c' is longer" };

        var note = NameLimitNotes.Describe(before, after, "postgresql");

        note.Should()
            .StartWith("Note:")
            .And.Contain("table 'b' is longer")
            .And.NotContain("table 'a'");
        // 文面の全体（前置き・新しい違反を「; 」でつなぐ・末尾の「.」）
        note.Should()
            .Be(
                "Note: postgresql would refuse these names, so DDL generation and sync stop until they are changed: "
                    + "table 'b' is longer; table 'c' is longer."
            );
        NameLimitNotes.Describe(after, after, "postgresql").Should().BeNull();
        NameLimitNotes.Describe(after, before, "postgresql").Should().BeNull();
    }
}
