using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成された値オブジェクト（VO）の検証・生成・等値・順序・表示 API を、コミット済みフィクスチャ
/// （<c>GeneratedFixture.g.cs</c>）の実型に対して POCO レベルで検証する（DB 不要・CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// 検証観点は以下:
/// (1) 文字列 VO の最大長超過で <see cref="ValueObjectValidationException"/> が投げられ、例外メッセージと
/// <see cref="ValueObjectValidationException.Errors"/> に <see cref="ValueObjectValidationMessages"/> の文言が乗ること。
/// (2) decimal VO の precision/scale 違反で「丸めず弾く」こと。
/// (3) 境界値ちょうどは通ること。
/// (4) <c>TryCreate</c> の true/false 両側。
/// (5) 順序付き基底（<see cref="ValueObjectOrderedBase{TSelf,TValue}"/>）の比較演算子と <c>CompareTo</c>。
/// (6) <c>Equals</c> / <c>GetHashCode</c> / <c>ToString</c> / <c>DisplayValue</c> / <c>==</c> / <c>!=</c>。
/// (7) bool 基底（<see cref="ValueObjectBooleanBase{TSelf}"/>）の True/False/IsTrue/IsFalse。
/// (8) 文字列基底（<see cref="ValueObjectStringBase{TSelf}"/>）の Contains/StartsWith/EndsWith。
/// </para>
/// <para>
/// この図の decimal VO（AmountValue / BalanceValue）は precision=10・scale=2＝整数部の最大桁は 8。
/// 文字列 VO（NameValue / BioValue / MemoValue）は最大長 50。int/bool VO（CustomerIdValue /
/// OrderIdValue / ProfileIdValue / IsActiveValue）は組み込み検証を持たないため <c>TryCreate</c> の false 経路は無い。
/// </para>
/// </remarks>
public sealed class ValueObjectValidationTests
{
    // ===== (1) 文字列 VO: 最大長超過は throw =====

    [Fact(
        DisplayName = "文字列 VO は最大長 50 超過で ValueObjectValidationException を投げ、メッセージに最大長文言が乗る"
    )]
    public void 文字列VOは最大長超過でthrowする()
    {
        var tooLong = new string('a', 51);

        var act = () => BioValue.Create(tooLong);

        var expectedMessage = "Enter at most 50 characters. (currently 51 characters)";
        var ex = act.Should().Throw<ValueObjectValidationException>().Which;
        ex.ValueObjectType.Should().Be(typeof(BioValue));
        ex.Errors.Should().ContainSingle().Which.Should().Be(expectedMessage);
        // 例外メッセージは「The value of {型名} is invalid: {エラー結合}」形式
        ex.Message.Should().Be($"The value of BioValue is invalid: {expectedMessage}");
    }

    [Theory(DisplayName = "全ての文字列 VO（Name/Bio/Memo）が最大長 50 超過で throw する")]
    [InlineData("Name")]
    [InlineData("Bio")]
    [InlineData("Memo")]
    public void 各文字列VOが最大長超過でthrowする(string which)
    {
        var tooLong = new string('x', 51);

        Action act = which switch
        {
            "Name" => () => NameValue.Create(tooLong),
            "Bio" => () => BioValue.Create(tooLong),
            _ => () => MemoValue.Create(tooLong),
        };

        act.Should()
            .Throw<ValueObjectValidationException>()
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Be("Enter at most 50 characters. (currently 51 characters)");
    }

    [Fact(DisplayName = "文字列 VO の境界値（ちょうど 50 文字）は通る")]
    public void 文字列VOの境界値は通る()
    {
        var exactly50 = new string('a', 50);

        var vo = NameValue.Create(exactly50);

        vo.Value.Should().Be(exactly50);
    }

    // ===== (2) decimal VO: precision/scale 違反は丸めず弾く =====

    [Fact(
        DisplayName = "decimal VO は小数部が scale(2) を超えると丸めず ScaleExceeded で throw する"
    )]
    public void decimalVOはscale超過で丸めずthrowする()
    {
        // 1.234m は小数第 3 位まで＝scale 3 > 2。1.23 へ丸められず弾かれる。
        var act = () => AmountValue.Create(1.234m);

        act.Should()
            .Throw<ValueObjectValidationException>()
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Be("Enter at most 2 digits after the decimal point.");
    }

