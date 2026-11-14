using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 方言の型の表の実 DB の段の手順（<see cref="DialectTypeRealDbRows"/>）の宣言の照合（DB に触れない・Docker なしで流れる）
/// </summary>
public sealed partial class DialectTypeRealDbTableTests(ITestOutputHelper output)
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

    [Fact(
        DisplayName = "[方言の型・実 DB] 書き込みの範囲の値の根拠が docs の約束なら、英日の docs にその見出しがある"
    )]
    public void WriteBases_PointAtExistingDocsHeadings()
    {
        var root = EngineFingerprintDriftTests.RepositoryRoot();
        var problems = new List<string>();

        foreach (
            var basis in DialectTypeRealDbRows
                .All.SelectMany(probe => probe.Writes ?? [])
                .Select(write => write.Basis)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
        )
        {
            foreach (Match match in DocsBasisPattern().Matches(basis))
            {
                var path = Path.Combine(root, "docs", match.Groups["file"].Value);
                var heading = match.Groups["heading"].Value;

                if (
                    !File.Exists(path)
                    || !File.ReadLines(path)
                        .Any(line => line.StartsWith('#') && line.TrimStart('#').Trim() == heading)
                )
                {
                    problems.Add($"{match.Value} の見出しが無い（根拠：{basis}）");
                }
            }
        }

        problems.Should().BeEmpty();
    }

    /// <summary>docs の約束の根拠の書き方（<see cref="DialectTypeRealDbRows.DocsBasis"/>）</summary>
    [GeneratedRegex(
        @"docs/(?<file>[a-z-]+(\.ja)?\.md)「(?<heading>[^」]+)」",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex DocsBasisPattern();

    /// <summary>書き込みの範囲の手順か（EF Core の経路と QuickER 版の束縛）</summary>
    private static bool IsWriteRange(string kind) =>
        kind
            is DialectTypeRealDbProbeKinds.EfCoreWriteRange
                or DialectTypeRealDbProbeKinds.AdoWriteRange;

    /// <summary>
    /// 書く値 1 つの期待の宣言の食い違い：DB が変えて保存する値と読めない値は根拠を持ち（根拠は変える値か読めない値にだけ書く）、
    /// 両方は持たない。変えて保存する値は DB が断らない値で、書く値と違う。docs の約束を根拠にするなら英日の両方を名指す
    /// </summary>
    private static IEnumerable<string> WriteExpectationProblems(DialectTypeRealDbWrite write)
    {
        var changes = write.Stored is not null || write.Unreadable;

        if (changes != write.Basis is not null)
        {
            yield return "DB が変えて保存する値・読めない値は根拠（Basis）を持ち、ほかの値は持たない";
        }

        if (write.Stored is not null && write.Unreadable)
        {
            yield return "変えて保存する値と読めない値の両方は持たない";
        }

        if (write.Stored is not null && !write.Fits)
        {
            yield return "変えて保存する値は DB が断らない値（Fits）にだけ書く";
        }

        if (write.Stored is not null && write.Stored.Equals(write.Value))
        {
            yield return "変えて保存する値が書く値と同じ（同じ値なら Stored を書かない）";
        }

        if (
            write.Basis is { } basis
            && basis.Contains("docs/", StringComparison.Ordinal)
            && DocsBasisPattern().Matches(basis).Count != 2
        )
        {
            yield return $"docs の約束の根拠は英日の見出しを DocsBasis の形で名指す（{basis}）";
        }
    }

    /// <summary>読み書きできない組の理由の書き出し（製品の候補＝直す区切りが手順を通る形へ書き換える）</summary>
    private const string ProductCandidate = "製品の候補（P5）：";

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

        if (probe.Kind != DialectTypeRealDbProbeKinds.EfCore && probe.Write is not null)
        {
            yield return "書く値は EF Core の手順だけが持つ";
        }

        if (!IsWriteRange(probe.Kind) && probe.Writes is not null)
        {
            yield return "範囲の端と外の書く値は書き込みの範囲の手順だけが持つ";
        }

        // 読み書きできない組は、理由と今の症状の両方を持つ（片方だけだと、直ったことを知らせられないか、理由が残らない）
        if ((probe.GapReason is null) != (probe.GapSymptom is null))
        {
            yield return "読み書きできない組は、理由（GapReason）と症状（GapSymptom）の両方を持つ";
        }

        if (
            probe.GapReason is { } reason
            && !reason.StartsWith(ProductCandidate, StringComparison.Ordinal)
        )
        {
            yield return $"読み書きできない組の理由は「{ProductCandidate}」で始める（直す区切りが埋める組と分かるように）";
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

            case DialectTypeRealDbProbeKinds.EfCore:
            {
                if (row.Facet != DialectTypeFacets.Map)
                {
                    yield return $"行の観点が {row.Facet}（マッパーの行を指す）";
                    break;
                }

                if (probe.Value is null || probe.ReadBack is null || probe.Write is null)
                {
                    yield return "EF Core の手順は、入れる値・読んだ値の期待・書く値を持つ";
                    break;
                }

                if (
                    probe.Spelling is not null
                    || probe.Driver is not null
                    || probe.MapperType is not null
                    || probe.NarrowingLoses
                )
                {
                    yield return "EF Core の手順は行の入力を DDL に流し、ドライバの型・マッパーの型・精度の確認を持たない";
                }

                // 読んだ値・書く値の型は、マッパーが行の入力を写す C# の型（違うと Equals が型の違いで食い違う・書けない）
                var mapped = DialectTypeAxis.Get(probe.Dialect).Map((string)row.Input!).TypeName;

                foreach (
                    var (what, value) in new[]
                    {
                        ("読んだ値", probe.ReadBack),
                        ("書く値", probe.Write),
                    }
                )
                {
                    var name = DialectTypeRealDbTestsBase.CSharpNames.GetValueOrDefault(
                        value.GetType()
                    );

                    if (name != mapped)
                    {
                        yield return $"{what}の型 {name ?? value.GetType().Name} がマッパーの型 {mapped} と違う";
                    }
                }

                break;
            }

            case DialectTypeRealDbProbeKinds.EfCoreWriteRange
            or DialectTypeRealDbProbeKinds.AdoWriteRange:
            {
                if (row.Facet != DialectTypeFacets.Map)
                {
                    yield return $"行の観点が {row.Facet}（マッパーの行を指す）";
                    break;
                }

                if (probe.Writes is not { } writes || !writes.Any(write => write.Fits))
                {
                    yield return "書き込みの範囲の手順は、範囲の中の値を 1 つ以上含む書く値を持つ";
                    break;
                }

                if (
                    probe.Spelling is not null
                    || probe.Value is not null
                    || probe.ReadBack is not null
                    || probe.Driver is not null
                    || probe.MapperType is not null
                    || probe.NarrowingLoses
                    || probe.GapReason is not null
                )
                {
                    yield return "書き込みの範囲の手順は行の入力を DDL に流し、書く値のほかを持たない";
                }

                // 書く値の型は、マッパーが行の入力を写す C# の型（違うと書けないか、読み戻した値と型の違いで食い違う）
                var mappedType = DialectTypeAxis
                    .Get(probe.Dialect)
                    .Map((string)row.Input!)
                    .TypeName;

                foreach (var write in writes)
                {
                    foreach (var value in new[] { write.Value, write.Stored }.OfType<object>())
                    {
                        var name = DialectTypeRealDbTestsBase.CSharpNames.GetValueOrDefault(
                            value.GetType()
                        );

                        if (name != mappedType)
                        {
                            yield return $"書く値・変えて保存する値の型 {name ?? value.GetType().Name} がマッパーの型 {mappedType} と違う";
                        }
                    }

                    foreach (var problem in WriteExpectationProblems(write))
                    {
                        yield return $"{write.Value}: {problem}";
                    }
                }

                // QuickER 版の束縛の手順は、同じ方言の書き込みの範囲の手順と同じ行・同じ値を持つ（束縛の違いだけを見る）
                if (
                    probe.Kind == DialectTypeRealDbProbeKinds.AdoWriteRange
                    && !DialectTypeRealDbRows.All.Any(other =>
                        other.Kind == DialectTypeRealDbProbeKinds.EfCoreWriteRange
                        && other.RowId == probe.RowId
                        && other.Dialect == probe.Dialect
                        && other.Writes!.SequenceEqual(probe.Writes!)
                    )
                )
                {
                    yield return "QuickER 版の束縛の手順に、同じ行・同じ値の書き込みの範囲の手順が無い";
                }

                break;
            }

            default:
                yield return $"知らない種類 {probe.Kind}";
                break;
        }
    }
}
