using QuickER.Documents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

/// <summary>図ファイルの形式の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 行の宣言（段 × 結果の種類）を数える（行を流さない）。結果の種類は流し手が実際の結果と照らし合わせるので、宣言を数えてよい。
/// </remarks>
internal static class DocumentFormatDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "document-format";

    /// <summary>段</summary>
    public const string Stage = "Stage";

    /// <summary>結果の種類</summary>
    public const string Result = "Result";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.DocumentFile,
            [
                new Factor(
                    Stage,
                    Enum.GetValues<FormatStage>()
                        .Select(stage => new Level(stage.ToString()))
                        .ToList(),
                    "呼ぶ JsonStorageService の段"
                ),
                new Factor(
                    Result,
                    Enum.GetValues<FormatResult>().Select(ResultLevel).ToList(),
                    "結果の種類（TryLoad の断り方は DocumentLoadError の項目）"
                ),
            ],
            null,
            IsValid
        );

    private static Level ResultLevel(FormatResult result) =>
        result switch
        {
            FormatResult.ReadFailed => new Level(
                result.ToString(),
                AxisMember.Of(DocumentLoadError.ReadFailed)
            ),
            FormatResult.InvalidJson => new Level(
                result.ToString(),
                AxisMember.Of(DocumentLoadError.InvalidJson)
            ),
            FormatResult.NotDiagramDocument => new Level(
                result.ToString(),
                AxisMember.Of(DocumentLoadError.NotDiagramDocument)
            ),
            FormatResult.DuplicateId => new Level(
                result.ToString(),
                AxisMember.Of(DocumentLoadError.DuplicateId)
            ),
            FormatResult.MissingRequiredText => new Level(
                result.ToString(),
                AxisMember.Of(DocumentLoadError.MissingRequiredText)
            ),
            _ => new Level(result.ToString()),
        };

    /// <summary>原理的に起こり得ない組（段ごとに取り得る結果の種類が決まっている）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Stage, out var stageName)
            || !assignment.TryGetValue(Result, out var resultName)
        )
        {
            return true;
        }

        var stage = Enum.Parse<FormatStage>(stageName);
        var result = Enum.Parse<FormatResult>(resultName);

        return stage switch
        {
            // Load は読めた図を返すか例外を投げる
            FormatStage.Load => result is FormatResult.Loaded or FormatResult.Threw,
            // TryLoad は例外を投げず、読めたか断った種類を返す
            FormatStage.TryLoad => result
                is FormatResult.Loaded
                    or FormatResult.ReadFailed
                    or FormatResult.InvalidJson
                    or FormatResult.NotDiagramDocument
                    or FormatResult.DuplicateId
                    or FormatResult.MissingRequiredText,
            // 往復は書いて読み戻す（書けないか読めなければ例外）
            FormatStage.RoundTrip => result is FormatResult.Loaded or FormatResult.Threw,
            // 書き込みは書けたか例外
            FormatStage.SaveAtomic or FormatStage.SaveNew => result
                is FormatResult.Written
                    or FormatResult.Threw,
            // 版の判定は真か偽（例外を投げない）
            FormatStage.VersionProbe => result is FormatResult.Newer or FormatResult.NotNewer,
            _ => false,
        };
    }

    /// <summary>網羅しない組の一覧（段 × 結果の種類の、段が取り得る組の全部を行が持つ）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.DocumentFile, []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.DocumentFile,
            "Format",
            "図ファイルの形式の表（段 × 結果の種類）",
            Table,
            DocumentFormatRows.Table.Count,
            [
                "tests/QuickER.Tests/Document/DocumentFormatDimensions.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.AtomicSave.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.IdIntegrity.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.NullAndOmitted.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.Persistence.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.RequiredText.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.RoundTrip.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.TargetDbms.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.TryLoadClassification.cs",
                "tests/QuickER.Tests/Document/DocumentFormatRows.Version.cs",
                "tests/QuickER.Tests/Document/DocumentFormatTests.cs",
            ],
            ["src/QuickER.Document"]
        );

    /// <summary>行の宣言を数える</summary>
    public static void ObserveDeclarations(CoverageCounter counter)
    {
        foreach (var row in DocumentFormatRows.Table)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Stage] = row.Input.Stage.ToString(),
                    [Result] = row.Input.Result.ToString(),
                }
            );
        }
    }
}
