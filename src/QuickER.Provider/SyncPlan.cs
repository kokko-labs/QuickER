using System.Collections.Generic;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>
/// 主キー変更（<see cref="SchemaDiffKind.AlterPrimaryKey"/>）セクションのフェーズ。
/// </summary>
/// <remarks>
/// <para>
/// 逐次 DDL 方言では主キー変更を「旧主キーの解除（<see cref="Drop"/>）」と「新主キーの付与（<see cref="Add"/>）」の
/// 2 セクションへ分割し、その間に列定義変更（<see cref="SchemaDiffKind.AlterColumn"/>）を挟む。旧主キー列の
/// NULL 許容化は主キー制約が残ったままでは失敗する（SQL Server Msg 5074 → 4922・Oracle ORA-01451 等）ため。
/// </para>
/// <para>
/// <see cref="None"/> は主キー変更以外の全セクション（既定値）。主キー変更セクションに <see cref="None"/> が
/// 現れた場合、レンダラーは従来どおり DROP → ADD を 1 セクション内で連続出力する（旧形の計画への後方互換）。
/// </para>
/// </remarks>
public enum PrimaryKeyPhase
{
    /// <summary>フェーズの概念を持たないセクション（主キー変更以外の全種別）。</summary>
    None = 0,

    /// <summary>旧主キー制約の解除フェーズ（列定義変更より前に置く）。</summary>
    Drop,

    /// <summary>新主キー制約の付与フェーズ（列定義変更より後に置く）。</summary>
    Add,
}

/// <summary>
/// 同期スクリプトの 1 セクション分（同一種別の差分項目の集合）を表す実行計画の単位。
/// </summary>
/// <remarks>
/// セクションは <see cref="SyncPlanner"/> が固定順序で並べて <see cref="SyncPlan"/> を構成する。
/// 方言別レンダラー（<see cref="ISyncScriptBuilder"/> の実装）はこの単位を消費して SQL 化する。
/// 同一 <see cref="Kind"/> のセクションが複数回・別位置に現れることがある（主キー変更の 2 フェーズ）。
/// </remarks>
public sealed class SyncPlanSection
{
    /// <summary>このセクションが担当する差分種別。</summary>
    public SchemaDiffKind Kind { get; init; }

    /// <summary>
    /// 主キー変更セクションのフェーズ（主キー変更以外は既定の <see cref="PrimaryKeyPhase.None"/>）。
    /// </summary>
    public PrimaryKeyPhase PrimaryKeyPhase { get; init; } = PrimaryKeyPhase.None;

    /// <summary>このセクションに含まれる差分項目（入力の出現順を保持する）。</summary>
    public IReadOnlyList<SchemaDiffItem> Items { get; init; } = [];
}

/// <summary>
/// 実行計画に付随する警告の種別。
/// </summary>
/// <remarks>
/// 表示文言は持たない（言語中立）。GUI は自前の resx で整形し、スクリプト側で示す場合は英語で書く。
/// いずれも「実行を止めるほどの確実性は無いが、静かに壊れうる」ものを利用者へ伝えるための材料。
/// </remarks>
public enum SyncPlanWarningKind
{
    /// <summary>
    /// 主キー変更に伴って自動再作成する外部キーの参照先列が、新しい主キー列に含まれない
    /// （＝参照先が候補キーでなくなり、再作成が実行時に失敗しうる）。
    /// </summary>
    /// <remarks>
    /// 一意制約 / 一意インデックスは取り込んでいないため「候補キーでない」と断定はできない
    /// （UNIQUE で候補キーが保たれる構成もある）。そのため実行ブロックではなく警告に留める。
    /// </remarks>
    ForeignKeyRebuildMayLoseCandidateKey,

    /// <summary>
    /// 複合外部キーの子テーブルであるため、そのテーブルのテーブル再構築を計画から除外した。
    /// </summary>
    /// <remarks>
    /// 意味モデルは複合外部キーの列対応を保持できない（<see cref="CompositeForeignKeyImportWarning"/>）ため、
    /// 再構築すると複合外部キーが単列外部キーへ作り替えられる＝成功して静かに壊れる。他テーブルの同期は続行する。
    /// </remarks>
    RebuildBlockedByCompositeForeignKey,
}

/// <summary>
/// 実行計画に付随する警告 1 件（言語中立）。
/// </summary>
/// <param name="Kind">警告種別</param>
/// <param name="TableName">
/// 対象テーブル。<see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/> では
/// 外部キーを保有する子テーブル、<see cref="SyncPlanWarningKind.RebuildBlockedByCompositeForeignKey"/> では
/// 再構築を止めたテーブル。
/// </param>
/// <param name="Detail">補足（外部キー制約名など。無ければ空文字）</param>
public sealed record SyncPlanWarning(
    SyncPlanWarningKind Kind,
    string TableName,
    string Detail = ""
);

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

    /// <summary>
    /// 列順をネイティブ DDL（<c>ALTER TABLE ... MODIFY ... AFTER</c>）で並べ替える計画
    /// （Native 方言＝MySQL のみ・他方言は空。rebuild 方言では再構築へ畳むためここは常に空）。
    /// </summary>
    public IReadOnlyList<TableReorderPlan> Reorders { get; init; } = [];

    /// <summary>
    /// この計画を実行する前に利用者へ伝えるべき警告（言語中立・SQL 生成には影響しない）。
    /// </summary>
    /// <remarks>
    /// 警告があっても計画自体は実行可能な形で成立している（危険な部分は除外済み、または実行時に失敗しうると
    /// 分かっているだけ）。表示層は実行確認の文言へ織り込む。
    /// </remarks>
    public IReadOnlyList<SyncPlanWarning> Warnings { get; init; } = [];

    /// <summary>生成対象のセクション・再構築・並べ替えが 1 件も無いか。</summary>
    public bool IsEmpty => Sections.Count == 0 && Rebuilds.Count == 0 && Reorders.Count == 0;
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
/// ネイティブ列順変更（<c>ALTER TABLE ... MODIFY ... AFTER</c>）1 テーブル分の実行計画。
/// </summary>
/// <remarks>
/// 逐次 DDL で列を並べ替えられる方言（MySQL）向け。<see cref="Moves"/> は「最小移動」に刈り込まれており
/// （最長増加部分列を不動とし、それ以外の列だけを移動する）、各移動は目標順で前から確定する
/// （<see cref="SyncPlanner"/> が合成する）。
/// </remarks>
public sealed class TableReorderPlan
{
    /// <summary>並べ替え対象のテーブル名。</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>実行順に並んだ列移動一覧（各列を <see cref="ColumnMove.AfterColumn"/> の直後へ動かす）。</summary>
    public IReadOnlyList<ColumnMove> Moves { get; init; } = [];

    /// <summary>この並べ替えの由来となった差分項目（UI の確認表示用）。</summary>
    public IReadOnlyList<SchemaDiffItem> SourceItems { get; init; } = [];
}

/// <summary>
/// ネイティブ列順変更での 1 列分の移動。
/// </summary>
/// <param name="Column">
/// 移動する列の完全定義（合成済み）。選択済み AlterColumn / AddColumn があればその新定義、無ければ live 定義。
/// </param>
/// <param name="AfterColumn">
/// この列を直後へ置く列名。<c>null</c> のときは先頭（<c>FIRST</c>）へ移動する。
/// </param>
public sealed record ColumnMove(Column Column, string? AfterColumn);

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
