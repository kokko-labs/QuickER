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
