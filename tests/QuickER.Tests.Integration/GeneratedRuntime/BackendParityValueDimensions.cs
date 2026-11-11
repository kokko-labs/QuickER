using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の乱数の操作の列が書く値の幅（<see cref="BackendParityGenerator"/> の値の池）の次元表・網羅しない組の一覧・
/// 台帳の行
/// </summary>
/// <remarks>
/// <para>
/// ケースは、操作が書く値 1 つ（注文の日時・金額・メモ・除外 blob、明細の品名・割引、顧客の備考、連絡先のラベル、blob の書き込み）。
/// 1 つのケースは値の系統（日時・decimal・blob・文字列）のどれか 1 つだけを持ち、ほかの系統は <c>None</c>。null の値は数えない。
/// 観測はシナリオの操作の記録を読むだけで、生成器の乱数を引かない。
/// </para>
/// <para>
/// 比べ方は今のまま（日時は tick、decimal は値）で、Kind と decimal のスケールは比べない。値の幅は、tick・値・バイト・文字が
/// 実装先の型変換と往復で変わらないことを確かめる（Kind とスケールの実装先ごとの返し方は docs の生成 Repository の節）。
/// </para>
/// </remarks>
internal static class BackendParityValueDimensions
{
    /// <summary>因子：操作の範囲</summary>
    public const string Mode = "Mode";

    /// <summary>因子：日時の Kind</summary>
    public const string DateKind = "DateKind";

    /// <summary>因子：日時の端数</summary>
    public const string DateFraction = "DateFraction";

    /// <summary>因子：decimal の値</summary>
    public const string DecimalEdge = "DecimalEdge";

    /// <summary>因子：blob の形</summary>
    public const string BlobShape = "BlobShape";

    /// <summary>因子：文字列の形</summary>
    public const string TextShape = "TextShape";

    /// <summary>その系統の値でないケースの水準</summary>
    private const string None = "None";

