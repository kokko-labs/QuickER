namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>同期エンジンのモデルベーステストの転送経路とサーバー役（軸の値の正本）</summary>
/// <remarks>
/// 値ごとに 1 つのテストクラス（<see cref="ISyncModelTransportTests"/> を実装する）が受け持ち、
/// クラスは <see cref="SyncModelTransportAxis.Name"/> の軸の値の属性で名乗る（<see cref="MatrixAxisGuardTests"/> が全数と
/// 名乗りの正しさを確かめる）。
/// </remarks>
public enum SyncModelTransport
{
    /// <summary>直結・SQLite のサーバー役（<see cref="SyncModelRuntimeTests"/>）</summary>
    Direct,

    /// <summary>HTTP 越し・SQLite のサーバー役（<see cref="SyncModelHttpRuntimeTests"/>）</summary>
    Http,

    /// <summary>直結・実 SQL Server のサーバー役（<see cref="SyncModelSqlServerRuntimeTests"/>）</summary>
    SqlServer,
}

/// <summary>転送経路の軸</summary>
internal static class SyncModelTransportAxis
{
    /// <summary>軸の名前</summary>
    public const string Name = "transport";

    /// <summary>軸の値（列挙値の名前）</summary>
    public static IReadOnlyList<string> Values { get; } = Enum.GetNames<SyncModelTransport>();
}

/// <summary>
/// 転送経路ごとのモデルベーステストのクラス（目印）。クラスが実際に使う世界の組み方を返す
/// （DB に触れないので、Docker なしで名乗りの正しさを確かめられる）
/// </summary>
internal interface ISyncModelTransportTests
{
    /// <summary>このクラスが世界を組むときの組み方</summary>
    SyncModelWorldOptions WorldOptions { get; }
}
