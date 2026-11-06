using Microsoft.CodeAnalysis;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の 1 行（走査する範囲・見つける形・照合の形・理由つきの既知の一覧）</summary>
/// <remarks>
/// <para>
/// 走査・照合・報告は共有の 1 か所（<see cref="GuardScan"/> と <see cref="SourceRuleTests"/>）が持ち、規則は宣言だけを持つ。
/// 見つけた出現は「ファイル・何か（<see cref="GuardHit.What"/>）」のキーごとの件数にして、既知の一覧と件数まで 2 方向で照合する
/// （<see cref="GuardKnownList.AssertMatches"/>）。一覧に無い出現も、一覧にあるのに無い出現も赤。
/// </para>
/// <para>
/// 照合の形：<see cref="GuardExpectation.Forbidden"/> は禁止の出現を数え、一覧は理由つきの例外。
/// <see cref="GuardExpectation.Required"/> はあるべき出現を数え、一覧は必須の出現（無くなると赤）。どちらも件数まで厳密。
/// </para>
/// </remarks>
/// <param name="Id">規則の Id（一意）</param>
/// <param name="Because">守ること（一覧に無い出現・無くなった必須の出現の報告に添える）</param>
/// <param name="Expectation">照合の形</param>
/// <param name="Scope">走査する範囲</param>
/// <param name="Finder">見つける形</param>
/// <param name="Known">既知の一覧（禁止なら理由つきの例外・必須ならあるべき出現）</param>
internal sealed record SourceRule(
    string Id,
    string Because,
    GuardExpectation Expectation,
    SourceScope Scope,
    GuardFinder Finder,
    IReadOnlyList<GuardKnown> Known
)
{
    /// <summary>空振りの検知：範囲に入るファイルの最低数（範囲の書き間違いで何も読まないのを止める）</summary>
    public int MinimumFiles { get; init; } = 1;

    /// <summary>空振りの検知：同じ見つける形を別の範囲に当てた結果の厳密な期待（走査が拾えることの確認。無ければ持たない）</summary>
    public GuardWitness? Witness { get; init; }

    /// <summary>空振りの検知：範囲の中の候補（規則が調べる対象）の数の下限（候補を見つけられなくなったのを止める。無ければ持たない）</summary>
    public GuardFloor? Floor { get; init; }

    /// <summary>禁止の出現を数える規則</summary>
    public static SourceRule Forbid(
        string id,
        string because,
        SourceScope scope,
        GuardFinder finder,
        params GuardKnown[] known
    ) => new(id, because, GuardExpectation.Forbidden, scope, finder, known);

    /// <summary>あるべき出現を数える規則</summary>
    public static SourceRule Require(
        string id,
        string because,
        SourceScope scope,
        GuardFinder finder,
        params GuardKnown[] required
    ) => new(id, because, GuardExpectation.Required, scope, finder, required);
}

/// <summary>照合の形</summary>
internal enum GuardExpectation
{
    /// <summary>禁止の出現を数える（一覧は理由つきの例外）</summary>
    Forbidden,

    /// <summary>あるべき出現を数える（一覧は必須の出現）</summary>
    Required,
}

/// <summary>読み方</summary>
internal enum GuardReader
{
    /// <summary>構文木の節（条件コンパイルのどの分岐も読む）</summary>
    SyntaxNodes,

    /// <summary>構文木のトークン（条件コンパイルのどの分岐も読む）</summary>
    Tokens,

    /// <summary>シンボル（src のプロジェクトのコンパイルと、条件コンパイルの変種のコンパイル）</summary>
    Symbols,

    /// <summary>本文の文字列（構文木を作らない。条件コンパイルの外れる分岐・コメントの中も読む）</summary>
    Text,

    /// <summary>XML（XAML・csproj）</summary>
    Xml,
}

/// <summary>既知の一覧の 1 項目（ファイル・何か・件数・理由）</summary>
/// <param name="File">リポジトリのルートからの相対パス（区切りは <c>/</c>）</param>
/// <param name="What">何か（見つける形が返す表記）</param>
/// <param name="Count">件数</param>
/// <param name="Reason">理由（例外なら正当な理由・必須ならあるべき理由）</param>
internal sealed record GuardKnown(string File, string What, int Count, string Reason);

