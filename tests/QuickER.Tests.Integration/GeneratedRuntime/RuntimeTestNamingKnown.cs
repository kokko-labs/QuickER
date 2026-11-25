namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>名前の規則（<see cref="RuntimeTestNaming"/>）に合わない既存の実行時テストクラスの一覧の 1 行</summary>
/// <param name="ClassName">クラスの名前</param>
/// <param name="Reason">規則に合わないまま残す理由</param>
public sealed record RuntimeTestNamingEntry(string ClassName, string Reason);

/// <summary>
/// 名前の規則に合わない既存の実行時テストクラス（今は同期のモデルの転送の本体 3 本だけ）。一覧に無い違反も、一覧にあるのに無くなった・
/// 合うようになったクラスも <c>RuntimeTestMatrixTests.NewRuntimeTestClasses_FollowNamingRule</c> が赤にする
/// </summary>
public static class RuntimeTestNamingKnown
{
    private const string SyncModelEngine =
        "同期のモデルのエンジンの転送ごとの本体。転送の軸の目印（ISyncModelTransportTests・MatrixAxisValueAttribute）を持つ軸の値ごとの 1 クラスで、"
        + "種の決め方と期待の照合の入口（AssertFailsExactlyAsListed・AssertMatchesDirect）を持ち、CLAUDE.md の索引と見出し「同期のモデル（SyncModelRuntimeTests）」・"
        + "ガードの名指し（ScenarioParts_CoverEveryScenarioOncePerTransport・InterruptScenarios_CoverEveryPointAndShape・Family）がエンジンの名前として読む。"
        + "転送ごとに分けたクラスは規則の名前（SyncSupport…ScenarioPart…RuntimeTests）";

    /// <summary>一覧（クラスの名前の序数順）</summary>
    public static IReadOnlyList<RuntimeTestNamingEntry> All { get; } =
    [
        new("SyncModelHttpRuntimeTests", SyncModelEngine),
        new("SyncModelRuntimeTests", SyncModelEngine),
        new("SyncModelSqlServerRuntimeTests", SyncModelEngine),
    ];
}
