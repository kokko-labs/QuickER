using System.IO;
using System.Text.RegularExpressions;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// スキャフォールドが <c>Generated/</c> へ書き出した <c>.g.cs</c> から、実際の <c>namespace</c> 宣言を読む（製品コードを通さない）。
/// </summary>
internal static class GeneratedNamespaceDeclarations
{
    private static readonly Regex FileScopedNamespace = new(
        @"^namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.Multiline
    );

    /// <summary>ファイル名 → そのファイルが宣言する名前空間（ファイルスコープの宣言を 1 つだけ持つ前提）</summary>
    public static IReadOnlyDictionary<string, string> Read(string generatedDirectory) =>
        Directory
            .EnumerateFiles(generatedDirectory, "*.g.cs")
            .ToDictionary(
                path => Path.GetFileName(path),
                path => FileScopedNamespace.Matches(File.ReadAllText(path)).Single().Groups[1].Value
            );

    /// <summary>本文に <paramref name="marker"/> を含むただ 1 つのファイルが宣言する名前空間</summary>
    public static string OfFileContaining(string generatedDirectory, string marker)
    {
        var path = Directory
            .EnumerateFiles(generatedDirectory, "*.g.cs")
            .Single(path => File.ReadAllText(path).Contains(marker, StringComparison.Ordinal));

        return FileScopedNamespace.Matches(File.ReadAllText(path)).Single().Groups[1].Value;
    }
}
