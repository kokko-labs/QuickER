using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.Tests.Integration.Dialects;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// 値のプールの受ける水準の宣言（<see cref="ValuePoolUseDeclarations"/>）の、Integration で観測する部分（値の束縛の生成の表・述語の表・実装先どうしの一致の
/// 値の幅・SQL Server の系統の値の往復・方言の型の表の実 DB の段）の行列の照合（Docker を使わない＝観測は入力を作る関数を読むだけ）
/// </summary>
/// <remarks>
/// 宣言の構造・対象の網羅・受け持つ守りは QuickER.Tests の <c>ValuePoolUseTests</c> が照合する（受け持つ守りの照合の関数と影響の地図が QuickER.Tests にしか無い）。
/// </remarks>
public sealed class ValuePoolUseIntegrationTests(ITestOutputHelper output)
{
    /// <summary>Integration で観測する部分の表（行 Id は守りの名前）</summary>
    internal static ScenarioTable<Func<IReadOnlyList<ValueObservation>>> Parts { get; } =
        new(
            "ValuePools",
            [
                new(
                    "GeneratedRuntime/TranslatorParityBindings",
                    null,
                    "値の束縛の生成の表の全行の比べる値",
                    TranslatorParityBindingValueObservations.Observe
                ),
                new(
                    "table:TranslatorParityPredicates",
                    null,
                    "述語の表の全行の比べる値とシードの行の値",
                    TranslatorParityPredicateValueObservations.Observe
                ),
                new(
                    "GeneratedRuntime/ParityValues",
                    null,
                    "実装先どうしの一致の網羅の行と普段の既定の種の手が書く値",
                    BackendParityValueObservations.Observe
                ),
                new(
                    "GeneratedRuntime/ParityValuesSqlServer",
                    null,
                    "SQL Server の系統の値の往復の決まった形と普段の既定の種の書く値",
                    BackendParitySqlServerValueObservations.Observe
                ),
                new(
                    "Dialect/TypesRealDb",
                    null,
                    "方言の型の表の実 DB の段の書き込みの範囲と EF Core の手順の書く値",
                    DialectTypeRealDbValueObservations.Observe
                ),
            ]
        );

    /// <summary>観測する部分の Id</summary>
    public static TheoryData<string> PartIds() => Parts.Ids();

    [Fact(
        DisplayName = "[値のプール] Integration の観測の表は、宣言の Integration で観測する部分の一覧と同じ"
    )]
    public void Parts_MatchDeclaredList() =>
        Parts
            .Select(row => row.Id)
            .Should()
            .BeEquivalentTo(
                ValuePoolUseDeclarations.ObservedInIntegration,
                "Integration は宣言の一覧の部分をちょうど観測する"
            );

    [Theory(
        DisplayName = "[値のプール] Integration で観測する部分の行列は、観測した水準と受けない行がちょうど一方"
    )]
    [MemberData(nameof(PartIds))]
    public void ObservedPart_MatchesDeclaration(string id)
    {
        var subject = ValuePoolUseDeclarations.All.Single(subject => subject.Name == id);
        var matrix = ValuePoolObservedMatrix.From(Parts[id].Input());

        output.WriteLine(ValuePoolUseChecks.Render(subject, matrix));
        NotGeneratedGuard.Verify(
            $"{id} の行列が宣言と食い違う",
            ValuePoolUseChecks.MatrixProblems(subject, matrix)
        );
    }
}
