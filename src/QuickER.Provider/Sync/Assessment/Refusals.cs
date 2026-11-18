using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Assessment;

/// <summary>
/// 計画の文の並びが DB に断られるかの判定 2 つ：必ず断られる文（<see cref="FindCertain"/>）と、既存の行で断られうる文
/// （<see cref="FindRefusable"/>）
/// </summary>
/// <remarks>
/// <para>
/// 文の並びは DB の今の図から歩いた結果（<see cref="PlanWalk"/>）で受け、同期後の事実は計画の事実（<see cref="AssessmentFacts.PostSync"/>）
/// から読む。どちらの判定も文の並び・状態・事実を書き換えない。
/// </para>
/// <para>
/// 制約 1 つと事実で決まる述語は今の計画器と共有する（<see cref="SyncPlanner.CanSupportForeignKey"/>・
/// <see cref="SyncPlanner.HasPostSyncTypeMismatch"/>・<see cref="SyncPlanner.UsesSetNull"/>・<see cref="SyncPlanner.ColumnTypeOf"/>・
/// <see cref="SyncPlanner.HoldsInLiveRows"/>）。
/// </para>
/// </remarks>
internal static class Refusals
{
    /// <summary>
    /// 計画が残す外部キーに阻まれて必ず断られる文のうち、文の並びの順で最初のもの（キーの削除は、同じ外部キーを支えうるキーの削除の組）
    /// </summary>
    /// <param name="walk">文の並びを DB の今の図から歩いた結果</param>
    /// <param name="facts">計画の事実（DB の今の図があること）</param>
    /// <returns>先頭へ移す文と、その判定（必ず断られる文が無ければ <c>null</c>）</returns>
    /// <remarks>
    /// <para>
    /// 残す外部キー：DB の今の外部キーのうち、文の並びのどの外部キーを外す文も外さないもの（外す文が外す外部キーは、その位置の状態から引く
    /// ＝<see cref="ConstraintState.FindDropped"/>）。残す外部キーが無ければ何も移さない。
    /// </para>
    /// <para>必ず断られる文（文の並びの順に見て、最初の 1 件で止める）：</para>
    /// <list type="number">
    /// <item>
    /// キーの削除（一意制約を外す文・主キーを外す文）：残す外部キーのうち、その表を親に持ち、支えうるキー（DB の今の主キーと一意制約の
    /// うち <see cref="SyncPlanner.CanSupportForeignKey"/> が真のもの）にこの文のキーが入り、支えうるキーが文の並びで全部外れるもの。
    /// 移すのは、その外部キーを支えうるキーを外す文の全部（並びの順）を、後ろで付け直すキーの削除が最後に来るように安定に並べたもの。
    /// </item>
    /// <item>
    /// 列の型の変更（この同期で型が決まる列の、列を変える文）：列の変更で外部キーを外し直す方言
    /// （<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/>）は、その列を端に持つ残す外部キーの両端が同期後に
    /// 食い違う。ほかの方言は、その列を含む組の相手の端の DB の今の型と、その列の新しい型が並べられない。
    /// </item>
    /// <item>
    /// 2 が当たらない列を変える文で、SET NULL の子の列を NOT NULL にできない方言
    /// （<see cref="SyncDialectCapabilities.SetNullRequiresNullableChildColumn"/>）の、DB の今は NULL を許し変更後は NOT NULL の列を、
    /// 残す SET NULL の外部キーが子の列に持つ。
    /// </item>
    /// <item>
    /// 上のどれも無く、列の変更で外部キーを外し直さない方言のときだけ：両端の変更の組（どちらも型が決まる列を変える 2 つの文で、新しい型
    /// どうしが並べられず、残す外部キーの組の両端がその 2 列。並びの順で最初の組）。移すのはその 2 文を並びの順。
    /// </item>
    /// </list>
    /// <para>付け直すか：同じ表・同じ名前の一意制約を付ける文（主キーは同じ表の主キーを付ける文）が並びのどこかに在るか。名前の無い削除は付け直さない。</para>
    /// </remarks>
    public static CertainRefusal? FindCertain(PlanWalk walk, AssessmentFacts facts)
    {
        var live = facts.Live!;
        var capabilities = facts.Capabilities;
        var positions = walk.Positions;

        // 残す外部キー（文の並びのどの外す文も外さない DB の今の外部キー）
        var droppedLive = new HashSet<ResolvedForeignKeyRelationship>(
            positions
                .Where(p => p.Step is DropForeignKeyStep)
                .Select(p => p.Before.FindDropped((DropForeignKeyStep)p.Step)?.Live)
                .OfType<ResolvedForeignKeyRelationship>(),
            ReferenceEqualityComparer.Instance
        );
        var retained = live
            .ForeignKeys.Where(fk => !droppedLive.Contains(fk))
            .Select(fk => new RetainedForeignKey(
                fk,
                SchemaDiffService.NormalizeTable(fk.Child),
                SchemaDiffService.NormalizeTable(fk.Parent)
            ))
            .ToList();

        if (retained.Count == 0)
        {
            return null;
        }

        var typeDecidingColumns = facts.PostSync.TypeDecidingColumns;

        // 文の並びで外れるキー（一意制約は識別・主キーは表）
        var droppedUniques = positions
            .Select(p => p.Step)
            .OfType<DropUniqueConstraintStep>()
            .Select(UniqueIdOf)
            .ToHashSet();
        var droppedPrimaryKeys = positions
            .Select(p => p.Step)
            .OfType<DropPrimaryKeyStep>()
            .Select(s => SchemaNames.ToStoredForm(s.TableName))
            .ToHashSet(SchemaNames.Comparer);

        foreach (var position in positions)
        {
            var moved = position.Step switch
            {
                DropUniqueConstraintStep or DropPrimaryKeyStep => KeyDropRefusal(position),
                AlterColumnStep alter => AlterColumnRefusal(position.Index, alter),
                _ => null,
            };

            if (moved is not null)
            {
                return moved;
            }
        }

        return capabilities.AlterColumnRequiresForeignKeyRebuild ? null : PairChangeRefusal();

        // キーの削除が、残す外部キーを支えうるキーを最後まで外す組に入るなら、その組
        CertainRefusal? KeyDropRefusal(WalkPosition position)
        {
            var table = SchemaNames.ToStoredForm(
                position.Step is DropPrimaryKeyStep primary
                    ? primary.TableName
                    : ((DropUniqueConstraintStep)position.Step).TableName
            );
            var owner = live.Tables.TryGet(table, out var found) ? found : null;

            if (owner is null)
            {
                return null;
            }

            var ownerTable = SchemaDiffService.NormalizeTable(owner);
            var keys = SupportingKeyCandidates(owner, ownerTable);
            var droppedKey = position.Step is DropUniqueConstraintStep uniqueDrop
                ? keys.FindIndex(k => !k.Primary && k.UniqueKey == UniqueIdOf(uniqueDrop))
                : keys.FindIndex(k => k.Primary);

            if (droppedKey < 0)
            {
                return null;
            }

            foreach (var fk in retained.Where(fk => SchemaNames.AreSame(fk.Parent, ownerTable)))
            {
                var referenced = ForeignKeyColumnPairResolver
                    .ParentColumns(fk.Relationship.ColumnPairs)
                    .ToList();
                var supporters = Enumerable
                    .Range(0, keys.Count)
                    .Where(i =>
                        SyncPlanner.CanSupportForeignKey(capabilities, keys[i].Columns, referenced)
                    )
                    .ToList();

                if (
                    !supporters.Contains(droppedKey)
                    || supporters.Any(i =>
                        keys[i].Primary
                            ? !droppedPrimaryKeys.Contains(ownerTable)
                            : !droppedUniques.Contains(keys[i].UniqueKey!)
                    )
                )
                {
                    continue;
                }

                // 組：文の並びのうち、支えうるキーを外す文（一意制約を外す文と主キーを外す文）
                var group = positions
                    .Where(p =>
                        p.Step switch
                        {
                            DropPrimaryKeyStep drop => SchemaNames.AreSame(
                                SchemaNames.ToStoredForm(drop.TableName),
                                ownerTable
                            ) && supporters.Any(i => keys[i].Primary),
                            DropUniqueConstraintStep drop => SchemaNames.AreSame(
                                SchemaNames.ToStoredForm(drop.TableName),
                                ownerTable
                            ) && supporters.Any(i => keys[i].UniqueKey == UniqueIdOf(drop)),
                            _ => false,
                        }
                    )
                    .Select(p => p.Index)
                    .ToList();

                // 後ろで付け直すキーの削除を最後へ（安定）
                var ordered = group.OrderBy(i => IsReadded(positions[i].Step) ? 1 : 0).ToList();

                var firstReaddedUnique = ordered.FindIndex(i =>
                    positions[i].Step is DropUniqueConstraintStep && IsReadded(positions[i].Step)
                );

                return new CertainRefusal(
                    ordered,
                    fk.Relationship,
                    ordered.Count(i => IsReadded(positions[i].Step)),
                    firstReaddedUnique < 0 ? null : ordered[firstReaddedUnique]
                );
            }

            return null;
        }

        // 列を変える文が、残す外部キーに阻まれて必ず断られるなら、その 1 文
        CertainRefusal? AlterColumnRefusal(int index, AlterColumnStep alter)
        {
            var table = SchemaNames.ToStoredForm(alter.TableName);
            var liveTable = live.Tables.TryGet(table, out var found) ? found : null;
            var liveColumn = liveTable?.Columns.FirstOrDefault(c =>
                SchemaNames.AreSame(c.Name, alter.Column.Name)
            );

            if (liveColumn is null)
            {
                return null;
            }

            var column = ColumnId.Of(table, alter.Column.Name);

            // 残す外部キーの列の型を変える文（外し直しの方言は外部キー単位で同期後の両端、ほかの方言はその列を含む組の相手の端の今の型）
            if (
                typeDecidingColumns.TryGetValue(column, out var newType)
                && (
                    capabilities.AlterColumnRequiresForeignKeyRebuild
                        ? retained.Any(fk =>
                            Touches(fk, column)
                            && SyncPlanner.HasPostSyncTypeMismatch(
                                fk.Relationship,
                                fk.Child,
                                fk.Parent,
                                typeDecidingColumns,
                                capabilities
                            )
                        )
                        : retained.Any(fk =>
                            OtherEnds(fk, column)
                                .Any(other =>
                                    !capabilities.AreForeignKeyCompatibleTypes(
                                        newType,
                                        other.LiveType
                                    )
                                )
                        )
                )
            )
            {
                return new CertainRefusal([index], null, 0);
            }

            // 残す SET NULL の外部キーの子の列を NOT NULL にする
            return
                capabilities.SetNullRequiresNullableChildColumn
                && ColumnNullability.AllowsNull(liveColumn)
                && !ColumnNullability.AllowsNull(alter.Column)
                && retained.Any(fk =>
                    SyncPlanner.UsesSetNull(fk.Relationship.Relationship, capabilities)
                    && fk.Relationship.ColumnPairs.Any(p =>
                        ColumnId.Of(fk.Child, p.ChildColumn) == column
                    )
                )
                ? new CertainRefusal([index], null, 0)
                : null;
        }

        // 外部キーの組の両端を変える列の変更の組（並びの順で最初のもの）
        CertainRefusal? PairChangeRefusal()
        {
            var alters = positions
                .Where(p => p.Step is AlterColumnStep)
                .Select(p =>
                    (
                        p.Index,
                        Column: ColumnId.Of(
                            SchemaNames.ToStoredForm(((AlterColumnStep)p.Step).TableName),
                            ((AlterColumnStep)p.Step).Column.Name
                        )
                    )
                )
                .ToList();

            for (var first = 0; first < alters.Count; first++)
            {
                if (!typeDecidingColumns.TryGetValue(alters[first].Column, out var firstType))
                {
                    continue;
                }

                for (var second = first + 1; second < alters.Count; second++)
                {
                    if (
                        typeDecidingColumns.TryGetValue(alters[second].Column, out var secondType)
                        && !capabilities.AreForeignKeyCompatibleTypes(firstType, secondType)
                        && retained.Any(fk =>
                            OtherEnds(fk, alters[first].Column)
                                .Any(other => other.Column == alters[second].Column)
                        )
                    )
                    {
                        return new CertainRefusal(
                            [alters[first].Index, alters[second].Index],
                            null,
                            0
                        );
                    }
                }
            }

            return null;
        }

        // このキーの削除を、同じキーを付ける文が並びのどこかで付け直すか
        bool IsReadded(SyncStep drop) =>
            drop switch
            {
                DropPrimaryKeyStep primary => positions.Any(p =>
                    p.Step is AddPrimaryKeyStep add
                    && SchemaNames.AreSame(
                        SchemaNames.ToStoredForm(add.TableName),
                        SchemaNames.ToStoredForm(primary.TableName)
                    )
                ),
                DropUniqueConstraintStep unique => unique.Name is not null
                    && positions.Any(p =>
                        p.Step is AddUniqueConstraintStep add
                        && add.Name is not null
                        && SchemaNames.AreSame(
                            SchemaNames.ToStoredForm(add.TableName),
                            SchemaNames.ToStoredForm(unique.TableName)
                        )
                        && SchemaNames.AreSame(add.Name, unique.Name)
                    ),
                _ => false,
            };
    }

