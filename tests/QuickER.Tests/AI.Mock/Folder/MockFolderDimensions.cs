using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Folder;

/// <summary>モックフォルダの保存単位の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>1 行＝（主な手の入口, 主な手の結果）を数える。</remarks>
internal static class MockFolderDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-folder";

    /// <summary>主な手の入口</summary>
    public const string Entry = "Entry";

    /// <summary>主な手の結果</summary>
    public const string Outcome = "Outcome";

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Entry,
                    Enum.GetValues<MockFolderEntry>()
                        .Select(entry => new Level(entry.ToString()))
                        .ToList(),
                    "主な手の入口（ストアの操作・読み取り・正規化・セッションの開始・フィードバック・ツール）"
                ),
                new Factor(
                    Outcome,
                    Enum.GetValues<MockFolderOutcome>()
                        .Select(outcome => new Level(outcome.ToString()))
                        .ToList(),
                    "主な手の結果（成功・拒否・I/O の失敗）"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                Accepted(
                    MockFolderEntry.SaveStylesheet,
                    MockFolderOutcome.Refused,
                    "SaveStylesheet に入力を断る経路は無い（検証は警告を返すだけ。src/QuickER.AI.Mock/MockFolderStore.cs:405-422）"
                ),
                Accepted(
                    MockFolderEntry.UpdateSourceSchema,
                    MockFolderOutcome.Refused,
                    "UpdateSourceSchema に入力を断る経路は無い（src/QuickER.AI.Mock/MockFolderStore.cs:449-454）"
                ),
                Accepted(
                    MockFolderEntry.Normalize,
                    MockFolderOutcome.Refused,
                    "NormalizeOperations・NormalizeEntities は断らない純関数（無効な入力は捨てて返す。src/QuickER.AI.Mock/MockFolderStore.cs:508-584）"
                ),
                Accepted(
                    MockFolderEntry.Normalize,
                    MockFolderOutcome.IoFailure,
                    "NormalizeOperations・NormalizeEntities はファイルを読み書きしない（src/QuickER.AI.Mock/MockFolderStore.cs:508-584）"
                ),
                Accepted(
                    MockFolderEntry.SessionFeedback,
                    MockFolderOutcome.IoFailure,
                    "SendFeedbackAsync はエンジンへ送るだけでストアを書かない（src/QuickER.AI.Mock/MockFolderDesignSession.cs:293-302）"
                ),
            ]
        );

    private static CoverageGapEntry Accepted(
        MockFolderEntry entry,
        MockFolderOutcome outcome,
        string reason
    ) =>
        CoverageGap.AcceptedTuple(
            Scope,
            "P3-11b",
            reason,
            (Entry, entry.ToString()),
            (Outcome, outcome.ToString())
        );

    /// <summary>1 行を数える</summary>
    public static void Observe(CoverageCounter counter, MockFolderScenario scenario) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entry] = scenario.Entry.ToString(),
                [Outcome] = scenario.Outcome.ToString(),
            }
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MockFolder",
            "モックフォルダの保存単位の操作の列（ストアとセッションの手・どの手の後にも不変条件を見張る）",
            Table,
            MockFolderRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/Folder/MockFolderDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/Folder/MockFolderHarness.cs",
                "tests/QuickER.Tests/AI.Mock/Folder/MockFolderRows.cs",
                "tests/QuickER.Tests/AI.Mock/Folder/MockFolderRows.Session.cs",
                "tests/QuickER.Tests/AI.Mock/Folder/MockFolderScenario.cs",
                "tests/QuickER.Tests/AI.Mock/Folder/MockFolderTests.cs",
            ],
            [
                "src/QuickER.AI.Mock/MockFolderStore.cs",
                "src/QuickER.AI.Mock/MockFolderDesignSession.cs",
                "src/QuickER.AI.Mock/MockManifest.cs",
            ]
        );
}
