using System.Collections.Generic;
using FluentAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.CodeGen.UI;

/// <summary>
/// <see cref="QueryDefinitionCommandService"/> が、ダイアログの確定結果でホストのクエリを差し替え、
/// キャンセル時は何もしないことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <c>MainViewModel</c> 由来の旧テスト（MainViewModelQueriesTests の OpenQueryDefinitions 系）を、
/// フィーチャーモジュール側サービスへ移植したもの。ホストは <see cref="StubErDiagramHost"/>、
/// ダイアログ提示はテスト内フェイクに差し替える。
/// </remarks>
public class QueryDefinitionCommandServiceTests
{
    /// <summary>ダイアログの確定結果で <see cref="IErDiagramHost.ReplaceQueries"/> が呼ばれることを検証する</summary>
    [Fact(DisplayName = "クエリ定義ダイアログの結果でホストのクエリが差し替わる")]
    public void Run_ReplacesQueriesWithDialogResult()
    {
        var replacement = new List<QueryDefinition> { new() { Name = "FromDialog" } };
        var host = new StubErDiagramHost();
        var presenter = new FakeQueryPresenter(replacement);
        var service = new QueryDefinitionCommandService(host, presenter);

        service.Run();

        presenter.ShowCallCount.Should().Be(1);
        host.LastReplacedQueries.Should().BeSameAs(replacement);
    }

    /// <summary>キャンセル（null 返却）ではクエリを差し替えないことを検証する</summary>
    [Fact(DisplayName = "クエリ定義ダイアログのキャンセルではクエリを差し替えない")]
    public void Run_Cancel_DoesNotReplaceQueries()
    {
        var host = new StubErDiagramHost();
        var presenter = new FakeQueryPresenter(result: null);
        var service = new QueryDefinitionCommandService(host, presenter);

        service.Run();

        presenter.ShowCallCount.Should().Be(1);
        host.LastReplacedQueries.Should().BeNull();
    }

    /// <summary>指定した確定結果を返し、渡された図を記録するダイアログ提示フェイク</summary>
    private sealed class FakeQueryPresenter(List<QueryDefinition>? result)
        : IQueryDefinitionDialogPresenter
    {
        /// <summary><see cref="Show"/> が呼ばれた回数</summary>
        public int ShowCallCount { get; private set; }

        /// <summary>直近の <see cref="Show"/> に渡された図</summary>
        public ErDiagram? LastDiagram { get; private set; }

        public List<QueryDefinition>? Show(ErDiagram diagram)
        {
            ShowCallCount++;
            LastDiagram = diagram;
            return result;
        }
    }
}
