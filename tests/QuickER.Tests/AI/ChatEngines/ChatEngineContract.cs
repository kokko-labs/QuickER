using AwesomeAssertions;

namespace QuickER.Tests.AI.ChatEngines;

/// <summary>契約の行を流す 1 ケースの文脈（アダプタ・組・取り消し）</summary>
/// <param name="Adapter">アダプタ（期待の文の組み方と性質の宣言）</param>
/// <param name="Rig">組</param>
/// <param name="CancellationToken">テストの取り消し</param>
internal sealed record ChatContractContext(
    ChatEngineAdapter Adapter,
    ChatEngineRig Rig,
    CancellationToken CancellationToken
)
{
    /// <summary>非同期に満たされる条件を、上限まで待つ（中断がバックエンドへ届くまでなど）</summary>
    /// <param name="condition">条件</param>
    /// <param name="because">満たされなかったときの説明</param>
    public async Task EventuallyAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, CancellationToken);
        }

        condition().Should().BeTrue(because);
    }

    /// <summary>
    /// バックエンドが受け取ったツールの結果を 1 件だけ表明する（成否は運ぶバックエンドでだけ見る。
    /// <see cref="ChatEngineCapability.ToolReturnCarriesSuccess"/>）
    /// </summary>
    /// <param name="success">期待する成否</param>
    /// <returns>受け取った結果</returns>
    public ChatToolReturn SingleToolReturn(bool success)
    {
        var toolReturn = Rig.ToolReturns.Should().ContainSingle().Subject;

        if (Adapter.Has(ChatEngineCapability.ToolReturnCarriesSuccess))
        {
            toolReturn.Success.Should().Be(success);
        }

        return toolReturn;
    }

    /// <summary>ターンの完了を 1 件だけ表明する</summary>
    /// <param name="success">期待する成否</param>
    /// <param name="error">期待するエラーの文（null は文が無いこと）</param>
    public void SingleCompletion(bool success, string? error)
    {
        var completion = Rig.Events.Completions.Should().ContainSingle().Subject;
        completion.Success.Should().Be(success);
        completion.Error.Should().Be(error);
    }
}

/// <summary>契約の行の入力（観点・前提の性質・ツールホスト・本体）</summary>
/// <param name="Contract">観点（turn / tool / cancel / failure など）</param>
/// <param name="Requires">前提にする性質（持たないエンジンでは流さない）</param>
/// <param name="Host">エンジンへ渡すツールホスト</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record ChatContractScenario(
    string Contract,
    IReadOnlyList<ChatEngineCapability> Requires,
    ChatToolHostKind Host,
    Func<ChatContractContext, Task> Body
)
{
    /// <summary>このアダプタで流すか（前提の性質をすべて持つ）</summary>
    /// <param name="adapter">アダプタ</param>
    public bool AppliesTo(ChatEngineAdapter adapter) => Requires.All(adapter.Has);
}
