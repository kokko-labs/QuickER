using System;
using System.Collections.Generic;
using System.Globalization;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedFixture;

// [DeclaredInstance] レジストリ（定義済みインスタンスの宣言的定義）と入力表記（InputText / ClaimsAbsent）の行。
// 引き当て（Materialize のフック優先→レジストリ→new）と未定義値の拒否（ValidateCore→メンバーシップ検査）、
// 基底 TryCreateFrom の 2 駅（空欄: フック→ClaimsAbsent→null／文字列入力: フック→InputText→通常変換）を固定する。
// 照合は Ordinal 完全一致・Trim なし・string 入力のみで、[Display] の名前は入力として受けない。
internal static partial class ValueObjectScenarioRows
{
    /// <summary>InputText の「○」（U+25CB）</summary>
    private const string Circle = "\u25CB";

    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> DeclaredCallRows() =>
        [
            // ===== 集合外の値の拒否 =====
            Exact(
                "trycreate-declared-outside-set-rejected",
                "[宣言済み] 集合外の値は Create / TryCreate / Validate すべてで拒否される（TryCreate）",
                () =>
                {
                    var ok = MeasureStatus.TryCreate(5, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=['5' is not a defined MeasureStatus.]"
            ),
            Like(
                "create-declared-outside-set-rejected",
                "[宣言済み] 集合外の値は Create / TryCreate / Validate すべてで拒否される（Create）",
                () => MeasureStatus.Create(5),
                "throws ValueObjectValidationException: *"
            ),
            Like(
                "validate-declared-outside-set-rejected",
                "[宣言済み] 集合外の値は Create / TryCreate / Validate すべてで拒否される（Validate）",
                () =>
                {
                    var collected = new List<string>();
                    var returned = MeasureStatus.Validate(5, collected);

                    return ValueObjectOutcome.Validated(returned, collected);
                },
                "returns false errors(1)=[*]"
            ),
            // 属性なしの便宜定数（Legacy=99）は集合に入らない＝99 も拒否される
            Like(
                "trycreate-declared-unmarked-constant-rejected",
                "[宣言済み] 集合外の値は Create / TryCreate / Validate すべてで拒否される（属性なしの便宜定数の値 99）",
                () =>
                {
                    var ok = MeasureStatus.TryCreate(99, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail *"
            ),
            // ===== 表示名（[Display] → DeclaredDisplayName、無ければ ToString へフォールバック） =====
            Exact(
                "displayvalue-declared-display-name-preparing",
                "[宣言済み] DeclaredDisplayName は [Display] の名前を返し、無い欄は null（Preparing）",
                () => MeasureStatus.Preparing.DisplayValue,
                "\"拾い出し\""
            ),
            Exact(
                "displayvalue-declared-display-name-in-progress",
                "[宣言済み] DeclaredDisplayName は [Display] の名前を返し、無い欄は null（InProgress）",
                () => MeasureStatus.InProgress.DisplayValue,
                "\"測定中\""
            ),
            Exact(
                "displayvalue-declared-without-display-falls-back",
                "[宣言済み] DeclaredDisplayName は [Display] の名前を返し、無い欄は null（Display なし→ToString へフォールバック）",
                () => MeasureStatus.Completed.DisplayValue,
                "\"9\""
            ),
            // 値等値で引くので、定義済み側の値なら Create が返す共有インスタンスでも名前が届く
            Exact(
                "displayvalue-declared-created-instance",
                "[宣言済み] DeclaredDisplayName は [Display] の名前を返し、無い欄は null（Create が返すインスタンス）",
                () => MeasureStatus.Create(1).DisplayValue,
                "\"拾い出し\""
            ),
            // ===== フックと属性の併用（OnValidate とメンバーシップ検査は両方走る） =====
            Like(
                "trycreate-hook-and-declared-both-validate",
                "[宣言済み] フックと属性は併用でき、引き当てはフックが優先される（値 3 は両方の違反が積まれる）",
                () =>
                {
                    var ok = StageMode.TryCreate(3, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=* errors(2)=[custom rule for 3|'3' is not a defined StageMode.]"
            ),
            // ===== 入力表記の不一致（Ordinal 完全一致・Trim なし） =====
            // 空白のみは「空欄」でも「○」でもない → bool への通常変換に失敗して検証エラー
            Like(
                "trycreatefrom-inputtext-whitespace-not-matched",
                "[入力表記] 空白のみ・表記ゆれの文字列は一致せず通常変換のまま（空白のみ）",
                () =>
                {
                    var ok = CheckMark.TryCreateFrom("\u0020", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[*]"
            ),
            Like(
                "trycreatefrom-inputtext-trailing-space-not-matched",
                "[入力表記] 空白のみ・表記ゆれの文字列は一致せず通常変換のまま（末尾スペース付きは Trim されない）",
                () =>
                {
                    var ok = CheckMark.TryCreateFrom(
                        Circle + "\u0020",
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=* errors(1)=[*]"
            ),
            // 閉じた集合の拒否（既存機能）は入力表記と同居してもそのまま効いている
            Like(
                "trycreate-int-code-outside-set-rejected",
                "[入力表記] int のコード列挙でも表記と通常変換が同居する（集合外の値 5 は拒否）",
                () =>
                {
                    var ok = WorkState.TryCreate(5, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=* errors(1)=[*]"
            ),
            // [Display] の名前は表示専用で、入力としては受けない（InputText の明示オプトインだけが入力になる）
            Like(
                "trycreatefrom-display-name-not-input",
                "[入力表記] Display の名前は入力として受けない",
                () =>
                {
                    var ok = MeasureStatus.TryCreateFrom("拾い出し", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[*]"
            ),
            // ===== 表示側（Display 名がそのまま出る） =====
            Exact(
                "displayvalue-inputtext-marked",
                "[入力表記] 表示側は Display 名がそのまま出る（空文字の Display も含む）（Marked）",
                () => CheckMark.Marked.DisplayValue,
                "\"" + Circle + "\""
            ),
            // Display(Name = "") は「空欄として表示する」の宣言
            Exact(
                "displayvalue-claimsabsent-unmarked-empty",
                "[入力表記] 表示側は Display 名がそのまま出る（空文字の Display も含む）（Unmarked）",
                () => CheckMark.Unmarked.DisplayValue,
                "\"\""
            ),
        ];

    private static IEnumerable<ScenarioRow<ValueObjectSameCase>> DeclaredSameRows() =>
        [
            // ===== 属性付きフィールドが全生成経路で返る =====
            SameAs(
                "declared-create-returns-declared-instance",
                "[宣言済み] 属性付きフィールドが全生成経路で定義済みインスタンスとして返る（Create）",
                () => MeasureStatus.Create(1),
                () => MeasureStatus.Preparing
            ),
            SameAs(
                "declared-trycreate-returns-declared-instance",
                "[宣言済み] 属性付きフィールドが全生成経路で定義済みインスタンスとして返る（TryCreate）",
                () =>
                {
                    var ok = MeasureStatus.TryCreate(2, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => MeasureStatus.InProgress,
                "ok errors=[]"
            ),
            // DB 読み出し経路（値→VO の再ラップ）も同じインスタンス
            SameAs(
                "declared-wrap-returns-declared-instance",
                "[宣言済み] 属性付きフィールドが全生成経路で定義済みインスタンスとして返る（Wrap）",
                () => SqlValueObjectActivator.Wrap(9, typeof(MeasureStatus)),
                () => MeasureStatus.Completed
            ),
            // ===== フックと属性の併用（引き当てはフック優先） =====
            SameAs(
                "hook-precedes-declared-instance",
                "[宣言済み] フックと属性は併用でき、引き当てはフックが優先される（フックが値 2 を引き当てる）",
                () => StageMode.Create(2),
                () => StageMode.HookPublished
            ),
            SameAs(
                "declared-instance-when-hook-skips",
                "[宣言済み] フックと属性は併用でき、引き当てはフックが優先される（フックが扱わない値 1 はレジストリ）",
                () => StageMode.Create(1),
                () => StageMode.Draft
            ),
            // ===== 属性を持たない型は毎回新しいインスタンス（等値は vo-equality の行） =====
            NotSameAs(
                "create-without-declared-returns-new-instance",
                "[宣言済み] 属性を持たない型の挙動は従来どおり（毎回新しいインスタンス）／[定義済み] フックを書かない VO は従来どおり毎回新しいインスタンスを作る",
                () => CustomerIdValue.Create(5),
                () => CustomerIdValue.Create(5)
            ),
            // ===== InputText はどの呼び形でも効く（行のラムダの中で具象型名のまま呼ぶ） =====
            SameAs(
                "inputtext-four-args-concrete-type",
                "[入力表記] InputText はどの呼び形でも効く（4 引数・具象型名）",
                () =>
                {
                    var ok = CheckMark.TryCreateFrom(
                        Circle,
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => CheckMark.Marked,
                "ok errors=[]"
            ),
            SameAs(
                "inputtext-three-args-concrete-type",
                "[入力表記] InputText はどの呼び形でも効く（3 引数・具象型名）",
                () =>
                {
                    var ok = CheckMark.TryCreateFrom(Circle, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => CheckMark.Marked,
                "ok *"
            ),
            SameAs(
                "inputtext-createfrom",
                "[入力表記] InputText はどの呼び形でも効く（CreateFrom）",
                () => CheckMark.CreateFrom(Circle),
                () => CheckMark.Marked
            ),
            SameAs(
                "inputtext-readcell-generic",
                "[入力表記] InputText はどの呼び形でも効く（型引数経由＝ジェネリック取り込み経路）",
                () => ReadCellOutcome<CheckMark>(Circle, null),
                () => CheckMark.Marked,
                "errors=[]"
            ),
            // ===== ClaimsAbsent は空欄 3 形で効く =====
            SameAs(
                "claimsabsent-empty-string-three-args",
                "[入力表記] ClaimsAbsent は空欄 3 形（null / DBNull / 空文字）すべてで効く（空文字・3 引数）",
                () =>
                {
                    var ok = CheckMark.TryCreateFrom("", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => CheckMark.Unmarked,
                "ok errors=[]"
            ),
            SameAs(
                "claimsabsent-createfrom-null",
                "[入力表記] ClaimsAbsent は空欄 3 形（null / DBNull / 空文字）すべてで効く（null・CreateFrom）",
                () => CheckMark.CreateFrom(null),
                () => CheckMark.Unmarked
            ),
            SameAs(
                "claimsabsent-createfrom-dbnull-invariant",
                "[入力表記] ClaimsAbsent は空欄 3 形（null / DBNull / 空文字）すべてで効く（DBNull・CreateFrom）",
                () => CheckMark.CreateFrom(DBNull.Value, CultureInfo.InvariantCulture),
                () => CheckMark.Unmarked
            ),
            // ===== 非文字列・通常変換可能な文字列は通常変換 → Materialize のレジストリが宣言済みへ引き当てる =====
            SameAs(
                "inputtext-createfrom-bool",
                "[入力表記] 非文字列と通常変換可能な文字列は従来どおり変換され、同じ宣言済みインスタンスに解決される（bool）",
                () => CheckMark.CreateFrom(true),
                () => CheckMark.Marked
            ),
            SameAs(
                "inputtext-createfrom-convertible-string",
                "[入力表記] 非文字列と通常変換可能な文字列は従来どおり変換され、同じ宣言済みインスタンスに解決される（\"True\"）",
                () => CheckMark.CreateFrom("True"),
                () => CheckMark.Marked
            ),
            // ===== int のコード列挙（表記と通常変換が同居） =====
            SameAs(
                "inputtext-int-code-text",
                "[入力表記] int のコード列挙でも表記と通常変換が同居する（表記「済」）",
                () => WorkState.CreateFrom("済"),
                () => WorkState.Done
            ),
            SameAs(
                "inputtext-int-code-digits",
                "[入力表記] int のコード列挙でも表記と通常変換が同居する（数字の文字列 \"9\" は通常変換）",
                () => WorkState.CreateFrom("9"),
                () => WorkState.Done
            ),
            SameAs(
                "inputtext-int-code-digits-without-text",
                "[入力表記] int のコード列挙でも表記と通常変換が同居する（表記なしの Open も \"1\" で引ける）",
                () => WorkState.CreateFrom("1"),
                () => WorkState.Open
            ),
            // ===== フックは宣言表記より優先される（書かれる側・空欄側とも） =====
            SameAs(
                "hook-precedes-inputtext",
                "[入力表記] フックは宣言表記より優先される（書かれる側・空欄側とも）（「○」はフックの HookMarked）",
                () => OverrideMark.CreateFrom(Circle),
                () => OverrideMark.HookMarked
            ),
            NotSameAs(
                "hook-precedes-inputtext-not-declared",
                "[入力表記] フックは宣言表記より優先される（書かれる側・空欄側とも）（「○」は InputText の Marked ではない）",
                () => OverrideMark.CreateFrom(Circle),
                () => OverrideMark.Marked
            ),
            SameAs(
                "hook-precedes-claimsabsent",
                "[入力表記] フックは宣言表記より優先される（書かれる側・空欄側とも）（空欄 null はフックの HookAbsent）",
                () => OverrideMark.CreateFrom(null),
                () => OverrideMark.HookAbsent
            ),
            NotSameAs(
                "hook-precedes-claimsabsent-not-declared",
                "[入力表記] フックは宣言表記より優先される（書かれる側・空欄側とも）（空文字は ClaimsAbsent の Unmarked ではない）",
                () => OverrideMark.CreateFrom(""),
                () => OverrideMark.Unmarked
            ),
        ];

    // 各型は初回使用（静的キャッシュ）を観測するので、1 型を 1 行だけで、行のラムダの中でだけ触れる
    private static IEnumerable<ScenarioRow<ValueObjectFirstUseCase>> FirstUseRows() =>
        [
            FirstUseOf(
                "first-use-declared-not-readonly-field",
                "[宣言済み] readonly でないフィールドへの付与は初回使用時に fail-fast する",
                () => BrokenDeclaredStatus.Create(1),
                typeof(InvalidOperationException),
                "*public static readonly*Oops*"
            ),
            FirstUseOf(
                "first-use-declared-binary-value-object",
                "[宣言済み] バイナリ VO への付与は初回使用時に fail-fast する",
                () => DeclaredBlob.Create(new byte[] { 1 }),
                typeof(InvalidOperationException),
                "*not supported on a binary value object*DeclaredBlob*"
            ),
            FirstUseOf(
                "first-use-inputtext-duplicated",
                "[入力表記] InputText の重複・ClaimsAbsent の複数・空の InputText は初回使用時に fail-fast する（InputText の重複）",
                () => DupInputState.Create(1),
                typeof(InvalidOperationException),
                "*InputText '\u00D7'*"
            ),
            FirstUseOf(
                "first-use-claimsabsent-twice",
                "[入力表記] InputText の重複・ClaimsAbsent の複数・空の InputText は初回使用時に fail-fast する（ClaimsAbsent の複数）",
                () => DoubleAbsentState.Create(1),
                typeof(InvalidOperationException),
                "*claim the absent input more than once*"
            ),
            FirstUseOf(
                "first-use-inputtext-empty",
                "[入力表記] InputText の重複・ClaimsAbsent の複数・空の InputText は初回使用時に fail-fast する（空の InputText）",
                () => EmptyInputState.Create(1),
                typeof(InvalidOperationException),
                "*empty InputText*"
            ),
        ];
}
