using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sync;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.Scheduling;
using QuickER.Provider.Sync.Steps;
using QuickER.Tests.TestSupport.Engines;
using Xunit.Sdk;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 能力の組の網（<see cref="SyncPlanCapabilityNetTestsBase"/>）の次元表・固定の水準・網羅しない組の一覧・台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、網の種 1 つ（同期計画の総当たりと同じ生成器・同じ番号の種）と、その種が目標の図へ加えた変更 1 つの組。
/// <b>網の種 N と実 DB の総当たりの種 N は別のケース</b>：実 DB は取り込んだ図（表・制約の並びが DB の返す順）の並びで変更の乱数を
/// 引くので、生成器が作った図の並びで引く網とは変更が変わる（種の番号を書くときは「網の種」「実 DB の種」を添える）。因子は総当たりと同じ
/// 差分の選び方・変更の種類に、方言の能力（<see cref="SyncDialectCapabilities"/> のうち同期計画の形を変える 28 項目）を足したもの。
/// </para>
/// <para>
/// 能力は方言ごとに固定の値なので、派生ごとの固定の水準（<c>fixedLevels</c>）として数える。固定の水準はここに書き下ろした表
/// （<see cref="DeclaredLevels"/>）から作り、観測は実際のプロバイダの値を読む（取り違えると宣言の誤りで赤）。
/// 固定した因子どうしの組は固定した水準のときだけ要るので、<b>能力どうしの組（5 方言の組み合わせでしか作れない組）は数えない</b>
/// （どの能力の値の組を網羅するかは、方言が 5 つしかないという製品の事実で決まり、エンジンが埋められる欠けではない）。
/// 数えるのは「能力の固定の値 × 変更の種類・差分の選び方」で、能力の組のセル（例：Oracle の TransactionalDdl=False ×
/// AlterColumnRequiresForeignKeyRebuild=False）で各変更が流れたことを表す。
/// </para>
/// </remarks>
internal static class SyncPlanCapabilityNetDimensions
{
    /// <summary>次元表に載せる能力（同期計画の形を変えるもの）</summary>
    public static IReadOnlyList<string> CapabilityMembers { get; } =
    [
        nameof(SyncDialectCapabilities.SupportsAlterColumn),
        nameof(SyncDialectCapabilities.SupportsForeignKeyAlter),
        nameof(SyncDialectCapabilities.SupportsDescriptions),
        nameof(SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild),
        nameof(SyncDialectCapabilities.AlterColumnRequiresKeyConstraintRebuild),
        nameof(SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex),
        nameof(SyncDialectCapabilities.PersistsForeignKeyConstraintNames),
        nameof(SyncDialectCapabilities.ColumnReorder),
        nameof(SyncDialectCapabilities.SetDefaultRequiresColumnDefault),
        nameof(SyncDialectCapabilities.SetNullRequiresNullableChildColumn),
        nameof(SyncDialectCapabilities.SupportsSchemaQualifiedTableNames),
        nameof(SyncDialectCapabilities.ChecksDefaultOnColumnTypeChange),
        nameof(SyncDialectCapabilities.TypeKindChangeRequiresEmptyColumn),
        nameof(SyncDialectCapabilities.PrecisionDecreaseRequiresEmptyColumn),
        nameof(SyncDialectCapabilities.AddNotNullColumnRequiresEmptyTable),
        nameof(SyncDialectCapabilities.AlterColumnBlockedByComputedColumn),
        nameof(SyncDialectCapabilities.SupportsComputedColumnTypeChange),
        nameof(SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey),
        nameof(SyncDialectCapabilities.ReferencedKeyRequiresSameColumnOrder),
        nameof(SyncDialectCapabilities.ForeignKeyAllowsStringLengthDifference),
        nameof(SyncDialectCapabilities.ForeignKeyAllowsIntegerWidthDifference),
        nameof(SyncDialectCapabilities.ForeignKeyColumnWidening),
        nameof(SyncDialectCapabilities.UniqueConstraintComparesPartiallyNullKeys),
        nameof(SyncDialectCapabilities.TransactionalDdl),
        nameof(SyncDialectCapabilities.ConstraintNameScope),
        nameof(SyncDialectCapabilities.AllowsDuplicateForeignKeyDefinitions),
        nameof(SyncDialectCapabilities.SupportsOnUpdateAction),
        nameof(SyncDialectCapabilities.SupportsSetDefaultAction),
    ];

    /// <summary>
    /// 方言ごとの能力の値（書き下ろし。並びは SQL Server・PostgreSQL・MySQL・Oracle・SQLite）。プロバイダの宣言と食い違うと、
    /// 観測が宣言の誤りとして赤にする
    /// </summary>
    private static readonly (string Member, string[] Values)[] DeclaredTable =
    [
        ("SupportsAlterColumn", ["True", "True", "True", "True", "False"]),
        ("SupportsForeignKeyAlter", ["True", "True", "True", "True", "False"]),
        ("SupportsDescriptions", ["True", "True", "True", "True", "False"]),
        ("AlterColumnRequiresForeignKeyRebuild", ["True", "False", "True", "False", "False"]),
        ("AlterColumnRequiresKeyConstraintRebuild", ["True", "False", "False", "False", "False"]),
        ("ForeignKeyRequiresLeadingIndex", ["False", "False", "True", "False", "False"]),
        ("PersistsForeignKeyConstraintNames", ["True", "True", "True", "True", "False"]),
        ("ColumnReorder", ["None", "None", "Native", "None", "Rebuild"]),
        ("SetDefaultRequiresColumnDefault", ["True", "False", "False", "False", "False"]),
        ("SetNullRequiresNullableChildColumn", ["True", "False", "True", "False", "False"]),
        ("SupportsSchemaQualifiedTableNames", ["True", "True", "True", "True", "False"]),
        ("ChecksDefaultOnColumnTypeChange", ["True", "True", "True", "True", "False"]),
        ("TypeKindChangeRequiresEmptyColumn", ["False", "False", "False", "True", "False"]),
        ("PrecisionDecreaseRequiresEmptyColumn", ["False", "False", "False", "True", "False"]),
        ("AddNotNullColumnRequiresEmptyTable", ["True", "True", "False", "True", "True"]),
        ("AlterColumnBlockedByComputedColumn", ["True", "True", "False", "True", "False"]),
        ("SupportsComputedColumnTypeChange", ["False", "True", "True", "False", "False"]),
        ("SupportsUniqueConstraintOnPrimaryKey", ["True", "True", "True", "False", "False"]),
        ("ReferencedKeyRequiresSameColumnOrder", ["True", "False", "True", "False", "False"]),
        ("ForeignKeyAllowsStringLengthDifference", ["False", "True", "True", "True", "True"]),
        ("ForeignKeyAllowsIntegerWidthDifference", ["False", "True", "False", "True", "True"]),
        (
            "ForeignKeyColumnWidening",
            ["Refused", "Accepted", "WithinLengthPrefix", "Accepted", "Accepted"]
        ),
        ("UniqueConstraintComparesPartiallyNullKeys", ["False", "False", "False", "True", "False"]),
        ("TransactionalDdl", ["True", "True", "False", "False", "True"]),
        (
            "ConstraintNameScope",
            [
                "Schema",
                "KeysInSchemaForeignKeysInTable",
                "ForeignKeysInSchemaAnyInTable",
                "Schema",
                "None",
            ]
        ),
        ("AllowsDuplicateForeignKeyDefinitions", ["True", "True", "True", "False", "True"]),
        ("SupportsOnUpdateAction", ["True", "True", "True", "False", "True"]),
        ("SupportsSetDefaultAction", ["True", "True", "False", "False", "True"]),
    ];

    private static readonly string[] DeclaredDialects =
    [
        DialectNames.SqlServer,
        DialectNames.PostgreSql,
        DialectNames.MySql,
        DialectNames.Oracle,
        DialectNames.Sqlite,
    ];

    /// <summary>方言ごとの固定の水準（<see cref="DeclaredTable"/> から作る）</summary>
    public static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > DeclaredLevels { get; } =
        DeclaredDialects
            .Select((dialect, index) => (dialect, index))
            .ToDictionary(
                pair => pair.dialect,
                pair =>
                    (IReadOnlyDictionary<string, string>)
                        DeclaredTable.ToDictionary(
                            row => row.Member,
                            row => row.Values[pair.index],
                            StringComparer.Ordinal
                        ),
                StringComparer.Ordinal
            );

    /// <summary>型の変更（<c>alter type</c>）の行き先の型の種類（方言の上限ちょうどの型は <c>AtLimit</c>）</summary>
    public const string AlteredType = "AlteredType";

    /// <summary>主キーの変更で、主キーを変えた表を参照する外部キーの数（自己参照は数えない）</summary>
    public const string ReferencingFks = "ReferencingFks";

    /// <summary>参照アクションの変更（<c>fk action</c>）の行き先（ON DELETE / ON UPDATE とアクション）</summary>
    public const string ReferentialAction = "ReferentialAction";

    /// <summary>その変更に当たらない因子の水準</summary>
    public const string NotApplicable = "-";

    /// <summary>主キーの変更の種類（<see cref="ReferencingFks"/> を数える変更）</summary>
    private static readonly HashSet<string> PrimaryKeyMutations = new(StringComparer.Ordinal)
    {
        "reorder pk",
        "extend pk",
        "shrink pk",
        "move pk",
        "replace pk",
        "remove pk",
        "reorder referenced pk",
    };

