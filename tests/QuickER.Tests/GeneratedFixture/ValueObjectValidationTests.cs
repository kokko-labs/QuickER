using System;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 順序付き基底（<see cref="ValueObjectOrderedBase{TSelf,TValue}"/>）の比較演算子と <c>CompareTo</c> を、
/// コミット済みフィクスチャ（<c>GeneratedFixture.g.cs</c>）の実型に対して POCO レベルで検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// 検証・生成・等値・表示・部分一致の決まった形の表明は <see cref="ValueObjectScenarioTests"/> の表にある。
/// 比較は 1 つの組に複数の観点（演算子・符号・null・型不一致）を連続して見るため、ここに残す。
/// </remarks>
public sealed class ValueObjectValidationTests
{
    // ===== (5) ValueObjectOrderedBase の比較 =====

    [Fact(DisplayName = "順序付き VO（int）の < > <= >= と CompareTo が値順で機能する")]
    public void 順序付きVO_int_の比較()
    {
        var one = CustomerIdValue.Create(1);
        var two = CustomerIdValue.Create(2);
        var anotherTwo = CustomerIdValue.Create(2);

        (one < two).Should().BeTrue();
        (two > one).Should().BeTrue();
        (one <= two).Should().BeTrue();
        (two >= anotherTwo).Should().BeTrue();
        (two <= anotherTwo).Should().BeTrue();

        one.CompareTo(two).Should().BeNegative();
        two.CompareTo(one).Should().BePositive();
        two.CompareTo(anotherTwo).Should().Be(0);
        // null との比較は自分が大きい
        two.CompareTo(null).Should().BePositive();
    }

    [Fact(
        DisplayName = "順序付き VO（decimal）の比較演算子と非ジェネリック IComparable が機能する"
    )]
    public void 順序付きVO_decimal_の比較()
    {
        var small = AmountValue.Create(10.00m);
        var large = AmountValue.Create(20.00m);

        (small < large).Should().BeTrue();
        (large >= small).Should().BeTrue();

        ((IComparable)small).CompareTo(large).Should().BeNegative();
        // 型不一致は ArgumentException
        var act = () => ((IComparable)small).CompareTo("not a vo");
        act.Should().Throw<ArgumentException>();
    }
}