    [Fact(
        DisplayName = "decimal VO は整数部が precision-scale(8) 桁を超えると PrecisionExceeded で throw する"
    )]
    public void decimalVOはprecision超過でthrowする()
    {
        // 123456789m は整数部 9 桁 > 8。
        var act = () => BalanceValue.Create(123456789m);

        act.Should()
            .Throw<ValueObjectValidationException>()
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Be("Enter at most 8 digits in the integer part.");
    }

    [Fact(
        DisplayName = "decimal VO の境界値（整数部 8 桁・小数 2 桁）は通り、末尾ゼロも保持される"
    )]
    public void decimalVOの境界値は通る()
    {
        var boundary = 12345678.99m;

        var vo = AmountValue.Create(boundary);

        vo.Value.Should().Be(boundary);
    }

    [Fact(
        DisplayName = "decimal VO は scale 超過と precision 超過が同時に起きると 2 件のエラーを積む"
    )]
    public void decimalVOは複数違反で複数エラーになる()
    {
        // 整数部 9 桁・小数 3 桁の値
        var act = () => AmountValue.Create(123456789.123m);

        act.Should().Throw<ValueObjectValidationException>().Which.Errors.Should().HaveCount(2);
    }

    // ===== (3)(4) TryCreate の true/false 両側 =====

    [Fact(DisplayName = "TryCreate: 文字列 VO は違反で false・空エラーなし、正常で true・エラー空")]
    public void 文字列VOのTryCreate両側()
    {
        NameValue.TryCreate(new string('a', 51), out var failed, out var errors).Should().BeFalse();
        failed.Should().BeNull();
        errors.Should().NotBeEmpty();

        NameValue.TryCreate("valid", out var ok, out var okErrors).Should().BeTrue();
        ok!.Value.Should().Be("valid");
        okErrors.Should().BeEmpty();
    }

