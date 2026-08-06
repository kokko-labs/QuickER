using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SchemaSignature"/> の一意制約部分（UNIQUE の差が署名へ反映されること・並び順に依存しないこと）を検証するテストクラス
/// </summary>
public class SchemaSignatureTests
{
    /// <summary>2 列を持つエンティティを生成する（一意制約はテスト側で足す）</summary>
    private static Entity BuildEntity()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var region = new Column { Name = "Region", DataType = "nvarchar(10)" };

        return new Entity { TableName = "Shop", Columns = { code, region } };
    }

    /// <summary>エンティティ単体の署名を計算する</summary>
    private static string Sign(Entity entity) =>
        SchemaSignature.Compute([entity], Array.Empty<Relationship>());

    /// <summary>一意制約の有無で署名が変わることを検証する</summary>
    [Fact(DisplayName = "Compute: 一意制約の有無で署名が変わる")]
    public void Compute_UniqueConstraintPresence_ChangesSignature()
    {
        var without = BuildEntity();
        var with = BuildEntity();
        with.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Shop_Code", ColumnIds = [with.Columns[0].Id] }
        );

        Sign(with).Should().NotBe(Sign(without));
    }

    /// <summary>制約名だけが異なる場合も署名が変わることを検証する</summary>
    [Fact(DisplayName = "Compute: 制約名が違えば署名が変わる")]
    public void Compute_DifferentConstraintName_ChangesSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_A", ColumnIds = [a.Columns[0].Id] }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_B", ColumnIds = [b.Columns[0].Id] }
        );

        Sign(a).Should().NotBe(Sign(b));
    }

    /// <summary>構成列の宣言順が異なれば署名が変わることを検証する（DDL 出力が変わるため差分として扱う）</summary>
    [Fact(DisplayName = "Compute: 構成列の宣言順が違えば署名が変わる")]
    public void Compute_DifferentColumnOrder_ChangesSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [a.Columns[0].Id, a.Columns[1].Id] }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [b.Columns[1].Id, b.Columns[0].Id] }
        );

        Sign(a).Should().NotBe(Sign(b));
    }

    /// <summary>同じ一意制約なら（制約 ID・列 ID が別インスタンスでも）署名が一致することを検証する</summary>
    [Fact(DisplayName = "Compute: 同じ一意制約なら署名は不変")]
    public void Compute_SameUniqueConstraints_SameSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Shop_Code_Region",
                ColumnIds = [a.Columns[0].Id, a.Columns[1].Id],
            }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = "UQ_Shop_Code_Region",
                ColumnIds = [b.Columns[0].Id, b.Columns[1].Id],
            }
        );

        Sign(a).Should().Be(Sign(b));
    }

    /// <summary>制約リストの並び順が違っても署名が一致することを検証する（取込側の列挙順に左右されない）</summary>
    [Fact(DisplayName = "Compute: 制約リストの並び順が違っても署名は一致する")]
    public void Compute_ConstraintListOrder_DoesNotAffectSignature()
    {
        var a = BuildEntity();
        a.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_1", ColumnIds = [a.Columns[0].Id] }
        );
        a.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_2", ColumnIds = [a.Columns[1].Id] }
        );

        var b = BuildEntity();
        b.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_2", ColumnIds = [b.Columns[1].Id] }
        );
        b.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_1", ColumnIds = [b.Columns[0].Id] }
        );

        Sign(a).Should().Be(Sign(b));
    }
}
