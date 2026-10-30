using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>実装先どうしの一致の軸の値（実装先）ごとのアダプタ</summary>
/// <param name="Kind">実装先</param>
/// <param name="IsBaseline">ほかの実装先と比べる基準か（ちょうど 1 つ）</param>
public sealed record ParityBackendAdapter(ParityBackendKind Kind, bool IsBaseline) : IMatrixAdapter
{
    /// <summary>軸の値（実装先の列挙値の名前）</summary>
    public string AxisValue => Kind.ToString();
}

/// <summary>
/// 実装先どうしの一致の実装先の軸（正本は <see cref="ParityBackendKind"/> の列挙値の名前）
/// </summary>
/// <remarks>
/// シナリオが流す実装先（<see cref="ParityScenario.Backends"/>）は、この軸のアダプタを正本の並びで引く。
/// 列挙値を足してアダプタを書き忘れると、軸を作る時点で赤になる（<see cref="MatrixAxis{TAdapter}"/>）。
/// </remarks>
public static class BackendParityAxis
{
    /// <summary>実装先の軸</summary>
    public static MatrixAxis<ParityBackendAdapter> Axis { get; } =
        new(
            "parity-backend",
            Enum.GetNames<ParityBackendKind>(),
            [
                new(ParityBackendKind.Ado, IsBaseline: true),
                new(ParityBackendKind.EfCore, IsBaseline: false),
                new(ParityBackendKind.InMemory, IsBaseline: false),
                new(ParityBackendKind.Remote, IsBaseline: false),
                new(ParityBackendKind.RemoteEfCoreOnly, IsBaseline: false),
            ]
        );

    /// <summary>実装先（正本の並び）</summary>
    public static IReadOnlyList<ParityBackendKind> Kinds { get; } =
        Axis.Adapters.Select(adapter => adapter.Kind).ToList();

    /// <summary>ほかの実装先と比べる基準の実装先</summary>
    public static ParityBackendKind Baseline { get; } =
        Axis.Adapters.Single(adapter => adapter.IsBaseline).Kind;
}
