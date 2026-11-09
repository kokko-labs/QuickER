using System.IO;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.UndoRedo;

/// <summary>操作の往復の行の図（元のテストの図のまま、1 図 1 か所）と、行の中で値を持ち回る仕組み</summary>
internal static partial class UndoOperationRows
{
    /// <summary>ViewModel ごとに、準備・表明の間で持ち回る値（同じインスタンスへ戻ることの表明に使う）</summary>
    private static readonly ConditionalWeakTable<MainViewModel, Dictionary<string, object?>> Memo =
        new();

    /// <summary>保存先のフォルダを覚えておくキー（<see cref="SaveClean"/>）</summary>
    private const string SaveFolderKey = "save-folder";

    /// <summary>値を覚える（後の準備・表明が <see cref="Recall{T}"/> で読む）</summary>
    private static void Remember(MainViewModel vm, string key, object? value) =>
        Memo.GetOrCreateValue(vm)[key] = value;

    /// <summary>覚えた値を読む</summary>
    private static T Recall<T>(MainViewModel vm, string key) => (T)Memo.GetOrCreateValue(vm)[key]!;

    /// <summary>
    /// 保存して未保存でない状態にする（保存先のフォルダは保存の間だけ作り、保存のあとに消す）
    /// </summary>
    /// <remarks>未保存になることを、未保存でない状態から確かめる行が使う（図は <see cref="SavableUniqueConstraintDiagram"/>）</remarks>
    private static void SaveClean(MainViewModel vm)
    {
        var folder = Recall<string>(vm, SaveFolderKey);
        Directory.CreateDirectory(folder);

        try
        {
            vm.SaveCommand.Execute(null);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        vm.IsDirty.Should().BeFalse("保存直後はクリーン");
    }

    /// <summary>テーブル名だけを持つテーブル（座標つき）</summary>
    private static EntityViewModel PlainEntity(string tableName, double x = 0, double y = 0) =>
        new(new Entity { TableName = tableName }, new EntityLayout { X = x, Y = y });

    /// <summary>2 つのテーブルを結ぶリレーション（列ペアなし。種別を省くとモデルの既定）を図へ足す</summary>
    private static RelationshipViewModel Link(
        MainViewModel vm,
        EntityViewModel source,
        EntityViewModel target,
        RelationshipType? type = null
    )
    {
        var model = new Relationship { SourceEntityId = source.Id, TargetEntityId = target.Id };

        if (type is { } declared)
        {
            model.Type = declared;
        }

        var relationship = new RelationshipViewModel(model, source, target);
        vm.Relationships.Add(relationship);

        return relationship;
    }

    /// <summary>A・B・C・D の 4 テーブルと、A→B・C→D・A→C の順のリレーション</summary>
    private static MainViewModel FourTablesDiagram()
    {
        var vm = new MainViewModel();

        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            vm.Entities.Add(
                new EntityViewModel(new Entity { TableName = name }, new EntityLayout())
            );
        }

        Link(vm, TableNamed(vm, "A"), TableNamed(vm, "B"));
        Link(vm, TableNamed(vm, "C"), TableNamed(vm, "D"));
        Link(vm, TableNamed(vm, "A"), TableNamed(vm, "C"));

        return vm;
    }

    /// <summary>テーブル名でテーブルを引く</summary>
    private static EntityViewModel TableNamed(MainViewModel vm, string name) =>
        vm.Entities.Single(entity => entity.TableName == name);

    /// <summary>テーブルの並びをテーブル名で返す</summary>
    private static List<string> TableOrder(MainViewModel vm) =>
        vm.Entities.Select(entity => entity.TableName).ToList();

    /// <summary>リレーションの並びを「親->子」で返す</summary>
    private static List<string> RelationshipOrder(MainViewModel vm) =>
        vm
            .Relationships.Select(relationship =>
                $"{relationship.Source.TableName}->{relationship.Target.TableName}"
            )
            .ToList();

    /// <summary>Order・Customer の 2 テーブルと、Order・Customer・Order の順のクエリ（EntityQueryRemovalCommandTests の図）</summary>
    private static MainViewModel QueryOwnersDiagram()
    {
        var vm = new MainViewModel();
        var order = new EntityViewModel(new Entity { TableName = "Order" }, new EntityLayout());
        var customer = new EntityViewModel(
            new Entity { TableName = "Customer" },
            new EntityLayout()
        );
        vm.Entities.Add(order);
        vm.Entities.Add(customer);
        vm.Queries.Add(new QueryDefinition { EntityId = order.Id, Name = "FindByCustomer" });
        vm.Queries.Add(new QueryDefinition { EntityId = customer.Id, Name = "FindByName" });
        vm.Queries.Add(new QueryDefinition { EntityId = order.Id, Name = "CountAll" });

        return vm;
    }

