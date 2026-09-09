using System;
using System.Globalization;
using AwesomeAssertions;
using Xunit;
using NoteBlobValue = QuickER.Tests.GeneratedBinaryVoFixture.NoteBlobValue;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 内包値が int で <c>ToString()</c> をオーバーライドする手書き VO（利用者 partial が
/// <c>ToString()</c> を差し替えるケースの代役）。書式なしの補間・合成書式がこのオーバーライドを
/// 尊重し続けることの検証用。
/// </summary>
public sealed class FormattableModeValue
    : ValueObjectBase<FormattableModeValue, int>,
        IValueObject<FormattableModeValue, int>
{
    private FormattableModeValue(int value)
        : base(value) { }

    static FormattableModeValue IValueObject<FormattableModeValue, int>.New(int value) =>
        new(value);

    /// <summary>内包値でなく名前を返すオーバーライド（1=Web / それ以外=Desktop）。</summary>
    public override string ToString() => Value == 1 ? "Web" : "Desktop";
}

/// <summary>
/// 値オブジェクト基底の <c>IFormattable</c> 実装（書式付き <c>ToString</c>）を検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 基底 <c>ValueObjectBase&lt;TSelf, TValue&gt;</c> が <c>IFormattable</c> を 1 回だけ実装し、
/// <b>書式指定子があるときだけ</b>書式と culture を内包値へ素通しする。これにより書式付きの直接呼び出しに加え、
/// 文字列補間の書式指定子・<c>string.Format</c>・WPF バインディングの StringFormat（内部は合成書式＝
/// <c>IFormattable</c> 経由）が内包値に効く。
/// </para>
/// <para>
/// 書式指定子が無い（null・空）場合の結果は常に仮想 <c>ToString()</c> と同じ＝<c>ToString()</c> の
/// オーバーライド（バイナリ VO の Base64 形・利用者 partial の差し替え）が書式なしの補間・合成書式にも
/// そのまま効く。補間・<c>string.Format</c> は実行時に <c>IFormattable</c> を優先するため、この委譲が無いと
/// <c>$"{vo}"</c> と <c>vo.ToString()</c> が食い違う。内包値が <c>IFormattable</c> でない型
/// （string・bool・byte[]）も同様に書式を無視して <c>ToString()</c> を返す。
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

    [Fact(DisplayName = "書式なし（null・空）は常に引数なし ToString() と同じ文字列になる")]
    public void NullOrEmptyFormat_MatchesDefaultToString()
    {
        var amount = AmountValue.Create(12.3m);

        // 書式指定子が無ければ culture 引数に依らず仮想 ToString() へ委譲する（新経路が開くのは書式があるときだけ）
        amount.ToString(null, CultureInfo.GetCultureInfo("de-DE")).Should().Be(amount.ToString());
        amount.ToString("", CultureInfo.GetCultureInfo("de-DE")).Should().Be(amount.ToString());
        amount.ToString(null).Should().Be(amount.ToString());
    }

    [Fact(
        DisplayName = "バイナリ VO: 書式なしの補間・合成書式は Base64（ToString() オーバーライド）を保つ"
    )]
    public void BinaryValueObject_UnformattedRenderingKeepsBase64()
    {
        var blob = NoteBlobValue.Create([1, 2, 3]);

        // 補間・string.Format は IFormattable を優先して ToString(null, provider) を呼ぶ
        // ＝基底が ToString() へ委譲しないと "System.Byte[]" に化ける（回帰の再現形）
        $"{blob}".Should().Be("AQID");
        string.Format(CultureInfo.InvariantCulture, "{0}", blob).Should().Be("AQID");

        // byte[] は IFormattable でないため書式があっても無視され ToString()（Base64）のまま
        blob.ToString("x", CultureInfo.InvariantCulture).Should().Be("AQID");
    }

    [Fact(
        DisplayName = "ToString() をオーバーライドした VO: 書式なしはオーバーライド・書式ありは内包値が勝つ"
    )]
    public void ToStringOverride_GovernsUnformattedRendering()
    {
        var mode = FormattableModeValue.Create(1);

        // 書式なしの全経路がオーバーライド（"Web"）を返す＝ToString() と補間が食い違わない
        mode.ToString().Should().Be("Web");
        $"{mode}".Should().Be("Web");
        string.Format(CultureInfo.InvariantCulture, "{0}", mode).Should().Be("Web");
        mode.ToString(null, CultureInfo.InvariantCulture).Should().Be("Web");

        // 書式指定子があるときだけ内包値（int）の書式化が効く
        mode.ToString("D3", CultureInfo.InvariantCulture).Should().Be("001");
        $"{mode:D3}".Should().Be("001");
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
