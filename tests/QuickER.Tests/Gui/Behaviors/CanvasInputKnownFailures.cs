using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Behaviors;

/// <summary>入力の列のエンジンの既知の失敗の一覧（形で名指す：不変条件・手の種類・中身の断片）</summary>
/// <remarks>
/// 載せているのは、きっかけ待ちの低（直す区切りを決めていない）だけ。網羅の行の範囲
/// （<see cref="CanvasInputDimensions.CoverageScope"/>）に当たる項目は置かない（網羅の行の違反は常に新しい失敗）ので、
/// きっかけ待ちの低に当たる組は網羅の行で作らない（<see cref="CanvasInputRows.PressCoverage"/> の remarks）。
/// </remarks>
internal static class CanvasInputKnownFailures
{
    private const string G3FixedIn = "きっかけ待ち（known-lows の Gui/Behaviors：G3）";

    private const string G5FixedIn = "きっかけ待ち（known-lows の Gui/Behaviors：G5）";

    /// <summary>ドラッグ中に取り除いたテーブルの移動を積む（G3）</summary>
    private static bool IsG3(EngineFailure failure) =>
        failure.Signature.StartsWith("I6:", StringComparison.Ordinal)
        && failure.Detail.Contains("取り除いた", StringComparison.Ordinal);

    /// <summary>Space の押下中に外れて戻るとキャンバスのカーソルが手のまま残る（G5）</summary>
    private static bool IsG5(EngineFailure failure) =>
        failure.Signature.StartsWith("I4:", StringComparison.Ordinal)
        && failure.Detail.Contains("キャンバスのカーソル Hand", StringComparison.Ordinal);

    /// <summary>項目</summary>
    public static IReadOnlyList<KnownEntry> Entries { get; } =
    [
        .. Rows("G3", G3FixedIn),
        .. Rows("G5", G5FixedIn),
        Random("G3", G3FixedIn, IsG3),
        Random("G5", G5FixedIn, IsG5),
    ];

    /// <summary>その指摘の決まった形の行（行ごとに 1 項目）</summary>
    private static IEnumerable<KnownEntry> Rows(string finding, string fixedIn) =>
        CanvasInputRows
            .Table.Where(row => row.Finding == finding)
            .Select(row => new KnownEntry(
                finding,
                KnownKind.Pending,
                fixedIn,
                null,
                row.Id,
                KnownMatch.KeyOnly
            ));

    /// <summary>乱数の手の列の項目（普段の既定の種の数で必ず起きる）</summary>
    private static KnownEntry Random(
        string finding,
        string fixedIn,
        Func<EngineFailure, bool> predicate
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            CanvasInputDimensions.RandomScope,
            KnownMatch.Predicate,
            Predicate: predicate,
            RandomSeeds: true,
            Reproduction: KnownReproduction.RegularSeeds
        );
}
