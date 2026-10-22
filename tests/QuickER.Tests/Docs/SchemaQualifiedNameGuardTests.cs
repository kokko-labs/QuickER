using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Docs;

/// <summary>
/// テーブル名を <c>.</c> で分ける・探す処理を、<c>SchemaQualifiedName</c>（QuickER.Dialects）の外に写さないことを固定する
/// </summary>
/// <remarks>
/// <para>
/// 拾う基準：<c>src/</c> の QuickER.Dialects・QuickER.Model・QuickER.Provider*・QuickER.CodeGen.CSharp の <c>.cs</c>
/// （<c>bin</c> / <c>obj</c> と生成物の <c>.g.cs</c> を除く）を構文木で走査し、第 1 引数が <c>'.'</c> か <c>"."</c> の
/// <c>Split</c>・<c>IndexOf</c>・<c>LastIndexOf</c>・<c>Contains</c> の呼び出しを数える。テーブル名でない用途を含め、
/// 理由つきの既知の一覧（ファイルとメソッドごとの件数）と厳密に一致させる。
/// </para>
/// <para>
/// 生成ランタイムの <c>EntitySaveMetadata.QuoteTableName</c>（テンプレートとパッケージ用のソース）は Dialects を参照できないので
/// 写しを持つ。これはこの走査の外で、一致は <c>SchemaQualifiedNameParityTests</c> が固定する。
/// </para>
/// </remarks>
public sealed class SchemaQualifiedNameGuardTests
{
    /// <summary>走査するプロジェクトのフォルダ名の接頭辞</summary>
    private static readonly string[] Projects =
    [
        "QuickER.Dialects",
        "QuickER.Model",
        "QuickER.Provider",
        "QuickER.CodeGen.CSharp",
    ];

    /// <summary>既知の出現（ファイル・メソッド → 件数）と理由</summary>
    private static readonly Dictionary<string, int> Known = new(StringComparer.Ordinal)
    {
        // 規則の正本そのもの
        ["src/QuickER.Dialects/SchemaQualifiedName.cs Contains"] = 1,
        ["src/QuickER.Dialects/SchemaQualifiedName.cs IndexOf"] = 1,
        // 名前空間を区間に分ける（テーブル名ではない）
        ["src/QuickER.CodeGen.CSharp/CSharpNamespaceValidator.cs Split"] = 1,
        // 簡易 DSL の数値のリテラルに小数点があるか（テーブル名ではない）
        ["src/QuickER.CodeGen.CSharp/Queries/QueryConditionTypeChecker.cs Contains"] = 1,
    };

    [Fact(DisplayName = "テーブル名を . で分ける処理は SchemaQualifiedName の外に写さない")]
    public void DotSplitting_IsOnlyInSchemaQualifiedName()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (
            var path in Directory
                .EnumerateDirectories(src)
                .Where(directory =>
                    Projects.Any(project =>
                        Path.GetFileName(directory).StartsWith(project, StringComparison.Ordinal)
                    )
                )
                .SelectMany(directory =>
                    Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                )
                .Where(path =>
                    !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                    && !path.Split(Path.DirectorySeparatorChar)
                        .Any(segment => segment is "bin" or "obj")
                )
        )
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var invocations = ConditionalSourceParse
                .DescendantNodes(
                    ConditionalSourceParse.Parse(
                        File.ReadAllText(path),
                        cancellationToken: TestContext.Current.CancellationToken
                    ),
                    TestContext.Current.CancellationToken
                )
                .OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                if (
                    invocation.Expression is MemberAccessExpressionSyntax member
                    && member.Name.Identifier.ValueText
                        is "Split"
                            or "IndexOf"
                            or "LastIndexOf"
                            or "Contains"
                    && invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                        is LiteralExpressionSyntax literal
                    && literal.Token.ValueText == "."
                )
                {
                    var key = $"{relative} {member.Name.Identifier.ValueText}";
                    found[key] = found.GetValueOrDefault(key) + 1;
                }
            }
        }

        found
            .Should()
            .BeEquivalentTo(
                Known,
                "テーブル名を . で分ける・探すときは SchemaQualifiedName（QuickER.Dialects）を使う"
            );
    }
}
