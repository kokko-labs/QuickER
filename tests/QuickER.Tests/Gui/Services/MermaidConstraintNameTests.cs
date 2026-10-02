using System;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider.SqlServer;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Mermaid 取込（<see cref="MermaidImporter"/>）がリレーション行のラベルから制約名を決める規則を検証する
/// </summary>
/// <remarks>
/// Mermaid のラベルは構文上必須で、出力側は名前の無いリレーションに <c>relates</c> を書き、他のツールの図は
/// <c>has</c> のような動詞を繰り返す。ラベルをそのまま制約名にすると同じ名前の制約が並び、DDL の 2 本目が失敗する。
/// 取り込んだ図では、DDL が付ける制約名（外部キー・主キー・一意制約）が大文字小文字を無視して重複しないことを、
/// 実際に生成した SQL Server の DDL で確かめる。
/// </remarks>
public class MermaidConstraintNameTests
{
    /// <summary>CUSTOMER・ORDER・INVOICE の 3 テーブル（子は customer_id を持つ）と、与えたリレーション行の Mermaid</summary>
    private static string BuildText(params string[] relationshipLines) =>
        string.Join(
            "\n",
            [
                "erDiagram",
                "    CUSTOMER {",
                "        int customer_id PK",
                "    }",
                "    ORDER {",
                "        int order_id PK",
                "        int customer_id FK",
                "        int bill_to_customer_id FK",
                "    }",
                "    INVOICE {",
                "        int invoice_id PK",
                "        int customer_id FK",
                "        int order_id FK",
                "    }",
                .. relationshipLines,
            ]
        );

    /// <summary>リレーションを親子のテーブル名と制約名の組で返す（ファイルの順）</summary>
    private static (string Parent, string Child, string? Name)[] Describe(ErDiagram diagram) =>
        diagram
            .Relationships.Select(relationship =>
                (
                    diagram
                        .Entities.Single(entity => entity.Id == relationship.SourceEntityId)
                        .TableName,
                    diagram
                        .Entities.Single(entity => entity.Id == relationship.TargetEntityId)
                        .TableName,
                    relationship.ConstraintName
                )
            )
            .ToArray();

    /// <summary>SQL Server の DDL が付ける制約名がすべて異なる（大文字小文字を無視）ことを表明する</summary>
    private static void ShouldHaveDistinctDdlConstraintNames(ErDiagram diagram)
    {
        var ddl = new SqlServerDdlGenerator().Build(diagram);
        var names = Regex
            .Matches(ddl, @"CONSTRAINT \[([^\]]+)\]")
            .Select(match => match.Groups[1].Value)
            .ToList();

        names.Should().NotBeEmpty();
        names
            .Should()
            .OnlyHaveUniqueItems(
                name => name.ToUpperInvariant(),
                "DDL の制約名は重複してはいけない"
            );
    }

    [Fact(
        DisplayName = "名前の無いリレーションは relates を経由して往復しても名前を持たず、制約名も重複しない"
    )]
    public void RoundTrip_UnnamedRelationships_StayUnnamed()
    {
        var diagram = MermaidImporter.Parse(
            BuildText("    CUSTOMER ||--o{ ORDER : named", "    CUSTOMER ||--o{ INVOICE : named2")
        );

        foreach (var relationship in diagram.Relationships)
        {
            relationship.ConstraintName = null;
        }

        var text = MermaidExporter.Build(diagram);
        text.Should().Contain($": {MermaidExporter.UnnamedRelationshipLabel}");

        var imported = MermaidImporter.Parse(text);

        Describe(imported)
            .Should()
            .Equal(("CUSTOMER", "ORDER", null), ("CUSTOMER", "INVOICE", null));
        ShouldHaveDistinctDdlConstraintNames(imported);
    }

