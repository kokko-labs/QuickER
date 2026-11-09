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
/// <param name="Via">ローカルの書き込みの経路</param>
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

    /// <summary>ローカルの同期する表のリポジトリをインスタンスの登録（シングルトン）にするか</summary>
    public bool LocalAsInstances => LocalKey == SyncConfigDimensions.UnkeyedInstance;

    /// <summary>サーバーが HTTP の向こうにいるか</summary>
    public bool OverHttp =>
        Server is SyncConfigDimensions.HttpAddress or SyncConfigDimensions.HttpClientFactory;

    /// <summary>フックが新しい行のキーを決めるか</summary>
    public bool HookAssignsKey => Hook == SyncConfigDimensions.AssignsKey;

    /// <summary>新しい親の行をグラフ保存で書くか（直接の InsertAsync でない水準はどれもグラフ保存で書く）</summary>
    public bool ViaGraph => Via != SyncConfigDimensions.Direct;

    /// <summary>保存済みの親をキーの無い新しい子ごと消すグラフ保存の段を流すか</summary>
    public bool RemovesParentWithNewChild =>
        Via == SyncConfigDimensions.GraphRemoveParentWithNewChild;

    /// <summary>親の表を構築時に除外して登録し、除外した親のグラフ保存で同期する子を書くか</summary>
    public bool ExcludesParent => Via == SyncConfigDimensions.GraphViaExcludedParent;
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
/// 降ろす→洗い替え」を流す（<see cref="SyncConfigMatrixRuntimeTests"/>）。書き込みの経路の水準のうち、保存済みの親をキーの
/// 無い新しい子ごと消すグラフ保存は流れの後ろにその段を足し、構築時に除外した親のグラフ保存は同期する子の表で同じ流れを流す。
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

    /// <summary>キーなしの登録の、同期する表のリポジトリをインスタンスの登録（シングルトン）に置き換えたもの</summary>
    public const string UnkeyedInstance = "UnkeyedInstance";

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

    /// <summary>
    /// グラフ保存で新しい親を書いて上げ下げした後、保存済みの親にキーの無い新しい子を足して親を消すグラフ保存（単数と複数の
    /// SaveAsync）と、新しい子だけを MarkRemoved したグラフ保存
    /// </summary>
    public const string GraphRemoveParentWithNewChild = "GraphRemoveParentWithNewChild";

    /// <summary>親の表を構築時に除外して登録し、除外した親のリポジトリのグラフ保存で同期する子を新しく足す</summary>
    public const string GraphViaExcludedParent = "GraphViaExcludedParent";

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
                    [new Level(Keyed), new Level(Unkeyed), new Level(UnkeyedInstance)],
                    "ローカルのリポジトリと同期エンジンの登録のキー（AddGeneratedSqliteRepositories と AddGeneratedSyncEngine の localServiceKey）と、キーなしの登録の同期する表のリポジトリをインスタンスの登録にした形"
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
                    [
                        new Level(Direct),
                        new Level(Graph),
                        new Level(GraphRemoveParentWithNewChild),
                        new Level(GraphViaExcludedParent),
                    ],
                    "ローカルの書き込みの経路（直接の InsertAsync・RowState=Added のグラフ保存・保存済みの親をキーの無い新しい子ごと消すグラフ保存・構築時に除外した親のグラフ保存で同期する子を足す）"
                ),
            ],
            [new FactorGroup(Fixture, Hook, Via), new FactorGroup(LocalKey, Server, Registration)],
            IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps { get; } = CoverageGap.Set(EngineIds.SyncModel, []);

    /// <summary>行（有効な組の全積から、フィクスチャのアダプタが作れる組だけ）</summary>
    public static ScenarioTable<SyncConfigCase> Rows { get; } =
        new(
            EngineIds.SyncModel,
            AllCases()
                .Where(c => SyncConfigFixtures.Axis.Get(c.Fixture).HasRemoteServices || !c.OverHttp)
                .Select(c => new ScenarioRow<SyncConfigCase>(c.Id, Finding(c), Description(c), c))
        );

    /// <summary>
    /// 既知の失敗（main で赤になる行）。空＝修正 E で 4m-E1（非 keyed のローカル登録で同期の DI の登録が必ず投げる）と
    /// 4m-E2（値オブジェクトのキーを Before フックが決める新しい行のグラフ保存が、ジャーナル記録デコレータ越しだけ失敗する）を直した。
    /// </summary>
    public static KnownCaseLedger Known { get; } = new("SyncConfig", []);

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.SyncModel,
            "Config",
            "同期の構成の行列（DI の登録の形・転送経路・キーの型・キーを決めるフック・書き込みの経路）",
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

    /// <summary>行の由来の指摘</summary>
    private static string? Finding(SyncConfigCase c) =>
        c.RemovesParentWithNewChild ? "本番E1"
        : c.ExcludesParent ? "本番E2"
        : c.LocalAsInstances ? "本番E4"
        : !c.LocalKeyed ? "4m-E1"
        : c.Fixture == VoKey && c.HookAssignsKey ? "4m-E2"
        : null;

    /// <summary>行の流れの説明</summary>
    private static string Description(SyncConfigCase c) =>
        c.ExcludesParent
            ? "親の表を除外して登録→解決→除外した親の直接の書き込みは記録しない→除外した親のグラフ保存で足した子を同期 1 回で上げる→サーバーの新しい子を同期 1 回で降ろす→洗い替え"
            : "登録→解決→ローカルの新しい行を同期 1 回で上げる→サーバーの新しい行を同期 1 回で降ろす→洗い替え"
                + (
                    c.RemovesParentWithNewChild
                        ? "→保存済みの親をキーの無い新しい子ごと消すグラフ保存を同期 1 回でサーバーへ届ける"
                        : ""
                );

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
        if (local is Unkeyed or UnkeyedInstance && server == DirectUnkeyed)
        {
            return false;
        }

        // Save フックはグラフ保存（SaveAsync）でだけ走り、直接の CRUD（InsertAsync）は素通りする＝キーを決めるフックは
        // 直接の書き込みでは走らない
        if (hook == AssignsKey && via == Direct)
        {
            return false;
        }

        // インスタンスの登録はコンテナを組む前に作るので、コンテナに登録した Save フック（AddSaveHook）を受け取れない
        if (local == UnkeyedInstance && hook == AssignsKey)
        {
            return false;
        }

        return true;
    }
}
