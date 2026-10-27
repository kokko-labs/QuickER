using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>
/// ケース ID ごとに検証本体を流し、起きた失敗（例外）を既知の失敗の一覧と突き合わせる台帳
/// </summary>
/// <remarks>
/// <para>
/// ガードのテストが、修正より前に入る段階で使う。照合の規則は <see cref="KnownFailureSet"/> が持ち、この台帳は
/// ケースの失敗を範囲＝ケース ID の <see cref="EngineFailure"/>（詳細＝例外のメッセージ）にして渡すだけ。
/// 一覧の項目は <see cref="Case"/> で作る（範囲＝ケース ID・<see cref="KnownMatch.KeyOnly"/>・期待する理由の断片）。
/// </para>
/// <para>
/// 台帳にない失敗・台帳にあるのに起きない失敗・期待した理由と別の理由で起きた失敗は、いずれも赤になる。
/// 修正が入るたびに該当の項目を消し、区切りの最後に空にする。
/// </para>
/// </remarks>
public sealed class KnownCaseLedger
{
    /// <summary>台帳を作る（一覧の整合は <see cref="KnownFailureSet"/> が構築時に検査する）</summary>
    /// <param name="engineId">台帳を持つテストの名前</param>
    /// <param name="known">既知の失敗（<see cref="Case"/> で作る）</param>
    public KnownCaseLedger(string engineId, IReadOnlyList<KnownEntry> known)
    {
        Set = new KnownFailureSet(engineId, known);
    }

    /// <summary>既知の失敗の一覧</summary>
    public KnownFailureSet Set { get; }

    /// <summary>一覧に載っているケース ID</summary>
    public IReadOnlyCollection<string> Ids =>
        Set.Entries.Select(entry => entry.Scope).ToHashSet(StringComparer.Ordinal);

    /// <summary>ケースの既知の失敗の項目を作る</summary>
    /// <param name="caseId">ケース ID</param>
    /// <param name="finding">指摘の番号</param>
    /// <param name="fixedIn">一覧から外す区切り</param>
    /// <param name="reasonContains">失敗のメッセージに含まれるべき理由の断片</param>
    public static KnownEntry Case(
        string caseId,
        string finding,
        string fixedIn,
        string reasonContains
    ) =>
        new(
            finding,
            KnownKind.Pending,
            fixedIn,
            null,
            caseId,
            KnownMatch.KeyOnly,
            ReasonContains: reasonContains
        );

    /// <summary>ケース ID の失敗を台帳と突き合わせる</summary>
    /// <param name="caseId">ケース ID</param>
    /// <param name="body">検証本体</param>
    /// <param name="problems">問題の集め先。null なら最初の問題をその場で例外にする</param>
    public void Run(string caseId, Action body, List<string>? problems = null)
    {
        Exception? failure = null;

        try
        {
            body();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Evaluate(caseId, failure, problems);
    }

    /// <summary>非同期の検証本体の失敗を台帳と突き合わせる（<see cref="Run"/> の非同期版）</summary>
    /// <param name="caseId">ケース ID</param>
    /// <param name="body">検証本体</param>
    public async Task RunAsync(string caseId, Func<Task> body)
    {
        Exception? failure = null;

        try
        {
            await body();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Evaluate(caseId, failure, problems: null);
    }

    /// <summary>集めた問題があれば、全件を 1 つの失敗として報告する</summary>
    public static void ThrowIfAny(List<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new XunitException(
                $"{problems.Count} 件: {Environment.NewLine}{string.Join(Environment.NewLine, problems)}"
            );
        }
    }

    /// <summary>起きた失敗（無ければ null）を一覧と突き合わせ、問題があれば報告する</summary>
    private void Evaluate(string caseId, Exception? failure, List<string>? problems)
    {
        EngineFailure[] observed = failure is null
            ? []
            : [new EngineFailure(caseId, "exception", failure.Message, Source: failure)];
        var problem = Set.Classify(
                observed,
                scope => string.Equals(scope, caseId, StringComparison.Ordinal)
            )
            .Problem($"[{caseId}]");

        if (problem is null)
        {
            return;
        }

        if (problems is null)
        {
            throw new XunitException(problem);
        }

        problems.Add(problem);
    }
}
