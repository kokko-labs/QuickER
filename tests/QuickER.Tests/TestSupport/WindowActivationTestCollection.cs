namespace QuickER.Tests.TestSupport;

/// <summary>
/// ウィンドウのアクティブ状態（キーボードフォーカスの行き来）に結果が左右されるテストを、ほかのテストと
/// 並列に走らせないためのコレクション。
/// </summary>
/// <remarks>
/// <para>
/// ほかのテストクラスがウィンドウを表示すると、そのウィンドウがアクティブになり、こちらのウィンドウは
/// 非アクティブになる。<c>Deactivated</c> を見て判断を変えるビヘイビア（<c>CanvasViewportBehavior</c> の
/// Space の判定）のテストは、その巻き添えで結果が変わる（並列で流すと数十回に 1 回落ちた）。
/// </para>
/// <para>
/// <c>DisableParallelization</c> のコレクションは、並列のテストが全部終わってから単独で走る。
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowActivationTestCollection
{
    /// <summary>コレクション名</summary>
    public const string Name = "WindowActivation";
}
