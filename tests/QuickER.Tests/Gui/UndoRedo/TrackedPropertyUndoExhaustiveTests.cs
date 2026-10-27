using System.IO;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.Tests.TestSupport.Engines;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// 図の ViewModel（エンティティ・カラム・リレーション・一意制約と、その行・MainViewModel）の読み書きプロパティを全数列挙し、
/// 「変更したら履歴が 1 件積まれ、Undo 1 回で元に戻り、未保存になる」ことを網羅して固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 変更追跡は、プロパティごとに「追跡する」と宣言した一覧（<c>DiagramChangeTracker</c> の Tracked*Properties）と
/// 連動変更の専用経路で成り立つ。宣言から漏れたプロパティは、ビルドも型検査も通ったまま
/// 「Ctrl+Z で戻らない・変更しても未保存にならない（外部変更の自動再読込で黙って失われる）」状態になる。
/// ここは次の 3 つを機械で表明する。
/// </para>
/// <list type="number">
///   <item>公開・internal の読み書きプロパティは、すべて下の宣言表（追跡する／除外）のどちらかに載っている（新しいプロパティは判断を強制される）。対象は図の ViewModel 4 型に加えて、行の ViewModel 3 型（一意制約の構成列・列ペア・主キーの順序）と MainViewModel。</item>
///   <item>追跡する宣言は、トラッカーの追跡一覧と一致する。</item>
///   <item>追跡するプロパティごとに、変更→Undo 1 回→元・Redo→変更後を、図全体の保存内容の比較で確かめる。連動する組（主キー↔NULL 許容、種別↔参照アクション・列ペア）は全組合せで 1 履歴に畳まれる。</item>
/// </list>
/// <para>
/// 既知の失敗は台帳（<see cref="UndoKnownFailures"/>）に載せる。台帳にない失敗と、台帳にあるのに起きない失敗はどちらも赤。
/// </para>
/// </remarks>
public class TrackedPropertyUndoExhaustiveTests : IDisposable
{
    /// <summary>テスト専用の一時作業フォルダ（保存済みの状態を作るためだけに使い、後始末で削除する）</summary>
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "quicker-undo-exhaustive-" + Guid.NewGuid().ToString("N")
    );

    public TrackedPropertyUndoExhaustiveTests() => Directory.CreateDirectory(_folder);

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

    /// <summary>履歴の外に置くプロパティの分類</summary>
    private enum Exclusion
    {
        /// <summary>専用のコマンドがある（位置の移動など）</summary>
        DedicatedCommand,

        /// <summary>履歴の外（表示だけの状態・導出値）</summary>
        OutsideHistory,
    }

    /// <summary>追跡するプロパティ（所有型.名前）</summary>
    private static readonly string[] Tracked =
    [
        "EntityViewModel.TableName",
        "EntityViewModel.Memo",
        "EntityViewModel.Description",
        "EntityViewModel.TitleBackgroundColor",
        "ColumnViewModel.Name",
        "ColumnViewModel.DataType",
        "ColumnViewModel.IsPrimaryKey",
        "ColumnViewModel.IsForeignKey",
        "ColumnViewModel.IsNullable",
        "ColumnViewModel.Description",
        "RelationshipViewModel.Type",
        "RelationshipViewModel.ConstraintName",
        "RelationshipViewModel.OnDelete",
        "RelationshipViewModel.OnUpdate",
        "UniqueConstraintViewModel.Name",
    ];

    /// <summary>履歴の外に置くプロパティ（所有型.名前 → 分類と理由）</summary>
    private static readonly Dictionary<string, (Exclusion Kind, string Reason)> Excluded = new()
    {
        ["EntityViewModel.X"] = (
            Exclusion.DedicatedCommand,
            "MoveEntityCommand / GroupMoveEntitiesCommand / ArrangeEntitiesCommand"
        ),
        ["EntityViewModel.Y"] = (
            Exclusion.DedicatedCommand,
            "MoveEntityCommand / GroupMoveEntitiesCommand / ArrangeEntitiesCommand"
        ),
        ["EntityViewModel.Width"] = (
            Exclusion.OutsideHistory,
            "保存文書の一部だが Undo の対象外。未保存の判定だけが別経路（MainViewModelUntrackedChangeDirtyTests）"
        ),
        ["EntityViewModel.ShowDescriptionsInDiagram"] = (
            Exclusion.OutsideHistory,
            "ツールバーの表示設定"
        ),
        ["EntityViewModel.ShowNullabilityInDiagram"] = (
            Exclusion.OutsideHistory,
            "ツールバーの表示設定"
        ),
        ["EntityViewModel.IsCompactView"] = (Exclusion.OutsideHistory, "ツールバーの表示設定"),
        ["EntityViewModel.IsSelected"] = (Exclusion.OutsideHistory, "選択の表示状態"),
        ["EntityViewModel.IsDimmed"] = (Exclusion.OutsideHistory, "強調表示の表示状態"),
        ["ColumnViewModel.IsForeignKeyManagedByRelationship"] = (
            Exclusion.OutsideHistory,
            "リレーションから導出する表示状態"
        ),
        ["ColumnViewModel.IsPrimaryKeyEditable"] = (
            Exclusion.OutsideHistory,
            "リレーションから導出する編集可否"
        ),
        ["ColumnViewModel.IsForeignKeyEditable"] = (
            Exclusion.OutsideHistory,
            "リレーションから導出する編集可否"
        ),
        ["RelationshipViewModel.IsSelected"] = (Exclusion.OutsideHistory, "選択の表示状態"),
        ["RelationshipViewModel.IsDimmed"] = (Exclusion.OutsideHistory, "強調表示の表示状態"),
        ["RelationshipViewModel.IsEmphasized"] = (Exclusion.OutsideHistory, "強調表示の表示状態"),
        ["ColumnViewModel.IsUniqueConstraintMember"] = (
            Exclusion.OutsideHistory,
            "一意制約から導出する表示状態（キー欄の UQ）"
        ),
        ["ColumnViewModel.IsIdentity"] = (
            Exclusion.DedicatedCommand,
            "ChangeTargetDbmsCommand（対象 DBMS の切替で印を外す。図の読込でだけ付く）"
        ),
        ["UniqueConstraintMemberViewModel.SelectedColumn"] = (
            Exclusion.DedicatedCommand,
            "ChangeUniqueConstraintColumnsCommand（行の選択を 1 履歴に畳む）"
        ),
        ["RelationshipColumnPairViewModel.SelectedSourceColumn"] = (
            Exclusion.DedicatedCommand,
            "ChangeRelationshipColumnPairsCommand（両側が決まった時点で 1 履歴）"
        ),
        ["RelationshipColumnPairViewModel.SelectedTargetColumn"] = (
            Exclusion.DedicatedCommand,
            "ChangeRelationshipColumnPairsCommand（両側が決まった時点で 1 履歴）"
        ),
        ["PrimaryKeyMemberViewModel.Column"] = (
            Exclusion.OutsideHistory,
            "主キーの順序（正本）から導出する行。並べ替えは SetPrimaryKeyCommand"
        ),
        ["PrimaryKeyMemberViewModel.CanMoveUp"] = (
            Exclusion.OutsideHistory,
            "行の位置から導出する編集可否"
        ),
        ["PrimaryKeyMemberViewModel.CanMoveDown"] = (
            Exclusion.OutsideHistory,
            "行の位置から導出する編集可否"
        ),
        ["MainViewModel.SelectedProvider"] = (
            Exclusion.DedicatedCommand,
            "ChangeTargetDbmsCommand（型・NULL 許容・採番の印の変換と 1 履歴）"
        ),
        ["MainViewModel.ShowNullabilityInDiagram"] = (
            Exclusion.OutsideHistory,
            "ツールバーの表示設定"
        ),
        ["MainViewModel.IsCompactViewInDiagram"] = (
            Exclusion.OutsideHistory,
            "ツールバーの表示設定"
        ),
        ["MainViewModel.ShowColumnDescriptionsInDiagram"] = (
            Exclusion.OutsideHistory,
            "ツールバーの表示設定"
        ),
        ["MainViewModel.IsMiniMapEnabled"] = (Exclusion.OutsideHistory, "ツールバーの表示設定"),
        ["MainViewModel.IsToolboxVisible"] = (Exclusion.OutsideHistory, "パネルの表示設定"),
        ["MainViewModel.IsPropertyPanelVisible"] = (Exclusion.OutsideHistory, "パネルの表示設定"),
        ["MainViewModel.IsFullScreen"] = (Exclusion.OutsideHistory, "ウィンドウの表示設定"),
        ["MainViewModel.IsUniqueConstraintCardExpanded"] = (
            Exclusion.OutsideHistory,
            "プロパティパネルの開閉状態"
        ),
        ["MainViewModel.IsSearchOverlayVisible"] = (Exclusion.OutsideHistory, "検索の表示状態"),
        ["MainViewModel.SearchQuery"] = (Exclusion.OutsideHistory, "検索の表示状態"),
        ["MainViewModel.SelectedSearchResult"] = (Exclusion.OutsideHistory, "検索の表示状態"),
        ["MainViewModel.ViewportContentBounds"] = (
            Exclusion.OutsideHistory,
            "ビューポート（表示位置）"
        ),
        ["MainViewModel.ZoomLevel"] = (Exclusion.OutsideHistory, "ビューポート（表示倍率）"),
        ["MainViewModel.FeatureToolbarItems"] = (
            Exclusion.OutsideHistory,
            "起動時に組み立てる機能モジュールのツールバー"
        ),
        ["MainViewModel.SelectedEntity"] = (Exclusion.OutsideHistory, "選択の表示状態"),
        ["MainViewModel.SelectedRelationship"] = (Exclusion.OutsideHistory, "選択の表示状態"),
        ["MainViewModel.SelectedColumn"] = (Exclusion.OutsideHistory, "選択の表示状態"),
        ["MainViewModel.PendingRelationshipSource"] = (
            Exclusion.OutsideHistory,
            "リレーション作成の途中状態（作成は AddRelationshipCommand）"
        ),
        ["MainViewModel.PendingRelationshipType"] = (
            Exclusion.OutsideHistory,
            "リレーション作成の途中状態（作成は AddRelationshipCommand）"
        ),
        ["MainViewModel.IsRelationshipMode"] = (
            Exclusion.OutsideHistory,
            "リレーション作成の途中状態（作成は AddRelationshipCommand）"
        ),
        ["MainViewModel.CurrentFilePath"] = (
            Exclusion.OutsideHistory,
            "文書の識別（開く・名前を付けて保存・新規作成で遷移する）"
        ),
        ["MainViewModel.IsRubberBandVisible"] = (
            Exclusion.OutsideHistory,
            "ラバーバンド選択の表示"
        ),
        ["MainViewModel.RubberBandX"] = (Exclusion.OutsideHistory, "ラバーバンド選択の表示"),
        ["MainViewModel.RubberBandY"] = (Exclusion.OutsideHistory, "ラバーバンド選択の表示"),
        ["MainViewModel.RubberBandWidth"] = (Exclusion.OutsideHistory, "ラバーバンド選択の表示"),
        ["MainViewModel.RubberBandHeight"] = (Exclusion.OutsideHistory, "ラバーバンド選択の表示"),
    };

    private static readonly Type[] ViewModelTypes =
    [
        typeof(EntityViewModel),
        typeof(ColumnViewModel),
        typeof(RelationshipViewModel),
        typeof(UniqueConstraintViewModel),
        typeof(UniqueConstraintMemberViewModel),
        typeof(RelationshipColumnPairViewModel),
        typeof(PrimaryKeyMemberViewModel),
        typeof(MainViewModel),
    ];

    /// <summary>
    /// テスト用の図（親子 2 テーブル＋リレーション 1 本＋一意制約 1 本に、子の表の手で外部キーの印を付けた列
    /// Child.OtherId〔どの列ペアにもいない〕と、その列を子列にできる親 Other を加えたもの）と、その ViewModel
    /// </summary>
    /// <remarks>
    /// 手で付けた印の列を初期図に置くのは、その列を子列にする操作（リレーションの作成・列ペアの変更）の Undo で
    /// 印が消えないことを、全数の往復と同じ物差しで確かめるため（全テストがこの列を含む図で流れる）。
    /// </remarks>
    private sealed class Fixture
    {
        public required MainViewModel Vm { get; init; }

        public required EntityViewModel Parent { get; init; }

        public required EntityViewModel Child { get; init; }

        public required EntityViewModel Other { get; init; }

        public required ColumnViewModel ChildOtherId { get; init; }

        public required ColumnViewModel ParentName { get; init; }

        public required RelationshipViewModel Relationship { get; init; }

        public required UniqueConstraintViewModel Unique { get; init; }
    }

    /// <summary>図を組み立てて ViewModel へ読み込む（履歴は空・未保存の変更なし）</summary>
    private Fixture Build(
        RelationshipType type = RelationshipType.OneToMany,
        ForeignKeyReferentialAction onDelete = ForeignKeyReferentialAction.Cascade,
        ForeignKeyReferentialAction onUpdate = ForeignKeyReferentialAction.NoAction,
        Action<Column>? tweakParentName = null
    )
    {
        var parentId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var parentName = new Column
        {
            Name = "Name",
            DataType = "nvarchar(50)",
            IsNullable = true,
        };
        tweakParentName?.Invoke(parentName);
        var parent = new Entity { TableName = "Parent" };
        parent.Columns.Add(parentId);
        parent.Columns.Add(parentName);
        parent.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_Parent_Name", ColumnIds = { parentName.Id } }
        );

        var childId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childParentId = new Column
        {
            Name = "ParentId",
            DataType = "int",
            IsNullable = true,
        };
        // 手で外部キーの印を付けた列（どの列ペアにもいない＝リレーションの管理下にない）
        var childOtherId = new Column
        {
            Name = "OtherId",
            DataType = "int",
            IsNullable = true,
            IsForeignKey = true,
        };
        var child = new Entity { TableName = "Child" };
        child.Columns.Add(childId);
        child.Columns.Add(childParentId);
        child.Columns.Add(childOtherId);

        var other = new Entity { TableName = "Other" };
        other.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );

        var relationship = new Relationship
        {
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = type,
            ConstraintName = "FK_Child_Parent",
            OnDelete = onDelete,
            OnUpdate = onUpdate,
        };

        if (type != RelationshipType.ManyToMany)
        {
            relationship.ColumnPairs.Add(
                new RelationshipColumnPair
                {
                    SourceColumnId = parentId.Id,
                    TargetColumnId = childParentId.Id,
                }
            );
        }

        var path = Path.Combine(_folder, "Doc.json");
        var vm = new MainViewModel(
            new StubDialogService(),
            files: new RecordingFileDialogService { SaveResult = new(path, 1) }
        );
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(_folder),
            Path.Combine(_folder, "last_diagram.json")
        );
        vm.DisableFileWatchingForTests();
        vm.ReplaceDiagramFromModule(
            new ErDiagram { Entities = { parent, child, other }, Relationships = { relationship } }
        );

        // 読み込み直後の状態を「保存済み」にして、未保存の判定を変更の有無だけで見られるようにする
        vm.SaveCommand.Execute(null);
        vm.IsDirty.Should().BeFalse("保存直後は未保存でない");
        vm.UndoRedo.CanUndo.Should().BeFalse("読み込み直後の履歴は空");

        var parentVm = vm.Entities.Single(e => e.TableName == "Parent");
        var childVm = vm.Entities.Single(e => e.TableName == "Child");

        return new Fixture
        {
            Vm = vm,
            Parent = parentVm,
            Child = childVm,
            Other = vm.Entities.Single(e => e.TableName == "Other"),
            ChildOtherId = childVm.Columns.Single(c => c.Name == "OtherId"),
            ParentName = parentVm.Columns.Single(c => c.Name == "Name"),
            Relationship = vm.Relationships.Single(),
            Unique = parentVm.UniqueConstraints.Single(),
        };
    }

    /// <summary>図全体の保存内容（意味モデル＋レイアウト）を文字列にして、往復の前後を比べる物差しにする</summary>
    private static string Snapshot(MainViewModel vm) => JsonSerializer.Serialize(vm.ToDocument());

    /// <summary>ケース ID に対応する対象オブジェクトを選ぶ</summary>
    private static object Select(Fixture fx, string typeName) =>
        typeName switch
        {
            nameof(EntityViewModel) => fx.Parent,
            nameof(ColumnViewModel) => fx.ParentName,
            nameof(RelationshipViewModel) => fx.Relationship,
            nameof(UniqueConstraintViewModel) => fx.Unique,
            _ => throw new ArgumentOutOfRangeException(nameof(typeName), typeName, null),
        };

    /// <summary>現在値と異なる値を作る</summary>
    private static object Alternate(PropertyInfo property, object? current)
    {
        var type = property.PropertyType;

        if (type == typeof(bool))
        {
            return !(bool)current!;
        }

        if (type == typeof(string))
        {
            return property.Name switch
            {
                nameof(ColumnViewModel.DataType) => "bigint",
                nameof(EntityViewModel.TitleBackgroundColor) => "#112233",
                _ => (string?)current + "X",
            };
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToList();

            return values[(values.IndexOf(current!) + 1) % values.Count];
        }

        throw new NotSupportedException($"{type.Name} の代替値の作り方が未定義: {property.Name}");
    }

    public static TheoryData<string> TrackedCases()
    {
        var data = new TheoryData<string>();

        foreach (var id in Tracked)
        {
            data.Add(id);
        }

        return data;
    }

    [Fact(
        DisplayName = "公開・internal の読み書きプロパティは、追跡する宣言か履歴の外の宣言のどちらかに必ず載っている"
    )]
    public void EveryPublicSettableProperty_IsClassified()
    {
        var actual = ViewModelTypes
            .SelectMany(type =>
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.SetMethod is { IsPublic: true } or { IsAssembly: true })
                    .Select(p => $"{type.Name}.{p.Name}")
            )
            .ToHashSet();
        var declared = Tracked.Concat(Excluded.Keys).ToHashSet();

        actual
            .Except(declared)
            .Order(StringComparer.Ordinal)
            .ToList()
            .Should()
            .BeEmpty("分類されていないプロパティは履歴に載せるか除外するかを決める");
        declared
            .Except(actual)
            .Order(StringComparer.Ordinal)
            .ToList()
            .Should()
            .BeEmpty("存在しないプロパティの宣言が残っている");
        Tracked.Intersect(Excluded.Keys).Should().BeEmpty("追跡と除外の両方には載せない");
    }

    [Fact(DisplayName = "追跡する宣言は、トラッカーの追跡一覧と一致する")]
    public void TrackedDeclaration_MatchesTrackerLists()
    {
        var fields = new Dictionary<string, string>
        {
            ["TrackedEntityProperties"] = nameof(EntityViewModel),
            ["TrackedColumnProperties"] = nameof(ColumnViewModel),
            ["TrackedRelationshipProperties"] = nameof(RelationshipViewModel),
            ["TrackedUniqueConstraintProperties"] = nameof(UniqueConstraintViewModel),
        };
        var fromTracker = new HashSet<string>();

        foreach (var (field, owner) in fields)
        {
            var list = (System.Collections.IEnumerable)
                typeof(DiagramChangeTracker)
                    .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetValue(null)!;

            foreach (var item in list)
            {
                var name = (string)item.GetType().GetProperty("Name")!.GetValue(item)!;

                // 列ペアの擬似プロパティ名はスナップショット専用で、実在のプロパティではない
                if (name != "ColumnPairs(snapshot)")
                {
                    fromTracker.Add($"{owner}.{name}");
                }
            }
        }

        fromTracker.Should().BeEquivalentTo(Tracked);
    }

    [Theory(
        DisplayName = "追跡するプロパティの変更は、履歴が 1 件積まれ、Undo 1 回で戻り、Redo で進み、未保存になる"
    )]
    [MemberData(nameof(TrackedCases))]
    public void TrackedProperty_ChangeUndoRedo(string id)
    {
        // 連動する 2 つ（主キー・種別）は組合せの全数テストが見る。ここでは単独の変更を見る
        if (id is "ColumnViewModel.IsPrimaryKey" or "RelationshipViewModel.Type")
        {
            return;
        }

        UndoKnownFailures.Run(
            id,
            () =>
            {
                var (typeName, propertyName) = Split(id);
                var fx = Build();
                var target = Select(fx, typeName);
                var property = target.GetType().GetProperty(propertyName)!;
                var original = property.GetValue(target);
                var changed = Alternate(property, original);

                var before = Snapshot(fx.Vm);
                var generation = fx.Vm.UndoRedo.ChangeGeneration;
                fx.Vm.IsDirty.Should().BeFalse("読み込み直後は未保存でない");

                property.SetValue(target, changed);

                property
                    .GetValue(target)
                    .Should()
                    .Be(changed, "セッターが値を握り潰していない前提");
                fx.Vm.UndoRedo.CanUndo.Should().BeTrue("変更が履歴に積まれる");
                fx.Vm.UndoRedo.ChangeGeneration.Should().BeGreaterThan(generation);
                fx.Vm.IsDirty.Should().BeTrue("変更すると未保存になる");

                var after = Snapshot(fx.Vm);
                after.Should().NotBe(before);

                fx.Vm.UndoCommand.Execute(null);

                property.GetValue(target).Should().Be(original, "Undo 1 回で元の値に戻る");
                Snapshot(fx.Vm).Should().Be(before, "Undo 1 回で図全体が元に戻る");
                fx.Vm.UndoRedo.CanUndo.Should().BeFalse("履歴は 1 件だけ");

                fx.Vm.RedoCommand.Execute(null);

                property.GetValue(target).Should().Be(changed);
                Snapshot(fx.Vm).Should().Be(after);
                fx.Vm.UndoRedo.CanRedo.Should().BeFalse();
            }
        );
    }

    [Fact(DisplayName = "主キーの ON/OFF は NULL 許容の連動を含めて 1 履歴で往復する（全組合せ）")]
    public void PrimaryKeyToggle_IsOneHistoryEntry_ForEveryCombination()
    {
        var problems = new List<string>();

        foreach (var (pk, nullable) in new[] { (false, true), (false, false), (true, false) })
        {
            UndoKnownFailures.Run(
                $"PrimaryKey:pk={pk},nullable={nullable}",
                () =>
                {
                    var fx = Build(tweakParentName: c =>
                    {
                        c.IsPrimaryKey = pk;
                        c.IsNullable = nullable;
                    });
                    var before = Snapshot(fx.Vm);

                    fx.ParentName.IsPrimaryKey = !pk;

                    var after = Snapshot(fx.Vm);
                    after.Should().NotBe(before);
                    fx.Vm.UndoRedo.CanUndo.Should().BeTrue();

                    fx.Vm.UndoCommand.Execute(null);

                    fx.ParentName.IsPrimaryKey.Should().Be(pk);
                    fx.ParentName.IsNullable.Should().Be(nullable);
                    Snapshot(fx.Vm).Should().Be(before);
                    fx.Vm.UndoRedo.CanUndo.Should().BeFalse("履歴は 1 件だけ");

                    fx.Vm.RedoCommand.Execute(null);

                    Snapshot(fx.Vm).Should().Be(after);
                    fx.Vm.UndoRedo.CanRedo.Should().BeFalse();
                },
                problems
            );
        }

        UndoKnownFailures.ThrowIfAny(problems);
    }

    [Fact(
        DisplayName = "主キーの間は NULL 許容の切り替えを受け付けず、値の変わらない履歴も積まない"
    )]
    public void NullableToggleOnPrimaryKey_IsSwallowedWithoutHistory()
    {
        UndoKnownFailures.Run(
            "NullableOnPrimaryKey",
            () =>
            {
                var fx = Build();
                var primaryKey = fx.Parent.Columns.Single(c => c.IsPrimaryKey);
                var generation = fx.Vm.UndoRedo.ChangeGeneration;

                primaryKey.IsNullable = true;

                primaryKey.IsNullable.Should().BeFalse("主キーは NULL 不可のまま");
                fx.Vm.UndoRedo.CanUndo.Should().BeFalse("値の変わらない履歴は積まない");
                fx.Vm.UndoRedo.ChangeGeneration.Should().Be(generation);
                fx.Vm.IsDirty.Should().BeFalse();
            }
        );
    }

    [Fact(
        DisplayName = "リレーションの種別変更は、参照アクション・列ペアの連動を含めて 1 履歴で往復する（全組合せ）"
    )]
    public void RelationshipTypeChange_IsOneHistoryEntry_ForEveryCombination()
    {
        var problems = new List<string>();
        var actions = Enum.GetValues<ForeignKeyReferentialAction>();
        var types = Enum.GetValues<RelationshipType>();
        var coverage = new CoverageCounter(RelationshipTypeChangeDimensions.Table);

        foreach (var from in types)
        {
            foreach (var to in types.Where(t => t != from))
            {
                // 多対多は参照アクションを設定できない（既定値のみ）ため、元が多対多の組は既定値だけを見る
                var values =
                    from == RelationshipType.ManyToMany
                        ? [ForeignKeyReferentialAction.NoAction]
                        : actions;

                foreach (var onDelete in values)
                {
                    foreach (var onUpdate in values)
                    {
                        // 組み合わせを数えるのは検証本体の外（台帳は本体の例外を受け止めるので、宣言の誤りを混ぜない）
                        RelationshipTypeChangeDimensions.Observe(
                            coverage,
                            from,
                            to,
                            onDelete,
                            onUpdate
                        );
                        UndoKnownFailures.Run(
                            $"RelationshipType:{from}->{to}(delete={onDelete},update={onUpdate})",
                            () =>
                            {
                                var fx = Build(from, onDelete, onUpdate);
                                var before = Snapshot(fx.Vm);

                                fx.Relationship.Type = to;

                                var after = Snapshot(fx.Vm);
                                after.Should().NotBe(before);

                                fx.Vm.UndoCommand.Execute(null);

                                fx.Relationship.Type.Should().Be(from);
                                fx.Relationship.OnDelete.Should().Be(onDelete);
                                fx.Relationship.OnUpdate.Should().Be(onUpdate);
                                Snapshot(fx.Vm)
                                    .Should()
                                    .Be(
                                        before,
                                        "Undo 1 回で種別・参照アクション・列ペアが元に戻る"
                                    );
                                fx.Vm.UndoRedo.CanUndo.Should().BeFalse("履歴は 1 件だけ");

                                fx.Vm.RedoCommand.Execute(null);

                                Snapshot(fx.Vm).Should().Be(after);
                                fx.Vm.UndoRedo.CanRedo.Should().BeFalse();
                            },
                            problems
                        );
                    }
                }
            }
        }

        // 網羅の宣言の誤りは組み合わせの結果より先に赤にする
        var coverageReport = coverage.Report();
        coverageReport.WriteSummary();
        coverageReport.WriteGapUsage(
            RelationshipTypeChangeDimensions.Gaps,
            RelationshipTypeChangeDimensions.Scope
        );

        UndoKnownFailures.ThrowIfAny(problems);

        // 網羅しない組は、組み合わせの結果を表明してから照合する（網羅の赤が組み合わせの失敗を隠さない）
        coverageReport.AssertGaps(
            RelationshipTypeChangeDimensions.Gaps,
            RelationshipTypeChangeDimensions.Scope
        );
    }

    [Fact(
        DisplayName = "主キーを切り替えたあとの同じ列の NULL 許容の切り替えは、別の 1 履歴になり Undo 1 回で戻る"
    )]
    public void NullableToggle_AfterPrimaryKeyToggle_IsSeparateUndoEntry() =>
        UndoKnownFailures.Run(
            nameof(NullableToggle_AfterPrimaryKeyToggle_IsSeparateUndoEntry),
            () =>
            {
                var fx = Build();

                // 主キーを ON→OFF にする（NULL 許容は連動して落ちたまま）
                fx.ParentName.IsPrimaryKey = true;
                fx.ParentName.IsPrimaryKey = false;
                fx.ParentName.IsNullable.Should().BeFalse();
                var before = Snapshot(fx.Vm);

                fx.ParentName.IsNullable = true;

                var after = Snapshot(fx.Vm);
                after.Should().NotBe(before);

                fx.Vm.UndoCommand.Execute(null);

                fx.ParentName.IsNullable.Should()
                    .BeFalse("Undo 1 回で NULL 許容の切り替えだけが戻る");
                fx.ParentName.IsPrimaryKey.Should().BeFalse();
                Snapshot(fx.Vm).Should().Be(before);

                fx.Vm.RedoCommand.Execute(null);

                Snapshot(fx.Vm).Should().Be(after);
            }
        );

    [Fact(
        DisplayName = "種別を変えたあとの同じリレーションの参照アクションの変更は、別の 1 履歴になり Undo 1 回で戻る"
    )]
    public void ReferentialAction_AfterTypeChange_IsSeparateUndoEntry() =>
        UndoKnownFailures.Run(
            nameof(ReferentialAction_AfterTypeChange_IsSeparateUndoEntry),
            () =>
            {
                var fx = Build(RelationshipType.OneToMany, ForeignKeyReferentialAction.Cascade);

                // 参照アクションが残る遷移（1 対多→1 対 1）
                fx.Relationship.Type = RelationshipType.OneToOne;
                fx.Relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
                var before = Snapshot(fx.Vm);

                fx.Relationship.OnDelete = ForeignKeyReferentialAction.SetNull;

                var after = Snapshot(fx.Vm);
                after.Should().NotBe(before);

                fx.Vm.UndoCommand.Execute(null);

                fx.Relationship.OnDelete.Should()
                    .Be(
                        ForeignKeyReferentialAction.Cascade,
                        "Undo 1 回で参照アクションの変更だけが戻る"
                    );
                fx.Relationship.Type.Should().Be(RelationshipType.OneToOne);
                Snapshot(fx.Vm).Should().Be(before);

                fx.Vm.RedoCommand.Execute(null);

                Snapshot(fx.Vm).Should().Be(after);
            }
        );

    [Fact(
        DisplayName = "外部キー印の切り替えを Undo / Redo すると、リレーションに基づく列のルールが再適用される"
    )]
    public void ForeignKeyToggle_UndoRedo_ReappliesRelationshipRules() =>
        UndoKnownFailures.Run(
            nameof(ForeignKeyToggle_UndoRedo_ReappliesRelationshipRules),
            () =>
            {
                var fx = Build();

                fx.ParentName.IsForeignKey = true;

                var afterToggle = fx.Vm.RelationshipRuleApplicationCountForTests;
                fx.Vm.UndoCommand.Execute(null);
                var afterUndo = fx.Vm.RelationshipRuleApplicationCountForTests;
                fx.Vm.RedoCommand.Execute(null);
                var afterRedo = fx.Vm.RelationshipRuleApplicationCountForTests;

                afterUndo.Should().BeGreaterThan(afterToggle, "Undo のあとにルールを再適用する");
                afterRedo.Should().BeGreaterThan(afterUndo, "Redo のあとにルールを再適用する");
            }
        );

    /// <summary>
    /// 手で外部キーの印を付けた列（Child.OtherId）を子列にする操作を実行し、Undo 1 回で印ごと図全体が元に戻ること、
    /// Redo の後の印は控えた値でなくリレーションの列のルールが決めること（操作の直後と同じ）を確かめる
    /// </summary>
    /// <param name="fx">テスト用の図</param>
    /// <param name="makeChild">OtherId を子列にする操作（履歴に 1 件積む）</param>
    private static void AssertManualForeignKeyMarkSurvivesUndo(Fixture fx, Action makeChild)
    {
        var column = fx.ChildOtherId;
        column.IsForeignKey.Should().BeTrue("手で外部キーの印を付けてある");
        column.IsForeignKeyManagedByRelationship.Should().BeFalse("どの列ペアにもいない");
        var before = Snapshot(fx.Vm);

        makeChild();

        fx.Vm.UndoRedo.CanUndo.Should().BeTrue("子列にする操作が履歴に積まれる");
        var after = Snapshot(fx.Vm);
        after.Should().NotBe(before);
        var marksAfter = (column.IsForeignKey, column.IsForeignKeyManagedByRelationship);
        marksAfter.Should().Be((true, true), "子列になった列の印はリレーションの管理になる");

        fx.Vm.UndoCommand.Execute(null);

        column.IsForeignKey.Should().BeTrue("Undo で手で付けた印へ戻る");
        column.IsForeignKeyManagedByRelationship.Should().BeFalse("Undo で管理外へ戻る");
        Snapshot(fx.Vm).Should().Be(before, "Undo 1 回で図全体が元に戻る");
        fx.Vm.UndoRedo.CanUndo.Should().BeFalse("履歴は 1 件だけ");

        fx.Vm.RedoCommand.Execute(null);

        Snapshot(fx.Vm).Should().Be(after);
        (column.IsForeignKey, column.IsForeignKeyManagedByRelationship)
            .Should()
            .Be(marksAfter, "Redo の後の印は控えた値でなくルールの再適用が決める");
        fx.Vm.UndoRedo.CanRedo.Should().BeFalse();
    }

    [Fact(
        DisplayName = "手で外部キーの印を付けた列を子列にするリレーションの作成（画面の操作）は、Undo 1 回で印ごと元に戻る"
    )]
    public void ManualForeignKeyMark_SurvivesUndoOfRelationshipCreation() =>
        UndoKnownFailures.Run(
            nameof(ManualForeignKeyMark_SurvivesUndoOfRelationshipCreation),
            () =>
            {
                var fx = Build();

                AssertManualForeignKeyMarkSurvivesUndo(
                    fx,
                    () =>
                    {
                        // Other の主キー Id は、名前の一致で Child.OtherId と組になる
                        fx.Vm.StartAddOneToManyCommand.Execute(null);
                        fx.Vm.OnEntityClicked(fx.Other);
                        fx.Vm.OnEntityClicked(fx.Child);
                        fx.Vm.Relationships.Should().HaveCount(2, "リレーションが作られる");
                        fx.Vm.Relationships[1]
                            .ColumnPairs.Should()
                            .ContainSingle()
                            .Which.TargetColumnId.Should()
                            .Be(fx.ChildOtherId.Id, "子列は手で印を付けた列");
                    }
                );
            }
        );

    [Fact(
        DisplayName = "手で外部キーの印を付けた列を子列にするリレーションの作成（内蔵チャットのツール）は、Undo 1 回で印ごと元に戻る"
    )]
    public void ManualForeignKeyMark_SurvivesUndoOfToolRelationshipCreation() =>
        UndoKnownFailures.Run(
            nameof(ManualForeignKeyMark_SurvivesUndoOfToolRelationshipCreation),
            () =>
            {
                var fx = Build();

                AssertManualForeignKeyMarkSurvivesUndo(
                    fx,
                    () =>
                        ErDiagramDynamicTools
                            .Execute(
                                "add_relationship",
                                JsonSerializer.SerializeToElement(
                                    new
                                    {
                                        source_table = "Other",
                                        target_table = "Child",
                                        relationship_type = "OneToMany",
                                        source_columns = new[] { "Id" },
                                        target_columns = new[] { "OtherId" },
                                    }
                                ),
                                fx.Vm
                            )
                            .Success.Should()
                            .BeTrue()
                );
            }
        );

    [Fact(
        DisplayName = "手で外部キーの印を付けた列へ列ペアの子列を差し替える操作は、Undo 1 回で印ごと元に戻る"
    )]
    public void ManualForeignKeyMark_SurvivesUndoOfColumnPairChange() =>
        UndoKnownFailures.Run(
            nameof(ManualForeignKeyMark_SurvivesUndoOfColumnPairChange),
            () =>
            {
                var fx = Build();

                AssertManualForeignKeyMarkSurvivesUndo(
                    fx,
                    () =>
                    {
                        // 列ペアの行の子列を ParentId から OtherId へ選び直す（画面のコンボボックスの選択と同じ経路）
                        fx.Relationship.ColumnPairRows[0].SelectedTargetColumn = fx.ChildOtherId;
                        fx.Relationship.ColumnPairs.Should()
                            .ContainSingle()
                            .Which.TargetColumnId.Should()
                            .Be(fx.ChildOtherId.Id);
                    }
                );
            }
        );

    private static (string TypeName, string Property) Split(string id)
    {
        var dot = id.IndexOf('.');

        return (id[..dot], id[(dot + 1)..]);
    }
}
