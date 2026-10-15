using AwesomeAssertions;
using QuickER.Dialects;

namespace QuickER.Tests.Dialects;

/// <summary>
/// 種類の一覧・方言の宣言・扱いの表が全部埋まっていて、組み合わせの決まりを守っていることを固定する（規則 2）
/// </summary>
/// <remarks>
/// <para>
/// 宣言と扱いの表は既定値を持たない。種類を足して宣言や扱いを書き忘れると、ここが欠けを名指しして赤になる。
/// </para>
/// <para>
/// 組み合わせの決まり：あり得ない種類は検出も同期も該当しない／検出しないなら理由を書く／向き 2 の種類は検出も同期も該当しない／
/// 取込の扱いは、検出する方言でだけ告知する（告知しない除外は、あり得て検出しない方言だけ）／機能ごとに使える扱いだけを使う。
/// </para>
/// </remarks>
public sealed class UnsupportedDefinitionExhaustiveTests
{
    private static readonly UnsupportedDefinitionKind[] Kinds =
        Enum.GetValues<UnsupportedDefinitionKind>();

    private static readonly UnsupportedFeature[] Features = Enum.GetValues<UnsupportedFeature>();

    [Fact(DisplayName = "種類の一覧：すべての種類に ID・向き・印があり、ID は重ならない")]
    public void Kinds_AllHaveInfo()
    {
        UnsupportedDefinitionKinds.All.Keys.Should().BeEquivalentTo(Kinds);
        UnsupportedDefinitionKinds
            .All.Values.Select(info => info.Id)
            .Should()
            .OnlyHaveUniqueItems()
            .And.OnlyContain(id => !string.IsNullOrWhiteSpace(id));
    }

    [Fact(DisplayName = "方言の宣言：5 方言 × すべての種類が埋まっている")]
    public void Declarations_CoverAllDialectsAndKinds()
    {
        DialectDeclarations.All.Keys.Should().BeEquivalentTo(DialectNames.All);

        var missing = DialectNames
            .All.SelectMany(dialect =>
                Kinds
                    .Where(kind => !DialectDeclarations.All[dialect].ContainsKey(kind))
                    .Select(kind => $"{dialect} {UnsupportedDefinitionKinds.Info(kind).Id}")
            )
            .ToList();

        missing.Should().BeEmpty("方言の宣言は既定値を持たず、全方言 × 全種類を書く");
    }

