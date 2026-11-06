using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ファイルの書き込み・削除を表す呼び出しの判定（規則 <c>raw-file-writes</c> の見つける形）</summary>
internal static class FileWriteCalls
{
    /// <summary>File / FileInfo / Directory / DirectoryInfo の、書き込み・作成・写し・移動・置き換え・削除のメソッド</summary>
    private static readonly Dictionary<string, HashSet<string>> WriteMethods = new(
        StringComparer.Ordinal
    )
    {
        ["System.IO.File"] =
        [
            nameof(File.WriteAllText),
            nameof(File.WriteAllTextAsync),
            nameof(File.WriteAllBytes),
            nameof(File.WriteAllBytesAsync),
            nameof(File.WriteAllLines),
            nameof(File.WriteAllLinesAsync),
            nameof(File.AppendAllText),
            nameof(File.AppendAllTextAsync),
            nameof(File.AppendAllLines),
            nameof(File.AppendAllLinesAsync),
            nameof(File.AppendAllBytes),
            nameof(File.AppendAllBytesAsync),
            nameof(File.Create),
            nameof(File.CreateText),
            nameof(File.AppendText),
            nameof(File.OpenWrite),
            nameof(File.Copy),
            nameof(File.Move),
            nameof(File.Replace),
            nameof(File.Delete),
            nameof(File.CreateSymbolicLink),
        ],
        ["System.IO.FileInfo"] =
        [
            nameof(FileInfo.Create),
            nameof(FileInfo.CreateText),
            nameof(FileInfo.AppendText),
            nameof(FileInfo.OpenWrite),
            nameof(FileInfo.CopyTo),
            nameof(FileInfo.MoveTo),
            nameof(FileInfo.Replace),
            nameof(FileInfo.Delete),
        ],
        ["System.IO.Directory"] = [nameof(Directory.Delete), nameof(Directory.Move)],
        ["System.IO.DirectoryInfo"] = [nameof(DirectoryInfo.Delete), nameof(DirectoryInfo.MoveTo)],
    };

    /// <summary>引数の FileMode / FileAccess しだいで書き込みになるメソッド</summary>
    private static readonly HashSet<(string Type, string Method)> ModeDependentMethods =
    [
        ("System.IO.File", nameof(File.Open)),
        ("System.IO.File", nameof(File.OpenHandle)),
        ("System.IO.FileInfo", nameof(FileInfo.Open)),
    ];

    /// <summary>シンボルが解決しなかったときに「未解決」として数える名前</summary>
    private static readonly HashSet<string> TargetNames = new(
        WriteMethods.Values.SelectMany(names => names).Concat(["Open", "OpenHandle", "SaveAs"]),
        StringComparer.Ordinal
    );

    /// <summary>数える書き込みなら、その表記（例 <c>File.WriteAllText</c>・<c>new FileStream</c>・<c>未解決 OpenWrite</c>）を返す</summary>
    public static string? Describe(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            InvocationExpressionSyntax invocation => DescribeInvocation(model, invocation),
            BaseObjectCreationExpressionSyntax creation => DescribeCreation(model, creation),
            _ => null,
        };

    private static string? DescribeInvocation(
        SemanticModel model,
        InvocationExpressionSyntax invocation
    )
    {
        var method = SourceSymbolScan.ResolveMethod(model, invocation);
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

        if (method is null)
        {
            return name is not null && TargetNames.Contains(name) ? $"未解決 {name}" : null;
        }

        var type = SourceSymbolScan.ContainingTypeName(method);

        if (type is null)
        {
            return null;
        }

        var shortType = type[(type.LastIndexOf('.') + 1)..];

        if (WriteMethods.TryGetValue(type, out var methods) && methods.Contains(method.Name))
        {
            return $"{shortType}.{method.Name}";
        }

        if (ModeDependentMethods.Contains((type, method.Name)))
        {
            return WritesByMode(model, method, invocation.ArgumentList)
                ? $"{shortType}.{method.Name}"
                : null;
        }

        // ClosedXML のブックなど、パスを受ける SaveAs（ストリームを受ける多重定義は数えない）
        return method.Name == "SaveAs" && FirstParameterIsString(method) ? "SaveAs" : null;
    }

    private static string? DescribeCreation(
        SemanticModel model,
        BaseObjectCreationExpressionSyntax creation
    )
    {
        var constructor = SourceSymbolScan.ResolveMethod(model, creation);

        if (constructor is null)
        {
            var written = creation is ObjectCreationExpressionSyntax explicitCreation
                ? explicitCreation.Type.ToString().Split('.')[^1]
                : null;

            return written is nameof(FileStream) or nameof(StreamWriter)
                ? $"未解決 new {written}"
                : null;
        }

        return SourceSymbolScan.ContainingTypeName(constructor) switch
        {
            "System.IO.FileStream" => WritesByMode(model, constructor, creation.ArgumentList)
                ? "new FileStream"
                : null,
            "System.IO.StreamWriter" => FirstParameterIsString(constructor)
                ? "new StreamWriter"
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// FileMode / FileAccess の引数から書き込みかを決める。読み取りと言えるのは、FileAccess が定数の Read で、FileMode が
    /// 定数の Open のとき（省略は既定値で判定する）だけ。変数・FileStreamOptions・ハンドルは書き込みとみなす
    /// </summary>
    private static bool WritesByMode(
        SemanticModel model,
        IMethodSymbol method,
        ArgumentListSyntax? arguments
    )
    {
        if (
            method.Parameters.Any(parameter =>
                parameter.Type.Name is nameof(FileStreamOptions) or "SafeFileHandle"
            )
        )
        {
            return true;
        }

        object? Constant(string parameterName)
        {
            var parameter = method.Parameters.FirstOrDefault(p => p.Name == parameterName);

            if (parameter is null)
            {
                return null;
            }

            var argument = arguments
                ?.Arguments.Select((argument, index) => (argument, index))
                .FirstOrDefault(pair =>
                    pair.argument.NameColon?.Name.Identifier.ValueText == parameterName
                    || (pair.argument.NameColon is null && pair.index == parameter.Ordinal)
                )
                .argument;

            if (argument is null)
            {
                return parameter.HasExplicitDefaultValue
                    ? parameter.ExplicitDefaultValue
                    : "omitted";
            }

            var constant = model.GetConstantValue(argument.Expression);
            return constant.HasValue ? constant.Value : "variable";
        }

        var mode = Constant("mode");
        var access = Constant("access");

        // FileStream(path, mode) と File.Open(path, mode) の FileAccess の省略は ReadWrite
        var readAccess = access is int accessValue && accessValue == (int)FileAccess.Read;
        var openMode = mode is int modeValue && modeValue == (int)FileMode.Open;

        return !(readAccess && openMode);
    }

    private static bool FirstParameterIsString(IMethodSymbol method) =>
        method.Parameters.FirstOrDefault()?.Type.SpecialType == SpecialType.System_String;
}
