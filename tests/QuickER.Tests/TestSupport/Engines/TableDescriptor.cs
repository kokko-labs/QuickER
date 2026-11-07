using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// 単体の表の台帳の 1 行（エンジンの台帳〔<see cref="EngineDescriptor"/>〕に載らない、決まった形の行の表とその対象の範囲）
/// </summary>
/// <remarks>
/// 影響の地図（<c>impact-map.md</c>）は、エンジンの部分と同じように、この行の <paramref name="Covers"/> を守りの範囲として読む。
/// 地図の名前は <c>table:{Name}</c>。宣言はアセンブリごとの台帳（<c>TableRegistry</c>）の 1 ファイルに並べ、表のファイルには置かない
/// （表のファイルは行を足す区切りが並行して直すので、宣言を同じファイルに置くとぶつかる）。
/// </remarks>
/// <param name="Name">表の名前（台帳の中で一意・英字で始まる英数字）</param>
/// <param name="Title">表示の名前</param>
/// <param name="SourceFiles">テストのソース（リポジトリの根からの相対パス・区切りは <c>/</c>・1 つ以上）</param>
/// <param name="Covers">対象の src の範囲（リポジトリの根からの相対パス・ファイルかフォルダ・1 つ以上・<c>src/</c> の下）</param>
public sealed record TableDescriptor(
    string Name,
    string Title,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Covers
);

/// <summary>単体の表の台帳の整合の検査</summary>
public static class TableRegistryChecks
{
    /// <summary>表の名前の形（英字で始まる英数字）</summary>
    private static readonly Regex NamePattern = new(
        "^[A-Za-z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant
    );

    /// <summary>台帳の問題をすべて並べる（無ければ空）</summary>
    /// <param name="tables">台帳</param>
    /// <param name="repositoryRoot">リポジトリの根（パスの存在を確かめる）</param>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<TableDescriptor> tables,
        string repositoryRoot
    )
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in tables)
        {
            if (table is null)
            {
                problems.Add("null の行がある");
                continue;
            }

            var name = $"table:{table.Name}";

            if (table.Name is null || !NamePattern.IsMatch(table.Name))
            {
                problems.Add($"{name}: 名前が英字で始まる英数字でない");
            }
            else if (!names.Add(table.Name))
            {
                problems.Add($"{name}: 名前が重複している");
            }

            if (string.IsNullOrWhiteSpace(table.Title))
            {
                problems.Add($"{name}: 表示の名前が空");
            }

            if (table.SourceFiles is null || table.SourceFiles.Count == 0)
            {
                problems.Add($"{name}: テストのソースが無い");
            }

            if (table.Covers is null || table.Covers.Count == 0)
            {
                problems.Add($"{name}: 対象の範囲が無い");
            }
            else
            {
                foreach (
                    var cover in table.Covers.Where(cover =>
                        cover is not null && !cover.StartsWith("src/", StringComparison.Ordinal)
                    )
                )
                {
                    problems.Add($"{name}: 対象の範囲の {cover} が src/ の下でない");
                }
            }

            EngineRegistryChecks.CheckPaths(
                name,
                "テストのソース",
                table.SourceFiles,
                repositoryRoot,
                problems
            );
            EngineRegistryChecks.CheckPaths(
                name,
                "対象の範囲",
                table.Covers,
                repositoryRoot,
                problems
            );
        }

        return problems;
    }

    /// <summary>台帳に問題があれば、すべて並べて赤にする</summary>
    /// <param name="tables">台帳</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static void Verify(IReadOnlyList<TableDescriptor> tables, string repositoryRoot)
    {
        var problems = Problems(tables, repositoryRoot);

        if (problems.Count > 0)
        {
            throw new XunitException(
                "単体の表の台帳が整っていない:\n  " + string.Join("\n  ", problems)
            );
        }
    }
}
