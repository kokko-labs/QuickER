using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリの診断と名前の規則のうち、生成の構成のエンジンの行では言えないものを固定するテストクラス：パーサの診断の位置と
/// 構造化引数（公開の契約）・Roslyn を全数のオラクルにした識別子と型名の規則・予約メソッド名の宣言・改行の集合のソースの走査。
/// </summary>
/// <remarks>
/// <para>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</para>
/// <para>
/// 生成の診断（使われない SQL がクエリを止めない・型の診断が利用者の書いた名前で名指しする・切り詰めがサロゲートの対を割らない など）は
/// P3-6 で生成の構成のエンジンの決まった行（<c>CodegenCases.Dialects.cs</c> の <c>DialectDslRows</c>）へ移した。
/// </para>
/// </remarks>
public class QueryCodegenDiagnosticsTests
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("QueryCodegenDiagnosticsTests", []);

    /// <summary>Orders（OrderId・status・customer_id〔NULL 許容〕・memo〔NULL 許容〕・seen_at）を作る</summary>
    private static Entity CreateOrders()
    {
        var order = new Entity { TableName = "Orders" };
        order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "status",
                DataType = "nvarchar(20)",
                IsNullable = false,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "customer_id",
                DataType = "int",
                IsNullable = true,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = "nvarchar(100)",
                IsNullable = true,
            }
        );
        order.Columns.Add(
            new Column
            {
                Name = "seen_at",
                DataType = "datetime2",
                IsNullable = false,
            }
        );

        return order;
    }

    [Fact(DisplayName = "LIKE の未対応パターンの診断は、パターンのリテラルの位置を指す")]
    public void UnsupportedLikePattern_PointsAtPattern() =>
        Known.Run(
            "like-pattern-position",
            () =>
            {
                const string text = "Memo LIKE 'a%b'";
                var diagnostic = QueryConditionParser
                    .Parse(text)
                    .Diagnostics.Should()
                    .ContainSingle()
                    .Subject;

                diagnostic
                    .Position.Should()
                    .Be(text.IndexOf('\''), "パターンのリテラルの位置を指す");
                diagnostic.Length.Should().Be("'a%b'".Length);
            }
        );

    [Theory(DisplayName = "想定外のトークンの診断は、利用者が書いたとおりの綴りで名指しする")]
    [InlineData("Qty = 1 @p", "@p")]
    [InlineData("'x' = 1", "'x'")]
    [InlineData("Qty = 1 ''", "''")]
    public void UnexpectedToken_QuotesSourceSpelling(string text, string spelling) =>
        Known.Run(
            "unexpected-token:" + spelling,
            () =>
            {
                var diagnostic = QueryConditionParser
                    .Parse(text)
                    .Diagnostics.Should()
                    .ContainSingle()
                    .Subject;

                diagnostic
                    .Text.Arguments.Should()
                    .Contain(spelling, "利用者が書いたとおりの綴りで名指しする");
            }
        );

    [Fact(
        DisplayName = "C# の予約キーワード（__arglist 系を含む）は、どれも名前の規則で識別子として断る"
    )]
    public void ReservedKeywords_AreNotValidIdentifiers() =>
        Known.Run(
            "identifier-keywords",
            () =>
            {
                var accepted = Microsoft
                    .CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKinds()
                    .Where(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsReservedKeyword)
                    .Select(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetText)
                    .Where(QueryDefinitionRules.IsValidIdentifier)
                    .ToList();

                accepted.Should().BeEmpty("C# の予約キーワードは識別子として使えない");
            }
        );

    [Fact(
        DisplayName = "型の名前にするとコンパイルエラーになる文脈キーワードは、射影の結果型名として断る"
    )]
    public void ContextualKeywordsThatCannotNameTypes_AreRejectedAsResultTypeNames() =>
        Known.Run(
            "result-type-contextual",
            () =>
            {
                var keywords = Microsoft
                    .CodeAnalysis.CSharp.SyntaxFacts.GetContextualKeywordKinds()
                    .Select(Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetText)
                    .Where(QueryDefinitionRules.IsValidIdentifier)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                // 1 回のコンパイルで、文脈キーワードごとに別の名前空間へ同名のクラスを置き、エラーの出た名前を集める
                var source = string.Join(
                    Environment.NewLine,
                    keywords.Select(
                        (keyword, index) =>
                            $"namespace N{index} {{ public sealed partial class {keyword} {{ }} }}"
                    )
                );
                var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                    "ContextualTypeNames",
                    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source)],
                    [
                        Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(
                            typeof(object).Assembly.Location
                        ),
                    ],
                    new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                        Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary
                    )
                );
                var invalidTypeNames = compilation
                    .GetDiagnostics()
                    .Where(diagnostic =>
                        diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                    )
                    .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
                    .Distinct()
                    .Select(line => keywords[line])
                    .ToList();
                invalidTypeNames
                    .Should()
                    .NotBeEmpty("型の名前に使えない文脈キーワードがある（scoped など）");

                var order = CreateOrders();
                var accepted = invalidTypeNames
                    .Where(name =>
                        QueryDefinitionRules
                            .Validate(
                                order,
                                new QueryDefinition
                                {
                                    EntityId = order.Id,
                                    Name = "Rows",
                                    Returns = QueryReturnShape.Projection,
                                    Implementation = QueryImplementationKind.Sql,
                                    Sql = { ["sqlserver"] = "SELECT 1 AS Total" },
                                    ResultTypeName = name,
                                    Fields =
                                    {
                                        new ProjectionField { Name = "Total", Type = "int32" },
                                    },
                                },
                                [],
                                [order]
                            )
                            .Count == 0
                    )
                    .ToList();

                accepted.Should().BeEmpty("型の名前に使えない文脈キーワードを結果型名として断る");
            }
        );

    [Fact(
        DisplayName = "予約メソッド名はすべて、クエリ名から作るメソッド名（末尾が Async）で当たりうる"
    )]
    public void ReservedMethodNames_AreReachable() =>
        Known.Run(
            "reserved-method-names",
            () =>
                QueryDefinitionRules
                    .ReservedMethodNames.Should()
                    .OnlyContain(
                        name => name.EndsWith("Async", StringComparison.Ordinal),
                        "クエリ名から作るメソッド名は必ず Async で終わるので、それ以外は当たらない"
                    )
        );

    [Fact(DisplayName = "描画後に改行へ変わる文字の集合は、生成器の中で 1 か所にだけ書く")]
    public void LineBreakSet_IsWrittenOnce() =>
        Known.Run(
            "line-break-set",
            () =>
            {
                var root = Path.Combine(RepositoryRoot(), "src", "QuickER.CodeGen.CSharp");
                var files = Directory
                    .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                    .Where(path =>
                        !TestSupport.SourceFiles.IsBuildOutput(Path.GetRelativePath(root, path))
                    )
                    .Where(path =>
                    {
                        var source = File.ReadAllText(path);

                        // U+2028 をコード上の文字・数値で書いている箇所（改行の集合の写し）
                        return source.Contains("'\\u2028'", StringComparison.Ordinal)
                            || source.Contains("0x2028", StringComparison.Ordinal);
                    })
                    .Select(path => Path.GetRelativePath(root, path))
                    .ToList();

                files.Should().ContainSingle("改行の集合の写しは 1 か所だけ");
            }
        );

    /// <summary>リポジトリのルート（QuickER.slnx のあるフォルダ）</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "QuickER.slnx"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("QuickER.slnx が見つからない");
    }
}
