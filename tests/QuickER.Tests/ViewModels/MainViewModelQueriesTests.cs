using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using QuickER.CodeGen.UI;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>MainViewModel での名前付きクエリの往復・列リネーム追従・置換時の消去を検証するテストクラス</summary>
public class MainViewModelQueriesTests
{
    /// <summary>クエリ定義入りの文書を作り、一時ファイルへ保存してそのパスを返す（列 ID を out で受け取る）</summary>
    private static string SaveDocumentWithQuery(out Guid entityId, out Guid customerColumnId)
    {
        var entity = new Entity { TableName = "Order" };
        var customerColumn = new Column
        {
            Name = "CustomerId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        entity.Columns.Add(customerColumn);
        entity.Columns.Add(new Column { Name = "Amount", DataType = "decimal(12,2)" });

        entityId = entity.Id;
        customerColumnId = customerColumn.Id;

        var document = new DiagramDocument
        {
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { entity },
                Queries =
                {
                    new QueryDefinition
                    {
                        EntityId = entity.Id,
                        Name = "GetByCustomer",
                        Condition = "CustomerId = @customerId",
                        Parameters =
                        {
                            new QueryParameter { Name = "customerId", Type = "int32" },
                        },
                    },
                },
            },
        };

        var path = Path.Combine(Path.GetTempPath(), $"er-vm-query-{Guid.NewGuid()}.json");
        JsonStorageService.Save(path, document);
        return path;
    }

    /// <summary>クエリ定義入り文書を開くと VM がクエリを保持し、ToDocument で往復することを検証する</summary>
    [Fact(DisplayName = "Open → ToDocument で名前付きクエリが往復する")]
    public void Open_ThenToDocument_RoundTripsQueries()
    {
        var path = SaveDocumentWithQuery(out var entityId, out _);

        var vm = new MainViewModel(
            new StubDialogService(),
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);

            // 開いた直後、VM がクエリを保持している
            vm.Queries.Should().ContainSingle();
            vm.Queries[0].EntityId.Should().Be(entityId);
            vm.Queries[0].Condition.Should().Be("CustomerId = @customerId");

            // ToDocument（保存経路）にもクエリが残る
            var document = vm.ToDocument();
            document.Schema.Queries.Should().ContainSingle();
            document.Schema.Queries[0].Name.Should().Be("GetByCustomer");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>開いた後に列名を変更すると、その列を参照するクエリの条件が新名へ書き換わることを検証する</summary>
    [Fact(DisplayName = "列名変更で名前付きクエリの条件式が自動書き換えされる")]
    public void RenameColumn_RewritesQueryCondition()
    {
        var path = SaveDocumentWithQuery(out _, out var customerColumnId);

        var vm = new MainViewModel(
            new StubDialogService(),
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);

            var column = vm.Entities[0].Columns.First(c => c.Id == customerColumnId);
            column.Name = "BuyerId";

            vm.Queries[0].Condition.Should().Be("BuyerId = @customerId");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>ファイル由来でない置換（新規作成）ではクエリが持ち越されず空になることを検証する</summary>
    [Fact(DisplayName = "新規作成（ファイル由来でない置換）でクエリが消去される")]
    public void NewDiagram_ClearsQueries()
    {
        var path = SaveDocumentWithQuery(out _, out _);

        var vm = new MainViewModel(
            new StubDialogService { ConfirmResult = true },
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);
            vm.Queries.Should().ContainSingle();

            // NewDiagram はファイル由来でない置換のため、旧図のクエリを持ち越さない
            vm.NewDiagramCommand.Execute(null);

            vm.Queries.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>OpenQueryDefinitionsCommand がアダプタを呼び、結果でクエリを置き換えることを検証する</summary>
    [Fact(DisplayName = "クエリ定義ダイアログの結果で Queries が置き換わる")]
    public void OpenQueryDefinitions_ReplacesQueriesWithDialogResult()
    {
        var replacement = new List<QueryDefinition> { new() { Name = "FromDialog" } };
        var appDialogs = new StubAppDialogService { QueryDialogResult = replacement };
        var vm = new MainViewModel(new StubDialogService(), appDialogs: appDialogs);

        vm.OpenQueryDefinitionsCommand.Execute(null);

        appDialogs.ShowQueryDefinitionCallCount.Should().Be(1);
        vm.Queries.Should().BeSameAs(replacement);
        vm.Queries[0].Name.Should().Be("FromDialog");
    }

    /// <summary>キャンセル（null 返却）ではクエリが置き換わらないことを検証する</summary>
    [Fact(DisplayName = "クエリ定義ダイアログのキャンセルでは Queries を変更しない")]
    public void OpenQueryDefinitions_Cancel_KeepsQueries()
    {
        var appDialogs = new StubAppDialogService { QueryDialogResult = null };
        var vm = new MainViewModel(new StubDialogService(), appDialogs: appDialogs);
        var before = vm.Queries;

        vm.OpenQueryDefinitionsCommand.Execute(null);

        appDialogs.ShowQueryDefinitionCallCount.Should().Be(1);
        vm.Queries.Should().BeSameAs(before);
    }

    /// <summary>クエリ定義ダイアログのみ結果を差し替え、他は既定挙動を返すアプリダイアログスタブ</summary>
    private sealed class StubAppDialogService : IAppDialogService
    {
        public List<QueryDefinition>? QueryDialogResult { get; init; }

        public int ShowQueryDefinitionCallCount { get; private set; }

        public List<QueryDefinition>? ShowQueryDefinitionDialog(ErDiagram diagram)
        {
            ShowQueryDefinitionCallCount++;
            return QueryDialogResult;
        }

        public CSharpGenerationDialogResult? ShowCSharpGenerationDialog(
            IDatabaseProvider currentProvider
        ) => null;

        public DbConnectionDialogResult? ShowDbConnectionDialog(
            DbConnectionDialogMode mode,
            IDatabaseProvider? fixedProvider = null,
            string? title = null
        ) => null;

        public void ShowSchemaSyncDialog(
            IDatabaseProvider provider,
            DbConnectionSettings settings,
            IReadOnlyList<Entity> entities,
            IReadOnlyList<Relationship> relationships
        ) { }

        public PrintOptions? ShowPrintOptionsDialog(string? defaultTitle) => null;
    }

    /// <summary>ファイル選択ダイアログを表示せず、設定済みの結果を返すスタブ</summary>
    private sealed class StubFileDialogService : IFileDialogService
    {
        public FileDialogResult? OpenResult { get; init; }

        public FileDialogResult? PickOpenFile(string filter) => OpenResult;

        public FileDialogResult? PickSaveFile(
            string filter,
            string defaultExt,
            string? initialFileName = null,
            string? initialDirectory = null
        ) => null;

        public string? PickFolder(string title, string? initialDirectory = null) => null;
    }
}
