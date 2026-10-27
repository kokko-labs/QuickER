using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>テストのエンジンの ID（台帳・次元表・既知の失敗の一覧が共有する正本）</summary>
public static class EngineIds
{
    /// <summary>同期のモデル</summary>
    public const string SyncModel = "SyncModel";

    /// <summary>同期計画</summary>
    public const string SyncPlan = "SyncPlan";

    /// <summary>方言</summary>
    public const string Dialect = "Dialect";

    /// <summary>生成コードのランタイム</summary>
    public const string GeneratedRuntime = "GeneratedRuntime";

    /// <summary>コード生成の設定</summary>
    public const string CodegenConfig = "CodegenConfig";

    /// <summary>簡易 DSL</summary>
    public const string Dsl = "Dsl";

    /// <summary>名前</summary>
    public const string Name = "Name";

    /// <summary>図の編集</summary>
    public const string DiagramEdit = "DiagramEdit";

    /// <summary>形式の往復</summary>
    public const string FormatRoundTrip = "FormatRoundTrip";

    /// <summary>図のファイル</summary>
    public const string DocumentFile = "DocumentFile";

    /// <summary>AI</summary>
    public const string Ai = "Ai";

    /// <summary>OS との境界</summary>
    public const string OsBoundary = "OsBoundary";

    /// <summary>ガード</summary>
    public const string Guard = "Guard";

    /// <summary>すべての ID（宣言の順）</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SyncModel,
        SyncPlan,
        Dialect,
        GeneratedRuntime,
        CodegenConfig,
        Dsl,
        Name,
        DiagramEdit,
        FormatRoundTrip,
        DocumentFile,
        Ai,
        OsBoundary,
        Guard,
    ];
}

/// <summary>エンジンの台帳の 1 行（エンジンの部分ごとの次元表・シナリオの数・テストのソース・対象の範囲）</summary>
/// <param name="Id">エンジンの ID（<see cref="EngineIds"/> のどれか）</param>
/// <param name="Part">エンジンの部分（入口・方言など。ID の中で一意）</param>
/// <param name="Title">表示の名前</param>
/// <param name="Dimensions">次元表（<see cref="DimensionTable.EngineId"/> は <paramref name="Id"/> と同じ）</param>
/// <param name="ScenarioCount">決定的なシナリオの数</param>
/// <param name="SourceFiles">テストのソース（リポジトリの根からの相対パス・区切りは <c>/</c>・1 つ以上）</param>
/// <param name="Covers">対象の src の範囲（リポジトリの根からの相対パス・ファイルかフォルダ）</param>
public sealed record EngineDescriptor(
    string Id,
    string Part,
    string Title,
    DimensionTable Dimensions,
    int ScenarioCount,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Covers
);

/// <summary>エンジンの台帳の整合の検査</summary>
public static class EngineRegistryChecks
{
    /// <summary>台帳の問題をすべて並べる（無ければ空）</summary>
    /// <param name="descriptors">台帳</param>
    /// <param name="repositoryRoot">リポジトリの根（パスの存在を確かめる）</param>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<EngineDescriptor> descriptors,
        string repositoryRoot
    )
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var problems = new List<string>();
        var keys = new HashSet<(string, string)>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor is null)
            {
                problems.Add("null の行がある");
                continue;
            }

            var name = $"{descriptor.Id}/{descriptor.Part}";

            if (!EngineIds.All.Contains(descriptor.Id, StringComparer.Ordinal))
            {
                problems.Add($"{name}: 知らないエンジンの ID {descriptor.Id}");
            }

            if (string.IsNullOrWhiteSpace(descriptor.Part))
            {
                problems.Add($"{name}: 部分が空");
            }

            if (string.IsNullOrWhiteSpace(descriptor.Title))
            {
                problems.Add($"{name}: 表示の名前が空");
            }

            if (
                descriptor.Id is not null
                && descriptor.Part is not null
                && !keys.Add((descriptor.Id, descriptor.Part))
            )
            {
                problems.Add($"{name}: エンジンの ID と部分の組が重複している");
            }

            if (descriptor.Dimensions is null)
            {
                problems.Add($"{name}: 次元表が無い");
            }
            else if (
                !string.Equals(
                    descriptor.Dimensions.EngineId,
                    descriptor.Id,
                    StringComparison.Ordinal
                )
            )
            {
                problems.Add(
                    $"{name}: 次元表のエンジンの ID {descriptor.Dimensions.EngineId} が台帳の ID と違う"
                );
            }

            if (descriptor.ScenarioCount < 0)
            {
                problems.Add($"{name}: シナリオの数が負");
            }

            if (descriptor.SourceFiles is null || descriptor.SourceFiles.Count == 0)
            {
                problems.Add($"{name}: テストのソースが無い");
            }

            CheckPaths(name, "テストのソース", descriptor.SourceFiles, repositoryRoot, problems);
            CheckPaths(name, "対象の範囲", descriptor.Covers, repositoryRoot, problems);
        }

        return problems;
    }

    /// <summary>台帳に問題があれば、すべて並べて赤にする</summary>
    /// <param name="descriptors">台帳</param>
    /// <param name="repositoryRoot">リポジトリの根</param>
    public static void Verify(IReadOnlyList<EngineDescriptor> descriptors, string repositoryRoot)
    {
        var problems = Problems(descriptors, repositoryRoot);

        if (problems.Count > 0)
        {
            throw new XunitException(
                "エンジンの台帳が整っていない:\n  " + string.Join("\n  ", problems)
            );
        }
    }

    private static void CheckPaths(
        string name,
        string what,
        IReadOnlyList<string>? paths,
        string root,
        List<string> problems
    )
    {
        if (paths is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                problems.Add($"{name}: {what}に空のパスがある");
                continue;
            }

            if (!seen.Add(path))
            {
                problems.Add($"{name}: {what}の {path} が重複している");
            }

            if (path.Contains('\\', StringComparison.Ordinal))
            {
                problems.Add($"{name}: {what}の {path} の区切りが / でない");
                continue;
            }

            if (
                Path.IsPathRooted(path)
                || path.StartsWith('/')
                || (path.Length >= 2 && path[1] == ':')
            )
            {
                problems.Add($"{name}: {what}の {path} が相対パスでない");
                continue;
            }

            if (path.Split('/').Any(segment => segment == ".."))
            {
                problems.Add($"{name}: {what}の {path} に .. がある");
                continue;
            }

            var full = Path.Combine(root, path);

            if (!File.Exists(full) && !Directory.Exists(full))
            {
                problems.Add($"{name}: {what}の {path} が無い");
            }
        }
    }
}

/// <summary>リポジトリの根を探す</summary>
public static class EngineRepository
{
    /// <summary>ソリューションのファイル</summary>
    public const string SolutionFileName = "QuickER.slnx";

    /// <summary>テストの実行フォルダから上へたどり、ソリューションのファイルがあるフォルダを返す</summary>
    /// <exception cref="InvalidOperationException">見つからない</exception>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"{AppContext.BaseDirectory} から上に {SolutionFileName} が見つからない"
        );
    }
}