    /// <summary>値の系統の因子（1 つのケースはちょうど 1 つの系統を持つ。日時は Kind と端数の 2 因子）</summary>
    private static readonly string[][] Families =
    [
        [DateKind, DateFraction],
        [DecimalEdge],
        [BlobShape],
        [TextShape],
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(
                    Mode,
                    Enum.GetValues<ParityGeneratorMode>()
                        .Select(value => new Level(value.ToString()))
                        .ToList(),
                    "種を 3 で割った余りで決まる操作の範囲（どの実装先で流すか）"
                ),
                new Factor(
                    DateKind,
                    [
                        new Level(None),
                        .. Enum.GetValues<DateTimeKind>()
                            .Select(value => new Level(value.ToString())),
                    ],
                    "注文の日時の Kind"
                ),
                new Factor(
                    DateFraction,
                    [new Level(None), new Level("Whole"), new Level("Ticks")],
                    "注文の日時の端数（整数秒・7 桁の tick）"
                ),
                new Factor(
                    DecimalEdge,
                    [
                        new Level(None),
                        new Level("Ordinary"),
                        new Level("Max"),
                        new Level("Min"),
                        new Level("Smallest"),
                        new Level("Zero"),
                    ],
                    "注文の金額・明細の割引（精度の上下限・最小の正の値・0・それ以外）"
                ),
                new Factor(
                    BlobShape,
                    [new Level(None), new Level("Empty"), new Level("Short"), new Level("Long")],
                    "除外 blob（挿入の値・Stream の書き込み）の長さ（0 バイト・短い・長い）"
                ),
                new Factor(
                    TextShape,
                    [
                        new Level(None),
                        new Level("Ascii"),
                        new Level("NonAscii"),
                        new Level("Surrogate"),
                    ],
                    "メモ・品名・備考・ラベルの文字（ASCII だけ・BMP の非 ASCII・サロゲートペア）"
                ),
            ],
            [],
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.GeneratedRuntime, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.GeneratedRuntime,
            "ParityValues",
            "実装先どうしの一致（乱数の操作の列が書く値の幅：日時の Kind と端数・decimal の端・blob の長さ・文字列の文字）",
            Table,
            0,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityValueDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityGenerator.Values.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/BackendParityRuntimeTests.cs",
            ],
            [
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.Sqlite",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Runtime.InMemory",
                "src/QuickER.Runtime.AspNetCore",
            ]
        );

    /// <summary>シナリオの操作が書く値を、流す前にケースとして数える</summary>
    public static void Observe(CoverageCounter counter, ParityScenario scenario)
    {
        var mode = scenario.Name.Split('-')[^1];

        foreach (var levels in scenario.Ops.SelectMany(Values))
        {
            var assignment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Mode] = mode,
                [DateKind] = None,
                [DateFraction] = None,
                [DecimalEdge] = None,
                [BlobShape] = None,
                [TextShape] = None,
            };

            foreach (var (name, value) in levels)
            {
                assignment[name] = value;
            }

            counter.Observe(assignment);
        }
    }

    /// <summary>操作が書く値（その系統の因子の水準）。null の値は返さない</summary>
    private static IEnumerable<(string Factor, string Level)[]> Values(ParityOp op)
    {
        switch (op)
        {
            case DeviatingOp deviating:
                foreach (var value in Values(deviating.Inner))
                {
                    yield return value;
                }

                break;

            case InsertOp { Row: OrderRow order }:
                if (order.At is { } at)
                {
                    yield return Date(at);
                }

                yield return Decimal(order.Amount, BackendParityGenerator.AmountEdges);

                foreach (var text in Text(order.Memo))
                {
                    yield return text;
                }

                foreach (var blob in Blob(order.Payload).Concat(Blob(order.Thumb)))
                {
                    yield return blob;
                }

                break;

            case InsertOp { Row: LineRow line }:
                foreach (var text in Text(line.Item))
                {
                    yield return text;
                }

                if (line.Discount is { } discount)
                {
                    yield return Decimal(discount, BackendParityGenerator.DiscountEdges);
                }

                break;

            case InsertOp { Row: CustomerRow customer }:
                foreach (var text in Text(customer.Note))
                {
                    yield return text;
                }

                break;

            case EditMemoOp edit:
                foreach (var text in Text(edit.Memo))
                {
                    yield return text;
                }

                break;

            case OutsideEditMemoOp edit:
                foreach (var text in Text(edit.Memo))
                {
                    yield return text;
                }

                break;

            case EditCustomerOp { Note: { } note }:
                foreach (var text in Text(note))
                {
                    yield return text;
                }

                break;

            case EditContactOp { Target: null, Label: { } label }:
                foreach (var text in Text(label))
                {
                    yield return text;
                }

                break;

            case WriteBlobOp write:
                foreach (var blob in Blob(write.Data))
                {
                    yield return blob;
                }

                break;
        }
    }

    private static (string, string)[] Date(DateTime value) =>
        [
            (DateKind, value.Kind.ToString()),
            (DateFraction, value.Ticks % TimeSpan.TicksPerSecond == 0 ? "Whole" : "Ticks"),
        ];

    private static (string, string)[] Decimal(decimal value, decimal[] edges) =>
        [
            (
                DecimalEdge,
                value == edges[0] ? "Max"
                : value == edges[1] ? "Min"
                : value == edges[2] ? "Smallest"
                : value == 0m ? "Zero"
                : "Ordinary"
            ),
        ];

    private static IEnumerable<(string, string)[]> Blob(byte[]? value)
    {
        if (value is null)
        {
            yield break;
        }

        yield return
        [
            (
                BlobShape,
                value.Length == 0 ? "Empty"
                : value.Length >= BackendParityGenerator.LongBlobLength ? "Long"
                : "Short"
            ),
        ];
    }

    private static IEnumerable<(string, string)[]> Text(string? value)
    {
        if (value is null)
        {
            yield break;
        }

        yield return
        [
            (
                TextShape,
                value.Any(char.IsSurrogate) ? "Surrogate"
                : value.Any(c => c > '\u007f') ? "NonAscii"
                : "Ascii"
            ),
        ];
    }

    /// <summary>1 つのケースはちょうど 1 つの系統を持ち、日時の 2 因子はそろって None か、そろって None でない</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        var present = Families
            .Select(family =>
                family
                    .Where(assignment.ContainsKey)
                    .Select(factor => assignment[factor] != None)
                    .ToList()
            )
            .ToList();

        // 日時の Kind と端数は同じ値の 2 面
        if (present[0] is [var kind, var fraction] && kind != fraction)
        {
            return false;
        }

        // 系統が 2 つ以上 None でない組は起こり得ない
        return present.Count(family => family.Any(isSet => isSet)) <= 1;
    }
}
