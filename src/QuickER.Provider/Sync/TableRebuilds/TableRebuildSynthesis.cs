using System.Collections.Generic;
using System.Linq;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.TableRebuilds;

/// <summary>テーブル再構築の合成の結果</summary>
/// <param name="Rebuilds">
/// 再構築の計画（新しい表を作るだけの再構築 → 既存の表の再構築の順。どちらも選んだ項目の順）
/// </param>
/// <param name="Folded">
/// 処理済みの項目＝再構築へ畳んだ項目と、削除を選んだ表の作り直しの項目（表ごと消えるので捨てる）。参照の同一性で比べる集合
/// </param>
internal sealed record TableRebuildSynthesisResult(
    IReadOnlyList<TableRebuildPlan> Rebuilds,
    IReadOnlySet<SchemaDiffItem> Folded
);

/// <summary>
/// テーブル再構築の方言（SQLite）で、選んだ項目の最終形と DB の今の図から、表ごとの再構築の計画（<see cref="TableRebuildPlan"/>）を合成する
/// </summary>
/// <remarks>
/// <para>
/// 合成は「live＋選択した差分だけ」で行う。図の定義を直接使うと、選んでいない変更が紛れ込む。
/// 逐次 DDL で表せない変更をテーブル単位の再構築へ集約し、残り（新規テーブル対象でない列追加・テーブル削除など）は畳まない
/// ＝呼び出し側が選んだ項目から <see cref="TableRebuildSynthesisResult.Folded"/> を除いてセクションにする。
/// </para>
/// <para>
/// 合成は差分の項目を作らず、受け取った項目・同期後の事実・DB の今の図を書き換えない（合成後の定義は live の表の写し）。
/// </para>
/// </remarks>
internal static class TableRebuildSynthesis
{
    /// <summary>選んだ項目の最終形から、再構築の計画と処理済みの項目を合成する</summary>
    /// <param name="postSync">選んだ項目の最終形の同期後の事実（<see cref="PlanStage.Selected"/>）</param>
    /// <param name="liveSchema">DB の今の図</param>
    /// <param name="context">計画の入力（補助オブジェクトを読む）</param>
    /// <param name="capabilities">方言の宣言（識別子の上限・rowid の別名の宣言を読む）</param>
    internal static TableRebuildSynthesisResult Synthesize(
        PostSyncSchema postSync,
        LiveSchema liveSchema,
        SyncPlanContext context,
        SyncDialectCapabilities capabilities
    )
    {
        var selected = postSync.Items;
        var limit = capabilities.IdentifierLengthLimit;

        // 選択済み AddTable のテーブル名（新規テーブル＝CreateOnly の対象）
        var addedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable)
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .ToHashSet(SchemaNames.Comparer);

        // live のテーブル索引（合成の土台）
        var liveByName = liveSchema.Tables;

        // 既存テーブルの再構築対象（新規テーブルを子とする AddForeignKey は CreateOnly へ畳むため除外）。
        // live に実在するテーブルに限る——「新規テーブルの AddTable は未選択のまま、そのテーブルへの
        // AddForeignKey だけ選択」のような組み合わせでは合成の土台が無く、該当項目はセクションへ残して
        // レンダラーのスキップコメントに委ねる（例外にすると UI のチェック操作でクラッシュするため）
        // 削除を選んだ表は作り直さない（作り直してから消すのは無駄で、意味の無い喪失警告も出る）。
        // その表への作り直しの項目は、表ごと消えるので処理済みとして捨てる
        var droppedTables = postSync.Dropped.Tables;
        var existingRebuildTables = selected
            .Where(i =>
                TableRebuildDialects.IsTriggerKind(i.Kind)
                && !addedTables.Contains(SchemaNames.ToStoredForm(i.TableName))
                && !droppedTables.Contains(SchemaNames.ToStoredForm(i.TableName))
            )
            .Select(i => SchemaNames.ToStoredForm(i.TableName))
            .Where(liveByName.Contains)
            .ToHashSet(SchemaNames.Comparer);

        // 処理済み（再構築へ畳んだ・表ごと消えるので捨てた）項目の集合（SchemaDiffItem は参照同一性で比較する）
        var folded = new HashSet<SchemaDiffItem>(ReferenceEqualityComparer.Instance);

        foreach (
            var item in selected.Where(i =>
                droppedTables.Contains(SchemaNames.ToStoredForm(i.TableName))
                && (
                    TableRebuildDialects.IsTriggerKind(i.Kind) || i.Kind == SchemaDiffKind.AddColumn
                )
            )
        )
        {
            folded.Add(item);
        }

