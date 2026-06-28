using System.IO;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using FluentAssertions;

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
            JsonStorageService.Save(path, vm);
            File.Exists(path).Should().BeTrue();

            var loaded = JsonStorageService.Load(path);
            loaded.Entities.Should().HaveCount(2);
            loaded.Relationships.Should().HaveCount(1);

            var ea = loaded.Entities.First(e => e.Id == a.Id);
            ea.TableName.Should().Be("Customer");
            ea.X.Should().Be(100);
            ea.Y.Should().Be(50);
            ea.TitleBackgroundColor.Should().Be("#FFF0BF");

            loaded.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
            loaded.Relationships[0].SourceColumnId.Should().Be(a.Columns[0].Id);
            loaded.Relationships[0].TargetColumnId.Should().Be(b.Columns[1].Id);
            loaded.Relationships[0].ConstraintName.Should().Be("FK_Order_Customer");
            loaded.Entities.First(e => e.Id == b.Id).Columns[1].IsNullable.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
