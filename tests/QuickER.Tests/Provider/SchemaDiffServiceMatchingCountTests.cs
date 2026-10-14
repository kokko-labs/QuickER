using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 差分が表と列の組み合わせを 1 回だけ行い、揃えた複製・列の差分・列順の判定が同じ組を使い回すことを検証するテストクラス
/// </summary>
/// <remarks>
/// 組み直しは結果を変えないので、既存の差分のテストは回数が増えても緑のままになる。回数を観測点
/// （<see cref="SchemaDiffService.TableMatchings"/> / <see cref="SchemaDiffService.ColumnMatchings"/>）で固定する。
/// </remarks>
public sealed class SchemaDiffServiceMatchingCountTests
{
    private static Entity Tbl(string name, params string[] columns)
    {
        var entity = new Entity { TableName = name };

        foreach (var column in columns)
        {
            entity.Columns.Add(new Column { Name = column, DataType = "int" });
        }

        return entity;
    }

    [Fact(
        DisplayName = "差分 1 回につき、表の組み合わせは 1 回、組になった表ごとの列の組み合わせも 1 回だけ行う"
    )]
    public void Compute_MatchesTablesAndColumnsOnce()
    {
        // 綴りを揃える表・揃えない表・列順の違う表・図にだけ在る表・DB にだけ在る表を混ぜる
        var live = new List<Entity>
        {
            Tbl("Orders", "Id", "Total"),
            Tbl("Customer", "Id", "Name"),
            Tbl("Item", "Id", "Price", "Qty"),
            Tbl("Legacy", "Id"),
        };
        var target = new List<Entity>
        {
            Tbl("orders", "id", "Total"),
            Tbl("Customer", "Id", "Name"),
            Tbl("Item", "Id", "Qty", "Price"),
            Tbl("Brand", "Id"),
        };
        var service = new SchemaDiffService();

        service.Compute(
            live,
            [],
            target,
            [],
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native }
        );

        service.TableMatchings.Should().Be(1);
        service.ColumnMatchings.Should().Be(3);
    }
}
