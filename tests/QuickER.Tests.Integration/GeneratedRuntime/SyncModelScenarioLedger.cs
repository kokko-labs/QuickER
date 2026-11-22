using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの決定的なシナリオを 1 つの端末の作り方で全部流し、行ごとの結果を控え（Markdown の表）にする部品。
/// 新しいエンジンの端末の控えと、今のエンジンの端末の控えが使う。
/// </summary>
/// <remarks>
/// 結果は <see cref="Green"/>（違反が無い。判定役の対照の行は出た署名の集合が宣言と一致）・<see cref="Known"/>（既知の失敗の一覧を
/// 渡した控えだけ。出た署名の組が一覧のその行の組どおり）・<see cref="Unsupported"/>（「流せない」の例外で終わった。詳細は手の名前）・
/// <see cref="Differs"/>（それ以外。詳細は署名を序数の順に並べたものか、例外の型と短い要約）。
/// </remarks>
internal static class SyncModelScenarioLedger
{
    /// <summary>違反が無い</summary>
    public const string Green = "green";

    /// <summary>既知の失敗の一覧のその行の署名の組どおりに失敗した</summary>
    public const string Known = "known";

    /// <summary>「流せない」の例外で終わった</summary>
    public const string Unsupported = "unsupported";

    /// <summary>期待と違う</summary>
    public const string Differs = "differs";

