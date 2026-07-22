using System.IO;
using FluentAssertions;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// スキーマのみ JSON（配置情報なし文書）のエクスポートと読み込み時の自動整列を検証するテストクラス。
/// JSON 直列化そのもの（layout キーの省略・キー欠落の読込）の検証は
/// tests/Document/JsonStorageServiceTests にあり、ここには MainViewModel の挙動のみ置く。
/// </summary>
public class MainViewModelSchemaJsonTests
{
    /// <summary>
    /// スキーマのみ形式でエクスポート → 開く の往復で、配置情報なしのファイルでも全エンティティが
    /// 自動整列され（原点への積み重ねが起きず位置が分散する）ことを実挙動で検証する。
    /// </summary>
    [Fact(DisplayName = "Export → Open: スキーマのみ JSON を開くと全体が自動整列される")]
    public void ExportSchemaJson_ThenOpen_AutoArrangesEntities()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-roundtrip-{Guid.NewGuid()}.json");

        try
        {
            // 3 エンティティ＋2 リレーション（親 a → 子 b / c）の図を組み、スキーマのみ形式で書き出す
            var source = new MainViewModel(
                new StubDialogService(),
                files: new StubFileDialogService { SaveResult = new FileDialogResult(path, 4) }
            );
            source.AddEntityCommand.Execute(null);
            source.AddEntityCommand.Execute(null);
            source.AddEntityCommand.Execute(null);

            var a = source.Entities[0];
            var b = source.Entities[1];
            var c = source.Entities[2];

            source.StartAddOneToManyCommand.Execute(null);
            source.OnEntityClicked(a);
            source.OnEntityClicked(b);
            source.StartAddOneToManyCommand.Execute(null);
            source.OnEntityClicked(a);
            source.OnEntityClicked(c);

            source.ExportDiagramCommand.Execute(null);

            // 書き出したファイルは配置情報を持たない
            File.ReadAllText(path).Should().NotContain("\"Layout\"");

            // 別 VM で開くと、配置なしでも自動整列で位置が分散する
            var opened = new MainViewModel(
                new StubDialogService(),
                files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
            );
            opened.OpenCommand.Execute(null);

            opened.Entities.Should().HaveCount(3);

            var positions = opened.Entities.Select(e => (e.X, e.Y)).ToList();
            positions
                .Distinct()
                .Should()
                .HaveCount(positions.Count, "自動整列で全エンティティが異なる位置に配置される");
            opened.Entities.Any(e => e.X != 0 || e.Y != 0).Should().BeTrue("原点に積み重ならない");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// 一部エンティティのみ layout を持つ文書を開くと、保存座標のあるものはそのまま・
    /// ないものは原点のままで、自動整列が発動しない（部分欠落の現状挙動）ことを検証する。
    /// </summary>
    [Fact(DisplayName = "Open: 一部のみ layout を持つ文書は自動整列せず欠落分だけ原点になる")]
    public void Open_PartialLayout_KeepsSavedAndDefaultsMissingToOrigin()
    {
        var e1 = new Entity { TableName = "A" };
        var e2 = new Entity { TableName = "B" };
        var document = new DiagramDocument
        {
            Schema = new ErDiagram { Entities = { e1, e2 } },
            Layout = new()
            {
                [e1.Id] = new EntityLayout { X = 300, Y = 150 },
            },
        };

        var path = Path.Combine(Path.GetTempPath(), $"er-partial-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, document);

            var vm = new MainViewModel(
                new StubDialogService(),
                files: new StubFileDialogService { OpenResult = new FileDialogResult(path, 1) }
            );
            vm.OpenCommand.Execute(null);

            var loaded1 = vm.Entities.First(e => e.Id == e1.Id);
            var loaded2 = vm.Entities.First(e => e.Id == e2.Id);

            // layout があるエンティティは保存座標のまま
            loaded1.X.Should().Be(300);
            loaded1.Y.Should().Be(150);

            // layout がないエンティティは既定位置（原点）のまま＝自動整列は発動しない
            loaded2.X.Should().Be(0);
            loaded2.Y.Should().Be(0);
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
