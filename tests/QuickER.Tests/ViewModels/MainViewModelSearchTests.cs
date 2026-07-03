using System.Collections.Generic;
using FluentAssertions;
using QuickER.Model;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>
/// <see cref="MainViewModel"/> のエンティティ検索（Ctrl+F オーバーレイ）ロジックを検証するテストクラス
/// </summary>
/// <remarks>WPF UI スレッドに依存しない絞り込み・巡回・開閉ロジックのみを対象とする（視点適用は View 側の責務）</remarks>
public class MainViewModelSearchTests
{
    /// <summary>テーブル名・カラムを持つエンティティを VM に直接投入する</summary>
    private static EntityViewModel AddEntity(
        MainViewModel vm,
        string tableName,
        params string[] columnNames
    )
    {
        var model = new Entity
        {
            TableName = tableName,
            Columns = columnNames.Select(name => new Column { Name = name }).ToList(),
        };
        var entity = new EntityViewModel(model);
        vm.Entities.Add(entity);

        return entity;
    }

    /// <summary>3 エンティティを持つ標準的な図を組み立てる</summary>
    private static MainViewModel BuildSampleDiagram()
    {
        var vm = new MainViewModel();
        AddEntity(vm, "Customer", "CustomerId", "Name");
        AddEntity(vm, "Order", "OrderId", "CustomerId", "OrderDate");
        AddEntity(vm, "Product", "ProductId", "Price");

        return vm;
    }

    /// <summary>テーブル名の部分一致で絞り込めることを検証する</summary>
    [Fact(DisplayName = "検索: テーブル名の部分一致で絞り込む")]
    public void Search_MatchesTableNameSubstring()
    {
        var vm = new MainViewModel();
        AddEntity(vm, "Customer", "CustomerId", "Name");
        AddEntity(vm, "Product", "ProductId", "Price");

        vm.SearchQuery = "custo";

        vm.SearchResults.Should().ContainSingle();
        vm.SearchResults[0].DisplayText.Should().Be("Customer");
    }

    /// <summary>カラム名一致は「テーブル名 (カラム名)」で表記されることを検証する</summary>
    [Fact(DisplayName = "検索: カラム名一致は『テーブル名 (カラム名)』で表記")]
    public void Search_MatchesColumnName_UsesQualifiedDisplay()
    {
        var vm = BuildSampleDiagram();

        vm.SearchQuery = "date";

        vm.SearchResults.Should().ContainSingle();
        vm.SearchResults[0].DisplayText.Should().Be("Order (OrderDate)");
    }

    /// <summary>大文字小文字を無視して一致することを検証する</summary>
    [Fact(DisplayName = "検索: 大文字小文字を無視する")]
    public void Search_IsCaseInsensitive()
    {
        var vm = BuildSampleDiagram();

        vm.SearchQuery = "PRODUCT";

        vm.SearchResults.Should().ContainSingle();
        vm.SearchResults[0].DisplayText.Should().Be("Product");
    }

    /// <summary>複数カラムが一致しても候補は 1 エンティティにつき 1 件で最初のカラムのみ表記することを検証する</summary>
    [Fact(DisplayName = "検索: 複数カラム一致でも 1 件・最初のカラムのみ表記")]
    public void Search_MultipleColumnMatches_ProducesSingleResultWithFirstColumn()
    {
        var vm = new MainViewModel();
        AddEntity(vm, "Ledger", "AccountId", "AccountName", "AccountType");

        vm.SearchQuery = "account";

        vm.SearchResults.Should().ContainSingle();
        vm.SearchResults[0].DisplayText.Should().Be("Ledger (AccountId)");
    }

    /// <summary>テーブル名一致とカラム名一致が同時に成立する場合、テーブル名表記が優先されることを検証する</summary>
    [Fact(DisplayName = "検索: テーブル名一致を優先表記する")]
    public void Search_TableNameMatchTakesPrecedenceOverColumn()
    {
        var vm = new MainViewModel();
        // "Order" はテーブル名にも子の "OrderId" にも一致するが、表記はテーブル名優先
        AddEntity(vm, "Order", "OrderId", "Total");

        vm.SearchQuery = "order";

        vm.SearchResults.Should().ContainSingle();
        vm.SearchResults[0].DisplayText.Should().Be("Order");
    }

    /// <summary>一致なしのとき結果 0 件・件数表示 "0/0" になることを検証する</summary>
    [Fact(DisplayName = "検索: 一致なしは 0 件・件数 0/0")]
    public void Search_NoMatch_YieldsEmptyResults()
    {
        var vm = BuildSampleDiagram();

        vm.SearchQuery = "zzz";

        vm.SearchResults.Should().BeEmpty();
        vm.MatchCountText.Should().Be("0/0");
    }

