using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 差分と同期が読む能力の宣言（<see cref="SyncDialectCapabilities"/>）が、扱いの表（<see cref="UnsupportedTreatments"/>）と
/// 食い違わないことを固定する
/// </summary>
/// <remarks>
/// <para>
/// 差分の「比較で畳む」「案内項目」と、同期の列順の扱いは、今は能力の宣言のフラグが決めている。フラグと扱いの表の片方だけを
/// 変えると、表は畳むと言うのに差分が出る（またはその逆）形で静かに食い違うので、方言ごとに対応を照合する。
/// </para>
/// <para>
/// 表が区切り 33 の終わりの姿を書き、フラグがまだ今の姿のマスは <see cref="Pending"/> に載せ、直したフェーズで消す。
/// 照合は厳密で、一覧にあるのに食い違わないマスも赤にする。
/// </para>
/// </remarks>
public sealed class CapabilityTreatmentParityTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>
    /// フラグ 1 つと表のマス 1 つの対応（フラグの値から、表が持つべき扱いを決める）
    /// </summary>
    private static readonly (
        string Flag,
        UnsupportedDefinitionKind Kind,
        UnsupportedFeature Feature,
        Func<SyncDialectCapabilities, UnsupportedTreatment> Expected
    )[] Rules =
    [
        (
            nameof(SyncDialectCapabilities.SupportsOnUpdateAction),
            UnsupportedDefinitionKind.OnUpdateReferentialAction,
            UnsupportedFeature.Diff,
            c => c.SupportsOnUpdateAction ? UnsupportedTreatment.None : UnsupportedTreatment.Fold
        ),
        (
            nameof(SyncDialectCapabilities.SupportsSetDefaultAction),
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Diff,
            c => c.SupportsSetDefaultAction ? UnsupportedTreatment.None : UnsupportedTreatment.Fold
        ),
        (
            nameof(SyncDialectCapabilities.SupportsDescriptions),
            UnsupportedDefinitionKind.Description,
            UnsupportedFeature.Diff,
            c => c.SupportsDescriptions ? UnsupportedTreatment.None : UnsupportedTreatment.Fold
        ),
        (
            nameof(SyncDialectCapabilities.PersistsForeignKeyConstraintNames),
            UnsupportedDefinitionKind.ForeignKeyConstraintName,
            UnsupportedFeature.Diff,
            c =>
                c.PersistsForeignKeyConstraintNames
                    ? UnsupportedTreatment.None
                    : UnsupportedTreatment.Fold
        ),
        (
            nameof(SyncDialectCapabilities.ReferencedKeyRequiresSameColumnOrder),
            UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder,
            UnsupportedFeature.Ddl,
            c =>
                c.ReferencedKeyRequiresSameColumnOrder
                    ? UnsupportedTreatment.Reorder
                    : UnsupportedTreatment.None
        ),
        (
            nameof(SyncDialectCapabilities.ReferencedKeyRequiresSameColumnOrder),
            UnsupportedDefinitionKind.CompositeForeignKeyColumnOrder,
            UnsupportedFeature.Sync,
            c =>
                c.ReferencedKeyRequiresSameColumnOrder
                    ? UnsupportedTreatment.Reorder
                    : UnsupportedTreatment.None
        ),
        (
            nameof(SyncDialectCapabilities.ForeignKeyAllowsStringLengthDifference),
            UnsupportedDefinitionKind.ForeignKeyTypeMismatch,
            UnsupportedFeature.Diff,
            c =>
                c.ForeignKeyAllowsStringLengthDifference
                    ? UnsupportedTreatment.Fold
                    : UnsupportedTreatment.None
        ),
        (
            nameof(SyncDialectCapabilities.SetDefaultRequiresColumnDefault),
            UnsupportedDefinitionKind.SetDefaultReferentialAction,
            UnsupportedFeature.Sync,
            c =>
                c.SetDefaultRequiresColumnDefault ? UnsupportedTreatment.Warning
                : c.SupportsSetDefaultAction ? UnsupportedTreatment.None
                : UnsupportedTreatment.OmitWithAnnotation
        ),
        (
            nameof(SyncDialectCapabilities.SupportsSchemaQualifiedTableNames),
            UnsupportedDefinitionKind.SchemaQualifiedTableName,
            UnsupportedFeature.Sync,
            c =>
                c.SupportsSchemaQualifiedTableNames
                    ? UnsupportedTreatment.None
                    : UnsupportedTreatment.Warning
        ),
        (
            nameof(SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey),
            UnsupportedDefinitionKind.UniqueConstraintOnPrimaryKey,
            UnsupportedFeature.Diff,
            c =>
                c.SupportsUniqueConstraintOnPrimaryKey
                    ? UnsupportedTreatment.None
                    : UnsupportedTreatment.Fold
        ),
        (
            nameof(SyncDialectCapabilities.SupportsComputedColumnTypeChange),
            UnsupportedDefinitionKind.ComputedColumnTypeChange,
            UnsupportedFeature.Diff,
            c =>
                c.SupportsComputedColumnTypeChange
                    ? UnsupportedTreatment.None
                    : UnsupportedTreatment.GuideItem
        ),
        (
            nameof(SyncDialectCapabilities.ColumnReorder),
            UnsupportedDefinitionKind.ColumnOrder,
            UnsupportedFeature.Diff,
            c =>
                c.ColumnReorder == ColumnReorderMode.None
                    ? UnsupportedTreatment.GuideItem
                    : UnsupportedTreatment.None
        ),
        (
            nameof(SyncDialectCapabilities.ColumnReorder),
            UnsupportedDefinitionKind.ColumnOrder,
            UnsupportedFeature.Sync,
            c =>
                c.ColumnReorder switch
                {
                    ColumnReorderMode.Native => UnsupportedTreatment.HandledNatively,
                    ColumnReorderMode.Rebuild => UnsupportedTreatment.HandledByRebuild,
                    _ => UnsupportedTreatment.None,
                }
        ),
    ];

    /// <summary>
    /// フラグ 1 つと表のマス 1 つの対応のうち、フラグの値からマスが 1 つに決まらないもの（フラグの値から、表が持ってよい
    /// 扱いの集合を決める）
    /// </summary>
    /// <remarks>
    /// 既定値の同期のマスは、同期が既定値を能動的に読み直して保つ方言（SQL Server・MySQL）を <c>Preserve</c>、DB が
    /// 型の変更で既定値を保ち、計画の事前の警告だけが扱いになる方言（PostgreSQL・Oracle）を <c>Warning</c> と書き分ける。
    /// どちらも <see cref="SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange"/> が <c>true</c> の姿なので、フラグからは
    /// 2 つのどちらかとしか言えない。<c>false</c> は再構築で既定値を落とす方言で、そのマスは再構築の警告になる。
    /// </remarks>
    private static readonly (
        string Flag,
        UnsupportedDefinitionKind Kind,
        UnsupportedFeature Feature,
        Func<SyncDialectCapabilities, UnsupportedTreatment[]> Allowed
    )[] SetRules =
    [
        (
            nameof(SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange),
            UnsupportedDefinitionKind.DefaultValue,
            UnsupportedFeature.Sync,
            c =>
                c.ChecksDefaultOnColumnTypeChange
                    ? [UnsupportedTreatment.Preserve, UnsupportedTreatment.Warning]
                : c.ColumnReorder == ColumnReorderMode.Rebuild ? [UnsupportedTreatment.Warning]
                : [UnsupportedTreatment.None]
        ),
    ];

    /// <summary>表が終わりの姿を書き、フラグがまだ今の姿のマス（方言・フラグ・直すフェーズ）</summary>
    private static readonly (string Dialect, string Flag, string Phase)[] Pending = [];

    [Fact(
        DisplayName = "能力の宣言のフラグと扱いの表のマスが、5 方言で食い違わない（未実装の一覧を除く）"
    )]
    public void Capabilities_MatchTreatmentTable()
    {
        var mismatches = (
            from provider in Providers
            from rule in Rules
            let expected = rule.Expected(provider.SyncCapabilities)
            let actual = UnsupportedTreatments.For(rule.Kind, rule.Feature, provider.Name)
            where expected != actual
            select (
                Dialect: provider.Name,
                rule.Flag,
                Detail: $"{rule.Kind} {rule.Feature}: フラグからは {expected}・表は {actual}"
            )
        )
            .Concat(
                from provider in Providers
                from rule in SetRules
                let allowed = rule.Allowed(provider.SyncCapabilities)
                let actual = UnsupportedTreatments.For(rule.Kind, rule.Feature, provider.Name)
                where !allowed.Contains(actual)
                select (
                    Dialect: provider.Name,
                    rule.Flag,
                    Detail: $"{rule.Kind} {rule.Feature}: フラグからは {string.Join(" か ", allowed)}・表は {actual}"
                )
            )
            .ToList();

        var unexpected = mismatches
            .Where(m => !Pending.Any(p => p.Dialect == m.Dialect && p.Flag == m.Flag))
            .Select(m => $"{m.Dialect} {m.Flag} {m.Detail}")
            .ToList();
        var stalePending = Pending
            .Where(p => !mismatches.Any(m => m.Dialect == p.Dialect && m.Flag == p.Flag))
            .Select(p => $"{p.Dialect} {p.Flag}（{p.Phase}）")
            .ToList();

        unexpected.Should().BeEmpty("フラグと扱いの表の片方だけを変えない");
        stalePending.Should().BeEmpty("食い違いが無くなったマスは未実装の一覧から消す");
    }
}
