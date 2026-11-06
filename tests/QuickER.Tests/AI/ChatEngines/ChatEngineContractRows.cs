using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>チャットエンジンの契約の行（行 × 軸の全エンジンで流す）</summary>
/// <remarks>
/// 行は元のテストの入力と期待をそのまま持つ。エンジンごとの期待の違いはアダプタの宣言から組み、
/// 行の中でエンジンを名指ししない。前提の性質を持たないエンジンでは流さない（網羅しない組の Accepted に載る）。
/// 主題ごとの行は <c>ChatEngineContractRows.{主題}.cs</c> に置く。
/// </remarks>
internal static partial class ChatEngineContractRows
{
    /// <summary>行の表</summary>
    public static ScenarioTable<ChatContractScenario> Table =>
        new(
            EngineIds.Ai,
            [
                .. Turn(),
                .. Tool(),
                .. Cancel(),
                .. Failure(),
                .. Attachment(),
                .. Disposal(),
                .. Ready(),
                .. Conversation(),
            ]
        );

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id（{観点}-{守っていること}）</param>
    /// <param name="description">説明</param>
    /// <param name="host">エンジンへ渡すツールホスト</param>
    /// <param name="body">流して表明する本体</param>
    /// <param name="requires">前提にする性質</param>
    private static ScenarioRow<ChatContractScenario> Row(
        string id,
        string description,
        ChatToolHostKind host,
        Func<ChatContractContext, Task> body,
        params ChatEngineCapability[] requires
    ) =>
        new(
            id,
            null,
            description,
            new ChatContractScenario(id[..id.IndexOf('-')], requires, host, body)
        );
}