/// <summary>空振りの検知：見つける形を別の範囲に当てた結果の厳密な期待</summary>
/// <param name="Scope">当てる範囲</param>
/// <param name="Expected">期待（件数まで厳密）</param>
internal sealed record GuardWitness(SourceScope Scope, IReadOnlyList<GuardKnown> Expected);

/// <summary>空振りの検知：規則の範囲の中の候補の数の下限</summary>
/// <param name="Candidates">候補を見つける形（規則の見つける形と同じ読み方の種類〔シンボルかファイル〕）</param>
/// <param name="AtLeast">候補の数の下限</param>
/// <param name="What">候補が何か（報告用）</param>
internal sealed record GuardFloor(GuardFinder Candidates, int AtLeast, string What);

/// <summary>見つけた出現 1 つ</summary>
/// <param name="File">リポジトリのルートからの相対パス（区切りは <c>/</c>）</param>
/// <param name="What">何か</param>
/// <param name="Line">行（1 始まり）</param>
internal sealed record GuardHit(string File, string What, int Line);

/// <summary>ファイル 1 つから見つけた出現（何か・行）</summary>
/// <param name="What">何か</param>
/// <param name="Line">行（1 始まり）</param>
internal readonly record struct GuardFinding(string What, int Line);

/// <summary>見つける形（読み方ごとの具象）</summary>
internal abstract record GuardFinder
{
    /// <summary>読み方</summary>
    public abstract GuardReader Reader { get; }

    /// <summary>構文木の節ごとに、拾う節なら何かを返す</summary>
    public static GuardFinder Nodes(Func<SyntaxNode, string?> describe) => new NodeFinder(describe);

    /// <summary>構文木のトークンごとに、拾うトークンなら何かを返す</summary>
    public static GuardFinder Tokens(Func<SyntaxToken, string?> describe) =>
        new TokenFinder(describe);

    /// <summary>節と意味モデルごとに、拾う節なら何かを返す</summary>
    public static GuardFinder Symbols(Func<SemanticModel, SyntaxNode, string?> describe) =>
        new SymbolFinder(describe);

    /// <summary>ファイル全体から出現を返す（文字列で読むもの・XML・ファイル全体の文脈が要る構文の判定）</summary>
    /// <param name="reader">読み方（<see cref="GuardReader.Symbols"/> は使えない）</param>
    /// <param name="find">ファイルから出現を返す</param>
    public static GuardFinder File(
        GuardReader reader,
        Func<GuardFile, IEnumerable<GuardFinding>> find
    ) =>
        reader == GuardReader.Symbols
            ? throw new ArgumentException("シンボルは Symbols で見つける", nameof(reader))
            : new FileFinder(reader, find);
}

/// <summary>構文木の節の見つける形</summary>
internal sealed record NodeFinder(Func<SyntaxNode, string?> Describe) : GuardFinder
{
    /// <inheritdoc/>
    public override GuardReader Reader => GuardReader.SyntaxNodes;
}

/// <summary>構文木のトークンの見つける形</summary>
internal sealed record TokenFinder(Func<SyntaxToken, string?> Describe) : GuardFinder
{
    /// <inheritdoc/>
    public override GuardReader Reader => GuardReader.Tokens;
}

/// <summary>シンボルの見つける形</summary>
internal sealed record SymbolFinder(Func<SemanticModel, SyntaxNode, string?> Describe) : GuardFinder
{
    /// <inheritdoc/>
    public override GuardReader Reader => GuardReader.Symbols;
}

/// <summary>ファイル全体の見つける形</summary>
internal sealed record FileFinder(
    GuardReader FileReader,
    Func<GuardFile, IEnumerable<GuardFinding>> Find
) : GuardFinder
{
    /// <inheritdoc/>
    public override GuardReader Reader => FileReader;
}
