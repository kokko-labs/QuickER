using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表と文言の表の次元表・台帳の行</summary>
/// <remarks>
/// 網羅は判定そのものの自己検査の行で数える：照合の表は比べ方 × 結果（一致・食い違い）、文言の表は種類（英語・必須の語）× 結果
/// （違反なし・違反あり）。どの比べ方・判定も、一致を通して食い違いを拾うことを確かめた行がある。製品の仕様の軸は持たない。
/// </remarks>
internal static class GuardTextDimensions
{
    /// <summary>照合の表の網羅しない組の照合の範囲</summary>
    public const string ParityScope = "guard-parity";

    /// <summary>文言の表の網羅しない組の照合の範囲</summary>
    public const string PhraseScope = "guard-phrases";

    /// <summary>比べ方</summary>
    public const string Compare = "Compare";

    /// <summary>文言の判定の種類</summary>
    public const string Kind = "Kind";

    /// <summary>結果</summary>
    public const string Outcome = "Outcome";

    /// <summary>一致する・違反なし</summary>
    public const string Agrees = "Agrees";

    /// <summary>食い違う・違反あり</summary>
    public const string Disagrees = "Disagrees";

    /// <summary>英語の文言</summary>
    public const string English = "English";

    /// <summary>必須の文言</summary>
    public const string Phrase = "Phrase";

    /// <summary>照合の表の次元表</summary>
    public static DimensionTable ParityTable { get; } =
        new(
            EngineIds.Guard,
            [
                new Factor(
                    Compare,
                    [
                        .. Enum.GetValues<ParityCompare>()
                            .Select(compare => new Level(compare.ToString())),
                    ],
                    "比べ方"
                ),
                new Factor(Outcome, [new Level(Agrees), new Level(Disagrees)], "結果"),
            ]
        );

    /// <summary>文言の表の次元表</summary>
    public static DimensionTable PhraseTable { get; } =
        new(
            EngineIds.Guard,
            [
                new Factor(Kind, [new Level(English), new Level(Phrase)], "判定の種類"),
                new Factor(Outcome, [new Level(Agrees), new Level(Disagrees)], "結果"),
            ]
        );

    /// <summary>照合の表の台帳の行</summary>
    public static EngineDescriptor ParityDescriptor =>
        new(
            EngineIds.Guard,
            "Parity",
            "照合の表（正本の一覧と写しの一覧の比べ方 × 結果。docs・ワークフロー・resx・設定のキー・パッケージの版）",
            ParityTable,
            ParityRows.All.Count + ParityTests.ComparerProbes.Length,
            [
                "tests/QuickER.Tests/TestSupport/Guards/GuardTextDimensions.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityRow.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityRows.AiMock.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityRows.CodeGen.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityRows.Docs.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityRows.Resources.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityRows.Workflow.cs",
                "tests/QuickER.Tests/TestSupport/Guards/ParityTests.cs",
            ],
            ["docs", ".github/workflows", "src"]
        );

    /// <summary>文言の表の台帳の行</summary>
    public static EngineDescriptor PhraseDescriptor =>
        new(
            EngineIds.Guard,
            "Phrases",
            "文言の表（英語の文言〔CJK が無い〕と必須の語 × 結果。resx・サンプル・ツール定義・プロンプト・フォーマッタの出力）",
            PhraseTable,
            EnglishTextRows.All.Count + PhraseRows.All.Count + PhraseTests.JudgeProbes.Length,
            [
                "tests/QuickER.Tests/TestSupport/Guards/GuardTextDimensions.cs",
                "tests/QuickER.Tests/TestSupport/Guards/PhraseRow.cs",
                "tests/QuickER.Tests/TestSupport/Guards/EnglishTextRows.AiChat.cs",
                "tests/QuickER.Tests/TestSupport/Guards/EnglishTextRows.AiMock.cs",
                "tests/QuickER.Tests/TestSupport/Guards/EnglishTextRows.CodeGen.cs",
                "tests/QuickER.Tests/TestSupport/Guards/EnglishTextRows.Mcp.cs",
                "tests/QuickER.Tests/TestSupport/Guards/EnglishTextRows.Resources.cs",
                "tests/QuickER.Tests/TestSupport/Guards/EnglishTextRows.Samples.cs",
                "tests/QuickER.Tests/TestSupport/Guards/PhraseRows.AiChat.cs",
                "tests/QuickER.Tests/TestSupport/Guards/PhraseRows.AiMock.cs",
                "tests/QuickER.Tests/TestSupport/Guards/PhraseTests.cs",
            ],
            ["src", "samples"]
        );
}
