namespace ERDesigner.Generator;

/// <summary>ER 図定義からテンプレート用の C# コード生成モデルを構築するビルダー</summary>
internal sealed class CSharpGenerationModelBuilder
{
    /// <summary>テーブル名・カラム名を C# 識別子へ変換するコンバーター</summary>
    private readonly CSharpNameConverter _nameConverter = new();

    /// <summary>SQL Server 型を C# 型へ対応付けるマッパー</summary>
    private readonly SqlServerCSharpTypeMapper _typeMapper = new();

    /// <summary>ER 図定義とオプションから生成モデル全体を構築する</summary>
    /// <param name="diagnostics">生成中に検出した警告などを蓄積する出力先</param>
    /// <remarks>
    /// ナビゲーションは全エンティティ分を一度だけ解決する。以前は Entity / EditModel / Mapper の
    /// 各構築で個別に <see cref="ResolveNavigations"/> を呼んでいたため、同一リレーションの警告が
    /// （エンティティ数 × パス数）回重複していた。解決結果を共有して重複と再計算を防ぐ。
    /// また生成対象として無効なクラス群は構築自体を行わない。
    /// </remarks>
    public CSharpGenerationModel Build(DiagramDefinition diagram, CodeGenerationOptions options, ICollection<GenerationDiagnostic> diagnostics)
    {
        var navigationsByEntity = ResolveAllNavigations(diagram, diagnostics);

        return new CSharpGenerationModel
        {
            NamespaceName = string.IsNullOrWhiteSpace(options.NamespaceName) ? "Generated" : options.NamespaceName.Trim(),
            EntityClasses = options.GenerateEntityClasses
                ? diagram.Entities.Select(entity => BuildEntityClass(entity, navigationsByEntity[entity.Id])).ToList()
                : [],
            EditModelClasses = options.GenerateEditModels
                ? diagram.Entities.Select(entity => BuildEditModelClass(entity, navigationsByEntity[entity.Id])).ToList()
                : [],
            MapperClasses = options.GenerateMappers
                ? diagram.Entities.Select(entity => BuildMapperClass(entity, navigationsByEntity[entity.Id])).ToList()
                : [],
            RepositoryClasses = options.GenerateRepositories
                ? diagram
                    .Entities.Select(entity => BuildRepositoryClass(entity, diagnostics))
                    .Where(model => model is not null)
                    .Cast<CSharpRepositoryModel>()
                    .ToList()
                : [],
            Usings = BuildUsings(options).ToList(),
        };
    }

    /// <summary>エンティティ定義と解決済みナビゲーションからエンティティクラスの生成モデルを構築する</summary>
    private CSharpClassModel BuildEntityClass(EntityDefinition entity, IReadOnlyList<NavigationInfo> navigations)
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
    private CSharpEditModelClassModel BuildEditModelClass(EntityDefinition entity, IReadOnlyList<NavigationInfo> navigations)
    {
        var className = _nameConverter.ToEditModelClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildEditModelProperty).ToList();

