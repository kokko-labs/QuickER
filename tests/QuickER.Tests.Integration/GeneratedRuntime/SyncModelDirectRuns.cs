using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの決定的なシナリオを、直結・新しいエンジンの端末（<see cref="Options"/>）で流した結果。違反の無い結果は
/// テストのプロセスの中でシナリオごとに 1 回だけ流し、直結の Theory と、直結の結果を比べる相手にする転送の Theory
/// （HTTP・実 SQL Server・HTTP × 実 SQL Server）が使い回す。
/// </summary>
/// <remarks>
/// <para>
/// 決定的なシナリオは同じ組み方なら同じ結果になるので、転送の Theory が行ごとに直結の世界を流し直す必要は無い。新しい
/// エンジンの端末の世界は、同じプロセスで並べる数が増えるほど 1 つあたりが遅くなるので、流す世界の数を減らす。
/// </para>
/// <para>
/// 使い回すのは「例外なく終わり、違反が 1 つも無い」結果だけ。違反のある結果（既知の失敗の行・判定役の対照の行・退行）と
/// 例外で終わった実行は使い回さず、呼び出した行ごとに自分で流す。1 つの行の失敗がほかの行の比べ方へ混ざらず、失敗は
/// その行の失敗になる。
/// </para>
/// <para>
/// 鍵は <see cref="KeyOf"/> の 1 か所で、世界の組み方の名乗り（<see cref="SyncModelWorldOptions.Name"/>）とシナリオの名前から
/// 作る。組み方はこの型が持つ <see cref="Options"/> の 1 つだけで、シナリオが決めるもの（外部の ISyncTable の代役・端末の
/// 数・判定役の包み・手の列）は名前で決まる。名前の一覧（<see cref="SyncModelScenarios.All"/>）に無い複製のシナリオは
/// 使い回さない。取り出した結果が頼まれたシナリオのものであることは、結果に持たせたシナリオの参照で確かめる（鍵の作り方を
/// 誤ると、流していないシナリオの結果で行が緑になるため）。
/// </para>
/// </remarks>
internal static class SyncModelDirectRuns
{
    /// <summary>直結・SQLite のサーバー役・新しいエンジンの端末の世界の組み方</summary>
    public static SyncModelWorldOptions Options { get; } =
        SyncModelEngineDevice.On(SyncModelWorldOptions.Direct);

    /// <summary>流している途中か、違反なく終わった実行（鍵は <see cref="KeyOf"/>）</summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<Outcome>>> Shared = new(
        StringComparer.Ordinal
    );

    /// <summary>使い回す鍵（世界の組み方の名乗りとシナリオの名前）</summary>
    /// <param name="scenario">シナリオ</param>
    public static string KeyOf(SyncModelScenario scenario) => $"{Options.Name}|{scenario.Name}";

    /// <summary>
    /// シナリオを直結・新しいエンジンの端末で流した結果を返す。違反の無い結果が既にあればそれを返し、流している途中なら
    /// 終わるのを待つ。使い回せない結果（違反あり・例外）だったときは、この呼び出しが自分で流した結果を返す
    /// </summary>
    /// <param name="scenario">シナリオ</param>
    /// <param name="ct">キャンセルトークン</param>
    public static async Task<(
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    )> RunAsync(SyncModelScenario scenario, CancellationToken ct)
    {
        // 一覧に無い複製（同じ名前で中身の違うシナリオ）は、名前の鍵では見分けられないので使い回さない
        if (!ReferenceEquals(SyncModelScenarios.Get(scenario.Name), scenario))
        {
            return await ExecuteAsync(scenario, ct).ConfigureAwait(false);
        }

        var key = KeyOf(scenario);
        var mine = new Lazy<Task<Outcome>>(() => ExecuteOutcomeAsync(scenario, ct));
        var shared = Shared.GetOrAdd(key, mine);
        var isMine = ReferenceEquals(shared, mine);
        Outcome outcome;

        try
        {
            outcome = await shared.Value.ConfigureAwait(false);
        }
        catch (Exception) when (!isMine)
        {
            // ほかの行が流した実行が例外で終わった：その例外をこの行へ持ち込まず、自分で流す
            Forget(key, shared);

            return await ExecuteAsync(scenario, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 自分が流した実行が例外で終わった：使い回さず、この行の失敗にする
            Forget(key, shared);

            throw;
        }

        if (!ReferenceEquals(outcome.Scenario, scenario))
        {
            throw new InvalidOperationException(
                $"The shared direct run under the key '{key}' belongs to the scenario '{outcome.Scenario.Name}', not '{scenario.Name}'."
            );
        }

        if (outcome.Failures.Count == 0)
        {
            return (outcome.Failures, outcome.Log);
        }

        // 違反のある結果は使い回さない。自分が流した結果ならそのまま返し、ほかの行が流した結果なら自分で流す
        Forget(key, shared);

        return isMine
            ? (outcome.Failures, outcome.Log)
            : await ExecuteAsync(scenario, ct).ConfigureAwait(false);
    }

    /// <summary>その実行を、使い回す候補から外す（ほかの行が入れ直した別の実行は消さない）</summary>
    private static void Forget(string key, Lazy<Task<Outcome>> run) =>
        Shared.TryRemove(new KeyValuePair<string, Lazy<Task<Outcome>>>(key, run));

    private static async Task<Outcome> ExecuteOutcomeAsync(
        SyncModelScenario scenario,
        CancellationToken ct
    )
    {
        var (failures, log) = await ExecuteAsync(scenario, ct).ConfigureAwait(false);

        return new(scenario, failures, log);
    }

    private static Task<(
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    )> ExecuteAsync(SyncModelScenario scenario, CancellationToken ct) =>
        scenario.RunAsync(ct, Options);

    /// <summary>流し終えた実行の結果（どのシナリオのものかを持つ）</summary>
    /// <param name="Scenario">流したシナリオ</param>
    /// <param name="Failures">見つかった違反</param>
    /// <param name="Log">手の列の記録</param>
    private sealed record Outcome(
        SyncModelScenario Scenario,
        IReadOnlyList<ModelFailure> Failures,
        IReadOnlyList<string> Log
    );
}
