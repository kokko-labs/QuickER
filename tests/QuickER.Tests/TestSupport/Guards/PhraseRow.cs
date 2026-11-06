namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表の 1 行：出どころの文字列に CJK が無いことを、理由つきの例外の一覧と照合する</summary>
/// <remarks>
/// 判定は <see cref="CjkText.Pattern"/> の 1 つ。出どころは関数で返す（resx の値・ファイルの行・定数・ツール定義・プロンプト・
/// UI 言語を ja にして実行した出力）。見つけた CJK は「どこか（<see cref="EnglishText.Where"/>）」ごとの件数にして、
/// 例外の一覧と件数まで 2 方向で照合する（<see cref="GuardKnownList.AssertMatches"/>）。
/// </remarks>
/// <param name="Id">行の Id（一意）</param>
/// <param name="Because">守ること</param>
/// <param name="Texts">走査する文字列（どこか・本文）</param>
/// <param name="Known">例外（どこか・件数・理由）</param>
internal sealed record EnglishTextRow(
    string Id,
    string Because,
    Func<IEnumerable<EnglishText>> Texts,
    params EnglishException[] Known
)
{
    /// <summary>空振りの検知：走査する文字列の最低数</summary>
    public int MinimumTexts { get; init; } = 1;
}

/// <summary>英語の文言の表の走査する文字列 1 つ</summary>
/// <param name="Where">どこか（ファイル・キー・ツール名など。報告と例外のキー）</param>
/// <param name="Text">本文</param>
internal sealed record EnglishText(string Where, string Text);

/// <summary>英語の文言の表の例外 1 つ</summary>
/// <param name="Where">どこか</param>
/// <param name="Count">件数</param>
/// <param name="Reason">理由</param>
internal sealed record EnglishException(string Where, int Count, string Reason);

/// <summary>必須の文言の表の 1 行：出どころの文字列が、決めた語を含み、決めた語を含まないこと</summary>
/// <param name="Id">行の Id（一意）</param>
/// <param name="Because">守ること</param>
/// <param name="Text">出どころの文字列（UI 言語などの条件は関数の中で整える）</param>
/// <param name="Contains">含むべき語（序数で照合）</param>
/// <param name="NotContains">含んではいけない語（序数で照合）</param>
internal sealed record PhraseRow(
    string Id,
    string Because,
    Func<string> Text,
    IReadOnlyList<string> Contains,
    IReadOnlyList<string> NotContains
);

/// <summary>英語の文言の表（領域ごとの行は partial のファイルに置く）</summary>
internal static partial class EnglishTextRows
{
    private static readonly Lazy<IReadOnlyList<EnglishTextRow>> LazyAll = new(() =>
        [.. Resources(), .. Samples(), .. Mcp(), .. CodeGen(), .. AiChat(), .. AiMock()]
    );

    /// <summary>すべての行（宣言の順）</summary>
    public static IReadOnlyList<EnglishTextRow> All => LazyAll.Value;

    /// <summary>Id から行を引く</summary>
    public static EnglishTextRow ById(string id) =>
        All.SingleOrDefault(row => row.Id == id)
        ?? throw new ArgumentException($"英語の文言の行が無い: {id}", nameof(id));
}

/// <summary>必須の文言の表（領域ごとの行は partial のファイルに置く）</summary>
internal static partial class PhraseRows
{
    private static readonly Lazy<IReadOnlyList<PhraseRow>> LazyAll = new(() =>
        [.. AiChat(), .. AiMock()]
    );

    /// <summary>すべての行（宣言の順）</summary>
    public static IReadOnlyList<PhraseRow> All => LazyAll.Value;

    /// <summary>Id から行を引く</summary>
    public static PhraseRow ById(string id) =>
        All.SingleOrDefault(row => row.Id == id)
        ?? throw new ArgumentException($"必須の文言の行が無い: {id}", nameof(id));
}
