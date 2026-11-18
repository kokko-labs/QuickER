namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 計画が選んだ項目のうち、計画に載せられない項目を外す規則（<see cref="SelectionRule"/>）と、規則を流す順の表
/// </summary>
/// <remarks>
/// <para>
/// 規則は 1 つ 1 ファイル（<c>SelectionRules.{規則}.cs</c>）に置く。どれも、いま選んでいる項目とその同期後の事実
/// （<see cref="SelectionState"/>）と、選んだ項目に依らない文脈（<see cref="SelectionContext"/>）を受け取り、取り下げ
/// （<see cref="Withdrawal"/>）を返すだけで、受け取った並びも警告・外した項目の一覧も書き換えない。同期後の事実は状態から読み、
/// 規則が自分で求め直さない。
/// </para>
/// <para>
/// 規則を流すのは <see cref="SyncPlanner"/> の除外の周だけ（規則をこの表の外から呼ばない）。周は表の順に規則を流し、規則が返した
/// 取り下げを次の規則の前に当てる。
/// </para>
/// </remarks>
internal static partial class SelectionRules
{
    /// <summary>
    /// 除外の周の前に 1 回だけ流す規則の表
    /// </summary>
    /// <remarks>
    /// 並びは仕様（後ろの規則は前の規則が外した後の並びを受ける）。項目そのものだけで決まり、ほかの選んだ項目を材料にしない
    /// 規則だけを置く。
    /// </remarks>
    internal static readonly SelectionRule[] BeforeRounds = [ExcludeTablesWithoutColumns];

    /// <summary>
    /// 除外の周で、何も外れなくなるまで周ごとに流す規則の表
    /// </summary>
    /// <remarks>
    /// 並びは仕様（後ろの規則は前の規則が外した後の並びを受ける。並びを変えると、外す項目・警告・外した項目の並びが変わりうる）。
    /// 方言や入力で掛からない規則も表に置き、掛からないときは空の取り下げを返す（門は規則の中に持つ）。
    /// </remarks>
    internal static readonly SelectionRule[] EachRound =
    [
        ExcludeTablesWithCollidingConstraintNames,
        ExcludeChangesBreakingLiveDependents,
        ExcludeItemsRequiringUnselected,
        ExcludeDropsLeavingNoColumns,
        ExcludeRebuildsLosingImportDiscardedContent,
        ExcludeRebuildsBlockedByExternalDefinition,
        ExcludeForeignKeysWithPostSyncTypeMismatch,
        ExcludeSetNullForeignKeysOnNotNullColumns,
    ];
}