        // live の外部キーは子表ごとに 1 回だけまとめる（作り直す表ごとに全外部キーを解決し直さない）
        var liveForeignKeysByChild = liveSchema.ForeignKeys.ToLookup(
            fk => SchemaDiffService.NormalizeTable(fk.Child),
            SchemaNames.Comparer
        );

        var rebuilds = new List<TableRebuildPlan>();
        var createOnly = BuildCreateOnlyRebuilds(selected, addedTables, limit);
        rebuilds.AddRange(createOnly.Rebuilds);
        folded.UnionWith(createOnly.Folded);
        var existing = BuildExistingTableRebuilds(
            selected,
            existingRebuildTables,
            liveByName,
            context,
            liveSchema,
            liveForeignKeysByChild,
            limit,
            capabilities
        );
        rebuilds.AddRange(existing.Rebuilds);
        folded.UnionWith(existing.Folded);

        return new TableRebuildSynthesisResult(rebuilds, folded);
    }

    /// <summary>選択済み AddTable を CreateOnly の再構築計画へ変換する（新規テーブルへの FK はインラインへ畳む）</summary>
    /// <remarks>畳み込んだ項目（AddTable と、解決できて畳んだ AddForeignKey）を再構築の計画と一緒に返す。</remarks>
    private static (
        List<TableRebuildPlan> Rebuilds,
        List<SchemaDiffItem> Folded
    ) BuildCreateOnlyRebuilds(
        IReadOnlyList<SchemaDiffItem> selected,
        HashSet<string> addedTables,
        IdentifierLengthLimit limit
    )
    {
        var rebuilds = new List<TableRebuildPlan>();
        var folded = new List<SchemaDiffItem>();

        foreach (var addTable in selected.Where(i => i.Kind == SchemaDiffKind.AddTable))
        {
            var table = SchemaNames.ToStoredForm(addTable.TableName);
            folded.Add(addTable);

            // その新テーブルを子とする選択済み AddForeignKey を FK 仕様として畳み込む。
            // 解決できない FK は畳まずセクションへ残し、レンダラーのスキップコメントで明示する
            var fkItems = selected
                .Where(i =>
                    i.Kind == SchemaDiffKind.AddForeignKey
                    && SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                )
                .ToList();
            var fks = new List<TableRebuildForeignKey>();
            var source = new List<SchemaDiffItem> { addTable };

            foreach (var fkItem in fkItems)
            {
                var resolved = TableRebuildForeignKeys.ResolveAdded(fkItem, limit);

                if (resolved is null)
                {
                    continue;
                }

                fks.Add(resolved);
                source.Add(fkItem);
                folded.Add(fkItem);
            }

            rebuilds.Add(
                new TableRebuildPlan
                {
                    TableName = addTable.TableName,
                    NewDefinition =
                        addTable.Entity?.Clone(preserveId: true)
                        ?? new Entity { TableName = addTable.TableName },
                    ForeignKeys = fks,
                    CreateOnly = true,
                    CopyColumns = [],
                    AuxiliaryObjects = [],
                    SourceItems = source,
                }
            );
        }

        return (rebuilds, folded);
    }

    /// <summary>既存テーブルの再構築計画を組み立てる（live 定義に選択差分のみを適用して合成する）</summary>
    /// <remarks>
    /// 合成で解決できなかった項目（無い列の定義変更・構成列の無い一意制約・解決できない外部キー）は処理済みにせず、
    /// セクションへ残す（レンダラーがスキップのコメントを出す）。処理済みにすると、何も変えずに成功扱いになり、次の差分で
    /// 同じ項目がまた出る。作り直す理由になる項目が 1 つも解決できなければ、その表は作り直さない。
    /// 畳み込んだ項目は再構築の計画と一緒に返す。
    /// </remarks>
    private static (
        List<TableRebuildPlan> Rebuilds,
        List<SchemaDiffItem> Folded
    ) BuildExistingTableRebuilds(
        IReadOnlyList<SchemaDiffItem> selected,
        HashSet<string> existingRebuildTables,
        NameLookup<Entity> liveByName,
        SyncPlanContext context,
        LiveSchema liveSchema,
        ILookup<string, ResolvedForeignKeyRelationship> liveForeignKeysByChild,
        IdentifierLengthLimit limit,
        SyncDialectCapabilities capabilities
    )
    {
        var rebuilds = new List<TableRebuildPlan>();
        var folded = new List<SchemaDiffItem>();

        foreach (var table in existingRebuildTables)
        {
            // existingRebuildTables は引ける名前だけに絞ってある
            if (!liveByName.TryGet(table, out var live))
            {
                continue;
            }

            // このテーブルに畳み込む項目（再構築トリガー種別＋列追加）を確定する
            var candidates = selected
                .Where(i =>
                    SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                    && (
                        TableRebuildDialects.IsTriggerKind(i.Kind)
                        || i.Kind == SchemaDiffKind.AddColumn
                    )
                )
                .ToList();

            // 列の追加・削除・変更を先に当てた定義で、残りの項目が解決できるかを見る
            var columnsOnly = live.Clone(preserveId: true);
            TableRebuildDefinition.ApplyColumnChanges(columnsOnly, candidates);
            var tableItems = candidates
                .Where(i => IsResolvableInRebuild(i, live, columnsOnly, limit))
                .ToList();

            if (!tableItems.Any(i => TableRebuildDialects.IsTriggerKind(i.Kind)))
            {
                continue;
            }

            foreach (var item in tableItems)
            {
                folded.Add(item);
            }

            // live 定義の深いコピーに、選択された差分のみを適用して合成する
            var newDef = live.Clone(preserveId: true);

            // 一意制約の削除は、列を消す前の live の列で照合する（列を先に消すと構成列を引き当てられず、
            // 消える列を含む一意制約が合成後の定義に残る）
            TableRebuildDefinition.ApplyUniqueConstraintDrops(newDef, tableItems);
            TableRebuildDefinition.ApplyColumnChanges(newDef, tableItems);

            // 主キー変更（AlterPrimaryKey）が選択されていれば、合成後の列へ target の主キー構成を反映する
            TableRebuildDefinition.ApplyPrimaryKeyChange(newDef, tableItems);

            // 列順変更（ReorderColumns）が選択されていれば、合成後の列を target の列名順へ並べ替える
            TableRebuildDefinition.ApplyReorder(newDef, tableItems);

            // 一意制約の追加が選択されていれば、合成後の一意制約集合へ反映する
            TableRebuildDefinition.ApplyUniqueConstraintAdds(newDef, tableItems);

            // FK 集合 = live の子側 FK − 選択済み DropForeignKey ＋ 選択済み AddForeignKey
            var foreignKeys = TableRebuildForeignKeys.Synthesize(
                tableItems,
                liveForeignKeysByChild[table],
                limit,
                liveSchema
            );

            // データ移送対象 = live と合成後の両方に存在する列（合成後の列順で並べる）
            var liveColumnNames = live.Columns.Select(c => c.Name).ToHashSet(SchemaNames.Comparer);
            var copyColumns = newDef
                .Columns.Select(c => c.Name)
                .Where(liveColumnNames.Contains)
                .ToList();

            var auxiliaryObjects = context
                .AuxiliaryObjects.Where(a =>
                    SchemaNames.AreSame(SchemaNames.ToStoredForm(a.TableName), table)
                )
                .ToList();

            rebuilds.Add(
                new TableRebuildPlan
                {
                    TableName = live.TableName,
                    NewDefinition = newDef,
                    ForeignKeys = foreignKeys,
                    CreateOnly = false,
                    CopyColumns = copyColumns,
                    RowidAliasNullGuardColumn = TableRebuildRowidAlias.NullGuardColumn(
                        live,
                        newDef,
                        copyColumns,
                        capabilities
                    ),
                    AuxiliaryObjects = auxiliaryObjects,
                    SourceItems = tableItems,
                }
            );
        }

        return (rebuilds, folded);
    }

    /// <summary>再構築の合成で項目を解決できるか（解決できない項目は再構築へ畳まずセクションへ残す）</summary>
    /// <param name="item">再構築へ畳む候補の項目</param>
    /// <param name="live">live の表</param>
    /// <param name="columnsApplied">live に選んだ列の追加・削除・変更を当てた定義</param>
    private static bool IsResolvableInRebuild(
        SchemaDiffItem item,
        Entity live,
        Entity columnsApplied,
        IdentifierLengthLimit limit
    )
    {
        bool HasColumn(Entity entity, string? name) =>
            name is not null && entity.Columns.Any(c => SchemaNames.AreSame(c.Name, name));

        return item.Kind switch
        {
            SchemaDiffKind.AlterColumn => HasColumn(live, item.ColumnName),
            SchemaDiffKind.AddUniqueConstraint => item.UniqueConstraintColumns.Count > 0
                && item.UniqueConstraintColumns.All(c => HasColumn(columnsApplied, c)),
            SchemaDiffKind.AddForeignKey => TableRebuildForeignKeys.ResolveAdded(item, limit)
                is { } fk
                && fk.ChildColumns.All(c => HasColumn(columnsApplied, c)),
            _ => true,
        };
    }
}
