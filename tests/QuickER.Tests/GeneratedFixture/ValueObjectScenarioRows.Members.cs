using System.Collections.Generic;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedFixture;

// vo-call の行：基底のメンバー（ToString / DisplayValue・bool 基底の True/False・文字列基底の部分一致・順序付き基底の < と >・
// DateTime 基底の Today / Now）。
internal static partial class ValueObjectScenarioRows
{
    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> MemberCallRows() =>
        [
            // ===== ToString / DisplayValue（DisplayValue は既定で ToString() に等しい） =====
            Exact(
                "tostring-int-customer-id",
                "VO の ToString / DisplayValue は下位値の文字列表現を返す（int の ToString）",
                () => CustomerIdValue.Create(42).ToString(),
                "\"42\""
            ),
            Exact(
                "tostring-string-name",
                "VO の ToString / DisplayValue は下位値の文字列表現を返す（string の ToString）",
                () => NameValue.Create("abc").ToString(),
                "\"abc\""
            ),
            Exact(
                "displayvalue-string-name",
                "VO の ToString / DisplayValue は下位値の文字列表現を返す（DisplayValue）",
                () => NameValue.Create("abc").DisplayValue,
                "\"abc\""
            ),
            Exact(
                "displayvalue-interface-string-name",
                "VO の ToString / DisplayValue は下位値の文字列表現を返す（IValueObject.DisplayValue）",
                () => ((IValueObject)NameValue.Create("abc")).DisplayValue,
                "\"abc\""
            ),
            Exact(
                "underlyingvalue-interface-string-name",
                "VO の ToString / DisplayValue は下位値の文字列表現を返す（IValueObject.UnderlyingValue）",
                () => ((IValueObject)NameValue.Create("abc")).UnderlyingValue,
                "\"abc\""
            ),
            // ===== bool 基底の True/False ファクトリと IsTrue/IsFalse =====
            Exact(
                "bool-factory-true-value",
                "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する（True.Value）",
                () => IsActiveValue.True.Value,
                "true"
            ),
            Exact(
                "bool-factory-true-istrue",
                "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する（True.IsTrue）",
                () => IsActiveValue.True.IsTrue,
                "true"
            ),
            Exact(
                "bool-factory-true-isfalse",
                "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する（True.IsFalse）",
                () => IsActiveValue.True.IsFalse,
                "false"
            ),
            Exact(
                "bool-factory-false-value",
                "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する（False.Value）",
                () => IsActiveValue.False.Value,
                "false"
            ),
            Exact(
                "bool-factory-false-isfalse",
                "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する（False.IsFalse）",
                () => IsActiveValue.False.IsFalse,
                "true"
            ),
            Exact(
                "bool-factory-false-istrue",
                "bool VO の True/False ファクトリと IsTrue/IsFalse が対応する（False.IsTrue）",
                () => IsActiveValue.False.IsTrue,
                "false"
            ),
            // ===== 文字列基底の部分一致（序数比較・string・VO 両オーバーロード） =====
            Exact(
                "contains-string-found",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（Contains・string）",
                () => NameValue.Create("HelloWorld").Contains("loWo"),
                "true"
            ),
            Exact(
                "startswith-string-found",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（StartsWith・string）",
                () => NameValue.Create("HelloWorld").StartsWith("Hello"),
                "true"
            ),
            Exact(
                "endswith-string-found",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（EndsWith・string）",
                () => NameValue.Create("HelloWorld").EndsWith("World"),
                "true"
            ),
            Exact(
                "contains-string-absent",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（Contains・含まない string）",
                () => NameValue.Create("HelloWorld").Contains("xyz"),
                "false"
            ),
            Exact(
                "contains-vo-found",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（Contains・VO）",
                () => NameValue.Create("HelloWorld").Contains(NameValue.Create("World")),
                "true"
            ),
            Exact(
                "startswith-vo-found",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（StartsWith・VO）",
                () => NameValue.Create("HelloWorld").StartsWith(NameValue.Create("Hello")),
                "true"
            ),
            Exact(
                "endswith-vo-found",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（EndsWith・VO）",
                () => NameValue.Create("HelloWorld").EndsWith(NameValue.Create("World")),
                "true"
            ),
            // ===== 順序付き基底の < と > は等しい値で false（<= / >= と分かれる） =====
            Exact(
                "ordered-int-less-than-same-value",
                "順序付き VO の < は等しい値どうしで false",
                () => CustomerIdValue.Create(2) < CustomerIdValue.Create(2),
                "false"
            ),
            Exact(
                "ordered-int-greater-than-same-value",
                "順序付き VO の > は等しい値どうしで false",
                () => CustomerIdValue.Create(2) > CustomerIdValue.Create(2),
                "false"
            ),
            // ===== DateTime 基底の Today / Now（Today は 0 時、どちらも現地時刻） =====
            Exact(
                "datetime-factory-today",
                "DateTime VO の Today は時刻が 0 時の現地時刻",
                () =>
                {
                    var today = DeliveryDateValue.Today.Value;

                    return $"time={today.TimeOfDay} kind={today.Kind}";
                },
                "\"time=00:00:00 kind=Local\""
            ),
            Exact(
                "datetime-factory-now",
                "DateTime VO の Now は現地時刻",
                () => $"kind={DeliveryDateValue.Now.Value.Kind}",
                "\"kind=Local\""
            ),
            // 元の表明は BeNegative＝符号が負であること
            Exact(
                "compareto-string-ordinal-negative",
                "文字列 VO の Contains/StartsWith/EndsWith が序数比較で機能する（文字列 VO の CompareTo は序数比較）",
                () => NameValue.Create("a").CompareTo(NameValue.Create("b")) < 0,
                "true"
            ),
            // ===== 部分一致の null 引数（BCL の string.Contains(null) と同じ契約＝ParamName は "value"） =====
            Like(
                "contains-string-null-argument",
                "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（Contains・string）",
                () => NameValue.Create("HelloWorld").Contains((string)null!),
                "throws ArgumentNullException: * param=value"
            ),
            Like(
                "startswith-string-null-argument",
                "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（StartsWith・string）",
                () => NameValue.Create("HelloWorld").StartsWith((string)null!),
                "throws ArgumentNullException: *"
            ),
            Like(
                "endswith-string-null-argument",
                "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（EndsWith・string）",
                () => NameValue.Create("HelloWorld").EndsWith((string)null!),
                "throws ArgumentNullException: *"
            ),
            Like(
                "contains-vo-null-argument",
                "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（Contains・VO）",
                () => NameValue.Create("HelloWorld").Contains((NameValue)null!),
                "throws ArgumentNullException: * param=value"
            ),
            Like(
                "startswith-vo-null-argument",
                "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（StartsWith・VO）",
                () => NameValue.Create("HelloWorld").StartsWith((NameValue)null!),
                "throws ArgumentNullException: *"
            ),
            Like(
                "endswith-vo-null-argument",
                "文字列 VO の Contains/StartsWith/EndsWith は null 引数で ArgumentNullException（EndsWith・VO）",
                () => NameValue.Create("HelloWorld").EndsWith((NameValue)null!),
                "throws ArgumentNullException: *"
            ),
        ];
}
