using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 一意制約（UNIQUE）の編集コマンド（追加・削除・構成列の切替）と、その Undo / Redo・ダーティ判定を
/// 検証するテストクラス。
/// </summary>
/// <remarks>
/// 永続化先は一時フォルダへ隔離し（<c>UsePersistenceForTests</c>）、実 %APPDATA% へは触れない。
/// 列削除は「巻き添えの制約ごと削除して 1 回の Undo で両方戻る」ことも併せて固定する。
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

    [Fact(DisplayName = "制約の追加は Undo で消え Redo で戻る")]
    public void AddUniqueConstraint_UndoRedo()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;

        vm.AddUniqueConstraintCommand.Execute(null);
        entity.UniqueConstraints.Should().ContainSingle();

        vm.UndoRedo.Undo();
        entity.UniqueConstraints.Should().BeEmpty();

        vm.UndoRedo.Redo();
        entity.UniqueConstraints.Should().ContainSingle();
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

    [Fact(DisplayName = "構成列の切替はチェック順に積まれ、Undo で元の構成へ戻る")]
    public void ToggleUniqueConstraintColumn_UndoRedo()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;

        vm.AddUniqueConstraintCommand.Execute(null);
        var constraint = entity.UniqueConstraints[0];

        // 「+ → チェック」の 2 操作で単一列制約が成立する
        var kind = constraint.ColumnCandidates.First(c => c.Column.Name == "Kind");
        vm.ToggleUniqueConstraintColumnCommand.Execute(kind);
        constraint.ColumnIds.Should().Equal(kind.Column.Id);
        kind.IsMember.Should().BeTrue();

        // 追加のチェックはチェック順（＝宣言順）で末尾へ積む
        var code = constraint.ColumnCandidates.First(c => c.Column.Name == "Code");
        vm.ToggleUniqueConstraintColumnCommand.Execute(code);
        constraint.ColumnIds.Should().Equal(kind.Column.Id, code.Column.Id);
        constraint.ColumnSummary.Should().Be("Kind, Code");

        // 再度のクリックは解除
        vm.ToggleUniqueConstraintColumnCommand.Execute(kind);
        constraint.ColumnIds.Should().Equal(code.Column.Id);

        vm.UndoRedo.Undo();
        constraint.ColumnIds.Should().Equal(kind.Column.Id, code.Column.Id);

        vm.UndoRedo.Redo();
        constraint.ColumnIds.Should().Equal(code.Column.Id);
    }

    [Fact(DisplayName = "制約の削除は Undo で元の位置へ戻る")]
    public void RemoveUniqueConstraint_UndoRestoresPosition()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;

        vm.AddUniqueConstraintCommand.Execute(null);
        vm.AddUniqueConstraintCommand.Execute(null);
        var first = entity.UniqueConstraints[0];
        var second = entity.UniqueConstraints[1];

        vm.RemoveUniqueConstraintCommand.Execute(first);
        entity.UniqueConstraints.Should().Equal(second);

        vm.UndoRedo.Undo();
        entity.UniqueConstraints.Should().Equal(first, second);
    }

    [Fact(DisplayName = "制約名の変更は Undo / Redo で往復する")]
    public void ConstraintNameChange_IsUndoable()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;

        vm.AddUniqueConstraintCommand.Execute(null);
        var constraint = entity.UniqueConstraints[0];

        constraint.Name = "UQ_Item_Code";
        vm.UndoRedo.CanUndo.Should().BeTrue();

        vm.UndoRedo.Undo();
        constraint.Name.Should().BeEmpty();

        vm.UndoRedo.Redo();
        constraint.Name.Should().Be("UQ_Item_Code");
    }

    [Fact(DisplayName = "制約の編集（追加・構成列・制約名）はダーティ判定に乗る")]
    public void UniqueConstraintEdits_MarkDocumentDirty()
    {
        var vm = CreateViewModelWithSelectedEntity(
            new RecordingFileDialogService
            {
                SaveResult = new(Path.Combine(_folder, "Doc.json"), 1),
            }
        );
        var entity = vm.SelectedEntity!;

        vm.SaveCommand.Execute(null);
        vm.IsDirty.Should().BeFalse("保存直後はクリーン");

        vm.AddUniqueConstraintCommand.Execute(null);
        vm.IsDirty.Should().BeTrue("制約の追加は保存文書を変える");

        vm.SaveCommand.Execute(null);
        var candidate = entity.UniqueConstraints[0].ColumnCandidates[1];
        vm.ToggleUniqueConstraintColumnCommand.Execute(candidate);
        vm.IsDirty.Should().BeTrue("構成列の変更は保存文書を変える");

        vm.SaveCommand.Execute(null);
        entity.UniqueConstraints[0].Name = "UQ_Item_Code";
        vm.IsDirty.Should().BeTrue("制約名の変更は保存文書を変える");
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

    [Fact(DisplayName = "列を削除するとその列を含む制約も同じ Undo 単位で消え、Undo で両方戻る")]
    public void RemoveColumn_RemovesDependentConstraints_AsSingleUndoUnit()
    {
        var vm = CreateViewModelWithSelectedEntity();
        var entity = vm.SelectedEntity!;

        // Code だけの制約と、Code + Kind の複合制約、Kind だけの制約を用意する
        var code = entity.Columns.First(column => column.Name == "Code");
        var kind = entity.Columns.First(column => column.Name == "Kind");
        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(entity, new UniqueConstraint { ColumnIds = { code.Id } })
        );
        entity.UniqueConstraints.Add(
            new UniqueConstraintViewModel(
                entity,
                new UniqueConstraint { ColumnIds = { code.Id, kind.Id } }
            )
        );
        var kindOnly = new UniqueConstraintViewModel(
            entity,
            new UniqueConstraint { ColumnIds = { kind.Id } }
        );
        entity.UniqueConstraints.Add(kindOnly);
        vm.UndoRedo.Clear();

        vm.RemoveColumnCommand.Execute(code);

        entity.Columns.Select(column => column.Name).Should().Equal("Id", "Kind");
        entity
            .UniqueConstraints.Should()
            .Equal([kindOnly], "Code を含む 2 件だけが巻き添えで消えるべき");

        // 1 回の Undo で列と制約の両方が戻る（削除位置も維持される）
        vm.UndoRedo.Undo();

        entity.Columns.Select(column => column.Name).Should().Equal("Id", "Code", "Kind");
        entity.UniqueConstraints.Should().HaveCount(3);
        entity.UniqueConstraints[2].Should().BeSameAs(kindOnly);
        entity
            .UniqueConstraints.Count(constraint => constraint.ContainsColumn(code.Id))
            .Should()
            .Be(2);
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
        copiedConstraint.ColumnSummary.Should().Be("Code");
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