    /// <summary>
    /// 既存の行で断られうる文の位置と文面（列が空でないと通らない変更と、ほかの断られうる文の 2 つに分ける。同じ位置が両方に当たるときは
    /// 列が空でないと通らない変更だけ）
    /// </summary>
    /// <param name="walk">文の並びを DB の今の図から歩いた結果</param>
    /// <param name="facts">計画の事実（DB の今の図があること）</param>
    /// <remarks>
    /// <para>
    /// 列が空でないと通らない変更：型が決まる列の列を変える文で、DB の今の型から新しい型への変更が
    /// <see cref="SyncDialectCapabilities.RequiresEmptyColumn"/>（文面は「表.列: 今の型 → 新しい型」）。
    /// </para>
    /// <para>ほかの断られうる文：</para>
    /// <list type="bullet">
    /// <item>主キーを付ける文・一意制約を付ける文で、DB の今のキーで一意と証明できない（<see cref="SyncPlanner.HoldsInLiveRows"/>）もの。</item>
    /// <item><see cref="SyncDialectCapabilities.AddNotNullColumnRequiresEmptyTable"/> の方言の NOT NULL の列の追加。</item>
    /// <item>
    /// DB の今の列を変える文のうち、NULL を許す列の NOT NULL 化・型を狭める変更（<see cref="SyncDialectCapabilities.NarrowsType"/>）・
    /// <see cref="SyncDialectCapabilities.AlterColumnBlockedByComputedColumn"/> の方言で DB に計算列のある表の普通の列の変更（この順に 1 つ）。
    /// </item>
    /// </list>
    /// </remarks>
    public static RefusableChanges FindRefusable(PlanWalk walk, AssessmentFacts facts)
    {
        var live = facts.Live!;
        var capabilities = facts.Capabilities;
        var typeDecidingColumns = facts.PostSync.TypeDecidingColumns;
        var emptyColumnChanges = new List<RefusableChange>();
        var rowDependentChanges = new List<RefusableChange>();

        foreach (var position in walk.Positions)
        {
            var table = SchemaNames.ToStoredForm(TableOf(position.Step));
            var liveTable = live.Tables.TryGet(table, out var found) ? found : null;
            var empty = EmptyColumnChange(position.Step, table, liveTable);

            if (empty is not null)
            {
                emptyColumnChanges.Add(new RefusableChange(position.Index, empty));
                continue;
            }

            var rowDependent = RowDependentChange(position.Step, table, liveTable);

            if (rowDependent is not null)
            {
                rowDependentChanges.Add(new RefusableChange(position.Index, rowDependent));
            }
        }

        return new RefusableChanges(emptyColumnChanges, rowDependentChanges);

        // 列が空でないと通らない列の変更なら、その文面
        string? EmptyColumnChange(SyncStep step, string table, Entity? liveTable)
        {
            if (step is not AlterColumnStep alter)
            {
                return null;
            }

            var columnName = alter.Column.Name;
            var liveColumn = liveTable?.Columns.FirstOrDefault(c =>
                SchemaNames.AreSame(c.Name, columnName)
            );

            return
                liveColumn is not null
                && typeDecidingColumns.TryGetValue(ColumnId.Of(table, columnName), out var newType)
                && capabilities.RequiresEmptyColumn(liveColumn.DataType, newType)
                ? $"{table}.{columnName}: {liveColumn.DataType} → {newType}"
                : null;
        }

        // 既存の行で断られうる文なら、その文面
        string? RowDependentChange(SyncStep step, string table, Entity? liveTable)
        {
            switch (step)
            {
                case AddPrimaryKeyStep add:
                    return
                        add.Columns.Count > 0
                        && !SyncPlanner.HoldsInLiveRows(
                            liveTable,
                            table,
                            add.Columns,
                            typeDecidingColumns
                        )
                        ? $"PRIMARY KEY {table} ({string.Join(", ", add.Columns)})"
                        : null;
                case AddUniqueConstraintStep add:
                    return
                        add.Columns.Count > 0
                        && !SyncPlanner.HoldsInLiveRows(
                            liveTable,
                            table,
                            add.Columns,
                            typeDecidingColumns
                        )
                        ? $"UNIQUE {table} ({string.Join(", ", add.Columns)})"
                        : null;
                case AddColumnStep add:
                    return
                        capabilities.AddNotNullColumnRequiresEmptyTable
                        && !ColumnNullability.AllowsNull(add.Column)
                        ? $"{table}.{add.Column.Name}: ADD NOT NULL"
                        : null;
                case AlterColumnStep alter:
                    return AlterColumnChange(alter, table, liveTable);
                default:
                    return null;
            }
        }

        // DB の今の列を変える文が既存の行で断られうるなら、その文面
        string? AlterColumnChange(AlterColumnStep alter, string table, Entity? liveTable)
        {
            var columnName = alter.Column.Name;
            var liveColumn = liveTable?.Columns.FirstOrDefault(c =>
                SchemaNames.AreSame(c.Name, columnName)
            );

            if (liveTable is null || liveColumn is null)
            {
                return null;
            }

            var typeDecided = typeDecidingColumns.TryGetValue(
                ColumnId.Of(table, columnName),
                out var newType
            );
            var typeText = typeDecided
                ? $"{table}.{columnName}: {liveColumn.DataType} → {newType}"
                : null;

            if (
                ColumnNullability.AllowsNull(liveColumn)
                && !ColumnNullability.AllowsNull(alter.Column)
            )
            {
                return $"{table}.{columnName}: NOT NULL";
            }

            if (typeDecided && capabilities.NarrowsType(liveColumn.DataType, newType))
            {
                return typeText;
            }

            return
                capabilities.AlterColumnBlockedByComputedColumn
                && !liveColumn.IsComputed
                && liveTable.Columns.Any(c => c.IsComputed)
                ? typeText
                    ?? $"{table}.{columnName}: {(ColumnNullability.AllowsNull(alter.Column) ? "NULL" : "NOT NULL")}"
                : null;
        }
    }

