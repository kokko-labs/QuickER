using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Platform;

/// <summary>
/// アプリ設定・作業状態と一時ファイルの置き場所を <see cref="AppDataPaths"/> だけが決めることを固定する
/// </summary>
/// <remarks>
/// <para>
/// 置き場所の算出が呼び出し側に写されていると、1 か所だけローミング（<c>%APPDATA%</c>）へ戻る・フォルダ名が変わる、
/// のような食い違いが、どのテストにも引っかからずに入る（保存先を差し替えるテストの経路は、既定の保存先を通らない）。
/// </para>
/// <para>
/// 走査で拾う基準：<c>src/</c> の <c>QuickER.Platform</c> 以外の <c>.cs</c>（<c>bin</c> / <c>obj</c> を除く）にある呼び出しのうち、
/// <c>Environment.GetFolderPath</c> の引数が <c>LocalApplicationData</c>・<c>ApplicationData</c>・<c>UserProfile</c> のどれか
/// のもの（保存先になりうるフォルダ。<c>Desktop</c> や <c>MyDocuments</c> のような既定の表示フォルダは保存先の算出ではないので数えない）と、
/// <c>Path.GetTempPath</c> の呼び出し。構文木で数えるので、コメント・XmlDoc の中の言及は数えない。
/// 正当な出現は理由つきで <see cref="Known"/> に載せ、一覧に無い出現も、一覧にあるのに無い出現（件数の食い違いを含む）も赤にする。
/// </para>
/// </remarks>
public sealed class AppDataPathGuardTests
{
    /// <summary>保存先ではなく、子プロセスの作業フォルダとして使う</summary>
    private const string WorkingDirectory = "子プロセスの作業フォルダ（そこへは何も書かない）";

    /// <summary>既知の出現（ファイル・呼び出し・件数・理由）</summary>
    private static readonly (string File, string Call, int Count, string Reason)[] Known =
    [
        (
            "src/QuickER.AI/CodexConfigTomlReader.cs",
            "GetFolderPath(UserProfile)",
            1,
            "利用者のホームにある codex の設定を読む（QuickER の保存先ではない）"
        ),
        ("src/QuickER.AI/ClaudeCodeClient.cs", "GetTempPath", 1, WorkingDirectory),
        ("src/QuickER.AI.Mock/DotnetBuildRunner.cs", "GetTempPath", 2, WorkingDirectory),
        (
            "src/QuickER.Runtime.Sync/Runtime.Sync.g.cs",
            "GetTempPath",
            1,
            "依存ゼロのランタイムのパッケージは QuickER.Platform を参照できない（同期の blob の一時ファイル）"
        ),
    ];

    /// <summary>保存先になりうるフォルダ（数える <c>GetFolderPath</c> の引数）</summary>
    private static readonly HashSet<string> StorageFolders =
    [
        nameof(Environment.SpecialFolder.LocalApplicationData),
        nameof(Environment.SpecialFolder.ApplicationData),
        nameof(Environment.SpecialFolder.UserProfile),
    ];

    /// <summary>アプリ設定・作業状態の保存先は %LOCALAPPDATA%\QuickER で、ローミングの配下ではない</summary>
    [Fact(
        DisplayName = "LocalRoot は %LOCALAPPDATA%\\QuickER で、ローミング（%APPDATA%）の配下ではない"
    )]
    public void LocalRoot_IsUnderLocalApplicationData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        AppDataPaths.LocalRoot.Should().Be(Path.Combine(local, "QuickER"));
        AppDataPaths
            .LocalRoot.Should()
            .NotStartWith(
                roaming + Path.DirectorySeparatorChar,
                "機械固有の作業状態と DPAPI の秘密はローミングで他端末へ複製しない"
            );
    }

    /// <summary>一時ファイルの置き場所は %TEMP%\QuickER</summary>
    [Fact(DisplayName = "TempRoot は %TEMP%\\QuickER")]
    public void TempRoot_IsUnderTempPath()
    {
        AppDataPaths.TempRoot.Should().Be(Path.Combine(Path.GetTempPath(), "QuickER"));
    }

    /// <summary>置き場所の算出は QuickER.Platform の外では理由つきの一覧にあるものだけ</summary>
    [Fact(
        DisplayName = "保存先・一時フォルダの算出は、QuickER.Platform の外では理由つきの一覧にあるものだけ"
    )]
    public void FolderLookups_OutsidePlatform_AreOnlyKnown()
    {
        var root = NeutralResxFiles.FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var platform = Path.Combine(src, "QuickER.Platform") + Path.DirectorySeparatorChar;
        var found = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(platform, StringComparison.OrdinalIgnoreCase))
            .Where(path =>
                !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj")
            )
            .SelectMany(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var variants = ConditionalSourceParse.Parse(
                    File.ReadAllText(path),
                    cancellationToken: TestContext.Current.CancellationToken
                );

                return ConditionalSourceParse
                    .DescendantNodes(variants, TestContext.Current.CancellationToken)
                    .OfType<InvocationExpressionSyntax>()
                    .Select(Describe)
                    .OfType<string>()
                    .Select(call => (File: relative, Call: call));
            })
            .GroupBy(hit => hit)
            .ToDictionary(group => group.Key, group => group.Count());

        var expected = Known.ToDictionary(known => (known.File, known.Call), known => known.Count);

        var unexpected = found
            .Where(pair => !expected.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: {pair.Key.Call} x{pair.Value}")
            .ToList();
        var stale = expected
            .Where(pair => !found.TryGetValue(pair.Key, out var count) || count != pair.Value)
            .Select(pair => $"{pair.Key.File}: {pair.Key.Call} x{pair.Value}")
            .ToList();

        unexpected
            .Should()
            .BeEmpty(
                "保存先・一時フォルダの算出は AppDataPaths を使い、例外は理由つきで一覧に載せる"
            );
        stale.Should().BeEmpty("無くなった出現は一覧から消す");
    }

    /// <summary>数える呼び出しなら、その表記（例 <c>GetFolderPath(UserProfile)</c>）を返す</summary>
    private static string? Describe(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var name = member.Name.Identifier.ValueText;

        if (name == nameof(Path.GetTempPath))
        {
            return name;
        }

        if (name != nameof(Environment.GetFolderPath))
        {
            return null;
        }

        var folder = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => null,
        };

        return folder is not null && StorageFolders.Contains(folder) ? $"{name}({folder})" : null;
    }
}