    /// <summary>絞り込み時に最初の一致へジャンプ（選択更新＋ScrollToEntityRequested 発火）することを検証する</summary>
    [Fact(DisplayName = "検索: 絞り込みで最初の一致へジャンプ")]
    public void Search_JumpsToFirstMatch_OnQueryChange()
    {
        var vm = BuildSampleDiagram();
        var jumped = new List<EntityViewModel>();
        vm.ScrollToEntityRequested += (_, entity) => jumped.Add(entity);

        vm.SearchQuery = "id"; // 全 3 エンティティのカラムに一致

        vm.SearchResults.Should().HaveCount(3);
        vm.MatchCountText.Should().Be("1/3");
        vm.SelectedEntity.Should().Be(vm.SearchResults[0].Entity);
        jumped.Should().ContainSingle().Which.Should().Be(vm.SearchResults[0].Entity);
    }

    /// <summary>Enter 相当（GoToNextMatch）で次候補へ進み、末尾で先頭へラップすることを検証する</summary>
    [Fact(DisplayName = "巡回: 次候補へ進み末尾で先頭へラップ")]
    public void GoToNextMatch_CyclesAndWraps()
    {
        var vm = BuildSampleDiagram();
        var jumped = new List<EntityViewModel>();
        vm.ScrollToEntityRequested += (_, entity) => jumped.Add(entity);

        vm.SearchQuery = "id"; // 3 件一致・初回ジャンプで 1 回発火
        jumped.Clear();

        vm.GoToNextMatchCommand.Execute(null);
        vm.MatchCountText.Should().Be("2/3");
        vm.SelectedEntity.Should().Be(vm.SearchResults[1].Entity);

        vm.GoToNextMatchCommand.Execute(null);
        vm.MatchCountText.Should().Be("3/3");
        vm.SelectedEntity.Should().Be(vm.SearchResults[2].Entity);

        // 末尾の次は先頭へラップする
        vm.GoToNextMatchCommand.Execute(null);
        vm.MatchCountText.Should().Be("1/3");
        vm.SelectedEntity.Should().Be(vm.SearchResults[0].Entity);

        // 巡回のたびにジャンプ要求が発火する
        jumped.Should().HaveCount(3);
    }

    /// <summary>候補選択（クリック相当）でそのエンティティへジャンプすることを検証する</summary>
    [Fact(DisplayName = "巡回: 候補選択でそのエンティティへジャンプ")]
    public void SelectedSearchResult_JumpsToChosenEntity()
    {
        var vm = BuildSampleDiagram();
        var jumped = new List<EntityViewModel>();
        vm.SearchQuery = "id";
        vm.ScrollToEntityRequested += (_, entity) => jumped.Add(entity);

        vm.SelectedSearchResult = vm.SearchResults[2];

        vm.MatchCountText.Should().Be("3/3");
        vm.SelectedEntity.Should().Be(vm.SearchResults[2].Entity);
        jumped.Should().ContainSingle().Which.Should().Be(vm.SearchResults[2].Entity);
    }

    /// <summary>OpenSearch で表示され、既存クエリが再評価されることを検証する</summary>
    [Fact(DisplayName = "開閉: Open で表示＋既存クエリ再評価")]
    public void OpenSearch_ShowsOverlayAndReevaluatesQuery()
    {
        var vm = new MainViewModel();
        AddEntity(vm, "Customer", "CustomerId");
        vm.SearchQuery = "custo";
        vm.SearchResults.Should().ContainSingle();

        // 開く前にエンティティを追加しても、Open で再評価すれば反映される
        AddEntity(vm, "CustomerSegment", "SegmentId");

        vm.OpenSearchCommand.Execute(null);

        vm.IsSearchOverlayVisible.Should().BeTrue();
        vm.SearchResults.Should().HaveCount(2);
    }

    /// <summary>CloseSearch で非表示になることを検証する</summary>
    [Fact(DisplayName = "開閉: Close で非表示")]
    public void CloseSearch_HidesOverlay()
    {
        var vm = BuildSampleDiagram();
        vm.OpenSearchCommand.Execute(null);

        vm.CloseSearchCommand.Execute(null);

        vm.IsSearchOverlayVisible.Should().BeFalse();
    }

    /// <summary>空クエリでは結果が空・件数 0/0 になることを検証する</summary>
    [Fact(DisplayName = "検索: 空クエリは 0 件・件数 0/0")]
    public void Search_EmptyQuery_YieldsEmptyResults()
    {
        var vm = BuildSampleDiagram();
        vm.SearchQuery = "cust";
        vm.SearchResults.Should().NotBeEmpty();

        vm.SearchQuery = string.Empty;

        vm.SearchResults.Should().BeEmpty();
        vm.MatchCountText.Should().Be("0/0");
    }
}
