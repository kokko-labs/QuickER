using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// UNIQUE 制約ベースの重複事前チェックのうち、ランタイムの出力（<see cref="RuntimePackageSourceRenderer"/>）の
/// 照合の呼び出しだけを検証する。生成された Entity の <c>[UniqueConstraint]</c> 属性・EditModel の制約テーブル・
/// <c>CheckUniquenessAsync</c> / <c>ValidateUniqueAsync</c> の振る舞いは、決まった行（<c>CodegenCaseTests</c>）の
/// <c>code-uniqueness-*</c> / <c>exec-uniqueness-*</c> が見る。
/// </summary>
public class UniquenessGenerationTests
{
    /// <summary>
    /// QuickER 版 Repository の事前チェックは、方言の UNIQUE 制約の NULL の規則を共有の照合へ渡す
    /// （SQL Server は NULL を値として比べる・SQLite は NULL を照合から外す）。EF Core とインメモリは照合へ規則を渡さない
    /// ＝従来どおり NULL を含む組を外す。
    /// </summary>
    [Fact(
        DisplayName = "事前チェックは SQL Server の QuickER 版だけ NULL を値として比べ、SQLite・EF Core・インメモリは外す"
    )]
    public void CheckUniqueness_PassesDialectNullRule()
    {
        var renderer = new RuntimePackageSourceRenderer();

        CheckUniquenessBody(renderer.RenderSqlServer()).Should().Contain("nullValuesCollide: true");
        CheckUniquenessBody(renderer.RenderSqlite()).Should().Contain("nullValuesCollide: false");
        CheckUniquenessBody(renderer.RenderEfCore()).Should().NotContain("nullValuesCollide");
        CheckUniquenessBody(renderer.RenderInMemory()).Should().NotContain("nullValuesCollide");
    }

    /// <summary>実装の <c>CheckUniquenessAsync</c> の本体を切り出す</summary>
    private static string CheckUniquenessBody(string content)
    {
        var start = content.IndexOf(
            "public async Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(",
            StringComparison.Ordinal
        );
        start.Should().BeGreaterThan(0);
        var end = content.IndexOf("return violations;", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return content[start..end];
    }
}
