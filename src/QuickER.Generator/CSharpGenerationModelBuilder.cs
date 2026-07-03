using QuickER.Model;

namespace QuickER.Generator;

/// <summary>ER 図定義からテンプレート用の C# コード生成モデルを構築するビルダー</summary>
internal sealed partial class CSharpGenerationModelBuilder
{
    /// <summary>列名（正規化キー）→ 値オブジェクト生成モデルの対応。GenerateValueObjects が OFF のときは空で、VO 化しない</summary>
    private IReadOnlyDictionary<string, CSharpValueObjectModel> _valueObjects =
        new Dictionary<string, CSharpValueObjectModel>();

    /// <summary>テーブル名・カラム名を C# 識別子へ変換するコンバーター</summary>
    private readonly CSharpNameConverter _nameConverter = new();

    /// <summary>カラム ID → 解決済み C# 型情報。Build 呼び出し時に外部（SQL Server プロバイダ等）から受け取る。
    /// 生成器自体は DB 非依存で、SQL 型のマッピングは行わない</summary>
    private IReadOnlyDictionary<Guid, CSharpTypeInfo> _columnTypes =
        new Dictionary<Guid, CSharpTypeInfo>();

    /// <summary>ER 図定義とオプションから生成モデル全体を構築する</summary>
    /// <param name="diagnostics">生成中に検出した警告などを蓄積する出力先</param>
    /// <remarks>
    /// ナビゲーションは全エンティティ分を一度だけ解決する。以前は Entity / EditModel / Mapper の
    /// 各構築で個別に <see cref="ResolveNavigations"/> を呼んでいたため、同一リレーションの警告が
    /// （エンティティ数 × パス数）回重複していた。解決結果を共有して重複と再計算を防ぐ。
    /// また生成対象として無効なクラス群は構築自体を行わない。
    /// </remarks>
    public CSharpGenerationModel Build(
        ErDiagram diagram,
        IReadOnlyDictionary<Guid, CSharpTypeInfo> columnTypes,
        CodeGenerationOptions options,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        _columnTypes = columnTypes;
        var navigationsByEntity = ResolveAllNavigations(diagram, diagnostics);
        _valueObjects = BuildValueObjects(diagram, options, diagnostics);

        return new CSharpGenerationModel
        {
            NamespaceName = string.IsNullOrWhiteSpace(options.NamespaceName)
                ? "Generated"
                : options.NamespaceName.Trim(),
            EntityClasses = options.GenerateEntityClasses
                ? diagram
                    .Entities.Select(entity =>
                        BuildEntityClass(entity, navigationsByEntity[entity.Id])
                    )
                    .ToList()
                : [],
            EditModelClasses = options.GenerateEditModels
                ? diagram
                    .Entities.Select(entity =>
                        BuildEditModelClass(entity, navigationsByEntity[entity.Id])
                    )
                    .ToList()
                : [],
            MapperClasses = options.GenerateMappers
                ? diagram
                    .Entities.Select(entity =>
                        BuildMapperClass(entity, navigationsByEntity[entity.Id])
                    )
                    .ToList()
                : [],
            RepositoryClasses = options.GenerateRepositories
                ? diagram
                    .Entities.Select(entity => BuildRepositoryClass(entity, diagnostics))
                    .Where(model => model is not null)
                    .Cast<CSharpRepositoryModel>()
                    .ToList()
                : [],
            Usings = BuildUsings(options).ToList(),
            ValueObjectClasses = _valueObjects
                .Values.OrderBy(vo => vo.ClassName, StringComparer.Ordinal)
                .ToList(),
        };
    }

    /// <summary>エンティティ定義と解決済みナビゲーションからエンティティクラスの生成モデルを構築する</summary>
    private CSharpClassModel BuildEntityClass(
        Entity entity,
        IReadOnlyList<NavigationInfo> navigations
    )
    {
        var className = _nameConverter.ToEntityClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildProperty).ToList();

