using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Platform;

/// <summary>
/// 外部のプロセス・URL・フォルダを開く処理は <see cref="ExternalLauncher"/> を通すことを固定する
/// </summary>
/// <remarks>
/// <para>
/// 拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）を構文木で走査し、
/// 静的な <c>Process.Start(</c> の呼び出し・<c>new Process(</c> / <c>new Process {</c>（<see cref="Process"/> の生成）・
/// <c>UseShellExecute = true</c> の代入（初期化子を含む）を数える。一覧は空で、出現は 1 つでも赤にする。
/// </para>
/// <para>
/// <see cref="ProcessStartInfo"/> を組み立てるのは許す（引数・環境変数・作業フォルダは呼び出し側が決める）。起動するのは
/// <see cref="ExternalLauncher.CreateProcess"/> が返した <see cref="Process"/> の <c>Start()</c> だけになる（インスタンスの
/// <c>Start()</c> は構文では見分けられないが、<see cref="Process"/> を作る経路が 1 つなので、そこで起動の前の規則を確かめられる）。
/// QuickER がプロセスを作らないもの（GitHub.Copilot.SDK の CLI の起動・Velopack の更新・WebView2 の子プロセス）は対象外。
/// </para>
/// </remarks>
public sealed class ProcessLaunchGuardTests
{
    [Fact(DisplayName = "プロセスの起動は QuickER.Platform の外に持たない")]
    public void ProcessLaunches_OutsidePlatform_AreNone()
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
                    .Select(Describe)
                    .OfType<string>()
                    .Select(launch => $"{relative}: {launch}");
            })
            .ToList();

        found.Should().BeEmpty("プロセス・URL・フォルダを開くのは ExternalLauncher を通す");
    }

    /// <summary>数える起動なら、その表記を返す</summary>
    private static string? Describe(SyntaxNode node) =>
        node switch
        {
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: nameof(Process.Start),
                } member,
            } when LastName(member.Expression) == nameof(Process) => "Process.Start",
            ObjectCreationExpressionSyntax creation
                when LastName(creation.Type) == nameof(Process) => "new Process",
            AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax or MemberAccessExpressionSyntax,
                Right: LiteralExpressionSyntax right,
            } assignment
                when LastName(assignment.Left) == nameof(ProcessStartInfo.UseShellExecute)
                    && right.IsKind(SyntaxKind.TrueLiteralExpression) => "UseShellExecute = true",
            _ => null,
        };

    /// <summary>名前の最後の識別子（<c>System.Diagnostics.Process</c> なら <c>Process</c>）</summary>
    private static string? LastName(SyntaxNode node) =>
        node switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => null,
        };
}
