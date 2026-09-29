using AwesomeAssertions;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// 制約名の安全化と既定名の合成（<see cref="ConstraintNames"/>）の規則そのものを検証するテストクラス
/// </summary>
/// <remarks>
/// DDL 生成・DB 同期・GUI・内蔵チャット・MCP のツールが共有する規則なので、ここで規則を固定し、
/// 各経路が同じ名前を作ることは経路ごとのテストが確かめる。
/// </remarks>
public class ConstraintNamesTests
{
    [Theory(DisplayName = "SafeName は前後の空白を除いてから . と空白を _ へ置き換える")]
    [InlineData("Orders", "Orders")]
    [InlineData(" Orders", "Orders")]
    [InlineData("Orders  ", "Orders")]
    [InlineData("dbo.Orders", "dbo_Orders")]
    [InlineData("Order Details", "Order_Details")]
    [InlineData("  dbo.Order Details ", "dbo_Order_Details")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SafeName_TrimsThenReplaces(string? name, string expected)
    {
        ConstraintNames.SafeName(name).Should().Be(expected);
    }

    /// <summary>安全化済みの名前をもう一度通しても変わらない（先に安全化してから合成する経路と結果が揃う）</summary>
    [Theory(DisplayName = "SafeName は冪等")]
    [InlineData(" dbo.Order Details ")]
    [InlineData("Orders")]
    public void SafeName_IsIdempotent(string name)
    {
        var once = ConstraintNames.SafeName(name);

        ConstraintNames.SafeName(once).Should().Be(once);
    }

    [Fact(DisplayName = "外部キーの既定名は FK_{子}_{親}（両方を安全化）")]
    public void ForeignKey_ComposesSafeNames()
    {
        ConstraintNames
            .ForeignKey(" dbo.Orders", "dbo.Customers")
            .Should()
            .Be("FK_dbo_Orders_dbo_Customers");
    }

    [Fact(DisplayName = "主キーの既定名は PK_{テーブル}（安全化）")]
    public void PrimaryKey_ComposesSafeName()
    {
        ConstraintNames.PrimaryKey(" dbo.Orders ").Should().Be("PK_dbo_Orders");
    }

    /// <summary>
    /// 一意制約の合成名も同じ安全化を通す（DDL・生成コード・ツールの要約表示が同じ名前を作る）
    /// </summary>
    [Fact(DisplayName = "一意制約の合成名もテーブル名と列名を安全化する")]
    public void UniqueConstraintSynthesizeName_UsesSafeName()
    {
        UniqueConstraint
            .SynthesizeName(" dbo.Orders", ["order code", "tenant"])
            .Should()
            .Be("UQ_dbo_Orders_order_code_tenant");
    }

    /// <summary>
    /// 先に方言の安全化を通してから合成する経路（DDL 生成）と、素の名前から合成する経路（生成コード・ツール）が
    /// 同じ名前になる
    /// </summary>
    [Fact(DisplayName = "一意制約の合成名は、先に安全化しても結果が同じ")]
    public void UniqueConstraintSynthesizeName_AgreesWithPresanitizedInput()
    {
        var direct = UniqueConstraint.SynthesizeName("dbo.Orders", ["code"]);
        var presanitized = UniqueConstraint.SynthesizeName(
            ConstraintNames.SafeName("dbo.Orders"),
            [ConstraintNames.SafeName("code")]
        );

        presanitized.Should().Be(direct);
    }
}
