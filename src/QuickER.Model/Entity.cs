using System.Collections.Generic;

namespace QuickER.Model;

/// <summary>
/// ER 図上のテーブル（エンティティ）1 件を表すモデル
/// JSON シリアライズの対象
/// </summary>
public class Entity
{
    /// <summary>エンティティの一意識別子（<see cref="Relationship"/> からの参照に使用する）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>物理テーブル名（例: <c>Customer</c>）</summary>
    public string TableName { get; set; } = "NewTable";

    /// <summary>備考メモ（プロパティパネルから編集する）</summary>
    public string Memo { get; set; } = string.Empty;

    /// <summary>テーブルの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>エンティティに属するカラム一覧</summary>
    public List<Column> Columns { get; set; } = new();

    /// <summary>カラムも含めてエンティティを複製する</summary>
    /// <param name="preserveId"><c>true</c> の場合は同じ ID を維持し、<c>false</c> の場合は新しい ID を割り当てる（カラムにも同様に適用される）</param>
    /// <returns>複製された <see cref="Entity"/></returns>
    public Entity Clone(bool preserveId) =>
        new()
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            TableName = TableName,
            Memo = Memo,
            Description = Description,
            Columns = Columns.Select(column => column.Clone(preserveId)).ToList(),
        };
}
