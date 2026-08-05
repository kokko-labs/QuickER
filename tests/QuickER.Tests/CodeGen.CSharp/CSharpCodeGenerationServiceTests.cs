using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.Tests.CodeGen.CSharp;

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
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().ContainSingle();
        result.Files[0].FileName.Should().Be("QuickEREntities.g.cs");
        result.Files[0].Content.Should().Contain("namespace Sample.Domain;");
        result
            .Files[0]
            .Content.Split("using System.ComponentModel.DataAnnotations;")
            .Length.Should()
            .Be(2);
        result
            .Files[0]
            .Content.Split("using System.ComponentModel.DataAnnotations.Schema;")
            .Length.Should()
            .Be(2);
        result.Files[0].Content.Should().Contain("public partial class CustomerEntity");
        result.Files[0].Content.Should().Contain("public partial class CustomerEditModel");
        result.Files[0].Content.Should().Contain("public abstract partial class EditModelBase");
        result.Files[0].Content.Should().Contain("[Table(\"customers\")]");
        result.Files[0].Content.Should().Contain("[Key]");
        result.Files[0].Content.Should().Contain("[MaxLength(100)]");
        // EditModel は画面バインディング用の文字列プロパティを持つ
        result.Files[0].Content.Should().Contain("public string BindingName");
        result
            .Files[0]
            .Content.Should()
            .Contain("public partial class CustomerEditModel : EditModelBase");
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
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = customerId,
                    TargetColumnId = orderCustomerId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        // NavigationReference 属性は (参照元テーブル, 参照元カラム, 参照先テーブル, 参照先カラム, IsCollection, Cascade) の 6 引数形式
        // 子方向（コレクション）はカスケード対象 true
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "[NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", true, true, false)]"
            );
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();"
            );
        result.Files[0].Content.Should().Contain("[JsonIgnore]");
        result
            .Files[0]
            .Content.Should()
            .Contain("public CustomerEntity Customer { get; set; } = null!;");
        // 親参照（多対1の戻り）はカスケード対象外 false、IsParentReference true
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "[NavigationReference(\"customers\", \"customer_id\", \"orders\", \"customer_id\", false, false, true)]"
            );
        // エンティティは EntityBase を継承し、RowState を持つ
        result
            .Files[0]
            .Content.Should()
            .Contain("public partial class CustomerEntity : EntityBase");
        result.Files[0].Content.Should().Contain("public abstract partial class EntityBase");
        // EntityBase は値比較・値ハッシュ・JSON 出力を提供する（値系はメタデータではなく自己完結の列プロパティ走査）
        result.Files[0].Content.Should().Contain("public bool HasSameValues(EntityBase? other)");
        result.Files[0].Content.Should().Contain("public int GetValueHashCode()");
        result
            .Files[0]
            .Content.Should()
            .Contain("public string ToJson(bool writeIndented = false)");
        result.Files[0].Content.Should().Contain("IgnoreReadOnlyProperties = true,");
        // ToJson を使ったディープコピー（JSON ラウンドトリップ）。戻り値は EntityBase
        result.Files[0].Content.Should().Contain("public EntityBase Clone()");
        // EntityBase が使う namespace は using に含め、テンプレートでは完全修飾しない
        result.Files[0].Content.Should().Contain("using System.Text.Json;");
        result.Files[0].Content.Should().Contain("using System.Text.Json.Serialization;");
        result.Files[0].Content.Should().Contain("using System.Collections;");
        result.Files[0].Content.Should().Contain("using System.Reflection;");
        result.Files[0].Content.Should().NotContain("System.Text.Json.JsonSerializer");
        result.Files[0].Content.Should().NotContain("System.Collections.StructuralComparisons");
        // ToJson / Clone は共有 static オプションを使い回し（毎回 new しない）、循環は IgnoreCycles で安全化
        result
            .Files[0]
            .Content.Should()
            .Contain("ReferenceHandler = ReferenceHandler.IgnoreCycles,");
        result
            .Files[0]
            .Content.Should()
            .Contain("writeIndented ? _jsonOptionsIndented : _jsonOptions");
        result.Files[0].Content.Should().NotContain("_cloneJsonOptions");

        var content = result.Files[0].Content;
        // EditModel も RowState を保持し、確定値変更時に Updated へ昇格する
        content.Should().Contain("public abstract partial class EditModelBase");
        content.Should().Contain("public RowState RowState");
        content.Should().Contain("public void MarkAdded() => RowState = RowState.Added;");
        content.Should().Contain("public void MarkRemoved() => RowState = RowState.Removed;");
        // 確定値（非バインド）setter は、ロード中以外のみ横断フック＋昇格条件を経て MarkUpdated で昇格する
        content.Should().Contain("if (!IsLoading)");
        content.Should().Contain("OnConfirmedValueChanged(nameof(CustomerId));");
        content.Should().Contain("if (ShouldMarkUpdated(nameof(CustomerId)))");
        content.Should().Contain("MarkUpdated();");
        // 拡張ポイント：確定値変更の横断フックと昇格条件、SetProperty は override 可能
        content
            .Should()
            .Contain("protected virtual void OnConfirmedValueChanged(string propertyName)");
        content
            .Should()
            .Contain("protected virtual bool ShouldMarkUpdated(string propertyName) => true;");
        content
            .Should()
            .Contain(
                "protected virtual bool SetProperty<T>(ref T field, T value, string propertyName)"
            );
        content.Should().Contain("public void ExecuteLoad(Action action)");
        content.Should().Contain("editModel.ExecuteLoad(() =>");
        // 生成の定型（EditModel 反映・コレクション化）は共通基底 MapperBase が提供し、具象 Mapper はそれを継承する
        content.Should().Contain("public abstract partial class MapperBase<TEntity, TEditModel>");
        content.Should().Contain(": MapperBase<CustomerEntity, CustomerEditModel>");
        content.Should().Contain(": MapperBase<OrderEntity, OrderEditModel>");
        // 新規入力用ファクトリ（基底が提供）は Entity を基に生成し、具象の生成フックを呼ぶ
        content.Should().Contain("public TEditModel CreateEditModel()");
        content.Should().Contain("OnEditModelCreated(editModel);");
        content.Should().Contain("partial void OnEditModelCreated(CustomerEditModel editModel);");
        // ApplyToEntity は MarkUpdated を撤廃し RowState を転写、子コレクションは CreateEntities で代入する
        content.Should().NotContain("entity.MarkUpdated();");
        content.Should().Contain("entity.RowState = editModel.RowState;");
        content
            .Should()
            .Contain(
                "entity.Orders = new OrderMapper().CreateEntities(editModel.Orders, includeRemoved);"
            );
        // 削除追跡コレクションと、CreateEntities が includeRemoved 指定時に Removed 分も含める
        content
            .Should()
            .Contain(
                "public sealed partial class EditModelCollection<T> : ObservableCollection<T>"
            );
        content.Should().Contain("public List<TEntity> CreateEntities(");
        content
            .Should()
            .Contain(
                "editModels.RemovedItems.Select(removed => CreateEntity(removed, includeRemoved))"
            );
        // EditModelCollection の変更集約（HasChanges / AcceptChanges）
        content
            .Should()
            .Contain(
                "public bool HasChanges => _removed.Count > 0 || this.Any(item => item.HasGraphChanges());"
            );
        content.Should().Contain("public void AcceptChanges(bool includeChildren = true)");
        // 親モデル取得（ParentModel）: 基底に保持＋通知、コレクションは OwnerModel を全要素へ伝播する
        content.Should().Contain("public EditModelBase? ParentModel => _parentModel;");
        // 親が一意（Order の親は Customer のみ）なので OrderEditModel は型付き ParentModel を生成する
        content.Should().Contain("public new CustomerEditModel? ParentModel =>");
        content.Should().Contain("base.ParentModel as CustomerEditModel;");
        content.Should().Contain("internal void SetParentModel(EditModelBase? parentModel)");
        content
            .Should()
            .Contain(
                "internal void RaiseParentCollectionChanged() => OnPropertyChanged(\"ParentCollection\");"
            );
        content.Should().Contain("internal EditModelBase? OwnerModel");
        content.Should().Contain("item.SetParentModel(value);");
        // EditModelCollection の一括操作・検証・並び替え API
        content
            .Should()
            .Contain(
                "public IEnumerable<EditModelError> CollectErrors(bool includeChildren = true)"
            );
        content.Should().Contain("this[i].CollectErrors($\"[{i}]\", includeChildren, errors);");
        content.Should().Contain("public bool MoveTo(T item, int newIndex)");
        content.Should().Contain("public void AddRange(IEnumerable<T> items)");
        content.Should().Contain("public void InsertRange(int index, IEnumerable<T> items)");
        content.Should().Contain("public void RemoveAll()");
        content.Should().Contain("public void RemoveRange(int index, int count)");
        // EditModel 列挙を受け取るコンストラクタと、Entity 列挙から生成する Mapper メソッド
        content.Should().Contain("public EditModelCollection(IEnumerable<T> items)");
        content.Should().Contain("public EditModelCollection<TEditModel> CreateEditModels(");
        // 子コレクションナビはバッキングフィールド＋プロパティで生成され、要素の親モデルリンク（OwnerModel）を張る
        content
            .Should()
            .Contain(
                "private EditModelCollection<OrderEditModel> _orders = new EditModelCollection<OrderEditModel>();"
            );
        content.Should().Contain("public EditModelCollection<OrderEditModel> Orders");
        content.Should().Contain("_orders.OwnerModel ??= this;");
        content.Should().Contain("_orders.OwnerModel = this;");
        content.Should().Contain("public override void ApplyToEntity(");
        // ApplyToEditModel は子コレクションを CreateEditModels で代入し、状態は生成元 Entity を基準にする
        content
            .Should()
            .Contain("editModel.Orders = new OrderMapper().CreateEditModels(entity.Orders);");
        content.Should().Contain("editModel.RowState = entity.RowState;");
        // EditModel.Validate（必須チェック＋ユーザー定義フック＋子への連鎖検証）
        // 必須メッセージの既定は全 EditModel 共通の静的プロバイダ、個別調整は具象クラスの partial
        content.Should().Contain("public static class EditModelMessages");
        content
            .Should()
            .Contain(
                "private string ResolveRequiredErrorMessage(string propertyName, string displayName)"
            );
        // 呼び出し口（Validate）は Base 側で定義し、固有処理は具象クラスの override に分離する
        content.Should().Contain("public bool Validate(bool includeChildren = true)");
        content.Should().Contain("protected virtual void ValidateSelf()");
        content.Should().Contain("protected override void ValidateSelf()");
        content
            .Should()
            .Contain(
                "SetError(nameof(BindingCustomerId), ResolveRequiredErrorMessage(nameof(CustomerId), GetDisplayName(nameof(CustomerId), null)));"
            );
        content.Should().Contain("partial void OnValidate();");
        content.Should().Contain("if (includeChildren)");
        // カスケードは ChildLink レジストリに一本化。子用の仮想メソッドと空オーバーライドは廃止
        content.Should().NotContain("ValidateChildren");
        content.Should().NotContain("CollectChildErrors");
        content.Should().Contain("protected virtual void RegisterChildren()");
        content.Should().Contain("protected virtual void RegisterExtraChildren()");
        content
            .Should()
            .Contain("protected void AddChild(string name, Func<EditModelBase?> accessor)");
        content
            .Should()
            .Contain(
                "protected void AddChildren<T>(string name, EditModelCollection<T> collection)"
            );
        content.Should().Contain("foreach (var link in ChildLinks)");
        // 子（カスケードナビ）を持つ EditModel は RegisterChildren を override し AddChildren で登録する
        content.Should().Contain("protected override void RegisterChildren()");
        content.Should().Contain("AddChildren(\"Orders\", Orders);");
        // グラフ全体のエラーをノードのパス付きで収集する（収集は ChildLink レジストリ経由）
        content
            .Should()
            .Contain(
                "public sealed record EditModelError(string Path, string Property, string Message);"
            );
        content
            .Should()
            .Contain(
                "public IEnumerable<EditModelError> CollectErrors(bool includeChildren = true)"
            );
        content
            .Should()
            .Contain(
                "internal void CollectErrors(string path, bool includeChildren, List<EditModelError> errors)"
            );
        content.Should().Contain("if (!includeChildren)");
        content.Should().Contain("errors.AddRange(CollectOwnErrors(path));");
        content.Should().Contain("link.CollectErrors(path, includeChildren, errors);");
        content.Should().NotContain("OnCollectChildErrors");
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
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = category,
                    TableName = "AirconditionerCategory",
                    Columns =
                    [
                        new Column
                        {
                            Id = categoryId,
                            Name = "AirconditionerCategoryId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = item,
                    TableName = "Airconditioner",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "AirconditionerId",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = category,
                    TargetEntityId = item,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = categoryId,
                    TargetColumnId = itemCategoryId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result
            .Files[0]
            .Content.Should()
            .Contain("public partial class AirconditionerCategoryEntity");
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "public ICollection<AirconditionerEntity> Airconditioners { get; set; } = new List<AirconditionerEntity>();"
            );
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "public AirconditionerCategoryEntity AirconditionerCategory { get; set; } = null!;"
            );
    }

    /// <summary>スネークケースのテーブル名がパスカルケースのエンティティ名へ変換されることを検証する</summary>
    [Fact]
    public void Generate_ShouldConvertSnakeCaseTableNamesToPascalCaseEntityNames()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "airconditioner_category",
                    Columns =
                    [
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result
            .Files[0]
            .Content.Should()
            .Contain("public partial class AirconditionerCategoryEntity");
        result
            .Files[0]
            .Content.Should()
            .Contain("public partial class AirconditionerCategoryEditModel");
        result
            .Files[0]
            .Content.Should()
            .Contain("public sealed partial class AirconditionerCategoryMapper");
    }

    /// <summary>多対多リレーションが警告付きで生成スキップされることを検証する</summary>
    [Fact]
    public void Generate_ShouldWarnAndSkipManyToManyRelationship()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = left,
                    TableName = "users",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = right,
                    TableName = "roles",
                    Columns =
                    [
                        new Column
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = left,
                    TargetEntityId = right,
                    Type = RelationshipType.ManyToMany,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        // 警告文言はカルチャ依存のため、resx テンプレートのプレースホルダ前プレフィックスで照合する
        var manyToManyWarningPrefix = Strings.CodeGen_Warning_ManyToManySkipped.Split("{0}")[0];
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                && diagnostic.Message.Contains(manyToManyWarningPrefix)
            );
        result.Files[0].Content.Should().NotContain("ICollection<RoleEntity>");
    }

    /// <summary>Entity ↔ EditModel を変換する Mapper クラスが生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateMapperClass()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "products",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "product_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        // Mapper は具象クラスのみ（インターフェースなし）。フック宣言のため partial
        result.Files[0].Content.Should().NotContain("public interface IProductMapper");
        result.Files[0].Content.Should().Contain("public sealed partial class ProductMapper");
        result.Files[0].Content.Should().NotContain(": IProductMapper");
        result
            .Files[0]
            .Content.Should()
            .Contain("ProductEditModel CreateEditModel(ProductEntity entity)");
        result.Files[0].Content.Should().Contain("void ApplyToEntity(");
        // 旧 Commit 系の名前は残っていないこと
        result.Files[0].Content.Should().NotContain("CommitToEditModel");
        result.Files[0].Content.Should().NotContain("CommitToEntity");
        // 空生成ファクトリと初期値フックは具象 Mapper が override で提供する
        result.Files[0].Content.Should().Contain("public override ProductEntity CreateEntity()");
        // EditModel 反映を含む生成の定型は共通基底 MapperBase が提供し、具象はそれを継承する
        result
            .Files[0]
            .Content.Should()
            .Contain("public abstract partial class MapperBase<TEntity, TEditModel>");
        result.Files[0].Content.Should().Contain(": MapperBase<ProductEntity, ProductEditModel>");
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "public TEntity CreateEntity(TEditModel editModel, bool includeRemoved = false)"
            );
        result
            .Files[0]
            .Content.Should()
            .Contain("partial void OnEntityCreated(ProductEntity entity);");
        // ApplyToEntity では nullable 化された確定値に対して保存前 null チェックを行う
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "editModel.ProductId ?? throw new InvalidOperationException(\"ProductId has no input value.\");"
            );
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "editModel.Name ?? throw new InvalidOperationException(\"Name has no input value.\");"
            );
        // Entity → EditModel 反映は public な ApplyToEditModel で行い、バインディング用プロパティ経由でロードする
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "public void ApplyToEditModel(ProductEntity entity, ProductEditModel editModel)"
            );
        result.Files[0].Content.Should().Contain("editModel.BindingName =");
        result.Files[0].Content.Should().NotContain("LoadFrom");
        // 既定ロード後の後処理フック（partial 実装で追加プロパティをロード）
        result.Files[0].Content.Should().Contain("OnEditModelLoaded(entity, editModel);");
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "partial void OnEditModelLoaded(ProductEntity entity, ProductEditModel editModel);"
            );
        // 反映後フック（partial 実装で追加プロパティを保存）
        result.Files[0].Content.Should().Contain("OnEntityApplied(editModel, entity);");
        result
            .Files[0]
            .Content.Should()
            .Contain(
                "partial void OnEntityApplied(ProductEditModel editModel, ProductEntity entity);"
            );
    }

    /// <summary>EditModel にバインディング文字列を確定値へ戻す処理が生成されることを検証する</summary>
    [Fact]
    public void Generate_EditModel_ShouldContainRevertInputMethod()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 通常プロパティ (private set)
        content.Should().Contain("public int? OrderId");
        content.Should().Contain("public decimal? Amount");
        // バインディング用プロパティ (string)
        content.Should().Contain("public string BindingOrderId");
        content.Should().Contain("public string BindingAmount");
        // TryParse 検証
        content.Should().Contain("int.TryParse(normalized, out var parsed)");
        content.Should().Contain("decimal.TryParse(normalized, out var parsed)");
        // エラーメッセージは ResolveParseErrorMessage 経由で生成され、安定キー（nameof）と表示名を渡す
        // （Description 無指定は null を渡し、ヘルパ側でプロパティ名へフォールバックする）
        content
            .Should()
            .Contain(
                "ResolveParseErrorMessage(nameof(OrderId), GetDisplayName(nameof(OrderId), null), normalized, \"int\")"
            );
        content
            .Should()
            .Contain(
                "ResolveParseErrorMessage(nameof(Amount), GetDisplayName(nameof(Amount), null), normalized, \"decimal\")"
            );
        // 既定文言は全 EditModel 共通の静的プロバイダ、個別調整は具象クラスの Resolve*／Customize* が担う
        content
            .Should()
            .Contain(
                "public static Func<string, string, string, string> ParseFailed { get; set; }"
            );
        content
            .Should()
            .Contain(
                "var message = EditModelMessages.ParseFailed(displayName, inputValue, typeName);"
            );
        content.Should().Contain("partial void CustomizeParseErrorMessage(");
        // 廃止した EditModelBase の virtual ビルダーは残っていない
        content.Should().NotContain("BuildParseErrorMessage");
        content.Should().NotContain("BuildRequiredErrorMessage");
        // RevertInput は Base 側に集約し、書き戻し本体は具象クラスの RevertCore で override する
        content.Should().Contain("public void RevertInput() => ExecuteRevert(RevertCore);");
        content.Should().Contain("protected virtual void RevertCore()");
        content.Should().Contain("protected override void RevertCore()");
        // 兄弟ナビゲーション：Base が所属コレクション（IList）経由の GetNext/GetPrevious を提供し、コレクションが所有者を設定、具象クラスが型付き版を生成する
        content.Should().Contain("internal IList? Owner { get; set; }");
        content.Should().Contain("public EditModelBase? GetNext()");
        content.Should().Contain("public EditModelBase? GetPrevious()");
        content.Should().Contain("var index = Owner.IndexOf(this);");
        content.Should().Contain("item.Owner = this;");
        content
            .Should()
            .Contain("public new OrderEditModel? GetNext() => (OrderEditModel?)base.GetNext();");
        content
            .Should()
            .Contain(
                "public new OrderEditModel? GetPrevious() => (OrderEditModel?)base.GetPrevious();"
            );
        // 親コレクション基準の位置取得・削除・並び替え。位置系と削除は Base（IList）、Move は型付き Parent と MoveCore の override で実装する
        content.Should().Contain("public int IndexInParent => Owner?.IndexOf(this) ?? -1;");
        content.Should().Contain("public bool IsFirstInParent => IndexInParent == 0;");
        content.Should().Contain("public bool IsLastInParent");
        content.Should().Contain("public bool RemoveFromParent()");
        content.Should().Contain("public bool MoveToFirst() => MoveSelfTo(0);");
        content
            .Should()
            .Contain(
                "public bool MoveToLast() => Owner is not null && MoveSelfTo(Owner.Count - 1);"
            );
        content.Should().Contain("public bool MoveToNext()");
        content.Should().Contain("public bool MoveToPrevious()");
        content.Should().Contain("protected virtual void MoveCore(int oldIndex, int newIndex)");
        // 所属コレクション取得は ParentCollection に改名（親モデル取得の ParentModel と区別する）
        content
            .Should()
            .Contain("public EditModelCollection<OrderEditModel>? ParentCollection =>");
        content.Should().NotContain("EditModelCollection<OrderEditModel>? Parent =>");
        // 親（親参照ナビ）を持たない単独エンティティでは型付き ParentModel は生成されず、基底の EditModelBase? のみ
        content.Should().Contain("public EditModelBase? ParentModel => _parentModel;");
        content.Should().NotContain("base.ParentModel as");
        content.Should().Contain("protected override void MoveCore(int oldIndex, int newIndex) =>");
        // ② RowState 変更時に派生フラグの変更通知も発行する
        content.Should().Contain("OnPropertyChanged(nameof(IsAdded));");
        content.Should().Contain("OnPropertyChanged(nameof(HasChanges));");
        // ① AcceptChanges / ③ HasGraphChanges は Base の公開エントリのみ（カスケードは ChildLink 経由）
        content.Should().Contain("public void AcceptChanges(bool includeChildren = true)");
        content.Should().Contain("public bool HasGraphChanges(bool includeChildren = true)");
        // 子用の仮想メソッド・空オーバーライドは全廃（カスケードなしの EditModel では何も生成されない）
        content.Should().NotContain("AcceptChildChanges");
        content.Should().NotContain("ChildHasChanges");
        content.Should().NotContain("OnAcceptChildChanges");
        content.Should().NotContain("OnChildHasChanges");
        // カスケードナビを持たないこの EditModel には RegisterChildren の override は生成されない
        content.Should().NotContain("protected override void RegisterChildren()");
        // ② 発見性：拡張ポイント一覧コメントを生成
        content
            .Should()
            .Contain("Extension points (implement only what you need in a partial class");
        // ④ IEditableObject（DataGrid 行編集の取り消し対応）
        content.Should().Contain("IEditableObject");
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
        content
            .Should()
            .Contain(
                "internal void RaiseParentCollectionChanged() => OnPropertyChanged(\"ParentCollection\");"
            );
        content.Should().Contain("private void NotifyPositionsChanged()");
        content.Should().Contain("protected override void MoveItem(int oldIndex, int newIndex)");
    }

    /// <summary>
    /// 親モデル取得（ParentModel）について、1対1の単一子・自己参照・複数親の各パターンで型付き ParentModel の
    /// 生成有無と単一子の所有者リンク設定が正しく出力されることを検証する
    /// </summary>
    [Fact]
    public void Generate_EditModel_ShouldEmitTypedParentModelForUnambiguousParents()
    {
        var owner = Guid.NewGuid();
        var ownerPk = Guid.NewGuid();
        var profile = Guid.NewGuid();
        var profilePk = Guid.NewGuid();
        var profileFk = Guid.NewGuid();
        var category = Guid.NewGuid();
        var categoryPk = Guid.NewGuid();
        var categoryParentFk = Guid.NewGuid();
        var salesOrder = Guid.NewGuid();
        var salesOrderPk = Guid.NewGuid();
        var product = Guid.NewGuid();
        var productPk = Guid.NewGuid();
        var lineItem = Guid.NewGuid();
        var lineItemPk = Guid.NewGuid();
        var lineOrderFk = Guid.NewGuid();
        var lineProductFk = Guid.NewGuid();

        static Column Pk(Guid id, string name) =>
            new()
            {
                Id = id,
                Name = name,
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            };
        static Column Fk(Guid id, string name) =>
            new()
            {
                Id = id,
                Name = name,
                DataType = "int",
                IsForeignKey = true,
                IsNullable = false,
            };

        var diagram = new ErDiagram
        {
            Entities =
            [
                new()
                {
                    Id = owner,
                    TableName = "profile_owner",
                    Columns = [Pk(ownerPk, "profile_owner_id")],
                },
                new()
                {
                    Id = profile,
                    TableName = "profile",
                    Columns = [Pk(profilePk, "profile_id"), Fk(profileFk, "profile_owner_id")],
                },
                new()
                {
                    Id = category,
                    TableName = "category",
                    Columns =
                    [
                        Pk(categoryPk, "category_id"),
                        Fk(categoryParentFk, "parent_category_id"),
                    ],
                },
                new()
                {
                    Id = salesOrder,
                    TableName = "sales_order",
                    Columns = [Pk(salesOrderPk, "sales_order_id")],
                },
                new()
                {
                    Id = product,
                    TableName = "product",
                    Columns = [Pk(productPk, "product_id")],
                },
                new()
                {
                    Id = lineItem,
                    TableName = "line_item",
                    Columns =
                    [
                        Pk(lineItemPk, "line_item_id"),
                        Fk(lineOrderFk, "sales_order_id"),
                        Fk(lineProductFk, "product_id"),
                    ],
                },
            ],
            Relationships =
            [
                // 1対1: profile_owner -> profile（単一の子）
                new()
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToOne,
                    SourceEntityId = owner,
                    TargetEntityId = profile,
                    SourceColumnId = ownerPk,
                    TargetColumnId = profileFk,
                },
                // 自己参照: category -> category
                new()
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = category,
                    TargetEntityId = category,
                    SourceColumnId = categoryPk,
                    TargetColumnId = categoryParentFk,
                },
                // 複数親: line_item は sales_order と product の両方を親に持つ
                new()
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = salesOrder,
                    TargetEntityId = lineItem,
                    SourceColumnId = salesOrderPk,
                    TargetColumnId = lineOrderFk,
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = product,
                    TargetEntityId = lineItem,
                    SourceColumnId = productPk,
                    TargetColumnId = lineProductFk,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateMappers = false,
                GenerateRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // 1対1の単一子: 親 ProfileOwner が Profile を保持し、setter で子の親モデルリンクを張る
        content.Should().Contain("private ProfileEditModel _profile = null!;");
        content.Should().Contain("_profile?.SetParentModel(null);");
        content.Should().Contain("_profile?.SetParentModel(this);");
        // 子 Profile は親が一意なので型付き ParentModel（ProfileOwnerEditModel）を生成する
        content.Should().Contain("public new ProfileOwnerEditModel? ParentModel =>");
        content.Should().Contain("base.ParentModel as ProfileOwnerEditModel;");

        // 自己参照: Category は自分自身の型で型付き ParentModel を生成する
        content.Should().Contain("public new CategoryEditModel? ParentModel =>");
        content.Should().Contain("base.ParentModel as CategoryEditModel;");

        // 複数親: LineItem は親が一意に定まらないため型付き ParentModel を生成しない
        content.Should().NotContain("base.ParentModel as LineItemEditModel;");
        content.Should().NotContain("base.ParentModel as SalesOrderEditModel;");
        content.Should().NotContain("base.ParentModel as ProductEditModel;");
    }

    /// <summary>Repository インターフェース・実装・DI 登録などの基盤コードが生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateRepositoryInfrastructure()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("using Microsoft.Data.SqlClient;");
        content.Should().Contain("using Microsoft.Extensions.DependencyInjection;");
        // Repository 関連はユーザー拡張用に partial（インターフェース・基底クラス・各実装）
        content.Should().Contain("public partial interface IRepository<TEntity, TKey>");
        content
            .Should()
            .Contain("public abstract partial class SqlServerRepository<TEntity, TKey>");
        content.Should().Contain("internal sealed class EntitySaveMetadata");
        content
            .Should()
            .Contain(
                "public partial interface ICustomerRepository : IRepository<CustomerEntity, int>"
            );
        content
            .Should()
            .Contain(
                "public sealed partial class CustomerRepository(\r\n    ISqlConnectionFactory connectionFactory,\r\n    ISaveHookRegistry? saveHooks = null\r\n)"
            );
        content.Should().Contain("services.AddScoped<ICustomerRepository, CustomerRepository>();");
        // カラム一覧は columnList へ抽出して SELECT 系で共用する（無制限バイナリ列を除いた SELECT 用列集合）
        content
            .Should()
            .Contain(
                "var columnList = string.Join(\", \", selectProperties.Select(property => $\"[{GetColumnName(property)}]\"));"
            );
        content
            .Should()
            .Contain(
                "SelectByIdSql = $\"SELECT {columnList} FROM {tableName} WHERE [{keyColumnName}] = @id;\""
            );
        content
            .Should()
            .Contain(
                "$\"INSERT INTO {tableName} ({string.Join(\", \", insertProperties.Select(property => $\"[{GetColumnName(property)}]\"))}) VALUES ({string.Join(\", \", insertProperties.Select(property => $\"@{property.Name}\"))});\""
            );
        content
            .Should()
            .Contain(
                "$\"UPDATE {tableName} SET {string.Join(\", \", updateAssignments)} WHERE [{keyColumnName}] = @id;\""
            );
        content
            .Should()
            .Contain("DeleteSql = $\"DELETE FROM {tableName} WHERE [{keyColumnName}] = @id;\"");
    }

    /// <summary>
    /// 生 SQL 実行の 3 メソッド（QueryBySqlAsync / ExecuteSqlAsync / ExecuteScalarSqlAsync）が
    /// IRepository インターフェースと SqlServerRepository 基底の両方に生成され、
    /// パラメータ束縛・厳密マッピングの補助（BindRawSqlParameters / MapEntityFromRawSql）も出力されることを検証する
    /// </summary>
    [Fact]
    public void Generate_Repository_ShouldEmitRawSqlMethods()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // インターフェース側のシグネチャ（3 メソッド。abstract メソッド宣言なので本文なし）
        content.Should().Contain("Task<IReadOnlyList<TEntity>> QueryBySqlAsync(");
        content.Should().Contain("Task<int> ExecuteSqlAsync(");
        content.Should().Contain("Task<TResult?> ExecuteScalarSqlAsync<TResult>(");

        // 実装側（SqlServerRepository 基底）は公開シグネチャを保ちつつ ISqlExecutor へ委譲する
        content.Should().Contain("public Task<IReadOnlyList<TEntity>> QueryBySqlAsync(");
        content.Should().Contain("public Task<int> ExecuteSqlAsync(");
        content.Should().Contain("public Task<TResult?> ExecuteScalarSqlAsync<TResult>(");
        content
            .Should()
            .Contain("_sqlExecutor.QueryBySqlAsync<TEntity>(sql, parameters, cancellationToken);");
        content
            .Should()
            .Contain("_sqlExecutor.ExecuteSqlAsync(sql, parameters, cancellationToken);");
        content
            .Should()
            .Contain(
                "_sqlExecutor.ExecuteScalarSqlAsync<TResult>(sql, parameters, cancellationToken);"
            );
        // 委譲先は SqlExecutor（Repository が private readonly で保持）
        content
            .Should()
            .Contain(
                "private readonly ISqlExecutor _sqlExecutor = new SqlExecutor(connectionFactory);"
            );

        // SqlCommand 版パラメータ束縛は SqlExecutor 側に集約（束縛対象プロパティの解決は RawSqlMapper と共有）
        content
            .Should()
            .Contain("internal static void BindParameters(SqlCommand command, object? parameters)");
        content
            .Should()
            .Contain(
                "command.Parameters.AddWithValue($\"@{property.Name}\", value ?? DBNull.Value);"
            );
        content.Should().NotContain("BindRawSqlParameters");
        // 厳密マッピング（列不足は全列を含む例外でラップ）は EntitySaveMetadata に残る
        content
            .Should()
            .Contain("public TEntity MapEntityFromRawSql<TEntity>(SqlDataReader reader)");
        content
            .Should()
            .Contain(
                "catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)"
            );
        content.Should().Contain("columns ({ColumnList})");
        // スカラー・単一値変換は ChangeType(InvariantCulture) / 変換不能で例外
        content
            .Should()
            .Contain("Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture)");
        // InvariantCulture 使用のため System.Globalization を using
        content.Should().Contain("using System.Globalization;");
    }

    /// <summary>
    /// エンティティ非依存の生 SQL 実行器 <c>ISqlExecutor</c> / <c>SqlExecutor</c> が生成され、
    /// 任意型射影 <c>QueryProjectionBySqlAsync</c>（単一値モード・DTO モード・typo ガード）と
    /// DI 登録（Singleton）が出力されることを検証する
    /// </summary>
    [Fact]
    public void Generate_Repository_ShouldEmitSqlExecutorAndProjection()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // インターフェースと実装（partial・sealed・ステートレス）
        content.Should().Contain("public partial interface ISqlExecutor");
        content
            .Should()
            .Contain(
                "public sealed partial class SqlExecutor(ISqlConnectionFactory connectionFactory) : ISqlExecutor"
            );

        // 任意型射影のシグネチャ（インターフェース・実装の両方）
        content
            .Should()
            .Contain("Task<IReadOnlyList<TResult>> QueryProjectionBySqlAsync<TResult>(");
        content
            .Should()
            .Contain(
                "public async Task<IReadOnlyList<TResult>> QueryProjectionBySqlAsync<TResult>("
            );
        // エンティティ厳密マップも執行器に存在する（型引数版）
        content
            .Should()
            .Contain("public async Task<IReadOnlyList<TEntity>> QueryBySqlAsync<TEntity>(");

        // 単一値モードの型判定（primitive/enum/string/decimal/日時/Guid/byte[]）
        content.Should().Contain("private static bool IsSingleValueType(Type type)");
        content.Should().Contain("actual == typeof(byte[])");
        // DTO モード: 引数なしコンストラクタ必須・位置指定 record 非対応の例外
        content.Should().Contain("positional records are not supported");
        // typo ガード: 1 列も一致しないと列名・プロパティ名を含む例外
        content.Should().Contain("No column in the result set matches");
        // 列⇔プロパティ解決子は ConcurrentDictionary でキャッシュ
        content
            .Should()
            .Contain(
                "private static readonly ConcurrentDictionary<Type, ProjectionAccessor> _projectionAccessorCache"
            );

        // DI 登録（SqlExecutor は Singleton）
        content.Should().Contain("services.AddSingleton<ISqlExecutor, SqlExecutor>();");
    }

    /// <summary>
    /// SQL パラメータ型明示化のため、Repository 生成時に Entity プロパティへ [SqlColumnType(...)] が
    /// DB 型に応じて付与されること（varchar(50)→VarChar+Size50 / nvarchar(max)→NVarChar+Size-1 /
    /// decimal(10,2)→Precision10/Scale2 / int→Int / 未知型→属性なし）を検証する
    /// </summary>
    [Fact]
    public void Generate_Repository_ShouldEmitSqlColumnTypeAttributes()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
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
                            Name = "code",
                            DataType = "varchar(50)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "note",
                            DataType = "nvarchar(max)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "price",
                            DataType = "decimal(10,2)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "location",
                            DataType = "geography",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 属性定義（SqlDbType 引数・Size/Precision/Scale の書き換え可能プロパティ）
        content.Should().Contain("public sealed class SqlColumnTypeAttribute : Attribute");
        content
            .Should()
            .Contain("public SqlColumnTypeAttribute(SqlDbType dbType) => DbType = dbType;");
        // int → 型だけ（Size/Precision なし）
        content.Should().Contain("[SqlColumnType(SqlDbType.Int)]");
        // varchar(50) → VarChar + Size 50
        content.Should().Contain("[SqlColumnType(SqlDbType.VarChar, Size = 50)]");
        // nvarchar(max) → NVarChar + Size -1
        content.Should().Contain("[SqlColumnType(SqlDbType.NVarChar, Size = -1)]");
        // decimal(10,2) → Decimal + Precision/Scale
        content.Should().Contain("[SqlColumnType(SqlDbType.Decimal, Precision = 10, Scale = 2)]");
        // 未知型（geography）は属性を付けない（AddWithValue フォールバック）
        content.Should().NotContain("SqlColumnType(SqlDbType.Udt");
        // ランタイムは属性から明示 SqlParameter を組み立て、Size 安全ガードで超過時は値長を使う
        content.Should().Contain("private static void AddColumnParameter(");
        content.Should().Contain("var parameter = new SqlParameter(name, attribute.DbType);");
        content
            .Should()
            .Contain(
                "parameter.Size = valueLength > attribute.Size ? valueLength : attribute.Size;"
            );
    }

    /// <summary>
    /// [SqlColumnType] の出力条件が「Repository 生成 または IncludeDataAnnotations」の OR であることを検証する
    /// （ColumnFacets 廃止・SqlColumnType への統合により、DataAnnotations 単独でも列メタ情報が必要なため）。
    /// 両方 OFF のときのみ属性が一切出力されない。
    /// </summary>
    [Fact]
    public void Generate_SqlColumnTypeAttribute_IsGatedOnRepositoryOrDataAnnotations()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
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

        // 両方 OFF → 属性は一切出力されない
        var neither = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                IncludeDataAnnotations = false,
            }
        );
        neither.HasErrors.Should().BeFalse();
        neither.Files[0].Content.Should().NotContain("SqlColumnType");

        // Repository ON（Repository は IncludeDataAnnotations 必須のため両方 ON）→ 属性が出力される
        var repoOn = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );
        repoOn.HasErrors.Should().BeFalse();
        repoOn.Files[0].Content.Should().Contain("[SqlColumnType(SqlDbType.Int)]");

        // IncludeDataAnnotations のみ ON（Repository なし）→ 属性が出力される
        var annotationsOnly = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                IncludeDataAnnotations = true,
            }
        );
        annotationsOnly.HasErrors.Should().BeFalse();
        annotationsOnly.Files[0].Content.Should().Contain("[SqlColumnType(SqlDbType.Int)]");
    }

    /// <summary>Repository にラムダ式ベースのクエリビルダー（Query / Where / OrderBy / 終端メソッド）が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateLambdaQueryBuilder()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 式木変換に必要な using とクエリ基盤クラス
        content.Should().Contain("using System.Linq.Expressions;");
        content.Should().Contain("public sealed class SqlQuery<TEntity>");
        content.Should().Contain("internal static class SqlExpressionTranslator");
        // リポジトリ起点のクエリ開始メソッド
        content.Should().Contain("public SqlQuery<TEntity> Query() =>");
        // ラムダ式で条件・並び順を指定するチェーンメソッド
        content
            .Should()
            .Contain("public SqlQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)");
        content
            .Should()
            .Contain(
                "public SqlQuery<TEntity> OrderBy(Expression<Func<TEntity, object?>> keySelector)"
            );
        content
            .Should()
            .Contain(
                "public SqlQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object?>> keySelector)"
            );
        content.Should().Contain("public SqlQuery<TEntity> Take(int count)");
        content.Should().Contain("public SqlQuery<TEntity> Skip(int count)");
        // 終端メソッド一式
        content.Should().Contain("public async Task<IReadOnlyList<TEntity>> ToListAsync(");
        content.Should().Contain("public async Task<TEntity?> FirstOrDefaultAsync(");
        content.Should().Contain("public async Task<int> CountAsync(");
        content.Should().Contain("public async Task<bool> AnyAsync(");
        // 値はパラメータ化し、列名が判明していれば列型で明示 SqlParameter を構築（なければ AddWithValue へフォールバック）
        content
            .Should()
            .Contain(
                "metadata.AddQueryParameter(command, parameter.Name, parameter.ColumnName, value);"
            );
        content.Should().Contain("FETCH NEXT {take.Value} ROWS ONLY");
    }

    /// <summary>コレクションの Contains（配列・List など）が SQL の IN 句へ変換されるコードが生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldTranslateCollectionContainsToInClause()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // Contains を IN 変換の対象として判定するヘルパーと、IN 句を組み立てる本体
        content.Should().Contain("private static bool TryGetIn(");
        content.Should().Contain("private static string BuildInClause(");
        // Visit に IN 変換のケースが組み込まれている
        content.Should().Contain("when TryGetIn(call, out var inColumn, out var inCollection):");
        content.Should().Contain("return BuildInClause(inColumn, inCollection, parameters);");
        // 静的 Enumerable.Contains（配列・2 引数）／C# 14 の Span 版 MemoryExtensions.Contains（3 引数・比較子 null）と、
        // インスタンス Contains（List/HashSet）の双方を判定
        content.Should().Contain("if (call.Object is null && call.Arguments.Count is 2 or 3)");
        content
            .Should()
            .Contain("is not ConstantExpression { Value: null }", "非 null 比較子は翻訳対象外");
        content.Should().Contain("if (call.Object is not null && call.Arguments.Count == 1)");
        // 要素をパラメータ化して IN (...) を生成、空コレクションは恒偽条件
        content.Should().Contain("{column} IN ({string.Join(\", \", placeholders)})");
        content.Should().Contain("? \"1 = 0\"");
        // 非ジェネリック IEnumerable を使うため System.Collections を using
        content.Should().Contain("using System.Collections;");
    }

    /// <summary>IsNullOrEmpty・日付コンポーネント・Equals（大文字小文字無視含む）と、値オブジェクトの .Value 解決コードが生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldTranslateNullOrEmptyDatePartAndEquals()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // ② IsNullOrEmpty / IsNullOrWhiteSpace
        content.Should().Contain("private static bool TryGetNullOrEmpty(");
        content.Should().Contain("({neColumn} IS NULL OR {neColumn} = '')");
        content.Should().Contain("({neColumn} IS NULL OR LTRIM(RTRIM({neColumn})) = '')");
        // ④ 日付コンポーネント
        content.Should().Contain("private static bool TryGetDatePart(");
        content.Should().Contain("\"Year\" => $\"YEAR({column})\"");
        content.Should().Contain("\"Date\" => $\"CAST({column} AS date)\"");
        // ⑤ Equals（大文字小文字無視は LOWER で畳む）
        content.Should().Contain("private static bool TryGetEquals(");
        content.Should().Contain("$\"LOWER({eqColumn}) = LOWER({eqParameter})\"");
        // 値オブジェクトの .Value を列へ解決する共通ヘルパー
        content.Should().Contain("private static string? TryColumnName(");
        content
            .Should()
            .Contain("typeof(IValueObject).IsAssignableFrom(member.Member.DeclaringType)");
    }

    /// <summary>RowState ベースのカスケード Save 基盤（EntityBase / SaveAsync / 保存エンジン）が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateCascadeSaveInfrastructure()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 状態管理
        content.Should().Contain("public enum RowState");
        content.Should().Contain("public abstract partial class EntityBase");
        content.Should().Contain("public RowState RowState { get; set; } = RowState.Unchanged;");
        content.Should().Contain("public void MarkAdded() => RowState = RowState.Added;");
        content.Should().Contain("public void MarkRemoved() => RowState = RowState.Removed;");
        // リポジトリ・インターフェースの Save 入口（既定でカスケード、既定は更新欠落で例外）
        content.Should().Contain("bool insertWhenUpdateMissing = false,");
        content.Should().Contain("public async Task<int> SaveAsync(");
        content.Should().Contain("await connection.BeginTransactionAsync(cancellationToken)");
        // 複数集約ルートを 1 トランザクションでまとめて保存するコレクション overload
        content.Should().Contain("IEnumerable<TEntity> entities,");
        content.Should().Contain("public async Task<int> SaveAsync(");
        // SqlBulkCopy によるコレクション一括追加（IDataReader でストリーミング）
        content.Should().Contain("Task<int> BulkInsertAsync(");
        content.Should().Contain("public async Task<int> BulkInsertAsync(");
        content.Should().Contain("DestinationTableName = _metadata.TableName,");
        content.Should().Contain("using var reader = _metadata.CreateDataReader(entities);");
        content.Should().Contain("await bulkCopy.WriteToServerAsync(reader, cancellationToken);");
        content.Should().Contain("private sealed class EntityDataReader : IDataReader");
        // 保存エンジン・競合例外・メタデータ・連鎖情報
        content.Should().Contain("internal static class EntityGraphSaver");
        content.Should().Contain("internal sealed class EntitySaveMetadata");
        content.Should().Contain("public sealed class SaveConflictException : Exception");
        content.Should().Contain("internal sealed record CascadeNavigation(");
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
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // JSON デシリアライズ基盤
        content.Should().Contain("using System.Text.Json;");
        content.Should().Contain("JsonSerializer.Deserialize<List<TEntity>>(json, JsonOptions)");
        content.Should().Contain("FOR JSON PATH;");
        content.Should().Contain("internal static class JsonQueryPlanner");
        // Include / ThenInclude（単一・コレクションの両オーバーロード）
        content.Should().Contain("Expression<Func<TEntity, TProperty>> navigationSelector");
        content
            .Should()
            .Contain("Expression<Func<TEntity, ICollection<TElement>>> navigationSelector");
        content.Should().Contain("Expression<Func<TProperty, TNext>> navigationSelector");
        content
            .Should()
            .Contain("Expression<Func<TProperty, ICollection<TNext>>> navigationSelector");
        // 列はプロパティ名へ別名付け、単一参照は WITHOUT_ARRAY_WRAPPER
        content.Should().Contain("[{columnName}] AS {propertyName}");
        content.Should().Contain("WITHOUT_ARRAY_WRAPPER");
        // ネストした FOR JSON はそのままだと文字列化されるため JSON_QUERY で包む
        content
            .Should()
            .Contain(
                "JSON_QUERY((SELECT {childProjection} FROM {childTable} AS {childAlias} WHERE {correlation} FOR JSON PATH{arrayMode})) AS {node.Property.Name}"
            );
        // FOR JSON の複数行結果を連結する
        content.Should().Contain("string.Concat(chunks)");
    }

    /// <summary>条件付き一括削除（ExecuteDeleteAsync）とカスケード削除基盤が生成されることを検証する</summary>
    [Fact]
    public void Generate_ShouldCreateExecuteDeleteOnQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // クエリ終端の一括削除（カスケード引数つき）。既存 DeleteAsync(TKey) は維持
        content.Should().Contain("public async Task<int> ExecuteDeleteAsync(");
        content
            .Should()
            .Contain(
                "Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);"
            );
        // 非カスケードはセットベースの DELETE
        content.Should().Contain("$\"DELETE FROM {TableName}{whereClause};\"");
        // カスケードは FK のネスト IN(SELECT …) で子から削除（DB 非依存の純粋プランナーで SQL 構築）
        content.Should().Contain("internal static class CascadeDeletePlanner");
        content.Should().Contain("public static IReadOnlyList<string> BuildDeleteStatements(");
        content
            .Should()
            .Contain(
                "IN (SELECT [{navigation.PrincipalColumn}] FROM {parentTable}{parentScopeWhere})"
            );
        // 循環カスケードは未対応として明示的に例外
        content.Should().Contain("Cyclic cascade");
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
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer,
                    TargetEntityId = order,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = customerId,
                    TargetColumnId = orderCustomerId,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
        content
            .Should()
            .Contain(
                "public ICollection<OrderEntity> Orders { get; set; } = new List<OrderEntity>();"
            );
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
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "files",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "file_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public int? FileId");
        content.Should().Contain("public bool? IsActive");
        content.Should().Contain("public byte[] Filedata { get; set; } = Array.Empty<byte>();");
        content.Should().Contain("Filedata = Convert.FromBase64String(normalized);");
        content
            .Should()
            .Contain(
                "BindingFiledata = Filedata is null ? string.Empty : Convert.ToBase64String(Filedata);"
            );
        content.Should().Contain("Filedata = Array.Empty<byte>();");
        content.Should().Contain("BindingFileId = FileId?.ToString() ?? string.Empty;");
        content
            .Should()
            .Contain("editModel.BindingIsActive = entity.IsActive.ToString() ?? string.Empty;");
        content.Should().NotContain("entity.FileId?.ToString()");
        content.Should().NotContain("entity.IsActive?.ToString()");
        content.Should().NotContain("private string? _errorFiledata;");
        content
            .Should()
            .Contain(
                "private static readonly EntitySaveMetadata _metadata = EntitySaveMetadata.For(typeof(TEntity));"
            );
        content
            .Should()
            .Contain(
                "private readonly ISqlConnectionFactory _connectionFactory = connectionFactory;"
            );
    }

    /// <summary>Entity のみ生成設定で EditModel や Mapper が出力されないことを検証する</summary>
    [Fact]
    public void Generate_EntityOnly_ShouldNotContainUiModelOrMapper()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
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
            RootNamespace = "Sample.Domain",
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
            .Select(index => new Entity
            {
                Id = Guid.NewGuid(),
                TableName = $"items_{index}",
                Columns =
                [
                    new Column
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

        var diagram = new ErDiagram { Entities = entities };
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
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
            RootNamespace = "Sample.Domain",
            GenerateRepositories = true,
            IncludeDataAnnotations = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message == Strings.CodeGen_Error_RepositoryRequiresDataAnnotations
            );
    }

    /// <summary>Mapper を EditModel なしで生成しようとするとエラーになることを検証する</summary>
    [Fact]
    public void Generate_MapperWithoutEditModel_ShouldFailWithError()
    {
        var diagram = SingleEntityDiagram();
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = false,
            GenerateMappers = true,
            GenerateRepositories = false,
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, options);

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("Mapper")
            );
    }

    /// <summary>
    /// 単数形化で同じエンティティクラス名になるテーブル（customer / customers）はエラーになり、
    /// コンパイル不能な出力を書き出さないことを検証する
    /// </summary>
    [Fact]
    public void Generate_CollidingEntityClassNames_ShouldFailWithError()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customer",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("CustomerEntity")
                && diagnostic.Message.Contains("'customer'")
                && diagnostic.Message.Contains("'customers'")
            );
    }

    /// <summary>クラス名が衝突しないテーブル名なら生成できることを検証する（衝突検証の偽陽性防止）</summary>
    [Fact]
    public void Generate_DistinctEntityClassNames_ShouldSucceed()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customer",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customer_address",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_address_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("class CustomerEntity");
        result.Files[0].Content.Should().Contain("class CustomerAddressEntity");
    }

    /// <summary>
    /// 正規化で同じプロパティ名になる列（user-id / user_id）が同一エンティティにあるとエラーになり、
    /// コンパイル不能な出力を書き出さないことを検証する
    /// </summary>
    [Fact]
    public void Generate_CollidingColumnPropertyNames_ShouldFailWithError()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "users",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user-id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("users")
                && diagnostic.Message.Contains("UserId")
                && diagnostic.Message.Contains("'user-id'")
                && diagnostic.Message.Contains("'user_id'")
            );
    }

    /// <summary>プロパティ名が衝突しない列なら生成できることを検証する（衝突検証の偽陽性防止）</summary>
    [Fact]
    public void Generate_DistinctColumnPropertyNames_ShouldSucceed()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "users",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public int UserId");
        result.Files[0].Content.Should().Contain("public string UserName");
    }

    /// <summary>
    /// 別エンティティに同じプロパティ名の列があっても衝突扱いにならないことを検証する
    /// （プロパティは別クラスのメンバーになるため）
    /// </summary>
    [Fact]
    public void Generate_SamePropertyNameInDifferentEntities_ShouldSucceed()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "users",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user-id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "user_profiles",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("class UserEntity");
        result.Files[0].Content.Should().Contain("class UserProfileEntity");
    }

    /// <summary>
    /// EditModel の派生名（Binding{Prop}）と衝突する列（id / binding_id）があるとエラーになり、
    /// コンパイル不能な出力を書き出さないことを検証する（列プロパティ名同士は衝突しないため、
    /// シンボル表検証だけが検出できる系統）
    /// </summary>
    [Fact]
    public void Generate_CollidingEditModelBindingMemberNames_ShouldFailWithError()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
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
                            Name = "binding_id",
                            DataType = "int",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("ItemEditModel")
                && diagnostic.Message.Contains("'BindingId'")
                && diagnostic.Message.Contains("'id'")
                && diagnostic.Message.Contains("'binding_id'")
            );
    }

    /// <summary>
    /// 列由来プロパティ名がナビゲーションプロパティ名と衝突する（customer 列 ＋ customers への参照）と
    /// エラーになることを検証する。EditModel を生成しない構成でも Entity 側が壊れるため検出できる必要がある
    /// </summary>
    [Fact]
    public void Generate_ColumnCollidingWithNavigationName_ShouldFailWithError()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customerId,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = orderId,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
                            Name = "customer_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                        // 親参照ナビゲーション "Customer" と同名のプロパティになる列
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customerId,
                    TargetEntityId = orderId,
                    SourceColumnId = customerPk,
                    TargetColumnId = orderFk,
                },
            ],
        };

        // EditModel / Mapper を生成しない構成でも Entity 側で衝突するため検出できる
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("OrderEntity")
                && diagnostic.Message.Contains("'Customer'")
                && diagnostic.Message.Contains("'customer'")
            );
    }

    /// <summary>
    /// 列・ナビゲーション・EditModel の派生名がすべて異なる図は生成できることを検証する
    /// （シンボル表検証の偽陽性防止）
    /// </summary>
    [Fact]
    public void Generate_DistinctGeneratedMemberNames_ShouldSucceed()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customerId,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = orderId,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
                            Name = "user_id",
                            DataType = "int",
                            IsForeignKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
            Relationships =
            [
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customerId,
                    TargetEntityId = orderId,
                    SourceColumnId = customerPk,
                    TargetColumnId = orderFk,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public int UserId");
        result.Files[0].Content.Should().Contain("public string UserName");
        result.Files[0].Content.Should().Contain("public CustomerEntity Customer");
        result.Files[0].Content.Should().Contain("public string BindingUserName");
    }

    /// <summary>
    /// 別エンティティの同名メンバー（一方は列プロパティ・他方はナビゲーション）は衝突扱いにならないことを検証する
    /// （衝突判定はクラス単位）
    /// </summary>
    [Fact]
    public void Generate_ColumnNameMatchingOtherEntityNavigation_ShouldSucceed()
    {
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();

        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customerId,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = customerPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        // CustomerEntity のプロパティ名は "Customer"。OrderEntity 側のナビゲーション名と
                        // 同名だが、別クラスのメンバーのため衝突ではない
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = orderId,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customerId,
                    TargetEntityId = orderId,
                    SourceColumnId = customerPk,
                    TargetColumnId = orderFk,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("public string Customer { get; set; }");
        result.Files[0].Content.Should().Contain("public CustomerEntity Customer { get; set; }");
    }

    /// <summary>C# の名前空間として不正な RootNamespace はエラーになることを検証する</summary>
    [Fact]
    public void Generate_InvalidRootNamespace_ShouldFailWithError()
    {
        var options = new CodeGenerationOptions { RootNamespace = "Bad-Namespace" };

        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("RootNamespace")
                && diagnostic.Message.Contains("Bad-Namespace")
            );
    }

    /// <summary>
    /// 空セグメントを含む RootNamespace（先頭・末尾・連続したドット）はエラーになることを検証する
    /// </summary>
    /// <remarks>
    /// 空セグメントを除去してから検証すると <c>namespace .Foo;</c> のようなコンパイル不能な出力が
    /// 無警告で書き出される（本テストがその回帰を防ぐ）
    /// </remarks>
    [Theory]
    [InlineData(".Sample")]
    [InlineData("Sample.")]
    [InlineData("Sample..Domain")]
    [InlineData("Sample. .Domain")]
    public void Generate_RootNamespaceWithEmptySegment_ShouldFailWithError(string rootNamespace)
    {
        var options = new CodeGenerationOptions { RootNamespace = rootNamespace };

        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("RootNamespace")
            );
    }

    /// <summary>C# の予約語をセグメントに含む RootNamespace はエラーになることを検証する</summary>
    [Fact]
    public void Generate_RootNamespaceWithReservedKeyword_ShouldFailWithError()
    {
        var options = new CodeGenerationOptions { RootNamespace = "Sample.class.Domain" };

        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("RootNamespace")
                && diagnostic.Message.Contains("Sample.class.Domain")
            );
    }

    /// <summary>複数セグメントの正当な RootNamespace はそのまま生成できることを検証する</summary>
    [Fact]
    public void Generate_ValidMultiSegmentRootNamespace_ShouldSucceed()
    {
        var options = new CodeGenerationOptions { RootNamespace = "My.App.Data" };

        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), options);

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain("namespace My.App.Data;");
    }

    /// <summary>分割時のカテゴリ別名前空間が不正ならエラーになることを検証する（オプション名を診断に含める）</summary>
    [Fact]
    public void Generate_InvalidCategoryNamespace_ShouldFailWithError()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            SplitFilesByCategory = true,
            EntityNamespace = "Sample.1Domain",
        };

        var result = new CSharpCodeGenerationService().Generate(SingleEntityDiagram(), options);

        result.HasErrors.Should().BeTrue();
        result.Files.Should().BeEmpty();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("EntityNamespace")
            );
    }

    /// <summary>多対多リレーションの警告がエンティティ数に関係なく 1 回だけ追加されることを検証する（重複解消）</summary>
    [Fact]
    public void Generate_ManyToManyWarning_ShouldNotBeDuplicated()
    {
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = left,
                    TableName = "users",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "user_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = right,
                    TableName = "roles",
                    Columns =
                    [
                        new Column
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = left,
                    TargetEntityId = right,
                    Type = RelationshipType.ManyToMany,
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        // 警告文言はカルチャ依存のため、resx テンプレートのプレースホルダ前プレフィックスで照合する
        var manyToManyWarningPrefix = Strings.CodeGen_Warning_ManyToManySkipped.Split("{0}")[0];
        result
            .Diagnostics.Count(diagnostic => diagnostic.Message.Contains(manyToManyWarningPrefix))
            .Should()
            .Be(1);
    }

    /// <summary>NULL 許容のバイナリ列が byte[]? として生成されることを検証する</summary>
    [Fact]
    public void Generate_NullableBinaryColumn_ShouldUseNullableByteArray()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "files",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "file_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

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
            .Select(index => new Entity
            {
                Id = Guid.NewGuid(),
                TableName = $"table_{index}",
                Columns =
                [
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
                        Name = "name",
                        DataType = "nvarchar(100)",
                        IsNullable = false,
                    },
                    new Column
                    {
                        Id = Guid.NewGuid(),
                        Name = "amount",
                        DataType = "decimal",
                        IsNullable = true,
                    },
                    new Column
                    {
                        Id = Guid.NewGuid(),
                        Name = "created_at",
                        DataType = "datetime2",
                        IsNullable = false,
                    },
                ],
            })
            .ToList();

        var result = new CSharpCodeGenerationService().Generate(
            new ErDiagram { Entities = entities },
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Length.Should().BeGreaterThan(1_048_576);
        // 切り捨て時に付与される省略記号で終わっていないこと
        content.TrimEnd().Should().NotEndWith("...");
        // 最後のエンティティ・リポジトリまで出力されていること
        content.Should().Contain("public partial class Table200Entity");
        content
            .Should()
            .Contain(
                "public sealed partial class Table200Repository(\r\n    ISqlConnectionFactory connectionFactory,\r\n    ISaveHookRegistry? saveHooks = null\r\n)"
            );
    }

    /// <summary>EditModel の確定値プロパティに変更通知パーシャルメソッド（Changing/Changed）が生成され、setter から呼ばれることを検証する</summary>
    [Fact]
    public void Generate_EditModel_ShouldGenerateChangeHookPartialMethods()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // パーシャルメソッドの宣言（本体はユーザーが partial クラスで実装）。新値のみ／旧値・新値の両オーバーロードを生成する
        content.Should().Contain("partial void OnCustomerIdChanging(int? value);");
        content
            .Should()
            .Contain("partial void OnCustomerIdChanging(int? oldValue, int? newValue);");
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
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // Repository の EntitySaveMetadata が参照する属性が未定義だと CS0246 になるため、定義の存在を確認
        content.Should().Contain("public sealed class NavigationReferenceAttribute : Attribute");
        content
            .Should()
            .Contain("property.GetCustomAttribute<NavigationReferenceAttribute>() is null");
    }

    /// <summary>
    /// IncludeDataAnnotations=true・GenerateRepositories=false でも [SqlColumnType] が出力され、
    /// SqlDbType（System.Data）の using が含まれることを検証する（ColumnFacets 廃止に伴う OR 条件）
    /// </summary>
    [Fact]
    public void Generate_IncludeDataAnnotationsWithoutRepository_ShouldEmitSqlColumnTypeAttribute()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("public sealed class SqlColumnTypeAttribute : Attribute");
        content.Should().Contain("[SqlColumnType(SqlDbType.NVarChar, Size = 50)]");
        content.Should().Contain("[SqlColumnType(SqlDbType.Decimal, Precision = 10, Scale = 2)]");
        // SqlDbType は System.Data（BCL）所属。Repository なし構成でも using が供給される必要がある
        content.Should().Contain("using System.Data;");
        // Repository が無いため EntitySaveMetadata 等のランタイム補助コードは出力されない
        content.Should().NotContain("_columnTypeCache");
    }

    /// <summary>
    /// IncludeDataAnnotations=false・GenerateRepositories=false の両方 OFF では [SqlColumnType] が一切出力されないことを検証する
    /// </summary>
    [Fact]
    public void Generate_NoAnnotationsNoRepository_ShouldNotEmitSqlColumnTypeAttribute()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                IncludeDataAnnotations = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().NotContain("SqlColumnTypeAttribute");
        content.Should().NotContain("[SqlColumnType(");
    }

    /// <summary>[SqlColumnType].IntegralDigits が Precision - Scale を返し、decimal 以外（Precision 未指定）は -1 を返すことを検証する</summary>
    [Fact]
    public void Generate_SqlColumnType_IntegralDigitsProperty_ShouldComputeFromPrecisionAndScale()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain("public int IntegralDigits => Precision > 0 ? Precision - Scale : -1;");
    }

    /// <summary>主キー 1 列のみを持つ単純なエンティティ 1 件のダイアグラムを生成する</summary>
    private static ErDiagram SingleEntityDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "items",
                    Columns =
                    [
                        new Column
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

    /// <summary>VO 生成 OFF（既定）では値オブジェクトの基底・インターフェースが一切出力されないことを検証する</summary>
    [Fact]
    public void Generate_ValueObjects_Disabled_ShouldNotEmitValueObjectTypes()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().NotContain("ValueObjectBase");
        content.Should().NotContain("interface IValueObject");
        content.Should().NotContain("ValueObjectJsonConverterFactory");
        // 既定どおりプリミティブ型のまま
        content.Should().Contain("public int CustomerId { get; set; }");
    }

    /// <summary>VO 生成 ON で基底・インターフェース・具象 VO が出力され、Entity/EditModel の型が VO になることを検証する</summary>
    [Fact]
    public void Generate_ValueObjects_Enabled_ShouldEmitAndApplyValueObjects()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 基底・インターフェース・例外・JSON 変換器
        content.Should().Contain("public abstract partial class ValueObjectBase<TSelf, TValue>");
        content
            .Should()
            .Contain("public abstract partial class ValueObjectOrderedBase<TSelf, TValue>");
        content.Should().Contain("public abstract partial class ValueObjectStringBase<TSelf>");
        // 文字列 VO は string / TSelf 両対応の部分一致メソッドを持つ
        content
            .Should()
            .Contain(
                "public bool Contains(TSelf? value) => value is not null && Contains(value.Value);"
            );
        content
            .Should()
            .Contain(
                "public bool StartsWith(TSelf? value) => value is not null && StartsWith(value.Value);"
            );
        content
            .Should()
            .Contain(
                "public bool EndsWith(TSelf? value) => value is not null && EndsWith(value.Value);"
            );
        content.Should().Contain("public abstract partial class ValueObjectBinaryBase<TSelf>");
        content.Should().Contain("public interface IValueObject<TSelf, TValue>");
        // 表示用プロパティ（マーカー IF に宣言、基底に virtual 既定実装）
        content.Should().Contain("string DisplayValue { get; }");
        content.Should().Contain("public virtual string DisplayValue => ToString();");
        content
            .Should()
            .Contain("public sealed class ValueObjectJsonConverterFactory : JsonConverterFactory");
        // 具象 VO（型別の基底を継承）
        content.Should().Contain(": ValueObjectOrderedBase<CustomerIdValue, int>,");
        content.Should().Contain(": ValueObjectStringBase<NameValue>,");
        content.Should().Contain(": ValueObjectBinaryBase<PhotoValue>,");
        content.Should().Contain(": ValueObjectBooleanBase<IsActiveValue>,");
        content
            .Should()
            .Contain(
                "public abstract partial class ValueObjectBooleanBase<TSelf> : ValueObjectBase<TSelf, bool>"
            );
        // string MaxLength・decimal precision/scale の自動検証
        content.Should().Contain("if (value.Length > 50)");
        content.Should().Contain("ValidateDecimal(value, 10, 2, errors);");
        // 既定メッセージは全 VO 共通の静的プロバイダから取得し、1 か所で差し替えできる
        content.Should().Contain("public static class ValueObjectValidationMessages");
        content
            .Should()
            .Contain(
                "var message = ValueObjectValidationMessages.MaxLengthExceeded(50, value.Length);"
            );
        content.Should().Contain("ValueObjectValidationMessages.ScaleExceeded(scale)");
        content
            .Should()
            .Contain("ValueObjectValidationMessages.PrecisionExceeded(precision - scale)");
        // null 値（必須）のメッセージも同じ 2 段チェーン（共通既定＋VO ごとの partial）に載る
        content.Should().Contain("var message = ValueObjectValidationMessages.ValueRequired();");
        content.Should().Contain("CustomizeValueRequiredErrorMessage(ref message);");
        content
            .Should()
            .Contain("static partial void CustomizeValueRequiredErrorMessage(ref string message);");
        // 表示名の既定値は全生成メンバー共通の静的リゾルバ経由（一括差し替え点）
        content.Should().Contain("public static class GeneratedDisplayNames");
        // 自動ルールのエラーメッセージはさらに VO ごとの partial で個別調整も可能（ref string message フック）
        content.Should().Contain("CustomizeMaxLengthErrorMessage(value, 50, ref message);");
        content.Should().Contain("static partial void CustomizeMaxLengthErrorMessage(");
        content.Should().Contain("CustomizeScaleErrorMessage(value, scale, ref message);");
        content
            .Should()
            .Contain("CustomizePrecisionErrorMessage(value, precision - scale, ref message);");
        content
            .Should()
            .Contain(
                "static partial void CustomizeScaleErrorMessage(decimal value, int scale, ref string message);"
            );
        // PK と同名 FK は同一 VO 型を共有（CustomerIdValue は 1 定義のみ）
        content.Split("public sealed partial class CustomerIdValue").Length.Should().Be(2);
        // Entity プロパティに DB カラムのメタ情報属性が付く（VO 型でも付与）。ColumnFacets は SqlColumnType へ統合済み
        content.Should().Contain("public sealed class SqlColumnTypeAttribute : Attribute");
        content.Should().Contain("[SqlColumnType(SqlDbType.NVarChar, Size = 50)]");
        content.Should().Contain("[SqlColumnType(SqlDbType.Decimal, Precision = 10, Scale = 2)]");
        // Entity の型が VO（非 NULL PK は null! 初期化）
        content.Should().Contain("public CustomerIdValue CustomerId { get; set; } = null!;");
        // EditModel 確定値は常に VO?（バインド setter は TryCreate）
        content.Should().Contain("public CustomerIdValue? CustomerId");
        content
            .Should()
            .Contain("CustomerIdValue.TryCreate(parsed, out var converted, out var voErrors)");
        // EntityBase / Repository の JSON オプションに VO 変換器が登録される
        content.Should().Contain("Converters = { new ValueObjectJsonConverterFactory() },");
        // Mapper のロードは必須 VO 列でも null 条件付きで ToString する（= null! のためロード前は null になり得る）
        content
            .Should()
            .Contain(
                "editModel.BindingCustomerId = entity.CustomerId?.ToString() ?? string.Empty;"
            );
    }

    /// <summary>string PK ＋ GuidKey オプションで PK が GuidKey 基底になり、非 PK の string は通常の string 基底になることを検証する</summary>
    [Fact]
    public void Generate_ValueObjects_GuidKey_ShouldUseGuidKeyBaseForStringPrimaryKey()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "documents",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "document_id",
                            DataType = "nvarchar(36)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "title",
                            DataType = "nvarchar(100)",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
                UseGuidKeyForStringPrimaryKey = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain(": ValueObjectGuidKeyBase<DocumentIdValue>,");
        // 非 PK の string は通常の string 基底
        content.Should().Contain(": ValueObjectStringBase<TitleValue>,");
    }

    /// <summary>同名列の定義が食い違う場合は Warning 診断を出すが、生成自体は成功する（PK 優先/最大定義で解決）ことを検証する</summary>
    [Fact]
    public void Generate_ValueObjects_ConflictingSameNameColumns_ShouldWarnButGenerate()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "products",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "product_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
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

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateValueObjects = true,
            }
        );

        // 競合は Warning（Error ではない）で、生成は成功する
        result.HasErrors.Should().BeFalse();
        result.Files.Should().ContainSingle();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("NameValue")
            );
        // NameValue は 1 定義のみ（共有）
        result
            .Files[0]
            .Content.Split("public sealed partial class NameValue")
            .Length.Should()
            .Be(2);
    }

    /// <summary>VO 生成テスト用の代表的なダイアグラム（PK/FK 共有・各種型を含む）</summary>
    private static ErDiagram ValueObjectDiagram()
    {
        var customer = Guid.NewGuid();
        var order = Guid.NewGuid();
        var custPk = Guid.NewGuid();
        var orderFk = Guid.NewGuid();
        return new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    Id = customer,
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = custPk,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "balance",
                            DataType = "decimal(10,2)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "is_active",
                            DataType = "bit",
                            IsNullable = false,
                        },
                    ],
                },
                new Entity
                {
                    Id = order,
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "order_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = orderFk,
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
                new Relationship
                {
                    Id = Guid.NewGuid(),
                    SourceEntityId = customer,
                    SourceColumnId = custPk,
                    TargetEntityId = order,
                    TargetColumnId = orderFk,
                    Type = RelationshipType.OneToMany,
                },
            ],
        };
    }

    /// <summary>生成結果から指定ファイル名の内容を取得する</summary>
    private static string Content(CodeGenerationResult result, string fileName) =>
        result.Files.Single(file => file.FileName == fileName).Content;

    /// <summary>分割時に「生成対象カテゴリ＋Runtime」が 1 カテゴリ 1 ファイルで、規約名・規約名前空間で出力されることを検証する</summary>
    [Fact]
    public void Generate_Split_ShouldEmitOneFilePerCategoryPlusRuntime()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateValueObjects = true,
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        // QuickER 版 Repository は単一方言でも契約（Repositories.g.cs）＋方言別実装（Repositories.SqlServer.g.cs）へ分ける
        result
            .Files.Select(file => file.FileName)
            .Should()
            .BeEquivalentTo([
                "Runtime.g.cs",
                "ValueObjects.g.cs",
                "Entities.g.cs",
                "EditModels.g.cs",
                "Mappers.g.cs",
                "Repositories.g.cs",
                "Repositories.SqlServer.g.cs",
            ]);

        Content(result, "Runtime.g.cs").Should().Contain("namespace Sample.Domain.Runtime;");
        Content(result, "Entities.g.cs").Should().Contain("namespace Sample.Domain.Entities;");
        Content(result, "ValueObjects.g.cs")
            .Should()
            .Contain("namespace Sample.Domain.ValueObjects;");

        // 共有基盤は Runtime ファイルに集約され、Entity ファイルには基底定義が出ない
        Content(result, "Runtime.g.cs")
            .Should()
            .Contain("public abstract partial class EntityBase");
        Content(result, "Runtime.g.cs")
            .Should()
            .Contain("public abstract partial class EditModelBase");
        Content(result, "Entities.g.cs").Should().Contain("public partial class CustomerEntity");
        Content(result, "Entities.g.cs")
            .Should()
            .NotContain("public abstract partial class EntityBase");

        // クロス参照 using が付与される（Entity→Runtime、Mapper→Entity/EditModel）
        Content(result, "Entities.g.cs").Should().Contain("using Sample.Domain.Runtime;");
        Content(result, "Mappers.g.cs").Should().Contain("using Sample.Domain.Entities;");
        Content(result, "Mappers.g.cs").Should().Contain("using Sample.Domain.EditModels;");
    }

    /// <summary>分割時に複数カテゴリへ同一名前空間を指定しても、ファイルは分かれ、自分自身の名前空間は using しないことを検証する</summary>
    [Fact]
    public void Generate_Split_SameNamespace_ShouldStillEmitSeparateFiles()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateEditModels = true,
                GenerateMappers = false,
                GenerateRepositories = false,
                EntityNamespace = "Shared.Models",
                EditModelNamespace = "Shared.Models",
            }
        );

        result.HasErrors.Should().BeFalse();
        result
            .Files.Select(file => file.FileName)
            .Should()
            .Contain("Entities.g.cs")
            .And.Contain("EditModels.g.cs");
        Content(result, "Entities.g.cs").Should().Contain("namespace Shared.Models;");
        Content(result, "EditModels.g.cs").Should().Contain("namespace Shared.Models;");
        // 同一名前空間どうしは using しない（自分自身の名前空間を除外）
        Content(result, "Entities.g.cs").Should().NotContain("using Shared.Models;");
    }

    /// <summary>分割時に Runtime 名前空間を未指定なら {root}.Runtime にフォールバックすることを検証する</summary>
    [Fact]
    public void Generate_Split_RuntimeNamespace_ShouldDefaultToRootDotRuntime()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions { RootNamespace = "Acme.App", SplitFilesByCategory = true }
        );

        result.HasErrors.Should().BeFalse();
        Content(result, "Runtime.g.cs").Should().Contain("namespace Acme.App.Runtime;");
    }

    // ---- EF Core（DbContext・Fluent 構成）----

    /// <summary>EF Core 生成 ON で DbContext・DbSet・ToTable/HasKey/HasColumnName/IsRequired/HasMaxLength/HasPrecision が出力されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldEmitDbContextWithFluentConfiguration()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // using・DbContext・コンストラクタ・OnModelCreating
        content.Should().Contain("using Microsoft.EntityFrameworkCore;");
        content.Should().Contain("public partial class QuickErDbContext : DbContext");
        content
            .Should()
            .Contain("public QuickErDbContext(DbContextOptions<QuickErDbContext> options)");
        content
            .Should()
            .Contain("protected override void OnModelCreating(ModelBuilder modelBuilder)");
        content.Should().Contain("partial void OnModelCreatingPartial(ModelBuilder modelBuilder);");

        // DbSet（複数形プロパティ名）
        content
            .Should()
            .Contain("public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();");
        content.Should().Contain("public DbSet<OrderEntity> Orders => Set<OrderEntity>();");

        // テーブル・主キー・列・必須・最大長・精度
        content.Should().Contain("modelBuilder.Entity<CustomerEntity>(entity =>");
        content.Should().Contain("entity.ToTable(\"customers\");");
        content.Should().Contain("entity.HasKey(e => e.CustomerId);");
        content
            .Should()
            .Contain(
                "entity.Property(e => e.Name).HasColumnName(\"name\").IsRequired().HasMaxLength(50);"
            );
        content
            .Should()
            .Contain(
                "entity.Property(e => e.Balance).HasColumnName(\"balance\").HasPrecision(10, 2);"
            );
    }

    /// <summary>EF Core 生成で EntityBase の永続化対象外メンバーが Fluent の Ignore で除外されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldIgnoreEntityBaseMembers()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain("entity.Ignore(e => e.RowState);");
        content.Should().Contain("entity.Ignore(e => e.IsAdded);");
        content.Should().Contain("entity.Ignore(e => e.HasChanges);");
    }

    /// <summary>EF Core 生成で 1 対多リレーションが HasMany/WithOne/HasForeignKey/OnDelete で構成されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldConfigureOneToManyRelationship()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().Contain(".HasMany(e => e.Orders)");
        content.Should().Contain(".WithOne(e => e.Customer)");
        content.Should().Contain(".HasForeignKey(e => e.CustomerId)");
        // OnDelete 既定（NoAction）はカスケードしないため Restrict
        content.Should().Contain(".OnDelete(DeleteBehavior.Restrict);");
    }

    /// <summary>OnDelete=Cascade のリレーションでは OnDelete(DeleteBehavior.Cascade) が構成されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldConfigureCascadeDeleteFromModel()
    {
        var diagram = ValueObjectDiagram();
        diagram.Relationships[0].OnDelete = ForeignKeyReferentialAction.Cascade;

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        result.Files[0].Content.Should().Contain(".OnDelete(DeleteBehavior.Cascade);");
    }

    /// <summary>VO 生成 ON の EF Core では各 VO 列に HasConversion が構成されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_WithValueObjects_ShouldEmitHasConversion()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCore = true,
                GenerateValueObjects = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        // 主キー VO の変換（v.Value / Create）
        content.Should().Contain(".HasConversion(v => v!.Value, v => CustomerIdValue.Create(v!))");
        // VO 列は Fluent の HasMaxLength/HasPrecision を出さない（長さ・桁数は VO 内部検証）
        content.Should().NotContain(".HasMaxLength(50).HasConversion");
    }

    /// <summary>rowversion 列を持つエンティティの EF Core 構成で IsRowVersion() が出力されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldConfigureRowVersion()
    {
        var result = new CSharpCodeGenerationService().Generate(
            RowVersionDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content
            .Should()
            .Contain(
                "entity.Property(e => e.RowVersion).HasColumnName(\"row_version\").IsRequired().IsRowVersion();"
            );
    }

    /// <summary>EF Core 生成 OFF（既定）では EF Core への using も DbContext も一切出力されないことを検証する</summary>
    [Fact]
    public void Generate_EfCore_Disabled_ShouldNotEmitAnyEfCode()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;
        content.Should().NotContain("Microsoft.EntityFrameworkCore");
        content.Should().NotContain("QuickErDbContext");
        content.Should().NotContain("OnModelCreating");
        content.Should().NotContain("DbSet<");
    }

    /// <summary>分割出力時に EfCore カテゴリが独自ファイル・独自名前空間で出力され、他ファイルへ EF Core using が漏れないことを検証する</summary>
    [Fact]
    public void Generate_EfCore_Split_ShouldEmitDedicatedFileAndNamespace()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateEfCore = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        // EF Core 実装は方言別実装と同じ流儀で Repositories.EfCore.g.cs・{Repository}.EfCore へ出力される
        result.Files.Should().Contain(file => file.FileName == "Repositories.EfCore.g.cs");

        var efCore = Content(result, "Repositories.EfCore.g.cs");
        efCore.Should().Contain("namespace Sample.Domain.Repositories.EfCore;");
        efCore.Should().Contain("using Microsoft.EntityFrameworkCore;");
        efCore.Should().Contain("public partial class QuickErDbContext : DbContext");

        // EF Core の using は EfCore ファイルにのみ現れ、Entity ファイルには漏れない
        Content(result, "Entities.g.cs")
            .Should()
            .NotContain("using Microsoft.EntityFrameworkCore;");
    }

    /// <summary>
    /// 分割×フル構成で、Entity / EditModel / Mapper / ValueObjects / Runtime の各ファイルに、それらが使わない
    /// 外部 using（EntityFrameworkCore / Data.SqlClient / DependencyInjection）が 1 行も含まれないことを検証する。
    /// </summary>
    /// <remarks>
    /// 本テストは「using をバケット単位で解決する」設計（<see cref="GeneratedFileUsings"/>）の核心を守る。
    /// 契約のみを持つ Repository ファイル（EF Core 単独時）に SqlClient / DI の using が漏れないことも併せて検証する。
    /// EF Core 系フラグを両方 ON（GenerateRepositories=true・GenerateEfCore=true）にしてすべての外部 using が
    /// 発生し得る最大構成で確認する。
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_Split_ShouldNotLeakForeignUsingsIntoUnrelatedFiles(bool vo)
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateValueObjects = vo,
                GenerateRepositories = true,
                GenerateEfCore = true,
            }
        );

        result.HasErrors.Should().BeFalse();

        // これら 3 つの外部 using は、その依存を実際に使わないファイルへ現れてはならない
        string[] forbiddenUsings =
        [
            "using Microsoft.EntityFrameworkCore",
            "using Microsoft.Data.SqlClient;",
            "using Microsoft.Extensions.DependencyInjection",
        ];

        string[] neutralFiles =
        [
            "Entities.g.cs",
            "EditModels.g.cs",
            "Mappers.g.cs",
            "Runtime.g.cs",
            .. (vo ? new[] { "ValueObjects.g.cs" } : []),
        ];

        foreach (var fileName in neutralFiles)
        {
            var content = Content(result, fileName);
            foreach (var forbidden in forbiddenUsings)
            {
                content
                    .Should()
                    .NotContain(
                        forbidden,
                        $"{fileName} に外部 using「{forbidden}」が漏れてはならない（VO={vo}）"
                    );
            }
        }
    }

    /// <summary>EF Core 単独出力の分割時、契約のみの Repository ファイルに SqlClient / DependencyInjection の using が漏れないことを検証する</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_EfCoreOnly_Split_RepositoryFile_ShouldNotUseSqlClientOrDi(bool vo)
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateValueObjects = vo,
                GenerateRepositories = false,
                GenerateEfCore = true,
            }
        );

        result.HasErrors.Should().BeFalse();

        // 契約のみ（QuickER の SQL Server 実装なし）の Repository ファイルは SqlClient・DI に依存しない
        var repository = Content(result, "Repositories.g.cs");
        repository.Should().NotContain("using Microsoft.Data.SqlClient;");
        repository.Should().NotContain("using Microsoft.Extensions.DependencyInjection;");
    }

    /// <summary>
    /// 分割時、EF Core 実装は方言別実装と同じ流儀で Repositories.EfCore.g.cs へ出力され、
    /// 名前空間が Repository 契約 namespace のサブ名前空間（{RepositoryNamespace}.EfCore）へ導出されることを検証する
    /// </summary>
    [Fact]
    public void Generate_EfCore_Split_ShouldDeriveNamespaceFromRepository()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateEfCore = true,
                // Repository 契約 namespace をカスタム指定 → EfCore はそのサブ名前空間へ導出される
                RepositoryNamespace = "Acme.Persistence.Repos",
            }
        );

        result.HasErrors.Should().BeFalse();
        Content(result, "Repositories.EfCore.g.cs")
            .Should()
            .Contain("namespace Acme.Persistence.Repos.EfCore;");
    }

    /// <summary>EF Core 単独出力（GenerateEfCore=true・GenerateRepositories=false）が合法で、エラーなく生成できることを検証する</summary>
    [Fact]
    public void Generate_EfCoreOnly_ShouldSucceedWithoutError()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCore = true,
                GenerateRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().NotBeEmpty();

        var content = string.Concat(result.Files.Select(file => file.Content));
        // 共通契約は出力される
        content.Should().Contain("public partial interface ISqlExecutor");
        content.Should().Contain("public sealed class SqlQuery<TEntity>");
        content.Should().Contain("internal static class RawSqlMapper");
        // EF Core 一式は出力される
        content.Should().Contain("public partial class QuickErDbContext : DbContext");
        content
            .Should()
            .Contain("public static IServiceCollection AddGeneratedEfCoreRepositories(");
        // QuickER の SQL Server 実装は出力されない
        content.Should().NotContain("public sealed partial class SqlExecutor(");
        content.Should().NotContain("public abstract partial class SqlServerRepository<");
        content
            .Should()
            .NotContain("public static IServiceCollection AddGeneratedSqlServerRepositories(");
    }

    /// <summary>EF Core 単独出力（VO 有無 × 分割有無の 4 通り）の生成物全ファイルに Microsoft.Data.SqlClient 依存が一切現れないことを検証する</summary>
    /// <remarks>
    /// SqlParameterValue（値オブジェクト unwrap ヘルパー・BCL のみ）は "SqlParameter " のような型参照ではないため、
    /// 誤検出しないよう "SqlParameter " は末尾スペース付きで（型参照のみ）判定する
    /// </remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Generate_EfCoreOnly_ShouldNotDependOnSqlClient(bool split, bool vo)
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = split,
                GenerateValueObjects = vo,
                GenerateEfCore = true,
                GenerateRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        result.Files.Should().NotBeEmpty();

        // SqlClient 由来のトークンが 1 つでも現れたら EF Core 単独出力の前提（プロバイダのみで使える）が崩れる
        string[] forbidden =
        [
            "Microsoft.Data.SqlClient",
            "SqlCommand",
            "SqlConnection(",
            "SqlDataReader",
            "SqlParameter ", // SqlParameterValue（VO unwrap・BCL のみ）は除外するため末尾スペース付き
        ];

        foreach (var file in result.Files)
        {
            foreach (var token in forbidden)
            {
                file.Content.Should()
                    .NotContain(
                        token,
                        $"EF Core 単独出力のファイル {file.FileName} に SqlClient 依存トークン「{token}」が現れてはならない（Split={split} VO={vo}）"
                    );
            }
        }
    }

    /// <summary>EF Core 単独出力の SqlQuery は SQL Server パス（FOR JSON・接続ファクトリ・SqlExpressionTranslator）を出力せず、実行器委譲のみになることを検証する</summary>
    [Fact]
    public void Generate_EfCoreOnly_SqlQuery_ShouldOnlyDelegateToExecutor()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEfCore = true,
                GenerateRepositories = false,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = string.Concat(result.Files.Select(file => file.Content));

        // 実行器委譲パスは出力される
        content.Should().Contain("public sealed class SqlQuery<TEntity>");
        content.Should().Contain("=> await _executor.ToListAsync(BuildPlan(), cancellationToken);");
        // SQL Server 専用の要素は出力されない（コード本体で判定。FOR JSON 等は契約の doc コメントに残るためコードで確認する）
        content.Should().NotContain("private readonly ISqlConnectionFactory _connectionFactory");
        content.Should().NotContain("internal static class SqlExpressionTranslator");
        content.Should().NotContain("internal static class JsonQueryPlanner");
        content.Should().NotContain("private async Task<string> ReadJsonAsync(");
        content.Should().NotContain("BuildJsonSelect(");
        // 各エンティティ用リポジトリインターフェイス（契約）は出力される
        content.Should().Contain(" : IRepository<");
    }

    // ---- EF Core（EF Core 版 Repository・SqlExecutor・DI 拡張）----

    /// <summary>EF Core 生成 ON で EF Core 版 Repository（基底クラス＋エンティティ別実装）が生成されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldEmitEfCoreRepositories()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // 基底クラス: 具象 DbContext 非依存で TContext ジェネリック化し、IDbContextFactory<TContext> を受け取る
        content
            .Should()
            .Contain("public abstract partial class EfCoreRepository<TEntity, TKey, TContext>(");
        content.Should().Contain("IDbContextFactory<TContext> contextFactory");
        content.Should().Contain(") : IRepository<TEntity, TKey>");
        content.Should().Contain("where TContext : DbContext");

        // エンティティ別実装: 生成側で TContext=QuickErDbContext を閉じ、既存の I{Entity}Repository を実装する
        content.Should().Contain("public sealed partial class EfCoreCustomerRepository(");
        content
            .Should()
            .Contain(
                ") : EfCoreRepository<CustomerEntity, int, QuickErDbContext>(contextFactory, saveHooks), ICustomerRepository { }"
            );

        // 読み取りは AsNoTracking（切断パターン）、事後状態は既存版と同じ Unchanged
        content.Should().Contain(".AsNoTracking()");
        content.Should().Contain("entity?.MarkUnchanged();");

        // 単発の追加・一括追加は Entry.State 代入（Add/AddRange のグラフ走査を避け、既存版と同じ範囲を挿入する）
        content.Should().Contain("context.Entry(entity).State = EntityState.Added;");
    }

    /// <summary>EF Core 版 SaveAsync が TrackGraph で RowState → EntityState 変換し、競合を SaveConflictException へ変換することを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldConvertRowStateViaTrackGraphInSave()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // TrackGraph による切断グラフの登録と RowState → EntityState の変換表
        content.Should().Contain("context.ChangeTracker.TrackGraph(");
        content.Should().Contain("RowState.Added => EntityState.Added,");
        content.Should().Contain("RowState.Updated => EntityState.Modified,");
        content.Should().Contain("RowState.Removed => EntityState.Deleted,");
        content.Should().Contain("_ => EntityState.Unchanged,");

        // DbUpdateConcurrencyException → 既存契約（SaveConflictException / insertWhenUpdateMissing 切替）
        content.Should().Contain("catch (DbUpdateConcurrencyException ex)");
        content.Should().Contain("throw new SaveConflictException(");
        content.Should().Contain("entry.State = EntityState.Added;");

        // 保存後の事後状態は既存版と同じ（AcceptChanges で Added/Updated → Unchanged）。
        // Save フック対応で保存本体は SaveTrackedGraphAsync へ分離され、集約ルートは root 変数で確定する
        content.Should().Contain("EntityGraphSaver.AcceptChanges(root, cascadeSave);");
    }

    /// <summary>EF Core 版 SqlExecutor が DbContext の接続上の ADO で既存版とマッピング・束縛を共有することを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldEmitEfCoreSqlExecutor()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // 具象 DbContext 非依存で TContext ジェネリック化（IDbContextFactory<TContext> を受け取る）
        content
            .Should()
            .Contain(
                "public sealed partial class EfCoreSqlExecutor<TContext>(IDbContextFactory<TContext> contextFactory)"
            );
        content.Should().Contain("    : ISqlExecutor");
        content.Should().Contain("where TContext : DbContext");

        // DbContext の接続上で ADO を直接実行し、共有ヘルパー RawSqlMapper のマッピング・束縛を使う
        content.Should().Contain("context.Database.GetDbConnection().CreateCommand()");
        content
            .Should()
            .Contain("RawSqlMapper.ReadProjectionRowsAsync<TResult>(reader, cancellationToken)");
        content.Should().Contain("RawSqlMapper.BindParameters(command, parameters);");
        content
            .Should()
            .Contain("internal static void BindParameters(DbCommand command, object? parameters)");

        // 厳密マッピング（全列必須・DbDataReader 版）を既存版と共有する
        content
            .Should()
            .Contain("public TEntity MapEntityFromRawSql<TEntity>(DbDataReader reader)");
    }

    /// <summary>EF Core 版 DI 拡張が DbContextFactory＋EF Core 版実装一式を既存と同じインターフェイスへ登録することを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldEmitDiExtension()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        content
            .Should()
            .Contain("public static IServiceCollection AddGeneratedEfCoreRepositories(");
        content.Should().Contain("Action<DbContextOptionsBuilder> configureDbContext");
        content
            .Should()
            .Contain("services.AddDbContextFactory<QuickErDbContext>(configureDbContext);");
        content
            .Should()
            .Contain("services.AddSingleton<ISqlExecutor, EfCoreSqlExecutor<QuickErDbContext>>();");
        // 既存と同じインターフェイスへ EF Core 版実装を登録する（DI 差し替えだけで切替可能）
        content
            .Should()
            .Contain("services.AddScoped<ICustomerRepository, EfCoreCustomerRepository>();");
        content.Should().Contain("services.AddScoped<IOrderRepository, EfCoreOrderRepository>();");
    }

    /// <summary>EF Core 生成 ON の SqlQuery に実行器差し替えバックエンド（式木捕捉・EF Core 実行）が追加されることを検証する</summary>
    [Fact]
    public void Generate_EfCore_ShouldAddExecutorBackendToSqlQuery()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain", GenerateEfCore = true }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // 内部抽象（BCL 型のみ）と SqlQuery への注入コンストラクタ
        content.Should().Contain("internal interface ISqlQueryExecutor<TEntity>");
        content.Should().Contain("internal sealed record SqlQueryPlan<TEntity>(");
        content.Should().Contain("internal SqlQuery(ISqlQueryExecutor<TEntity> executor)");

        // SqlQuery 本体の公開シグネチャは不変（sealed のまま）
        content.Should().Contain("public sealed class SqlQuery<TEntity>");

        // EF Core モードではQuickER のトランスレータを通さず式木のまま捕捉する
        content.Should().Contain("_predicates.Add(predicate);");
        content
            .Should()
            .Contain("_orderSelectors.Add(new SqlQueryOrdering(keySelector, Descending: false));");

        // EF Core 実行器: AsNoTracking・ドットパス Include・boxing を剥がした OrderBy 合成・Repository からの注入
        // 実行器も TContext ジェネリック（具象 DbContext 非依存）
        content
            .Should()
            .Contain("internal sealed class EfCoreSqlQueryExecutor<TEntity, TContext>(");
        content
            .Should()
            .Contain("new(new EfCoreSqlQueryExecutor<TEntity, TContext>(_contextFactory));");
        content.Should().Contain("query = query.Include(path);");
        content.Should().Contain("nameof(Queryable.OrderBy)");
    }

    /// <summary>
    /// EF Core 生成 OFF（QuickER 版 Repository 単独）でも、SqlQuery は実行器抽象（ISqlQueryExecutor / SqlQueryPlan）経由へ統一され、
    /// 方言別 ADO 実行器（SqlServerSqlQueryExecutor）が出力される一方、EF Core 版クラスは一切出力されないことを検証する。
    /// </summary>
    /// <remarks>
    /// M2a のランタイム統一により、SqlQuery は常に <c>ISqlQueryExecutor&lt;TEntity&gt;</c> 経由で実行するよう変更された
    /// （以前は EF Core 生成時のみ実行器抽象が出て、QuickER 版 Repository は方言 SQL を SqlQuery 内に埋め込んでいた）。
    /// EF Core 依存（EfCore プレフィックスのクラス・DbContext 等）が漏れないことは引き続き守る。
    /// </remarks>
    [Fact]
    public void Generate_EfCore_Disabled_ShouldStillUnifyThroughAdoExecutor()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // 実行器抽象と方言別 ADO 実行器は出力される（ランタイム統一）
        content.Should().Contain("internal interface ISqlQueryExecutor<TEntity>");
        content.Should().Contain("internal sealed record SqlQueryPlan<TEntity>(");
        content.Should().Contain("internal sealed class SqlServerSqlQueryExecutor<TEntity>(");
        content
            .Should()
            .Contain(
                "public SqlQuery<TEntity> Query() => new(new SqlServerSqlQueryExecutor<TEntity>(_connectionFactory));"
            );

        // EF Core 依存（EfCore プレフィックスのクラス・DbContext）は一切出力されない
        content.Should().NotContain("EfCore");
        content.Should().NotContain("QuickErDbContext");

        // 既存の SQL Server パスは SqlDataReader ベースのまま（マッピングの互換維持）
        content
            .Should()
            .Contain("public TEntity MapEntityFromRawSql<TEntity>(SqlDataReader reader)");
    }

    /// <summary>分割出力時、EfCore ファイルの EF Core 版コードが SqlClient の型（SqlCommand 等）に依存しないことを検証する</summary>
    [Fact]
    public void Generate_EfCore_Split_ShouldKeepEfCodeFreeOfSqlClientTypes()
    {
        var result = new CSharpCodeGenerationService().Generate(
            ValueObjectDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                SplitFilesByCategory = true,
                GenerateEfCore = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var efCore = Content(result, "Repositories.EfCore.g.cs");

        // EF Core 版コードは方言非依存（System.Data.Common の DbCommand/DbConnection/DbDataReader のみ使用）。
        // SqlBulkCopy は「性能特性が異なる」旨の XML コメントでのみ言及されるため、型使用（"SqlBulkCopy("）だけを禁止する
        efCore.Should().NotContain("SqlCommand");
        efCore.Should().NotContain("SqlDataReader");
        efCore.Should().NotContain("SqlBulkCopy(");
        efCore.Should().NotContain("new SqlConnection");
        efCore.Should().Contain("EfCoreSqlExecutor");
        efCore.Should().Contain("EfCoreRepository<TEntity, TKey, TContext>");
    }

    /// <summary>rowversion 列と単一主キーを持つ最小ダイアグラム（IsRowVersion 構成の検証用）</summary>
    private static ErDiagram RowVersionDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "documents",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "document_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "row_version",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>インメモリ Repository 単独出力（契約＋インメモリ実装）が生成され、ADO・EF Core 依存を含まないことを検証する</summary>
    [Fact(
        DisplayName = "インメモリ Repository 単独出力は契約＋インメモリ実装を出し ADO/EF Core 依存を含まない"
    )]
    public void Generate_InMemoryOnly_EmitsContractAndInMemory_NoAdoOrEf()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = false,
                GenerateInMemoryRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // 契約・インメモリ実装・データストア・シーダー・DI が出る
        content.Should().Contain("public partial interface IItemRepository");
        content.Should().Contain("class InMemoryDataStore");
        content.Should().Contain("class InMemoryItemRepository");
        content.Should().Contain("class InMemorySampleData");
        content.Should().Contain("AddGeneratedInMemoryRepositories");
        // 方言非依存: ADO・EF Core・QuickER 版 Repository 実装は一切出ない
        content.Should().NotContain("Microsoft.Data.SqlClient");
        content.Should().NotContain("Microsoft.Data.Sqlite");
        content.Should().NotContain("Microsoft.EntityFrameworkCore");
        content.Should().NotContain("class ItemRepository");
        content.Should().NotContain("AddGeneratedSqlServerRepositories");
        content.Should().NotContain("AddGeneratedSqliteRepositories");
    }

    /// <summary>インメモリ Repository とランタイムパッケージ参照モードの併用が診断エラーになることを検証する</summary>
    [Fact(DisplayName = "インメモリ Repository ＋ UseRuntimePackages は診断エラー（併用不可）")]
    public void Generate_InMemoryWithRuntimePackages_ReturnsErrorDiagnostic()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = false,
                GenerateInMemoryRepositories = true,
                UseRuntimePackages = true,
            }
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message == Strings.CodeGen_Error_InMemoryRuntimePackagesExclusive
            );
        result.Files.Should().BeEmpty("診断エラー時はファイルを出力しない");
    }

    /// <summary>varbinary(max)（無制限バイナリ）と rowversion（有界バイナリ）を持つ単一エンティティ図（除外機能の検証用）</summary>
    private static ErDiagram BinaryColumnDiagram() =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "documents",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "document_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "photo",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "row_version",
                            DataType = "rowversion",
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

    /// <summary>
    /// ExcludeUnboundedBinaryColumns=true のとき、varbinary(max) 列にマーカー属性が付与され、
    /// Info 診断が 1 件出ることを検証する（rowversion 列には付与されない。ヘッダには出さない＝
    /// 生成コード側の可視化は属性が担い、一覧は生成時の診断でのみ通知する）。
    /// </summary>
    [Fact(DisplayName = "無制限バイナリ除外 ON: マーカー付与・Info 診断（rowversion は対象外）")]
    public void Generate_ExcludeUnboundedBinary_On_MarksColumnAndReportsInfo()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // マーカー属性クラスの定義が出る
        content.Should().Contain("public sealed class UnboundedBinaryColumnAttribute : Attribute");
        // varbinary(max) の Photo プロパティにマーカーが付く
        content
            .Should()
            .MatchRegex(@"\[UnboundedBinaryColumn\]\s*\r?\n\s*public byte\[\]\? Photo");
        // 付与は varbinary(max) の 1 列のみ（rowversion の RowVersion には付かない）
        System
            .Text.RegularExpressions.Regex.Matches(content, @"\[UnboundedBinaryColumn\]")
            .Count.Should()
            .Be(1, "付与対象は無制限バイナリの photo 1 列だけで rowversion は対象外");
        // ヘッダには除外列一覧を出さない（属性と Info 診断で可視化する）
        content.Should().NotContain("無制限バイナリ列（SELECT / UPDATE 除外）");
        // Info 診断が 1 件出る
        result
            .Diagnostics.Should()
            .ContainSingle(d => d.Severity == GenerationDiagnosticSeverity.Info)
            .Which.Message.Should()
            .Contain("DocumentEntity.Photo（documents.photo）");
    }

    /// <summary>
    /// ExcludeUnboundedBinaryColumns=false（既定）のとき、マーカー付与・Info 診断が出ないことを検証する
    /// （属性クラスの定義自体は Repository 生成のため出力される）。
    /// </summary>
    [Fact(DisplayName = "無制限バイナリ除外 OFF: 付与・Info なし（属性定義は repo 生成で出る）")]
    public void Generate_ExcludeUnboundedBinary_Off_DoesNotMarkOrReport()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // 付与・Info 診断はいずれも出ない
        content.Should().NotContain("[UnboundedBinaryColumn]");
        result
            .Diagnostics.Should()
            .NotContain(d => d.Severity == GenerationDiagnosticSeverity.Info);
        // ただし属性クラスの定義は Repository 生成（既定 ON）のため出力される（後続ステージの固定 infra が参照する）
        content.Should().Contain("public sealed class UnboundedBinaryColumnAttribute : Attribute");
    }

    /// <summary>
    /// rowversion（store-generated）列に <c>[StoreGeneratedColumn]</c> が付与され、EntitySaveMetadata が
    /// 書き込み集合（<c>InsertProperties</c>）から除外することを検証する（付与はオプション非依存で無条件）。
    /// SELECT 系（プロパティ生成）には残るため rowversion は読める。
    /// </summary>
    [Fact(
        DisplayName = "rowversion 列: [StoreGeneratedColumn] 付与・書き込み集合から除外（SELECT は残す）"
    )]
    public void Generate_RowVersionColumn_MarksStoreGeneratedAndExcludesFromWrite()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // マーカー属性クラスの定義が出る
        content.Should().Contain("public sealed class StoreGeneratedColumnAttribute : Attribute");
        // rowversion の RowVersion プロパティ（非 nullable byte[]）にマーカーが付く（オプション非依存で無条件）
        content
            .Should()
            .MatchRegex(@"\[StoreGeneratedColumn\]\s*\r?\n\s*public byte\[\] RowVersion");
        // 付与は rowversion の 1 列だけ（主キー・photo には付かない）
        System
            .Text.RegularExpressions.Regex.Matches(content, @"\[StoreGeneratedColumn\]")
            .Count.Should()
            .Be(1, "付与対象は rowversion の row_version 1 列だけ");
        // EntitySaveMetadata が store-generated 列を検出し、INSERT / UPDATE の書き込み集合から除外する
        content
            .Should()
            .Contain("GetCustomAttribute<StoreGeneratedColumnAttribute>()")
            .And.Contain("InsertProperties");
        // SELECT 系: RowVersion プロパティは通常どおり生成され読める（除外は書き込みのみ）
        content.Should().Contain("public byte[] RowVersion");
    }

    /// <summary>
    /// rowversion 列を持たない図では <c>[StoreGeneratedColumn]</c> の付与が起きないことを検証する
    /// （属性クラスの定義自体は Repository 生成のため出力される）。
    /// </summary>
    [Fact(
        DisplayName = "rowversion なし: [StoreGeneratedColumn] 付与なし（属性定義は repo 生成で出る）"
    )]
    public void Generate_NoRowVersion_DoesNotMarkStoreGenerated()
    {
        var result = new CSharpCodeGenerationService().Generate(
            SingleEntityDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // 付与は起きない
        content.Should().NotContain("[StoreGeneratedColumn]");
        // ただし属性クラスの定義は Repository 生成（既定 ON）のため出力される（固定 infra が参照する）
        content.Should().Contain("public sealed class StoreGeneratedColumnAttribute : Attribute");
    }

    /// <summary>
    /// GenerateRepositories &amp;&amp; ExcludeUnboundedBinaryColumns のとき、除外列ごとに Stream アクセサ
    /// （契約の Read/Write・エンジンへの委譲・ファイル糖衣の拡張メソッド）が生成されることを検証する。
    /// </summary>
    [Fact(DisplayName = "Stream アクセサ: 除外列に契約・委譲・ファイル糖衣が生成される")]
    public void Generate_BinaryStreamAccessors_On_GeneratesContractAndSugar()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // 全機能インターフェイスへ Read/Write の Stream 版が載る
        content
            .Should()
            .Contain("Task<bool> ReadPhotoAsync(int id, Stream destination, CancellationToken")
            .And.Contain(
                "Task<bool> WritePhotoAsync(int id, Stream? source, long? length = null, CancellationToken"
            );
        // 実装は固定 infra のエンジンへ委譲する
        content
            .Should()
            .Contain("ReadUnboundedBinaryColumnAsync(nameof(DocumentEntity.Photo)")
            .And.Contain("WriteUnboundedBinaryColumnAsync(nameof(DocumentEntity.Photo)");
        // ファイル糖衣は拡張メソッド静的クラスとして 1 本出る
        content
            .Should()
            .Contain("public static class DocumentRepositoryBinaryStreamExtensions")
            .And.Contain("ReadPhotoToFileAsync")
            .And.Contain("WritePhotoFromFileAsync");
    }

    /// <summary>ExcludeUnboundedBinaryColumns=false（既定）のとき、Stream アクセサは生成されない</summary>
    [Fact(DisplayName = "Stream アクセサ: 除外 OFF では生成されない")]
    public void Generate_BinaryStreamAccessors_Off_NotGenerated()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions { RootNamespace = "Sample.Domain" }
        );

        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;
        content.Should().NotContain("ReadPhotoAsync");
        content.Should().NotContain("BinaryStreamExtensions");
    }

    /// <summary>
    /// EF Core 単独生成（GenerateRepositories=false・GenerateEfCore=true）では、除外オプション ON でも
    /// Stream アクセサは契約にも現れない（QuickER 版 Repository 前提の機能のため）。
    /// </summary>
    [Fact(DisplayName = "Stream アクセサ: EF Core 単独生成では契約にも出ない")]
    public void Generate_BinaryStreamAccessors_EfOnly_NotGenerated()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCore = true,
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;
        content.Should().NotContain("ReadPhotoAsync");
        content.Should().NotContain("BinaryStreamExtensions");
    }

    /// <summary>
    /// リモート契約生成時、Stream アクセサはリモート面（<c>I{Entity}RemoteRepository</c>）へ移設され、全機能面
    /// （<c>I{Entity}Repository</c>）はリモート面を継承して見える（ネットワーク境界を越えられる操作の定義に合致）。
    /// ファイル糖衣もリモート面を対象にする。
    /// </summary>
    [Fact(
        DisplayName = "Stream アクセサ: リモート契約 ON でリモート面へ移設され全機能面から見える"
    )]
    public void Generate_BinaryStreamAccessors_OnRemoteInterface_WhenRemoteContracts()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                ExcludeUnboundedBinaryColumns = true,
                GenerateRemoteContracts = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // リモート面（インターフェイス宣言直後の本体）に Read/Write が載る
        content
            .Should()
            .MatchRegex(
                @"(?s)interface IDocumentRemoteRepository[^{]*\{[^}]*Task<bool> ReadPhotoAsync\(int id, Stream destination"
            );
        // 全機能面はリモート面を継承する空本体（Read/Write を再宣言しない）
        content
            .Should()
            .MatchRegex(@"interface IDocumentRepository\s*:\s*IDocumentRemoteRepository,");
        // ファイル糖衣はリモート面を対象にする（全機能面でもリモート面経由でも呼べる）
        content.Should().Contain("this IDocumentRemoteRepository repository");
    }

    /// <summary>
    /// リモート契約 OFF（既定）のとき、Stream アクセサは従来どおり全機能面（<c>I{Entity}Repository</c>）へ直接載り、
    /// ファイル糖衣も全機能面を対象にする（リモート面インターフェイス自体が生成されないため）。
    /// </summary>
    [Fact(DisplayName = "Stream アクセサ: リモート契約 OFF では全機能面へ直載せ")]
    public void Generate_BinaryStreamAccessors_OnFullInterface_WhenNoRemoteContracts()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BinaryColumnDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files.Single(f => f.FileName.EndsWith(".g.cs")).Content;

        // 全機能面（インターフェイス宣言直後の本体）へ直接載る
        content
            .Should()
            .MatchRegex(
                @"(?s)interface IDocumentRepository\s*:\s*IRepository<[^{]*\{[^}]*Task<bool> ReadPhotoAsync\(int id, Stream destination"
            );
        // ファイル糖衣は全機能面を対象にする
        content.Should().Contain("this IDocumentRepository repository");
    }
}
