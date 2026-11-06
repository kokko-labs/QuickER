using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>プロセスの起動の判定（規則 <c>process-launches</c> の見つける形）</summary>
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
