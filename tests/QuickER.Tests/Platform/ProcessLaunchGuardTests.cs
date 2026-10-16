using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary>
/// 外部のプロセス・URL・フォルダを開く処理は <see cref="ExternalLauncher"/> を通すことを固定する
/// </summary>
/// <remarks>
/// <para>
/// 拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外のプロジェクトを <see cref="SourceSymbolScan"/> でコンパイルとして組み、シンボルで
/// 判定する（目的型の <c>new()</c>・<c>using static</c>・別名も同じ API として拾う）。数えるのは次のもので、一覧は空＝1 つでも赤。
/// </para>
/// <list type="bullet">
/// <item><see cref="Process.Start()"/> の静的な多重定義の呼び出し</item>
/// <item>インスタンスの <see cref="Process.Start()"/> のうち、受け手が <see cref="ExternalLauncher.CreateProcess"/> の結果で初期化した
/// 局所変数でないもの（起動の前の規則は <c>CreateProcess</c> が確かめるので、その戻り値を起動するのだけを許す）</item>
/// <item><see cref="Process"/> の生成</item>
/// <item><see cref="ProcessStartInfo.UseShellExecute"/> への、定数の <c>false</c> でない値の代入（初期化子を含む。値が定数でないものも数える）</item>
/// <item>P/Invoke（<c>[DllImport]</c> / <c>[LibraryImport]</c>）で、入口の名前が <c>ShellExecute</c>・<c>CreateProcess</c>・<c>WinExec</c> で
/// 始まるもの</item>
/// </list>
/// <para>
/// メソッド・型を解決できない呼び出し・生成は、数えない側へ倒さずに「未解決」として同じ一覧に出す。QuickER がプロセスを作らないもの
/// （GitHub.Copilot.SDK の CLI の起動・Velopack の更新・WebView2 の子プロセス）は対象外。
/// </para>
/// </remarks>
public sealed class ProcessLaunchGuardTests
{
    [Fact(DisplayName = "プロセスの起動は QuickER.Platform の外に持たない")]
    public void ProcessLaunches_OutsidePlatform_AreNone()
    {
        var found = ScanOutsidePlatform((model, node) => ProcessLaunchCalls.Describe(model, node));

        string.Join(Environment.NewLine, found)
            .Should()
            .BeEmpty("プロセス・URL・フォルダを開くのは ExternalLauncher を通す");
    }

    [Fact(
        DisplayName = "走査は CreateProcess の戻り値の起動を許し、いまの 4 か所を数えない（空振りしていない）"
    )]
    public void CreateProcessStarts_AreRecognized()
    {
        var starts = ScanOutsidePlatform(
            (model, node) =>
                node is InvocationExpressionSyntax invocation
                && ProcessLaunchCalls.IsLauncherProcessStart(model, invocation)
                    ? "start"
                    : null
        );

        starts
            .Should()
            .BeEquivalentTo(
                "src/QuickER.AI/ClaudeCodeClient.cs: start",
                "src/QuickER.AI/ClaudeCodeClient.cs: start",
                "src/QuickER.AI/CodexAppServerClient.cs: start",
                "src/QuickER.AI.Mock/DotnetBuildRunner.cs: start"
            );
    }

    private static List<string> ScanOutsidePlatform(
        Func<SemanticModel, SyntaxNode, string?> describe
    ) =>
        SourceSymbolScan
            .SrcProjects.Where(project => project.Name != "QuickER.Platform")
            .SelectMany(project =>
                project.Files.SelectMany(file =>
                {
                    var model = project.Compilation.GetSemanticModel(file.Tree);

                    return file
                        .Tree.GetRoot(TestContext.Current.CancellationToken)
                        .DescendantNodes()
                        .Select(node => describe(model, node))
                        .OfType<string>()
                        .Select(launch => $"{file.RelativePath}: {launch}");
                })
            )
            .ToList();

    /// <summary>ガードが拾うべき書き方と、拾わない書き方</summary>
    private const string ProbeSource = """
        using System;
        using System.Diagnostics;
        using System.Runtime.InteropServices;
        using QuickER.Platform;
        using static System.Diagnostics.Process;
        using P = System.Diagnostics.Process;

        internal static partial class Probe
        {
            public static void Hits(string path, bool shell, ProcessStartInfo info)
            {
                Process.Start(path);
                Start(path, "arg");
                P.Start(new ProcessStartInfo(path));
                Process a = new();
                var b = new P();
                b.Start();
                info.UseShellExecute = true;
                info.UseShellExecute = shell;
                _ = new ProcessStartInfo(path) { UseShellExecute = !false };
            }

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr ShellExecuteW(IntPtr h, string op, string file, string args, string dir, int show);

            [DllImport("kernel32.dll", EntryPoint = "CreateProcessW")]
            private static extern bool Spawn(string app);

            [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
            private static partial uint WinExec(string command, uint show);

            public static void Misses(string path, ProcessStartInfo info)
            {
                using var process = ExternalLauncher.CreateProcess(info);
                process.Start();
                info.UseShellExecute = false;
                _ = new ProcessStartInfo(path) { UseShellExecute = false };
                ExternalLauncher.OpenUrl(new Uri("https://example.com/"));
            }

            [DllImport("user32.dll")]
            private static extern bool SetForegroundWindow(IntPtr hWnd);
        }
        """;

    [Fact(
        DisplayName = "走査はシンボルで判定し、using static・別名・目的型の new・非定数の代入・P/Invoke も拾う"
    )]
    public void Describe_FindsEveryLaunchForm()
    {
        var (compilation, tree) = SourceSymbolScan.CompileProbe(ProbeSource);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);

