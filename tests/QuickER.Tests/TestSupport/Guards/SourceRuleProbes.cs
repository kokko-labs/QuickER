namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表（領域ごとの行は partial のファイルに置く）</summary>
internal static partial class SourceRuleProbes
{
    private static readonly Lazy<IReadOnlyList<SourceRuleProbe>> LazyAll = new(() =>
        [.. Platform(), .. Names(), .. Provider(), .. Text(), .. Tests(), .. Gui(), .. Mcp()]
    );

    /// <summary>すべての行（宣言の順）</summary>
    public static IReadOnlyList<SourceRuleProbe> All => LazyAll.Value;

    /// <summary>Id から行を引く</summary>
    public static SourceRuleProbe ById(string id) =>
        All.SingleOrDefault(probe => probe.Id == id)
        ?? throw new ArgumentException($"自己検査の行が無い: {id}", nameof(id));
}
