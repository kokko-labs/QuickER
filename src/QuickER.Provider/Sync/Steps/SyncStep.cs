using System.Collections.Generic;
using QuickER.Model;

namespace QuickER.Provider.Sync.Steps;

/// <summary>
/// 同期の計画の 1 文（DB へ流す変更 1 つ）
/// </summary>
/// <remarks>
/// <para>
/// 種類は派生の型で決まり、派生は閉じている（このファイルの 15 個）。文は、方言の SQL を組み立てるのに要る値だけを持つ。
/// </para>
/// <para>
/// 表名は図の表名（スキーマで修飾した名前を含む）のまま持つ。名前の無いこともある制約の名前（<c>null</c>）は、
/// 同期スクリプトが外すときに DB から引き、付けるときに作る。
/// </para>
/// </remarks>
/// <param name="Origin">この文がどこから来たか</param>
public abstract record SyncStep(StepOrigin Origin)
{
    /// <summary>外して付け直す対（制約を外す文と付け直す文だけが持つ。対の無い文は <c>null</c>）</summary>
    public ReaddPair? Pair { get; init; }
}

/// <summary>表を作る</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Definition">表の定義（列・主キーの並び・一意制約）</param>
/// <param name="UniqueConstraintNames">
/// 表の定義の一意制約に付ける名前（図全体で解決したもの）。<c>null</c> なら、同期スクリプトがその表だけを範囲にして名前を作る
/// </param>
public sealed record CreateTableStep(
    StepOrigin Origin,
    string TableName,
    Entity Definition,
    IReadOnlyDictionary<UniqueConstraint, string>? UniqueConstraintNames
) : SyncStep(Origin);

/// <summary>表を消す</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
public sealed record DropTableStep(StepOrigin Origin, string TableName) : SyncStep(Origin);

/// <summary>列を足す</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Column">足す列の定義</param>
public sealed record AddColumnStep(StepOrigin Origin, string TableName, Column Column)
    : SyncStep(Origin);

/// <summary>列の定義（型・NULL 許容）を変える</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Column">列の新しい定義</param>
/// <param name="OldColumn">列の旧い定義（分からなければ <c>null</c>）</param>
public sealed record AlterColumnStep(
    StepOrigin Origin,
    string TableName,
    Column Column,
    Column? OldColumn
) : SyncStep(Origin);

/// <summary>列を消す</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="ColumnName">列名</param>
public sealed record DropColumnStep(StepOrigin Origin, string TableName, string ColumnName)
    : SyncStep(Origin);

/// <summary>主キーを外す</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
public sealed record DropPrimaryKeyStep(StepOrigin Origin, string TableName) : SyncStep(Origin);

/// <summary>主キーを付ける</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Columns">主キーの列名（主キーの中の並びの順）</param>
public sealed record AddPrimaryKeyStep(
    StepOrigin Origin,
    string TableName,
    IReadOnlyList<string> Columns
) : SyncStep(Origin);

/// <summary>一意制約を外す</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Name">DB の制約名（無ければ <c>null</c>）</param>
/// <param name="Columns">構成列の名前（宣言の順）</param>
public sealed record DropUniqueConstraintStep(
    StepOrigin Origin,
    string TableName,
    string? Name,
    IReadOnlyList<string> Columns
) : SyncStep(Origin);

/// <summary>一意制約を付ける</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Name">付ける制約名（<c>null</c> なら同期スクリプトが構成列から作る）</param>
/// <param name="Columns">構成列の名前（宣言の順）</param>
public sealed record AddUniqueConstraintStep(
    StepOrigin Origin,
    string TableName,
    string? Name,
    IReadOnlyList<string> Columns
) : SyncStep(Origin);

/// <summary>外部キーを外す</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="ChildTable">子（外部キーを持つ側）の表名</param>
/// <param name="ParentTable">親（参照される側）の表名</param>
/// <param name="Name">DB の制約名（<c>null</c> なら同期スクリプトが DB から引く）</param>
/// <param name="ColumnPairs">列の組（DB の宣言の順）</param>
/// <param name="OnDelete">親の行の削除のときの参照アクション</param>
/// <param name="OnUpdate">親のキーの更新のときの参照アクション</param>
public sealed record DropForeignKeyStep(
    StepOrigin Origin,
    string ChildTable,
    string ParentTable,
    string? Name,
    IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate
) : SyncStep(Origin);

/// <summary>外部キーを付ける</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="ChildTable">子（外部キーを持つ側）の表名</param>
/// <param name="ParentTable">親（参照される側）の表名</param>
/// <param name="Name">付ける制約名（<c>null</c> なら同期スクリプトが既定名を作る）</param>
/// <param name="ColumnPairs">列の組（外部キーの句に書く順）</param>
/// <param name="OnDelete">親の行の削除のときの参照アクション</param>
/// <param name="OnUpdate">親のキーの更新のときの参照アクション</param>
public sealed record AddForeignKeyStep(
    StepOrigin Origin,
    string ChildTable,
    string ParentTable,
    string? Name,
    IReadOnlyList<ForeignKeyColumnNamePair> ColumnPairs,
    ForeignKeyReferentialAction OnDelete,
    ForeignKeyReferentialAction OnUpdate
) : SyncStep(Origin);

/// <summary>表の説明を設定・更新・削除する</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="NewDescription">新しい説明（空なら削除）</param>
public sealed record SetTableDescriptionStep(
    StepOrigin Origin,
    string TableName,
    string? NewDescription
) : SyncStep(Origin);

/// <summary>列の説明を設定・更新・削除する</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="ColumnName">列名</param>
/// <param name="NewDescription">新しい説明（空なら削除）</param>
public sealed record SetColumnDescriptionStep(
    StepOrigin Origin,
    string TableName,
    string ColumnName,
    string? NewDescription
) : SyncStep(Origin);

/// <summary>表を作り直す（新しい表を作り、行を写し、入れ替える。逐次の DDL で変えられない方言）</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="Rebuild">作り直しの中身</param>
public sealed record RebuildTableStep(StepOrigin Origin, TableRebuildPlan Rebuild)
    : SyncStep(Origin);

/// <summary>列を表の中の別の位置へ動かす（列の並びを DDL で変えられる方言）</summary>
/// <param name="Origin">この文がどこから来たか</param>
/// <param name="TableName">表名</param>
/// <param name="Column">動かす列の定義</param>
/// <param name="AfterColumn">この列の直後へ置く。<c>null</c> なら表の先頭へ置く</param>
public sealed record MoveColumnStep(
    StepOrigin Origin,
    string TableName,
    Column Column,
    string? AfterColumn
) : SyncStep(Origin);
