using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>
/// モックプロジェクト生成の実行器 4 種（API キー・Claude Code・Codex・Copilot）の共通の契約を、同じ行で全実行器へ流す網
/// </summary>
/// <remarks>
/// 実の実行器＋既存のテストダブルで組み、中立の台本をアダプタが各バックエンドの入口へ訳す。
/// 実行器ごとの違いはアダプタの宣言（性質と値と出典・期待の組み方）が持つ。
/// 揃わない組が出たら、既知の失敗の一覧へ載せずに止める（製品の違いか不具合かもしれない）。
/// </remarks>
public sealed class MockAgentContractTests
{
    /// <summary>行 × 前提の性質を持つ実行器</summary>
    public static TheoryData<string, string> Cases() => MockAgentDimensions.Cases();

    [Theory(DisplayName = "契約の行：実行器ごとに同じ入力で同じ契約を守る")]
    [MemberData(nameof(Cases))]
    public async Task Row_Holds(string id, string agent)
    {
        var row = MockAgentContractRows.Table[id];
        var adapter = MockAgentAxis.Axis.Get(agent);
        await using var rig = adapter.CreateRig(row.Input.Options);

        await row.Input.Body(
            new MockAgentContext(adapter, rig, TestContext.Current.CancellationToken)
        );
    }

    [Fact(
        DisplayName = "網羅：実行器 × 契約の行（前提の性質を持たない組と、直す区切りの決まった組だけを理由つきで外す）"
    )]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockAgentDimensions.Table);

        foreach (var (id, agent) in MockAgentDimensions.CaseList())
        {
            MockAgentDimensions.Observe(counter, id, agent);
        }

        counter.Report().AssertGaps(MockAgentDimensions.Gaps, MockAgentDimensions.Scope);
    }

    [Fact(DisplayName = "実行器の軸：IMockProjectAgent の実装ごとにアダプタがちょうど 1 つ")]
    public void Axis_CoversAuthoritativeValues()
    {
        MockAgentAxis
            .Axis.Adapters.Select(adapter => adapter.AxisValue)
            .Should()
            .Equal(MockAgentAxis.Authoritative);
        MockAgentAxis.Axis.Excluded.Should().BeEmpty();
        MockAgentAxis.Authoritative.Should().HaveCount(4);
    }

    [Fact(
        DisplayName = "アダプタの宣言：全性質と値を、実在する src のファイルの出典つきで宣言する"
    )]
    public void Declarations_CoverEveryCapabilityWithEvidence()
    {
        foreach (var adapter in MockAgentAxis.Axis.Adapters)
        {
            adapter
                .Declarations.Keys.Should()
                .BeEquivalentTo(Enum.GetValues<MockAgentCapability>(), adapter.AxisValue);

            var evidences = adapter
                .Declarations.Select(pair => (Name: pair.Key.ToString(), pair.Value.Evidence))
                .Append((nameof(MockAgentAdapter.BackendName), adapter.BackendName.Evidence))
                .Append(
                    (
                        nameof(MockAgentAdapter.FinalBuildEscapesSandbox),
                        adapter.FinalBuildEscapesSandbox.Evidence
                    )
                );

            // 出典は「src/…:行」で始め、先頭のパス（最初の : か空白か全角の括弧まで）が実在する
            // （ファイルの名前が変わったときに出典が嘘になるのを止める）
            foreach (var (name, evidence) in evidences)
            {
                evidence.Should().StartWith("src/", $"{adapter.AxisValue} の {name} の出典");
                evidence
                    .Should()
                    .MatchRegex(
                        @"^src/[^:]+:[0-9]",
                        $"{adapter.AxisValue} の {name} の出典は行を持つ"
                    );
                var path = evidence.Split([':', ' ', '（'])[0];
                File.Exists(Path.Combine(SourceFiles.RepositoryRoot, path))
                    .Should()
                    .BeTrue($"{adapter.AxisValue} の {name} の出典 {path} が実在する");
            }
        }
    }
}
