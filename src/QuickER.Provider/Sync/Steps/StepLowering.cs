using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 文の並びで表した計画（<see cref="StepPlan"/>）を、セクションと差分の項目で表した計画（<see cref="SyncPlan"/>）へ写す
/// </summary>
/// <remarks>
/// <para>
/// セクションは、並びの隣り合う文を「種類・主キーのフェーズ・前寄せか」が同じ間 1 つにまとめて作る（セクションの中の順は文の順）。
/// 主キーを外す文は主キーの変更の解除のフェーズへ、付ける文は付与のフェーズへ写す。列を動かす文は表ごとの並べ替えの計画へ、
/// 表を作り直す文は再構築の計画へ写す。警告と外した項目はそのまま渡す。
/// </para>
/// <para>
/// 項目の作り方は 2 通り。
/// 読み取り用の形（既定）は、選んだ項目から来て種類の変わらない文に元の項目そのものを返す（列の組を並べ替えた外部キーの追加は、
/// 列の組だけ違う複製）。作り直しを展開した文と計画器が足した文は、文の値から項目を組み立てる。対になった主キーの解除と付与は
/// 同じ 1 つの項目になる。
/// 文の値だけの形は、どの文も文の値だけから項目を組み立てる（元の項目から読むのは説明だけ）。
/// </para>
/// </remarks>
internal static class StepLowering
{
    /// <summary>文の並びの計画を、セクションと差分の項目の計画へ写す</summary>
    /// <param name="plan">文の並びの計画</param>
    /// <param name="fieldsOnly">どの文も文の値だけから項目を組み立てるか（<c>false</c> なら読み取り用の形）</param>
    /// <returns>同じ文を同じ順で持つ計画</returns>
    public static SyncPlan ToSyncPlan(StepPlan plan, bool fieldsOnly = false)
    {
        // 対になった主キーの付与（解除の文から、同じ対の付与の列の並びを引く）
        var keyAdds = new Dictionary<ReaddPair, AddPrimaryKeyStep>();

        // 対になった外部キーの削除（付け直す文から、同じ対の外す文の列の組の並びを引く）
        var foreignKeyDrops = new Dictionary<ReaddPair, DropForeignKeyStep>();

        foreach (var step in plan.Steps)
        {
            if (step is AddPrimaryKeyStep { Pair: { } keyPair } add)
            {
                keyAdds[keyPair] = add;
            }

            if (step is DropForeignKeyStep { Pair: { } foreignKeyPair } drop)
            {
                foreignKeyDrops[foreignKeyPair] = drop;
            }
        }

        // 対ごとに 1 つだけ作る主キーの変更の項目（解除と付与の両方のセクションへ同じ項目を入れる）
        var keyItems = new Dictionary<ReaddPair, SchemaDiffItem>();

        var sections = new List<SyncPlanSection>();
        var rebuilds = new List<TableRebuildPlan>();
        var reorders = new List<ReorderGroup>();
        SectionKey? currentKey = null;
        var currentItems = new List<SchemaDiffItem>();

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];

            if (step is RebuildTableStep rebuild)
            {
                rebuilds.Add(rebuild.Rebuild);
                continue;
            }

            if (step is MoveColumnStep move)
            {
                AddMove(reorders, move);
                continue;
            }

            var (kind, phase) = SectionOf(step);
            var key = new SectionKey(kind, phase, i < plan.MovedFirstCount);

            if (key != currentKey)
            {
                Flush();
                currentKey = key;
            }

