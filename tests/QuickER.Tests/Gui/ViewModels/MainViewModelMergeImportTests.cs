using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// マージ取込（Guid 引継）に対応した <see cref="MainViewModel.ReplaceDiagramFromModule"/> と
/// Excel 再取込経路のレイアウト温存・クエリ透過・自動整列フォールバックを検証するテストクラス。
/// </summary>
/// <remarks>
/// 再取込では取込結果の Id を現在図の Guid へ寄せる（マージは呼び出し側の責務）。VM 側は
/// 「現在図と Id が一致するエンティティのレイアウトを引き継ぎ自動整列しない・新規のみ幅自動調整」
/// という置換規則と、名前付きクエリの透過を担う。ここではその VM 挙動を実オブジェクトで検証する。
/// </remarks>
public class MainViewModelMergeImportTests
{
    /// <summary>一致エンティティのレイアウト・クエリが維持され、新規は既存と重ならない空き領域へ追記配置される</summary>
    [Fact(DisplayName = "マージ置換で一致分のレイアウト・クエリが維持され新規は重ならず追記配置")]
    public void ReplaceDiagramFromModule_WithMatch_PreservesLayoutAndQueries()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.AddEntityCommand.Execute(null);

        var existing = vm.Entities[0];
        existing.X = 400;
        existing.Y = 250;
        existing.Width = 321;
        var existingId = existing.Id;

        // マージ済み図: 現在図と同一 Id の一致エンティティ＋新規エンティティ、クエリは一致エンティティを参照
        var matched = new Entity
        {
            Id = existingId,
            TableName = existing.TableName,
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var added = new Entity
        {
            TableName = "AddedTable",
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var diagram = new ErDiagram
        {
            Entities = { matched, added },
            TargetDbms = "sqlserver",
            Queries =
            {
                new QueryDefinition { Name = "KeptQuery", EntityId = existingId },
            },
        };

        vm.ReplaceDiagramFromModule(diagram);

        // 一致エンティティは保存レイアウト（位置・幅）を 1px も動かさず引き継ぐ
        var mergedExisting = vm.Entities.First(entity => entity.Id == existingId);
        mergedExisting.X.Should().Be(400);
        mergedExisting.Y.Should().Be(250);
        mergedExisting.Width.Should().Be(321);

        // 新規エンティティは原点に積まれず、既存エンティティと矩形が重ならない空き領域へ追記配置される
        var mergedNew = vm.Entities.First(entity => entity.Id == added.Id);
        (mergedNew.X == 0 && mergedNew.Y == 0)
            .Should()
            .BeFalse("新規は原点へ積まず空き領域へ配置される");

        var overlap =
            mergedExisting.X < mergedNew.X + mergedNew.Width
            && mergedNew.X < mergedExisting.X + mergedExisting.Width
            && mergedExisting.Y < mergedNew.Y + mergedNew.DisplayHeight
            && mergedNew.Y < mergedExisting.Y + mergedExisting.DisplayHeight;
        overlap.Should().BeFalse("新規と一致分の矩形は重ならない");

        // クエリはそのまま透過する
        vm.Queries.Should().ContainSingle().Which.Name.Should().Be("KeptQuery");
    }

    /// <summary>一致が 1 件も無い置換（AI 生成・全新規取込相当）は従来どおり全体を自動整列する</summary>
    [Fact(DisplayName = "一致 0 件の置換は従来どおり全体自動整列される")]
    public void ReplaceDiagramFromModule_NoMatch_AutoLayouts()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.AddEntityCommand.Execute(null);

        // すべて新規 Id のエンティティ（現在図と交差なし）＋親子リレーションでツリー整列を誘発する
        var parent = new Entity
        {
            TableName = "Parent",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };
        var child = new Entity
        {
            TableName = "Child",
            Columns =
            {
                new Column { Name = "ParentId", DataType = "int" },
            },
        };
        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
        };
        var diagram = new ErDiagram
        {
            Entities = { parent, child },
            Relationships = { relationship },
            TargetDbms = "sqlserver",
        };

        vm.ReplaceDiagramFromModule(diagram);

        // 従来挙動: 現在図は完全に置き換わり、自動整列で位置が分散する
        vm.Entities.Should().HaveCount(2);
        var positions = vm.Entities.Select(entity => (entity.X, entity.Y)).ToList();
        positions
            .Distinct()
            .Should()
            .HaveCount(positions.Count, "自動整列で各エンティティが異なる位置に置かれる");
    }

    /// <summary>Excel 再取込の実経路で、テーブル・列が一致するクエリが生存する（Guid 引継）</summary>
    [Fact(DisplayName = "Excel 再取込経路: 一致するクエリが生存する")]
    public void ImportExcel_Reimport_SurvivesQuery()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-merge-excel-{Guid.NewGuid()}.xlsx");

        try
        {
            var files = new StubFileDialogService
            {
                SaveResult = new FileDialogResult(path, 8),
                OpenResult = new FileDialogResult(path, 3),
            };
            var vm = new MainViewModel(new StubDialogService(), files: files);
            vm.AddEntityCommand.Execute(null);

            var entity = vm.Entities[0];
            var entityId = entity.Id;
            var columnId = entity.Columns[0].Id;

            // 現在図に、エンティティと列を Guid 参照する名前付きクエリを持たせる
            vm.ReplaceQueries(
                new[]
                {
                    new QueryDefinition
                    {
                        Name = "SortById",
                        EntityId = entityId,
                        OrderBy = { new QueryOrdering { ColumnId = columnId } },
                    },
                }
            );

            // 現在図を Excel 定義書へ書き出し、そのまま取り込む（テーブル・列名が一致＝マージ対象）
            vm.ExportDiagramCommand.Execute(null);
            vm.ImportDiagramCommand.Execute(null);

            // 取込結果は新規 Guid だがマージで Id が現在図へ寄るため、クエリの参照が解決し生存する
            vm.Queries.Should().ContainSingle().Which.Name.Should().Be("SortById");
            vm.Entities.Should().ContainSingle().Which.Id.Should().Be(entityId);
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
