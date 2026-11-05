using System;
using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.Gui.Services.FormatRoundTrip;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.Services;

/// <summary>DbmlExporter.SaveTo / DbmlImporter.Load のファイルの入口をテストするクラス</summary>
/// <remarks>
/// DBML の出力の記法・告知・取込の規則・往復は、形式の往復のエンジン（<c>FormatRoundTripTests</c> の形の行 <c>dbml-…</c> と規則の行 <c>dbml-rule-…</c>）が持つ。
/// ここはファイルの読み書きそのもの（SaveTo が Build と同じテキストを書き、Load が Parse と同じ図を返す）だけを見る。
/// </remarks>
public class DbmlTests
{
    /// <summary>SaveTo で書き出した DBML ファイルを Load で読み戻せることを検証する（往復の中身は形の行 mermaid-save-load の DBML の期待）</summary>
    [Fact(DisplayName = "DBML ファイルの SaveTo と Load を往復できる")]
    public void SaveAndLoad_RoundTrip()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].TableName = "Parent";
        vm.Entities[0].Columns[0].Name = "ParentId";
        vm.Entities[1].TableName = "Child";
        vm.Entities[1].Columns[0].Name = "ChildId";
        vm.Entities[1]
            .Columns.Add(
                new ColumnViewModel(
                    new Column
                    {
                        Name = "ParentId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    }
                )
            );
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        vm.Relationships[0]
            .SetColumnPairs([
                new RelationshipColumnPair(
                    vm.Entities[0].Columns[0].Id,
                    vm.Entities[1].Columns[1].Id
                ),
            ]);
        vm.Relationships[0].ConstraintName = "FK_Child_Parent";
        var model = vm.ToDiagramModel();
        var text = DbmlExporter.Build(model);

        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.dbml");

        try
        {
            DbmlExporter.SaveTo(model, path);

            File.ReadAllText(path).Should().Be(text, "SaveTo は Build と同じテキストを書く");
            FormatRoundTripRunner
                .Describe(DbmlImporter.Load(path, "sqlserver"))
                .Should()
                .Equal(
                    FormatRoundTripRunner.Describe(DbmlImporter.Parse(text, "sqlserver")),
                    "Load はファイルのテキストを Parse したのと同じ図を返す"
                );
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
