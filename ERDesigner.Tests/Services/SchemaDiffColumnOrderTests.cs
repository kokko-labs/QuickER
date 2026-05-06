using System.Collections.Generic;
using ERDesigner.Models;
using ERDesigner.Services;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// <see cref="SchemaDiffService"/> の列順差分検知ロジックのテスト。
/// </summary>
public class SchemaDiffColumnOrderTests
{
    private static Entity Tbl(string name, params string[] cols)
    {
        var e = new Entity { TableName = name };

        foreach (var c in cols)
        {
            e.Columns.Add(new Column { Name = c, DataType = "int" });
        }

        return e;
    }

    [Fact(DisplayName = "同一列集合で順序のみ異なる場合は列順変更として検知される")]
    public void DetectColumnOrderChanges_OrderOnly_ReturnsTable()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name", "Email") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Email", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().ContainSingle().Which.Should().Be("Customer");
    }

    [Fact(DisplayName = "列追加がある場合は列順変更としては検知しない")]
    public void DetectColumnOrderChanges_WithAddedColumn_DoesNotReturnTable()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Email", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }

    [Fact(DisplayName = "同一順序なら列順変更としては検知しない")]
    public void DetectColumnOrderChanges_SameOrder_ReturnsEmpty()
    {
        var live = new List<Entity> { Tbl("Customer", "Id", "Name") };
        var target = new List<Entity> { Tbl("Customer", "Id", "Name") };

        var changed = SchemaDiffService.DetectColumnOrderChanges(live, target);

        changed.Should().BeEmpty();
    }
}
