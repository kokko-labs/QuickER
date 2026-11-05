using System.Globalization;
using AwesomeAssertions;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 網羅の配列の行をいくつかのクラスへ分ける（xUnit は同じクラスの Theory の行を並列に流さないため）
/// </summary>
/// <remarks>
/// 行は並びの番号を分ける数で割った余りで振り分ける。分ける数とクラスの数が食い違うと余りの組の行が黙って流れなくなるので、
/// 表ごとのガード（<see cref="CodegenMatrixTests.Parts_CoverEveryRowOnce"/> など）が、各クラスの MemberData の和が
/// 行とちょうど一致することを確かめる。
/// </remarks>
internal static class CodegenMatrixParts
{
    /// <summary>分ける数</summary>
    public const int Parts = 4;

    /// <summary>
    /// 夜間の Errors の行の決め方（種＝夜間の網羅の配列の行の番号）。普段は分ける数だけ（各クラス 1 行）、夜間は全行を流す
    /// </summary>
    /// <remarks>
    /// 夜間の行の網羅は夜間の深い実行（<c>QUICKER_ENGINE_PROFILE=nightly</c>）で流す。普段の実行では先頭の数行だけを流して、
    /// 夜間の経路が腐っていないことだけを見る（網羅の照合は普段の行で行う）。変数で数を変えられる。
    /// </remarks>
    public static SeedSettings NightlySeeds { get; } =
        new(
            "QUICKER_CODEGEN_NIGHTLY_ROWS",
            Parts,
            Math.Max(Parts, CodegenConfigDimensions.NightlyRows.Count),
            firstSeed: 0
        );

    /// <summary>夜間の Success の行の決め方（<see cref="NightlySeeds"/> と同じ流儀）</summary>
    public static SeedSettings NightlySuccessSeeds { get; } =
        new(
            "QUICKER_CODEGEN_NIGHTLY_SUCCESS_ROWS",
            Parts,
            Math.Max(Parts, CodegenConfigDimensions.NightlySuccessRows.Count),
            firstSeed: 0
        );

    private static readonly Lazy<Dictionary<string, CodegenRow>> ById = new(() =>
        CodegenConfigDimensions
            .Rows.Concat(CodegenConfigDimensions.SuccessRows)
            .Concat(CodegenConfigDimensions.NightlyRows)
            .Concat(CodegenConfigDimensions.NightlySuccessRows)
            .ToDictionary(row => row.Id, StringComparer.Ordinal)
    );

    /// <summary>行のうち <paramref name="part"/> 番目のクラスが受け持つものの Id</summary>
    /// <param name="rows">行</param>
    /// <param name="part">クラスの番号（0 から）</param>
    public static TheoryData<string> Ids(IReadOnlyList<CodegenRow> rows, int part)
    {
        var data = new TheoryData<string>();

        foreach (var row in rows.Where((_, index) => index % Parts == part))
        {
            data.Add(row.Id);
        }

        return data;
    }

    /// <summary>夜間の行のうち、いまのプロファイルで流すもので <paramref name="part"/> 番目のクラスが受け持つものの Id</summary>
    /// <param name="rows">夜間の行</param>
    /// <param name="seeds">流す行の決め方</param>
    /// <param name="part">クラスの番号（0 から）</param>
    public static TheoryData<string> NightlyIds(
        IReadOnlyList<CodegenRow> rows,
        SeedSettings seeds,
        int part
    )
    {
        var data = new TheoryData<string>();

        foreach (
            var seed in SeedSource
                .Resolve(seeds)
                .Seeds.Where(seed => seed < rows.Count && seed % Parts == part)
        )
        {
            data.Add(rows[seed].Id);
        }

        return data;
    }

    /// <summary>いまのプロファイルで流す夜間の行の Id</summary>
    public static IEnumerable<string> PlannedNightlyIds(
        IReadOnlyList<CodegenRow> rows,
        SeedSettings seeds
    )
    {
        var plan = SeedSource.Resolve(seeds);
        plan.WriteWarnings();
        return plan.Seeds.Where(seed => seed < rows.Count).Select(seed => rows[seed].Id);
    }

