using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Provider.Sync.State;

/// <summary>
/// 同期の計画の、制約 1 つと状態で決まる事実の述語（選択・組み立て・評価が共有する）
/// </summary>
/// <remarks>
/// 集める・足す・並べるロジックは持たない（それは選択の規則・組み立て・評価の規則の側に書く）。同期後の事実そのものは
/// <see cref="PostSyncSchema"/>、識別の入口と外部キーの名前は <see cref="PlanIdentity"/> が持つ。
/// </remarks>
internal static class PlanFacts
{
    /// <summary>表の列の名前の集合か表の有無を変える項目の種別（列の削除・列の追加・表の削除）か</summary>
    internal static bool IsColumnNameSetChange(SchemaDiffKind kind) =>
        kind is SchemaDiffKind.DropColumn or SchemaDiffKind.AddColumn or SchemaDiffKind.DropTable;

    /// <summary>
    /// 列集合が live のキー（主キー・一意制約）の列集合を含み、その列がどれも live にあって型を変えないか（含む列集合の値の組は、
    /// 含まれる側が一意なので一意）
    /// </summary>
    /// <param name="liveTable">live の表（無ければ証明できない）</param>
    /// <param name="table">保存する形の表名</param>
    /// <param name="columns">新しい主キー・一意制約の構成列</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="PostSyncSchema.TypeDecidingColumns"/>）</param>
    /// <param name="excludeAllNullableUniques">
    /// 構成列が全部 NULL 許容の live の一意制約を証拠に使わないか（一意制約の追加で、方言が一部が NULL の行を比べる＝
    /// <see cref="SyncDialectCapabilities.UniqueConstraintComparesPartiallyNullKeys"/> のとき。主キーの付与は新しい主キーの列を先に NOT NULL に
    /// するので偽）
    /// </param>
    /// <remarks>
    /// 一部が NULL の行を比べる方言（Oracle）は、全部が NULL の行だけを検査の外に置く。構成列が全部 NULL 許容の一意制約の下には、
    /// その列が NULL で残りの列が同じ行が何行でもありうるので、上位集合の一意制約は既存の行で断られうる（ORA-02299）。一部の列が
    /// NOT NULL の一意制約の下には一部が NULL の重複が入らないので、証拠のまま使う。主キーはどの列も NOT NULL なので常に証拠。
    /// </remarks>
    internal static bool HoldsInLiveRows(
        Entity? liveTable,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<ColumnId, string> typeDecidingColumns,
        bool excludeAllNullableUniques
    )
    {
        if (
            liveTable is null
            || columns.Any(c =>
                !liveTable.Columns.Any(l => SchemaNames.AreSame(l.Name, c))
                || typeDecidingColumns.ContainsKey(PlanIdentity.ColumnKey(table, c))
            )
        )
        {
            return false;
        }

        var set = columns.Select(SchemaNames.Key).ToHashSet(StringComparer.Ordinal);
        var namesById = liveTable.Columns.ToDictionary(c => c.Id, c => SchemaNames.Key(c.Name));
        var nullableById = liveTable
            .Columns.Where(ColumnNullability.AllowsNull)
            .Select(c => c.Id)
            .ToHashSet();
        IEnumerable<IReadOnlyList<string>> keys =
        [
            [.. liveTable.GetPrimaryKeyColumnsInOrder().Select(c => SchemaNames.Key(c.Name))],
            .. liveTable
                .UniqueConstraints.Where(u =>
                    u.ColumnIds.All(namesById.ContainsKey)
                    && !(excludeAllNullableUniques && u.ColumnIds.All(nullableById.Contains))
                )
                .Select(u => (IReadOnlyList<string>)[.. u.ColumnIds.Select(id => namesById[id])]),
        ];

        return keys.Any(key => key.Count > 0 && key.All(set.Contains));
    }

    /// <summary>この主キー変更項目が新しい主キー列を持つか（＝付与フェーズを要するか）</summary>
    internal static bool HasNewPrimaryKeyColumns(SchemaDiffItem item) =>
        item.Entity?.Columns.Any(c => c.IsPrimaryKey) == true;

