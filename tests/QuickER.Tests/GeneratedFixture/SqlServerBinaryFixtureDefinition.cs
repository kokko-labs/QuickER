using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedSqlServerBinaryFixture;

/// <summary>
/// 無制限バイナリ列の除外（<see cref="CodeGenerationOptions.ExcludeUnboundedBinaryColumns"/>）を
/// <b>SQL Server 方言</b>のQuickER 版 Repository で生成する固定フィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図はバイナリフィクスチャ（<see cref="Tests.GeneratedBinaryFixture.BinaryFixtureDefinition"/>）と
/// 同一（<c>documents</c>＝無制限バイナリ列 payload/thumb・有界バイナリ checksum・rowversion row_ver、子
/// <c>document_notes</c>、名前付きクエリ 3 本）で、オプションだけを SQL Server 方言のQuickER 版 Repository 単独へ
/// 差し替えたもの（<c>RemoteContractFixtureDefinition</c> が <c>QueryFixtureDefinition</c> へ委譲する先例と同型）。
/// </para>
/// <para>
/// バイナリフィクスチャ（SQLite 方言）が SQLite 一時ファイル DB で検証できないのは、SQL Server 版
/// ストリーミングエンジン（読み=<c>CommandBehavior.SequentialAccess</c>＋<c>GetStream</c>・書き=Stream 値の
/// <c>SqlParameter</c>）と、除外の FOR JSON 縮小 SELECT・<c>WithUnboundedBinary()</c> のプレーン全列 SELECT の
/// SQL Server 経路。これらを Testcontainers の実 SQL Server で往復検証するために本フィクスチャを追加する
/// （<see cref="Tests.Integration.SqlServerBinaryColumnRuntimeTests"/>・Docker 依存）。
/// </para>
/// <para>
/// オプションは SQL Server エンジン検証に必要な最小構成（EF Core / インメモリ / リモートは付けない）。
/// </para>
/// </remarks>
public static class SqlServerBinaryFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedSqlServerBinaryFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "SqlServerBinaryFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。
    /// SQL Server 方言のQuickER 版 Repository 単独＋無制限バイナリ除外の最小構成。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            NamespaceName = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEntityClasses = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = false,
            RepositoryDialect = "sqlserver",
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    /// <summary>バイナリフィクスチャと同一の図（documents / document_notes＋名前付きクエリ）を返す</summary>
    public static ErDiagram Build() => Tests.GeneratedBinaryFixture.BinaryFixtureDefinition.Build();
}
