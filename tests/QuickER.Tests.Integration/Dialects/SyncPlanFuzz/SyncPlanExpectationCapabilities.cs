using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 期待の行（<see cref="SyncPlanExpectationRows"/>）が流す能力の宣言の一覧（実方言 5 つと、名前つきの合成の能力）
/// </summary>
/// <remarks>
/// <para>
/// 実方言の鍵は <see cref="DialectNames"/> の値で、行は網の派生（<see cref="SyncPlanCapabilityNetTestsBase"/>）が自分の方言の行として
/// 流し、静的な規則（<see cref="SyncPlanStaticRules"/>）も当てる。合成の能力は、どの実方言とも一致しない能力の組み合わせで、
/// 計画の分岐を 1 つずつ確かめる行が使う（静的な規則は当てない＝方言が無い）。
/// </para>
/// <para>
/// 合成の能力はこのファイルの 1 か所にだけ書き、行の側で <c>with { … }</c> を書かない。それぞれ、近い実方言と、その実方言と
/// 違う宣言をコメントに書く（近い実方言で流して期待がそのまま合う行は、実方言の行にする）。
/// </para>
/// </remarks>
internal static class SyncPlanExpectationCapabilities
{
    /// <summary>
    /// どの方言の制約も持たない宣言（<see cref="TestDialectCapabilities.Neutral"/>）。近い実方言は SQL Server で、違う宣言は
    /// AlterColumnRequiresForeignKeyRebuild・AlterColumnRequiresKeyConstraintRebuild（偽）・SetDefaultRequiresColumnDefault・
    /// SetNullRequiresNullableChildColumn（偽）・ChecksDefaultOnColumnTypeChange（偽）・型の正規化（恒等）・識別子の上限（なし）
    /// </summary>
    public const string Neutral = "Neutral";

    /// <summary>
    /// <see cref="Neutral"/> から説明の同期だけを外したもの。近い実方言は無い（説明を持たないのは SQLite だけで、SQLite は
    /// 再構築の方言）。能力の差で計画が変わらないことを確かめる行が使う
    /// </summary>
    public const string NeutralWithoutDescriptions = "Neutral-NoDescriptions";

    /// <summary>
    /// 外部キーの制約名を保存しない <see cref="Neutral"/>。近い実方言は SQLite で、違う宣言は SupportsAlterColumn・
    /// SupportsForeignKeyAlter・SupportsDescriptions（真）・ColumnReorder（None）ほか <see cref="Neutral"/> の違いすべて
    /// </summary>
    public const string NeutralWithoutPersistedForeignKeyNames = "Neutral-NoFkNames";

    /// <summary>
    /// ネイティブの列順の移動を持つ <see cref="Neutral"/>。近い実方言は MySQL で、違う宣言は
    /// AlterColumnRequiresForeignKeyRebuild・ForeignKeyRequiresLeadingIndex・SetNullRequiresNullableChildColumn（偽）・
    /// ReferencedKeyRequiresSameColumnOrder 以外の MySQL の宣言（TransactionalDdl は真・型の正規化は恒等）
    /// </summary>
    public const string Native = "Native";

    /// <summary>
    /// テーブル再構築の方言（ALTER・外部キーの後付け・説明・外部キー名の保存なし・列順は再構築）。近い実方言は SQLite で、違う宣言は
    /// SupportsSchemaQualifiedTableNames（真）・ChecksDefaultOnColumnTypeChange（偽）・SupportsUniqueConstraintOnPrimaryKey（真）・
    /// ReferencedKeyRequiresSameColumnOrder（真）・ForeignKeyAllowsStringLengthDifference（偽）・型の正規化・識別子の上限
    /// </summary>
    public const string Rebuild = "Rebuild";

    /// <summary>
    /// <see cref="Rebuild"/> で主キーと同じ列集合の一意制約を持てないもの。近い実方言は SQLite（違う宣言は <see cref="Rebuild"/> から
    /// SupportsUniqueConstraintOnPrimaryKey を除いたもの）
    /// </summary>
    public const string RebuildWithoutUniqueOnPrimaryKey = "Rebuild-NoUniqueOnPk";

    /// <summary>
    /// 外部キー・主キー・一意制約の構成列の型の変更に、制約の外し直しが要る宣言。近い実方言は SQL Server で、違う宣言は
    /// SetDefaultRequiresColumnDefault・SetNullRequiresNullableChildColumn・ChecksDefaultOnColumnTypeChange（偽）・
    /// 型の正規化（恒等）・識別子の上限（なし）
    /// </summary>
    public const string FkRebuild = "FkRebuild";

