using FluentAssertions;
using QuickER.CodeGen.CSharp.Queries;

namespace QuickER.Tests.Generator;

/// <summary>列リネームに伴う条件式（ミニ DSL）の列参照書き換えを網羅するテストクラス</summary>
public class QueryConditionRenamerTests
{
    /// <summary>単純な等値条件の列名が新名へ置換されることを検証する</summary>
    [Fact(DisplayName = "RenameColumn: 単純な列参照を置換する")]
    public void RenameColumn_SimpleReference_Replaced()
    {
        var result = QueryConditionRenamer.RenameColumn(
            "CustomerId = @customerId",
            "CustomerId",
            "BuyerId"
        );

        result.Should().Be("BuyerId = @customerId");
    }

    /// <summary>旧列名の大文字小文字がゆれていても一致・置換され、置換後は新名の表記になることを検証する</summary>
    [Fact(DisplayName = "RenameColumn: 大文字小文字ゆれの列参照も置換する")]
    public void RenameColumn_CaseInsensitiveMatch_Replaced()
    {
        var result = QueryConditionRenamer.RenameColumn(
            "customerid = @customerId",
            "CustomerId",
            "BuyerId"
        );

        result.Should().Be("BuyerId = @customerId");
    }

    /// <summary>同一列名が複数箇所に出現する場合、すべてが置換されることを検証する</summary>
    [Fact(DisplayName = "RenameColumn: 複数箇所の列参照をすべて置換する")]
    public void RenameColumn_MultipleOccurrences_AllReplaced()
    {
        var result = QueryConditionRenamer.RenameColumn(
            "Amount > 100 OR Amount < -10",
            "Amount",
            "Total"
        );

        result.Should().Be("Total > 100 OR Total < -10");
    }

    /// <summary>
    /// パラメータ名（@Amount）に旧列名と同じ綴りが含まれても、列参照でないため置換されないことを検証する
    /// </summary>
    [Fact(DisplayName = "RenameColumn: 同名のパラメータ名は置換しない")]
    public void RenameColumn_SameNameParameter_NotReplaced()
    {
        var result = QueryConditionRenamer.RenameColumn("Amount > @Amount", "Amount", "Total");

        // 列参照 Amount のみ置換し、@Amount パラメータはそのまま残す
        result.Should().Be("Total > @Amount");
    }

    /// <summary>
    /// 文字列リテラル中に旧列名と同じ綴りが含まれても、列参照でないため置換されないことを検証する
    /// </summary>
    [Fact(DisplayName = "RenameColumn: 文字列リテラル中の同名文字列は置換しない")]
    public void RenameColumn_SameStringInLiteral_NotReplaced()
    {
        var result = QueryConditionRenamer.RenameColumn(
            "Memo CONTAINS 'Memo is here'",
            "Memo",
            "Note"
        );

        // 列参照 Memo のみ置換し、リテラル 'Memo is here' はそのまま残す
        result.Should().Be("Note CONTAINS 'Memo is here'");
    }

    /// <summary>対象の旧列名が条件式に出現しない場合、原文がそのまま返ることを検証する</summary>
    [Fact(DisplayName = "RenameColumn: 対象列が無ければ原文のまま")]
    public void RenameColumn_NoMatch_ReturnsOriginal()
    {
        var result = QueryConditionRenamer.RenameColumn(
            "CustomerId = @customerId",
            "Amount",
            "Total"
        );

        result.Should().Be("CustomerId = @customerId");
    }

    /// <summary>構文エラーの条件は列参照位置が信用できないため、書き換えず原文のまま返ることを検証する</summary>
    [Fact(DisplayName = "RenameColumn: 構文エラーの条件は原文のまま")]
    public void RenameColumn_SyntaxError_ReturnsOriginal()
    {
        // 演算子の右辺が欠落した構文エラー
        var input = "CustomerId = ";

        var result = QueryConditionRenamer.RenameColumn(input, "CustomerId", "BuyerId");

        result.Should().Be(input);
    }

    /// <summary>旧名と新名が同一（大文字小文字含む）なら原文をそのまま返すことを検証する</summary>
    [Fact(DisplayName = "RenameColumn: 旧名と新名が同一なら原文のまま")]
    public void RenameColumn_SameName_ReturnsOriginal()
    {
        var input = "CustomerId = @customerId";

        var result = QueryConditionRenamer.RenameColumn(input, "CustomerId", "CustomerId");

        result.Should().Be(input);
    }
}
