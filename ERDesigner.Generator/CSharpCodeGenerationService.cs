namespace ERDesigner.Generator;

public sealed class CSharpCodeGenerationService
{
    private readonly CSharpGenerationModelBuilder _modelBuilder = new();
    private readonly ScribanCSharpRenderer _renderer = new();

    public CodeGenerationResult Generate(DiagramDefinition diagram, CodeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<GenerationDiagnostic>();
        Validate(diagram, options, diagnostics);

        if (diagnostics.Any(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error))
        {
            return new CodeGenerationResult { Files = [], Diagnostics = diagnostics };
        }

        var model = _modelBuilder.Build(diagram, options, diagnostics);
        var content = _renderer.Render(model, options);

        return new CodeGenerationResult { Files = [new GeneratedFile { FileName = SanitizeFileName(options.OutputFileName), Content = content }], Diagnostics = diagnostics };
    }

    private static void Validate(DiagramDefinition diagram, CodeGenerationOptions options, ICollection<GenerationDiagnostic> diagnostics)
    {
        if (!options.GenerateEntityClasses && !options.GenerateEditModels && !options.GenerateMappers && !options.GenerateRepositories)
        {
            diagnostics.Add(Error("Entity / EditModel / Mapper / Repository のいずれも生成対象になっていません。少なくとも一つを有効にしてください。"));
        }

        if (diagram.Entities.Count == 0)
        {
            diagnostics.Add(Error("ER 図にエンティティがありません。"));
        }

        foreach (var entity in diagram.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.TableName))
            {
                diagnostics.Add(Error("テーブル名が空のエンティティがあります。"));
            }

            if (entity.Columns.Count(column => column.IsPrimaryKey) > 1)
            {
                diagnostics.Add(Warning($"テーブル '{entity.TableName}' は複合主キーのため [Key] 属性生成は最小限になります。MVP では単一主キーを推奨します。"));
            }
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var value = string.IsNullOrWhiteSpace(fileName) ? "ErDesignerEntities.g.cs" : fileName.Trim();
        return value.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ? value : Path.GetFileNameWithoutExtension(value) + ".g.cs";
    }

    private static GenerationDiagnostic Error(string message) => new() { Severity = GenerationDiagnosticSeverity.Error, Message = message };

    private static GenerationDiagnostic Warning(string message) => new() { Severity = GenerationDiagnosticSeverity.Warning, Message = message };
}
