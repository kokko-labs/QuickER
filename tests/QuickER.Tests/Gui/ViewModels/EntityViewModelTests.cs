using System.ComponentModel;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary><see cref="EntityViewModel"/> の表示状態と表示高さの連動を検証するテストクラス</summary>
public class EntityViewModelTests
{
    /// <summary>PK1 + FK1 + 一般3 のカラムを持つテスト用エンティティを生成する</summary>
    private static EntityViewModel NewEntityWithMixedColumns() =>
        new(
            new Entity
            {
                TableName = "Orders",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "CustomerId",
                        DataType = "int",
                        IsForeignKey = true,
                    },
                    new Column { Name = "Note1", DataType = "nvarchar(50)" },
                    new Column { Name = "Note2", DataType = "nvarchar(50)" },
                    new Column { Name = "Note3", DataType = "nvarchar(50)" },
                },
            },
            new EntityLayout { Width = 220 }
        );

    /// <summary>簡易表示への切替で DisplayHeight が縮み PropertyChanged が発火することを検証する</summary>
    [Fact(DisplayName = "IsCompactView 切替で DisplayHeight が変わり PropertyChanged が発火する")]
    public void IsCompactView_Toggle_ChangesDisplayHeightAndNotifies()
    {
        var entity = NewEntityWithMixedColumns();
        var fullHeight = entity.DisplayHeight;

        var notified = false;

        entity.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EntityViewModel.DisplayHeight))
            {
                notified = true;
            }
        };

        entity.IsCompactView = true;

        notified.Should().BeTrue();
        entity.DisplayHeight.Should().BeLessThan(fullHeight);
    }

    /// <summary>PK/FK のみのエンティティは簡易表示でも DisplayHeight が変わらないことを検証する</summary>
    [Fact(DisplayName = "PK/FK のみのエンティティは簡易表示で DisplayHeight が不変")]
    public void IsCompactView_KeyOnlyEntity_KeepsDisplayHeight()
    {
        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "OrderItems",
                Columns =
                {
                    new Column
                    {
                        Name = "OrderId",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "ProductId",
                        DataType = "int",
                        IsForeignKey = true,
                    },
                },
            },
            new EntityLayout { Width = 220 }
        );
        var fullHeight = entity.DisplayHeight;

        entity.IsCompactView = true;

        entity.DisplayHeight.Should().Be(fullHeight);
    }

    /// <summary>主キーの順序が ViewModel 往復（意味モデル → VM → 意味モデル）で保全されることを検証する</summary>
    /// <remarks>
    /// 編集 UI を持たないパススルーのため、読込値がそのまま書き戻らないと
    /// GUI で開いて保存しただけで複合主キーの並びが黙って失われる。
    /// </remarks>
    [Fact(DisplayName = "ToModel: 主キーの順序が ViewModel 往復で保全される")]
    public void ToModel_PreservesPrimaryKeyColumnIds()
    {
        var a = new Column
        {
            Name = "a",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var b = new Column
        {
            Name = "b",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var model = new Entity
        {
            TableName = "Pair",
            Columns = { a, b },
            // 列宣言順（a → b）とは逆の主キー順
            PrimaryKeyColumnIds = [b.Id, a.Id],
        };

        var roundTripped = new EntityViewModel(model).ToModel();

        roundTripped.PrimaryKeyColumnIds.Should().Equal(b.Id, a.Id);
        roundTripped.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).Should().Equal("b", "a");
    }

    /// <summary>主キー順表示のテスト用に「a, b の 2 列とも主キー」のエンティティを生成する</summary>
    /// <param name="reversePrimaryKeyOrder">true なら実効順を列宣言順の逆（b → a）へ上書きする</param>
    private static EntityViewModel NewCompositeKeyEntity(bool reversePrimaryKeyOrder = true)
    {
        var a = new Column
        {
            Name = "a",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var b = new Column
        {
            Name = "b",
            DataType = "int",
            IsPrimaryKey = true,
        };
        var model = new Entity { TableName = "Pair", Columns = { a, b } };

        if (reversePrimaryKeyOrder)
        {
            model.PrimaryKeyColumnIds = [b.Id, a.Id];
        }

        return new EntityViewModel(model, new EntityLayout { Width = 220 });
    }

    /// <summary>複合主キーの表示テキストが実効順になることを検証する</summary>
    [Fact(DisplayName = "主キー順の表示テキストは実効順の列名を並べる")]
    public void PrimaryKeyOrderText_UsesEffectiveOrder()
    {
        var entity = NewCompositeKeyEntity();

        entity.IsCompositePrimaryKey.Should().BeTrue();
        entity.PrimaryKeyOrderText.Should().Be("b, a");
    }

    /// <summary>単一主キーでは複合フラグが立たない（＝表示されない）ことを検証する</summary>
    [Fact(DisplayName = "単一主キーでは複合主キーフラグが立たない")]
    public void IsCompositePrimaryKey_SinglePrimaryKey_IsFalse()
    {
        NewEntityWithMixedColumns().IsCompositePrimaryKey.Should().BeFalse();
    }

    /// <summary>主キーの切替・列のリネーム・列の並び替えで主キー順表示が再通知されることを検証する</summary>
    [Fact(DisplayName = "主キー切替・リネーム・並び替えで主キー順表示が再通知される")]
    public void PrimaryKeyOrder_RaisesPropertyChanged_OnColumnChanges()
    {
        var entity = NewCompositeKeyEntity();
        var changed = new List<string?>();
        entity.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // 主キーの解除（構成列が変わる）
        entity.Columns[1].IsPrimaryKey = false;
        changed.Should().Contain(nameof(EntityViewModel.IsCompositePrimaryKey));
        changed.Should().Contain(nameof(EntityViewModel.PrimaryKeyOrderText));
        entity.IsCompositePrimaryKey.Should().BeFalse();

        // 列のリネーム（表示名が変わる）
        entity.Columns[1].IsPrimaryKey = true;
        changed.Clear();
        entity.Columns[0].Name = "renamed";
        changed.Should().Contain(nameof(EntityViewModel.PrimaryKeyOrderText));
        entity.PrimaryKeyOrderText.Should().Be("b, renamed");

        // 列の並び替え（実効順の第 2 キーが表示順のため追従が要る）
        changed.Clear();
        entity.Columns.Move(0, 1);
        changed.Should().Contain(nameof(EntityViewModel.PrimaryKeyOrderText));
    }
}
