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
}