    /// <summary>一意制約を外す文の識別（表・DB の名前・列集合）</summary>
    private static UniqueConstraintId UniqueIdOf(DropUniqueConstraintStep drop) =>
        UniqueConstraintId.Of(SchemaNames.ToStoredForm(drop.TableName), drop.Name, drop.Columns);

    /// <summary>文の表（外部キーの文は子の表。表を持たない文は空文字列）</summary>
    private static string TableOf(SyncStep step) =>
        step switch
        {
            AddPrimaryKeyStep s => s.TableName,
            AddUniqueConstraintStep s => s.TableName,
            AddColumnStep s => s.TableName,
            AlterColumnStep s => s.TableName,
            _ => string.Empty,
        };

    /// <summary>外部キーを支えうる親のキーの候補（DB の今の主キーと一意制約。主キーが先）</summary>
    private static List<SupportingKey> SupportingKeyCandidates(Entity owner, string table)
    {
        var keys = new List<SupportingKey>();
        var primaryKey = owner.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

        if (primaryKey.Count > 0)
        {
            keys.Add(new SupportingKey(true, null, primaryKey));
        }

        foreach (var unique in owner.UniqueConstraints)
        {
            if (UniqueConstraintNaming.TryResolveColumnNames(owner, unique, out var columns))
            {
                keys.Add(
                    new SupportingKey(
                        false,
                        UniqueConstraintId.Of(table, unique.Name, columns),
                        columns
                    )
                );
            }
        }

        return keys;
    }

