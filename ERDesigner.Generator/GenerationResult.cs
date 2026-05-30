namespace ERDesigner.Generator;

public sealed class CodeGenerationResult
{
    public IReadOnlyList<GeneratedFile> Files { get; init; } = [];

    public IReadOnlyList<GenerationDiagnostic> Diagnostics { get; init; } = [];

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error);
}

public sealed class GeneratedFile
{
    public required string FileName { get; init; }

    public required string Content { get; init; }
}

public sealed class GenerationDiagnostic
{
    public required GenerationDiagnosticSeverity Severity { get; init; }

    public required string Message { get; init; }
}

public enum GenerationDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}