    [Fact(DisplayName = "同じラベルが複数のリレーションに付いていれば、どれも制約名に採らない")]
    public void Import_RepeatedLabel_IsNotAdopted()
    {
        var diagram = MermaidImporter.Parse(
            BuildText(
                "    CUSTOMER ||--o{ ORDER : has",
                "    CUSTOMER ||--o{ INVOICE : HAS",
                "    ORDER ||--o{ INVOICE : has"
            )
        );

        Describe(diagram)
            .Should()
            .Equal(
                ("CUSTOMER", "ORDER", null),
                ("CUSTOMER", "INVOICE", null),
                ("ORDER", "INVOICE", null)
            );
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Theory(
        DisplayName = "同じ親子の組に名前の無いリレーションが 2 本残れば、2 本目に衝突しない名前を付ける"
    )]
    [InlineData("relates", "relates")]
    [InlineData("has", "has")]
    [InlineData("relates", "\"bills to\"")]
    public void Import_SamePairTwiceWithoutNames_SecondGetsUniqueName(
        string firstLabel,
        string secondLabel
    )
    {
        var diagram = MermaidImporter.Parse(
            BuildText(
                $"    CUSTOMER ||--o{{ ORDER : {firstLabel}",
                $"    CUSTOMER ||--o{{ ORDER : {secondLabel}"
            )
        );

        Describe(diagram)
            .Should()
            .Equal(
                ("CUSTOMER", "ORDER", null),
                ("CUSTOMER", "ORDER", "FK_ORDER_CUSTOMER_bill_to_customer_id")
            );
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Fact(DisplayName = "主キー・一意制約の名前と衝突するラベルは制約名に採らない")]
    public void Import_LabelCollidingWithTableConstraint_IsNotAdopted()
    {
        var diagram = MermaidImporter.Parse(BuildText("    CUSTOMER ||--o{ ORDER : pk_customer"));

        Describe(diagram).Should().Equal(("CUSTOMER", "ORDER", null));
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Theory(DisplayName = "引用つきや複数語のラベルは動詞句として扱い、制約名に採らない")]
    [InlineData("\"places\"")]
    [InlineData("\"places order\"")]
    [InlineData("places order")]
    public void Import_PhraseLabel_IsNotAdopted(string label)
    {
        var diagram = MermaidImporter.Parse(BuildText($"    CUSTOMER ||--o{{ ORDER : {label}"));

        Describe(diagram).Should().Equal(("CUSTOMER", "ORDER", null));
    }

    [Fact(
        DisplayName = "採ったラベルが名前の無いリレーションの既定名と同じなら、名前の無いほうに衝突しない名前を付ける"
    )]
    public void Import_AdoptedLabelTakesDefaultName_UnnamedGetsUniqueName()
    {
        // 名前の無いリレーションがファイルの先にあっても、採ったラベルを先に予約する
        var diagram = MermaidImporter.Parse(
            BuildText(
                "    CUSTOMER ||--o{ INVOICE : relates",
                "    ORDER ||--o{ INVOICE : FK_INVOICE_CUSTOMER"
            )
        );

        Describe(diagram)
            .Should()
            .Equal(
                ("CUSTOMER", "INVOICE", "FK_INVOICE_CUSTOMER_customer_id"),
                ("ORDER", "INVOICE", "FK_INVOICE_CUSTOMER")
            );
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    /// <summary>
    /// 一意かどうかは、プレースホルダ・引用つき・複数語のラベルを除いた後に数えることを固定する
    /// （除いたラベルは名前にならないので、同じ綴りでも衝突しない）
    /// </summary>
    [Fact(DisplayName = "一意かどうかは名前の候補になるラベルだけで数える")]
    public void Import_UniquenessIsCountedAmongCandidatesOnly()
    {
        var diagram = MermaidImporter.Parse(
            BuildText("    CUSTOMER ||--o{ ORDER : has", "    CUSTOMER ||--o{ INVOICE : \"has\"")
        );

        Describe(diagram)
            .Should()
            .Equal(("CUSTOMER", "ORDER", "has"), ("CUSTOMER", "INVOICE", null));
        ShouldHaveDistinctDdlConstraintNames(diagram);
    }

    [Fact(DisplayName = "ラベル relates は大文字小文字を問わず制約名に採らない")]
    public void Import_PlaceholderLabel_IsNotAdoptedRegardlessOfCase()
    {
        var diagram = MermaidImporter.Parse(BuildText("    CUSTOMER ||--o{ ORDER : Relates"));

        Describe(diagram).Should().Equal(("CUSTOMER", "ORDER", null));
    }
}
