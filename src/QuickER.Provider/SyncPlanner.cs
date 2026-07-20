using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// テーブル再構築（rebuild）方言で、live スキーマと選択差分から合成計画を組み立てるための入力。
/// </summary>
/// <remarks>
/// rebuild は「DB 現状（live）＋選択された差分のみ」を合成した定義でテーブルを作り直す。図の定義を直接
/// 使うと未選択の変更が紛れ込みデータ破壊を招くため、live スキーマ（＝差分計算に用いたもの）を必須入力とする。
/// </remarks>
public sealed class SyncPlanContext
{
    /// <summary>DB から取得した現在のエンティティ（合成の土台）。</summary>
    public IReadOnlyList<Entity> LiveEntities { get; init; } = [];

    /// <summary>DB から取得した現在のリレーション（既存 FK 集合の復元に用いる）。</summary>
    public IReadOnlyList<Relationship> LiveRelationships { get; init; } = [];

    /// <summary>DB から取得した補助オブジェクト（再構築で温存するインデックス・トリガー・一意制約）。</summary>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];
}

/// <summary>
/// 差分項目から方言中立の実行計画（<see cref="SyncPlan"/>）を組み立てるサービス。
/// </summary>
/// <remarks>
/// <para>
/// 選択済みの差分を「依存関係で失敗しない固定順序」でセクション化するのが責務。方言別レンダラー
/// （<see cref="ISyncScriptBuilder"/> の実装）はこの計画を SQL へ変換するだけでよく、選択フィルタ・
/// 出力順序・種別グループ化の知識を各方言に重複させない。
/// </para>
/// <para>
/// 逐次 DDL 方言（SQL Server / PostgreSQL / MySQL / Oracle）では全差分をそのままセクション化する。
/// テーブル再構築方言（SQLite＝<see cref="SyncDialectCapabilities.SupportsAlterColumn"/> または
/// <see cref="SyncDialectCapabilities.SupportsForeignKeyAlter"/> が <c>false</c>）では、逐次 DDL で表現できない
/// 変更（列型変更・列削除・FK 変更・新規テーブルへの後付け FK）をテーブル単位の <see cref="TableRebuildPlan"/> へ
/// 集約する（合成には live スキーマが必須＝<see cref="SyncPlanContext"/> を要求する）。
/// </para>
/// </remarks>
public sealed class SyncPlanner
{
    /// <summary>
    /// セクションの出力順序。依存関係による失敗を避けるため、
    /// テーブル / 列の追加 → FK 解除 → 列 / テーブル削除 → FK 追加 → 説明設定の順に並べる。
    /// </summary>
    private static readonly SchemaDiffKind[] SectionOrder =
    [
        SchemaDiffKind.AddTable,
        SchemaDiffKind.AddColumn,
        SchemaDiffKind.AlterColumn,
        SchemaDiffKind.DropForeignKey,
        SchemaDiffKind.DropColumn,
        SchemaDiffKind.DropTable,
        SchemaDiffKind.AddForeignKey,
        SchemaDiffKind.SetTableDescription,
        SchemaDiffKind.SetColumnDescription,
    ];

    /// <summary>テーブル名の比較は方言横断で大文字小文字を無視する</summary>
    private static readonly StringComparer TableComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>選択済みの差分項目から実行計画を組み立てる</summary>
    /// <param name="items">全差分項目（未選択・情報表示専用の項目を含んでよい）</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ（rebuild への振り分けに用いる）</param>
    /// <param name="context">
    /// rebuild 方言で合成に用いる live スキーマ。逐次 DDL 方言では不要（<c>null</c> 可）。
    /// rebuild 方言で <c>null</c> を渡すと <see cref="InvalidOperationException"/> を投げる（呼び出し側のバグ）。
    /// </param>
    /// <returns>選択済み項目のみを固定順序でセクション化した計画（空セクションは含まない）</returns>
    public SyncPlan BuildPlan(
        IEnumerable<SchemaDiffItem> items,
        SyncDialectCapabilities capabilities,
        SyncPlanContext? context = null
    )
    {
        // 選択済みの項目のみを対象にする（未選択は完全に除外）
        var selected = items.Where(i => i.IsSelected).ToList();

        // ALTER COLUMN も FK の後付けもできない方言はテーブル再構築が必要になる
        var isRebuildDialect =
            !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

        if (!isRebuildDialect)
        {
            // 逐次 DDL 方言: 全差分をそのままセクション化する（Phase 1 と完全同一）
            return new SyncPlan { Sections = BuildSections(selected) };
        }

        if (context is null)
        {
            // rebuild 合成には live スキーマが必須（図の定義を直接使うと未選択の変更が紛れ込むため）
            throw new InvalidOperationException(
                "Rebuild dialects require a SyncPlanContext (live schema) to synthesize table rebuilds."
            );
        }

        return BuildRebuildPlan(selected, context);
    }

