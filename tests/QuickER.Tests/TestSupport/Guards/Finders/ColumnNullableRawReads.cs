using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 列の NULL 許容の印（<c>QuickER.Model.Column.IsNullable</c>）の読み取りの判定
/// （規則 <c>provider-column-nullability-raw-reads</c>・<c>mcp-tools-column-nullability-raw-reads</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// 拾うのは <c>IsNullable</c> の名前の節で、<c>QuickER.Model.Column.IsNullable</c> に解決する読み取り
/// （<c>c.IsNullable</c>・<c>!c.IsNullable</c>・<c>c?.IsNullable</c>・パターン <c>{ IsNullable: true }</c>・代入の右辺）。
/// 書き込み（代入の左辺・オブジェクト初期化子と <c>with</c> の左辺）は拾わない。何かは読み取りを含むメソッド
/// （ローカル関数・ラムダは外側のメソッド。メソッドの外はコンストラクターなどの名前の代わりに固定の文字列）の名前。
/// </para>
/// <para>
/// live の列（取込が主キーの列の印を false にそろえる）と図の列（主キーの列に印が立ちうる）を見分けない。
/// live の列では <c>ColumnNullability.AllowsNull</c> と印が同じ値なので、どちらの列も判定を通して読めば足りる。
/// </para>
/// <para>読みの限界：<c>nameof(Column.IsNullable)</c> とリフレクションでの読み取りは拾わない（シンボルの読み取りでない）。</para>
/// </remarks>
internal static class ColumnNullableRawReads
{
    private const string ColumnType = "QuickER.Model.Column";

    private const string Nullable = "IsNullable";

    /// <summary>節が NULL 許容の印の読み取りなら、それを含むメソッドの名前</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node)
    {
        if (
            node is not IdentifierNameSyntax { Identifier.ValueText: Nullable } name
            || model.GetSymbolInfo(name).Symbol is not IPropertySymbol { Name: Nullable } property
            || property.ContainingType?.ToDisplayString() != ColumnType
            || IsInsideNameOf(name)
            || IsWriteTarget(name)
        )
        {
            return null;
        }

        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method
            ? method.Identifier.ValueText
            : "(member outside a method)";
    }

    /// <summary>名前が代入の左辺（<c>c.IsNullable = …</c>・初期化子と <c>with</c> の <c>IsNullable = …</c>）か</summary>
    private static bool IsWriteTarget(IdentifierNameSyntax name)
    {
        SyntaxNode target =
            name.Parent is MemberAccessExpressionSyntax access && access.Name == name
                ? access
                : name;

        return target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target;
    }

    /// <summary>名前が <c>nameof(...)</c> の中か</summary>
    private static bool IsInsideNameOf(IdentifierNameSyntax name) =>
        name.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            );
}
