using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// <see cref="PathSegments"/> の「形 → 違反」の一覧（1 区間の名前と相対パス）
/// </summary>
/// <remarks>
/// 判定そのものを見る表（<see cref="PathSegmentsTests"/>）と、名前を受け取る入口の総当たり
/// （<see cref="PathSegmentEntranceFuzzTests"/>）が同じ形を使う。同じ「形 → 違反」を 2 か所に書くと食い違うので、ここの 1 つから導く。
/// 入口の総当たりが使う形は、判定を見る表と同じか、入口で成り立たせるために別の綴りにしたもの
/// （<see cref="PathForm.EntranceForm"/>）か、入口だけが流すもの（<see cref="PathForm.InCheck"/> が false）。
/// </remarks>
internal static class PathSegmentForms
{
    /// <summary>形の 1 項目</summary>
    /// <param name="Form">形（判定を見る表へ渡す文字列）</param>
    /// <param name="Violation">断る理由（断らない形は null）</param>
    /// <param name="InCheck">判定を見る表（<c>CheckSegment</c> / <c>CheckRelativePath</c>）の項目か</param>
    /// <param name="InEntrance">入口の総当たりの項目か（断る形だけ）</param>
    /// <param name="EntranceForm">入口の総当たりへ渡す別の綴り（無ければ <paramref name="Form"/>）</param>
    internal sealed record PathForm(
        string Form,
        PathSegmentViolation? Violation,
        bool InCheck = true,
        bool InEntrance = false,
        string? EntranceForm = null
    );

    /// <summary>1 区間の名前の形（断る理由・断らない名前）</summary>
    public static IReadOnlyList<PathForm> Segments { get; } =
    [
        new("", PathSegmentViolation.Empty),
        new(".", PathSegmentViolation.DotSegment, InEntrance: true),
        new("..", PathSegmentViolation.DotSegment, InEntrance: true),
        new("a/b", PathSegmentViolation.Separator, InEntrance: true),
        new("a\\b", PathSegmentViolation.Separator, InEntrance: true),
        // 入口は相対パスの入口でもあり、2 文字目の ":" はドライブ指定（相対パスの入口では Rooted）になるので、3 文字目に置く
        new("a:b", PathSegmentViolation.InvalidCharacter, InEntrance: true, EntranceForm: "ab:c"),
        new("C:", PathSegmentViolation.InvalidCharacter),
        new("a*b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a?b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a<b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a>b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a|b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a\"b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a\u0001b", PathSegmentViolation.InvalidCharacter, InEntrance: true),
        new("a\u0009b", PathSegmentViolation.InvalidCharacter),
        new(" a", PathSegmentViolation.LeadingWhitespace, InEntrance: true),
        new("\u3000a", PathSegmentViolation.LeadingWhitespace, InEntrance: true),
        new("a ", PathSegmentViolation.TrailingDotOrWhitespace, InEntrance: true),
        new("a.", PathSegmentViolation.TrailingDotOrWhitespace, InEntrance: true),
        // モックのプロジェクト名で断っていた綴り（元の MockProjectScaffoldServiceTests の入力をそのまま）
        new(" Foo", PathSegmentViolation.LeadingWhitespace, InEntrance: true),
        new("   ", PathSegmentViolation.LeadingWhitespace, InEntrance: true),
        new("Foo ", PathSegmentViolation.TrailingDotOrWhitespace, InEntrance: true),
        new("Bar.", PathSegmentViolation.TrailingDotOrWhitespace, InEntrance: true),
        new("GENERA~1", PathSegmentViolation.Tilde, InEntrance: true),
        new("a~b", PathSegmentViolation.Tilde, InEntrance: true),
        new("CON", PathSegmentViolation.ReservedDeviceName, InEntrance: true),
        new("con", PathSegmentViolation.ReservedDeviceName),
        new("NUL.txt", PathSegmentViolation.ReservedDeviceName),
        new("CON .txt", PathSegmentViolation.ReservedDeviceName),
        new("COM1", PathSegmentViolation.ReservedDeviceName, InEntrance: true),
        new("LPT9.md", PathSegmentViolation.ReservedDeviceName),
        new("CONIN$", PathSegmentViolation.ReservedDeviceName, InEntrance: true),
        new("conout$.txt", PathSegmentViolation.ReservedDeviceName),
        new("COM\u00B9", PathSegmentViolation.ReservedDeviceName, InEntrance: true),
        new("com\u00B2.txt", PathSegmentViolation.ReservedDeviceName),
        new("LPT\u00B3", PathSegmentViolation.ReservedDeviceName),
        new("LPT\u00B9.md", PathSegmentViolation.ReservedDeviceName),
        new("COM\u2074", null),
        new("CONIN", null),
        new(
            new string('a', PathSegments.MaxLength + 1),
            PathSegmentViolation.TooLong,
            InEntrance: true
        ),
        new(new string('a', PathSegments.MaxLength), null),
        new("COM10", null),
        new("CONSOLE", null),
        new("a..b", null),
        new("MyApp.Domain", null),
        new("\u65E5\u672C\u8A9E", null),
        new(".gitignore", null),
        // 入口の総当たりだけが流す形（判定を見る表は別の綴りで同じ理由を持つ）
        new("nul", PathSegmentViolation.ReservedDeviceName, InCheck: false, InEntrance: true),
        new("LPT9", PathSegmentViolation.ReservedDeviceName, InCheck: false, InEntrance: true),
        new("AUX.txt", PathSegmentViolation.ReservedDeviceName, InCheck: false, InEntrance: true),
    ];

