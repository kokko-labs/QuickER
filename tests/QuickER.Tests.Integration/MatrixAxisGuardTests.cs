using System.Reflection;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.Integration.Dialects;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.Integration.GeneratedRuntime;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration;

/// <summary>
/// Integration の「共通基底 1＋値ごとの派生 N」のテストと行列の軸が、軸の正本（方言の名前・転送経路・実装先）と
/// 一対一に対応し、派生クラスが名乗る値が実際に使う値と一致することを確かめる
/// </summary>
/// <remarks>
/// <para>
/// どの Fact も Docker を使わない（コンテナのフィクスチャのコレクションに入らず、スキップもしない）。派生クラスは、
/// コンストラクタの引数（コンテナのフィクスチャ・出力）をすべて null にして作る。派生クラスはフィクスチャに、DB を使う
/// ときにだけ触れるので、方言の名前・世界の組み方は Docker なしで読める（CI の Windows のジョブは Linux のコンテナを
/// 使えないので、名乗りの正しさをコンテナのテストの中だけで確かめると、そのジョブでは確かめられない）。
/// </para>
/// <para>
/// 正本に値を足すと、派生クラス・アダプタを書くか理由つきで除外するまで赤になる（<see cref="MatrixAxisGuard"/>）。
/// </para>
/// </remarks>
public sealed class MatrixAxisGuardTests
{
    [Fact(DisplayName = "同期計画の総当たりの派生クラスは、方言の名前の正本と一対一に対応する")]
    public void SyncPlanFuzzDialects_CoverDialectNames() =>
        MatrixAxisGuard.DerivedClassesCover(
            typeof(SyncPlanFuzzTestsBase),
            SyncPlanFuzzTestsBase.DialectAxis,
            DialectNames.All
        );

    [Fact(DisplayName = "同期計画の能力の組の網の派生クラスは、方言の名前の正本と一対一に対応する")]
    public void SyncPlanCapabilityNetDialects_CoverDialectNames() =>
        MatrixAxisGuard.DerivedClassesCover(
            typeof(SyncPlanCapabilityNetTestsBase),
            SyncPlanCapabilityNetTestsBase.DialectAxis,
            DialectNames.All
        );

