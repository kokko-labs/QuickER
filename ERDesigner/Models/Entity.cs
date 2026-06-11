using System.Collections.Generic;

namespace ERDesigner.Models;

/// <summary>
/// ER 図上のテーブル（エンティティ）1 件を表すモデル
/// JSON シリアライズの対象
/// </summary>
public class Entity
{
    /// <summary>見出し帯の背景色の既定値（淡い青）</summary>
    public const string DefaultTitleBackgroundColor = "#DCEBFF";

    /// <summary>エンティティの一意識別子（<see cref="Relationship"/> からの参照に使用する）</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>物理テーブル名（例: <c>Customer</c>）</summary>
    public string TableName { get; set; } = "NewTable";

    /// <summary>キャンバス上の X 座標 (px)</summary>
    public double X { get; set; }

    /// <summary>キャンバス上の Y 座標 (px)</summary>
    public double Y { get; set; }

    /// <summary>エンティティカードの横幅 (px)</summary>
    public double Width { get; set; } = 200;

    /// <summary>備考メモ（プロパティパネルから編集する）</summary>
    public string Memo { get; set; } = string.Empty;

    /// <summary>テーブルの説明（SQL Server の拡張プロパティ <c>MS_Description</c> と同期する）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>ダイアグラム上の見出し帯に表示する背景色（<c>#RRGGBB</c> 形式）</summary>
    public string TitleBackgroundColor { get; set; } = DefaultTitleBackgroundColor;

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
            X = X,
            Y = Y,
            Width = Width,
            Memo = Memo,
            Description = Description,
            TitleBackgroundColor = TitleBackgroundColor,
            Columns = Columns.Select(column => column.Clone(preserveId)).ToList(),
        };
}
