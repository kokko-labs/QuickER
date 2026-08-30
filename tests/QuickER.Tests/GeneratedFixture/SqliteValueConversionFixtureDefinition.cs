using System;
using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedSqliteValueConversionFixture;

/// <summary>
/// SQLite 方言 × 値オブジェクト（VO）で、<c>Convert.ChangeType</c> が扱えない CLR 型を持つ列を生成するフィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// Microsoft.Data.Sqlite は <c>TimeSpan</c> / <c>Guid</c> / <c>DateTimeOffset</c> をいずれも TEXT で格納し、
/// <c>reader.GetValue</c> は string を返す。これらの型は <see cref="IConvertible"/> を実装しないため、
/// 変換を <c>Convert.ChangeType</c> だけに任せると読み戻しが必ず <see cref="InvalidCastException"/> になる
/// （SQL Server は型付きで返すため露見しなかった）。本フィクスチャは、その 3 型を VO の内包値に持つ図を
/// SQLite 方言のQuickER 版 Repository として生成し、行マッピング・生 SQL スカラー・生 SQL 射影の
/// 3 経路が実 DB で読み戻せることを固定する。
/// </para>
/// <para>
/// 図は 1 テーブルの最小構成:
/// </para>
/// <list type="bullet">
///   <item><c>time_probes</c>: <c>probe_id</c>（int・PK）／<c>duration</c>（time＝TimeSpan）／
///   <c>session_id</c>（uniqueidentifier＝Guid）／<c>occurred_at</c>（datetimeoffset＝DateTimeOffset）／
///   <c>label</c>（nvarchar(50)・NULL 許容＝対照の string 列）</item>
/// </list>
/// <para>
/// EditModel / Mapper / EF Core / インメモリ / リモートは交差の焦点でないため生成しない
/// （VO と SQLite 方言エンジンだけを最小構成で焼き付ける）。
/// </para>
/// </remarks>
public static class SqliteValueConversionFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSqliteValueConversionFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "SqliteValueConversionFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQLite 方言のQuickER 版 Repository ＋ 値オブジェクトのみを生成する。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCoreRepositories = false,
            RepositoryDialects = ["sqlite"],
            SplitFilesByCategory = false,
        };

    // 図の要素 ID は決定的でなければ再生成時に差分が出るため、固定 GUID を用いる。
    private static readonly Guid ProbeEntityId = new("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid ProbePkColId = new("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid ProbeDurationColId = new("c1000000-0000-0000-0000-000000000003");
    private static readonly Guid ProbeSessionColId = new("c1000000-0000-0000-0000-000000000004");
    private static readonly Guid ProbeOccurredColId = new("c1000000-0000-0000-0000-000000000005");
    private static readonly Guid ProbeLabelColId = new("c1000000-0000-0000-0000-000000000006");

    /// <summary>非 IConvertible 型の検証用 ER 図を決定的に構築する</summary>
    public static ErDiagram Build()
    {
        var probe = new Entity
        {
            Id = ProbeEntityId,
            TableName = "time_probes",
            Columns =
            {
                new Column
                {
                    Id = ProbePkColId,
                    Name = "probe_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                // TimeSpan・Guid・DateTimeOffset はいずれも IConvertible 非実装＝ChangeType では復元できない
                new Column
                {
                    Id = ProbeDurationColId,
                    Name = "duration",
                    DataType = "time",
                    IsNullable = false,
                },
                new Column
                {
                    Id = ProbeSessionColId,
                    Name = "session_id",
                    DataType = "uniqueidentifier",
                    IsNullable = false,
                },
                new Column
                {
                    Id = ProbeOccurredColId,
                    Name = "occurred_at",
                    DataType = "datetimeoffset",
                    IsNullable = false,
                },
                // 対照の NULL 許容 string 列（従来から素通しで読める経路が壊れていないことの確認用）
                new Column
                {
                    Id = ProbeLabelColId,
                    Name = "label",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                },
            },
        };

        return new ErDiagram { TargetDbms = "sqlite", Entities = { probe } };
    }
}
