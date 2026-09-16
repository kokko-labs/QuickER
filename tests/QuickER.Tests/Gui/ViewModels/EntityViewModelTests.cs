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

    /// <summary>複合主キーの並び替え行が実効順になることを検証する</summary>
    [Fact(DisplayName = "主キーの並び替え行は実効順の列を並べる")]
    public void PrimaryKeyMembers_UseEffectiveOrder()
    {
        var entity = NewCompositeKeyEntity();

        entity.IsCompositePrimaryKey.Should().BeTrue();
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("b", "a");

        // 端の行は上下移動できない（ボタンの IsEnabled の元）
        entity.PrimaryKeyMembers[0].CanMoveUp.Should().BeFalse();
        entity.PrimaryKeyMembers[0].CanMoveDown.Should().BeTrue();
        entity.PrimaryKeyMembers[1].CanMoveUp.Should().BeTrue();
        entity.PrimaryKeyMembers[1].CanMoveDown.Should().BeFalse();
    }

    /// <summary>単一主キーでは複合フラグが立たない（＝表示されない）ことを検証する</summary>
    [Fact(DisplayName = "単一主キーでは複合主キーフラグが立たない")]
    public void IsCompositePrimaryKey_SinglePrimaryKey_IsFalse()
    {
        NewEntityWithMixedColumns().IsCompositePrimaryKey.Should().BeFalse();
    }

    /// <summary>主キーの切替・列のリネーム・列の並び替えで並び替え行が追従することを検証する</summary>
    [Fact(DisplayName = "主キー切替・リネーム・並び替えで主キーの並び替え行が追従する")]
    public void PrimaryKeyMembers_FollowColumnChanges()
    {
        var entity = NewCompositeKeyEntity();
        var changed = new List<string?>();
        entity.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // 主キーの解除（構成列が変わる＝行が 1 本減る）
        entity.Columns[1].IsPrimaryKey = false;
        changed.Should().Contain(nameof(EntityViewModel.IsCompositePrimaryKey));
        entity.IsCompositePrimaryKey.Should().BeFalse();
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("a");

        // 列のリネーム（表示名は行が持つ ColumnViewModel の通知で届く）
        entity.Columns[1].IsPrimaryKey = true;
        entity.Columns[0].Name = "renamed";
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("b", "renamed");

        // 列の並び替え（実効順の第 2 キーが表示順のため追従が要る）
        entity.Columns.Move(0, 1);
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("b", "renamed");
    }

    /// <summary>
    /// 並び替え行の増減が末尾でのみ吸収され、残る行のインスタンスが使い回されることを検証する。
    /// </summary>
    /// <remarks>
    /// 毎回作り直すと ItemsControl のコンテナが再生成され、↑ ボタンの連打中にフォーカスが落ちる。
    /// ビルドでも型検査でも出ない性質のため参照一致で固定する。
    /// </remarks>
    [Fact(DisplayName = "主キーの並び替え行は再構築されても行インスタンスを使い回す")]
    public void PrimaryKeyMembers_ReuseRowInstances()
    {
        var entity = NewCompositeKeyEntity();
        var first = entity.PrimaryKeyMembers[0];
        var second = entity.PrimaryKeyMembers[1];

        // 並び替え（行数は同じ＝両方の行が生き残る）
        entity.SetPrimaryKeyColumnIds([entity.Columns[0].Id, entity.Columns[1].Id]);
        entity.PrimaryKeyMembers[0].Should().BeSameAs(first);
        entity.PrimaryKeyMembers[1].Should().BeSameAs(second);
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("a", "b");

        // 膜を 1 本外す（末尾で吸収＝先頭行は生き残る）
        entity.Columns[1].IsPrimaryKey = false;
        entity.PrimaryKeyMembers.Should().ContainSingle().Which.Should().BeSameAs(first);

        // 戻すと末尾へ足される（先頭行は依然として同じインスタンス）
        entity.Columns[1].IsPrimaryKey = true;
        entity.PrimaryKeyMembers[0].Should().BeSameAs(first);
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("a", "b");
    }

    /// <summary>保持中の順序リストそのものを渡しても順序が失われないことを検証する</summary>
    /// <remarks>
    /// <see cref="EntityViewModel.PrimaryKeyColumnIds"/> は内部リストを直接返すため、それを差し替えの引数に
    /// 渡すと「消してから読む」形になり、何も読めずに空になり得る。
    /// </remarks>
    [Fact(DisplayName = "主キー順の差し替えへ保持中のリスト自身を渡しても順序が保たれる")]
    public void SetPrimaryKeyColumnIds_PassingOwnList_KeepsOrder()
    {
        var entity = NewCompositeKeyEntity();
        var before = entity.PrimaryKeyColumnIds.ToList();

        entity.SetPrimaryKeyColumnIds(entity.PrimaryKeyColumnIds);

        entity.PrimaryKeyColumnIds.Should().Equal(before);
        entity.PrimaryKeyMembers.Select(m => m.Column.Name).Should().Equal("b", "a");
    }
}
