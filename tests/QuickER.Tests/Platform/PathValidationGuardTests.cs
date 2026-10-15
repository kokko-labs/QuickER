using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Platform;

/// <summary>
/// パスの 1 区間・相対パスの妥当性の判定を <see cref="PathSegments"/> だけが持つことを固定する
/// </summary>
/// <remarks>
/// 拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）を構文木で走査し、
/// 名前の判定に使う BCL の呼び出し（<c>Path.GetInvalidFileNameChars</c>・<c>Path.GetInvalidPathChars</c>・<c>Path.IsPathRooted</c>）を数える。
/// 一覧は空で、出現は 1 つでも赤にする。「フルパスか」を見る <c>Path.IsPathFullyQualified</c> と、正規化した結果が
/// フォルダの中かを見る <c>Path.GetFullPath</c> は名前の判定ではないので数えない。
/// </remarks>
public sealed class PathValidationGuardTests
{
    /// <summary>数える BCL の呼び出し</summary>
    private static readonly HashSet<string> Calls =
    [
        nameof(Path.GetInvalidFileNameChars),
        nameof(Path.GetInvalidPathChars),
        nameof(Path.IsPathRooted),
    ];

    [Fact(DisplayName = "パスの名前の判定は QuickER.Platform の外に持たない")]
    public void PathNameChecks_OutsidePlatform_AreNone()
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
                var tree = CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    cancellationToken: TestContext.Current.CancellationToken
                );

                return tree.GetRoot(TestContext.Current.CancellationToken)
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(invocation =>
                        invocation.Expression is MemberAccessExpressionSyntax member
                        && Calls.Contains(member.Name.Identifier.ValueText)
                    )
                    .Select(invocation =>
                        $"{relative}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                    );
            })
            .ToList();

        found
            .Should()
            .BeEmpty("パスの名前の判定は PathSegments の CheckSegment / CheckRelativePath を使う");
    }
}
