using System.IO;
using AwesomeAssertions;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.Docs;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.TestSupport;

/// <summary>QuickER.Tests で観測する部分 1 つ（守りの名前と観測の関数）</summary>
/// <param name="Observe">観測の関数（DB を使わない・乱数を新しく引かない）</param>
public sealed record ValuePoolObservedPart(
    Func<CancellationToken, Task<IReadOnlyList<ValueObservation>>> Observe
);

/// <summary>
/// 値のプールの受ける水準の宣言（<see cref="ValuePoolUseDeclarations"/>）の照合：宣言の構造・対象が両アセンブリの台帳とちょうど一致・受け持つ守りの実在・
/// 観測の網羅と、QuickER.Tests で観測する部分（簡易 DSL の総当たりの 5 部分・インターセプターの表 2 つ・翻訳器の変換の表）の行列
/// </summary>
/// <remarks>
/// 行列は観測した水準と受けない行がちょうど一方であることを部分ごとに照合し、行列をテストの出力へ書く。Integration で観測する部分は Integration の
/// <c>ValuePoolUseIntegrationTests</c> が同じ照合を流す。
/// </remarks>
public sealed class ValuePoolUseTests(ITestOutputHelper output)
{
    /// <summary>QuickER.Tests で観測する部分の表（行 Id は守りの名前）</summary>
    internal static ScenarioTable<ValuePoolObservedPart> Parts { get; } =
        new(
            "ValuePools",
            [
                .. DslFuzzValueObservations.Parts.Select(
                    part => new ScenarioRow<ValuePoolObservedPart>(
                        part.Key,
                        null,
                        $"簡易 DSL の総当たり {part.Key} の普段の既定の種と網羅の単位の行の値・リテラル・パラメータ",
                        new(_ => Task.FromResult(part.Value()))
                    )
                ),
                new(
                    "table:SqlServerQueryParameters",
                    null,
                    "SQL Server のインターセプターの表の行の操作のパラメータの値（インターセプターを外して流す）",
                    new(SqlServerQueryParameterInterceptorTests.ObserveValuesAsync)
                ),
                new(
                    "table:PostgreSqlQueryParameters",
                    null,
                    "PostgreSQL のインターセプターの表の行の操作のパラメータの値（インターセプターを外して流す）",
                    new(PostgreSqlQueryParameterInterceptorTests.ObserveValuesAsync)
                ),
                new(
                    "table:TranslatorConversions",
                    null,
                    "翻訳器の変換の表の述語の比べる値と束縛の行の値",
                    new(_ =>
                        Task.FromResult(SqlExpressionTranslatorConversionTests.ObserveValues())
                    )
                ),
            ]
        );

    /// <summary>観測する部分の Id</summary>
    public static TheoryData<string> PartIds() => Parts.Ids();

    [Fact(DisplayName = "[値のプール] 受ける水準の宣言は構造の問題が無い")]
    public void Declarations_AreWellFormed() =>
        NotGeneratedGuard.Verify(
            "値のプールの受ける水準の宣言が整っていない",
            ValuePoolUseChecks.Problems(ValuePoolUseDeclarations.All)
        );

    [Fact(
        DisplayName = "[値のプール] 宣言の対象は、両アセンブリのエンジンの台帳の全部の部分と単体の表の台帳の全部の表にちょうど一致する"
    )]
    public void Declarations_CoverEveryRegisteredPartOnce()
    {
        var snapshot = ProtectionSnapshot.Parse(
            File.ReadAllText(
                Path.Combine(EngineRepository.Root(), ProtectionSnapshot.IntegrationPath)
            )
        );
        var expected = EngineRegistry
            .All.Select(descriptor => ProtectionNames.EnginePart(descriptor.Id, descriptor.Part))
            .Concat(
                TableRegistry.All.Select(table =>
                    ProtectionNames.Of(ProtectionKind.Table, table.Name)
                )
            )
            .Concat(
                snapshot.Engines.Select(engine =>
                    ProtectionNames.EnginePart(engine.Id, engine.Part)
                )
            )
            .Concat(
                snapshot.Tables.Select(table =>
                    ProtectionNames.Of(ProtectionKind.Table, table.Name)
                )
            )
            .ToList();

        ValuePoolUseDeclarations
            .All.Select(subject => subject.Name)
            .Should()
            .BeEquivalentTo(
                expected,
                "台帳の部分と表はどれもちょうど 1 回宣言する（値を受けるか、受けない理由）"
            );
    }

    [Fact(
        DisplayName = "[値のプール] 受けない水準の行の受け持つ守りに書いた名前と行 Id は実在する"
    )]
    public void Guards_ReferToExistingProtections() =>
        NotGeneratedGuard.Verify(
            "受けない水準の行の受け持つ守りが実在しない（CLAUDE.md の「ガード：」と同じ書き方で書く。どこでも見ていなければ NotGenerated.Unguarded）",
            ValuePoolUseChecks.ReferenceProblems(
                ValuePoolUseDeclarations.All,
                NotGeneratedGuardTests.GuardChecker()
            )
        );

    [Fact(
        DisplayName = "[値のプール] 値を受ける部分はどれも、どちらかのアセンブリで観測するか観測できない理由を持ち、QuickER.Tests の観測の表は宣言の一覧と同じ"
    )]
    public void ValueReceivingParts_AreObservedSomewhere()
    {
        NotGeneratedGuard.Verify(
            "値を受ける部分の観測の網羅が崩れている",
            ValuePoolUseChecks.ObservationCoverageProblems(
                ValuePoolUseDeclarations.All,
                ValuePoolUseDeclarations.ObservedInUnitTests,
                ValuePoolUseDeclarations.ObservedInIntegration,
                ValuePoolUseDeclarations.Unobserved
            )
        );
        Parts
            .Select(row => row.Id)
            .Should()
            .BeEquivalentTo(
                ValuePoolUseDeclarations.ObservedInUnitTests,
                "QuickER.Tests は宣言の一覧の部分をちょうど観測する"
            );
    }

    [Fact(DisplayName = "[値のプール] 宣言のファイルは、CLAUDE.md の照合の行 Id の本文に入らない")]
    public void DeclarationFile_IsLeftOutOfRowIdText()
    {
        var path = Path.Combine(
            EngineRepository.Root(),
            "tests/QuickER.Tests/TestSupport/ValuePools",
            ValuePoolUseChecks.DeclarationFileName
        );

        ClaudeMdReferenceTests
            .Sources.Value.AllText.Contains(File.ReadAllText(path), StringComparison.Ordinal)
            .Should()
            .BeFalse(
                "受け持つ守りに書いた行 Id が宣言そのものに当たって実在に見えないよう、名前で外す"
            );
    }

    [Theory(
        DisplayName = "[値のプール] QuickER.Tests で観測する部分の行列は、観測した水準と受けない行がちょうど一方"
    )]
    [MemberData(nameof(PartIds))]
    public async Task ObservedPart_MatchesDeclaration(string id)
    {
        var subject = ValuePoolUseDeclarations.All.Single(subject => subject.Name == id);
        var observations = await Parts[id].Input.Observe(TestContext.Current.CancellationToken);
        var matrix = ValuePoolObservedMatrix.From(observations);

        output.WriteLine(ValuePoolUseChecks.Render(subject, matrix));
        NotGeneratedGuard.Verify(
            $"{id} の行列が宣言と食い違う",
            ValuePoolUseChecks.MatrixProblems(subject, matrix)
        );
    }
}
