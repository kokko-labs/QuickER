using System;
using System.IO;
using System.Linq;
using Xunit;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

// 一時的な判定（P3-1a の指摘 2）：合成の能力の行を近い実方言で流し、期待がそのまま合い静的な規則の違反も無いかを書き出す。
// 判定が済んだら消す。
public sealed class SyncPlanExpectationProbeTests
{
    [Fact]
    public void Probe()
    {
        var path = Environment.GetEnvironmentVariable("QUICKER_EXPECTATION_PROBE");

        if (path is null)
        {
            return;
        }

        var lines = new System.Collections.Generic.List<string>();

        foreach (var row in SyncPlanExpectationRows.Table)
        {
            if (row.Input.Expect.SamePlanAcrossCapabilities)
            {
                continue;
            }

            foreach (
                var key in row.Input.Capabilities.Where(k =>
                    !SyncPlanExpectationCapabilities.IsDialect(k)
                )
            )
            {
                var dialect = SyncPlanExpectationCapabilities.NearestDialect[key];

                if (row.Input.Capabilities.Contains(dialect))
                {
                    lines.Add($"{row.Id}\t{key}\t{dialect}\tSKIP-dialect-already-in-row");
                    continue;
                }

                var problems = SyncPlanExpectationRunner.Run(
                    row,
                    [dialect],
                    new System.Collections.Generic.Dictionary<string, string> { [dialect] = key }
                );
                lines.Add(
                    $"{row.Id}\t{key}\t{dialect}\t{(problems.Count == 0 ? "MATCH" : "DIFF")}\t{string.Join(" || ", problems.Select(p => p.Replace('\n', ' ')))}"
                );
            }
        }

        File.WriteAllLines(path, lines);
    }
}
