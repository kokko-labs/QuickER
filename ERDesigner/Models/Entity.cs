using System;
using System.Collections.Generic;

namespace ERDesigner.Models;

/// <summary>
/// ER 図のテーブル（エンティティ）1 個を表すモデルクラスです。
/// JSON 保存対象になります。
/// </summary>
public class Entity
{
    /// <summary>エンティティを一意に識別する ID です。リレーションから参照されます。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>物理テーブル名（例: <c>Customer</c>）。</summary>
    public string TableName { get; set; } = "NewTable";

    /// <summary>キャンバス上の X 座標 (px)。</summary>
    public double X { get; set; }

    /// <summary>キャンバス上の Y 座標 (px)。</summary>
    public double Y { get; set; }

    /// <summary>カードの横幅 (px)。</summary>
    public double Width { get; set; } = 200;

    /// <summary>備考メモ（プロパティパネルから入力可能）。</summary>
    public string Memo { get; set; } = string.Empty;

    /// <summary>
    /// テーブルの説明 (SQL Server の拡張プロパティ <c>MS_Description</c> と同期します)。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>このエンティティに含まれるカラム一覧です。</summary>
    public List<Column> Columns { get; set; } = new();
}
