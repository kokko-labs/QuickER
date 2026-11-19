namespace QuickER.Provider.Sync.TableRebuilds;

/// <summary>
/// テーブル再構築の方言かの判定（<see cref="Requires"/>）と、既存の表の作り直しの理由になる差分の種類の判定（<see cref="IsTriggerKind"/>）
/// </summary>
internal static class TableRebuildDialects
{
    /// <summary>
    /// 方言がテーブル再構築を要るか（ALTER COLUMN も外部キーの後付けもできない方言＝
    /// <see cref="SyncDialectCapabilities.SupportsAlterColumn"/> か <see cref="SyncDialectCapabilities.SupportsForeignKeyAlter"/> が偽）
    /// </summary>
    internal static bool Requires(SyncDialectCapabilities capabilities) =>
        !capabilities.SupportsAlterColumn || !capabilities.SupportsForeignKeyAlter;

    /// <summary>
    /// この種別が既存テーブルの再構築を要求するか
    /// （列型変更・主キー変更・列削除・FK 変更・列順変更・一意制約の変更）
    /// </summary>
    /// <remarks>
    /// rebuild 方言（SQLite）では列順変更・主キー変更・一意制約の追加削除もテーブル再構築で実現するため、
    /// ReorderColumns / AlterPrimaryKey / Add・DropUniqueConstraint も再構築トリガーに含める
    /// （それだけが選択された場合も CreateOnly=false の再構築になる）。
    /// </remarks>
    internal static bool IsTriggerKind(SchemaDiffKind kind) =>
        kind
            is SchemaDiffKind.AlterColumn
                or SchemaDiffKind.AlterPrimaryKey
                or SchemaDiffKind.DropColumn
                or SchemaDiffKind.DropForeignKey
                or SchemaDiffKind.AddForeignKey
                or SchemaDiffKind.ReorderColumns
                or SchemaDiffKind.AddUniqueConstraint
                or SchemaDiffKind.DropUniqueConstraint;
}
