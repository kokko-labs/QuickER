using QuickER.Services;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.Services.FormatRoundTrip;

/// <summary>形の行 × 1 つの形式の共通の表明（形式ごとの派生クラスで流す）</summary>
/// <remarks>
/// 形式ごとにクラスを分けるのは、xUnit がクラスの中のテストを順に流し、クラスどうしを並列に流すため
/// （C# リバースの形の行が重く、1 つのクラスに集めると壁時計が延びる）。派生と形式の軸の正本の対応は
/// <see cref="FormatRoundTripTests.ShapeTestClasses_CoverFormatAxis"/> が照合する。派生の中で形式を名指しして分岐しない。
/// </remarks>
public abstract class FormatShapeTestsBase
{
    /// <summary>流す形式（形式の軸の値）</summary>
    public abstract string Format { get; }

    /// <summary>形の行</summary>
    public static TheoryData<string> ShapeIds() => FormatShapeRows.Table.Ids();

    [Theory(DisplayName = "形の行：往復・告知・注入・マージの共通の表明")]
    [MemberData(nameof(ShapeIds))]
    public void Shape_KeepsCommonProperties(string id) => RunShapeCase(id, Format);

    /// <summary>形の行 × 形式の 1 ケースを、エンジンの UI のカルチャで流して既知の失敗の台帳と照合する</summary>
    /// <param name="id">形の行の Id</param>
    /// <param name="format">形式</param>
    internal static void RunShapeCase(string id, string format) =>
        FormatUiCulture.Run(() =>
            FormatRoundTripKnownFailures.Ledger.Run(
                FormatRoundTripKnownFailures.CaseId(id, format),
                () =>
                {
                    var problems = FormatRoundTripRunner.Check(
                        FormatAxis.Axis.Get(format),
                        FormatShapeRows.Table[id].Input
                    );

                    Assert.True(
                        problems.Count == 0,
                        $"{id} × {format}:\n  " + string.Join("\n  ", problems)
                    );
                }
            )
        );
}

/// <summary>形の行 × DBML</summary>
[MatrixAxisValue(FormatRoundTripDimensions.Format, nameof(DiagramExportFormat.Dbml))]
public sealed class FormatShapeDbmlTests : FormatShapeTestsBase
{
    /// <inheritdoc/>
    public override string Format => nameof(DiagramExportFormat.Dbml);
}

/// <summary>形の行 × Mermaid</summary>
[MatrixAxisValue(FormatRoundTripDimensions.Format, nameof(DiagramExportFormat.Mermaid))]
public sealed class FormatShapeMermaidTests : FormatShapeTestsBase
{
    /// <inheritdoc/>
    public override string Format => nameof(DiagramExportFormat.Mermaid);
}

/// <summary>形の行 × Excel 定義書</summary>
[MatrixAxisValue(FormatRoundTripDimensions.Format, nameof(DiagramExportFormat.Excel))]
public sealed class FormatShapeExcelTests : FormatShapeTestsBase
{
    /// <inheritdoc/>
    public override string Format => nameof(DiagramExportFormat.Excel);
}

/// <summary>形の行 × HTML 定義書</summary>
[MatrixAxisValue(FormatRoundTripDimensions.Format, nameof(DiagramExportFormat.Html))]
public sealed class FormatShapeHtmlTests : FormatShapeTestsBase
{
    /// <inheritdoc/>
    public override string Format => nameof(DiagramExportFormat.Html);
}

/// <summary>形の行 × C# リバース</summary>
[MatrixAxisValue(FormatRoundTripDimensions.Format, CSharpReverseAdapter.Value)]
public sealed class FormatShapeCSharpReverseTests : FormatShapeTestsBase
{
    /// <inheritdoc/>
    public override string Format => CSharpReverseAdapter.Value;
}

/// <summary>規則の行（形式の固有の入力の取込と不動点）</summary>
public sealed class FormatRuleTests
{
    /// <summary>規則の行</summary>
    public static TheoryData<string> RuleIds() => FormatRuleRows.Table.Ids();

    [Theory(DisplayName = "規則の行：形式の固有の入力の取込と不動点")]
    [MemberData(nameof(RuleIds))]
    public void Rule_Matches(string id) => RunRuleCase(id);

    /// <summary>規則の行の 1 ケースを、エンジンの UI のカルチャで流す</summary>
    /// <param name="id">規則の行の Id</param>
    internal static void RunRuleCase(string id) =>
        FormatUiCulture.Run(() =>
        {
            var problems = FormatRuleRunner.Check(FormatRuleRows.Table[id].Input);

            Assert.True(problems.Count == 0, $"{id}:\n  " + string.Join("\n  ", problems));
        });
}

/// <summary>マージの行（現在の図と取込の図からマージ後の図・Id の引き継ぎ・クエリ）</summary>
public sealed class FormatMergeTests
{
    /// <summary>マージの行</summary>
    public static TheoryData<string> MergeIds() => FormatMergeRows.Table.Ids();

    [Theory(DisplayName = "マージの行：現在の図と取込の図からマージ後の図・Id の引き継ぎ・クエリ")]
    [MemberData(nameof(MergeIds))]
    public void Merge_Matches(string id) =>
        FormatUiCulture.Run(() =>
        {
            var problems = FormatMergeRunner.Check(FormatMergeRows.Table[id].Input);

            Assert.True(problems.Count == 0, $"{id}:\n  " + string.Join("\n  ", problems));
        });
}
