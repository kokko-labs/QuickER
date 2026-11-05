using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Document;

internal static partial class DocumentFormatRows
{
    /// <summary>将来版の判定（読み込んだ図の IsNewerFormat・上書き前の IsNewerFormatFile）</summary>
    private static IEnumerable<ScenarioRow<FormatCase>> VersionRows() =>
        [
            Row(
                "roundtrip-newer-version-sets-is-newer-format",
                "Load: CurrentVersion より新しいフォーマットは IsNewerFormat が true（新しい版を書いて読む）",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Document = () =>
                        new DiagramDocument { Version = DiagramDocument.CurrentVersion + 1 },
                    Expect = o => o.Loaded!.IsNewerFormat.Should().BeTrue(),
                }
            ),
            Row(
                "roundtrip-current-version-over-newer-file-clears-is-newer-format",
                "Load: CurrentVersion より新しいフォーマットは IsNewerFormat が true（新しい版のファイルへ現行版を上書きして読む）",
                new FormatCase
                {
                    Stage = FormatStage.RoundTrip,
                    Result = FormatResult.Loaded,
                    Prior = () =>
                        new DiagramDocument { Version = DiagramDocument.CurrentVersion + 1 },
                    Document = () => new DiagramDocument(),
                    Expect = o => o.Loaded!.IsNewerFormat.Should().BeFalse(),
                }
            ),
            VersionProbeRow(
                "versionprobe-missing-file-is-not-newer",
                "null＝ファイル無し",
                null,
                false
            ),
            VersionProbeRow("versionprobe-not-json-is-not-newer", "not json", "not json", false),
            VersionProbeRow(
                "versionprobe-no-version-key-is-not-newer",
                "Version キー無し",
                "{\"Schema\":{}}",
                false
            ),
            VersionProbeRow(
                "versionprobe-current-version-is-not-newer",
                "Version が 1",
                "{\"Version\":1}",
                false
            ),
            VersionProbeRow(
                "versionprobe-future-number-is-newer",
                "Version が 2",
                "{\"Version\":2}",
                true
            ),
            VersionProbeRow(
                "versionprobe-future-string-is-newer",
                "Version が文字列の 2",
                "{\"Version\":\"2\"}",
                true
            ),
            VersionProbeRow(
                "versionprobe-future-float-is-newer",
                "Version が 2.0",
                "{\"Version\":2.0}",
                true
            ),
            VersionProbeRow(
                "versionprobe-null-version-is-newer",
                "Version が null",
                "{\"Version\":null}",
                true
            ),
            VersionProbeRow(
                "versionprobe-object-version-is-newer",
                "Version がオブジェクト",
                "{\"Version\":{}}",
                true
            ),
            VersionProbeRow(
                "versionprobe-array-version-is-newer",
                "Version が配列",
                "{\"Version\":[1]}",
                true
            ),
        ];

    /// <summary>版の判定の 1 行（content が null ならファイルを置かない）</summary>
    private static ScenarioRow<FormatCase> VersionProbeRow(
        string id,
        string shape,
        string? content,
        bool expected
    ) =>
        Row(
            id,
            $"IsNewerFormatFile: 将来版と解釈できない版だけを上書き注意の対象にする（{shape}）",
            new FormatCase
            {
                Stage = FormatStage.VersionProbe,
                Result = expected ? FormatResult.Newer : FormatResult.NotNewer,
                Json = content,
                Expect = o => o.IsNewer.Should().Be(expected),
            }
        );
}
