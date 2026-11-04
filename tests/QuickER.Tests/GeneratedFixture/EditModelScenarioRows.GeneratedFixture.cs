using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// <see cref="EditModelScenarioTests"/> の表と行。
/// </summary>
/// <remarks>
/// 表は式形式のプロパティで、引くたびに作る。em-format の期待（<c>ToString()</c> など）はカルチャで変わるので、
/// 行を作る時点でなく照合の時点（行のカルチャの中）でラムダから作る。
/// </remarks>
internal static class EditModelScenarioRows
{
    /// <summary>em-format：書式ヘルパーの表示文字列（型の場合×カルチャ）</summary>
    internal static ScenarioTable<EditModelFormatCase> Formats =>
        new(EngineIds.GeneratedRuntime, FormatRows());

    /// <summary>em-blank：名前欄の空白のみ・null の入力</summary>
    internal static ScenarioTable<EditModelBlankNameCase> Blanks =>
        new(EngineIds.GeneratedRuntime, BlankRows());

    /// <summary>em-range：RemoveRange の範囲外指定</summary>
    internal static ScenarioTable<EditModelRemoveRangeCase> Ranges =>
        new(EngineIds.GeneratedRuntime, RangeRows());

    /// <summary>em-removed：Remove→（Clear）→Add した要素の保存グラフ</summary>
    internal static ScenarioTable<EditModelRemoveAddCase> Removeds =>
        new(EngineIds.GeneratedRuntime, RemovedRows());

    /// <summary>em-mapper-null：NULL の列のロードと往復</summary>
    internal static ScenarioTable<EditModelMapperNullCase> MapperNulls =>
        new(EngineIds.GeneratedRuntime, MapperNullRows());

    /// <summary>em-mapper-removed：削除済み要素の includeRemoved の両分岐</summary>
    internal static ScenarioTable<EditModelMapperRemovedCase> MapperRemoveds =>
        new(EngineIds.GeneratedRuntime, MapperRemovedRows());

    /// <summary>em-sibling：削除マークした行の除外</summary>
    internal static ScenarioTable<EditModelRemovedSkipCase> Siblings =>
        new(EngineIds.GeneratedRuntime, SiblingRows());

    /// <summary>em-format で回すカルチャ（既定書式の癖が割れるもの・空文字＝インバリアント）と Id に付ける名前</summary>
    private static readonly (string Name, string IdSuffix)[] Cultures =
    [
        (string.Empty, "invariant"),
        ("ja-JP", "ja-jp"),
        ("en-US", "en-us"),
        ("de-DE", "de-de"),
        ("fr-FR", "fr-fr"),
    ];

    // ===== em-format =====

