using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 同期計画の総当たりの生成器の、制約の形の水準（乱数の列 <c>ConstraintShapeSeed</c>）
/// </summary>
/// <remarks>
/// <para>
/// DB 側の図：2 列の一意制約と、それを参照する 2 列の外部キー（<see cref="AddMultiColumnUnique"/>）、外部キーが参照する名前つきの一意制約の
/// 双子（<see cref="AddReferencedUniqueTwin"/>）。目標の図：2 列の一意制約の追加・双子の一意制約の両方の列の変更・外部キーの親の表の変更・
/// 主キー参照と一意制約参照の入れ替え・外部キーを同じ名前で別の子の表へ移す・外部キーと一意制約の名前だけの変更・一意制約を同じ名前で
/// 別の表へ移す（<see cref="ApplyConstraintShapeLevel"/>）。
/// </para>
/// <para>
/// どの水準も、この列だけから引き、既存の 5 本の列を引かない。水準が図を変えなかった種は、足す前と同じケースのまま残る。データは
/// どの制約も満たすように選ぶ（重なる値・親の無い値を作らない）。足す名前は図の名前と重ならない名前にし（<see cref="UnusedConstraintName"/>）、
/// 既存の行で断られる文として宣言した名前（<see cref="FuzzLiveSchema.DataFailures"/>）の制約は変えない。
/// </para>
/// </remarks>
internal sealed partial class SyncPlanFuzzGenerator
{
    /// <summary>DB 側の図で、外部キーが参照する一意制約に名前つきの双子を置いた表と列（無ければ null）</summary>
    private (string Table, string Column)? _referencedTwin;

    /// <summary>
    /// DB 側の図：2 列の一意制約を張り、半々で後ろの表からそれを参照する 2 列の外部キーを足す
    /// </summary>
    /// <remarks>
    /// 列は主キーでない整数・文字列・固定長の文字列で、行のある表では NULL を含まず、最初の列の値が重ならない（組が重ならない）。同じ列の並びの
    /// 一意制約を 2 つ張れない方言でも、2 列の組はほかの制約と重ならない（主キーの列と、1 列の一意制約は使わない並び）。
    /// </remarks>
    private void AddMultiColumnUnique(FuzzLiveSchema live)
    {
        if (constraintShapeRng is null || constraintShapeRng.Next(10) != 0)
        {
            return;
        }

        var tables = live
            .Diagram.Entities.Where(e => !e.TableName.Contains('.', StringComparison.Ordinal))
            .Select(e => (Entity: e, Pair: PairColumns(live, e, e.Columns)))
            .Where(t => t.Pair is not null)
            .ToList();

        if (tables.Count == 0)
        {
            return;
        }

        var (parent, pair) = tables[constraintShapeRng.Next(tables.Count)];
        var (first, second) = pair!.Value;
        var name =
            constraintShapeRng.Next(2) == 0
                ? UnusedConstraintName(
                    $"UQ_{NamePart(parent.TableName)}_pair",
                    live.Diagram.Entities,
                    live.Diagram.Relationships
                )
                : null;
        parent.UniqueConstraints.Add(
            new UniqueConstraint { Name = name, ColumnIds = [first.Id, second.Id] }
        );
        live.Shapes.Add(FuzzShape.MultiColumnUnique);

        var index = live.Diagram.Entities.IndexOf(parent);
        var children = live
            .Diagram.Entities.Skip(index + 1)
            .Where(e => !e.TableName.Contains('.', StringComparison.Ordinal))
            .ToList();

        if (children.Count == 0 || constraintShapeRng.Next(2) != 0)
        {
            return;
        }

        var child = children[constraintShapeRng.Next(children.Count)];
        var parentRows = live.RowCounts[parent.TableName];
        var mustBeNull = parentRows == 0 && live.RowCounts[child.TableName] > 0;
        var pairs = new List<(Column Parent, Column Child)>();

        foreach (var (column, position) in new[] { (first, 0), (second, 1) })
        {
            var info = live.Find(parent.TableName, column.Name)!;
            var childColumn = AddColumn(
                live,
                child,
                UnusedColumnName(child, $"{BaseName(parent)}_uq_{position}"),
                info.Kind,
                info.Length,
                mustBeNull,
                r => mustBeNull ? null : info.Values[(r - 1) % parentRows]
            );
            childColumn.IsForeignKey = true;
            pairs.Add((column, childColumn));
        }

        live.Diagram.Relationships.Add(
            NewRelationship(
                parent,
                child,
                pairs,
                name: constraintShapeRng.Next(2) == 0
                    ? UnusedConstraintName(
                        $"FK_{NamePart(child.TableName)}_uqpair",
                        live.Diagram.Entities,
                        live.Diagram.Relationships
                    )
                    : null
            )
        );
        live.Shapes.Add(FuzzShape.ForeignKeyOnMultiColumnUnique);
    }

