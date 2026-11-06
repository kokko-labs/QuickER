using QuickER.Platform;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>包含の判定の表の行のパスの形（<see cref="PathContainmentRow.Form"/>・網羅の計数の水準）</summary>
public enum PathContainmentForm
{
    /// <summary>同じパス</summary>
    SamePath,

    /// <summary>末尾に区切りのある同じパス（パス側・フォルダ側）</summary>
    SameWithTrailingSeparator,

    /// <summary>フォルダの下で大文字小文字が違う</summary>
    CaseDifference,

    /// <summary>孫（直下でない下）</summary>
    Grandchild,

    /// <summary>兄弟の前方一致（フォルダ名を前置きに持つ別の名前）</summary>
    SiblingPrefix,

    /// <summary>フォルダの外</summary>
    OutsideFolder,

    /// <summary><c>..</c> で外へ抜ける</summary>
    DotDotEscape,

    /// <summary><c>..</c> を中で畳むとフォルダの中に収まる</summary>
    DotDotFolded,

    /// <summary>区切りの揺れ（二重の区切り・<c>/</c>）</summary>
    SeparatorVariation,

    /// <summary>区間の末尾が <c>.</c></summary>
    TrailingDotSegment,

    /// <summary>ドライブ直下をフォルダとするときの直下</summary>
    DriveRootChild,

    /// <summary>ドライブ直下をフォルダとするときの孫</summary>
    DriveRootGrandchild,

    /// <summary>ドライブ直下そのもの</summary>
    DriveRootSame,

    /// <summary>別のドライブ</summary>
    OtherDrive,

    /// <summary>UNC の共有の直下</summary>
    UncChild,

    /// <summary>UNC の共有の下で大文字小文字が違う孫</summary>
    UncGrandchild,

    /// <summary>UNC の共有名の前方一致（別の共有）</summary>
    UncSiblingPrefix,

    /// <summary>パスが正規化できない値（NUL を含む・空）</summary>
    UnnormalizablePath,

    /// <summary>フォルダ（根）が空</summary>
    EmptyRoot,
}

/// <summary>
/// 包含の判定の表の次元表・網羅しない組の一覧・台帳の行
/// （QuickER.Platform の <c>PathContainment</c> と、呼び出し元の QuickER.AI の <c>CopilotWorkspacePermissionPolicy</c>・
/// QuickER.AI.Mock の <c>MockProjectFiles.PathsOverlap</c>・<c>MockPreviewPanel</c>・<c>MockProjectEmitTools</c>）
/// </summary>
/// <remarks>
/// 呼び出し元 × パスの形を数える。提出の出力フォルダ（EmitRoot）はフォルダの形を流すので、根の形（<see cref="RootForms"/>）を水準に当てる。
/// 呼び出し元と水準の種類が違う組は原理的に起こり得ないので <c>isValid</c> で外す。
/// </remarks>
internal static class PathContainmentDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-path-containment";

    /// <summary>呼び出し元</summary>
    public const string Caller = "Caller";

    /// <summary>パスの形</summary>
    public const string Form = "Form";

    private const string EmitRoot = "EmitRoot";

    /// <summary>根の形（提出の出力フォルダの水準）</summary>
    public static readonly string[] RootForms = ["OrdinaryRoot", "DriveRoot", "UncRoot"];

    /// <summary>呼び出し元</summary>
    public static readonly string[] Callers =
    [
        "Direct",
        "Copilot",
        "PathsOverlap",
        "PreviewResource",
        "PreviewNavigation",
        EmitRoot,
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Caller,
                    Callers.Select(c => new Level(c)).ToList(),
                    "包含の判定を使う呼び出し元（Direct は PathContainment を直接呼ぶ表）"
                ),
                new Factor(
                    Form,
                    Enum.GetNames<PathContainmentForm>()
                        .Concat(RootForms)
                        .Select(f => new Level(f))
                        .ToList(),
                    "パスの形（提出の出力フォルダは根の形）"
                ),
            ],
            isValid: IsValid
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                CoverageGap.Matching(
                    Scope,
                    "P3-10",
                    "P4",
                    "テストが流していないだけ：重なりの判定は UNC を実体を辿る呼び出し（存在しないサーバーの問い合わせで待たされる）として"
                        + "流さず飛ばしている。UNC の形は直接の表・Copilot・プレビューが見ている",
                    Caller,
                    Form,
                    (caller, form) =>
                        caller == "PathsOverlap"
                        && form
                            is nameof(PathContainmentForm.UncChild)
                                or nameof(PathContainmentForm.UncGrandchild)
                                or nameof(PathContainmentForm.UncSiblingPrefix)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "Containment",
            "包含の判定の表（呼び出し元 × パスの形。PathContainment を直接と呼び出し元の 5 か所から）",
            Table,
            PathContainmentRow.All.Count,
            [
                "tests/QuickER.Tests/Platform/PathContainmentTests.cs",
                "tests/QuickER.Tests/Platform/PathContainmentCallerFuzzTests.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathTableDimensions.cs",
            ],
            [
                "src/QuickER.Platform/PathContainment.cs",
                "src/QuickER.AI/CopilotWorkspacePermissionPolicy.cs",
                "src/QuickER.AI.Mock/MockProjectFiles.cs",
                "src/QuickER.AI.Mock/MockPreviewPanel.xaml.cs",
                "src/QuickER.AI.Mock/MockProjectEmitTools.cs",
            ]
        );

    /// <summary>1 つ流した後、呼び出し元とパスの形を数える</summary>
    public static void Observe(CoverageCounter counter, string caller, string form) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Caller] = caller,
                [Form] = form,
            }
        );

    /// <summary>フォルダ（根）の形（ドライブ直下・UNC・それ以外）</summary>
    public static string RootFormOf(string root)
    {
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return "UncRoot";
        }

        return root.TrimEnd('\\', '/').EndsWith(':') ? "DriveRoot" : "OrdinaryRoot";
    }

    /// <summary>原理的に起こり得ない組：提出の出力フォルダは根の形だけ、ほかの呼び出し元は行のパスの形だけを流す</summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Caller, out var caller)
            || !assignment.TryGetValue(Form, out var form)
        )
        {
            return true;
        }

        return (caller == EmitRoot) == RootForms.Contains(form);
    }
}

