namespace ERDesigner.Generator;

public sealed class DiagramDefinition
{
    public IReadOnlyList<EntityDefinition> Entities { get; init; } = [];

    public IReadOnlyList<RelationshipDefinition> Relationships { get; init; } = [];
}

public sealed class EntityDefinition
{
    public Guid Id { get; init; }

    public string TableName { get; init; } = string.Empty;

    public IReadOnlyList<ColumnDefinition> Columns { get; init; } = [];
}

public sealed class ColumnDefinition
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;

    public bool IsPrimaryKey { get; init; }

    public bool IsForeignKey { get; init; }

    public bool IsNullable { get; init; } = true;
}

public sealed class RelationshipDefinition
{
    public Guid Id { get; init; }

    public Guid SourceEntityId { get; init; }

    public Guid TargetEntityId { get; init; }

    public RelationshipMultiplicity Type { get; init; } = RelationshipMultiplicity.OneToMany;

    public Guid? SourceColumnId { get; init; }

    public Guid? TargetColumnId { get; init; }
}

public enum RelationshipMultiplicity
{
    OneToOne,
    OneToMany,
    ManyToMany,
}