    /// <summary>固定順序のセクション一覧を組み立てる（空セクションは含まない）</summary>
    private static List<SyncPlanSection> BuildSections(IReadOnlyList<SchemaDiffItem> selected)
    {
        var sections = new List<SyncPlanSection>();

        foreach (var kind in SectionOrder)
        {
            // 種別ごとに抽出する。RebuildTable 等 SectionOrder に無い種別はここで自然に除外される
            var subset = selected.Where(i => i.Kind == kind).ToList();

            if (subset.Count == 0)
            {
                continue;
            }

            sections.Add(new SyncPlanSection { Kind = kind, Items = subset });
        }

        return sections;
    }

    /// <summary>
    /// rebuild 方言の実行計画を組み立てる。逐次 DDL で表現できない変更をテーブル単位の再構築へ集約し、
    /// 残り（新規テーブル対象でない列追加・テーブル削除）は従来どおりセクションへ残す。
    /// </summary>
    private static SyncPlan BuildRebuildPlan(List<SchemaDiffItem> selected, SyncPlanContext context)
    {
        // 選択済み AddTable のテーブル名（新規テーブル＝CreateOnly の対象）
        var addedTables = selected
            .Where(i => i.Kind == SchemaDiffKind.AddTable)
            .Select(i => i.TableName.Trim())
            .ToHashSet(TableComparer);

        // live のテーブル索引（合成の土台）
        var liveByName = new Dictionary<string, Entity>(TableComparer);

        foreach (var live in context.LiveEntities)
        {
            liveByName.TryAdd(SchemaDiffService.NormalizeTable(live), live);
        }

        // 既存テーブルの再構築対象（新規テーブルを子とする AddForeignKey は CreateOnly へ畳むため除外）。
        // live に実在するテーブルに限る——「新規テーブルの AddTable は未選択のまま、そのテーブルへの
        // AddForeignKey だけ選択」のような組み合わせでは合成の土台が無く、該当項目はセクションへ残して
        // レンダラーのスキップコメントに委ねる（例外にすると UI のチェック操作でクラッシュするため）
        var existingRebuildTables = selected
            .Where(i => IsRebuildTriggerKind(i.Kind) && !addedTables.Contains(i.TableName.Trim()))
            .Select(i => i.TableName.Trim())
            .Where(liveByName.ContainsKey)
            .ToHashSet(TableComparer);

        // rebuild へ転用（畳み込み）が確定した項目の集合（SchemaDiffItem は参照同一性で比較する）
        var diverted = new HashSet<SchemaDiffItem>();

        var rebuilds = new List<TableRebuildPlan>();
        rebuilds.AddRange(BuildCreateOnlyRebuilds(selected, addedTables, diverted));
        rebuilds.AddRange(
            BuildExistingTableRebuilds(
                selected,
                existingRebuildTables,
                liveByName,
                diverted,
                context
            )
        );

        // 転用済みの項目を除いた残りをセクション化する（転用できなかった項目はレンダラーがスキップを明示する）
        var sectionItems = selected.Where(i => !diverted.Contains(i)).ToList();

        return new SyncPlan { Sections = BuildSections(sectionItems), Rebuilds = rebuilds };
    }

