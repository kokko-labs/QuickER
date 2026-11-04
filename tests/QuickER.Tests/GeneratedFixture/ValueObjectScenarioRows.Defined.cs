using System;
using System.Collections.Generic;
using System.Globalization;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedFixture;

// 定義済みインスタンス（TryGetDefined / partial フック GetDefinedInstance・ConvertCustomInput・ConvertAbsentInput）の行。
// 基底がどの呼び形でも TSelf. 経由でフックを照会することを、行のラムダの中で具象型名のまま呼んで固定する
// （旧設計では「4 引数の TryCreateFrom を具象型名で呼ぶ」形だけが override を静的束縛で迂回した）。
internal static partial class ValueObjectScenarioRows
{
    private static IEnumerable<ScenarioRow<ValueObjectCallCase>> DefinedCallRows() =>
        [
            Exact(
                "defined-handwritten-create-mode-name",
                "[定義済み] 手書きの列挙型 VO は Create で定義済みインスタンスを返す（Create(1) の ModeName）",
                () => DataAccessMode.Create(1).ModeName,
                "\"Web\""
            ),
            Exact(
                "trycreate-defined-handwritten-undefined-rejected",
                "[定義済み] 未定義の値は検証エラーになる（TryCreate）",
                () =>
                {
                    var ok = DataAccessMode.TryCreate(9, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=null errors(1)=[DataAccessMode 9 is not defined.]"
            ),
            Like(
                "create-defined-handwritten-undefined-rejected",
                "[定義済み] 未定義の値は検証エラーになる（Create）",
                () => DataAccessMode.Create(9),
                "throws ValueObjectValidationException: *"
            ),
            // New が作る素のインスタンスでは ModeName が空になる。定義済みが返っていることの裏取り
            Exact(
                "defined-generated-partial-mode-name",
                "[定義済み] 生成形の VO も partial の GetDefinedInstance だけで列挙型にできる（Create(2) の ModeName）",
                () => ScreenMode.Create(2).ModeName,
                "\"Edit\""
            ),
            Like(
                "trycreate-defined-generated-undefined-rejected",
                "[定義済み] 生成形の VO も partial の GetDefinedInstance だけで列挙型にできる（未定義の値 9 は拒否）",
                () =>
                {
                    var ok = ScreenMode.TryCreate(9, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                "fail value=* errors(1)=[ScreenMode 9 is not defined.]"
            ),
            Like(
                "readcell-defined-name-not-defined-rejected",
                "[定義済み] 名前からの生成をジェネリック経路へ乗せられる（定義に無い名前は null とエラー 1 件）",
                () => ReadCellOutcome<DataAccessMode>("Nope", null),
                "value=null errors(1)=[*]"
            ),
            // フックが扱わない値は通常の変換・生成のまま（他テストへの不干渉の裏取り）
            Exact(
                "bridge-generated-createfrom-unhandled-value",
                "[定義済み] 実生成 VO のブリッジが partial フックへ配線されている（フックが扱わない値の CreateFrom）／[空欄フック] 空欄以外は素通し",
                () => BioValue.CreateFrom("plain"),
                "BioValue(\"plain\")"
            ),
            // 空白のみの文字列は「空欄」ではない＝フックを通らず通常の生成になる（空欄述語の境界の固定）
            Exact(
                "absent-bridge-generated-whitespace-not-absent",
                "[空欄フック] 実生成 VO のブリッジが partial フックへ配線されている（空白のみは空欄でなく通常の生成）",
                () => BioValue.CreateFrom("\u0020"),
                "BioValue(\"\u0020\")"
            ),
            // 空欄以外は素通し（[空欄フック] の同じ表明は bridge-generated-createfrom-unhandled-value が持つ）
        ];

    private static IEnumerable<ScenarioRow<ValueObjectSameCase>> DefinedSameRows() =>
        [
            // ===== 手書きの列挙型 VO（TryGetDefined の直接実装） =====
            SameAs(
                "defined-handwritten-create",
                "[定義済み] 手書きの列挙型 VO は Create で定義済みインスタンスを返す（Create）",
                () => DataAccessMode.Create(2),
                () => DataAccessMode.Database
            ),
            SameAs(
                "defined-handwritten-trycreate",
                "[定義済み] 手書きの列挙型 VO は Create で定義済みインスタンスを返す（TryCreate）",
                () =>
                {
                    var ok = DataAccessMode.TryCreate(3, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => DataAccessMode.Fake,
                "ok *"
            ),
            // ===== 生成形の VO を partial の GetDefinedInstance だけで列挙型に =====
            SameAs(
                "defined-generated-partial-create",
                "[定義済み] 生成形の VO も partial の GetDefinedInstance だけで列挙型にできる（Create）",
                () => ScreenMode.Create(1),
                () => ScreenMode.List
            ),
            SameAs(
                "defined-generated-partial-trycreate",
                "[定義済み] 生成形の VO も partial の GetDefinedInstance だけで列挙型にできる（TryCreate）",
                () =>
                {
                    var ok = ScreenMode.TryCreate(2, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => ScreenMode.Edit,
                "ok *"
            ),
            // ===== 名前からの生成をジェネリック経路へ =====
            SameAs(
                "readcell-defined-name-handwritten",
                "[定義済み] 名前からの生成をジェネリック経路へ乗せられる（手書き VO・名前）",
                () => ReadCellOutcome<DataAccessMode>("Fake", null),
                () => DataAccessMode.Fake,
                "errors=[]"
            ),
            SameAs(
                "readcell-defined-digits-handwritten",
                "[定義済み] 名前からの生成をジェネリック経路へ乗せられる（手書き VO・数字の文字列）",
                () => ReadCellOutcome<DataAccessMode>("2", null),
                () => DataAccessMode.Database,
                "errors=[]"
            ),
            SameAs(
                "readcell-defined-name-generated-partial",
                "[定義済み] 名前からの生成をジェネリック経路へ乗せられる（生成形 VO・名前）",
                () => ReadCellOutcome<ScreenMode>("Edit", null),
                () => ScreenMode.Edit,
                "errors=[]"
            ),
            SameAs(
                "readcell-defined-int-generated-partial",
                "[定義済み] 名前からの生成をジェネリック経路へ乗せられる（生成形 VO・int）",
                () => ReadCellOutcome<ScreenMode>(1, null),
                () => ScreenMode.List,
                "errors=[]"
            ),
            // ===== 変換フックはどの呼び形でも効く =====
            SameAs(
                "convert-hook-four-args-concrete-type",
                "[定義済み] 変換フックはどの呼び形でも効く（4 引数・具象型名＝旧設計で唯一 override を迂回した形）",
                () =>
                {
                    var ok = ScreenMode.TryCreateFrom(
                        "Edit",
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => ScreenMode.Edit,
                "ok *"
            ),
            SameAs(
                "convert-hook-three-args-concrete-type",
                "[定義済み] 変換フックはどの呼び形でも効く（3 引数・具象型名）",
                () =>
                {
                    var ok = ScreenMode.TryCreateFrom("Edit", out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => ScreenMode.Edit,
                "ok *"
            ),
            SameAs(
                "convert-hook-createfrom",
                "[定義済み] 変換フックはどの呼び形でも効く（CreateFrom）",
                () => ScreenMode.CreateFrom("Edit", CultureInfo.InvariantCulture),
                () => ScreenMode.Edit
            ),
            SameAs(
                "convert-hook-readcell-generic",
                "[定義済み] 変換フックはどの呼び形でも効く（型引数経由＝ジェネリック取り込み経路）",
                () => ReadCellOutcome<ScreenMode>("Edit", null),
                () => ScreenMode.Edit,
                "errors=[]"
            ),
            SameAs(
                "convert-hook-four-args-handwritten",
                "[定義済み] 変換フックはどの呼び形でも効く（手書き型＝interface フック直接実装の 4 引数・具象型名）",
                () =>
                {
                    var ok = DataAccessMode.TryCreateFrom(
                        "Database",
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => DataAccessMode.Database,
                "ok *"
            ),
            // ===== 実生成 VO（BioValue）のブリッジが partial フックへ配線されている =====
            SameAs(
                "bridge-generated-create-defined",
                "[定義済み] 実生成 VO のブリッジが partial フックへ配線されている（GetDefinedInstance・Create）",
                () => BioValue.Create("#defined"),
                () => BioValue.DefinedBio
            ),
            SameAs(
                "bridge-generated-wrap-defined",
                "[定義済み] 実生成 VO のブリッジが partial フックへ配線されている（GetDefinedInstance・DB 読み出しの Wrap）",
                () => SqlValueObjectActivator.Wrap("#defined", typeof(BioValue)),
                () => BioValue.DefinedBio
            ),
            SameAs(
                "bridge-generated-trycreatefrom-four-args-custom",
                "[定義済み] 実生成 VO のブリッジが partial フックへ配線されている（ConvertCustomInput・4 引数の型名呼び）",
                () =>
                {
                    var ok = BioValue.TryCreateFrom(
                        "@defined",
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => BioValue.DefinedBio,
                "ok *"
            ),
            NotSameAs(
                "bridge-generated-create-unhandled-new-instance",
                "[定義済み] 実生成 VO のブリッジが partial フックへ配線されている（フックが扱わない値は通常の生成）",
                () => BioValue.Create("plain"),
                () => BioValue.DefinedBio
            ),
            // ===== 空欄フックはどの呼び形でも効く（ScreenMode の ConvertAbsentInput は List を返す） =====
            SameAs(
                "absent-hook-four-args-concrete-type",
                "[空欄フック] 空欄フックはどの呼び形でも効く（4 引数・具象型名・空文字）",
                () =>
                {
                    var ok = ScreenMode.TryCreateFrom(
                        "",
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => ScreenMode.List,
                "ok errors=[]"
            ),
            SameAs(
                "absent-hook-three-args-concrete-type",
                "[空欄フック] 空欄フックはどの呼び形でも効く（3 引数・具象型名・null）",
                () =>
                {
                    var ok = ScreenMode.TryCreateFrom(null, out var value, out var errors);

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => ScreenMode.List,
                "ok *"
            ),
            SameAs(
                "absent-hook-createfrom-dbnull",
                "[空欄フック] 空欄フックはどの呼び形でも効く（CreateFrom・DBNull）",
                () => ScreenMode.CreateFrom(DBNull.Value, CultureInfo.InvariantCulture),
                () => ScreenMode.List
            ),
            SameAs(
                "absent-hook-readcell-generic",
                "[空欄フック] 空欄フックはどの呼び形でも効く（型引数経由＝ジェネリック取り込み経路・空文字）",
                () => ReadCellOutcome<ScreenMode>("", null),
                () => ScreenMode.List,
                "errors=[]"
            ),
            // 空欄でない入力は空欄フックを通らない（変換フック・通常変換は従来どおり）
            SameAs(
                "absent-hook-not-used-for-name",
                "[空欄フック] 空欄フックはどの呼び形でも効く（空欄でない名前は変換フックのまま）",
                () => ScreenMode.CreateFrom("Edit"),
                () => ScreenMode.Edit
            ),
            SameAs(
                "absent-hook-not-used-for-int",
                "[空欄フック] 空欄フックはどの呼び形でも効く（空欄でない int は通常変換のまま）",
                () => ScreenMode.CreateFrom(2, CultureInfo.InvariantCulture),
                () => ScreenMode.Edit
            ),
            // ===== 実生成 VO（BioValue）の空欄フックのブリッジ =====
            SameAs(
                "absent-bridge-generated-four-args-empty",
                "[空欄フック] 実生成 VO のブリッジが partial フックへ配線されている（4 引数・空文字）",
                () =>
                {
                    var ok = BioValue.TryCreateFrom(
                        "",
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => BioValue.AbsentBio,
                "ok *"
            ),
            SameAs(
                "absent-bridge-generated-createfrom-dbnull",
                "[空欄フック] 実生成 VO のブリッジが partial フックへ配線されている（CreateFrom・DBNull）",
                () => BioValue.CreateFrom(DBNull.Value),
                () => BioValue.AbsentBio
            ),
            SameAs(
                "absent-bridge-generated-readcell-null",
                "[空欄フック] 実生成 VO のブリッジが partial フックへ配線されている（型引数経由・null）",
                () => ReadCellOutcome<BioValue>(null, null),
                () => BioValue.AbsentBio,
                "errors=[]"
            ),
            NotSameAs(
                "absent-bridge-generated-whitespace-new-instance",
                "[空欄フック] 実生成 VO のブリッジが partial フックへ配線されている（空白のみは空欄のインスタンスにならない）",
                () => BioValue.CreateFrom("\u0020"),
                () => BioValue.AbsentBio
            ),
            // ===== 手書き VO が interface の TryConvertAbsentInput を直接実装する側 =====
            SameAs(
                "absent-handwritten-four-args-empty",
                "[空欄フック] 手書き VO は interface の直接実装でも空欄を引き取れる（4 引数の型名呼び・空文字）",
                () =>
                {
                    var ok = InspectionMark.TryCreateFrom(
                        "",
                        CultureInfo.InvariantCulture,
                        out var value,
                        out var errors
                    );

                    return ValueObjectOutcome.Try(ok, value, errors);
                },
                () => InspectionMark.Unmarked,
                "ok *"
            ),
            SameAs(
                "absent-handwritten-createfrom-null",
                "[空欄フック] 手書き VO は interface の直接実装でも空欄を引き取れる（CreateFrom・null）",
                () => InspectionMark.CreateFrom(null),
                () => InspectionMark.Unmarked
            ),
            // 書かれる側（○）は従来の変換フックのまま＝2 つのフックで 1 つの記法が閉じる
            SameAs(
                "absent-handwritten-marked-by-convert-hook",
                "[空欄フック] 手書き VO は interface の直接実装でも空欄を引き取れる（書かれる側は変換フック）",
                () => InspectionMark.CreateFrom(Circle),
                () => InspectionMark.Marked
            ),
            SameAs(
                "absent-handwritten-readcell-dbnull",
                "[空欄フック] 手書き VO は interface の直接実装でも空欄を引き取れる（型引数経由・DBNull）",
                () => ReadCellOutcome<InspectionMark>(DBNull.Value, null),
                () => InspectionMark.Unmarked,
                "errors=[]"
            ),
        ];
}
