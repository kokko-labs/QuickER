using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedRemoteServiceFixture;

/// <summary>
/// リモートサービス生成（GenerateRemoteServices）の固定フィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図はクエリフィクスチャ（<see cref="Tests.GeneratedQueryFixture.QueryFixtureDefinition"/>）と
/// 同一（2 エンティティ＋名前付きクエリ 10 本・VO 有効）で、オプションに <c>GenerateRemoteServices = true</c> を
/// 加えたもの（リモート面は自動含意）。出力は本体（クライアント実装同梱）＋サーバー実装の 2 ファイルで、
/// どちらもチェックインしてドリフト検知＋テストプロジェクトでの実コンパイル検証を兼ねる。
/// </para>
/// <para>
/// SQLite 方言のQuickER 版 Repository＋EF Core 併存のため、実ファイル DB（Docker 不要＝CI 常時実行）の
/// in-process HTTP end-to-end テスト（RemoteServiceRuntimeTests）が両バックエンドでこの生成物を検証できる。
/// </para>
/// </remarks>
public static class RemoteServiceFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedRemoteServiceFixture";

    /// <summary>コミット済みフィクスチャファイル名（本体）</summary>
    public const string OutputFileName = "RemoteServiceFixture.g.cs";

    /// <summary>コミット済みフィクスチャファイル名（サーバー実装）</summary>
    public const string ServerOutputFileName = "RemoteServiceFixture.RemoteServer.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（SQLite 方言・QuickER＋EF Core 併存・VO 有効・リモートサービス生成）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            NamespaceName = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEntityClasses = true,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCore = true,
            RepositoryDialect = "sqlite",
            GenerateRemoteServices = true,
            SplitFilesByCategory = false,
        };

    /// <summary>クエリフィクスチャと同一の図（customers / orders＋名前付きクエリ）を返す</summary>
    public static ErDiagram Build() => Tests.GeneratedQueryFixture.QueryFixtureDefinition.Build();
}
