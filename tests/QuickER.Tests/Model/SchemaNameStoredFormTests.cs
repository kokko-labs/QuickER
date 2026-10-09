using System.Text.Json;
using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// 図に保存する名前（テーブル名・列名・制約名・クエリ名・パラメータ名・射影のフィールド名・結果の型名）が、モデルの setter で
/// 前後の空白を除いた形になることを確かめるテストクラス（綴りの大文字小文字は書き換えない）
/// </summary>
public sealed class SchemaNameStoredFormTests
{
    [Fact(DisplayName = "列名は代入したとき前後の空白が除かれ、綴りはそのまま残る")]
    public void ColumnName_IsStoredTrimmed()
    {
        new Column { Name = "  OrderId　" }
            .Name.Should()
            .Be("OrderId");
        new Column { Name = " Äpfel " }
            .Name.Should()
            .Be("Äpfel");
    }

    [Fact(
        DisplayName = "一意制約名・外部キー制約名は代入したとき前後の空白が除かれる（null は null のまま）"
    )]
    public void ConstraintNames_AreStoredTrimmed()
    {
        new UniqueConstraint { Name = " UQ_a " }
            .Name.Should()
            .Be("UQ_a");
        new UniqueConstraint { Name = null }
            .Name.Should()
            .BeNull();
        new Relationship { ConstraintName = "\tFK_a　" }
            .ConstraintName.Should()
            .Be("FK_a");
        new Relationship { ConstraintName = null }
            .ConstraintName.Should()
            .BeNull();
    }

    [Fact(
        DisplayName = "クエリ名・パラメータ名・射影のフィールド名・結果の型名は代入したとき前後の空白が除かれる"
    )]
    public void QueryNames_AreStoredTrimmed()
    {
        new QueryDefinition { Name = " GetAll ", ResultTypeName = " Row " }
            .Should()
            .BeEquivalentTo(new { Name = "GetAll", ResultTypeName = "Row" });
        new QueryParameter { Name = " id " }
            .Name.Should()
            .Be("id");
        new ProjectionField { Name = " total " }
            .Name.Should()
            .Be("total");
        new QueryDefinition { ResultTypeName = null }
            .ResultTypeName.Should()
            .BeNull();
    }

    [Fact(DisplayName = "図の JSON を読むと、空白つきの名前が除かれた形でモデルに入る")]
    public void LoadedJson_HasTrimmedNames()
    {
        const string json = """
            { "TableName": " T ", "Columns": [ { "Name": " C " } ] }
            """;

        var entity = JsonSerializer.Deserialize<Entity>(json)!;

        entity.TableName.Should().Be("T");
        entity.Columns.Single().Name.Should().Be("C");
    }

    [Fact(
        DisplayName = "名前の setter は null をそのまま通す（読込の必須名の検査が場所を名指しして断る）"
    )]
    public void ExplicitNull_PassesThrough()
    {
        var column = new Column { Name = null! };

        column.Name.Should().BeNull();
    }
}
