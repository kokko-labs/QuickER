using QuickER.CodeGen.CSharp;
using QuickER.Model;

namespace QuickER.Tests.GeneratedTranslatorParityEfCoreFixture;

/// <summary>
/// 翻訳器の一致のフィクスチャ（<see cref="Tests.GeneratedTranslatorParityFixture.TranslatorParityFixtureDefinition"/>）と
/// 同じ図を、<b>SQL Server 方言の EF Core 版 Repository 単独</b>で生成する固定フィクスチャの単一ソース。
/// </summary>
/// <remarks>
/// <para>
/// EF Core はマルチターゲットと生成時エラーで排他になるので、翻訳器の一致のフィクスチャには入らない。
/// 述語の表（<c>TranslatorParityPredicateRows</c>）を EF Core 版でも流すために、同じ図をこのフィクスチャへ別に生成し、
/// <c>TranslatorParityEfCoreRuntimeTestsBase</c> が述語の型をこの名前空間へ付け替え、派生が実 SQL Server と SQLite で比べる
/// （生成の EF Core のコードはプロバイダに依らないので、SQL Server 方言で生成したこのフィクスチャを SQLite でも流す）。
/// </para>
/// <para>
/// 図は型の違う外部キー（主キーが bigint・外部キーが int）を外した <c>BuildWithoutCrossTypeForeignKey</c>。
/// EF Core のモデルの検証は外部キーと参照先の主キーの CLR の型の一致を求め、この組では DbContext を組めない
/// （SQL Server もこの組の外部キー制約を作れない）。列 <c>big_parent_id</c> はそのまま残る。
/// </para>
/// </remarks>
public static class TranslatorParityEfCoreFixtureDefinition
{
    /// <summary>生成フィクスチャの契約 namespace（既存フィクスチャと衝突しない専用 namespace）</summary>
    public const string NamespaceName = "QuickER.Tests.GeneratedTranslatorParityEfCoreFixture";

    /// <summary>コミット済みフィクスチャファイル名</summary>
    public const string OutputFileName = "TranslatorParityEfCoreFixture.g.cs";

    /// <summary>
    /// フィクスチャ生成に用いる決定的なオプション。SQL Server 方言の EF Core 版 Repository 単独の最小構成。
    /// </summary>
    public static CodeGenerationOptions Options { get; } =
        new()
        {
            RootNamespace = NamespaceName,
            OutputFileName = OutputFileName,
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
            GenerateValueObjects = false,
            GenerateEfCoreRepositories = true,
            GenerateInMemoryRepositories = false,
            SplitFilesByCategory = false,
        };

    /// <summary>翻訳器の一致のフィクスチャから型の違う外部キーを外した図を返す</summary>
    public static ErDiagram Build() =>
        Tests.GeneratedTranslatorParityFixture.TranslatorParityFixtureDefinition.BuildWithoutCrossTypeForeignKey();
}
