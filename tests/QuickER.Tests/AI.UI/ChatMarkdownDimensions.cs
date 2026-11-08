using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.AI.UI;

/// <summary>AI の回答の Markdown 表示の表の要素</summary>
internal enum MarkdownElement
{
    /// <summary>インラインのリンク <c>[t](u)</c></summary>
    InlineLink,

    /// <summary>参照のリンク <c>[t][r]</c> と定義 <c>[r]: u</c></summary>
    ReferenceLink,

    /// <summary>山括弧の自動リンク <c>&lt;u&gt;</c></summary>
    AngleAutolink,

    /// <summary>拡張の自動リンク（文中に裸で書いた URL）</summary>
    BareAutolink,

    /// <summary>画像 <c>![a](u)</c></summary>
    Image,

    /// <summary>リンクの中の画像 <c>[![a](i)](u)</c></summary>
    LinkedImage,

    /// <summary>文中に書いた HTML</summary>
    HtmlInline,

    /// <summary>ブロックとして書いた HTML</summary>
    HtmlBlock,

    /// <summary>入れ子の深さ（強調・引用・リスト）</summary>
    Nesting,

    /// <summary>開いてよい URL の規則（<c>UrlPolicy.TryGetSafeUri</c> の直接の呼び出し）</summary>
    Policy,
}

/// <summary>AI の回答の Markdown 表示の表の入力の形（要素の族ごとに定義する）</summary>
internal enum MarkdownForm
{
    /// <summary>URL：開いてよい（http・https・www）</summary>
    Allowed,

    /// <summary>URL：断るスキーム（file・javascript・data・vbscript・ms-settings・vscode・ftp・tel・mailto・メール）</summary>
    Refused,

    /// <summary>URL：スキームの大文字小文字</summary>
    Case,

    /// <summary>URL：前後・スキームの前の空白、タブ・制御文字、URL の中の空白</summary>
    Whitespace,

    /// <summary>URL：HTML の文字参照・パーセント・逆斜線のエスケープ</summary>
    Escaped,

    /// <summary>URL：空・空白だけ・null</summary>
    Empty,

    /// <summary>URL：相対・<c>//server/share</c>・UNC</summary>
    Network,

    /// <summary>URL：ユーザー情報を持つ</summary>
    UserInfo,

    /// <summary>URL：国際化ドメイン名</summary>
    Idn,

    /// <summary>HTML：タグ（飾り・アンカー）</summary>
    Tag,

    /// <summary>HTML：script</summary>
    Script,

    /// <summary>HTML：img タグ</summary>
    ImgTag,

    /// <summary>HTML：コメント</summary>
    Comment,

    /// <summary>HTML：文字参照</summary>
    Entity,

    /// <summary>深さ：測った深さがちょうど上限（32）</summary>
    AtLimit,

    /// <summary>深さ：上限を 1 つ超える（33）</summary>
    OverLimit,

    /// <summary>深さ：上限を大きく超える（200 段）</summary>
    Far,
}

/// <summary>AI の回答の Markdown 表示の表の次元表・網羅しない組の一覧・台帳の行</summary>
/// <remarks>
/// 1 行＝要素 1 つ × 入力の形 1 つ（表示の行は次元を持たない）。形は要素の族（URL・HTML・深さ）ごとに定義するので、
/// 族の違う組は書けない（<see cref="IsValid"/>）。行の宣言を数える（行を流さない）。
/// </remarks>
internal static class ChatMarkdownDimensions
{
    /// <summary>網羅しない組の照合の範囲</summary>
    public const string Scope = "ai-markdown-render";

    /// <summary>要素</summary>
    public const string Element = "Element";

    /// <summary>入力の形</summary>
    public const string Form = "Form";

    /// <summary>URL を持つ要素</summary>
    public static readonly MarkdownElement[] UrlElements =
    [
        MarkdownElement.InlineLink,
        MarkdownElement.ReferenceLink,
        MarkdownElement.AngleAutolink,
        MarkdownElement.BareAutolink,
        MarkdownElement.Image,
        MarkdownElement.LinkedImage,
        MarkdownElement.Policy,
    ];