    /// <summary>クエリ名を並び順のまま返す</summary>
    private static List<string> QueryNames(MainViewModel vm) =>
        vm.Queries.Select(query => query.Name).ToList();

    /// <summary>テーブル名「T」の 2 テーブルと、その間のリレーション 1 本（CommandTests の図）</summary>
    private static MainViewModel LinkedPairDiagram()
    {
        var vm = new MainViewModel();
        var a = PlainEntity("T");
        var b = PlainEntity("T");
        vm.Entities.Add(a);
        vm.Entities.Add(b);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship { SourceEntityId = a.Id, TargetEntityId = b.Id },
                a,
                b
            )
        );

        return vm;
    }

    /// <summary>A・B・C の 3 列を持つテーブル 1 つ（CommandTests の図）</summary>
    private static MainViewModel ThreeColumnsDiagram()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = "T",
                    Columns =
                    {
                        new Column { Name = "A", DataType = "int" },
                        new Column { Name = "B", DataType = "int" },
                        new Column { Name = "C", DataType = "int" },
                    },
                }
            )
        );

        return vm;
    }

    /// <summary>列のないテーブル「T」1 つ（CommandTests の図）</summary>
    private static MainViewModel SingleTableDiagram()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(PlainEntity("T"));

        return vm;
    }

    /// <summary>(10, 20) にあるテーブル 1 つ（CommandTests の図）</summary>
    private static MainViewModel MovableTableDiagram()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(PlainEntity("T", 10, 20));

        return vm;
    }

    /// <summary>(10, 10) と (50, 60) にあるテーブル 2 つ（CommandTests の図）</summary>
    private static MainViewModel MovableTablesDiagram()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(PlainEntity("T", 10, 10));
        vm.Entities.Add(PlainEntity("T", 50, 60));

        return vm;
    }

    /// <summary>主キー 1 列のテーブル（一括操作のテストの形・位置 (0, 0)・幅 200・見出しの色は任意）</summary>
    private static EntityViewModel BulkTable(
        MainViewModel vm,
        string tableName,
        string? titleColor = null
    )
    {
        var layout = new EntityLayout
        {
            X = 0,
            Y = 0,
            Width = 200,
        };

        if (titleColor is not null)
        {
            layout.TitleBackgroundColor = titleColor;
        }

        var entity = new EntityViewModel(
            new Entity
            {
                TableName = tableName,
                Columns =
                {
                    new Column
                    {
                        Name = "ID",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                },
            },
            layout
        );
        vm.Entities.Add(entity);

        return entity;
    }

    /// <summary>A・B・C の 3 テーブルと A→B・B→C のリレーション（MainViewModelBulkOperationTests の図）</summary>
    private static MainViewModel BulkChainDiagram()
    {
        var vm = new MainViewModel();
        var a = BulkTable(vm, "A");
        var b = BulkTable(vm, "B");
        var c = BulkTable(vm, "C");
        Link(vm, a, b, RelationshipType.OneToMany);
        Link(vm, b, c, RelationshipType.OneToMany);

        return vm;
    }

    /// <summary>A・B・C の 3 テーブル（MainViewModelBulkOperationTests の図）</summary>
    private static MainViewModel BulkThreeDiagram()
    {
        var vm = new MainViewModel();
        BulkTable(vm, "A");
        BulkTable(vm, "B");
        BulkTable(vm, "C");

        return vm;
    }

    /// <summary>見出しの色が #111111・#222222・#333333 の 3 テーブル（MainViewModelBulkOperationTests の図）</summary>
    private static MainViewModel BulkThreeColoredDiagram()
    {
        var vm = new MainViewModel();
        BulkTable(vm, "A", "#111111");
        BulkTable(vm, "B", "#222222");
        BulkTable(vm, "C", "#333333");

        return vm;
    }

    /// <summary>見出しの色が #111111・#FFCC00 の 2 テーブル（MainViewModelBulkOperationTests の図）</summary>
    private static MainViewModel BulkTwoColoredDiagram()
    {
        var vm = new MainViewModel();
        BulkTable(vm, "A", "#111111");
        BulkTable(vm, "B", "#FFCC00");

        return vm;
    }

    /// <summary>A・B の 2 テーブル（MainViewModelBulkOperationTests の図）</summary>
    private static MainViewModel BulkTwoDiagram()
    {
        var vm = new MainViewModel();
        BulkTable(vm, "A");
        BulkTable(vm, "B");

        return vm;
    }

    /// <summary>空の図（MainViewModelTests の図。テーブルは準備で画面の操作から足す）</summary>
    private static MainViewModel EmptyDiagram() => new();

    /// <summary>テーブル名「X」のテーブル 1 つ（DuplicateEntityCommandTests の図）</summary>
    private static MainViewModel DuplicateSourceDiagram()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(new EntityViewModel(new Entity { TableName = "X" }));

        return vm;
    }

    /// <summary>主キー 1 列の First・Second の 2 テーブルを読み込んだ図（EntityLifecycleUndoTests の図）</summary>
    private static MainViewModel FirstSecondDiagram()
    {
        var vm = new MainViewModel();
        vm.ReplaceDiagramFromModule(
            new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "First",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                        },
                    },
                    new Entity
                    {
                        TableName = "Second",
                        Columns =
                        {
                            new Column
                            {
                                Name = "Id",
                                DataType = "int",
                                IsPrimaryKey = true,
                            },
                        },
                    },
                },
            }
        );

        return vm;
    }

    /// <summary>
    /// TenantId / RegionCode の複合主キーと NULL 許容の Code 列を持つテーブルを選択した図
    /// </summary>
    /// <param name="pinPrimaryKeyOrder">true なら宣言順どおりの主キー順を明示的に保存する</param>
    private static MainViewModel TenantRegionDiagram(bool pinPrimaryKeyOrder)
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.DisableFileWatchingForTests();

        var tenantId = new Column
        {
            Name = "TenantId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var regionCode = new Column
        {
            Name = "RegionCode",
            DataType = "nvarchar(10)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = true,
        };

        var model = new Entity
        {
            TableName = "TenantRegion",
            Columns = { tenantId, regionCode, code },
        };

        if (pinPrimaryKeyOrder)
        {
            model.PrimaryKeyColumnIds = [tenantId.Id, regionCode.Id];
        }

        var entity = new EntityViewModel(model);
        vm.Entities.Add(entity);
        vm.SelectedEntity = entity;
        vm.UndoRedo.Clear();

        return vm;
    }

    /// <summary>実効順の主キー列名</summary>
    private static List<string> PrimaryKeyOrder(EntityViewModel entity) =>
        entity.GetPrimaryKeyColumnsInOrder().Select(column => column.Name).ToList();

    /// <summary>Id / Code / Kind の 3 列を持つテーブル Item を選択した図（MainViewModelUniqueConstraintTests の図）</summary>
    private static MainViewModel ItemDiagram() => ItemDiagram(null, null);

    /// <summary>
    /// <see cref="ItemDiagram()"/> と同じ図で、保存できるもの（保存先は一時フォルダ。<see cref="SaveClean"/> が保存の間だけ作る）
    /// </summary>
    private static MainViewModel SavableUniqueConstraintDiagram()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-undo-rows-" + Guid.NewGuid().ToString("N")
        );
        var vm = ItemDiagram(
            new RecordingFileDialogService
            {
                SaveResult = new(Path.Combine(folder, "Doc.json"), 1),
            },
            folder
        );
        Remember(vm, SaveFolderKey, folder);

        return vm;
    }

    private static MainViewModel ItemDiagram(RecordingFileDialogService? files, string? folder)
    {
        var vm = new MainViewModel(new StubDialogService(), files: files);

        if (folder is not null)
        {
            vm.UsePersistenceForTests(
                new GuiAppSettingsStore(folder),
                Path.Combine(folder, "last_diagram.json")
            );
        }

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

    /// <summary>選択中のテーブルの列を名前で引く</summary>
    private static ColumnViewModel SelectedColumnNamed(MainViewModel vm, string name) =>
        vm.SelectedEntity!.Columns.First(column => column.Name == name);

    /// <summary>
    /// 複合主キー (OrderId, LineNo) を持つ親 OrderLine と、指定の列を持つ子 Shipment を並べた図
    /// （MainViewModelRelationshipColumnPairTests の図）
    /// </summary>
    /// <param name="childColumns">子テーブルの列（主キー以外）を宣言順で指定する</param>
    private static MainViewModel OrderLineShipmentDiagram(params string[] childColumns)
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.DisableFileWatchingForTests();

        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "OrderLine",
                Columns =
                {
                    new Column
                    {
                        Name = "OrderId",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    new Column
                    {
                        Name = "LineNo",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var child = new Entity
        {
            TableName = "Shipment",
            Columns =
            {
                new Column
                {
                    Name = "ShipmentId",
                    DataType = "int",
                    IsPrimaryKey = true,
                },
            },
        };

        foreach (var columnName in childColumns)
        {
            child.Columns.Add(new Column { Name = columnName, DataType = "int" });
        }

        vm.Entities.Add(parent);
        vm.Entities.Add(new EntityViewModel(child));
        vm.UndoRedo.Clear();

        return vm;
    }

    /// <summary>リレーション作成モードで、先頭のテーブルから 2 番目のテーブルへ 1 対多のリレーションを作る</summary>
    private static void CreateOneToMany(MainViewModel vm)
    {
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
    }

    /// <summary>子テーブルの列を名前で引く</summary>
    private static ColumnViewModel ChildColumn(MainViewModel vm, string name) =>
        vm.Entities[1].Columns.Single(column => column.Name == name);

    /// <summary>主キー 1 列の A・B の 2 テーブルと A→B のリレーション（MainViewModelHighlightTests の図）</summary>
    private static MainViewModel HighlightDiagram()
    {
        var vm = new MainViewModel();
        var a = KeyedTable(vm, "A");
        var b = KeyedTable(vm, "B");
        Link(vm, a, b, RelationshipType.OneToMany);

        return vm;
    }

    /// <summary>主キー 1 列のテーブル（レイアウトを省くと既定）を図へ足す</summary>
    private static EntityViewModel KeyedTable(
        MainViewModel vm,
        string tableName,
        EntityLayout? layout = null
    )
    {
        var model = new Entity
        {
            TableName = tableName,
            Columns =
            {
                new Column
                {
                    Name = "ID",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var entity = layout is null
            ? new EntityViewModel(model)
            : new EntityViewModel(model, layout);
        vm.Entities.Add(entity);

        return entity;
    }

    /// <summary>(0, 0) と (400, 0) にある幅 200 の A・B の 2 テーブル（MainViewModelSelectionTests の図）</summary>
    private static MainViewModel SelectionDiagram()
    {
        var vm = new MainViewModel();
        KeyedTable(
            vm,
            "A",
            new EntityLayout
            {
                X = 0,
                Y = 0,
                Width = 200,
            }
        );
        KeyedTable(
            vm,
            "B",
            new EntityLayout
            {
                X = 400,
                Y = 0,
                Width = 200,
            }
        );

        return vm;
    }

    /// <summary>
    /// 複製の合成名（<c>UQ_{表}_Copy_Code</c>）が SQL Server の上限 128 を超える長さの表名（118 文字。複製の表名・主キーの既定名は上限に収まる）
    /// </summary>
    private static string LongOrderTableName => "Order" + new string('x', 113);

    /// <summary>
    /// Customer(Id) と、一意制約 UQ_Order_Code（Code）と外部キー FK_Order_Customer（Customer.Id → CustomerId）を持つ表を選択した図
    /// </summary>
    /// <param name="orderTable">一意制約と外部キーを持つ表の名前（制約名は表名に依らず固定）</param>
    /// <param name="copyNameTaken">true なら、表 Other が複製の合成名 UQ_Order_Copy_Code を明示の名前で持つ</param>
    private static MainViewModel NamedConstraintsDiagram(
        string orderTable = "Order",
        bool copyNameTaken = false
    )
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.DisableFileWatchingForTests();

        var customer = new EntityViewModel(
            new Entity
            {
                TableName = "Customer",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                },
            }
        );
        var code = new Column { Name = "Code", DataType = "nvarchar(20)" };
        var customerId = new Column { Name = "CustomerId", DataType = "int" };
        var order = new EntityViewModel(
            new Entity
            {
                TableName = orderTable,
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                    },
                    code,
                    customerId,
                },
                UniqueConstraints =
                {
                    new UniqueConstraint { Name = "UQ_Order_Code", ColumnIds = { code.Id } },
                },
            },
            new EntityLayout { X = 300 }
        );
        vm.Entities.Add(customer);
        vm.Entities.Add(order);

        if (copyNameTaken)
        {
            var otherCode = new Column { Name = "Code", DataType = "nvarchar(20)" };
            vm.Entities.Add(
                new EntityViewModel(
                    new Entity
                    {
                        TableName = "Other",
                        Columns = { otherCode },
                        UniqueConstraints =
                        {
                            new UniqueConstraint
                            {
                                Name = "UQ_Order_Copy_Code",
                                ColumnIds = { otherCode.Id },
                            },
                        },
                    },
                    new EntityLayout { X = 600 }
                )
            );
        }

        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs =
                    {
                        new RelationshipColumnPair(customer.Columns[0].Id, customerId.Id),
                    },
                    ConstraintName = "FK_Order_Customer",
                },
                customer,
                order
            )
        );
        vm.SelectedEntity = order;
        vm.UndoRedo.Clear();

        return vm;
    }
}