        return new CSharpEditModelClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = navigations.Select(BuildEditModelNavigation).ToList(),
        };
    }

    /// <summary>エンティティ定義と解決済みナビゲーションから Entity ↔ EditModel 変換 Mapper の生成モデルを構築する</summary>
    private CSharpMapperModel BuildMapperClass(EntityDefinition entity, IReadOnlyList<NavigationInfo> navigations)
    {
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var editModelClassName = _nameConverter.ToEditModelClassName(entity.TableName);
        var mapperClassName = _nameConverter.ToMapperClassName(entity.TableName);

        var scalarProperties = entity
            .Columns.Select(column =>
            {
                var property = BuildProperty(column);
                var editModelProperty = BuildEditModelProperty(column);
                return new CSharpMappingPropertyPair
                {
                    PropertyName = property.PropertyName,
                    EntityTypeName = property.TypeName,
                    EditModelTypeName = editModelProperty.TypeName,
                    EditModelIsNullable = editModelProperty.IsNullable,
                    IsBinary = editModelProperty.IsBinary,
                    LoadBindingExpression = BuildMapperBindingExpression(property.TypeName, editModelProperty.IsBinary, property.PropertyName),
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
    private CSharpRepositoryModel? BuildRepositoryClass(EntityDefinition entity, ICollection<GenerationDiagnostic> diagnostics)
    {
        var keyColumn = entity.Columns.Where(column => column.IsPrimaryKey).ToList();

        // Repository は単一主キーを前提とするため、複合・主キーなしのテーブルはスキップする
        if (keyColumn.Count != 1)
        {
            diagnostics.Add(Warning($"テーブル '{entity.TableName}' の Repository は単一主キーのみ対応のため生成をスキップしました。"));
            return null;
        }

        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var repositoryName = entityClassName.EndsWith("Entity", StringComparison.Ordinal) ? entityClassName[..^"Entity".Length] : entityClassName;
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
    private CSharpPropertyModel BuildProperty(ColumnDefinition column)
    {
        var typeInfo = _typeMapper.Map(column.DataType);
        var typeName = typeInfo.TypeName;

        // NULL 許容列は型へ ? を付与する。値型は Nullable<T>、参照型（string / byte[]）は nullable 注釈となる。
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
            IsReferenceType = typeInfo.IsReferenceType,
            IsPrimaryKey = column.IsPrimaryKey,
            IsForeignKey = column.IsForeignKey,
            MaxLength = typeInfo.MaxLength,
            Initializer = BuildEntityInitializer(typeName, column.IsNullable),
        };
    }

    /// <summary>カラム定義から EditModel のプロパティ生成モデルを構築する</summary>
    /// <remarks>
    /// EditModel は入力途中の不正値も保持するため、値型・文字列・バイナリは原則 NULL 許容とし、
    /// 確定値プロパティと UI バインディング用文字列プロパティの両方の情報を組み立てる
    /// </remarks>
    private CSharpEditModelPropertyModel BuildEditModelProperty(ColumnDefinition column)
    {
        var typeInfo = _typeMapper.Map(column.DataType);
        var typeName = typeInfo.TypeName;

        var isBytes = typeName == "byte[]";
        var editModelIsNullable = column.IsNullable || !typeInfo.IsReferenceType || typeName == "string" || isBytes;

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
        var errorFieldName = "_error" + propertyName;

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
            ErrorFieldName = errorFieldName,
            NeedsParse = needsParse,
            ParseTypeName = parseTypeName,
            FieldInitializer = fieldInitializer,
            BindingFieldInitializer = bindingFieldInitializer,
            IsNullable = editModelIsNullable,
            IsReferenceType = typeInfo.IsReferenceType,
            IsBinary = isBytes,
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
    private static string BuildMapperBindingExpression(string entityTypeName, bool isBinary, string propertyName)
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
            DisplayTypeName = nav.IsCollection ? $"ICollection<{targetEntityTypeName}>" : (nav.IsNullable ? targetEntityTypeName + "?" : targetEntityTypeName),
            Initializer = nav.IsCollection ? $" = new List<{targetEntityTypeName}>();" : (nav.IsNullable ? string.Empty : " = null!;"),
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
            DisplayTypeName = nav.IsCollection ? $"ICollection<{targetEditModelTypeName}>" : (nav.IsNullable ? targetEditModelTypeName + "?" : targetEditModelTypeName),
            Initializer = nav.IsCollection ? $" = new List<{targetEditModelTypeName}>();" : (nav.IsNullable ? string.Empty : " = null!;"),
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
            IsCollection = nav.IsCollection,
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
    private Dictionary<Guid, List<NavigationInfo>> ResolveAllNavigations(DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        var navigationsByEntity = diagram.Entities.ToDictionary(entity => entity.Id, _ => new List<NavigationInfo>());

        foreach (var relationship in diagram.Relationships)
        {
            // 多対多は中間テーブルを介する設計のため C# 生成では直接ナビゲーションを作らない
            if (relationship.Type == RelationshipMultiplicity.ManyToMany)
            {
                diagnostics.Add(Warning($"多対多リレーション '{relationship.Id}' は C# 生成対象外のためスキップしました。"));
                continue;
            }

            var source = diagram.Entities.FirstOrDefault(item => item.Id == relationship.SourceEntityId);
            var target = diagram.Entities.FirstOrDefault(item => item.Id == relationship.TargetEntityId);

            if (source is null || target is null)
            {
                diagnostics.Add(Warning($"リレーション '{relationship.Id}' は参照先エンティティが見つからないためスキップしました。"));
                continue;
            }

            // 明示指定の列を優先し、無ければ principal は主キー、dependent は外部キー列にフォールバックする
            var sourceColumn = relationship.SourceColumnId is null ? null : source.Columns.FirstOrDefault(column => column.Id == relationship.SourceColumnId.Value);
            var targetColumn = relationship.TargetColumnId is null ? null : target.Columns.FirstOrDefault(column => column.Id == relationship.TargetColumnId.Value);
            var principalColumn = sourceColumn ?? source.Columns.FirstOrDefault(column => column.IsPrimaryKey);
            var dependentColumn = targetColumn ?? target.Columns.FirstOrDefault(column => column.IsForeignKey);

            if (principalColumn is null || dependentColumn is null)
            {
                diagnostics.Add(Warning($"リレーション '{relationship.Id}' はキーが不明なためナビゲーション生成をスキップしました。"));
                continue;
            }

            var isCollection = relationship.Type == RelationshipMultiplicity.OneToMany;

            // source 側（親）は子へのナビゲーション（1 対多なら collection）を持つ
            navigationsByEntity[source.Id].Add(new NavigationInfo(
                PropertyName: _nameConverter.ToNavigationName(target.TableName, collection: isCollection),
                TargetTableName: target.TableName,
                IsCollection: isCollection,
                IsNullable: false,
                IsParentReference: false,
                PrincipalTableName: source.TableName,
                PrincipalColumnName: principalColumn.Name,
                DependentTableName: target.TableName,
                DependentColumnName: dependentColumn.Name
            ));

            // target 側（子）は親への単一参照ナビゲーションを持つ。
            // 自己参照（source == target）の場合は従来どおり子側ナビゲーションのみとし、重複を避ける
            if (target.Id != source.Id)
            {
                navigationsByEntity[target.Id].Add(new NavigationInfo(
                    PropertyName: _nameConverter.ToNavigationName(source.TableName, collection: false),
                    TargetTableName: source.TableName,
                    IsCollection: false,
                    IsNullable: dependentColumn.IsNullable,
                    IsParentReference: true,
                    PrincipalTableName: source.TableName,
                    PrincipalColumnName: principalColumn.Name,
                    DependentTableName: target.TableName,
                    DependentColumnName: dependentColumn.Name
                ));
            }
        }

        return navigationsByEntity;
    }

    /// <summary>生成オプションに応じて必要な using 名前空間の集合を構築する</summary>
    private static IEnumerable<string> BuildUsings(CodeGenerationOptions options)
    {
        var usings = new HashSet<string> { "System.Collections.Generic", "System.ComponentModel" };

        if (options.GenerateRepositories)
        {
            usings.Add("System.Reflection");
            usings.Add("System.Threading");
            usings.Add("System.Threading.Tasks");
            usings.Add("Microsoft.Data.SqlClient");
            usings.Add("Microsoft.Extensions.DependencyInjection");
        }

        if (options.IncludeDataAnnotations)
        {
            usings.Add("System.ComponentModel.DataAnnotations");
            usings.Add("System.ComponentModel.DataAnnotations.Schema");
        }

        if (options.IncludeJsonIgnoreOnParentNavigation)
        {
            usings.Add("System.Text.Json.Serialization");
        }

        foreach (var usingNamespace in usings)
        {
            yield return usingNamespace;
        }
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
    private static GenerationDiagnostic Warning(string message) => new() { Severity = GenerationDiagnosticSeverity.Warning, Message = message };
}
