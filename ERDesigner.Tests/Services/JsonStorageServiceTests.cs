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
        a.DisplayName = "顧客";
        a.TableName = "Customer";
        a.X = 100; a.Y = 50;

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        var path = Path.Combine(Path.GetTempPath(), $"er-{System.Guid.NewGuid()}.json");
        try
        {
            JsonStorageService.Save(path, vm);
            File.Exists(path).Should().BeTrue();

            var loaded = JsonStorageService.Load(path);
            loaded.Entities.Should().HaveCount(2);
            loaded.Relationships.Should().HaveCount(1);

            var ea = loaded.Entities.First(e => e.Id == a.Id);
            ea.DisplayName.Should().Be("顧客");
            ea.TableName.Should().Be("Customer");
            ea.X.Should().Be(100);
            ea.Y.Should().Be(50);

            loaded.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
