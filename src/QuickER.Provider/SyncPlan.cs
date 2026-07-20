using System.Collections.Generic;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 同期スクリプトの 1 セクション分（同一種別の差分項目の集合）を表す実行計画の単位。
/// </summary>
/// <remarks>
/// セクションは <see cref="SyncPlanner"/> が固定順序で並べて <see cref="SyncPlan"/> を構成する。
/// 方言別レンダラー（<see cref="ISyncScriptBuilder"/> の実装）はこの単位を消費して SQL 化する。
/// </remarks>
public sealed class SyncPlanSection
{
    /// <summary>このセクションが担当する差分種別。</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>このセクションに含まれる差分項目（入力の出現順を保持する）。</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; init; } = [];
}

/// <summary>
/// 差分項目から組み立てた方言中立の実行計画。
/// </summary>
/// <remarks>
/// <see cref="SyncPlanner.BuildPlan"/> が選択済み項目を種別ごとにグループ化し、依存関係を満たす
/// 固定順序でセクションを並べたもの。方言別レンダラーはこの計画を SQL へ変換するだけでよい。
/// </remarks>
public sealed class SyncPlan
{
    /// <summary>実行順に並んだセクション一覧（空セクションは含まない）。</summary>
    public IReadOnlyList<SyncPlanSection> Sections { get; init; } = [];

    /// <summary>
    /// 逐次 DDL で表現できない変更をテーブル再構築でまとめた計画（rebuild 方言のみ・他方言は空）。
    /// </summary>
    public IReadOnlyList<TableRebuildPlan> Rebuilds { get; init; } = [];

    /// <summary>生成対象のセクション・再構築が 1 件も無いか。</summary>
    public bool IsEmpty => Sections.Count == 0 && Rebuilds.Count == 0;
}

/// <summary>
/// テーブル再構築（新テーブル作成 → データ移送 → 入替）1 件分の実行計画。
/// </summary>
/// <remarks>
/// <para>
/// SQLite は <c>ALTER COLUMN</c> / <c>ADD・DROP CONSTRAINT</c> を持たないため、列型変更・列削除・FK 変更は
/// テーブル再構築で実現する。再構築後のスキーマ（<see cref="NewDefinition"/>）は「DB 現状（live）＋選択された差分のみ」
/// を合成したもので、未選択の変更は決して混入させない（<see cref="SyncPlanner"/> が合成する）。
/// </para>
/// <para>
/// <see cref="CreateOnly"/> が <c>true</c> のときは新規テーブル作成のみで、データ移送・旧テーブルの
/// DROP / RENAME を行わない（<see cref="CopyColumns"/> / <see cref="AuxiliaryObjects"/> は空）。
/// </para>
/// </remarks>
public sealed class TableRebuildPlan
{
    /// <summary>再構築対象のテーブル名。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>再構築後のテーブル定義（合成済み・深いコピー）。</summary>
    public Entity NewDefinition { get; init; } = new();

    /// <summary>再構築後に張る外部キー仕様（解決済み・<c>CREATE TABLE</c> 内へインライン出力する）。</summary>
    public IReadOnlyList<TableRebuildForeignKey> ForeignKeys { get; init; } = [];

    /// <summary>新規テーブル作成のみか（データ移送・DROP / RENAME を行わない）。</summary>
    public bool CreateOnly { get; init; }

    /// <summary>
    /// データ移送（<c>INSERT ... SELECT</c>）の対象列名（live と <see cref="NewDefinition"/> の両方に存在する列）。
    /// </summary>
    public IReadOnlyList<string> CopyColumns { get; init; } = [];

    /// <summary>再構築後に再現する補助オブジェクト（インデックス・トリガー・一意制約）。</summary>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];

    /// <summary>この再構築の由来となった差分項目（UI の確認表示用）。</summary>
    public IReadOnlyList<SchemaDiffItem> SourceItems { get; init; } = [];
}

/// <summary>
/// テーブル再構築の <c>CREATE TABLE</c> にインライン出力する、解決済みの外部キー 1 件。
/// </summary>
/// <param name="ConstraintName">FK 制約名</param>
/// <param name="ChildColumn">子テーブル側の外部キー列名</param>
/// <param name="ParentTable">参照先（親）テーブル名</param>
/// <param name="ParentColumn">参照先（親）の被参照列名</param>
/// <param name="OnDelete">親行削除時の参照アクション</param>
/// <param name="OnUpdate">親キー更新時の参照アクション</param>
public sealed record TableRebuildForeignKey(
    string ConstraintName,
    string ChildColumn,
    string ParentTable,
    string ParentColumn,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate
);