    /// <summary>外部キーがこの列を端に持つか</summary>
    private static bool Touches(RetainedForeignKey fk, ColumnId column) =>
        fk.Relationship.ColumnPairs.Any(p =>
            ColumnId.Of(fk.Child, p.ChildColumn) == column
            || ColumnId.Of(fk.Parent, p.ParentColumn) == column
        );

    /// <summary>この列を含む組の、相手の端の列と DB の今の型</summary>
    private static IEnumerable<(ColumnId Column, string LiveType)> OtherEnds(
        RetainedForeignKey fk,
        ColumnId column
    )
    {
        foreach (var pair in fk.Relationship.ColumnPairs)
        {
            if (ColumnId.Of(fk.Child, pair.ChildColumn) == column)
            {
                yield return (
                    ColumnId.Of(fk.Parent, pair.ParentColumn),
                    SyncPlanner.ColumnTypeOf(fk.Relationship.Parent, pair.ParentColumn)
                );
            }

            if (ColumnId.Of(fk.Parent, pair.ParentColumn) == column)
            {
                yield return (
                    ColumnId.Of(fk.Child, pair.ChildColumn),
                    SyncPlanner.ColumnTypeOf(fk.Relationship.Child, pair.ChildColumn)
                );
            }
        }
    }

    /// <summary>残す外部キー（保存する形の子の表と親の表を添える）</summary>
    private sealed record RetainedForeignKey(
        ResolvedForeignKeyRelationship Relationship,
        string Child,
        string Parent
    );