    /// <summary>URL の形</summary>
    public static readonly MarkdownForm[] UrlForms =
    [
        MarkdownForm.Allowed,
        MarkdownForm.Refused,
        MarkdownForm.Case,
        MarkdownForm.Whitespace,
        MarkdownForm.Escaped,
        MarkdownForm.Empty,
        MarkdownForm.Network,
        MarkdownForm.UserInfo,
        MarkdownForm.Idn,
    ];

    /// <summary>HTML の形</summary>
    public static readonly MarkdownForm[] HtmlForms =
    [
        MarkdownForm.Tag,
        MarkdownForm.Script,
        MarkdownForm.ImgTag,
        MarkdownForm.Comment,
        MarkdownForm.Entity,
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.Ai,
            [
                new Factor(
                    Element,
                    Enum.GetValues<MarkdownElement>().Select(e => new Level(e.ToString())).ToList(),
                    "Markdown の要素（リンク・自動リンク・参照・画像・HTML・入れ子・URL の規則）"
                ),
                new Factor(
                    Form,
                    Enum.GetValues<MarkdownForm>().Select(f => new Level(f.ToString())).ToList(),
                    "入力の形（URL・HTML・深さの族ごと）"
                ),
            ],
            null,
            IsValid
        );

    /// <summary>網羅しない組の一覧（空。族の中のすべての組を行が流す）</summary>
    public static CoverageGapSet Gaps => CoverageGap.Set(EngineIds.Ai, []);

    /// <summary>行の宣言を数える</summary>
    public static void Observe(
        CoverageCounter counter,
        MarkdownElement element,
        MarkdownForm form
    ) =>
        counter.Observe(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Element] = element.ToString(),
                [Form] = form.ToString(),
            }
        );

    /// <summary>組み合わせられない組</summary>
    /// <remarks>
    /// 形は要素の族ごとに定義する（URL の形は URL を持つ要素だけ・HTML の形は HTML の要素だけ・深さの形は入れ子だけ）。
    /// 族の違う形はその要素に書けない（例：入れ子の深さに「断るスキーム」は無い）。
    /// </remarks>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        if (
            !assignment.TryGetValue(Element, out var element)
            || !assignment.TryGetValue(Form, out var form)
        )
        {
            return true;
        }

        return FamilyOf(Enum.Parse<MarkdownElement>(element))
            == FamilyOf(Enum.Parse<MarkdownForm>(form));
    }

    /// <summary>要素の族</summary>
    private static int FamilyOf(MarkdownElement element) =>
        element switch
        {
            MarkdownElement.HtmlInline or MarkdownElement.HtmlBlock => 1,
            MarkdownElement.Nesting => 2,
            _ => 0,
        };

    /// <summary>形の族</summary>
    private static int FamilyOf(MarkdownForm form) =>
        UrlForms.Contains(form) ? 0
        : HtmlForms.Contains(form) ? 1
        : 2;

    /// <summary>台帳の行</summary>
    public static EngineDescriptor Descriptor =>
        new(
            EngineIds.Ai,
            "MarkdownRender",
            "AI の回答の Markdown 表示の表（要素 × 入力の形。リンクは http / https だけ・画像を読み込まない・HTML を解釈しない・入れ子の上限でテキスト表示へ）",
            Table,
            ChatMarkdownRows.Table.Count,
            [
                "tests/QuickER.Tests/AI.UI/ChatMarkdownDimensions.cs",
                "tests/QuickER.Tests/AI.UI/ChatMarkdownRows.cs",
                "tests/QuickER.Tests/AI.UI/ChatMarkdownRows.Blocks.cs",
                "tests/QuickER.Tests/AI.UI/ChatMarkdownRendererTests.cs",
            ],
            [
                "src/QuickER.AI.UI/ChatMarkdownRenderer.cs",
                "src/QuickER.AI.UI/AssistantMessageView.cs",
                "src/QuickER.Platform/UrlPolicy.cs",
            ]
        );
}
