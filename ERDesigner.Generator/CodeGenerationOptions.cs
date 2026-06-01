namespace ERDesigner.Generator;

/// <summary>
/// C# コード生成の設定です。
/// </summary>
public sealed class CodeGenerationOptions
{
    public string NamespaceName { get; init; } = "Generated";

    public string OutputFileName { get; init; } = "ErDesignerEntities.g.cs";

    public bool GenerateEntityClasses { get; init; } = true;

    public bool GenerateEditModels { get; init; } = true;

    public bool GenerateMappers { get; init; } = true;

    public bool GenerateRepositories { get; init; } = true;

    public bool IncludeDataAnnotations { get; init; } = true;

    public bool IncludeJsonIgnoreOnParentNavigation { get; init; } = true;
}
