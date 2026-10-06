using System.Collections.Generic;
using AwesomeAssertions;
using QuickER.CodeGen.UI;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
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

    /// <summary>
    /// ダイアログへ図の方言の型カタログを渡し（列で型付けした行を「なし」へ戻したときの型トークンの変換に使う）、
    /// 方言を解決できなければ null を渡すことを検証する
    /// </summary>
    [Fact(DisplayName = "クエリ定義ダイアログへ図の方言の型カタログを渡す")]
    public void Run_PassesTypeCatalogOfDiagramDialect()
    {
        var sqlServer = new SqlServerProvider();
        var host = new StubErDiagramHost
        {
            ProvidersToReturn = new DatabaseProviderRegistry([sqlServer]),
            DiagramToReturn = new ErDiagram { TargetDbms = SqlServerProvider.ProviderName },
        };
        var presenter = new FakeQueryPresenter(result: null);

        new QueryDefinitionCommandService(host, presenter).Run();

        presenter.LastTypeCatalog.Should().BeSameAs(sqlServer.TypeCatalog);

        var unknown = new StubErDiagramHost
        {
            ProvidersToReturn = new DatabaseProviderRegistry([sqlServer]),
            DiagramToReturn = new ErDiagram { TargetDbms = "unknown" },
        };

        new QueryDefinitionCommandService(unknown, presenter).Run();

        presenter.LastTypeCatalog.Should().BeNull();
    }

    /// <summary>指定した確定結果を返し、渡された図を記録するダイアログ提示フェイク</summary>
    private sealed class FakeQueryPresenter(List<QueryDefinition>? result)
        : IQueryDefinitionDialogPresenter
    {
        /// <summary><see cref="Show"/> が呼ばれた回数</summary>
        public int ShowCallCount { get; private set; }

        /// <summary>直近の <see cref="Show"/> に渡された図</summary>
        public ErDiagram? LastDiagram { get; private set; }

        /// <summary>直近の <see cref="Show"/> に渡された型カタログ</summary>
        public ITypeCatalog? LastTypeCatalog { get; private set; }

        public List<QueryDefinition>? Show(ErDiagram diagram, ITypeCatalog? typeCatalog)
        {
            ShowCallCount++;
            LastDiagram = diagram;
            LastTypeCatalog = typeCatalog;
            return result;
        }
    }
}