    /// <summary>
    /// 型の場合ごとに、5 つのカルチャの行を作る。
    /// </summary>
    /// <remarks>
    /// 守る性質は 2 つ。(1) 秒未満がゼロの値の表示文字列は従来と 1 文字も変わらない（＝その型自身の
    /// <c>ToString()</c> と一致する）。(2) 秒未満を持つ値の表示文字列は、バインド setter と同じパース
    /// （現在カルチャ・<c>TryParse(text, null, out _)</c>）で tick まで完全に往復する。
    /// 小数部の区切りは不変カルチャの '.' 固定なので、de-DE のように小数点がカンマのカルチャでもパーサーが受けることを確かめる。
    /// </remarks>
    private static IEnumerable<ScenarioRow<EditModelFormatCase>> FormatRows()
    {
        var cases = new (string Id, string Description, Func<string, EditModelFormatCase> Make)[]
        {
            // ===== (1) 秒未満ゼロ: 表示は従来（その型の ToString()）と完全一致 =====
            (
                "format-whole-second-datetime",
                "秒未満がゼロなら表示文字列は ToString() と 1 文字も変わらない（DateTime）",
                culture =>
                    Text(
                        culture,
                        () => EditModelInputFormat.Format(EditModelInputFormatTests.WholeSecond()),
                        () => EditModelInputFormatTests.WholeSecond().ToString()
                    )
            ),
            (
                "format-whole-second-datetimeoffset",
                "秒未満がゼロなら表示文字列は ToString() と 1 文字も変わらない（DateTimeOffset）",
                culture =>
                    Text(
                        culture,
                        () => EditModelInputFormat.Format(WholeSecondOffset()),
                        () => WholeSecondOffset().ToString()
                    )
            ),
            (
                "format-whole-second-timespan",
                "秒未満がゼロなら表示文字列は ToString() と 1 文字も変わらない（TimeSpan）",
                culture =>
                    Text(
                        culture,
                        () => EditModelInputFormat.Format(new TimeSpan(0, 14, 30, 15)),
                        () => new TimeSpan(0, 14, 30, 15).ToString()
                    )
            ),
            (
                "format-whole-second-timeonly",
                // TimeOnly の ToString() は秒を出さない短い形式なので、秒の無い値でだけ同じ表示になる
                "秒未満がゼロなら表示文字列は ToString() と 1 文字も変わらない（TimeOnly）",
                culture =>
                    Text(
                        culture,
                        () => EditModelInputFormat.Format(new TimeOnly(14, 30)),
                        () => new TimeOnly(14, 30).ToString()
                    )
            ),
            // ===== null は空文字 =====
            (
                "format-null-empty-datetime",
                "null は空文字（DateTime）",
                culture =>
                    Text(culture, () => EditModelInputFormat.Format((DateTime?)null), () => "")
            ),
            (
                "format-null-empty-datetimeoffset",
                "null は空文字（DateTimeOffset）",
                culture =>
                    Text(
                        culture,
                        () => EditModelInputFormat.Format((DateTimeOffset?)null),
                        () => ""
                    )
            ),
            (
                "format-null-empty-timespan",
                "null は空文字（TimeSpan）",
                culture =>
                    Text(culture, () => EditModelInputFormat.Format((TimeSpan?)null), () => "")
            ),
            (
                "format-null-empty-timeonly",
                "null は空文字（TimeOnly）",
                culture =>
                    Text(culture, () => EditModelInputFormat.Format((TimeOnly?)null), () => "")
            ),
            // ===== (2) 秒未満あり: バインド setter と同じパースで tick まで往復 =====
            (
                "format-roundtrip-datetime",
                "秒未満を持つ DateTime は表示→パースで tick 一致（往復）",
                culture =>
                    new(
                        culture,
                        () => EditModelInputFormat.Format(EditModelInputFormatTests.WithFraction()),
                        null,
                        ".1234567",
                        null,
                        ParseDateTimeTicks,
                        () => Ticks(EditModelInputFormatTests.WithFraction().Ticks)
                    )
            ),
            (
                "format-roundtrip-datetimeoffset",
                "秒未満を持つ DateTimeOffset は表示→パースで tick とオフセットごと往復",
                culture =>
                    new(
                        culture,
                        () => EditModelInputFormat.Format(FractionOffset()),
                        null,
                        ".1234567",
                        null,
                        text =>
                            DateTimeOffset.TryParse(text, null, out var parsed)
                                ? PointAndOffset(parsed)
                                : null,
                        () => PointAndOffset(FractionOffset())
                    )
            ),
            (
                "format-roundtrip-timespan",
                "秒未満を持つ TimeSpan は表示→パースで tick 一致（既定書式が端数を含む）",
                culture =>
                    new(
                        culture,
                        () => EditModelInputFormat.Format(FractionSpan()),
                        null,
                        ".1234567",
                        null,
                        text =>
                            TimeSpan.TryParse(text, null, out var parsed)
                                ? Ticks(parsed.Ticks)
                                : null,
                        () => Ticks(FractionSpan().Ticks)
                    )
            ),
            (
                "format-roundtrip-timeonly",
                "秒未満を持つ TimeOnly は表示→パースで tick 一致（秒まで出る書式へ切り替わる）",
                culture =>
                    new(
                        culture,
                        () => EditModelInputFormat.Format(FractionTime()),
                        null,
                        ".1234567",
                        null,
                        ParseTimeOnlyTicks,
                        () => Ticks(FractionTime().Ticks)
                    )
            ),
            (
                "format-roundtrip-timeonly-seconds",
                "秒を持つ TimeOnly は秒まで出し、表示→パースで往復する（分へ切り詰めない）",
                culture =>
                    new(
                        culture,
                        () => EditModelInputFormat.Format(new TimeOnly(14, 30, 15)),
                        () => new TimeOnly(14, 30, 15).ToLongTimeString(),
                        null,
                        null,
                        ParseTimeOnlyTicks,
                        () => Ticks(new TimeOnly(14, 30, 15).Ticks)
                    )
            ),
            // ===== 端数の綴り方（末尾ゼロなし・'.' 固定） =====
            (
                "format-fraction-spelling-datetime",
                // 100 ミリ秒ちょうど（末尾ゼロを落とせば ".1"）
                "端数は末尾ゼロを落とし、カルチャの小数点に依らず '.' で綴る",
                culture =>
                    new(
                        culture,
                        () => EditModelInputFormat.Format(HundredMilliseconds()),
                        null,
                        ".1",
                        ".1000000",
                        ParseDateTimeTicks,
                        () => Ticks(HundredMilliseconds().Ticks)
                    )
            ),
        };

        foreach (var (id, description, make) in cases)
        {
            foreach (var (culture, suffix) in Cultures)
            {
                var label = culture.Length == 0 ? "インバリアント" : culture;

                yield return new(
                    $"{id}-{suffix}",
                    null,
                    $"{description}（{label}）",
                    make(culture)
                );
            }
        }
    }

