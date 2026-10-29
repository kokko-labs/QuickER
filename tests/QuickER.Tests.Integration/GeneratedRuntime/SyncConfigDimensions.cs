using System;
using System.Collections.Generic;
using System.Linq;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>同期の構成の行列の 1 行（因子ごとの水準）</summary>
/// <param name="Fixture">フィクスチャ（キーの型）</param>
/// <param name="LocalKey">ローカルのリポジトリの登録の形</param>
/// <param name="Server">サーバーの差分ソースの形</param>
/// <param name="Registration">同期の登録の呼び方</param>
/// <param name="Hook">ローカルの Save フック</param>
/// <param name="Via">ローカルの新しい行の書き込みの経路</param>
internal sealed record SyncConfigCase(
    string Fixture,
    string LocalKey,
    string Server,
    string Registration,
    string Hook,
    string Via
)
{
    /// <summary>行の ID（台帳と報告が名指しする）</summary>
    public string Id => $"{Fixture}/{LocalKey}/{Server}/{Registration}/{Hook}/{Via}";

    /// <summary>網羅の計数へ渡す割り当て</summary>
    public IReadOnlyDictionary<string, string> Assignment =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SyncConfigDimensions.Fixture] = Fixture,
            [SyncConfigDimensions.LocalKey] = LocalKey,
            [SyncConfigDimensions.Server] = Server,
            [SyncConfigDimensions.Registration] = Registration,
            [SyncConfigDimensions.Hook] = Hook,
            [SyncConfigDimensions.Via] = Via,
        };

    /// <summary>ローカルのリポジトリを keyed で登録するか</summary>
    public bool LocalKeyed => LocalKey == SyncConfigDimensions.Keyed;

    /// <summary>サーバーが HTTP の向こうにいるか</summary>
    public bool OverHttp =>
        Server is SyncConfigDimensions.HttpAddress or SyncConfigDimensions.HttpClientFactory;

    /// <summary>フックが新しい行のキーを決めるか</summary>
    public bool HookAssignsKey => Hook == SyncConfigDimensions.AssignsKey;

    /// <summary>新しい行をグラフ保存で書くか</summary>
    public bool ViaGraph => Via == SyncConfigDimensions.Graph;
}

