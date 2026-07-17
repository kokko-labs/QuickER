using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedRemoteContractFixture;

/// <summary>
/// リモート契約生成（GenerateRemoteContracts）の固定フィクスチャを生成する単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// 入力の ER 図はクエリフィクスチャ（<see cref="Tests.GeneratedQueryFixture.QueryFixtureDefinition"/>）と
/// 同一（2 エンティティ＋名前付きクエリ 13 本・VO 有効）で、オプションだけに
/// <c>GenerateRemoteContracts = true</c> を加えたもの。SQLite 方言のQuickER 版 Repository＋EF Core 併存のため、
/// リモート面（I{Entity}RemoteRepository）・全機能面（I{Entity}Repository）・両面 DI 登録を
/// QuickER・EF Core の両実装で実ファイル DB（Docker 不要＝CI 常時実行）検証できる。
/// </para>
/// <para>
/// クエリフィクスチャ（リモート面なし）との対は「同一の図・同一のクエリでリモート契約生成だけが違う」比較対照でもあり、
/// 名前付きクエリがリモート面へ載ること・全機能面が従来の利用感のまま残ることをドリフト検知で固定する。
/// </para>
/// </remarks>
public static class RemoteContractFixtureDefinition
{
    /// <summary>生成フィクスチャの名前空間（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedRemoteContractFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "RemoteContractFixture.g.cs";

    /// <summary>フィクスチャ生成に用いる決定的なオプション（SQLite 方言・QuickER＋EF Core 併存・VO 有効・リモート契約生成）</summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateValueObjects = true,
            GenerateEfCore = true,
            RepositoryDialects = ["sqlite"],
            GenerateRemoteContracts = true,
            SplitFilesByCategory = false,
        };

    /// <summary>クエリフィクスチャと同一の図（customers / orders＋名前付きクエリ）を返す</summary>
    public static ErDiagram Build() => Tests.GeneratedQueryFixture.QueryFixtureDefinition.Build();
}
