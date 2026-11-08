using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 差分の項目の列の新しい型（<c>SchemaDiffItem.Column</c> の <c>Column.DataType</c>）の読み取りの判定
/// （規則 <c>sync-type-deciding-column-copies</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは <c>DataType</c> の名前の節で、<c>QuickER.Model.Column.DataType</c> に解決し、受け手が
/// <c>QuickER.Provider.SchemaDiffItem.Column</c> に解決するもの（<c>item.Column.DataType</c>・<c>item.Column?.DataType</c>・
/// <c>item.Column!.DataType</c>）。何かは読み取りを含むメソッド（ローカル関数・ラムダは外側のメソッド）の名前。
/// <c>OldColumn</c> の型・live の列の型・図の列の型は拾わない。
/// </para>
/// <para>
/// 読みの限界：<c>var column = item.Column;</c> と別の変数へ取ってから型を読む形は拾わない（変数の型は <c>Column</c> で、
/// 差分の項目から来たかを追わない）。
/// </para>
/// </remarks>
internal static class DiffItemNewTypeReads
{
    private const string ColumnType = "QuickER.Model.Column";

    private const string DiffItemType = "QuickER.Provider.SchemaDiffItem";

    /// <summary>節が差分の項目の列の新しい型の読み取りなら、それを含むメソッドの名前</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node is not IdentifierNameSyntax { Identifier.ValueText: "DataType" } name
            || !IsProperty(model.GetSymbolInfo(name).Symbol, ColumnType, "DataType")
            || Receiver(name) is not { } receiver
            || !IsProperty(model.GetSymbolInfo(receiver).Symbol, DiffItemType, "Column")
        )
        {
            return null;
        }

        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
            ? method.Identifier.ValueText
            : "(member outside a method)";
    }

    /// <summary><c>DataType</c> の受け手の式（<c>x.Column.DataType</c>・<c>x.Column!.DataType</c>・<c>x.Column?.DataType</c>）</summary>
    private static ExpressionSyntax? Receiver(IdentifierNameSyntax name)
    {
        ExpressionSyntax? receiver = name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name => access.Expression,
            MemberBindingExpressionSyntax binding
                when binding.Parent?.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()
                    is { } conditional => conditional.Expression,
            _ => null,
        };

        while (receiver is PostfixUnaryExpressionSyntax suppress)
        {
            receiver = suppress.Operand;
        }

        return receiver;
    }

    private static bool IsProperty(ISymbol? symbol, string containingType, string name) =>
        symbol is IPropertySymbol property
        && property.Name == name
        && property.ContainingType?.ToDisplayString() == containingType;
}
