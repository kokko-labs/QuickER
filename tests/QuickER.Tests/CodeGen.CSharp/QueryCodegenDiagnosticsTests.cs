using System.IO;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリの生成で、診断が利用者の書いたものを正しく名指しすること、使われない SQL がクエリ全体を止めないこと、
/// 生成すればコンパイルできない条件を生成前に断ること、識別子・型名の規則が C# の実際と合うことを固定するテストクラス。
/// </summary>
/// <remarks>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</remarks>
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

    /// <summary>SQL Server の QuickER 版 Repository で生成する</summary>
    private static CodeGenerationResult GenerateSqlServer(ErDiagram diagram)
    {
        var provider = new SqlServerProvider();

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );
    }

    /// <summary>生成物のどこかにその文字列があるか</summary>
    private static bool GeneratedContains(CodeGenerationResult result, string text) =>
        result.Files.Any(file => file.Content.Contains(text, StringComparison.Ordinal));

    [Fact(DisplayName = "使われない方言の SQL の未宣言パラメータで、クエリ全体を飛ばさない")]
    public void UndeclaredParameterInUnsupportedDialect_DoesNotSkipQuery() =>
        Known.Run(
            "unused-dialect-veto",
            () =>
            {
                var order = CreateOrders();
                var diagram = new ErDiagram { Entities = { order } };
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "FindByStatus",
                        Implementation = QueryImplementationKind.Sql,
                        Sql =
                        {
                            ["sqlserver"] = "SELECT * FROM Orders WHERE status = @status",
                            ["mysql"] = "SELECT * FROM Orders WHERE status = @statsu",
                        },
                        Parameters =
                        {
                            new QueryParameter { Name = "status", Type = "string(20)" },
                        },
                    }
                );

                var result = GenerateSqlServer(diagram);

                GeneratedContains(result, "FindByStatusAsync(")
                    .Should()
                    .BeTrue("使われない方言の SQL の誤りで、使われる方言の実装まで止めない");
            }
        );

    [Fact(
        DisplayName = "この生成で選んでいない方言の SQL の未宣言パラメータで、選んだ方言の実装を止めない（sqlite だけの生成）"
    )]
    public void UndeclaredParameterInUnselectedDialect_DoesNotSkipQuery()
    {
        var order = CreateOrders();
        var diagram = new ErDiagram { Entities = { order }, TargetDbms = "sqlite" };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "FindByStatus",
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT * FROM Orders WHERE status = @statsu",
                    ["sqlite"] = "SELECT * FROM Orders WHERE status = @status",
                },
                Parameters =
                {
                    new QueryParameter { Name = "status", Type = "string(20)" },
                },
            }
        );
        var provider = new QuickER.Provider.Sqlite.SqliteProvider();

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
                RepositoryDialects = ["sqlite"],
            }
        );

        result.HasErrors.Should().BeFalse();
        GeneratedContains(result, "this.QueryBySqlAsync(")
            .Should()
            .BeTrue("選んだ方言（sqlite）の実装は、選んでいない方言の SQL の誤りで止めない");
        result
            .Diagnostics.Should()
            .Contain(
                diagnostic =>
                    diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                    && diagnostic.Message.Contains("@statsu", StringComparison.Ordinal),
                "選んでいない方言の SQL の誤りも警告では知らせる"
            );
    }

    [Fact(
        DisplayName = "QuickER 版 Repository を生成しない構成では、生 SQL の未宣言パラメータでクエリを飛ばさない（契約は出る）"
    )]
    public void UndeclaredParameterWithoutRepositories_KeepsContract()
    {
        var order = CreateOrders();
        var diagram = new ErDiagram { Entities = { order } };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "FindByStatus",
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT * FROM Orders WHERE status = @statsu" },
                Parameters =
                {
                    new QueryParameter { Name = "status", Type = "string(20)" },
                },
            }
        );
        var provider = new SqlServerProvider();

        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateEfCoreRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        GeneratedContains(result, "FindByStatusAsync(")
            .Should()
            .BeTrue("生 SQL を使わない構成では、クエリは手動実装として契約が出る");
    }

    [Fact(DisplayName = "対応する方言の SQL の未宣言パラメータは、従来どおりクエリを飛ばす")]
    public void UndeclaredParameterInSupportedDialect_StillSkipsQuery()
    {
        var order = CreateOrders();
        var diagram = new ErDiagram { Entities = { order } };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = order.Id,
                Name = "FindByStatus",
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT * FROM Orders WHERE status = @statsu" },
                Parameters =
                {
                    new QueryParameter { Name = "status", Type = "string(20)" },
                },
            }
        );

        var result = GenerateSqlServer(diagram);

        GeneratedContains(result, "FindByStatusAsync(").Should().BeFalse();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Message.Contains("@statsu", StringComparison.Ordinal)
            );
    }

    [Fact(
        DisplayName = "型を解決できなかったパラメータを、SQL の未宣言パラメータとしても報告しない"
    )]
    public void UnresolvedParameterType_IsNotAlsoReportedAsUndeclared() =>
        Known.Run(
            "false-undeclared",
            () =>
            {
                var order = CreateOrders();
                var diagram = new ErDiagram { Entities = { order } };
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "FindByStatus",
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = "SELECT * FROM Orders WHERE status = @status" },
                        Parameters =
                        {
                            new QueryParameter { Name = "status", Type = "strng" },
                        },
                    }
                );

                var result = GenerateSqlServer(diagram);

                result
                    .Diagnostics.Should()
                    .Contain(diagnostic => diagnostic.Message.Contains("strng"));
                result
                    .Diagnostics.Where(diagnostic => diagnostic.Message.Contains("@status"))
                    .Should()
                    .BeEmpty("宣言したパラメータを未宣言として報告しない");
            }
        );

    [Fact(DisplayName = "射影フィールドの NULL 許容の診断は、DB の列名を名指しする")]
    public void ProjectionNotNullOverride_NamesDatabaseColumn() =>
        Known.Run(
            "projection-column-name",
            () =>
            {
                var order = CreateOrders();
                var customerId = order.Columns.Single(column => column.Name == "customer_id");
                var diagram = new ErDiagram { Entities = { order } };
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "Customers",
                        Returns = QueryReturnShape.Projection,
                        Implementation = QueryImplementationKind.Dsl,
                        ResultTypeName = "CustomerRow",
                        Fields =
                        {
                            new ProjectionField
                            {
                                Name = "Customer",
                                SourceColumnId = customerId.Id,
                                IsNullable = false,
                            },
                        },
                    }
                );

                var message = GenerateSqlServer(diagram)
                    .Diagnostics.Single(diagnostic => diagnostic.Message.Contains("'Customers'"))
                    .Message;

                message.Should().Contain("'customer_id'", "DB の列名で名指しする");
            }
        );

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
        DisplayName = "内包値が文字列でない値オブジェクトの列への文字列一致は、生成前に断る（コンパイルできないため）"
    )]
    public void StringMatchOnNonStringValueObject_IsRejected() =>
        Known.Run(
            "vo-string-match",
            () =>
            {
                var hosts = new Entity { TableName = "hosts" };
                hosts.Columns.Add(
                    new Column
                    {
                        Name = "id",
                        DataType = "integer",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    }
                );
                hosts.Columns.Add(
                    new Column
                    {
                        Name = "net",
                        DataType = "cidr",
                        IsNullable = false,
                    }
                );
                var diagram = new ErDiagram { Entities = { hosts }, TargetDbms = "postgresql" };
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = hosts.Id,
                        Name = "InPrivateRange",
                        Implementation = QueryImplementationKind.Dsl,
                        Condition = "net CONTAINS '10.'",
                    }
                );
                var provider = new PostgreSqlProvider();

                var result = DiagramCodeGenerator.Generate(
                    provider.TypeMapper,
                    provider.TypeCatalog,
                    diagram,
                    new CodeGenerationOptions
                    {
                        RootNamespace = "Test.Ns",
                        GenerateEfCoreRepositories = true,
                        IncludeDataAnnotations = true,
                        GenerateValueObjects = true,
                    }
                );

                GeneratedContains(result, "InPrivateRangeAsync(")
                    .Should()
                    .BeFalse("文字列一致を持たない値オブジェクトへの CONTAINS は生成前に断る");
            }
        );

    [Theory(DisplayName = "LIKE で書いた条件の型の診断は、LIKE の名前で報告する")]
    [InlineData("seen_at LIKE @p")]
    [InlineData("seen_at LIKE '%1'")]
    public void StringMatchViolation_UsesLikeSpelling(string condition) =>
        Known.Run(
            "like-spelling:" + condition,
            () =>
            {
                var order = CreateOrders();
                var diagram = new ErDiagram { Entities = { order } };
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "BySeen",
                        Implementation = QueryImplementationKind.Dsl,
                        Condition = condition,
                        Parameters =
                        {
                            new QueryParameter { Name = "p", Type = "string(10)" },
                        },
                    }
                );

                var message = GenerateSqlServer(diagram)
                    .Diagnostics.Single(diagnostic => diagnostic.Message.Contains("'BySeen'"))
                    .Message;

                message.Should().Contain("LIKE");
                message.Should().NotContain("CONTAINS", "利用者が書いていない演算子名で報告しない");
                message.Should().NotContain("ENDSWITH", "利用者が書いていない演算子名で報告しない");
            }
        );

    [Fact(DisplayName = "診断に載せる長いリテラルの切り詰めで、サロゲートの対を割らない")]
    public void TruncatedLiteral_DoesNotSplitSurrogatePair() =>
        Known.Run(
            "truncate-surrogate",
            () =>
            {
                var order = CreateOrders();
                var diagram = new ErDiagram { Entities = { order } };
                var literal = new string('a', 19) + "\U0001F600" + "tail";
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = order.Id,
                        Name = "ByQty",
                        Implementation = QueryImplementationKind.Dsl,
                        Condition = $"OrderId = '{literal}'",
                    }
                );

                var message = GenerateSqlServer(diagram)
                    .Diagnostics.Single(diagnostic => diagnostic.Message.Contains("'ByQty'"))
                    .Message;

                HasLoneSurrogate(message).Should().BeFalse("切り詰めでサロゲートの対を割らない");
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
                        !path.Contains(
                            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
                        )
                        && !path.Contains(
                            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"
                        )
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

    /// <summary>対になっていないサロゲートを含むか</summary>
    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return true;
                }

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

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
