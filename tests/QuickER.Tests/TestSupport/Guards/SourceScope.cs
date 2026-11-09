using System.IO;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則が走査する範囲（リポジトリの中のフォルダ・ファイルの種類・除外・明示のファイル）</summary>
/// <remarks>
/// 列挙は <see cref="SourceFiles.Enumerate"/> と同じ（<c>bin</c> / <c>obj</c> を除く）。除外は根からの相対パスの接頭辞
/// （大文字小文字を区別しない）で、除外ごとに理由を持つ（「QuickER.Platform の外」のような範囲の形を決めるもの。範囲の中の
/// 正当な出現は規則の既知の一覧に置く）。<see cref="Only"/> を持つ範囲は、そのファイルだけを読む。<see cref="Areas"/> を持つ範囲は、
/// 根からの相対パスがどれかの接頭辞で始まるファイルだけを読む（同じ名前の前置きを持つ複数のプロジェクト＝<c>QuickER.Provider</c> と
/// <c>QuickER.Provider.*</c> を 1 つの範囲にする）。
/// </remarks>
/// <param name="Root">リポジトリのルートからのフォルダ（区切りは <c>/</c>。例 <c>src</c>・<c>tests/QuickER.Tests</c>）</param>
/// <param name="Extension">読むファイルの拡張子（例 <c>.cs</c>・<c>.xaml</c>）</param>
/// <param name="Only">読むファイルを限るときの、リポジトリのルートからの相対パス（空なら根の下の全部）</param>
/// <param name="Exclusions">除外（根からの相対パスの接頭辞と理由）</param>
internal sealed record SourceScope(
    string Root,
    string Extension,
    IReadOnlyList<string> Only,
    IReadOnlyList<ScopeExclusion> Exclusions
)
{
    /// <summary>フォルダの下の、拡張子が同じファイルの全部</summary>
    /// <param name="root">リポジトリのルートからのフォルダ</param>
    /// <param name="extension">拡張子（既定は C# のソース）</param>
    public static SourceScope Under(string root, string extension = ".cs") =>
        new(root, extension, [], []);

    /// <summary>明示のファイルだけ（リポジトリのルートからの相対パス。すべて <paramref name="root"/> の下にあること）</summary>
    /// <param name="root">ファイルを列挙するフォルダ</param>
    /// <param name="paths">読むファイル</param>
    public static SourceScope Files(string root, params string[] paths)
    {
        var extension = Path.GetExtension(paths[0]);

        if (
            paths.Any(path =>
                !path.StartsWith(root + "/", StringComparison.Ordinal)
                || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new ArgumentException(
                $"明示のファイルは {root} の下の、拡張子が同じファイルにする: {string.Join(", ", paths)}"
            );
        }

        return new(root, extension, paths, []);
    }

    /// <summary>読む領域（根からの相対パスの接頭辞・大文字小文字を区別しない。空なら根の下の全部）</summary>
    public IReadOnlyList<string> Areas { get; init; } = [];

    /// <summary>根からの相対パスの接頭辞で読む領域を限った範囲（重ねて呼ぶと、どれかの領域に入るファイルを読む）</summary>
    /// <param name="areaPrefix">根からの相対パスの接頭辞（例 <c>QuickER.Provider</c>）</param>
    public SourceScope Within(string areaPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaPrefix);
        return this with { Areas = [.. Areas, areaPrefix] };
    }

    /// <summary>根からの相対パスの接頭辞で除外した範囲</summary>
    /// <param name="areaPrefix">根からの相対パスの接頭辞（例 <c>QuickER.Platform/</c>）</param>
    /// <param name="reason">除外する理由</param>
    public SourceScope Except(string areaPrefix, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return this with { Exclusions = [.. Exclusions, new ScopeExclusion(areaPrefix, reason)] };
    }

    /// <summary>リポジトリのルートからの相対パス（区切りは <c>/</c>）のファイルが範囲に入るか</summary>
    public bool Contains(string relativePath)
    {
        if (
            !relativePath.StartsWith(Root + "/", StringComparison.Ordinal)
            || !relativePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var area = relativePath[(Root.Length + 1)..];

        if (SourceFiles.IsBuildOutput(area))
        {
            return false;
        }

        if (Only.Count > 0 && !Only.Contains(relativePath, StringComparer.Ordinal))
        {
            return false;
        }

        if (
            Areas.Count > 0
            && !Areas.Any(prefix => area.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        )
        {
            return false;
        }

        return !Exclusions.Any(exclusion =>
            area.StartsWith(exclusion.AreaPrefix, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>範囲の表記（報告用）</summary>
    public override string ToString() =>
        Only.Count > 0
            ? string.Join(", ", Only)
            : $"{Root}/**/*{Extension}"
                + (Areas.Count > 0 ? $" (within {string.Join(", ", Areas)})" : "")
                + string.Concat(Exclusions.Select(exclusion => $" - {exclusion.AreaPrefix}"));
}

/// <summary>範囲の除外（根からの相対パスの接頭辞と理由）</summary>
/// <param name="AreaPrefix">根からの相対パスの接頭辞（大文字小文字を区別しない）</param>
/// <param name="Reason">除外する理由</param>
internal sealed record ScopeExclusion(string AreaPrefix, string Reason);