/// <summary>
/// 同期の構成の行列（DI の登録の形 × 転送経路 × キーの型 × キーを決めるフック × 書き込みの経路）の次元表・網羅しない組・
/// 台帳の行・既知の失敗
/// </summary>
/// <remarks>
/// <para>
/// 同期のモデル（<see cref="SyncModelRuntimeTests"/>）は DI を使わずに表とエンジンを手で組むので、生成された DI の登録の形
/// （keyed／非 keyed・<c>AddGeneratedSyncSupport</c> か分けた登録・直結か HTTP か）と値オブジェクトのキーに届かない。
/// この行列は、有効な組の全積の行ごとに「登録→解決→ローカルの新しい行を同期 1 回で上げる→サーバーの新しい行を同期 1 回で
/// 降ろす」を流す（<see cref="SyncConfigMatrixRuntimeTests"/>）。
/// </para>
/// <para>
/// 行はフィクスチャのアダプタが作れる組だけ（値オブジェクトのキーのフィクスチャはリモートサービスを持たないので HTTP の行が
/// 無い＝網羅しない組の一覧に載せる）。行を表から落とすと、その水準の組が欠けて網羅の照合が赤になる。
/// </para>
/// </remarks>
internal static class SyncConfigDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "config";

    /// <summary>フィクスチャ（キーの型）</summary>
    public const string Fixture = "Fixture";

    /// <summary>ローカルのリポジトリの登録の形</summary>
    public const string LocalKey = "LocalKey";

    /// <summary>サーバーの差分ソースの形</summary>
    public const string Server = "Server";

    /// <summary>同期の登録の呼び方</summary>
    public const string Registration = "Registration";

    /// <summary>ローカルの Save フック</summary>
    public const string Hook = "Hook";

    /// <summary>ローカルの新しい行の書き込みの経路</summary>
    public const string Via = "Via";

    /// <summary>整数のキー（SyncFixture）</summary>
    public const string IntKey = "IntKey";

    /// <summary>値オブジェクトのキー（SyncVoFixture）</summary>
    public const string VoKey = "VoKey";

    /// <summary>キーつきの登録</summary>
    public const string Keyed = "Keyed";

    /// <summary>キーなしの登録</summary>
    public const string Unkeyed = "Unkeyed";

    /// <summary>直結・サーバーのリポジトリはキーつき</summary>
    public const string DirectKeyed = "DirectKeyed";

    /// <summary>直結・サーバーのリポジトリはキーなし</summary>
    public const string DirectUnkeyed = "DirectUnkeyed";

    /// <summary>HTTP・ベースアドレスで登録</summary>
    public const string HttpAddress = "HttpAddress";

    /// <summary>HTTP・HttpClient のファクトリで登録</summary>
    public const string HttpClientFactory = "HttpClientFactory";

    /// <summary>AddGeneratedSyncSupport の 1 回の呼び出し</summary>
    public const string Support = "Support";

    /// <summary>差分ソースとエンジンを分けて登録</summary>
    public const string Split = "Split";

    /// <summary>フックなし</summary>
    public const string NoHook = "None";

    /// <summary>キーの無い新しい行に Before フックがキーを決める</summary>
    public const string AssignsKey = "AssignsKey";

    /// <summary>直接の CRUD（InsertAsync）</summary>
    public const string Direct = "Direct";

    /// <summary>グラフ保存（RowState=Added の SaveAsync）</summary>
    public const string Graph = "Graph";

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.SyncModel,
            [
                new Factor(
                    Fixture,
                    [.. SyncConfigFixtures.Values.Select(value => new Level(value))],
                    "同期する表の主キーの型（SyncFixture＝int・SyncVoFixture＝値オブジェクト）"
                ),
                new Factor(
                    LocalKey,
                    [new Level(Keyed), new Level(Unkeyed)],
                    "ローカルのリポジトリと同期エンジンの登録のキー（AddGeneratedSqliteRepositories と AddGeneratedSyncEngine の localServiceKey）"
                ),
                new Factor(
                    Server,
                    [
                        new Level(DirectKeyed),
                        new Level(DirectUnkeyed),
                        new Level(HttpAddress),
                        new Level(HttpClientFactory),
                    ],
                    "サーバーの差分ソース（直結はサーバーのリポジトリのキーの有無・HTTP は AddGeneratedHttpSyncSources の 2 つの形。HTTP の差分ソースはキーを取らない）"
                ),
                new Factor(
                    Registration,
                    [new Level(Support), new Level(Split)],
                    "AddGeneratedSyncSupport の 1 回の呼び出しか、差分ソース（AddGeneratedDirectSyncSources・AddGeneratedHttpSyncSources）と AddGeneratedSyncEngine を分けて呼ぶか"
                ),
                new Factor(
                    Hook,
                    [new Level(NoHook), new Level(AssignsKey)],
                    "ローカルの Save フック（なし／キーの無い新しい行に Before フックがキーを決める）"
                ),
                new Factor(
                    Via,
                    [new Level(Direct), new Level(Graph)],
                    "ローカルの新しい行の書き込みの経路（直接の InsertAsync・RowState=Added のグラフ保存）"
                ),
            ],
            [new FactorGroup(Fixture, Hook, Via), new FactorGroup(LocalKey, Server, Registration)],
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } =
        CoverageGap.Set(
            EngineIds.SyncModel,
            [
                CoverageGap.Matching(
                    Scope,
                    "P2-3",
                    "P3",
                    "SyncVoFixture にリモートサービスが無い（GenerateRemoteServices を有効にしていない）ので、値オブジェクトのキーで HTTP の差分ソースを組めない",
                    Fixture,
                    Server,
                    (fixture, server) =>
                        fixture == VoKey && server is HttpAddress or HttpClientFactory
                ),
            ]
        );

    /// <summary>行（有効な組の全積から、フィクスチャのアダプタが作れる組だけ）</summary>
    public static ScenarioTable<SyncConfigCase> Rows { get; } =
        new(
            EngineIds.SyncModel,
            AllCases()
                .Where(c => SyncConfigFixtures.Axis.Get(c.Fixture).HasRemoteServices || !c.OverHttp)
                .Select(c => new ScenarioRow<SyncConfigCase>(
                    c.Id,
                    Finding(c),
                    "登録→解決→ローカルの新しい行を同期 1 回で上げる→サーバーの新しい行を同期 1 回で降ろす",
                    c
                ))
        );

    /// <summary>
    /// 既知の失敗（main で赤になる行）。4m-E1＝非 keyed のローカル登録で同期の DI の登録が必ず投げる。
    /// 4m-E2＝値オブジェクトのキーを Before フックが決める新しい行のグラフ保存が、ジャーナル記録デコレータ越しだけ失敗する。
    /// </summary>
    /// <remarks>
    /// E1 の行は修正 E まで登録の段で止まり、フック・書き込みの経路・転送の部分は流れていない（網羅の計数は試みた行を数えるので、
    /// その組も網羅済みに数える）。
    /// </remarks>
    public static KnownCaseLedger Known { get; } =
        new(
            "SyncConfig",
            [
                .. Rows.Select(row => row.Input)
                    .Where(c => !c.LocalKeyed)
                    .Select(c =>
                        KnownCaseLedger.Case(
                            c.Id,
                            "4m-E1",
                            "修正 E",
                            "is not a factory registration, so the journaling decorator cannot build the instance it wraps"
                        )
                    ),
                .. Rows.Select(row => row.Input)
                    .Where(c => c.LocalKeyed && c.Fixture == VoKey && c.HookAssignsKey)
                    .Select(c =>
                        KnownCaseLedger.Case(
                            c.Id,
                            "4m-E2",
                            "修正 E",
                            "Object reference not set to an instance of an object"
                        )
                    ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncModel,
            "Config",
            "同期の構成の行列（DI の登録の形・転送経路・キーの型・キーを決めるフック）",
            Table,
            Rows.Count,
            [
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigDimensions.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigFixtures.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigIntKeyFixture.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigVoKeyFixture.cs",
                "tests/QuickER.Tests.Integration/GeneratedRuntime/SyncConfigMatrixRuntimeTests.cs",
            ],
            [
                "src/QuickER.Runtime.Sync",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_10_Sync.scriban",
            ]
        );

    /// <summary>行の由来の指摘（main で赤になる行だけ）</summary>
    private static string? Finding(SyncConfigCase c) =>
        !c.LocalKeyed ? "4m-E1"
        : c.Fixture == VoKey && c.HookAssignsKey ? "4m-E2"
        : null;

    /// <summary>全因子の水準の全積のうち、原理的に組める組</summary>
    private static IEnumerable<SyncConfigCase> AllCases()
    {
        IEnumerable<IReadOnlyList<string>> product =
        [
            [],
        ];

        foreach (var factor in Table.Factors)
        {
            product = product.SelectMany(prefix =>
                factor.Levels.Select(level => (IReadOnlyList<string>)[.. prefix, level.Name])
            );
        }

        return product
            .Select(values => new SyncConfigCase(
                values[0],
                values[1],
                values[2],
                values[3],
                values[4],
                values[5]
            ))
            .Where(c => Table.IsValid(c.Assignment));
    }

    /// <summary>原理的に組めない組のときだけ偽（判断に要る因子が割り当てに無ければ真）</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        assignment.TryGetValue(LocalKey, out var local);
        assignment.TryGetValue(Server, out var server);
        assignment.TryGetValue(Registration, out var registration);
        assignment.TryGetValue(Hook, out var hook);
        assignment.TryGetValue(Via, out var via);

        // AddGeneratedSyncSupport は直結の差分ソース（AddGeneratedDirectSyncSources）とエンジンの合成で、HTTP の差分ソースを
        // 組まない
        if (registration == Support && server is HttpAddress or HttpClientFactory)
        {
            return false;
        }

        // ローカルもサーバーも同じコンテナにキーなしで登録すると、同じ I{E}Repository の登録が 2 つになり後の登録が勝つ
        // （同じ DB を指すことになり、ローカルとサーバーを分けられない）
        if (local == Unkeyed && server == DirectUnkeyed)
        {
            return false;
        }

        // Save フックはグラフ保存（SaveAsync）でだけ走り、直接の CRUD（InsertAsync）は素通りする＝キーを決めるフックは
        // 直接の書き込みでは走らない
        if (hook == AssignsKey && via == Direct)
        {
            return false;
        }

        return true;
    }
}
