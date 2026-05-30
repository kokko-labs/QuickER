namespace ERDesigner.Generator;

internal sealed class CSharpTypeInfo
{
    public required string TypeName { get; init; }

    public required bool IsReferenceType { get; init; }

    public int? MaxLength { get; init; }
}
