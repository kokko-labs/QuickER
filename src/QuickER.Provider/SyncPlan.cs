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
    /// 判定は「同期後に被参照列が候補キーであることを証明できたか」で行う（同期後の主キー、または同期後に
    /// 存在する一意制約と完全に一致するか）。証明できない構成でも一意インデックス等で実際には通ることがあり、
    /// 断定はできないため実行ブロックではなく警告に留める。
    /// </remarks>
    ForeignKeyRebuildMayLoseCandidateKey,

    /// <summary>
    /// 削除しようとしている一意制約が、既存の外部キーの被参照列そのものである
    /// （＝候補キーが失われ、外部キーが壊れる・削除が実行時に拒否されうる）。
    /// </summary>
    /// <remarks>
    /// 主キーが同じ列を覆っていれば候補キーは保たれるが、その判定には同期後の主キー構成が要る。
    /// 過剰警告を許容して実行はブロックしない（<see cref="ForeignKeyRebuildMayLoseCandidateKey"/> と同じ方針）。
    /// </remarks>
    UniqueConstraintDropMayBreakForeignKey,

    /// <summary>
    /// テーブル再構築で、意味モデルが持たない列レベル属性（<c>AUTOINCREMENT</c> / <c>DEFAULT</c> /
    /// <c>CHECK</c> / <c>COLLATE</c> / 生成列）が失われる。
    /// </summary>
    /// <remarks>
    /// 再構築の <c>CREATE TABLE</c> はモデルから組み立て直すため、モデルに無い属性は再現されない
    /// （インデックス・トリガーは <see cref="SchemaAuxiliaryObject"/> が CREATE 文全文で温存しているのと対照的）。
    /// 検出は live の <c>CREATE TABLE</c> 文に対する文字列検査（<see cref="TableRebuildAttributeDetector"/>）で、
    /// 断定はできないため実行ブロックではなく警告に留める（他 2 種と同じ方針）。
    /// </remarks>
    TableRebuildDropsColumnAttribute,
}

/// <summary>
/// 実行計画に付随する警告 1 件（言語中立）。
/// </summary>
/// <param name="Kind">警告種別</param>
/// <param name="TableName">
/// 対象テーブル。<see cref="SyncPlanWarningKind.ForeignKeyRebuildMayLoseCandidateKey"/> では
/// 外部キーを保有する子テーブル、<see cref="SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey"/>
/// では一意制約を削除するテーブル（＝外部キーの参照先）。
/// </param>
/// <param name="Detail">
/// 補足（外部キー制約名など。無ければ空文字）。
/// <see cref="SyncPlanWarningKind.UniqueConstraintDropMayBreakForeignKey"/> では壊れうる外部キーの制約名。
/// <see cref="SyncPlanWarningKind.TableRebuildDropsColumnAttribute"/> では失われる属性の SQL キーワードを
/// カンマ区切りで並べたもの（<c>AUTOINCREMENT, DEFAULT</c> 等）。SQL の綴りそのものなので言語中立に扱える。
/// </param>
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

    /// <summary>再構築後に再現する補助オブジェクト（インデックス・トリガー）。</summary>
    /// <remarks>
    /// 一意制約は意味モデル（<see cref="Entity.UniqueConstraints"/>）が正本のため、
    /// <see cref="NewDefinition"/> 側で合成済み＝ここには含まれない。
    /// </remarks>
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
/// <param name="ChildColumns">子テーブル側の外部キー列名（宣言順。複合外部キーは 2 件以上）</param>
/// <param name="ParentTable">参照先（親）テーブル名</param>
/// <param name="ParentColumns">参照先（親）の被参照列名（宣言順。子側と同数・同順）</param>
/// <param name="OnDelete">親行削除時の参照アクション</param>
/// <param name="OnUpdate">親キー更新時の参照アクション</param>
public sealed record TableRebuildForeignKey(
    string ConstraintName,
    IReadOnlyList<string> ChildColumns,
    string ParentTable,
    IReadOnlyList<string> ParentColumns,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate
);
