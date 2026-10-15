using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 取込警告の種別と、図で表せない定義の種類の対応（<see cref="SchemaImportWarningKinds"/>）を両方向で固定する
/// </summary>
/// <remarks>
/// <para>
/// (1) すべての種別が対応を持つ（種類の外は 3 種別だけ）。(2) 種別が告げる種類は、取込で告知する方言を持つ。
/// (3) 取込で告知する種類は、それを告げる種別を持つ。区切り 33 で検出と一緒に種別を足す種類は <see cref="PendingKinds"/> に載せ、
/// 足したフェーズで消す（照合は厳密）。
/// </para>
/// </remarks>
public sealed class SchemaImportWarningKindMapTests
{
    /// <summary>種類の外の種別（取込範囲の外・名前の衝突）</summary>
    private static readonly SchemaImportWarningKind[] OutsideKinds =
    [
        SchemaImportWarningKind.ForeignKeyOutsideScope,
        SchemaImportWarningKind.TableNameCollision,
        SchemaImportWarningKind.ColumnNameCollision,
    ];

    /// <summary>取込で告知するが、告げる種別がまだ無い種類（足すフェーズ）</summary>
    private static readonly (UnsupportedDefinitionKind Kind, string Phase)[] PendingKinds =
    [
        (UnsupportedDefinitionKind.IdentityRejectingValues, "2d"),
    ];

    [Fact(
        DisplayName = "すべての取込警告の種別が対応を持ち、種類の外は取込範囲の外と名前の衝突の 3 種別だけ"
    )]
    public void EveryWarningKind_IsMapped()
    {
        var outside = Enum.GetValues<SchemaImportWarningKind>()
            .Where(kind => SchemaImportWarningKinds.DefinitionKind(kind) is null)
            .ToList();

        outside.Should().BeEquivalentTo(OutsideKinds);
    }

    [Fact(DisplayName = "種別が告げる種類は、取込で告知する方言を持つ")]
    public void MappedKinds_AreNoticedOnImport()
    {
        var silent = Enum.GetValues<SchemaImportWarningKind>()
            .Select(SchemaImportWarningKinds.DefinitionKind)
            .OfType<UnsupportedDefinitionKind>()
            .Where(kind => !NoticedSomewhere(kind))
            .ToList();

        silent.Should().BeEmpty();
    }

    [Fact(DisplayName = "取込で告知する種類は、告げる種別を持つ（未実装の一覧を除く）")]
    public void NoticedKinds_HaveWarningKinds()
    {
        var mapped = Enum.GetValues<SchemaImportWarningKind>()
            .Select(SchemaImportWarningKinds.DefinitionKind)
            .OfType<UnsupportedDefinitionKind>()
            .ToHashSet();
        var withoutWarning = Enum.GetValues<UnsupportedDefinitionKind>()
            .Where(NoticedSomewhere)
            .Where(kind => !mapped.Contains(kind))
            .ToHashSet();
        var pending = PendingKinds.Select(p => p.Kind).ToHashSet();

        withoutWarning.Except(pending).Should().BeEmpty("取込で告知する種類には告げる種別が要る");
        pending.Except(withoutWarning).Should().BeEmpty("種別を足した種類は未実装の一覧から消す");
    }

    [Fact(
        DisplayName = "まとめて告げる種別（段 2）は、扱いの表の取込が「外す＋まとめて告知」の種類と一致する"
    )]
    public void AggregatedKinds_MatchTreatmentTable()
    {
        var mismatched = Enum.GetValues<SchemaImportWarningKind>()
            .Where(kind => SchemaImportWarningKinds.DefinitionKind(kind) is not null)
            .Where(kind =>
            {
                var definition = SchemaImportWarningKinds.DefinitionKind(kind)!.Value;
                var treatments = DialectNames
                    .All.Select(dialect =>
                        UnsupportedTreatments.For(definition, UnsupportedFeature.Import, dialect)
                    )
                    .Where(t => t != UnsupportedTreatment.None)
                    .ToList();
                var aggregatedInTable =
                    treatments.Count > 0
                    && treatments.All(t => t == UnsupportedTreatment.ExcludeWithAggregatedNotice);
                return SchemaImportWarningKinds.IsAggregated(kind) != aggregatedInTable;
            })
            .ToList();

        mismatched.Should().BeEmpty("表示の段は扱いの表の取込のマスに従う");
        Enum.GetValues<SchemaImportWarningKind>()
            .Where(kind => SchemaImportWarningKinds.DefinitionKind(kind) is null)
            .Should()
            .OnlyContain(
                kind => !SchemaImportWarningKinds.IsAggregated(kind),
                "種類の外の種別（範囲の外・名前の衝突）は箇所ごとの文で告げる"
            );
    }

    [Fact(
        DisplayName = "別名型（SQL Server）とドメイン型（PostgreSQL）はどちらも D1a（基底型へ平坦化される型）へ写る"
    )]
    public void AliasAndDomainTypes_MapToFlattenedType()
    {
        SchemaImportWarningKinds
            .DefinitionKind(SchemaImportWarningKind.AliasTypeFlattened)
            .Should()
            .Be(UnsupportedDefinitionKind.FlattenedType);
        SchemaImportWarningKinds
            .DefinitionKind(SchemaImportWarningKind.DomainTypeFlattened)
            .Should()
            .Be(UnsupportedDefinitionKind.FlattenedType);
    }

    /// <summary>取込で（告知しない除外以外の）扱いを持つ方言が 1 つでもあるか</summary>
    private static bool NoticedSomewhere(UnsupportedDefinitionKind kind) =>
        DialectNames.All.Any(dialect =>
            UnsupportedTreatments.For(kind, UnsupportedFeature.Import, dialect)
                is not UnsupportedTreatment.None
                    and not UnsupportedTreatment.ExcludeSilently
        );
}
