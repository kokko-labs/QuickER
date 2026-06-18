using ERDesigner.Generator;
using FluentAssertions;

namespace ERDesigner.Tests.Generator;

/// <summary>
/// <see cref="CSharpCodeGenerationService"/> がダイアグラム定義から生成する C# コードの内容を検証するテストクラス
/// </summary>
public class CSharpCodeGenerationServiceTests
{
    /// <summary>
    /// 単一の生成ファイルに Entity・EditModel・EditModelBase と各種属性が出力され、using ディレクティブが重複しないことを検証する
    /// </summary>
    [Fact]
    public void Generate_ShouldCreateSingleGeneratedFileWithEntityAndEditModel()
    {
        var customerId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files.Should().ContainSingle();
        result.Files[0].FileName.Should().Be("ErDesignerEntities.g.cs");
        result.Files[0].Content.Should().Contain("namespace Sample.Domain;");
        result.Files[0].Content.Split("using System.ComponentModel.DataAnnotations;").Length.Should().Be(2);
        result.Files[0].Content.Split("using System.ComponentModel.DataAnnotations.Schema;").Length.Should().Be(2);
        result.Files[0].Content.Should().Contain("public partial class CustomerEntity");
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel");
        result.Files[0].Content.Should().Contain("public abstract partial class EditModelBase");
        result.Files[0].Content.Should().Contain("[Table(\"customers\")]");
        result.Files[0].Content.Should().Contain("[Key]");
        result.Files[0].Content.Should().Contain("[MaxLength(100)]");
        // EditModel は画面バインディング用の文字列プロパティを持つ
        result.Files[0].Content.Should().Contain("public string BindingName");
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel : EditModelBase");
    }

