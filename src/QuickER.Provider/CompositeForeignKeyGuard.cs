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
    /// 取込警告から、複合外部キーの作り直しを招く変更の照合範囲を組み立てる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 照合範囲の正本は<b>取込警告そのもの</b>（<see cref="CompositeForeignKeyImportWarning"/>）で、
    /// live のリレーション列挙は使わない。警告は取込時の実 FK メタデータから作られ、全構成列
    /// （<see cref="CompositeForeignKeyImportWarning.ChildColumns"/> /
    /// <see cref="CompositeForeignKeyImportWarning.ParentColumns"/>）を保持しているのに対し、
    /// live 列挙は意味モデルへ劣化した後の<b>1 組の列</b>しか復元できない（＝副構成列を取りこぼす）。
    /// 列の解決に失敗して列挙から落ちる複合外部キーも、警告からなら確実に拾える。
    /// </para>
    /// <para>
    /// 構成列は 1 列でも定義が変われば外部キー全体が外して作り直される（＝列対応を失った定義で
    /// 単列外部キーへ置き換わる）ため、副構成列も含めて全て登録する。過剰ブロックにはならない——
    /// この判定が効く SQL Server（<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/>
    /// が <c>true</c>）では複合外部キーの構成列はどれも外部キーを外さずに変更できず（Msg 5074）、
    /// 止めなければ実行時に必ず失敗する。説明のない恒久的な失敗を、明示的な警告へ置き換えている。
    /// </para>
    /// </remarks>
    public static CompositeForeignKeySyncScope BuildSyncScope(SyncPlanContext context)
    {
        if (context.CompositeForeignKeyWarnings.Count == 0)
        {
            return CompositeForeignKeySyncScope.Empty;
        }

        var referencedTables = new HashSet<string>(NameComparer);
        var involvedColumns = new HashSet<string>(NameComparer);

        foreach (var warning in context.CompositeForeignKeyWarnings)
        {
            // 警告のテーブル名は差分項目の TableName と同じ正規化形（SchemaDiffService.NormalizeTable の出力＝Trim）
            var childTable = warning.ChildTable.Trim();
            var parentTable = warning.ParentTable.Trim();

            // 親テーブルの主キーが変わると、この複合外部キーは方言を問わず一旦外して作り直される
            referencedTables.Add(parentTable);

            // 子側・親側の全構成列を登録する（副構成列の変更も同じ作り直しを招くため）
            foreach (var column in warning.ChildColumns)
            {
                involvedColumns.Add(CompositeForeignKeySyncScope.ColumnKey(childTable, column));
            }

            foreach (var column in warning.ParentColumns)
            {
                involvedColumns.Add(CompositeForeignKeySyncScope.ColumnKey(parentTable, column));
            }
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
    /// (b) 複合外部キーの<b>全構成列</b>（子側・親側とも、第 2 列以降も含む）の定義変更
    /// （<see cref="SyncDialectCapabilities.AlterColumnRequiresForeignKeyRebuild"/> が <c>true</c> の方言のみ）。
    /// どちらも列対応を失った定義で外部キーが作り直され、単列外部キーへ置き換わる。
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
/// 複合外部キーの作り直しを招く変更を見分けるための照合範囲（取込警告から 1 度だけ組み立てる）。
/// </summary>
/// <remarks>
/// 計画側（<see cref="SyncPlanner"/> の除外）と表示側（同期ダイアログの選択不可化）が同じ判定を使うため、
/// 取込警告を畳んだ結果をここへ持たせて両者へ渡す（<see cref="CompositeForeignKeyGuard.BuildSyncScope"/>）。
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