    /// <summary><see cref="FkRebuild"/> に SQL Server の型の正規化を足したもの（近い実方言は SQL Server）</summary>
    public const string FkRebuildWithSqlServerTypes = "FkRebuild-SqlServerTypes";

    /// <summary><see cref="FkRebuild"/> で参照先のキーの並びを問わないもの（実方言に無い組み合わせ）</summary>
    public const string FkRebuildUnorderedKey = "FkRebuild-UnorderedKey";

    /// <summary>
    /// 外部キーの子列を先頭に持つ索引を要する <see cref="Neutral"/>（近い実方言は MySQL・違う宣言は <see cref="Native"/> と同じく
    /// MySQL の残りの宣言）
    /// </summary>
    public const string NeutralLeadingIndex = "Neutral-LeadingIndex";

    /// <summary><see cref="Neutral"/> で参照先のキーの並びを問わないもの（近い実方言は PostgreSQL・違う宣言は Neutral の違い）</summary>
    public const string NeutralUnorderedKey = "Neutral-UnorderedKey";

    /// <summary>
    /// MySQL 相当（列順はネイティブ・外部キーは外し直す・外部キーは先頭の索引を要する）。近い実方言は MySQL で、違う宣言は
    /// SetNullRequiresNullableChildColumn（偽）・TransactionalDdl（真）・ForeignKeyAllowsStringLengthDifference（偽）・型の正規化・
    /// 識別子の上限
    /// </summary>
    public const string MySqlLike = "MySqlLike";

    /// <summary><see cref="MySqlLike"/> で文字列型の長さの違いを外部キーに許すもの（近い実方言は MySQL）</summary>
    public const string MySqlLikeLengthLenient = "MySqlLike-LengthLenient";

    /// <summary><see cref="MySqlLike"/> で DDL が暗黙コミットされるもの（近い実方言は MySQL）</summary>
    public const string NonTransactionalOrdered = "NonTransactionalOrdered";

    /// <summary><see cref="NonTransactionalOrdered"/> で参照先のキーの並びを問わないもの（実方言に無い組み合わせ）</summary>
    public const string NonTransactionalUnordered = "NonTransactional-UnorderedKey";

    /// <summary>
    /// ON UPDATE と ON DELETE SET DEFAULT を持たない <see cref="Neutral"/>（Oracle の参照アクションの宣言を模したもの）。
    /// 近い実方言は Oracle で、違う宣言は ChecksDefaultOnColumnTypeChange（偽）・SupportsUniqueConstraintOnPrimaryKey（真）・
    /// ReferencedKeyRequiresSameColumnOrder（真）・ForeignKeyAllowsStringLengthDifference（偽）・TransactionalDdl（真）・
    /// 型の正規化（恒等）・識別子の上限（なし）
    /// </summary>
    public const string NeutralWithoutOnUpdateAndSetDefault = "Neutral-NoOnUpdateNoSetDefault";

    private static readonly SyncDialectCapabilities MySqlLikeValue =
        TestDialectCapabilities.Neutral with
        {
            ColumnReorder = ColumnReorderMode.Native,
            AlterColumnRequiresForeignKeyRebuild = true,
            ForeignKeyRequiresLeadingIndex = true,
        };

    private static readonly SyncDialectCapabilities FkRebuildValue =
        TestDialectCapabilities.Neutral with
        {
            AlterColumnRequiresForeignKeyRebuild = true,
            AlterColumnRequiresKeyConstraintRebuild = true,
        };

    private static readonly SyncDialectCapabilities RebuildValue =
        TestDialectCapabilities.Neutral with
        {
            SupportsAlterColumn = false,
            SupportsForeignKeyAlter = false,
            SupportsDescriptions = false,
            PersistsForeignKeyConstraintNames = false,
            ColumnReorder = ColumnReorderMode.Rebuild,
        };