    /// <summary>
    /// DB 側の図：外部キーが参照する名前つきの 1 列の一意制約に、名前つきの双子を半々で足す（同じ列の並びの一意制約を 2 つ持てて、
    /// 名前で見分ける方言だけ。目標の図で両方の列を変えると、付け直すキーが 2 つの組になる）
    /// </summary>
    private void AddReferencedUniqueTwin(FuzzLiveSchema live)
    {
        if (
            constraintShapeRng is null
            || !dialect.AllowDuplicateUniqueConstraints
            || !dialect.DistinguishesSameSetConstraints
        )
        {
            return;
        }

        var candidates = live
            .Diagram.Entities.SelectMany(e =>
                e.UniqueConstraints.Where(u =>
                        u.Name is not null
                        && u.ColumnIds.Count == 1
                        && live.Diagram.Relationships.Any(r =>
                            r.SourceEntityId == e.Id
                            && r.ColumnPairs.Count == 1
                            && r.ColumnPairs[0].SourceColumnId == u.ColumnIds[0]
                        )
                    )
                    .Select(u => (Entity: e, Column: e.Columns.Single(c => c.Id == u.ColumnIds[0])))
            )
            .ToList();

        if (candidates.Count == 0 || constraintShapeRng.Next(2) != 0)
        {
            return;
        }

        var (entity, column) = candidates[constraintShapeRng.Next(candidates.Count)];
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = UnusedConstraintName(
                    $"UQ_{NamePart(entity.TableName)}_{NamePart(column.Name)}_fktwin",
                    live.Diagram.Entities,
                    live.Diagram.Relationships
                ),
                ColumnIds = [column.Id],
            }
        );
        _referencedTwin = (entity.TableName, column.Name);
        live.Shapes.Add(FuzzShape.UniqueTwinOnReferencedKey);
    }

    /// <summary>目標の図へ制約の形の水準を 1 つ加える（双子があれば半々で両方の列を変え、無ければ 40 分の 7 で残りの水準の 1 つ）</summary>
    private void ApplyConstraintShapeLevel(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        if (constraintShapeRng is null)
        {
            return;
        }

        if (
            _referencedTwin is { } twin
            && constraintShapeRng.Next(2) == 0
            && ChangeReferencedUniqueTwins(entities, live, twin)
        )
        {
            return;
        }

        switch (constraintShapeRng.Next(40))
        {
            case 0:
                AddMultiColumnUniqueToTarget(entities, live);
                break;
            case 1:
                RetargetForeignKeyParent(entities, relationships, live);
                break;
            case 2:
                SwapForeignKeyKeyKind(entities, relationships, live);
                break;
            case 3:
                MoveForeignKeyToOtherChild(entities, relationships, live);
                break;
            case 4:
                RenameForeignKey(entities, relationships, live);
                break;
            case 5:
                RenameUnique(entities, relationships, live);
                break;
            case 6:
                MoveUniqueToOtherTable(entities, relationships, live);
                break;
        }
    }

    /// <summary>双子の一意制約の列を両方変える（1 つ目は別の 1 列・2 つ目は元の列とその列の 2 列）</summary>
    private bool ChangeReferencedUniqueTwins(
        List<Entity> entities,
        FuzzLiveSchema live,
        (string Table, string Column) twin
    )
    {
        var entity = entities.FirstOrDefault(e => SchemaNames.AreSame(e.TableName, twin.Table));
        var column = entity?.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, twin.Column));

        if (entity is null || column is null)
        {
            return false;
        }

        var named = entity
            .UniqueConstraints.Where(u =>
                u.Name is not null
                && u.ColumnIds.SequenceEqual([column.Id])
                && !IsDeclared(live, u.Name)
            )
            .ToList();
        var other = entity
            .Columns.Where(c => c.Id != column.Id && IsUniqueCandidate(live, entity, c))
            .ToList();

        if (named.Count < 2 || other.Count == 0)
        {
            return false;
        }

        var w = other[constraintShapeRng!.Next(other.Count)];
        named[0].ColumnIds = [w.Id];
        named[1].ColumnIds = [column.Id, w.Id];
        Mutations.Add($"change unique {entity.TableName} {named[0].Name} -> ({w.Name})");
        Mutations.Add(
            $"change unique {entity.TableName} {named[1].Name} -> ({column.Name}, {w.Name})"
        );
        live.Shapes.Add(FuzzShape.ReaddedUniqueTwins);
        return true;
    }

    /// <summary>目標の図で 2 列の一意制約を足す</summary>
    private void AddMultiColumnUniqueToTarget(List<Entity> entities, FuzzLiveSchema live)
    {
        var tables = entities
            .Where(e => !e.TableName.Contains('.', StringComparison.Ordinal))
            .Select(e => (Entity: e, Pair: PairColumns(live, e, e.Columns)))
            .Where(t =>
                t.Pair is { } pair
                && !t.Entity.UniqueConstraints.Any(u =>
                    u.ColumnIds.Count == 2
                    && u.ColumnIds.Contains(pair.First.Id)
                    && u.ColumnIds.Contains(pair.Second.Id)
                )
            )
            .ToList();

        if (tables.Count == 0)
        {
            return;
        }

        var (entity, pair) = tables[constraintShapeRng!.Next(tables.Count)];
        var named = constraintShapeRng.Next(2) == 0;
        entity.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = named
                    ? UnusedConstraintName(
                        $"UQ_added_pair_{constraintShapeRng.Next(1000)}",
                        entities,
                        []
                    )
                    : null,
                ColumnIds = [pair!.Value.First.Id, pair.Value.Second.Id],
            }
        );
        Mutations.Add(
            $"add unique {entity.TableName}({pair.Value.First.Name}, {pair.Value.Second.Name}){(named ? " named" : "")}"
        );
        live.Shapes.Add(FuzzShape.MultiColumnUniqueAdd);
    }

    /// <summary>外部キーの親の表を、子の値を全部持つ別の整数 1 列の主キーの表へ変える</summary>
    private void RetargetForeignKeyParent(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates = new List<(Relationship Relationship, Entity Child, Entity Other)>();

        foreach (var relationship in relationships)
        {
            if (
                relationship.SourceEntityId == relationship.TargetEntityId
                || relationship.ColumnPairs.Count != 1
                || IsDeclared(live, relationship.ConstraintName)
                || entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId)
                    is not { } parent
                || entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId)
                    is not { } child
                || child.Columns.FirstOrDefault(c =>
                    c.Id == relationship.ColumnPairs[0].TargetColumnId
                )
                    is not { } childColumn
            )
            {
                continue;
            }

            var values = Info(live, child, childColumn)?.Values ?? [];

            foreach (var other in entities.Where(IsSingleIntPrimaryKey))
            {
                var otherKey = other.Columns.Single(c => c.IsPrimaryKey);
                var otherIds = Info(live, other, otherKey)?.Values ?? [];

                if (
                    other != parent
                    && other != child
                    && string.Equals(
                        otherKey.DataType,
                        childColumn.DataType,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && values.All(v => v is null || otherIds.Contains(v))
                    && CanAddForeignKey(
                        relationships,
                        other,
                        childColumn,
                        relationship.OnDelete,
                        relationship.OnUpdate
                    )
                )
                {
                    candidates.Add((relationship, child, other));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var (chosen, chosenChild, target) = candidates[constraintShapeRng!.Next(candidates.Count)];
        chosen.SourceEntityId = target.Id;
        chosen.ColumnPairs =
        [
            new RelationshipColumnPair(
                target.Columns.Single(c => c.IsPrimaryKey).Id,
                chosen.ColumnPairs[0].TargetColumnId
            ),
        ];
        Mutations.Add(
            $"retarget fk {chosen.ConstraintName ?? "unnamed"} {chosenChild.TableName} parent -> {target.TableName}"
        );
        live.Shapes.Add(FuzzShape.ForeignKeyParentRetarget);
    }

    /// <summary>
    /// 外部キーの参照先を、親の主キーと親の 1 列の一意制約の間で入れ替える（子が行 0 か値が全部 NULL のときだけ＝値の都合で断られない）
    /// </summary>
    private void SwapForeignKeyKeyKind(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates = new List<(Relationship Relationship, Entity Parent, Column Target)>();

        foreach (var relationship in relationships)
        {
            if (
                relationship.SourceEntityId == relationship.TargetEntityId
                || relationship.ColumnPairs.Count != 1
                || IsDeclared(live, relationship.ConstraintName)
                || entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId)
                    is not { } parent
                || entities.FirstOrDefault(e => e.Id == relationship.TargetEntityId)
                    is not { } child
                || child.Columns.FirstOrDefault(c =>
                    c.Id == relationship.ColumnPairs[0].TargetColumnId
                )
                    is not { } childColumn
                || Info(live, child, childColumn) is not { } childInfo
                || childInfo.Values.Any(v => v is not null)
            )
            {
                continue;
            }

            var current = relationship.ColumnPairs[0].SourceColumnId;
            var keyColumns = parent
                .UniqueConstraints.Where(u => u.ColumnIds.Count == 1)
                .Select(u => u.ColumnIds[0])
                .Concat(
                    parent.PrimaryKeyColumnIds.Count == 1 ? [parent.PrimaryKeyColumnIds[0]] : []
                )
                .Distinct();

            foreach (var keyColumnId in keyColumns.Where(id => id != current))
            {
                var keyColumn = parent.Columns.FirstOrDefault(c => c.Id == keyColumnId);

                if (
                    keyColumn is not null
                    && !keyColumn.IsComputed
                    && string.Equals(
                        keyColumn.DataType,
                        childColumn.DataType,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !relationships.Any(r =>
                        r != relationship
                        && r.SourceEntityId == parent.Id
                        && r.ColumnPairs.Count == 1
                        && r.ColumnPairs[0].TargetColumnId == childColumn.Id
                        && r.ColumnPairs[0].SourceColumnId == keyColumnId
                    )
                )
                {
                    candidates.Add((relationship, parent, keyColumn));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var (chosen, chosenParent, target) = candidates[constraintShapeRng!.Next(candidates.Count)];
        chosen.ColumnPairs =
        [
            new RelationshipColumnPair(target.Id, chosen.ColumnPairs[0].TargetColumnId),
        ];
        Mutations.Add(
            $"retarget fk {chosen.ConstraintName ?? "unnamed"} {Name(entities, chosen.TargetEntityId)} -> {chosenParent.TableName}.{target.Name}"
        );
        live.Shapes.Add(FuzzShape.ForeignKeyKeyKindSwap);
    }

    /// <summary>
    /// 名前つきの外部キーを同じ名前のまま別の子の表へ移す（親の主キーの値だけを持つ整数の列か、足した NULL 許容の列へ張る）
    /// </summary>
    private void MoveForeignKeyToOtherChild(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates = new List<(Relationship Relationship, Entity Parent, Entity Other)>();

        foreach (var relationship in relationships)
        {
            if (
                relationship.ConstraintName is null
                || relationship.SourceEntityId == relationship.TargetEntityId
                || relationship.ColumnPairs.Count != 1
                || IsDeclared(live, relationship.ConstraintName)
                || entities.FirstOrDefault(e => e.Id == relationship.SourceEntityId)
                    is not { } parent
                || !IsSingleIntPrimaryKey(parent)
            )
            {
                continue;
            }

            candidates.AddRange(
                entities
                    .Where(e =>
                        e.Id != relationship.TargetEntityId
                        && e.Id != parent.Id
                        && !e.TableName.Contains('.', StringComparison.Ordinal)
                    )
                    .Select(e => (relationship, parent, e))
            );
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var (chosen, chosenParent, other) = candidates[constraintShapeRng!.Next(candidates.Count)];
        var parentKey = chosenParent.Columns.Single(c => c.IsPrimaryKey);
        var parentIds = Info(live, chosenParent, parentKey)?.Values ?? [];
        var column = other.Columns.FirstOrDefault(c =>
            !c.IsPrimaryKey
            && !c.IsComputed
            && string.Equals(c.DataType, parentKey.DataType, StringComparison.OrdinalIgnoreCase)
            && Info(live, other, c) is { Kind: FuzzTypeKind.Int, Computed: false } info
            && info.Values.All(v => v is null || parentIds.Contains(v))
            && CanAddForeignKey(relationships, chosenParent, c, chosen.OnDelete, chosen.OnUpdate)
        );

        if (column is null)
        {
            column = new Column
            {
                Name = UnusedColumnName(other, $"{BaseName(chosenParent)}_moved"),
                DataType = parentKey.DataType,
            };
            other.Columns.Add(column);
        }

        chosen.TargetEntityId = other.Id;
        chosen.ColumnPairs = [new RelationshipColumnPair(parentKey.Id, column.Id)];
        Mutations.Add($"retarget fk {chosen.ConstraintName} -> {other.TableName}.{column.Name}");
        live.Shapes.Add(FuzzShape.ForeignKeyMovedToOtherChild);
    }

    /// <summary>外部キーの名前だけを変える（名前の無い外部キーには名前を付ける）</summary>
    private void RenameForeignKey(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates = relationships.Where(r => !IsDeclared(live, r.ConstraintName)).ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var chosen = candidates[constraintShapeRng!.Next(candidates.Count)];
        var renamed = UnusedConstraintName(
            $"FK_renamed_{constraintShapeRng.Next(1000)}",
            entities,
            relationships
        );
        Mutations.Add(
            $"drop fk {Name(entities, chosen.TargetEntityId)} -> {Name(entities, chosen.SourceEntityId)} ({chosen.ConstraintName ?? "unnamed"})"
        );
        chosen.ConstraintName = renamed;
        Mutations.Add(
            $"add fk {Name(entities, chosen.TargetEntityId)} -> {Name(entities, chosen.SourceEntityId)} ({renamed})"
        );
        live.Shapes.Add(FuzzShape.ForeignKeyRename);
    }

    /// <summary>一意制約の名前だけを変える（名前の無い一意制約には名前を付ける）</summary>
    private void RenameUnique(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates = entities
            .SelectMany(e =>
                e.UniqueConstraints.Where(u => !IsDeclared(live, u.Name))
                    .Select(u => (Entity: e, Unique: u))
            )
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var (entity, unique) = candidates[constraintShapeRng!.Next(candidates.Count)];
        var renamed = UnusedConstraintName(
            $"UQ_renamed_{constraintShapeRng.Next(1000)}",
            entities,
            relationships
        );
        Mutations.Add(
            $"change unique {entity.TableName} {unique.Name ?? "(unnamed)"} -> renamed {renamed}"
        );
        unique.Name = renamed;
        live.Shapes.Add(FuzzShape.UniqueRename);
    }

    /// <summary>名前つきの一意制約を同じ名前のまま別の表の列へ移す</summary>
    private void MoveUniqueToOtherTable(
        List<Entity> entities,
        List<Relationship> relationships,
        FuzzLiveSchema live
    )
    {
        var candidates =
            new List<(Entity From, UniqueConstraint Unique, Entity To, Column Column)>();

        foreach (var from in entities)
        {
            foreach (
                var unique in from.UniqueConstraints.Where(u =>
                    u.Name is not null && !IsDeclared(live, u.Name)
                )
            )
            {
                foreach (
                    var to in entities.Where(e =>
                        e != from && !e.TableName.Contains('.', StringComparison.Ordinal)
                    )
                )
                {
                    candidates.AddRange(
                        to.Columns.Where(c =>
                                IsUniqueCandidate(live, to, c)
                                && !to.UniqueConstraints.Any(u => u.ColumnIds.SequenceEqual([c.Id]))
                            )
                            .Select(c => (from, unique, to, c))
                    );
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var (source, moved, destination, column) = candidates[
            constraintShapeRng!.Next(candidates.Count)
        ];
        source.UniqueConstraints.Remove(moved);
        destination.UniqueConstraints.Add(
            new UniqueConstraint { Name = moved.Name, ColumnIds = [column.Id] }
        );
        Mutations.Add($"drop unique {source.TableName} {moved.Name}");
        Mutations.Add($"add unique {destination.TableName}({column.Name}) named");
        live.Shapes.Add(FuzzShape.UniqueMovedToOtherTable);
    }

    /// <summary>
    /// 2 列の一意制約にできる列の組（主キーでない整数・文字列・固定長の文字列で、行のある表では NULL を含まず最初の列の値が重ならない。
    /// 同じ列の並びの一意制約を 2 つ張れない方言では、その組にもう一意制約か主キーがあれば使わない。無ければ null）
    /// </summary>
    private (Column First, Column Second)? PairColumns(
        FuzzLiveSchema live,
        Entity entity,
        IEnumerable<Column> columns
    )
    {
        var rows = RowCount(live, entity);
        var usable = columns
            .Where(c =>
                !c.IsPrimaryKey
                && !c.IsComputed
                && Info(live, entity, c)
                    is {
                        Kind: FuzzTypeKind.Int or FuzzTypeKind.VarChar or FuzzTypeKind.FixedChar,
                        Computed: false,
                    } info
                && (rows == 0 || info.HasNoNull)
            )
            .ToList();
        var first = usable.FirstOrDefault(c => rows == 0 || Info(live, entity, c)!.IsDistinct);

        if (first is null)
        {
            return null;
        }

        var second = usable.FirstOrDefault(c => c != first);

        if (second is null)
        {
            return null;
        }

        var duplicated = entity.UniqueConstraints.Any(u =>
            u.ColumnIds.Count == 2
            && u.ColumnIds.Contains(first.Id)
            && u.ColumnIds.Contains(second.Id)
        );

        return duplicated && !dialect.AllowDuplicateUniqueConstraints ? null : (first, second);
    }

    /// <summary>1 列の一意制約を張れる列か（主キーでない・計算列でない・小数・日時・バイト列でない・行のある表では値が重ならない）</summary>
    private static bool IsUniqueCandidate(FuzzLiveSchema live, Entity entity, Column column) =>
        !column.IsPrimaryKey
        && !column.IsComputed
        && Info(live, entity, column) is { Computed: false } info
        && info.Kind != FuzzTypeKind.Decimal
        && IsUniqueKind(info.Kind)
        && (RowCount(live, entity) == 0 || info.IsDistinct);

    /// <summary>既存の行で断られる文として宣言した名前か（その制約は変えない）</summary>
    private static bool IsDeclared(FuzzLiveSchema live, string? name) =>
        name is not null && live.DataFailures.Any(f => SchemaNames.AreSame(f.Name, name));

    /// <summary>表の列名と重ならない列名（重なれば番号を足す。乱数を引かない＝ほかの種の入力を変えない）</summary>
    private static string UnusedColumnName(Entity entity, string name)
    {
        var candidate = name;

        for (var n = 2; entity.Columns.Any(c => SchemaNames.AreSame(c.Name, candidate)); n++)
        {
            candidate = $"{name}_{n}";
        }

        return candidate;
    }

    /// <summary>図の表名と重ならない表名（重なれば番号を足す。乱数を引かない）</summary>
    private static string UnusedTableName(List<Entity> entities, string name)
    {
        var candidate = name;

        for (var n = 2; entities.Any(e => SchemaNames.AreSame(e.TableName, candidate)); n++)
        {
            candidate = $"{name}_{n}";
        }

        return candidate;
    }
}
