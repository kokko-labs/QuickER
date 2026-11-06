using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Outputs;

/// <summary>出力の表の対象（SUT）</summary>
internal enum MockOutputSut
{
    /// <summary>MockScreenLinks（相対画面リンクの抽出と画面 ID の正本）</summary>
    ScreenLinks,

    /// <summary>MockContentValidator（保存の検証）</summary>
    Validator,

    /// <summary>MockBundleExporter（単一 HTML）</summary>
    Bundle,

    /// <summary>MockDesignDocExporter（設計書）</summary>
    DesignDoc,

    /// <summary>MockResumePrompt.Build（再開のプロンプト）</summary>
    ResumePrompt,

    /// <summary>MockResumePrompt.IsSchemaChanged（スキーマの変更の判定）</summary>
    SchemaChange,

    /// <summary>MockSchemaMismatch・MockSchemaSerializer.Split（スキーマの食い違い）</summary>
    SchemaMismatch,

    /// <summary>MockFolderStore.Open・CreateNew の読み込みの検証と文言</summary>
    Load,
}

/// <summary>出力の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 行＝（対象, 観点）を 1 つ以上数える。リンクの表記の行は同じ入力を ScreenLinks・検証器・単一 HTML へ流すので 3 つの組を数え、
/// 検証器にはリンク先の宣言の状態の組も数える。画面 ID の行は ScreenLinks・設計書・単一 HTML へ流す（保存できる名前だけ）。
/// 観点はその対象の入力の形にだけ当てはまる（ほかの対象との組は <c>isValid</c> で外す）。
/// </remarks>
internal static class MockOutputsDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "mock-outputs";

    /// <summary>対象</summary>
    public const string Sut = "Sut";

    /// <summary>観点</summary>
    public const string Aspect = "Aspect";

    /// <summary>リンクの表記（ScreenLinks・検証器・単一 HTML）</summary>
    public static readonly IReadOnlyList<string> LinkNotations =
    [
        "link-double-quote",
        "link-single-quote",
        "link-dot-slash",
        "link-folder",
        "link-backslash",
        "link-parent-folder",
        "link-upper-case-extension",
        "link-fragment",
        "link-mismatched-quotes",
        "link-absolute-url",
        "link-file-url",
        "link-empty-file-name",
        "link-dotted-name",
    ];

    /// <summary>リンク先の宣言の状態（検証器）</summary>
    public static readonly IReadOnlyList<string> LinkTargets =
    [
        "target-known",
        "target-transition",
        "target-other-screen",
        "target-unknown",
    ];

    /// <summary>画面 ID の元の名前の形（ScreenLinks・設計書・単一 HTML）</summary>
    public static readonly IReadOnlyList<string> ScreenIdShapes =
    [
        "id-plain",
        "id-folder",
        "id-dotted",
        "id-extension-only",
        "id-reserved-word",
        "id-leading-digit",
    ];

    /// <summary>対象ごとの固有の観点</summary>
    private static readonly IReadOnlyDictionary<MockOutputSut, IReadOnlyList<string>> OwnAspects =
        new Dictionary<MockOutputSut, IReadOnlyList<string>>
        {
            [MockOutputSut.ScreenLinks] = [],
            [MockOutputSut.Validator] =
            [
                "validator-external-attribute",
                "validator-external-css",
                "validator-transition-not-linked",
                "validator-declared-targets-link-only",
                "validator-stylesheet-missing",
            ],
            [MockOutputSut.Bundle] =
            [
                "bundle-sections",
                "bundle-inline-css",
                "bundle-router",
                "bundle-script-moved",
            ],
            [MockOutputSut.DesignDoc] =
            [
                "doc-structure",
                "doc-title",
                "doc-mermaid",
                "doc-items",
                "doc-crud",
                "doc-escaping",
                "doc-line-endings",
            ],
            [MockOutputSut.ResumePrompt] =
            [
                "resume-contents",
                "resume-entities",
                "resume-schema-note",
                "resume-revision-time",
                "resume-revision-limit",
            ],
            [MockOutputSut.SchemaChange] = ["change-same", "change-newline", "change-difference"],
            [MockOutputSut.SchemaMismatch] =
            [
                "mismatch-same",
                "mismatch-tables",
                "mismatch-content",
                "mismatch-no-source",
                "mismatch-split",
            ],
            [MockOutputSut.Load] =
            [
                "load-missing",
                "load-corrupt",
                "load-newer",
                "load-invalid-file-name",
                "load-legacy",
                "load-already-exists",
            ],
        };

    /// <summary>対象に当てはまる観点</summary>
    public static IReadOnlyList<string> AspectsOf(MockOutputSut sut)
    {
        var aspects = new List<string>(OwnAspects[sut]);

        if (sut is MockOutputSut.ScreenLinks or MockOutputSut.Validator or MockOutputSut.Bundle)
        {
            aspects.AddRange(LinkNotations);
        }

        if (sut is MockOutputSut.Validator)
        {
            aspects.AddRange(LinkTargets);
        }

        if (sut is MockOutputSut.ScreenLinks or MockOutputSut.DesignDoc or MockOutputSut.Bundle)
        {
            // フォルダつきの名前は画面として保存できない（区切りは PathSegments.CheckSegment が断る。
            // src/QuickER.AI.Mock/MockFolderStore.cs:693-712）ので、ストアを入力にする設計書・単一 HTML には起こり得ない
            aspects.AddRange(
                ScreenIdShapes.Where(shape =>
                    sut is MockOutputSut.ScreenLinks || shape != "id-folder"
                )
            );
        }

        return aspects;
    }

    /// <summary>次元表</summary>
    public static DimensionTable Table =>
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Sut,
                    Enum.GetValues<MockOutputSut>()
                        .Select(sut => new Level(sut.ToString()))
                        .ToList(),
                    "出力・検証の対象"
                ),
                new Factor(
                    Aspect,
                    Enum.GetValues<MockOutputSut>()
                        .SelectMany(AspectsOf)
                        .Distinct(StringComparer.Ordinal)
                        .Select(aspect => new Level(aspect))
                        .ToList(),
                    "入力の形・確かめる観点（リンクの表記・リンク先の宣言・画面 ID の元の名前・対象ごとの観点）"
                ),
            ],
            isValid: assignment =>
                !assignment.TryGetValue(Sut, out var sut)
                || !assignment.TryGetValue(Aspect, out var aspect)
                || AspectsOf(Enum.Parse<MockOutputSut>(sut))
                    .Contains(aspect, StringComparer.Ordinal)
        );

    /// <summary>網羅しない組の一覧（手で書いた静的な一覧）</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.Ai,
            [
                CoverageGap.Tuple(
                    Scope,
                    "P3-11b",
                    "P4",
                    "再開のプロンプトは改訂を直近 5 件に絞る（src/QuickER.AI.Mock/MockResumePrompt.cs:19・:172-174）が、6 件以上の改訂を流す元のテストが無い",
                    (Sut, nameof(MockOutputSut.ResumePrompt)),
                    (Aspect, "resume-revision-limit")
                ),
            ]
        );

    /// <summary>1 行を数える</summary>
    public static void Observe(CoverageCounter counter, MockOutputScenario scenario)
    {
        foreach (var (sut, aspect) in scenario.Observations)
        {
            counter.Observe(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Sut] = sut.ToString(),
                    [Aspect] = aspect,
                }
            );
        }
    }

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MockOutputs",
            "モックフォルダの出力の入力→出力の表（リンクの表記と画面 ID を検証器・単一 HTML・設計書へ流す・再開のプロンプト・スキーマの食い違い・読み込みの検証）",
            Table,
            MockOutputsRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsContext.cs",
                "tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsDimensions.cs",
                "tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsRows.cs",
                "tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsRows.Doc.cs",
                "tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsRows.Prompt.cs",
                "tests/QuickER.Tests/AI.Mock/Outputs/MockOutputsTests.cs",
            ],
            [
                "src/QuickER.AI.Mock/MockScreenLinks.cs",
                "src/QuickER.AI.Mock/MockContentValidator.cs",
                "src/QuickER.AI.Mock/MockBundleExporter.cs",
                "src/QuickER.AI.Mock/MockDesignDocExporter.cs",
                "src/QuickER.AI.Mock/MockResumePrompt.cs",
                "src/QuickER.AI.Mock/MockSchemaMismatch.cs",
                "src/QuickER.AI.Mock/MockFolderStore.cs",
            ]
        );
}
