namespace QuickER.Provider;

/// <summary>
/// 列順同期（列の並び替え）を DB 方言がどう実現できるかを表す区分。
/// </summary>
public enum ColumnReorderMode
{
    /// <summary>列順の同期は非対応（差分検知時は案内表示のみ）。</summary>
    None,

    /// <summary><c>ALTER TABLE ... MODIFY ... AFTER</c> など、逐次 DDL で直接並び替えられる（MySQL）。</summary>
    Native,

    /// <summary>テーブル再構築（新テーブル作成 → データ移送 → 入替）が必要（SQLite）。</summary>
    Rebuild,
}

/// <summary>
/// DB 方言がスキーマ同期でどこまで表現できるかを宣言するケーパビリティ。
/// </summary>
/// <remarks>
/// <para>
/// 将来フェーズで <see cref="SyncPlanner"/> がこの宣言を参照し、逐次 DDL で表現できない変更
/// （SQLite のテーブル再構築・MySQL のネイティブ列順変更など）をセクションへ振り分ける。
/// Phase 1 では契約定義と各プロバイダからの配線のみで、プランナーはまだ参照しない。
/// </para>
/// </remarks>
public sealed record SyncDialectCapabilities
{
    /// <summary><c>ALTER COLUMN</c>（型 / NULL 制約の変更）を逐次 DDL で実行できるか。</summary>
    public bool SupportsAlterColumn { get; init; } = true;

    /// <summary>外部キーの <c>ADD</c> / <c>DROP CONSTRAINT</c> を逐次 DDL で実行できるか。</summary>
    public bool SupportsForeignKeyAlter { get; init; } = true;

    /// <summary>テーブル / 列コメント（説明）を設定する機構を持つか。</summary>
    public bool SupportsDescriptions { get; init; } = true;

    /// <summary>外部キー制約名が DB に永続化されるか（SQLite は制約名が合成名で永続化されない）。</summary>
    public bool PersistsForeignKeyConstraintNames { get; init; } = true;

    /// <summary>列順同期の実現方式。</summary>
    public ColumnReorderMode ColumnReorder { get; init; } = ColumnReorderMode.None;
}