    /// <summary>行を Id で引く（4 つの表のどれか）</summary>
    public static CodegenRow Row(string id) => ById.Value[id];

    /// <summary>クラスの MemberData（<c>RowIds</c>）が返す行の Id を全部並べる（クラスの数と分ける数の食い違いも拾う）</summary>
    public static IEnumerable<string> RowsOf(params Type[] classes)
    {
        classes.Should().HaveCount(Parts, "分ける数とクラスの数をそろえる");

        return classes.SelectMany(type =>
            ((TheoryData<string>)type.GetMethod("RowIds")!.Invoke(null, null)!).Select(row =>
                row.Data
            )
        );
    }

    /// <summary>行の網羅を数え、網羅しない組の一覧と照合する</summary>
    public static void AssertCoverage(
        DimensionTable table,
        IReadOnlyList<CodegenRow> rows,
        CoverageGapSet gaps,
        string scope
    )
    {
        var counter = new CoverageCounter(table);

        foreach (var row in rows)
        {
            CodegenConfigDimensions.Observe(counter, row);
        }

        var report = counter.Report();
        report.WriteSummary();
        TestContext.Current.TestOutputHelper?.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"[CodegenConfig] {scope}: 行 {rows.Count}")
        );
        report.AssertGaps(gaps, scope);
    }
}

