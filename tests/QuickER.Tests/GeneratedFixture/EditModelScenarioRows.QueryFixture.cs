using System.Collections.Generic;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedQueryFixture;

/// <summary><see cref="EditModelScenarioTests"/> の表と行</summary>
internal static class EditModelScenarioRows
{
    /// <summary>em-sibling：コレクション内の重複の検出</summary>
    internal static ScenarioTable<EditModelSiblingCase> Siblings =>
        new(EngineIds.GeneratedRuntime, SiblingRows());

    /// <summary>em-db-error：確定値の編集で DB 照合の重複エラーが取り下げられる</summary>
    internal static ScenarioTable<EditModelDbErrorCase> DbErrors =>
        new(EngineIds.GeneratedRuntime, DbErrorRows());

    private const string BindingMemo = nameof(OrderEditModel.BindingMemo);
    private const string BindingCustomerId = nameof(OrderEditModel.BindingCustomerId);
    private const string BindingAmount = nameof(OrderEditModel.BindingAmount);

    // フィクスチャの orders には単一列制約 UQ_orders_memo（NULL 許容列）と複合制約（customer_id＋amount）がある
    private static IEnumerable<ScenarioRow<EditModelSiblingCase>> SiblingRows() =>
        [
            // 同じ値の組を持つ要素すべてに重複エラーが登録される（登録先は構成列のバインディングプロパティ）。
            // 重複していない列（確定値プロパティそのもの）には登録しない
            new(
                "sibling-duplicate-registers-on-every-member",
                null,
                "コレクション内の重複が全要素へ登録される",
                new(
                    [
                        new(10, 1, 100m, "apple pie"),
                        new(11, 2, 50m, "apple pie"),
                        new(12, 3, 20m, "banana"),
                    ],
                    null,
                    false,
                    [
                        new(0, BindingMemo, 1),
                        new(1, BindingMemo, 1),
                        new(2, BindingMemo, 0),
                        new(0, nameof(OrderEditModel.Memo), 0),
                    ]
                )
            ),
            // 複合制約は構成列すべてが一致したときだけ、全構成列のバインディングプロパティへ登録される。
            // 単一列制約（memo）は重複していないのでエラーなし
            new(
                "sibling-composite-registers-on-all-columns",
                null,
                "複合制約の重複は全構成列へ登録される",
                new(
                    [new(10, 1, 100m, "apple pie"), new(11, 1, 100m, "banana")],
                    null,
                    false,
                    [
                        new(0, BindingCustomerId, 1),
                        new(0, BindingAmount, 1),
                        new(1, BindingCustomerId, 1),
                        new(0, BindingMemo, 0),
                    ]
                )
            ),
            // 構成列の値に null を含む組は判定対象外（未入力の memo が並んでも重複にならない）
            new(
                "sibling-null-member-not-duplicate",
                null,
                "NULL を含む組は重複判定から外れる",
                new(
                    [new(10, 1, 100m, string.Empty), new(11, 2, 50m, string.Empty)],
                    null,
                    true,
                    [new(0, BindingMemo, 0), new(1, BindingMemo, 0)]
                )
            ),
            // 削除対象（RowState.Removed）の要素は比較から外れる
            new(
                "sibling-removed-element-excluded",
                null,
                "削除対象の要素は重複判定から外れる",
                new(
                    [new(10, 1, 100m, "apple pie"), new(11, 2, 50m, "apple pie")],
                    1,
                    true,
                    [new(0, BindingMemo, 0)]
                )
            ),
        ];

    // 照合したのは編集前の値なので、値が変われば結論は無効になる。複合制約は構成列すべての組で判定して
    // いるため、1 列でも変われば同じモデルの DB 由来所見はすべて取り下げる
    private static IEnumerable<ScenarioRow<EditModelDbErrorCase>> DbErrorRows() =>
        [
            // 重複していた欄そのものを編集する（バインディング経由＝確定値まで変わる）
            new(
                "db-error-cleared-by-editing-same-column",
                null,
                "確定値を編集すると DB 由来の重複エラーが解消される",
                new(BindingMemo, model => model.BindingMemo = "banana", true)
            ),
            // 複合制約（customer_id + amount）の違反は先頭の構成列へ載る。組のもう一方の列を編集する
            new(
                "db-error-cleared-by-editing-other-column",
                null,
                "複合制約の別列を編集しても DB 由来の重複エラーが残らない",
                new(BindingCustomerId, model => model.BindingAmount = "999", false)
            ),
        ];
}
