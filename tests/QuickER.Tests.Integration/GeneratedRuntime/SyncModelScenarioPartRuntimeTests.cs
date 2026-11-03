using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 同期のモデルの決定的なシナリオを、転送ごとにいくつかのクラスへ分ける（xUnit は同じクラスの Theory の行を並列に
/// 流さないため、1 つのクラスのままだと HTTP の Theory が全件実行の時間を決める）。
/// </summary>
/// <remarks>
/// <para>
/// シナリオは <see cref="SyncModelScenarios.All"/> の並びで、転送で流すものの番号を分ける数で割った余りで振り分ける。
/// 余り 0 は転送の本体のクラス（<see cref="SyncModelRuntimeTests"/>・<see cref="SyncModelHttpRuntimeTests"/>）が、
/// 残りはこのファイルのクラスが受け持つ。分けたクラスは転送の軸の目印（<see cref="ISyncModelTransportTests"/>・
/// <see cref="MatrixAxisValueAttribute"/>）を持たない＝軸の値ごとのクラスは本体の 1 つだけ。
/// </para>
/// <para>
/// 実 SQL Server の Theory は分けない（コンテナのコレクションに入り、どのみち 1 本ずつ流れる）。
/// </para>
/// <para>
/// 分ける数とその数のクラスは別々の場所にあり、片方だけを変えると余りの組のシナリオが黙って流れなくなる。
/// <see cref="SyncModelRuntimeTests.ScenarioParts_CoverEveryScenarioOncePerTransport"/> が、このメソッドを呼ぶ
/// MemberData を反射で集めて、転送ごとの行の和が流すシナリオとちょうど一致することを確かめる。
/// </para>
/// </remarks>
internal static class SyncModelScenarioParts
{
    /// <summary>直結のシナリオを分ける数</summary>
    public const int DirectParts = 2;

    /// <summary>HTTP のシナリオを分ける数</summary>
    public const int HttpParts = 4;

    /// <summary><see cref="Record"/> の中で呼ばれた <see cref="Names"/> の引数（ガードが MemberData の転送と番号を知るため）</summary>
    [ThreadStatic]
    private static List<(SyncModelTransport Transport, int Part)>? _calls;

    /// <summary>
    /// <paramref name="body"/> の中で呼ばれた <see cref="Names"/> の引数を返す（呼んだスレッドの呼び出しだけ）
    /// </summary>
    public static IReadOnlyList<(SyncModelTransport Transport, int Part)> Record(Action body)
    {
        var outer = _calls;
        var calls = new List<(SyncModelTransport, int)>();
        _calls = calls;

        try
        {
            body();
        }
        finally
        {
            _calls = outer;
        }

        return calls;
    }

    /// <summary>転送で流すシナリオのうち、<paramref name="part"/> 番目（0 から）のクラスが受け持つものの名前</summary>
    public static TheoryData<string> Names(SyncModelTransport transport, int part)
    {
        _calls?.Add((transport, part));

        var parts = transport switch
        {
            SyncModelTransport.Direct => DirectParts,
            SyncModelTransport.Http => HttpParts,
            _ => 1,
        };
        var data = new TheoryData<string>();
        var names = SyncModelScenarios
            .All.Where(s => s.RunsOn(transport))
            .Select(s => s.Name)
            .Where((_, index) => index % parts == part);

        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }
}

/// <summary>直結の決定的なシナリオの 2 つ目の分（<see cref="SyncModelScenarioParts"/>）</summary>
[Trait("Category", "Integration")]
public sealed class SyncModelRuntimeTestsPart2
{
    /// <summary>このクラスが受け持つシナリオの名前</summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.Direct, 1);

    [Theory(
        DisplayName = "[Sync/モデル] 決定的なシナリオは、既知の失敗の一覧に載った署名の組どおりに失敗する（載っていなければ失敗しない）"
    )]
    [MemberData(nameof(ScenarioNames))]
    public Task Scenario_FailsExactlyAsListed(string name) =>
        SyncModelRuntimeTests.AssertFailsExactlyAsListed(name);
}

/// <summary>HTTP の決定的なシナリオの 2 つ目の分（<see cref="SyncModelScenarioParts"/>）</summary>
[Trait("Category", "Integration")]
public sealed class SyncModelHttpRuntimeTestsPart2
{
    /// <summary>このクラスが受け持つシナリオの名前</summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.Http, 1);

    [Theory(
        DisplayName = "[Sync/モデル/HTTP] 決定的なシナリオは、HTTP 越しでも直結と同じ違反（正規化した署名）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public Task Scenario_MatchesDirect(string name) =>
        SyncModelHttpRuntimeTests.AssertMatchesDirect(name);
}

/// <summary>HTTP の決定的なシナリオの 3 つ目の分（<see cref="SyncModelScenarioParts"/>）</summary>
[Trait("Category", "Integration")]
public sealed class SyncModelHttpRuntimeTestsPart3
{
    /// <summary>このクラスが受け持つシナリオの名前</summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.Http, 2);

    [Theory(
        DisplayName = "[Sync/モデル/HTTP] 決定的なシナリオは、HTTP 越しでも直結と同じ違反（正規化した署名）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public Task Scenario_MatchesDirect(string name) =>
        SyncModelHttpRuntimeTests.AssertMatchesDirect(name);
}

/// <summary>HTTP の決定的なシナリオの 4 つ目の分（<see cref="SyncModelScenarioParts"/>）</summary>
[Trait("Category", "Integration")]
public sealed class SyncModelHttpRuntimeTestsPart4
{
    /// <summary>このクラスが受け持つシナリオの名前</summary>
    public static TheoryData<string> ScenarioNames() =>
        SyncModelScenarioParts.Names(SyncModelTransport.Http, 3);

    [Theory(
        DisplayName = "[Sync/モデル/HTTP] 決定的なシナリオは、HTTP 越しでも直結と同じ違反（正規化した署名）になる"
    )]
    [MemberData(nameof(ScenarioNames))]
    public Task Scenario_MatchesDirect(string name) =>
        SyncModelHttpRuntimeTests.AssertMatchesDirect(name);
}