/// <summary>
/// 生成の構成のエンジンの Errors の表（全組の網羅の配列。生成時エラーになる組を含め、診断の予測を照合する）の 1 つ目の分と、
/// その網羅の照合・分け方・入口の軸のガード
/// </summary>
public sealed class CodegenMatrixTests
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.Rows, 0);

    [Theory(
        DisplayName = "[生成の構成/Errors] 網羅の配列の行は、診断・ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));

    [Fact(
        DisplayName = "[生成の構成/Errors] 網羅の配列の行は次元表の網羅すべき組をすべて含む（網羅しない組の一覧と一致）"
    )]
    public void Coverage_MatchesGaps() =>
        CodegenMatrixParts.AssertCoverage(
            CodegenConfigDimensions.Table,
            CodegenConfigDimensions.Rows,
            CodegenConfigDimensions.Gaps,
            CodegenConfigDimensions.Scope
        );

    [Fact(
        DisplayName = "[生成の構成/Errors] 網羅の配列の行を分けたクラスの和は、行とちょうど一致する（重ならず欠けない）"
    )]
    public void Parts_CoverEveryRowOnce()
    {
        var ids = CodegenMatrixParts
            .RowsOf(
                typeof(CodegenMatrixTests),
                typeof(CodegenMatrixTestsPart2),
                typeof(CodegenMatrixTestsPart3),
                typeof(CodegenMatrixTestsPart4)
            )
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeEquivalentTo(CodegenConfigDimensions.Rows.Select(row => row.Id));
    }

    [Fact(
        DisplayName = "[生成の構成] 入口の軸は作れ、4 つの入口を正本（CodegenEntrance）の並びで流す"
    )]
    public void EntranceAxis_IsComplete()
    {
        var axis = CodegenEntrances.Axis;

        axis.Adapters.Select(adapter => adapter.AxisValue).Should().Equal(CodegenEntrances.Names);
        axis.Excluded.Should().BeEmpty();
    }
}

/// <summary>Errors の表の 2 つ目の分（<see cref="CodegenMatrixParts"/>）</summary>
public sealed class CodegenMatrixTestsPart2
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.Rows, 1);

    [Theory(
        DisplayName = "[生成の構成/Errors] 網羅の配列の行は、診断・ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));
}

/// <summary>Errors の表の 3 つ目の分（<see cref="CodegenMatrixParts"/>）</summary>
public sealed class CodegenMatrixTestsPart3
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.Rows, 2);

    [Theory(
        DisplayName = "[生成の構成/Errors] 網羅の配列の行は、診断・ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));
}

/// <summary>Errors の表の 4 つ目の分（<see cref="CodegenMatrixParts"/>）</summary>
public sealed class CodegenMatrixTestsPart4
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.Rows, 3);

    [Theory(
        DisplayName = "[生成の構成/Errors] 網羅の配列の行は、診断・ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));
}

/// <summary>
/// 生成の構成のエンジンの Success の表（生成が通る構成の領域の網羅の配列。コンパイル・意味・探針・関係をこの表で数える）の
/// 1 つ目の分と、その網羅の照合・分け方・領域の述語のガード
/// </summary>
public sealed class CodegenSuccessMatrixTests
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.SuccessRows, 0);

    [Theory(
        DisplayName = "[生成の構成/Success] 生成が通る構成の行は、エラーなしで、ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);

    [Fact(
        DisplayName = "[生成の構成/Success] 網羅の配列の行は、領域の中の網羅すべき組をすべて含む（網羅しない組の一覧と一致）"
    )]
    public void Coverage_MatchesGaps() =>
        CodegenMatrixParts.AssertCoverage(
            CodegenConfigDimensions.SuccessTable,
            CodegenConfigDimensions.SuccessRows,
            CodegenConfigDimensions.SuccessGaps,
            CodegenConfigDimensions.SuccessScope
        );

    [Fact(
        DisplayName = "[生成の構成/Success] 網羅の配列の行を分けたクラスの和は、行とちょうど一致する（重ならず欠けない）"
    )]
    public void Parts_CoverEveryRowOnce()
    {
        var ids = CodegenMatrixParts
            .RowsOf(
                typeof(CodegenSuccessMatrixTests),
                typeof(CodegenSuccessMatrixTestsPart2),
                typeof(CodegenSuccessMatrixTestsPart3),
                typeof(CodegenSuccessMatrixTestsPart4)
            )
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().BeEquivalentTo(CodegenConfigDimensions.SuccessRows.Select(row => row.Id));
    }

    [Fact(
        DisplayName = "[生成の構成/Success] 領域の述語はエラーの規則が読む因子だけで決まる（全部の行で、そのまま評価した結果と読む因子だけに絞った結果が同じ）"
    )]
    public void DomainPredicate_ReadsOnlyRuleFactors()
    {
        var rows = CodegenConfigDimensions
            .Rows.Concat(CodegenConfigDimensions.SuccessRows)
            .Concat(CodegenConfigDimensions.NightlyRows)
            .Concat(CodegenConfigDimensions.NightlySuccessRows)
            .ToList();
        var mismatched = rows.Where(row =>
                CodegenSuccessDomain.PredictsSuccess(row)
                != CodegenSuccessDomain.PredictsSuccess(CodegenSuccessDomain.Project(row))
            )
            .Select(row => row.Describe())
            .ToList();

        mismatched.Should().BeEmpty("エラーの規則が読む因子の一覧（RuleFactors）に漏れがある");
        CodegenConfigDimensions
            .SuccessRows.Concat(CodegenConfigDimensions.NightlySuccessRows)
            .Should()
            .OnlyContain(row => CodegenSuccessDomain.PredictsSuccess(row));
        rows.Should()
            .Contain(
                row => !CodegenSuccessDomain.PredictsSuccess(row),
                "領域の外の行を Errors の表が流す"
            );
    }

    [Fact(DisplayName = "[生成の構成] 既知の失敗の一覧の項目は網羅の配列の行を指す")]
    public void KnownFailures_PointAtRows()
    {
        var rowIds = CodegenConfigDimensions
            .Rows.Concat(CodegenConfigDimensions.SuccessRows)
            .Concat(CodegenConfigDimensions.NightlyRows)
            .Concat(CodegenConfigDimensions.NightlySuccessRows)
            .Select(row => row.Id)
            .ToHashSet(StringComparer.Ordinal);

        CodegenKnownFailures
            .Set.Entries.Select(entry => entry.Scope)
            .Should()
            .OnlyContain(scope => rowIds.Contains(scope), "項目の範囲は網羅の配列の行の Id");
    }

    [Fact(
        DisplayName = "[生成の構成/Success] 普段の Success の行に、どの探針と（決まった行だけの関係を除く）どの関係も当たる行があり、決まった行だけの関係は決まった行に当たる"
    )]
    public void Probes_HaveRows()
    {
        var expectations = CodegenConfigDimensions
            .SuccessRows.Select(row =>
                CodegenRunner.MatrixExpectation(
                    new ShapeDiagram(row.Shape),
                    CodegenRunner.EffectiveOptions(row)
                )
            )
            .ToList();

        foreach (var probe in Enum.GetValues<Judges.CodegenProbe>())
        {
            expectations
                .Should()
                .Contain(expectation => expectation.Probes.Contains(probe), $"探針 {probe}");
        }

        foreach (var relation in Enum.GetValues<Judges.CodegenRelation>())
        {
            if (Judges.Relations.CasesOnly.Contains(relation))
            {
                expectations
                    .Should()
                    .NotContain(
                        expectation => expectation.Relations.Contains(relation),
                        $"関係 {relation} は決まった行だけに掛ける"
                    );
                CodegenCases
                    .Table.Should()
                    .Contain(
                        row =>
                            row.Input.Relations != null && row.Input.Relations.Contains(relation),
                        $"関係 {relation}"
                    );
            }
            else
            {
                expectations
                    .Should()
                    .Contain(
                        expectation => expectation.Relations.Contains(relation),
                        $"関係 {relation}"
                    );
            }
        }
    }
}

/// <summary>Success の表の 2 つ目の分（<see cref="CodegenMatrixParts"/>）</summary>
public sealed class CodegenSuccessMatrixTestsPart2
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.SuccessRows, 1);

    [Theory(
        DisplayName = "[生成の構成/Success] 生成が通る構成の行は、エラーなしで、ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);
}

/// <summary>Success の表の 3 つ目の分（<see cref="CodegenMatrixParts"/>）</summary>
public sealed class CodegenSuccessMatrixTestsPart3
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.SuccessRows, 2);

    [Theory(
        DisplayName = "[生成の構成/Success] 生成が通る構成の行は、エラーなしで、ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);
}

/// <summary>Success の表の 4 つ目の分（<see cref="CodegenMatrixParts"/>）</summary>
public sealed class CodegenSuccessMatrixTestsPart4
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.Ids(CodegenConfigDimensions.SuccessRows, 3);

    [Theory(
        DisplayName = "[生成の構成/Success] 生成が通る構成の行は、エラーなしで、ファイル構成・名前空間・コンパイル・意味・探針・関係の期待どおり"
    )]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);
}

