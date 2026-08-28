using System;
using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生の値（CSV / Excel のセル）から値オブジェクトを起こす <c>TryCreateFrom</c> / <c>CreateFrom</c> の検証。
/// </summary>
/// <remarks>
/// 要点は「下地の型を知らないジェネリックコードから使える」こと。<c>IValueObject&lt;TSelf&gt;</c> は TValue を
/// 型引数に取らないため、取り込みコードは <c>where T : IValueObject&lt;T&gt;</c> だけで全 VO を受けられる。
/// 対象は実際の生成 VO（GeneratedFixture.g.cs）で、テスト側の作り込みではない。
/// </remarks>
public sealed class ValueObjectInputConversionTests
{
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>取り込みコードの想定形。下地の型を名指しせずに任意の VO を作る。</summary>
    private static T? ReadCell<T>(object? cell, IFormatProvider? provider, List<string> errors)
        where T : class, IValueObject<T>
    {
        if (T.TryCreateFrom(cell, provider, out var value, out var messages))
        {
            return value;
        }

        errors.AddRange(messages);

        return null;
    }

    [Fact(DisplayName = "[生値変換] 文字列・数値のどちらのセルからも int の VO を作れる")]
    public void 文字列と数値のセルから数値VOを作れる()
    {
        var errors = new List<string>();

        ReadCell<CustomerIdValue>("42", null, errors)!.Value.Should().Be(42);
        ReadCell<CustomerIdValue>(42, null, errors)!.Value.Should().Be(42);
        ReadCell<CustomerIdValue>(42.0d, null, errors)!.Value.Should().Be(42);

        errors.Should().BeEmpty();
    }

    // 表計算の書式付き数値を GetString() で読むと "1,234" で届く。Convert.ChangeType はこれを弾くため、
    // 数値は NumberStyles を明示して桁区切りを許すのが ConvertInput の役割。
    [Fact(DisplayName = "[生値変換] 桁区切り付きの数値文字列を、指定カルチャで読める")]
    public void 桁区切り付きの数値を読める()
    {
        var errors = new List<string>();

        ReadCell<CustomerIdValue>("1,234", Japanese, errors)!.Value.Should().Be(1234);
        ReadCell<AmountValue>("1,234.5", Japanese, errors)!.Value.Should().Be(1234.5m);

        errors.Should().BeEmpty();
    }

    // 整数は NumberStyles.Integer 系＝小数点を許さない。Number にすると "1,234.5" が int として通ってしまう。
    [Fact(DisplayName = "[生値変換] 整数の VO は小数点付きの文字列を受け付けない")]
    public void 整数VOは小数点を受け付けない()
    {
        var errors = new List<string>();

        ReadCell<CustomerIdValue>("1,234.5", Japanese, errors).Should().BeNull();

        errors.Should().ContainSingle().Which.Should().Contain("1,234.5");
    }

    [Fact(DisplayName = "[生値変換] 日付は指定カルチャの書式で解釈される")]
    public void 日付は指定カルチャで解釈される()
    {
        var errors = new List<string>();

        ReadCell<DeliveryDateValue>("2026/08/28", Japanese, errors)!
            .Value.Should()
            .Be(new DateTime(2026, 8, 28));

        errors.Should().BeEmpty();
    }

    [Fact(DisplayName = "[生値変換] カルチャを渡さなければインバリアントで解釈する")]
    public void カルチャ未指定はインバリアント()
    {
        var errors = new List<string>();

        ReadCell<DeliveryDateValue>("2026-08-28", null, errors)!
            .Value.Should()
            .Be(new DateTime(2026, 8, 28));

        errors.Should().BeEmpty();
    }

    // 空セルは「未入力」であって違反ではない。NULL 許容列はプロパティ自体を null に保つ設計に合わせ、
    // 成功（true）＋ null を返し、エラーは 1 件も積まない。
    [Theory(DisplayName = "[生値変換] 空のセルは成功したうえで null を返し、エラーを積まない")]
    [InlineData(null)]
    [InlineData("")]
    public void 空セルは未入力として扱う(string? cell)
    {
        var errors = new List<string>();

        CustomerIdValue
            .TryCreateFrom(cell, null, out var value, out var messages)
            .Should()
            .BeTrue();

        value.Should().BeNull();
        messages.Should().BeEmpty();
        ReadCell<CustomerIdValue>(cell, null, errors).Should().BeNull();
        errors.Should().BeEmpty();
    }

    [Fact(DisplayName = "[生値変換] DBNull も空のセルとして扱う")]
    public void DBNullも未入力として扱う()
    {
        CustomerIdValue
            .TryCreateFrom(DBNull.Value, null, out var value, out var messages)
            .Should()
            .BeTrue();

        value.Should().BeNull();
        messages.Should().BeEmpty();
    }

    [Fact(DisplayName = "[生値変換] 変換できた値は、その型自身の検証を通常どおり受ける")]
    public void 変換後は通常の検証を受ける()
    {
        var errors = new List<string>();
        var tooLong = new string('a', 51);

        ReadCell<NameValue>(tooLong, null, errors).Should().BeNull();

        errors
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(ValueObjectValidationMessages.MaxLengthExceeded(50, 51));
    }

    [Fact(DisplayName = "[生値変換] 変換できない値は差し替え可能なメッセージで報告される")]
    public void 変換不能はメッセージフックを通る()
    {
        var original = ValueObjectValidationMessages.InputNotConvertible;

        try
        {
            ValueObjectValidationMessages.InputNotConvertible = (raw, displayName) =>
                $"[{displayName}] cannot read '{raw}'.";

            CustomerIdValue
                .TryCreateFrom("abc", null, out var value, out var messages)
                .Should()
                .BeFalse();

            value.Should().BeNull();
            messages
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be($"[{CustomerIdValue.DisplayName}] cannot read 'abc'.");
        }
        finally
        {
            ValueObjectValidationMessages.InputNotConvertible = original;
        }
    }

    [Fact(DisplayName = "[生値変換] CreateFrom は違反で例外・未入力では null を返す")]
    public void CreateFromの例外と未入力()
    {
        CustomerIdValue.CreateFrom("7")!.Value.Should().Be(7);
        CustomerIdValue.CreateFrom(null).Should().BeNull();
        CustomerIdValue.CreateFrom("", Japanese).Should().BeNull();

        var act = () => CustomerIdValue.CreateFrom("abc");

        act.Should()
            .Throw<ValueObjectValidationException>()
            .Which.ValueObjectType.Should()
            .Be(typeof(CustomerIdValue));
    }

    // ジェネリック経路（型引数経由）と、具象型を名指しした直接呼び出しの両方で同じ結果になること。
    [Fact(DisplayName = "[生値変換] カルチャ省略の糖衣オーバーロードが具象型からも使える")]
    public void 糖衣オーバーロードが具象型から使える()
    {
        CustomerIdValue.TryCreateFrom("7", out var value, out var messages).Should().BeTrue();

        value!.Value.Should().Be(7);
        messages.Should().BeEmpty();
    }
}