    /// <summary>次元表</summary>
    /// <remarks>
    /// 型・参照の数・参照アクションの 3 因子は、変更 1 つに当たる因子だけが値を持ち、ほかは <see cref="NotApplicable"/>
    /// （<c>isValid</c> はこの定義を書くだけ）。<c>reorder referenced pk</c> は参照されている主キーの変更なので参照の数が 0 にならない
    /// </remarks>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncPlan,
            [
                SyncPlanFuzzDimensions.Table.Factor(SyncPlanFuzzDimensions.Selection),
                SyncPlanFuzzDimensions.Table.Factor(SyncPlanFuzzDimensions.Mutation),
                new Factor(
                    AlteredType,
                    [
                        new Level(NotApplicable),
                        .. new[]
                        {
                            "Int",
                            "BigInt",
                            "VarChar",
                            "Decimal",
                            "DateTime",
                            "FixedChar",
                            "Binary",
                        }.Select(kind => new Level(kind)),
                        new Level("AtLimit"),
                    ],
                    "型の変更（alter type）の行き先の型の種類（方言の上限ちょうどの型は AtLimit）"
                ),
                new Factor(
                    ReferencingFks,
                    [new Level(NotApplicable), new Level("0"), new Level("1"), new Level("2+")],
                    "主キーの変更で、主キーを変えた表を参照する外部キーの数"
                ),
                new Factor(
                    ReferentialAction,
                    [
                        new Level(NotApplicable),
                        .. new[] { "OnDelete", "OnUpdate" }.SelectMany(clause =>
                            Enum.GetValues<ForeignKeyReferentialAction>()
                                .Select(action => new Level($"{clause}:{action}"))
                        ),
                    ],
                    "参照アクションの変更（fk action）の行き先"
                ),
                .. CapabilityMembers.Select(member => new Factor(
                    member,
                    member switch
                    {
                        nameof(SyncDialectCapabilities.ColumnReorder) =>
                            Enum.GetValues<ColumnReorderMode>()
                                .Select(value => new Level(value.ToString()))
                                .ToList(),
                        nameof(SyncDialectCapabilities.ConstraintNameScope) =>
                            Enum.GetValues<ConstraintNameScope>()
                                .Select(value => new Level(value.ToString()))
                                .ToList(),
                        nameof(SyncDialectCapabilities.ForeignKeyColumnWidening) =>
                            Enum.GetValues<ForeignKeyColumnWidening>()
                                .Select(value => new Level(value.ToString()))
                                .ToList(),
                        _ => [new Level("True"), new Level("False")],
                    },
                    $"方言の能力 {member}（派生ごとに固定の水準）",
                    AxisMember.Property(typeof(SyncDialectCapabilities), member)
                )),
            ],
            isValid: IsValid
        );

    /// <summary>変更 1 つに当たる因子だけが値を持つ（部分の割り当てを受ける）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        var tagged = new[] { AlteredType, ReferencingFks, ReferentialAction }.Count(factor =>
            assignment.TryGetValue(factor, out var level) && level != NotApplicable
        );

        if (tagged > 1)
        {
            return false;
        }

        if (!assignment.TryGetValue(SyncPlanFuzzDimensions.Mutation, out var mutation))
        {
            return true;
        }

        bool Applies(string factor, bool expected) =>
            !assignment.TryGetValue(factor, out var level) || (level != NotApplicable) == expected;

        return Applies(AlteredType, mutation == "alter type")
            && Applies(ReferencingFks, PrimaryKeyMutations.Contains(mutation))
            && Applies(ReferentialAction, mutation == "fk action")
            && !(
                mutation == "reorder referenced pk"
                && assignment.TryGetValue(ReferencingFks, out var refs)
                && refs == "0"
            );
    }

    /// <summary>網羅しない組の一覧（範囲は方言の名前）</summary>
    /// <remarks>
    /// 既定の 5000 種で、変更の種類と差分の選び方の組は全方言で揃う（変更の種類そのものを作らない方言の欠けだけが残る）。
    /// 生成器を変えて種の指紋が変わったら、一緒に照合し直す。
    /// </remarks>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.SyncPlan,
            [
                // SQL Server の参照アクションの変更（fk action と ReferentialAction の各組）は、生成器が作らない（AllowCascade が偽）
                // ので網羅の行（SyncPlanCoverageRows の coverage-sqlserver-fk-actions-*）が埋める
                .. new[] { "alter type duplicated fk", "drop one of duplicated fks" }.SelectMany(
                    mutation =>
                        MutationGaps(
                            DialectNames.Oracle,
                            mutation,
                            "Oracle は同じ子の列の並びから同じ親へ外部キーを 2 本張れない（ORA-02275・DB の事実）ので、同じ列ペアに外部キーが 2 本ある形を前提にする変更は作れない"
                        )
                ),
                .. new[]
                {
                    "alter type duplicated unique",
                    "drop one of duplicated uniques",
                }.SelectMany(mutation =>
                    MutationGaps(
                        DialectNames.Oracle,
                        mutation,
                        "Oracle は同じ列の並びに一意制約を 2 つ張れない（ORA-02261・DB の事実）ので、同じ列の並びに一意制約が 2 つある形を前提にする変更は作れない"
                    )
                ),
                .. MutationGaps(
                    DialectNames.Sqlite,
                    "respell type",
                    "SQLite は宣言の型をそのまま覚え、型の正規化を持たない（製品の事実＝NormalizeTypeNotation が恒等）ので、同じ型の別の綴りが無い"
                ),
                .. MutationGaps(
                    DialectNames.Sqlite,
                    "set description",
                    "SQLite は表と列の説明を保存しない（製品の事実＝SupportsDescriptions が偽）ので、生成器は説明を作らない"
                ),
                .. LevelGaps(
                    DialectNames.Sqlite,
                    AlteredType,
                    level => level == "AtLimit",
                    "SQLite は型の長さを検査せず上限を持たない（DB の事実）ので、方言の上限ちょうどの型が無い"
                ),
            ]
        );

    /// <summary>
    /// 変更の種類が一度も出ない方言の欠け（選び方との組と、固定した能力の水準との組の全部・受け入れ＝製品か DB の事実）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> MutationGaps(
        string dialect,
        string mutation,
        string reason
    ) => LevelGaps(dialect, SyncPlanFuzzDimensions.Mutation, kind => kind == mutation, reason);

    /// <summary>
    /// 因子 <paramref name="factor"/> の水準（<paramref name="levels"/> が真）が一度も出ない方言の欠け（その因子とほかの全因子との組・
    /// 受け入れ＝製品か DB の事実）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> LevelGaps(
        string dialect,
        string factor,
        Func<string, bool> levels,
        string reason
    )
    {
        var names = Table.Factors.Select(f => f.Name).ToList();
        var index = names.IndexOf(factor);

        for (var other = 0; other < names.Count; other++)
        {
            if (other == index)
            {
                continue;
            }

            var (a, b) = other < index ? (names[other], factor) : (factor, names[other]);
            Func<string, string, bool> predicate =
                a == factor ? (level, _) => levels(level) : (_, level) => levels(level);

            yield return CoverageGap.AcceptedMatching(dialect, "P2-4", reason, a, b, predicate);
        }
    }

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "CapabilityNet",
            "同期計画の能力の組の網（5 方言の能力・DB なし）",
            Table,
            SyncPlanScenarios.Table.Count + SyncPlanCoverageRows.Table.Count,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCapabilityNet.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCoverageRows.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanStaticRules.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzGenerator.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs",
            ],
            [
                "src/QuickER.Provider/SchemaDiffService.cs",
                "src/QuickER.Provider/SyncPlanner.cs",
                "src/QuickER.Provider/SyncPlanContext.cs",
                "src/QuickER.Provider/Sync",
                "src/QuickER.Provider/SyncPlan.cs",
                "src/QuickER.Provider/SyncDialectCapabilities.cs",
            ]
        );

    /// <summary>方言の固定の水準</summary>
    /// <param name="dialect">方言名</param>
    public static IReadOnlyDictionary<string, string> FixedLevelsFor(string dialect) =>
        DeclaredLevels[dialect];

    /// <summary>種 1 つの結果を、変更ごとのケースとして数える（能力はプロバイダの値を読む）</summary>
    /// <param name="counter">数える先</param>
    /// <param name="outcome">種 1 つの結果</param>
    /// <param name="capabilities">プロバイダの能力</param>
    public static void Observe(
        CoverageCounter counter,
        FuzzSeedOutcome outcome,
        SyncDialectCapabilities capabilities
    )
    {
        foreach (var mutation in outcome.Mutations)
        {
            var kind = SyncPlanFuzzDimensions.KindOf(mutation);
            var assignment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SyncPlanFuzzDimensions.Selection] = outcome.Selection.ToString(),
                [SyncPlanFuzzDimensions.Mutation] = kind,
                [AlteredType] =
                    kind == "alter type" ? Tag(mutation, "type") ?? "(no tag)" : NotApplicable,
                [ReferencingFks] = PrimaryKeyMutations.Contains(kind)
                    ? Tag(mutation, "refs") ?? "(no tag)"
                    : NotApplicable,
                [ReferentialAction] = kind == "fk action" ? ActionOf(mutation) : NotApplicable,
            };

            foreach (var member in CapabilityMembers)
            {
                assignment[member] = LevelOf(capabilities, member);
            }

            counter.Observe(assignment);
        }
    }

    /// <summary>変更の文の札（<c>{name=value}</c>）の値（無ければ null＝宣言していない水準として観測が断る）</summary>
    private static string? Tag(string mutation, string name)
    {
        var start = mutation.IndexOf("{" + name + "=", StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        start += name.Length + 2;
        var end = mutation.IndexOf('}', start);
        return end < 0 ? null : mutation[start..end];
    }

    /// <summary>参照アクションの変更の文（<c>... ON DELETE Cascade</c>）の水準</summary>
    private static string ActionOf(string mutation)
    {
        var parts = mutation.Split(' ');
        var clause = parts[^3] + " " + parts[^2];
        return (
                clause == "ON DELETE" ? "OnDelete"
                : clause == "ON UPDATE" ? "OnUpdate"
                : clause
            )
            + ":"
            + parts[^1];
    }

    /// <summary>能力の値を水準の名前にする</summary>
    internal static string LevelOf(SyncDialectCapabilities capabilities, string member) =>
        Convert.ToString(
            typeof(SyncDialectCapabilities).GetProperty(member)!.GetValue(capabilities),
            CultureInfo.InvariantCulture
        )!;
}

/// <summary>能力の組の網の既知の失敗（厳密：一覧に無い失敗も、載っているのに起きない失敗も赤）</summary>
internal static class SyncPlanCapabilityNetKnownFailures
{
    /// <summary>項目</summary>
    public static readonly IReadOnlyList<KnownEntry> Entries =
    [
        // synthesized-unique-name-collision（mysql-386・postgresql-386 の行）は 2026-11-01 の修正 E で直して一覧から消した
        // （名前の無い一意制約の名前を図全体で決め、ぶつかれば番号を足す）。行は回帰の網として残る
        //
        // 3 回目の効果測定の直し残し（分岐の軸の台帳の項目 pa1・pe1・pe3）は、区切り c で形の行の水準に結んでから製品を直して
        // 一覧から消した（台帳は結び済み・修正済み）。指摘のセルの行は回帰の網として残る
    ];

    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } = new("SyncPlan.CapabilityNet", Entries);

    /// <summary>違反のあった種か行を、照合に渡す失敗にする（範囲＝方言、行なら方言＋"/scenarios"）</summary>
    public static EngineFailure ToFailure(FuzzSeedOutcome outcome) =>
        SyncPlanFuzzKnownFailures.ToFailure(outcome);
}

/// <summary>能力の組の網の 1 ケース（DB の代わりに生成した図を DB 側の図にして、計画を作り規則で判定する）</summary>
internal static class SyncPlanCapabilityNetRunner
{
    /// <summary>1 ケースを流す</summary>
    /// <param name="dialect">方言の部品（DB は使わない）</param>
    /// <param name="generator">ケースの作り方</param>
    /// <param name="outcome">結果を書き込む先</param>
    public static FuzzSeedOutcome Run(
        SyncPlanFuzzDialect dialect,
        ISyncPlanFuzzCase generator,
        FuzzSeedOutcome outcome,
        bool caseFoldRelation = true
    )
    {
        var provider = dialect.Provider;
        var capabilities = provider.SyncCapabilities;
        var live = generator.BuildLive();
        live.Diagram.TargetDbms = provider.Name;
        AddComputedColumns(live, dialect);
        var before = new SchemaImportResult
        {
            Entities = [.. live.Diagram.Entities],
            Relationships = [.. live.Diagram.Relationships],
            ColumnDefaults = [.. live.ColumnDefaults],
            ColumnByteLengths = SyncPlanFuzzDialect.ColumnByteLengths(
                dialect.Name,
                live.Diagram.Entities
            ),
            Warnings = [.. live.ImportWarnings],
        };
        var (targetEntities, targetRelationships) = SyncPlanFuzzRunner.CloneWithFreshIds(before);
        generator.Mutate(targetEntities, targetRelationships, live);
        outcome.Mutations.AddRange(generator.Mutations);
        outcome.Shapes.UnionWith(live.Shapes);

        var diff = new SchemaDiffService().Compute(
            before.Entities,
            before.Relationships,
            targetEntities,
            targetRelationships,
            capabilities
        );
        outcome.Selection = generator.Select(diff.Items);
        var selected = diff.Items.Where(i => i.IsSelected).ToList();
        outcome.Selected.AddRange(selected.Select(i => $"{i.Kind}: {i.Description}"));
        outcome.Shapes.UnionWith(SyncPlanFuzzRunner.SelectionShapes(before, selected));
        outcome.Shapes.UnionWith(SyncPlanFuzzRunner.ExclusionChainShapes(live, selected));
        outcome.Unselected.AddRange(
            diff.Items.Where(i => !i.IsSelected).Select(i => $"{i.Kind}: {i.Description}")
        );

        SyncPlan plan;
        // 同期の画面・実 DB のランナーと同じ写し方で取込の結果から計画の入力を作る
        var context = SyncPlanContext.FromImport(before);

        try
        {
            plan = new SyncPlanner().BuildPlan(diff.Items, capabilities, context);
            outcome.Script = provider.SyncScriptBuilder.Build(plan);
        }
        catch (Exception ex)
        {
            outcome.Violations.Add($"planning threw: {ex.GetType().Name}: {ex.Message}");
            return outcome;
        }

        outcome.Plan = plan;
        outcome.Warnings.AddRange(plan.Warnings.Select(w => $"{w.Kind}({w.TableName})"));
        var verdict = SyncPlanStaticRules.Evaluate(
            dialect.Name,
            before.Entities,
            before.Relationships,
            selected,
            plan,
            capabilities,
            live.DataFailures,
            live.ColumnDefaults,
            before.ColumnByteLengths
        );
        outcome.Violations.AddRange(verdict.Violations);
        outcome.Violations.AddRange(
            PrimaryKeyNullableFlagProblems(
                provider,
                before,
                targetEntities,
                targetRelationships,
                diff.Items,
                plan,
                outcome.Script
            )
        );

        outcome.Violations.AddRange(
            ExclusionFixedPointProblems(
                diff.Items,
                plan,
                capabilities,
                context,
                provider.SyncScriptBuilder.Build
            )
        );

        var stepPlan = StepPlanProblems(diff.Items, plan, capabilities, context)!;
        outcome.Violations.AddRange(stepPlan.Problems);
        outcome.StepPlan = stepPlan;

        if (caseFoldRelation)
        {
            outcome.Violations.AddRange(
                CaseFoldedLiveProblems(
                    provider,
                    before,
                    targetEntities,
                    targetRelationships,
                    diff.Items,
                    plan
                )
            );
        }

        if (verdict.PredictedFailure is not null)
        {
            outcome.Notes.Add(SyncPlanFuzzRunner.StaticRulePredictedNote);
        }

        return outcome;
    }

    /// <summary>関係 N1 の違反の頭</summary>
    public const string NullableFlagRelationPrefix = "relation N1: ";

