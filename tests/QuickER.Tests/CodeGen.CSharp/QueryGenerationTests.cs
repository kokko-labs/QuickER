using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 名前付きクエリ（ErDiagram.Queries）から Repository メソッドが生成されることを、
/// 実経路（<see cref="DiagramCodeGenerator"/>＝型トークン解決込み）で検証するテストクラス
/// </summary>
public class QueryGenerationTests
{
    private readonly Entity _order;

    /// <summary>Order エンティティ（OrderId PK / CustomerId / Amount / Memo / CreatedAt）を用意する</summary>
    public QueryGenerationTests()
    {
        _order = new Entity { TableName = "Order" };
        _order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        _order.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsNullable = false,
            }
        );
        _order.Columns.Add(
            new Column
            {
                Name = "Amount",
                DataType = "decimal(12,2)",
                IsNullable = false,
            }
        );
        _order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });
    }

    /// <summary>クエリ定義付きの図を作る</summary>
    private ErDiagram CreateDiagram(params QueryDefinition[] queries)
    {
        var diagram = new ErDiagram { Entities = { _order } };

        foreach (var query in queries)
        {
            query.EntityId = _order.Id;
            diagram.Queries.Add(query);
        }

        return diagram;
    }

    /// <summary>実経路（SqlServer プロバイダ）で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            options
        );
    }

    /// <summary>QuickER 版 Repository＋EF Core 併存の標準オプション</summary>
    private static CodeGenerationOptions CreateOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>全ファイルの内容を連結して返す</summary>
    private static string AllContent(CodeGenerationResult result) =>
        string.Join("\n", result.Files.Select(file => file.Content));

    /// <summary>ミニ DSL の一覧クエリ（条件・並び順・ページング）が契約・QuickER 版 Repository・EF Core 版 Repository に同一本体で出ることを検証する</summary>
    // 残す理由（P3-4b）：共有本体の式（条件・並び替え・ページングの訳し方＝P3-6〔簡易 DSL〕の領分）。構造の表明（契約・実装の宣言・説明の XmlDoc）は行 code-query-dsl-list-contract-and-both-implementations へ移した
    [Fact(DisplayName = "DSL 一覧クエリ: 契約＋QuickER＋EF Core に同一の Query() 本体が出る")]
    public void Generate_DslListQuery_EmitsSharedBody()
    {
        var amountColumn = _order.Columns.First(c => c.Name == "Amount");
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByCustomer",
                Description = "顧客IDで注文を検索する",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
                OrderBy =
                {
                    new QueryOrdering { ColumnId = amountColumn.Id, Descending = true },
                },
                HasPaging = true,
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 共有本体（QuickER 版 Repository と EF Core の双方に同一テキスト）
        var body =
            "Query().Where(e => e.CustomerId == customerId).OrderByDescending(e => e.Amount).Skip(skip).Take(take).ToListAsync(cancellationToken)";
        content
            .Split(body)
            .Length.Should()
            .Be(3, "QuickER 版 Repository と EF Core 版 Repository の 2 箇所に同一本体が出る");
    }

    /// <summary>単一・件数・文字列一致・IN の各形が生成されることを検証する</summary>
    // 残す理由（P3-4b）：4 つの本体の式（条件・並び替え・文字列一致・IN の訳し方＝P3-6〔簡易 DSL〕の領分）。構造の表明（各メソッドの宣言）は行 code-query-dsl-shapes-declared-on-contract-and-implementations へ移した
    [Fact(DisplayName = "DSL 単一/件数/LIKE/IN の各形が生成される")]
    public void Generate_DslShapes_EmitBodies()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindTop",
                Returns = QueryReturnShape.Single,
                OrderBy =
                {
                    new QueryOrdering
                    {
                        ColumnId = _order.Columns.First(c => c.Name == "Amount").Id,
                        Descending = true,
                    },
                },
            },
            new QueryDefinition
            {
                Name = "CountLarge",
                Returns = QueryReturnShape.Count,
                Parameters =
                {
                    new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
                },
                Condition = "Amount >= @minAmount",
            },
            new QueryDefinition
            {
                Name = "SearchMemo",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "keyword", Type = "string(50)" },
                },
                Condition = "Memo LIKE @keyword",
            },
            new QueryDefinition
            {
                Name = "GetByIds",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "OrderId IN @ids",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain(
                "Query().OrderByDescending(e => e.Amount).FirstOrDefaultAsync(cancellationToken)"
            );
        content
            .Should()
            .Contain("Query().Where(e => e.Amount >= minAmount).CountAsync(cancellationToken)");
        content
            .Should()
            .Contain(
                "Query().Where(e => (e.Memo != null && e.Memo!.Contains(keyword))).ToListAsync(cancellationToken)"
            );
        content
            .Should()
            .Contain("Query().Where(e => ids.Contains(e.OrderId)).ToListAsync(cancellationToken)");
    }

    /// <summary>自由 SQL: QuickER 版 Repository のみ実装され、EF Core 版 Repository は契約宣言のみ（manual 扱い）になることを検証する</summary>
    // 残す理由（P3-4b）：manual 注記の固定の英文（生成器の固定の英文）と、SQL Server の SQL の断片（逐語リテラルの表記）。構造の表明（契約の 3 宣言・EF Core 版に実装が無いこと・生 SQL を実行する呼び出し）は行 code-query-sql-and-manual-contract-declared-efcore-skips-bodies へ移した
    [Fact(DisplayName = "自由 SQL: QuickER のみ実装・EF Core 版 Repository は manual 扱い")]
    public void Generate_SqlQuery_AdoOnlyAndEfManual()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetRecent",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT * FROM [Order] WHERE [Amount] > 0",
                    ["sqlite"] = "SELECT * FROM \"Order\" WHERE \"Amount\" > 0",
                },
            },
            new QueryDefinition
            {
                Name = "SumAmount",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "decimal(12,2)",
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT SUM([Amount]) FROM [Order]" },
            },
            new QueryDefinition
            {
                Name = "SpecialLookup",
                Returns = QueryReturnShape.Single,
                Implementation = QueryImplementationKind.Manual,
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 契約の manual 注記（固定の英文）
        content.Should().Contain("Implementation targets that do not get a generated body");

        // QuickER 版 Repository（sqlserver 方言）の SQL 入り本体の SQL の断片
        content.Should().Contain("@\"SELECT * FROM [Order] WHERE [Amount] > 0\"");
    }

    /// <summary>マルチターゲット: 共有 DSL 本体は両方言に、方言 SQL は該当方言のみに出ることを検証する</summary>
    // 残す理由（P3-4b）：共有 DSL 本体の式（P3-6〔簡易 DSL〕の領分）と、SQL Server の SQL の断片。構造の表明（契約・方言ごとの Repository の宣言）は行 code-query-multi-target-shared-on-both-sql-only-on-its-dialect へ移した
    [Fact(DisplayName = "マルチターゲット: DSL は両方言・SQL は該当方言のみ")]
    public void Generate_MultiTarget_DispatchesByDialect()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
            },
            new QueryDefinition
            {
                Name = "ServerOnly",
                Returns = QueryReturnShape.Count,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "SELECT COUNT(*) FROM [Order]" },
            }
        );

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            RepositoryDialects = ["sqlserver", "sqlite"],
            IncludeDataAnnotations = true,
        };
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            new Dictionary<string, IColumnTypeMapper> { ["sqlserver"] = provider.TypeMapper },
            diagram,
            options
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 共有 DSL 本体は sqlserver / sqlite の両実装に出る（契約 1＋方言実装 2 で計 2 回）
        var sharedBody =
            "Query().Where(e => e.CustomerId == customerId).ToListAsync(cancellationToken)";
        content.Split(sharedBody).Length.Should().Be(3);

        // sqlserver 限定の SQL 本体は 1 回だけ
        content.Split("SELECT COUNT(*) FROM [Order]").Length.Should().Be(2);
    }

    /// <summary>VO 生成時: 比較は VO.Create で包まれ、IN は前置文で持ち上げられることを検証する</summary>
    [Fact(DisplayName = "VO 図: 比較は VO.Create・IN は前置文つき本体になる")]
    public void Generate_WithValueObjects_WrapsComparisons()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
            },
            new QueryDefinition
            {
                Name = "GetByIds",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "ids",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "OrderId IN @ids",
            }
        );

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            IncludeDataAnnotations = true,
            GenerateValueObjects = true,
        };
        var result = Generate(diagram, options);

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content.Should().Contain("e.CustomerId == CustomerIdValue.Create(customerId)");
        content
            .Should()
            .Contain("var idsValues = ids.Select(OrderIdValue.Create).ToList();")
            .And.Contain("idsValues.Contains(e.OrderId)");
    }

    /// <summary>列参照で型付けしたパラメータが、VO 図では VO 型引数＋直接比較・VO なし図ではプリミティブ引数になることを検証する</summary>
    // 残す理由（P3-4b）：VO 図の本体の式（Create なしの直接比較＝P3-6〔簡易 DSL〕の領分）。構造の表明（VO 図は VO 型引数・VO なし図はプリミティブ引数）は行 code-query-column-typed-parameter-follows-value-object / code-query-column-typed-parameter-follows-primitive へ移した
    [Fact(DisplayName = "列参照パラメータ: VO 図は VO 型引数・VO なし図はプリミティブ引数")]
    public void Generate_ColumnTypedParameter_FollowsColumnType()
    {
        var customerColumn = _order.Columns.First(c => c.Name == "CustomerId");

        QueryDefinition CreateQuery() =>
            new()
            {
                Name = "GetByCustomerTyped",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", SourceColumnId = customerColumn.Id },
                },
                Condition = "CustomerId = @customerId",
            };

        // VO 有効: 条件は Create なしの直接比較
        var voOptions = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEfCoreRepositories = true,
            IncludeDataAnnotations = true,
            GenerateValueObjects = true,
        };
        var voResult = Generate(CreateDiagram(CreateQuery()), voOptions);
        voResult.HasErrors.Should().BeFalse(FormatDiagnostics(voResult));
        AllContent(voResult).Should().Contain("Query().Where(e => e.CustomerId == customerId)");
    }

    /// <summary>
    /// NULL 許容の値型列への IN が、メソッド冒頭で NULL 許容型のリストへ持ち上げてから比較されることを
    /// 検証する。素のままだと <c>IReadOnlyList&lt;int&gt;.Contains(int?)</c> の型推論が一致せず
    /// CS1929 でコンパイルできない（VO リストの持ち上げと同じ経路。列が NULL の行は
    /// <c>Contains(null)</c>＝false で SQL / EF Core と揃う）
    /// </summary>
    // 残す理由（P3-4b）：持ち上げ文の式（IN の訳し方＝P3-6〔簡易 DSL〕の領分）。コンパイルの成否は行 compile-query-in-on-nullable-value-type-column へ移した
    [Fact(DisplayName = "NULL 許容の値型列への IN は NULL 許容型リストへ持ち上げる")]
    public void Generate_InOnNullableValueTypeColumn_LiftsToNullableList()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByQuantities",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "quantities",
                        Type = "int32",
                        IsList = true,
                    },
                },
                Condition = "Quantity IN @quantities",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("var quantitiesValues = quantities.Cast<int?>().ToList();")
            .And.Contain("quantitiesValues.Contains(e.Quantity)");
    }

    /// <summary>
    /// 否定 LIKE の等値形（ワイルドカードなしの <c>NOT LIKE 'abc'</c>）と前置 <c>NOT</c> の文字列一致が、
    /// どちらも NULL 前提の内側で否定されることを検証する（docs の「NOT LIKE も NULL 前提の内側＝NULL 行は
    /// どちらの向きでも一致しない」に一致させる）。従来は等値の <c>!=</c> 経路・<c>!(...)</c> 包みへ落ちて
    /// NULL 行が一致に含まれ、<c>col NOT LIKE '%x%'</c> と結果が割れていた。肯定形の <c>LIKE 'abc'</c> は
    /// 従来どおり素の等値比較（NULL 行は一致しない＝結論は同じ）
    /// </summary>
    [Fact(DisplayName = "否定 LIKE の等値形と前置 NOT も NULL 前提の内側で否定される")]
    public void Generate_NegatedLike_StaysInsideNullPremise()
    {
        static QueryDefinition ListQuery(string name, string condition) =>
            new()
            {
                Name = name,
                Returns = QueryReturnShape.List,
                Condition = condition,
            };

        _order.Columns.Add(
            new Column
            {
                Name = "Code",
                DataType = "nvarchar(20)",
                IsNullable = false,
            }
        );
        var diagram = CreateDiagram(
            ListQuery("ExactNegated", "Memo NOT LIKE 'abc'"),
            ListQuery("ExactNegatedNotNull", "Code NOT LIKE 'abc'"),
            ListQuery("PrefixNot", "NOT Memo LIKE '%x%'"),
            ListQuery("DoubleNot", "NOT Memo NOT LIKE '%x%'"),
            ListQuery("ExactPositive", "Memo LIKE 'abc'")
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 等値形の否定: NULL 前提の内側（NULL 行はどちらの向きでも一致しない）
        content.Should().Contain("(e.Memo != null && !(e.Memo == \"abc\"))");
        // 非 NULL 列は前提が不要（従来どおり素の否定）
        content.Should().Contain("!(e.Code == \"abc\")");
        // 前置 NOT は Negated へ畳み込まれ、col NOT LIKE と同じ形になる
        content.Should().Contain("(e.Memo != null && !(e.Memo!.Contains(\"x\")))");
        // 二重否定は肯定形へ畳まれる（前提は保たれる）
        content.Should().Contain("(e.Memo != null && e.Memo!.Contains(\"x\"))");
        // 肯定形のワイルドカードなし LIKE は従来どおり素の等値比較
        content
            .Should()
            .Contain("e.Memo == \"abc\"")
            .And.NotContain("e.Memo != null && e.Memo == \"abc\"");
    }

    /// <summary>
    /// VO 有効 × NULL 許容列への順序比較が「列が NULL でないこと」を AND した形へエミットされることを
    /// 検証する（文字列一致の NULL 前提と同じ流儀）。VO の比較演算子は null を最小として順序付けるため、
    /// 素のままだとインメモリ実行器だけが &lt; / &lt;= で NULL 行を返し、SQL・EF Core（UNKNOWN で脱落）と
    /// 観測結果が割れていた。等値比較は従来どおりガードなし（null == 値 は C# でも false＝3 実装先で一致）
    /// </summary>
    [Fact(DisplayName = "VO 有効: NULL 許容列への順序比較は null ガードの内側にエミットされる")]
    public void Generate_NullableVoOrderedComparison_EmitsNullGuard()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetBelow",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "limit", Type = "int32" },
                },
                Condition = "Quantity < @limit",
            },
            new QueryDefinition
            {
                Name = "GetExact",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "exact", Type = "int32" },
                },
                Condition = "Quantity = @exact",
            }
        );

        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateValueObjects = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("(e.Quantity != null && e.Quantity < QuantityValue.Create(limit))");
        content
            .Should()
            .Contain("e.Quantity == QuantityValue.Create(exact)")
            .And.NotContain("e.Quantity != null && e.Quantity ==");
    }

    /// <summary>
    /// IN の持ち上げリスト変数名（{パラメータ名}Values）が別のパラメータ名と衝突するとき、_ を後置して
    /// 回避することを検証する（衝突したままだとローカル変数が引数を隠して CS0136 になる）
    /// </summary>
    // 残す理由（P3-4b）：持ち上げ変数のリネームの綴り（式本体＝IN の訳し方で P3-6〔簡易 DSL〕の領分）。コンパイルの成否は既存の行 compile-query-in-lift-variable-renamed が見る
    [Fact(DisplayName = "IN の持ち上げ変数はパラメータ名との衝突を _ 後置で回避する")]
    public void Generate_InLiftVariableCollidingWithParameter_IsRenamed()
    {
        _order.Columns.Add(
            new Column
            {
                Name = "Quantity",
                DataType = "int",
                IsNullable = true,
            }
        );
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "GetByQuantitySet",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "quantities",
                        Type = "int32",
                        IsList = true,
                    },
                    new QueryParameter { Name = "quantitiesValues", Type = "int32" },
                },
                Condition = "Quantity IN @quantities AND CustomerId = @quantitiesValues",
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("var quantitiesValues_ = quantities.Cast<int?>().ToList();")
            .And.Contain("quantitiesValues_.Contains(e.Quantity)");

        // コンパイルの成否（持ち上げ変数がリネームされてコンパイルできる）は決まった行 compile-query-in-lift-variable-renamed
    }

    /// <summary>
    /// 生 SQL の逐語リテラル内の連続空行が、レンダラーの空行畳み込みに書き換えられないことを検証する
    /// （SQL の文字列リテラルに連続空行を含むユーザーの SQL が黙って変わっていた）。
    /// リテラルの外の連続空行は従来どおり 1 空行へ畳まれる
    /// </summary>
    // 残す理由（P3-4b）：生成テキストに連続空行（改行 3 連）が無いこと（書式）。SQL の値の完全一致は行 exec-query-raw-sql-blank-lines-inside-literal-are-kept（SQLite で実行）へ移した
    [Fact(DisplayName = "生 SQL の逐語リテラル内の連続空行は畳まれない")]
    public void Generate_RawSqlWithBlankLines_PreservesThemInsideVerbatimLiteral()
    {
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindByBlankMemo",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT * FROM [Order] WHERE [Memo] = 'first\n\n\nsecond'",
                },
            }
        );

        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 生成コードに連続空行は残らない（SQL の改行は文字列の中へ入り、レンダラーの畳み込みの対象外）
        System
            .Text.RegularExpressions.Regex.Matches(
                content,
                $"(?:{System.Text.RegularExpressions.Regex.Escape(Environment.NewLine)}){{3,}}"
            )
            .Count.Should()
            .Be(0);
    }

    /// <summary>
    /// 改行を含む生 SQL の値が、生成コードの中で 1 文字も変わらないことを検証する
    /// （逐語リテラルに改行を生のまま書くと、レンダラーの改行の正規化が CRLF / LF を生成する OS の改行へ、
    /// LINE SEPARATOR などを改行へ書き換え、SQL の文字列リテラルの値が変わっていた）。改行を含まない SQL は
    /// 従来どおり逐語リテラル 1 つ
    /// </summary>
    // 残す理由（P3-4b）：1 行の SQL は逐語リテラル 1 つのまま（書式。CLAUDE.md が名指すガード）。改行を含む SQL の値の完全一致は行 exec-query-raw-sql-line-breaks-and-separators-keep-exact-value（SQLite で実行）へ移した
    [Fact(
        DisplayName = "改行を含む生 SQL は値を変えずに埋め込まれ、1 行の SQL は逐語リテラルのまま"
    )]
    public void Generate_RawSqlWithLineBreaks_KeepsExactValue()
    {
        var separators = $"{(char)0x2028}b{(char)0x2029}c\f{(char)0x85}d";
        var multiLine = "SELECT *\r\nFROM [Order]\nWHERE [Memo] = 'a" + separators + "\r'\r\n\r\n";
        var singleLine = "SELECT * FROM [Order] WHERE [Memo] = \"q\"";
        var diagram = CreateDiagram(
            new QueryDefinition
            {
                Name = "FindMultiLine",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = multiLine },
            },
            new QueryDefinition
            {
                Name = "CountMultiLine",
                Returns = QueryReturnShape.Count,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = "\n" + multiLine },
            },
            new QueryDefinition
            {
                Name = "FindSingleLine",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Sql,
                Sql = { ["sqlserver"] = singleLine },
            }
        );

        var result = Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain(
                "@\"SELECT * FROM [Order] WHERE [Memo] = \"\"q\"\"\"",
                "1 行の SQL は従来の形のまま"
            );

        // コンパイルの成否は決まった行 compile-query-raw-sql-line-breaks-and-single-line
    }

    /// <summary>診断メッセージを失敗理由として整形する</summary>
    private static string FormatDiagnostics(CodeGenerationResult result) =>
        string.Join(" / ", result.Diagnostics.Select(d => d.Message));
}
