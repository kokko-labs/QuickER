using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Provider;

/// <summary>
/// 複合外部キー（取込で列対応が失われた外部キー）に関与する同期操作を見分けるための照合ヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// 意味モデルは複合外部キーを単一の列ペアへ劣化させて取り込む（<see cref="CompositeForeignKeyImportWarning"/>）。
/// そのため同期でその外部キーを作り直すと「複合外部キーが単列外部キーへ置き換わる」＝成功して静かに壊れる。
/// </para>
/// <para>
/// 計画側（テーブル再構築のブロック＝<see cref="SyncPlanner"/>）と表示側（外部キー差分の選択不可化）で
/// 同じ照合規則を使うため、判定をここへ 1 本化する。テーブル名・列名の比較は大文字小文字と前後空白を無視する
/// （取込警告のテーブル名は差分項目の <see cref="SchemaDiffItem.TableName"/> と同じ正規化形＝
/// <see cref="SchemaDiffService.NormalizeTable"/> の出力と同一の書式で作られる）。
/// </para>
/// </remarks>
public static class CompositeForeignKeyGuard
{
    /// <summary>テーブル名・列名の比較規則（大文字小文字を無視する）</summary>
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>このテーブルが複合外部キーを保有する子テーブルか</summary>
    /// <param name="tableName">判定するテーブル名</param>
    /// <param name="warnings">取込で得た複合外部キーの警告一覧</param>
    public static bool IsCompositeChildTable(
        string tableName,
        IReadOnlyList<CompositeForeignKeyImportWarning> warnings
    )
    {
        var table = (tableName ?? string.Empty).Trim();
        return warnings.Any(w => NameComparer.Equals(w.ChildTable.Trim(), table));
    }

    /// <summary>
    /// この差分項目が、複合外部キーに関与する外部キー操作か（外部キー以外の種別は常に <c>false</c>）。
    /// </summary>
    /// <remarks>
    /// 子テーブルが一致し、かつ子側の列が複合外部キーの構成列に含まれるものだけを「関与」とみなす
    /// （同じテーブルの無関係な単列外部キーまで巻き込まないため）。子列を解決できない差分は、
    /// 複合外部キー由来である可能性を否定できないため安全側で「関与」とみなす。
    /// </remarks>
    public static bool IsAffectedForeignKeyDiff(
        SchemaDiffItem item,
        IReadOnlyList<CompositeForeignKeyImportWarning> warnings
    )
    {
        if (item.Kind is not (SchemaDiffKind.AddForeignKey or SchemaDiffKind.DropForeignKey))
        {
            return false;
        }

        var childTable = (
            item.ChildEntity is not null
                ? SchemaDiffService.NormalizeTable(item.ChildEntity)
                : item.TableName
        ).Trim();
        var childColumn = ResolveChildColumn(item);

        foreach (var warning in warnings)
        {
            if (!NameComparer.Equals(warning.ChildTable.Trim(), childTable))
            {
                continue;
            }

            if (
                childColumn is null
                || warning.ChildColumns.Any(c => NameComparer.Equals(c.Trim(), childColumn))
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>差分項目から子側の外部キー列名を解決する（解決できなければ null）</summary>
    /// <remarks>
    /// <see cref="SchemaDiffKind.AddForeignKey"/> は差分生成時に列名が入るが、
    /// <see cref="SchemaDiffKind.DropForeignKey"/> は入らないため、差分計算と同じ規則で解決し直す。
    /// </remarks>
    private static string? ResolveChildColumn(SchemaDiffItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ColumnName))
        {
            return item.ColumnName!.Trim();
        }

        if (item.Relationship is null || item.ParentEntity is null || item.ChildEntity is null)
        {
            return null;
        }

        var parentColumn = SchemaDiffService.ResolveReferencedColumn(
            item.Relationship,
            item.ParentEntity
        );

        if (parentColumn is null)
        {
            return null;
        }

        return SchemaDiffService
            .ResolveFkColumnName(
                item.Relationship,
                item.ChildEntity,
                item.ParentEntity,
                parentColumn
            )
            ?.Trim();
    }
}
