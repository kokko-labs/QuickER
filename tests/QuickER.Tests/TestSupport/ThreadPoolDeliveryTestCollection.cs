namespace QuickER.Tests.TestSupport;

/// <summary>
/// スレッドプールで届く通知（FileSystemWatcher の通知・タイマのコールバック）を上限つきで待つテストを、ほかのテストと
/// 並列に走らせないためのコレクション。
/// </summary>
/// <remarks>
/// <para>
/// 並列の全件実行・変異テストの初回のテストの実行では、ほかのテストがスレッドプールを埋め、通知を運ぶ仕事が数十秒待たされる
/// （同じ実行で、スレッドプールの仕事 1 つを待つだけの行が 13 秒かかり、監視のテストが 30 秒の上限を割って落ちた）。
/// 製品は通知が届くまでの時間の上限を約束していないので、上限を伸ばすのでなく、スレッドプールを分け合わない形で流す。
/// </para>
/// <para>
/// <c>DisableParallelization</c> のコレクションは、並列のテストが全部終わってから単独で走る。
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThreadPoolDeliveryTestCollection
{
    /// <summary>コレクション名</summary>
    public const string Name = "ThreadPoolDelivery";
}