    /// <summary>
    /// 1対多リレーションからコレクション型ナビゲーションと NavigationReference 属性が生成され、親参照プロパティに JsonIgnore が付与されることを検証する
    /// </summary>
    /// <summary>ナビゲーションプロパティが生成され、親参照側に JsonIgnore 属性が付くことを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateNavigationAndJsonIgnoreOnParentReference()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderCustomerId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = orderCustomerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipMultiplicity.OneToMany,
                    SourceColumnId = customerId,
                    TargetColumnId = orderCustomerId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        // NavigationReference 属性は (参照元テーブル, 参照元カラム, 参照先テーブル, 参照先カラム, IsCollection, Cascade) の 6 引数形式
        // 子方向（コレクション）はカスケード対象 true
        result.Files[0].Content.Should().Contain("[NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", true, true, false)]");
        result.Files[0].Content.Should().Contain("public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();");
        result.Files[0].Content.Should().Contain("[JsonIgnore]");
        result.Files[0].Content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
        // 親参照（多対1の戻り）はカスケード対象外 false、IsParentReference true
        result.Files[0].Content.Should().Contain("[NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", false, false, true)]");
        // エンティティは EntityBase を継承し、RowState を持つ
        result.Files[0].Content.Should().Contain("public partial class CustomerEntity : EntityBase");
        result.Files[0].Content.Should().Contain("public abstract partial class EntityBase");

        var content = result.Files[0].Content;
        // EditModel も RowState を保持し、確定値変更時に Updated へ昇格する
        content.Should().Contain("public abstract partial class EditModelBase : INotifyPropertyChanged, INotifyDataErrorInfo");
        content.Should().Contain("public RowState RowState");
        content.Should().Contain("public void MarkAdded() => RowState = RowState.Added;");
        content.Should().Contain("public void MarkRemoved() => RowState = RowState.Removed;");
        // 確定値（非バインド）setter は、ロード中以外のみ MarkUpdated で昇格する
        content.Should().Contain("if (!IsLoading)");
        content.Should().Contain("MarkUpdated();");
        content.Should().Contain("public void ExecuteLoad(Action action)");
        content.Should().Contain("editModel.ExecuteLoad(() =>");
        // 新規入力用ファクトリは Entity を基に生成し、生成フックを呼ぶ
        content.Should().Contain("public CustomerEditModel CreateEditModel()");
        content.Should().Contain("OnEditModelCreated(editModel);");
        content.Should().Contain("partial void OnEditModelCreated(CustomerEditModel editModel);");
        // ApplyToEntity は MarkUpdated を撤廃し RowState を転写、子コレクションは CreateEntities で代入する
        content.Should().NotContain("entity.MarkUpdated();");
        content.Should().Contain("entity.RowState = editModel.RowState;");
        content.Should().Contain("entity.Orders = new OrderMapper().CreateEntities(editModel.Orders, includeRemoved);");
        // 削除追跡コレクションと、CreateEntities が includeRemoved 指定時に Removed 分も含める
        content.Should().Contain("public sealed partial class EditModelCollection<T> : ObservableCollection<T>");
        content.Should().Contain("public List<OrderEntity> CreateEntities(EditModelCollection<OrderEditModel> editModels, bool includeRemoved = false)");
        content.Should().Contain("entities.AddRange(editModels.RemovedItems.Select(removed => CreateEntity(removed, includeRemoved)));");
        // EditModelCollection の変更集約（HasChanges / AcceptChanges）
        content.Should().Contain("public bool HasChanges => _removed.Count > 0 || this.Any(item => item.HasGraphChanges());");
        content.Should().Contain("public void AcceptChanges(bool includeChildren = true)");
        // EditModelCollection の一括操作・検証・並び替え API
        content.Should().Contain("public IEnumerable<EditModelError> CollectErrors(bool includeChildren = true)");
        content.Should().Contain("this[i].CollectErrors($\"[{i}]\", includeChildren, errors);");
        content.Should().Contain("public bool MoveTo(T item, int newIndex)");
        content.Should().Contain("public void AddRange(IEnumerable<T> items)");
        content.Should().Contain("public void InsertRange(int index, IEnumerable<T> items)");
        content.Should().Contain("public void RemoveAll()");
        content.Should().Contain("public void RemoveRange(int index, int count)");
        // EditModel 列挙を受け取るコンストラクタと、Entity 列挙から生成する Mapper メソッド
        content.Should().Contain("public EditModelCollection(IEnumerable<T> items)");
        content.Should().Contain("public EditModelCollection<OrderEditModel> CreateEditModels(IEnumerable<OrderEntity> entities)");
        content.Should().Contain("public EditModelCollection<OrderEditModel> Orders { get; set; } = new EditModelCollection<OrderEditModel>();");
        content.Should().Contain("public void ApplyToEntity(CustomerEditModel editModel, CustomerEntity entity, bool includeRemoved = false)");
        // ApplyToEditModel は子コレクションを CreateEditModels で代入し、状態は生成元 Entity を基準にする
        content.Should().Contain("editModel.Orders = new OrderMapper().CreateEditModels(entity.Orders);");
        content.Should().Contain("editModel.RowState = entity.RowState;");
        // EditModel.Validate（必須チェック＋ユーザー定義フック＋子への連鎖検証）
        content.Should().Contain("protected virtual string BuildRequiredErrorMessage(string propertyName)");
        // 呼び出し口（Validate）は Base 側で定義し、固有処理は具象クラスの override に分離する
        content.Should().Contain("public bool Validate(bool includeChildren = true)");
        content.Should().Contain("protected virtual void ValidateSelf()");
        content.Should().Contain("protected virtual bool ValidateChildren(bool includeChildren, bool valid)");
        content.Should().Contain("protected override void ValidateSelf()");
        content.Should().Contain("protected override bool ValidateChildren(bool includeChildren, bool valid)");
        content.Should().Contain("SetError(nameof(BindingCustomerId), BuildRequiredErrorMessage(nameof(CustomerId)));");
        content.Should().Contain("partial void OnValidate();");
        content.Should().Contain("if (includeChildren)");
        content.Should().Contain("if (!child.Validate(includeChildren))");
        content.Should().Contain("OnValidateChildren(includeChildren, ref valid);");
        content.Should().Contain("partial void OnValidateChildren(bool includeChildren, ref bool valid);");
        // グラフ全体のエラーをノードのパス付きで収集する
        content.Should().Contain("public sealed record EditModelError(string Path, string Property, string Message);");
        content.Should().Contain("public IEnumerable<EditModelError> CollectErrors(bool includeChildren = true)");
        content.Should().Contain("internal void CollectErrors(string path, bool includeChildren, List<EditModelError> errors)");
        content.Should().Contain("protected virtual void CollectChildErrors(string path, bool includeChildren, List<EditModelError> errors)");
        content.Should().Contain("protected override void CollectChildErrors(string path, bool includeChildren, List<EditModelError> errors)");
        content.Should().Contain("if (!includeChildren)");
        content.Should().Contain("errors.AddRange(CollectOwnErrors(path));");
        content.Should().Contain("Orders[i].CollectErrors(CombineErrorPath(path, $\"Orders[{i}]\"), includeChildren, errors);");
        content.Should().Contain("OnCollectChildErrors(path, includeChildren, errors);");
        content.Should().Contain("partial void OnCollectChildErrors(string path, bool includeChildren, List<EditModelError> errors);");
    }

    /// <summary>
    /// パスカルケースのテーブル名がそのままエンティティ名・ナビゲーションプロパティ名に反映されることを検証する
    /// </summary>
    /// <summary>パスカルケースのテーブル名がエンティティ名・ナビゲーション名に保持されることを検証する</summary>
    [Fact]
    public void Generate_ShouldPreservePascalCaseTableNamesInEntityAndNavigationNames()
    {
        var category = Guid.NewGuid();
        var item = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var itemCategoryId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = category,
                    TableName = "AirconditionerCategory",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = categoryId,
                            Name = "AirconditionerCategoryId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = item,
                    TableName = "Airconditioner",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "AirconditionerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = itemCategoryId,
                            Name = "AirconditionerCategoryId",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = category,
                    TargetEntityId = item,
                    Type = RelationshipMultiplicity.OneToMany,
                    SourceColumnId = categoryId,
                    TargetColumnId = itemCategoryId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class AirconditionerCategoryEntity");
        result.Files[0].Content.Should().Contain("public ICollection<AirconditionerEntity> Airconditioners { get; set; } = new List<AirconditionerEntity>();");
        result.Files[0].Content.Should().Contain("public AirconditionerCategoryEntity AirconditionerCategory { get; set; } = null!;");
    }

    /// <summary>スネークケースのテーブル名がパスカルケースのエンティティ名へ変換されることを検証する</summary>
    [Fact]
    public void Generate_ShouldConvertSnakeCaseTableNamesToPascalCaseEntityNames()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "airconditioner_category",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "airconditioner_category_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class AirconditionerCategoryEntity");
        result.Files[0].Content.Should().Contain("public partial class AirconditionerCategoryEditModel");
        result.Files[0].Content.Should().Contain("public sealed partial class AirconditionerCategoryMapper");
    }

    /// <summary>多対多リレーションが警告付きで生成スキップされることを検証する</summary>
    [Fact]
    public void Generate_ShouldWarnAndSkipManyToManyRelationship()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = left,
                    TableName = "users",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = right,
                    TableName = "roles",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "role_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = left,
                    TargetEntityId = right,
                    Type = RelationshipMultiplicity.ManyToMany,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Warning && diagnostic.Message.Contains("多対多"));
        result.Files[0].Content.Should().NotContain("ICollection<RoleEntity>");
    }

    /// <summary>Entity ↔ EditModel を変換する Mapper クラスが生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateMapperClass()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "products",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "product_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(200)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        // Mapper は具象クラスのみ（インターフェースなし）。フック宣言のため partial
        result.Files[0].Content.Should().NotContain("public interface IProductMapper");
        result.Files[0].Content.Should().Contain("public sealed partial class ProductMapper");
        result.Files[0].Content.Should().NotContain(": IProductMapper");
        result.Files[0].Content.Should().Contain("ProductEditModel CreateEditModel(ProductEntity entity)");
        result.Files[0].Content.Should().Contain("void ApplyToEntity(ProductEditModel editModel, ProductEntity entity, bool includeRemoved = false)");
        // 旧 Commit 系の名前は残っていないこと
        result.Files[0].Content.Should().NotContain("CommitToEditModel");
        result.Files[0].Content.Should().NotContain("CommitToEntity");
        // Entity ファクトリ（空生成・EditModel 反映）と初期値フックを生成する
        result.Files[0].Content.Should().Contain("public ProductEntity CreateEntity()");
        result.Files[0].Content.Should().Contain("public ProductEntity CreateEntity(ProductEditModel editModel, bool includeRemoved = false)");
        result.Files[0].Content.Should().Contain("partial void OnEntityCreated(ProductEntity entity);");
        // ApplyToEntity では nullable 化された確定値に対して保存前 null チェックを行う
        result.Files[0].Content.Should().Contain("entity.ProductId = editModel.ProductId ?? throw new InvalidOperationException(\"ProductId が未入力です。\");");
        result.Files[0].Content.Should().Contain("entity.Name = editModel.Name ?? throw new InvalidOperationException(\"Name が未入力です。\");");
        // Entity → EditModel 反映は public な ApplyToEditModel で行い、バインディング用プロパティ経由でロードする
        result.Files[0].Content.Should().Contain("public void ApplyToEditModel(ProductEntity entity, ProductEditModel editModel)");
        result.Files[0].Content.Should().Contain("editModel.BindingName =");
        result.Files[0].Content.Should().NotContain("LoadFrom");
        // 既定ロード後の後処理フック（partial 実装で追加プロパティをロード）
        result.Files[0].Content.Should().Contain("OnEditModelLoaded(entity, editModel);");
        result.Files[0].Content.Should().Contain("partial void OnEditModelLoaded(ProductEntity entity, ProductEditModel editModel);");
        // 反映後フック（partial 実装で追加プロパティを保存）
        result.Files[0].Content.Should().Contain("OnEntityApplied(editModel, entity);");
        result.Files[0].Content.Should().Contain("partial void OnEntityApplied(ProductEditModel editModel, ProductEntity entity);");
    }

    /// <summary>EditModel にバインディング文字列を確定値へ戻す処理が生成されることを検証する</summary>
    [Fact]
    public void Generate_EditModel_ShouldContainRevertInputMethod()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "amount",
                            DataType = "decimal",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 通常プロパティ (private set)
        content.Should().Contain("public int? OrderId");
        content.Should().Contain("public decimal? Amount");
        // バインディング用プロパティ (string)
        content.Should().Contain("public string BindingOrderId");
        content.Should().Contain("public string BindingAmount");
        // TryParse 検証
        content.Should().Contain("int.TryParse(value, out var parsed)");
        content.Should().Contain("decimal.TryParse(value, out var parsed)");
        // エラーメッセージは ResolveParseErrorMessage 経由で生成される
        content.Should().Contain("ResolveParseErrorMessage(nameof(BindingOrderId), value, \"int\")");
        content.Should().Contain("ResolveParseErrorMessage(nameof(BindingAmount), value, \"decimal\")");
        // EditModelBase に BuildParseErrorMessage / CustomizeParseErrorMessage が存在する
        content.Should().Contain("protected virtual string BuildParseErrorMessage(");
        content.Should().Contain("partial void CustomizeParseErrorMessage(");
        // RevertInput は Base 側に集約し、書き戻し本体は具象クラスの RevertCore で override する
        content.Should().Contain("public void RevertInput() => ExecuteRevert(RevertCore);");
        content.Should().Contain("protected virtual void RevertCore()");
        content.Should().Contain("protected override void RevertCore()");
        // 兄弟ナビゲーション：Base が所属コレクション（IList）経由の GetNext/GetPrevious を提供し、コレクションが所有者を設定、具象クラスが型付き版を生成する
        content.Should().Contain("internal System.Collections.IList? Owner { get; set; }");
        content.Should().Contain("public EditModelBase? GetNext()");
        content.Should().Contain("public EditModelBase? GetPrevious()");
        content.Should().Contain("var index = Owner.IndexOf(this);");
        content.Should().Contain("item.Owner = this;");
        content.Should().Contain("public new OrderEditModel? GetNext() => (OrderEditModel?)base.GetNext();");
        content.Should().Contain("public new OrderEditModel? GetPrevious() => (OrderEditModel?)base.GetPrevious();");
        // 親コレクション基準の位置取得・削除・並び替え。位置系と削除は Base（IList）、Move は型付き Parent と MoveCore の override で実装する
        content.Should().Contain("public int IndexInParent => Owner?.IndexOf(this) ?? -1;");
        content.Should().Contain("public bool IsFirstInParent => IndexInParent == 0;");
        content.Should().Contain("public bool IsLastInParent");
        content.Should().Contain("public bool RemoveFromParent()");
        content.Should().Contain("public bool MoveToFirst() => MoveSelfTo(0);");
        content.Should().Contain("public bool MoveToLast() => Owner is not null && MoveSelfTo(Owner.Count - 1);");
        content.Should().Contain("public bool MoveToNext()");
        content.Should().Contain("public bool MoveToPrevious()");
        content.Should().Contain("protected virtual void MoveCore(int oldIndex, int newIndex)");
        content.Should().Contain("public EditModelCollection<OrderEditModel>? Parent => Owner as EditModelCollection<OrderEditModel>;");
        content.Should().Contain("protected override void MoveCore(int oldIndex, int newIndex) => Parent?.Move(oldIndex, newIndex);");
        // ② RowState 変更時に派生フラグの変更通知も発行する
        content.Should().Contain("OnPropertyChanged(nameof(IsAdded));");
        content.Should().Contain("OnPropertyChanged(nameof(HasChanges));");
        // ① AcceptChanges（保存後の状態確定）。Base に公開エントリ、具象クラスに子確定 override＋フック
        content.Should().Contain("public void AcceptChanges(bool includeChildren = true)");
        content.Should().Contain("protected override void AcceptChildChanges(bool includeChildren)");
        content.Should().Contain("partial void OnAcceptChildChanges(bool includeChildren);");
        // ③ HasGraphChanges（ダーティ判定）。Base に公開エントリ、具象クラスに子判定 override＋フック
        content.Should().Contain("public bool HasGraphChanges(bool includeChildren = true)");
        content.Should().Contain("protected override bool ChildHasChanges(bool includeChildren)");
        content.Should().Contain("partial void OnChildHasChanges(bool includeChildren, ref bool hasChanges);");
        // ④ IEditableObject（DataGrid 行編集の取り消し対応）
        content.Should().Contain("INotifyDataErrorInfo, IEditableObject");
        content.Should().Contain("public void BeginEdit()");
        content.Should().Contain("public void CancelEdit()");
        content.Should().Contain("public void EndEdit()");
        content.Should().Contain("protected override void BeginEditCore()");
        content.Should().Contain("protected override void CancelEditCore()");
        content.Should().Contain("protected override void EndEditCore() => OnEndEdit();");
        content.Should().Contain("_bindingOrderIdSnapshot = _bindingOrderId;");
        // 行編集ライフサイクルの partial フック（partial クラスで追加したフィールドの控え/復元・副作用用）
        content.Should().Contain("partial void OnBeginEdit();");
        content.Should().Contain("partial void OnEndEdit();");
        content.Should().Contain("partial void OnCancelEdit();");
        // コレクションの増減・並び替えで位置プロパティの変更通知を発行し、バインドでボタン活性を制御できるようにする
        content.Should().Contain("internal void RaisePositionChanged()");
        content.Should().Contain("internal void RaiseParentChanged() => OnPropertyChanged(\"Parent\");");
        content.Should().Contain("private void NotifyPositionsChanged()");
        content.Should().Contain("protected override void MoveItem(int oldIndex, int newIndex)");
    }

    /// <summary>Repository インターフェース・実装・DI 登録などの基盤コードが生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateRepositoryInfrastructure()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("using Microsoft.Data.SqlClient;");
        content.Should().Contain("using Microsoft.Extensions.DependencyInjection;");
        // Repository 関連はユーザー拡張用に partial（インターフェース・基底クラス・各実装）
        content.Should().Contain("public partial interface IRepository<TEntity, TKey>");
        content.Should().Contain("public abstract partial class SqlServerRepository<TEntity, TKey>");
        content.Should().Contain("internal sealed class SqlEntityMetadata<TEntity, TKey>");
        content.Should().Contain("public partial interface ICustomerRepository : IRepository<CustomerEntity, int>");
        content.Should().Contain("public sealed partial class CustomerRepository(ISqlConnectionFactory connectionFactory)");
        content.Should().Contain("services.AddScoped<ICustomerRepository, CustomerRepository>();");
        // カラム一覧は columnList へ抽出して SELECT 系で共用する
        content.Should().Contain("var columnList = string.Join(\", \", allColumns.Select(column => $\"[{column}]\"));");
        content.Should().Contain("SelectByIdSql = $\"SELECT {columnList} FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
        content
            .Should()
            .Contain(
                "InsertSql = $\"INSERT INTO {tableName} ({string.Join(\", \", insertColumns.Select(column => $\"[{column}]\"))}) VALUES ({string.Join(\", \", properties.Select(property => $\"@{property.Name}\"))});\""
            );
        content.Should().Contain("UpdateSql = $\"UPDATE {tableName} SET {string.Join(\", \", updateAssignments)} WHERE [{keyColumnName}] = @id;\"");
        content.Should().Contain("DeleteSql = $\"DELETE FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
    }

    /// <summary>Repository にラムダ式ベースのクエリビルダー（Query / Where / OrderBy / 終端メソッド）が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateLambdaQueryBuilder()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 式木変換に必要な using とクエリ基盤クラス
        content.Should().Contain("using System.Linq.Expressions;");
        content.Should().Contain("public sealed class SqlQuery<TEntity>");
        content.Should().Contain("internal static class SqlExpressionTranslator");
        // リポジトリ起点のクエリ開始メソッド
        content.Should().Contain("public SqlQuery<TEntity> Query() =>");
        // ラムダ式で条件・並び順を指定するチェーンメソッド
        content.Should().Contain("public SqlQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)");
        content.Should().Contain("public SqlQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> keySelector)");
        content.Should().Contain("public SqlQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> keySelector)");
        content.Should().Contain("public SqlQuery<TEntity> Take(int count)");
        content.Should().Contain("public SqlQuery<TEntity> Skip(int count)");
        // 終端メソッド一式
        content.Should().Contain("public async Task<IReadOnlyList<TEntity>> ToListAsync(CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<TEntity?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<int> CountAsync(CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)");
        // 値はパラメータ化、OFFSET/FETCH でページング
        content.Should().Contain("AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value)");
        content.Should().Contain("FETCH NEXT {take.Value} ROWS ONLY");
    }

    /// <summary>RowState ベースのカスケード Save 基盤（EntityBase / SaveAsync / 保存エンジン）が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateCascadeSaveInfrastructure()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 状態管理
        content.Should().Contain("public enum RowState");
        content.Should().Contain("public abstract partial class EntityBase");
        content.Should().Contain("public RowState RowState { get; set; } = RowState.Unchanged;");
        content.Should().Contain("public void MarkAdded() => RowState = RowState.Added;");
        content.Should().Contain("public void MarkRemoved() => RowState = RowState.Removed;");
        // リポジトリ・インターフェースの Save 入口（既定でカスケード、既定は更新欠落で例外）
        content.Should().Contain("Task<int> SaveAsync(TEntity entity, bool cascadeSave = true, bool cascadeDelete = true, bool insertWhenUpdateMissing = false, CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<int> SaveAsync(TEntity entity");
        content.Should().Contain("await connection.BeginTransactionAsync(cancellationToken)");
        // 複数集約ルートを 1 トランザクションでまとめて保存するコレクション overload
        content.Should().Contain("Task<int> SaveAsync(IEnumerable<TEntity> entities, bool cascadeSave = true, bool cascadeDelete = true, bool insertWhenUpdateMissing = false, CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<int> SaveAsync(IEnumerable<TEntity> entities");
        // SqlBulkCopy によるコレクション一括追加（IDataReader でストリーミング）
        content.Should().Contain("Task<int> BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)");
        content.Should().Contain("public async Task<int> BulkInsertAsync(IEnumerable<TEntity> entities");
        content.Should().Contain("new SqlBulkCopy(connection) { DestinationTableName = _metadata.TableName }");
        content.Should().Contain("using var reader = _metadata.CreateDataReader(entities);");
        content.Should().Contain("await bulkCopy.WriteToServerAsync(reader, cancellationToken);");
        content.Should().Contain("private sealed class EntityDataReader : IDataReader");
        // 保存エンジン・競合例外・メタデータ・連鎖情報
        content.Should().Contain("internal static class EntityGraphSaver");
        content.Should().Contain("internal sealed class EntitySaveMetadata");
        content.Should().Contain("public sealed class SaveConflictException : Exception");
        content.Should().Contain("internal sealed record CascadeNavigation(PropertyInfo Property, bool IsCollection, Type ChildType, string PrincipalColumn, string DependentColumn);");
        // 更新欠落時の方針（既定は例外、insertWhenUpdateMissing で INSERT へ切替）
        content.Should().Contain("if (insertWhenUpdateMissing)");
        content.Should().Contain("throw new SaveConflictException(");
        // DB ロード時は Unchanged に確定する
        content.Should().Contain("entity.RowState = RowState.Unchanged;");
        // ApplyToEntity は EditModel の RowState を転写する（Updated は EditModel 側の確定値変更で立つ）
        content.Should().Contain("entity.RowState = editModel.RowState;");
        content.Should().NotContain("entity.MarkUpdated();");
    }

    /// <summary>Query の取得が FOR JSON＋STJ に統一され、Include / ThenInclude（多階層）が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateJsonIncludeQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // JSON デシリアライズ基盤
        content.Should().Contain("using System.Text.Json;");
        content.Should().Contain("JsonSerializer.Deserialize<List<TEntity>>(json, JsonOptions)");
        content.Should().Contain("FOR JSON PATH;");
        content.Should().Contain("internal static class JsonQueryPlanner");
        // Include / ThenInclude（単一・コレクションの両オーバーロード）
        content.Should().Contain("public IncludableSqlQuery<TEntity, TProperty> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigationSelector)");
        content.Should().Contain("public IncludableSqlQuery<TEntity, TElement> Include<TElement>(Expression<Func<TEntity, ICollection<TElement>>> navigationSelector)");
        content.Should().Contain("public IncludableSqlQuery<TEntity, TNext> ThenInclude<TNext>(Expression<Func<TProperty, TNext>> navigationSelector)");
        content.Should().Contain("public IncludableSqlQuery<TEntity, TNext> ThenInclude<TNext>(Expression<Func<TProperty, ICollection<TNext>>> navigationSelector)");
        // 列はプロパティ名へ別名付け、単一参照は WITHOUT_ARRAY_WRAPPER
        content.Should().Contain("[{columnName}] AS {propertyName}");
        content.Should().Contain("WITHOUT_ARRAY_WRAPPER");
        // ネストした FOR JSON はそのままだと文字列化されるため JSON_QUERY で包む
        content.Should().Contain("JSON_QUERY((SELECT {childProjection} FROM {childTable} AS {childAlias} WHERE {correlation} FOR JSON PATH{arrayMode})) AS {node.Property.Name}");
        // FOR JSON の複数行結果を連結する
        content.Should().Contain("string.Concat(chunks)");
    }

    /// <summary>条件付き一括削除（ExecuteDeleteAsync）とカスケード削除基盤が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateExecuteDeleteOnQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // クエリ終端の一括削除（カスケード引数つき）。既存 DeleteAsync(TKey) は維持
        content.Should().Contain("public async Task<int> ExecuteDeleteAsync(bool cascadeDelete = false, CancellationToken cancellationToken = default)");
        content.Should().Contain("Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);");
        // 非カスケードはセットベースの DELETE
        content.Should().Contain("$\"DELETE FROM {TableName}{whereClause};\"");
        // カスケードは FK のネスト IN(SELECT …) で子から削除（DB 非依存の純粋プランナーで SQL 構築）
        content.Should().Contain("internal static class CascadeDeletePlanner");
        content.Should().Contain("public static IReadOnlyList<string> BuildDeleteStatements(Type rootType, string rootTable, string whereClause)");
        content.Should().Contain("IN (SELECT [{navigation.PrincipalColumn}] FROM {parentTable}{parentScopeWhere})");
        // 循環カスケードは未対応として明示的に例外
        content.Should().Contain("循環するカスケード");
        content.Should().Contain("CascadeNavigations");
    }

    /// <summary>Repository の SQL 生成でナビゲーションプロパティが列に含まれないことを検証する</summary>
    [Fact]
    public void Generate_RepositorySql_ShouldExcludeNavigationProperties()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderCustomerId = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
                new EntityDefinition
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = orderCustomerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipMultiplicity.OneToMany,
                    SourceColumnId = customerId,
                    TargetColumnId = orderCustomerId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
        content.Should().Contain("public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();");
        content.Should().Contain("public CustomerEntity Customer { get; set; } = null!;");
        content.Should().NotContain("@Orders");
        content.Should().NotContain("@Customer");
        content.Should().NotContain("[Orders]");
        content.Should().NotContain("[Customer]");
    }

    /// <summary>バイナリ・値型カラムで安全なバインディング変換（Base64 / TryParse）が生成されることを検証する</summary>
    [Fact]
    public void Generate_EditModel_WithBinaryAndValueTypes_ShouldUseSafeBindingConversions()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "files",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "file_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "filedata",
                            DataType = "varbinary(max)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public int? FileId");
        content.Should().Contain("public bool? IsActive");
        content.Should().Contain("public byte[] Filedata { get; set; } = Array.Empty<byte>();");
        content.Should().Contain("Filedata = Convert.FromBase64String(value);");
        content.Should().Contain("BindingFiledata = Filedata is null ? string.Empty : Convert.ToBase64String(Filedata);");
        content.Should().Contain("Filedata = Array.Empty<byte>();");
        content.Should().Contain("BindingFileId = FileId?.ToString() ?? string.Empty;");
        content.Should().Contain("editModel.BindingIsActive = entity.IsActive.ToString() ?? string.Empty;");
        content.Should().NotContain("entity.FileId?.ToString()");
        content.Should().NotContain("entity.IsActive?.ToString()");
        content.Should().NotContain("private string? _errorFiledata;");
        content.Should().Contain("private static readonly SqlEntityMetadata<TEntity, TKey> _metadata = SqlEntityMetadata<TEntity, TKey>.Create();");
        content.Should().Contain("private readonly ISqlConnectionFactory _connectionFactory = connectionFactory;");
    }

    /// <summary>Entity のみ生成設定で EditModel や Mapper が出力されないことを検証する</summary>
    [Fact]
    public void Generate_EntityOnly_ShouldNotContainUiModelOrMapper()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new ColumnDefinition
                        {
                            Id = Guid.NewGuid(),
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEntityClasses = true,
            GenerateEditModels = false,
            GenerateMappers = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class ItemEntity");
        result.Files[0].Content.Should().NotContain("ItemEditModel");
        result.Files[0].Content.Should().NotContain("ItemMapper");
    }

    /// <summary>多数のエンティティでも Scriban のループ上限に達せず生成が完了することを検証する</summary>
    [Fact]
    public void Generate_ManyEntities_ShouldNotHitScribanLoopLimit()
    {
        var entities = Enumerable
            .Range(1, 1100)
            .Select(index => new EntityDefinition
            {
                Id = Guid.NewGuid(),
                TableName = $"items_{index}",
                Columns =
                [
                    new ColumnDefinition
                    {
                        Id = Guid.NewGuid(),
                        Name = "item_id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                ],
            })
            .ToList();

        var diagram = new DiagramDefinition { Entities = entities };
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEditModels = false,
            GenerateMappers = false,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public partial class Items1Entity");
        result.Files[0].Content.Should().Contain("public partial class Items1100Entity");
    }

    /// <summary>Repository をデータアノテーション無効で生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_RepositoryWithoutDataAnnotations_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateRepositories = true,
            IncludeDataAnnotations = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error && diagnostic.Message.Contains("データアノテーション"));
    }

    /// <summary>Mapper を EditModel なしで生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_MapperWithoutEditModel_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEntityClasses = true,
            GenerateEditModels = false,
            GenerateMappers = true,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error && diagnostic.Message.Contains("Mapper"));
    }

    /// <summary>Repository を Entity なしで生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_RepositoryWithoutEntity_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateEntityClasses = false,
            GenerateEditModels = true,
            GenerateMappers = false,
            GenerateRepositories = true,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error && diagnostic.Message.Contains("Repository"));
    }

    /// <summary>多対多リレーションの警告がエンティティ数に関係なく 1 回だけ追加されることを検証する（重複解消）</summary>
    [Fact]
    public void Generate_ManyToManyWarning_ShouldNotBeDuplicated()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = left,
                    TableName = "users",
                    Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "user_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
                new EntityDefinition
                {
                    Id = right,
                    TableName = "roles",
                    Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "role_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
            ],
            Relationships =
            [
                new RelationshipDefinition { Id = Guid.NewGuid(), SourceEntityId = left, TargetEntityId = right, Type = RelationshipMultiplicity.ManyToMany },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Count(diagnostic => diagnostic.Message.Contains("多対多")).Should().Be(1);
    }

    /// <summary>NULL 許容のバイナリ列が byte[]? として生成されることを検証する</summary>
    [Fact]
    public void Generate_NullableBinaryColumn_ShouldUseNullableByteArray()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "files",
                    Columns =
                    [
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "file_id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "photo", DataType = "varbinary(max)", IsNullable = true },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // NULL 許容バイナリは byte[]? となり、初期化子は付かない（CS8618 回避は ? 注釈による）
        content.Should().Contain("public byte[]? Photo { get; set; }");
        content.Should().NotContain("public byte[] Photo { get; set; } = Array.Empty<byte>();");
        // EditModel 側でも byte[]? を確定値型とし、Base64 変換で書き戻す
        content.Should().Contain("entity.Photo = editModel.Photo;");
    }

    /// <summary>出力が 1MB（Scriban 既定の LimitToString）を超える大規模スキーマでも末尾まで生成され、切り捨てられないことを検証する</summary>
    [Fact]
    public void Generate_LargeSchemaExceeding1MB_ShouldNotTruncateOutput()
    {
        // 全生成対象を有効にした 200 エンティティで出力は 1MB を大きく超える（Scriban 既定上限超過の回帰確認）
        var entities = Enumerable
            .Range(1, 200)
            .Select(index => new EntityDefinition
            {
                Id = Guid.NewGuid(),
                TableName = $"table_{index}",
                Columns =
                [
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "name", DataType = "nvarchar(100)", IsNullable = false },
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "amount", DataType = "decimal", IsNullable = true },
                    new ColumnDefinition { Id = Guid.NewGuid(), Name = "created_at", DataType = "datetime2", IsNullable = false },
                ],
            })
            .ToList();

        var result = new CSharpCodeGenerationService().Generate(new DiagramDefinition { Entities = entities }, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Length.Should().BeGreaterThan(1_048_576);
        // 切り捨て時に付与される省略記号で終わっていないこと
        content.TrimEnd().Should().NotEndWith("...");
        // 最後のエンティティ・リポジトリまで出力されていること
        content.Should().Contain("public partial class Table200Entity");
        content.Should().Contain("public sealed partial class Table200Repository(ISqlConnectionFactory connectionFactory)");
    }

    /// <summary>EditModel の確定値プロパティに変更通知パーシャルメソッド（Changing/Changed）が生成され、setter から呼ばれることを検証する</summary>
    [Fact]
    public void Generate_EditModel_ShouldGenerateChangeHookPartialMethods()
    {
        var diagram = new DiagramDefinition
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "customer_id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new ColumnDefinition { Id = Guid.NewGuid(), Name = "name", DataType = "nvarchar(100)", IsNullable = false },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // パーシャルメソッドの宣言（本体はユーザーが partial クラスで実装）。新値のみ／旧値・新値の両オーバーロードを生成する
        content.Should().Contain("partial void OnCustomerIdChanging(int? value);");
        content.Should().Contain("partial void OnCustomerIdChanging(int? oldValue, int? newValue);");
        content.Should().Contain("partial void OnCustomerIdChanged(int? value);");
        content.Should().Contain("partial void OnCustomerIdChanged(int? oldValue, int? newValue);");
        content.Should().Contain("partial void OnNameChanged(string? oldValue, string? newValue);");
        // 確定値 setter から代入前後で、新値版・旧値新値版の両方が呼び出される
        content.Should().Contain("var oldValue = _customerId;");
        content.Should().Contain("OnCustomerIdChanging(value);");
        content.Should().Contain("OnCustomerIdChanging(oldValue, value);");
        content.Should().Contain("OnCustomerIdChanged(value);");
        content.Should().Contain("OnCustomerIdChanged(oldValue, value);");
        // 値が変わらない場合は早期 return
        content.Should().Contain("if (EqualityComparer<int?>.Default.Equals(_customerId, value))");
    }

    /// <summary>リレーションが無くても Repository を生成する場合に NavigationReference 属性が定義され、生成コードがコンパイル可能であることを検証する</summary>
    [Fact]
    public void Generate_RepositoryWithoutRelationships_ShouldEmitNavigationReferenceAttribute()
    {
        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), new CodeGenerationOptions { NamespaceName = "Sample.Domain" });

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // Repository の SqlEntityMetadata が参照する属性が未定義だと CS0246 になるため、定義の存在を確認
        content.Should().Contain("public sealed class NavigationReferenceAttribute : Attribute");
        content.Should().Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
    }

    /// <summary>主キー 1 列のみを持つ単純なエンティティ 1 件のダイアグラムを生成する</summary>
    private static DiagramDefinition SingleEntityDiagram() =>
        new()
        {
            Entities =
            [
                new EntityDefinition
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns = [new ColumnDefinition { Id = Guid.NewGuid(), Name = "item_id", DataType = "int", IsPrimaryKey = true, IsNullable = false }],
                },
            ],
        };
}
