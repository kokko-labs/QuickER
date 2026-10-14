using AwesomeAssertions;
using QuickER.CodeGen.CSharp.Queries;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>診断文言 <see cref="QueryDiagnosticText"/> が、書式引数の配列を要素ごとに比べる値であることのテストクラス</summary>
public sealed class QueryDiagnosticTextEqualityTests
{
    [Fact(DisplayName = "同じ資源キーと同じ書式引数の診断文言は等しく、ハッシュも同じになる")]
    public void SameKeyAndArguments_AreEqual()
    {
        var left = new QueryDiagnosticText("Key", "a", 1, new QueryDiagnosticText("Inner", "x"));
        var right = new QueryDiagnosticText("Key", "a", 1, new QueryDiagnosticText("Inner", "x"));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact(DisplayName = "資源キーか書式引数が違う診断文言は等しくない")]
    public void DifferentKeyOrArguments_AreNotEqual()
    {
        new QueryDiagnosticText("Key", "a").Should().NotBe(new QueryDiagnosticText("Key", "b"));
        new QueryDiagnosticText("Key", "a").Should().NotBe(new QueryDiagnosticText("Other", "a"));
        new QueryDiagnosticText("Key", "a").Should().NotBe(new QueryDiagnosticText("Key"));
    }
}
