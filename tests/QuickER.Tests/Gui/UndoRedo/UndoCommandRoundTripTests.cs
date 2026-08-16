using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using QuickER.UndoRedo;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>
/// <see cref="IUndoableCommand"/> の全実装へ「往復不変式」を 1 本の性質テストとして掛けるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// Undo コマンドは同型の実装が並ぶファミリーで、個別テストを足すだけでは N+1 個目が網に入らない。
/// ここはリフレクションで <b>QuickER.Gui アセンブリの具象 <see cref="IUndoableCommand"/> 実装を全列挙</b>し、
/// シナリオ未登録の型があれば型名を名指しして落とす。新しいコマンドを足すと、シナリオを宣言しない限り赤くなる。
/// </para>
/// <para>
/// 検証する性質（各コマンド共通）:
/// </para>
/// <list type="number">
///   <item>s0 = 実行前の状態</item>
///   <item><c>Execute()</c> → s1</item>
///   <item><c>Undo()</c> → 状態が s0 と一致する</item>
///   <item>再 <c>Execute()</c>（Redo と同じ経路）→ 状態が s1 と一致する</item>
///   <item>もう 1 往復（Undo → s0・Execute → s1）＝ 2 往復目でも劣化しない</item>
/// </list>
/// <para>
/// 状態の物差し（スナップショット）は各シナリオが宣言する。既定は「コマンドが触る意味モデル＋視覚/VM 状態」で、
/// <b>エンティティ・リレーションシップは Id 順に正規化</b>し（キャンバス上の集合であって並びに意味がない。
/// 実際 <c>RemoveEntityCommand</c> の Undo は末尾へ再追加する）、<b>列・UNIQUE 制約の構成列は宣言順のまま</b>
/// 比較する（並びが DDL の意味そのもの）。
/// </para>
/// <para>
/// 既存の個別テスト（<see cref="CommandTests"/> 等）は削除しない。性質テストは「網」、個別テストは「意図の名指し」で、
/// 役割が違う（性質テストは往復することしか言わず、何がどう変わるべきかは言わない）。
/// </para>
/// </remarks>
public class UndoCommandRoundTripTests
{
    /// <summary>1 シナリオ＝検証対象のコマンドと、その状態を文字列化する物差し</summary>
    /// <param name="Command">検証対象のコマンド（未実行の状態で渡す）</param>
    /// <param name="Snapshot">現在の状態を文字列化する関数（同じ状態なら同じ文字列になること）</param>
    private sealed record UndoScenario(IUndoableCommand Command, Func<string> Snapshot);

    /// <summary>コマンド型 → シナリオ組み立て。ここに無い具象コマンドがあればテストが落ちる</summary>
    private static readonly IReadOnlyDictionary<Type, Func<UndoScenario>> Scenarios =
        new Dictionary<Type, Func<UndoScenario>>
        {
            [typeof(AddEntityCommand)] = BuildAddEntity,
            [typeof(RemoveEntityCommand)] = BuildRemoveEntity,
            [typeof(AddRelationshipCommand)] = BuildAddRelationship,
            [typeof(RemoveRelationshipCommand)] = BuildRemoveRelationship,
            [typeof(AddColumnCommand)] = BuildAddColumn,
            [typeof(RemoveColumnCommand)] = BuildRemoveColumn,
            [typeof(MoveColumnOrderCommand)] = BuildMoveColumnOrder,
            [typeof(MoveEntityCommand)] = BuildMoveEntity,
            [typeof(GroupMoveEntitiesCommand)] = BuildGroupMoveEntities,
            [typeof(GroupRemoveEntitiesCommand)] = BuildGroupRemoveEntities,
            [typeof(GroupChangeTitleColorCommand)] = BuildGroupChangeTitleColor,
            [typeof(DuplicateEntityCommand)] = BuildDuplicateEntity,
            [typeof(ImportSchemaCommand)] = BuildImportSchema,
            [typeof(ArrangeEntitiesCommand)] = BuildArrangeEntities,
            [typeof(PropertyChangeCommand)] = BuildPropertyChange,
            [typeof(SnapshotChangeCommand)] = BuildSnapshotChange,
            [typeof(AddUniqueConstraintCommand)] = BuildAddUniqueConstraint,
            [typeof(RemoveUniqueConstraintCommand)] = BuildRemoveUniqueConstraint,
            [typeof(ChangeUniqueConstraintColumnsCommand)] = BuildChangeUniqueConstraintColumns,
            [typeof(ChangeRelationshipColumnPairsCommand)] = BuildChangeRelationshipColumnPairs,
            [typeof(ChangeTargetDbmsCommand)] = BuildChangeTargetDbms,
        };