    /// <summary>選択済み AddTable を CreateOnly の再構築計画へ変換する（新規テーブルへの FK はインラインへ畳む）</summary>
    private static IEnumerable<TableRebuildPlan> BuildCreateOnlyRebuilds(
        List<SchemaDiffItem> selected,
        HashSet<string> addedTables,
        HashSet<SchemaDiffItem> diverted
    )
    {
        foreach (var addTable in selected.Where(i => i.Kind == SchemaDiffKind.AddTable))
        {
            var table = addTable.TableName.Trim();
            diverted.Add(addTable);

            // その新テーブルを子とする選択済み AddForeignKey を FK 仕様として畳み込む。
            // 解決できない FK は畳まずセクションへ残し、レンダラーのスキップコメントで明示する
            var fkItems = selected
                .Where(i =>
                    i.Kind == SchemaDiffKind.AddForeignKey
                    && TableComparer.Equals(i.TableName.Trim(), table)
                )
                .ToList();
            var fks = new List<TableRebuildForeignKey>();
            var source = new List<SchemaDiffItem> { addTable };

            foreach (var fkItem in fkItems)
            {
                var resolved = ResolveAddedForeignKey(fkItem);

                if (resolved is null)
                {
                    continue;
                }

                fks.Add(resolved);
                source.Add(fkItem);
                diverted.Add(fkItem);
            }

            yield return new TableRebuildPlan
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
            };
        }
    }

    /// <summary>既存テーブルの再構築計画を組み立てる（live 定義に選択差分のみを適用して合成する）</summary>
    private static IEnumerable<TableRebuildPlan> BuildExistingTableRebuilds(
        List<SchemaDiffItem> selected,
        HashSet<string> existingRebuildTables,
        Dictionary<string, Entity> liveByName,
        HashSet<SchemaDiffItem> diverted,
        SyncPlanContext context
    )
    {
        foreach (var table in existingRebuildTables)
        {
            var live = liveByName[table];

            // このテーブルに畳み込む項目（再構築トリガー種別＋列追加）を確定する
            var tableItems = selected
                .Where(i =>
                    TableComparer.Equals(i.TableName.Trim(), table)
                    && (IsRebuildTriggerKind(i.Kind) || i.Kind == SchemaDiffKind.AddColumn)
                )
                .ToList();

            foreach (var item in tableItems)
            {
                diverted.Add(item);
            }

            // live 定義の深いコピーに、選択された差分のみを適用して合成する
            var newDef = live.Clone(preserveId: true);
            ApplySelectedColumnChanges(newDef, tableItems);

            // FK 集合 = live の子側 FK − 選択済み DropForeignKey ＋ 選択済み AddForeignKey
            var foreignKeys = SynthesizeForeignKeys(table, tableItems, context);

            // データ移送対象 = live と合成後の両方に存在する列（合成後の列順で並べる）
            var liveColumnNames = live.Columns.Select(c => c.Name).ToHashSet(TableComparer);
            var copyColumns = newDef
                .Columns.Select(c => c.Name)
                .Where(liveColumnNames.Contains)
                .ToList();

            var auxiliaryObjects = context
                .AuxiliaryObjects.Where(a => TableComparer.Equals(a.TableName.Trim(), table))
                .ToList();

            yield return new TableRebuildPlan
            {
                TableName = live.TableName,
                NewDefinition = newDef,
                ForeignKeys = foreignKeys,
                CreateOnly = false,
                CopyColumns = copyColumns,
                AuxiliaryObjects = auxiliaryObjects,
                SourceItems = tableItems,
            };
        }
    }

    /// <summary>合成後の定義へ、選択された列変更（AlterColumn 置換 / DropColumn 除去 / AddColumn 追加）を適用する</summary>
    private static void ApplySelectedColumnChanges(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        // AlterColumn: 同名の列定義を置換する
        foreach (var alter in tableItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn))
        {
            var index = newDef.Columns.FindIndex(c =>
                TableComparer.Equals(c.Name, alter.ColumnName)
            );

            if (index >= 0 && alter.Column is not null)
            {
                newDef.Columns[index] = alter.Column.Clone(preserveId: true);
            }
        }

        // DropColumn: 同名の列を除去する
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropColumn))
        {
            newDef.Columns.RemoveAll(c => TableComparer.Equals(c.Name, drop.ColumnName));
        }

        // AddColumn: 末尾へ畳み込む（このテーブルが他理由で再構築される場合のみここへ来る）
        foreach (var add in tableItems.Where(i => i.Kind == SchemaDiffKind.AddColumn))
        {
            if (add.Column is not null)
            {
                newDef.Columns.Add(add.Column.Clone(preserveId: true));
            }
        }
    }

    /// <summary>再構築後に張る FK 集合を合成する（live 集合から Drop を除き Add を足す）</summary>
    private static List<TableRebuildForeignKey> SynthesizeForeignKeys(
        string childTable,
        IReadOnlyList<SchemaDiffItem> tableItems,
        SyncPlanContext context
    )
    {
        var foreignKeys = ResolveLiveForeignKeys(childTable, context).ToList();

        // 選択済み DropForeignKey に一致する live FK を除去する（列・親テーブル・親列のシグネチャで照合）
        foreach (var dropFk in tableItems.Where(i => i.Kind == SchemaDiffKind.DropForeignKey))
        {
            var signature = ResolveDroppedForeignKeySignature(dropFk);

            if (signature is null)
            {
                continue;
            }

            var (childCol, parentTable, parentCol) = signature.Value;
            foreignKeys.RemoveAll(fk =>
                TableComparer.Equals(fk.ChildColumn, childCol)
                && TableComparer.Equals(fk.ParentTable, parentTable)
                && TableComparer.Equals(fk.ParentColumn, parentCol)
            );
        }

        // 選択済み AddForeignKey を追加する
        foreach (var addFk in tableItems.Where(i => i.Kind == SchemaDiffKind.AddForeignKey))
        {
            var resolved = ResolveAddedForeignKey(addFk);

            if (resolved is not null)
            {
                foreignKeys.Add(resolved);
            }
        }

        return foreignKeys;
    }

    /// <summary>live のリレーションから、指定した子テーブルの FK 仕様を解決する</summary>
    private static IEnumerable<TableRebuildForeignKey> ResolveLiveForeignKeys(
        string childTable,
        SyncPlanContext context
    )
    {
        foreach (var rel in context.LiveRelationships)
        {
            if (rel.Type == RelationshipType.ManyToMany)
            {
                continue;
            }

            var parent = context.LiveEntities.FirstOrDefault(e => e.Id == rel.SourceEntityId);
            var child = context.LiveEntities.FirstOrDefault(e => e.Id == rel.TargetEntityId);

            if (parent is null || child is null)
            {
                continue;
            }

            if (!TableComparer.Equals(SchemaDiffService.NormalizeTable(child), childTable))
            {
                continue;
            }

            var parentCol = SchemaDiffService.ResolveReferencedColumn(rel, parent);

            if (parentCol is null)
            {
                continue;
            }

            var childCol = SchemaDiffService.ResolveFkColumnName(rel, child, parent, parentCol);

            if (childCol is null)
            {
                continue;
            }

            var parentTable = SchemaDiffService.NormalizeTable(parent);
            var name = string.IsNullOrWhiteSpace(rel.ConstraintName)
                ? $"FK_{SafeName(SchemaDiffService.NormalizeTable(child))}_{SafeName(parentTable)}"
                : rel.ConstraintName!;

            yield return new TableRebuildForeignKey(
                name,
                childCol,
                parentTable,
                parentCol.Name,
                rel.OnDelete,
                rel.OnUpdate
            );
        }
    }

    /// <summary>選択済み AddForeignKey 差分項目を解決済みの FK 仕様へ変換する（解決不能なら null）</summary>
    private static TableRebuildForeignKey? ResolveAddedForeignKey(SchemaDiffItem item)
    {
        if (item.ChildEntity is null || item.ParentEntity is null)
        {
            return null;
        }

        var parentCol = SyncScriptBuilderHelper.ResolveReferencedColumn(item);

        if (parentCol is null || string.IsNullOrEmpty(item.ColumnName))
        {
            return null;
        }

        var childTable = SchemaDiffService.NormalizeTable(item.ChildEntity);
        var parentTable = SchemaDiffService.NormalizeTable(item.ParentEntity);
        var name = string.IsNullOrWhiteSpace(item.Relationship?.ConstraintName)
            ? $"FK_{SafeName(childTable)}_{SafeName(parentTable)}"
            : item.Relationship!.ConstraintName!;

        return new TableRebuildForeignKey(
            name,
            item.ColumnName!,
            parentTable,
            parentCol.Name,
            item.Relationship?.OnDelete ?? ForeignKeyReferentialAction.NoAction,
            item.Relationship?.OnUpdate ?? ForeignKeyReferentialAction.NoAction
        );
    }

    /// <summary>DropForeignKey 差分項目を、live FK 照合用のシグネチャ（子列・親テーブル・親列）へ解決する</summary>
    private static (
        string ChildColumn,
        string ParentTable,
        string ParentColumn
    )? ResolveDroppedForeignKeySignature(SchemaDiffItem item)
    {
        if (item.Relationship is null || item.ParentEntity is null || item.ChildEntity is null)
        {
            return null;
        }

        var parentCol = SchemaDiffService.ResolveReferencedColumn(
            item.Relationship,
            item.ParentEntity
        );

        if (parentCol is null)
        {
            return null;
        }

        var childCol = SchemaDiffService.ResolveFkColumnName(
            item.Relationship,
            item.ChildEntity,
            item.ParentEntity,
            parentCol
        );

        if (childCol is null)
        {
            return null;
        }

        return (childCol, SchemaDiffService.NormalizeTable(item.ParentEntity), parentCol.Name);
    }

    /// <summary>この種別が既存テーブルの再構築を要求するか（列型変更・列削除・FK 変更）</summary>
    private static bool IsRebuildTriggerKind(SchemaDiffKind kind) =>
        kind
            is SchemaDiffKind.AlterColumn
                or SchemaDiffKind.DropColumn
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AddForeignKey;

    /// <summary>制約名の安全化（"." と空白を "_" へ置換。<c>SqliteIdentifier.SafeName</c> と同一規則）</summary>
    private static string SafeName(string name) =>
        (name ?? string.Empty).Replace(".", "_").Replace(" ", "_");
}
