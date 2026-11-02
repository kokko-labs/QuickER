using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 差分と計画を、期待の行（<see cref="SyncPlanExpectation"/>）が照合する決まった書式の行へ写す
/// </summary>
/// <remarks>
/// <para>
/// 項目の鍵は <c>{種類} {表}[.{列}][ fk={外部キー名}][ uq={一意制約名}]</c>。行が手で組んだ項目には並びの番号を
/// <c>#{番号} </c> として頭に付ける（計画が足した項目には付かない＝手で組んだ項目と見分けられる）。
/// </para>
/// <para>
/// 項目の行は鍵の後ろに <c> sel=0|1 able=0|1 destr=0|1</c> と、値のある詳細だけを決まった順で並べる：
/// <c>col=</c>（型・null|notnull・pk・computed・identity）、<c>old=</c>（変更前の列）、<c>pairs=(親>子,…)</c>、
/// <c>parent=</c>・<c>child=</c>、<c>del=</c>・<c>upd=</c>（リレーションの参照アクション）、<c>uqcols=(…)</c>、
/// <c>uqnames=(…)</c>、<c>pk=(…)</c>（主キーの変更の新しい主キー）、<c>cols=(…)</c>（表の追加の列）、
/// <c>replaced-fk=[…]</c>・<c>replaced-uq=[…]</c>、<c>newdesc="…"</c>・<c>olddesc="…"</c>、最後に <c>desc="…"</c>。
/// </para>
/// <para>
/// 再構築・列順の移動へ畳んだ元の項目は、その面の <c>source</c> 行（鍵だけ）に加えて、<see cref="ExpectationFacet.Sources"/> の面に
/// <c>{表} rebuild|reorder &gt; {項目の行}</c> として詳細ごと並べる。
/// </para>
/// </remarks>
internal static class SyncPlanProjection
{
    /// <summary>差分の項目の行</summary>
    /// <param name="items">項目</param>
    /// <param name="labels">手で組んだ項目の番号（無ければ空）</param>
    public static IReadOnlyList<string> Diff(
        IEnumerable<SchemaDiffItem> items,
        IReadOnlyDictionary<SchemaDiffItem, int> labels
    ) => [.. items.Select(item => Line(item, labels))];

