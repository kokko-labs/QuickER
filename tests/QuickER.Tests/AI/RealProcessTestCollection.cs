namespace QuickER.Tests.AI;

/// <summary>
/// 偽の CLI を実プロセスとして起動するテスト（偽の app-server・偽の claude）を 1 つのコレクションにまとめ、
/// 互いに並列で走らないようにする。
/// </summary>
/// <remarks>
/// 実プロセス（偽の app-server・cmd.exe）の起動は短時間に CPU を使うため、重なると全スイート実行中の時間に依存するテストの
/// 余裕を削る。ここに入れたテストどうしは順に走り、ほかのクラスとは従来どおり並列に走る
/// </remarks>
[CollectionDefinition(Name)]
public sealed class RealProcessTestCollection
{
    /// <summary>コレクション名</summary>
    public const string Name = "RealProcess";
}
