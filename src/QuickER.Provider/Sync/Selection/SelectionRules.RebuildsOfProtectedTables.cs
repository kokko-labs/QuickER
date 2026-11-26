using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// テーブル再構築の方言で、取込が表の中の物を捨てた表（<see cref="SelectionContext.ImportDiscardedTables"/>）を作り直す選んだ項目を
    /// 計画から外し、<see cref="SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent"/> で表ごとに名指しする
    /// （そのような表が無ければ何も外さない。本体は <see cref="ExcludeRebuildsOfProtectedTables"/>）
    /// </summary>
    private static IReadOnlyList<Withdrawal> ExcludeRebuildsLosingImportDiscardedContent(
        SelectionState state,
        SelectionContext context
    )
    {
        var tables = context.ImportDiscardedTables;

        if (tables.Count == 0)
        {
            return [];
        }

        // 一覧が空でなければ計画の入力があり、live スキーマも必ずある
        return ExcludeRebuildsOfProtectedTables(
            state.Items,
            tables,
            SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent,
            context.LiveSchema!
        );
    }

    /// <summary>
    /// テーブル再構築の方言で、DB の外の物で作り直せない表（<see cref="SelectionContext.RebuildBlockedTables"/>）を作り直す選んだ項目を
    /// 計画から外し、<see cref="SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition"/> で表ごとに名指しする
    /// （そのような表が無ければ何も外さない。本体は <see cref="ExcludeRebuildsOfProtectedTables"/>）
    /// </summary>
    private static IReadOnlyList<Withdrawal> ExcludeRebuildsBlockedByExternalDefinition(
        SelectionState state,
        SelectionContext context
    )
    {
        var tables = context.RebuildBlockedTables;

        if (tables.Count == 0)
        {
            return [];
        }

        // 一覧が空でなければ計画の入力と live スキーマが必ずある
        return ExcludeRebuildsOfProtectedTables(
            state.Items,
            tables,
            SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition,
            context.LiveSchema!
        );
    }

    /// <summary>
    /// テーブル再構築の方言で、守る表（取込が捨てた物がある表・DB の外の物で作り直せない表）を作り直す選んだ項目を計画から外し、
    /// <paramref name="kind"/> の警告で表ごとに名指しする。
    /// </summary>
    /// <param name="selected">依存の除外を済ませた選んだ項目</param>
    /// <param name="protectedTables">
    /// 表 → 名指しする物（<see cref="PlanSelection.CollectImportDiscardedContent"/>・<see cref="PlanSelection.CollectRebuildBlockedTables"/>）
    /// </param>
    /// <param name="kind">
    /// 警告の種類（<see cref="SyncPlanWarningKind.TableRebuildLosesImportDiscardedContent"/>・
    /// <see cref="SyncPlanWarningKind.TableRebuildBlockedByExternalDefinition"/>）
    /// </param>
    /// <param name="liveSchema">live スキーマ</param>
    /// <returns>取り下げ（項目の並びの順）</returns>
    /// <remarks>
    /// <para>
    /// 再構築は live の図から表を作り直すので、取込が捨てた物（大文字小文字・前後の空白だけ違う列名の後着の列・範囲の外や取り込まなかった
    /// 表への外部キーなど）は新しい表に無く、データごと黙って消える。DB の外で登録した照合順序・関数を使う物は、QuickER の接続では作り直せず
    /// 同期全体が巻き戻る。外すのは次の 2 つ（どちらもその表を作り直す）。
    /// </para>
    /// <list type="bullet">
    /// <item>その表の列・主キー・外部キー・一意制約・列順の変更（作り直しを含む）。列の追加だけは作り直さずに足せるので残す。</item>
    /// <item>その表の外部キーの端（親の表・親の列）を消す表・列の削除。消すと、その外部キーを外すためにその表を作り直す。
    /// 端を外部キーに持つ守る表が 2 つ以上なら、その全部を名指しする（守る表ごとに 1 件）。</item>
    /// </list>
    /// <para>
    /// 依存の除外の後・暗黙の削除の注入の前に置く（外した削除を前提に、端の消える外部キーの暗黙の削除を足さない）。その表自身の削除は
    /// 作り直さないので外さない。逐次 DDL の方言は列・制約を ALTER で変えるので掛けない。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeRebuildsOfProtectedTables(
        IReadOnlyList<SchemaDiffItem> selected,
        IReadOnlyDictionary<string, IReadOnlyList<string>> protectedTables,
        SyncPlanWarningKind kind,
        LiveSchema liveSchema
    )
    {
        var droppedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.DropTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);

        // 守る表を子に持つ live の外部キーの端（親の表・親の列）
        var protectedEnds = liveSchema
            .ForeignKeys.Where(fk =>
                protectedTables.ContainsKey(SchemaDiffService.NormalizeTable(fk.Child))
                && !droppedTables.Contains(SchemaDiffService.NormalizeTable(fk.Child))
            )
            .Select(fk =>
                (
                    Child: SchemaDiffService.NormalizeTable(fk.Child),
                    Parent: SchemaDiffService.NormalizeTable(fk.Parent),
                    ParentColumns: ForeignKeyColumnPairResolver
                        .ParentColumns(fk.ColumnPairs)
                        .ToList()
                )
            )
            .ToList();

        var withdrawals = new List<Withdrawal>();

        foreach (var item in selected)
        {
            var table = SchemaNames.ToStoredForm(item.TableName);
            var rebuilt = ProtectedTablesRebuiltBy(item, table);

            if (rebuilt.Count == 0)
            {
                continue;
            }

            withdrawals.Add(
                new Withdrawal(
                    item,
                    [
                        .. rebuilt.Select(protectedTable => new SyncPlanWarning(
                            kind,
                            protectedTable,
                            string.Join(", ", protectedTables[protectedTable])
                        )),
                    ]
                )
            );
        }

        return withdrawals;

        // この項目が作り直させる守る表（取込が捨てた物がある表・DB の外の物で作り直せない表）。表・列の削除は、その端を外部キーに持つ
        // 守る表をすべて返す（live の外部キーの並びの順・重なりなし）。作り直させなければ空
        IReadOnlyList<string> ProtectedTablesRebuiltBy(SchemaDiffItem item, string table)
        {
            if (
                protectedTables.ContainsKey(table)
                && !droppedTables.Contains(table)
                && (
                    TableRebuildDialects.IsTriggerKind(item.Kind)
                    || item.Kind
                        is SchemaDiffKind.AlterForeignKey
                            or SchemaDiffKind.AlterUniqueConstraint
                )
            )
            {
                return [protectedTables.Keys.First(k => SchemaNames.AreSame(k, table))];
            }

            return item.Kind switch
            {
                SchemaDiffKind.DropTable =>
                [
                    .. protectedEnds
                        .Where(end => SchemaNames.AreSame(end.Parent, table))
                        .Select(end => end.Child)
                        .Distinct(SchemaNames.Comparer),
                ],
                SchemaDiffKind.DropColumn when item.ColumnName is not null =>
                [
                    .. protectedEnds
                        .Where(end =>
                            SchemaNames.AreSame(end.Parent, table)
                            && end.ParentColumns.Any(c => SchemaNames.AreSame(c, item.ColumnName))
                        )
                        .Select(end => end.Child)
                        .Distinct(SchemaNames.Comparer),
                ],
                _ => [],
            };
        }
    }
}
