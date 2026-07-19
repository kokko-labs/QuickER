using System.Collections.Generic;
using FluentAssertions;
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
}