    /// <summary>
    /// 関係 N1：目標の図の主キーの列に NULL 許容の印を立てても、差分・計画の文・警告・外した項目は変わらない
    /// （破れた面ごとに違反の文を返す）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 主キーの列は印に依らず NOT NULL（<see cref="ColumnNullability.AllowsNull"/>）。図は主キーの列に印を持てる（図のファイルの
    /// 手書き。GUI・MCP・取込は落とす）ので、差分・計画・同期スクリプトが印をそのまま読むと、同じ実効の図から別の計画を作る。
    /// </para>
    /// <para>
    /// DB 側の図には立てない。DB 側の図は取込が作り、取込は主キーの列の印を false にそろえる（5 方言の SchemaImporter）。その印は、
    /// 主キーの変更で列が主キーから外れたとき、SQLite の再構築が写す live の列の NOT NULL として意味を持つ（立てると再構築の
    /// 新しい表の列が NULL 許容になる＝入力が製品の契約の外）。
    /// </para>
    /// <para>
    /// 選び方は元のケースのものを差分の位置で写す（生成器の選び方は乱数を引くので、2 回呼ぶと種の列が変わる）。差分の項目の
    /// 並び・種類・説明が元と食い違えば、計画を比べずに差分の違反だけを返す。
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> PrimaryKeyNullableFlagProblems(
        IDatabaseProvider provider,
        SchemaImportResult before,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships,
        IReadOnlyList<SchemaDiffItem> items,
        SyncPlan plan,
        string script
    )
    {
        var capabilities = provider.SyncCapabilities;
        var flaggedTarget = FlagPrimaryKeyColumnsNullable(targetEntities);
        var flaggedItems = new SchemaDiffService()
            .Compute(
                before.Entities,
                before.Relationships,
                flaggedTarget,
                [.. targetRelationships],
                capabilities
            )
            .Items;

        if (
            !flaggedItems
                .Select(Describe)
                .SequenceEqual(items.Select(Describe), StringComparer.Ordinal)
        )
        {
            return
            [
                NullableFlagRelationPrefix
                    + "the diff differs: "
                    + string.Join(
                        " / ",
                        flaggedItems.Select(Describe).Except(items.Select(Describe))
                    )
                    + " <> "
                    + string.Join(
                        " / ",
                        items.Select(Describe).Except(flaggedItems.Select(Describe))
                    ),
            ];
        }

        for (var i = 0; i < items.Count; i++)
        {
            flaggedItems[i].IsSelected = items[i].IsSelected;
        }

        SyncPlan flaggedPlan;
        string flaggedScript;

        try
        {
            flaggedPlan = new SyncPlanner().BuildPlan(
                flaggedItems,
                capabilities,
                new SyncPlanContext
                {
                    LiveEntities = before.Entities,
                    LiveRelationships = before.Relationships,
                    ColumnDefaults = before.ColumnDefaults,
                    ColumnByteLengths = before.ColumnByteLengths,
                    ImportWarnings = before.Warnings,
                }
            );
            flaggedScript = provider.SyncScriptBuilder.Build(flaggedPlan);
        }
        catch (Exception ex)
        {
            return
            [
                $"{NullableFlagRelationPrefix}planning threw: {ex.GetType().Name}: {ex.Message}",
            ];
        }

        var problems = new List<string>();
        Compare(
            "warnings",
            plan.Warnings.Select(DescribeWarning),
            flaggedPlan.Warnings.Select(DescribeWarning)
        );
        Compare(
            "excluded",
            plan.ExcludedItems.Select(Describe),
            flaggedPlan.ExcludedItems.Select(Describe)
        );

        if (!string.Equals(script, flaggedScript, StringComparison.Ordinal))
        {
            var lines = script.Split('\n');
            var flaggedLines = flaggedScript.Split('\n');
            problems.Add(
                $"{NullableFlagRelationPrefix}the script differs: "
                    + string.Join(" / ", flaggedLines.Except(lines).Select(l => l.Trim()))
                    + " <> "
                    + string.Join(" / ", lines.Except(flaggedLines).Select(l => l.Trim()))
            );
        }

        return problems;

        void Compare(string facet, IEnumerable<string> expected, IEnumerable<string> actual)
        {
            var expectedList = expected.ToList();
            var actualList = actual.ToList();

            if (!expectedList.SequenceEqual(actualList, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{NullableFlagRelationPrefix}the {facet} differ: [{string.Join(" / ", actualList)}] <> [{string.Join(" / ", expectedList)}]"
                );
            }
        }

        static string Describe(SchemaDiffItem item) => $"{item.Kind}: {item.Description}";

        static string DescribeWarning(SyncPlanWarning warning) =>
            $"{warning.Kind}({warning.TableName}): {warning.Detail}";
    }

    /// <summary>関係 N3 の違反の頭</summary>
    public const string ExclusionFixedPointRelationPrefix = "relation N3: ";

    /// <summary>
    /// 関係 N3（除外の不動点）：計画が外した項目を最初から選ばずに計画を作り直しても、新しく外れる項目が無く、計画の形が元と同じ
    /// （破れた面ごとに違反の文を返す。計画が何も外さなかったケースは何もしない）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 製品は選んだ項目を外す除外の規則を、何も外れなくなるまで繰り返す周の中で呼ぶ（<c>src/QuickER.Provider/CLAUDE.md</c>「除外の周」）。
    /// 周の外で項目を外すと、周の中の判定はその項目を選ばれていると数えたまま残る。外した項目を最初から選ばない 2 回目の計画では、
    /// 周の中の判定がその先の項目を外すので、「新しく外れる項目が無い」が破れる。DB の規則を知らずに周の約束そのものを見るので、
    /// 静的な規則が判定しない再構築の方言にも、予告の警告があって静的な規則が通す形にも掛かる。
    /// </para>
    /// <para>
    /// 警告は比べない（外した項目の名指しは、項目を選ばない 2 回目には出ない）。選び方は元の項目の印を一時的に落として渡し、
    /// 必ず戻す（差分を作り直さない＝項目の同一性を保つ）。
    /// </para>
    /// </remarks>
    /// <param name="items">差分の項目（元のケースの選び方のまま）</param>
    /// <param name="plan">元の計画</param>
    /// <param name="capabilities">元の計画と同じ能力</param>
    /// <param name="context">元の計画と同じ文脈</param>
    /// <param name="describe">計画の形を比べる文字列にする（実方言は同期スクリプト）</param>
    internal static IEnumerable<string> ExclusionFixedPointProblems(
        IReadOnlyList<SchemaDiffItem> items,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context,
        Func<SyncPlan, string> describe
    ) =>
        ExclusionFixedPointCore(
            items,
            plan,
            capabilities,
            () => new SyncPlanner().BuildPlan(items, capabilities, context),
            describe,
            ExclusionFixedPointRelationPrefix
        );

    /// <summary>除外の不動点の本体（計画の作り直し方と違反の頭を受ける。関係 N3 が使う）</summary>
    /// <param name="items">差分の項目（元のケースの選び方のまま）</param>
    /// <param name="plan">元の計画</param>
    /// <param name="capabilities">元の計画と同じ能力</param>
    /// <param name="replan">外した項目の印を落とした間に、計画を作り直す</param>
    /// <param name="describe">計画の形を比べる文字列にする（実方言は同期スクリプト）</param>
    /// <param name="prefix">違反の頭</param>
    internal static IEnumerable<string> ExclusionFixedPointCore(
        IReadOnlyList<SchemaDiffItem> items,
        SyncPlan plan,
        SyncDialectCapabilities capabilities,
        Func<SyncPlan> replan,
        Func<SyncPlan, string> describe,
        string prefix
    )
    {
        if (plan.ExcludedItems.Count == 0)
        {
            return [];
        }

        var excluded = new HashSet<SchemaDiffItem>(
            plan.ExcludedItems,
            ReferenceEqualityComparer.Instance
        );
        var found = items.Where(i => i.IsSelected && excluded.Contains(i)).ToList();

        if (found.Count != excluded.Count)
        {
            return
            [
                prefix
                    + "an excluded item is not one of the selected items: "
                    + string.Join(" / ", excluded.Except(found).Select(Describe)),
            ];
        }

        // 一部だけ外す項目（型の変更も含む列の変更の NULL 許容化だけを外し、型の変更は残す＝src/QuickER.Provider/CLAUDE.md
        // 「選んでいない変更を前提にする項目」）は、外した一覧に載っても型の変更が計画に残る。選んだままにして、2 回目も同じ項目が
        // 同じように外れることを見る（選ばないと、残るはずの型の変更まで消えて計画の形が変わる）
        var partial = found
            .Where(i =>
                i is { Kind: SchemaDiffKind.AlterColumn, Column: not null, OldColumn: not null }
                && !capabilities.AreSameType(i.OldColumn.DataType, i.Column.DataType)
            )
            .ToHashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);
        var dropped = found.Where(i => !partial.Contains(i)).ToList();

        if (dropped.Count == 0)
        {
            return [];
        }

        SyncPlan second;
        string first;
        string again;

        foreach (var item in dropped)
        {
            item.IsSelected = false;
        }

        try
        {
            second = replan();
            again = describe(second);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return
            [
                $"{prefix}planning without the excluded items threw: {ex.GetType().Name}: {ex.Message}",
            ];
        }
        finally
        {
            foreach (var item in dropped)
            {
                item.IsSelected = true;
            }
        }

        var problems = new List<string>();

        var more = second.ExcludedItems.Where(i => !partial.Contains(i)).ToList();

        if (more.Count > 0 || second.ExcludedItems.Count(partial.Contains) != partial.Count)
        {
            problems.Add(
                prefix
                    + "planning without the excluded items excludes more: "
                    + string.Join(" / ", more.Select(Describe))
                    + (
                        second.ExcludedItems.Count(partial.Contains) != partial.Count
                            ? " [a partially excluded column change is no longer excluded]"
                            : ""
                    )
                    + " (first excluded: "
                    + string.Join(" / ", plan.ExcludedItems.Select(Describe))
                    + ")"
            );
        }

        try
        {
            first = describe(plan);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 元の計画の形を作れない失敗は、呼び出し側が自分の違反として報告する
            _ = ex;
            return problems;
        }

        if (!string.Equals(first, again, StringComparison.Ordinal))
        {
            var lines = first.Split('\n');
            var againLines = again.Split('\n');
            var at = Enumerable
                .Range(0, Math.Min(lines.Length, againLines.Length))
                .FirstOrDefault(
                    i => !string.Equals(lines[i], againLines[i], StringComparison.Ordinal),
                    Math.Min(lines.Length, againLines.Length)
                );
            problems.Add(
                $"{prefix}the plan differs without the excluded items: "
                    + string.Join(" / ", againLines.Except(lines).Select(l => l.Trim()))
                    + " <> "
                    + string.Join(" / ", lines.Except(againLines).Select(l => l.Trim()))
                    + $" (first difference at line {at + 1}: [{Line(againLines, at)}] <> [{Line(lines, at)}])"
            );

            static string Line(string[] source, int index) =>
                index < source.Length ? source[index].Trim() : "(end)";
        }

        return problems;

