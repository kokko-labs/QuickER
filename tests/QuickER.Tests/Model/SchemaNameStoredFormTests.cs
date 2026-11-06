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
    // 列名・クエリ名・パラメータ名・射影のフィールド名が代入で前後の空白を除いた形になり綴りを変えないことは、名前の総当たり
    // （NameIdentityToolFuzzTests・NameIdentityQueryDialogFuzzTests ほかの保存形と重複の照合＝NameFuzz.FixedPairs の前後の空白つきの組と
    // 種の組）が入口越しに固定する（setter の除去を外すと赤になることを mutation で確かめた）

    /// <remarks>
    /// 残す理由（P3-6）：一意制約名・外部キー制約名の setter が前後の空白を除くこと（mutation で確かめたところ、名前の総当たりの制約名の
    /// 入口は手前でも空白を除くので、setter の除去を外しても赤にならない）と null の契約。元の表明をすべて残す
    /// </remarks>
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

    /// <remarks>
    /// 残す理由（P3-6）：結果の型名は名前の総当たりの名前の種類に無い（保存形と null）。クエリ名・パラメータ名・射影のフィールド名の保存形は
    /// 名前の総当たりのクエリ・パラメータ・射影のフィールドの保存形の照合へ移した
    /// </remarks>
    [Fact(DisplayName = "結果の型名は代入したとき前後の空白が除かれる（null は null のまま）")]
    public void QueryNames_AreStoredTrimmed()
    {
        new QueryDefinition { ResultTypeName = " Row " }
            .ResultTypeName.Should()
            .Be("Row");
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