    /// <summary>シナリオ 1 本を流し、結果と詳細を返す</summary>
    /// <param name="scenario">シナリオ</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="options">世界の組み方（端末の作り方を含む）</param>
    /// <param name="known">
    /// 既知の失敗の一覧（渡すと、出た署名の組が一覧のその行の組どおりのとき <see cref="Known"/> か <see cref="Green"/> にし、
    /// 一覧に載っているのに起きない行は <see cref="Differs"/> にする。null なら違反が無い行だけが <see cref="Green"/>）
    /// </param>
    public static async Task<(string Outcome, string Detail)> RunAsync(
        SyncModelScenario scenario,
        CancellationToken ct,
        SyncModelWorldOptions options,
        KnownFailureSet? known = null
    )
    {
        try
        {
            // 控えの Fact だけが素通しの印を立てる（「流せない」を違反にせず、行の結果として数える）
            var (failures, _) = await scenario.RunAsync(
                ct,
                options with
                {
                    PassesNotRunnableThrough = true,
                }
            );
            var signatures = failures.Select(f => f.Signature).ToHashSet(StringComparer.Ordinal);
            var detail =
                signatures.Count == 0
                    ? "(no violations)"
                    : string.Join(", ", signatures.Order(StringComparer.Ordinal));

            if (scenario.ExpectedJudgeSignatures is { } expected)
            {
                return signatures.SetEquals(expected) ? (Green, "") : (Differs, detail);
            }

            if (known is null)
            {
                return signatures.Count == 0 ? (Green, "") : (Differs, detail);
            }

            var listed = known
                .Classify(
                    failures
                        .GroupBy(f => f.Signature, StringComparer.Ordinal)
                        .Select(g => new EngineFailure(scenario.Name, g.Key, g.First().Detail)),
                    scope => scope == scenario.Name
                )
                .IsClean;

            if (!listed)
            {
                return (Differs, detail);
            }

            return signatures.Count == 0 ? (Green, "") : (Known, detail);
        }
        catch (Exception ex) when (SyncModelDevice.UnsupportedHand(ex) is { } hand)
        {
            return (Unsupported, hand);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Differs, "throw " + Summary(ex));
        }
    }

    /// <summary>すべての決定的なシナリオを並列に流し、シナリオの名前 → 結果と詳細を返す</summary>
    /// <param name="options">世界の組み方（端末の作り方を含む）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <param name="known">既知の失敗の一覧（<see cref="RunAsync"/>）</param>
    public static async Task<
        IReadOnlyDictionary<string, (string Outcome, string Detail)>
    > RunAllAsync(
        SyncModelWorldOptions options,
        CancellationToken ct,
        KnownFailureSet? known = null
    )
    {
        var results = new ConcurrentDictionary<string, (string Outcome, string Detail)>(
            StringComparer.Ordinal
        );

        await Parallel.ForEachAsync(
            SyncModelScenarios.All,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
                CancellationToken = ct,
            },
            async (scenario, token) =>
                results[scenario.Name] = await RunAsync(scenario, token, options, known)
        );

        return results;
    }

    /// <summary>控えのテキスト（改行は LF）を作る</summary>
    /// <param name="source">先頭のコメントに書く、この控えを作るテストと作り方の説明</param>
    /// <param name="regenerateSteps">書き直しのコマンド</param>
    /// <param name="title">見出し</param>
    /// <param name="outcomes">集計の行に並べる結果の名前</param>
    /// <param name="results">シナリオの名前 → 結果と詳細</param>
    /// <param name="extraHeader">4 列目の見出し</param>
    /// <param name="extra">シナリオの名前 → 4 列目のセル</param>
    public static string Render(
        string source,
        string regenerateSteps,
        string title,
        IReadOnlyList<string> outcomes,
        IReadOnlyDictionary<string, (string Outcome, string Detail)> results,
        string extraHeader,
        Func<string, string> extra
    )
    {
        var text = new StringBuilder();
        text.Append(
            "<!-- 手で直さない生成物（" + source + "）。書き直し：" + regenerateSteps + " -->\n\n"
        );
        text.Append("# " + title + "\n\n");
        text.Append(
            string.Join(
                " / ",
                outcomes.Select(outcome =>
                    $"{outcome}: {results.Values.Count(r => r.Outcome == outcome).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                )
            )
        );
        text.Append("\n\n| シナリオ | 結果 | 詳細 | " + extraHeader + " |\n|---|---|---|---|\n");

        foreach (var (name, result) in results.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append(
                $"| {Cell(name)} | {result.Outcome} | {Cell(result.Detail)} | {extra(name)} |\n"
            );
        }

        return text.ToString();
    }

    /// <summary>控えを読み、シナリオの名前 → 結果と詳細を返す（表のセルの逃がした縦棒を戻す）</summary>
    /// <param name="path">控えのファイルの絶対パス</param>
    public static Dictionary<string, (string Outcome, string Detail)> Read(string path)
    {
        var ledger = new Dictionary<string, (string Outcome, string Detail)>(
            StringComparer.Ordinal
        );

        foreach (var line in File.ReadAllLines(path))
        {
            if (
                !line.StartsWith("| ", StringComparison.Ordinal)
                || line.StartsWith("| シナリオ |", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var cells = CellSeparator
                .Split(line.Trim().Trim('|'))
                .Select(cell => cell.Trim().Replace("\\|", "|", StringComparison.Ordinal))
                .ToArray();
            ledger[cells[0]] = (cells[1], cells[2]);
        }

        return ledger;
    }

    /// <summary>控えの表のセルの区切り（逃がした縦棒は区切りにしない）</summary>
    private static readonly Regex CellSeparator = new(@"(?<!\\)\|", RegexOptions.CultureInvariant);

    /// <summary>例外の型と、機械やパスに依らない短い要約（数字は # へ畳み、パスに見える部分は伏せる）</summary>
    private static string Summary(Exception ex) =>
        PathLike.Replace(SyncModelRun.ErrorText(ex), "<path>");

    /// <summary>パスに見える部分（ドライブ文字かスラッシュで始まり空白・引用符まで）</summary>
    private static readonly Regex PathLike = new(
        @"([A-Za-z]:[\\/]|[\\/]{1,2}[^\s'""]*[\\/])[^\s'""]*",
        RegexOptions.CultureInvariant
    );

    /// <summary>表のセルに置けるようにする（縦棒を逃がす）</summary>
    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
}
