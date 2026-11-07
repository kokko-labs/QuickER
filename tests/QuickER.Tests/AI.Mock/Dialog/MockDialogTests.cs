using System.Reflection;
using AwesomeAssertions;
using CommunityToolkit.Mvvm.Input;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.Mock.Dialog;

/// <summary>
/// モック生成のダイアログの操作の列の表（状態 × 手の列 → 可否・メッセージ・画面一覧・出力・生成・破棄）と、
/// どの手の後にも掛ける不変条件
/// </summary>
public sealed class MockDialogTests
{
    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => MockDialogRows.Table.Ids();

    [Theory(
        DisplayName = "モック生成のダイアログの表：手の列の後の状態が期待どおりで、不変条件を破らない"
    )]
    [MemberData(nameof(Ids))]
    public async Task Row_Holds(string id)
    {
        var row = MockDialogRows.Table[id];
        using var harness = new MockDialogHarness();

        await row.Input.Body(harness);
        await harness.FinishAsync();
    }

    /// <remarks>
    /// 不変条件 12 の自己検査（ガードの数え漏れで静かに空振りしないため。行ではなく組の部品を見るので表の外に置く）。
    /// 集めたコマンドが VM の [RelayCommand] のメソッドと 1 対 1 で、偽の型にコマンドを足すとその分だけ増えることを見る。
    /// </remarks>
    [Fact(
        DisplayName = "不変条件 12 の対象：VM の [RelayCommand] のコマンドを全部集め、コマンドを足すと自動で増える"
    )]
    public void RefusedCommands_CollectEveryCommand()
    {
        using var harness = new MockDialogHarness();
        var vm = harness.Create();

        var expected = typeof(QuickER.AI.Mock.MockGenerationDialogViewModel)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<RelayCommandAttribute>() is not null)
            .Select(method =>
                (
                    method.Name.EndsWith("Async", StringComparison.Ordinal)
                        ? method.Name[..^"Async".Length]
                        : method.Name
                ) + "Command"
            )
            .Order(StringComparer.Ordinal)
            .ToList();

        expected.Should().NotBeEmpty("[RelayCommand] がメタデータに残っていないと照合が空振りする");
        MockDialogRefusedCommands
            .Collect(vm)
            .Select(command => command.Name)
            .Should()
            .Equal(expected);

        MockDialogRefusedCommands
            .Collect(new OneCommand())
            .Select(command => command.Name)
            .Should()
            .Equal(nameof(OneCommand.FirstCommand));
        MockDialogRefusedCommands
            .Collect(new TwoCommands())
            .Select(command => command.Name)
            .Should()
            .Equal(nameof(TwoCommands.FirstCommand), nameof(TwoCommands.SecondCommand));
    }

    /// <summary>自己検査の偽の型（コマンド 1 本とコマンドでないプロパティ）</summary>
    private class OneCommand
    {
        public IRelayCommand FirstCommand { get; } = new RelayCommand(() => { });

        public string NotACommand { get; } = string.Empty;
    }

    /// <summary>自己検査の偽の型（<see cref="OneCommand"/> にコマンドを 1 本足したもの）</summary>
    private sealed class TwoCommands : OneCommand
    {
        public IAsyncRelayCommand SecondCommand { get; } =
            new AsyncRelayCommand(() => Task.CompletedTask);
    }

    [Fact(DisplayName = "網羅：ダイアログの状態 × 手の種類")]
    public void Coverage_MatchesGaps()
    {
        var counter = new CoverageCounter(MockDialogDimensions.Table);

        foreach (var row in MockDialogRows.Table)
        {
            foreach (var (state, op) in row.Input.Touches)
            {
                MockDialogDimensions.Observe(counter, state, op);
            }
        }

        counter.Report().AssertGaps(MockDialogDimensions.Gaps, MockDialogDimensions.Scope);
    }
}