/// <summary>名前を受け取る入口の表の次元表・網羅しない組の一覧・台帳の行（QuickER.Platform の <c>PathSegments</c>）</summary>
/// <remarks>
/// 入口（判定そのもの 2 つと、名前を受け取る 9 つの入口）× 当たった規則（<c>PathSegmentViolation</c> の全値と、受け付ける形）を数える。
/// 入口が名前に拡張子を付ける・区切りが正当な区切りになるなど、入口の成り立ちで成り立たない形は <c>Applies</c> で外してあり、
/// 規則の組が出ない理由は一覧へ書く。
/// </remarks>
internal static class PathSegmentsDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "os-boundary-path-segments";

    /// <summary>入口</summary>
    public const string Entrance = "Entrance";

    /// <summary>当たった規則（断らなければ <see cref="Accepted"/>）</summary>
    public const string Violation = "Violation";

    /// <summary>受け付ける形</summary>
    public const string Accepted = "Accepted";

    /// <summary>入口の名前</summary>
    public static readonly string[] Entrances =
    [
        "CheckSegment",
        "CheckRelativePath",
        "DomainLayerDirectory",
        "CodeSubdirectory",
        "ApiDocsSubdirectory",
        "ApiDocsFileName",
        "OutputFileName",
        "GeneratedFileName",
        "ProjectName",
        "ScreenFileName",
        "EmitPath",
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.OsBoundary,
            [
                new Factor(
                    Entrance,
                    Entrances.Select(e => new Level(e)).ToList(),
                    "名前の判定とそれを使う入口"
                ),
                new Factor(
                    Violation,
                    Enum.GetNames<PathSegmentViolation>()
                        .Append(Accepted)
                        .Select(v => new Level(v))
                        .ToList(),
                    "当たった規則（断らない形は Accepted）"
                ),
            ]
        );

    /// <summary>網羅しない組の一覧</summary>
    public static CoverageGapSet Gaps =>
        CoverageGap.Set(
            EngineIds.OsBoundary,
            [
                // 1 区間の名前の判定は、先頭の区切り・ドライブ指定・絶対パスの規則（Rooted）を返さない
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P3-10",
                    "Rooted は CheckRelativePath が先頭の区切り・ドライブ指定・絶対パスに返す規則で、1 区間の判定（CheckSegment）は"
                        + "区切りを Separator・':' を InvalidCharacter として返し Rooted を返さない。"
                        + "1 区間の名前を受け取る入口（API リファレンスと出力のファイル名・生成物のファイル名・プロジェクト名・画面のファイル名）は"
                        + "CheckSegment を呼ぶので Rooted に当たらない（src/QuickER.Platform/PathSegments.cs・"
                        + "src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs・GeneratedFileWriter.cs・"
                        + "src/QuickER.AI.Mock/MockProjectScaffoldService.cs・MockFolderStore.cs）",
                    Entrance,
                    Violation,
                    (entrance, violation) =>
                        violation == nameof(PathSegmentViolation.Rooted)
                        && entrance
                            is "CheckSegment"
                                or "ApiDocsFileName"
                                or "OutputFileName"
                                or "GeneratedFileName"
                                or "ProjectName"
                                or "ScreenFileName"
                ),
                // 相対パスでは区切りが区間の区切りで、区間の中に区切りは残らない
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P3-10",
                    "CheckRelativePath は区切りで分けた区間を CheckSegment で判定するので、区間の中の区切り（Separator）に当たらない。"
                        + "相対パスの入口（層フォルダ・コードのサブフォルダ・API リファレンスのサブフォルダ・提出のパス）でも"
                        + "区切りは正当な区間の区切りで、Separator の形は成り立たない（src/QuickER.Platform/PathSegments.cs）",
                    Entrance,
                    Violation,
                    (entrance, violation) =>
                        violation == nameof(PathSegmentViolation.Separator)
                        && entrance
                            is "CheckRelativePath"
                                or "DomainLayerDirectory"
                                or "CodeSubdirectory"
                                or "ApiDocsSubdirectory"
                                or "EmitPath"
                ),
                // 出力・API リファレンスのファイル名は、前後の空白を取り除き、空・空白だけなら検査しない
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P3-10",
                    "出力ファイル名と API リファレンスのファイル名は、空・空白だけなら検査せず（未指定の扱い）、前後の空白を取り除いてから"
                        + "CheckSegment へ渡すので、空と先頭の空白は規則に当たらない"
                        + "（src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs の ValidateOutputFileName・ValidateApiDocsFileName）",
                    Entrance,
                    Violation,
                    (entrance, violation) =>
                        entrance is "ApiDocsFileName" or "OutputFileName"
                        && violation
                            is nameof(PathSegmentViolation.Empty)
                                or nameof(PathSegmentViolation.LeadingWhitespace)
                ),
                // 拡張子を付けて検査する入口では、拡張子の手前の形が成り立たない
                CoverageGap.AcceptedMatching(
                    Scope,
                    "P3-10",
                    "生成物のファイル名は「.g.cs」・画面のファイル名は「.html」が付いた形で検査するので、空・'.'・'..'・末尾の '.' と空白の形は"
                        + "名前として成り立たない。生成物のファイル名はディレクトリ要素（区切り・ドライブ）を Path.GetFileName が先に落とすので"
                        + "区切りにも当たらない（src/QuickER.CodeGen.CSharp/GeneratedFileWriter.cs・src/QuickER.AI.Mock/MockFolderStore.cs の"
                        + "CheckScreenFileName。入口の宣言はテストの Applies のコメント）",
                    Entrance,
                    Violation,
                    (entrance, violation) =>
                        (
                            entrance == "GeneratedFileName"
                            && violation
                                is nameof(PathSegmentViolation.DotSegment)
                                    or nameof(PathSegmentViolation.Separator)
                                    or nameof(PathSegmentViolation.TrailingDotOrWhitespace)
                        )
                        || (
                            entrance == "ScreenFileName"
                            && violation
                                is nameof(PathSegmentViolation.Empty)
                                    or nameof(PathSegmentViolation.DotSegment)
                                    or nameof(PathSegmentViolation.TrailingDotOrWhitespace)
                        )
                ),
                CoverageGap.Matching(
                    Scope,
                    "P3-10",
                    "P4",
                    "元のテストが持たず、P3-10 では行を足していない：入口へ流す形の一覧に空の値が無い。生成物のファイル名とプロジェクト名は"
                        + "CheckSegment が空を Empty として断るが、その入力を流していない。提出のパスの空（'.' だけ）は"
                        + "パスの入口の表（PathEntrances）の行が見ている",
                    Entrance,
                    Violation,
                    (entrance, violation) =>
                        violation == nameof(PathSegmentViolation.Empty)
                        && entrance is "GeneratedFileName" or "ProjectName" or "EmitPath"
                ),
                CoverageGap.Matching(
                    Scope,
                    "P3-10",
                    "P4",
                    "元のテストが持たず、P3-10 では行を足していない：CheckRelativePath の形の一覧が区間の内側の使えない文字と上限超過を"
                        + "持たない（区間ごとの判定は CheckSegment の行が見ている）",
                    Entrance,
                    Violation,
                    (entrance, violation) =>
                        entrance == "CheckRelativePath"
                        && violation
                            is nameof(PathSegmentViolation.InvalidCharacter)
                                or nameof(PathSegmentViolation.TooLong)
                ),
            ]
        );

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.OsBoundary,
            "Segments",
            "名前の判定の表（入口 × 当たった規則。PathSegments の判定 2 つと、名前を受け取る 9 つの入口）",
            Table,
            PathSegmentForms.SegmentChecks.Count() + PathSegmentForms.RelativeChecks.Count(),
            [
                "tests/QuickER.Tests/Platform/PathSegmentsTests.cs",
                "tests/QuickER.Tests/Platform/PathSegmentEntranceFuzzTests.cs",
                "tests/QuickER.Tests/Platform/PathSegmentForms.cs",
                "tests/QuickER.Tests/Platform/OsBoundary/PathTableDimensions.cs",
            ],
            [
                "src/QuickER.Platform/PathSegments.cs",
                "src/QuickER.CodeGen.CSharp/CSharpCodeGenerationService.cs",
                "src/QuickER.CodeGen.CSharp/GeneratedFileWriter.cs",
                "src/QuickER.AI.Mock/MockFolderStore.cs",
                "src/QuickER.AI.Mock/MockProjectScaffoldService.cs",
                "src/QuickER.AI.Mock/MockProjectEmitTools.cs",
            ]
        );

    /// <summary>1 つ流した後、入口と当たった規則を数える（断らなかったら <paramref name="violation"/> は null）</summary>
    public static void Observe(
        CoverageCounter counter,
        string entrance,
        PathSegmentViolation? violation
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Entrance] = entrance,
                [Violation] = violation?.ToString() ?? Accepted,
            }
        );
}
