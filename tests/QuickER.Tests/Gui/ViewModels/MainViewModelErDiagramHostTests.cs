using System.Collections.Generic;
using AwesomeAssertions;
using QuickER.Extensibility;
using QuickER.Model;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Services;
using QuickER.SqlServer;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// <see cref="MainViewModelErDiagramHost"/> が <see cref="MainViewModel"/> へ各操作を委譲し、
/// ツール実行の引数 JSON 不正時に固定エラー文言を返すことを検証するテストクラス。
/// </summary>
/// <remarks>
/// 旧 <c>ErDiagramToolHost</c> が持っていた「引数 JSON 不正時のエラー文言」「実行後の
/// RefreshCanvasSize」の検証内容を、本ホストへ移植している（実 MainViewModel を用いる）。
/// </remarks>
public class MainViewModelErDiagramHostTests
{
    /// <summary>IsEmpty が VM のエンティティ有無を反映することを検証する</summary>
    [Fact(DisplayName = "IsEmpty は VM のエンティティ有無を反映する")]
    public void IsEmpty_ReflectsViewModelEntities()
    {
        var vm = new MainViewModel();
        var host = new MainViewModelErDiagramHost(vm);

        host.IsEmpty.Should().BeTrue();

        vm.AddEntityCommand.Execute(null);

        host.IsEmpty.Should().BeFalse();
    }

    /// <summary>GetDiagram が現在の図を意味モデルとして返すことを検証する</summary>
    [Fact(DisplayName = "GetDiagram は現在の図を意味モデルで返す")]
    public void GetDiagram_ReturnsCurrentDiagram()
    {
        var vm = new MainViewModel();
        var host = new MainViewModelErDiagramHost(vm);
        host.ExecuteTool("add_entity", "{\"table_name\":\"Book\"}");

        var diagram = host.GetDiagram();

        diagram.Entities.Should().ContainSingle(entity => entity.TableName == "Book");
    }

