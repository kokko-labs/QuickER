using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>対象 DBMS の既定・旧形式・往復</summary>
    private static IEnumerable<ScenarioRow<FormatCase>> TargetDbmsRows() =>
        [
            Row(
                "roundtrip-default-target-dbms-is-sqlserver",
                "ErDiagram の既定 TargetDbms は sqlserver",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () => new DiagramDocument { Schema = new ErDiagram() },
                    Expect = o =>
                    {
                        o.Written!.Schema.TargetDbms.Should().Be("sqlserver");
                        o.Loaded!.Schema.TargetDbms.Should().Be("sqlserver");
                    },
                }
            ),
            Row(
                "load-legacy-without-target-dbms-defaults-sqlserver",
                "旧形式 JSON（TargetDbms 欠落）は sqlserver として読み込まれる",
                new FormatCase
                {
                    Stage = FormatStage.Load,
                    Result = FormatResult.Loaded,
                    Json = """
                    {
                      "version": 1,
                      "schema": {
                        "entities": [],
                        "relationships": []
                      },
                      "layout": {}
                    }
                    """,
                    Expect = o => o.Loaded!.Schema.TargetDbms.Should().Be("sqlserver"),
                }
            ),
            Row(
                "roundtrip-target-dbms-preserved",
                "Save → Load で TargetDbms が保持される",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () =>
                        new DiagramDocument
                        {
                            Schema = new ErDiagram { TargetDbms = "postgresql" },
                        },
                    Expect = o => o.Loaded!.Schema.TargetDbms.Should().Be("postgresql"),
                }
            ),
        ];
}