    /// <summary>表示文字列の完全一致だけを見る行</summary>
    private static EditModelFormatCase Text(
        string culture,
        Func<string> format,
        Func<string> expected
    ) => new(culture, format, expected, null, null, null, null);

    private static DateTimeOffset WholeSecondOffset() =>
        new(EditModelInputFormatTests.WholeSecond(), TimeSpan.FromHours(9));

    private static DateTimeOffset FractionOffset() =>
        new(EditModelInputFormatTests.WithFraction(), TimeSpan.FromHours(9));

    private static TimeSpan FractionSpan() =>
        new TimeSpan(0, 14, 30, 15).Add(TimeSpan.FromTicks(1_234_567));

    private static TimeOnly FractionTime() =>
        new TimeOnly(14, 30, 15).Add(TimeSpan.FromTicks(1_234_567));

    private static DateTime HundredMilliseconds() =>
        EditModelInputFormatTests.WholeSecond().AddTicks(TimeSpan.TicksPerMillisecond * 100);

    /// <summary>tick を比べる形（インバリアントの整数）にする</summary>
    private static string Ticks(long ticks) => ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// DateTimeOffset の「同じ時点」と「同じオフセット」を比べる形にする
    /// （元の表明は時点の等値とオフセットの等値の 2 つ）
    /// </summary>
    private static string PointAndOffset(DateTimeOffset value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"utc-ticks={value.UtcTicks} offset-ticks={value.Offset.Ticks}"
        );

    private static string? ParseDateTimeTicks(string text) =>
        DateTime.TryParse(text, null, out var parsed) ? Ticks(parsed.Ticks) : null;

    private static string? ParseTimeOnlyTicks(string text) =>
        TimeOnly.TryParse(text, null, out var parsed) ? Ticks(parsed.Ticks) : null;

    // ===== em-blank =====

    private static IEnumerable<ScenarioRow<EditModelBlankNameCase>> BlankRows() =>
        [
            new(
                "blank-whitespace-only-clears-name",
                null,
                "空白のみ入力: nullable 文字列列（VO）は確定値 null になる",
                new("\u0020\u0020\u0020\t\u0020\u3000\u0020\u0020", "", true)
            ),
            new(
                "blank-null-input-clears-name",
                null,
                "null 入力は落ちず未入力扱いになる（ComboBox 等が null を書き込むケースとの互換）",
                new(null, null, false)
            ),
        ];

    // ===== em-range =====

    private static IEnumerable<ScenarioRow<EditModelRemoveRangeCase>> RangeRows() =>
        [
            // 元のテストは指定範囲の削除（b,c）の後のコレクション（a,d）で範囲外を指定した
            new(
                "range-negative-index-rejected",
                null,
                "RemoveRange: 指定範囲を削除し、範囲外指定は ArgumentOutOfRangeException（index が負）",
                new(AfterValidRemoveRange, -1, 1, null, null, false)
            ),
            new(
                "range-count-beyond-end-rejected",
                null,
                "RemoveRange: 指定範囲を削除し、範囲外指定は ArgumentOutOfRangeException（count が末尾を越える）",
                new(AfterValidRemoveRange, 0, 99, null, null, false)
            ),
            // index + count が負へ桁あふれする指定は、素の足し算の比較では範囲内に見えて途中まで消してしまう
            new(
                "range-index-plus-count-overflow-rejected",
                null,
                "RemoveRange: index + count が int を超える指定も、1 件も消さずに範囲外として断る",
                new(
                    () =>
                        new EditModelCollection<OrderEditModel>
                        {
                            new OrderEditModel(),
                            new OrderEditModel(),
                            new OrderEditModel(),
                        },
                    1,
                    int.MaxValue,
                    "count",
                    3,
                    true
                )
            ),
        ];