    /// <summary>外部キーを支えうる親のキーの候補</summary>
    /// <param name="Primary">主キーか</param>
    /// <param name="UniqueKey">一意制約の識別（主キーは <c>null</c>）</param>
    /// <param name="Columns">構成列</param>
    private sealed record SupportingKey(
        bool Primary,
        UniqueConstraintId? UniqueKey,
        IReadOnlyList<string> Columns
    );
}

/// <summary>必ず断られる文の判定</summary>
/// <param name="Moved">先頭へ移す文の位置（移す順）</param>
/// <param name="BlockingForeignKey">キーの削除の組を阻む残す外部キー（キーの削除でなければ <c>null</c>）</param>
/// <param name="ReaddedDrops">組の中の、後ろで付け直すキーの削除の数（キーの削除でなければ 0）</param>
/// <param name="FirstReaddedUniqueDrop">
/// 組（付け直すキーの削除を最後へ並べた後）の最初の、後ろで付け直す一意制約を外す文の位置（無ければ <c>null</c>）
/// </param>
internal sealed record CertainRefusal(
    IReadOnlyList<int> Moved,
    ResolvedForeignKeyRelationship? BlockingForeignKey,
    int ReaddedDrops,
    int? FirstReaddedUniqueDrop = null
);

/// <summary>既存の行で断られうる文 1 つ</summary>
/// <param name="Index">文の位置</param>
/// <param name="Text">名指しの文面</param>
internal sealed record RefusableChange(int Index, string Text);

/// <summary>既存の行で断られうる文の 2 つの並び（どちらも文の位置の順）</summary>
/// <param name="EmptyColumn">列が空でないと通らない列の変更</param>
/// <param name="RowDependent">ほかの断られうる文（列が空でないと通らない変更に当たる位置は含まない）</param>
internal sealed record RefusableChanges(
    IReadOnlyList<RefusableChange> EmptyColumn,
    IReadOnlyList<RefusableChange> RowDependent
);
