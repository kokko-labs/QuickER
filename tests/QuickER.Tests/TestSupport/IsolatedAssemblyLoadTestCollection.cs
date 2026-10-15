namespace QuickER.Tests.TestSupport;

/// <summary>
/// QuickER のアセンブリを別の読み込みコンテキストへ読み込み直すテストを、ほかのテストと並列に走らせないためのコレクション。
/// </summary>
/// <remarks>
/// <para>
/// 読み込み直したアセンブリは、回収されるまで <c>AppDomain.GetAssemblies()</c> に同じ名前の写しとして現れる。
/// その間に走るテストが名前でアセンブリを引くと（WPF の pack URI の <c>/アセンブリ名;component/…</c>、
/// 読み込み済みのアセンブリを走査するテスト）、写しのほうを掴んで失敗する（WPF のアセンブリを読み込み直したときに、
/// 全件実行でだけウィンドウの <c>InitializeComponent</c> が失敗した）。
/// </para>
/// <para>
/// <c>DisableParallelization</c> のコレクションは、並列のテストが全部終わってから単独で走る。
/// このコレクションのテストは、読み込み直したコンテキストを回収し終えてから戻ること。
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IsolatedAssemblyLoadTestCollection
{
    /// <summary>コレクション名</summary>
    public const string Name = "IsolatedAssemblyLoad";
}
