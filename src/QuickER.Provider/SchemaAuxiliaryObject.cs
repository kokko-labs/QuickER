using System.Collections.Generic;

namespace QuickER.Provider;

/// <summary>スキーマ取込で拾う補助オブジェクトの種別。</summary>
public enum SchemaAuxiliaryObjectKind
{
    /// <summary>インデックス（<c>CREATE INDEX</c> / <c>CREATE UNIQUE INDEX</c>）。CREATE SQL 全文を温存する。</summary>
    Index,

    /// <summary>トリガー（<c>CREATE TRIGGER</c>）。CREATE SQL 全文を温存する。</summary>
    Trigger,

    /// <summary>
    /// テーブルレベルの一意制約（<c>CREATE TABLE</c> 内の <c>UNIQUE (...)</c>）。
    /// 単体の SQL 文を持たない（自動インデックス）ため、構成列（<see cref="SchemaAuxiliaryObject.Columns"/>）で表現する。
    /// </summary>
    UniqueConstraint,
}

/// <summary>
/// 意味モデル（<see cref="QuickER.Model.Entity"/> / <see cref="QuickER.Model.Relationship"/>）では表現しきれない、
/// テーブルに付随する補助オブジェクト 1 件（インデックス・トリガー・無名の一意制約）。
/// </summary>
/// <remarks>
/// <para>
/// SQLite のテーブル再構築（rebuild）同期では、新テーブルを作り直す過程でこれらが失われる。
/// それをサイレントに消失させないため、取込時に <see cref="SchemaImportResult.AuxiliaryObjects"/> へ収集し、
/// 再構築計画（<see cref="TableRebuildPlan.AuxiliaryObjects"/>）へ引き継いで再現する。
/// </para>
/// <para>
/// <see cref="SchemaAuxiliaryObjectKind.Index"/> / <see cref="SchemaAuxiliaryObjectKind.Trigger"/> は
/// <see cref="CreateSql"/>（元の CREATE 文全文）を温存して再実行する。
/// <see cref="SchemaAuxiliaryObjectKind.UniqueConstraint"/> は単体の SQL を持たないため、
/// <see cref="Columns"/> を <c>CREATE TABLE</c> 内のテーブルレベル <c>UNIQUE</c> 句として再現する。
/// </para>
/// </remarks>
public sealed class SchemaAuxiliaryObject
{
    /// <summary>この補助オブジェクトが属するテーブル名。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>補助オブジェクト名（インデックス名 / トリガー名。一意制約は取込時の自動インデックス名）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>補助オブジェクトの種別。</summary>
    public SchemaAuxiliaryObjectKind Kind { get; init; }

    /// <summary>
    /// 元の CREATE 文全文（<see cref="SchemaAuxiliaryObjectKind.Index"/> /
    /// <see cref="SchemaAuxiliaryObjectKind.Trigger"/> のみ・そのまま再実行する）。
    /// </summary>
    public string CreateSql { get; init; } = string.Empty;

    /// <summary>
    /// 構成列名（<see cref="SchemaAuxiliaryObjectKind.UniqueConstraint"/> のみ・出現順を保持する）。
    /// </summary>
    public IReadOnlyList<string> Columns { get; init; } = [];
}