    private static readonly Dictionary<string, SyncDialectCapabilities> Synthetic = new(
        StringComparer.Ordinal
    )
    {
        [Neutral] = TestDialectCapabilities.Neutral,
        [NeutralWithoutDescriptions] = TestDialectCapabilities.Neutral with
        {
            SupportsDescriptions = false,
        },
        [NeutralWithoutPersistedForeignKeyNames] = TestDialectCapabilities.Neutral with
        {
            PersistsForeignKeyConstraintNames = false,
        },
        [Native] = TestDialectCapabilities.Neutral with
        {
            ColumnReorder = ColumnReorderMode.Native,
        },
        [Rebuild] = RebuildValue,
        [RebuildWithoutUniqueOnPrimaryKey] = RebuildValue with
        {
            SupportsUniqueConstraintOnPrimaryKey = false,
        },
        [FkRebuild] = FkRebuildValue,
        [FkRebuildWithSqlServerTypes] = FkRebuildValue with
        {
            NormalizeTypeNotation = SqlServerTypeNotation.Normalize,
        },
        [FkRebuildUnorderedKey] = FkRebuildValue with
        {
            ReferencedKeyRequiresSameColumnOrder = false,
        },
        [NeutralLeadingIndex] = TestDialectCapabilities.Neutral with
        {
            ForeignKeyRequiresLeadingIndex = true,
        },
        [NeutralUnorderedKey] = TestDialectCapabilities.Neutral with
        {
            ReferencedKeyRequiresSameColumnOrder = false,
        },
        [MySqlLike] = MySqlLikeValue,
        [MySqlLikeLengthLenient] = MySqlLikeValue with
        {
            ForeignKeyAllowsStringLengthDifference = true,
        },
        [NonTransactionalOrdered] = MySqlLikeValue with { TransactionalDdl = false },
        [NonTransactionalUnordered] = MySqlLikeValue with
        {
            TransactionalDdl = false,
            ReferencedKeyRequiresSameColumnOrder = false,
        },
        [NeutralWithoutOnUpdateAndSetDefault] = TestDialectCapabilities.Neutral with
        {
            SupportsOnUpdateAction = false,
            SupportsSetDefaultAction = false,
        },
    };

    private static readonly Dictionary<string, IDatabaseProvider> Providers = new(
        StringComparer.Ordinal
    )
    {
        [DialectNames.SqlServer] = new SqlServerProvider(),
        [DialectNames.PostgreSql] = new PostgreSqlProvider(),
        [DialectNames.MySql] = new MySqlProvider(),
        [DialectNames.Oracle] = new OracleProvider(),
        [DialectNames.Sqlite] = new SqliteProvider(),
    };

    /// <summary>
    /// 合成の能力の近い実方言（期待がそのまま合えば実方言の行にする判定に使う）
    /// </summary>
    public static IReadOnlyDictionary<string, string> NearestDialect { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Neutral] = DialectNames.SqlServer,
            [NeutralWithoutDescriptions] = DialectNames.SqlServer,
            [NeutralWithoutPersistedForeignKeyNames] = DialectNames.Sqlite,
            [Native] = DialectNames.MySql,
            [Rebuild] = DialectNames.Sqlite,
            [RebuildWithoutUniqueOnPrimaryKey] = DialectNames.Sqlite,
            [FkRebuild] = DialectNames.SqlServer,
            [FkRebuildWithSqlServerTypes] = DialectNames.SqlServer,
            [FkRebuildUnorderedKey] = DialectNames.SqlServer,
            [NeutralLeadingIndex] = DialectNames.MySql,
            [NeutralUnorderedKey] = DialectNames.PostgreSql,
            [MySqlLike] = DialectNames.MySql,
            [MySqlLikeLengthLenient] = DialectNames.MySql,
            [NonTransactionalOrdered] = DialectNames.MySql,
            [NonTransactionalUnordered] = DialectNames.MySql,
            [NeutralWithoutOnUpdateAndSetDefault] = DialectNames.Oracle,
        };

    /// <summary>実方言 5 つ（<see cref="DialectNames.All"/> の並び）</summary>
    public static IReadOnlyList<string> AllDialects => DialectNames.All;

    /// <summary>鍵が実方言か</summary>
    public static bool IsDialect(string key) => Providers.ContainsKey(key);

    /// <summary>鍵の能力の宣言</summary>
    public static SyncDialectCapabilities Get(string key) =>
        Providers.TryGetValue(key, out var provider) ? provider.SyncCapabilities
        : Synthetic.TryGetValue(key, out var capabilities) ? capabilities
        : throw new KeyNotFoundException($"能力の鍵 {key} が無い");

    /// <summary>実方言のプロバイダ</summary>
    public static IDatabaseProvider Provider(string dialect) => Providers[dialect];

    /// <summary>すべての鍵（表の検査に使う）</summary>
    public static IEnumerable<string> AllKeys => Providers.Keys.Concat(Synthetic.Keys);
}
