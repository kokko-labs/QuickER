using AwesomeAssertions;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 大文字小文字（と前後の空白）だけ違う列名の衝突で捨てた列の一意制約・外部キーが、採用した列へ付け替わらないことの表明
/// （<c>*ImportFidelityIntegrationTests</c> が共有する）
/// </summary>
/// <remarks>
/// どの方言も同じ形のテーブルを作る＝親 <c>p</c>（主キー <c>id</c>・採用する列・捨てる列〔一意制約つき〕）と、
/// 子 <c>t</c>（主キー <c>id</c>・採用する列・捨てる列〔一意制約つき・<c>p.id</c> への外部キー〕・捨てる親の列への外部キーを持つ <c>r</c>）。
/// 捨てた列の一意制約と外部キーは、どのテーブルにもリレーションにも残らない（捨てた列は
/// <see cref="SchemaImportWarningKind.ColumnNameCollision"/> が名指しする）。
/// </remarks>
internal static class CollidedColumnImportAssertions
{
    /// <summary>捨てた列の一意制約・外部キーが、採用した列にもリレーションにも残らないことを表明する</summary>
    /// <param name="result">取込の結果</param>
    /// <param name="parent">親のテーブル名</param>
    /// <param name="child">子のテーブル名</param>
    /// <param name="id">主キーの列名</param>
    /// <param name="kept">採用する列の名前（先に並ぶ）</param>
    /// <param name="dropped">捨てる列の名前（後に並ぶ）</param>
    /// <param name="other">捨てる親の列を参照する子の列の名前</param>
    public static void ShouldNotMoveConstraintsOfDroppedColumns(
        SchemaImportResult result,
        string parent,
        string child,
        string id,
        string kept,
        string dropped,
        string other
    )
    {
        var parentEntity = result.Entities.Single(e => e.TableName == parent);
        var childEntity = result.Entities.Single(e => e.TableName == child);

        parentEntity.Columns.Select(c => c.Name).Should().Equal(id, kept);
        childEntity.Columns.Select(c => c.Name).Should().Equal(id, kept, other);

        parentEntity
            .UniqueConstraints.Should()
            .BeEmpty("捨てた列の一意制約が、採用した列へ付け替わってはいけない");
        childEntity
            .UniqueConstraints.Should()
            .BeEmpty("捨てた列の一意制約が、採用した列へ付け替わってはいけない");
        childEntity
            .Columns.Single(c => c.Name == kept)
            .IsForeignKey.Should()
            .BeFalse("捨てた列の外部キーの印が、採用した列へ付いてはいけない");
        childEntity.Columns.Single(c => c.Name == other).IsForeignKey.Should().BeFalse();
        result
            .Relationships.Should()
            .BeEmpty(
                "捨てた子の列の外部キーも、捨てた親の列を参照する外部キーも、採用した列のリレーションにしない"
            );

        result
            .Warnings.Select(w => (w.Kind, w.TableName, w.Subject, w.Detail))
            .Should()
            .BeEquivalentTo([
                (SchemaImportWarningKind.ColumnNameCollision, parent, dropped, kept),
                (SchemaImportWarningKind.ColumnNameCollision, child, dropped, kept),
            ]);
    }
}
