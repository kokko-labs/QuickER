using System.Collections.Generic;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary><see cref="SchemaDiffService.DetectColumnOrderChanges"/> の列順差分検知を検証するテストクラス</summary>
public class SchemaDiffColumnOrderTests
{
    /// <summary>指定名・指定カラム列を持つテスト用エンティティを生成する</summary>
    private static Entity Tbl(string name, params string[] cols)
    {
        var e = new Entity { TableName = name };

        foreach (var c in cols)
        {
            e.Columns.Add(new Column { Name = c, DataType = "int" });
        }

        return e;
    }

    /// <summary>列集合が同一で順序のみ異なる場合に列順変更として検知されることを検証する</summary>
    [Fact(DisplayName = "同一列集合で順序のみ異なる場合は列順変更として検知される")]
    public void DetectColumnOrderChanges_OrderOnly_ReturnsTable()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name", "Email") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Email", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().ContainSingle().Which.Should().Be("Customer");
    }

    /// <summary>列追加を伴う場合は列順変更として検知しないことを検証する</summary>
    [Fact(DisplayName = "列追加がある場合は列順変更としては検知しない")]
    public void DetectColumnOrderChanges_WithAddedColumn_DoesNotReturnTable()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Email", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }

    /// <summary>列集合・順序とも同一なら列順変更として検知しないことを検証する</summary>
    [Fact(DisplayName = "同一順序なら列順変更としては検知しない")]
    public void DetectColumnOrderChanges_SameOrder_ReturnsEmpty()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }

    /// <summary>真ん中への列追加を伴っても、共通列の相対順序が変われば列順変更として検知することを検証する</summary>
    [Fact(DisplayName = "列追加を伴う並び替えでも共通列の順序が変われば検知する")]
    public void DetectColumnOrderChanges_ReorderWithAddedColumn_ReturnsTable()
    {
        // 共通列 Id/Name/Email の相対順が Name→Email と Email→Name で入れ替わり、真ん中に X を追加
        var live = new List<Entity> { Tbl("Customer", "Id", "Name", "Email") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Email", "X", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().ContainSingle().Which.Should().Be("Customer");
    }

    /// <summary>列削除を伴っても、共通列の相対順序が変われば列順変更として検知することを検証する</summary>
    [Fact(DisplayName = "列削除を伴う並び替えでも共通列の順序が変われば検知する")]
    public void DetectColumnOrderChanges_ReorderWithDroppedColumn_ReturnsTable()
    {
        // live の Old を削除しつつ、共通列 Id/Name/Email の相対順が入れ替わる
        var live = new List<Entity> { Tbl("Customer", "Id", "Name", "Old", "Email") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Email", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().ContainSingle().Which.Should().Be("Customer");
    }

    /// <summary>共通列の相対順序が保たれた純粋な列追加は検知しないことを検証する</summary>
    [Fact(DisplayName = "共通列の相対順序が保たれた列追加は検知しない")]
    public void DetectColumnOrderChanges_PureInsertionKeepingOrder_ReturnsEmpty()
    {
        // 共通列 Id/Name/Email の相対順は保たれたまま、真ん中へ X を追加しただけ
        var live = new List<Entity> { Tbl("Customer", "Id", "Name", "Email") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Name", "X", "Email") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }

    /// <summary>列名比較が大文字小文字を無視することを検証する（順序が同一なら検知しない）</summary>
    [Fact(DisplayName = "列順比較は大文字小文字を無視する")]
    public void DetectColumnOrderChanges_CaseInsensitive_ReturnsEmpty()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name", "Email") };
        var target = new List<Entity> { Tbl("Customer", "id", "name", "email") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }

    /// <summary>共通列が 1 列以下なら相対順序の概念が無く検知しないことを検証する</summary>
    [Fact(DisplayName = "共通列が 1 列以下なら列順変更としては検知しない")]
    public void DetectColumnOrderChanges_FewerThanTwoCommonColumns_ReturnsEmpty()
    {
        // 共通列は Id のみ（1 列）＝相対順序を語れない
        var live = new List<Entity> { Tbl("Customer", "Id", "OldA", "OldB") };
        var target = new List<Entity> { Tbl("Customer", "NewB", "NewA", "Id") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }
}
