using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AwesomeAssertions;
using QuickER.Converters;
using QuickER.Tests.TestSupport.Engines;
using AiUiBoolToVisibilityConverter = QuickER.AI.UI.BoolToVisibilityConverter;

namespace QuickER.Tests.Gui.Converters;

/// <summary>コンバーターの表の行の入力</summary>
/// <param name="Converter">流すコンバーター</param>
/// <param name="Value">変換する値</param>
/// <param name="Parameter">コンバーターのパラメーター</param>
/// <param name="Expected">変換の結果（<see cref="ConvertBackThrows"/> の行は使わない）</param>
/// <param name="ConvertBackThrows">逆変換が <see cref="NotSupportedException"/> を投げることを見る行</param>
internal sealed record ConverterCase(
    IValueConverter Converter,
    object? Value,
    object? Parameter,
    object? Expected,
    bool ConvertBackThrows = false
);

/// <summary>XAML の表示の値のコンバーターの表の行</summary>
/// <remarks>
/// <para>
/// 対象は、アプリ本体の件数・null のコンバーター 3 つ（<c>src/QuickER.Gui/Converters</c>）と、AI のダイアログが使う
/// <see cref="AiUiBoolToVisibilityConverter"/>（<c>src/QuickER.AI.UI</c>）。判断は、反転のパラメーター <c>"Inverse"</c>（大文字小文字を無視）・
/// 件数のしきい値（0 より大きい int だけ表示）・null の扱い。
/// </para>
/// <para>
/// アプリ本体の <c>QuickER.Converters.BoolToVisibilityConverter</c> は表に入れない。<c>App.xaml</c> のキー <c>BoolToVisibilityConverter</c> は
/// WPF 組み込みの <c>BooleanToVisibilityConverter</c> を当てていて、アプリ本体のこの型はどこからも使われていない（消す候補）。
/// 使われていないコードを表で守ると、消すときに表まで直すことになる。
/// </para>
/// <para>期待はコンバーターの XmlDoc から手で書く。文書に定めの無い値（null・型の違う値）は説明の先頭に「特性の固定」と書く。</para>
/// </remarks>
internal static class ConverterRows
{
    private static readonly AiUiBoolToVisibilityConverter Bool = new();
    private static readonly CountToVisibilityConverter Count = new();
    private static readonly NullToVisibilityConverter NullToVisibility = new();
    private static readonly NullToBooleanConverter NullToBoolean = new();

    /// <summary>行の表</summary>
    public static ScenarioTable<ConverterCase> Table =>
        new(
            EngineIds.DiagramEdit,
            [.. BoolRows(), .. CountRows(), .. NullRows(), .. ConvertBackRows()]
        );

    /// <summary>真偽 → 表示（AI.UI の写し）</summary>
    private static IEnumerable<ScenarioRow<ConverterCase>> BoolRows() =>
        [
            Row("bool-true-visible", "真は表示", Bool, true, null, Visibility.Visible),
            Row("bool-false-collapsed", "偽は畳む", Bool, false, null, Visibility.Collapsed),
            Row(
                "bool-null-collapsed",
                "特性の固定（文書に定めが無い今の振る舞い）：null は偽として畳む（BoolToVisibilityConverter.cs の value is true）",
                Bool,
                null,
                null,
                Visibility.Collapsed
            ),
            Row(
                "bool-non-bool-collapsed",
                "特性の固定（文書に定めが無い今の振る舞い）：bool でない値（文字列の true）は偽として畳む",
                Bool,
                "true",
                null,
                Visibility.Collapsed
            ),
            Row(
                "bool-inverse-true-collapsed",
                "パラメーター Inverse は反転する（真は畳む）",
                Bool,
                true,
                "Inverse",
                Visibility.Collapsed
            ),
            Row(
                "bool-inverse-false-visible",
                "パラメーター Inverse は反転する（偽は表示）",
                Bool,
                false,
                "Inverse",
                Visibility.Visible
            ),
            Row(
                "bool-inverse-lower-case",
                "パラメーターの Inverse は大文字小文字を無視する（inverse）",
                Bool,
                false,
                "inverse",
                Visibility.Visible
            ),
            Row(
                "bool-inverse-upper-case",
                "パラメーターの Inverse は大文字小文字を無視する（INVERSE）",
                Bool,
                true,
                "INVERSE",
                Visibility.Collapsed
            ),
            Row(
                "bool-inverse-null-visible",
                "特性の固定（文書に定めが無い今の振る舞い）：Inverse のとき null は偽として反転し表示する",
                Bool,
                null,
                "Inverse",
                Visibility.Visible
            ),
            Row(
                "bool-other-parameter-not-inverted",
                "Inverse 以外の文字列のパラメーターは反転しない",
                Bool,
                true,
                "Reverse",
                Visibility.Visible
            ),
            Row(
                "bool-padded-parameter-not-inverted",
                "特性の固定（文書に定めが無い今の振る舞い）：前後に空白のあるパラメーターは Inverse とみなさない",
                Bool,
                true,
                " Inverse",
                Visibility.Visible
            ),
            Row(
                "bool-non-string-parameter-not-inverted",
                "文字列でないパラメーター（bool の true）は反転しない",
                Bool,
                false,
                true,
                Visibility.Collapsed
            ),
        ];