        // プローブはソース生成器を通さないので、LibraryImport の部分メソッドの実装が無い（CS8795）ことだけは除く
        compilation
            .GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795"
            )
            .Should()
            .BeEmpty();

        string[] HitsIn(string method) =>
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(declaration => declaration.Identifier.ValueText == method)
                .DescendantNodes()
                .Select(node => ProcessLaunchCalls.Describe(model, node))
                .OfType<string>()
                .ToArray();

        HitsIn("Hits")
            .Should()
            .Equal(
                "Process.Start",
                "Process.Start",
                "Process.Start",
                "new Process",
                "new Process",
                "Process.Start（CreateProcess の戻り値でない）",
                "UseShellExecute = true",
                "UseShellExecute = (定数でない)",
                "UseShellExecute = true"
            );
        HitsIn("Misses").Should().BeEmpty();
        root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(method => ProcessLaunchCalls.Describe(model, method))
            .OfType<string>()
            .Should()
            .Equal("P/Invoke ShellExecuteW", "P/Invoke CreateProcessW", "P/Invoke WinExec");
    }
}

/// <summary>プロセスの起動の判定（<see cref="ProcessLaunchGuardTests"/> の走査）</summary>
internal static class ProcessLaunchCalls
{
    private const string ProcessType = "System.Diagnostics.Process";

    private static readonly string[] ForbiddenEntryPoints =
    [
        "ShellExecute",
        "CreateProcess",
        "WinExec",
    ];

    /// <summary>節が数える起動ならその表記</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            InvocationExpressionSyntax invocation => DescribeInvocation(model, invocation),
            BaseObjectCreationExpressionSyntax creation => DescribeCreation(model, creation),
            AssignmentExpressionSyntax assignment => DescribeAssignment(model, assignment),
            MethodDeclarationSyntax method => DescribeImport(model, method),
            _ => null,
        };

    /// <summary>
    /// インスタンスの <c>Start()</c> で、受け手が <see cref="ExternalLauncher.CreateProcess"/> の結果で初期化した局所変数か
    /// </summary>
    public static bool IsLauncherProcessStart(
        SemanticModel model,
        InvocationExpressionSyntax invocation
    ) =>
        SourceSymbolScan.ResolveMethod(model, invocation)
            is { Name: nameof(Process.Start), IsStatic: false } method
        && SourceSymbolScan.ContainingTypeName(method) == ProcessType
        && invocation.Expression is MemberAccessExpressionSyntax { Expression: var receiver }
        && model.GetSymbolInfo(receiver).Symbol is ILocalSymbol local
        && local.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax()
                is VariableDeclaratorSyntax
                {
                    Initializer.Value: InvocationExpressionSyntax initializer,
                }
            && SourceSymbolScan.ResolveMethod(model, initializer) is { } created
            && created.Name == nameof(ExternalLauncher.CreateProcess)
            && SourceSymbolScan.ContainingTypeName(created) == "QuickER.Platform.ExternalLauncher"
        );

    private static string? DescribeInvocation(
        SemanticModel model,
        InvocationExpressionSyntax invocation
    )
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

        if (name != nameof(Process.Start))
        {
            return null;
        }

        var method = SourceSymbolScan.ResolveMethod(model, invocation);

        if (method is null)
        {
            return "未解決 Start";
        }

        if (SourceSymbolScan.ContainingTypeName(method) != ProcessType)
        {
            return null;
        }

        if (method.IsStatic)
        {
            return "Process.Start";
        }

        return IsLauncherProcessStart(model, invocation)
            ? null
            : "Process.Start（CreateProcess の戻り値でない）";
    }

    private static string? DescribeCreation(
        SemanticModel model,
        BaseObjectCreationExpressionSyntax creation
    )
    {
        var type = model.GetTypeInfo(creation).Type;

        if (type is null or IErrorTypeSymbol)
        {
            return
                creation is ObjectCreationExpressionSyntax { Type: var syntax }
                && syntax.ToString().EndsWith(nameof(Process), StringComparison.Ordinal)
                ? "未解決 new Process"
                : null;
        }

        return type.ToDisplayString() == ProcessType ? "new Process" : null;
    }

    private static string? DescribeAssignment(
        SemanticModel model,
        AssignmentExpressionSyntax assignment
    )
    {
        if (
            model.GetSymbolInfo(assignment.Left).Symbol
                is not IPropertySymbol { Name: nameof(ProcessStartInfo.UseShellExecute) } property
            || property.ContainingType.ToDisplayString() != "System.Diagnostics.ProcessStartInfo"
        )
        {
            return null;
        }

        var value = model.GetConstantValue(assignment.Right);

        if (!value.HasValue)
        {
            return "UseShellExecute = (定数でない)";
        }

        return value.Value is false ? null : "UseShellExecute = true";
    }

    private static string? DescribeImport(SemanticModel model, MethodDeclarationSyntax method)
    {
        if (model.GetDeclaredSymbol(method) is not { } symbol)
        {
            return null;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();

            if (
                attributeName
                is not (
                    "System.Runtime.InteropServices.DllImportAttribute"
                    or "System.Runtime.InteropServices.LibraryImportAttribute"
                )
            )
            {
                continue;
            }

            var entryPoint =
                attribute
                    .NamedArguments.Where(argument => argument.Key == "EntryPoint")
                    .Select(argument => argument.Value.Value as string)
                    .FirstOrDefault()
                ?? symbol.Name;

            return ForbiddenEntryPoints.Any(prefix =>
                entryPoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            )
                ? $"P/Invoke {entryPoint}"
                : null;
        }

        return null;
    }
}
