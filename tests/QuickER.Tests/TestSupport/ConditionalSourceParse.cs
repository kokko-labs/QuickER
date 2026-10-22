using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// ソースを、条件コンパイル（<c>#if</c> / <c>#elif</c>）が試すプリプロセッサの記号のすべての組み合わせでパースする
/// （ソースを走査するガードの土台）
/// </summary>
/// <remarks>
/// <para>
/// 1 通りの記号でパースすると、その記号で外れる分岐（<c>#if !DEBUG</c> の中・<c>#if QUICKER_*</c> の <c>#else</c> など）は
/// 無効なテキスト（トリビア）になり、走査から見えない。ここでは、ファイルの <c>#if</c> / <c>#elif</c> の条件に現れる記号を
/// 構文から集め、その記号の定義の有無のすべての組み合わせ（2 の記号の数の乗）で 1 本ずつ構文木を作る。
/// 記号が無いファイルは、基準の設定でパースした 1 本だけを返す（従来の走査と同じ結果になる）。
/// </para>
/// <para>
/// 組み合わせの先頭は必ず基準の設定と同じもの（構文木もそのときの 1 本）にする。記号が <see cref="MaxSymbols"/> を超える
/// ファイルは、組み合わせを黙って間引かずに、ファイル名を添えて例外にする。
/// </para>
/// <para>
/// 複数の構文木を走査すると、分岐の外の節は構文木の数だけ現れる。<see cref="DescendantNodes(IReadOnlyList{SyntaxTree}, CancellationToken)"/> と
/// <see cref="DistinctAcrossVariants{T}"/> は、節の種類・範囲・トークンの並びが同じものを 1 つにまとめる
/// （範囲はファイルの文字の位置なので、同じファイルの構文木どうしでだけ比べられる＝まとめるのはファイルごとに行う）。
/// 範囲だけで比べないのは、分岐を含む節（クラス宣言など）は範囲が同じでも組み合わせごとに子が違うため。
/// </para>
/// </remarks>
internal static class ConditionalSourceParse
{
    /// <summary>1 つのファイル（またはプロジェクト）で試す記号の上限（2^6＝64 通り）</summary>
    public const int MaxSymbols = 6;

    /// <summary>ソースを、条件コンパイルの記号のすべての組み合わせでパースする（先頭は基準の設定のもの）</summary>
    /// <param name="text">ソースの本文</param>
    /// <param name="baseOptions">基準の設定（省略は <see cref="CSharpParseOptions.Default"/>＝記号なし）</param>
    /// <param name="path">構文木のパス（上限を超えたときの例外にも出す）</param>
    /// <param name="cancellationToken">取り消し</param>
    public static IReadOnlyList<SyntaxTree> Parse(
        string text,
        CSharpParseOptions? baseOptions = null,
        string path = "",
        CancellationToken cancellationToken = default
    )
    {
        var options = baseOptions ?? CSharpParseOptions.Default;
        var baseTree = CSharpSyntaxTree.ParseText(
            text,
            options,
            path,
            cancellationToken: cancellationToken
        );
        var symbols = ConditionalSymbols(baseTree, cancellationToken);

        if (symbols.Count == 0)
        {
            return [baseTree];
        }

        return
        [
            baseTree,
            .. VariantOptions(options, symbols, path)
                .Skip(1)
                .Select(variant =>
                    CSharpSyntaxTree.ParseText(
                        text,
                        variant,
                        path,
                        cancellationToken: cancellationToken
                    )
                ),
        ];
    }