    /// <summary>件数 → 表示（アプリ本体）</summary>
    private static IEnumerable<ScenarioRow<ConverterCase>> CountRows() =>
        [
            Row(
                "count-negative-collapsed",
                "0 以下（-1）は畳む",
                Count,
                -1,
                null,
                Visibility.Collapsed
            ),
            Row("count-zero-collapsed", "0 は畳む", Count, 0, null, Visibility.Collapsed),
            Row("count-one-visible", "1 件以上は表示する", Count, 1, null, Visibility.Visible),
            Row(
                "count-max-visible",
                "大きな件数も表示する",
                Count,
                int.MaxValue,
                null,
                Visibility.Visible
            ),
            Row(
                "count-null-collapsed",
                "特性の固定（文書に定めが無い今の振る舞い）：null は畳む",
                Count,
                null,
                null,
                Visibility.Collapsed
            ),
            Row(
                "count-long-collapsed",
                "int でない件数（long の 1）は畳む（件数は int と定める）",
                Count,
                1L,
                null,
                Visibility.Collapsed
            ),
            Row(
                "count-string-collapsed",
                "int でない件数（文字列の 1）は畳む",
                Count,
                "1",
                null,
                Visibility.Collapsed
            ),
        ];

    /// <summary>null → 表示・真偽（アプリ本体）</summary>
    private static IEnumerable<ScenarioRow<ConverterCase>> NullRows() =>
        [
            Row(
                "null-visibility-null-collapsed",
                "null は畳む",
                NullToVisibility,
                null,
                null,
                Visibility.Collapsed
            ),
            Row(
                "null-visibility-object-visible",
                "null でない値は表示する",
                NullToVisibility,
                new object(),
                null,
                Visibility.Visible
            ),
            Row(
                "null-visibility-empty-string-visible",
                "空文字列も null でないので表示する",
                NullToVisibility,
                string.Empty,
                null,
                Visibility.Visible
            ),
            Row("null-boolean-null-false", "null は false", NullToBoolean, null, null, false),
            Row(
                "null-boolean-object-true",
                "null でない値は true",
                NullToBoolean,
                new object(),
                null,
                true
            ),
            Row(
                "null-boolean-false-value-true",
                "偽の bool も null でないので true（値の真偽は見ない）",
                NullToBoolean,
                false,
                null,
                true
            ),
        ];

    /// <summary>逆変換はどれも非対応</summary>
    private static IEnumerable<ScenarioRow<ConverterCase>> ConvertBackRows() =>
        new (string Id, IValueConverter Converter)[]
        {
            ("bool", Bool),
            ("count", Count),
            ("null-visibility", NullToVisibility),
            ("null-boolean", NullToBoolean),
        }.Select(c => new ScenarioRow<ConverterCase>(
            $"{c.Id}-convert-back-not-supported",
            null,
            "逆変換は非対応で NotSupportedException を投げる",
            new ConverterCase(c.Converter, Visibility.Visible, null, null, ConvertBackThrows: true)
        ));

    /// <summary>行を流して表明する</summary>
    public static void Check(ConverterCase input)
    {
        if (input.ConvertBackThrows)
        {
            var back = () =>
                input.Converter.ConvertBack(
                    input.Value,
                    typeof(object),
                    input.Parameter,
                    CultureInfo.InvariantCulture
                );
            back.Should().Throw<NotSupportedException>();
            return;
        }

        input
            .Converter.Convert(
                input.Value,
                typeof(object),
                input.Parameter,
                CultureInfo.InvariantCulture
            )
            .Should()
            .Be(input.Expected);
    }

    private static ScenarioRow<ConverterCase> Row(
        string id,
        string description,
        IValueConverter converter,
        object? value,
        object? parameter,
        object expected
    ) => new(id, null, description, new ConverterCase(converter, value, parameter, expected));
}