            currentItems.Add(ItemOf(step, fieldsOnly, keyAdds, keyItems, foreignKeyDrops));
        }

        Flush();

        return new SyncPlan
        {
            Sections = sections,
            Rebuilds = rebuilds,
            Reorders =
            [
                .. reorders.Select(group => new TableReorderPlan
                {
                    TableName = group.TableName,
                    Moves = group.Moves,
                    SourceItems = group.Source is null ? [] : [group.Source],
                }),
            ],
            Warnings = plan.Warnings,
            ExcludedItems = plan.ExcludedItems,
        };

        void Flush()
        {
            if (currentKey is { } closing && currentItems.Count > 0)
            {
                sections.Add(
                    new SyncPlanSection
                    {
                        Kind = closing.Kind,
                        PrimaryKeyPhase = closing.Phase,
                        Items = [.. currentItems],
                        IsMovedFirst = closing.MovedFirst,
                    }
                );
            }

            currentItems.Clear();
        }
    }

    /// <summary>セクションをまとめる鍵（種類・主キーのフェーズ・前寄せか）</summary>
    private readonly record struct SectionKey(
        SchemaDiffKind Kind,
        PrimaryKeyPhase Phase,
        bool MovedFirst
    );

    /// <summary>1 つの並べ替えの計画になる、隣り合う列を動かす文のまとまり</summary>
    private sealed record ReorderGroup(string TableName, SchemaDiffItem? Source)
    {
        public List<ColumnMove> Moves { get; } = [];
    }

    /// <summary>列を動かす文を、同じ表・同じ元の項目の直前のまとまりへ足す（違えば新しいまとまりを始める）</summary>
    private static void AddMove(List<ReorderGroup> reorders, MoveColumnStep move)
    {
        var source = (move.Origin as SelectedOrigin)?.Item;

        if (
            reorders.Count == 0
            || !SchemaNames.AreSame(reorders[^1].TableName, move.TableName)
            || !ReferenceEquals(reorders[^1].Source, source)
        )
        {
            reorders.Add(new ReorderGroup(move.TableName, source));
        }

        reorders[^1].Moves.Add(new ColumnMove(move.Column, move.AfterColumn));
    }

    /// <summary>文が入るセクションの種類と主キーのフェーズ</summary>
    private static (SchemaDiffKind Kind, PrimaryKeyPhase Phase) SectionOf(SyncStep step) =>
        step switch
        {
            CreateTableStep => (SchemaDiffKind.AddTable, PrimaryKeyPhase.None),
            DropTableStep => (SchemaDiffKind.DropTable, PrimaryKeyPhase.None),
            AddColumnStep => (SchemaDiffKind.AddColumn, PrimaryKeyPhase.None),
            AlterColumnStep => (SchemaDiffKind.AlterColumn, PrimaryKeyPhase.None),
            DropColumnStep => (SchemaDiffKind.DropColumn, PrimaryKeyPhase.None),
            DropPrimaryKeyStep => (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Drop),
            AddPrimaryKeyStep => (SchemaDiffKind.AlterPrimaryKey, PrimaryKeyPhase.Add),
            DropUniqueConstraintStep => (SchemaDiffKind.DropUniqueConstraint, PrimaryKeyPhase.None),
            AddUniqueConstraintStep => (SchemaDiffKind.AddUniqueConstraint, PrimaryKeyPhase.None),
            DropForeignKeyStep => (SchemaDiffKind.DropForeignKey, PrimaryKeyPhase.None),
            AddForeignKeyStep => (SchemaDiffKind.AddForeignKey, PrimaryKeyPhase.None),
            SetTableDescriptionStep => (SchemaDiffKind.SetTableDescription, PrimaryKeyPhase.None),
            SetColumnDescriptionStep => (SchemaDiffKind.SetColumnDescription, PrimaryKeyPhase.None),
            _ => throw new System.InvalidOperationException(
                $"The step {step.GetType().Name} does not belong to a section."
            ),
        };

    /// <summary>文をセクションの項目へ写す</summary>
    private static SchemaDiffItem ItemOf(
        SyncStep step,
        bool fieldsOnly,
        Dictionary<ReaddPair, AddPrimaryKeyStep> keyAdds,
        Dictionary<ReaddPair, SchemaDiffItem> keyItems,
        Dictionary<ReaddPair, DropForeignKeyStep> foreignKeyDrops
    )
    {
        if (fieldsOnly)
        {
            return FromFields(step, keyAdds, keyItems, foreignKeyColumnName: null);
        }

        if (step.Origin is SelectedOrigin selected)
        {
            var item = selected.Item;

            // 列の組を並べ替えた外部キーの追加は、列の組だけ違う複製（ほかのメンバーは元の項目のまま）
            if (step is AddForeignKeyStep add && item.Kind == SchemaDiffKind.AddForeignKey)
            {
                return add.ColumnPairs.SequenceEqual(item.ForeignKeyColumnPairs)
                    ? item
                    : item.WithForeignKeyColumnPairs(add.ColumnPairs);
            }

            // 種類の変わらない文は元の項目そのもの（主キーの解除と付与は、どちらも元の主キーの変更の項目）
            if (SectionOf(step).Kind == item.Kind)
            {
                return item;
            }
        }

        return FromFields(
            step,
            keyAdds,
            keyItems,
            step is AddForeignKeyStep added
                ? DeclaredFirstChildColumn(added, foreignKeyDrops)
                : null
        );
    }

    /// <summary>
    /// 読み取り用の形の外部キーの追加の項目が名乗る列名（列の組を並べ替える前の、宣言の順の先頭の組の子の列）
    /// </summary>
    /// <remarks>
    /// 作り直しを展開した追加は元の作り直しの項目の列名、外して付け直す追加は対の外す文（DB の宣言の順）の先頭の組の子の列。
    /// どちらでもなければ <c>null</c>（文の列の組の先頭を使う）。
    /// </remarks>
    private static string? DeclaredFirstChildColumn(
        AddForeignKeyStep add,
        Dictionary<ReaddPair, DropForeignKeyStep> foreignKeyDrops
    )
    {
        if (add.Origin is SelectedOrigin { Item.ColumnName: { } column })
        {
            return column;
        }

        return
            add.Pair is { } pair
            && foreignKeyDrops.TryGetValue(pair, out var drop)
            && drop.ColumnPairs.Count > 0
            ? drop.ColumnPairs[0].ChildColumn
            : null;
    }

    /// <summary>文の値だけから項目を組み立てる（説明だけは文の出どころから取る）</summary>
    /// <param name="step">文</param>
    /// <param name="keyAdds">対になった主キーの付与</param>
    /// <param name="keyItems">対ごとに 1 つだけ作る主キーの変更の項目</param>
    /// <param name="foreignKeyColumnName">
    /// 外部キーの追加の項目が名乗る列名（<c>null</c> なら文の列の組の先頭の子の列）
    /// </param>
    private static SchemaDiffItem FromFields(
        SyncStep step,
        Dictionary<ReaddPair, AddPrimaryKeyStep> keyAdds,
        Dictionary<ReaddPair, SchemaDiffItem> keyItems,
        string? foreignKeyColumnName
    )
    {
        var description = DescriptionOf(step.Origin);

        switch (step)
        {
            case CreateTableStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = s.TableName,
                    Entity = s.Definition,
                    UniqueConstraintNames = s.UniqueConstraintNames,
                    Description = description,
                };

            case DropTableStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropTable,
                    TableName = s.TableName,
                    Description = description,
                };

            case AddColumnStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddColumn,
                    TableName = s.TableName,
                    ColumnName = s.Column.Name,
                    Column = s.Column,
                    Description = description,
                };

            case AlterColumnStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = s.TableName,
                    ColumnName = s.Column.Name,
                    Column = s.Column,
                    OldColumn = s.OldColumn,
                    Description = description,
                };

            case DropColumnStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropColumn,
                    TableName = s.TableName,
                    ColumnName = s.ColumnName,
                    Description = description,
                };

            case DropPrimaryKeyStep s:
                return PrimaryKeyItem(s, s.TableName, [], description, keyAdds, keyItems);

            case AddPrimaryKeyStep s:
                return PrimaryKeyItem(s, s.TableName, s.Columns, description, keyAdds, keyItems);

            case DropUniqueConstraintStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropUniqueConstraint,
                    TableName = s.TableName,
                    UniqueConstraintName = s.Name,
                    UniqueConstraintColumns = s.Columns,
                    Description = description,
                };

            case AddUniqueConstraintStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddUniqueConstraint,
                    TableName = s.TableName,
                    UniqueConstraintName = s.Name,
                    UniqueConstraintColumns = s.Columns,
                    Description = description,
                };

            case DropForeignKeyStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.DropForeignKey,
                    TableName = SchemaNames.ToStoredForm(s.ChildTable),
                    ParentEntity = new Entity { TableName = s.ParentTable },
                    ChildEntity = new Entity { TableName = s.ChildTable },
                    Relationship = new Relationship
                    {
                        OnDelete = s.OnDelete,
                        OnUpdate = s.OnUpdate,
                    },
                    ForeignKeyName = s.Name,
                    ForeignKeyColumnPairs = s.ColumnPairs,
                    Description = description,
                };

            case AddForeignKeyStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddForeignKey,
                    TableName = SchemaNames.ToStoredForm(s.ChildTable),
                    ColumnName =
                        foreignKeyColumnName
                        ?? (s.ColumnPairs.Count > 0 ? s.ColumnPairs[0].ChildColumn : null),
                    ParentEntity = new Entity { TableName = s.ParentTable },
                    ChildEntity = new Entity { TableName = s.ChildTable },
                    Relationship = new Relationship
                    {
                        OnDelete = s.OnDelete,
                        OnUpdate = s.OnUpdate,
                    },
                    ForeignKeyName = s.Name,
                    ForeignKeyColumnPairs = s.ColumnPairs,
                    Description = description,
                };

            case SetTableDescriptionStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.SetTableDescription,
                    TableName = s.TableName,
                    NewDescription = s.NewDescription,
                    Description = description,
                };

            case SetColumnDescriptionStep s:
                return new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.SetColumnDescription,
                    TableName = s.TableName,
                    ColumnName = s.ColumnName,
                    NewDescription = s.NewDescription,
                    Description = description,
                };

            default:
                throw new System.InvalidOperationException(
                    $"The step {step.GetType().Name} does not become a section item."
                );
        }
    }

    /// <summary>主キーを外す文・付ける文を、主キーの変更の項目へ写す</summary>
    /// <remarks>
    /// 項目は主キーの列だけを持つ表の定義を持つ（同期スクリプトは付与のフェーズで主キーの列名を並びの順に読む）。
    /// 対になった解除と付与は同じ 1 つの項目で、付与の列の並びを持つ。対の無い解除は主キーの列の無い定義を持つ。
    /// </remarks>
    private static SchemaDiffItem PrimaryKeyItem(
        SyncStep step,
        string tableName,
        IReadOnlyList<string> columns,
        string description,
        Dictionary<ReaddPair, AddPrimaryKeyStep> keyAdds,
        Dictionary<ReaddPair, SchemaDiffItem> keyItems
    )
    {
        if (step.Pair is not { } pair)
        {
            return Build(columns);
        }

        if (!keyItems.TryGetValue(pair, out var shared))
        {
            shared = Build(keyAdds.TryGetValue(pair, out var add) ? add.Columns : columns);
            keyItems[pair] = shared;
        }

        return shared;

        SchemaDiffItem Build(IReadOnlyList<string> keyColumns) =>
            new()
            {
                Kind = SchemaDiffKind.AlterPrimaryKey,
                TableName = tableName,
                Entity = new Entity
                {
                    TableName = tableName,
                    Columns =
                    [
                        .. keyColumns.Select(name => new Column
                        {
                            Name = name,
                            IsPrimaryKey = true,
                            IsNullable = false,
                        }),
                    ],
                },
                Description = description,
            };
    }

    /// <summary>読み取り用の形の項目の説明（選んだ項目から来た文は元の項目の説明、計画器が足した文は足した側の説明）</summary>
    private static string DescriptionOf(StepOrigin origin) =>
        origin switch
        {
            SelectedOrigin selected => selected.Item.Description,
            ImplicitOrigin implied => implied.Description,
            _ => string.Empty,
        };
}
