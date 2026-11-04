using System;
using System.Collections.Generic;
using System.Globalization;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedInMemoryFixture;

/// <summary>
/// <see cref="EditModelScenarioTests"/> の表と行・行を書くための部品。
/// </summary>
/// <remarks>表は式形式のプロパティで、引くたびに作る。</remarks>
internal static class EditModelScenarioRows
{
    /// <summary>em-blank-null：NULL 許容列の空欄は確定値を null にする</summary>
    internal static ScenarioTable<EditModelBlankNullCase> BlankNulls =>
        new(EngineIds.GeneratedRuntime, BlankNullRows());

    /// <summary>em-blank-required：非 NULL 列の空欄は必須エラー</summary>
    internal static ScenarioTable<EditModelBlankRequiredCase> BlankRequireds =>
        new(EngineIds.GeneratedRuntime, BlankRequiredRows());

    /// <summary>em-blank-parse：解析できない入力は変換エラー</summary>
    internal static ScenarioTable<EditModelBlankParseCase> BlankParses =>
        new(EngineIds.GeneratedRuntime, BlankParseRows());

    /// <summary>em-removed-graph：削除行の保存グラフ</summary>
    internal static ScenarioTable<EditModelRemovedGraphCase> RemovedGraphs =>
        new(EngineIds.GeneratedRuntime, RemovedGraphRows());

    /// <summary>em-removed-accept：削除確定行の受理</summary>
    internal static ScenarioTable<EditModelRemovedAcceptCase> RemovedAccepts =>
        new(EngineIds.GeneratedRuntime, RemovedAcceptRows());

    /// <summary>全列に値が入った Unchanged な BlankProbeEntity を作る。</summary>
    private static BlankProbeEntity BuildEntity() =>
        new()
        {
            ProbeId = 1,
            Flag = true,
            SeenAt = new DateTime(2026, 1, 2, 3, 4, 5),
            Chunk = [0x01, 0x02],
            Rate = 12.5m,
            LoggedAt = new DateTime(2026, 2, 3),
            Stamp = [0x0A, 0x0B],
        };

    /// <summary>ロード済み（Unchanged）の EditModel を作る。</summary>
    internal static BlankProbeEditModel BuildLoadedModel()
    {
        var entity = BuildEntity();
        entity.MarkUnchanged();
        return new BlankProbeMapper().CreateEditModel(entity);
    }

    /// <summary>
    /// 確定値を比べる形にする（null は <c>null</c>・decimal は末尾のゼロを落としたインバリアント・
    /// byte[] は <c>0x</c>＋大文字の 16 進）。
    /// </summary>
    /// <remarks>
    /// decimal の等値はスケールを見ず、byte[] の Equal は要素の並びを比べるので、それと同じ強さにする。
    /// </remarks>
    internal static string RenderValue(object? value) =>
        value switch
        {
            null => "null",
            decimal d => (d / 1.0000000000000000000000000000m).ToString(
                CultureInfo.InvariantCulture
            ),
            byte[] bytes => "0x" + Convert.ToHexString(bytes),
            _ => throw new NotSupportedException(
                $"写せない値の型 {value.GetType().FullName}（形を足すなら書式を決めてから）"
            ),
        };

    // ===== em-blank-null =====

    private static IEnumerable<ScenarioRow<EditModelBlankNullCase>> BlankNullRows() =>
        [
            new(
                "blank-null-nullable-bool",
                null,
                "空欄入力はNULL許容のbool列の確定値をnullへ戻す",
                new(
                    (model, text) => model.BindingFlag = text,
                    model => model.Flag,
                    nameof(BlankProbeEditModel.BindingFlag),
                    RowState.Updated
                )
            ),
            new(
                "blank-null-nullable-datetime",
                null,
                "空欄入力はNULL許容の日時列の確定値をnullへ戻す",
                new(
                    (model, text) => model.BindingSeenAt = text,
                    model => model.SeenAt,
                    nameof(BlankProbeEditModel.BindingSeenAt),
                    null
                )
            ),
            new(
                "blank-null-nullable-decimal",
                null,
                "空欄入力はNULL許容のdecimal列の確定値をnullへ戻す",
                new(
                    (model, text) => model.BindingRate = text,
                    model => model.Rate,
                    nameof(BlankProbeEditModel.BindingRate),
                    null
                )
            ),
            new(
                "blank-null-nullable-binary",
                null,
                "空欄入力はNULL許容のバイナリ列の確定値をnullへ戻す",
                new(
                    (model, text) => model.BindingChunk = text,
                    model => model.Chunk,
                    nameof(BlankProbeEditModel.BindingChunk),
                    null
                )
            ),
        ];

