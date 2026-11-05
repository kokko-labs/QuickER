using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 一意制約（UNIQUE）の編集の、往復の表では見えない振る舞い（カードの開閉・編集行の作り直し・複製・保存と読み直し）を
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// 永続化先は一時フォルダへ隔離し（<c>UsePersistenceForTests</c>）、実 %LOCALAPPDATA% へは触れない。
/// 追加・削除・構成列の切替・制約名の変更と、巻き添えの制約ごと消す列削除の Undo / Redo・ダーティ判定は、
/// 操作の往復の表（<see cref="QuickER.Tests.Gui.UndoRedo.UndoOperationRows"/> の <c>unique-constraint-*</c> の行）が流す。
/// </remarks>
public class MainViewModelUniqueConstraintTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-unique-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelUniqueConstraintTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>Id / Code / Kind の 3 列を持つエンティティ 1 個を選択済みにした VM を返す</summary>
    private MainViewModel CreateViewModelWithSelectedEntity(
        RecordingFileDialogService? files = null
    )
    {
        var vm = new MainViewModel(new StubDialogService(), files: files);
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        var entity = new EntityViewModel(
            new Entity
            {
                TableName = "Item",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column { Name = "Code", DataType = "nvarchar(20)" },
                    new Column { Name = "Kind", DataType = "int" },
                },
            }
        );

        vm.Entities.Add(entity);
        vm.SelectedEntity = entity;
        vm.UndoRedo.Clear();
        return vm;
    }

    [Fact(
        DisplayName = "制約カードの開閉既定: 制約なしのエンティティ選択では畳まれ、追加操作で開く"
    )]
    public void UniqueConstraintCard_CollapsedByDefault_ExpandsOnAdd()
    {
        var vm = CreateViewModelWithSelectedEntity();

        // 制約が無いエンティティを選択した時点では畳まれている
        vm.IsUniqueConstraintCardExpanded.Should().BeFalse();

        // 追加すると（畳んだままでは追加した制約が見えないため）自動で開く
        vm.AddUniqueConstraintCommand.Execute(null);
        vm.IsUniqueConstraintCardExpanded.Should().BeTrue();
    }

    [Fact(
        DisplayName = "制約カードの開閉既定: 制約が定義されたエンティティの選択で開き、選択のたびに再評価される"
    )]
    public void UniqueConstraintCard_ExpandsWhenSelectedEntityHasConstraints()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var withConstraint = vm.SelectedEntity!;
        withConstraint.UniqueConstraints.Add(
            new UniqueConstraintViewModel(
                withConstraint,
                new UniqueConstraint { ColumnIds = { withConstraint.Columns[1].Id } }
            )
        );

        var without = new EntityViewModel(new Entity { TableName = "Plain" });
        vm.Entities.Add(without);

        // 制約なし → 畳む（手動で開いても、選択が変われば既定へ戻る）
        vm.SelectedEntity = without;
        vm.IsUniqueConstraintCardExpanded.Should().BeFalse();
        vm.IsUniqueConstraintCardExpanded = true;

        // 制約あり → 開く
        vm.SelectedEntity = withConstraint;
        vm.IsUniqueConstraintCardExpanded.Should().BeTrue();

        // 再度制約なしへ → 畳む（直前の手動状態を引き継がない）
        vm.SelectedEntity = without;
        vm.IsUniqueConstraintCardExpanded.Should().BeFalse();
    }

    [Fact(DisplayName = "エンティティ未選択では制約を追加できない")]
    public void AddUniqueConstraint_RequiresSelectedEntity()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        vm.AddUniqueConstraintCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact(DisplayName = "外部からの構成列変更で編集行が作り直され、空スロットは破棄される")]
    public void UniqueConstraintMembers_RebuildOnExternalColumnIdsChange()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;
        var code = entity.Columns.First(column => column.Name == "Code");
        var kind = entity.Columns.First(column => column.Name == "Kind");

        vm.AddUniqueConstraintCommand.Execute(null);
        var constraint = entity.UniqueConstraints[0];
        vm.AddUniqueConstraintMemberSlotCommand.Execute(constraint);
        constraint.Members[0].IsPendingSlot.Should().BeTrue();

        // MCP・AI チャット経由の変更と同じ「正本の直接差し替え」
        constraint.SetColumnIds([kind.Id, code.Id]);

        constraint.Members.Select(m => m.SelectedColumn).Should().Equal(kind, code);
        constraint.Members.Should().OnlyContain(m => !m.IsPendingSlot, "空スロットは破棄される");
    }

    [Fact(
        DisplayName = "保存した図を読み直しても一意制約が残る（GUI 往復でのデータ喪失の回帰テスト）"
    )]
    public void SaveAndReopen_KeepsUniqueConstraints()
    {
        var path = Path.Combine(_folder, "RoundTrip.json");
        var files = new RecordingFileDialogService
        {
            SaveResult = new(path, 1),
            OpenResult = new(path, 1),
        };
        var vm = CreateViewModelWithSelectedEntity(files);
        var entity = vm.SelectedEntity!;
        var code = entity.Columns.First(column => column.Name == "Code");

        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(
                entity,
                new UniqueConstraint { Name = "UQ_Item_Code", ColumnIds = { code.Id } }
            )
        );

        vm.SaveCommand.Execute(null);
        vm.NewDiagramCommand.Execute(null);
        vm.Entities.Should().BeEmpty();

        vm.OpenCommand.Execute(null);

        vm.Entities.Should().ContainSingle();
        var reopened = vm.Entities[0].UniqueConstraints.Should().ContainSingle().Subject;
        reopened.Name.Should().Be("UQ_Item_Code");
        reopened.ColumnIds.Should().Equal(code.Id);
    }

    [Fact(DisplayName = "エンティティ複製で一意制約も複製され、構成列は複製側の列 Guid を指す")]
    public void DuplicateEntity_ClonesUniqueConstraints_WithRemappedColumnIds()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;
        var code = entity.Columns.First(column => column.Name == "Code");
        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(entity, new UniqueConstraint { ColumnIds = { code.Id } })
        );

        vm.DuplicateSelectedEntityCommand.Execute(null);

        var copy = vm.Entities[1];
        var copiedConstraint = copy.UniqueConstraints.Should().ContainSingle().Subject;
        copiedConstraint
            .ColumnIds.Should()
            .BeSubsetOf(
                copy.Columns.Select(column => column.Id),
                "構成列は複製側の列 Guid へ張り替わるべき"
            );
        copiedConstraint
            .Members.Select(member => member.SelectedColumn!.Name)
            .Should()
            .Equal("Code");
    }

    [Fact(DisplayName = "図の置換（ファイル読込・DB 取込経路）でも一意制約が保たれる")]
    public void ReplaceDiagramFromModule_PreservesUniqueConstraints()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var entity = new Entity
        {
            TableName = "Item",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
                code,
            },
            UniqueConstraints = { new UniqueConstraint { ColumnIds = { code.Id } } },
        };

        vm.ReplaceDiagramFromModule(new ErDiagram { Entities = { entity } });

        vm.Entities.Should().ContainSingle();
        vm.Entities[0].UniqueConstraints.Should().ContainSingle();
        vm.Entities[0].UniqueConstraints[0].ColumnIds.Should().Equal(code.Id);
        vm.Entities[0]
            .Columns.First(column => column.Id == code.Id)
            .IsUniqueConstraintMember.Should()
            .BeTrue();
    }
}
