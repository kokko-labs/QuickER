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

    /// <summary>
    /// 否定 LIKE の等値形（ワイルドカードなしの <c>NOT LIKE 'abc'</c>）と前置 <c>NOT</c> の文字列一致が、
    /// どちらも NULL 前提の内側で否定されることを検証する（docs の「NOT LIKE も NULL 前提の内側＝NULL 行は
    /// どちらの向きでも一致しない」に一致させる）。従来は等値の <c>!=</c> 経路・<c>!(...)</c> 包みへ落ちて
    /// NULL 行が一致に含まれ、<c>col NOT LIKE '%x%'</c> と結果が割れていた。肯定形の <c>LIKE 'abc'</c> は
    /// 従来どおり素の等値比較（NULL 行は一致しない＝結論は同じ）
    /// </summary>
    // 残す理由（P3-6）：動作に出ない最適化の形（NULL 非許容の列の否定に NULL 前提を付けない・肯定のワイルドカードなし LIKE に NULL 前提を付けない）だけを残す。NULL 前提の内側の否定・前置 NOT・二重否定の意味は DslFuzz の NotNesting（single・double）と網羅の単位の決まった形 KF1（NOT NOT LIKE 'x'）・KF10（NOT s NOT LIKE '%x%'）・KF11（NOT s LIKE '%x%'）
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

        // 非 NULL 列は前提が不要（従来どおり素の否定）
        content.Should().Contain("!(e.Code == literal1)");
        // 肯定形のワイルドカードなし LIKE は従来どおり素の等値比較
        content
            .Should()
            .Contain("e.Memo == literal1")
            .And.NotContain("e.Memo != null && e.Memo == literal1");
    }

    /// <summary>
    /// VO 有効 × NULL 許容列への順序比較が「列が NULL でないこと」を AND した形へエミットされることを
    /// 検証する（文字列一致の NULL 前提と同じ流儀）。VO の比較演算子は null を最小として順序付けるため、
    /// 素のままだとインメモリ実行器だけが &lt; / &lt;= で NULL 行を返し、SQL・EF Core（UNKNOWN で脱落）と
    /// 観測結果が割れていた。等値比較は従来どおりガードなし（null == 値 は C# でも false＝3 実装先で一致）
    /// </summary>
    // 残す理由（P3-6）：動作に出ない最適化の形（値オブジェクトの NULL 許容の列の等値に NULL ガードを付けない）だけを残す。順序比較の NULL ガードの意味は DslFuzz の sqlite-vo / sqlserver-vo の単位（NULL 許容の int 列 × compare-lt/le/gt/ge-param をインメモリで実行してオラクルと照合）
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
            .Contain("e.Quantity == QuantityValue.Create(exact)")
            .And.NotContain("e.Quantity != null && e.Quantity ==");
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