        static string Describe(SchemaDiffItem item) => $"{item.Kind}: {item.Description}";
    }

    /// <summary>関係 N4 の違反の頭</summary>
    public const string StepPlanRelationPrefix = "relation N4: ";

    /// <summary>関係 N4 の、文で表せない計画を製品が断ることの照合の違反の頭</summary>
    public const string StepPlanProductPrefix = StepPlanRelationPrefix + "product plan: ";

    /// <summary>関係 N4 の、歩きの終わりの状態の照合の違反の頭</summary>
    public const string StepPlanEndStatePrefix = StepPlanRelationPrefix + "end state: ";

    /// <summary>関係 N4 の、取り下げの名指しと位置の照合の違反の頭</summary>
    public const string StepPlanWithdrawalPrefix = StepPlanRelationPrefix + "withdrawal: ";

    /// <summary>関係 N4 の結果</summary>
    /// <param name="Problems">違反の文</param>
    /// <param name="Unrepresentable">計画を文で表せなかった理由（関係を掛けなかった。掛けたなら null）</param>
    /// <param name="Withdrawals">計画器が取り下げた項目の数（無ければ 0）</param>
    /// <param name="EndStateChecked">歩きの終わりの状態の照合を掛けたか</param>
    /// <param name="MovedFirst">製品の計画が前寄せのセクションを持つか</param>
    /// <param name="NamedAroundRefusable">製品の計画が断られうる文の前後の名指しの 2 種別の警告を持つか</param>
    /// <param name="ExistingTableRebuild">
    /// 製品の計画が既存の表の再構築（<see cref="TableRebuildPlan.CreateOnly"/> が偽）を持つか
    /// </param>
    /// <param name="CreateOnlyRebuild">
    /// 製品の計画が表を作るだけの再構築（<see cref="TableRebuildPlan.CreateOnly"/> が真）を持つか
    /// </param>
    /// <param name="RebuildWithSections">
    /// 製品の計画が、再構築と、再構築へ畳まれずに残ったセクションの両方を持つか
    /// </param>
    /// <param name="TableRebuildDialect">
    /// 計画器の結果の事実が、その計画をテーブル再構築の方言の計画としたか（<see cref="AssessmentFacts.IsTableRebuildDialect"/>。
    /// 計画器の結果が無ければ null）。空振りの検査が網の宣言（<see cref="SyncPlanFuzzDialect.PlansTableRebuilds"/>）と照らす
    /// </param>
    /// <param name="PairedPrimaryKey">
    /// 製品の計画が、対になった主キーの解除と付与（同じ <see cref="ReaddPair"/> を持つ主キーを外す文と付ける文）を持つか
    /// </param>
    /// <param name="ImplicitReasons">製品の計画の、計画器が足した文の理由（<see cref="ImplicitOrigin.Reason"/>）の集合</param>
    internal sealed record StepPlanResult(
        IReadOnlyList<string> Problems,
        string? Unrepresentable,
        int Withdrawals = 0,
        bool EndStateChecked = false,
        bool MovedFirst = false,
        bool NamedAroundRefusable = false,
        bool ExistingTableRebuild = false,
        bool CreateOnlyRebuild = false,
        bool RebuildWithSections = false,
        bool? TableRebuildDialect = null,
        bool PairedPrimaryKey = false,
        IReadOnlySet<ImplicitStepReason>? ImplicitReasons = null
    );

    /// <summary>
    /// 関係 N4（文の並びの計画の形）：計画器（<see cref="StepPlanner"/>）が作った文の並びの計画（製品の入口がそのまま返す計画）の、
    /// 文の由来と対は、対の約束（外す文 1 つと付ける文 1 つ・同じ表・外すのが前）と暗黙の理由ごとの対の有無を守る。読み取り用の形
    /// （テストの側の写し <see cref="SyncPlanSections"/>）の前寄せの印は、計画の前寄せした文の数（<see cref="SyncPlan.MovedFirstCount"/>）と合い、
    /// 写しは文を落とさず取り違えない（<see cref="ReadableFormProblems"/>）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 再構築の成分のうち面の行が書かないもの（<see cref="UnwrittenRebuildParts"/>）は、製品の計画と計画器の計画を位置で突き合わせて比べる
    /// （製品は同じ計画器を流すので、計画が決定的であることの照合になる）。
    /// </para>
    /// <para>
    /// 取り下げた項目の並び K（<see cref="StepPlannerResult.Withdrawn"/>）について、種別 <see cref="SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey"/>
    /// の警告は K の数だけあり、断られうる文の前後の名指しの 2 種別より前・ほかの警告より後に並び、外した項目の末尾は K の順の K
    /// （違反の頭 <see cref="StepPlanWithdrawalPrefix"/>。K が空ならその種別の警告は無い）。
    /// </para>
    /// <para>
    /// 文で表せた逐次 DDL の方言の計画（計画器の結果の事実 <see cref="AssessmentFacts.IsTableRebuildDialect"/> が偽）には、歩きの終わりの
    /// 状態の照合（<see cref="EndStateProblems"/>）を掛ける。
    /// </para>
    /// <para>
    /// 文で表せない計画（手で組んだ項目のメンバーの欠けなど）には掛けず、理由を返す。そのとき製品は、理由を名指しした
    /// <see cref="InvalidOperationException"/> を投げる（違反の頭 <see cref="StepPlanProductPrefix"/>）。
    /// </para>
    /// </remarks>
    /// <param name="items">差分の項目（元のケースの選び方のまま）</param>
    /// <param name="plan">製品の計画（<see cref="SyncPlanner.BuildPlan(IEnumerable{SchemaDiffItem}, SyncDialectCapabilities, SyncPlanContext?)"/> が例外を投げたら null）</param>
    /// <param name="capabilities">製品の計画と同じ能力</param>
    /// <param name="context">製品の計画と同じ文脈</param>
    /// <returns>結果（製品も計画器も例外を投げて、比べる計画が無ければ null）</returns>
    internal static StepPlanResult? StepPlanProblems(
        IReadOnlyList<SchemaDiffItem> items,
        SyncPlan? plan,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context
    )
    {
        StepPlannerResult planned;

        try
        {
            planned = StepPlanner.Plan(items, capabilities, context);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return plan is null
                ? null
                : new StepPlanResult(
                    [
                        $"{StepPlanRelationPrefix}the planner threw: {ex.GetType().Name}: {ex.Message}",
                    ],
                    null
                );
        }

        // 計画器がこの計画をテーブル再構築の方言の計画としたか（空振りの検査が網の宣言と照らす。文で表せない計画でも運ぶ）
        var rebuildDialect = planned.Facts.IsTableRebuildDialect;

        if (planned.Plan is not { } steps)
        {
            return new StepPlanResult(
                [.. ProductRefusalProblems(planned.Unrepresentable)],
                planned.Unrepresentable,
                TableRebuildDialect: rebuildDialect
            );
        }

        if (plan is null)
        {
            return new StepPlanResult(
                [
                    $"{StepPlanRelationPrefix}the product threw although the planner expressed the plan as steps",
                ],
                null,
                TableRebuildDialect: rebuildDialect
            );
        }

        var problems = new List<string>(StepShapeProblems(steps));

        // 歩きの終わりの状態（文の並びだけを読む）。テーブル再構築の方言の計画には掛けない（位置の状態は再構築の文を当てない。
        // 再構築の結果が同期後の図と合うことは実 SQLite の総当たりが確かめる）
        var endState =
            planned.Facts.Live is not null && !rebuildDialect
                ? EndStateProblems(steps, planned.Facts)
                : null;
        problems.AddRange(endState ?? []);

        // 前寄せの印（読み取り用の形のセクションの印が、計画の前寄せした文の数と合う＝先頭から続く印のセクションの文の数がちょうどその数）
        problems.AddRange(MovedFirstProblems(plan));

        // 読み取り用の形（テストの側の写し）が文を落とさず取り違えない（数の一致・主キーの項目の共有・元の項目の参照）
        problems.AddRange(ReadableFormProblems(plan));

        // 再構築の成分のうち面の行が書かないもの（rowid の別名の NULL の守りの列・補助オブジェクトの表と CREATE 文）は、製品の計画と
        // 計画器の計画を位置で突き合わせて別に比べる（製品は同じ計画器を流すので、計画が決定的であることの照合になる）
        foreach (
            var (index, expectedRebuild, actualRebuild) in plan
                .Rebuilds.Zip(steps.Rebuilds)
                .Select((p, i) => (i, p.First, p.Second))
        )
        {
            var expectedUnwritten = UnwrittenRebuildParts(expectedRebuild);
            var actualUnwritten = UnwrittenRebuildParts(actualRebuild);

            if (!expectedUnwritten.SequenceEqual(actualUnwritten, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{StepPlanRelationPrefix}the rebuild {index} ({expectedRebuild.TableName}) differs in parts the facets do not write{FirstDifference(expectedUnwritten, actualUnwritten)}"
                );
            }
        }

        problems.AddRange(WithdrawalProblems(planned.Withdrawn, steps));

        return new StepPlanResult(
            problems,
            null,
            planned.Withdrawn.Count,
            endState is not null,
            plan.Sections.Any(s => s.IsMovedFirst),
            plan.Warnings.Any(IsNamedAroundRefusable),
            plan.Rebuilds.Any(r => !r.CreateOnly),
            plan.Rebuilds.Any(r => r.CreateOnly),
            plan.Rebuilds.Count > 0 && plan.Sections.Count > 0,
            rebuildDialect,
            HasPairedPrimaryKey(plan),
            plan.Steps.Select(s => s.Origin)
                .OfType<ImplicitOrigin>()
                .Select(o => o.Reason)
                .ToHashSet()
        );

        // 計画器が文で表せない計画は、製品が項目の種類と無いメンバーを名指しした InvalidOperationException で断る
        IEnumerable<string> ProductRefusalProblems(string? reason)
        {
            try
            {
                new SyncPlanner().BuildPlan(items, capabilities, context);
            }
            catch (InvalidOperationException ex)
                when (reason is not null && ex.Message.Contains(reason, StringComparison.Ordinal))
            {
                // 期待どおり
                return [];
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return
                [
                    $"{StepPlanProductPrefix}the product threw {ex.GetType().Name}: {ex.Message}, "
                        + $"expected an InvalidOperationException naming [{reason}]",
                ];
            }

            return
            [
                $"{StepPlanProductPrefix}the product built a plan although the planner cannot express it as steps: {reason}",
            ];
        }
    }

    /// <summary>
    /// 取り下げの名指しと位置の違反：取り下げた項目の並び K について、取り下げの名指しは K の数だけあり、断られうる文の前後の名指しの
    /// 2 種別より前・ほかの警告より後に並び、外した項目の末尾は K の順の K
    /// </summary>
    /// <param name="withdrawn">計画器が取り下げた項目の並び（<see cref="StepPlannerResult.Withdrawn"/>）</param>
    /// <param name="stepPlan">計画器の計画</param>
    private static IEnumerable<string> WithdrawalProblems(
        IReadOnlyList<SchemaDiffItem> withdrawn,
        SyncPlan stepPlan
    )
    {
        var categories = stepPlan
            .Warnings.Select(w =>
                IsNamedAroundRefusable(w) ? 2
                : IsWithdrawalWarning(w) ? 1
                : 0
            )
            .ToList();

        if (
            categories.Count(c => c == 1) != withdrawn.Count
            || categories.Zip(categories.Skip(1)).Any(p => p.First > p.Second)
        )
        {
            yield return $"{StepPlanWithdrawalPrefix}the withdrawal warnings are not {withdrawn.Count} warnings between the other warnings and the warnings around refusable statements: "
                + string.Join(",", categories);
        }

        var tail = stepPlan
            .ExcludedItems.Skip(Math.Max(0, stepPlan.ExcludedItems.Count - withdrawn.Count))
            .ToList();

        if (
            tail.Count != withdrawn.Count
            || tail.Zip(withdrawn).Any(p => !ReferenceEquals(p.First, p.Second))
        )
        {
            yield return $"{StepPlanWithdrawalPrefix}the excluded items do not end with the withdrawn items in order: "
                + string.Join(
                    " / ",
                    stepPlan.ExcludedItems.Select(i => $"{i.Kind}: {i.Description}")
                );
        }
    }

    /// <summary>
    /// 前寄せの印の違反：読み取り用の形のセクションのうち前寄せの印（<see cref="SyncPlanSection.IsMovedFirst"/>）を持つものは先頭から続き、
    /// その文の数の和は計画の前寄せした文の数（<see cref="SyncPlan.MovedFirstCount"/>）とちょうど同じ
    /// </summary>
    /// <param name="plan">製品の計画</param>
    private static IEnumerable<string> MovedFirstProblems(SyncPlan plan)
    {
        var marks = plan.Sections.Select(s => s.IsMovedFirst).ToList();
        var leading = marks.TakeWhile(m => m).Count();
        var movedSteps = plan.Sections.Take(leading).Sum(s => s.Items.Count);

        if (marks.Skip(leading).Any(m => m) || movedSteps != plan.MovedFirstCount)
        {
            yield return $"{StepPlanRelationPrefix}the readable form marks sections moved first differently from the plan: "
                + $"[{string.Join(",", marks.Select(m => m ? 1 : 0))}] for {plan.MovedFirstCount} statements moved first";
        }
    }

    /// <summary>
    /// 読み取り用の形（テストの側の写し <see cref="SyncPlanSections"/>）の違反：写しが文を落とさず、文と項目を取り違えない
    /// </summary>
    /// <remarks>
    /// <para>(a) 数の一致：セクションの項目の数の和 ＋ 再構築の数 ＋ 列を動かす文の数 ＝ 計画の文の数。</para>
    /// <para>
    /// (b) 主キーの項目の共有：対を持つ主キーを外す文と付ける文は同じ 1 つの項目になる。ほかのどの文の項目も、ほかの文の項目と
    /// 共有しない（1 つの項目が 2 つの文に入るのは、対になった主キーの解除と付与だけ）。
    /// </para>
    /// <para>
    /// (c) 元の項目の参照：選んだ項目から来て、項目の種類が文の種類と同じ文は、元の項目そのもの。ただし列の組を並べ替えた外部キーの
    /// 追加は、列の組が文の列の組と同じで、元の項目とは別の参照。
    /// </para>
    /// </remarks>
    /// <param name="plan">製品の計画</param>
    internal static IEnumerable<string> ReadableFormProblems(SyncPlan plan)
    {
        var sections = plan.Sections;
        var itemCount = sections.Sum(s => s.Items.Count);
        var moveCount = plan.Steps.Count(s => s is MoveColumnStep);

        // (a) 数の一致（数が合わなければ、文と項目の対応が取れないので後の照合はしない）
        if (itemCount + plan.Rebuilds.Count + moveCount != plan.Steps.Count)
        {
            yield return $"{StepPlanRelationPrefix}the readable form dropped or added statements: "
                + $"{itemCount} items + {plan.Rebuilds.Count} rebuilds + {moveCount} column moves for {plan.Steps.Count} statements";
            yield break;
        }

        var pairs = sections
            .SelectMany(s => s.Steps.Zip(s.Items, (step, item) => (Step: step, Item: item)))
            .ToList();

        // (b) 主キーの項目の共有：項目ごとに、その項目を持つ文を集める
        foreach (
            var group in pairs.GroupBy(p => p.Item, ReferenceEqualityComparer.Instance).ToList()
        )
        {
            var steps = group.Select(p => p.Step).ToList();

            if (steps.Count == 1)
            {
                continue;
            }

            var isKeyPair =
                steps.Count == 2
                && steps.OfType<DropPrimaryKeyStep>().Count() == 1
                && steps.OfType<AddPrimaryKeyStep>().Count() == 1
                && steps[0].Pair is { } pair
                && ReferenceEquals(steps[1].Pair, pair);

            if (!isKeyPair)
            {
                yield return $"{StepPlanRelationPrefix}the readable form shares one item between statements other than a paired primary key drop and add: "
                    + string.Join(" / ", steps.Select(DescribeStep));
            }
        }

        foreach (
            var keyPair in pairs
                .Where(p =>
                    p.Step is DropPrimaryKeyStep or AddPrimaryKeyStep && p.Step.Pair is not null
                )
                .GroupBy(p => p.Step.Pair!)
        )
        {
            if (
                keyPair.Select(p => p.Item).Distinct(ReferenceEqualityComparer.Instance).Count()
                != 1
            )
            {
                yield return $"{StepPlanRelationPrefix}the readable form gives a paired primary key drop and add separate items: "
                    + string.Join(" / ", keyPair.Select(p => DescribeStep(p.Step)));
            }
        }

        // (c) 元の項目の参照
        foreach (var (step, item) in pairs)
        {
            if (
                step.Origin is not SelectedOrigin selected
                || selected.Item.Kind != StepSections.SectionOf(step).Kind
            )
            {
                continue;
            }

            if (
                step is AddForeignKeyStep add
                && !add.ColumnPairs.SequenceEqual(selected.Item.ForeignKeyColumnPairs)
            )
            {
                if (
                    ReferenceEquals(item, selected.Item)
                    || !item.ForeignKeyColumnPairs.SequenceEqual(add.ColumnPairs)
                )
                {
                    yield return $"{StepPlanRelationPrefix}the readable form does not give a reordered foreign key addition a copy with the statement's column pairs: "
                        + DescribeStep(step);
                }

                continue;
            }

            if (!ReferenceEquals(item, selected.Item))
            {
                yield return $"{StepPlanRelationPrefix}the readable form does not return the selected item itself: "
                    + DescribeStep(step);
            }
        }
    }

    /// <summary>計画が、対になった主キーの解除と付与（同じ対を持つ主キーを外す文と付ける文）を持つか</summary>
    private static bool HasPairedPrimaryKey(SyncPlan plan) =>
        plan
            .Steps.OfType<DropPrimaryKeyStep>()
            .Any(drop =>
                drop.Pair is { } pair
                && plan.Steps.OfType<AddPrimaryKeyStep>()
                    .Any(add => ReferenceEquals(add.Pair, pair))
            );

    /// <summary>
    /// 関係 N4 を掛けた数と掛けなかった数の要約（「relation N4 on X of Y; unrepresentable: 理由=数, …; withdrawals on W; end state on Z;
    /// moved first on M; rebuilds: existing E, create-only C, with sections S」）
    /// </summary>
    /// <param name="results">計画ごとの結果</param>
    internal static string DescribeStepPlans(IReadOnlyCollection<StepPlanResult> results)
    {
        var applied = results.Where(r => r.Unrepresentable is null).ToList();

        return $"relation N4 on {applied.Count} of {results.Count}; unrepresentable: "
            + Counts(results.Select(r => r.Unrepresentable))
            + $"; withdrawals on {applied.Count(r => r.Withdrawals > 0)}"
            + $"; end state on {applied.Count(r => r.EndStateChecked)}"
            + $"; moved first on {applied.Count(r => r.MovedFirst)}"
            + $"; rebuilds: existing {applied.Count(r => r.ExistingTableRebuild)}"
            + $", create-only {applied.Count(r => r.CreateOnlyRebuild)}"
            + $", with sections {applied.Count(r => r.RebuildWithSections)}"
            + $"; paired primary keys on {applied.Count(r => r.PairedPrimaryKey)}"
            + "; implicit steps: "
            + string.Join(
                ", ",
                Enum.GetValues<ImplicitStepReason>()
                    .Select(reason =>
                        $"{reason}={applied.Count(r => r.ImplicitReasons?.Contains(reason) == true)}"
                    )
            );
    }

    /// <summary>2 つの行の並びの最初の違い（「: [写し] &lt;&gt; [元] at line n」）</summary>
    private static string FirstDifference(
        IReadOnlyList<string> original,
        IReadOnlyList<string> copy
    )
    {
        var at = Enumerable
            .Range(0, Math.Min(original.Count, copy.Count))
            .FirstOrDefault(
                i => !string.Equals(original[i], copy[i], StringComparison.Ordinal),
                Math.Min(original.Count, copy.Count)
            );

        return $" at line {at + 1}: [{Line(copy, at)}] <> [{Line(original, at)}]";

        static string Line(IReadOnlyList<string> source, int index) =>
            index < source.Count ? source[index].Trim() : "(end)";
    }

    /// <summary>文の由来と対の形の違反（関係 N4 の 4 つ目の面）</summary>
    private static IEnumerable<string> StepShapeProblems(SyncPlan plan)
    {
        var steps = plan.Steps;

        // 1 つの対を持つ文は、制約を外す文 1 つと付ける文 1 つ（同じ種類の制約・同じ表）で、外す文が前
        foreach (
            var group in steps
                .Select((step, index) => (Step: step, Index: index))
                .Where(s => s.Step.Pair is not null)
                .GroupBy(s => s.Step.Pair!, ReferenceEqualityComparer.Instance)
        )
        {
            var members = group.ToList();
            var drops = members.Where(m => ConstraintOf(m.Step) is (_, false, _)).ToList();
            var adds = members.Where(m => ConstraintOf(m.Step) is (_, true, _)).ToList();

            if (
                members.Count != 2
                || drops.Count != 1
                || adds.Count != 1
                || ConstraintOf(drops[0].Step).Family != ConstraintOf(adds[0].Step).Family
                || SchemaNames.Key(ConstraintOf(drops[0].Step).Table)
                    != SchemaNames.Key(ConstraintOf(adds[0].Step).Table)
                || drops[0].Index > adds[0].Index
            )
            {
                yield return StepPlanRelationPrefix
                    + "a re-add pair is not one drop before one add of the same constraint on the same table: "
                    + string.Join(" / ", members.Select(m => $"#{m.Index} {DescribeStep(m.Step)}"));
            }
        }

        // 暗黙の理由ごとの対の有無
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (step.Origin is not ImplicitOrigin { Reason: var reason })
            {
                continue;
            }

            var isAdd = ConstraintOf(step) is (_, true, _);
            bool? needsPair = reason switch
            {
                ImplicitStepReason.ForeignKeyRebuild or ImplicitStepReason.PrimaryKeyRebuild =>
                    true,
                ImplicitStepReason.UniqueConstraintRebuild => isAdd ? true : null,
                _ => false,
            };

            if (needsPair is { } expected && expected != (step.Pair is not null))
            {
                yield return StepPlanRelationPrefix
                    + $"an implicit {reason} step {(expected ? "has no" : "has a")} re-add pair: #{i} {DescribeStep(step)}";
            }

            // 暗黙の理由と文の種類の対応
            var fits = reason switch
            {
                ImplicitStepReason.PrimaryKeyColumnNotNull => step is AlterColumnStep,
                ImplicitStepReason.VanishingForeignKey => step is DropForeignKeyStep,
                ImplicitStepReason.VanishingUniqueConstraint
                or ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey => step
                    is DropUniqueConstraintStep,
                ImplicitStepReason.ForeignKeyRebuild => step
                    is DropForeignKeyStep
                        or AddForeignKeyStep,
                ImplicitStepReason.PrimaryKeyRebuild => step
                    is DropPrimaryKeyStep
                        or AddPrimaryKeyStep,
                ImplicitStepReason.UniqueConstraintRebuild => step
                    is DropUniqueConstraintStep
                        or AddUniqueConstraintStep,
                _ => false,
            };

            if (!fits)
            {
                yield return StepPlanRelationPrefix
                    + $"an implicit {reason} step is of an unexpected kind: #{i} {DescribeStep(step)}";
            }

            // 外すだけの一意制約の削除は、理由ごとの警告（列と一緒に消える・主キーが置き換える）が同じ表・同じ構成列を名指しする
            SyncPlanWarningKind? naming = reason switch
            {
                ImplicitStepReason.VanishingUniqueConstraint =>
                    SyncPlanWarningKind.UniqueConstraintDroppedWithColumn,
                ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey =>
                    SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey,
                _ => null,
            };

            if (
                naming is { } kind
                && step is DropUniqueConstraintStep dropped
                && !plan.Warnings.Any(w =>
                    w.Kind == kind
                    && string.Equals(w.TableName, dropped.TableName, StringComparison.Ordinal)
                    && w.Detail.EndsWith(
                        $"({string.Join(", ", dropped.Columns)})",
                        StringComparison.Ordinal
                    )
                )
            )
            {
                yield return StepPlanRelationPrefix
                    + $"an implicit {reason} step has no {kind} warning naming it: #{i} {DescribeStep(step)}";
            }
        }

        // 作り直しを展開した外す文と付ける文は、両方あれば同じ対を持つ
        foreach (
            var group in steps
                .Where(s =>
                    s.Origin
                        is SelectedOrigin
                        {
                            Item.Kind: SchemaDiffKind.AlterForeignKey
                                or SchemaDiffKind.AlterUniqueConstraint
                        }
                )
                .GroupBy(s => ((SelectedOrigin)s.Origin).Item, ReferenceEqualityComparer.Instance)
        )
        {
            var drops = group.Where(s => ConstraintOf(s) is (_, false, _)).ToList();
            var adds = group.Where(s => ConstraintOf(s) is (_, true, _)).ToList();

            if (
                drops.Count > 0
                && adds.Count > 0
                && (
                    drops.Count != 1
                    || adds.Count != 1
                    || drops[0].Pair is null
                    || !ReferenceEquals(drops[0].Pair, adds[0].Pair)
                )
            )
            {
                yield return StepPlanRelationPrefix
                    + "an expanded alter's drop and add do not share one re-add pair: "
                    + string.Join(" / ", group.Select(DescribeStep));
            }
        }
    }

    /// <summary>制約を外す・付ける文の制約の種類・付けるか・表（制約の文でなければ種類は null）</summary>
    private static (string? Family, bool? Adds, string Table) ConstraintOf(SyncStep step) =>
        step switch
        {
            DropForeignKeyStep s => ("fk", false, s.ChildTable),
            AddForeignKeyStep s => ("fk", true, s.ChildTable),
            DropUniqueConstraintStep s => ("uq", false, s.TableName),
            AddUniqueConstraintStep s => ("uq", true, s.TableName),
            DropPrimaryKeyStep s => ("pk", false, s.TableName),
            AddPrimaryKeyStep s => ("pk", true, s.TableName),
            _ => (null, null, string.Empty),
        };

    private static string DescribeStep(SyncStep step) =>
        step switch
        {
            DropForeignKeyStep s => $"DropFK {s.ChildTable} {s.Name}",
            AddForeignKeyStep s => $"AddFK {s.ChildTable} {s.Name}",
            DropUniqueConstraintStep s =>
                $"DropUQ {s.TableName} {s.Name} ({string.Join(",", s.Columns)})",
            AddUniqueConstraintStep s =>
                $"AddUQ {s.TableName} {s.Name} ({string.Join(",", s.Columns)})",
            DropPrimaryKeyStep s => $"DropPK {s.TableName}",
            AddPrimaryKeyStep s => $"AddPK {s.TableName} ({string.Join(",", s.Columns)})",
            _ => step.GetType().Name,
        }
        + step.Origin switch
        {
            ImplicitOrigin o => $" [implicit {o.Reason}]",
            SelectedOrigin o => $" [selected {o.Item.Kind}]",
            _ => " [merged]",
        };

    /// <summary>
    /// 再構築の成分のうち、計画の面の行（<see cref="SyncPlanProjection.Plan"/>）が書かないものの行：rowid の別名の NULL の守りの列と、
    /// 補助オブジェクトの表と CREATE 文（種類と名前は面の行が書く）
    /// </summary>
    private static List<string> UnwrittenRebuildParts(TableRebuildPlan rebuild) =>
        [
            $"rowid-null-guard {rebuild.RowidAliasNullGuardColumn ?? "-"}",
            .. rebuild.AuxiliaryObjects.Select(a =>
                $"aux {a.Name} on {a.TableName}: {a.CreateSql}"
            ),
        ];

    /// <summary>一意制約の作り直しの取り下げの名指しか</summary>
    private static bool IsWithdrawalWarning(SyncPlanWarning warning) =>
        warning.Kind == SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey;

    /// <summary>断られうる文の前後で外れている制約の名指しの 2 種別か</summary>
    private static bool IsNamedAroundRefusable(SyncPlanWarning warning) =>
        warning.Kind
            is SyncPlanWarningKind.ConstraintRebuildAroundEmptyColumnChange
                or SyncPlanWarningKind.ConstraintRebuildAroundRowDependentChange;

    /// <summary>
    /// 関係 N4 の歩きの終わりの状態の照合：計画器の文の並びを DB の今の図から歩いた終わりの状態
    /// （<see cref="PlanWalk.Over"/> の <see cref="PlanWalk.End"/>）が、同期後に在る制約と合う
    /// </summary>
    /// <remarks>
    /// <para>期待の側は、計画の事実（<see cref="StepPlannerResult.Facts"/>）の同期後の事実と DB の今の図から組む。</para>
    /// <list type="bullet">
    /// <item>
    /// 主キー：同期後に在る表（DB の今の表のうち削除しない表と、足す表）ごとに、同期後の主キー（<see cref="PostSyncSchema.PrimaryKeyOf"/>）と
    /// 同じ列の並び。主キーの無い表は状態にも無い。
    /// </item>
    /// <item>
    /// 一意制約：同期後に在る表ごとに、同期後の一意制約（<see cref="PostSyncSchema.UniqueKeys"/>）と同じ列集合の多重集合。
    /// </item>
    /// <item>
    /// 外部キー：DB の今の外部キーから、選んだ項目の最終形の外部キーの削除（項目の DB のリレーション）を除き、外部キーの追加を足した多重集合
    /// （子の表・親の表・列の組の集合・実効名・参照アクション）。暗黙に外して付け直す外部キーは在る。
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="plan">計画器の文の並びの計画</param>
    /// <param name="facts">計画を作るのに使った計画の事実（DB の今の図があること）</param>
    private static IEnumerable<string> EndStateProblems(SyncPlan plan, AssessmentFacts facts)
    {
        var live = facts.Live!;
        var postSync = facts.PostSync;
        var limit = facts.Capabilities.IdentifierLengthLimit;
        var end = PlanWalk.Over(plan.Steps, live, facts.Capabilities).End;

        // 同期後に在る表（保存する形の表名）
        var tables = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (
            var table in live
                .Entities.Select(SchemaDiffService.NormalizeTable)
                .Where(t => !postSync.Dropped.Tables.Contains(t))
                .Concat(postSync.AddedTables.Keys)
        )
        {
            if (seen.Add(SchemaNames.Key(table)))
            {
                tables.Add(table);
            }
        }

        // 主キー
        foreach (var table in tables)
        {
            var expected = (postSync.PrimaryKeyOf(table) ?? []).Select(SchemaNames.Key).ToList();
            var actual = end.PrimaryKeys.TryGetValue(table, out var columns)
                ? columns.Select(SchemaNames.Key).ToList()
                : [];

            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                yield return $"{StepPlanEndStatePrefix}the primary key of {table} is ({string.Join(", ", actual)}), expected ({string.Join(", ", expected)})";
            }
        }

        foreach (var table in end.PrimaryKeys.Keys.Where(t => !seen.Contains(SchemaNames.Key(t))))
        {
            yield return $"{StepPlanEndStatePrefix}a primary key remains on {table}, which does not exist after the sync";
        }

        // 一意制約（表と列集合の多重集合）
        var expectedUniques = tables
            .SelectMany(table =>
                (
                    postSync.UniqueKeys.TryGetValue(table, out var keys)
                        ? keys
                        : (IReadOnlyList<IReadOnlyList<string>>)[]
                ).Select(columns =>
                    $"{SchemaNames.Key(table)} {UniqueConstraintNaming.ColumnSetSignature(columns)}"
                )
            )
            .Order(StringComparer.Ordinal)
            .ToList();
        var actualUniques = end
            .UniqueConstraints.Select(u => $"{SchemaNames.Key(u.Table)} {u.Id.ColumnSet}")
            .Order(StringComparer.Ordinal)
            .ToList();

        if (!expectedUniques.SequenceEqual(actualUniques, StringComparer.Ordinal))
        {
            yield return $"{StepPlanEndStatePrefix}the unique constraints differ{FirstDifference(expectedUniques, actualUniques)}";
        }

        // 外部キー（子の表・親の表・列の組の集合・実効名・参照アクションの多重集合）
        var droppedRelationships = new HashSet<Relationship>(
            postSync
                .Items.Where(i =>
                    i.Kind == SchemaDiffKind.DropForeignKey && i.Relationship is not null
                )
                .Select(i => i.Relationship!),
            ReferenceEqualityComparer.Instance
        );
        var liveNames = live.ForeignKeyNames(limit);
        var expectedForeignKeys = live
            .ForeignKeys.Where(fk => !droppedRelationships.Contains(fk.Relationship))
            .Select(fk =>
            {
                var child = SchemaDiffService.NormalizeTable(fk.Child);
                var parent = SchemaDiffService.NormalizeTable(fk.Parent);
                var name = liveNames.TryGetValue(fk.Relationship, out var resolved)
                    ? resolved
                    : ConstraintNames.EffectiveForeignKey(
                        fk.Relationship.ConstraintName,
                        child,
                        parent,
                        limit
                    );
                return ForeignKeyLine(
                    child,
                    parent,
                    fk.ColumnPairs,
                    SchemaNames.Key(name),
                    fk.Relationship.OnDelete,
                    fk.Relationship.OnUpdate
                );
            })
            .Concat(
                postSync
                    .Items.Where(i =>
                        i.Kind == SchemaDiffKind.AddForeignKey
                        && i.ChildEntity is not null
                        && i.ParentEntity is not null
                        && i.Relationship is not null
                    )
                    .Select(i =>
                        ForeignKeyLine(
                            SchemaDiffService.NormalizeTable(i.ChildEntity!),
                            SchemaDiffService.NormalizeTable(i.ParentEntity!),
                            i.ForeignKeyColumnPairs,
                            SchemaNames.Key(SyncScriptBuilderHelper.ForeignKeyNameToAdd(i, limit)),
                            i.Relationship!.OnDelete,
                            i.Relationship.OnUpdate
                        )
                    )
            )
            .Order(StringComparer.Ordinal)
            .ToList();
        var actualForeignKeys = end
            .ForeignKeys.Select(fk =>
                ForeignKeyLine(
                    fk.ChildTable,
                    fk.ParentTable,
                    fk.ColumnPairs,
                    fk.Id.Name,
                    fk.OnDelete,
                    fk.OnUpdate
                )
            )
            .Order(StringComparer.Ordinal)
            .ToList();

        if (!expectedForeignKeys.SequenceEqual(actualForeignKeys, StringComparer.Ordinal))
        {
            yield return $"{StepPlanEndStatePrefix}the foreign keys differ{FirstDifference(expectedForeignKeys, actualForeignKeys)}";
        }

        // 外部キー 1 本の行（列の組は並びを見ずに対応の集合で並べる。実効名は畳んだもの）
        static string ForeignKeyLine(
            string child,
            string parent,
            IEnumerable<ForeignKeyColumnNamePair> pairs,
            string foldedName,
            ForeignKeyReferentialAction onDelete,
            ForeignKeyReferentialAction onUpdate
        ) =>
            $"{SchemaNames.Key(child)} -> {SchemaNames.Key(parent)} ("
            + string.Join(
                ",",
                pairs
                    .Select(p =>
                        $"{SchemaNames.Key(p.ParentColumn)}>{SchemaNames.Key(p.ChildColumn)}"
                    )
                    .Order(StringComparer.Ordinal)
            )
            + $") {foldedName} {onDelete}/{onUpdate}";
    }

    /// <summary>理由ごとの数（「理由=数, …」。無ければ none）</summary>
    private static string Counts(IEnumerable<string?> reasons) =>
        string.Join(
            ", ",
            reasons
                .OfType<string>()
                .GroupBy(r => r, StringComparer.Ordinal)
                .Select(g => $"{g.Key}={g.Count()}")
                .DefaultIfEmpty("none")
        );

    /// <summary>
    /// 関係 N4 が空振りしていないこと：どの方言も、計画を作れた全部のケースに掛けた（掛けなかったケースが無い）。計画器の結果の事実
    /// （テーブル再構築の方言の計画か）が、どの計画でも網の宣言（<paramref name="plansTableRebuilds"/>）と一致する。逐次 DDL の方言では
    /// 掛けた計画の全部に歩きの終わりの状態の照合を掛け、テーブル再構築の方言では 1 つにも掛けない。DDL がトランザクションの方言には
    /// 取り下げのあるケースが無い。DDL が暗黙コミットされる方言で、流す種が普段の既定の種を全部含むときは、前寄せのある計画と断られうる文の
    /// 前後の名指しのある計画がそれぞれ 1 つ以上ある。逐次 DDL の方言で同じとき、対になった主キーの解除と付与を持つ計画が 1 つ以上ある。
    /// テーブル再構築の計画を組む方言で同じとき、既存の表の再構築・表を作るだけの再構築・
    /// 再構築と残ったセクションの両方を持つ計画がそれぞれ 1 つ以上ある。決定的な行が取り下げの形を必ず出す方言
    /// （<see cref="SyncPlanFuzzDialect.ScenarioRowsWithdrawUniqueRebuild"/>）で決定的な行を流したときは、取り下げのあるケースが 1 つ以上ある
    /// </summary>
    /// <param name="dialect">方言の名前</param>
    /// <param name="capabilities">方言の宣言</param>
    /// <param name="results">計画ごとの結果</param>
    /// <param name="assertsBehaviours">
    /// 前寄せ・前後の名指し・再構築の形を表明するか（流す種が普段の既定の種を全部含むときだけ。必須の形の表明と同じ門）
    /// </param>
    /// <param name="expectsWithdrawal">取り下げのあるケースを表明するか（決定的な行が取り下げの形を必ず出す方言で、決定的な行を流したとき）</param>
    /// <param name="plansTableRebuilds">
    /// 網の宣言で、方言がテーブル再構築の計画を組むか（<see cref="SyncPlanFuzzDialect.PlansTableRebuilds"/>。製品の判定を呼ばない）
    /// </param>
    internal static IEnumerable<string> StepPlanCoverageProblems(
        string dialect,
        SyncDialectCapabilities capabilities,
        IReadOnlyCollection<StepPlanResult> results,
        bool assertsBehaviours,
        bool expectsWithdrawal,
        bool plansTableRebuilds,
        IReadOnlySet<ImplicitStepReason>? declaredImplicitReasons = null
    )
    {
        // 種・決定的な行・網羅の行は、どの方言でも全部が文で表せる（掛けなかった理由は文で表せないことだけで、ここには出ない）
        foreach (
            var unexpected in results
                .Select(r => r.Unrepresentable)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
        )
        {
            yield return $"{dialect}: relation N4 was not applied for an unexpected reason: {unexpected}";
        }

        var applied = results.Where(r => r.Unrepresentable is null).ToList();

        if (results.Count > 0 && applied.Count == 0)
        {
            yield return $"{dialect}: relation N4 was not applied to any plan";
        }

        // 製品がテーブル再構築の方言かを決める判定を、網の独立した宣言で照らす（製品の判定が誤ると、歩きの終わりの状態の照合が
        // 黙って飛ばされるか、再構築の方言に掛かる）
        var disagreeing = results.Count(r =>
            r.TableRebuildDialect is { } rebuild && rebuild != plansTableRebuilds
        );

        if (disagreeing > 0)
        {
            yield return $"{dialect}: relation N4: the planner treats the dialect as a table-rebuild dialect differently from the net's declaration "
                + $"(declared {plansTableRebuilds}; {disagreeing} of {results.Count} plans disagree)";
        }

        // 歩きの終わりの状態の照合は、逐次 DDL の方言の掛けた計画の全部に掛かり、テーブル再構築の方言には掛からない
        // （照合が黙って飛ばされていない）
        var endStates = applied.Count(r => r.EndStateChecked);
        var expectedEndStates = plansTableRebuilds ? 0 : applied.Count;

        if (endStates != expectedEndStates)
        {
            yield return $"{dialect}: relation N4 checked the end state on {endStates} of {applied.Count} plans, expected {expectedEndStates}";
        }

        // 前寄せの中の取り下げは DDL が暗黙コミットされる方言だけの振る舞い
        if (capabilities.TransactionalDdl && results.Any(r => r.Withdrawals > 0))
        {
            yield return $"{dialect}: relation N4 counted a withdrawal on a dialect with transactional DDL";
        }

        // 前寄せと前後の名指しは、暗黙コミットの方言の普段の種の計画に必ず出る（出ないなら照合が空振りしている）
        if (assertsBehaviours && !capabilities.TransactionalDdl)
        {
            if (!applied.Any(r => r.MovedFirst))
            {
                yield return $"{dialect}: relation N4 checked no plan with statements moved first";
            }

            if (!applied.Any(r => r.NamedAroundRefusable))
            {
                yield return $"{dialect}: relation N4 checked no plan naming constraints around refusable statements";
            }
        }

        // 逐次 DDL の方言の普段の種では、対になった主キーの解除と付与を持つ計画が必ず出る（出ないなら読み取り用の形の主キーの
        // 項目の共有の照合が空振りしている）
        if (assertsBehaviours && !plansTableRebuilds && !applied.Any(r => r.PairedPrimaryKey))
        {
            yield return $"{dialect}: relation N4 checked no plan with a paired primary key drop and add";
        }

        // テーブル再構築の計画を組む方言の普段の種では、既存の表の再構築・表を作るだけの再構築・再構築と畳まれずに残ったセクションの
        // 両方を持つ計画が必ず出る（出ないなら再構築の照合が空振りしている）
        if (assertsBehaviours && plansTableRebuilds)
        {
            if (!applied.Any(r => r.ExistingTableRebuild))
            {
                yield return $"{dialect}: relation N4 checked no plan with a rebuild of an existing table";
            }

            if (!applied.Any(r => r.CreateOnlyRebuild))
            {
                yield return $"{dialect}: relation N4 checked no plan with a create-only rebuild";
            }

            if (!applied.Any(r => r.RebuildWithSections))
            {
                yield return $"{dialect}: relation N4 checked no plan with both rebuilds and sections";
            }
        }

        // 決定的な行が取り下げの形を必ず出す方言では、取り下げのあるケースがある（出ないなら取り下げの照合が空振りしている）
        if (expectsWithdrawal && !applied.Any(r => r.Withdrawals > 0))
        {
            yield return $"{dialect}: relation N4 checked no case with a withdrawal";
        }

        // 計画器が足す文の理由：方言の宣言（SyncPlanFuzzDialect.ImplicitStepReasonsPlannedFor）の理由ごとに、その理由の文を持つ計画が
        // 1 つ以上あり（出ないなら、その理由の文に掛かる照合と、その理由の文を読む規則が空振りしている）、宣言に無い理由の文を持つ計画が無い
        if (assertsBehaviours && declaredImplicitReasons is not null)
        {
            var observed = applied
                .SelectMany(r => r.ImplicitReasons ?? new HashSet<ImplicitStepReason>())
                .ToHashSet();

            foreach (var missing in declaredImplicitReasons.Where(r => !observed.Contains(r)))
            {
                yield return $"{dialect}: relation N4 checked no plan with an implicit step for {missing}";
            }

            foreach (var undeclared in observed.Where(r => !declaredImplicitReasons.Contains(r)))
            {
                yield return $"{dialect}: relation N4 checked a plan with an implicit step for {undeclared}, which the net does not declare for the dialect";
            }
        }
    }

    /// <summary>関係 N2 の違反の頭</summary>
    public const string CaseFoldRelationPrefix = "relation N2: ";

    /// <summary>
    /// 関係 N2：DB 側の図の表名・列名を大文字小文字だけ変えても（DB が畳む形＝ASCII の小文字。制約の名前は変えない）、差分・計画の
    /// 警告・外した項目は大文字小文字を無視して変わらない（破れた面ごとに違反の文を返す）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 大文字小文字を畳む DB（MySQL の lower_case_table_names=1 など）は、図の綴りで DDL が作った表を小文字で返し、制約の名前は
    /// DDL が付けたまま返す。差分は図の名前の無い制約の既定名を DDL と同じ入力（図の綴り）から求めるので、live の綴りに依らない。
    /// live の綴りへ揃えた図から求め直すと、識別子の上限で縮めた既定名のハッシュが変わり、偽の外部キーの追加と消えない削除が出る。
    /// </para>
    /// <para>
    /// 畳んだ DB 側の図は、名前の無い制約に元の図での実効名（DDL が付ける名前）を付けてから綴りを変える（取込の結果は必ず名前を持つ。
    /// 名前の無いまま畳むと、live の既定名そのものが変わって入力が製品の契約の外になる）。同期スクリプトは比べない（名前の分からない
    /// 外部キーを実行時に逆引きする文と、名前で外す文の違いが出る）。選び方は元のケースのものを差分の位置で写す。種は普段 4 つに 1 つ・
    /// 夜間は全部（<see cref="SyncPlanCapabilityNetTestsBase"/>）、決定的な行・網羅の行は全部に掛ける。
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> CaseFoldedLiveProblems(
        IDatabaseProvider provider,
        SchemaImportResult before,
        IReadOnlyList<Entity> targetEntities,
        IReadOnlyList<Relationship> targetRelationships,
        IReadOnlyList<SchemaDiffItem> items,
        SyncPlan plan
    )
    {
        var capabilities = provider.SyncCapabilities;
        var (foldedEntities, foldedRelationships) = FoldLiveNames(before, capabilities);
        var foldedItems = new SchemaDiffService()
            .Compute(
                foldedEntities,
                foldedRelationships,
                targetEntities,
                targetRelationships,
                capabilities
            )
            .Items;
        var expected = items.Select(Describe).ToList();
        var actual = foldedItems.Select(Describe).ToList();

        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            return
            [
                CaseFoldRelationPrefix
                    + "the diff differs: "
                    + string.Join(" / ", actual.Except(expected))
                    + " <> "
                    + string.Join(" / ", expected.Except(actual)),
            ];
        }

        for (var i = 0; i < items.Count; i++)
        {
            foldedItems[i].IsSelected = items[i].IsSelected;
        }

        SyncPlan foldedPlan;

        try
        {
            foldedPlan = new SyncPlanner().BuildPlan(
                foldedItems,
                capabilities,
                new SyncPlanContext
                {
                    LiveEntities = foldedEntities,
                    LiveRelationships = foldedRelationships,
                    ColumnDefaults =
                    [
                        .. before.ColumnDefaults.Select(d => new SchemaColumnDefault(
                            Fold(d.TableName),
                            Fold(d.ColumnName),
                            d.Expression
                        )),
                    ],
                    ColumnByteLengths =
                    [
                        .. before.ColumnByteLengths.Select(b =>
                            b with
                            {
                                TableName = Fold(b.TableName),
                                ColumnName = Fold(b.ColumnName),
                            }
                        ),
                    ],
                    ImportWarnings =
                    [
                        .. before.Warnings.Select(w => w with { TableName = Fold(w.TableName) }),
                    ],
                }
            );
        }
        catch (Exception ex)
        {
            return [$"{CaseFoldRelationPrefix}planning threw: {ex.GetType().Name}: {ex.Message}"];
        }

        var problems = new List<string>();
        Compare(
            "warnings",
            plan.Warnings.Select(w => Fold($"{w.Kind}({w.TableName}): {w.Detail}")),
            foldedPlan.Warnings.Select(w => Fold($"{w.Kind}({w.TableName}): {w.Detail}"))
        );
        Compare(
            "excluded",
            plan.ExcludedItems.Select(Describe),
            foldedPlan.ExcludedItems.Select(Describe)
        );
        return problems;

        void Compare(
            string facet,
            IEnumerable<string> expectedFacet,
            IEnumerable<string> actualFacet
        )
        {
            var expectedList = expectedFacet.ToList();
            var actualList = actualFacet.ToList();

            if (!expectedList.SequenceEqual(actualList, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{CaseFoldRelationPrefix}the {facet} differ: [{string.Join(" / ", actualList)}] <> [{string.Join(" / ", expectedList)}]"
                );
            }
        }

        static string Describe(SchemaDiffItem item) => Fold($"{item.Kind}: {item.Description}");
    }

    /// <summary>
    /// DB 側の図を Id を保って複製し、名前の無い外部キー・一意制約に実効名を付けてから、表名・列名を ASCII の小文字へ畳む
    /// （関係 N2 の入力）
    /// </summary>
    private static (List<Entity> Entities, List<Relationship> Relationships) FoldLiveNames(
        SchemaImportResult before,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;
        var foreignKeyNames = ConstraintNames.ResolveForeignKeyNames(
            before.Entities,
            before.Relationships,
            limit
        );
        var uniqueNames = ConstraintNames.ResolveUniqueConstraintNames(
            before.Entities,
            before.Relationships,
            limit
        );
        var uniqueNamesById = new Dictionary<Guid, string>();

        foreach (var (constraint, name) in uniqueNames)
        {
            uniqueNamesById[constraint.Id] = name;
        }

        var entities = before
            .Entities.Select(entity =>
            {
                var clone = entity.Clone(preserveId: true);

                foreach (var constraint in clone.UniqueConstraints)
                {
                    if (
                        constraint.Name is null
                        && uniqueNamesById.TryGetValue(constraint.Id, out var name)
                    )
                    {
                        constraint.Name = name;
                    }
                }

                clone.TableName = Fold(clone.TableName);

                foreach (var column in clone.Columns)
                {
                    column.Name = Fold(column.Name);
                }

                return clone;
            })
            .ToList();
        var relationships = before
            .Relationships.Select(relationship =>
            {
                var clone = new Relationship
                {
                    Id = relationship.Id,
                    SourceEntityId = relationship.SourceEntityId,
                    TargetEntityId = relationship.TargetEntityId,
                    Type = relationship.Type,
                    ColumnPairs = [.. relationship.ColumnPairs],
                    ConstraintName = relationship.ConstraintName,
                    OnDelete = relationship.OnDelete,
                    OnUpdate = relationship.OnUpdate,
                };

                if (
                    clone.ConstraintName is null
                    && foreignKeyNames.TryGetValue(relationship, out var name)
                )
                {
                    clone.ConstraintName = name;
                }

                return clone;
            })
            .ToList();
        return (entities, relationships);
    }

    /// <summary>ASCII の英大文字だけを小文字へ畳む（大文字小文字を畳む DB の形。ASCII の外の文字は変えない）</summary>
    private static string Fold(string text) =>
        string.Create(
            text.Length,
            text,
            (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
                }
            }
        );

    /// <summary>エンティティを Id を保って複製し、主キーの列に NULL 許容の印を立てる</summary>
    private static List<Entity> FlagPrimaryKeyColumnsNullable(IEnumerable<Entity> entities) =>
        [
            .. entities.Select(entity =>
            {
                var clone = entity.Clone(preserveId: true);

                foreach (var column in clone.Columns.Where(c => c.IsPrimaryKey))
                {
                    column.IsNullable = true;
                }

                return clone;
            }),
        ];

    /// <summary>
    /// 実 DB では DDL の後に SQL で足す計算列（<see cref="FuzzLiveSchema.ComputedColumnStatements"/>）を、取込の結果と同じく
    /// DB 側の図の列にする（NULL を許す計算列。式は図に載らない）。同じ文で足す普通の列（<see cref="FuzzLiveSchema.StatementColumns"/>＝
    /// 取込が外部キーを捨てる列）も、取込の結果と同じく NULL を許す普通の列にする（文の順＝列の情報を足した順）
    /// </summary>
    private static void AddComputedColumns(FuzzLiveSchema live, SyncPlanFuzzDialect dialect)
    {
        var statementColumns = live
            .StatementColumns.Select(c => FuzzLiveSchema.Key(c.Table, c.Column))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (
            var (key, info) in live
                .Columns.Where(pair => pair.Value.Computed || statementColumns.Contains(pair.Key))
                .ToList()
        )
        {
            var parts = key.Split('\u0001');
            var entity = live.Diagram.Entities.Single(e =>
                e.TableName.Equals(parts[0], StringComparison.OrdinalIgnoreCase)
            );
            entity.Columns.Add(
                new QuickER.Model.Column
                {
                    Name = parts[1],
                    DataType = dialect.TypeName(info.Kind, info.Length),
                    IsNullable = true,
                    IsComputed = info.Computed,
                }
            );
        }
    }
}