    /// <summary>相対パスの形（断る理由・断らないパス）</summary>
    public static IReadOnlyList<PathForm> RelativePaths { get; } =
    [
        new("", PathSegmentViolation.Empty),
        new("/a", PathSegmentViolation.Rooted, InEntrance: true),
        new("\\a", PathSegmentViolation.Rooted, InEntrance: true),
        new("C:\\a", PathSegmentViolation.Rooted, InEntrance: true),
        new("C:a", PathSegmentViolation.Rooted, InEntrance: true),
        new("a//b", PathSegmentViolation.Empty, InEntrance: true),
        new("a/./b", PathSegmentViolation.DotSegment, InEntrance: true),
        new("a/../b", PathSegmentViolation.DotSegment, InEntrance: true),
        new("./a", PathSegmentViolation.DotSegment),
        new("a/ b", PathSegmentViolation.LeadingWhitespace, InEntrance: true),
        new("a /b", PathSegmentViolation.TrailingDotOrWhitespace, InEntrance: true),
        new("a./b", PathSegmentViolation.TrailingDotOrWhitespace),
        new("a/CON/b", PathSegmentViolation.ReservedDeviceName, InEntrance: true),
        new("a/x~1", PathSegmentViolation.Tilde, InEntrance: true),
        // 区間の内側の使えない文字と上限超過（相対パスの入口へは 1 区間の形の行が同じ理由で流れているので InEntrance は false）
        new("a/b*c", PathSegmentViolation.InvalidCharacter),
        new("a/" + new string('a', PathSegments.MaxLength + 1), PathSegmentViolation.TooLong),
        new("MyApp.Domain/Generated", null),
        new("MyApp.Domain\\Generated", null),
        new("Domain/", null),
        new("a\\b\\", null),
    ];

    /// <summary>判定を見る表（<c>CheckSegment</c>）の項目</summary>
    public static IEnumerable<PathForm> SegmentChecks => Segments.Where(f => f.InCheck);

    /// <summary>判定を見る表（<c>CheckRelativePath</c>）の項目</summary>
    public static IEnumerable<PathForm> RelativeChecks => RelativePaths.Where(f => f.InCheck);

    /// <summary>入口の総当たりが流す、1 区間の名前として断るべき形</summary>
    public static (string Form, PathSegmentViolation Violation)[] EntranceSegmentForms() =>
        [.. EntranceForms(Segments)];

    /// <summary>入口の総当たりが流す、相対パスとしてだけ断るべき形</summary>
    public static (string Form, PathSegmentViolation Violation)[] EntranceRelativeForms() =>
        [.. EntranceForms(RelativePaths)];

    private static IEnumerable<(string Form, PathSegmentViolation Violation)> EntranceForms(
        IEnumerable<PathForm> forms
    ) => forms.Where(f => f.InEntrance).Select(f => (f.EntranceForm ?? f.Form, f.Violation!.Value));
}