    [Fact(
        DisplayName = "方言の宣言：組み合わせの決まり（あり得ない・検出しない理由・向き 2）を守る"
    )]
    public void Declarations_FollowRules()
    {
        var violations = new List<string>();

        foreach (var dialect in DialectNames.All)
        {
            foreach (var kind in Kinds)
            {
                var info = UnsupportedDefinitionKinds.Info(kind);
                var declaration = DialectDeclarations.For(dialect, kind);
                var place = $"{dialect} {info.Id}";

                if (
                    declaration.Occurrence == DialectOccurrence.Impossible
                    && (
                        declaration.Detection != ImportDetection.NotApplicable
                        || declaration.SyncEffect != SyncAlterEffect.NotApplicable
                    )
                )
                {
                    violations.Add($"{place}: あり得ないのに検出・同期が該当する");
                }

                if (
                    info.Direction == UnsupportedDirection.InDiagramOnly
                    && (
                        declaration.Detection != ImportDetection.NotApplicable
                        || declaration.SyncEffect != SyncAlterEffect.NotApplicable
                    )
                )
                {
                    violations.Add($"{place}: 向き 2 なのに検出・同期が該当する");
                }

                var needsReason = declaration.Detection == ImportDetection.NotDetected;

                if (needsReason != !string.IsNullOrWhiteSpace(declaration.NotDetectedReason))
                {
                    violations.Add($"{place}: 検出しない理由の有無が検出の宣言と合わない");
                }
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact(DisplayName = "扱いの表：すべての種類 × 機能が埋まり、機能ごとに使える扱いだけを使う")]
    public void Treatments_CoverAllCellsWithAllowedValues()
    {
        var violations = new List<string>();

        foreach (var kind in Kinds)
        {
            foreach (var feature in Features)
            {
                var place = $"{UnsupportedDefinitionKinds.Info(kind).Id} {feature}";

                if (!UnsupportedTreatments.All.TryGetValue((kind, feature), out _))
                {
                    violations.Add($"{place}: マスが無い");
                    continue;
                }

                foreach (var dialect in DialectNames.All)
                {
                    var treatment = UnsupportedTreatments.For(kind, feature, dialect);

                    if (!UnsupportedTreatments.AllowedFor(feature).Contains(treatment))
                    {
                        violations.Add($"{place} {dialect}: {treatment} はこの機能では使えない");
                    }
                }
            }
        }

        UnsupportedTreatments.All.Should().HaveCount(Kinds.Length * Features.Length);
        violations.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "扱いの表：取込で告知するのは検出する方言だけ、告知しない除外はあり得て検出しない方言だけ"
    )]
    public void ImportTreatments_FollowDeclarations()
    {
        var violations = new List<string>();

        foreach (var kind in Kinds)
        {
            foreach (var dialect in DialectNames.All)
            {
                var treatment = UnsupportedTreatments.For(kind, UnsupportedFeature.Import, dialect);
                var declaration = DialectDeclarations.For(dialect, kind);
                var place = $"{UnsupportedDefinitionKinds.Info(kind).Id} {dialect}";
                var silent = treatment == UnsupportedTreatment.ExcludeSilently;
                var notices = treatment != UnsupportedTreatment.None && !silent;

                if (notices && declaration.Detection != ImportDetection.Detected)
                {
                    violations.Add($"{place}: 検出しないのに取込で {treatment}");
                }

                if (
                    silent
                    && (
                        declaration.Occurrence == DialectOccurrence.Impossible
                        || declaration.Detection != ImportDetection.NotDetected
                    )
                )
                {
                    violations.Add($"{place}: 告知しない除外は、あり得て検出しない方言だけ");
                }
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "扱いの表：あり得ない方言の向き 1 の種類は、取込・差分・同期・DDL で何もしない"
    )]
    public void Treatments_AreNoneWhereKindIsImpossible()
    {
        UnsupportedFeature[] runtime =
        [
            UnsupportedFeature.Import,
            UnsupportedFeature.Diff,
            UnsupportedFeature.Sync,
            UnsupportedFeature.Ddl,
        ];
        var violations = (
            from kind in Kinds
            where
                UnsupportedDefinitionKinds.Info(kind).Direction
                == UnsupportedDirection.InDatabaseOnly
            from dialect in DialectNames.All
            where DialectDeclarations.For(dialect, kind).Occurrence == DialectOccurrence.Impossible
            from feature in runtime
            let treatment = UnsupportedTreatments.For(kind, feature, dialect)
            where treatment != UnsupportedTreatment.None
            select $"{UnsupportedDefinitionKinds.Info(kind).Id} {feature} {dialect}: {treatment}"
        ).ToList();

        violations.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "扱いの表：方言に依らない機能（C# 生成・出力・ツール・リバース）は 5 方言で同じ扱い"
    )]
    public void DialectIndependentFeatures_AreSameForAllDialects()
    {
        UnsupportedFeature[] independent =
        [
            UnsupportedFeature.CSharpGeneration,
            UnsupportedFeature.Export,
            UnsupportedFeature.ToolHosts,
            UnsupportedFeature.Reverse,
        ];
        var violations = (
            from kind in Kinds
            from feature in independent
            let cell = UnsupportedTreatments.All[(kind, feature)]
            where cell != TreatmentCell.Same(cell.SqlServer)
            select $"{UnsupportedDefinitionKinds.Info(kind).Id} {feature}"
        ).ToList();

        violations.Should().BeEmpty();
    }
}