    // ===== em-blank-required =====

    private static IEnumerable<ScenarioRow<EditModelBlankRequiredCase>> BlankRequiredRows() =>
        [
            new(
                "blank-required-not-null-datetime",
                null,
                "非NULLの日時列の空欄は必須エラーになり変換エラーにはならない",
                new(
                    BuildLoadedModel,
                    model => model.BindingLoggedAt = string.Empty,
                    model => model.LoggedAt,
                    nameof(BlankProbeEditModel.BindingLoggedAt),
                    nameof(BlankProbeEditModel.LoggedAt),
                    model => model.BindingLoggedAt = "2026-02-03"
                )
            ),
            // 旧挙動は空欄→空配列（非 null）で必須検証を素通りしていた
            new(
                "blank-required-not-null-binary",
                null,
                "非NULLのバイナリ列の空欄は必須エラーになり空配列で素通りしない",
                new(
                    BuildLoadedModel,
                    model => model.BindingStamp = string.Empty,
                    model => model.Stamp,
                    nameof(BlankProbeEditModel.BindingStamp),
                    nameof(BlankProbeEditModel.Stamp),
                    null
                )
            ),
            new(
                "blank-required-new-row-empty-binary",
                null,
                "新しい行の除外していないバイナリ列の空配列は未入力として読み必須エラーになる",
                new(
                    () => new BlankProbeMapper().CreateEditModel(),
                    null,
                    model => model.Stamp,
                    nameof(BlankProbeEditModel.BindingStamp),
                    nameof(BlankProbeEditModel.Stamp),
                    null
                )
            ),
        ];

    // ===== em-blank-parse =====

    private static IEnumerable<ScenarioRow<EditModelBlankParseCase>> BlankParseRows() =>
        [
            new(
                "blank-parse-decimal-then-blank-clears",
                null,
                "解析不能な入力は従来どおり変換エラーになり空欄で取り下がる",
                new(
                    (model, text) => model.BindingRate = text,
                    "abc",
                    model => model.Rate,
                    "12.5",
                    nameof(BlankProbeEditModel.BindingRate),
                    nameof(BlankProbeEditModel.Rate),
                    "decimal",
                    true
                )
            ),
            new(
                "blank-parse-invalid-base64",
                null,
                "不正なBase64は従来どおり変換エラーになる",
                new(
                    (model, text) => model.BindingChunk = text,
                    "@@not-base64@@",
                    model => model.Chunk,
                    "0x0102",
                    nameof(BlankProbeEditModel.BindingChunk),
                    nameof(BlankProbeEditModel.Chunk),
                    "byte[]",
                    false
                )
            ),
        ];

    // ===== em-removed-graph =====

    private static IEnumerable<ScenarioRow<EditModelRemovedGraphCase>> RemovedGraphRows() =>
        [
            new(
                "removed-graph-removed-from-collection",
                null,
                "削除行の必須欄が空でも保存用のグラフ生成は失敗せずキーが載る",
                new((model, row) => model.Orders.Remove(row))
            ),
            new(
                "removed-graph-marked-in-collection",
                null,
                "コレクションに残した削除マーク行も未入力の非キー列で保存グラフ生成が止まらない",
                new((_, row) => row.MarkRemoved())
            ),
        ];

    // ===== em-removed-accept =====

    private static IEnumerable<ScenarioRow<EditModelRemovedAcceptCase>> RemovedAcceptRows() =>
        [
            new(
                "removed-accept-collection-accept-changes",
                null,
                "AcceptChangesは削除確定行をコレクションから外しRemovedのまま追跡にも残さない",
                new(model => model.Orders.AcceptChanges())
            ),
            new(
                "removed-accept-parent-accept-changes",
                null,
                "親のAcceptChanges経由でも削除確定行はコレクションから外れる",
                new(model => model.AcceptChanges(includeChildren: true))
            ),
        ];
}
