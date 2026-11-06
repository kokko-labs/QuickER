namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表（領域ごとの行は partial のファイルに置く）</summary>
/// <remarks>
/// 行を足すときは、領域のファイル（<c>SourceRules.{領域}.cs</c>）のメソッドに足す。規則の Id・既知の一覧の理由・範囲の除外の理由は
/// <see cref="SourceRuleTests.Table_IsWellFormed"/> が検査する。規則の表のファイル自身の文字列が tests を走査する規則に写り込む
/// ときは、走査の範囲を狭めずに綴りを割って避ける。
/// </remarks>
internal static partial class SourceRules
{
    private static readonly Lazy<IReadOnlyList<SourceRule>> LazyAll = new(() =>
        [.. Platform(), .. Names(), .. Provider(), .. Text(), .. Tests(), .. Gui(), .. Mcp()]
    );

    /// <summary>すべての規則（宣言の順）</summary>
    public static IReadOnlyList<SourceRule> All => LazyAll.Value;

    /// <summary>Id から規則を引く</summary>
    public static SourceRule ById(string id) =>
        All.SingleOrDefault(rule => rule.Id == id)
        ?? throw new ArgumentException($"規則が無い: {id}", nameof(id));

    /// <summary>src の全部</summary>
    private static SourceScope Src => SourceScope.Under("src");

    /// <summary>src の QuickER.Platform の外（OS との境界の正本を持つプロジェクトを除く）</summary>
    private static SourceScope SrcOutsidePlatform =>
        Src.Except(
            "QuickER.Platform/",
            "OS との境界の正本（QuickER.Platform）は、規則が守る API そのものを持つ"
        );
}
