using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// Excel 定義書のマージ取込の入口の配線（書き出して読み戻しただけの定義書で置換の確認を出さないこと）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 定義書の往復と、マージ取込が定義書の運べない情報を現在図から引き継ぐ規則は、形式の往復のエンジン
/// （<c>FormatRoundTrip/</c> の形の行 <c>excel-*</c>・マージの行 <c>merge-excel-*</c>）が見る。
/// ここでは <see cref="DiagramImportService"/> が後処理（<see cref="TableDefinitionMergePostProcessor"/>）を置換の確認より前に通すことだけを固定する。
/// </remarks>
public class TableDefinitionRoundTripTests
{
    /// <summary>親 customers(id) と子 orders(id, customer_id, a, b) の図を組み立てる（リレーションは呼び出し側が足す）</summary>
    private static ErDiagram BuildDiagram()
    {
        var customers = new Entity
        {
            TableName = "customers",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var orders = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column { Name = "customer_id", DataType = "int" },
                new Column { Name = "a", DataType = "int" },
                new Column { Name = "b", DataType = "int" },
            ],
        };

        return new ErDiagram { Entities = [customers, orders], TargetDbms = "sqlserver" };
    }

    /// <summary>orders の主キーを (b, a)＝列の並びと逆の順序で明示した現在図を組み立てる</summary>
    private static ErDiagram BuildCurrentWithReorderedPrimaryKey()
    {
        var diagram = BuildDiagram();
        var orders = diagram.Entities[1];
        var a = orders.Columns.Single(column => column.Name == "a");
        var b = orders.Columns.Single(column => column.Name == "b");
        orders.Columns[0].IsPrimaryKey = false;
        a.IsPrimaryKey = true;
        a.IsNullable = false;
        b.IsPrimaryKey = true;
        b.IsNullable = false;
        orders.PrimaryKeyColumnIds = [b.Id, a.Id];

        return diagram;
    }

    /// <summary>
    /// 定義書は複合主キーの順序を書かないが、マージ取込は後処理で現在図の順序を引き継いでから構造を比べるので、
    /// 書き出して読み戻しただけで置換の確認を出さないことを検証する
    /// </summary>
    /// <remarks>順序を引き継ぐこと自体はマージの行 merge-excel-inherits-primary-key-order が見る</remarks>
    [Fact(
        DisplayName = "Excel のマージ取込: 主キー列の集合が同じなら現在図の主キーの順序を引き継ぎ、置換の確認を出さない"
    )]
    public void MergeImport_InheritsPrimaryKeyOrder()
    {
        var current = BuildCurrentWithReorderedPrimaryKey();
        var dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "quicker-tabledoc-" + Guid.NewGuid().ToString("N"))
        );

        try
        {
            var path = Path.Combine(dir.FullName, "doc.xlsx");
            TableDefinitionDocumentExporter.SaveTo(current, path);

            var host = new StubDiagramTransferHost { Model = current };
            var dialog = new StubDialogService();
            var service = new DiagramImportService(host, dialog, new StubFileDialogService());

            service.ImportDiagramFile(DiagramImportFormat.Excel, path);

            host.MergedReplacements.Should().ContainSingle();
            dialog.ConfirmMessages.Should().BeEmpty("構造が同じなので置換の確認は出ない");
            dialog.WarningConfirmMessages.Should().BeEmpty();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
