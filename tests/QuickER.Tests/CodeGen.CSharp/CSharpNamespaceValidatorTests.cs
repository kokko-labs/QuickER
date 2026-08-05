using AwesomeAssertions;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// <see cref="CSharpNamespaceValidator"/> が名前空間の妥当性を判定する規則を検証するテストクラス
/// </summary>
/// <remarks>
/// このバリデーターは GUI / CLI / MCP の全経路が共有する単一正本のため、
/// ここで通してしまった不正値はそのまま <c>namespace ...;</c> として書き出され、
/// コンパイル不能な生成物になる。境界（空セグメント・予約語）を重点的に固定する。
/// </remarks>
public class CSharpNamespaceValidatorTests
{
    /// <summary>妥当な名前空間が受理されることを検証する</summary>
    [Theory]
    [InlineData("My.App.Data")]
    [InlineData("Generated")]
    [InlineData("_x.y1")]
    [InlineData("A1.B2.C3")]
    [InlineData("__.__")]
    // 文脈キーワードは識別子として合法のため受理する（予約語ではない）
    [InlineData("My.Record.Var")]
    [InlineData("record")]
    [InlineData("var")]
    [InlineData("nint")]
    // 前後の空白は TrimEntries で除去されるため妥当
    [InlineData(" My.App ")]
    [InlineData("My . App")]
    public void IsValid_WithValidNamespace_ReturnsTrue(string value) =>
        CSharpNamespaceValidator.IsValid(value).Should().BeTrue();

    /// <summary>空セグメントを含む名前空間が拒否されることを検証する</summary>
    /// <remarks>
    /// 空セグメントを除去してから検証すると <c>namespace .Foo;</c> のような
    /// コンパイル不能な出力が無警告で書き出される（本テストがその回帰を防ぐ）
    /// </remarks>
    [Theory]
    [InlineData(".Foo")]
    [InlineData("Foo.")]
    [InlineData("Foo..Bar")]
    [InlineData("Foo. .Bar")]
    [InlineData(".")]
    [InlineData("..")]
    public void IsValid_WithEmptySegment_ReturnsFalse(string value) =>
        CSharpNamespaceValidator.IsValid(value).Should().BeFalse();

    /// <summary>C# の予約語をセグメントに含む名前空間が拒否されることを検証する</summary>
    /// <remarks>生成器は識別子を <c>@</c> エスケープしないため、予約語をそのまま出すとコンパイルできない</remarks>
    [Theory]
    [InlineData("class")]
    [InlineData("int")]
    [InlineData("namespace")]
    [InlineData("Foo.class.Bar")]
    [InlineData("My.App.static")]
    [InlineData("void.Foo")]
    public void IsValid_WithReservedKeywordSegment_ReturnsFalse(string value) =>
        CSharpNamespaceValidator.IsValid(value).Should().BeFalse();

    /// <summary>識別子の綴りとして不正な名前空間が拒否されることを検証する</summary>
    [Theory]
    [InlineData("1Invalid.Namespace")]
    [InlineData("My-App")]
    [InlineData("My App")]
    [InlineData("My.@App")]
    [InlineData("My.App;")]
    public void IsValid_WithMalformedIdentifier_ReturnsFalse(string value) =>
        CSharpNamespaceValidator.IsValid(value).Should().BeFalse();

    /// <summary>null・空白は不正扱いになることを検証する（呼び出し側が既定値へフォールバックする前提）</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_WithNullOrWhiteSpace_ReturnsFalse(string? value) =>
        CSharpNamespaceValidator.IsValid(value).Should().BeFalse();
}
