using System.Collections.Generic;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 乱数の種を流す 1 つの組（<see cref="SyncModelRuntimeTests.RunRandomSeedsAsync"/> へ渡す）。世界の組み方（サーバー役・端末の
/// 作り方）と、その世界の結果を照合する一覧・宣言をまとめる。種の決め方・次元表・生成器は組に依らない。
/// </summary>
/// <param name="Name">組の名前（失敗の報告の見出しと、再現の手順の文に使う）</param>
/// <param name="Options">世界の組み方（端末の数は種が決める）</param>
/// <param name="Known">既知の失敗の一覧</param>
/// <param name="Gaps">操作の次元表（<see cref="SyncModelDimensions"/>）の網羅しない組の一覧</param>
/// <param name="OutcomeGaps">実行の結果の表（<see cref="SyncModelOutcomeDimensions"/>）の網羅しない組の一覧</param>
/// <param name="Scope">網羅しない組の一覧のうち、この組が照合する範囲の名前</param>
/// <param name="RefusalBasisKinds">
/// 既定の種の数で、確かめた件数と根拠のあった件数が 1 件以上あることを表明する、根拠を確かめる競合・決着の種類
/// （<see cref="SyncModelRun.BasisChecks"/> の鍵）
/// </param>
/// <param name="GuardedRunChecks">
/// 2 つの範囲がどちらも既定の種の数のとき、確かめた件数が 1 件以上あることを表明する実行ごとの照合
/// （<see cref="SyncModelRun.RunChecks"/> の鍵）
/// </param>
/// <param name="NightlyGuardedRunChecks">
/// 夜間の既定の種の数のときだけ同じことを表明する実行ごとの照合（普段の種の数では起きない照合）
/// </param>
internal sealed record SyncModelRandomSuite(
    string Name,
    SyncModelWorldOptions Options,
    KnownFailureSet Known,
    CoverageGapSet Gaps,
    CoverageGapSet OutcomeGaps,
    string Scope,
    IReadOnlyList<string> RefusalBasisKinds,
    IReadOnlyList<string> GuardedRunChecks,
    IReadOnlyList<string> NightlyGuardedRunChecks
);
