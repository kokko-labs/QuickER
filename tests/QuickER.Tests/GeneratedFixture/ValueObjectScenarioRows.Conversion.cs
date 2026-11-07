using System;
using System.Collections.Generic;
using System.Globalization;
using QuickER.Tests.TestSupport.Engines;
using NoteBlobValue = QuickER.Tests.GeneratedBinaryVoFixture.NoteBlobValue;

namespace QuickER.Tests.GeneratedFixture;

// vo-call の行：生の値（CSV / Excel のセル）から VO を起こす TryCreateFrom / CreateFrom / ReadCell。
// 要点は「下地の型を知らないジェネリックコードから使える」こと（ReadCell は where T : IValueObject<T> だけで全 VO を受ける）。
internal static partial class ValueObjectScenarioRows
{
    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> ConversionCallRows() =>
        [
            // ===== 数値・日付の変換（指定カルチャ・インバリアント） =====
            Exact(
                "readcell-int-from-string-cell",
                "[生値変換] 文字列・数値のどちらのセルからも int の VO を作れる（文字列 \"42\"）",
                () => ReadCellOutcome<CustomerIdValue>("42", null),
                "value=CustomerIdValue(42) errors=[]"
            ),
            Exact(
                "readcell-int-from-int-cell",
                "[生値変換] 文字列・数値のどちらのセルからも int の VO を作れる（int 42）",
                () => ReadCellOutcome<CustomerIdValue>(42, null),
                "value=CustomerIdValue(42) errors=[]"
            ),
            Exact(
                "readcell-int-from-double-cell",
                "[生値変換] 文字列・数値のどちらのセルからも int の VO を作れる（double 42.0）",
                () => ReadCellOutcome<CustomerIdValue>(42.0d, null),
                "value=CustomerIdValue(42) errors=[]"
            ),
            // 表計算の書式付き数値を GetString() で読むと "1,234" で届く。Convert.ChangeType はこれを弾くため、
            // 数値は NumberStyles を明示して桁区切りを許すのが ConvertInput の役割
            Exact(
                "readcell-int-thousands-separator-ja",
                "[生値変換] 桁区切り付きの数値文字列を、指定カルチャで読める（int）",
                () => ReadCellOutcome<CustomerIdValue>("1,234", Japanese),
                "value=CustomerIdValue(1234) errors=[]"
            ),
            Exact(
                "readcell-decimal-thousands-separator-ja",
                "[生値変換] 桁区切り付きの数値文字列を、指定カルチャで読める（decimal）",
                () => ReadCellOutcome<AmountValue>("1,234.5", Japanese),
                "value=AmountValue(1234.5) errors=[]"
            ),
            // 整数は NumberStyles.Integer 系＝小数点を許さない。Number にすると "1,234.5" が int として通ってしまう
            Like(
                "readcell-int-rejects-decimal-point-ja",
                "[生値変換] 整数の VO は小数点付きの文字列を受け付けない（エラーは入力の文字列を含む 1 件）",
                () => ReadCellOutcome<CustomerIdValue>("1,234.5", Japanese),
                "value=null errors(1)=[*1,234.5*]"
            ),
            Exact(
                "readcell-date-culture-ja",
                "[生値変換] 日付は指定カルチャの書式で解釈される",
                () => ReadCellOutcome<DeliveryDateValue>("2026/08/28", Japanese),
                "value=DeliveryDateValue(2026-08-28T00:00:00.0000000) errors=[]"
            ),
            // インバリアントでは読めない書式（日が先）で、渡したカルチャが日付の変換まで届くことを確かめる
            Exact(
                "readcell-date-culture-day-first-en-gb",
                "[生値変換] 日付は指定カルチャの書式で解釈される（日が先の en-GB）",
                () =>
                    ReadCellOutcome<DeliveryDateValue>(
                        "28/08/2026",
                        CultureInfo.GetCultureInfo("en-GB")
                    ),
                "value=DeliveryDateValue(2026-08-28T00:00:00.0000000) errors=[]"
            ),
            Exact(
                "readcell-date-invariant-without-provider",
                "[生値変換] カルチャを渡さなければインバリアントで解釈する",
                () => ReadCellOutcome<DeliveryDateValue>("2026-08-28", null),
                "value=DeliveryDateValue(2026-08-28T00:00:00.0000000) errors=[]"
            ),
            // 数でない文字列は decimal の VO でも 0 などへ倒さず、変換不能のエラーにする
            Like(
                "readcell-decimal-not-convertible",
                "[生値変換] decimal の VO は数でない文字列を変換不能のエラーにする",
                () => ReadCellOutcome<AmountValue>("abc", null),
                "value=null errors(1)=[*]"
            ),
            // ===== 空のセル（未入力であって違反ではない＝成功＋null・エラーなし） =====
            Exact(
                "trycreatefrom-blank-null-cell-int",
                "[生値変換] 空のセルは成功したうえで null を返し、エラーを積まない（null・TryCreateFrom 4 引数）",
                () =>
                {
                    var ok = CustomerIdValue.TryCreateFrom(
                        null,
                        null,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors=[]"
            ),
            Exact(
                "readcell-blank-null-cell-int",
                "[生値変換] 空のセルは成功したうえで null を返し、エラーを積まない（null・ReadCell）",
                () => ReadCellOutcome<CustomerIdValue>(null, null),
                "value=null errors=[]"
            ),
            Exact(
                "trycreatefrom-blank-empty-cell-int",
                "[生値変換] 空のセルは成功したうえで null を返し、エラーを積まない（空文字・TryCreateFrom 4 引数）",
                () =>
                {
                    var ok = CustomerIdValue.TryCreateFrom("", null, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors=[]"
            ),
            Exact(
                "readcell-blank-empty-cell-int",
                "[生値変換] 空のセルは成功したうえで null を返し、エラーを積まない（空文字・ReadCell）",
                () => ReadCellOutcome<CustomerIdValue>("", null),
                "value=null errors=[]"
            ),
            Exact(
                "trycreatefrom-blank-dbnull-cell-int",
                "[生値変換] DBNull も空のセルとして扱う",
                () =>
                {
                    var ok = CustomerIdValue.TryCreateFrom(
                        DBNull.Value,
                        null,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors=[]"
            ),
            // ===== 変換後の検証・CreateFrom・糖衣オーバーロード =====
            ExactFrom(
                "readcell-string-validates-after-conversion-name",
                "[生値変換] 変換できた値は、その型自身の検証を通常どおり受ける",
                () => ReadCellOutcome<NameValue>(new string('a', 51), null),
                () =>
                    "value=null errors(1)=["
                    + ValueObjectValidationMessages.MaxLengthExceeded(NameValue.DisplayName, 50, 51)
                    + "]"
            ),
            Exact(
                "createfrom-int-from-string",
                "[生値変換] CreateFrom は違反で例外・未入力では null を返す（変換できる値）",
                () => CustomerIdValue.CreateFrom("7"),
                "CustomerIdValue(7)"
            ),
            Exact(
                "createfrom-int-blank-null",
                "[生値変換] CreateFrom は違反で例外・未入力では null を返す（null）",
                () => CustomerIdValue.CreateFrom(null),
                "null"
            ),
            Exact(
                "createfrom-int-blank-empty-ja",
                "[生値変換] CreateFrom は違反で例外・未入力では null を返す（空文字・ja-JP）",
                () => CustomerIdValue.CreateFrom("", Japanese),
                "null"
            ),
            Like(
                "createfrom-int-not-convertible-throws",
                "[生値変換] CreateFrom は違反で例外・未入力では null を返す（変換できない値は例外）",
                () => CustomerIdValue.CreateFrom("abc"),
                "throws ValueObjectValidationException: * type=QuickER.Tests.GeneratedFixture.CustomerIdValue errors*"
            ),
            // ジェネリック経路（型引数経由）と、具象型を名指しした直接呼び出しの両方で同じ結果になること
            Exact(
                "trycreatefrom-three-args-int-concrete-type",
                "[生値変換] カルチャ省略の糖衣オーバーロードが具象型からも使える",
                () =>
                {
                    var ok = CustomerIdValue.TryCreateFrom("7", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=CustomerIdValue(7) errors=[]"
            ),
            // ===== フックを書かない型の空欄（既定は「成功＋null・エラーなし」のまま） =====
            // 生成 VO（ブリッジはあるが partial 未実装＝呼び出しごと消える）
            Exact(
                "trycreatefrom-blank-empty-three-args-int",
                "[空欄フック] フックを書かない型の空欄入力は従来どおり成功＋null（生成 VO・空文字・3 引数）",
                () =>
                {
                    var ok = CustomerIdValue.TryCreateFrom("", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors=[]"
            ),
            Like(
                "trycreatefrom-blank-null-four-args-int",
                "[空欄フック] フックを書かない型の空欄入力は従来どおり成功＋null（生成 VO・null・4 引数）",
                () =>
                {
                    var ok = CustomerIdValue.TryCreateFrom(
                        null,
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors*"
            ),
            Exact(
                "createfrom-blank-dbnull-int",
                "[空欄フック] フックを書かない型の空欄入力は従来どおり成功＋null（生成 VO・DBNull・CreateFrom）",
                () => CustomerIdValue.CreateFrom(DBNull.Value),
                "null"
            ),
            // 手書き VO（interface の既定実装が効く＝実装なしで挙動不変）
            Exact(
                "trycreatefrom-blank-empty-three-args-handwritten",
                "[空欄フック] フックを書かない型の空欄入力は従来どおり成功＋null（手書き VO・空文字・3 引数）",
                () =>
                {
                    var ok = DataAccessMode.TryCreateFrom("", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors=[]"
            ),
            // ===== バイナリ VO の Base64（取り込み経路の文字列は EditModel の入力と同じ記法） =====
            Exact(
                "trycreatefrom-binary-base64",
                "バイナリ値オブジェクトは Base64 文字列から TryCreateFrom で生成できる",
                () =>
                {
                    var ok = NoteBlobValue.TryCreateFrom("AQID", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=NoteBlobValue(0x010203) errors=[]"
            ),
            Exact(
                "createfrom-binary-base64",
                "バイナリ値オブジェクトは Base64 文字列から CreateFrom で生成できる",
                () => NoteBlobValue.CreateFrom("AQID"),
                "NoteBlobValue(0x010203)"
            ),
            // 変換不能は例外でなく TryCreateFrom の失敗として返る
            Like(
                "trycreatefrom-binary-not-base64-rejected",
                "Base64 でない文字列は変換不能の検証エラーになる",
                () =>
                {
                    var ok = NoteBlobValue.TryCreateFrom(
                        "@@not-base64@@",
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[*]"
            ),
            Like(
                "trycreatefrom-binary-byte-array-passes-through",
                "バイト配列の入力は従来どおり素通しで生成される",
                () =>
                {
                    var ok = NoteBlobValue.TryCreateFrom(
                        new byte[] { 0x0A, 0x0B },
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=NoteBlobValue(0x0A0B) errors*"
            ),
            // 空欄は Base64 変換より先に空欄早期リターンが引き取る
            Exact(
                "trycreatefrom-binary-blank-empty",
                "空文字は従来どおり空欄として成功し null を返す",
                () =>
                {
                    var ok = NoteBlobValue.TryCreateFrom(
                        string.Empty,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=null errors=[]"
            ),
        ];
}
