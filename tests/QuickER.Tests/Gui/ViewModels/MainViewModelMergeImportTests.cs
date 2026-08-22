using System.IO;
using AwesomeAssertions;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Resources;
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
public class MainViewModelMergeImportTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（永続化の隔離先。後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-merge-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelMergeImportTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch
        {
            // 後始末の失敗はテスト結果に影響させない
        }
    }

    /// <summary>
    /// 実 %APPDATA% を汚さないよう、永続化先を一時フォルダへ隔離した VM を生成する
    /// （<see cref="MainViewModel.ReplaceQueries"/> が AutoSave を呼ぶため隔離が必須）。
    /// </summary>
    private MainViewModel CreateViewModel(
        IFileDialogService? files = null,
        StubDialogService? dialogs = null
    )
    {
        var resolvedDialogs = dialogs ?? new StubDialogService();
        var vm = files is null
            ? new MainViewModel(resolvedDialogs)
            : new MainViewModel(resolvedDialogs, files: files);
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        return vm;
    }

    /// <summary>一致エンティティのレイアウト・クエリが維持され、新規は既存と重ならない空き領域へ追記配置される</summary>
    [Fact(DisplayName = "マージ置換で一致分のレイアウト・クエリが維持され新規は重ならず追記配置")]
    public void ReplaceDiagramFromModule_WithMatch_PreservesLayoutAndQueries()
    {
        var vm = CreateViewModel();
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

    /// <summary>一致が 1 件も無い置換（AI 生成・全新規取込相当）は全体を自動整列する</summary>
    [Fact(DisplayName = "一致 0 件の置換は全体自動整列される")]
    public void ReplaceDiagramFromModule_NoMatch_AutoLayouts()
    {
        var vm = CreateViewModel();
        vm.AddEntityCommand.Execute(null);

        // すべて新規 Id のエンティティ（現在図と交差なし）＋親子リレーションで全体整列を誘発する
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

        // 新規作成経路の全体整列は AI 生成直後（AutoArrangeNewDiagram＝格子整列）と同一であること。
        // 両者は決定的なので、続けて整列し直しても座標は 1 つも動かない
        vm.AutoArrangeNewDiagram();
        vm.Entities.Select(entity => (entity.X, entity.Y))
            .Should()
            .Equal(positions, "取込の自動整列は AI 生成直後の格子整列と同じでなければならない");
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
            var vm = CreateViewModel(files);
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

    /// <summary>構造同一の Excel 再取込でも、説明が取込値で上書きされる場合は確認が出て件数が載る</summary>
    /// <remarks>
    /// 構造署名は説明・Memo を含まないため、実差分を見ないと書き出し後に手書きした説明が
    /// 無確認で消える。ここでは確認が出ることと件数の表示を固定する。
    /// </remarks>
    [Fact(DisplayName = "Excel 再取込経路: 説明の上書きがあると確認が出て件数が載る")]
    public void ImportExcel_DescriptionOverwrite_Confirms()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-merge-excel-{Guid.NewGuid()}.xlsx");

        try
        {
            var files = new StubFileDialogService
            {
                SaveResult = new FileDialogResult(path, 8),
                OpenResult = new FileDialogResult(path, 3),
            };
            var dialogs = new StubDialogService { ConfirmResult = false };
            var vm = CreateViewModel(files, dialogs);
            vm.AddEntityCommand.Execute(null);

            // 説明が空の状態で書き出してから、図の側にだけ説明を書き加える（構造は変わらない）
            vm.ExportDiagramCommand.Execute(null);
            vm.Entities[0].Description = "手書きしたテーブル説明";

            vm.ImportDiagramCommand.Execute(null);

            // 確認水準（通常/警告）は未保存変更の有無で決まるため、両方の記録を合わせて検査する
            var shown = dialogs
                .ConfirmMessages.Concat(dialogs.WarningConfirmMessages)
                .Should()
                .ContainSingle()
                .Which;
            shown.Should().Contain(string.Format(Strings.Import_DescriptionOverwriteWarning, 1));

            // 確認を拒否したため取込は行われず、書き加えた説明はそのまま残る
            vm.Entities.Should()
                .ContainSingle()
                .Which.Description.Should()
                .Be("手書きしたテーブル説明");
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