        return new CSharpClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = navigations.Select(BuildEntityNavigation).ToList(),
        };
    }

    /// <summary>エンティティ定義と解決済みナビゲーションから EditModel クラスの生成モデルを構築する</summary>
    private CSharpEditModelClassModel BuildEditModelClass(
        Entity entity,
        IReadOnlyList<NavigationInfo> navigations
    )
    {
        var className = _nameConverter.ToEditModelClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildEditModelProperty).ToList();
        var navigationModels = navigations.Select(BuildEditModelNavigation).ToList();

        return new CSharpEditModelClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = navigationModels,
            HasCascadeNavigations = navigationModels.Any(navigation => navigation.Cascade),
            TypedParentModelTypeName = ResolveTypedParentModelTypeName(className, navigationModels),
        };
    }

    /// <summary>型付き ParentModel を生成できる場合の親 EditModel 型名を解決する（親候補型がちょうど 1 つのときのみ）</summary>
    /// <remarks>
    /// 親候補は (1) 親参照ナビ（IsParentReference）の参照先型、(2) 自己参照（自身をカスケード子に持つ）の場合の自分自身の型。
    /// 複数 FK などで親候補型が 2 つ以上になる場合は型付き版を生成せず、基底の EditModelBase? のみとする。
    /// </remarks>
    private static string? ResolveTypedParentModelTypeName(
        string className,
        IReadOnlyList<CSharpNavigationModel> navigationModels
    )
    {
        var parentTypeNames = navigationModels
            .Where(navigation => navigation.IsParentReference)
            .Select(navigation => navigation.TypeName)
            .ToHashSet(StringComparer.Ordinal);

        // 自己参照（自分自身をカスケード子＝コレクション/単一参照で持つ）場合、親候補に自分自身を加える
        if (
            navigationModels.Any(navigation =>
                navigation.Cascade
                && !navigation.IsParentReference
                && string.Equals(navigation.TypeName, className, StringComparison.Ordinal)
            )
        )
        {
            parentTypeNames.Add(className);
        }

        return parentTypeNames.Count == 1 ? parentTypeNames.First() : null;
    }

    /// <summary>エンティティ定義と解決済みナビゲーションから Entity ↔ EditModel 変換 Mapper の生成モデルを構築する</summary>
    private CSharpMapperModel BuildMapperClass(
        Entity entity,
        IReadOnlyList<NavigationInfo> navigations
    )
    {
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var editModelClassName = _nameConverter.ToEditModelClassName(entity.TableName);
        var mapperClassName = _nameConverter.ToMapperClassName(entity.TableName);

        var scalarProperties = entity
            .Columns.Select(column =>
            {
                var property = BuildProperty(column);
                var editModelProperty = BuildEditModelProperty(column);
                // VO は ToString() が型ごとの表現（binary は Base64）を返す。さらに非 NULL の VO 列は
                // = null! 初期化のためロード前は null になり得るので、必須でも null 条件付きで ToString する。
                var isValueObject = ResolveValueObject(column) is not null;
                return new CSharpMappingPropertyPair
                {
                    PropertyName = property.PropertyName,
                    EntityTypeName = property.TypeName,
                    EditModelTypeName = editModelProperty.TypeName,
                    EditModelIsNullable = editModelProperty.IsNullable,
                    IsBinary = editModelProperty.IsBinary,
                    LoadBindingExpression = isValueObject
                        ? $"entity.{property.PropertyName}?.ToString() ?? string.Empty"
                        : BuildMapperBindingExpression(
                            property.TypeName,
                            editModelProperty.IsBinary,
                            property.PropertyName
                        ),
                    BindingPropertyName = "Binding" + property.PropertyName,
                };
            })
            .ToList();

        return new CSharpMapperModel
        {
            ClassName = mapperClassName,
            EntityClassName = entityClassName,
            EditModelClassName = editModelClassName,
            ScalarProperties = scalarProperties,
            NavigationProperties = navigations.Select(BuildMapperNavigation).ToList(),
        };
    }

    /// <summary>エンティティ定義から Repository の生成モデルを構築する</summary>
    /// <returns>単一主キーを持たないテーブルは対象外として null を返す</returns>
    private CSharpRepositoryModel? BuildRepositoryClass(
        Entity entity,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var keyColumn = entity.Columns.Where(column => column.IsPrimaryKey).ToList();

        // Repository は単一主キーを前提とするため、複合・主キーなしのテーブルはスキップする
        if (keyColumn.Count != 1)
        {
            diagnostics.Add(
                Warning(
                    $"テーブル '{entity.TableName}' の Repository は単一主キーのみ対応のため生成をスキップしました。"
                )
            );
            return null;
        }

        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var repositoryName = entityClassName.EndsWith("Entity", StringComparison.Ordinal)
            ? entityClassName[..^"Entity".Length]
            : entityClassName;
        var keyTypeName = BuildProperty(keyColumn[0]).TypeName.TrimEnd('?');

        return new CSharpRepositoryModel
        {
            InterfaceName = $"I{repositoryName}Repository",
            ClassName = $"{repositoryName}Repository",
            EntityClassName = entityClassName,
            KeyTypeName = keyTypeName,
        };
    }

    /// <summary>カラム定義からエンティティのスカラープロパティ生成モデルを構築する</summary>
    private CSharpPropertyModel BuildProperty(Column column)
    {
        var typeInfo = _columnTypes[column.Id];
        var valueObject = ResolveValueObject(column);

        // 値オブジェクト生成時は列の C# 型を VO 型へ置き換える（VO は参照型）。
        var typeName = valueObject?.ClassName ?? typeInfo.TypeName;
        var isReferenceType = valueObject is not null || typeInfo.IsReferenceType;

        // NULL 許容列は型へ ? を付与する。値型は Nullable<T>、参照型（string / byte[] / VO）は nullable 注釈となる。
        // 非 NULL の string / byte[] は ? を付けず、後段の初期化子で空既定値を与えて CS8618 を回避する
        if (column.IsNullable)
        {
            typeName += "?";
        }

        return new CSharpPropertyModel
        {
            PropertyName = _nameConverter.ToPropertyName(column.Name),
            ColumnName = column.Name,
            TypeName = typeName,
            IsNullable = column.IsNullable,
            IsReferenceType = isReferenceType,
            IsPrimaryKey = column.IsPrimaryKey,
            IsForeignKey = column.IsForeignKey,
            // VO は [MaxLength] を出さない（長さ検証は VO 内部で行う。非 string 型に [MaxLength] を付けない安全策にもなる）
            MaxLength = valueObject is not null ? null : typeInfo.MaxLength,
            // [SqlColumnType] の Precision/Scale に載せる DB カラムのメタ情報。VO 抑制なしの生値。decimal は precision 指定時に scale 既定 0
            FacetPrecision = typeInfo.Precision,
            FacetScale = typeInfo.Precision is not null ? typeInfo.Scale ?? 0 : null,
            // SQL パラメータ型明示化（[SqlColumnType]）用。VO 有無に関わらず DB 由来の生値を載せる（束縛は素値へ開いてから）
            SqlDbTypeName = typeInfo.SqlDbTypeName,
            SqlDeclaredLength = typeInfo.SqlDeclaredLength,
            // 非 NULL の VO は妥当な空既定値を作れないため null! でロード前提を表明（NULL 許容 VO は初期化不要）
            Initializer = valueObject is not null
                ? (column.IsNullable ? string.Empty : " = null!;")
                : BuildEntityInitializer(typeName, column.IsNullable),
        };
    }

    /// <summary>カラム定義から EditModel のプロパティ生成モデルを構築する</summary>
    /// <remarks>
    /// EditModel は入力途中の不正値も保持するため、値型・文字列・バイナリは原則 NULL 許容とし、
    /// 確定値プロパティと UI バインディング用文字列プロパティの両方の情報を組み立てる
    /// </remarks>
    private CSharpEditModelPropertyModel BuildEditModelProperty(Column column)
    {
        var typeInfo = _columnTypes[column.Id];
        var valueObject = ResolveValueObject(column);
        if (valueObject is not null)
        {
            return BuildValueObjectEditModelProperty(column, valueObject);
        }

        var typeName = typeInfo.TypeName;

        var isBytes = typeName == "byte[]";
        var editModelIsNullable =
            column.IsNullable || !typeInfo.IsReferenceType || typeName == "string" || isBytes;

        if (editModelIsNullable && !typeInfo.IsReferenceType)
        {
            typeName += "?";
        }
        else if (editModelIsNullable && typeInfo.IsReferenceType)
        {
            typeName += "?";
        }

        var propertyName = _nameConverter.ToPropertyName(column.Name);
        var fieldName = ToFieldName(propertyName);
        var bindingPropertyName = "Binding" + propertyName;
        var bindingFieldName = ToFieldName(bindingPropertyName);

        isBytes = typeName == "byte[]" || typeName == "byte[]?";
        var needsParse = !typeInfo.IsReferenceType && !isBytes;
        var parseTypeName = needsParse ? typeName.TrimEnd('?') : string.Empty;

        string fieldInitializer;

        if (typeInfo.IsReferenceType)
        {
            fieldInitializer = string.Empty;
        }
        else if (editModelIsNullable)
        {
            fieldInitializer = string.Empty;
        }
        else
        {
            fieldInitializer = "default";
        }

        var bindingFieldInitializer = "string.Empty";

        return new CSharpEditModelPropertyModel
        {
            PropertyName = propertyName,
            TypeName = typeName,
            FieldName = fieldName,
            BindingPropertyName = bindingPropertyName,
            BindingFieldName = bindingFieldName,
            NeedsParse = needsParse,
            ParseTypeName = parseTypeName,
            FieldInitializer = fieldInitializer,
            BindingFieldInitializer = bindingFieldInitializer,
            IsNullable = editModelIsNullable,
            IsReferenceType = typeInfo.IsReferenceType,
            IsBinary = isBytes,
            // Entity 側が非 NULL（必須）で EditModel 側は入力途中を許容して NULL 許容にした項目を必須とみなす
            IsRequired = editModelIsNullable && !column.IsNullable,
            RevertBindingExpression = BuildBindingExpression(propertyName, isBytes),
        };
    }

    /// <summary>確定値から UI バインディング文字列へ戻す式を生成する（バイナリは Base64 化する）</summary>
    private static string BuildBindingExpression(string propertyName, bool isBinary)
    {
        if (isBinary)
        {
            return $"{propertyName} is null ? string.Empty : Convert.ToBase64String({propertyName})";
        }

        return $"{propertyName}?.ToString() ?? string.Empty";
    }

    /// <summary>非 NULL の string / byte[] プロパティに対する空既定値の初期化子を生成する</summary>
    private static string BuildEntityInitializer(string typeName, bool isNullable)
    {
        if (typeName == "string" && !isNullable)
        {
            return " = string.Empty;";
        }

        if (typeName == "byte[]" && !isNullable)
        {
            return " = Array.Empty<byte>();";
        }

        return string.Empty;
    }

    /// <summary>Mapper 内で Entity プロパティを EditModel のバインディング文字列へ変換する式を生成する</summary>
    private static string BuildMapperBindingExpression(
        string entityTypeName,
        bool isBinary,
        string propertyName
    )
    {
        if (isBinary)
        {
            return $"entity.{propertyName} is null ? string.Empty : Convert.ToBase64String(entity.{propertyName})";
        }

        if (entityTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            return $"entity.{propertyName}?.ToString() ?? string.Empty";
        }

        return $"entity.{propertyName}.ToString() ?? string.Empty";
    }

    /// <summary>解決済みナビゲーション情報からエンティティのナビゲーションプロパティ生成モデルを構築する</summary>
    private CSharpNavigationModel BuildEntityNavigation(NavigationInfo nav)
    {
        var targetEntityTypeName = _nameConverter.ToEntityClassName(nav.TargetTableName);
        return new CSharpNavigationModel
        {
            PropertyName = nav.PropertyName,
            TypeName = targetEntityTypeName,
            IsCollection = nav.IsCollection,
            IsNullable = nav.IsNullable,
            IsParentReference = nav.IsParentReference,
            // 子方向（親参照でない）ナビゲーションのみカスケード保存/削除の対象とする
            Cascade = !nav.IsParentReference,
            DisplayTypeName = nav.IsCollection
                ? $"ICollection<{targetEntityTypeName}>"
                : (nav.IsNullable ? targetEntityTypeName + "?" : targetEntityTypeName),
            Initializer = nav.IsCollection
                ? $" = new List<{targetEntityTypeName}>();"
                : (nav.IsNullable ? string.Empty : " = null!;"),
            PrincipalTableName = nav.PrincipalTableName,
            PrincipalColumnName = nav.PrincipalColumnName,
            DependentTableName = nav.DependentTableName,
            DependentColumnName = nav.DependentColumnName,
        };
    }

    /// <summary>解決済みナビゲーション情報から EditModel のナビゲーションプロパティ生成モデルを構築する</summary>
    private CSharpNavigationModel BuildEditModelNavigation(NavigationInfo nav)
    {
        var targetEditModelTypeName = _nameConverter.ToEditModelClassName(nav.TargetTableName);
        return new CSharpNavigationModel
        {
            PropertyName = nav.PropertyName,
            TypeName = targetEditModelTypeName,
            IsCollection = nav.IsCollection,
            IsNullable = nav.IsNullable,
            IsParentReference = nav.IsParentReference,
            Cascade = !nav.IsParentReference,
            DisplayTypeName = nav.IsCollection
                ? $"EditModelCollection<{targetEditModelTypeName}>"
                : (nav.IsNullable ? targetEditModelTypeName + "?" : targetEditModelTypeName),
            // カスケード子（親→子）のみバッキングフィールドで所有者リンクを張るため、フィールド名を用意する
            FieldName = nav.IsParentReference ? string.Empty : ToFieldName(nav.PropertyName),
            Initializer = nav.IsCollection
                ? $" = new EditModelCollection<{targetEditModelTypeName}>();"
                : (nav.IsNullable ? string.Empty : " = null!;"),
            PrincipalTableName = nav.PrincipalTableName,
            PrincipalColumnName = nav.PrincipalColumnName,
            DependentTableName = nav.DependentTableName,
            DependentColumnName = nav.DependentColumnName,
        };
    }

    /// <summary>解決済みナビゲーション情報から Mapper が扱うナビゲーションプロパティ生成モデルを構築する</summary>
    private CSharpMapperNavigationModel BuildMapperNavigation(NavigationInfo nav) =>
        new()
        {
            PropertyName = nav.PropertyName,
            EditModelTypeName = _nameConverter.ToEditModelClassName(nav.TargetTableName),
            MapperClassName = _nameConverter.ToMapperClassName(nav.TargetTableName),
            IsCollection = nav.IsCollection,
            IsNullable = nav.IsNullable,
            // 子方向（親参照でない）のみカスケード変換の対象とし、親をたどる無限再帰を防ぐ
            IsCascade = !nav.IsParentReference,
            PrincipalColumnName = nav.PrincipalColumnName,
            DependentColumnName = nav.DependentColumnName,
        };

    /// <summary>ナビゲーション解決の中間結果（生成側の表現に依存しない情報）</summary>
    private sealed record NavigationInfo(
        string PropertyName,
        string TargetTableName,
        bool IsCollection,
        bool IsNullable,
        bool IsParentReference,
        string PrincipalTableName,
        string PrincipalColumnName,
        string DependentTableName,
        string DependentColumnName
    );

    /// <summary>全リレーションを一度だけ走査し、エンティティ ID ごとのナビゲーション情報を解決する</summary>
    /// <remarks>
    /// 多対多や参照先・キーが解決できないリレーションは警告を出して生成対象外とする。
    /// 警告はリレーション単位で 1 回だけ追加されるため、従来のような重複は発生しない。
    /// </remarks>
    private Dictionary<Guid, List<NavigationInfo>> ResolveAllNavigations(
        ErDiagram diagram,
        ICollection<GenerationDiagnostic> diagnostics
    )
    {
        var navigationsByEntity = diagram.Entities.ToDictionary(
            entity => entity.Id,
            _ => new List<NavigationInfo>()
        );

        foreach (var relationship in diagram.Relationships)
        {
            // 多対多は中間テーブルを介する設計のため C# 生成では直接ナビゲーションを作らない
            if (relationship.Type == RelationshipType.ManyToMany)
            {
                diagnostics.Add(
                    Warning(
                        $"多対多リレーション '{relationship.Id}' は C# 生成対象外のためスキップしました。"
                    )
                );
                continue;
            }

            var source = diagram.Entities.FirstOrDefault(item =>
                item.Id == relationship.SourceEntityId
            );
            var target = diagram.Entities.FirstOrDefault(item =>
                item.Id == relationship.TargetEntityId
            );

            if (source is null || target is null)
            {
                diagnostics.Add(
                    Warning(
                        $"リレーション '{relationship.Id}' は参照先エンティティが見つからないためスキップしました。"
                    )
                );
                continue;
            }

            // 明示指定の列を優先し、無ければ principal は主キー、dependent は外部キー列にフォールバックする
            var sourceColumn = relationship.SourceColumnId is null
                ? null
                : source.Columns.FirstOrDefault(column =>
                    column.Id == relationship.SourceColumnId.Value
                );
            var targetColumn = relationship.TargetColumnId is null
                ? null
                : target.Columns.FirstOrDefault(column =>
                    column.Id == relationship.TargetColumnId.Value
                );
            var principalColumn =
                sourceColumn ?? source.Columns.FirstOrDefault(column => column.IsPrimaryKey);
            var dependentColumn =
                targetColumn ?? target.Columns.FirstOrDefault(column => column.IsForeignKey);

            if (principalColumn is null || dependentColumn is null)
            {
                diagnostics.Add(
                    Warning(
                        $"リレーション '{relationship.Id}' はキーが不明なためナビゲーション生成をスキップしました。"
                    )
                );
                continue;
            }

            var isCollection = relationship.Type == RelationshipType.OneToMany;

            // source 側（親）は子へのナビゲーション（1 対多なら collection）を持つ
            navigationsByEntity[source.Id]
                .Add(
                    new NavigationInfo(
                        PropertyName: _nameConverter.ToNavigationName(
                            target.TableName,
                            collection: isCollection
                        ),
                        TargetTableName: target.TableName,
                        IsCollection: isCollection,
                        IsNullable: false,
                        IsParentReference: false,
                        PrincipalTableName: source.TableName,
                        PrincipalColumnName: principalColumn.Name,
                        DependentTableName: target.TableName,
                        DependentColumnName: dependentColumn.Name
                    )
                );

            // target 側（子）は親への単一参照ナビゲーションを持つ。
            // 自己参照（source == target）の場合は従来どおり子側ナビゲーションのみとし、重複を避ける
            if (target.Id != source.Id)
            {
                navigationsByEntity[target.Id]
                    .Add(
                        new NavigationInfo(
                            PropertyName: _nameConverter.ToNavigationName(
                                source.TableName,
                                collection: false
                            ),
                            TargetTableName: source.TableName,
                            IsCollection: false,
                            IsNullable: dependentColumn.IsNullable,
                            IsParentReference: true,
                            PrincipalTableName: source.TableName,
                            PrincipalColumnName: principalColumn.Name,
                            DependentTableName: target.TableName,
                            DependentColumnName: dependentColumn.Name
                        )
                    );
            }
        }

        return navigationsByEntity;
    }

    /// <summary>生成オプションに応じて必要な using 名前空間の集合を構築する</summary>
    private static IEnumerable<string> BuildUsings(CodeGenerationOptions options)
    {
        // 明示的な using を網羅し、ImplicitUsings 無効のプロジェクトでもそのままコンパイルできるようにする。
        // System / System.Collections.Generic / System.Linq は共有フレームワークに常時含まれ、生成コードの
        // ほぼ全構成で使用するため無条件で付与する（未使用でも auto-generated ファイルでは警告抑止される）。
        var usings = new HashSet<string> { "System", "System.Collections.Generic", "System.Linq" };

        // EntityBase の値比較・値ハッシュ・JSON 出力／クローンで使用：
        // StructuralComparisons（System.Collections）、値プロパティのキャッシュ（System.Collections.Concurrent /
        // System.Reflection）、ToJson / Clone（System.Text.Json）
        if (options.GenerateEntityClasses)
        {
            usings.Add("System.Collections");
            usings.Add("System.Collections.Concurrent");
            usings.Add("System.Reflection");
            usings.Add("System.Text.Json");
            usings.Add("System.Text.Json.Serialization");
        }

        // INotifyPropertyChanged / INotifyDataErrorInfo（EditModelBase）、EditModelCollection の ObservableCollection、
        // Owner（IList）・GetErrors（IEnumerable）の System.Collections
        if (options.GenerateEditModels)
        {
            usings.Add("System.ComponentModel");
            usings.Add("System.Collections");
            usings.Add("System.Collections.ObjectModel");
        }

        if (options.GenerateRepositories)
        {
            usings.Add("System.Collections");
            usings.Add("System.Collections.Concurrent");
            usings.Add("System.Data");
            usings.Add("System.Linq.Expressions");
            usings.Add("System.Reflection");
            usings.Add("System.Text.Json");
            usings.Add("System.Text.Json.Serialization.Metadata");
            usings.Add("System.Threading");
            usings.Add("System.Threading.Tasks");
            usings.Add("Microsoft.Data.SqlClient");
            usings.Add("Microsoft.Extensions.DependencyInjection");
        }

        if (options.IncludeDataAnnotations)
        {
            usings.Add("System.ComponentModel.DataAnnotations");
            usings.Add("System.ComponentModel.DataAnnotations.Schema");
            // [SqlColumnType] は Repository 生成時に加え IncludeDataAnnotations 時にも出力され得るため、
            // SqlDbType（System.Data、BCL）の using をこちらでも保証する（Repository なし構成向け）。
            usings.Add("System.Data");
        }

        if (options.IncludeJsonIgnoreOnParentNavigation)
        {
            usings.Add("System.Text.Json.Serialization");
        }

        // 値オブジェクト：等価(StructuralComparisons)・JSON 変換器・SqlValueObjectActivator(CultureInfo)・リフレクション
        if (options.GenerateValueObjects)
        {
            usings.Add("System.Collections");
            usings.Add("System.Globalization");
            usings.Add("System.Reflection");
            usings.Add("System.Text.Json");
            usings.Add("System.Text.Json.Serialization");
        }

        // System を先頭、続いて System.* を序数順、最後にそれ以外（Microsoft.* 等）を序数順で安定的に並べる
        return usings
            .OrderByDescending(ns => ns == "System")
            .ThenByDescending(ns => ns.StartsWith("System", StringComparison.Ordinal))
            .ThenBy(ns => ns, StringComparer.Ordinal);
    }

    /// <summary>プロパティ名から先頭小文字・アンダースコア始まりのフィールド名を導出する</summary>
    private static string ToFieldName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return "_field";
        }

        // 逐語的識別子の @ を除いた先頭文字を小文字化し _ を前置する
        var stripped = propertyName.TrimStart('@');
        return "_" + char.ToLowerInvariant(stripped[0]) + stripped[1..];
    }

    /// <summary>警告レベルの診断情報を生成する</summary>
    private static GenerationDiagnostic Warning(string message) =>
        new() { Severity = GenerationDiagnosticSeverity.Warning, Message = message };
}
