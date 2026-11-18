using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.Selection;

internal static partial class SelectionRules
{
    /// <summary>
    /// DB のビュー・トリガー・索引が参照する表の列を消す・足す変更と表の削除のうち、ビューを読めなくするもの・トリガーを発火できなくするもの・
    /// 作り直す表の索引を作れなくするものを計画から外し、<see cref="SyncPlanWarningKind.TableChangeBreaksDependentView"/>・
    /// <see cref="SyncPlanWarningKind.TableChangeBreaksDependentTrigger"/>・<see cref="SyncPlanWarningKind.TableChangeBreaksDependentIndex"/> で表ごとに名指しする（<see cref="SyncPlanContext.LiveDependentCheck"/> が
    /// 無ければ何もしない）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ビューもトリガーも図に無いので、同期は直さない。ビューが読む列・表を消すと（列の並びを書いた <c>SELECT *</c> のビューは列を足しても）
    /// ビューが読めなくなり、トリガーが参照する列・表を消すと（列の並びを書かない <c>INSERT</c> のトリガーは列を足しても）トリガーは発火した
    /// ときに断られる（テーブル再構築はトリガーの本文を検証しないので、同期はそのままコミットする）。どちらも表の列の名前の集合と表の有無の
    /// 変化だけで壊れるので、外すのはその表の列の削除・列の追加・表の削除だけで、型・主キー・制約などの変更は流す。
    /// 索引は、索引が読む列（列の並び・式・部分索引の WHERE）を消す変化だけで作り直せなくなる（作り直しの途中で断られて同期全体が
    /// 巻き戻る）ので、索引だけが原因の表は列の削除だけを外し、列の追加は流す。消した表の索引は表と一緒に消える。
    /// </para>
    /// <para>
    /// 判定は DB 自身に試させる（<see cref="ILiveDependentCheck"/>。SQL を解析しない）。まず計画全体の同期後の列名で試し、壊れるビュー・トリガーが
    /// 無ければ何も外さない（<c>SELECT *</c> のビューの列の削除・列の数が変わらない名前の変更・トリガーが読まない列の削除などに偽の警告を
    /// 出さない）。壊れるときだけ、表ごとに試して原因の表を決め、その表の名前を変える項目を外す。表どうしの組み合わせでだけ壊れるときは、
    /// 名前を変える表をすべて原因とする。外した後の計画は <see cref="SyncPlanner.ExcludeUntilStable"/> の次の周でもう一度試す。
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Withdrawal> ExcludeChangesBreakingLiveDependents(
        SelectionState state,
        SelectionContext context
    )
    {
        var selected = state.Items;

        if (context.PlanContext?.LiveDependentCheck is not { } check || context.LiveSchema is null)
        {
            return [];
        }

        var changes = state.PostSync.ChangedTableColumnNames;

        if (changes.Count == 0 || check.DependentsBrokenBy(ToTableChanges(changes)).IsEmpty)
        {
            return [];
        }

        // 表ごとに試して原因の表を決める（表 → 壊れるビュー・トリガー）
        var culprits = new Dictionary<string, (string Table, LiveDependentBreaks Breaks)>(
            SchemaNames.Comparer
        );

        foreach (var change in changes)
        {
            var breaks = check.DependentsBrokenBy(ToTableChanges([change]));

            if (!breaks.IsEmpty)
            {
                culprits[change.Table] = (change.Table, breaks);
            }
        }

        // 表どうしの組み合わせでだけ壊れる（どの表の変更も単独では壊さない）ときは、名前を変える表をすべて原因とする。
        // この枝を通す形は見つかっておらず、行で通していない守りの無い枝（src/QuickER.Provider.Sqlite/CLAUDE.md「計画の段のビュー・トリガー・索引の判定」）。
        // 作れる形が見つかったら、同期計画の期待の行の実 SQLite の段へ行を足す
        if (culprits.Count == 0)
        {
            var breaks = check.DependentsBrokenBy(ToTableChanges(changes));

            foreach (var change in changes)
            {
                culprits[change.Table] = (change.Table, breaks);
            }
        }

        var withdrawals = new List<Withdrawal>();

        foreach (var item in selected)
        {
            if (
                culprits.TryGetValue(SchemaNames.ToStoredForm(item.TableName), out var culprit)
                && BreaksDependents(item, culprit.Breaks)
            )
            {
                // 1 項目の警告は索引 → ビュー → トリガーの順（同じ値の警告が既に積まれていれば、当てるときに積まない）
                var itemWarnings = new List<SyncPlanWarning>();

                if (culprit.Breaks.Indexes.Count > 0 && item.Kind == SchemaDiffKind.DropColumn)
                {
                    itemWarnings.Add(
                        new SyncPlanWarning(
                            SyncPlanWarningKind.TableChangeBreaksDependentIndex,
                            culprit.Table,
                            string.Join(", ", culprit.Breaks.Indexes)
                        )
                    );
                }

                if (culprit.Breaks.Views.Count > 0)
                {
                    itemWarnings.Add(
                        new SyncPlanWarning(
                            SyncPlanWarningKind.TableChangeBreaksDependentView,
                            culprit.Table,
                            string.Join(", ", culprit.Breaks.Views)
                        )
                    );
                }

                if (culprit.Breaks.Triggers.Count > 0)
                {
                    itemWarnings.Add(
                        new SyncPlanWarning(
                            SyncPlanWarningKind.TableChangeBreaksDependentTrigger,
                            culprit.Table,
                            string.Join(", ", culprit.Breaks.Triggers)
                        )
                    );
                }

                withdrawals.Add(new Withdrawal(item, itemWarnings));
            }
        }

        return withdrawals;

        // ビュー・トリガーは列の名前の集合を変える 3 種類の項目で壊れ、索引は索引が読む列の削除でだけ作り直せなくなる
        // （列の追加・表の削除では壊れないので、索引だけが原因の表の列の追加は流す）
        static bool BreaksDependents(SchemaDiffItem item, LiveDependentBreaks breaks) =>
            (breaks.Views.Count > 0 || breaks.Triggers.Count > 0)
                && SyncPlanner.IsColumnNameSetChange(item.Kind)
            || breaks.Indexes.Count > 0 && item.Kind == SchemaDiffKind.DropColumn;

        static IReadOnlyDictionary<string, IReadOnlyList<string>?> ToTableChanges(
            IEnumerable<(string Table, IReadOnlyList<string>? Columns)> tables
        ) => tables.ToDictionary(t => t.Table, t => t.Columns, SchemaNames.Comparer);
    }
}
