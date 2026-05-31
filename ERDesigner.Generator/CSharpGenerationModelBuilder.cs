namespace ERDesigner.Generator;

internal sealed class CSharpGenerationModelBuilder
{
    private readonly CSharpNameConverter _nameConverter = new();
    private readonly SqlServerCSharpTypeMapper _typeMapper = new();

    public CSharpGenerationModel Build(DiagramDefinition diagram, CodeGenerationOptions options, ICollection<GenerationDiagnostic> diagnostics)
    {
        var entityClasses = diagram.Entities.Select(entity => BuildEntityClass(entity, diagram, diagnostics)).ToList();
        var editModelClasses = diagram.Entities.Select(entity => BuildEditModelClass(entity, diagram, diagnostics)).ToList();
        var mapperClasses = diagram.Entities.Select(entity => BuildMapperClass(entity, diagram, diagnostics)).ToList();
        var usings = BuildUsings(options).ToList();

        return new CSharpGenerationModel
        {
            NamespaceName = string.IsNullOrWhiteSpace(options.NamespaceName) ? "Generated" : options.NamespaceName.Trim(),
            EntityClasses = options.GenerateEntityClasses ? entityClasses : [],
            EditModelClasses = options.GenerateEditModels ? editModelClasses : [],
            MapperClasses = options.GenerateMappers ? mapperClasses : [],
            Usings = usings,
        };
    }

    private CSharpClassModel BuildEntityClass(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        var className = _nameConverter.ToEntityClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildProperty).ToList();
        var navigations = BuildEntityNavigations(entity, diagram, diagnostics).ToList();

        return new CSharpClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = navigations,
        };
    }

    private CSharpEditModelClassModel BuildEditModelClass(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        var className = _nameConverter.ToEditModelClassName(entity.TableName);
        var properties = entity.Columns.Select(BuildEditModelProperty).ToList();
        var navigations = BuildEditModelNavigations(entity, diagram, diagnostics).ToList();

        return new CSharpEditModelClassModel
        {
            ClassName = className,
            TableName = entity.TableName,
            Properties = properties,
            Navigations = navigations,
        };
    }

    private CSharpMapperModel BuildMapperClass(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        var entityClassName = _nameConverter.ToEntityClassName(entity.TableName);
        var editModelClassName = _nameConverter.ToEditModelClassName(entity.TableName);
        var mapperClassName = _nameConverter.ToMapperClassName(entity.TableName);

        var scalarProperties = entity
            .Columns.Select(column => new CSharpMappingPropertyPair
            {
                PropertyName = _nameConverter.ToPropertyName(column.Name),
                BindingPropertyName = "Binding" + _nameConverter.ToPropertyName(column.Name),
            })
            .ToList();

        var navigations = BuildMapperNavigations(entity, diagram, diagnostics).ToList();

        return new CSharpMapperModel
        {
            ClassName = mapperClassName,
            EntityClassName = entityClassName,
            EditModelClassName = editModelClassName,
            ScalarProperties = scalarProperties,
            NavigationProperties = navigations,
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

    private CSharpEditModelPropertyModel BuildEditModelProperty(ColumnDefinition column)
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

        var propertyName = _nameConverter.ToPropertyName(column.Name);
        var fieldName = ToFieldName(propertyName);
        var bindingPropertyName = "Binding" + propertyName;
        var bindingFieldName = ToFieldName(bindingPropertyName);
        var errorFieldName = "_error" + propertyName;

        var isBytes = typeName == "byte[]" || typeName == "byte[]?";
        var needsParse = !typeInfo.IsReferenceType && !isBytes;
        var parseTypeName = needsParse ? typeName.TrimEnd('?') : string.Empty;

        string fieldInitializer;

        if (isBytes)
        {
            fieldInitializer = string.Empty;
        }
        else if (typeName == "string" && !column.IsNullable)
        {
            fieldInitializer = "string.Empty";
        }
        else if (typeInfo.IsReferenceType)
        {
            fieldInitializer = string.Empty;
        }
        else if (column.IsNullable)
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
            IsNullable = column.IsNullable,
            IsReferenceType = typeInfo.IsReferenceType,
        };
    }

    private IEnumerable<CSharpNavigationModel> BuildEntityNavigations(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        foreach (var nav in ResolveNavigations(entity, diagram, diagnostics))
        {
            var targetEntityTypeName = _nameConverter.ToEntityClassName(nav.TargetTableName);
            yield return new CSharpNavigationModel
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
    }

    private IEnumerable<CSharpNavigationModel> BuildEditModelNavigations(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        foreach (var nav in ResolveNavigations(entity, diagram, diagnostics))
        {
            var targetEditModelTypeName = _nameConverter.ToEditModelClassName(nav.TargetTableName);
            yield return new CSharpNavigationModel
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
    }

    private IEnumerable<CSharpMapperNavigationModel> BuildMapperNavigations(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
        foreach (var nav in ResolveNavigations(entity, diagram, diagnostics))
        {
            yield return new CSharpMapperNavigationModel
            {
                PropertyName = nav.PropertyName,
                EditModelTypeName = _nameConverter.ToEditModelClassName(nav.TargetTableName),
                IsCollection = nav.IsCollection,
                PrincipalColumnName = nav.PrincipalColumnName,
                DependentColumnName = nav.DependentColumnName,
            };
        }
    }

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

    private IEnumerable<NavigationInfo> ResolveNavigations(EntityDefinition entity, DiagramDefinition diagram, ICollection<GenerationDiagnostic> diagnostics)
    {
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
                yield return new NavigationInfo(
                    PropertyName: _nameConverter.ToNavigationName(target.TableName, collection: relationship.Type == RelationshipMultiplicity.OneToMany),
                    TargetTableName: target.TableName,
                    IsCollection: relationship.Type == RelationshipMultiplicity.OneToMany,
                    IsNullable: false,
                    IsParentReference: false,
                    PrincipalTableName: source.TableName,
                    PrincipalColumnName: principalColumn.Name,
                    DependentTableName: target.TableName,
                    DependentColumnName: dependentColumn.Name
                );
            }
            else if (entity.Id == target.Id)
            {
                yield return new NavigationInfo(
                    PropertyName: _nameConverter.ToNavigationName(source.TableName, collection: false),
                    TargetTableName: source.TableName,
                    IsCollection: false,
                    IsNullable: dependentColumn.IsNullable,
                    IsParentReference: true,
                    PrincipalTableName: source.TableName,
                    PrincipalColumnName: principalColumn.Name,
                    DependentTableName: target.TableName,
                    DependentColumnName: dependentColumn.Name
                );
            }
        }
    }

    private static IEnumerable<string> BuildUsings(CodeGenerationOptions options)
    {
        yield return "System.Collections.Generic";
        yield return "System.ComponentModel";

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

    private static string ToFieldName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return "_field";
        }

        var stripped = propertyName.TrimStart('@');
        return "_" + char.ToLowerInvariant(stripped[0]) + stripped[1..];
    }

    private static GenerationDiagnostic Warning(string message) => new() { Severity = GenerationDiagnosticSeverity.Warning, Message = message };
}
