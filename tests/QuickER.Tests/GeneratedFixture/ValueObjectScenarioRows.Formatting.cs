using System;
using System.Collections.Generic;
using System.Globalization;
using QuickER.Tests.TestSupport.Engines;
using NoteBlobValue = QuickER.Tests.GeneratedBinaryVoFixture.NoteBlobValue;

namespace QuickER.Tests.GeneratedFixture;

// vo-call の行：基底の IFormattable 実装（書式付き ToString）。書式指定子があるときだけ書式と culture を
// 内包値へ素通しし、書式なし（null・空）の結果は常に仮想 ToString() と同じ。内包値が IFormattable でない型
// （string・bool・byte[]）は書式を無視して ToString() を返す。
internal static partial class ValueObjectScenarioRows
{
    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> FormattingCallRows() =>
        [
            Exact(
                "format-decimal-invariant",
                "decimal VO: 書式と culture が内包値へ素通しされる（インバリアント）",
                () => AmountValue.Create(1234.5m).ToString("N2", CultureInfo.InvariantCulture),
                "\"1,234.50\""
            ),
            // de-DE は桁区切りがピリオド・小数点がカンマ
            Exact(
                "format-decimal-de-de",
                "decimal VO: 書式と culture が内包値へ素通しされる（de-DE）",
                () =>
                    AmountValue.Create(1234.5m).ToString("N2", CultureInfo.GetCultureInfo("de-DE")),
                "\"1.234,50\""
            ),
            Exact(
                "format-datetime-invariant",
                "DateTime VO: カスタム日付書式が内包値へ素通しされる",
                () =>
                    DeliveryDateValue
                        .Create(new DateTime(2026, 9, 9, 14, 30, 0))
                        .ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
                "\"2026/09/09 14:30\""
            ),
            // "0000" はどの culture でも同じ出力＝並列実行中の CurrentCulture に依存しない
            Exact(
                "format-int-format-only-overload",
                "int VO: culture 非依存の書式は書式のみのオーバーロードで足りる",
                () => CustomerIdValue.Create(42).ToString("0000"),
                "\"0042\""
            ),
            // 書式指定子が無ければ culture 引数に依らず仮想 ToString() へ委譲する（元の表明は ToString() との文字列の一致）
            Exact(
                "format-decimal-null-format-matches-tostring-de-de",
                "書式なし（null・空）は常に引数なし ToString() と同じ文字列になる（null・de-DE）",
                () =>
                {
                    var amount = AmountValue.Create(12.3m);

                    return amount.ToString(null, CultureInfo.GetCultureInfo("de-DE"))
                        == amount.ToString();
                },
                "true"
            ),
            Exact(
                "format-decimal-empty-format-matches-tostring-de-de",
                "書式なし（null・空）は常に引数なし ToString() と同じ文字列になる（空・de-DE）",
                () =>
                {
                    var amount = AmountValue.Create(12.3m);

                    return amount.ToString("", CultureInfo.GetCultureInfo("de-DE"))
                        == amount.ToString();
                },
                "true"
            ),
            Exact(
                "format-decimal-null-format-only-matches-tostring",
                "書式なし（null・空）は常に引数なし ToString() と同じ文字列になる（書式のみのオーバーロードに null）",
                () =>
                {
                    var amount = AmountValue.Create(12.3m);

                    return amount.ToString(null) == amount.ToString();
                },
                "true"
            ),
            // 補間・string.Format は IFormattable を優先して ToString(null, provider) を呼ぶ
            // ＝基底が ToString() へ委譲しないと "System.Byte[]" に化ける（回帰の再現形）
            Exact(
                "format-binary-interpolation-base64",
                "バイナリ VO: 書式なしの補間・合成書式は Base64（ToString() オーバーライド）を保つ（補間）",
                () =>
                {
                    var blob = NoteBlobValue.Create([1, 2, 3]);

                    return $"{blob}";
                },
                "\"AQID\""
            ),
            Exact(
                "format-binary-composite-base64",
                "バイナリ VO: 書式なしの補間・合成書式は Base64（ToString() オーバーライド）を保つ（string.Format）",
                () =>
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}",
                        NoteBlobValue.Create([1, 2, 3])
                    ),
                "\"AQID\""
            ),
            // byte[] は IFormattable でないため書式があっても無視され ToString()（Base64）のまま
            Exact(
                "format-binary-format-ignored-base64",
                "バイナリ VO: 書式なしの補間・合成書式は Base64（ToString() オーバーライド）を保つ（書式は無視）",
                () => NoteBlobValue.Create([1, 2, 3]).ToString("x", CultureInfo.InvariantCulture),
                "\"AQID\""
            ),
            // WPF の Binding StringFormat も内部はこの合成書式経路＝ここが通れば StringFormat も効く
            Exact(
                "format-decimal-composite-string-format",
                "合成書式（string.Format / 補間）の書式指定子が IFormattable 経由で効く（string.Format）",
                () =>
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:N2}",
                        AmountValue.Create(1234.5m)
                    ),
                "\"1,234.50\""
            ),
            Exact(
                "format-decimal-composite-invariant-interpolation",
                "合成書式（string.Format / 補間）の書式指定子が IFormattable 経由で効く（FormattableString.Invariant）",
                () =>
                {
                    var amount = AmountValue.Create(1234.5m);

                    return FormattableString.Invariant($"単価: {amount:N0}");
                },
                "\"単価: 1,235\""
            ),
            Exact(
                "format-string-format-ignored",
                "非 IFormattable の内包値（string / bool）は書式を無視して素の文字列を返す（string）",
                () => NameValue.Create("Alice").ToString("N2"),
                "\"Alice\""
            ),
            // 元の期待は true.ToString()（行を作る時点で値オブジェクトの型には触れない）
            Exact(
                "format-bool-format-ignored",
                "非 IFormattable の内包値（string / bool）は書式を無視して素の文字列を返す（bool）",
                () => IsActiveValue.Create(true).ToString("x", CultureInfo.InvariantCulture),
                "\"" + true.ToString() + "\""
            ),
            // 合成書式でも同じ規約（非 IFormattable は書式が無視される）
            Exact(
                "format-string-composite-format-ignored",
                "非 IFormattable の内包値（string / bool）は書式を無視して素の文字列を返す（合成書式）",
                () =>
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:anything}",
                        NameValue.Create("Alice")
                    ),
                "\"Alice\""
            ),
        ];
}
