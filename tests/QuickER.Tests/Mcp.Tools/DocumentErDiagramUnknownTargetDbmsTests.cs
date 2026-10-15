using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 図の対象 DBMS が知らない方言名（手で編集した図の "foo" など）でも、MCP のツールが例外にならないことを検証する
/// </summary>
/// <remarks>
/// ツールホストは上限を <c>IdentifierLengthLimits.ForDiagramTarget</c> で引き、知らない方言名なら上限を検査しない
/// （何も断らず、注意文も添えない）。
/// </remarks>
public sealed class DocumentErDiagramUnknownTargetDbmsTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(),
        "quicker-unknown-dbms-" + Guid.NewGuid().ToString("N") + ".json"
    );

    public void Dispose()
    {
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    private (string Result, bool Success) Exec(string tool, object args) =>
        DocumentErDiagramToolHost.Execute(tool, _file, JsonSerializer.SerializeToElement(args));

    [Fact(
        DisplayName = "知らない方言名の図を MCP のツールで変更しても例外にならず、上限の注意文を添えない"
    )]
    public void UnknownDbms_DoesNotThrowAndSkipsLimitNotes()
    {
        JsonStorageService.SaveAtomic(
            _file,
            new DiagramDocument { Schema = new ErDiagram { TargetDbms = "foo" } }
        );
        var longName = new string('t', 300);

        var added = Exec("add_entity", new { table_name = longName });
        var column = Exec(
            "add_column",
            new
            {
                table_name = longName,
                column_name = "id",
                data_type = "int",
                is_primary_key = true,
            }
        );
        var summary = Exec("get_diagram_summary", new { });

        added.Success.Should().BeTrue(added.Result);
        added.Result.Should().NotContain("would refuse", "方言が分からないので上限を検査しない");
        column.Success.Should().BeTrue(column.Result);
        summary.Success.Should().BeTrue(summary.Result);
        summary.Result.Should().Contain(longName);
    }
}
