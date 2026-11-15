namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>名前の規則（<see cref="RuntimeTestNaming"/>）に合わない既存の実行時テストクラスの一覧の 1 行</summary>
/// <param name="ClassName">クラスの名前</param>
/// <param name="Reason">規則に合わないまま残す理由</param>
public sealed record RuntimeTestNamingEntry(string ClassName, string Reason);

/// <summary>
/// 名前の規則に合わない既存の実行時テストクラス（今は双方向同期のクラスだけ）。一覧に無い違反も、一覧にあるのに無くなった・
/// 合うようになったクラスも <c>RuntimeTestMatrixTests.NewRuntimeTestClasses_FollowNamingRule</c> が赤にする
/// </summary>
public static class RuntimeTestNamingKnown
{
    private const string SyncRebuild =
        "双方向同期の作り直し（0.4.0 の後）で、同期のテストの構成と一緒に名乗る機能と実装先の名前へ付け直す";

    /// <summary>一覧（クラスの名前の序数順）</summary>
    public static IReadOnlyList<RuntimeTestNamingEntry> All { get; } =
    [
        new("SyncBinaryTransferResponseRuntimeTests", SyncRebuild),
        new("SyncConfigMatrixRuntimeTests", SyncRebuild),
        new("SyncConstructionExclusionTests", SyncRebuild),
        new("SyncEndpointRegistrationRuntimeTests", SyncRebuild),
        new("SyncHttpRuntimeTests", SyncRebuild),
        new("SyncKeyTextRuntimeTests", SyncRebuild),
        new("SyncLocalSaveHookRegistrationTests", SyncRebuild),
        new("SyncModelHttpRuntimeTests", SyncRebuild),
        new("SyncModelHttpRuntimeTestsPart2", SyncRebuild),
        new("SyncModelHttpRuntimeTestsPart3", SyncRebuild),
        new("SyncModelHttpRuntimeTestsPart4", SyncRebuild),
        new("SyncModelRuntimeTests", SyncRebuild),
        new("SyncModelRuntimeTestsPart2", SyncRebuild),
        new("SyncModelSqlServerRuntimeTests", SyncRebuild),
        new("SyncNullKeyRuntimeTests", SyncRebuild),
        new("SyncRefreshBenchmarkRuntimeTests", SyncRebuild),
        new("SyncSqlServerHttpRuntimeTests", SyncRebuild),
        new("SyncSqlServerRuntimeTests", SyncRebuild),
        new("SyncSqliteRuntimeTests", SyncRebuild),
        new("SyncVoRuntimeTests", SyncRebuild),
    ];
}
