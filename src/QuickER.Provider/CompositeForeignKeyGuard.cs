using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Model;

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

    /// <summary>この外部キーが、取込で列対応を失った複合外部キーそのものか</summary>
    /// <param name="childTable">外部キーを保有する側（子）のテーブル名</param>
    /// <param name="constraintName">外部キー制約名（live リレーションの <see cref="Relationship.ConstraintName"/>）</param>
    /// <param name="warnings">取込で得た複合外部キーの警告一覧</param>
    /// <remarks>
    /// <para>
    /// 照合は<b>子テーブル名＋制約名</b>で行う。取込警告と live リレーションはどちらも同じ取込結果に由来し、
    /// 制約名は同一の文字列（SQLite のように制約名を持たない方言では同じ規則で合成した名前）が入るため、
    /// この 2 つが最も確実に同一の外部キーを指す。列での照合は使わない——複合外部キーは列対応を失っており、
    /// 子側の列は <see cref="SchemaDiffService.ResolveFkColumnName"/> の <c>IsForeignKey</c> フォールバックで
    /// 決まる（＝構成列のうちどれが選ばれるか、あるいは無関係な列が選ばれるかを当てにできない）ため。
    /// </para>
    /// <para>
    /// 制約名が空の外部キー（取込由来なら必ず入るため実質は手組みの入力）は、複合外部キーである可能性を
    /// 否定できないため、その子テーブルに複合外部キーの警告があれば安全側で「複合」とみなす。
    /// </para>
    /// </remarks>
    public static bool IsCompositeForeignKey(
        string childTable,
        string? constraintName,
        IReadOnlyList<CompositeForeignKeyImportWarning> warnings
    )
    {
        var table = (childTable ?? string.Empty).Trim();
        var name = (constraintName ?? string.Empty).Trim();

        foreach (var warning in warnings)
        {
            if (!NameComparer.Equals(warning.ChildTable.Trim(), table))
            {
                continue;
            }

            if (name.Length == 0 || NameComparer.Equals(warning.ConstraintName.Trim(), name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// live スキーマから、複合外部キーの作り直しを招く変更の照合範囲を組み立てる。
    /// </summary>
    /// <remarks>
    /// 外部キーの列解決は <see cref="SyncPlanner"/> の自動 DROP → 再 ADD と同じ列挙
    /// （<see cref="SyncPlanner.EnumerateLiveForeignKeys"/>）を使う。「作り直しの対象になる条件」と
    /// 「作り直しを止める条件」が同じ解決規則で動かないと、片方だけがすり抜けるため。
    /// </remarks>
    public static CompositeForeignKeySyncScope BuildSyncScope(SyncPlanContext context)
    {
        if (context.CompositeForeignKeyWarnings.Count == 0)
        {
            return CompositeForeignKeySyncScope.Empty;
        }

        var referencedTables = new HashSet<string>(NameComparer);
        var involvedColumns = new HashSet<string>(NameComparer);

        foreach (var fk in SyncPlanner.EnumerateLiveForeignKeys(context))
        {
            var childTable = SchemaDiffService.NormalizeTable(fk.Child);

            if (
                !IsCompositeForeignKey(
                    childTable,
                    fk.Relationship.ConstraintName,
                    context.CompositeForeignKeyWarnings
                )
            )
            {
                continue;
            }

            var parentTable = SchemaDiffService.NormalizeTable(fk.Parent);

            // 親テーブルの主キーが変わると、この複合外部キーは方言を問わず一旦外して作り直される
            referencedTables.Add(parentTable);

            // 解決済みの子側・親側の列は、その定義変更が外部キーの作り直しを招く（capability 依存）
            involvedColumns.Add(CompositeForeignKeySyncScope.ColumnKey(childTable, fk.ChildColumn));
            involvedColumns.Add(
                CompositeForeignKeySyncScope.ColumnKey(parentTable, fk.ParentColumn.Name)
            );
        }

        return new CompositeForeignKeySyncScope(referencedTables, involvedColumns);
    }

    /// <summary>
    /// この差分項目が、複合外部キーの作り直しを招くため同期できない変更か。
    /// </summary>
    /// <param name="item">判定する差分項目</param>
    /// <param name="capabilities">対象方言の同期ケーパビリティ</param>
    /// <param name="scope">live スキーマから組み立てた照合範囲（<see cref="BuildSyncScope"/>）</param>
    /// <remarks>
    /// <para>
    /// 対象は (a) 複合外部キーが参照している親テーブルの主キー変更（全方言で外部キーの作り直しを伴う）と、
    /// (b) 複合外部キーが関与する列の定義変更（<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/>
    /// が <c>true</c> の方言のみ）。どちらも列対応を失った定義で外部キーが作り直され、単列外部キーへ置き換わる。
    /// </para>
    /// <para>
    /// 判定対象は逐次 DDL 方言のみ。テーブル再構築方言（SQLite）は主キー変更で子テーブルの外部キーを
    /// 作り直さないため、この経路の危険が無い（その方言固有の危険は再構築のブロックで別途止めている）。
    /// </para>
    /// </remarks>
    public static bool IsBlockedChange(
        SchemaDiffItem item,
        SyncDialectCapabilities capabilities,
        CompositeForeignKeySyncScope scope
    )
    {
        if (scope.IsEmpty)
        {
            return false;
        }

        var isRebuildDialect =
            !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

        if (isRebuildDialect)
        {
            return false;
        }

        return item.Kind switch
        {
            SchemaDiffKind.AlterPrimaryKey => scope.BlocksPrimaryKeyChange(item.TableName),
            SchemaDiffKind.AlterColumn => capabilities.AlterColumnRequiresForeignKeyRebuild
                && item.ColumnName is not null
                && scope.BlocksColumnChange(item.TableName, item.ColumnName),
            _ => false,
        };
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

/// <summary>
/// 複合外部キーの作り直しを招く変更を見分けるための照合範囲（live スキーマから 1 度だけ組み立てる）。
/// </summary>
/// <remarks>
/// 計画側（<see cref="SyncPlanner"/> の除外）と表示側（同期ダイアログの選択不可化）が同じ判定を使うため、
/// live 外部キーの列挙結果をここへ畳んで両者へ渡す（<see cref="CompositeForeignKeyGuard.BuildSyncScope"/>）。
/// </remarks>
public sealed class CompositeForeignKeySyncScope
{
    /// <summary>テーブル名・列名の比較規則（大文字小文字を無視する）</summary>
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>複合外部キーが参照している親テーブル（主キーを変えると外部キーの作り直しを招く）</summary>
    private readonly HashSet<string> _referencedTables;

    /// <summary>複合外部キーが関与する列（<c>テーブル|列</c>。定義を変えると外部キーの作り直しを招く）</summary>
    private readonly HashSet<string> _involvedColumns;

    internal CompositeForeignKeySyncScope(
        HashSet<string> referencedTables,
        HashSet<string> involvedColumns
    )
    {
        _referencedTables = referencedTables;
        _involvedColumns = involvedColumns;
    }

    /// <summary>複合外部キーが 1 件も無い（＝何も止めない）範囲</summary>
    public static CompositeForeignKeySyncScope Empty { get; } =
        new(new HashSet<string>(NameComparer), new HashSet<string>(NameComparer));

    /// <summary>止める対象が 1 件も無いか</summary>
    public bool IsEmpty => _referencedTables.Count == 0 && _involvedColumns.Count == 0;

    /// <summary>このテーブルの主キー変更が複合外部キーの作り直しを招くか</summary>
    public bool BlocksPrimaryKeyChange(string tableName) =>
        _referencedTables.Contains((tableName ?? string.Empty).Trim());

    /// <summary>この列の定義変更が複合外部キーの作り直しを招くか</summary>
    public bool BlocksColumnChange(string tableName, string columnName) =>
        _involvedColumns.Contains(ColumnKey(tableName, columnName));

    /// <summary>テーブル・列の照合キー（前後空白を無視する。大文字小文字は比較子が無視する）</summary>
    internal static string ColumnKey(string table, string column) =>
        $"{(table ?? string.Empty).Trim()}|{(column ?? string.Empty).Trim()}";
}
