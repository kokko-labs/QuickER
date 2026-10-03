using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML のリテラル・引用（型の引用、設定ブロック内の文字列、複合外部キーの列リスト）の往復と、計算列の欠落告知を検証するテストクラス。
/// </summary>
/// <remarks>
/// リテラルの終わりは、エスケープを 1 文字ずつ追って決める必要がある。「直前の 1 文字が <c>\</c> か」で判定すると、
/// <c>\</c> で終わる文字列（<c>'C:\\'</c>）の閉じ引用をエスケープと取り違え、後ろの設定まで飲み込む。
/// </remarks>
public class DbmlLiteralFidelityTests
{
    /// <summary>1 テーブル（主キー id と、型・説明・NULL 許可を指定した列 value）の図を組み立てる</summary>
    private static ErDiagram BuildSingleColumnDiagram(
        string dataType,
        string? description = null,
        bool isNullable = false
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "value",
                            DataType = dataType,
                            Description = description ?? string.Empty,
                            IsNullable = isNullable,
                        },
                    ],
                    UniqueConstraints = [],
                },
            ],
        };

    [Theory(DisplayName = "DBML 往復: 二重引用符を含む型と、その後ろの設定が保たれる")]
    [InlineData("\"MyType\"")]
    [InlineData("my\"type")]
    [InlineData("\"od;d\"(10)")]
    public void RoundTrip_TypeWithDoubleQuote_IsPreserved(string dataType)
    {
        var diagram = BuildSingleColumnDiagram(dataType, description: "note text");

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram)).Entities[0].Columns[1];

        restored.DataType.Should().Be(dataType);
        restored.Description.Should().Be("note text");
        restored.IsNullable.Should().BeFalse();
    }

    [Fact(DisplayName = "DBML 往復: バックスラッシュで終わる列の説明が保たれる")]
    public void RoundTrip_DescriptionEndingWithBackslash_IsPreserved()
    {
        var diagram = BuildSingleColumnDiagram("varchar(10)", description: @"C:\");

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram)).Entities[0].Columns[1];

        restored.Description.Should().Be(@"C:\");
    }

    [Fact(
        DisplayName = "DBML 取込: バックスラッシュで終わる note の後ろの設定も読む（他ツール形式）"
    )]
    public void Parse_NoteEndingWithBackslash_FollowedBySettings()
    {
        const string Dbml = """
            Table items {
              id int [pk]
              path varchar [note: 'C:\\', not null, unique]
              Note: 'ends with \\'
            }
            """;

        var entity = DbmlImporter.Parse(Dbml).Entities[0];
        var column = entity.Columns[1];

        column.Description.Should().Be(@"C:\");
        column.IsNullable.Should().BeFalse();
        entity.UniqueConstraints.Should().ContainSingle();
        entity.Description.Should().Be(@"ends with \");
    }

    [Fact(DisplayName = "DBML 取込: バックスラッシュで終わる索引名の後ろの設定も読む")]
    public void Parse_IndexNameEndingWithBackslash_FollowedBySettings()
    {
        const string Dbml = """
            Table items {
              id int [pk]
              code varchar

              Indexes {
                code [name: 'UQ\\', unique]
              }
            }
            """;

        var constraint = DbmlImporter
            .Parse(Dbml)
            .Entities[0]
            .UniqueConstraints.Should()
            .ContainSingle()
            .Subject;

        constraint.Name.Should().Be(@"UQ\");
    }

    [Fact(
        DisplayName = "DBML 取込: Ref 行の設定ブロック内の文字列に ] があっても参照アクションを読む"
    )]
    public void Parse_RefSettingsWithBracketInLiteral()
    {
        const string Dbml = """
            Table a {
              id int [pk]
            }

            Table b {
              a_id int
            }

            Ref: a.id < b.a_id [note: 'x]y', delete: cascade]
            """;

        DbmlImporter
            .Parse(Dbml)
            .Relationships.Should()
            .ContainSingle()
            .Which.OnDelete.Should()
            .Be(ForeignKeyReferentialAction.Cascade);
    }

    [Fact(DisplayName = "DBML 往復: 閉じ括弧・角括弧を含む列名と制約名の複合外部キーが保たれる")]
    public void RoundTrip_CompositeForeignKeyWithBracketsInNames_IsPreserved()
    {
        var parent = new Entity
        {
            TableName = "parents",
            Columns =
            [
                new Column
                {
                    Name = "a)",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "b",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var child = new Entity
        {
            TableName = "children",
            Columns =
            [
                new Column { Name = "x)", DataType = "int" },
                new Column { Name = "y", DataType = "int" },
            ],
        };
        var diagram = new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = "FK]1",
                    ColumnPairs =
                    [
                        new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[0].Id),
                        new RelationshipColumnPair(parent.Columns[1].Id, child.Columns[1].Id),
                    ],
                },
            ],
        };

        var restored = DbmlImporter.Parse(DbmlExporter.Build(diagram));

        var relationship = restored.Relationships.Should().ContainSingle().Subject;
        var restoredParent = restored.Entities.Single(entity => entity.TableName == "parents");
        var restoredChild = restored.Entities.Single(entity => entity.TableName == "children");
        relationship.ConstraintName.Should().Be("FK]1");
        relationship
            .ColumnPairs.Select(pair =>
                restoredParent.Columns.Single(c => c.Id == pair.SourceColumnId).Name
                + "->"
                + restoredChild.Columns.Single(c => c.Id == pair.TargetColumnId).Name
            )
            .Should()
            .Equal("a)->x)", "b->y");
    }

    [Fact(DisplayName = "DBML の欠落: 計算列があるときだけ計算列の指定を告知する")]
    public void DetectOmissions_ComputedColumn_IsReportedOnlyWhenPresent()
    {
        var diagram = BuildSingleColumnDiagram("int");

        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.ComputedColumn);

        diagram.Entities[0].Columns[1].IsComputed = true;

        DbmlExporter.DetectOmissions(diagram).Should().Contain(ExportOmissionKind.ComputedColumn);
    }
}
