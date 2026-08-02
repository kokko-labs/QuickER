using AwesomeAssertions;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>名前付きクエリ条件（ミニ DSL）のパーサ・検証を網羅するテストクラス</summary>
public class QueryConditionParserTests
{
    /// <summary>検証用のエンティティ（Order: CustomerId / Amount / Memo / 顧客名）を作る</summary>
    private static Entity CreateOrderEntity()
    {
        var entity = new Entity { TableName = "Order" };
        entity.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Amount", DataType = "decimal(12,2)" });
        entity.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });
        entity.Columns.Add(new Column { Name = "顧客名", DataType = "nvarchar(50)" });
        return entity;
    }

    /// <summary>標準のパラメータ定義（customerId / minAmount / keyword / statuses(リスト)）</summary>
    private static List<QueryParameter> CreateParameters() =>
        [
            new QueryParameter { Name = "customerId", Type = "int32" },
            new QueryParameter { Name = "minAmount", Type = "decimal(12,2)" },
            new QueryParameter { Name = "keyword", Type = "string(50)" },
            new QueryParameter
            {
                Name = "statuses",
                Type = "int32",
                IsList = true,
            },
        ];

    /// <summary>ParseAndValidate のショートカット</summary>
    private static ConditionParseResult ParseValid(string text) =>
        QueryConditionParser.ParseAndValidate(text, CreateOrderEntity(), CreateParameters());

    // ---------------- 構文（成功系） ----------------

    /// <summary>比較・AND・括弧・OR・NOT を含む式が構文木になることを検証する</summary>
    [Fact(DisplayName = "比較・論理結合・括弧・NOT がパースできる")]
    public void Parse_LogicalCombination_BuildsTree()
    {
        var result = ParseValid(
            "CustomerId = @customerId AND (Amount >= @minAmount OR NOT Memo IS NULL)"
        );

        result.Success.Should().BeTrue();
        var root = result.Root.Should().BeOfType<LogicalNode>().Which;
        root.Operator.Should().Be(LogicalOperator.And);
        root.Left.Should().BeOfType<ComparisonNode>();
        var right = root.Right.Should().BeOfType<LogicalNode>().Which;
        right.Operator.Should().Be(LogicalOperator.Or);
        right.Right.Should().BeOfType<NotNode>().Which.Operand.Should().BeOfType<NullCheckNode>();
    }

    /// <summary>AND が OR より強く結合することを検証する</summary>
    [Fact(DisplayName = "AND は OR より優先される")]
    public void Parse_AndBindsTighterThanOr()
    {
        var result = ParseValid("CustomerId = 1 OR CustomerId = 2 AND Amount > 0");

        result.Success.Should().BeTrue();
        var root = result.Root.Should().BeOfType<LogicalNode>().Which;
        root.Operator.Should().Be(LogicalOperator.Or);
        root.Right.Should().BeOfType<LogicalNode>().Which.Operator.Should().Be(LogicalOperator.And);
    }

    /// <summary>全比較演算子（= &lt;&gt; != &lt; &lt;= &gt; &gt;=）がパースできることを検証する</summary>
    [Theory(DisplayName = "全比較演算子がパースできる")]
    [InlineData("=", ComparisonOperator.Equal)]
    [InlineData("<>", ComparisonOperator.NotEqual)]
    [InlineData("!=", ComparisonOperator.NotEqual)]
    [InlineData("<", ComparisonOperator.Less)]
    [InlineData("<=", ComparisonOperator.LessOrEqual)]
    [InlineData(">", ComparisonOperator.Greater)]
    [InlineData(">=", ComparisonOperator.GreaterOrEqual)]
    public void Parse_AllComparisonOperators(string op, ComparisonOperator expected)
    {
        var result = ParseValid($"Amount {op} @minAmount");

        result.Success.Should().BeTrue();
        result.Root.Should().BeOfType<ComparisonNode>().Which.Operator.Should().Be(expected);
    }

    /// <summary>数値（負数・小数）・文字列リテラルがオペランドになることを検証する</summary>
    [Fact(DisplayName = "数値・負数・文字列リテラルがパースできる")]
    public void Parse_Literals()
    {
        var negative = ParseValid("Amount > -1.5");
        negative.Success.Should().BeTrue();
        negative
            .Root.Should()
            .BeOfType<ComparisonNode>()
            .Which.Operand.Should()
            .BeOfType<NumberOperand>()
            .Which.Literal.Should()
            .Be("-1.5");

        var text = ParseValid("Memo = 'it''s done'");
        text.Success.Should().BeTrue();
        text.Root.Should()
            .BeOfType<ComparisonNode>()
            .Which.Operand.Should()
            .BeOfType<StringOperand>()
            .Which.Value.Should()
            .Be("it's done");
    }

    /// <summary>IS NULL / IS NOT NULL がパースできることを検証する</summary>
    [Fact(DisplayName = "IS NULL / IS NOT NULL がパースできる")]
    public void Parse_NullChecks()
    {
        ParseValid("Memo IS NULL")
            .Root.Should()
            .BeOfType<NullCheckNode>()
            .Which.IsNotNull.Should()
            .BeFalse();
        ParseValid("Memo IS NOT NULL")
            .Root.Should()
            .BeOfType<NullCheckNode>()
            .Which.IsNotNull.Should()
            .BeTrue();
    }

    /// <summary>IN / NOT IN がリストパラメータでパースできることを検証する</summary>
    [Fact(DisplayName = "IN / NOT IN がパースできる")]
    public void Parse_In()
    {
        var result = ParseValid("CustomerId IN @statuses");
        result.Success.Should().BeTrue();
        var node = result.Root.Should().BeOfType<InNode>().Which;
        node.Negated.Should().BeFalse();
        node.Parameter.ResolvedName.Should().Be("statuses");

        ParseValid("CustomerId NOT IN @statuses")
            .Root.Should()
            .BeOfType<InNode>()
            .Which.Negated.Should()
            .BeTrue();
    }

    /// <summary>キーワードの大文字小文字と日本語列名が扱えることを検証する</summary>
    [Fact(DisplayName = "キーワードは大文字小文字を区別せず、日本語列名も参照できる")]
    public void Parse_CaseInsensitiveKeywordsAndJapaneseColumns()
    {
        var result = ParseValid("顧客名 like @keyword and Amount is not null");

        result.Success.Should().BeTrue();
        result.ColumnReferences.Should().HaveCount(2);
        result.ColumnReferences[0].ResolvedName.Should().Be("顧客名");
    }

    /// <summary>列名の大文字小文字ゆれが正準名へ解決されることを検証する</summary>
    [Fact(DisplayName = "列名は大文字小文字を区別せず正準名へ解決される")]
    public void Validate_ColumnNameCaseInsensitive()
    {
        var result = ParseValid("customerid = @customerId");

        result.Success.Should().BeTrue();
        result.ColumnReferences[0].ResolvedName.Should().Be("CustomerId");
        result.ColumnReferences[0].ResolvedColumnId.Should().NotBeNull();
    }

    // ---------------- LIKE の意味論 ----------------

    /// <summary>LIKE のリテラルパターンが % 位置で一致種別に分解されることを検証する</summary>
    [Theory(DisplayName = "LIKE リテラルは % 位置で Contains/StartsWith/EndsWith に分解される")]
    [InlineData("'%abc%'", StringMatchKind.Contains)]
    [InlineData("'abc%'", StringMatchKind.StartsWith)]
    [InlineData("'%abc'", StringMatchKind.EndsWith)]
    public void Parse_LikeLiteral_Decomposes(string pattern, StringMatchKind expected)
    {
        var result = ParseValid($"Memo LIKE {pattern}");

        result.Success.Should().BeTrue();
        var node = result.Root.Should().BeOfType<StringMatchNode>().Which;
        node.Kind.Should().Be(expected);
        node.Operand.Should().BeOfType<StringOperand>().Which.Value.Should().Be("abc");
    }

    /// <summary>ワイルドカードなしの LIKE リテラルは等値比較になることを検証する</summary>
    [Fact(DisplayName = "ワイルドカードなしの LIKE は等値比較になる")]
    public void Parse_LikeWithoutWildcard_BecomesEquality()
    {
        ParseValid("Memo LIKE 'abc'")
            .Root.Should()
            .BeOfType<ComparisonNode>()
            .Which.Operator.Should()
            .Be(ComparisonOperator.Equal);

        ParseValid("Memo NOT LIKE 'abc'")
            .Root.Should()
            .BeOfType<NotNode>()
            .Which.Operand.Should()
            .BeOfType<ComparisonNode>();
    }

    /// <summary>LIKE @param は部分一致（Contains）に固定されることを検証する</summary>
    [Fact(DisplayName = "LIKE @param は部分一致になる")]
    public void Parse_LikeParameter_BecomesContains()
    {
        var node = ParseValid("Memo LIKE @keyword").Root.Should().BeOfType<StringMatchNode>().Which;
        node.Kind.Should().Be(StringMatchKind.Contains);
        node.Operand.Should().BeOfType<ParameterOperand>();
    }

    /// <summary>CONTAINS / STARTSWITH / ENDSWITH キーワードがパースできることを検証する</summary>
    [Theory(DisplayName = "CONTAINS / STARTSWITH / ENDSWITH がパースできる")]
    [InlineData("CONTAINS", StringMatchKind.Contains)]
    [InlineData("STARTSWITH", StringMatchKind.StartsWith)]
    [InlineData("ENDSWITH", StringMatchKind.EndsWith)]
    public void Parse_ExplicitStringMatchKeywords(string keyword, StringMatchKind expected)
    {
        ParseValid($"Memo {keyword} @keyword")
            .Root.Should()
            .BeOfType<StringMatchNode>()
            .Which.Kind.Should()
            .Be(expected);
    }

    /// <summary>内部 % や _ を含む LIKE パターンが診断エラーになることを検証する</summary>
    [Theory(DisplayName = "表現できない LIKE パターンは診断エラー")]
    [InlineData("'a%b'")]
    [InlineData("'a_c%'")]
    public void Parse_UnsupportedLikePattern_Fails(string pattern)
    {
        var result = ParseValid($"Memo LIKE {pattern}");

        result.Root.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("LIKE");
    }

    // ---------------- 構文（エラー系） ----------------

    /// <summary>途中で終わる式・想定外トークン・閉じない文字列が診断になることを検証する</summary>
    [Theory(DisplayName = "構文エラーは診断 1 件で打ち切られる")]
    [InlineData("CustomerId =")]
    [InlineData("CustomerId = @customerId AND")]
    [InlineData("= @customerId")]
    [InlineData("CustomerId = @customerId extra")]
    [InlineData("(CustomerId = 1")]
    [InlineData("Memo = 'unterminated")]
    [InlineData("CustomerId IS 1")]
    [InlineData("CustomerId IN 1")]
    [InlineData("Memo LIKE 1")]
    public void Parse_SyntaxErrors_ReportDiagnostic(string text)
    {
        var result = ParseValid(text);

        result.Root.Should().BeNull();
        result.Diagnostics.Should().ContainSingle();
    }

    /// <summary>診断が原文内の位置を指すことを検証する</summary>
    [Fact(DisplayName = "診断は原文内の位置を保持する")]
    public void Parse_Diagnostic_CarriesPosition()
    {
        var result = ParseValid("CustomerId = @customerId AND unknown_col = 1");

        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics[0].Position.Should().Be("CustomerId = @customerId AND ".Length);
        result.Diagnostics[0].Length.Should().Be("unknown_col".Length);
    }

    // ---------------- 検証（列・パラメータ突合） ----------------

    /// <summary>存在しない列参照が診断エラーになることを検証する</summary>
    [Fact(DisplayName = "未知の列参照は診断エラー")]
    public void Validate_UnknownColumn_Fails()
    {
        var result = ParseValid("Nope = @customerId");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("Nope");
    }

    /// <summary>未定義パラメータ参照が診断エラーになることを検証する</summary>
    [Fact(DisplayName = "未定義パラメータは診断エラー")]
    public void Validate_UnknownParameter_Fails()
    {
        var result = ParseValid("CustomerId = @nope");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("nope");
    }

    /// <summary>IN に非リストパラメータを使うと診断エラーになることを検証する</summary>
    [Fact(DisplayName = "IN × 非リストパラメータは診断エラー")]
    public void Validate_InWithScalarParameter_Fails()
    {
        var result = ParseValid("CustomerId IN @customerId");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("customerId");
    }

    /// <summary>リストパラメータを比較に使うと診断エラーになることを検証する</summary>
    [Fact(DisplayName = "比較 × リストパラメータは診断エラー")]
    public void Validate_ComparisonWithListParameter_Fails()
    {
        var result = ParseValid("CustomerId = @statuses");

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("statuses");
    }

    /// <summary>検証エラーは複数件を収集することを検証する</summary>
    [Fact(DisplayName = "検証エラーは複数件収集される")]
    public void Validate_CollectsMultipleErrors()
    {
        var result = ParseValid("Nope1 = @nope2 AND Nope3 = 1");

        result.Diagnostics.Should().HaveCount(3);
    }

    /// <summary>Parse（検証なし）は列参照の位置一覧を返すことを検証する（リネーム書き換え用）</summary>
    [Fact(DisplayName = "Parse は列参照の位置一覧を返す")]
    public void Parse_CollectsColumnReferenceSpans()
    {
        var text = "CustomerId = @customerId AND Amount > 0";
        var result = QueryConditionParser.Parse(text);

        result.Root.Should().NotBeNull();
        result.ColumnReferences.Should().HaveCount(2);
        result.ColumnReferences[0].Position.Should().Be(0);
        result.ColumnReferences[0].Length.Should().Be("CustomerId".Length);
        result.ColumnReferences[1].Position.Should().Be(text.IndexOf("Amount"));
        result.ColumnReferences[1].ResolvedName.Should().BeNull();
    }

    // ---------------- 結合強度（NOT）・LIKE 退化パターン・意味検証（型整合） ----------------

    /// <summary>NOT が AND より強く結合する（NOT は直後の述語のみに掛かる）ことを木構造で検証する</summary>
    [Fact(DisplayName = "NOT は AND より強く結合する（直後の述語のみに掛かる）")]
    public void Parse_NotBindsTighterThanAnd()
    {
        var result = ParseValid("NOT CustomerId = 1 AND Amount > 0");

        result.Success.Should().BeTrue();
        var root = result.Root.Should().BeOfType<LogicalNode>().Which;
        root.Operator.Should().Be(LogicalOperator.And);
        root.Left.Should().BeOfType<NotNode>().Which.Operand.Should().BeOfType<ComparisonNode>();
        root.Right.Should().BeOfType<ComparisonNode>();
    }

    /// <summary>括弧付きの NOT は論理結合全体に掛かる（NOT が根になる）ことを検証する</summary>
    [Fact(DisplayName = "NOT (A OR B) は論理結合全体を否定する")]
    public void Parse_NotWithParentheses_NegatesWholeGroup()
    {
        var result = ParseValid("NOT (CustomerId = 1 OR Amount > 0)");

        result.Success.Should().BeTrue();
        var not = result.Root.Should().BeOfType<NotNode>().Which;
        not.Operand.Should().BeOfType<LogicalNode>().Which.Operator.Should().Be(LogicalOperator.Or);
    }

    /// <summary>二重 NOT が入れ子の NotNode になることを検証する</summary>
    [Fact(DisplayName = "NOT NOT は入れ子の否定になる")]
    public void Parse_DoubleNot_Nests()
    {
        var result = ParseValid("NOT NOT CustomerId = 1");

        result.Success.Should().BeTrue();
        result
            .Root.Should()
            .BeOfType<NotNode>()
            .Which.Operand.Should()
            .BeOfType<NotNode>()
            .Which.Operand.Should()
            .BeOfType<ComparisonNode>();
    }

    /// <summary>空文字リテラルの LIKE（ワイルドカードなしの退化形）が等値比較になることを検証する</summary>
    [Fact(DisplayName = "LIKE ''（空文字）は空文字との等値比較になる")]
    public void Parse_LikeEmptyLiteral_BecomesEmptyEquality()
    {
        var result = ParseValid("Memo LIKE ''");

        result.Success.Should().BeTrue();
        var comparison = result.Root.Should().BeOfType<ComparisonNode>().Which;
        comparison.Operator.Should().Be(ComparisonOperator.Equal);
        comparison.Operand.Should().BeOfType<StringOperand>().Which.Value.Should().BeEmpty();
    }

    /// <summary>'%' のみの LIKE（全件一致の退化形）が空文字の Contains になることを検証する</summary>
    [Fact(DisplayName = "LIKE '%'（ワイルドカードのみ）は空文字の Contains になる")]
    public void Parse_LikeWildcardOnly_BecomesEmptyContains()
    {
        var result = ParseValid("Memo LIKE '%'");

        result.Success.Should().BeTrue();
        var match = result.Root.Should().BeOfType<StringMatchNode>().Which;
        match.Kind.Should().Be(StringMatchKind.Contains);
        match.Operand.Should().BeOfType<StringOperand>().Which.Value.Should().BeEmpty();
    }

    /// <summary>NULL 非許容列への IS NULL / IS NOT NULL が診断エラーになることを検証する</summary>
    [Theory(DisplayName = "NULL 非許容列への IS [NOT] NULL は診断エラー")]
    [InlineData("CustomerId IS NULL")]
    [InlineData("CustomerId IS NOT NULL")]
    public void Validate_NullCheckOnNonNullableColumn_Fails(string text)
    {
        var result = ParseValid(text);

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("CustomerId");
    }

    /// <summary>文字列一致系（LIKE / CONTAINS）にリストパラメータを使うと診断エラーになることを検証する</summary>
    [Theory(DisplayName = "文字列一致 × リストパラメータは診断エラー")]
    [InlineData("Memo LIKE @statuses")]
    [InlineData("Memo CONTAINS @statuses")]
    public void Validate_StringMatchWithListParameter_Fails(string text)
    {
        var result = ParseValid(text);

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("statuses");
    }
}
