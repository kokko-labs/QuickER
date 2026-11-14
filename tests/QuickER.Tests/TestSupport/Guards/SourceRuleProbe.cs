namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の行：合成のソースへ規則の見つける形を当て、拾う出現を並びまで照合する</summary>
/// <remarks>
/// 拾うべき書き方と拾わない書き方を 1 つのソースに並べ、拾った「何か」の並びが <see cref="ProbeFile.Expected"/> と一致することを
/// 見る（拾わない書き方を拾えば並びが崩れて赤）。シンボルの規則は、ソースを 1 プロジェクトとしてコンパイルし（条件コンパイルの
/// 変種のコンパイルも作る）、エラーが無いことも確かめる。
/// </remarks>
/// <param name="Id">行の Id（一意）</param>
/// <param name="RuleId">当てる規則の Id</param>
/// <param name="Files">合成のソース</param>
internal sealed record SourceRuleProbe(string Id, string RuleId, IReadOnlyList<ProbeFile> Files)
{
    /// <summary>拾うべき出現を条件コンパイルの外れる分岐（基準の記号では無効になる分岐）に置いた行か</summary>
    public bool InactiveBranch { get; init; }

    /// <summary>シンボルの規則のコンパイルで、エラーでも数えない診断の Id（理由はソースのコメントに書く）</summary>
    public IReadOnlyList<string> IgnoredDiagnostics { get; init; } = [];

    /// <summary>
    /// シンボルの規則で、合成のソースを 1 プロジェクトにせず、本物の src と同じプロジェクトの集め方を通す行か
    /// （ファイルのパスは合成の木の根からの相対パス。csproj もファイルとして置く）
    /// </summary>
    public bool ThroughProjectDiscovery { get; init; }
}

/// <summary>自己検査の合成のソース 1 つ</summary>
/// <param name="Path">相対パス（報告とシンボルのコンパイルの構文木のパス）</param>
/// <param name="Text">本文</param>
/// <param name="Expected">拾う「何か」の並び（ソースの中の順）</param>
internal sealed record ProbeFile(string Path, string Text, params string[] Expected);
