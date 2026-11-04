using System;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

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
/// <para>
/// 書式の決まった形の表明は <see cref="ValueObjectScenarioTests"/> の表にある。ここに残すのは、
/// <c>ToString()</c> をオーバーライドした型の書式なし・書式ありの全経路を 1 つの型で続けて見るテストだけ。
/// </para>
/// </remarks>
public sealed class ValueObjectFormattableTests
{
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
}
