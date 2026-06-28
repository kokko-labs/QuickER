namespace QuickER.Models;

/// <summary>
/// テーブル内の 1 カラムを表すモデル
/// JSON シリアライズの対象となる単純な POCO
/// </summary>
public class Column
{
    /// <summary>カラムの一意識別子</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>カラム名（例: <c>顧客ID</c>）</summary>
    public string Name { get; set; } = "NewColumn";

    /// <summary>SQL のデータ型（例: <c>int</c>, <c>varchar(100)</c>）</summary>
    public string DataType { get; set; } = "int";

    /// <summary>主キーかどうかを示す</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>外部キーかどうかを示す</summary>
    public bool IsForeignKey { get; set; }

    /// <summary>NULL を許容するかどうかを示す</summary>
    public bool IsNullable { get; set; } = true;

    /// <summary>カラムの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>カラムを複製する</summary>
    /// <param name="preserveId"><c>true</c> の場合は同じ ID を維持し、<c>false</c> の場合は新しい ID を割り当てる</param>
    /// <returns>複製された <see cref="Column"/></returns>
    public Column Clone(bool preserveId) =>
        new()
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            Name = Name,
            DataType = DataType,
            IsPrimaryKey = IsPrimaryKey,
            IsForeignKey = IsForeignKey,
            IsNullable = IsNullable,
            Description = Description,
        };
}
