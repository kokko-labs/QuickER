using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML の <c>Ref</c> 行の端点（書き出すかどうか・どの列を書くか）と、インライン参照・多対1 記号の取込を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 取込は <c>Ref</c> 行の列を外部キーの構成列として採るため、列ペアを持たない 1 対 1・1 対多を先頭列で書き出すと、
/// 定義していない外部キー（多くは子の主キー列）が往復で黙ってできる。書けないリレーションは出さずに告知する。
/// </para>
/// <para>
/// インライン参照の 1 対 1（<c>-</c>）は、<c>Ref</c> 行と逆に「<c>ref</c> を書いた列が外部キー」（DBML の規則）。
/// 取り違えやすいので、向きを列ペアで固定する。
/// </para>
/// </remarks>
public class DbmlRelationshipEndpointTests
{
    /// <summary>親 customers(id) と子 orders(id, customer_id) の図を組み立てる（リレーションは呼び出し側が足す）</summary>
    private static ErDiagram BuildParentChild()
    {
        var parent = new Entity
        {
            TableName = "customers",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var child = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column { Name = "customer_id", DataType = "int" },
            ],
        };

        return new ErDiagram { Entities = [parent, child] };
    }

    /// <summary>親子の間にリレーションを 1 本足す</summary>
    private static Relationship AddRelationship(
        ErDiagram diagram,
        RelationshipType type,
        bool withPair
    )
    {
        var parent = diagram.Entities[0];
        var child = diagram.Entities[1];
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = type,
            ColumnPairs = withPair
                ? [new RelationshipColumnPair(parent.Columns[0].Id, child.Columns[^1].Id)]
                : [],
        };
        diagram.Relationships.Add(relationship);

        return relationship;
    }

    [Theory(
        DisplayName = "DBML 出力: 列ペアの無い 1 対 1・1 対多は Ref 行を書かず告知し、往復で外部キーを作らない"
    )]
    [InlineData(RelationshipType.OneToMany)]
    [InlineData(RelationshipType.OneToOne)]
    public void Export_PairlessRelationship_IsOmittedAndReported(RelationshipType type)
    {
        var diagram = BuildParentChild();
        AddRelationship(diagram, type, withPair: false);

        var dbml = DbmlExporter.Build(diagram);
        var restored = DbmlImporter.Parse(dbml, "sqlserver");

        dbml.Should().NotContain("Ref");
        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.RelationshipWithoutColumns);
        restored.Relationships.Should().BeEmpty();
        restored
            .Entities.SelectMany(entity => entity.Columns)
            .Should()
            .OnlyContain(column => !column.IsForeignKey);
    }

    [Fact(
        DisplayName = "DBML 出力: 解決できない列を含む列ペアのリレーションも Ref 行を書かず告知する"
    )]
    public void Export_UnresolvablePair_IsOmittedAndReported()
    {
        var diagram = BuildParentChild();
        var relationship = AddRelationship(diagram, RelationshipType.OneToMany, withPair: true);
        relationship.ColumnPairs[0].TargetColumnId = Guid.NewGuid();

        DbmlExporter.Build(diagram).Should().NotContain("Ref");
        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.RelationshipWithoutColumns);
    }

    [Theory(
        DisplayName = "DBML 出力: 列の無いテーブルにつながるリレーションでも例外にならず、Ref 行を書かず告知する"
    )]
    [InlineData(RelationshipType.ManyToMany)]
    [InlineData(RelationshipType.OneToMany)]
    public void Export_RelationshipToTableWithoutColumns_DoesNotThrow(RelationshipType type)
    {
        var diagram = BuildParentChild();
        diagram.Entities[0].Columns.Clear();
        AddRelationship(diagram, type, withPair: false);

        var act = () => DbmlExporter.Build(diagram);

        act.Should().NotThrow().Which.Should().NotContain("Ref");
        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .Contain(ExportOmissionKind.RelationshipWithoutColumns);
    }

    [Fact(
        DisplayName = "DBML 出力: 列のあるテーブル間の多対多は従来どおり先頭列で書き、告知しない"
    )]
    public void Export_ManyToManyWithColumns_IsWrittenWithoutOmission()
    {
        var diagram = BuildParentChild();
        AddRelationship(diagram, RelationshipType.ManyToMany, withPair: false);

        var dbml = DbmlExporter.Build(diagram);

        dbml.Should().Contain("Ref: customers.id <> orders.id");
        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.RelationshipWithoutColumns);
        DbmlImporter
            .Parse(dbml, "sqlserver")
            .Relationships.Should()
            .ContainSingle()
            .Which.Type.Should()
            .Be(RelationshipType.ManyToMany);
    }

    [Fact(DisplayName = "DBML 出力: 列ペアのあるリレーションは書き出し、告知しない（対照）")]
    public void Export_PairedRelationship_IsWrittenWithoutOmission()
    {
        var diagram = BuildParentChild();
        AddRelationship(diagram, RelationshipType.OneToMany, withPair: true);

        DbmlExporter.Build(diagram).Should().Contain("Ref: customers.id < orders.customer_id");
        DbmlExporter
            .DetectOmissions(diagram)
            .Should()
            .NotContain(ExportOmissionKind.RelationshipWithoutColumns);
    }

    /// <summary>取込結果のリレーション 1 本を「親.列 → 子.列」の文字列にして返す</summary>
    private static string DescribeSingleRelationship(ErDiagram diagram)
    {
        var relationship = diagram.Relationships.Should().ContainSingle().Subject;
        var parent = diagram.Entities.Single(entity => entity.Id == relationship.SourceEntityId);
        var child = diagram.Entities.Single(entity => entity.Id == relationship.TargetEntityId);
        var pairs = relationship.ColumnPairs.Select(pair =>
            $"{parent.TableName}.{parent.Columns.Single(c => c.Id == pair.SourceColumnId).Name}"
            + $" -> {child.TableName}.{child.Columns.Single(c => c.Id == pair.TargetColumnId).Name}"
        );

        return $"{relationship.Type}: {string.Join(", ", pairs)}";
    }

    [Fact(DisplayName = "DBML 取込: dbdiagram 形式のインライン参照 ref: > と primary key を読む")]
    public void Parse_InlineManyToOne_AndPrimaryKeySetting()
    {
        const string Dbml = """
            Table users {
              id integer [primary key]
            }

            Table posts {
              id integer [primary key]
              user_id integer [ref: > users.id]
            }
            """;

        var diagram = DbmlImporter.Parse(Dbml, "sqlserver");

        DescribeSingleRelationship(diagram).Should().Be("OneToMany: users.id -> posts.user_id");
        diagram
            .Entities.SelectMany(entity => entity.Columns)
            .Where(column => column.IsPrimaryKey)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(column => column.Name == "id" && !column.IsNullable);
        diagram
            .Entities.Single(entity => entity.TableName == "posts")
            .Columns.Single(column => column.Name == "user_id")
            .IsForeignKey.Should()
            .BeTrue();
    }

    [Fact(DisplayName = "DBML 取込: インライン参照 ref: < はこの列を親として読む")]
    public void Parse_InlineOneToMany_CurrentColumnIsParent()
    {
        const string Dbml = """
            Table users {
              id integer [pk, ref: < posts.user_id]
            }

            Table posts {
              user_id integer
            }
            """;

        DescribeSingleRelationship(DbmlImporter.Parse(Dbml, "sqlserver"))
            .Should()
            .Be("OneToMany: users.id -> posts.user_id");
    }

    /// <summary>
    /// インライン参照の 1 対 1 は ref を書いた列が外部キー（子）。Ref 行の 1 対 1（2 つ目が外部キー）とは逆になる。
    /// </summary>
    [Fact(
        DisplayName = "DBML 取込: インライン参照 ref: - は ref を書いた列を外部キー（子）として読む"
    )]
    public void Parse_InlineOneToOne_CurrentColumnIsChild()
    {
        const string Dbml = """
            Table users {
              id integer [pk]
            }

            Table user_infos {
              user_id integer [ref: - users.id]
            }
            """;

        DescribeSingleRelationship(DbmlImporter.Parse(Dbml, "sqlserver"))
            .Should()
            .Be("OneToOne: users.id -> user_infos.user_id");
    }

    [Fact(
        DisplayName = "DBML 取込: Ref 行の 1 対 1 は 2 つ目の列を外部キーとして読む（インラインと逆の対照）"
    )]
    public void Parse_RefLineOneToOne_SecondColumnIsChild()
    {
        const string Dbml = """
            Table users {
              id integer [pk]
            }

            Table user_infos {
              user_id integer
            }

            Ref: users.id - user_infos.user_id
            """;

        DescribeSingleRelationship(DbmlImporter.Parse(Dbml, "sqlserver"))
            .Should()
            .Be("OneToOne: users.id -> user_infos.user_id");
    }

    [Fact(DisplayName = "DBML 取込: インライン参照 ref: <> は多対多として読む")]
    public void Parse_InlineManyToMany()
    {
        const string Dbml = """
            Table students {
              id integer [pk, ref: <> courses.id]
            }

            Table courses {
              id integer [pk]
            }
            """;

        DescribeSingleRelationship(DbmlImporter.Parse(Dbml, "sqlserver"))
            .Should()
            .Be("ManyToMany: ");
    }

    [Fact(DisplayName = "DBML 取込: Ref 行の多対1（>）は左右を入れ替えた 1 対多として読む")]
    public void Parse_RefLineManyToOne_SwapsSides()
    {
        const string Dbml = """
            Table users {
              id integer [pk]
            }

            Table posts {
              user_id integer
            }

            Ref fk_posts_users: posts.user_id > users.id [delete: cascade]
            """;

        var diagram = DbmlImporter.Parse(Dbml, "sqlserver");

        DescribeSingleRelationship(diagram).Should().Be("OneToMany: users.id -> posts.user_id");
        diagram.Relationships[0].ConstraintName.Should().Be("fk_posts_users");
        diagram.Relationships[0].OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
    }

    [Fact(
        DisplayName = "DBML 取込: インライン参照は引用識別子と、後ろに定義されるテーブルへの参照を読む"
    )]
    public void Parse_InlineReference_QuotedAndForward()
    {
        const string Dbml = """
            Table "order lines" {
              "order id" integer [ref: > "sales orders"."order id"]
            }

            Table "sales orders" {
              "order id" integer [pk]
            }
            """;

        DescribeSingleRelationship(DbmlImporter.Parse(Dbml, "sqlserver"))
            .Should()
            .Be("OneToMany: sales orders.order id -> order lines.order id");
    }

    [Fact(DisplayName = "DBML 取込: リレーションは Ref 行とインライン参照の書かれた順に並ぶ")]
    public void Parse_InlineAndRefLines_KeepDocumentOrder()
    {
        const string Dbml = """
            Ref first: a.id < b.a_id

            Table a {
              id integer [pk]
            }

            Table b {
              a_id integer
              c_id integer [ref: > c.id]
            }

            Table c {
              id integer [pk]
            }

            Ref last: a.id < c.id
            """;

        var diagram = DbmlImporter.Parse(Dbml, "sqlserver");

        diagram
            .Relationships.Select(relationship => relationship.ConstraintName)
            .Should()
            .Equal("first", null, "last");
    }

    [Fact(DisplayName = "DBML 取込: 読めないインライン参照は項目と行を名指しして断る")]
    public void Parse_UnsupportedInlineReference_IsRejected()
    {
        const string Dbml = """
            Table users {
              id integer [pk]
            }

            Table posts {
              user_id integer [ref: > public.users.id]
            }
            """;

        var act = () => DbmlImporter.Parse(Dbml, "sqlserver");

        act.Should()
            .Throw<InvalidDataException>()
            .Which.Message.Should()
            .Contain("ref: > public.users.id")
            .And.Contain("6");
    }

    [Fact(DisplayName = "DBML 取込: 値なしの ref 設定は従来どおり読み飛ばす")]
    public void Parse_BareRefSetting_IsIgnored()
    {
        const string Dbml = """
            Table posts {
              user_id integer [ref]
            }
            """;

        var diagram = DbmlImporter.Parse(Dbml, "sqlserver");

        diagram.Relationships.Should().BeEmpty();
        diagram.Entities[0].Columns[0].IsForeignKey.Should().BeFalse();
    }
}
