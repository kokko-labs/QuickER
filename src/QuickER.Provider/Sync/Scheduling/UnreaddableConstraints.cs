namespace QuickER.Provider.Sync.Scheduling;

/// <summary>
/// 付け直せない制約の判定。付け直しが必ず断られる制約は、外す理由に依らず外さない
/// </summary>
/// <remarks>
/// <para>
/// 外すと、付け直しが断られたときに制約が消えたまま残る（DDL が暗黙コミットされる方言）。外さずに残せば、その制約に阻まれる文が
/// 何も変えずに断られる。判定は制約 1 つと同期後の事実だけで決まり、どの宣言が外すと答えたかを見ない。
/// </para>
/// <list type="bullet">
/// <item>同期後に両端の列の型が並べられない外部キー（<see cref="SyncPlanner.HasPostSyncTypeMismatch"/>）。</item>
/// <item>参照アクションが SET NULL で、子の列が同期後に NOT NULL になる外部キー（その外部キーを作れない方言＝
/// <see cref="SyncPlanner.BlocksSetNullRebuild"/>）。</item>
/// </list>
/// </remarks>
internal static class UnreaddableConstraints
{
    /// <summary>外部キーの付け直しが必ず断られるか</summary>
    /// <param name="fk">DB の今の図の外部キー</param>
    /// <param name="input">外し直しを決める段の入力</param>
    public static bool IsUnreaddable(ResolvedForeignKeyRelationship fk, SchedulingInput input)
    {
        var childTable = SchemaDiffService.NormalizeTable(fk.Child);

        return SyncPlanner.BlocksSetNullRebuild(
                fk,
                childTable,
                input.Capabilities,
                input.PostSync.AllowsNull
            )
            || SyncPlanner.HasPostSyncTypeMismatch(
                fk,
                childTable,
                SchemaDiffService.NormalizeTable(fk.Parent),
                input.PostSync.TypeDecidingColumns,
                input.Capabilities
            );
    }
}