    [Fact(DisplayName = "TryCreate: decimal VO は違反で false、正常で true")]
    public void decimalVOのTryCreate両側()
    {
        AmountValue.TryCreate(1.234m, out var failed, out var errors).Should().BeFalse();
        failed.Should().BeNull();
        errors.Should().NotBeEmpty();

        AmountValue.TryCreate(12.34m, out var ok, out var okErrors).Should().BeTrue();
        ok!.Value.Should().Be(12.34m);
        okErrors.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "TryCreate: 組み込み検証を持たない int/bool VO は正常入力で常に true・エラー空"
    )]
    public void 検証なしVOのTryCreateは常にtrue()
    {
        // これらの VO は OnValidate 未実装＝false 経路が構造的に存在しない（正常入力の true 側のみ観測可能）。
        CustomerIdValue.TryCreate(1, out var cid, out var e1).Should().BeTrue();
        cid!.Value.Should().Be(1);
        e1.Should().BeEmpty();

        OrderIdValue.TryCreate(7, out var oid, out _).Should().BeTrue();
        oid!.Value.Should().Be(7);

        ProfileIdValue.TryCreate(9, out var pid, out _).Should().BeTrue();
        pid!.Value.Should().Be(9);

        IsActiveValue.TryCreate(true, out var active, out _).Should().BeTrue();
        active!.Value.Should().BeTrue();

        MemoValue.TryCreate("ok", out var memo, out _).Should().BeTrue();
        memo!.Value.Should().Be("ok");
    }

    // ===== (4b) 参照型 VO への null 入力（Try パターンの契約） =====

    [Fact(DisplayName = "TryCreate(null): 文字列 VO は throw せず false＋必須エラーを返す")]
    public void 文字列VOのTryCreateはnullでfalse()
    {
        // VO は null を内包しない設計（nullable 列は VO プロパティ自体を null にする）。
        // Try パターンの公開 API として null は NRE でなく false＋エラーで返す。
        NameValue.TryCreate(null!, out var failed, out var errors).Should().BeFalse();
        failed.Should().BeNull();
        errors.Should().ContainSingle().Which.Should().Be("A value is required.");

        MemoValue.TryCreate(null!, out var memo, out var memoErrors).Should().BeFalse();
        memo.Should().BeNull();
        memoErrors.Should().NotBeEmpty();

        BioValue.TryCreate(null!, out var bio, out var bioErrors).Should().BeFalse();
        bio.Should().BeNull();
        bioErrors.Should().NotBeEmpty();
    }

    [Fact(
        DisplayName = "Create(null): 文字列 VO は ValueObjectValidationException（TryCreate false と対称）"
    )]
    public void 文字列VOのCreateはnullでValidationException()
    {
        var act = () => NameValue.Create(null!);

        act.Should()
            .Throw<ValueObjectValidationException>()
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Be("A value is required.");
    }

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

    // ===== (6) Equals / GetHashCode / ToString / DisplayValue / == != =====

    [Fact(DisplayName = "VO の Equals / GetHashCode は値ベース（同値は等しく同一ハッシュ）")]
    public void VOのEqualsとGetHashCode()
    {
        var a = CustomerIdValue.Create(5);
        var b = CustomerIdValue.Create(5);
        var c = CustomerIdValue.Create(6);

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Equals(c).Should().BeFalse();
        a.Equals("5").Should().BeFalse();
    }

    [Fact(DisplayName = "VO の == / != 演算子が値ベースで機能し、null も扱える")]
    public void VOの等値演算子()
    {
        var a = CustomerIdValue.Create(5);
        var b = CustomerIdValue.Create(5);
        var c = CustomerIdValue.Create(6);
        CustomerIdValue? nil = null;

        (a == b).Should().BeTrue();
        (a != c).Should().BeTrue();
        (a == nil).Should().BeFalse();
        (nil == null).Should().BeTrue();
    }

    [Fact(DisplayName = "VO の ToString / DisplayValue は下位値の文字列表現を返す")]
    public void VOのToStringとDisplayValue()
    {
        CustomerIdValue.Create(42).ToString().Should().Be("42");
        NameValue.Create("abc").ToString().Should().Be("abc");

        // DisplayValue は既定で ToString() に等しい
        var vo = NameValue.Create("abc");
        vo.DisplayValue.Should().Be("abc");
        ((IValueObject)vo).DisplayValue.Should().Be("abc");
        ((IValueObject)vo).UnderlyingValue.Should().Be("abc");
    }

    // ===== (7) ValueObjectBooleanBase =====

    [Fact(DisplayName = "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する")]
    public void bool_VOのTrueFalse()
    {
        IsActiveValue.True.Value.Should().BeTrue();
        IsActiveValue.True.IsTrue.Should().BeTrue();
        IsActiveValue.True.IsFalse.Should().BeFalse();

        IsActiveValue.False.Value.Should().BeFalse();
        IsActiveValue.False.IsFalse.Should().BeTrue();
        IsActiveValue.False.IsTrue.Should().BeFalse();
    }

    // ===== (8) ValueObjectStringBase の部分一致 =====

    [Fact(
        DisplayName = "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（string・VO 両オーバーロード）"
    )]
    public void 文字列VOの部分一致()
    {
        var vo = NameValue.Create("HelloWorld");

        vo.Contains("loWo").Should().BeTrue();
        vo.StartsWith("Hello").Should().BeTrue();
        vo.EndsWith("World").Should().BeTrue();
        vo.Contains("xyz").Should().BeFalse();

        // VO オーバーロード
        vo.Contains(NameValue.Create("World")).Should().BeTrue();
        vo.StartsWith(NameValue.Create("Hello")).Should().BeTrue();
        vo.EndsWith(NameValue.Create("World")).Should().BeTrue();

        // 文字列 VO の CompareTo は序数比較
        NameValue.Create("a").CompareTo(NameValue.Create("b")).Should().BeNegative();
    }

    [Fact(
        DisplayName = "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（string・VO 両オーバーロード計 6 本）"
    )]
    public void 文字列VOの部分一致はnullで例外()
    {
        var vo = NameValue.Create("HelloWorld");

        // string オーバーロード 3 本（BCL の string.Contains(null) と同じ契約＝ParamName は "value"）
        vo.Invoking(v => v.Contains((string)null!))
            .Should()
            .Throw<ArgumentNullException>()
            .And.ParamName.Should()
            .Be("value");
        vo.Invoking(v => v.StartsWith((string)null!)).Should().Throw<ArgumentNullException>();
        vo.Invoking(v => v.EndsWith((string)null!)).Should().Throw<ArgumentNullException>();

        // VO オーバーロード 3 本
        vo.Invoking(v => v.Contains((NameValue)null!))
            .Should()
            .Throw<ArgumentNullException>()
            .And.ParamName.Should()
            .Be("value");
        vo.Invoking(v => v.StartsWith((NameValue)null!)).Should().Throw<ArgumentNullException>();
        vo.Invoking(v => v.EndsWith((NameValue)null!)).Should().Throw<ArgumentNullException>();
    }
}
