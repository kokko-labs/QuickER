using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Resources;
using QuickER.Services;
using QuickER.Tests.TestDoubles;
using QuickER.ViewModels;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary><see cref="MainViewModel"/> の選択・リレーション作成・Undo/Redo・コピー貼り付け等のロジックを検証するテストクラス</summary>
/// <remarks>WPF UI スレッドに依存しないロジックのみを対象とする</remarks>
public class MainViewModelTests
{
    /// <summary>AutoArrangeNewDiagram がエンティティを整列しつつ Undo 履歴に積まないこと（エンティティが無ければ何も起きないこと）を検証する</summary>
    /// <param name="entityCount">追加するエンティティの数（0 は何も無い図）</param>
    [Theory(
        DisplayName = "AutoArrangeNewDiagram: 整列するが履歴に積まれない（空の図では何もしない）"
    )]
    [InlineData(2)] // 整列するが履歴に積まれない
    [InlineData(0)] // 空の図では何もしない
    public void AutoArrangeNewDiagram_ArrangesWithoutHistory(int entityCount)
    {
        var vm = new MainViewModel();

        for (var i = 0; i < entityCount; i++)
        {
            vm.AddEntityCommand.Execute(null);
        }

        vm.AutoArrangeNewDiagram();

        vm.Entities.Should().HaveCount(entityCount);

        if (entityCount > 0)
        {
            // 先頭エンティティが格子レイアウトの左上余白へ移動している（初期の階段状座標 60,60 から変化）
            vm.Entities[0].X.Should().Be(40);
            vm.Entities[0].Y.Should().Be(40);
        }

        // 整列は履歴に積まれないため、追加分を取り消すと完全に空になり Undo 不能へ戻る
        for (var i = 0; i < entityCount; i++)
        {
            vm.UndoCommand.Execute(null);
        }

        vm.Entities.Should().BeEmpty();
        vm.UndoRedo.CanUndo.Should().BeFalse();
    }

    /// <summary>エンティティクリックで単一選択状態になることを検証する</summary>
    [Fact(DisplayName = "OnEntityClicked: 単一選択になる")]
    public void OnEntityClicked_SetsSingleSelection()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var first = vm.Entities[0];
        var second = vm.Entities[1];

