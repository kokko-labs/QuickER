using System.IO;
using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary>
/// UTF-8 の文字コードのインスタンスは <see cref="TextEncodings"/> だけが作ることを固定する
/// </summary>
/// <remarks>
/// <para>
/// 同じ意味の文字コード（BOM なし・不正なサロゲートの扱い）を呼び出し側で作ると、1 か所だけ BOM の有無や
/// 不正なサロゲートの扱いが食い違う。書き出した内容のハッシュのように、書き出しと同じバイト列を作る必要がある側は、
/// 食い違うと黙って一致しなくなる。
/// </para>
/// <para>
/// 拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）を構文木で走査し、
/// <c>new UTF8Encoding(</c> と、宣言した型が <c>UTF8Encoding</c> の <c>new(</c> を数える。一覧は空で、出現は 1 つでも赤にする。
/// BOM 付きの <see cref="Encoding.UTF8"/> は BCL の既存のインスタンスなので数えない。
/// </para>
/// </remarks>
public sealed class Utf8EncodingGuardTests
{
    [Fact(DisplayName = "UTF8Encoding のインスタンスを QuickER.Platform の外で作らない")]
    public void Utf8EncodingCreations_OutsidePlatform_AreNone()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var platform = Path.Combine(src, "QuickER.Platform") + Path.DirectorySeparatorChar;
        var found = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(platform, StringComparison.OrdinalIgnoreCase))
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")
            )
            .SelectMany(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var variants = ConditionalSourceParse.Parse(
                    File.ReadAllText(path),
                    cancellationToken: TestContext.Current.CancellationToken
                );

                return ConditionalSourceParse
                    .DescendantNodes(variants, TestContext.Current.CancellationToken)
                    .Where(CreatesUtf8Encoding)
                    .Select(node =>
                        $"{relative}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                    );
            })
            .ToList();

        found
            .Should()
            .BeEmpty("UTF-8 の文字コードは TextEncodings の Utf8NoBom か Utf8NoBomLenient を使う");
    }

    /// <summary>節が UTF8Encoding のインスタンスを作るか</summary>
    private static bool CreatesUtf8Encoding(SyntaxNode node) =>
        node switch
        {
            ObjectCreationExpressionSyntax creation => creation.Type.ToString().Split('.')[^1]
                == nameof(UTF8Encoding),
            ImplicitObjectCreationExpressionSyntax implicitCreation => DeclaredType(
                implicitCreation
            )
                ?.ToString()
                .Split('.')[^1] == nameof(UTF8Encoding),
            _ => false,
        };

    /// <summary>型を省いた <c>new(</c> が初期化する変数・プロパティの宣言の型</summary>
    private static TypeSyntax? DeclaredType(ImplicitObjectCreationExpressionSyntax creation) =>
        creation.Parent?.Parent switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } =>
                declaration.Type,
            PropertyDeclarationSyntax property => property.Type,
            _ => null,
        };
}
