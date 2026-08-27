using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Cli;

namespace QuickER.Tests.Samples;

/// <summary>実 CLI が書き出した生成ファイル 1 件（書き出し順で並ぶ）</summary>
/// <param name="FileName">出力ディレクトリ直下のファイル名</param>
/// <param name="Content">書き出された内容（読み戻した文字列。バイト比較の基準）</param>
public sealed record GeneratedSampleFile(string FileName, string Content);

/// <summary>
/// samples/ 配下のドリフト検知テストが共有する「実 CLI でサンプルを再生成する」ヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// 設定ローダーの挙動（<c>RepositoryDialects</c> の補完・<c>OutputPath</c> → <c>OutputFileName</c> の導出・
/// 未知キー警告）をテスト側へ写さず、<see cref="CliApp.InvokeAsync(string[], TextWriter, TextWriter)"/> を
/// そのまま呼ぶ。鏡実装だとローダーを変えてもドリフトテストは緑のままで、「チェックイン済み生成物は実 CLI が
/// 生成したものと同一」という主張が検証されなくなるため。
/// </para>
/// <para>
/// 実行コマンドは各サンプルの README が案内する再生成コマンドと同じ形
/// （<c>generate --schema … --out … --provider sqlite --config …/quicker.json</c>）。
/// 出力先だけを一時フォルダへ差し替え、書き出された内容を呼び出し側へ返す。
/// </para>
/// </remarks>
internal static class SampleCliRunner
{
    /// <summary>
    /// サンプルを実 CLI で一時フォルダへ生成し、書き出されたファイルを<b>書き出し順</b>で返す。
    /// </summary>
    /// <param name="sampleDir">サンプルディレクトリ（リポジトリ相対。例 <c>samples/ec-order</c>）</param>
    /// <param name="schemaFileName">サンプルディレクトリ直下の図 JSON のファイル名</param>
    /// <param name="expectedFileNames">出力されるべきファイル名の一覧（出力構成そのものもドリフト検知の対象）</param>
    /// <param name="orderSensitive">
    /// <c>true</c> なら <paramref name="expectedFileNames"/> と書き出し順まで一致すること要求する
    /// （本体＋サーバーの 2 ファイル構成など、順序に意味があるサンプル向け）
    /// </param>
    /// <param name="extraArgs">追加の CLI フラグ（例 <c>--generate-api-docs</c>）</param>
    public static async Task<IReadOnlyList<GeneratedSampleFile>> GenerateAsync(
        string sampleDir,
        string schemaFileName,
        IReadOnlyList<string> expectedFileNames,
        bool orderSensitive,
        params string[] extraArgs
    )
    {
        var outDir = Path.Combine(
            Path.GetTempPath(),
            "QuickERSampleDrift",
            Guid.NewGuid().ToString("N")
        );

        try
        {
            string[] args =
            [
                "generate",
                "--schema",
                ResolveRepoRelativePath(sampleDir + "/" + schemaFileName),
                "--out",
                outDir,
                // 2 サンプルとも SQLite 方言（README の再生成コマンドと同じ指定）
                "--provider",
                "sqlite",
                "--config",
                ResolveRepoRelativePath(sampleDir + "/quicker.json"),
                .. extraArgs,
            ];

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            // コンソールの出力エンコーディングを触らない注入版を使う（並列実行中の他テストと競合しない）
            var exitCode = await CliApp.InvokeAsync(args, stdout, stderr);

            exitCode
                .Should()
                .Be(
                    0,
                    $"サンプル {sampleDir} の実 CLI 生成は成功しなければならない。"
                        + $"stderr: {stderr}"
                );

            var written = WrittenFileNames(stdout.ToString(), outDir);

            if (orderSensitive)
            {
                written
                    .Should()
                    .Equal(
                        expectedFileNames,
                        "出力ファイル構成（順序込み）そのものもドリフト検知の対象とする"
                    );
            }
            else
            {
                written
                    .Should()
                    .BeEquivalentTo(
                        expectedFileNames,
                        "出力ファイル構成そのものもドリフト検知の対象とする"
                    );
            }

            return written
                .Select(fileName => new GeneratedSampleFile(
                    fileName,
                    File.ReadAllText(Path.Combine(outDir, fileName))
                ))
                .ToList();
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// 出力ディレクトリに実際に書き出されたファイルを、<b>書き出し順</b>で返す。
    /// </summary>
    /// <remarks>
    /// ファイルの実体はディスクから、順序は CLI の標準出力から取る。CLI は書き出したファイルのパスを
    /// 1 行 1 件・書き出し順（＝生成結果のファイル順）で出力するため、名前が最初に現れた位置で並べれば
    /// 生成順が復元できる（見出し語はローカライズされるため文言には依存しない）。
    /// </remarks>
    private static IReadOnlyList<string> WrittenFileNames(string stdout, string outDir) =>
        Directory
            .GetFiles(outDir, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(fileName => stdout.IndexOf(fileName, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// リポジトリ直下（<c>QuickER.slnx</c> を目印）からの相対パスを絶対パスへ解決する。
    /// </summary>
    public static string ResolveRepoRelativePath(string repoRelativePath)
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
