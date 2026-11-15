using System.IO;
using System.Text.RegularExpressions;
using QuickER.Tests.Docs;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：Integration のテストの流し方（コンテナのコレクションの順の宣言）</summary>
internal static partial class ParityRows
{
    private static IEnumerable<ParityRow> Integration() =>
        [
            new(
                "integration-container-collections-ordered",
                "Integration のコンテナのコレクション（CollectionDefinition を持ち ICollectionFixture を実装するクラス）は、どれも"
                    + "ContainerCollectionOrderer.Order にちょうど 1 回載り、Order にはコンテナのコレクションだけを載せる（載せ漏れたコレクションは"
                    + "xunit の既定の順へ落ち、長い直列のコレクションが後ろに並ぶと夜間のジョブの終わりが遅れる）",
                ParityCompare.Multiset,
                () =>
                    [
                        .. IntegrationSources()
                            .SelectMany(source =>
                                Regex
                                    .Matches(
                                        source,
                                        @"\[CollectionDefinition\([^\]]*\)\]\s*public sealed class (?<name>\w+)\s*:\s*ICollectionFixture<"
                                    )
                                    .Select(match => match.Groups["name"].Value)
                            )
                            .Order(StringComparer.Ordinal),
                    ],
                () =>
                {
                    var source = File.ReadAllText(
                        Path.Combine(
                            WorkflowGuardTests.Root,
                            "tests",
                            "QuickER.Tests.Integration",
                            "ContainerCollectionOrderer.cs"
                        )
                    );
                    var block = Regex.Match(source, @"\bType\[\] Order =\s*\[(?<body>[^\]]*)\];");

                    return block.Success
                        ?
                        [
                            .. Regex
                                .Matches(block.Groups["body"].Value, @"\btypeof\((?<name>\w+)\)")
                                .Select(match => match.Groups["name"].Value),
                        ]
                        : throw new InvalidOperationException(
                            "ContainerCollectionOrderer.cs に Order の宣言（Type[] Order = [ … ];）が無い"
                        );
                }
            )
            {
                // いまのコンテナのコレクションは 6 つ（SQL Server・SQL Server の簡易 DSL・Oracle・PostgreSQL・MySQL・PostGIS）。
                // 拾い方が外れて正本が減ったら赤にする
                MinimumCanon = 6,
            },
        ];

    /// <summary>Integration の C# ソース（ビルドの出力を除く）</summary>
    private static IEnumerable<string> IntegrationSources() =>
        Directory
            .EnumerateFiles(
                Path.Combine(WorkflowGuardTests.Root, "tests", "QuickER.Tests.Integration"),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Where(path => !SourceFiles.IsBuildOutput(path))
            .Select(File.ReadAllText);
}
