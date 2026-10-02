using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// GUI のリレーション作成フローが付ける制約名を検証するテストクラス
/// </summary>
/// <remarks>
/// 既定名 <c>FK_{子}_{親}</c> は、テーブル名に <c>_</c> を含むと別の組でも同じになる
/// （子 <c>A_B</c>・親 <c>C</c> と、子 <c>A</c>・親 <c>B_C</c> はどちらも <c>FK_A_B_C</c>）。
/// 作成フローは AI ツールと同じ <see cref="ConstraintNames.UniqueForeignKey"/> で衝突を避けることを固定する。
/// 永続化先は一時フォルダへ隔離する（<c>UsePersistenceForTests</c>）。
/// </remarks>
public class MainViewModelRelationshipConstraintNameTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（各テストで独立・後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-relname-" + Guid.NewGuid().ToString("N")
    );

    public MainViewModelRelationshipConstraintNameTests() => Directory.CreateDirectory(_folder);

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
    /// 親 <c>C</c>・<c>B_C</c> と、それぞれを参照する列を持つ子 <c>A_B</c>・<c>A</c> を並べた図を返す
    /// </summary>
    private MainViewModel CreateViewModel()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();

        vm.Entities.Add(CreateEntity("C", "CId"));
        vm.Entities.Add(CreateEntity("A_B", "A_BId", "CId"));
        vm.Entities.Add(CreateEntity("B_C", "B_CId"));
        vm.Entities.Add(CreateEntity("A", "AId", "B_CId"));
        vm.UndoRedo.Clear();

        return vm;
    }

    /// <summary>主キー 1 列と、残りの int 列を持つテーブルを作る</summary>
    private static EntityViewModel CreateEntity(
        string tableName,
        string keyColumn,
        params string[] otherColumns
    )
    {
        var entity = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Name = keyColumn,
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        foreach (var column in otherColumns)
        {
            entity.Columns.Add(new Column { Name = column, DataType = "int" });
        }

        return new EntityViewModel(entity);
    }

    /// <summary>リレーション作成モードで親 → 子の順にクリックし、作られたリレーションを返す</summary>
    private static RelationshipViewModel Connect(MainViewModel vm, string parent, string child)
    {
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities.Single(entity => entity.TableName == parent));
        vm.OnEntityClicked(vm.Entities.Single(entity => entity.TableName == child));

        return vm.Relationships[^1];
    }

    [Fact(DisplayName = "作成時の制約名は、衝突が無ければ既定名 FK_{子}_{親} のまま")]
    public void CreateRelationship_NoCollision_UsesDefaultName()
    {
        var vm = CreateViewModel();

        Connect(vm, "C", "A_B").ConstraintName.Should().Be("FK_A_B_C");
        Connect(vm, "B_C", "A_B").ConstraintName.Should().Be("FK_A_B_B_C");
    }

    [Fact(
        DisplayName = "別の組が同じ既定名になるとき、作成時の制約名は子の列名を足して衝突を避ける"
    )]
    public void CreateRelationship_DefaultNameTakenByAnotherPair_GetsUniqueName()
    {
        var vm = CreateViewModel();

        var first = Connect(vm, "C", "A_B");
        var second = Connect(vm, "B_C", "A");

        first.ConstraintName.Should().Be("FK_A_B_C");
        second.ConstraintName.Should().Be("FK_A_B_C_B_CId");
    }

    [Fact(
        DisplayName = "衝突を避けて名前を付けたリレーションの作成も、1 回の Undo で消え Redo で同じ名前で戻る"
    )]
    public void CreateRelationship_UniqueName_UndoRedoInOneStep()
    {
        var vm = CreateViewModel();
        Connect(vm, "C", "A_B");
        Connect(vm, "B_C", "A");

        vm.UndoRedo.Undo();

        vm.Relationships.Should().ContainSingle().Which.ConstraintName.Should().Be("FK_A_B_C");

        vm.UndoRedo.Redo();

        vm.Relationships.Select(relationship => relationship.ConstraintName)
            .Should()
            .Equal("FK_A_B_C", "FK_A_B_C_B_CId");
    }
}
