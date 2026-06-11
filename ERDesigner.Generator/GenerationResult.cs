namespace ERDesigner.Generator;

/// <summary>
/// コード生成の結果（生成ファイルと診断情報）
/// </summary>
public sealed class CodeGenerationResult
{
    /// <summary>生成されたファイルの一覧。エラー時は空になる</summary>
    public IReadOnlyList<GeneratedFile> Files { get; init; } = [];

    /// <summary>生成中に収集した診断情報の一覧</summary>
    public IReadOnlyList<GenerationDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>エラー診断を含むかどうか</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == GenerationDiagnosticSeverity.Error);
}

/// <summary>
/// 生成された 1 ファイルの内容
/// </summary>
public sealed class GeneratedFile
{
    /// <summary>出力ファイル名（".g.cs" 拡張子に正規化済み）</summary>
    public required string FileName { get; init; }

    /// <summary>生成された C# ソースコード全文</summary>
    public required string Content { get; init; }
}

/// <summary>
/// 生成処理の診断メッセージ
/// </summary>
public sealed class GenerationDiagnostic
{
    /// <summary>診断の重要度</summary>
    public required GenerationDiagnosticSeverity Severity { get; init; }

    /// <summary>利用者向けの診断メッセージ（日本語）</summary>
    public required string Message { get; init; }
}

/// <summary>
/// 診断の重要度
/// </summary>
public enum GenerationDiagnosticSeverity
{
    /// <summary>情報。生成には影響しない</summary>
    Info,

    /// <summary>警告。一部の生成がスキップ・縮退されるが処理は継続する</summary>
    Warning,

    /// <summary>エラー。生成処理を中断する</summary>
    Error,
}