        vm.OnEntityClicked(first);
        first.IsSelected.Should().BeTrue();
        second.IsSelected.Should().BeFalse();
        vm.SelectedEntity.Should().Be(first);
    }

    /// <summary>リレーション作成モードで 2 エンティティを順にクリックするとリレーションが追加されることを検証する</summary>
    [Fact(DisplayName = "リレーション作成モードで2つのエンティティを順にクリックすると追加される")]
    public void RelationshipMode_CreatesRelationship()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var a = vm.Entities[0];
        var b = vm.Entities[1];

        vm.StartAddOneToManyCommand.Execute(null);
        vm.IsRelationshipMode.Should().BeTrue();

        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        vm.IsRelationshipMode.Should().BeFalse();
        vm.Relationships.Should().HaveCount(1);
        vm.Relationships[0].Source.Should().Be(a);
        vm.Relationships[0].Target.Should().Be(b);
        vm.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
        vm.Relationships[0].ConstraintName.Should().Be("FK_NewTable_NewTable");
    }

    /// <summary>同じエンティティを 2 回クリックすると自己参照リレーションが追加されることを検証する</summary>
    [Fact(
        DisplayName = "リレーション作成モードで同じエンティティを2回クリックすると自己参照リレーションが追加される"
    )]
    public void RelationshipMode_CreatesSelfRelationship()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0]
            .Columns.Add(new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[0]);

        vm.Relationships.Should().ContainSingle();
        vm.Relationships[0].Source.Should().Be(vm.Entities[0]);
        vm.Relationships[0].Target.Should().Be(vm.Entities[0]);
        vm.Relationships[0]
            .ColumnPairs.Should()
            .ContainSingle()
            .Subject.TargetColumnId.Should()
            .Be(vm.Entities[0].Columns[1].Id);
        vm.Relationships[0].ConstraintName.Should().Be("FK_NewTable_NewTable");
    }

    /// <summary>
    /// リレーション作成時に、親の先頭列を起点に子の列が既定の参照先として引き当てられ（引き当てられなければ未割当）、
    /// 引き当てた列だけが外部キーの印を持つことを検証する
    /// </summary>
    /// <remarks>
    /// 子の列の引き当ては「親テーブル名+Id」の命名規則・親の主キーと同名の列の優先で決まる（NewTable → NewTableId）。
    /// 2 つのテーブルは追加コマンドで作る（既定の先頭列は主キー）。
    /// </remarks>
    /// <param name="parentTable">親テーブル名（null は既定のまま）</param>
    /// <param name="parentFirstColumn">親の先頭列の名前（null は既定のまま）</param>
    /// <param name="childTable">子テーブル名（null は既定のまま）</param>
    /// <param name="childFirstColumn">子の先頭列の名前（null は既定のまま）</param>
    /// <param name="childExtraColumns">子へ足す列（「名前:型」のカンマ区切り。空は足さない）</param>
    /// <param name="expectedTarget">引き当てられるはずの子の列の名前（null は未割当）</param>
    [Theory(
        DisplayName = "リレーション作成時の列ペアの自動解決は、子の列を引き当て、引き当てた列だけを外部キーにする"
    )]
    [InlineData(null, "ParentKey", null, null, "NewTableId:int", "NewTableId")] // 既定の参照先列と外部キー列が設定される
    [InlineData(
        "Customer",
        null,
        "Order",
        null,
        "OrderDate:datetime2,CustomerId:int",
        "CustomerId"
    )] // 親 PK が Id でも 親テーブル名+Id の列が参照先に解決される
    [InlineData(null, null, null, null, "Quantity:int", null)] // FK らしい列が無ければ参照先列は未割当となる
    [InlineData(null, "CustomerId", null, "Id", "CustomerId:int", "CustomerId")] // 参照先 PK と同名の列があれば既定 FK に選ばれ、FK チェックも入る
    public void RelationshipMode_ResolvesColumnPair(
        string? parentTable,
        string? parentFirstColumn,
        string? childTable,
        string? childFirstColumn,
        string childExtraColumns,
        string? expectedTarget
    )
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var parent = vm.Entities[0];
        var child = vm.Entities[1];

        if (parentTable is not null)
        {
            parent.TableName = parentTable;
        }

        if (childTable is not null)
        {
            child.TableName = childTable;
        }

        if (parentFirstColumn is not null)
        {
            parent.Columns[0].Name = parentFirstColumn;
        }

        if (childFirstColumn is not null)
        {
            child.Columns[0].Name = childFirstColumn;
        }

        var extraNames = new List<string>();

        foreach (var spec in childExtraColumns.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = spec.Split(':');
            extraNames.Add(parts[0]);
            child.Columns.Add(
                new ColumnViewModel(new Column { Name = parts[0], DataType = parts[1] })
            );
        }

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(parent);
        vm.OnEntityClicked(child);

        vm.Relationships.Should().ContainSingle();

        if (expectedTarget is null)
        {
            vm.Relationships[0].ColumnPairs.Should().BeEmpty();
            child
                .Columns.Where(column => extraNames.Contains(column.Name))
                .Should()
                .OnlyContain(column => !column.IsForeignKey);

            return;
        }

        var target = child.Columns.Single(column => column.Name == expectedTarget);
        var pair = vm.Relationships[0].ColumnPairs.Should().ContainSingle().Subject;
        pair.SourceColumnId.Should().Be(parent.Columns[0].Id);
        pair.TargetColumnId.Should().Be(target.Id);
        target.IsForeignKey.Should().BeTrue();
    }

    /// <summary>
    /// 同じ始点・終点（自己参照を含む）のリレーションは、種別が違っても重複追加されず、情報ダイアログで知らせることを検証する
    /// </summary>
    /// <param name="selfReferencing">始点と終点が同じテーブルか</param>
    /// <param name="secondIsOneToOne">2 本目を 1 対 1 で作るか（偽なら 1 本目と同じ 1 対多）</param>
    [Theory(
        DisplayName = "同じ始点と終点のリレーションは、種別が違っても自己参照でも重複追加されない"
    )]
    [InlineData(false, true)] // 同じ始点と終点のリレーションは種別が違っても重複追加されない
    [InlineData(true, true)] // 自己参照リレーションも重複追加されない
    [InlineData(false, false)] // 重複リレーション作成時は情報ダイアログが表示され追加されない
    public void RelationshipMode_DoesNotCreateDuplicateRelationship(
        bool selfReferencing,
        bool secondIsOneToOne
    )
    {
        var dialogs = new StubDialogService();
        var vm = new MainViewModel(dialogs);
        vm.AddEntityCommand.Execute(null);

        if (selfReferencing)
        {
            vm.Entities[0]
                .Columns.Add(
                    new ColumnViewModel(new Column { Name = "ParentId", DataType = "int" })
                );
        }
        else
        {
            vm.AddEntityCommand.Execute(null);
        }

        var source = vm.Entities[0];
        var target = selfReferencing ? vm.Entities[0] : vm.Entities[1];

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(source);
        vm.OnEntityClicked(target);

        if (secondIsOneToOne)
        {
            vm.StartAddOneToOneCommand.Execute(null);
        }
        else
        {
            vm.StartAddOneToManyCommand.Execute(null);
        }

        vm.OnEntityClicked(source);
        vm.OnEntityClicked(target);

        vm.Relationships.Should().ContainSingle();
        vm.Relationships[0].Source.Should().Be(source);
        vm.Relationships[0].Target.Should().Be(target);
        vm.IsRelationshipMode.Should().BeFalse();
        vm.PendingRelationshipSource.Should().BeNull();
        dialogs
            .InformationMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Relationship_DuplicateMessage);
    }

    /// <summary>リレーションにより自動管理される列は PK / FK チェックを編集できないことを検証する</summary>
    [Fact(DisplayName = "リレーション列は PK/FK チェックを編集できない")]
    public void RelationshipColumns_AreLocked()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.Entities[0].Columns[0].Name = "ParentKey";
        vm.Entities[1]
            .Columns.Add(new ColumnViewModel(new Column { Name = "NewTableId", DataType = "int" }));

        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);

        var fkColumn = vm.Entities[1]
            .Columns.First(c => c.Id == vm.Relationships[0].ColumnPairs[0].TargetColumnId);

        vm.Entities[0].Columns[0].IsPrimaryKeyEditable.Should().BeFalse();
        fkColumn.IsForeignKeyEditable.Should().BeFalse();
    }

    /// <summary>リレーション選択中の DeleteSelected がそのリレーションを削除することを検証する</summary>
    [Fact(DisplayName = "DeleteSelected はリレーション選択中ならリレーションを削除する")]
    public void DeleteSelected_RemovesSelectedRelationship()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        vm.StartAddOneToManyCommand.Execute(null);
        vm.OnEntityClicked(vm.Entities[0]);
        vm.OnEntityClicked(vm.Entities[1]);
        vm.OnRelationshipClicked(vm.Relationships[0]);

        vm.DeleteSelectedCommand.Execute(null);

        vm.Relationships.Should().BeEmpty();
    }

    /// <summary>エンティティ移動に追従してリレーション端点座標が更新されることを検証する</summary>
    [Fact(DisplayName = "エンティティ移動でリレーションの端点が追従する")]
    public void Relationship_FollowsEntityMove()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);
        var a = vm.Entities[0];
        var b = vm.Entities[1];

        // 幾何条件を安定させるため、同じ Y 軸上に十分離して配置する。
        a.X = 0;
        a.Y = 0;
        b.X = 400;
        b.Y = 0;

        vm.StartAddOneToOneCommand.Execute(null);
        vm.OnEntityClicked(a);
        vm.OnEntityClicked(b);

        var rel = vm.Relationships[0];
        var oldX1 = rel.X1;
        var oldX2 = rel.X2;

        a.X = a.X + 100;

        rel.X1.Should().Be(oldX1 + 100);
        rel.X2.Should().Be(oldX2);
    }

    /// <summary>選択エンティティをコピーして位置をずらした複製として貼り付けできることを検証する</summary>
    [Fact(DisplayName = "選択エンティティはコピーして貼り付けできる")]
    public void CopyPasteSelectedEntity_AddsShiftedClone()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        var original = vm.Entities[0];
        original.TableName = "Customer";
        original.X = 120;
        original.Y = 200;
        original.Width = 280;
        original.Description = "顧客";
        original.Memo = "メモ";
        original.TitleBackgroundColor = "#E4F1C9";
        original.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                    Description = "顧客コード",
                }
            )
        );
        vm.OnEntityClicked(original);

        vm.CopySelectedEntityCommand.Execute(null);
        vm.PasteCopiedEntityCommand.Execute(null);

        vm.Entities.Should().HaveCount(2);
        var pasted = vm.Entities[1];
        pasted.Should().NotBeSameAs(original);
        pasted.Id.Should().NotBe(original.Id);
        pasted.TableName.Should().Be("Customer_Copy");
        pasted.X.Should().Be(150);
        pasted.Y.Should().Be(230);
        pasted.Width.Should().Be(280);
        pasted.Description.Should().Be("顧客");
        pasted.Memo.Should().Be("メモ");
        pasted.TitleBackgroundColor.Should().Be("#E4F1C9");
        pasted.Columns.Should().HaveCount(2);
        pasted.Columns[0].Id.Should().NotBe(original.Columns[0].Id);
        pasted.Columns[1].Id.Should().NotBe(original.Columns[1].Id);
        pasted.Columns[1].Name.Should().Be("Code");
        pasted.Columns[1].Description.Should().Be("顧客コード");
        vm.SelectedEntity.Should().Be(pasted);
        pasted.IsSelected.Should().BeTrue();
        original.IsSelected.Should().BeFalse();
    }

    /// <summary>選択カラムをコピーして選択行の直下へ複製挿入できることを検証する</summary>
    [Fact(DisplayName = "選択カラムはコピーして直下へ貼り付けできる")]
    public void CopyPasteSelectedColumn_InsertsCloneBelowSelection()
    {
        var vm = new MainViewModel();
        vm.AddEntityCommand.Execute(null);
        vm.SelectedEntity = vm.Entities[0];
        vm.SelectedEntity.Columns[0].Name = "Id";
        vm.SelectedEntity.Columns.Add(
            new ColumnViewModel(
                new Column
                {
                    Name = "Code",
                    DataType = "nvarchar(50)",
                    IsNullable = true,
                    Description = "コード",
                }
            )
        );
        vm.SelectedEntity.Columns.Add(
            new ColumnViewModel(new Column { Name = "UpdatedAt", DataType = "datetime2" })
        );
        vm.SelectedColumn = vm.SelectedEntity.Columns[1];
        var original = vm.SelectedColumn;

        vm.CopySelectedColumnCommand.Execute(null);
        vm.PasteCopiedColumnCommand.Execute(null);

        vm.SelectedEntity.Columns.Select(column => column.Name)
            .Should()
            .Equal("Id", "Code", "Code", "UpdatedAt");
        vm.SelectedColumn.Should().Be(vm.SelectedEntity.Columns[2]);
        vm.SelectedColumn.Should().NotBeSameAs(original);
        vm.SelectedColumn!.Id.Should().NotBe(original.Id);
        vm.SelectedColumn.DataType.Should().Be("nvarchar(50)");
        vm.SelectedColumn.IsNullable.Should().BeTrue();
        vm.SelectedColumn.Description.Should().Be("コード");
    }

    /// <summary>新規作成で図をクリアすると Undo / Redo 履歴もリセットされることを検証する</summary>
    [Fact(DisplayName = "新規で図をクリアした後は Undo/Redo 履歴もリセットされる")]
    public void NewDiagram_ClearsUndoRedoHistory()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.AddEntityCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);

        vm.UndoRedo.CanUndo.Should().BeFalse();
        vm.UndoRedo.CanRedo.Should().BeFalse();
    }

    /// <summary>ウィンドウタイトルが現在ファイルパスに連動して変化することを検証する</summary>
    [Fact(
        DisplayName = "WindowTitle: 未保存は「QuickER」、ファイル名設定で「ファイル名 - QuickER」になる"
    )]
    public void WindowTitle_FollowsCurrentFilePath()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.WindowTitle.Should().Be("QuickER");

        vm.CurrentFilePath = @"C:\docs\Sample.json";

        vm.LastDocumentFileName.Should().Be("Sample");
        vm.WindowTitle.Should().Be("Sample - QuickER");
        raised.Should().Contain(nameof(MainViewModel.WindowTitle));
    }

    /// <summary>新規作成でウィンドウタイトルが既定へ戻ることを検証する</summary>
    [Fact(DisplayName = "WindowTitle: 新規作成で「QuickER」へ戻る")]
    public void NewDiagram_ResetsWindowTitle()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.CurrentFilePath = @"C:\docs\Sample.json";

        vm.NewDiagramCommand.Execute(null);

        vm.CurrentFilePath.Should().BeNull();
        vm.WindowTitle.Should().Be("QuickER");
    }

    /// <summary>実 %LOCALAPPDATA% を汚さないよう、永続化先を一時フォルダへ隔離した VM を生成する</summary>
    private static MainViewModel CreateIsolatedPersistenceViewModel(string folder)
    {
        var vm = new MainViewModel();
        vm.UsePersistenceForTests(
            new GuiAppSettingsStore(folder),
            Path.Combine(folder, "last_diagram.json")
        );
        return vm;
    }

    /// <summary>初期化時に説明表示状態が自動保存から復元されることを検証する</summary>
    [Fact(DisplayName = "説明表示状態は自動保存から復元される")]
    public void Initialize_RestoresShowDescriptionsState()
    {
        // AutoSave / Initialize は永続化ファイルへ触れるため、一時フォルダへ隔離して実 AppData を守る
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-mainvm-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            var vm = CreateIsolatedPersistenceViewModel(folder);
            vm.ShowColumnDescriptionsInDiagram = true;
            vm.AutoSave();

            var restored = CreateIsolatedPersistenceViewModel(folder);
            restored.Initialize();

            restored.ShowColumnDescriptionsInDiagram.Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }

    /// <summary>初期化時に左右パネルの表示状態が自動保存から復元されることを検証する</summary>
    [Fact(DisplayName = "左右パネルの表示状態は自動保存から復元される")]
    public void Initialize_RestoresPanelVisibility()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-mainvm-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            var vm = CreateIsolatedPersistenceViewModel(folder);

            // 既定は両方とも表示
            vm.IsToolboxVisible.Should().BeTrue();
            vm.IsPropertyPanelVisible.Should().BeTrue();

            vm.IsToolboxVisible = false;
            vm.IsPropertyPanelVisible = false;
            vm.AutoSave();

            var restored = CreateIsolatedPersistenceViewModel(folder);
            restored.Initialize();

            restored.IsToolboxVisible.Should().BeFalse();
            restored.IsPropertyPanelVisible.Should().BeFalse();
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }

    /// <summary>簡易表示の切替が全エンティティへ伝播することを検証する</summary>
    [Fact(DisplayName = "簡易表示の切替は全エンティティへ伝播する")]
    public void IsCompactViewInDiagram_PropagatesToAllEntities()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.AddEntityCommand.Execute(null);
        vm.AddEntityCommand.Execute(null);

        vm.IsCompactViewInDiagram = true;

        vm.Entities.Should().OnlyContain(e => e.IsCompactView);
    }

    /// <summary>簡易表示 ON のとき新規追加エンティティにも設定が適用されることを検証する</summary>
    [Fact(DisplayName = "簡易表示 ON 中に追加したエンティティにも設定が適用される")]
    public void IsCompactViewInDiagram_AppliesToNewlyAddedEntity()
    {
        var vm = new MainViewModel(new StubDialogService());
        vm.IsCompactViewInDiagram = true;

        vm.AddEntityCommand.Execute(null);

        vm.Entities.Should().ContainSingle().Which.IsCompactView.Should().BeTrue();
    }

    /// <summary>初期化時に簡易表示状態が自動保存から復元されることを検証する</summary>
    [Fact(DisplayName = "簡易表示状態は自動保存から復元される")]
    public void Initialize_RestoresCompactViewState()
    {
        // AutoSave / Initialize は永続化ファイルへ触れるため、一時フォルダへ隔離して実 AppData を守る
        var folder = Path.Combine(
            Path.GetTempPath(),
            "quicker-mainvm-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            var vm = CreateIsolatedPersistenceViewModel(folder);
            vm.IsCompactViewInDiagram = true;
            vm.AutoSave();

            var restored = CreateIsolatedPersistenceViewModel(folder);
            restored.Initialize();

            restored.IsCompactViewInDiagram.Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // 後始末の失敗はテスト結果に影響させない
            }
        }
    }

    // ---------------- ダイアログサービス (IDialogService) ----------------

    /// <summary>新規作成の確認でキャンセルすると現在のダイアグラムが保持されることを検証する（未保存編集ありのため警告水準の確認）</summary>
    [Fact(
        DisplayName = "NewDiagram: ダーティ時は警告確認になりキャンセルでダイアグラムは保持される"
    )]
    public void NewDiagram_ConfirmDeclined_KeepsDiagram()
    {
        var dialogs = new StubDialogService { ConfirmResult = false };
        var vm = new MainViewModel(dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);

        vm.Entities.Should().HaveCount(1);
        dialogs
            .WarningConfirmMessages.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Strings.Confirm_ClearDiagram);
        dialogs
            .ConfirmMessages.Should()
            .BeEmpty("未保存編集がある間の確認は警告水準（Warning）を使う");
    }

    /// <summary>新規作成の確認で OK するとダイアグラムがクリアされることを検証する（未保存編集ありのため警告水準の確認）</summary>
    [Fact(DisplayName = "NewDiagram: ダーティ時は警告確認になり OK でダイアグラムがクリアされる")]
    public void NewDiagram_ConfirmAccepted_ClearsDiagram()
    {
        var dialogs = new StubDialogService { ConfirmResult = true };
        var vm = new MainViewModel(dialogs);
        vm.AddEntityCommand.Execute(null);

        vm.NewDiagramCommand.Execute(null);

        vm.Entities.Should().BeEmpty();
        dialogs.WarningConfirmMessages.Should().HaveCount(1);
        dialogs.ConfirmMessages.Should().BeEmpty();
    }
}
