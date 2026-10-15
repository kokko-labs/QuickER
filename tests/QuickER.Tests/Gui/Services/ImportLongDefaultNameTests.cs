using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Services;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// DBML・Mermaid の取込が、名前の無い外部キーに付けて図へ保存する名前を、取込先の対象 DBMS の識別子の長さの上限に収めることを検証する
/// </summary>
/// <remarks>
/// 取込は同じテーブルの組に名前の無い外部キーが 2 本あると、2 本目へ一意な名前（<c>FK_{子}_{親}_{列}</c>）を付けて保存する
/// （<see cref="ConstraintNames.NameUnnamedForeignKeys"/>）。この名前は図へ保存するので、取込先の図の対象 DBMS の上限で縮める
/// （経路の正本は <see cref="ConstraintNames"/> の XmlDoc）。返す図の対象 DBMS も同じ値にする。
/// </remarks>
public class ImportLongDefaultNameTests
{
    private static readonly string Parent = new('p', 60);
    private static readonly string Child = new('c', 60);

    private static string Dbml =>
        $$"""
            Table {{Parent}} {
              id int [pk]
            }

            Table {{Child}} {
              id int [pk]
              bill_id int
              ship_id int
            }

            Ref: {{Child}}.bill_id > {{Parent}}.id
            Ref: {{Child}}.ship_id > {{Parent}}.id
            """;

    [Fact(
        DisplayName = "DBML: 2 本目の外部キーに付ける名前は、取込先の対象 DBMS（PostgreSQL の 63 バイト）に収める"
    )]
    public void Dbml_UniquifiedNameFitsTargetDbms()
    {
        var diagram = DbmlImporter.Parse(Dbml, "postgresql");

        diagram.TargetDbms.Should().Be("postgresql");
        diagram
            .Relationships[0]
            .ConstraintName.Should()
            .BeNull("既定名のままの 1 本目は名前を持たない");
        var second = diagram.Relationships[1].ConstraintName!;
        IdentifierLengthLimits.PostgreSql.Fits(second).Should().BeTrue(second);
        second
            .Should()
            .Be(
                ConstraintNames.ResolveForeignKeyNames(
                    diagram.Entities,
                    diagram.Relationships,
                    IdentifierLengthLimits.PostgreSql
                )[diagram.Relationships[1]],
                "DDL が付ける名前と同じ"
            );
    }

    [Fact(DisplayName = "DBML: 上限の無い対象 DBMS（SQLite）では縮めない")]
    public void Dbml_UnlimitedTarget_KeepsFullName()
    {
        var diagram = DbmlImporter.Parse(Dbml, "sqlite");

        diagram.Relationships[1].ConstraintName.Should().Be($"FK_{Child}_{Parent}_ship_id");
    }

    [Fact(
        DisplayName = "Mermaid: 返す図の対象 DBMS は取込先の対象 DBMS（名前を付けるときの上限もこれ）"
    )]
    public void Mermaid_CarriesTargetDbms()
    {
        var text = $$"""
            erDiagram
                {{Parent}} {
                    int id PK
                }
            """;

        MermaidImporter.Parse(text, "postgresql").TargetDbms.Should().Be("postgresql");
    }
}
