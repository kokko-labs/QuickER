using System.Collections.Generic;
using QuickER.Tests.TestSupport.Engines;
using NoteBlobValue = QuickER.Tests.GeneratedBinaryVoFixture.NoteBlobValue;

namespace QuickER.Tests.GeneratedFixture;

// vo-call の行：Create / TryCreate / Validate（生成 VO の組み込み検証と、手書き VO の 3 メンバのレシピ）。
// この図の decimal VO（AmountValue / BalanceValue）は precision=10・scale=2＝整数部の最大桁は 8。
// 文字列 VO（NameValue / BioValue / MemoValue）は最大長 50。int/bool VO は組み込み検証を持たない。
internal static partial class ValueObjectScenarioRows
{
    private const string MaxLength50Exceeded =
        "Enter at most 50 characters. (currently 51 characters)";

    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> CreationCallRows() =>
        [
            // ===== 文字列 VO: 最大長超過は throw =====
            Exact(
                "create-string-max-length-exceeded-bio",
                "文字列 VO は最大長 50 超過で ValueObjectValidationException を投げ、メッセージに最大長文言が乗る",
                () => BioValue.Create(new string('a', 51)),
                "throws ValueObjectValidationException: The value of BioValue is invalid: "
                    + MaxLength50Exceeded
                    + " type=QuickER.Tests.GeneratedFixture.BioValue errors(1)=["
                    + MaxLength50Exceeded
                    + "]"
            ),
            Like(
                "create-string-max-length-errors-name",
                "全ての文字列 VO（Name/Bio/Memo）が最大長 50 超過で throw する（Name）",
                () => NameValue.Create(new string('x', 51)),
                "throws ValueObjectValidationException: * type=* errors(1)=["
                    + MaxLength50Exceeded
                    + "]"
            ),
            Like(
                "create-string-max-length-errors-bio",
                "全ての文字列 VO（Name/Bio/Memo）が最大長 50 超過で throw する（Bio）",
                () => BioValue.Create(new string('x', 51)),
                "throws ValueObjectValidationException: * type=* errors(1)=["
                    + MaxLength50Exceeded
                    + "]"
            ),
            Like(
                "create-string-max-length-errors-memo",
                "全ての文字列 VO（Name/Bio/Memo）が最大長 50 超過で throw する（Memo）",
                () => MemoValue.Create(new string('x', 51)),
                "throws ValueObjectValidationException: * type=* errors(1)=["
                    + MaxLength50Exceeded
                    + "]"
            ),
            Exact(
                "create-string-max-length-boundary-name",
                "文字列 VO の境界値（ちょうど 50 文字）は通る",
                () => NameValue.Create(new string('a', 50)),
                "NameValue(\"" + new string('a', 50) + "\")"
            ),
            Like(
                "create-string-null-name",
                "Create(null): 文字列 VO は ValueObjectValidationException（TryCreate false と対称）",
                () => NameValue.Create(null!),
                "throws ValueObjectValidationException: * type=* errors(1)=[A value is required.]"
            ),
            // ===== decimal VO: precision/scale 違反は丸めず弾く =====
            Like(
                "create-decimal-scale-exceeded-amount",
                "decimal VO は小数部が scale(2) を超えると丸めず ScaleExceeded で throw する（1.234m は小数第 3 位まで）",
                () => AmountValue.Create(1.234m),
                "throws ValueObjectValidationException: * type=* errors(1)=[Enter at most 2 digits after the decimal point.]"
            ),
            Like(
                "create-decimal-precision-exceeded-balance",
                "decimal VO は整数部が precision-scale(8) 桁を超えると PrecisionExceeded で throw する（123456789m は整数部 9 桁）",
                () => BalanceValue.Create(123456789m),
                "throws ValueObjectValidationException: * type=* errors(1)=[Enter at most 8 digits in the integer part.]"
            ),
            // 元の表明は decimal の等値（スケールを見ない）＝写しも末尾のゼロを落とした形で比べる
            Exact(
                "create-decimal-boundary-amount",
                "decimal VO の境界値（整数部 8 桁・小数 2 桁）は通り、末尾ゼロも保持される",
                () => AmountValue.Create(12345678.99m),
                "AmountValue(12345678.99)"
            ),
            Like(
                "create-decimal-multiple-violations-amount",
                "decimal VO は scale 超過と precision 超過が同時に起きると 2 件のエラーを積む（整数部 9 桁・小数 3 桁）",
                () => AmountValue.Create(123456789.123m),
                "throws ValueObjectValidationException: * type=* errors(2)=[*]"
            ),
            Like(
                "create-decimal-declared-scale-exceeded-amount",
                "decimal VO の桁数判定は宣言スケール基準（末尾ゼロを含む）＝decimal.Scale と GetBits は同義（1.230m は弾かれる）",
                () => AmountValue.Create(1.230m),
                "throws ValueObjectValidationException: * type=* errors(1)=[Enter at most 2 digits after the decimal point.]"
            ),
            // 元の表明は decimal の等値（スケールを見ない）＝写しも末尾のゼロを落とした形で比べる
            Exact(
                "create-decimal-declared-scale-boundary-amount",
                "decimal VO の桁数判定は宣言スケール基準（末尾ゼロを含む）＝decimal.Scale と GetBits は同義（1.20m は通る）",
                () => AmountValue.Create(1.20m),
                "AmountValue(1.2)"
            ),
            // ===== TryCreate の true/false 両側 =====
            Like(
                "trycreate-string-max-length-exceeded-name",
                "TryCreate: 文字列 VO は違反で false・空エラーなし、正常で true・エラー空（違反の側）",
                () =>
                {
                    var ok = NameValue.TryCreate(
                        new string('a', 51),
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(*)=[*]"
            ),
            Exact(
                "trycreate-string-valid-name",
                "TryCreate: 文字列 VO は違反で false・空エラーなし、正常で true・エラー空（正常の側）",
                () =>
                {
                    var ok = NameValue.TryCreate("valid", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=NameValue(\"valid\") errors=[]"
            ),
            Like(
                "trycreate-decimal-scale-exceeded-amount",
                "TryCreate: decimal VO は違反で false、正常で true（違反の側）",
                () =>
                {
                    var ok = AmountValue.TryCreate(1.234m, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(*)=[*]"
            ),
            Exact(
                "trycreate-decimal-valid-amount",
                "TryCreate: decimal VO は違反で false、正常で true（正常の側）",
                () =>
                {
                    var ok = AmountValue.TryCreate(12.34m, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=AmountValue(12.34) errors=[]"
            ),
            // 組み込み検証を持たない VO は OnValidate 未実装＝false 経路が構造的に存在しない（true 側のみ観測可能）
            Exact(
                "trycreate-without-rules-customer-id",
                "TryCreate: 組み込み検証を持たない int/bool VO は正常入力で常に true・エラー空（CustomerIdValue）",
                () =>
                {
                    var ok = CustomerIdValue.TryCreate(1, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=CustomerIdValue(1) errors=[]"
            ),
            Like(
                "trycreate-without-rules-order-id",
                "TryCreate: 組み込み検証を持たない int/bool VO は正常入力で常に true・エラー空（OrderIdValue）",
                () =>
                {
                    var ok = OrderIdValue.TryCreate(7, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=OrderIdValue(7) errors*"
            ),
            Like(
                "trycreate-without-rules-profile-id",
                "TryCreate: 組み込み検証を持たない int/bool VO は正常入力で常に true・エラー空（ProfileIdValue）",
                () =>
                {
                    var ok = ProfileIdValue.TryCreate(9, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=ProfileIdValue(9) errors*"
            ),
            Like(
                "trycreate-without-rules-is-active",
                "TryCreate: 組み込み検証を持たない int/bool VO は正常入力で常に true・エラー空（IsActiveValue）",
                () =>
                {
                    var ok = IsActiveValue.TryCreate(true, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=IsActiveValue(true) errors*"
            ),
            Like(
                "trycreate-without-rules-memo",
                "TryCreate: 組み込み検証を持たない int/bool VO は正常入力で常に true・エラー空（MemoValue）",
                () =>
                {
                    var ok = MemoValue.TryCreate("ok", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=MemoValue(\"ok\") errors*"
            ),
            // VO は null を内包しない設計。Try パターンの公開 API として null は NRE でなく false＋エラーで返す
            Exact(
                "trycreate-string-null-name",
                "TryCreate(null): 文字列 VO は throw せず false＋必須エラーを返す（NameValue）",
                () =>
                {
                    var ok = NameValue.TryCreate(null!, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[A value is required.]"
            ),
            Like(
                "trycreate-string-null-memo",
                "TryCreate(null): 文字列 VO は throw せず false＋必須エラーを返す（MemoValue）",
                () =>
                {
                    var ok = MemoValue.TryCreate(null!, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(*)=[*]"
            ),
            Like(
                "trycreate-string-null-bio",
                "TryCreate(null): 文字列 VO は throw せず false＋必須エラーを返す（BioValue）",
                () =>
                {
                    var ok = BioValue.TryCreate(null!, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(*)=[*]"
            ),
            // バイナリ VO も null を内包しない（必須の検証が null を断る）
            Like(
                "trycreate-binary-null-note-blob",
                "TryCreate(null): バイナリ VO は throw せず false＋必須エラーを返す（NoteBlobValue）",
                () =>
                {
                    var ok = NoteBlobValue.TryCreate(null!, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[*]"
            ),
            Like(
                "create-binary-null-note-blob",
                "Create(null): バイナリ VO は ValueObjectValidationException（TryCreate false と対称）",
                () => NoteBlobValue.Create(null!),
                "throws ValueObjectValidationException: *"
            ),
            // ===== Validate(value, errors) は呼び出し側コレクションへ追記する（戻り値は見ない） =====
            Exact(
                "validate-string-appends-to-existing-name",
                "Validate(value, errors) は呼び出し側コレクションへ追記する（違反あり／なしの両側）（違反あり）",
                () =>
                {
                    var errors = new List<string> { "pre-existing" };
                    NameValue.Validate(new string('a', 51), errors);

                    return ValueObjectOutcome.ErrorsOnly(errors);
                },
                "errors(2)=[pre-existing|" + MaxLength50Exceeded + "]"
            ),
            Exact(
                "validate-string-no-violation-name",
                "Validate(value, errors) は呼び出し側コレクションへ追記する（違反あり／なしの両側）（違反なし）",
                () =>
                {
                    var errors = new List<string>();
                    NameValue.Validate("ok", errors);

                    return ValueObjectOutcome.ErrorsOnly(errors);
                },
                "errors=[]"
            ),
            // ===== 手書き VO の 3 メンバのレシピ（基底実装が動く） =====
            Exact(
                "create-handwritten-valid-contact-mail",
                "[手書きVO] Create は検証を通し、違反は ValueObjectValidationException になる（正常の側）",
                () => ContactMailValue.Create("a@example.com"),
                "ContactMailValue(\"a@example.com\")"
            ),
            Like(
                "create-handwritten-invalid-contact-mail",
                "[手書きVO] Create は検証を通し、違反は ValueObjectValidationException になる（違反の側）",
                () => ContactMailValue.Create("no-at-mark"),
                "throws ValueObjectValidationException: * type=QuickER.Tests.GeneratedFixture.ContactMailValue errors(1)=[Mail address must contain '@'.]"
            ),
            Exact(
                "trycreate-handwritten-valid-contact-mail",
                "[手書きVO] TryCreate は成功と失敗の両側で基底実装が動く（成功の側）",
                () =>
                {
                    var ok = ContactMailValue.TryCreate(
                        "a@example.com",
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=ContactMailValue(\"a@example.com\") errors=[]"
            ),
            Like(
                "trycreate-handwritten-invalid-contact-mail",
                "[手書きVO] TryCreate は成功と失敗の両側で基底実装が動く（失敗の側）",
                () =>
                {
                    var ok = ContactMailValue.TryCreate("bad", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[*]"
            ),
            Exact(
                "validate-handwritten-appends-contact-mail",
                "[手書きVO] Validate は VO を作らずに違反だけを既存コレクションへ足す",
                () =>
                {
                    var errors = new List<string> { "existing" };
                    var returned = ContactMailValue.Validate("bad", errors);

                    return ValueObjectOutcome.Validated(returned, errors);
                },
                "returns false errors(2)=[existing|Mail address must contain '@'.]"
            ),
            Exact(
                "create-handwritten-without-rules-plain-tag",
                "[手書きVO] 検証規則なしの VO は ValidateCore の既定実装（検証なし）で成立する（Create）",
                () => PlainTagValue.Create("anything"),
                "PlainTagValue(\"anything\")"
            ),
            Exact(
                "trycreate-handwritten-without-rules-plain-tag",
                "[手書きVO] 検証規則なしの VO は ValidateCore の既定実装（検証なし）で成立する（TryCreate）",
                () =>
                {
                    var ok = PlainTagValue.TryCreate("x", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "ok value=PlainTagValue(\"x\") errors=[]"
            ),
            Exact(
                "create-via-type-parameter-contact-mail",
                "[手書きVO] 型引数経由の TVo.Create（static abstract ディスパッチ）が継承実装へ届く（手書き VO）",
                () => CreateViaTypeParameter<ContactMailValue, string>("t@example.com"),
                "ContactMailValue(\"t@example.com\")"
            ),
            Exact(
                "create-via-type-parameter-name",
                "[手書きVO] 型引数経由の TVo.Create（static abstract ディスパッチ）が継承実装へ届く（生成 VO＝従来からの利用形が不変であることの対照）",
                () => CreateViaTypeParameter<NameValue, string>("Alice"),
                "NameValue(\"Alice\")"
            ),
        ];
}
