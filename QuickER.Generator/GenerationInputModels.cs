namespace QuickER.Generator;

/// <summary>
/// コード生成の入力となる ER 図定義
/// </summary>
/// <remarks>UI 層のモデルから切り離された生成専用の不変 DTO で、エンティティとリレーションのみを保持する</remarks>
public sealed class DiagramDefinition
{
    /// <summary>ER 図に含まれるエンティティ（テーブル）の一覧</summary>
    public IReadOnlyList<EntityDefinition> Entities { get; init; } = [];

    /// <summary>エンティティ間のリレーション一覧</summary>
    public IReadOnlyList<RelationshipDefinition> Relationships { get; init; } = [];
}

/// <summary>
/// エンティティ（テーブル）の定義
/// </summary>
public sealed class EntityDefinition
{
    /// <summary>エンティティの一意識別子。リレーションの参照解決に使う</summary>
    public Guid Id { get; init; }

    /// <summary>テーブル名。クラス名への変換元および [Table] 属性の値になる</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>テーブルに属するカラムの一覧</summary>
    public IReadOnlyList<ColumnDefinition> Columns { get; init; } = [];
}

/// <summary>
/// カラムの定義
/// </summary>
public sealed class ColumnDefinition
{
    /// <summary>カラムの一意識別子。リレーションのキー解決に使う</summary>
    public Guid Id { get; init; }

    /// <summary>カラム名。プロパティ名への変換元および [Column] 属性の値になる</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>SQL Server のデータ型表記（例: "nvarchar(50)"）。<see cref="SqlServerCSharpTypeMapper"/> で C# 型へ変換する</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>主キーかどうか。[Key] 属性の付与と Repository のキー型決定に使う</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>外部キーかどうか。リレーションのキー列が未指定の場合のフォールバック解決に使う</summary>
    public bool IsForeignKey { get; init; }

    /// <summary>NULL 許容かどうか。C# 型の Nullable 注釈と [Required] 属性の判定に使う</summary>
    public bool IsNullable { get; init; } = true;
}

/// <summary>
/// エンティティ間リレーションの定義
/// </summary>
/// <remarks>Source 側が principal（参照される側）、Target 側が dependent（FK を持つ側）に対応する</remarks>
public sealed class RelationshipDefinition
{
    /// <summary>リレーションの一意識別子。診断メッセージでの特定に使う</summary>
    public Guid Id { get; init; }

    /// <summary>principal 側（参照される側）エンティティの識別子</summary>
    public Guid SourceEntityId { get; init; }

    /// <summary>dependent 側（FK を持つ側）エンティティの識別子</summary>
    public Guid TargetEntityId { get; init; }

    /// <summary>リレーションの多重度</summary>
    public RelationshipMultiplicity Type { get; init; } = RelationshipMultiplicity.OneToMany;

    /// <summary>principal 側の参照キー列。null の場合は主キー列へフォールバックする</summary>
    public Guid? SourceColumnId { get; init; }

    /// <summary>dependent 側の FK 列。null の場合は IsForeignKey が立った最初の列へフォールバックする</summary>
    public Guid? TargetColumnId { get; init; }
}

/// <summary>
/// リレーションの多重度
/// </summary>
public enum RelationshipMultiplicity
{
    /// <summary>1対1。dependent 側は単一参照、principal 側も単一参照のナビゲーションになる</summary>
    OneToOne,

    /// <summary>1対多。principal 側はコレクション、dependent 側は単一参照のナビゲーションになる</summary>
    OneToMany,

    /// <summary>多対多。C# 生成では未対応のため警告付きでスキップされる</summary>
    ManyToMany,
}
