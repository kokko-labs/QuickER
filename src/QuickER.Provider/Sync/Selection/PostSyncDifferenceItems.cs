using QuickER.Model;
using QuickER.Provider.Resources;
using QuickER.Provider.Sync.Assessment;
using QuickER.Provider.Sync.State;
using QuickER.Provider.Sync.Steps;
using QuickER.Provider.Sync.TableRebuilds;

namespace QuickER.Provider.Sync.Selection;

/// <summary>
/// 同期の計画の選択の前半で、除外の周と作り直しの展開の後に、選んでいなくても同期後の状態の差として足す項目
/// （主キーの列の NOT NULL 化・端が消える外部キーの削除・構成列が消える一意制約の削除・主キーが置き換える一意制約の削除）
/// </summary>
/// <remarks>
/// 呼ぶのは <see cref="PlanSelection.PrepareSelection"/> で、この順に足す（各関数は自分が足す前の並びを見る）。
/// </remarks>
internal static class PostSyncDifferenceItems
{
    /// <summary>
    /// 選んだ主キーの変更で新しく主キーに加わる列のうち、live で NULL 許容のものを、暗黙の
    /// <see cref="SchemaDiffKind.AlterColumn"/>（live の型のまま NOT NULL）として足す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 主キーの列は NOT NULL でなければならず、図でも主キーの列は NOT NULL になっている。主キーの変更を選んだ時点で
    /// その意図に含まれるので、列の NOT NULL 化（差分では別の項目＝既定で未選択）を選んでいなくても行う。
    /// 選んでいないと、逐次 DDL の方言では主キーの付与が NULL 許容の列で失敗した。
    /// </para>
    /// <para>
    /// 型は live のまま（選んでいない型の変更は持ち込まない）。同じ列の変更を利用者が選んでいれば足さない
    /// （その項目が図の NOT NULL を運ぶ）。live に無い列（同じ計画で足す列）は追加の定義が運ぶので対象外。
    /// 足した項目は以降の段から選んだ列の変更と同じに見える（逐次 DDL では列の変更のセクション・
    /// SQLite では再構築の合成）。
    /// </para>
    /// </remarks>
    internal static List<SchemaDiffItem> InjectPrimaryKeyNotNullChanges(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema
    )
    {
        if (liveSchema is null)
        {
            return selected;
        }

        var liveByName = liveSchema.Tables;
        var injected = new List<SchemaDiffItem>();

        foreach (
            var change in selected.Where(i =>
                i.Kind == SchemaDiffKind.AlterPrimaryKey && i.Entity is not null
            )
        )
        {
            var table = SchemaNames.ToStoredForm(change.TableName);

            if (!liveByName.TryGet(table, out var live))
            {
                continue;
            }

            foreach (var keyColumn in change.Entity!.GetPrimaryKeyColumnsInOrder())
            {
                var liveColumn = live.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, keyColumn.Name)
                );

                if (
                    liveColumn is null
                    || !ColumnNullability.AllowsNull(liveColumn)
                    || selected.Any(i =>
                        i.Kind == SchemaDiffKind.AlterColumn
                        && SchemaNames.AreSame(SchemaNames.ToStoredForm(i.TableName), table)
                        && SchemaNames.AreSame(i.ColumnName, liveColumn.Name)
                    )
                )
                {
                    continue;
                }

                var notNull = liveColumn.Clone(preserveId: true);
                notNull.IsNullable = false;
                injected.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.AlterColumn,
                        TableName = table,
                        ColumnName = liveColumn.Name,
                        Entity = live,
                        Column = notNull,
                        OldColumn = liveColumn,
                        Description = string.Format(
                            Strings.Diff_AutoPrimaryKeyNotNull,
                            table,
                            liveColumn.Name
                        ),
                        PlanMark = PlanItemMark.Implicit(
                            ImplicitStepReason.PrimaryKeyColumnNotNull
                        ),
                    }
                );
            }
        }

        return injected.Count == 0 ? selected : [.. selected, .. injected];
    }

    /// <summary>
    /// 選んだ表・列の削除（<see cref="SchemaDiffKind.DropTable"/> / <see cref="SchemaDiffKind.DropColumn"/>）で、
    /// 子表・親表・構成列のどれかが消える live の外部キーを、暗黙の <see cref="SchemaDiffKind.DropForeignKey"/>
    /// として先頭へ足す（付け直さない）。
    /// </summary>
    /// <param name="selected">選択済みの項目（外部キーの作り直しは展開済み）</param>
    /// <param name="liveSchema">live スキーマ（無ければ何もしない）</param>
    /// <param name="includeDroppedChildTables">
    /// 子表そのものを消す外部キーも外すか。逐次 DDL の方言では外す（外部キーの削除は表の削除より前のセクションに
    /// 並ぶので、親と子を一緒に消すときに親を先に消して失敗する順序の問題も消える）。再構築の方言（SQLite）では
    /// 外さない（消す表を作り直す意味が無く、表の削除が外部キーごと消す）。
    /// </param>
    /// <param name="capabilities">方言のケーパビリティ（外部キーの実効名を求める識別子の上限）</param>
    /// <remarks>
    /// <para>
    /// 端が消える外部キーは同期後に残れないので、差分の外部キーの削除（既定で未選択）を選んでいなくても外す。
    /// 外さないと、逐次 DDL の方言では参照されている表・列の削除が拒否され（DDL が暗黙コミットされる方言では
    /// その前の文が適用されたまま止まる）、SQLite では存在しない表・列を指す外部キーが残る（行があれば
    /// <c>foreign_key_check</c> で失敗し、無ければ黙って壊れた外部キーが残る）。
    /// </para>
    /// <para>
    /// 利用者が同じ外部キーの削除を選んでいれば足さない。足した項目はそれ以降の段（暗黙の外し直し・候補キーの警告・
    /// 再構築の外部キーの合成）から、明示的に外す外部キーと同じに見える。SQLite では子表の再構築の項目として
    /// 畳まれ、作り直した表から外部キーが外れる。
    /// </para>
    /// </remarks>
    internal static List<SchemaDiffItem> InjectVanishingForeignKeyDrops(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema,
        bool includeDroppedChildTables,
        SyncDialectCapabilities capabilities
    )
    {
        var limit = capabilities.IdentifierLengthLimit;

        if (liveSchema is null)
        {
            return selected;
        }

        var postSync = PostSyncSchema.From(PlanStage.Expanded, selected, liveSchema, capabilities);
        var dropped = postSync.Dropped;

        if (dropped.IsEmpty)
        {
            return selected;
        }

        var explicitlyDropped = postSync.DroppedForeignKeys;
        var drops = new List<SchemaDiffItem>();

        foreach (var fk in liveSchema.ForeignKeys)
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);
            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            if (
                !dropped.Touches(fk, childTable, parentTable)
                || (dropped.Tables.Contains(childTable) && !includeDroppedChildTables)
                || explicitlyDropped.Contains(
                    PlanIdentity.ForeignKeyKey(
                        childTable,
                        parentTable,
                        fk.ColumnPairs,
                        fk.Relationship,
                        limit
                    )
                )
            )
            {
                continue;
            }

            drops.Add(
                BuildImplicitForeignKeyDrop(
                    fk,
                    childTable,
                    string.Format(
                        Strings.Diff_AutoForeignKeyDrop,
                        PlanIdentity.ResolveForeignKeyName(
                            fk.Relationship,
                            childTable,
                            parentTable,
                            limit,
                            liveSchema
                        )
                    ),
                    PlanItemMark.Implicit(ImplicitStepReason.VanishingForeignKey)
                )
            );
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 選んだ列の削除で構成列が消える live の一意制約を、暗黙の <see cref="SchemaDiffKind.DropUniqueConstraint"/> として
    /// 先頭へ足す（付け直さない）。名指し（<see cref="SyncPlanWarningKind.UniqueConstraintDroppedWithColumn"/>）は評価の規則
    /// <see cref="AssessmentRules.WarnUniqueConstraintsDroppedWithColumns"/> が、足した項目から作る。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 構成列が消える一意制約は同期後に残れないが、差分の一意制約の削除は既定で未選択なので、列の削除だけを選ぶと
    /// 方言で結果が割れる（SQL Server は削除が断られる・Oracle は複数列の制約で断られる・PostgreSQL は黙って消す・
    /// MySQL は残りの列の別の制約へ縮める・SQLite の再構築は黙って消す）。そこで外部キーの消える端と同じく暗黙に外す。
    /// 外部キーの消える端と違って警告を出すのは、外部キーは参照する列が消えれば意味を失うのに対し、一意制約は残りの列で
    /// 作り直せる（その判断を利用者に委ねる）ため。
    /// </para>
    /// <para>
    /// 表を消す計画の表は対象外（表の削除が制約ごと消す）。利用者が同じ一意制約の削除を選んでいれば足さない。
    /// 足した項目はそれ以降の段（暗黙の外し直し・候補キーの警告・索引の喪失・再構築の合成）から、明示的に外す一意制約と
    /// 同じに見える。
    /// </para>
    /// </remarks>
    internal static List<SchemaDiffItem> InjectVanishingUniqueConstraintDrops(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema,
        SyncDialectCapabilities capabilities
    )
    {
        if (liveSchema is null)
        {
            return selected;
        }

        var postSync = PostSyncSchema.From(
            PlanStage.WithVanishingForeignKeyDrops,
            selected,
            liveSchema,
            capabilities
        );
        var dropped = postSync.Dropped;

        if (dropped.Columns.Count == 0)
        {
            return selected;
        }

        var explicitlyDropped = postSync.DroppedUniqueConstraints;
        var drops = new List<SchemaDiffItem>();

        foreach (var entity in liveSchema.Entities)
        {
            var table = SchemaDiffService.NormalizeTable(entity);

            if (dropped.Tables.Contains(table))
            {
                continue;
            }

            foreach (var constraint in entity.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(
                        entity,
                        constraint,
                        out var columns
                    )
                    || !dropped.TouchesColumns(table, columns)
                    || explicitlyDropped.Contains(
                        PlanIdentity.UniqueConstraintKey(table, constraint.Name, columns)
                    )
                )
                {
                    continue;
                }

                drops.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropUniqueConstraint,
                        TableName = table,
                        Entity = entity,
                        UniqueConstraintName = constraint.Name,
                        UniqueConstraintColumns = columns,
                        Description = string.Format(
                            Strings.Diff_AutoUniqueConstraintDrop,
                            string.Join(", ", columns)
                        ),
                        PlanMark = PlanItemMark.Implicit(
                            ImplicitStepReason.VanishingUniqueConstraint
                        ),
                    }
                );
            }
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// 選んだ主キーの変更の新しい主キーと同じ列集合の live の一意制約を、暗黙の <see cref="SchemaDiffKind.DropUniqueConstraint"/>
    /// として先頭へ足す（付け直さない）。名指し（<see cref="SyncPlanWarningKind.UniqueConstraintReplacedByPrimaryKey"/>）は評価の規則
    /// <see cref="AssessmentRules.WarnUniqueConstraintsReplacedByPrimaryKey"/> が、足した項目から作る。
    /// 主キーと同じ列集合の一意制約を持てない逐次 DDL の方言（Oracle）でだけ呼ぶ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Oracle は同じ列に主キーと一意制約を両方は持てず、残すと主キーの付与が ORA-02261 で断られ、DDL が暗黙コミットされるので
    /// 旧主キーを外したまま主キーが消えた。差分は図の主キーと同じ列集合の一意制約を比較から外し、DB 側の削除も出さないので、
    /// 利用者は一意制約の削除を選べない。値の一意性は新しい主キーが保ち、図からの DDL もその一意制約を省くので、同期の後に差分は
    /// 残らない。利用者が同じ一意制約の削除を選んでいれば足さない。
    /// </para>
    /// <para>
    /// その一意制約を参照する外部キーは、主キーの変更の外し直し（外し直しの宣言
    /// <c>RebuildDeclarations.RequireForeignKeysOffUniquesReplacedByPrimaryKey</c>）が外して、新しい主キーへ付け直す
    /// （外さないと一意制約の削除が断られる）。
    /// </para>
    /// </remarks>
    internal static List<SchemaDiffItem> InjectUniqueConstraintsReplacedByPrimaryKey(
        List<SchemaDiffItem> selected,
        LiveSchema? liveSchema,
        SyncDialectCapabilities capabilities
    )
    {
        if (liveSchema is null)
        {
            return selected;
        }

        var postSync = PostSyncSchema.From(
            PlanStage.WithVanishingUniqueDrops,
            selected,
            liveSchema,
            capabilities
        );
        var pkChangedTables = postSync.PrimaryKeyChanges;

        if (pkChangedTables.Count == 0)
        {
            return selected;
        }

        var explicitlyDropped = postSync.DroppedUniqueConstraints;
        var liveByName = liveSchema.Tables;
        var drops = new List<SchemaDiffItem>();

        foreach (var (table, newKey) in pkChangedTables)
        {
            if (newKey.Count == 0)
            {
                continue;
            }

            if (!liveByName.TryGet(table, out var live))
            {
                continue;
            }

            foreach (var constraint in live.UniqueConstraints)
            {
                if (
                    !UniqueConstraintNaming.TryResolveColumnNames(live, constraint, out var columns)
                    || !IsReplacedByPrimaryKey(postSync, table, columns)
                    || explicitlyDropped.Contains(
                        PlanIdentity.UniqueConstraintKey(table, constraint.Name, columns)
                    )
                )
                {
                    continue;
                }

                drops.Add(
                    new SchemaDiffItem
                    {
                        Kind = SchemaDiffKind.DropUniqueConstraint,
                        TableName = table,
                        Entity = live,
                        UniqueConstraintName = constraint.Name,
                        UniqueConstraintColumns = columns,
                        Description = string.Format(
                            Strings.Diff_AutoUniqueConstraintReplacedByPrimaryKey,
                            string.Join(", ", columns)
                        ),
                        PlanMark = PlanItemMark.Implicit(
                            ImplicitStepReason.UniqueConstraintReplacedByPrimaryKey
                        ),
                    }
                );
            }
        }

        return drops.Count == 0 ? selected : [.. drops, .. selected];
    }

    /// <summary>
    /// この方言で、選んだ主キーの変更が新しい主キーと同じ列集合の live の一意制約を暗黙に外すか（主キーと同じ列集合の一意制約を持てない
    /// 逐次 DDL の方言＝Oracle。再構築の方言は合成がその一意制約を省く）
    /// </summary>
    /// <param name="capabilities">方言の宣言</param>
    internal static bool ReplacesUniqueConstraintsWithPrimaryKey(
        SyncDialectCapabilities capabilities
    ) =>
        !TableRebuildDialects.Requires(capabilities)
        && !capabilities.SupportsUniqueConstraintOnPrimaryKey;

    /// <summary>
    /// この表の、この列集合の live の一意制約を、選んだ主キーの変更の新しい主キーが置き換えるか（新しい主キーと同じ列集合。
    /// <see cref="ReplacesUniqueConstraintsWithPrimaryKey"/> の方言でだけ意味を持つ）
    /// </summary>
    /// <param name="postSync">選んだ項目の同期後の事実（主キーの変更はどの時点の並びから求めても同じ）</param>
    /// <param name="table">正規化した表名</param>
    /// <param name="columns">一意制約の構成列</param>
    /// <remarks>
    /// 暗黙の削除を足す <see cref="InjectUniqueConstraintsReplacedByPrimaryKey"/> と、同期後も残る DB の制約
    /// （<see cref="RemainingLiveConstraints"/>＝この一意制約を残る側に数えない）が共有する。写すと、暗黙に外す一意制約を残る側に数え、
    /// 同じ名前の一意制約・外部キーの追加を偽の理由で外す。
    /// </remarks>
    internal static bool IsReplacedByPrimaryKey(
        PostSyncSchema postSync,
        string table,
        IReadOnlyList<string> columns
    ) =>
        postSync.PrimaryKeyChanges.TryGetValue(table, out var newKey)
        && newKey.Count > 0
        && PlanFacts.IsSameColumnSet(columns, newKey);

    /// <summary>live の外部キーを外す暗黙の DropForeignKey 項目を作る（差分計算の FK 差分と同じフィールド規則）</summary>
    private static SchemaDiffItem BuildImplicitForeignKeyDrop(
        ResolvedForeignKeyRelationship fk,
        string childTable,
        string description,
        PlanItemMark mark
    ) =>
        new()
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = childTable,
            Entity = fk.Child,
            ParentEntity = fk.Parent,
            ChildEntity = fk.Child,
            Relationship = fk.Relationship,
            ForeignKeyName = fk.Relationship.ConstraintName,
            ForeignKeyColumnPairs = fk.ColumnPairs,
            Description = description,
            PlanMark = mark,
        };
}
