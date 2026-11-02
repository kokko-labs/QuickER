using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>期待の行を流して照合する（DB を使わない）</summary>
internal static class SyncPlanExpectationRunner
{
    /// <summary>行を、行の能力（<paramref name="capabilities"/> を渡せばその能力だけ）で流し、問題の文を返す</summary>
    /// <param name="row">行</param>
    /// <param name="capabilities">流す能力の鍵（null なら行のすべての能力）</param>
    /// <param name="scopeAlias">
    /// 流す能力の鍵 → 期待の能力の鍵の読み替え（合成の能力の行を近い実方言で流してみるときに、合成の能力へ書いた期待をその実方言で照合する）
    /// </param>
    public static IReadOnlyList<string> Run(
        ScenarioRow<SyncPlanExpectationInput> row,
        IEnumerable<string>? capabilities = null,
        IReadOnlyDictionary<string, string>? scopeAlias = null
    )
    {
        var input = row.Input;
        var problems = new List<string>();
        var projections =
            new List<(string Key, IReadOnlyDictionary<string, IReadOnlyList<string>> Facets)>();

        foreach (var key in capabilities ?? input.Capabilities)
        {
            var facets = Project(input, key, problems);
            projections.Add((key, facets));
            var scope =
                scopeAlias is not null && scopeAlias.TryGetValue(key, out var aliased)
                    ? aliased
                    : key;

            if (
                facets.TryGetValue(ExpectationFacet.Exception, out var thrown)
                && !input.Expect.Expectations.Any(e =>
                    e.Facet == ExpectationFacet.Exception
                    && (e.Capability is null || e.Capability == scope)
                )
            )
            {
                problems.Add($"[{key}] unexpected exception: {thrown[0]}");
            }

            foreach (
                var expectation in input.Expect.Expectations.Where(e =>
                    e.Capability is null || e.Capability == scope
                )
            )
            {
                var lines = facets.TryGetValue(expectation.Facet, out var found) ? found : [];

                if (expectation.Check(lines) is { } problem)
                {
                    problems.Add($"[{key}] {problem}");
                }
            }
        }

        if (input.Expect.SamePlanAcrossCapabilities && projections.Count > 1)
        {
            var first = projections[0];

            foreach (var other in projections.Skip(1))
            {
                foreach (
                    var facet in ExpectationFacet.PlanFacets.Append(ExpectationFacet.Exception)
                )
                {
                    var a = first.Facets.TryGetValue(facet, out var x) ? x : [];
                    var b = other.Facets.TryGetValue(facet, out var y) ? y : [];

                    if (!a.SequenceEqual(b, StringComparer.Ordinal))
                    {
                        problems.Add(
                            $"[{first.Key} vs {other.Key}] {facet} differs:\n      {first.Key}:\n        "
                                + string.Join("\n        ", a)
                                + $"\n      {other.Key}:\n        "
                                + string.Join("\n        ", b)
                        );
                    }
                }
            }
        }

        return [.. problems.Select(p => $"{row.Id}: {p}")];
    }

    /// <summary>能力 1 つで流し、面ごとの行を返す（静的な規則の違反は <paramref name="problems"/> へ足す）</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Project(
        SyncPlanExpectationInput input,
        string key,
        List<string> problems
    )
    {
        var facets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var isDialect = SyncPlanExpectationCapabilities.IsDialect(key);
        var world = new ExpectationWorld(key, SyncPlanExpectationCapabilities.Get(key));
        input.Live(world);
        var labels = new Dictionary<SchemaDiffItem, int>(ReferenceEqualityComparer.Instance);
        List<SchemaDiffItem> items;

        try
        {
            if (input.Items is { } handItems)
            {
                items = [.. handItems(world)];

                for (var i = 0; i < items.Count; i++)
                {
                    labels[items[i]] = i;
                }
            }
            else
            {
                input.Target!(world);
                items = new SchemaDiffService()
                    .Compute(
                        world.Live.Entities,
                        world.Live.Relationships,
                        world.Target.Entities,
                        world.Target.Relationships,
                        world.Capabilities
                    )
                    .Items;
                facets[ExpectationFacet.Diff] = SyncPlanProjection.Diff(items, labels);
                input.Select?.Invoke(items);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            facets[ExpectationFacet.Exception] = [Describe(ex)];
            return facets;
        }

        if (!input.Plans && !input.ExpectsPlan && !isDialect)
        {
            return facets;
        }

        SyncPlan plan;
        var context = input.Context(world);

        try
        {
            plan = new SyncPlanner().BuildPlan(items, world.Capabilities, context);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            facets[ExpectationFacet.Exception] = [Describe(ex)];
            return facets;
        }

        foreach (var (facet, lines) in SyncPlanProjection.Plan(plan, labels, world.Live.Entities))
        {
            facets[facet] = lines;
        }

        // 計画が空か（IsEmpty）は、セクション・再構築・列順の移動の 3 面が空であることと一致する（行は 3 面で「空」を書く）
        if (
            plan.IsEmpty
            != (plan.Sections.Count == 0 && plan.Rebuilds.Count == 0 && plan.Reorders.Count == 0)
        )
        {
            problems.Add(
                $"[{key}] plan.IsEmpty is {plan.IsEmpty}, but the sections, rebuilds and reorders say otherwise"
            );
        }

        if (isDialect)
        {
            // 実方言の行は、網と同じく同期スクリプトまで作り、静的な規則で DB が断る文の並びを見る
            try
            {
                SyncPlanExpectationCapabilities.Provider(key).SyncScriptBuilder.Build(plan);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems.Add($"[{key}] script threw: {Describe(ex)}");
            }

            try
            {
                var verdict = SyncPlanStaticRules.Evaluate(
                    key,
                    world.Live.Entities,
                    world.Live.Relationships,
                    [.. items.Where(i => i.IsSelected)],
                    plan,
                    world.Capabilities
                );
                problems.AddRange(verdict.Violations.Select(v => $"[{key}] static rules: {v}"));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 静的な規則は取り込んだ図と差分の項目を前提にする（手で組んだスタブの項目では読めないことがある）
                problems.Add($"[{key}] static rules threw: {Describe(ex)}");
            }
        }

        return facets;
    }

    private static string Describe(Exception ex) =>
        $"{ex.GetType().Name}: {ex.Message.Split('\n')[0].TrimEnd('\r')}";
}
