using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// セクションと差分の項目で表した計画（<see cref="SyncPlan"/>）を、文の並びの計画（<see cref="StepPlan"/>）として読み取る
/// （関係 N4＝文の往復の入口）
/// </summary>
/// <remarks>
/// <para>
/// 文の並びは、セクションの順 × セクションの中の項目の順に 1 項目 1 文、続けて列順の移動の各移動、最後に再構築の各表。文の種類は
/// 項目の種類でなく、セクションの種類と主キーのフェーズで決める。文の値は項目のメンバーをそのまま写す。
/// </para>
/// <para>
/// 由来は計画器の印（<see cref="SchemaDiffItem.PlanMark"/>）で決める。印の無い項目は選んだ項目（参照で比べる）でなければならない
/// （選んだ項目でも印のある項目でもない項目は、計画器が印を付け忘れた項目として問題にする）。
/// </para>
/// <para>
/// 文で表せない項目（要るメンバーの無い項目・主キーのフェーズの無い主キーの変更のセクション・文の無い種類のセクション）があれば、
/// 計画を返さずに理由だけを返す（関係を掛けずに数える）。
/// </para>
/// </remarks>
internal static class SyncPlanStepReader
{
    /// <summary>読み取りの結果</summary>
    /// <param name="Plan">文の並びの計画（表せない項目があれば null）</param>
    /// <param name="Problems">読み取りで見つけた問題（由来の読めない項目・前寄せの位置など）</param>
    /// <param name="Unrepresentable">文で表せない理由（表せれば null）</param>
    internal sealed record Result(
        StepPlan? Plan,
        IReadOnlyList<string> Problems,
        string? Unrepresentable
    );