/// <summary>
/// 同期計画の能力の組の網：同期計画の総当たりと同じ生成器の種と決定的な行を、方言ごとの能力で計画まで作り、計画の項目の並びを
/// DB の規則（<see cref="SyncPlanStaticRules"/>）で判定する（DB を使わない＝Docker 不要・両 CI ジョブで流れる）
/// </summary>
/// <remarks>
/// 規則が正しいことは、実 DB の総当たり（<see cref="SyncPlanFuzzRunner"/>）が同じ規則を取り込んだ図と実際の計画に当て、予言した
/// 失敗がコミットしたら違反にすることで確かめる。環境変数 <c>QUICKER_SYNC_CAPNET_SEED</c> で種を 1 つだけ、
/// <c>QUICKER_SYNC_CAPNET_SEEDS</c> で種の数を変えられる。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SyncPlanCapabilityNetTestsBase
{
    /// <summary>派生クラスが受け持つ方言の軸の名前（値は <see cref="DialectNames"/>）</summary>
    public const string DialectAxis = "dialect";

    /// <summary>1 つの種だけを流す環境変数</summary>
    public const string SeedVariable = "QUICKER_SYNC_CAPNET_SEED";

    /// <summary>種の数を変える環境変数</summary>
    public const string SeedCountVariable = "QUICKER_SYNC_CAPNET_SEEDS";

    /// <summary>既定の種の数（方言ごと）</summary>
    public const int DefaultSeeds = 5000;

    /// <summary>夜間の既定の種の数（方言ごと）</summary>
    public const int NightlySeeds = 20000;

    /// <summary>報告に全文を載せる失敗の数</summary>
    private const int DetailedFailures = 8;

    /// <summary>種の決め方</summary>
    public static SeedSettings Seeds { get; } =
        new(SeedCountVariable, DefaultSeeds, NightlySeeds, singleVariable: SeedVariable);

    /// <summary>方言の部品を作る（DB には触れない）</summary>
    internal abstract SyncPlanFuzzDialect CreateDialect();

    [Fact(
        DisplayName = "[Integration] 同期計画の能力の組の網: 期待の行の差分と計画が期待どおりで、DB の規則を守る"
    )]
    public void ExpectationRows_Match()
    {
        var dialect = CreateDialect();

        try
        {
            var rows = SyncPlanExpectationRows.ForDialect(dialect.Name).ToList();
            var stepPlans = new List<SyncPlanCapabilityNetRunner.StepPlanResult>();
            var problems = rows.SelectMany(row =>
                    SyncPlanExpectationRunner.Run(row, [dialect.Name], onStepPlan: stepPlans.Add)
                )
                .ToList();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{dialect.Name}: expectation rows {rows.Count}, problems {problems.Count}"
            );
            // 関係 N3 を掛けない行は、手で項目を組む行だけ（差分が作らない項目の組み合わせを持ちうる）
            var handBuilt = rows.Count(row => row.Input.Items is not null);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{dialect.Name}: relation N3 on {rows.Count - handBuilt} of {rows.Count} expectation rows "
                    + $"(not on {handBuilt} rows with hand-built items)"
            );
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{dialect.Name}: {SyncPlanCapabilityNetRunner.DescribeStepPlans(stepPlans)} expectation rows"
            );

            if (problems.Count > 0)
            {
                throw new XunitException(string.Join("\n\n", problems));
            }

            CoverageGuard.AtLeast(
                $"{dialect.Name}: 照合した期待の行",
                rows.Count,
                SyncPlanExpectationRows.MinimumRowsFor(dialect.Name)
            );
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }
    }

    [Fact(DisplayName = "[Integration] 同期計画の能力の組の網: 計画の文の並びが DB の規則を守る")]
    [Trait(NightlyEngineTrait.Name, NightlyEngineTrait.Selected)]
    public void Plans_FollowDatabaseRules()
    {
        var plan = SeedSource.Resolve(Seeds);
        plan.WriteWarnings();
        var dialect = CreateDialect();
        var capabilities = dialect.Provider.SyncCapabilities;
        var coverage = new CoverageCounter(
            SyncPlanCapabilityNetDimensions.Table,
            SyncPlanCapabilityNetDimensions.FixedLevelsFor(dialect.Name)
        );
        var outcomes = new List<FuzzSeedOutcome>();
        var scenarioOutcomes = new List<FuzzSeedOutcome>();
        var coverageOutcomes = new List<FuzzSeedOutcome>();
        var caseFoldSeeds = 0;

        try
        {
            if (!plan.IsSingle)
            {
                foreach (var row in SyncPlanScenarios.For(dialect.Name))
                {
                    scenarioOutcomes.Add(
                        RunGuarded(
                            dialect,
                            row.Input.Create(dialect),
                            new FuzzSeedOutcome
                            {
                                Seed = 0,
                                Dialect = dialect.Name,
                                ScenarioId = row.Id,
                            }
                        )
                    );
                }

                // 網羅の行（SyncPlanCoverageRows）は、決定的な行と違って網羅の計数に数える
                foreach (var row in SyncPlanCoverageRows.ForNet(dialect.Name))
                {
                    var covered = RunGuarded(
                        dialect,
                        SyncPlanCoverageRows.Create(row, dialect),
                        new FuzzSeedOutcome
                        {
                            Seed = 0,
                            Dialect = dialect.Name,
                            ScenarioId = row.Id,
                        }
                    );
                    coverageOutcomes.Add(covered);
                    SyncPlanCapabilityNetDimensions.Observe(coverage, covered, capabilities);
                }
            }

            foreach (var seed in plan.Seeds)
            {
                // 関係 N2 は普段は種 4 つに 1 つ（種の番号で決める）、夜間は全部に掛ける（決定的な行・網羅の行は全部）
                var caseFold = CaseFoldRelationApplies(plan, seed);
                caseFoldSeeds += caseFold ? 1 : 0;
                var outcome = RunGuarded(
                    dialect,
                    SyncPlanFuzzGenerator.ForSeed(seed, dialect),
                    new FuzzSeedOutcome { Seed = seed, Dialect = dialect.Name },
                    caseFold
                );
                outcomes.Add(outcome);
                SyncPlanCapabilityNetDimensions.Observe(coverage, outcome, capabilities);
            }
        }
        finally
        {
            (dialect as IDisposable)?.Dispose();
        }

        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine(
            $"{dialect.Name}: seeds {outcomes.Count}, predicted failures "
                + $"{outcomes.Count(o => o.Notes.Contains(SyncPlanFuzzRunner.StaticRulePredictedNote))}, "
                + $"violations {outcomes.Count(o => o.Violations.Count > 0)}"
        );
        output?.WriteLine(
            $"{dialect.Name}: relation N2 on {caseFoldSeeds} of {outcomes.Count} seeds"
                + (
                    caseFoldSeeds < outcomes.Count
                        ? " (regular runs check every 4th seed; nightly checks all)"
                        : ""
                )
        );
        // 関係 N3 が実際に 2 回目の計画を作った種の数（計画が項目を外した種）と、除外の連鎖の形の種の数
        output?.WriteLine(
            $"{dialect.Name}: relation N3 on {outcomes.Count(o => o.Plan is { ExcludedItems.Count: > 0 })} of {outcomes.Count} seeds; exclusion chains "
                + string.Join(
                    ", ",
                    outcomes
                        .SelectMany(o => o.Shapes)
                        .Where(s => s.StartsWith("exclusion-chain-", StringComparison.Ordinal))
                        .GroupBy(s => s, StringComparer.Ordinal)
                        .OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => $"{g.Key}={g.Count()}")
                )
        );
        // 関係 N4 を掛けた種の数と、計画を文で表せずに掛けなかった種の数（理由ごと）
        output?.WriteLine(
            $"{dialect.Name}: {DescribeStepPlans(outcomes)} seeds; "
                + $"{DescribeStepPlans([.. scenarioOutcomes, .. coverageOutcomes])} scenario and coverage rows"
        );

        // 計画を作れなかったケース（結果の無いケース）は数えない
        static string DescribeStepPlans(IEnumerable<FuzzSeedOutcome> source) =>
            SyncPlanCapabilityNetRunner.DescribeStepPlans([
                .. source
                    .Select(o => o.StepPlan)
                    .OfType<SyncPlanCapabilityNetRunner.StepPlanResult>(),
            ]);

        foreach (var scenario in scenarioOutcomes)
        {
            output?.WriteLine(
                $"{dialect.Name}: scenario {scenario.ScenarioId}: predicted "
                    + $"{scenario.Notes.Contains(SyncPlanFuzzRunner.StaticRulePredictedNote)}, violations {scenario.Violations.Count} ({SyncPlanScenarios.Table[scenario.ScenarioId!].Description})"
            );
        }

        output?.WriteLine(
            $"{dialect.Name}: shape rows {SyncPlanScenarios.For(dialect.Name).Count(r => r.Id.StartsWith(SyncPlanShapeRows.IdPrefix, StringComparison.Ordinal))}"
                + $" (regular real runs {SyncPlanScenarios.For(dialect.Name).Count(r => r.Id.StartsWith(SyncPlanShapeRows.IdPrefix, StringComparison.Ordinal) && r.Input.RegularRun)})"
        );

        var coverageReport = coverage.Report();
        coverageReport.WriteSummary(output);

        if (!plan.IsSingle)
        {
            coverageReport.WriteGapUsage(
                SyncPlanCapabilityNetDimensions.Gaps,
                dialect.Name,
                output
            );
        }

        var report = new EngineReport($"SyncPlan.CapabilityNet.{dialect.Name}", DetailedFailures);

        foreach (var failure in outcomes.Where(o => o.Violations.Count > 0))
        {
            report.Add(
                SyncPlanCapabilityNetKnownFailures.ToFailure(failure),
                plan.ReproductionFor(failure.Seed)
            );
        }

        foreach (
            var failure in scenarioOutcomes
                .Concat(coverageOutcomes)
                .Where(o => o.Violations.Count > 0)
        )
        {
            report.Add(
                SyncPlanCapabilityNetKnownFailures.ToFailure(failure),
                $"{dialect.Name} の能力の組の網のテストを流す（行 `{failure.ScenarioId}` は種の前に必ず流れる）"
            );
        }

        var scenarioScope = SyncPlanScenarios.ScopeOf(dialect.Name);
        var known = SyncPlanCapabilityNetKnownFailures.Set.Classify(
            report.Failures,
            plan,
            // 網は全行を流すので、夜間にだけ流す行の範囲も受け持つ
            scope =>
                scope == dialect.Name
                || (
                    !plan.IsSingle
                    && (
                        scope == scenarioScope
                        || scope == scenarioScope + SyncPlanScenarios.NightlyScopeSuffix
                    )
                )
        );
        report.WriteSummary(output, known);

        var problems = new List<string>();

        if (
            known.Problem(
                "every plan must order its statements so that the database accepts them, or warn and drop nothing it re-adds after the failing statement. "
                    + $"Failed seeds: {string.Join(", ", known.Unknown.Select(f => f.Seed))}",
                report
            ) is
            { } problem
        )
        {
            problems.Add(problem);
        }

        // 合格側の行は、規則が失敗を予言し（偽陽性の守りが空振りしていない）、違反が 0
        problems.AddRange(SyncPlanScenarios.PassRowProblems(scenarioOutcomes));

        // 来歴の行：静的な規則が失敗を予言しない。行が実際に作った状態（来歴・子の表名の長さ・
        // 2 本目）で次元表の全組を数える（行を流したときだけ＝来歴の行は PostgreSQL だけ）
        problems.AddRange(SyncPlanHistoryRows.NetProblems(scenarioOutcomes));

        // 網羅の行が、出すはずの変更の種類と強制した選び方を出している
        problems.AddRange(SyncPlanCoverageRows.MissingKindProblems(coverageOutcomes));

        // 形の行に結んだ分岐の軸の台帳の項目：修正済みなら指摘のセルが流れて証拠が出る・未修正ならセルが流れない（決定的な行を流したときだけ）
        if (!plan.IsSingle)
        {
            problems.AddRange(SyncPlanBranchEvidence.NetProblems(dialect.Name, scenarioOutcomes));
        }

        if (scenarioOutcomes.Any(SyncPlanHistoryRows.IsHistoryRow))
        {
            var historyCoverage = new CoverageCounter(SyncPlanHistoryRows.Table);

            foreach (var outcome in scenarioOutcomes.Where(SyncPlanHistoryRows.IsHistoryRow))
            {
                SyncPlanHistoryRows.Observe(historyCoverage, outcome);
            }

            try
            {
                historyCoverage
                    .Report()
                    .AssertGaps(CoverageGap.Set(EngineIds.SyncPlan, []), dialect.Name);
            }
            catch (XunitException ex)
            {
                problems.Add(ex.Message);
            }
        }

        // 関係 N4 の結果（種・決定的な行・網羅の行）。どの方言も全部に掛ける（文で表せない計画は種・行に出ない）
        var stepPlanResults = outcomes
            .Concat(scenarioOutcomes)
            .Concat(coverageOutcomes)
            .Select(o => o.StepPlan)
            .OfType<SyncPlanCapabilityNetRunner.StepPlanResult>()
            .ToList();

        // 形の行（D4）の網羅の配列は、P1・Q6 の 3 つ組を含む
        problems.AddRange(
            SyncPlanShapeRows.MissingTriples().Select(t => $"shape rows miss the triple {t}")
        );

        // 必須の形（方言が作れない形を除く）は、網の種の集合全体で全部出る。表明するのは、流す種が普段の既定の種を全部含む
        // とき（種は開始番号からの連番なので、数が既定以上のとき）だけ。既定で全部出る形は、それより多い種でも必ず出る。
        // 既定より少ない種を明示した実行（変異テストの 500 種）では、確率でしか出ない形が欠けるので表明しない
        var assertsRequiredShapes = !plan.IsSingle && outcomes.Count >= plan.Settings.RegularCount;

        // 関係 N4 の空振りの検査（前寄せ・前後の名指し・再構築の形は、必須の形と同じ門で表明する）
        problems.AddRange(
            SyncPlanCapabilityNetRunner.StepPlanCoverageProblems(
                dialect.Name,
                capabilities,
                stepPlanResults,
                assertsRequiredShapes,
                !plan.IsSingle && dialect.ScenarioRowsWithdrawUniqueRebuild,
                dialect.PlansTableRebuilds,
                SyncPlanFuzzDialect.ImplicitStepReasonsPlannedFor(dialect.Name)
            )
        );

        if (!plan.IsSingle && !assertsRequiredShapes)
        {
            output?.WriteLine(
                $"{dialect.Name}: required shapes not asserted (seeds {outcomes.Count} < regular default {plan.Settings.RegularCount})"
            );
        }

        if (assertsRequiredShapes)
        {
            try
            {
                CoverageGuard.ContainsAll(
                    $"{dialect.Name}: 網の種の集合全体に出た図の形",
                    outcomes.SelectMany(o => o.Shapes),
                    FuzzShape.RequiredFor(dialect, realDatabase: false)
                );
            }
            catch (XunitException ex)
            {
                problems.Add(ex.Message);
            }
        }

        if (problems.Count > 0)
        {
            throw new XunitException(string.Join("\n\n", problems));
        }

        if (!plan.IsSingle)
        {
            coverageReport.AssertGaps(SyncPlanCapabilityNetDimensions.Gaps, dialect.Name, plan);
        }
    }

    /// <summary>1 ケースを流し、予期しない例外はそのケースの違反にする</summary>
    /// <summary>
    /// 関係 N2（<see cref="SyncPlanCapabilityNetRunner.CaseFoldedLiveProblems"/>）をこの種に掛けるか（夜間は全部、普段は種の番号が
    /// 4 の倍数のものだけ。差分と計画をもう 1 回作るので、普段の網の時間を抑える）
    /// </summary>
    private static bool CaseFoldRelationApplies(SeedPlan plan, int seed) =>
        plan.Profile == EngineProfile.Nightly || seed % 4 == 0;

    private static FuzzSeedOutcome RunGuarded(
        SyncPlanFuzzDialect dialect,
        ISyncPlanFuzzCase generator,
        FuzzSeedOutcome outcome,
        bool caseFoldRelation = true
    )
    {
        try
        {
            return SyncPlanCapabilityNetRunner.Run(dialect, generator, outcome, caseFoldRelation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome.Violations.Add(
                $"{SyncPlanFuzzRunner.SeedThrewPrefix} {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"
            );
            return outcome;
        }
    }
}

