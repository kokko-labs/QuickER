using AwesomeAssertions;
using QuickER.Tests.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 方言の型の表の実 DB の段の手順（<see cref="DialectTypeRealDbRows"/>）の宣言の照合（DB に触れない・Docker なしで流れる）
/// </summary>
public sealed class DialectTypeRealDbTableTests(ITestOutputHelper output)
{
    [Fact(
        DisplayName = "[方言の型・実 DB] 手順は、その方言の期待を持つ表の行を指し、行の観点と境界が手順の種類に合う"
    )]
    public void Probes_PointAtMatchingRows()
    {
        var problems = new List<string>();

        foreach (var probe in DialectTypeRealDbRows.All)
        {
            problems.AddRange(Problems(probe).Select(problem => $"{probe.Describe()}: {problem}"));
        }

        problems.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "[方言の型・実 DB] 方言 × 手順の種類の網羅しない組は、宣言した一覧とちょうど一致する"
    )]
    public void Coverage_MatchesDeclaredGaps()
    {
        var counter = new CoverageCounter(DialectTypeRealDbDimensions.Table);

        foreach (var probe in DialectTypeRealDbRows.All)
        {
            DialectTypeRealDbDimensions.Observe(counter, probe);
        }

        var report = counter.Report();
        report.WriteSummary(output);
        report.AssertGaps(
            DialectTypeRealDbDimensions.Gaps,
            DialectTypeRealDbDimensions.Scope,
            output: output
        );
    }

    /// <summary>手順の宣言の食い違い（無ければ空）</summary>
    private static IEnumerable<string> Problems(DialectTypeRealDbProbe probe)
    {
        if (!DialectTypeRows.Table.Any(row => row.Id == probe.RowId))
        {
            yield return "表に無い行";
            yield break;
        }

        var row = DialectTypeRows.Table[probe.RowId].Input;

        if (!row.Expected.ContainsKey(probe.Dialect))
        {
            yield return $"行が {probe.Dialect} の期待を持たない";
        }

        switch (probe.Kind)
        {
            case DialectTypeRealDbProbeKinds.AtLimit or DialectTypeRealDbProbeKinds.OverLimit:
            {
                var boundary =
                    probe.Kind == DialectTypeRealDbProbeKinds.AtLimit
                        ? DialectTypeBoundaries.AtLimit
                        : DialectTypeBoundaries.OverLimit;

                if (row.Boundary != boundary)
                {
                    yield return $"行の境界が {row.Boundary}（{boundary} の行を指す）";
                }

                // 書き出せない（1 つ超えの）書き出しの行は表記を手順に書き、パラメータの書き出しの行は行の出力を使う
                var needsSpelling =
                    row.Facet == DialectTypeFacets.Format
                    && probe.Kind == DialectTypeRealDbProbeKinds.OverLimit;

                if (row.Facet is not (DialectTypeFacets.Format or DialectTypeFacets.FormatParam))
                {
                    yield return $"行の観点が {row.Facet}（書き出しかパラメータの書き出しの行を指す）";
                }
                else if (needsSpelling != probe.Spelling is not null)
                {
                    yield return needsSpelling
                        ? "書き出せない行なので、DB に流す表記を手順に書く"
                        : "行の出力を DDL に流すので、表記を手順に書かない";
                }

                if (probe.Value is not null || probe.Driver is not null)
                {
                    yield return "上限の手順は値とドライバの型を持たない";
                }

                break;
            }

            case DialectTypeRealDbProbeKinds.Driver:
            {
                if (row.Facet != DialectTypeFacets.Map)
                {
                    yield return $"行の観点が {row.Facet}（マッパーの行を指す）";
                }

                if (probe.Value is null || (probe.Driver is null && probe.MapperType is null))
                {
                    yield return "マッパーの手順は値と、ドライバの型かマッパーの型を持つ";
                }

                if (probe.Spelling is not null)
                {
                    yield return "マッパーの手順は行の入力を DDL に流す（表記を書かない）";
                }

                break;
            }

            default:
                yield return $"知らない種類 {probe.Kind}";
                break;
        }
    }
}