    /// <summary>計画を文の並びとして読み取る</summary>
    /// <param name="plan">計画</param>
    /// <param name="selected">計画器へ渡した選んだ項目（参照で比べる）</param>
    /// <returns>読み取りの結果</returns>
    public static Result Read(SyncPlan plan, IEnumerable<SchemaDiffItem> selected)
    {
        var selectedSet = new HashSet<SchemaDiffItem>(selected, ReferenceEqualityComparer.Instance);
        var problems = new List<string>();
        var steps = new List<SyncStep>();

        // 主キーの変更の項目が解除と付与のどちらのセクションにあるか（両方にある項目の文だけが対を持つ）
        var keyDrops = new HashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);
        var keyAdds = new HashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);

        foreach (var section in plan.Sections)
        {
            if (section.Kind != SchemaDiffKind.AlterPrimaryKey)
            {
                continue;
            }

            var target = section.PrimaryKeyPhase switch
            {
                PrimaryKeyPhase.Drop => keyDrops,
                PrimaryKeyPhase.Add => keyAdds,
                _ => null,
            };

            if (target is null)
            {
                return new Result(
                    null,
                    problems,
                    "a primary key section without a phase (PrimaryKeyPhase.None)"
                );
            }

            foreach (var item in section.Items)
            {
                target.Add(item);
            }
        }

        var keyPairs = new Dictionary<SchemaDiffItem, ReaddPair>(
            ReferenceEqualityComparer.Instance
        );
        var movedFirstCount = 0;
        var leadingMovedFirst = true;

        foreach (var section in plan.Sections)
        {
            if (section.IsMovedFirst)
            {
                if (!leadingMovedFirst)
                {
                    problems.Add(
                        $"a moved-first section ({SectionName(section)}) does not lead the plan"
                    );
                }
                else
                {
                    movedFirstCount += section.Items.Count;
                }
            }
            else
            {
                leadingMovedFirst = false;
            }

            foreach (var item in section.Items)
            {
                var origin = OriginOf(item, selectedSet, problems);
                var (step, unrepresentable) = StepOf(section, item, origin);

                if (step is null)
                {
                    return new Result(null, problems, unrepresentable);
                }

                steps.Add(step with { Pair = PairOf(section, item, keyDrops, keyAdds, keyPairs) });
            }
        }

        foreach (var reorder in plan.Reorders)
        {
            if (reorder.SourceItems.Count != 1)
            {
                problems.Add(
                    $"the reorder of {reorder.TableName} has {reorder.SourceItems.Count} source items (expected 1)"
                );
                continue;
            }

            var source = reorder.SourceItems[0];

            if (!selectedSet.Contains(source))
            {
                problems.Add(
                    $"the reorder of {reorder.TableName} comes from an item that was not selected: {Describe(source)}"
                );
            }

            foreach (var move in reorder.Moves)
            {
                steps.Add(
                    new MoveColumnStep(
                        new SelectedOrigin(source),
                        reorder.TableName,
                        move.Column,
                        move.AfterColumn
                    )
                );
            }
        }

        foreach (var rebuild in plan.Rebuilds)
        {
            steps.Add(new RebuildTableStep(new MergedOrigin(rebuild.SourceItems), rebuild));
        }

        return new Result(
            new StepPlan(steps, movedFirstCount, plan.Warnings, plan.ExcludedItems),
            problems,
            null
        );
    }

    /// <summary>項目の由来（読めない項目は問題を積み、項目自身を選んだ項目とみなす）</summary>
    private static StepOrigin OriginOf(
        SchemaDiffItem item,
        HashSet<SchemaDiffItem> selected,
        List<string> problems
    )
    {
        if (item.PlanMark is not { } mark)
        {
            if (selected.Contains(item))
            {
                return new SelectedOrigin(item);
            }

            problems.Add(
                $"an item is neither a selected item nor marked by the planner: {Describe(item)}"
            );
            return new SelectedOrigin(item);
        }

        if (mark.ImplicitReason is { } reason)
        {
            return new ImplicitOrigin(reason, item.Description);
        }

        if (mark.DerivedFrom is { } source)
        {
            if (!selected.Contains(source))
            {
                problems.Add(
                    $"a derived item comes from an item that was not selected: {Describe(item)} <- {Describe(source)}"
                );
            }

            return new SelectedOrigin(source);
        }

        if (mark.Replaces is { } original)
        {
            if (!selected.Contains(original))
            {
                problems.Add(
                    $"a replacement item replaces an item that was not selected: {Describe(item)} <- {Describe(original)}"
                );
            }

            return new SelectedOrigin(item);
        }

        problems.Add($"an item has a mark without an origin: {Describe(item)}");
        return new SelectedOrigin(item);
    }

    /// <summary>セクションの項目を文にする（表せない項目は理由を返す）</summary>
    private static (SyncStep? Step, string? Unrepresentable) StepOf(
        SyncPlanSection section,
        SchemaDiffItem item,
        StepOrigin origin
    )
    {
        var table = item.TableName;

        switch (section.Kind)
        {
            case SchemaDiffKind.AddTable:
                return item.Entity is { } definition
                    ? (
                        new CreateTableStep(origin, table, definition, item.UniqueConstraintNames),
                        null
                    )
                    : (null, Missing(section, "Entity"));

            case SchemaDiffKind.DropTable:
                return (new DropTableStep(origin, table), null);

            case SchemaDiffKind.AddColumn:
                return item.Column is { } added
                    ? (new AddColumnStep(origin, table, added), null)
                    : (null, Missing(section, "Column"));

            case SchemaDiffKind.AlterColumn:
                return item.Column is { } altered
                    ? (new AlterColumnStep(origin, table, altered, item.OldColumn), null)
                    : (null, Missing(section, "Column"));

            case SchemaDiffKind.DropColumn:
                return item.ColumnName is { } dropped
                    ? (new DropColumnStep(origin, table, dropped), null)
                    : (null, Missing(section, "ColumnName"));

            case SchemaDiffKind.AlterPrimaryKey
                when section.PrimaryKeyPhase == PrimaryKeyPhase.Drop:
                return (new DropPrimaryKeyStep(origin, table), null);

            case SchemaDiffKind.AlterPrimaryKey when section.PrimaryKeyPhase == PrimaryKeyPhase.Add:
                return (
                    new AddPrimaryKeyStep(
                        origin,
                        table,
                        [.. item.Entity?.GetPrimaryKeyColumnsInOrder().Select(c => c.Name) ?? []]
                    ),
                    null
                );

            case SchemaDiffKind.DropUniqueConstraint:
                return (
                    new DropUniqueConstraintStep(
                        origin,
                        table,
                        item.UniqueConstraintName,
                        item.UniqueConstraintColumns
                    ),
                    null
                );

            case SchemaDiffKind.AddUniqueConstraint:
                return (
                    new AddUniqueConstraintStep(
                        origin,
                        table,
                        item.UniqueConstraintName,
                        item.UniqueConstraintColumns
                    ),
                    null
                );

            case SchemaDiffKind.DropForeignKey:
            case SchemaDiffKind.AddForeignKey:
                if (item.ChildEntity is not { } child)
                {
                    return (null, Missing(section, "ChildEntity"));
                }

                if (item.ParentEntity is not { } parent)
                {
                    return (null, Missing(section, "ParentEntity"));
                }

                if (item.Relationship is not { } relationship)
                {
                    return (null, Missing(section, "Relationship"));
                }

                return section.Kind == SchemaDiffKind.DropForeignKey
                    ? (
                        new DropForeignKeyStep(
                            origin,
                            child.TableName,
                            parent.TableName,
                            item.ForeignKeyName,
                            item.ForeignKeyColumnPairs,
                            relationship.OnDelete,
                            relationship.OnUpdate
                        ),
                        null
                    )
                    : (
                        new AddForeignKeyStep(
                            origin,
                            child.TableName,
                            parent.TableName,
                            item.ForeignKeyName ?? relationship.ConstraintName,
                            item.ForeignKeyColumnPairs,
                            relationship.OnDelete,
                            relationship.OnUpdate
                        ),
                        null
                    );

            case SchemaDiffKind.SetTableDescription:
                return (new SetTableDescriptionStep(origin, table, item.NewDescription), null);

            case SchemaDiffKind.SetColumnDescription:
                return item.ColumnName is { } described
                    ? (
                        new SetColumnDescriptionStep(origin, table, described, item.NewDescription),
                        null
                    )
                    : (null, Missing(section, "ColumnName"));

            default:
                return (null, $"a section of kind {SectionName(section)} has no step");
        }
    }

    /// <summary>文の対（主キーの変更は解除と付与の両方のセクションにある項目だけ。ほかは印の対）</summary>
    private static ReaddPair? PairOf(
        SyncPlanSection section,
        SchemaDiffItem item,
        HashSet<SchemaDiffItem> keyDrops,
        HashSet<SchemaDiffItem> keyAdds,
        Dictionary<SchemaDiffItem, ReaddPair> keyPairs
    )
    {
        if (section.Kind != SchemaDiffKind.AlterPrimaryKey)
        {
            return item.PlanMark?.Pair;
        }

        if (!keyDrops.Contains(item) || !keyAdds.Contains(item))
        {
            return null;
        }

        if (!keyPairs.TryGetValue(item, out var pair))
        {
            pair = new ReaddPair();
            keyPairs[item] = pair;
        }

        return pair;
    }

    private static string Missing(SyncPlanSection section, string member) =>
        $"{SectionName(section)} item without {member}";

    internal static string SectionName(SyncPlanSection section) =>
        section.PrimaryKeyPhase == PrimaryKeyPhase.None
            ? section.Kind.ToString()
            : $"{section.Kind}/{section.PrimaryKeyPhase}";

    private static string Describe(SchemaDiffItem item) => $"{item.Kind}: {item.Description}";
}
