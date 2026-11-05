using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 各 .g.cs の namespace 宣言（構文木）を、名前空間の規則（<see cref="CodegenNamespaces"/>）と照合する
/// </summary>
/// <remarks>
/// 構文木は <see cref="GeneratedCodeCompiler.CreateCompilation"/> の <c>SyntaxTrees</c> から取る（テストから Roslyn のパースを直接呼ばない）。
/// ファイルの系統はファイル構成の期待（<see cref="FileLayoutOracle"/>）の系統と同じ。
/// </remarks>
internal static class NamespaceJudge
{
    /// <summary>1 つの生成ファイルが宣言する名前空間の集合（構文木で読む。決まった行の名前空間の完全一致が使う）</summary>
    /// <param name="file">生成ファイル</param>
    public static IReadOnlySet<string> Declared(GeneratedFile file) =>
        GeneratedCodeCompiler
            .CreateCompilation(
                [(file.FileName, file.Content)],
                $"QuickER.CodegenConfig.FileNamespace.{Guid.NewGuid():N}"
            )
            .SyntaxTrees.Single()
            .GetRoot()
            .DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(declaration => declaration.Name.ToString())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>名前空間を照合する（問題を返す）</summary>
    /// <param name="options">実効の生成オプション</param>
    /// <param name="result">生成結果</param>
    public static IReadOnlyList<string> Check(
        CodeGenerationOptions options,
        CodeGenerationResult result
    )
    {
        var namespaces = new CodegenNamespaces(options);
        var expectedByPath = FileLayoutOracle
            .Expected(options)
            .Where(file => file.Kind != FileKind.ApiDoc)
            .ToDictionary(
                file => file.Path,
                file => Expected(file, namespaces, options),
                StringComparer.Ordinal
            );
        var sources = result
            .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(file => (FileLayoutOracle.PathOf(file), file.Content))
            .ToList();
        var compilation = GeneratedCodeCompiler.CreateCompilation(
            sources,
            $"QuickER.CodegenConfig.Namespaces.{Guid.NewGuid():N}"
        );
        var problems = new List<string>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var actual = tree.GetRoot()
                .DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(declaration => declaration.Name.ToString())
                .ToHashSet(StringComparer.Ordinal);

            if (!expectedByPath.TryGetValue(tree.FilePath, out var expected))
            {
                problems.Add($"名前空間: 期待の無いファイル {tree.FilePath}");
                continue;
            }

            if (!actual.SetEquals(expected))
            {
                problems.Add(
                    $"名前空間: {tree.FilePath} の namespace が [{string.Join(", ", actual.Order(StringComparer.Ordinal))}]（期待 [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]）"
                );
            }
        }

        return problems;
    }

    /// <summary>ファイルの系統から、そのファイルが宣言する名前空間の集合を決める</summary>
    private static HashSet<string> Expected(
        ExpectedFile file,
        CodegenNamespaces namespaces,
        CodeGenerationOptions options
    )
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        switch (file.Kind)
        {
            case FileKind.SingleMain:
                set.Add(namespaces.Root);

                // 非分割のマルチターゲットは、契約とほかのバケットをルートへ、方言実装を方言の名前空間のブロックへ連結する
                if (CodegenOptionFacts.MultiTarget(options))
                {
                    foreach (var dialect in CodegenOptionFacts.Dialects(options))
                    {
                        set.Add(namespaces.DialectImplementation(dialect));
                    }
                }

                break;

            case FileKind.SingleServer:
                set.Add(namespaces.RemoteServer);
                break;

            case FileKind.Entities:
                set.Add(namespaces.Entity);
                break;

            case FileKind.EditModels:
                set.Add(namespaces.EditModel);
                break;

            case FileKind.Mappers:
                set.Add(namespaces.Mapper);
                break;

            case FileKind.ValueObjects:
                set.Add(namespaces.ValueObject);
                break;

            case FileKind.Contracts:
                set.Add(namespaces.Contract);
                break;

            case FileKind.DialectRepositories:
                set.Add(namespaces.DialectImplementation(file.Dialect!));
                break;

            case FileKind.EfCoreRepositories:
                set.Add(namespaces.EfCore);
                break;

            case FileKind.InMemoryRepositories:
                set.Add(namespaces.InMemory);
                break;

            case FileKind.SyncRepositories:
                set.Add(namespaces.Sync);
                break;

            case FileKind.HttpRepositories:
                set.Add(namespaces.Http);
                break;

            case FileKind.RemoteServer:
                set.Add(namespaces.RemoteServer);
                break;

            case FileKind.Runtime:
                set.Add(namespaces.Runtime);
                break;

            case FileKind.RuntimeDialect:
                set.Add(namespaces.DialectEngine(file.Dialect!));
                break;

            case FileKind.RuntimeEfCore:
                set.Add(namespaces.RuntimeFamily("EntityFrameworkCore"));
                break;

            case FileKind.RuntimeInMemory:
                set.Add(namespaces.RuntimeFamily("InMemory"));
                break;

            case FileKind.RuntimeSync:
                set.Add(namespaces.RuntimeFamily("Sync"));
                break;

            case FileKind.RuntimeAspNetCore:
                set.Add(namespaces.RuntimeAspNetCore);
                break;
        }

        return set;
    }
}