    /// <summary>
    /// 構文木の <c>#if</c> / <c>#elif</c> の条件に現れる記号（無効な分岐の中の入れ子の条件も含む・序数順）
    /// </summary>
    public static IReadOnlyList<string> ConditionalSymbols(
        SyntaxTree tree,
        CancellationToken cancellationToken = default
    )
    {
        var root = tree.GetRoot(cancellationToken);

        if (!root.ContainsDirectives)
        {
            return [];
        }

        return root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .OfType<ConditionalDirectiveTriviaSyntax>()
            .SelectMany(directive =>
                directive
                    .Condition.DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>()
                    .Select(name => name.Identifier.ValueText)
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 記号の定義の有無のすべての組み合わせのパース設定（先頭は <paramref name="baseOptions"/> そのもの）。
    /// 記号に挙げていない基準の記号（<c>TRACE</c> など）はそのまま残す
    /// </summary>
    /// <exception cref="InvalidOperationException">記号が <see cref="MaxSymbols"/> を超える</exception>
    public static IReadOnlyList<CSharpParseOptions> VariantOptions(
        CSharpParseOptions baseOptions,
        IReadOnlyList<string> symbols,
        string path
    )
    {
        if (symbols.Count > MaxSymbols)
        {
            throw new InvalidOperationException(
                $"{path}: 条件コンパイルの記号が {symbols.Count} 個ある（上限は {MaxSymbols} 個＝{1 << MaxSymbols} 通り）。"
                    + $"記号: {string.Join(", ", symbols)}。組み合わせを間引かずに止める（上限を上げるか、分岐を減らす）"
            );
        }

        var defined = baseOptions.PreprocessorSymbolNames.ToHashSet(StringComparer.Ordinal);
        var others = defined.Where(symbol => !symbols.Contains(symbol)).ToArray();
        var baseMask = Enumerable
            .Range(0, symbols.Count)
            .Where(index => defined.Contains(symbols[index]))
            .Sum(index => 1 << index);

        return Enumerable
            .Range(0, 1 << symbols.Count)
            .Select(flip =>
                flip == 0
                    ? baseOptions
                    : baseOptions.WithPreprocessorSymbols(
                        others.Concat(
                            Enumerable
                                .Range(0, symbols.Count)
                                .Where(index => ((baseMask ^ flip) & (1 << index)) != 0)
                                .Select(index => symbols[index])
                        )
                    )
            )
            .ToList();
    }

    /// <summary>同じファイルの全組み合わせの構文木の節（組み合わせをまたいで同じ節は 1 回だけ）</summary>
    public static IEnumerable<SyntaxNode> DescendantNodes(
        IReadOnlyList<SyntaxTree> variants,
        CancellationToken cancellationToken = default
    ) =>
        variants.Count == 1
            ? variants[0].GetRoot(cancellationToken).DescendantNodes()
            : DistinctAcrossVariants(
                variants.SelectMany(tree => tree.GetRoot(cancellationToken).DescendantNodes()),
                node => node
            );

    /// <summary>同じファイルの全組み合わせの構文木のトークン（組み合わせをまたいで同じトークンは 1 回だけ）</summary>
    public static IEnumerable<SyntaxToken> DescendantTokens(
        IReadOnlyList<SyntaxTree> variants,
        CancellationToken cancellationToken = default
    )
    {
        if (variants.Count == 1)
        {
            return variants[0].GetRoot(cancellationToken).DescendantTokens();
        }

        var seen = new HashSet<(int Kind, TextSpan Span, string Text)>();

        return variants
            .SelectMany(tree => tree.GetRoot(cancellationToken).DescendantTokens())
            .Where(token => seen.Add((token.RawKind, token.Span, token.Text)));
    }

    /// <summary>
    /// 同じファイルの組み合わせをまたいで、同じ節（種類・範囲・トークンの並びが同じ）を指す項目を最初の 1 つにまとめる
    /// </summary>
    /// <param name="items">同じファイルの項目（別のファイルの項目を混ぜない）</param>
    /// <param name="nodeOf">項目が指す節</param>
    public static IEnumerable<T> DistinctAcrossVariants<T>(
        IEnumerable<T> items,
        Func<T, SyntaxNode> nodeOf
    )
    {
        var seen = new HashSet<(int Kind, TextSpan Span, string Tokens)>();

        foreach (var item in items)
        {
            var node = nodeOf(item);

            // トークンの区切りに NUL を挟む（"ab"+"c" と "a"+"bc" を同じにしない）
            var tokens = string.Join('\0', node.DescendantTokens().Select(token => token.Text));

            if (seen.Add((node.RawKind, node.Span, tokens)))
            {
                yield return item;
            }
        }
    }
}
