using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedBackendParityEfCoreOnlyFixture;

/// <summary>
/// 実装先どうしの一致テストの「EF Core 単独で生成した構成」の固定フィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 図は <see cref="GeneratedBackendParityFixture.BackendParityFixtureDefinition.Build"/> と同じ。QuickER 版 Repository とインメモリを
/// 出さず、EF Core とリモートサービスだけを出す（EF Core 単独のスコープ＝行の組み立て器と SQL 文のメンバーを出さず、生 SQL の
/// エンティティ取得は列ごとの写しで行う）。一致テストは、このサーバー（EF Core 単独で生成したエンドポイントとリポジトリ）を、
/// 実装先一致フィクスチャの HTTP クライアントから呼ぶ（実装先 <c>RemoteEfCoreOnly</c>。応答の型名は名前空間を含まないので互換）。
/// </para>
/// </remarks>
public static class BackendParityEfCoreOnlyFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedBackendParityEfCoreOnlyFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "BackendParityEfCoreOnlyFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（サーバー実装）</summary>
    public const string ServerOutputFileName = "BackendParityEfCoreOnlyFixture.RemoteServer.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。EF Core とリモートサービスだけを出し、値オブジェクトと無制限バイナリ除外を
    /// 実装先一致フィクスチャとそろえる
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
            GenerateValueObjects = true,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = false,
            GenerateRemoteServices = true,
            ExcludeUnboundedBinaryColumns = true,
            SplitFilesByCategory = false,
        };

    /// <summary>実装先一致フィクスチャと同じ図</summary>
    public static ErDiagram Build() =>
        GeneratedBackendParityFixture.BackendParityFixtureDefinition.Build();
}