    /// <summary>
    /// 外部キーが参照先の旧主キー（live の主キー）に支えられているか（主キーを外すと巻き込まれるか）。
    /// </summary>
    /// <remarks>
    /// 判定は <see cref="CanSupportForeignKey"/>（同じ列集合か、MySQL では旧主キーの先頭の列だけを参照する非標準キーの外部キーも）。
    /// 一意制約を参照する外部キーは巻き込まれない。参照先が live に無ければ支えられているとみなす。
    /// </remarks>
    internal static bool DependsOnOldPrimaryKey(
        NameLookup<Entity> liveByName,
        SyncDialectCapabilities capabilities,
        string parentTable,
        IReadOnlyList<string> referencedColumns
    )
    {
        if (!liveByName.TryGet(parentTable, out var liveParent))
        {
            return true;
        }

        return CanSupportForeignKey(
            capabilities,
            [.. liveParent.GetPrimaryKeyColumnsInOrder().Select(c => c.Name)],
            referencedColumns
        );
    }

    /// <summary>外部キーの子列が、索引の先頭の列とちょうど同じ集合か</summary>
    /// <remarks>
    /// MySQL は外部キーの子列を先頭に持つ索引を要し、それが消える索引しか無いと索引を外せない（エラー 1553）。
    /// 別の索引が支えている場合も外し直すことになるが、外部キーを外して付け直すだけなので害は無い。
    /// </remarks>
    internal static bool IsLeadingPrefix(
        IReadOnlyList<string> indexColumns,
        IReadOnlyList<ForeignKeyColumnNamePair> columnPairs
    )
    {
        if (indexColumns.Count < columnPairs.Count)
        {
            return false;
        }

        var leading = indexColumns.Take(columnPairs.Count).ToHashSet(SchemaNames.Comparer);
        return ForeignKeyColumnPairResolver.ChildColumns(columnPairs).All(leading.Contains);
    }

    /// <summary>
    /// 端の型がこの同期で決まる組のうち、同期後（その型を当てた後）に両端の列の型が並べられない組がある外部キーか
    /// </summary>
    /// <param name="fk">live の外部キー</param>
    /// <param name="childTable">子表</param>
    /// <param name="parentTable">親表</param>
    /// <param name="typeDecidingColumns">この同期で型が決まる列（<see cref="PostSyncSchema.TypeDecidingColumns"/>）</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <param name="column">null でなければ、この列（列の照合キー）を端に持つ組だけを見る</param>
    /// <remarks>
    /// 判定は組単位（<see cref="IsMismatchedUnacceptedPair"/>）。型の比較は差分の型比較と同じ規則
    /// （<see cref="SyncDialectCapabilities.AreForeignKeyCompatibleTypes"/>＝方言の取込表記へ寄せる）。live の表記と図の表記が
    /// 混ざって比べられるため、正規化を通さないと綴りだけ違う同じ型を「食い違う」と判定し、外し直しを止めてしまう。
    /// 型の決まらない端は live の型で比べる。どちらの端の型も決まらない組は数えない（DB が今の両端の型のまま受け入れている）。
    /// 外部キー単位で見ると、複合外部キーのほかの組の型を変えたとき、DB が受け入れている型の違う組まで食い違いに数え、外部キーを
    /// 外し直さずに主キーの解除・列の変更を必ず失敗させた。
    /// </remarks>
    internal static bool HasPostSyncTypeMismatch(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        string parentTable,
        IReadOnlyDictionary<ColumnId, string> typeDecidingColumns,
        SyncDialectCapabilities capabilities,
        ColumnId? column = null
    )
    {
        return fk.ColumnPairs.Any(pair =>
            (
                column is null
                || PlanIdentity.ColumnKey(childTable, pair.ChildColumn) == column
                || PlanIdentity.ColumnKey(parentTable, pair.ParentColumn) == column
            )
            // live の外部キーの組は DB が今受け入れている組
            && IsMismatchedUnacceptedPair(
                PostSyncType(fk.Child, childTable, pair.ChildColumn),
                PostSyncType(fk.Parent, parentTable, pair.ParentColumn),
                liveHasPair: true,
                capabilities
            )
        );

        (string Type, bool Decided) PostSyncType(Entity entity, string table, string name) =>
            typeDecidingColumns.TryGetValue(PlanIdentity.ColumnKey(table, name), out var decided)
                ? (decided, true)
                : (ColumnTypeOf(entity, name), false);
    }

