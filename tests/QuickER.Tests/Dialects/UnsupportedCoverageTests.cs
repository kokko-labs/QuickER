using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Dialects;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Dialects;

/// <summary>
/// 方言の宣言の「検出する」と、扱いの表の「なし」以外のマスが、それぞれを固定するテストに結び付いていることを確かめる
/// </summary>
/// <remarks>
/// <para>
/// 宣言と扱いの表はデータなので、書いた内容が実装と合っているかはデータだけでは分からない。宣言が「検出する」なのに取込が
/// 検出しない、表が「告知する」なのに告知しない、を防ぐため、セルとマスごとにそれを固定するテストの名前を
/// <see cref="UnsupportedCoverage"/> に持ち、名前のテストが実在することを確かめる（<c>tests/</c> の全 <c>.cs</c> を構文木で走査し、
/// クラス名とメソッド名の組で照合する。2 つのテストプロジェクトのどちらにあってもよい）。
/// </para>
/// <para>
/// まだ実装していないセルとマスは未実装の一覧に載せる。照合は厳密で、一覧にも登録にも無いセル・マスも、
/// 登録したのに一覧にも残っているセル・マスも赤にする。未実装の一覧は区切り 33 の 2d の終わりに空になる。
/// </para>
/// </remarks>
public sealed class UnsupportedCoverageTests
{
    [Fact(
        DisplayName = "宣言で検出する方言 × 種類は、検出のテストか未実装の一覧のどちらか一方にある"
    )]
    public void DetectedCells_AreCoveredOrPending()
    {
        var declared = (
            from dialect in DialectNames.All
            from kind in Enum.GetValues<UnsupportedDefinitionKind>()
            where DialectDeclarations.For(dialect, kind).Detection == ImportDetection.Detected
            select (dialect, kind)
        ).ToHashSet();
        var covered = UnsupportedCoverage.Detections.Select(d => (d.Dialect, d.Kind)).ToHashSet();
        var pending = UnsupportedCoverage
            .PendingDetections.Select(d => (d.Dialect, d.Kind))
            .ToHashSet();

        declared
            .Except(covered)
            .Except(pending)
            .Should()
            .BeEmpty("検出の宣言には検出のテストが要る");
        covered.Except(declared).Should().BeEmpty("検出しない宣言のセルに検出のテストを登録しない");
        pending.Except(declared).Should().BeEmpty("未実装の一覧は検出する宣言のセルだけ");
        covered.Intersect(pending).Should().BeEmpty("テストを登録したセルは未実装の一覧から消す");
    }

    [Fact(
        DisplayName = "扱いの表の「なし」以外のマスは、扱いのテストか未実装の一覧のどちらか一方にある"
    )]
    public void TreatmentCells_AreCoveredOrPending()
    {
        var cells = (
            from pair in UnsupportedTreatments.All
            from dialect in DialectNames.All
            where pair.Value.For(dialect) != UnsupportedTreatment.None
            select (pair.Key.Kind, pair.Key.Feature, Dialect: dialect)
        ).ToHashSet();
        var covered = UnsupportedCoverage
            .Treatments.SelectMany(t =>
                t.Dialect is null
                    ? DialectNames.All.Select(d => (t.Kind, t.Feature, Dialect: d))
                    : [(t.Kind, t.Feature, Dialect: t.Dialect)]
            )
            .Where(cells.Contains)
            .ToHashSet();
        var pending = UnsupportedCoverage
            .PendingTreatments.SelectMany(t =>
                t.Dialect is null
                    ? DialectNames.All.Select(d => (t.Kind, t.Feature, Dialect: d))
                    : [(t.Kind, t.Feature, Dialect: t.Dialect)]
            )
            .Where(cells.Contains)
            .ToHashSet();
        var registeredOnNone = UnsupportedCoverage
            .Treatments.Where(t =>
                t.Dialect is not null
                    ? UnsupportedTreatments.For(t.Kind, t.Feature, t.Dialect)
                        == UnsupportedTreatment.None
                    : DialectNames.All.All(d =>
                        UnsupportedTreatments.For(t.Kind, t.Feature, d) == UnsupportedTreatment.None
                    )
            )
            .Select(t => $"{t.Kind} {t.Feature} {t.Dialect ?? "all"}")
            .ToList();

        cells
            .Except(covered)
            .Except(pending)
            .Select(c => $"{c.Kind} {c.Feature} {c.Dialect}")
            .Should()
            .BeEmpty("扱いのマスには扱いのテストが要る");
        covered
            .Intersect(pending)
            .Select(c => $"{c.Kind} {c.Feature} {c.Dialect}")
            .Should()
            .BeEmpty("テストを登録したマスは未実装の一覧から消す");
        var pendingOnNone = UnsupportedCoverage
            .PendingTreatments.Where(t =>
                t.Dialect is not null
                    ? UnsupportedTreatments.For(t.Kind, t.Feature, t.Dialect)
                        == UnsupportedTreatment.None
                    : DialectNames.All.All(d =>
                        UnsupportedTreatments.For(t.Kind, t.Feature, d) == UnsupportedTreatment.None
                    )
            )
            .Select(t => $"{t.Kind} {t.Feature} {t.Dialect ?? "all"}")
            .ToList();

        registeredOnNone.Should().BeEmpty("「なし」のマスにテストを登録しない");
        pendingOnNone.Should().BeEmpty("「なし」のマスを未実装の一覧に載せない");
    }