/// <summary>
/// 夜間の Errors の網羅の配列（オプションの 15 因子を 1 つの 3 因子の集まりにした次元表）の行。夜間は全行、普段は分ける数だけ流す
/// </summary>
public sealed class CodegenNightlyMatrixTests
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlyRows,
            CodegenMatrixParts.NightlySeeds,
            0
        );

    [Theory(DisplayName = "[生成の構成/夜間/Errors] 夜間の網羅の配列の行は、規則の期待どおり")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));

    [Fact(
        DisplayName = "[生成の構成/夜間/Errors] 夜間の行を分けたクラスの和は、いまのプロファイルで流す行とちょうど一致する"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Parts_CoverEveryNightlyRowOnce()
    {
        var ids = CodegenMatrixParts
            .RowsOf(
                typeof(CodegenNightlyMatrixTests),
                typeof(CodegenNightlyMatrixTestsPart2),
                typeof(CodegenNightlyMatrixTestsPart3),
                typeof(CodegenNightlyMatrixTestsPart4)
            )
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should()
            .BeEquivalentTo(
                CodegenMatrixParts.PlannedNightlyIds(
                    CodegenConfigDimensions.NightlyRows,
                    CodegenMatrixParts.NightlySeeds
                )
            );
    }
}

/// <summary>夜間の Errors の行の 2 つ目の分</summary>
public sealed class CodegenNightlyMatrixTestsPart2
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlyRows,
            CodegenMatrixParts.NightlySeeds,
            1
        );

    [Theory(DisplayName = "[生成の構成/夜間/Errors] 夜間の網羅の配列の行は、規則の期待どおり")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));
}

