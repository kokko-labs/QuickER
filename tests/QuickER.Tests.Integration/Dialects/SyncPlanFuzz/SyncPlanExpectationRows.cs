using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>期待の行の入力（流す能力・図・項目・選び方・計画の文脈・期待）</summary>
/// <param name="Capabilities">流す能力の鍵（<see cref="SyncPlanExpectationCapabilities"/>）</param>
/// <param name="Live">DB 側の図を組む</param>
/// <param name="Target">目標の図を組む（差分の行。null なら項目を手で組む行）</param>
/// <param name="Items">差分を通さずに手で組む項目（null なら差分の行）</param>
/// <param name="Select">差分の項目の選び方を変える（null なら差分の既定の選択のまま）</param>
/// <param name="Context">計画の文脈（null を返せば文脈なしで計画を作る）</param>
/// <param name="Expect">期待</param>
/// <param name="Plans">計画を作るか（偽なら差分の期待だけ。実方言では静的な規則のため常に作る）</param>
internal sealed record SyncPlanExpectationInput(
    IReadOnlyList<string> Capabilities,
    Action<ExpectationWorld> Live,
    Action<ExpectationWorld>? Target,
    Func<ExpectationWorld, IEnumerable<SchemaDiffItem>>? Items,
    Action<IReadOnlyList<SchemaDiffItem>>? Select,
    Func<ExpectationWorld, SyncPlanContext?> Context,
    ExpectationBuilder Expect,
    bool Plans
)
{
    /// <summary>計画の面を照合する期待があるか</summary>
    public bool ExpectsPlan =>
        Expect.SamePlanAcrossCapabilities
        || Expect.Expectations.Any(e => ExpectationFacet.PlanFacets.Contains(e.Facet));
}

/// <summary>
/// 同期計画の期待の行：差分と計画の中身（項目・セクション・警告・外した項目・再構築・列順の移動）を、決まった書式の行
/// （<see cref="SyncPlanProjection"/>）への期待として書いた表
/// </summary>
/// <remarks>
/// <para>
/// 実方言の行は能力の組の網の派生（<see cref="SyncPlanCapabilityNetTestsBase.ExpectationRows_Match"/>）が自分の方言の行として
/// 流し、期待に加えて静的な規則（<see cref="SyncPlanStaticRules"/>）も当てる（予言した失敗に予告の警告が無ければ違反）。
/// 合成の能力の行と、複数の能力の計画を比べる行は <see cref="SyncPlanExpectationTests"/> が流す。実 DB の総当たりでは流さない
/// （<see cref="SyncPlanScenarios"/> と別の表）・網羅の計数に数えない（決定的な行と同じ扱い）。
/// </para>
/// <para>
/// 期待は書いたものだけを照合する。期待が 1 つも無い行・計画の期待が無いのに計画を作る指定の行・知らない能力の鍵は、表の構築で断る。
/// 行の主題ごとに <c>SyncPlanExpectationRows.{主題}.cs</c> へ分け、図は <see cref="ExpectationSchema"/> だけで組む。
/// </para>
/// </remarks>
internal static partial class SyncPlanExpectationRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<SyncPlanExpectationInput> Table { get; } = Build();

    /// <summary>実方言の行のうち、その方言の網の派生が流す行（複数の能力を比べる行は除く）</summary>
    public static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> ForDialect(string dialect) =>
        Table.Where(row =>
            !row.Input.Expect.SamePlanAcrossCapabilities
            && row.Input.Capabilities.Contains(dialect, StringComparer.Ordinal)
        );

    /// <summary><see cref="SyncPlanExpectationTests"/> が流す行（合成の能力を含む行と、能力を比べる行）</summary>
    public static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> ForSyntheticOrComparison() =>
        Table.Where(row =>
            row.Input.Expect.SamePlanAcrossCapabilities
            || row.Input.Capabilities.Any(c => !SyncPlanExpectationCapabilities.IsDialect(c))
        );

    /// <summary><see cref="SyncPlanExpectationTests"/> の行の数の下限の鍵</summary>
    public const string SyntheticOrComparisonKey = "synthetic-or-comparison";

    /// <summary>
    /// 照合する行の数の下限（黙った空振りの守り＝行を消したらここも下げる）。鍵は方言名か <see cref="SyntheticOrComparisonKey"/>
    /// </summary>
    public static int MinimumRowsFor(string key) => MinimumRows[key];

    private static readonly System.Collections.Generic.Dictionary<string, int> MinimumRows = new(
        StringComparer.Ordinal
    )
    {
        [QuickER.Dialects.DialectNames.SqlServer] = 94,
        [QuickER.Dialects.DialectNames.PostgreSql] = 11,
        [QuickER.Dialects.DialectNames.MySql] = 33,
        [QuickER.Dialects.DialectNames.Oracle] = 15,
        [QuickER.Dialects.DialectNames.Sqlite] = 49,
        [SyntheticOrComparisonKey] = 19,
    };

    private static ScenarioTable<SyncPlanExpectationInput> Build()
    {
        var rows = AllRows().ToList();
        var known = SyncPlanExpectationCapabilities.AllKeys.ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var row in rows)
        {
            var input = row.Input;

            if (input.Expect.Expectations.Count == 0 && !input.Expect.SamePlanAcrossCapabilities)
            {
                problems.Add($"{row.Id}: 期待が 1 つも無い");
            }

            if (input.Capabilities.Count == 0)
            {
                problems.Add($"{row.Id}: 能力が無い");
            }

            foreach (var key in input.Capabilities.Where(k => !known.Contains(k)))
            {
                problems.Add($"{row.Id}: 知らない能力の鍵 {key}");
            }

            foreach (
                var scoped in input
                    .Expect.Expectations.Where(e => e.Capability is not null)
                    .Select(e => e.Capability!)
                    .Where(k => !input.Capabilities.Contains(k, StringComparer.Ordinal))
                    .Distinct()
            )
            {
                problems.Add($"{row.Id}: 行が流さない能力 {scoped} への期待");
            }

            if (input.Expect.SamePlanAcrossCapabilities && input.Capabilities.Count < 2)
            {
                problems.Add($"{row.Id}: 計画を比べる行に能力が 2 つ以上無い");
            }

            if ((input.Target is null) == (input.Items is null))
            {
                problems.Add($"{row.Id}: 目標の図か手で組む項目のちょうど一方が要る");
            }

            if (
                input.Items is not null
                && input.Expect.Expectations.Any(e => e.Facet == ExpectationFacet.Diff)
            )
            {
                problems.Add($"{row.Id}: 手で組む項目の行に差分の期待がある");
            }
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                "同期計画の期待の行が整っていない:\n  " + string.Join("\n  ", problems)
            );
        }

        return new ScenarioTable<SyncPlanExpectationInput>(EngineIds.SyncPlan, rows);
    }

    /// <summary>主題ごとの行を並べる（主題のファイルが足す）</summary>
    private static IEnumerable<ScenarioRow<SyncPlanExpectationInput>> AllRows() =>
        [
            .. DiffRows(),
            .. DiffTypeRows(),
            .. DiffPrimaryKeyRows(),
            .. DiffForeignKeyRows(),
            .. DiffUniqueConstraintRows(),
            .. DiffColumnOrderRows(),
            .. PlanSectionRows(),
            .. PlanRebuildRows(),
            .. PlanRebuildColumnRows(),
            .. PlanReorderRows(),
            .. PlanPrimaryKeyRows(),
            .. PlanUniqueConstraintRows(),
        ];

    /// <summary>行を書き始める</summary>
    /// <param name="id">行の Id（守っていることが読める名前）</param>
    /// <param name="description">何を確かめる行か</param>
    /// <param name="capabilities">流す能力の鍵</param>
    private static ExpectationRow Row(
        string id,
        string description,
        params string[] capabilities
    ) => new(id, description, capabilities);

    /// <summary>実方言 5 つ</summary>
    private static string[] AllDialects => [.. SyncPlanExpectationCapabilities.AllDialects];
}

