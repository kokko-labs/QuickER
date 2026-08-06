using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>スキーマ取込で拾う補助オブジェクトの種別。</summary>
public enum SchemaAuxiliaryObjectKind
{
    /// <summary>インデックス（<c>CREATE INDEX</c> / <c>CREATE UNIQUE INDEX</c>）。CREATE SQL 全文を温存する。</summary>
    Index,

    /// <summary>トリガー（<c>CREATE TRIGGER</c>）。CREATE SQL 全文を温存する。</summary>
    Trigger,
}

/// <summary>
/// 意味モデル（<see cref="QuickER.Model.Entity"/> / <see cref="QuickER.Model.Relationship"/>）では表現しきれない、
/// テーブルに付随する補助オブジェクト 1 件（インデックス・トリガー）。
/// </summary>
/// <remarks>
/// <para>
/// SQLite のテーブル再構築（rebuild）同期では、新テーブルを作り直す過程でこれらが失われる。
/// それをサイレントに消失させないため、取込時に <see cref="SchemaImportResult.AuxiliaryObjects"/> へ収集し、
/// 再構築計画（<see cref="TableRebuildPlan.AuxiliaryObjects"/>）へ引き継いで <see cref="CreateSql"/>
/// （元の CREATE 文全文）をそのまま再実行する。
/// </para>
/// <para>
/// テーブルレベルの一意制約は意味モデル（<see cref="QuickER.Model.Entity.UniqueConstraints"/>）が正本のため、
/// ここでは扱わない（再構築後の <c>CREATE TABLE</c> は合成済みの定義から <c>UNIQUE</c> 句を出力する）。
/// </para>
/// </remarks>
public sealed class SchemaAuxiliaryObject
{
    /// <summary>この補助オブジェクトが属するテーブル名。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>補助オブジェクト名（インデックス名 / トリガー名）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>補助オブジェクトの種別。</summary>
    public SchemaAuxiliaryObjectKind Kind { get; init; }

    /// <summary>元の CREATE 文全文（そのまま再実行する）。</summary>
    public string CreateSql { get; init; } = string.Empty;
}
