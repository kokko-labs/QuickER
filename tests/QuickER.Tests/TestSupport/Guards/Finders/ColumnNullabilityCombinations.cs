using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 同じ列の <c>Column.IsPrimaryKey</c> と <c>Column.IsNullable</c> を 1 つの式で組み合わせる節の判定
/// （規則 <c>column-nullability-copies</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 候補の節は論理の <c>&amp;&amp;</c> / <c>||</c>・条件式・プロパティパターン・<c>and</c> / <c>or</c> のパターン・switch 式・
/// switch 文。節の中で
/// <c>QuickER.Model.Column</c> の 2 つのプロパティに解決する読み取りを受け手ごとに集め、同じ受け手が両方を読む節を拾う。
/// 拾う節の子孫にも拾う節があれば外側は数えない（最小の節を 1 件）。
/// </para>
/// <para>
/// 受け手は、名前 1 つの式ならそのシンボル（別々のラムダの同じ名前の引数を同じ列とみなさない）、それ以外は空白を除いた式の綴り、
/// <c>x?.</c> は <c>x</c>、パターンは入力（<c>is</c> の左辺・switch の対象の式が取れればそのキー＝<c>c is { … } &amp;&amp; c.IsNullable</c>
/// を同じ列とみなす。取れなければパターンの位置。<c>and</c> / <c>or</c> で並ぶパターンは同じ入力）、修飾の無い読み取りは <c>this</c>。
/// </para>
/// <para>
/// 読みの限界：文をまたぐ組み合わせ（<c>if (c.IsPrimaryKey) return false; return c.IsNullable;</c>）と、
/// 片方を別の変数へ取ってから組む形は拾わない。
/// </para>
/// </remarks>
internal static class ColumnNullabilityCombinations
{
    private const string ColumnType = "QuickER.Model.Column";

    private const string PrimaryKey = "IsPrimaryKey";

    private const string Nullable = "IsNullable";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>節が拾う最小の節なら、本文の空白を 1 つに詰めたもの</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (!Qualifies(model, node))
        {
            return null;
        }

        return node.DescendantNodes().Any(inner => Qualifies(model, inner))
            ? null
            : Whitespace.Replace(node.ToString(), " ");
    }

    /// <summary>節が <c>Column.IsNullable</c> の読み取りなら <c>Column.IsNullable</c>（空振りの検知の候補）</summary>
    public static string? DescribeNullableRead(SemanticModel model, SyntaxNode node) =>
        node is IdentifierNameSyntax { Identifier.ValueText: Nullable } name
        && IsColumnProperty(model, name)
            ? "Column.IsNullable"
            : null;

    /// <summary>候補の節で、同じ受け手が 2 つのプロパティを両方読むか</summary>
    private static bool Qualifies(SemanticModel model, SyntaxNode node)
    {
        if (
            !node.IsKind(SyntaxKind.LogicalAndExpression)
            && !node.IsKind(SyntaxKind.LogicalOrExpression)
            && !node.IsKind(SyntaxKind.ConditionalExpression)
            && !node.IsKind(SyntaxKind.RecursivePattern)
            && !node.IsKind(SyntaxKind.AndPattern)
            && !node.IsKind(SyntaxKind.OrPattern)
            && !node.IsKind(SyntaxKind.SwitchExpression)
            && !node.IsKind(SyntaxKind.SwitchStatement)
        )
        {
            return false;
        }

        var reads = node.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText is PrimaryKey or Nullable)
            .Where(name => IsColumnProperty(model, name))
            .Select(name => (Receiver: ReceiverKey(model, name), name.Identifier.ValueText))
            .ToList();

        return reads
            .Where(read => read.ValueText == PrimaryKey)
            .Any(primaryKey =>
                reads.Any(read =>
                    read.ValueText == Nullable && read.Receiver == primaryKey.Receiver
                )
            );
    }

    /// <summary>名前が <c>QuickER.Model.Column</c> のプロパティに解決するか</summary>
    private static bool IsColumnProperty(SemanticModel model, IdentifierNameSyntax name) =>
        model.GetSymbolInfo(name).Symbol is IPropertySymbol property
        && property.ContainingType.ToDisplayString() == ColumnType;

    /// <summary>読み取りの受け手の照合用のキー</summary>
    private static string ReceiverKey(SemanticModel model, IdentifierNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => ExpressionKey(
                model,
                access.Expression
            ),
            MemberBindingExpressionSyntax binding
                when binding.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()
                    is { } conditional => ExpressionKey(model, conditional.Expression),
            NameColonSyntax or ExpressionColonSyntax => PatternInputKey(model, name),
            _ => "this",
        };

    /// <summary>名前 1 つの式はシンボル（宣言の位置つき）、それ以外は空白を除いた綴り</summary>
    private static string ExpressionKey(SemanticModel model, ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax && model.GetSymbolInfo(expression).Symbol is { } symbol
            ? $"{symbol.Kind}:{symbol.Name}@{symbol.Locations.FirstOrDefault()?.SourceSpan.Start}"
            : Whitespace.Replace(expression.ToString(), string.Empty);

    /// <summary>
    /// プロパティパターンの入力（<c>and</c> / <c>or</c>・括弧・<c>not</c> で並ぶパターンは同じ入力）。<c>is</c> の左辺か
    /// switch の対象の式が取れればその式のキー（メンバー参照の受け手と同じ列とみなす）、取れなければパターンの位置
    /// </summary>
    private static string PatternInputKey(SemanticModel model, IdentifierNameSyntax name)
    {
        SyntaxNode? pattern = name.FirstAncestorOrSelf<RecursivePatternSyntax>();

        while (
            pattern?.Parent
                is BinaryPatternSyntax
                    or ParenthesizedPatternSyntax
                    or UnaryPatternSyntax
        )
        {
            pattern = pattern.Parent;
        }

        var input = pattern?.Parent switch
        {
            IsPatternExpressionSyntax isPattern => isPattern.Expression,
            SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax switchExpression } =>
                switchExpression.GoverningExpression,
            CasePatternSwitchLabelSyntax
            {
                Parent: SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement },
            } => switchStatement.Expression,
            _ => null,
        };

        return input is not null ? ExpressionKey(model, input) : $"pattern@{pattern?.SpanStart}";
    }
}
