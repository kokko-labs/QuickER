namespace ERDesigner.Generator;

internal sealed class CSharpGenerationModelBuilder
{
    private readonly CSharpNameConverter _nameConverter = new();
    private readonly SqlServerCSharpTypeMapper _typeMapper = new();

    public CSharpGenerationModel Build(DiagramDefinition diagram, CodeGenerationOptions options, ICollection<GenerationDiagnostic> diagnostics)
    {
        var entityClasses = diagram.Entities.Select(entity => BuildEntityClass(entity, diagram, diagnostics)).ToList();
        var bindingModelClasses = diagram.Entities.Select(entity => BuildBindingModelClass(entity)).ToList();
        var usings = BuildUsings(options).ToList();

        return new CSharpGenerationModel
        {
            NamespaceName = string.IsNullOrWhiteSpace(options.NamespaceName) ? "Generated" : options.NamespaceName.Trim(),
            EntityClasses = options.GenerateEntityClasses ? entityClasses : [],
            BindingModelClasses = options.GenerateBindingModels ? bindingModelClasses : [],
            Usings = usings,
        };
    }

    private CSharpClassModel BuildEntityClass(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        var className = _nameConverter.ToEntityClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildProperty).ToList();
        var navigations = BuildNavigations(entity, diagram, diagnostics, entityClass: true).ToList();

        return new CSharpClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = navigations,
        };
    }

    private CSharpClassModel BuildBindingModelClass(EntityDefinition entity)
    {
        var className = _nameConverter.ToBindingModelClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildProperty).ToList();

        return new CSharpClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = [],
        };
    }

    private CSharpPropertyModel BuildProperty(ColumnDefinition column)
    {
        var typeInfo = _typeMapper.Map(column.DataType);
        var typeName = typeInfo.TypeName;

        if (column.IsNullable && !typeInfo.IsReferenceType)
        {
            typeName += "?";
        }
        else if (column.IsNullable && typeInfo.IsReferenceType && typeName != "byte[]")
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
            Initializer = typeName == "string" && !column.IsNullable ? " = string.Empty;" : string.Empty,
        };
    }

    private IEnumerable<CSharpNavigationModel> BuildNavigations(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics, bool entityClass)
    {
        if (!entityClass)
        {
            yield break;
        }

        foreach (var relationship in diagram.Relationships)
        {
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

            var sourceColumn = relationship.SourceColumnId is null ? null : source.Columns.FirstOrDefault(column => column.Id == relationship.SourceColumnId.Value);
            var targetColumn = relationship.TargetColumnId is null ? null : target.Columns.FirstOrDefault(column => column.Id == relationship.TargetColumnId.Value);
            var principalColumn = sourceColumn ?? source.Columns.FirstOrDefault(column => column.IsPrimaryKey);
            var dependentColumn = targetColumn ?? target.Columns.FirstOrDefault(column => column.IsForeignKey);

            if (principalColumn is null || dependentColumn is null)
            {
                diagnostics.Add(Warning($"リレーション '{relationship.Id}' はキーが不明なためナビゲーション生成をスキップしました。"));
                continue;
            }

            if (entity.Id == source.Id)
            {
                yield return new CSharpNavigationModel
                {
                    PropertyName = _nameConverter.ToNavigationName(target.TableName, collection: relationship.Type == RelationshipMultiplicity.OneToMany),
                    TypeName = _nameConverter.ToEntityClassName(target.TableName),
                    IsCollection = relationship.Type == RelationshipMultiplicity.OneToMany,
                    IsNullable = false,
                    IsParentReference = false,
                    DisplayTypeName = relationship.Type == RelationshipMultiplicity.OneToMany
                        ? $"ICollection<{_nameConverter.ToEntityClassName(target.TableName)}>"
                        : _nameConverter.ToEntityClassName(target.TableName),
                    Initializer = relationship.Type == RelationshipMultiplicity.OneToMany
                        ? $" = new List<{_nameConverter.ToEntityClassName(target.TableName)}>();"
                        : " = null!;",
                };
            }
            else if (entity.Id == target.Id)
            {
                var typeName = _nameConverter.ToEntityClassName(source.TableName);
                yield return new CSharpNavigationModel
                {
                    PropertyName = _nameConverter.ToNavigationName(source.TableName, collection: false),
                    TypeName = typeName,
                    IsCollection = false,
                    IsNullable = dependentColumn.IsNullable,
                    IsParentReference = true,
                    DisplayTypeName = dependentColumn.IsNullable ? typeName + "?" : typeName,
                    Initializer = dependentColumn.IsNullable ? string.Empty : " = null!;",
                };
            }
        }
    }

    private static IEnumerable<string> BuildUsings(CodeGenerationOptions options)
    {
        yield return "System.Collections.Generic";

        if (options.IncludeDataAnnotations)
        {
            yield return "System.ComponentModel.DataAnnotations";
            yield return "System.ComponentModel.DataAnnotations.Schema";
        }

        if (options.IncludeJsonIgnoreOnParentNavigation)
        {
            yield return "System.Text.Json.Serialization";
        }
    }

    private static GenerationDiagnostic Warning(string message) =>
        new()
        {
            Severity = GenerationDiagnosticSeverity.Warning,
            Message = message,
        };
}
