using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 名前に <c>.</c> を含むテーブルを取込が外したことの表明（5 方言の <c>*ImportFidelityIntegrationTests</c> が共有する）
/// </summary>
/// <remarks>
/// どの方言も同じ形のテーブルを作る＝名前に <c>.</c> を含む <c>Order.Lines</c>（主キー・一意制約・説明・自分の外部キーを持つ）と、
/// 名前の <c>.</c> の後ろと同じ名前の <c>Lines</c>、<c>Order.Lines</c> を参照する外部キー <c>fk_child_dotted</c> を持つ <c>child</c>。
/// 外した <c>Order.Lines</c> の列・主キー・一意制約・説明・外部キーが、<c>Lines</c> にも <c>child</c> にも混ざらないことまで確かめる。
/// </remarks>
internal static class DottedTableNameImportAssertions
{
    /// <summary>名前に <c>.</c> を含むテーブル</summary>
    public const string DottedTable = "Order.Lines";

    /// <summary>
    /// <see cref="DottedTable"/> を外して名指しし、それを参照する外部キーも名指しし、ほかのテーブルに何も混ざらないことを表明する
    /// </summary>
    /// <param name="result">取込の結果</param>
    /// <param name="schemaSubject">外したテーブルの警告に添えるスキーマ名（SQL Server だけ。ほかは空）</param>
    /// <param name="foreignKeyName">外部キーの名前（名前を持たない SQLite は、取込が識別に付ける名前）</param>
    public static void ShouldExcludeDottedTable(
        SchemaImportResult result,
        string schemaSubject,
        string foreignKeyName = "fk_child_dotted"
    )
    {
        result
            .Entities.Select(e => e.TableName)
            .Should()
            .BeEquivalentTo(["Lines", "child"], "名前に . を含むテーブルは取り込まない");
        result
            .Entities.Should()
            .NotContain(
                e => SchemaQualifiedName.IsQualified(e.TableName),
                "取り込んだテーブル名は、最初の . で別のテーブルに読まれる名前を含まない"
            );

        var excluded = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.DottedTableNameExcluded)
            .Subject;
        excluded.TableName.Should().Be(DottedTable);
        excluded.Subject.Should().Be(schemaSubject);
        excluded.Detail.Should().BeEmpty();

        var foreignKey = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.ForeignKeyToExcludedTable)
            .Subject;
        foreignKey.TableName.Should().Be("child");
        foreignKey.Subject.Should().Be(foreignKeyName);
        foreignKey.Detail.Should().Be(DottedTable);

        result
            .Warnings.Should()
            .NotContain(w =>
                w.Kind == SchemaImportWarningKind.TableNameCollision
                || w.Kind == SchemaImportWarningKind.ForeignKeyOutsideScope
            );
        result
            .Relationships.Should()
            .BeEmpty("外したテーブルの外部キーも、それを参照する外部キーもリレーションにしない");

        var lines = result.Entities.Single(e => e.TableName == "Lines");
        lines
            .Columns.Select(c => c.Name)
            .Should()
            .Equal(["id", "qty"], "外したテーブルの列が混ざってはいけない");
        lines.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal(["id"]);
        lines.UniqueConstraints.Should().BeEmpty("外したテーブルの一意制約が混ざってはいけない");
        lines.Description.Should().BeNullOrEmpty("外したテーブルの説明が混ざってはいけない");

        var child = result.Entities.Single(e => e.TableName == "child");
        child.Columns.Select(c => c.Name).Should().Equal(["id", "dotted_id"]);
        child.UniqueConstraints.Should().BeEmpty();
    }
}
