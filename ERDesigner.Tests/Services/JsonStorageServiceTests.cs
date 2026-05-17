using System.IO;
using ERDesigner.Models;
using ERDesigner.Services;
using ERDesigner.ViewModels;
using FluentAssertions;

namespace ERDesigner.Tests.Services;

/// <summary>
/// JSON 保存/読込の往復が正しく動くかを検証します。
/// </summary>
public class JsonStorageServiceTests
{
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
