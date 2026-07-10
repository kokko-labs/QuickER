using System.IO;
using FluentAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Services;

/// <summary><see cref="JsonStorageService"/> の JSON 保存・読込往復を検証するテストクラス</summary>
public class JsonStorageServiceTests
{
    /// <summary>保存後に読み込み、エンティティ座標・色・リレーションの各属性が往復で保持されることを検証する</summary>
    [Fact(DisplayName = "Save → Load でエンティティとリレーションが復元される")]
    public void SaveAndLoad_RoundTrip()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        var a = vm.Entities[0];
        var b = vm.Entities[1];
        a.TableName = "Customer";
        a.X = 100;
        a.Y = 50;
        a.TitleBackgroundColor = "#FFF0BF";

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);
        b.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "CustomerId",
                    DataType = "int",
                    IsNullable = false,
                }
            )
        );
        vm.Relationships[0].SourceColumnId = a.Columns[0].Id;
        vm.Relationships[0].TargetColumnId = b.Columns[1].Id;
        vm.Relationships[0].ConstraintName = "FK_Order_Customer";

        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, vm.ToDocument());
            File.Exists(path).Should().BeTrue();

            var loaded = JsonStorageService.Load(path);
            loaded.Schema.Entities.Should().HaveCount(2);
            loaded.Schema.Relationships.Should().HaveCount(1);

            // 意味情報は schema、視覚情報は layout サイドカーへ分離して往復する
            var ea = loaded.Schema.Entities.First(e => e.Id == a.Id);
            ea.TableName.Should().Be("Customer");

            var la = loaded.Layout[a.Id];
            la.X.Should().Be(100);
            la.Y.Should().Be(50);
            la.TitleBackgroundColor.Should().Be("#FFF0BF");

            loaded.Schema.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
            loaded.Schema.Relationships[0].SourceColumnId.Should().Be(a.Columns[0].Id);
            loaded.Schema.Relationships[0].TargetColumnId.Should().Be(b.Columns[1].Id);
            loaded.Schema.Relationships[0].ConstraintName.Should().Be("FK_Order_Customer");
            loaded
                .Schema.Entities.First(e => e.Id == b.Id)
                .Columns[1]
                .IsNullable.Should()
                .BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>名前付きクエリ定義の全フィールドが保存・読込で往復することを検証する</summary>
    [Fact(DisplayName = "Save → Load で名前付きクエリ定義が全フィールド復元される")]
    public void SaveAndLoad_QueryDefinition_RoundTrip()
    {
        var entity = new Entity { TableName = "Order" };
        var amountColumn = new Column { Name = "Amount", DataType = "decimal(12,2)" };
        entity.Columns.Add(amountColumn);

        var query = new QueryDefinition
        {
            EntityId = entity.Id,
            Name = "GetByCustomer",
            Description = "顧客IDで注文を検索する",
            Returns = QueryReturnShape.Projection,
            ScalarType = null,
            Parameters =
            {
                new QueryParameter
                {
                    Name = "customerId",
                    Type = "int32",
                    SourceColumnId = amountColumn.Id,
                },
                new QueryParameter
                {
                    Name = "statuses",
                    Type = "int32",
                    IsList = true,
                },
            },
            Condition = "CustomerId = @customerId AND Status IN @statuses",
            OrderBy =
            {
                new QueryOrdering { ColumnId = amountColumn.Id, Descending = true },
            },
            HasPaging = true,
            Implementation = QueryImplementationKind.Sql,
            Sql = { ["sqlserver"] = "SELECT ...", ["sqlite"] = "SELECT ... LIMIT ..." },
            ResultTypeName = "OrderSummaryRow",
            Fields =
            {
                new ProjectionField
                {
                    Name = "TotalAmount",
                    Type = "decimal(12,2)",
                    SourceColumnId = amountColumn.Id,
                },
            },
        };

        var document = new DiagramDocument();
        document.Schema.Entities.Add(entity);
        document.Schema.Queries.Add(query);

        var path = Path.Combine(Path.GetTempPath(), $"er-query-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, document);
            var loaded = JsonStorageService
                .Load(path)
                .Schema.Queries.Should()
                .ContainSingle()
                .Which;

            loaded.Id.Should().Be(query.Id);
            loaded.EntityId.Should().Be(entity.Id);
            loaded.Name.Should().Be("GetByCustomer");
            loaded.Description.Should().Be("顧客IDで注文を検索する");
            loaded.Returns.Should().Be(QueryReturnShape.Projection);
            loaded.ScalarType.Should().BeNull();
            loaded.Parameters.Should().HaveCount(2);
            loaded.Parameters[0].SourceColumnId.Should().Be(amountColumn.Id);
            loaded.Parameters[1].Name.Should().Be("statuses");
            loaded.Parameters[1].IsList.Should().BeTrue();
            loaded.Parameters[1].SourceColumnId.Should().BeNull();
            loaded.Condition.Should().Be("CustomerId = @customerId AND Status IN @statuses");
            loaded.OrderBy.Should().ContainSingle();
            loaded.OrderBy[0].ColumnId.Should().Be(amountColumn.Id);
            loaded.OrderBy[0].Descending.Should().BeTrue();
            loaded.HasPaging.Should().BeTrue();
            loaded.Implementation.Should().Be(QueryImplementationKind.Sql);
            loaded.Sql.Should().HaveCount(2).And.ContainKey("sqlite");
            loaded.ResultTypeName.Should().Be("OrderSummaryRow");
            loaded.Fields.Should().ContainSingle();
            loaded.Fields[0].SourceColumnId.Should().Be(amountColumn.Id);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>queries を持たない既存フォーマットの JSON が空のクエリ一覧で読み込めることを検証する（後方互換）</summary>
    [Fact(DisplayName = "Load: queries が無い既存 JSON は空のクエリ一覧になる")]
    public void Load_LegacyJsonWithoutQueries_YieldsEmptyQueries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-legacy-{Guid.NewGuid()}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "Version": 1,
                  "Schema": {
                    "Entities": [ { "TableName": "Customer" } ],
                    "Relationships": [],
                    "TargetDbms": "sqlserver"
                  },
                  "Layout": {}
                }
                """
            );

            var loaded = JsonStorageService.Load(path);

            loaded.Schema.Entities.Should().ContainSingle();
            loaded.Schema.Queries.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>フォーマットバージョンが CurrentVersion より大きい文書は IsNewerFormat が立つことを検証する</summary>
    [Fact(DisplayName = "Load: CurrentVersion より新しいフォーマットは IsNewerFormat が true")]
    public void Load_NewerVersion_SetsIsNewerFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-newer-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(
                path,
                new DiagramDocument { Version = DiagramDocument.CurrentVersion + 1 }
            );

            JsonStorageService.Load(path).IsNewerFormat.Should().BeTrue();

            JsonStorageService.Save(path, new DiagramDocument());

            JsonStorageService.Load(path).IsNewerFormat.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>新しいフォーマットの文書を開くとき、警告確認でキャンセルすると現在の図が保持されることを検証する</summary>
    [Fact(DisplayName = "Open: 新しいフォーマットの警告をキャンセルすると読み込まない")]
    public void Open_NewerFormat_CancelKeepsCurrentDiagram()
    {
        var path = SaveNewerFormatDocument();
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new MainViewModel(
            dialogs,
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);

            vm.Entities.Should().BeEmpty();
            dialogs.WarningConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>新しいフォーマットの文書を開くとき、警告確認で続行すると読み込まれることを検証する</summary>
    [Fact(DisplayName = "Open: 新しいフォーマットの警告に続行すると読み込む")]
    public void Open_NewerFormat_ConfirmLoads()
    {
        var path = SaveNewerFormatDocument();
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new MainViewModel(
            dialogs,
            files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
        );

        try
        {
            vm.OpenCommand.Execute(null);

            vm.Entities.Should().ContainSingle();
            dialogs.WarningConfirmMessages.Should().ContainSingle();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>CurrentVersion より新しいフォーマットバージョンでエンティティ 1 件の文書を一時ファイルへ保存する</summary>
    private static string SaveNewerFormatDocument()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-newer-{Guid.NewGuid()}.json");
        var document = new DiagramDocument
        {
            Version = DiagramDocument.CurrentVersion + 1,
            Schema = new ErDiagram
            {
                TargetDbms = "sqlserver",
                Entities = { new Entity { TableName = "T1" } },
            },
        };
        JsonStorageService.Save(path, document);
        return path;
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
