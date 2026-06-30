using QuickER.Generator;
using QuickER.Model;

namespace QuickER.Provider;

/// <summary>スキーマ取得（DB-first）結果。意味モデルのエンティティ・リレーションを保持する</summary>
public sealed class SchemaImportResult
{
    /// <summary>取得したエンティティ一覧</summary>
    public IReadOnlyList<Entity> Entities { get; init; } = [];

    /// <summary>取得したリレーション一覧</summary>
    public IReadOnlyList<Relationship> Relationships { get; init; } = [];
}

/// <summary>接続文字列から DB スキーマを取得して意味モデルへ変換するインポーター（DB 方言ごとに実装）</summary>
public interface ISchemaImporter
{
    /// <summary>接続文字列で DB へ接続し、スキーマを取得する</summary>
    Task<SchemaImportResult> ImportAsync(
        string connectionString,
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

    /// <summary>DB-first スキーマ取込</summary>
    ISchemaImporter SchemaImporter { get; }

    /// <summary>DB 型 → C# 型マッピング</summary>
    IColumnTypeMapper TypeMapper { get; }

    /// <summary>この DBMS で選択可能なデータ型の一覧（UI の型候補・検証に使用）</summary>
    IReadOnlyList<string> DataTypes { get; }
}