/// <summary>夜間の Errors の行の 3 つ目の分</summary>
public sealed class CodegenNightlyMatrixTestsPart3
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlyRows,
            CodegenMatrixParts.NightlySeeds,
            2
        );

    [Theory(DisplayName = "[生成の構成/夜間/Errors] 夜間の網羅の配列の行は、規則の期待どおり")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));
}

/// <summary>夜間の Errors の行の 4 つ目の分</summary>
public sealed class CodegenNightlyMatrixTestsPart4
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlyRows,
            CodegenMatrixParts.NightlySeeds,
            3
        );

    [Theory(DisplayName = "[生成の構成/夜間/Errors] 夜間の網羅の配列の行は、規則の期待どおり")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_MatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id));
}

/// <summary>
/// 夜間の Success の網羅の配列（オプションの 15 因子の 3 因子網羅＋領域の述語）の行。夜間は全行、普段は分ける数だけ流す
/// </summary>
public sealed class CodegenNightlySuccessMatrixTests
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlySuccessRows,
            CodegenMatrixParts.NightlySuccessSeeds,
            0
        );

    [Theory(
        DisplayName = "[生成の構成/夜間/Success] 夜間の生成が通る構成の行は、エラーなしで規則の期待どおり"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);

    [Fact(
        DisplayName = "[生成の構成/夜間/Success] 夜間の行を分けたクラスの和は、いまのプロファイルで流す行とちょうど一致する"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Parts_CoverEveryNightlyRowOnce()
    {
        var ids = CodegenMatrixParts
            .RowsOf(
                typeof(CodegenNightlySuccessMatrixTests),
                typeof(CodegenNightlySuccessMatrixTestsPart2),
                typeof(CodegenNightlySuccessMatrixTestsPart3),
                typeof(CodegenNightlySuccessMatrixTestsPart4)
            )
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should()
            .BeEquivalentTo(
                CodegenMatrixParts.PlannedNightlyIds(
                    CodegenConfigDimensions.NightlySuccessRows,
                    CodegenMatrixParts.NightlySuccessSeeds
                )
            );
    }
}

/// <summary>夜間の Success の行の 2 つ目の分</summary>
public sealed class CodegenNightlySuccessMatrixTestsPart2
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlySuccessRows,
            CodegenMatrixParts.NightlySuccessSeeds,
            1
        );

    [Theory(
        DisplayName = "[生成の構成/夜間/Success] 夜間の生成が通る構成の行は、エラーなしで規則の期待どおり"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);
}

/// <summary>夜間の Success の行の 3 つ目の分</summary>
public sealed class CodegenNightlySuccessMatrixTestsPart3
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlySuccessRows,
            CodegenMatrixParts.NightlySuccessSeeds,
            2
        );

    [Theory(
        DisplayName = "[生成の構成/夜間/Success] 夜間の生成が通る構成の行は、エラーなしで規則の期待どおり"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);
}

/// <summary>夜間の Success の行の 4 つ目の分</summary>
public sealed class CodegenNightlySuccessMatrixTestsPart4
{
    /// <summary>このクラスが受け持つ行</summary>
    public static TheoryData<string> RowIds() =>
        CodegenMatrixParts.NightlyIds(
            CodegenConfigDimensions.NightlySuccessRows,
            CodegenMatrixParts.NightlySuccessSeeds,
            3
        );

    [Theory(
        DisplayName = "[生成の構成/夜間/Success] 夜間の生成が通る構成の行は、エラーなしで規則の期待どおり"
    )]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    [MemberData(nameof(RowIds))]
    public Task Row_GeneratesAndMatchesRules(string id) =>
        CodegenRunner.RunRowAsync(CodegenMatrixParts.Row(id), requireSuccess: true);
}
