using System.Net.Http;
using QuickER.Tests.TestSupport.Engines;
using static QuickER.Tests.AI.ChatEngines.ChatEngineCapability;

namespace QuickER.Tests.AI.ChatEngines;

internal static partial class ChatEngineContractRows
{
    /// <summary>失敗の分類と文（何が起きたかの要約に、原因を「詳細: …」の行で添える）</summary>
    private static IEnumerable<ScenarioRow<ChatContractScenario>> Failure() =>
        [
            Row(
                "failure-reported-is-summary-with-detail",
                "バックエンドが報告したターンの失敗は、要約＋詳細の文で 1 回だけ完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.ReportedFailure("rate limited")),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, c.Adapter.ExpectedReportedFailure("rate limited"));
                },
                ReportedFailure
            ),
            Row(
                "failure-reported-without-detail-is-summary-only",
                "理由の無いターンの失敗の報告は、要約だけの文で完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.ReportedFailure(null)),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, c.Adapter.ExpectedReportedFailure(null));
                },
                ReportedFailure
            ),
            Row(
                "failure-send-throws-is-reported",
                "送信が例外で終わったら、その例外から組んだ失敗の文で 1 回完了する",
                ChatToolHostKind.None,
                async c =>
                {
                    var failure = new HttpRequestException("connection refused");

                    await c.Rig.PlayTurnAsync(
                        "やあ",
                        ChatTurnScript.Of(new ChatTurnEnding.Throws(failure)),
                        c.CancellationToken
                    );

                    c.SingleCompletion(false, c.Adapter.ExpectedFailureFromException(failure));
                }
            ),
        ];
}