    /// <summary>Providers が VM のプロバイダレジストリをそのまま返すことを検証する</summary>
    [Fact(DisplayName = "Providers は VM のレジストリへ委譲する")]
    public void Providers_DelegatesToViewModel()
    {
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider() }
        );
        var vm = new MainViewModel(providers: registry);
        var host = new MainViewModelErDiagramHost(vm);

        host.Providers.Should().BeSameAs(vm.Providers);
    }

    /// <summary>AutoArrangeNewDiagram が VM の整列を起動することを検証する</summary>
    [Fact(DisplayName = "AutoArrangeNewDiagram は VM の整列へ委譲する")]
    public void AutoArrangeNewDiagram_DelegatesToViewModel()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var host = new MainViewModelErDiagramHost(vm);

        host.AutoArrangeNewDiagram();

        // 格子レイアウトの左上余白へ整列される（VM 直接呼び出しと同じ結果）
        vm.Entities[0].X.Should().Be(40);
        vm.Entities[0].Y.Should().Be(40);
    }

    /// <summary>引数 JSON が壊れているとき、リソース由来のエラー文言と失敗を返すことを検証する</summary>
    [Fact(DisplayName = "ExecuteTool は不正 JSON でエラー文言を返す")]
    public void ExecuteTool_InvalidJson_ReturnsError()
    {
        var vm = new MainViewModel();
        var host = new MainViewModelErDiagramHost(vm);

        var (result, success) = host.ExecuteTool("add_entity", "{ not json");

        success.Should().BeFalse();

        // 文言は UI 言語追従のため resx 参照で照合（実行環境のカルチャに依存しない）
        result
            .Should()
            .Be(string.Format(QuickER.Resources.Strings.Tool_InvalidArgumentsJson, "add_entity"));
    }

    /// <summary>正常な引数では ErDiagramDynamicTools 経由でツールが実行されることを検証する</summary>
    [Fact(DisplayName = "ExecuteTool は正常系で ErDiagramDynamicTools を実行する")]
    public void ExecuteTool_ValidTool_ExecutesViaDynamicTools()
    {
        var vm = new MainViewModel();
        var host = new MainViewModelErDiagramHost(vm);

        var (_, success) = host.ExecuteTool("add_entity", "{\"table_name\":\"Book\"}");

        success.Should().BeTrue();
        vm.Entities.Should().ContainSingle(entity => entity.TableName == "Book");
    }

    /// <summary>ReplaceQueries が VM の名前付きクエリを差し替えることを検証する</summary>
    [Fact(DisplayName = "ReplaceQueries は VM の Queries を差し替える")]
    public void ReplaceQueries_ReplacesViewModelQueries()
    {
        var vm = new MainViewModel();
        var host = new MainViewModelErDiagramHost(vm);
        var replacement = new List<QueryDefinition> { new() { Name = "FromModule" } };

        host.ReplaceQueries(replacement);

        vm.Queries.Should().ContainSingle();
        vm.Queries[0].Name.Should().Be("FromModule");
    }

    /// <summary>ReplaceDiagram が図の TargetDbms を採用し、エンティティを置換することを検証する</summary>
    [Fact(DisplayName = "ReplaceDiagram は方言採用とエンティティ置換を行う")]
    public void ReplaceDiagram_AdoptsDialectAndReplacesEntities()
    {
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), new PostgreSqlProvider() }
        );
        var vm = new MainViewModel(providers: registry);
        var host = new MainViewModelErDiagramHost(vm);

        var diagram = new ErDiagram
        {
            TargetDbms = "postgresql",
            Entities = { new Entity { TableName = "Book" } },
        };

        host.ReplaceDiagram(diagram);

        // 図の方言（postgresql）が現在方言として採用される
        vm.CurrentProvider.Name.Should().Be("postgresql");
        // エンティティが丸ごと差し替えられる
        vm.Entities.Should().ContainSingle(entity => entity.TableName == "Book");
    }

    /// <summary>TargetDbms が現在プロバイダの名前を返すことを検証する</summary>
    [Fact(DisplayName = "TargetDbms は CurrentProvider.Name を返す")]
    public void TargetDbms_ReturnsCurrentProviderName()
    {
        var vm = new MainViewModel();
        var host = new MainViewModelErDiagramHost(vm);

        host.TargetDbms.Should().Be(vm.CurrentProvider.Name);
        host.TargetDbms.Should().Be("sqlserver");
    }

    /// <summary>方言が切り替わったとき、host の TargetDbmsChanged が中継発火することを検証する</summary>
    [Fact(DisplayName = "方言切替で host の TargetDbmsChanged が中継発火する")]
    public void TargetDbmsChanged_RelaysOnDialectSwitch()
    {
        var registry = new DatabaseProviderRegistry(
            new IDatabaseProvider[] { new SqlServerProvider(), new PostgreSqlProvider() }
        );
        var vm = new MainViewModel(providers: registry);
        var host = new MainViewModelErDiagramHost(vm);

        var raised = 0;
        host.TargetDbmsChanged += (_, _) => raised++;

        vm.SelectedProvider = registry.Get(PostgreSqlProvider.ProviderName);

        raised.Should().BeGreaterThan(0);
        host.TargetDbms.Should().Be("postgresql");
    }

    /// <summary>VM で列リネームが起きたとき、host の ColumnRenamed が正しい EntityId・新旧名で中継発火することを検証する</summary>
    [Fact(DisplayName = "列リネームで host の ColumnRenamed が中継発火する")]
    public void ColumnRenamed_RelaysFromViewModel()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var owner = vm.Entities[0];
        var column = owner.Columns[0];
        var oldName = column.Name;

        var host = new MainViewModelErDiagramHost(vm);
        ColumnRenamedEventArgs? received = null;
        host.ColumnRenamed += (_, e) => received = e;

        column.Name = "RenamedId";

        received.Should().NotBeNull();
        received!.EntityId.Should().Be(owner.Id);
        received.OldName.Should().Be(oldName);
        received.NewName.Should().Be("RenamedId");
    }
}