/// <summary>SQL Server の能力</summary>
[MatrixAxisValue(SyncPlanCapabilityNetTestsBase.DialectAxis, DialectNames.SqlServer)]
public sealed class SqlServerSyncPlanCapabilityNetTests : SyncPlanCapabilityNetTestsBase
{
    internal override SyncPlanFuzzDialect CreateDialect() =>
        new SqlServerSyncPlanFuzzDialect(null!);
}

/// <summary>PostgreSQL の能力</summary>
[MatrixAxisValue(SyncPlanCapabilityNetTestsBase.DialectAxis, DialectNames.PostgreSql)]
public sealed class PostgreSqlSyncPlanCapabilityNetTests : SyncPlanCapabilityNetTestsBase
{
    internal override SyncPlanFuzzDialect CreateDialect() =>
        new PostgreSqlSyncPlanFuzzDialect(null!);
}

/// <summary>MySQL の能力</summary>
[MatrixAxisValue(SyncPlanCapabilityNetTestsBase.DialectAxis, DialectNames.MySql)]
public sealed class MySqlSyncPlanCapabilityNetTests : SyncPlanCapabilityNetTestsBase
{
    internal override SyncPlanFuzzDialect CreateDialect() => new MySqlSyncPlanFuzzDialect(null!);
}

/// <summary>Oracle の能力</summary>
[MatrixAxisValue(SyncPlanCapabilityNetTestsBase.DialectAxis, DialectNames.Oracle)]
public sealed class OracleSyncPlanCapabilityNetTests : SyncPlanCapabilityNetTestsBase
{
    internal override SyncPlanFuzzDialect CreateDialect() => new OracleSyncPlanFuzzDialect(null!);
}

/// <summary>SQLite の能力（再構築の方言なので規則は判定しない＝計画の例外と網羅だけ）</summary>
[MatrixAxisValue(SyncPlanCapabilityNetTestsBase.DialectAxis, DialectNames.Sqlite)]
public sealed class SqliteSyncPlanCapabilityNetTests : SyncPlanCapabilityNetTestsBase
{
    internal override SyncPlanFuzzDialect CreateDialect() => new SqliteSyncPlanFuzzDialect();
}