    /// <summary>QuickER.Gui アセンブリに実在する具象 <see cref="IUndoableCommand"/> 実装をすべて返す</summary>
    private static IReadOnlyList<Type> ConcreteCommandTypes() =>
        typeof(IUndoableCommand)
            .Assembly.GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && typeof(IUndoableCommand).IsAssignableFrom(type)
            )
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>xUnit へ渡すケース（型の完全名。失敗表示にコマンド名が出る）</summary>
    public static TheoryData<string> CommandTypeNames()
    {
        var data = new TheoryData<string>();

        foreach (var type in ConcreteCommandTypes().Where(Scenarios.ContainsKey))
        {
            data.Add(type.FullName!);
        }

        return data;
    }

    /// <summary>
    /// 具象 <see cref="IUndoableCommand"/> 実装がすべてシナリオ登録されていることを検証する（網の目そのもの）。
    /// </summary>
    [Fact(DisplayName = "IUndoableCommand の具象実装がすべて往復シナリオへ登録されている")]
    public void EveryCommand_ShouldHaveRoundTripScenario()
    {
        var actual = ConcreteCommandTypes();

        var unregistered = actual.Where(type => !Scenarios.ContainsKey(type)).ToList();
        var stale = Scenarios.Keys.Where(type => !actual.Contains(type)).ToList();

        unregistered
            .Should()
            .BeEmpty(
                "往復シナリオが未登録の Undo コマンドがある（UndoCommandRoundTripTests.Scenarios へ追加すること）: "
                    + string.Join(", ", unregistered.Select(type => type.Name))
            );
        stale
            .Should()
            .BeEmpty(
                "実在しないコマンド型のシナリオが残っている: "
                    + string.Join(", ", stale.Select(type => type.Name))
            );

        actual
            .Should()
            .HaveCountGreaterThan(15, "Undo コマンドのファミリーが丸ごと消えていないこと");
    }

    /// <summary>
    /// 各コマンドについて Execute → Undo → Redo → Undo → Redo の 2 往復で状態が劣化しないことを検証する。
    /// </summary>
    [Theory(DisplayName = "Undo コマンドは Execute / Undo / Redo の 2 往復で状態が往復する")]
    [MemberData(nameof(CommandTypeNames))]
    public void Command_ShouldRoundTrip(string commandTypeName)
    {
        var type = ConcreteCommandTypes()
            .Single(candidate => candidate.FullName == commandTypeName);
        var scenario = Scenarios[type]();
        var command = scenario.Command;

        command.Should().BeOfType(type, $"{type.Name} のシナリオが別のコマンドを組み立てている");
        command
            .Description.Should()
            .NotBeNullOrWhiteSpace(
                $"{type.Name} の Description は履歴 UI に出るため空であってはならない"
            );

        var before = scenario.Snapshot();

        command.Execute();
        var after = scenario.Snapshot();

        after
            .Should()
            .NotBe(
                before,
                $"{type.Name} の Execute が状態を何も変えていない（シナリオが実際の変更を起こしていない）"
            );

        command.Undo();
        scenario.Snapshot().Should().Be(before, $"{type.Name}: Undo 後の状態が実行前と一致しない");

        // Redo は UndoRedoManager が Execute を呼び直す経路そのもの
        command.Execute();
        scenario
            .Snapshot()
            .Should()
            .Be(after, $"{type.Name}: Redo 後の状態が Execute 直後と一致しない");

        // 2 往復目（1 回目だけ偶然一致する実装＝キャッシュの取り違え等を落とす）
        command.Undo();
        scenario
            .Snapshot()
            .Should()
            .Be(before, $"{type.Name}: 2 往復目の Undo 後の状態が実行前と一致しない");

        command.Execute();
        scenario
            .Snapshot()
            .Should()
            .Be(after, $"{type.Name}: 2 往復目の Redo 後の状態が Execute 直後と一致しない");
    }

    // ------------------------------------------------------------------
    // シナリオ組み立て
    // ------------------------------------------------------------------

    private static UndoScenario BuildAddEntity()
    {
        var main = new MainViewModel();
        var entity = NewEntity("Added", 10, 20);

        return new UndoScenario(new AddEntityCommand(main, entity), () => Describe(main));
    }

    private static UndoScenario BuildRemoveEntity()
    {
        var main = new MainViewModel();
        var (parent, child) = AddRelatedEntities(main);

        return new UndoScenario(new RemoveEntityCommand(main, parent), () => Describe(main));
    }

    private static UndoScenario BuildAddRelationship()
    {
        var main = new MainViewModel();
        var parent = NewEntity("Parent");
        var child = NewEntity("Child");
        main.Entities.Add(parent);
        main.Entities.Add(child);

        var relationship = NewRelationship(parent, child);

        return new UndoScenario(
            new AddRelationshipCommand(main, relationship),
            () => Describe(main)
        );
    }

    private static UndoScenario BuildRemoveRelationship()
    {
        var main = new MainViewModel();
        var (_, _) = AddRelatedEntities(main);

        return new UndoScenario(
            new RemoveRelationshipCommand(main, main.Relationships[0]),
            () => Describe(main)
        );
    }

    private static UndoScenario BuildAddColumn()
    {
        var entity = NewEntity("T");
        var column = new ColumnViewModel(
            new Column
            {
                Id = Guid.NewGuid(),
                Name = "Added",
                DataType = "int",
            }
        );

        // 途中位置への挿入（末尾追加だけでは「index を覚えているか」が検証されない）
        return new UndoScenario(
            new AddColumnCommand(entity.Columns, column, index: 1),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildRemoveColumn()
    {
        var main = new MainViewModel();
        var (parent, child) = AddRelatedEntities(main);
        var target = child.Columns[1];

        // 列削除は「列＋その列を含む UNIQUE 制約＋その列を使うリレーションの列ペア」を 1 履歴で巻き戻す
        child.UniqueConstraints.Add(
            new UniqueConstraintViewModel(
                child,
                new UniqueConstraint
                {
                    Id = Guid.NewGuid(),
                    Name = "UQ_child_code",
                    ColumnIds = { target.Id },
                }
            )
        );

        var affected = main.Relationships.ToList();

        return new UndoScenario(
            new RemoveColumnCommand(child, target, affected, () => { }),
            () => Describe(main)
        );
    }

    private static UndoScenario BuildMoveColumnOrder()
    {
        var entity = NewEntity("T");

        return new UndoScenario(
            new MoveColumnOrderCommand(entity.Columns, entity.Columns[0], newIndex: 2),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildMoveEntity()
    {
        var entity = NewEntity("T", 10, 20);

        return new UndoScenario(
            new MoveEntityCommand(entity, 10, 20, 130, 240),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildGroupMoveEntities()
    {
        var first = NewEntity("A", 10, 10);
        var second = NewEntity("B", 50, 60);

        var moves = new List<(EntityViewModel, double, double, double, double)>
        {
            (first, 10, 10, 40, 50),
            (second, 50, 60, 80, 100),
        };

        return new UndoScenario(
            new GroupMoveEntitiesCommand(moves),
            () => Describe(first) + Describe(second)
        );
    }

    private static UndoScenario BuildGroupRemoveEntities()
    {
        var main = new MainViewModel();
        var (parent, child) = AddRelatedEntities(main);

        // このコマンドの Undo は SelectedEntity を先頭の対象へ戻す（Execute は選択に触らない）非対称を持つ。
        // 選択もユーザーに見える状態なので物差しへ含め、その非対称が成立する初期状態から検証する。
        main.SelectedEntity = parent;

        return new UndoScenario(
            new GroupRemoveEntitiesCommand(main, [parent, child]),
            () => Describe(main)
        );
    }

    private static UndoScenario BuildGroupChangeTitleColor()
    {
        var first = NewEntity("A");
        var second = NewEntity("B");

        var changes = new List<(EntityViewModel, string, string)>
        {
            (first, first.TitleBackgroundColor, "#FF0000"),
            (second, second.TitleBackgroundColor, "#00FF00"),
        };

        // 本番は変更追跡の抑止デリゲートを渡す。ここでは素通しで同じ意味になる
        return new UndoScenario(
            new GroupChangeTitleColorCommand(changes, action => action()),
            () => Describe(first) + Describe(second)
        );
    }

    private static UndoScenario BuildDuplicateEntity()
    {
        var main = new MainViewModel();
        var original = NewEntity("Original", 10, 20);
        main.Entities.Add(original);

        return new UndoScenario(new DuplicateEntityCommand(main, original), () => Describe(main));
    }

    private static UndoScenario BuildImportSchema()
    {
        var main = new MainViewModel();
        AddRelatedEntities(main);

        var importedParent = NewEntityModel("ImportedParent");
        var importedChild = NewEntityModel("ImportedChild");
        var importedRelationship = new Relationship
        {
            Id = Guid.NewGuid(),
            SourceEntityId = importedParent.Id,
            TargetEntityId = importedChild.Id,
            Type = RelationshipType.OneToMany,
        };

        return new UndoScenario(
            new ImportSchemaCommand(main, [importedParent, importedChild], [importedRelationship]),
            () => Describe(main)
        );
    }

    private static UndoScenario BuildArrangeEntities()
    {
        var first = NewEntity("A", 10, 10);
        var second = NewEntity("B", 50, 60);

        var before = new Dictionary<Guid, (double X, double Y)>
        {
            [first.Id] = (first.X, first.Y),
            [second.Id] = (second.X, second.Y),
        };
        var after = new Dictionary<Guid, (double X, double Y)>
        {
            [first.Id] = (300, 400),
            [second.Id] = (500, 600),
        };

        return new UndoScenario(
            new ArrangeEntitiesCommand([first, second], before, after, null, "自動整列"),
            () => Describe(first) + Describe(second)
        );
    }

    private static UndoScenario BuildPropertyChange()
    {
        var entity = NewEntity("Before");
        var tracked = new TrackedProperty<EntityViewModel>(
            nameof(EntityViewModel.TableName),
            target => target.TableName,
            (target, value) => target.TableName = (string)value!
        );

        return new UndoScenario(
            new PropertyChangeCommand(entity, tracked, "Before", "After"),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildSnapshotChange()
    {
        var entity = NewEntity("T");

        // NULL 許容の非キー列を主キーへ昇格させる＝主キーと NULL 許容が連動して動く組合せ
        // （本番はこの連動を 1 履歴へ畳むために SnapshotChangeCommand を使う）
        var column = entity.Columns[2];
        var before = new Dictionary<string, object?>
        {
            [nameof(ColumnViewModel.IsPrimaryKey)] = column.IsPrimaryKey,
            [nameof(ColumnViewModel.IsNullable)] = column.IsNullable,
        };
        var after = new Dictionary<string, object?>
        {
            [nameof(ColumnViewModel.IsPrimaryKey)] = true,
            [nameof(ColumnViewModel.IsNullable)] = false,
        };

        return new UndoScenario(
            new SnapshotChangeCommand(column, before, after, ApplyColumnSnapshot),
            () => Describe(entity)
        );
    }

    /// <summary>スナップショットの適用（本番の <c>DiagramChangeTracker.ApplySnapshot</c> に相当する最小実装）</summary>
    private static void ApplyColumnSnapshot(
        object target,
        IReadOnlyDictionary<string, object?> values
    )
    {
        var column = (ColumnViewModel)target;

        column.IsPrimaryKey = (bool)values[nameof(ColumnViewModel.IsPrimaryKey)]!;
        column.IsNullable = (bool)values[nameof(ColumnViewModel.IsNullable)]!;
    }

    private static UndoScenario BuildAddUniqueConstraint()
    {
        var entity = NewEntity("T");
        var constraint = new UniqueConstraintViewModel(
            entity,
            new UniqueConstraint
            {
                Id = Guid.NewGuid(),
                Name = "UQ_added",
                ColumnIds = { entity.Columns[1].Id },
            }
        );

        return new UndoScenario(
            new AddUniqueConstraintCommand(entity.UniqueConstraints, constraint),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildRemoveUniqueConstraint()
    {
        var entity = NewEntity("T");
        var first = AddConstraint(entity, "UQ_first", entity.Columns[0].Id);
        var second = AddConstraint(entity, "UQ_second", entity.Columns[1].Id);

        // 先頭を消して「元の位置へ戻る」ことを見る（末尾だけだと index の記憶が検証されない）
        _ = second;

        return new UndoScenario(
            new RemoveUniqueConstraintCommand(entity.UniqueConstraints, first),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildChangeUniqueConstraintColumns()
    {
        var entity = NewEntity("T");
        var constraint = AddConstraint(entity, "UQ_target", entity.Columns[0].Id);

        return new UndoScenario(
            new ChangeUniqueConstraintColumnsCommand(
                constraint,
                [.. constraint.ColumnIds],
                [entity.Columns[1].Id, entity.Columns[2].Id]
            ),
            () => Describe(entity)
        );
    }

    private static UndoScenario BuildChangeRelationshipColumnPairs()
    {
        var main = new MainViewModel();
        var (parent, child) = AddRelatedEntities(main);
        var relationship = main.Relationships[0];

        var before = relationship.SnapshotColumnPairs();
        var after = new List<RelationshipColumnPair>
        {
            new() { SourceColumnId = parent.Columns[1].Id, TargetColumnId = child.Columns[2].Id },
        };

        return new UndoScenario(
            new ChangeRelationshipColumnPairsCommand(relationship, before, after),
            () => Describe(main)
        );
    }

    private static UndoScenario BuildChangeTargetDbms()
    {
        // rowversion（SQL Server）→ BLOB（SQLite）は、型だけでなく NOT NULL の解除も伴う唯一の変換。
        // 「型は戻るが NULL 許容が戻らない」型の欠陥をここで固定する。
        var model = new Entity
        {
            Id = Guid.NewGuid(),
            TableName = "sync_items",
            Columns =
            {
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "item_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "row_ver",
                    DataType = "rowversion",
                    IsNullable = false,
                },
            },
        };

        var diagram = new ErDiagram { TargetDbms = "sqlserver", Entities = { model } };
        var entity = new EntityViewModel(model);
        var from = new SqlServerProvider();
        var to = new SqliteProvider();

        var plan = DiagramTypeConverter.CreatePlan(diagram, from.TypeCatalog, to.TypeCatalog);

        plan.Converted.Should()
            .Contain(
                conversion => conversion.MakeNullable,
                "rowversion → BLOB は NOT NULL の解除を伴うはず（このシナリオの前提）"
            );

        IDatabaseProvider current = from;

        var command = new ChangeTargetDbmsCommand(
            from,
            to,
            plan.Converted,
            entity.Columns.ToDictionary(column => column.Id),
            provider => current = provider
        );

        return new UndoScenario(command, () => Describe(entity) + $"|provider={current.Name}");
    }

    // ------------------------------------------------------------------
    // 世界の組み立てヘルパー
    // ------------------------------------------------------------------

    /// <summary>3 列を持つテスト用エンティティ VM を作る</summary>
    private static EntityViewModel NewEntity(string tableName, double x = 0, double y = 0) =>
        new(NewEntityModel(tableName), new EntityLayout { X = x, Y = y });

    /// <summary>3 列（PK＋2 列）を持つ意味モデルのエンティティを作る</summary>
    private static Entity NewEntityModel(string tableName) =>
        new()
        {
            Id = Guid.NewGuid(),
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "code",
                    DataType = "nvarchar(50)",
                },
                new Column
                {
                    Id = Guid.NewGuid(),
                    Name = "memo",
                    DataType = "nvarchar(200)",
                    IsNullable = true,
                },
            },
        };

    /// <summary>親子 2 エンティティと 1対多リレーションを持つ図を組み立てる</summary>
    private static (EntityViewModel Parent, EntityViewModel Child) AddRelatedEntities(
        MainViewModel main
    )
    {
        var parent = NewEntity("Parent", 10, 20);
        var child = NewEntity("Child", 300, 20);

        main.Entities.Add(parent);
        main.Entities.Add(child);
        main.Relationships.Add(NewRelationship(parent, child));

        return (parent, child);
    }

    /// <summary>親の PK と子の 2 列目を結ぶ 1対多リレーションを作る</summary>
    private static RelationshipViewModel NewRelationship(
        EntityViewModel parent,
        EntityViewModel child
    )
    {
        var model = new Relationship
        {
            Id = Guid.NewGuid(),
            SourceEntityId = parent.Id,
            TargetEntityId = child.Id,
            Type = RelationshipType.OneToMany,
            ConstraintName = "FK_child_parent",
            ColumnPairs =
            {
                new RelationshipColumnPair
                {
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[1].Id,
                },
            },
        };

        return new RelationshipViewModel(model, parent, child);
    }

    /// <summary>エンティティへ UNIQUE 制約を 1 本足して返す</summary>
    private static UniqueConstraintViewModel AddConstraint(
        EntityViewModel entity,
        string name,
        params Guid[] columnIds
    )
    {
        var model = new UniqueConstraint { Id = Guid.NewGuid(), Name = name };

        foreach (var columnId in columnIds)
        {
            model.ColumnIds.Add(columnId);
        }

        var constraint = new UniqueConstraintViewModel(entity, model);
        entity.UniqueConstraints.Add(constraint);

        return constraint;
    }

    // ------------------------------------------------------------------
    // スナップショット（状態の物差し）
    // ------------------------------------------------------------------

    /// <summary>MainViewModel が保持する図の状態（エンティティ・リレーション・選択）を文字列化する</summary>
    private static string Describe(MainViewModel main)
    {
        var builder = new StringBuilder();

        // キャンバス上の集合として比較する（Undo が末尾へ再追加しても意味は変わらない）
        foreach (var entity in main.Entities.OrderBy(entity => entity.Id))
        {
            builder.Append(Describe(entity));
        }

        foreach (var relationship in main.Relationships.OrderBy(relationship => relationship.Id))
        {
            builder.Append(Describe(relationship));
        }

        builder
            .Append("selection(")
            .Append(main.SelectedEntity?.Id)
            .Append(',')
            .Append(main.SelectedRelationship?.Id)
            .Append(',')
            .Append(main.SelectedColumn?.Id)
            .Append(")\n");

        return builder.ToString();
    }

    /// <summary>エンティティの意味・視覚状態（列・UNIQUE 制約は宣言順のまま）を文字列化する</summary>
    private static string Describe(EntityViewModel entity)
    {
        var builder = new StringBuilder();

        builder
            .Append("entity(")
            .Append(entity.Id)
            .Append(',')
            .Append(entity.TableName)
            .Append(',')
            .Append(entity.X.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(entity.Y.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(entity.TitleBackgroundColor)
            .Append(',')
            .Append(entity.Memo)
            .Append(',')
            .Append(entity.Description)
            .Append(")\n");

        foreach (var column in entity.Columns)
        {
            builder
                .Append("  column(")
                .Append(column.Id)
                .Append(',')
                .Append(column.Name)
                .Append(',')
                .Append(column.DataType)
                .Append(",pk=")
                .Append(column.IsPrimaryKey)
                .Append(",fk=")
                .Append(column.IsForeignKey)
                .Append(",null=")
                .Append(column.IsNullable)
                .Append(",pkEditable=")
                .Append(column.IsPrimaryKeyEditable)
                .Append(",fkEditable=")
                .Append(column.IsForeignKeyEditable)
                .Append(",fkManaged=")
                .Append(column.IsForeignKeyManagedByRelationship)
                .Append(")\n");
        }

        foreach (var constraint in entity.UniqueConstraints)
        {
            builder
                .Append("  unique(")
                .Append(constraint.Id)
                .Append(',')
                .Append(constraint.Name)
                .Append(',')
                .Append(string.Join('/', constraint.ColumnIds))
                .Append(")\n");
        }

        return builder.ToString();
    }

    /// <summary>リレーションシップの意味状態（列ペアは宣言順）を文字列化する</summary>
    private static string Describe(RelationshipViewModel relationship)
    {
        var pairs = string.Join(
            '/',
            relationship.ColumnPairs.Select(pair => $"{pair.SourceColumnId}>{pair.TargetColumnId}")
        );

        return $"relationship({relationship.Id},{relationship.Source.Id},{relationship.Target.Id},"
            + $"{relationship.Type},{relationship.ConstraintName},{relationship.OnDelete},"
            + $"{relationship.OnUpdate},[{pairs}])\n";
    }
}
