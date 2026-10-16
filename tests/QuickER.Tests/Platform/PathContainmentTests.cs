using AwesomeAssertions;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// <see cref="PathContainment"/> の判定を表で固定する（ドライブ直下・UNC・末尾の区切り・大文字小文字・<c>..</c>・兄弟の前方一致）
/// </summary>
/// <remarks>
/// 期待値は表に書き下ろす（判定そのものを呼んで期待値を作らない）。同じ表を呼び出し元へ流すのは
/// <see cref="PathContainmentCallerFuzzTests"/>。
/// </remarks>
public sealed class PathContainmentTests
{
    [Theory(DisplayName = "パスがフォルダと同じか・中か・直下か")]
    [MemberData(nameof(Rows))]
    public void Table(PathContainmentRow row)
    {
        PathContainment
            .IsSameOrUnder(row.Path, row.Root)
            .Should()
            .Be(row.SameOrUnder, row.ToString());
        PathContainment.IsUnder(row.Path, row.Root).Should().Be(row.Under, row.ToString());
        PathContainment
            .IsDirectChild(row.Path, row.Root)
            .Should()
            .Be(row.DirectChild, row.ToString());
    }

    [Theory(DisplayName = "正規化は末尾の区切りを落とすが、ドライブ直下は区切りで終わる形のまま")]
    [InlineData(@"C:\root\", @"C:\root")]
    [InlineData(@"C:\root\sub\..", @"C:\root")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"\\server\share\x\", @"\\server\share\x")]
    public void Normalize_KeepsRootSeparator(string path, string expected)
    {
        PathContainment.Normalize(path).Should().Be(expected);
    }

    public static TheoryData<PathContainmentRow> Rows() => new(PathContainmentRow.All);
}

/// <summary>包含の判定の表の 1 行</summary>
/// <param name="Path">調べるパス</param>
/// <param name="Root">フォルダ</param>
/// <param name="SameOrUnder">同じか中にあるか</param>
/// <param name="Under">中にあるか（同じは含まない）</param>
/// <param name="DirectChild">直下にあるか</param>
/// <param name="Invalid">どちらかが正規化できない（呼び出し元によって扱いが違う）</param>
public sealed record PathContainmentRow(
    string Path,
    string Root,
    bool SameOrUnder,
    bool Under,
    bool DirectChild,
    bool Invalid = false
)
{
    /// <summary>UNC のパスか（実体を辿る呼び出し元へは流さない＝存在しないサーバーの問い合わせで待たされる）</summary>
    public bool IsUnc => Path.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>判定の表（ドライブ・UNC・末尾の区切り・大文字小文字・<c>..</c>・兄弟の前方一致・同じパス・正規化できない値）</summary>
    public static IReadOnlyList<PathContainmentRow> All { get; } =
    [
        new(@"C:\root", @"C:\root", true, false, false),
        new(@"C:\root\", @"C:\root", true, false, false),
        new(@"C:\root", @"C:\root\", true, false, false),
        new(@"C:\ROOT\a.html", @"C:\root", true, true, true),
        new(@"C:\root\a\b.html", @"C:\root", true, true, false),
        new(@"C:\root2\a.html", @"C:\root", false, false, false),
        new(@"C:\rootfile.html", @"C:\root", false, false, false),
        new(@"C:\root\..\other\a.html", @"C:\root", false, false, false),
        new(@"C:\root\..\a.html", @"C:\root", false, false, false),
        new(@"C:\root\sub\..\a.html", @"C:\root", true, true, true),
        new(@"C:\root\\a.html", @"C:\root", true, true, true),
        new(@"C:\root/a.html", @"C:\root", true, true, true),
        new(@"C:\root.\a.html", @"C:\root", true, true, true),
        new(@"C:\other\a.html", @"C:\root", false, false, false),
        new(@"C:\a.html", @"C:\", true, true, true),
        new(@"C:\x\a.html", @"C:\", true, true, false),
        new(@"D:\a.html", @"C:\", false, false, false),
        new(@"C:\", @"C:\", true, false, false),
        new(@"\\server\share\a.html", @"\\server\share", true, true, true),
        new(@"\\server\share\a.html", @"\\server\share\", true, true, true),
        new(@"\\SERVER\Share\x\a.html", @"\\server\share", true, true, false),
        new(@"\\server\share2\a.html", @"\\server\share", false, false, false),
        new("C:\\root\\a\0.html", @"C:\root", false, false, false, Invalid: true),
        new(string.Empty, @"C:\root", false, false, false, Invalid: true),
        new(@"C:\root\a.html", string.Empty, false, false, false, Invalid: true),
    ];
}
