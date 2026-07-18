using System.IO;
using FluentAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.ViewModels;

/// <summary>MainViewModel での名前付きクエリの往復・置換時の消去を検証するテストクラス</summary>
/// <remarks>
/// クエリ定義ダイアログ経由の差し替え・列リネーム追従は、コード生成フィーチャーモジュール側の
/// サービス（QueryDefinitionCommandService / QueryConditionRenameFollower）へ移設済みで、
/// それらの検証は tests/CodeGenUi 配下にある。ここには MainViewModel 固有の責務のみ残す。
/// </remarks>
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
