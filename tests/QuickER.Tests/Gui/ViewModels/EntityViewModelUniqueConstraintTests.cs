using AwesomeAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="EntityViewModel"/> が一意制約（UNIQUE）をモデルと往復させることを検証するテストクラス。
/// </summary>
/// <remarks>
/// GUI で図を開いて保存すると制約が消える（<c>ToModel</c> が一意制約をコピーしない）という
/// データ喪失を防ぐための回帰テスト。構成列は Guid 参照のため、往復で列 Guid の整合が崩れないことも固定する。
/// </remarks>
public class EntityViewModelUniqueConstraintTests
{
    /// <summary>Code / Kind の 2 列と、その両方を構成列に持つ複合一意制約を備えたモデルを作る</summary>
    private static Entity NewModelWithCompositeConstraint(
        out Column code,
        out Column kind,
        string? constraintName = "UQ_Item_Code_Kind"
    )
    {
        code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        kind = new Column { Name = "Kind", DataType = "int" };

        return new Entity
        {
            TableName = "Item",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                code,
                kind,
            },
            UniqueConstraints =
            {
                new UniqueConstraint { Name = constraintName, ColumnIds = { code.Id, kind.Id } },
            },
        };
    }

    [Fact(DisplayName = "モデル → VM → ToModel で一意制約（名前・構成列・Id）が保存される")]
    public void ToModel_RoundTrips_UniqueConstraints()
    {
        var model = NewModelWithCompositeConstraint(out var code, out var kind);
        var constraintId = model.UniqueConstraints[0].Id;

        var restored = new EntityViewModel(model).ToModel();

        restored.UniqueConstraints.Should().ContainSingle();
        restored.UniqueConstraints[0].Id.Should().Be(constraintId);
        restored.UniqueConstraints[0].Name.Should().Be("UQ_Item_Code_Kind");
        restored
            .UniqueConstraints[0]
            .ColumnIds.Should()
            .Equal([code.Id, kind.Id], "構成列は宣言順のまま列 Guid を保つべき");

        // 復元後の構成列 Guid が、同じ復元モデルの列 Guid を指していること（参照の自己整合）
        var restoredColumnIds = restored.Columns.Select(column => column.Id).ToList();
        restored.UniqueConstraints[0].ColumnIds.Should().BeSubsetOf(restoredColumnIds);
    }

    [Fact(DisplayName = "制約名なし（自動命名）は往復後も null のまま")]
    public void ToModel_KeepsNullConstraintName()
    {
        var model = NewModelWithCompositeConstraint(out _, out _, constraintName: null);

        var restored = new EntityViewModel(model).ToModel();

        restored.UniqueConstraints[0].Name.Should().BeNull();
    }

    [Fact(DisplayName = "空欄の制約名はモデルでは null（未設定）へ戻る")]
    public void ToModel_ConvertsBlankNameToNull()
    {
        var model = NewModelWithCompositeConstraint(out _, out _);
        var entity = new EntityViewModel(model);

        entity.UniqueConstraints[0].Name = "   ";

        entity.ToModel().UniqueConstraints[0].Name.Should().BeNull();
    }

    [Fact(DisplayName = "構成列に含まれる列は IsUniqueConstraintMember が立つ")]
    public void Columns_ReflectUniqueConstraintMembership()
    {
        var model = NewModelWithCompositeConstraint(out var code, out var kind);
        var entity = new EntityViewModel(model);

        entity
            .Columns.Where(column => column.IsUniqueConstraintMember)
            .Select(column => column.Id)
            .Should()
            .BeEquivalentTo(new[] { code.Id, kind.Id });
    }

    [Fact(DisplayName = "構成列候補はエンティティの全カラムを映し、参加状態を導出する")]
    public void ColumnCandidates_MirrorEntityColumns()
    {
        var model = NewModelWithCompositeConstraint(out var code, out _);
        var entity = new EntityViewModel(model);
        var constraint = entity.UniqueConstraints[0];

        constraint.ColumnCandidates.Select(c => c.Column.Name).Should().Equal("Id", "Code", "Kind");
        constraint
            .ColumnCandidates.Where(c => c.IsMember)
            .Select(c => c.Column.Name)
            .Should()
            .Equal("Code", "Kind");

        // カラムを足すと候補も追随する（チェックはされない）
        entity.Columns.Add(new ColumnViewModel(new Column { Name = "Extra", DataType = "int" }));

        constraint
            .ColumnCandidates.Select(c => c.Column.Name)
            .Should()
            .Equal("Id", "Code", "Kind", "Extra");
        constraint.ContainsColumn(code.Id).Should().BeTrue();
    }

    [Fact(DisplayName = "列リネームは Guid 参照のため制約の表示だけが追従する")]
    public void ColumnRename_UpdatesConstraintDisplayOnly()
    {
        var model = NewModelWithCompositeConstraint(
            out var code,
            out var kind,
            constraintName: null
        );
        var entity = new EntityViewModel(model);
        var constraint = entity.UniqueConstraints[0];

        constraint.ColumnSummary.Should().Be("Code, Kind");
        constraint.ResolvedName.Should().Be("UQ_Item_Code_Kind");

        entity.Columns.First(column => column.Id == code.Id).Name = "ItemCode";

        constraint.ColumnSummary.Should().Be("ItemCode, Kind");
        constraint.ResolvedName.Should().Be("UQ_Item_ItemCode_Kind");
        constraint.ColumnIds.Should().Equal([code.Id, kind.Id], "参照は Guid のまま");
    }

    [Fact(DisplayName = "テーブル名の変更は合成名プレビューへ追従する")]
    public void TableRename_UpdatesResolvedName()
    {
        var model = NewModelWithCompositeConstraint(out _, out _, constraintName: null);
        var entity = new EntityViewModel(model) { TableName = "Product" };

        entity.UniqueConstraints[0].ResolvedName.Should().Be("UQ_Product_Code_Kind");
    }

    [Fact(DisplayName = "制約名を指定すると合成名ではなくその名前が DDL 上の名前になる")]
    public void ExplicitName_WinsOverSynthesizedName()
    {
        var model = NewModelWithCompositeConstraint(out _, out _);

        new EntityViewModel(model)
            .UniqueConstraints[0]
            .ResolvedName.Should()
            .Be("UQ_Item_Code_Kind");
    }
}
