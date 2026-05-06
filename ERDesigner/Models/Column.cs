namespace ERDesigner.Models;

/// <summary>
/// テーブル内の 1 列（カラム）を表すモデルクラスです。
/// JSON で保存されるシンプルな POCO（Plain Old CLR Object）です。
/// </summary>
public class Column
{
    /// <summary>カラムを一意に識別する ID です。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>カラム名（例: <c>顧客ID</c>）。</summary>
    public string Name { get; set; } = "NewColumn";

    /// <summary>SQL のデータ型（例: <c>int</c>, <c>varchar(100)</c>）。</summary>
    public string DataType { get; set; } = "int";

    /// <summary>主キー（Primary Key）かどうかを示します。</summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>外部キー（Foreign Key）かどうかを示します。</summary>
    public bool IsForeignKey { get; set; }

    /// <summary>NULL を許容するかどうかを示します。</summary>
    public bool IsNullable { get; set; } = true;

    /// <summary>
    /// カラムの説明 (SQL Server の拡張プロパティ <c>MS_Description</c> と同期します)。
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
