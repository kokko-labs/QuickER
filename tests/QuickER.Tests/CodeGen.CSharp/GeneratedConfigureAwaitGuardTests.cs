using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コード（フィクスチャ・ランタイムパッケージ用ソース・サンプルの生成物）の <c>await</c> がすべて
/// <c>.ConfigureAwait(false)</c> 付きであることを検証するガード。
/// </summary>
/// <remarks>
/// <para>
/// 生成コードはライブラリとして利用者のアプリへ組み込まれる（ランタイムパッケージは NuGet で出荷される）ため、
/// 同期コンテキストへ戻る必要がない。テンプレートに <c>await</c> を足したとき付け忘れると、UI スレッドを
/// 持つアプリでデッドロックや不要なコンテキスト復帰を招くが、コンパイルは通るためビルドでは検出できない。
/// </para>
/// <para>
/// <c>await using</c> はリポジトリの手書きコード（550 箇所超）と同じ流儀で対象外とする
/// （<c>ConfiguredAsyncDisposable</c> へ包むと変数宣言を分割する必要があり、生成コードの可読性を大きく損なうため）。
/// </para>
/// <para>
/// 判定は「<c>await</c> から、括弧の入れ子深さ 0 で現れる <c>;</c> または閉じ括弧までの範囲」に
/// <c>.ConfigureAwait(false)</c> が含まれるか、という単純な走査で行う（ラムダ本体の <c>{ }</c> や
/// パターンの <c>{ }</c> は括弧深さで吸収される）。
/// </para>
/// </remarks>
public class GeneratedConfigureAwaitGuardTests
{
    /// <summary>await キーワード（識別子の一部ではなく、直後に空白が続くもの）</summary>
    private static readonly Regex AwaitKeyword = new(
        @"(?<![A-Za-z0-9_])await\s",
        RegexOptions.Compiled
    );

    /// <summary>検査対象のチェックイン済み生成物（リポジトリ直下からの相対パス）</summary>
    public static TheoryData<string> GeneratedFiles =>
        [
            "src/QuickER.Runtime/Runtime.g.cs",
            "src/QuickER.Runtime.SqlServer/Runtime.SqlServer.g.cs",
            "src/QuickER.Runtime.Sqlite/Runtime.Sqlite.g.cs",
            "src/QuickER.Runtime.EntityFrameworkCore/Runtime.EntityFrameworkCore.g.cs",
            "src/QuickER.Runtime.InMemory/Runtime.InMemory.g.cs",
            "src/QuickER.Runtime.AspNetCore/Runtime.AspNetCore.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/GeneratedFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/PortableFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/SqlitePortableFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/MultiTargetPortableFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/InMemoryFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/QueryFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/RemoteContractFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/RemoteServiceFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/RemoteServiceFixture.RemoteServer.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/BinaryFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/BinaryFixture.RemoteServer.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/SqlServerBinaryFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/ConcurrencyFixture.g.cs",
            "tests/QuickER.Tests/GeneratedFixture/ConcurrencyFixture.RemoteServer.g.cs",
            "samples/ec-order/EcOrderSample/Generated/EcOrder.g.cs",
            "samples/ec-order-remote/Generated/EcOrderRemote.g.cs",
            "samples/ec-order-remote/Generated/EcOrderRemote.RemoteServer.g.cs",
        ];

    /// <summary>生成物のすべての await が ConfigureAwait(false) 付きである（await using は対象外）</summary>
    [Theory(DisplayName = "生成コードの await はすべて ConfigureAwait(false) 付き")]
    [MemberData(nameof(GeneratedFiles))]
    public void 生成コードのawaitはConfigureAwait付き(string repoRelativePath)
    {
        var content = File.ReadAllText(ResolveRepoRelativePath(repoRelativePath));
        var offenders = FindBareAwaits(content);

        offenders
            .Should()
            .BeEmpty(
                $"{repoRelativePath} の await は生成ライブラリのため ConfigureAwait(false) が必要: "
                    + string.Join(" / ", offenders)
            );
    }

    /// <summary>await 式の直後に ConfigureAwait(false) が無い箇所（await using を除く）を列挙する</summary>
    private static List<string> FindBareAwaits(string content)
    {
        var offenders = new List<string>();

        foreach (Match match in AwaitKeyword.Matches(content))
        {
            var rest = content.AsSpan(match.Index + match.Length);

            // await using は対象外（手書きコードと同じ流儀）。await foreach は WithCancellation 側で扱う
            if (rest.StartsWith("using") || rest.StartsWith("foreach"))
            {
                continue;
            }

            var statement = AwaitedExpression(content, match.Index);

            if (statement.Contains(".ConfigureAwait(false)", StringComparison.Ordinal))
            {
                continue;
            }

            var line = content.Take(match.Index).Count(c => c == '\n') + 1;
            offenders.Add($"L{line}: {Collapse(statement)}");
        }

        return offenders;
    }

    /// <summary>await から、括弧深さ 0 の <c>;</c> または閉じ括弧までを切り出す</summary>
    private static string AwaitedExpression(string text, int start)
    {
        var depth = 0;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (c is '(' or '[' or '{')
            {
                depth++;
                continue;
            }

            if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    return text[start..i];
                }

                depth--;
                continue;
            }

            if (depth == 0 && c == ';')
            {
                return text[start..i];
            }
        }

        return text[start..];
    }

    /// <summary>失敗メッセージ用に空白を 1 つへ畳む</summary>
    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>リポジトリ直下（QuickER.slnx の位置）からの相対パスを解決する</summary>
    private static string ResolveRepoRelativePath(string repoRelativePath)
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return Path.Combine(
                    dir.FullName,
                    repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
                );
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"リポジトリ直下（QuickER.slnx）が見つからず {repoRelativePath} を解決できませんでした。"
        );
    }
}
