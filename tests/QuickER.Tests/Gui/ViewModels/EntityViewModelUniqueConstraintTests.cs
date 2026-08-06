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

    // チェックボックス方式から行方式（列選択コンボボックス 1 行＝構成列 1 つ）へ変わったため、
    // 「全カラムのミラー＋参加状態」ではなく「構成列そのものの行と、その行ごとの選択候補」を固定する
    [Fact(DisplayName = "構成列の編集行は宣言順に並び、候補は他行の未使用列に絞られる")]
    public void Members_ReflectColumnIds_AndFilterCandidates()
    {
        var model = NewModelWithCompositeConstraint(out var code, out var kind);
        var entity = new EntityViewModel(model);
        var constraint = entity.UniqueConstraints[0];

        constraint.Members.Select(m => m.SelectedColumn!.Name).Should().Equal("Code", "Kind");

        // 各行の候補は「他行が使っていない列＋自行の現在選択」＝重複選択が構造的に起きない
        constraint.Members[0].AvailableColumns.Select(c => c.Name).Should().Equal("Id", "Code");
        constraint.Members[1].AvailableColumns.Select(c => c.Name).Should().Equal("Id", "Kind");

        // カラムを足すと候補だけが追随する（構成列は変わらない）
        entity.Columns.Add(new ColumnViewModel(new Column { Name = "Extra", DataType = "int" }));

        constraint
            .Members[0]
            .AvailableColumns.Select(c => c.Name)
            .Should()
            .Equal("Id", "Code", "Extra");
        constraint.ColumnIds.Should().Equal(code.Id, kind.Id);
        constraint.ContainsColumn(code.Id).Should().BeTrue();
    }

    [Fact(DisplayName = "全カラムが構成列なら未使用列が無いため行を足せない")]
    public void CanAddMember_IsFalse_WhenEveryColumnIsUsed()
    {
        var model = NewModelWithCompositeConstraint(out var code, out var kind);
        var entity = new EntityViewModel(model);
        var constraint = entity.UniqueConstraints[0];

        constraint.CanAddMember.Should().BeTrue("Id がまだ未使用");

        constraint.SetColumnIds([entity.Columns[0].Id, code.Id, kind.Id]);

        constraint.CanAddMember.Should().BeFalse();
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

        // 表示は行の SelectedColumn（ColumnViewModel 直参照）経由なのでリネームへ自動追従する
        constraint
            .Members.Select(member => member.SelectedColumn!.Name)
            .Should()
            .Equal("Code", "Kind");
        constraint.ResolvedName.Should().Be("UQ_Item_Code_Kind");

        entity.Columns.First(column => column.Id == code.Id).Name = "ItemCode";

        constraint
            .Members.Select(member => member.SelectedColumn!.Name)
            .Should()
            .Equal("ItemCode", "Kind");
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
