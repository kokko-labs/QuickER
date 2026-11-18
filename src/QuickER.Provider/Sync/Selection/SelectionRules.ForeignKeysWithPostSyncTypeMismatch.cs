using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// 選んだ外部キーの追加・作り直しのうち、同期後に両端の列の型が食い違うものを計画から外し、
    /// <see cref="SyncPlanWarningKind.ForeignKeyColumnTypeMismatch"/> で名指しする（逐次 DDL の方言のみ）。
    /// </summary>
    /// <param name="state">選択済みの項目（外部キーの作り直しは展開前）とその同期後の事実</param>
    /// <param name="context">
    /// 文脈（型の比較に使う方言の宣言と live スキーマ。再構築の方言か live スキーマが無ければ何も外さない）
    /// </param>
    /// <returns>取り下げ（項目の並びの順）</returns>
    /// <remarks>
    /// <para>
    /// 同期後の型は、型を変える列の変更を選んだ列はその新しい型、live に在る列は live の型、live に無い列（追加する表・列）は図の型。
    /// 判定は、構成列のどれかの型がこの同期で決まるときだけ（<see cref="PostSyncSchema.TypeDecidingColumns"/>＝NULL 許容だけの変更は型を
    /// 決めない。live の時点で綴りだけ違う・DB が受け入れている型の違う既存の外部キーの作り直しは止めない）。
    /// 型の比較は暗黙の付け直しと同じ <see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>。
    /// </para>
    /// <para>
    /// 食い違う外部キーの追加は必ず失敗する。作り直しは展開前の 1 項目として外すので、削除だけが走って外部キーが
    /// 消えたまま残ることはない（DDL が暗黙コミットされる方言では、付け直しの失敗で消えたまま残っていた）。
    /// 作り直しを外した外部キーは live に残るので、列の変更で外部キーを外し直す方言では、暗黙の付け直しがこの外部キーを
    /// 食い違う外部キーとして数え、ほかの外部キーも外し直さない（<see cref="SyncPlanner.InjectImplicitForeignKeyRebuilds"/>）。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeForeignKeysWithPostSyncTypeMismatch(
        SelectionState state,
        SelectionContext context
    )
    {
        var selected = state.Items;

        // テーブル再構築の方言は外部キーの両端の型を確かめない
        if (context.IsRebuildDialect)
        {
            return [];
        }

        var capabilities = context.Capabilities;
        var liveSchema = context.LiveSchema;

        if (liveSchema is null)
        {
            return [];
        }

        var liveByName = liveSchema.Tables;
        var typeDecidingColumns = state.PostSync.TypeDecidingColumns;
        var livePairs = liveSchema.ForeignKeyPairs;
        var withdrawals = new List<Withdrawal>();

        foreach (var item in selected)
        {
            if (
                item.Kind is SchemaDiffKind.AddForeignKey or SchemaDiffKind.AlterForeignKey
                && item.ChildEntity is { } child
                && item.ParentEntity is { } parent
            )
            {
                var childTable = SchemaDiffService.NormalizeTable(child);
                var parentTable = SchemaDiffService.NormalizeTable(parent);
                var pairs = SyncScriptBuilderHelper.ResolveColumnPairs(item);

                // 組単位で判定する（DB が受け入れた組＝両端とも型が決まらず live に同じ組の外部キーがある組は数えない＝
                // IsMismatchedUnacceptedPair）。DB にまだ無い組は、両端とも型の決まらない live の列でも数える
                if (
                    pairs.Any(pair =>
                        SyncPlanner.IsMismatchedUnacceptedPair(
                            PostSyncType(child, childTable, pair.ChildColumn),
                            PostSyncType(parent, parentTable, pair.ParentColumn),
                            livePairs.Contains(
                                SyncPlanner.ForeignKeyPairKey(
                                    childTable,
                                    pair.ChildColumn,
                                    parentTable,
                                    pair.ParentColumn
                                )
                            ),
                            capabilities
                        )
                    )
                )
                {
                    var warning = new SyncPlanWarning(
                        SyncPlanWarningKind.ForeignKeyColumnTypeMismatch,
                        childTable,
                        SyncPlanner.SelectedForeignKeyName(item, capabilities.IdentifierLengthLimit)
                    );
                    withdrawals.Add(new Withdrawal(item, [warning]));
                }

                // 構成列の同期後の型と、その型がこの同期で決まるか（列の変更の項目の型・live に無い列は図の型＝決まる。
                // live の列は live の型＝決まらない）
                (string Type, bool Decided) PostSyncType(
                    Entity diagramEntity,
                    string table,
                    string column
                )
                {
                    if (
                        typeDecidingColumns.TryGetValue(
                            SyncPlanner.ColumnKey(table, column),
                            out var decided
                        )
                    )
                    {
                        return (decided, true);
                    }

                    if (
                        liveByName.TryGet(table, out var live)
                        && live.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))
                            is { } liveColumn
                    )
                    {
                        return (liveColumn.DataType, false);
                    }

                    return (SyncPlanner.ColumnTypeOf(diagramEntity, column), true);
                }
            }
        }

        return withdrawals;
    }
}
