using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 書式のカルチャで結果が変わる文字列の組み立て（数値・日時の補間の穴と文字列の連結の被演算子）の判定
/// （規則 <c>numeric-interpolation-culture</c> の見つける形）
/// </summary>
internal static class CultureDependentText
{
    /// <summary>日時の型（<see cref="SpecialType"/> を持たないものは完全名で判定する）</summary>
    private static readonly HashSet<string> DateTypeNames = new(StringComparer.Ordinal)
    {
        "System.DateTimeOffset",
        "System.DateOnly",
        "System.TimeOnly",
        "System.TimeSpan",
    };

    /// <summary>節が拾う形ならその表記（節のテキスト。補間の穴は <c>{…}</c>・連結は被演算子の式）</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node) =>
        IsHit(model, node) ? node.ToString() : null;

    /// <summary>
    /// 節が、カルチャで書式の変わる値を文字列へ埋める箇所（数値・日時の型の補間の穴、または文字列の連結の被演算子）か
    /// </summary>
    /// <remarks>補間の穴は <see cref="InterpolationSyntax"/>、連結は被演算子の式の節で拾う</remarks>
    public static bool IsHit(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            InterpolationSyntax interpolation => IsCultureFormatted(model, interpolation.Expression)
                && !IsInvariantCreate(model, interpolation),
            ExpressionSyntax expression
                when expression.Parent is BinaryExpressionSyntax binary
                    && binary.IsKind(SyntaxKind.AddExpression)
                    && IsStringConcatenation(model, binary) => IsCultureFormatted(
                model,
                expression
            ),
            _ => false,
        };

    /// <summary>式の型が数値・日時（<see cref="Nullable{T}"/> を含む）か</summary>
    private static bool IsCultureFormatted(SemanticModel model, ExpressionSyntax expression)
    {
        var type = model.GetTypeInfo(expression).Type;

        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
        )
        {
            type = nullable.TypeArguments[0];
        }

        return type is not null
            && (
                type.SpecialType
                    is SpecialType.System_Byte
                        or SpecialType.System_SByte
                        or SpecialType.System_Int16
                        or SpecialType.System_UInt16
                        or SpecialType.System_Int32
                        or SpecialType.System_UInt32
                        or SpecialType.System_Int64
                        or SpecialType.System_UInt64
                        or SpecialType.System_Decimal
                        or SpecialType.System_Double
                        or SpecialType.System_Single
                        or SpecialType.System_DateTime
                || DateTypeNames.Contains(type.ToDisplayString())
            );
    }

    /// <summary>加算の演算子が文字列の連結か（どちらかの被演算子が文字列）</summary>
    private static bool IsStringConcatenation(SemanticModel model, BinaryExpressionSyntax binary) =>
        model.GetSymbolInfo(binary).Symbol
            is IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String };

    /// <summary>
    /// 補間の穴を含む補間文字列が、<c>string.Create</c> の第 1 引数に <c>CultureInfo.InvariantCulture</c> を渡した呼び出しの引数か
    /// </summary>
    private static bool IsInvariantCreate(SemanticModel model, InterpolationSyntax interpolation)
    {
        if (
            interpolation.Parent
            is not InterpolatedStringExpressionSyntax
            {
                Parent: ArgumentSyntax
                {
                    Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation },
                },
            }
        )
        {
            return false;
        }

        if (
            SourceSymbolScan.ResolveMethod(model, invocation)
                is not { Name: nameof(string.Create) } method
            || method.ContainingType?.SpecialType != SpecialType.System_String
            || invocation.ArgumentList.Arguments.Count == 0
        )
        {
            return false;
        }

        return model.GetSymbolInfo(invocation.ArgumentList.Arguments[0].Expression).Symbol
                is IPropertySymbol { Name: "InvariantCulture" } property
            && property.ContainingType?.ToDisplayString() == "System.Globalization.CultureInfo";
    }
}
