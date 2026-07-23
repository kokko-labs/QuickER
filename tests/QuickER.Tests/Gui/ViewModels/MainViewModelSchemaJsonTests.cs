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
    /// 一部エンティティのみ layout を持つ文書（外部ツールがエンティティだけ追記した文書など）を開くと、
    /// layout を持つ既存エンティティは 1px も動かず、layout の無い欠落分のみが空き領域へ追記配置され
    /// （原点への積み重ねが起きず既存と重ならない）ことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "Open: 一部のみ layout を持つ文書は既存を動かさず欠落分を空き領域へ追記配置する"
    )]
    public void Open_PartialLayout_KeepsSavedAndAppendsMissingWithoutOverlap()
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

            // layout があるエンティティは保存座標を 1px も動かさない
            loaded1.X.Should().Be(300);
            loaded1.Y.Should().Be(150);

            // layout がない欠落エンティティは原点に積まれず空き領域へ追記配置される
            (loaded2.X == 0 && loaded2.Y == 0)
                .Should()
                .BeFalse("欠落分は原点へ積まず空き領域へ配置される");

            // 既存と欠落分の矩形は重ならない
            var overlap =
                loaded1.X < loaded2.X + loaded2.Width
                && loaded2.X < loaded1.X + loaded1.Width
                && loaded1.Y < loaded2.Y + loaded2.DisplayHeight
                && loaded2.Y < loaded1.Y + loaded1.DisplayHeight;
            overlap.Should().BeFalse("既存と欠落分の矩形は重ならない");
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
