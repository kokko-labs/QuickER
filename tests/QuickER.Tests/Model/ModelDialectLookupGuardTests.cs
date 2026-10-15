using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Model;

/// <summary>
/// 意味モデル（<c>QuickER.Model</c>）が、方言名から識別子の上限を引かないことを固定する。
/// </summary>
/// <remarks>
/// <para>
/// 方言名から上限を引く表（<c>IdentifierLengthLimits</c>）は方言の宣言（<c>QuickER.Dialects</c>）の持ち物で、
/// どの方言の上限を使うかは呼び出し側（DDL・同期・ツールのホスト・GUI）が決めて引数で渡す。
/// Model が自分で <c>ErDiagram.TargetDbms</c> から上限を引くと、図の対象 DBMS と違う方言で DDL を作る経路や、
/// 上限を差し替えたいテストから、その判断が見えなくなる。
/// </para>
/// <para>
/// 構文木で <c>IdentifierLengthLimits</c> という名前の参照を数える（コメント・XmlDoc の中の言及は数えない）。
/// 既知の一覧は持たない（例外を認めない）。
/// </para>
/// </remarks>
public sealed class ModelDialectLookupGuardTests
{
    [Fact(
        DisplayName = "Model のソースは方言名から識別子の上限を引かない（IdentifierLengthLimits を参照しない）"
    )]
    public void ModelSources_DoNotReferenceIdentifierLengthLimits()
    {
        var model = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Model");
        var hits = Directory
            .EnumerateFiles(model, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")
            )
            .SelectMany(path =>
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));

                return tree.GetRoot()
                    .DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(name => name.Identifier.ValueText == "IdentifierLengthLimits")
                    .Select(name =>
                        $"{Path.GetFileName(path)}:{name.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"
                    );
            })
            .ToList();

        hits.Should()
            .BeEmpty(
                "Model は方言名から上限を引かず、上限は呼び出し側が引数で渡す（参照している場所: "
                    + string.Join(", ", hits)
                    + "）"
            );
    }
}