    /// <summary>4 件のうち 2 件目から 2 件を削除した後のコレクション（元のテストの範囲外指定の前の状態）</summary>
    private static EditModelCollection<OrderEditModel> AfterValidRemoveRange()
    {
        var col = new EditModelCollection<OrderEditModel>
        {
            new OrderEditModel(),
            new OrderEditModel(),
            new OrderEditModel(),
            new OrderEditModel(),
        };

        col.RemoveRange(1, 2);

        return col;
    }

    // ===== em-removed =====

    private static IEnumerable<ScenarioRow<EditModelRemoveAddCase>> RemovedRows() =>
        [
            new(
                "removed-remove-clear-add-not-in-removed",
                null,
                "Remove→Clear→Add: 戻した要素は保存対象（includeRemoved）の削除分に現れない",
                new(true, 1)
            ),
            new(
                "removed-remove-add-not-in-removed",
                null,
                "Remove→Add: 戻した要素は保存対象（includeRemoved）の削除分に現れない",
                new(false, 2)
            ),
        ];

    // ===== em-mapper-null =====

    private static IEnumerable<ScenarioRow<EditModelMapperNullCase>> MapperNullRows() =>
        [
            new(
                "mapper-null-date-column-round-trips",
                null,
                "ロード: NULL の date 列は null のまま往復し、表示文字列は空になる",
                new(em => em.DeliveryDate, em => em.BindingDeliveryDate, e => e.DeliveryDate)
            ),
            new(
                "mapper-null-datetime-column-round-trips",
                null,
                "ロード: NULL の DateTime 列は null のまま往復し、表示文字列は空になる",
                new(em => em.OrderedAt, em => em.BindingOrderedAt, e => e.OrderedAt)
            ),
        ];

    // ===== em-mapper-removed =====

    private static IEnumerable<ScenarioRow<EditModelMapperRemovedCase>> MapperRemovedRows() =>
        [
            new(
                "mapper-removed-child-collection-both-branches",
                null,
                "CreateEntity(em, includeRemoved): 子コレクションの削除済み要素を分岐で除外／包含する",
                new(() =>
                {
                    var mapper = new CustomerMapper();
                    var em = mapper.CreateEditModel(MapperRoundTripTests.BuildFullCustomer()); // Orders×2
                    em.Orders.RemoveAt(1); // 既存要素を削除追跡

                    return includeRemoved =>
                        mapper
                            .CreateEntity(em, includeRemoved)
                            .Orders.Select(o => o.RowState)
                            .ToList();
                })
            ),
            new(
                "mapper-removed-create-entities-both-branches",
                null,
                "CreateEntities: 削除済みを includeRemoved で除外／包含する",
                new(() =>
                {
                    var orders = new EditModelCollection<OrderEditModel>
                    {
                        new OrderMapper().CreateEditModel(MapperRoundTripTests.BuildOrder(1)),
                        new OrderMapper().CreateEditModel(MapperRoundTripTests.BuildOrder(2)),
                    };
                    orders.RemoveAt(1); // 既存要素を削除追跡

                    var om = new OrderMapper();

                    return includeRemoved =>
                        om.CreateEntities(orders, includeRemoved).Select(o => o.RowState).ToList();
                })
            ),
        ];

    // ===== em-sibling =====

    private static IEnumerable<ScenarioRow<EditModelRemovedSkipCase>> SiblingRows() =>
        [
            // 重複側: 同じ customer_id を持つ 2 行のうち片方を削除マークすると重複ではなくなる
            new(
                "sibling-removed-duplicate-excluded",
                null,
                "削除行は重複検証でも変換エラーでも同じく除外される（非対称の解消・重複）",
                new([(1, 7, "bio-a"), (2, 7, "bio-b")], null)
            ),
            // 入力エラー側: 同じコレクション形で、削除マークした行の変換エラーも検証を止めない
            new(
                "sibling-removed-parse-error-excluded",
                null,
                "削除行は重複検証でも変換エラーでも同じく除外される（非対称の解消・変換エラー）",
                new([(1, 7, "bio-a"), (2, 8, "bio-b")], "not-a-number")
            ),
        ];
}