    /// <summary>
    /// 外部キーの 1 組が、DB が受け入れた組でなく、同期後に両端が並べられないか（型の食い違いの判定の正本）
    /// </summary>
    /// <param name="child">子の端の同期後の型と、その型がこの同期で決まるか</param>
    /// <param name="parent">親の端の同期後の型と、その型がこの同期で決まるか</param>
    /// <param name="liveHasPair">live にこの組（子の表と列・親の表と列）を持つ外部キーがあるか</param>
    /// <param name="capabilities">型の比較に使う方言のケーパビリティ</param>
    /// <remarks>
    /// 「DB が受け入れた組」は、どちらの端の型もこの同期で決まらず、live の外部キーが同じ組を持つ組（DB が今その両端の型のまま
    /// 受け入れている）。端の型が決まる組と、DB にまだ無い組（選んだ外部キーの追加・列ペアを変える作り直しの新しい組）は数える。
    /// DB にまだ無い組まで「型が決まらないから受け入れている」とみなすと、live の列どうしで型の違う外部キーの追加
    /// （親 int・子 bigint など）が名指しされずに MySQL（エラー 3780）・SQL Server（Msg 1778）で断られる。
    /// </remarks>
    internal static bool IsMismatchedUnacceptedPair(
        (string Type, bool Decided) child,
        (string Type, bool Decided) parent,
        bool liveHasPair,
        SyncDialectCapabilities capabilities
    ) =>
        (child.Decided || parent.Decided || !liveHasPair)
        && !capabilities.AreForeignKeyCompatibleTypes(child.Type, parent.Type);

    /// <summary>主キーが変わる表を参照する外部キーの被参照列が、同期後も候補キーであることを証明できるか</summary>
    /// <remarks>
    /// 根拠は「同期後の主キーが被参照列集合とちょうど一致」か「同期後に同じ列集合の一意制約が在る」のいずれか。
    /// 被参照列が新主キーに含まれていても他の列と複合になっていれば主キーは根拠にならない
    /// （(id) → (id, code) の拡張は 4 方言中 3 方言で再 ADD が失敗する）。
    /// 一意制約はモデルの正本（Entity.UniqueConstraints）から同期後の集合を厳密に合成して判定するため、
    /// 「自然キー UNIQUE を持つ表の主キー付け替え」は誤警告しない。証明できない構成は
    /// （一意インデックス等で実際には通ることがあっても）警告する＝実行は止めない安全側へ倒す。
    /// 比べるのは列の集合で、どの方言でも並びを問わない（参照する列の並びがキーの並びと一致しないと外部キーを作れない
    /// 方言では、計画が付け直しを参照先の同期後のキーの並びへ並べ替える＝<see cref="Scheduling.ForeignKeyPairOrder"/>）。
    /// </remarks>
    internal static bool StaysCandidateKey(
        IReadOnlyList<string>? newPkColumns,
        IReadOnlyDictionary<string, IReadOnlySet<string>> postSyncUniques,
        string parentTable,
        IReadOnlyList<string> referencedColumns
    ) =>
        (
            newPkColumns is not null
            && string.Equals(
                UniqueConstraintNaming.ColumnSetSignature(newPkColumns),
                UniqueConstraintNaming.ColumnSetSignature(referencedColumns),
                StringComparison.Ordinal
            )
        )
        || (
            postSyncUniques.TryGetValue(parentTable, out var signatures)
            && signatures.Contains(UniqueConstraintNaming.ColumnSetSignature(referencedColumns))
        );

    /// <summary>
    /// 作れない方言で、この外部キーの付け直しが必ず失敗するか（参照アクション <c>SET NULL</c> で、子の列が同期後に NOT NULL）
    /// </summary>
    internal static bool BlocksSetNullRebuild(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        SyncDialectCapabilities capabilities,
        Func<Entity, string, string, bool> postSyncNullable
    ) =>
        capabilities.SetNullRequiresNullableChildColumn
        && UsesSetNull(fk.Relationship, capabilities)
        && PostSyncNotNullChildColumns(fk, childTable, postSyncNullable).Count > 0;

    /// <summary>live の外部キーの子の列のうち、同期後に NOT NULL のもの（宣言順）</summary>
    internal static List<string> PostSyncNotNullChildColumns(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        Func<Entity, string, string, bool> postSyncNullable
    ) =>
        [
            .. fk
                .ColumnPairs.Select(p => p.ChildColumn)
                .Where(column => !postSyncNullable(fk.Child, childTable, column)),
        ];

