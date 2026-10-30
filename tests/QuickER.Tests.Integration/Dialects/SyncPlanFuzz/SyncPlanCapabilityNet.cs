using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
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
/// 差分の選び方・変更の種類に、方言の能力（<see cref="SyncDialectCapabilities"/> のうち同期計画の形を変える 17 項目）を足したもの。
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
        nameof(SyncDialectCapabilities.SupportsComputedColumnTypeChange),
        nameof(SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey),
        nameof(SyncDialectCapabilities.ReferencedKeyRequiresSameColumnOrder),
        nameof(SyncDialectCapabilities.ForeignKeyAllowsStringLengthDifference),
        nameof(SyncDialectCapabilities.TransactionalDdl),
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
        ("SupportsComputedColumnTypeChange", ["False", "True", "True", "False", "False"]),
        ("SupportsUniqueConstraintOnPrimaryKey", ["True", "True", "True", "False", "False"]),
        ("ReferencedKeyRequiresSameColumnOrder", ["True", "False", "True", "False", "False"]),
        ("ForeignKeyAllowsStringLengthDifference", ["False", "True", "True", "True", "True"]),
        ("TransactionalDdl", ["True", "True", "False", "False", "True"]),
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
                    member == nameof(SyncDialectCapabilities.ColumnReorder)
                        ? Enum.GetValues<ColumnReorderMode>()
                            .Select(value => new Level(value.ToString()))
                            .ToList()
                        : [new Level("True"), new Level("False")],
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
                .. MutationGaps(
                    DialectNames.SqlServer,
                    "fk action",
                    "P2-末",
                    "生成器は SQL Server で参照アクションを変えない（SyncPlanFuzzDialect.AllowCascade が偽：連鎖の経路の重なりを CREATE で断る DB の制約に当たらないよう、ON DELETE の切り替えをまとめて作らない。重ならない経路だけで切り替える生成はエンジンの側で作れる）"
                ),
                .. new[] { "alter type duplicated fk", "drop one of duplicated fks" }.SelectMany(
                    mutation =>
                        MutationGaps(
                            DialectNames.Oracle,
                            mutation,
                            null,
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
                        null,
                        "Oracle は同じ列の並びに一意制約を 2 つ張れない（ORA-02261・DB の事実）ので、同じ列の並びに一意制約が 2 つある形を前提にする変更は作れない"
                    )
                ),
                .. MutationGaps(
                    DialectNames.Sqlite,
                    "respell type",
                    null,
                    "SQLite は宣言の型をそのまま覚え、型の正規化を持たない（製品の事実＝NormalizeTypeNotation が恒等）ので、同じ型の別の綴りが無い"
                ),
                .. LevelGaps(
                    DialectNames.Sqlite,
                    AlteredType,
                    level => level == "AtLimit",
                    null,
                    "SQLite は型の長さを検査せず上限を持たない（DB の事実）ので、方言の上限ちょうどの型が無い"
                ),
                .. LevelGaps(
                    DialectNames.SqlServer,
                    ReferentialAction,
                    level => level != NotApplicable,
                    "P2-末",
                    "生成器は SQL Server で参照アクションを変えない（fk action の欠けと同じ理由）",
                    // 変更の種類との組（fk action × 行き先）は fk action の項目が受け持つ
                    except: SyncPlanFuzzDimensions.Mutation
                ),
            ]
        );

    /// <summary>
    /// 変更の種類が一度も出ない方言の欠け（選び方との組と、固定した能力の水準との組の全部）。
    /// <paramref name="fixedIn"/> が null なら受け入れ（製品か DB の事実）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> MutationGaps(
        string dialect,
        string mutation,
        string? fixedIn,
        string reason
    ) =>
        LevelGaps(
            dialect,
            SyncPlanFuzzDimensions.Mutation,
            kind => kind == mutation,
            fixedIn,
            reason
        );

    /// <summary>
    /// 因子 <paramref name="factor"/> の水準（<paramref name="levels"/> が真）が一度も出ない方言の欠け（その因子とほかの全因子との組）。
    /// <paramref name="fixedIn"/> が null なら受け入れ（製品か DB の事実）。<paramref name="except"/> の因子との組は作らない
    /// （ほかの項目が受け持つ組）
    /// </summary>
    private static IEnumerable<CoverageGapEntry> LevelGaps(
        string dialect,
        string factor,
        Func<string, bool> levels,
        string? fixedIn,
        string reason,
        string? except = null
    )
    {
        var names = Table.Factors.Select(f => f.Name).ToList();
        var index = names.IndexOf(factor);

        for (var other = 0; other < names.Count; other++)
        {
            if (other == index || names[other] == except)
            {
                continue;
            }

            var (a, b) = other < index ? (names[other], factor) : (factor, names[other]);
            Func<string, string, bool> predicate =
                a == factor ? (level, _) => levels(level) : (_, level) => levels(level);

            yield return fixedIn is null
                ? CoverageGap.AcceptedMatching(dialect, "P2-4", reason, a, b, predicate)
                : CoverageGap.Matching(dialect, "P2-4", fixedIn, reason, a, b, predicate);
        }
    }

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncPlan,
            "CapabilityNet",
            "同期計画の能力の組の網（5 方言の能力・DB なし）",
            Table,
            SyncPlanScenarios.Table.Count,
            [
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanCapabilityNet.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanStaticRules.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanFuzzGenerator.cs",
                "tests/QuickER.Tests.Integration/Dialects/SyncPlanFuzz/SyncPlanScenarios.cs",
            ],
            [
                "src/QuickER.Provider/SchemaDiffService.cs",
                "src/QuickER.Provider/SyncPlanner.cs",
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
        // synthesized-unique-name-collision（MySQL・PostgreSQL・修正 E）：名前の無い一意制約の合成名が、列を変えた名前付きの
        // 一意制約の名前と同じになる（規則 R4 が名前の衝突を予言する）。行は実 DB と同じ mysql-386・postgresql-386
        .. SyncPlanFuzzKnownFailures.Scenario(
            "synthesized-unique-name-collision",
            DialectNames.MySql,
            "修正 E",
            ["mysql-386-synthesized-unique-name"],
            IsUnwarnedNameCollision
        ),
        .. SyncPlanFuzzKnownFailures.Scenario(
            "synthesized-unique-name-collision",
            DialectNames.PostgreSql,
            "修正 E",
            ["postgresql-386-synthesized-unique-name"],
            IsUnwarnedNameCollision
        ),
        // 4m-f1-legacy-fk-name（4m-F1・PostgreSQL・修正 E）：旧版の DDL で作った DB の切り詰めた外部キー名を、差分の吸収が後から図に足した
        // 2 本目の追加にも与える（規則 R4 が名前の衝突を予言する）。行は実 DB と同じ来歴の行
        .. SyncPlanFuzzKnownFailures.Scenario(
            SyncPlanHistoryRows.F1Finding,
            DialectNames.PostgreSql,
            "修正 E",
            [SyncPlanHistoryRows.F1Id],
            IsUnwarnedNameCollision
        ),
    ];

    /// <summary>一覧</summary>
    public static KnownFailureSet Set { get; } = new("SyncPlan.CapabilityNet", Entries);

    /// <summary>警告なしに、名前の衝突（規則 R4）で断られる文がある</summary>
    private static bool IsUnwarnedNameCollision(FuzzSeedOutcome o) =>
        o.Violations.Count == 1
        && o.Violations[0].StartsWith("static rule:", StringComparison.Ordinal)
        && o.Violations[0].Contains(": R4:", StringComparison.Ordinal)
        && o.Violations[0].EndsWith("(no warning predicts the failure)", StringComparison.Ordinal);

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
        FuzzSeedOutcome outcome
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
        outcome.Unselected.AddRange(
            diff.Items.Where(i => !i.IsSelected).Select(i => $"{i.Kind}: {i.Description}")
        );

        SyncPlan plan;

        try
        {
            plan = new SyncPlanner().BuildPlan(
                diff.Items,
                capabilities,
                new SyncPlanContext
                {
                    LiveEntities = before.Entities,
                    LiveRelationships = before.Relationships,
                }
            );
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
            capabilities
        );
        outcome.Violations.AddRange(verdict.Violations);

        if (verdict.PredictedFailure is not null)
        {
            outcome.Notes.Add(SyncPlanFuzzRunner.StaticRulePredictedNote);
        }

        return outcome;
    }

    /// <summary>
    /// 実 DB では DDL の後に SQL で足す計算列（<see cref="FuzzLiveSchema.ComputedColumnStatements"/>）を、取込の結果と同じく
    /// DB 側の図の列にする（NULL を許す計算列。式は図に載らない）
    /// </summary>
    private static void AddComputedColumns(FuzzLiveSchema live, SyncPlanFuzzDialect dialect)
    {
        foreach (var (key, info) in live.Columns.Where(pair => pair.Value.Computed).ToList())
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
                    IsComputed = true,
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
            }

            foreach (var seed in plan.Seeds)
            {
                var outcome = RunGuarded(
                    dialect,
                    new SyncPlanFuzzGenerator(new Random(seed), dialect),
                    new FuzzSeedOutcome { Seed = seed, Dialect = dialect.Name }
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

        foreach (var failure in scenarioOutcomes.Where(o => o.Violations.Count > 0))
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

        // 必須の形（方言が作れない形を除く）は、網の種の集合全体で全部出る（網は 5 方言とも既定の 5000 種で表明する）
        if (!plan.IsSingle)
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
    private static FuzzSeedOutcome RunGuarded(
        SyncPlanFuzzDialect dialect,
        ISyncPlanFuzzCase generator,
        FuzzSeedOutcome outcome
    )
    {
        try
        {
            return SyncPlanCapabilityNetRunner.Run(dialect, generator, outcome);
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
