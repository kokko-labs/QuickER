using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;
using QuickER.Provider.Sync.State;

namespace QuickER.Provider.Sync.TableRebuilds;

/// <summary>
/// テーブル再構築の合成後の定義（live の表の写し）へ、選んだ項目を当てる 5 つの関数。当てる順は合成の入口
/// （<see cref="TableRebuildSynthesis"/>）が決める＝一意制約の削除 → 列の変更 → 主キーの変更 → 列順の変更 → 一意制約の追加
/// </summary>
internal static class TableRebuildDefinition
{
    /// <summary>合成後の定義へ、選択された列変更（AlterColumn の写し込み / DropColumn 除去 / AddColumn 追加）を適用する</summary>
    /// <remarks>
    /// <para>
    /// AlterColumn は live の列オブジェクトを残したまま、差分が語る性質（型・NULL 許容・計算列）だけを写す。
    /// 図側の列へ差し替えてはいけない。図は live と別の Guid 空間にあるため、差し替えると live の
    /// <see cref="Entity.UniqueConstraints"/> と <see cref="Entity.PrimaryKeyColumnIds"/> が指す列 ID が
    /// 解決できなくなり、一意制約が再構築後の表から黙って消え、複合主キーの順序も変わる。
    /// 図側の主キー指定も持ち込まれ、選んでいない主キー変更が紛れ込む。
    /// </para>
    /// <para>
    /// AddColumn の列は主キーにしない。主キーの膜と順序を変えるのは選択済みの AlterPrimaryKey だけ
    /// （<see cref="ApplyPrimaryKeyChange"/>）で、それが無いのに図側の主キー指定を持ち込むと、
    /// 選んでいない主キー変更が適用される。
    /// </para>
    /// </remarks>
    internal static void ApplyColumnChanges(Entity newDef, IReadOnlyList<SchemaDiffItem> tableItems)
    {
        // AlterColumn: 同名の live 列へ、型・NULL 許容・計算列だけを写す（Id・名前・主キー指定は live のまま）
        foreach (var alter in tableItems.Where(i => i.Kind == SchemaDiffKind.AlterColumn))
        {
            var column = newDef.Columns.Find(c => SchemaNames.AreSame(c.Name, alter.ColumnName));

            if (column is not null && alter.Column is not null)
            {
                column.DataType = alter.Column.DataType;
                column.IsNullable = ColumnNullability.AllowsNull(alter.Column);
                column.IsComputed = alter.Column.IsComputed;
            }
        }

        // DropColumn: 同名の列を除去する
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropColumn))
        {
            newDef.Columns.RemoveAll(c => SchemaNames.AreSame(c.Name, drop.ColumnName));
        }

        // AddColumn: 末尾へ畳み込む（このテーブルが他理由で再構築される場合のみここへ来る）。
        // 主キーにするかは AlterPrimaryKey の選択だけが決める
        foreach (var add in tableItems.Where(i => i.Kind == SchemaDiffKind.AddColumn))
        {
            if (add.Column is not null)
            {
                var added = add.Column.Clone(preserveId: true);

                // 主キーを外す前に実効の NULL 許容を写す（図の主キーの列は印に依らず NOT NULL で、逐次 DDL の方言の
                // 列の追加も NOT NULL で作る。印のまま主キーを外すと、再構築の表だけが NULL 許容の列になる）
                added.IsNullable = ColumnNullability.AllowsNull(add.Column);
                added.IsPrimaryKey = false;
                newDef.Columns.Add(added);
            }
        }
    }

    /// <summary>
    /// 選択された AlterPrimaryKey があれば、合成後の各列の主キー指定と主キーの順序を target の構成で上書きする。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上書きするのは <see cref="Column.IsPrimaryKey"/> と <see cref="Entity.PrimaryKeyColumnIds"/> のみで、
    /// 列の型・NULL 許容などは他差分の選択状況に従う
    /// （未選択の AlterColumn を主キー変更のついでに適用してしまわないため）。AlterPrimaryKey が未選択なら何もしない。
    /// </para>
    /// <para>
    /// target は live とは別の図＝列 Guid の空間が違うため、順序は target の実効順を列名で
    /// 合成後定義の列へ写像して作り直す（合成後定義に無い列名は読み飛ばす）。
    /// live 由来の順序リストをそのまま残すと、上書きした主キーの膜と順序が食い違う。
    /// </para>
    /// </remarks>
    internal static void ApplyPrimaryKeyChange(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        var alterPk = tableItems.FirstOrDefault(i => i.Kind == SchemaDiffKind.AlterPrimaryKey);

        if (alterPk?.Entity is null)
        {
            return;
        }

        var targetPkColumns = alterPk.Entity.GetPrimaryKeyColumnsInOrder();
        var pkNames = targetPkColumns.Select(c => c.Name).ToHashSet(SchemaNames.Comparer);

        foreach (var column in newDef.Columns)
        {
            column.IsPrimaryKey = pkNames.Contains(column.Name);
        }

        // target の実効順を列名で合成後定義の列 Guid へ写像し直す
        newDef.PrimaryKeyColumnIds = targetPkColumns
            .Select(pk => newDef.Columns.FirstOrDefault(c => SchemaNames.AreSame(c.Name, pk.Name)))
            .Where(c => c is not null)
            .Select(c => c!.Id)
            .ToList();
    }

    /// <summary>
    /// 選択された ReorderColumns があれば、合成後の列を target（差分項目の Entity）の列名順へ並べ替える。
    /// </summary>
    /// <remarks>
    /// target に存在する列を target の順序で先頭側へ並べ、target に無い列（未選択の削除で残った列）は
    /// 元の相対順序を保ったまま末尾へ回す（安定ソート）。ReorderColumns が未選択なら何もしない。
    /// </remarks>
    internal static void ApplyReorder(Entity newDef, IReadOnlyList<SchemaDiffItem> tableItems)
    {
        var reorder = tableItems.FirstOrDefault(i => i.Kind == SchemaDiffKind.ReorderColumns);

        if (reorder?.Entity is null)
        {
            return;
        }

        // target 列名 → 目標順インデックス
        var targetOrder = new Dictionary<string, int>(SchemaNames.Comparer);

        for (var i = 0; i < reorder.Entity.Columns.Count; i++)
        {
            targetOrder.TryAdd(reorder.Entity.Columns[i].Name, i);
        }

        // target にある列は目標順、無い列は元の相対順を保って末尾へ（OrderBy は安定ソート）
        var reordered = newDef
            .Columns.Select((column, index) => (column, index))
            .OrderBy(x => targetOrder.TryGetValue(x.column.Name, out var ti) ? ti : int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.column)
            .ToList();

        newDef.Columns.Clear();
        newDef.Columns.AddRange(reordered);
    }

    /// <summary>
    /// 選択された一意制約の削除を、合成後の定義（<paramref name="newDef"/>）の <see cref="Entity.UniqueConstraints"/>
    /// へ反映する。列の変更を当てる前に呼ぶ（構成列を live の列で引き当てるため）。
    /// </summary>
    /// <remarks>
    /// 合成後の定義は live の写しで、削除の項目も live の制約の名前を運ぶので、DB の一意制約の照合キー
    /// （<see cref="PlanIdentity.UniqueConstraintKey"/>＝名前と列集合）で除去する（同じ列集合のほかの一意制約は残す）。
    /// </remarks>
    internal static void ApplyUniqueConstraintDrops(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        foreach (var drop in tableItems.Where(i => i.Kind == SchemaDiffKind.DropUniqueConstraint))
        {
            var dropped = PostSyncSchema.ExplicitlyDroppedUniqueConstraints([drop]);
            newDef.UniqueConstraints.RemoveAll(constraint =>
                UniqueConstraintNaming.TryResolveColumnNames(newDef, constraint, out var columns)
                && dropped.Contains(
                    PlanIdentity.UniqueConstraintKey(drop.TableName, constraint.Name, columns)
                )
            );
        }
    }

    /// <summary>
    /// 選択された一意制約の追加を、合成後の定義（<paramref name="newDef"/>）の <see cref="Entity.UniqueConstraints"/>
    /// へ反映する。
    /// </summary>
    /// <remarks>
    /// 差分項目が運ぶ<b>列名</b>を合成後の列へ引き当てて新しい構成列 ID を作る
    /// （差分項目の Entity は図側＝列 ID が live のクローンと一致しないため、ID をそのまま持ち込めない）。
    /// 引き当てられない列を含む追加は黙って捨てる（DDL 生成が解決不能な制約を出力しないのと同じ流儀）。
    /// </remarks>
    internal static void ApplyUniqueConstraintAdds(
        Entity newDef,
        IReadOnlyList<SchemaDiffItem> tableItems
    )
    {
        foreach (var add in tableItems.Where(i => i.Kind == SchemaDiffKind.AddUniqueConstraint))
        {
            if (add.UniqueConstraintColumns.Count == 0)
            {
                continue;
            }

            var columnIds = new List<Guid>(add.UniqueConstraintColumns.Count);
            var allResolved = true;

            foreach (var columnName in add.UniqueConstraintColumns)
            {
                var column = newDef.Columns.FirstOrDefault(c =>
                    SchemaNames.AreSame(c.Name, columnName)
                );

                if (column is null)
                {
                    allResolved = false;
                    break;
                }

                columnIds.Add(column.Id);
            }

            if (!allResolved)
            {
                continue;
            }

            newDef.UniqueConstraints.Add(
                new UniqueConstraint { Name = add.UniqueConstraintName, ColumnIds = columnIds }
            );
        }
    }
}
