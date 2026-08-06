using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// <see cref="Entity.Clone"/> の複製規則（ID 維持 / 新規採番と、一意制約の <see cref="UniqueConstraint.ColumnIds"/> 再マップ）を検証するテストクラス
/// </summary>
public class EntityCloneTests
{
    /// <summary>2 列＋その 2 列を構成列とする一意制約 1 件を持つエンティティを組み立てる</summary>
    private static Entity BuildEntity()
    {
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var region = new Column { Name = "Region", DataType = "nvarchar(10)" };

        var entity = new Entity { TableName = "Shop", Columns = { code, region } };

        entity.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Shop_Code_Region", ColumnIds = [code.Id, region.Id] }
        );

        return entity;
    }

    /// <summary>preserveId=true では ID を維持したまま制約もそのまま複製されることを検証する</summary>
    [Fact(DisplayName = "Clone(preserveId: true): 一意制約は ID・構成列 ID ごとそのまま複製される")]
    public void Clone_PreserveId_KeepsConstraintIdsAsIs()
    {
        var source = BuildEntity();

        var clone = source.Clone(preserveId: true);

        clone.Id.Should().Be(source.Id);
        clone.Columns.Select(c => c.Id).Should().Equal(source.Columns.Select(c => c.Id));

        var constraint = clone.UniqueConstraints.Should().ContainSingle().Which;
        constraint.Id.Should().Be(source.UniqueConstraints[0].Id);
        constraint.Name.Should().Be("UQ_Shop_Code_Region");
        constraint.ColumnIds.Should().Equal(source.Columns[0].Id, source.Columns[1].Id);

        // 複製はリスト実体を共有しない（片方の編集がもう片方へ波及しない）
        clone.UniqueConstraints.Should().NotBeSameAs(source.UniqueConstraints);
        constraint.ColumnIds.Should().NotBeSameAs(source.UniqueConstraints[0].ColumnIds);
    }

    /// <summary>preserveId=false では制約 ID が新規化され、構成列 ID が複製後のカラム ID へ張り替わることを検証する</summary>
    [Fact(
        DisplayName = "Clone(preserveId: false): 一意制約の ColumnIds が複製後のカラム ID へ再マップされる"
    )]
    public void Clone_NewId_RemapsConstraintColumnIds()
    {
        var source = BuildEntity();

        var clone = source.Clone(preserveId: false);

        clone.Id.Should().NotBe(source.Id);
        clone.Columns.Select(c => c.Id).Should().NotIntersectWith(source.Columns.Select(c => c.Id));

        var constraint = clone.UniqueConstraints.Should().ContainSingle().Which;
        constraint.Id.Should().NotBe(source.UniqueConstraints[0].Id);
        constraint.Name.Should().Be("UQ_Shop_Code_Region");
        // 元のカラム ID ではなく複製側のカラム ID を指す（宣言順も維持する）
        constraint.ColumnIds.Should().Equal(clone.Columns[0].Id, clone.Columns[1].Id);
    }

    /// <summary>エンティティに属さないカラム ID を含む制約は、再マップ対象が無いためそのまま維持されることを検証する</summary>
    [Fact(DisplayName = "Clone(preserveId: false): 対応表に無いカラム ID はそのまま維持される")]
    public void Clone_NewId_KeepsUnknownColumnIds()
    {
        var orphan = Guid.NewGuid();
        var entity = new Entity { TableName = "T" };
        entity.UniqueConstraints.Add(new UniqueConstraint { ColumnIds = [orphan] });

        var clone = entity.Clone(preserveId: false);

        clone.UniqueConstraints.Should().ContainSingle().Which.ColumnIds.Should().Equal(orphan);
    }

    /// <summary>一意制約を持たないエンティティの複製が空リストのままであることを検証する</summary>
    [Fact(DisplayName = "Clone: 一意制約なしのエンティティは空リストのまま複製される")]
    public void Clone_WithoutConstraints_StaysEmpty()
    {
        var entity = new Entity
        {
            TableName = "T",
            Columns =
            {
                new Column { Name = "Id", DataType = "int" },
            },
        };

        entity.Clone(preserveId: true).UniqueConstraints.Should().BeEmpty();
        entity.Clone(preserveId: false).UniqueConstraints.Should().BeEmpty();
    }
}
