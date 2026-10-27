using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// テストの DbContext が、内部のサービスプロバイダの数の警告で失敗しないことを固定するテストクラス
/// （<see cref="EfCoreTestOptions"/>）。
/// </summary>
public partial class EfCoreServiceProviderLimitGuardTests
{
    /// <summary>
    /// 何もしないインターセプター。具体化（materialization）のインターセプターはシングルトンのインターセプターで、
    /// インスタンスごとに内部のサービスプロバイダが別になる（コマンドのインターセプターは別にならない）。
    /// </summary>
    private sealed class NoopInterceptor : IMaterializationInterceptor;

    [Fact(
        DisplayName = "EF Core: 内部のサービスプロバイダを 21 個以上作っても、共通の設定を通した DbContext は失敗しない"
    )]
    public void ManyServiceProviders_DoNotFailTheContext()
    {
        for (var i = 0; i < 25; i++)
        {
            var options = new DbContextOptionsBuilder()
                .UseSqlite("Data Source=:memory:")
                .IgnoreServiceProviderCacheLimit()
                .AddInterceptors(new NoopInterceptor())
                .Options;
            using var context = new DbContext(options);

            var act = () => context.Model;

            act.Should().NotThrow($"{i + 1} 個目の内部のサービスプロバイダ");
        }
    }

    /// <summary>DbContext のプロバイダを指定する呼び出し</summary>
    [GeneratedRegex(@"\.Use(Sqlite|SqlServer|Npgsql|Oracle|MySQL|MySql)\(")]
    private static partial Regex ProviderCall();

    [Fact(
        DisplayName = "テストのソースで、プロバイダを指定する呼び出しの直後には必ず共通の設定が続く"
    )]
    public void EveryProviderCall_IsFollowedByTheSharedOptions()
    {
        var testsRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")
        );
        Directory.Exists(Path.Combine(testsRoot, "QuickER.Tests")).Should().BeTrue(testsRoot);

        var violations = new List<string>();
        var checkedCalls = 0;

        foreach (
            var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            if (
                SourceFiles.IsBuildOutput(file[testsRoot.Length..])
                || file.EndsWith(".g.cs", StringComparison.Ordinal)
                || Path.GetFileName(file) == "EfCoreTestOptions.cs"
                || Path.GetFileName(file) == "EfCoreServiceProviderLimitGuardTests.cs"
            )
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (Match match in ProviderCall().Matches(text))
            {
                checkedCalls++;
                var close = MatchingParenthesis(text, match.Index + match.Length - 1);
                var rest = text[(close + 1)..].TrimStart();

                if (
                    !rest.StartsWith(".IgnoreServiceProviderCacheLimit()", StringComparison.Ordinal)
                )
                {
                    var line = text[..match.Index].Count(c => c == '\n') + 1;
                    violations.Add($"{file[testsRoot.Length..]}:{line}");
                }
            }
        }

        checkedCalls.Should().BeGreaterThan(40, "走査がテストのソースに届いていること");
        violations
            .Should()
            .BeEmpty("プロバイダの指定の直後に .IgnoreServiceProviderCacheLimit() を続けること");
    }

    /// <summary>開き括弧の位置から、対応する閉じ括弧の位置を返す（文字列・文字のリテラルの中の括弧は数えない）</summary>
    private static int MatchingParenthesis(string text, int open)
    {
        var depth = 0;

        for (var i = open; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '"':
                    i = SkipLiteral(text, i, '"');
                    break;
                case '\'':
                    i = SkipLiteral(text, i, '\'');
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;

                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        throw new InvalidOperationException($"閉じ括弧が見つからない（位置 {open}）");
    }

    /// <summary>リテラルの終わりの位置を返す（逐語的文字列の "" と、通常のリテラルの \ のエスケープを扱う）</summary>
    private static int SkipLiteral(string text, int start, char quote)
    {
        var verbatim = quote == '"' && start > 0 && text[start - 1] == '@';

        for (var i = start + 1; i < text.Length; i++)
        {
            if (!verbatim && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == quote)
                {
                    i++;
                }
                else
                {
                    return i;
                }
            }
        }

        return text.Length - 1;
    }
}
