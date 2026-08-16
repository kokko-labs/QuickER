using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>スキーマ取得（DB-first）結果。意味モデルのエンティティ・リレーションを保持する</summary>
public sealed class SchemaImportResult
{
    /// <summary>取得したエンティティ一覧</summary>
    public IReadOnlyList<Entity> Entities { get; init; } = [];

    /// <summary>取得したリレーション一覧</summary>
    public IReadOnlyList<Relationship> Relationships { get; init; } = [];

    /// <summary>
    /// 意味モデルでは表現しきれない補助オブジェクト（インデックス・トリガー）。
    /// </summary>
    /// <remarks>
    /// 現状は SQLite のテーブル再構築同期のためだけに収集する（他方言は空のまま）。一意制約は
    /// <see cref="Entity.UniqueConstraints"/> が正本のため、ここには含まれない。
    /// </remarks>
    public IReadOnlyList<SchemaAuxiliaryObject> AuxiliaryObjects { get; init; } = [];

    /// <summary>
    /// テーブル名 → そのテーブルの <c>CREATE TABLE</c> 文全文（取得できた方言のみ・キーは大文字小文字非依存）。
    /// </summary>
    /// <remarks>
    /// 現状は SQLite だけが埋める（他方言は空のまま）。テーブル再構築同期は意味モデルから
    /// <c>CREATE TABLE</c> を組み立て直すため、モデルが持たない列レベル属性（<c>AUTOINCREMENT</c> /
    /// <c>DEFAULT</c> / <c>CHECK</c> / <c>COLLATE</c> / 生成列）は再現されない。その喪失を実行前に警告するには
    /// 「今 DB にある定義そのもの」が要るため、意味モデルとは別に原文を運ぶ
    /// （<see cref="SchemaAuxiliaryObject"/> へ載せると再作成ループに乗ってしまうので別枠にしている）。
    /// </remarks>
    public IReadOnlyDictionary<string, string> TableCreateSql { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>接続文字列から DB スキーマを取得して意味モデルへ変換するインポーター（DB 方言ごとに実装）</summary>
public interface ISchemaImporter
{
    /// <summary>接続文字列で DB へ接続し、スキーマを取得する</summary>
    /// <param name="connectionString">接続文字列</param>
    /// <param name="commandTimeoutSeconds">
    /// カタログ照会 1 本ごとの実行タイムアウト（秒）。<c>0</c> は無制限（ADO.NET の規約）。
    /// 既定値を持たせないのは、呼び出し側（GUI の接続設定・CLI のオプション）に必ず選ばせるため。
    /// </param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task<SchemaImportResult> ImportAsync(
        string connectionString,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken = default
    );
}

/// <summary>カラムの DB 型を C# 型へ解決するマッパー（DB 方言ごとに実装）</summary>
public interface IColumnTypeMapper
{
    /// <summary>ER 図の全カラムの DB 型を解決し、カラム ID → C# 型情報の対応表を構築する</summary>
    IReadOnlyDictionary<Guid, CSharpTypeInfo> ResolveColumnTypes(ErDiagram diagram);
}

/// <summary>
/// 特定の DBMS（SQL Server / PostgreSQL / MySQL など）向けの機能一式を束ねるプロバイダ抽象。
/// </summary>
/// <remarks>
/// DB 方言に依存する責務（スキーマ取込・型マッピング・型カタログ）をここへ集約し、
/// コア（Generator・CLI・アプリの生成経路）はこの抽象にのみ依存する。新 DBMS 対応は実装追加のみで済む。
/// </remarks>
public interface IDatabaseProvider
{
    /// <summary>プロバイダ識別名（CLI の <c>--provider</c> 値。例: <c>sqlserver</c>）</summary>
    string Name { get; }

    /// <summary>UI 表示名（例: <c>SQL Server</c>）</summary>
    string DisplayName { get; }

    /// <summary>方言の既定ポート。SQL Server は <c>null</c>（インスタンス名運用のため）</summary>
    int? DefaultPort { get; }

    /// <summary>DB-first スキーマ取込</summary>
    ISchemaImporter SchemaImporter { get; }

    /// <summary>DB 型 → C# 型マッピング</summary>
    IColumnTypeMapper TypeMapper { get; }

    /// <summary>この DBMS のネイティブ型カタログ（型候補・正規型との相互変換）</summary>
    ITypeCatalog TypeCatalog { get; }

    /// <summary>同期スクリプト生成</summary>
    ISyncScriptBuilder SyncScriptBuilder { get; }

    /// <summary>この方言がスキーマ同期でどこまで表現できるかを宣言するケーパビリティ</summary>
    /// <remarks><see cref="SyncPlanner"/> が実行計画を組み立てる際の振り分けに用いる</remarks>
    SyncDialectCapabilities SyncCapabilities { get; }

    /// <summary>同期スクリプトの実行</summary>
    ISchemaSyncExecutor SyncExecutor { get; }

    /// <summary>ER 図からの DDL 生成</summary>
    IDdlGenerator DdlGenerator { get; }

    /// <summary>共通接続設定から接続文字列を構築する</summary>
    string BuildConnectionString(DbConnectionSettings settings);
}
