using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.Documents;
using QuickER.Model;
using Xunit;
using CliStrings = QuickER.Cli.Resources.Strings;

namespace QuickER.Tests.Cli;

/// <summary>
/// MCP のコード生成ツールの結果が、呼び出し元の UI 言語に依らず英語になることと、
/// その固定が呼び出しの外（呼び出し元・並行する別の呼び出し）へ漏れないことを検証する。
/// </summary>
/// <remarks>
/// 生成コアは CLI と resx（生成ファイルの一覧・件数・生成器の診断）を共有するため、固定しないと
/// 日本語の OS では英語の枠文に日本語の行が混ざる。固定はツールの実行中だけ行い、終われば元へ戻す。
/// </remarks>
public class CodeGenToolSetLanguageTests
{
    /// <summary>CJK 文字の検出パターン（CodeGenToolSetEnglishGuardTests と同じ範囲）</summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>日本語の UI 言語で呼んでも結果は英語で、呼び出し後は呼び出し元の UI 言語が元のまま</summary>
    [Fact(
        DisplayName = "generate_csharp の結果は日本語の UI 言語で呼んでも英語で、UI 言語は元へ戻る"
    )]
    public void GenerateCSharp_UnderJapaneseUICulture_ReturnsEnglishAndRestoresCulture()
    {
        // 前提: 日本語の resx は実際に日本語（そうでなければこのテストは何も確かめていない）
        CjkPattern
            .IsMatch(
                CliStrings.ResourceManager.GetString(
                    nameof(CliStrings.Cli_GeneratedFile),
                    Japanese
                )!
            )
            .Should()
            .BeTrue();

        var root = CreateRoot();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = Japanese;

            var (result, success) = GenerateCSharp(root, "out");

            CultureInfo.CurrentUICulture.Should().Be(Japanese, "呼び出し元の UI 言語は戻す");
            success.Should().BeTrue(result);
            result.Should().Contain("Generated: ");
            CjkPattern.IsMatch(result).Should().BeFalse("結果は英語に統一する: " + result);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            Cleanup(root);
        }
    }

    /// <summary>
    /// 並行に届いた呼び出しが互いの UI 言語を書き換えない。各呼び出しの結果は英語で、各呼び出し元の UI 言語は
    /// 呼び出しの前後で変わらず、ツールを呼ばずに並走しているスレッドの UI 言語も一度も変わらない
    /// </summary>
    /// <remarks>
    /// 呼び出し元の言語は日本語とフランス語を交互にする（固定が呼び出しの外へ漏れて別の呼び出しの言語へ
    /// 書き戻されると、日本語の行が混ざるか、呼び出し後の言語が食い違う）。開始は Barrier でそろえ、
    /// 生成（図の読み取り以外はロックの外）が実際に重なるようにする。Barrier で待つため、スレッドプールを
    /// 詰まらせないよう各呼び出しは専用スレッド（LongRunning）で動かす。
    /// </remarks>
    [Fact(DisplayName = "generate_csharp の並行呼び出しは互いの UI 言語を書き換えない")]
    public async Task GenerateCSharp_ConcurrentCalls_DoNotAffectEachOthersUICulture()
    {
        const int callers = 8;
        var root = CreateRoot();
        var french = CultureInfo.GetCultureInfo("fr-FR");
        var failures = new ConcurrentQueue<string>();
        using var barrier = new Barrier(callers + 1);
        using var stop = new CancellationTokenSource();

        try
        {
            // ツールを呼ばずに並走し、自分の UI 言語を読み続けるスレッド
            var bystander = Task.Factory.StartNew(
                () =>
                {
                    CultureInfo.CurrentUICulture = Japanese;
                    barrier.SignalAndWait();

                    while (!stop.IsCancellationRequested)
                    {
                        if (!Equals(CultureInfo.CurrentUICulture, Japanese))
                        {
                            failures.Enqueue($"bystander saw {CultureInfo.CurrentUICulture.Name}");
                        }
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );

            var calls = Enumerable
                .Range(0, callers)
                .Select(index =>
                    Task.Factory.StartNew(
                        () =>
                        {
                            var own = index % 2 == 0 ? Japanese : french;
                            CultureInfo.CurrentUICulture = own;
                            barrier.SignalAndWait();

                            var (result, success) = GenerateCSharp(root, $"out{index}");

                            if (!Equals(CultureInfo.CurrentUICulture, own))
                            {
                                failures.Enqueue(
                                    $"call {index}: culture after call was {CultureInfo.CurrentUICulture.Name}, expected {own.Name}"
                                );
                            }

                            if (!success || CjkPattern.IsMatch(result))
                            {
                                failures.Enqueue($"call {index}: {result}");
                            }
                        },
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default
                    )
                )
                .ToList();

            await Task.WhenAll(calls);
            await stop.CancelAsync();
            await bystander;

            failures.Should().BeEmpty();
        }
        finally
        {
            Cleanup(root);
        }
    }

    /// <summary>作業フォルダに SQLite 方言の図を 1 つ置き、そのフォルダを返す</summary>
    private static string CreateRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERCodeGenToolLanguage",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);

        var document = new DiagramDocument { Schema = { TargetDbms = "sqlite" } };
        var entity = new Entity { TableName = "Customer" };
        entity.Columns.Add(
            new Column
            {
                Name = "Id",
                DataType = "INTEGER",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(new Column { Name = "Name", DataType = "TEXT" });
        document.Schema.Entities.Add(entity);
        JsonStorageService.Save(Path.Combine(root, "diagram.json"), document);

        return root;
    }

    /// <summary>図を作業フォルダ配下の指定フォルダへ generate_csharp で生成する</summary>
    private static (string Result, bool Success) GenerateCSharp(string root, string outName) =>
        CodeGenToolSet
            .Create()
            .Execute(
                "generate_csharp",
                JsonSerializer.Serialize(
                    new
                    {
                        file = Path.Combine(root, "diagram.json"),
                        out_dir = Path.Combine(root, outName),
                    }
                )
            );

    private static void Cleanup(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // 一時フォルダの後始末の失敗は検証の結果に関係しない
        }
    }
}
