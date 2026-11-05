using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>マージの行の表（現在の図と取込の図 → マージ後の図）</summary>
/// <remarks>主題ごとの行は <c>FormatMergeRows.{経路}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。</remarks>
internal static partial class FormatMergeRows
{
    /// <summary>マージの行の表</summary>
    public static ScenarioTable<FormatMerge> Table { get; } =
        // 行の期待が resx から組む文（断り方・警告）を、ケースと同じ UI のカルチャで組む（FormatUiCulture）
        FormatUiCulture.Run(() =>
            new ScenarioTable<FormatMerge>(EngineIds.FormatRoundTrip, AllRows())
        );

    private static IEnumerable<ScenarioRow<FormatMerge>> AllRows() =>
        [.. ReconcileRows(), .. ExcelMergeRows(), .. CSharpReverseMergeRows()];

    /// <summary>マージの行を始める</summary>
    private static FormatMergeBuilder Merge(FormatMergePath path, string id, string description) =>
        new(path, id, description);
}