    /// <summary>計画の面の行（面の名前ごと）</summary>
    /// <param name="plan">計画</param>
    /// <param name="labels">手で組んだ項目の番号</param>
    /// <param name="liveEntities">DB 側の図（再構築の列が DB の列と同じ Id かを見る）</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Plan(
        SyncPlan plan,
        IReadOnlyDictionary<SchemaDiffItem, int> labels,
        IReadOnlyList<Entity> liveEntities
    ) =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ExpectationFacet.Sections] =
            [
                .. plan.Sections.Select(section =>
                    $"{SectionName(section)}: {string.Join("; ", section.Items.Select(i => Key(i, labels)))}"
                ),
            ],
            [ExpectationFacet.Items] =
            [
                .. plan.Sections.SelectMany(section =>
                    section.Items.Select(item => $"{SectionName(section)} > {Line(item, labels)}")
                ),
            ],
            [ExpectationFacet.Warnings] =
            [
                .. plan.Warnings.Select(w =>
                    w.Detail.Length == 0
                        ? $"{w.Kind}({w.TableName})"
                        : $"{w.Kind}({w.TableName}): {w.Detail}"
                ),
            ],
            [ExpectationFacet.Excluded] = [.. plan.ExcludedItems.Select(i => Key(i, labels))],
            [ExpectationFacet.Rebuilds] =
            [
                .. plan.Rebuilds.SelectMany(rebuild => Rebuild(rebuild, labels, liveEntities)),
            ],
            [ExpectationFacet.Reorders] =
            [
                .. plan.Reorders.SelectMany(reorder =>
                    reorder
                        .Moves.Select(m =>
                            $"{reorder.TableName} move {m.Column.Name}:{ColumnText(m.Column)} after {m.AfterColumn ?? "^"}"
                        )
                        .Concat(
                            reorder.SourceItems.Select(i =>
                                $"{reorder.TableName} source {Key(i, labels)}"
                            )
                        )
                ),
            ],
            [ExpectationFacet.Sources] =
            [
                .. plan.Rebuilds.SelectMany(rebuild =>
                    rebuild.SourceItems.Select(i =>
                        $"{rebuild.TableName} rebuild > {Line(i, labels)}"
                    )
                ),
                .. plan.Reorders.SelectMany(reorder =>
                    reorder.SourceItems.Select(i =>
                        $"{reorder.TableName} reorder > {Line(i, labels)}"
                    )
                ),
            ],
        };

    /// <summary>項目の鍵</summary>
    public static string Key(SchemaDiffItem item, IReadOnlyDictionary<SchemaDiffItem, int> labels)
    {
        var key = item.Kind + " " + item.TableName;

        if (item.ColumnName is { } column)
        {
            key += "." + column;
        }

        if (item.ForeignKeyName is { } fk)
        {
            key += " fk=" + fk;
        }

        if (item.UniqueConstraintName is { } uq)
        {
            key += " uq=" + uq;
        }

        return labels.TryGetValue(item, out var index)
            ? "#" + index.ToString(CultureInfo.InvariantCulture) + " " + key
            : key;
    }

    /// <summary>項目の行（鍵＋選択の状態＋詳細）</summary>
    public static string Line(SchemaDiffItem item, IReadOnlyDictionary<SchemaDiffItem, int> labels)
    {
        var parts = new List<string>
        {
            Key(item, labels),
            $"sel={Bit(item.IsSelected)} able={Bit(item.IsSelectable)} destr={Bit(item.IsDestructive)}",
        };

        if (item.Column is { } column)
        {
            parts.Add("col=" + ColumnText(column));
        }

        if (item.OldColumn is { } old)
        {
            parts.Add("old=" + ColumnText(old));
        }

        AddForeignKey(parts, item);

        if (item.UniqueConstraintColumns.Count > 0)
        {
            parts.Add($"uqcols=({string.Join(",", item.UniqueConstraintColumns)})");
        }

        if (item.UniqueConstraintNames is { } names && item.Entity is { } named)
        {
            parts.Add(
                "uqnames=("
                    + string.Join(
                        ",",
                        named.UniqueConstraints.Select(u =>
                            names.TryGetValue(u, out var name) ? name : "?"
                        )
                    )
                    + ")"
            );
        }

        if (item.Kind == SchemaDiffKind.AlterPrimaryKey && item.Entity is { } keyed)
        {
            parts.Add(
                $"pk=({string.Join(",", keyed.GetPrimaryKeyColumnsInOrder().Select(c => c.Name))})"
            );
        }

        if (item.Kind == SchemaDiffKind.AddTable && item.Entity is { } added)
        {
            parts.Add($"cols=({string.Join(",", added.Columns.Select(c => c.Name))})");
        }

        if (item.ReplacedForeignKey is { } replacedFk)
        {
            var replaced = new List<string> { Key(replacedFk, labels) };
            AddForeignKey(replaced, replacedFk);
            parts.Add($"replaced-fk=[{string.Join(" ", replaced)}]");
        }

        if (item.ReplacedUniqueConstraint is { } replacedUq)
        {
            parts.Add(
                $"replaced-uq=[{Key(replacedUq, labels)} uqcols=({string.Join(",", replacedUq.UniqueConstraintColumns)})]"
            );
        }

        if (item.NewDescription is { } newDescription)
        {
            parts.Add($"newdesc=\"{newDescription}\"");
        }

        if (item.OldDescription is { } oldDescription)
        {
            parts.Add($"olddesc=\"{oldDescription}\"");
        }

        parts.Add($"desc=\"{item.Description}\"");
        return string.Join(" ", parts);
    }

    private static void AddForeignKey(List<string> parts, SchemaDiffItem item)
    {
        if (item.ForeignKeyColumnPairs.Count > 0)
        {
            parts.Add(
                "pairs=("
                    + string.Join(
                        ",",
                        item.ForeignKeyColumnPairs.Select(p => $"{p.ParentColumn}>{p.ChildColumn}")
                    )
                    + ")"
            );
        }

        if (item.ParentEntity is { } parent)
        {
            parts.Add("parent=" + parent.TableName);
        }

        if (item.ChildEntity is { } child)
        {
            parts.Add("child=" + child.TableName);
        }

        if (item.Relationship is { } relationship)
        {
            parts.Add($"del={relationship.OnDelete} upd={relationship.OnUpdate}");
        }
    }

    private static IEnumerable<string> Rebuild(
        TableRebuildPlan rebuild,
        IReadOnlyDictionary<SchemaDiffItem, int> labels,
        IReadOnlyList<Entity> liveEntities
    )
    {
        var t = rebuild.TableName;
        var definition = rebuild.NewDefinition;
        var liveIds = liveEntities
            .Where(e => e.TableName.Equals(t, StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => e.Columns)
            .Select(c => c.Id)
            .ToHashSet();

        yield return $"{t} {(rebuild.CreateOnly ? "create-only" : "rebuild")}";
        yield return $"{t} cols "
            + string.Join(
                ", ",
                definition.Columns.Select(c =>
                    $"{c.Name}:{ColumnText(c)}{(liveIds.Contains(c.Id) ? " live" : "")}"
                )
            );
        yield return $"{t} pk ({string.Join(",", definition.GetPrimaryKeyColumnsInOrder().Select(c => c.Name))})";

        if (definition.PrimaryKeyColumnIds.Count > 0)
        {
            // 保存された主キーの順序の並びそのもの（列は Id で引く・引けなければ ?）
            yield return $"{t} pkids ("
                + string.Join(
                    ",",
                    definition.PrimaryKeyColumnIds.Select(id =>
                        definition.Columns.FirstOrDefault(c => c.Id == id)?.Name ?? "?"
                    )
                )
                + ")";
        }

        foreach (var unique in definition.UniqueConstraints)
        {
            var columns = unique.ColumnIds.Select(id =>
                definition.Columns.FirstOrDefault(c => c.Id == id)?.Name ?? "?"
            );
            yield return $"{t} uq {unique.Name ?? "-"} ({string.Join(",", columns)})";
        }

        foreach (var fk in rebuild.ForeignKeys)
        {
            yield return $"{t} fk {fk.ConstraintName} ({string.Join(",", fk.ChildColumns)}) -> "
                + $"{fk.ParentTable}({string.Join(",", fk.ParentColumns)}) del={fk.OnDelete} upd={fk.OnUpdate}";
        }

        yield return $"{t} copy ({string.Join(",", rebuild.CopyColumns)})";

        foreach (var aux in rebuild.AuxiliaryObjects)
        {
            yield return $"{t} aux {aux.Kind} {aux.Name}";
        }

        foreach (var source in rebuild.SourceItems)
        {
            yield return $"{t} source {Key(source, labels)}";
        }
    }

    private static string SectionName(SyncPlanSection section) =>
        section.PrimaryKeyPhase == PrimaryKeyPhase.None
            ? section.Kind.ToString()
            : $"{section.Kind}/{section.PrimaryKeyPhase}";

    internal static string ColumnText(Column column) =>
        column.DataType
        + (column.IsNullable ? " null" : " notnull")
        + (column.IsPrimaryKey ? " pk" : "")
        + (column.IsComputed ? " computed" : "")
        + (column.IsIdentity ? " identity" : "");

    private static string Bit(bool value) => value ? "1" : "0";
}
