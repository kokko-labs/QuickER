using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの決定的なシナリオを 1 つの端末の作り方で流し、行ごとの結果を返す部品。小さい塊の端末の照合
/// （<see cref="SyncSupportAdoSqliteEngineRuntimeTests.SmallChunkScenarios_MatchDirect"/>）が使う。
/// </summary>
/// <remarks>
/// 結果は <see cref="Green"/>（違反が無い。判定役の対照の行は出た署名の集合が宣言と一致）・<see cref="Known"/>（既知の失敗の一覧を
/// 渡したときだけ。出た署名の組が一覧のその行の組どおり）・<see cref="Differs"/>（それ以外。詳細は署名を序数の順に並べたものか、
/// 例外の型と短い要約）。
/// </remarks>
internal static class SyncModelScenarioLedger
{
    /// <summary>違反が無い</summary>
    public const string Green = "green";

    /// <summary>既知の失敗の一覧のその行の署名の組どおりに失敗した</summary>
    public const string Known = "known";

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
            var (failures, _) = await scenario.RunAsync(ct, options);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (Differs, "throw " + Summary(ex));
        }
    }

    /// <summary>例外の型と、機械やパスに依らない短い要約（数字は # へ畳み、パスに見える部分は伏せる）</summary>
    private static string Summary(Exception ex) =>
        PathLike.Replace(SyncModelRun.ErrorText(ex), "<path>");

    /// <summary>パスに見える部分（ドライブ文字かスラッシュで始まり空白・引用符まで）</summary>
    private static readonly Regex PathLike = new(
        @"([A-Za-z]:[\\/]|[\\/]{1,2}[^\s'""]*[\\/])[^\s'""]*",
        RegexOptions.CultureInvariant
    );
}
