using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// 選んだ項目のうち、選んでいない変更を前提にするものを外し、
    /// <see cref="SyncPlanWarningKind.SelectedItemRequiresUnselectedItem"/> で名指しする。
    /// </summary>
    /// <param name="state">選択済みの項目（外部キーの作り直しは展開前）とその同期後の事実</param>
    /// <param name="context">文脈（型の比較に使う方言の宣言と live スキーマ。live スキーマが無ければ何も外さない）</param>
    /// <returns>取り下げ（項目の並びの順。型の変更も含む列の変更は、NULL 許容化だけを外した複製を代わりの項目に持つ）</returns>
    /// <remarks>
    /// <para>
    /// 前提にする変更は次のとおり。表・列は live に在るか、同じ計画で追加を選んでいれば在るとみなす
    /// （追加を選んだ表の列はすべて在る）。
    /// </para>
    /// <list type="bullet">
    /// <item>外部キーの追加・作り直し: 子表・親表と、両端の構成列。参照先の列が図の主キーとだけ一致し、同期後の主キー
    /// （選んだ主キーの変更か live の主キー）にも一意制約にも一致しないなら、主キーの変更（選んでいない主キーの変更を
    /// 前提にしている。比べるのは列の集合＝並びの違いは同期が参照先のキーの並びへ並べ替える）</item>
    /// <item>主キーの変更: 新しい主キーの列（「新しい列を選ばずに主キー変更だけ選ぶ」と、SQLite の再構築は無い列を
    /// 読み飛ばして部分的な主キーのまま成功していた）</item>
    /// <item>一意制約の追加・作り直し: 構成列（SQLite の再構築は解決できない制約を黙って捨てていた）。主キーと同じ列集合の一意制約を
    /// 持てない方言（<see cref="SyncDialectCapabilities.SupportsUniqueConstraintOnPrimaryKey"/>）では、同期後の主キーと同じ
    /// 列集合なら主キーの変更（差分は図の主キーと同じ列集合を案内項目にするので、ここに来るのは選んでいない主キーの変更で
    /// live の主キーが残る形）</item>
    /// <item>表・列の説明: その表・列</item>
    /// <item>列を NULL 許容にする変更: 同期後もその列が主キーに残るなら、主キーから外す変更（逐次 DDL の方言は
    /// 失敗し、SQLite は黙って NOT NULL のまま）。型の変更も含む項目は、NULL 許容化だけを外した複製で残す</item>
    /// </list>
    /// <para>
    /// 依存先を自動で選ぶことはしない（利用者が選ばなかった変更、特に破壊的な変更を入れることになるため）。
    /// 外した項目はセクションにも再構築にも入らない。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeItemsRequiringUnselected(
        SelectionState state,
        SelectionContext context
    )
    {
        var selected = state.Items;
        var capabilities = context.Capabilities;
        var liveSchema = context.LiveSchema;

        if (liveSchema is null)
        {
            return [];
        }

        var liveByName = liveSchema.Tables;
        var addedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);
        var addedColumns = selected
            .Where(i => i.Kind == SchemaDiffKind.AddColumn && i.ColumnName is not null)
            .Select(i => PlanIdentity.ColumnKey(i.TableName, i.ColumnName!))
            .ToHashSet();
        // この段で外す主キーの変更（構成列が同期後に無い）は、同期後の主キーに数えない（数えると、その変更の後にだけ
        // 主キーから外れる列の削除を残してしまう）
        var primaryKeyChanges = selected
            .Where(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey
                && i.Entity is not null
                && HasAllColumns(
                    SchemaNames.ToStoredForm(i.TableName),
                    i.Entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)
                )
            )
            .GroupBy(i => SchemaNames.ToStoredForm(i.TableName), SchemaNames.Comparer)
            .ToDictionary(
                g => g.Key,
                g => g.First().Entity!.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList(),
                SchemaNames.Comparer
            );
        var addedEntities = state.PostSync.AddedTables;
        // 作り直しを展開した並びの同期後の事実（同期後の一意制約・外す一意制約と外部キー・消える表と列）
        var expanded = state.ExpandedPostSync;
        var postSyncUniques = expanded.UniqueColumnSets;
        // 選んだ削除・作り直しで外す DB の一意制約（照合キー）
        var droppedUniques = expanded.DroppedUniqueConstraints;
        // 同期後も残る DB の外部キー・一意制約（選んだ削除・作り直しで外れず、表・構成列も消えないもの）。名前・定義の重なりの判定に使う
        var remaining = state.RemainingConstraints;

        var withdrawals = new List<Withdrawal>();

        foreach (var item in selected)
        {
            var (subject, missing) = FindMissingDependencies(item);

            if (missing.Count == 0)
            {
                continue;
            }

            var warning = new SyncPlanWarning(
                SyncPlanWarningKind.SelectedItemRequiresUnselectedItem,
                SchemaNames.ToStoredForm(item.TableName),
                $"{subject} ({string.Join(", ", missing)})"
            );
            SchemaDiffItem? replacement = null;

            // 型の変更も含む列の変更は、NULL 許容化だけを外して型の変更は残す
            if (
                item.Kind == SchemaDiffKind.AlterColumn
                && item.Column is not null
                && item.OldColumn is not null
                && !capabilities.AreSameType(item.OldColumn.DataType, item.Column.DataType)
            )
            {
                var column = item.Column.Clone(preserveId: true);
                column.IsNullable = ColumnNullability.AllowsNull(item.OldColumn);
                replacement = new SchemaDiffItem
                {
                    Kind = item.Kind,
                    TableName = item.TableName,
                    ColumnName = item.ColumnName,
                    Entity = item.Entity,
                    Column = column,
                    OldColumn = item.OldColumn,
                    // 説明も型の変更だけにする（元の説明は外した NULL 許容化を含む）
                    Description = SchemaDiffService.DescribeColumnTypeChange(
                        item.TableName,
                        item.ColumnName!,
                        item.OldColumn.DataType,
                        column.DataType
                    ),
                };
            }

            withdrawals.Add(new Withdrawal(item, [warning], replacement));
        }

        return withdrawals;

        (string Subject, List<string> Missing) FindMissingDependencies(SchemaDiffItem item)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);
            var missing = new List<string>();

            switch (item.Kind)
            {
                case SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.AlterForeignKey
                    when item.ChildEntity is not null && item.ParentEntity is not null:
                {
                    var child = SchemaDiffService.NormalizeTable(item.ChildEntity);
                    var parent = SchemaDiffService.NormalizeTable(item.ParentEntity);
                    var pairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);
                    RequireColumns(
                        child,
                        ForeignKeyColumnPairResolver.ChildColumns(pairs),
                        missing
                    );
                    RequireColumns(
                        parent,
                        ForeignKeyColumnPairResolver.ParentColumns(pairs),
                        missing
                    );

                    // 参照先の列が図の主キーとだけ一致し、同期後の主キーにも一意制約にも一致しないなら、
                    // 選んでいない主キーの変更を前提にしている（比べるのは列の集合）
                    if (
                        missing.Count == 0
                        && !addedTables.Contains(parent)
                        && RequiresPrimaryKeyChange(
                            parent,
                            item.ParentEntity,
                            ForeignKeyColumnPairResolver.ParentColumns(pairs).ToList()
                        )
                    )
                    {
                        missing.Add("PRIMARY KEY");
                    }

                    // 名指しの名前はスクリプトが付ける名前（警告の名前の正本を 1 か所にする）
                    var name = PlanIdentity.SelectedForeignKeyName(
                        item,
                        capabilities.IdentifierLengthLimit
                    );

                    // 同期後も残る DB の制約と名前が重なる追加・同じ定義の外部キーを 2 本持てない方言での同じ定義の追加は、その制約の
                    // 削除を前提にする（外さずに足すと、名前の重なり・定義の重なりで断られる）
                    if (missing.Count == 0)
                    {
                        missing.AddRange(
                            remaining.NamedLike(name, child, isForeignKey: true, capabilities)
                        );
                        missing.AddRange(
                            remaining.SameDefinitionAs(child, parent, pairs, capabilities)
                        );
                    }

                    return (name, missing);
                }

                case SchemaDiffKind.AlterPrimaryKey when item.Entity is not null:
                    RequireColumns(
                        table,
                        item.Entity.GetPrimaryKeyColumnsInOrder().Select(c => c.Name),
                        missing
                    );
                    return ("PRIMARY KEY", missing);

                case SchemaDiffKind.AddUniqueConstraint
                or SchemaDiffKind.AlterUniqueConstraint:
                    RequireColumns(table, item.UniqueConstraintColumns, missing);

                    // 主キーと同じ列集合の一意制約を持てない方言では、同期後の主キー（選んでいない主キーの変更なら
                    // live の主キー）と同じ列集合の追加は、主キーの変更を前提にする
                    if (
                        missing.Count == 0
                        && !capabilities.SupportsUniqueConstraintOnPrimaryKey
                        && PlanFacts.IsSameColumnSet(
                            PostSyncPrimaryKey(table),
                            item.UniqueConstraintColumns
                        )
                    )
                    {
                        missing.Add("PRIMARY KEY");
                    }

                    // 同じ列集合の live の一意制約が同期後も残るなら、その一意制約を外す変更（削除・作り直し）を前提にする
                    // （外さずに足すと、同じ列に一意制約が重なる。Oracle は ORA-02261 で断る）
                    if (missing.Count == 0)
                    {
                        missing.AddRange(
                            RemainingLiveUniquesOn(table, item.UniqueConstraintColumns)
                        );
                    }

                    var uniqueName = UniqueConstraintNaming.Resolve(
                        item.UniqueConstraintName,
                        table,
                        item.UniqueConstraintColumns,
                        capabilities.IdentifierLengthLimit
                    );

                    // 同期後も残る DB の制約と、方言の名前の範囲で名前が重なるなら、その制約の削除を前提にする
                    if (missing.Count == 0)
                    {
                        missing.AddRange(
                            remaining.NamedLike(
                                uniqueName,
                                table,
                                isForeignKey: false,
                                capabilities
                            )
                        );
                    }

                    return (uniqueName, missing);

                case SchemaDiffKind.SetTableDescription:
                    RequireColumns(table, [], missing);
                    return ("COMMENT", missing);

                case SchemaDiffKind.SetColumnDescription when item.ColumnName is not null:
                    RequireColumns(table, [item.ColumnName], missing);
                    return (item.ColumnName, missing);

                case SchemaDiffKind.AlterColumn
                    when item.ColumnName is not null
                        && item.Column is not null
                        && ColumnNullability.AllowsNull(item.Column)
                        && item.OldColumn is not null
                        && !ColumnNullability.AllowsNull(item.OldColumn)
                        && StaysInPrimaryKey(table, item.ColumnName):
                    missing.Add("PRIMARY KEY");
                    return (item.ColumnName, missing);

                // 同期後も主キーに入っている列の削除は、その列を外す主キーの変更を前提にする（選ばずに消すと、
                // 方言によって主キーが黙って消える・縮む・削除が断られる）
                case SchemaDiffKind.DropColumn
                    when item.ColumnName is not null && StaysInPrimaryKey(table, item.ColumnName):
                    missing.Add("PRIMARY KEY");
                    return (item.ColumnName, missing);

                default:
                    return (string.Empty, missing);
            }
        }

        // 表と列が同期後にすべて在るか
        bool HasAllColumns(string table, IEnumerable<string> columns)
        {
            var missing = new List<string>();
            RequireColumns(table, columns, missing);
            return missing.Count == 0;
        }

        // 表と列が同期後に在るか（無いものを missing へ「表」「表.列」で足す。自己参照の外部キーは子と親で同じ表を 2 回
        // 調べるので、すでに足したものは重ねない）
        void RequireColumns(string table, IEnumerable<string> columns, List<string> missing)
        {
            if (addedTables.Contains(table))
            {
                return;
            }

            if (!liveByName.TryGet(table, out var live))
            {
                AddOnce(table);
                return;
            }

            foreach (var column in columns)
            {
                if (
                    !addedColumns.Contains(PlanIdentity.ColumnKey(table, column))
                    && !live.Columns.Any(c => SchemaNames.AreSame(c.Name, column))
                )
                {
                    AddOnce($"{table}.{column}");
                }
            }

            void AddOnce(string name)
            {
                if (!missing.Contains(name, StringComparer.Ordinal))
                {
                    missing.Add(name);
                }
            }
        }

        // 列集合が同じ live の一意制約のうち、選んだ削除・作り直しで外れないものの名前（照合は DB の一意制約の照合キー）
        IEnumerable<string> RemainingLiveUniquesOn(string table, IReadOnlyList<string> columns)
        {
            if (!liveByName.TryGet(table, out var live) || columns.Count == 0)
            {
                yield break;
            }

            foreach (var constraint in live.UniqueConstraints)
            {
                if (
                    UniqueConstraintNaming.TryResolveColumnNames(
                        live,
                        constraint,
                        out var liveColumns
                    )
                    && PlanFacts.IsSameColumnSet(liveColumns, columns)
                    && !droppedUniques.Contains(
                        PlanIdentity.UniqueConstraintKey(table, constraint.Name, liveColumns)
                    )
                )
                {
                    yield return UniqueConstraintNaming.Resolve(
                        constraint.Name,
                        table,
                        liveColumns,
                        capabilities.IdentifierLengthLimit
                    );
                }
            }
        }

        // 同期後もこの列が主キーに入っているか（主キーの変更を選んでいればその構成、無ければ live の構成）
        bool StaysInPrimaryKey(string table, string column) =>
            PostSyncPrimaryKey(table).Any(c => SchemaNames.AreSame(c, column));

        // 外部キーの参照先の列が図の主キーとだけ一致し、同期後の主キーにも一意制約にも一致しないか（選んでいない主キーの変更を
        // 前提にしているか。比べるのは列の集合）
        bool RequiresPrimaryKeyChange(
            string parentTable,
            Entity targetParent,
            IReadOnlyList<string> referencedColumns
        )
        {
            var targetKey = targetParent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name).ToList();

            return targetKey.Count > 0
                && string.Equals(
                    UniqueConstraintNaming.ColumnSetSignature(targetKey),
                    UniqueConstraintNaming.ColumnSetSignature(referencedColumns),
                    StringComparison.Ordinal
                )
                && !PlanFacts.StaysCandidateKey(
                    PostSyncPrimaryKey(parentTable),
                    postSyncUniques,
                    parentTable,
                    referencedColumns
                );
        }

        // 同期後の主キーの列名（この段で外さない主キーの変更を選んでいればその構成、追加する表は図の構成、無ければ live の構成）
        IReadOnlyList<string> PostSyncPrimaryKey(string table) =>
            PostSyncSchema.PostSyncPrimaryKeyOf(table, primaryKeyChanges, addedEntities, liveByName)
            ?? [];
    }
}