    /// <summary>方言が書く参照アクション（ON DELETE か ON UPDATE）が <c>SET NULL</c> か</summary>
    internal static bool UsesSetNull(
        Relationship relationship,
        SyncDialectCapabilities capabilities
    ) =>
        capabilities.EffectiveOnDelete(relationship.OnDelete) == ForeignKeyReferentialAction.SetNull
        || capabilities.EffectiveOnUpdate(relationship.OnUpdate)
            == ForeignKeyReferentialAction.SetNull;

    /// <summary><see cref="SyncPlanWarningKind.SetNullOnNotNullColumn"/> の警告（Detail は「制約名 (同期後に NOT NULL の子の列, …)」）</summary>
    internal static SyncPlanWarning SetNullWarning(
        string childTable,
        string constraintName,
        IReadOnlyList<string> notNullColumns
    ) =>
        new(
            SyncPlanWarningKind.SetNullOnNotNullColumn,
            childTable,
            $"{constraintName} ({string.Join(", ", notNullColumns)})"
        );

    /// <summary>2 つの列名集合が（順序・大文字小文字を無視して）ちょうど一致するか</summary>
    internal static bool IsSameColumnSet(IEnumerable<string> left, IEnumerable<string> right) =>
        string.Equals(
            UniqueConstraintNaming.ColumnSetSignature(left),
            UniqueConstraintNaming.ColumnSetSignature(right),
            StringComparison.Ordinal
        );

    /// <summary>エンティティの列の型（列名は <see cref="SchemaNames"/> の規則で引く。列が無ければ空文字列）</summary>
    /// <remarks>
    /// live の列の型（DB が今受け入れている型）・図の列の型を読む箇所が通す。この同期で型が決まる列の新しい型は
    /// <see cref="PostSyncSchema.TypeDecidingColumns"/> が決め、ここでは読まない。
    /// </remarks>
    internal static string ColumnTypeOf(Entity entity, string column) =>
        entity.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, column))?.DataType
        ?? string.Empty;

    /// <summary>親のキー（主キー・一意制約）が、その列を参照する外部キーを支えうるか（DB がそのキーの削除を外部キーで断るか）</summary>
    /// <param name="capabilities">方言のケーパビリティ</param>
    /// <param name="keyColumns">キーの構成列（主キーは実効順・一意制約は宣言順）</param>
    /// <param name="referencedColumns">外部キーが参照する親の列</param>
    /// <remarks>
    /// <para>
    /// 参照する列と同じ列の集合を持つキーは、どの方言でも支える。外部キーが参照先の索引の先頭の列を要する方言
    /// （<see cref="SyncDialectCapabilities.ForeignKeyRequiresLeadingIndex"/>＝MySQL）では、参照する列を先頭に並べるもっと長いキーも
    /// 支えうる（8.0 系や restrict_fk_on_non_standard_key=OFF で作れる非標準キーの外部キー。ほかに支える索引が無いと、そのキーの
    /// 削除がエラー 1553 になる）。
    /// </para>
    /// <para>
    /// 判定はここの 1 か所で、前寄せ（<see cref="Assessment.Refusals.FindCertain"/>）・一意制約の削除の警告
    /// （<see cref="Assessment.AssessmentRules.WarnUniqueConstraintDropsBreakingForeignKeys"/>）・主キーの変更の外し直し（<see cref="DependsOnOldPrimaryKey"/> と、
    /// 新しい主キーへ付け直す一意制約の枝）が通す。写しが割れると、前寄せした削除を警告が名指しせず、警告を飛ばした外部キーを
    /// 外し直しも付け直さない。付け直せるか（候補キーの証明＝<see cref="StaysCandidateKey"/>）は
    /// 別の判定で、列の集合の完全一致のまま（非標準キーへの付け直しは証明しない）。
    /// </para>
    /// </remarks>
    internal static bool CanSupportForeignKey(
        SyncDialectCapabilities capabilities,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> referencedColumns
    ) =>
        IsSameColumnSet(keyColumns, referencedColumns)
        || capabilities.ForeignKeyRequiresLeadingIndex
            && keyColumns.Count > referencedColumns.Count
            && IsSameColumnSet(keyColumns.Take(referencedColumns.Count), referencedColumns);
}
