using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
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
/// 差分の選び方・変更の種類に、方言の能力（<see cref="SyncDialectCapabilities"/> のうち同期計画の形を変える 22 項目）を足したもの。
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
        // n3-kept-unique-stale-judgments：前寄せの中で一意制約の作り直しを 1 つ取り下げた（KeepOneReaddedUnique）後、その前に決めた
        // 判定が見直されない。取り下げで参照先の一意制約が残るのに、付け直す外部キーの候補キーの警告
        // （ForeignKeyRebuildMayLoseCandidateKey）と一意制約の削除の警告が残り、外部キーの追加の並びも「証明できない付け直しを最後」の
        // ままになる（取り下げた項目を最初から選ばない計画では、付け直しが新しい外部キーより前に並ぶ）。関係 N3 が見つけた。
        // 直すのは、取り下げを輪へ戻す区切り（設計書の区切り 7）。
        // 条件：再設計が止まるか、0.5.0 までに区切り 7 に届かない見込みになったら、今の計画器で直す区切りを立てる
        // （2026-11-16 のレビューの判断。害は当てはまらない警告と安全側でない並びで、名指しなしに制約が消える形ではない）
        .. SyncPlanFuzzKnownFailures.Scenario(
            "n3-kept-unique-stale-judgments",
            "mysql",
            "スキーマ同期の再設計の区切り 7（評価の取り下げを輪へ戻す）",
            ["d4-mysql-265"],
            outcome =>
                outcome.Violations.Count > 0
                && outcome.Violations.All(v =>
                    v.StartsWith(
                        SyncPlanCapabilityNetRunner.ExclusionFixedPointRelationPrefix
                            + "the plan differs without the excluded items",
                        StringComparison.Ordinal
                    )
                )
                && outcome.Warnings.Any(w =>
                    w.StartsWith(
                        nameof(SyncPlanWarningKind.UniqueConstraintRebuildKeptForForeignKey),
                        StringComparison.Ordinal
                    )
                )
        ),
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
        var context = new SyncPlanContext
        {
            LiveEntities = before.Entities,
            LiveRelationships = before.Relationships,
            ColumnDefaults = before.ColumnDefaults,
            ImportWarnings = before.Warnings,
        };

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
            live.DataFailures
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

        var roundTrip = StepRoundTripProblems(
            diff.Items,
            plan,
            new Dictionary<SchemaDiffItem, int>(ReferenceEqualityComparer.Instance),
            before.Entities,
            provider.SyncScriptBuilder.Build
        );
        outcome.Violations.AddRange(roundTrip.Problems);
        outcome.StepRoundTripChecked = roundTrip.Unrepresentable is null;
        outcome.StepRoundTripUnrepresentable = roundTrip.Unrepresentable;

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
                ExclusionFixedPointRelationPrefix
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
            second = new SyncPlanner().BuildPlan(items, capabilities, context);
            again = describe(second);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return
            [
                $"{ExclusionFixedPointRelationPrefix}planning without the excluded items threw: {ex.GetType().Name}: {ex.Message}",
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
                ExclusionFixedPointRelationPrefix
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
                $"{ExclusionFixedPointRelationPrefix}the plan differs without the excluded items: "
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
    public const string StepRoundTripRelationPrefix = "relation N4: ";

    /// <summary>関係 N4 の結果</summary>
    /// <param name="Problems">違反の文</param>
    /// <param name="Unrepresentable">計画を文で表せなかった理由（関係を掛けなかった。掛けたなら null）</param>
    internal sealed record StepRoundTripResult(
        IReadOnlyList<string> Problems,
        string? Unrepresentable
    );

    /// <summary>
    /// 関係 N4（文の往復）：計画を文の並びとして読み取り（<see cref="SyncPlanStepReader"/>）、製品の写し
    /// （<see cref="StepLowering.ToSyncPlan"/>）で計画へ戻すと、読み取り用の形は計画の全部の面が元と同じで、同期スクリプトは
    /// 読み取り用の形も文の値だけの形も元と同じ。文の由来と対は、対の約束（外す文 1 つと付ける文 1 つ・同じ表・外すのが前）と
    /// 暗黙の理由ごとの対の有無を守る
    /// </summary>
    /// <remarks>
    /// <para>
    /// 文の並びは計画器の作り直しの入口で、写しは今の計画・同期スクリプト・画面へ渡す形を作る。往復で面が変われば、写しか文の値が
    /// 今の計画の項目の持つ情報を落としている。文で表せない計画（手で組んだ項目のメンバーの欠けなど）には掛けず、理由を返す。
    /// </para>
    /// <para>
    /// 同期スクリプトのビルダーが例外を投げる計画は、写した計画のビルダーも同じ型の例外を投げることを見る。
    /// </para>
    /// </remarks>
    /// <param name="items">差分の項目（元のケースの選び方のまま）</param>
    /// <param name="plan">元の計画</param>
    /// <param name="labels">手で組んだ項目の番号（無ければ空）</param>
    /// <param name="liveEntities">DB 側の図</param>
    /// <param name="buildScript">同期スクリプトを作る（実方言だけ。能力の組だけなら null）</param>
    internal static StepRoundTripResult StepRoundTripProblems(
        IReadOnlyList<SchemaDiffItem> items,
        SyncPlan plan,
        IReadOnlyDictionary<SchemaDiffItem, int> labels,
        IReadOnlyList<Entity> liveEntities,
        Func<SyncPlan, string>? buildScript
    )
    {
        var read = SyncPlanStepReader.Read(plan, items.Where(i => i.IsSelected));

        if (read.Plan is not { } steps)
        {
            return new StepRoundTripResult(
                [.. read.Problems.Select(p => StepRoundTripRelationPrefix + "reading: " + p)],
                read.Unrepresentable
            );
        }

        var problems = new List<string>(
            read.Problems.Select(p => StepRoundTripRelationPrefix + "reading: " + p)
        );
        problems.AddRange(StepShapeProblems(steps));

        SyncPlan lowered;
        SyncPlan loweredFields;

        try
        {
            lowered = StepLowering.ToSyncPlan(steps);
            loweredFields = StepLowering.ToSyncPlan(steps, fieldsOnly: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            problems.Add(
                $"{StepRoundTripRelationPrefix}lowering threw: {ex.GetType().Name}: {ex.Message}"
            );
            return new StepRoundTripResult(problems, null);
        }

        // 文の値だけの形の項目は、どれも文から組み立てた項目（選んだ項目そのものを返すと、文の値がビルダーに足りるかを見ていない）
        var selectedItems = new HashSet<SchemaDiffItem>(
            items.Where(i => i.IsSelected),
            ReferenceEqualityComparer.Instance
        );

        if (
            loweredFields
                .Sections.SelectMany(s => s.Items)
                .FirstOrDefault(selectedItems.Contains) is
            { } reused
        )
        {
            problems.Add(
                $"{StepRoundTripRelationPrefix}the fields-only form returns a selected item itself: "
                    + $"{reused.Kind}: {reused.Description}"
            );
        }

        var original = SyncPlanProjection.Plan(plan, labels, liveEntities);
        var copied = SyncPlanProjection.Plan(lowered, labels, liveEntities);

        foreach (var (facet, lines) in original)
        {
            var other = copied.TryGetValue(facet, out var found) ? found : [];

            if (!lines.SequenceEqual(other, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{StepRoundTripRelationPrefix}the {facet} differ{FirstDifference(lines, other)}"
                );
            }
        }

        // 2 つのセクションに入る同じ 1 つの項目（主キーの変更の解除と付与）は、写した計画でも同じ 1 つの項目（面の行は参照を見ない）
        var sharing = SharedItemPattern(plan);

        foreach (
            var (form, copy) in new[] { ("readable", lowered), ("fields-only", loweredFields) }
        )
        {
            if (!sharing.SequenceEqual(SharedItemPattern(copy)))
            {
                problems.Add(
                    $"{StepRoundTripRelationPrefix}the {form} form shares items between sections differently: "
                        + $"[{string.Join(",", SharedItemPattern(copy))}] <> [{string.Join(",", sharing)}]"
                );
            }
        }

        if (buildScript is not null)
        {
            var (script, thrown) = Build(plan);
            CompareScript("readable", lowered);
            CompareScript("fields-only", loweredFields);

            void CompareScript(string form, SyncPlan copy)
            {
                var (copyScript, copyThrown) = Build(copy);

                if (thrown is not null || copyThrown is not null)
                {
                    if (thrown?.GetType() != copyThrown?.GetType())
                    {
                        problems.Add(
                            $"{StepRoundTripRelationPrefix}the {form} script builder throws differently: "
                                + $"[{Name(copyThrown)}] <> [{Name(thrown)}]"
                        );
                    }

                    return;
                }

                if (!string.Equals(script, copyScript, StringComparison.Ordinal))
                {
                    var lines = script!.Split('\n');
                    var copyLines = copyScript!.Split('\n');
                    problems.Add(
                        $"{StepRoundTripRelationPrefix}the {form} script differs{FirstDifference(lines, copyLines)}"
                    );
                }
            }
        }

        return new StepRoundTripResult(problems, null);

        (string? Script, Exception? Thrown) Build(SyncPlan target)
        {
            try
            {
                return (buildScript!(target), null);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return (null, ex);
            }
        }

        static string Name(Exception? ex) =>
            ex is null ? "no exception" : $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
    }

    /// <summary>セクションの項目ごとの、同じ項目（参照）が最初に現れた位置（計画の項目の共有の形）</summary>
    private static List<int> SharedItemPattern(SyncPlan plan)
    {
        var first = new Dictionary<SchemaDiffItem, int>(ReferenceEqualityComparer.Instance);
        var pattern = new List<int>();

        foreach (var item in plan.Sections.SelectMany(s => s.Items))
        {
            if (!first.TryGetValue(item, out var at))
            {
                at = pattern.Count;
                first[item] = at;
            }

            pattern.Add(at);
        }

        return pattern;
    }

    /// <summary>関係 N4 を掛けた数と掛けなかった数の要約（「relation N4 on X of Y; unrepresentable: 理由=数, …」）</summary>
    /// <param name="results">計画ごとの結果（掛けたら null、掛けなかったら理由）</param>
    internal static string DescribeStepRoundTrips(IReadOnlyCollection<string?> results) =>
        $"relation N4 on {results.Count(r => r is null)} of {results.Count}; unrepresentable: "
        + string.Join(
            ", ",
            results
                .OfType<string>()
                .GroupBy(r => r, StringComparer.Ordinal)
                .Select(g => $"{g.Key}={g.Count()}")
                .DefaultIfEmpty("none")
        );

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
    private static IEnumerable<string> StepShapeProblems(StepPlan plan)
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
                yield return StepRoundTripRelationPrefix
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
                yield return StepRoundTripRelationPrefix
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
                yield return StepRoundTripRelationPrefix
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
                yield return StepRoundTripRelationPrefix
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
                yield return StepRoundTripRelationPrefix
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
            var roundTrips = new List<string?>();
            var problems = rows.SelectMany(row =>
                    SyncPlanExpectationRunner.Run(
                        row,
                        [dialect.Name],
                        onStepRoundTrip: roundTrips.Add
                    )
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
                $"{dialect.Name}: {SyncPlanCapabilityNetRunner.DescribeStepRoundTrips(roundTrips)} expectation rows"
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
            $"{dialect.Name}: {DescribeRoundTrips(outcomes)} seeds; "
                + $"{DescribeRoundTrips([.. scenarioOutcomes, .. coverageOutcomes])} scenario and coverage rows"
        );

        // 計画を作れなかったケース（掛けたでも表せないでもない）は数えない
        static string DescribeRoundTrips(IEnumerable<FuzzSeedOutcome> source) =>
            SyncPlanCapabilityNetRunner.DescribeStepRoundTrips([
                .. source
                    .Where(o =>
                        o.StepRoundTripChecked || o.StepRoundTripUnrepresentable is not null
                    )
                    .Select(o => o.StepRoundTripUnrepresentable),
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

        // 来歴の行：4m-F1 の行は静的な規則が名前の衝突を予言し、合格の行は予言しない。行が実際に作った状態（来歴・子の表名の長さ・
        // 2 本目）で次元表の全組を数える（行を流したときだけ＝来歴の行は PostgreSQL だけ）
        problems.AddRange(SyncPlanHistoryRows.NetProblems(scenarioOutcomes));

        // 網羅の行が、出すはずの変更の種類と強制した選び方を出している
        problems.AddRange(SyncPlanCoverageRows.MissingKindProblems(coverageOutcomes));

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

        // 形の行（D4）の網羅の配列は、P1・Q6 の 3 つ組を含む
        problems.AddRange(
            SyncPlanShapeRows.MissingTriples().Select(t => $"shape rows miss the triple {t}")
        );

        // 必須の形（方言が作れない形を除く）は、網の種の集合全体で全部出る。表明するのは、流す種が普段の既定の種を全部含む
        // とき（種は開始番号からの連番なので、数が既定以上のとき）だけ。既定で全部出る形は、それより多い種でも必ず出る。
        // 既定より少ない種を明示した実行（変異テストの 500 種）では、確率でしか出ない形が欠けるので表明しない
        var assertsRequiredShapes = !plan.IsSingle && outcomes.Count >= plan.Settings.RegularCount;

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
