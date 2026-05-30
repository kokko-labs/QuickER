namespace ERDesigner.Generator;

internal sealed class CSharpGenerationModel
{
    public required string NamespaceName { get; init; }

    public required IReadOnlyList<CSharpClassModel> EntityClasses { get; init; }

    public required IReadOnlyList<CSharpClassModel> BindingModelClasses { get; init; }

    public required IReadOnlyList<string> Usings { get; init; }
}

internal sealed class CSharpClassModel
{
    public required string ClassName { get; init; }

    public required string TableName { get; init; }

    public required IReadOnlyList<CSharpPropertyModel> Properties { get; init; }

    public required IReadOnlyList<CSharpNavigationModel> Navigations { get; init; }
}

internal sealed class CSharpPropertyModel
{
    public required string PropertyName { get; init; }

    public required string ColumnName { get; init; }

    public required string TypeName { get; init; }

    public required bool IsNullable { get; init; }

    public required bool IsReferenceType { get; init; }

    public required bool IsPrimaryKey { get; init; }

    public required bool IsForeignKey { get; init; }

    public int? MaxLength { get; init; }

    public required string Initializer { get; init; }
}

internal sealed class CSharpNavigationModel
{
    public required string PropertyName { get; init; }

    public required string TypeName { get; init; }

    public required bool IsCollection { get; init; }

    public required bool IsNullable { get; init; }

    public required bool IsParentReference { get; init; }

    public required string DisplayTypeName { get; init; }

    public required string Initializer { get; init; }
}
