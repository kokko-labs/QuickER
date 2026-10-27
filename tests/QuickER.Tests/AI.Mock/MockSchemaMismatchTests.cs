using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 今の ER 図とモックの設計（元のスキーマ・画面が宣言したテーブル）の食い違いの判定（<see cref="MockSchemaMismatch"/>）と、
/// スキーマの書き出しをテーブルごとに分ける関数（<see cref="MockSchemaSerializer.Split"/>）を検証するテストクラス。
/// </summary>
public class MockSchemaMismatchTests
{
    /// <summary>列 1 つのテーブルを作る</summary>
    private static Entity Table(string name, string column = "id", string type = "int") =>
        new()
        {
            Id = Guid.NewGuid(),
            TableName = name,
            Columns =
            [
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = column,
                    DataType = type,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };

    private static ErDiagram Diagram(params Entity[] tables) =>
        new() { TargetDbms = "sqlserver", Entities = [.. tables] };

    /// <summary>設計（元のスキーマと、テーブルを宣言した画面）を作る</summary>
    private static MockManifest Design(
        ErDiagram source,
        params (string Screen, string Table)[] declarations
    ) =>
        new()
        {
            SourceSchema = source is null ? string.Empty : MockSchemaSerializer.Serialize(source),
            Screens =
            [
                .. declarations
                    .GroupBy(d => d.Screen)
                    .Select(group => new MockScreen
                    {
                        File = group.Key,
                        Name = group.Key,
                        Entities =
                        [
                            .. group.Select(d => new MockScreenEntity
                            {
                                Name = d.Table,
                                Operations = "R",
                            }),
                        ],
                    }),
            ],
        };

    /// <summary>設計と同じ図では食い違いが無いことを検証する</summary>
    [Fact(DisplayName = "設計と同じ図では食い違いが無い")]
    public void Compare_SameSchema_NoMismatch()
    {
        var diagram = Diagram(Table("Customer"), Table("Order"));
        var mismatch = MockSchemaMismatch.Compare(
            diagram,
            Design(diagram, ("CustomerList.html", "Customer"))
        );

        mismatch.RequiresConfirmation.Should().BeFalse();
        mismatch.HasContentDifferences.Should().BeFalse();
        mismatch.SourceSchemaRecorded.Should().BeTrue();
    }

    /// <summary>
    /// テーブルの有無と、画面が宣言しているのに図に無いテーブルは確認の対象で、内訳がテーブル名と画面を名指しすることを検証する
    /// </summary>
    [Fact(DisplayName = "テーブルの増減と、図に無い画面のテーブルは確認の対象")]
    public void Compare_TablesAddedOrRemoved_RequiresConfirmation()
    {
        var design = Diagram(Table("Product"), Table("Category"));
        var now = Diagram(Table("Customer"), Table("Category"));
        var mismatch = MockSchemaMismatch.Compare(
            now,
            Design(design, ("ProductList.html", "Product"), ("ProductList.html", "Category"))
        );

        mismatch.RequiresConfirmation.Should().BeTrue();
        mismatch.TablesOnlyInDesign.Should().Equal("Product");
        mismatch.TablesOnlyInDiagram.Should().Equal("Customer");
        mismatch.ScreenTablesNotInDiagram.Should().Equal(("ProductList.html", "Product"));
        mismatch.ChangedTables.Should().BeEmpty();

        var details = mismatch.Describe();
        details
            .Should()
            .Contain(
                string.Format(MockStrings.Mock_SchemaMismatch_TablesOnlyInDesignFormat, "Product")
            );
        details
            .Should()
            .Contain(
                string.Format(MockStrings.Mock_SchemaMismatch_TablesOnlyInDiagramFormat, "Customer")
            );
        details
            .Should()
            .Contain(
                string.Format(
                    MockStrings.Mock_SchemaMismatch_ScreenTableFormat,
                    "ProductList.html",
                    "Product"
                )
            );
    }

    /// <summary>
    /// 列・型・リレーション・対象 DBMS だけの違いは確認の対象にせず、内訳には載ることを検証する
    /// </summary>
    [Fact(DisplayName = "内容・リレーション・対象 DBMS だけの違いは確認の対象にしない")]
    public void Compare_ContentOnly_DoesNotRequireConfirmation()
    {
        var designCustomer = Table("Customer");
        var designOrder = Table("Order");
        var design = Diagram(designCustomer, designOrder);
        design.Relationships.Add(
            new Relationship
            {
                Id = Guid.NewGuid(),
                SourceEntityId = designCustomer.Id,
                TargetEntityId = designOrder.Id,
                Type = RelationshipType.OneToMany,
            }
        );
        var now = Diagram(Table("Customer", type: "bigint"), Table("Order"));
        now.TargetDbms = "sqlite";

        var mismatch = MockSchemaMismatch.Compare(
            now,
            Design(design, ("CustomerList.html", "customer"))
        );

        mismatch.RequiresConfirmation.Should().BeFalse();
        mismatch.HasContentDifferences.Should().BeTrue();
        mismatch.ChangedTables.Should().Equal("Customer");
        mismatch.RelationshipsChanged.Should().BeTrue();
        mismatch.TargetChanged.Should().BeTrue();
        // 画面の宣言は SchemaNames の規則（大文字小文字を区別しない）で照らす
        mismatch.ScreenTablesNotInDiagram.Should().BeEmpty();
        mismatch
            .Describe()
            .Should()
            .Contain(
                string.Format(MockStrings.Mock_SchemaMismatch_ChangedTablesFormat, "Customer")
            );
    }

    /// <summary>
    /// 元のスキーマが記録されていない古い設計では、スキーマを比べず、画面の宣言だけを照らすことを検証する
    /// </summary>
    [Theory(DisplayName = "元のスキーマが無い設計は、画面の宣言だけを照らす")]
    [InlineData("Customer", false)]
    [InlineData("Product", true)]
    public void Compare_NoSourceSchema_ChecksDeclarationsOnly(string declared, bool expected)
    {
        var now = Diagram(Table("Customer"));
        var manifest = Design(null!, ("Screen.html", declared));

        var mismatch = MockSchemaMismatch.Compare(now, manifest);

        mismatch.SourceSchemaRecorded.Should().BeFalse();
        mismatch.RequiresConfirmation.Should().Be(expected);
        mismatch.TablesOnlyInDesign.Should().BeEmpty();
        mismatch.TablesOnlyInDiagram.Should().BeEmpty();
        mismatch.HasContentDifferences.Should().BeFalse();
    }

    /// <summary>
    /// スキーマの書き出しをテーブルごとの節に分けると、表示名つきの見出しからもテーブル名が取れ、
    /// 見出し・節・リレーションに書き出しの全行が漏れなく入ることを検証する
    /// </summary>
    [Fact(DisplayName = "スキーマの書き出しはテーブルごとの節に漏れなく分かれる")]
    public void Split_CoversEveryLine()
    {
        var customer = Table("Customer");
        customer.Description = "顧客";
        var order = Table("Order");
        var diagram = Diagram(customer, order);
        diagram.Relationships.Add(
            new Relationship
            {
                Id = Guid.NewGuid(),
                SourceEntityId = customer.Id,
                TargetEntityId = order.Id,
                Type = RelationshipType.OneToMany,
            }
        );
        var text = MockSchemaSerializer.Serialize(diagram);

        var sections = MockSchemaSerializer.Split(text);

        sections.Tables.Select(t => t.Key).Should().Equal("Customer", "Order");
        sections.Tables[0].Value.Should().StartWith("### Customer (顧客)");
        sections.Header.Should().Contain("Target DBMS: sqlserver");
        sections.Relationships.Should().Contain("Customer → Order");

        // 見出しの「## Tables」「## Relationships」以外の空でない行は、どれかの節に入る
        var parts = string.Join(
            "\n",
            [sections.Header, .. sections.Tables.Select(t => t.Value), sections.Relationships]
        );

        foreach (
            var line in text.ReplaceLineEndings("\n")
                .Split('\n')
                .Where(line =>
                    line.Length > 0 && line is not "## Tables" and not "## Relationships"
                )
        )
        {
            parts.Should().Contain(line);
        }
    }
}