    [Fact(
        DisplayName = "同期計画の能力の組の網の派生クラスが名乗る方言は、作る方言の部品の名前とプロバイダの名前に一致する"
    )]
    public void SyncPlanCapabilityNetDialects_DeclareTheirRealDialect()
    {
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(SyncPlanCapabilityNetTestsBase),
            SyncPlanCapabilityNetTestsBase.DialectAxis,
            type => WithCapabilityNetDialect(type, dialect => dialect.Name)
        );
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(SyncPlanCapabilityNetTestsBase),
            SyncPlanCapabilityNetTestsBase.DialectAxis,
            type => WithCapabilityNetDialect(type, dialect => dialect.Provider.Name)
        );
    }

    [Fact(
        DisplayName = "同期計画の総当たりの派生クラスが名乗る方言は、作る方言の部品の名前とプロバイダの名前に一致する（Docker なし）"
    )]
    public void SyncPlanFuzzDialects_DeclareTheirRealDialect()
    {
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(SyncPlanFuzzTestsBase),
            SyncPlanFuzzTestsBase.DialectAxis,
            type => WithDialect(type, dialect => dialect.Name)
        );
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(SyncPlanFuzzTestsBase),
            SyncPlanFuzzTestsBase.DialectAxis,
            type => WithDialect(type, dialect => dialect.Provider.Name)
        );
    }

    [Fact(
        DisplayName = "名前の総当たりの DDL の往復の派生クラスは、方言の名前の正本と一対一に対応する"
    )]
    public void NameFuzzDdlDialects_CoverDialectNames() =>
        MatrixAxisGuard.DerivedClassesCover(
            typeof(NameFuzzDdlRoundTripTestsBase),
            NameFuzzDdlRoundTripTestsBase.DialectAxis,
            DialectNames.All
        );

    [Fact(
        DisplayName = "名前の総当たりの DDL の往復の派生クラスが名乗る方言は、プロバイダの名前に一致する（Docker なし）"
    )]
    public void NameFuzzDdlDialects_DeclareTheirRealDialect() =>
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(NameFuzzDdlRoundTripTestsBase),
            NameFuzzDdlRoundTripTestsBase.DialectAxis,
            type =>
                WithInstance<NameFuzzDdlRoundTripTestsBase, string>(type, test => test.DialectName)
        );

    [Fact(
        DisplayName = "同期のモデルベーステストのクラスは、転送経路の正本（SyncModelTransport）と一対一に対応する"
    )]
    public void SyncModelTransports_CoverTransports() =>
        MatrixAxisGuard.DerivedClassesCover(
            typeof(ISyncModelTransportTests),
            SyncModelTransportAxis.Name,
            SyncModelTransportAxis.Values
        );

    [Fact(
        DisplayName = "同期のモデルベーステストのクラスが名乗る転送経路は、実際に使う世界の組み方（HTTP か・サーバー役・名前）に一致する（Docker なし）"
    )]
    public void SyncModelTransports_DeclareTheirRealTransport()
    {
        // 組み方の中身から経路を求める（HTTP 越しか・サーバー役が既定の SQLite の代役か）
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(ISyncModelTransportTests),
            SyncModelTransportAxis.Name,
            type =>
                WithInstance<ISyncModelTransportTests, string>(
                    type,
                    test => TransportOf(test.WorldOptions)
                )
        );

        // 出力に使う組み方の名前も、名乗る経路と同じ（大文字小文字は問わない）
        MatrixAxisGuard.DeclaredValuesMatch(
            typeof(ISyncModelTransportTests),
            SyncModelTransportAxis.Name,
            type =>
                WithInstance<ISyncModelTransportTests, string?>(
                    type,
                    test =>
                        SyncModelTransportAxis.Values.FirstOrDefault(value =>
                            string.Equals(
                                value,
                                test.WorldOptions.Name,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                )
        );
    }

    [Fact(
        DisplayName = "実装先どうしの一致の実装先の軸は作れ、値は列挙値の正本の並び・基準は QuickER 版 Repository"
    )]
    public void ParityBackendAxis_FollowsKinds()
    {
        BackendParityAxis
            .Axis.Values()
            .Select(row => row.Data)
            .Should()
            .Equal(Enum.GetNames<ParityBackendKind>());
        BackendParityAxis.Kinds.Should().Equal(Enum.GetValues<ParityBackendKind>());
        BackendParityAxis.Axis.Excluded.Should().BeEmpty();
        BackendParityAxis.Baseline.Should().Be(ParityBackendKind.Ado);
    }

    [Fact(
        DisplayName = "簡易 DSL の総当たりの SQL Server の構成の実装先の軸は作れ、インメモリだけを理由つきで除く（Docker なし）"
    )]
    public void DslSqlServerBackendAxis_IsComplete()
    {
        // コンテナには実装先を開くときにだけ触れるので、軸を作るだけなら null でよい
        var axis = DslConditionFuzzSqlServerRuntimeTests.BackendAxis(null!);

        axis.Adapters.Select(backend => backend.Name)
            .Should()
            .Equal(DslFuzzBackends.Ado, DslFuzzBackends.EfCore);
        axis.Excluded.Keys.Should().Equal(DslFuzzBackends.InMemory);
    }

    /// <summary>
    /// 派生クラスを、コンストラクタの引数（コンテナのフィクスチャ・出力）をすべて null にして作り、値を読む
    /// （作ったものが破棄できれば破棄する）
    /// </summary>
    private static TResult WithInstance<TBase, TResult>(Type type, Func<TBase, TResult> read)
    {
        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .ContainSingle($"{type.Name} のコンストラクタは 1 つ")
            .Which;
        var instance = (TBase)
            constructor.Invoke([.. constructor.GetParameters().Select(_ => (object?)null)]);

        try
        {
            return read(instance);
        }
        finally
        {
            (instance as IDisposable)?.Dispose();
        }
    }

    /// <summary>同期計画の総当たりの派生クラスが作る方言の部品から値を読む（部品が破棄できれば破棄する）</summary>
    private static string WithDialect(Type type, Func<SyncPlanFuzzDialect, string> read) =>
        WithInstance<SyncPlanFuzzTestsBase, string>(
            type,
            test =>
            {
                var dialect = test.CreateDialect();

                try
                {
                    return read(dialect);
                }
                finally
                {
                    (dialect as IDisposable)?.Dispose();
                }
            }
        );

    /// <summary>能力の組の網の派生クラスが作る方言の部品から値を読む（部品が破棄できれば破棄する）</summary>
    private static string WithCapabilityNetDialect(
        Type type,
        Func<SyncPlanFuzzDialect, string> read
    ) =>
        WithInstance<SyncPlanCapabilityNetTestsBase, string>(
            type,
            test =>
            {
                var dialect = test.CreateDialect();

                try
                {
                    return read(dialect);
                }
                finally
                {
                    (dialect as IDisposable)?.Dispose();
                }
            }
        );

    /// <summary>世界の組み方から、実際の転送経路を求める</summary>
    private static string TransportOf(SyncModelWorldOptions options)
    {
        var defaultServer = options.CreateServer.Equals(SyncModelWorldOptions.Direct.CreateServer);

        return (options.Http, defaultServer) switch
        {
            (false, true) => nameof(SyncModelTransport.Direct),
            (true, true) => nameof(SyncModelTransport.Http),
            (false, false) => nameof(SyncModelTransport.SqlServer),
            _ => "HTTP 越しの実 SQL Server（正本に無い組み方）",
        };
    }
}
