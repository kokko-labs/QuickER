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

    /// <summary>テーブルに定義された一意制約（主キーを除く）の一覧</summary>
    public List<UniqueConstraint> UniqueConstraints { get; set; } = [];

    /// <summary>カラム・一意制約も含めてエンティティを複製する</summary>
    /// <param name="preserveId"><c>true</c> の場合は同じ ID を維持し、<c>false</c> の場合は新しい ID を割り当てる（カラム・一意制約にも同様に適用される）</param>
    /// <returns>複製された <see cref="Entity"/></returns>
    /// <remarks>
    /// <paramref name="preserveId"/> が <c>false</c> のときはカラム ID も新規採番されるため、
    /// 一意制約の <see cref="UniqueConstraint.ColumnIds"/> は複製後のカラム ID へ張り替える
    /// （張り替えないと複製側の制約が元エンティティのカラムを指し続け、参照が壊れる）。
    /// </remarks>
    public Entity Clone(bool preserveId)
    {
        // 先にカラムを複製し、旧 ID → 新 ID の対応表を作る（一意制約の張り替えに使う）
        var columns = new List<Column>(Columns.Count);
        var columnIdMap = new Dictionary<Guid, Guid>(Columns.Count);

        foreach (var column in Columns)
        {
            var cloned = column.Clone(preserveId);
            columns.Add(cloned);
            columnIdMap[column.Id] = cloned.Id;
        }

        return new Entity
        {
            Id = preserveId ? Id : Guid.NewGuid(),
            TableName = TableName,
            Memo = Memo,
            Description = Description,
            Columns = columns,
            UniqueConstraints = UniqueConstraints
                .Select(constraint => constraint.Clone(preserveId, columnIdMap))
                .ToList(),
        };
    }
}