/// <summary>期待の行を書く（最後に表の行へ暗黙に変わる）</summary>
internal sealed class ExpectationRow(string id, string description, string[] capabilities)
{
    private Action<ExpectationWorld> _live = _ => { };
    private Action<ExpectationWorld>? _target;
    private Func<ExpectationWorld, IEnumerable<SchemaDiffItem>>? _items;
    private Action<IReadOnlyList<SchemaDiffItem>>? _select;
    private Func<ExpectationWorld, SyncPlanContext?> _context = w => w.LiveContext();
    private readonly ExpectationBuilder _expect = new();
    private bool _plans;

    /// <summary>DB 側の図を組む</summary>
    public ExpectationRow Live(Action<ExpectationSchema> build)
    {
        _live = world => build(world.Live);
        return this;
    }

    /// <summary>DB 側の図を組む（能力を見る）</summary>
    public ExpectationRow LiveWith(Action<ExpectationWorld> build)
    {
        _live = build;
        return this;
    }

    /// <summary>目標の図を組む（DB 側の図と別に組む）</summary>
    public ExpectationRow Target(Action<ExpectationSchema> build)
    {
        _target = world => build(world.Target);
        return this;
    }

    /// <summary>目標の図を、DB 側の図の複製（新しい Id）へ編集を加えて組む</summary>
    public ExpectationRow TargetFromLive(Action<ExpectationSchema> edit)
    {
        _target = world =>
        {
            world.Target.CopyFrom(world.Live, preserveIds: false);
            edit(world.Target);
        };
        return this;
    }

    /// <summary>目標の図を組む（両方の図を見る）</summary>
    public ExpectationRow TargetWith(Action<ExpectationWorld> build)
    {
        _target = build;
        return this;
    }

    /// <summary>差分を通さずに項目を手で組む（項目の行の頭に並びの番号 <c>#n</c> が付く）</summary>
    public ExpectationRow Items(Func<ExpectationWorld, IEnumerable<SchemaDiffItem>> items)
    {
        _items = items;
        return this;
    }

    /// <summary>差分の項目の選び方を変える</summary>
    public ExpectationRow Select(Action<IReadOnlyList<SchemaDiffItem>> select)
    {
        _select = select;
        return this;
    }

    /// <summary>計画の文脈を変える</summary>
    public ExpectationRow Context(Func<ExpectationWorld, SyncPlanContext?> context)
    {
        _context = context;
        return this;
    }

    /// <summary>文脈なしで計画を作る</summary>
    public ExpectationRow NoContext() => Context(_ => null);

    /// <summary>計画まで作る（計画の期待が無くても、例外が出ないことを確かめる）</summary>
    public ExpectationRow Plan()
    {
        _plans = true;
        return this;
    }

    /// <summary>期待を書く</summary>
    public ExpectationRow Expect(Action<ExpectationBuilder> expect)
    {
        expect(_expect);
        return this;
    }

    /// <summary>表の行にする</summary>
    public static implicit operator ScenarioRow<SyncPlanExpectationInput>(ExpectationRow row) =>
        new(
            row.Id,
            null,
            row.Description,
            new SyncPlanExpectationInput(
                row.CapabilityKeys,
                row._live,
                row._target,
                row._items,
                row._select,
                row._context,
                row._expect,
                row._plans
            )
        );

    private string Id { get; } = id;

    private string Description { get; } = description;

    private IReadOnlyList<string> CapabilityKeys { get; } = capabilities;
}