    [Fact(DisplayName = "登録したテストの名前は tests/ に実在する（クラス名とメソッド名の組）")]
    public void RegisteredTests_Exist()
    {
        var declared = SourceFiles
            .Enumerate("tests")
            .Select(file => file.FullPath)
            .SelectMany(path =>
                ConditionalSourceParse
                    .DescendantNodes(
                        ConditionalSourceParse.Parse(
                            File.ReadAllText(path),
                            cancellationToken: TestContext.Current.CancellationToken
                        ),
                        TestContext.Current.CancellationToken
                    )
                    .OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Parent is TypeDeclarationSyntax)
                    .Select(method =>
                        $"{((TypeDeclarationSyntax)method.Parent!).Identifier.ValueText}.{method.Identifier.ValueText}"
                    )
            )
            .ToHashSet(StringComparer.Ordinal);

        var rowIds = ExpectationRowIds();
        var missing = UnsupportedCoverage
            .Detections.SelectMany(d => d.Tests)
            .Concat(UnsupportedCoverage.Treatments.SelectMany(t => t.Tests))
            .Where(test =>
                test.StartsWith(UnsupportedCoverage.ExpectationRowPrefix, StringComparison.Ordinal)
                    ? !rowIds.Any(id =>
                        Matches(test[UnsupportedCoverage.ExpectationRowPrefix.Length..], id)
                    )
                    : !declared.Contains(test)
            )
            .Distinct()
            .ToList();

        UnsupportedCoverage.Detections.Should().OnlyContain(d => d.Tests.Length > 0);
        UnsupportedCoverage.Treatments.Should().OnlyContain(t => t.Tests.Length > 0);
        missing.Should().BeEmpty("登録したテストは実在する");
    }

    /// <summary>
    /// 同期計画の期待の行の Id（<c>SyncPlanExpectationRows*.cs</c> の <c>Row("…", …)</c> の第 1 引数。補間した Id は先頭の文字列＋<c>*</c>）
    /// </summary>
    private static List<string> ExpectationRowIds()
    {
        var folder = Path.Combine(
            SourceFiles.RepositoryRoot,
            "tests",
            "QuickER.Tests.Integration",
            "Dialects",
            "SyncPlanFuzz"
        );
        var ids = new List<string>();

        foreach (var path in Directory.EnumerateFiles(folder, "SyncPlanExpectationRows*.cs"))
        {
            foreach (
                var call in ConditionalSourceParse
                    .DescendantNodes(
                        ConditionalSourceParse.Parse(
                            File.ReadAllText(path),
                            cancellationToken: TestContext.Current.CancellationToken
                        ),
                        TestContext.Current.CancellationToken
                    )
                    .OfType<InvocationExpressionSyntax>()
                    .Where(c =>
                        c.Expression is IdentifierNameSyntax { Identifier.ValueText: "Row" }
                    )
            )
            {
                switch (call.ArgumentList.Arguments.FirstOrDefault()?.Expression)
                {
                    case LiteralExpressionSyntax literal:
                        ids.Add(literal.Token.ValueText);
                        break;
                    case InterpolatedStringExpressionSyntax interpolated:
                        ids.Add(
                            string.Concat(
                                interpolated
                                    .Contents.TakeWhile(c => c is InterpolatedStringTextSyntax)
                                    .Select(c =>
                                        ((InterpolatedStringTextSyntax)c).TextToken.ValueText
                                    )
                            ) + "*"
                        );
                        break;
                }
            }
        }

        ids.Should().NotBeEmpty("期待の行の Id を読めている");
        return ids;
    }

    /// <summary>
    /// 登録の Id（末尾の <c>*</c> は前方一致）が行の Id に当たるか。補間した行の Id（末尾が <c>*</c>）は、登録が同じ先頭で始まれば当たる
    /// </summary>
    private static bool Matches(string registered, string id) =>
        registered.EndsWith('*')
            ? id.TrimEnd('*').StartsWith(registered.TrimEnd('*'), StringComparison.Ordinal)
                || registered.TrimEnd('*').StartsWith(id.TrimEnd('*'), StringComparison.Ordinal)
                    && id.EndsWith('*')
        : id.EndsWith('*') ? registered.StartsWith(id.TrimEnd('*'), StringComparison.Ordinal)
        : registered == id;
}
