using System;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 値オブジェクト基底の <c>IFormattable</c> 実装（書式付き <c>ToString</c>）を検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 基底 <c>ValueObjectBase&lt;TSelf, TValue&gt;</c> が <c>IFormattable</c> を 1 回だけ実装し、書式と culture を
/// 内包値へ素通しする。これにより書式付きの直接呼び出しだけでなく、文字列補間の書式指定子・
/// <c>string.Format</c>・WPF バインディングの StringFormat（内部は合成書式＝<c>IFormattable</c> 経由）が
/// 内包値に効く。
/// </para>
/// <para>
/// 内包値が <c>IFormattable</c> でない型（string・bool・byte[]）は書式を無視して素の文字列表現を返す
/// ＝合成書式（<c>string.Format</c>）が非 <c>IFormattable</c> 値に対して行うのと同じ規約。
/// </para>
/// </remarks>
public sealed class ValueObjectFormattableTests
{
    [Fact(DisplayName = "decimal VO: 書式と culture が内包値へ素通しされる")]
    public void DecimalValueObject_FormatsUnderlyingValue()
    {
        var amount = AmountValue.Create(1234.5m);

        amount.ToString("N2", CultureInfo.InvariantCulture).Should().Be("1,234.50");
        amount
            .ToString("N2", CultureInfo.GetCultureInfo("de-DE"))
            .Should()
            .Be("1.234,50", "de-DE は桁区切りがピリオド・小数点がカンマ");
    }

    [Fact(DisplayName = "DateTime VO: カスタム日付書式が内包値へ素通しされる")]
    public void DateTimeValueObject_FormatsUnderlyingValue()
    {
        var delivered = DeliveryDateValue.Create(new DateTime(2026, 9, 9, 14, 30, 0));

        delivered
            .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)
            .Should()
            .Be("2026/09/09 14:30");
    }

    [Fact(DisplayName = "int VO: culture 非依存の書式は書式のみのオーバーロードで足りる")]
    public void IntValueObject_FormatOnlyOverload()
    {
        // "0000" はどの culture でも同じ出力＝並列実行中の CurrentCulture に依存しない
        CustomerIdValue.Create(42).ToString("0000").Should().Be("0042");
    }

    [Fact(DisplayName = "null 書式は既定書式＝引数なし ToString() と同じ文字列になる")]
    public void NullFormat_MatchesDefaultToString()
    {
        var amount = AmountValue.Create(12.3m);

        amount
            .ToString(null, CultureInfo.InvariantCulture)
            .Should()
            .Be(amount.Value.ToString(CultureInfo.InvariantCulture));
    }

    [Fact(DisplayName = "合成書式（string.Format / 補間）の書式指定子が IFormattable 経由で効く")]
    public void CompositeFormatting_ReachesUnderlyingValue()
    {
        var amount = AmountValue.Create(1234.5m);

        // WPF の Binding StringFormat も内部はこの合成書式経路＝ここが通れば StringFormat も効く
        string.Format(CultureInfo.InvariantCulture, "{0:N2}", amount).Should().Be("1,234.50");
        FormattableString.Invariant($"単価: {amount:N0}").Should().Be("単価: 1,235");
    }

    [Fact(
        DisplayName = "非 IFormattable の内包値（string / bool）は書式を無視して素の文字列を返す"
    )]
    public void NonFormattableUnderlyingValue_IgnoresFormat()
    {
        var name = NameValue.Create("Alice");
        var active = IsActiveValue.Create(true);

        name.ToString("N2").Should().Be("Alice");
        active.ToString("x", CultureInfo.InvariantCulture).Should().Be(true.ToString());

        // 合成書式でも同じ規約（非 IFormattable は書式が無視される）
        string.Format(CultureInfo.InvariantCulture, "{0:anything}", name).Should().Be("Alice");
    }
}
