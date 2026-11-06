using System.Globalization;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 決まった行が生成した API リファレンス（<c>.g.md</c>）へ表明する期待 1 つ（Markdown の構文木で見る）
/// </summary>
/// <remarks>
/// <para>
/// Markdown を Markdig（パイプ表つき）で構文木にし、見出し・表のセル・リンク・コードブロックを見る。セルの値はインラインを組み立て直した文
/// （エスケープを外した値。コードの span は中身）で、完全一致で比べる。csharp のコードブロックは Roslyn の構文木にして、呼び出すメソッドの名前と
/// 作る型（<c>new X(...)</c>）の名前を集める（コンパイルしない）。生成テキストの部分文字列・正規表現・行では判定しない。
/// </para>
/// <para>表は見出しの行のセルの名前で選ぶ（言語ごとに見出しの名前が違うので、行は英語版・日本語版のファイルごとに書く）。</para>
/// </remarks>
internal abstract record MarkdownRule
{
    /// <summary>照合する（問題を返す）</summary>
    /// <param name="document">選んだファイルの構文木</param>
    /// <param name="result">生成結果（ファイル名と照らす規則が読む）</param>
    protected abstract IEnumerable<string> Check(
        MarkdownDocument document,
        CodeGenerationResult result
    );

    /// <summary>規則を当てるファイル名</summary>
    public abstract string File { get; }

    /// <summary>ファイルを選んで照合する（問題を返す）</summary>
    /// <param name="result">生成結果</param>
    public IReadOnlyList<string> Run(CodeGenerationResult result)
    {
        var files = result.Files.Where(file => file.FileName == File).ToList();

        if (files.Count != 1)
        {
            return
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Markdown: {File} が {files.Count} 個（期待 1 個）"
                ),
            ];
        }

        var document = Markdown.Parse(files[0].Content, Pipeline);
        return Check(document, result).Select(problem => $"Markdown（{File}）: {problem}").ToList();
    }

    /// <summary>
    /// 列 <paramref name="keyColumn"/> の値が <paramref name="key"/> の行の、列 <paramref name="column"/> の値が <paramref name="expected"/>
    /// （両方の列を見出しに持つ表のうち、その行を持つものがちょうど 1 つ）
    /// </summary>
    public static MarkdownRule Cell(
        string file,
        string keyColumn,
        string key,
        string column,
        string expected
    ) => new CellRule(file, keyColumn, key, column, expected);

    /// <summary>列 <paramref name="column"/> を見出しに持つ表の全部の、その列のセルに <paramref name="values"/> が全部ある（包含）</summary>
    public static MarkdownRule ColumnContains(
        string file,
        string column,
        IReadOnlyList<string> values
    ) => new ColumnRule(file, column, values, Exact: false, AgainstFiles: false);

    /// <summary>列 <paramref name="column"/> を見出しに持つ表の全部の、その列のセルの集合が <paramref name="values"/> と完全一致する</summary>
    public static MarkdownRule ColumnEquals(
        string file,
        string column,
        IReadOnlyList<string> values
    ) => new ColumnRule(file, column, values, Exact: true, AgainstFiles: false);

    /// <summary>列 <paramref name="column"/> を見出しに持つ表のセルの集合が、生成結果の <c>.g.cs</c> のファイル名の集合と完全一致する</summary>
    public static MarkdownRule ColumnEqualsGeneratedCodeFiles(string file, string column) =>
        new ColumnRule(file, column, [], Exact: true, AgainstFiles: true);

    /// <summary>どの表も、各行のセルの数が見出しの行のセルの数と同じ（セルの中の改行で行が割れていない）</summary>
    public static MarkdownRule TablesWellFormed(string file) => new TablesWellFormedRule(file);

    /// <summary>レベル <paramref name="level"/> の見出しがちょうど <paramref name="count"/> 個</summary>
    public static MarkdownRule HeadingCount(string file, int level, int count) =>
        new HeadingCountRule(file, level, count);

    /// <summary>リンクの URL に <paramref name="url"/> がある</summary>
    public static MarkdownRule Link(string file, string url) => new LinkRule(file, url);

    /// <summary>csharp のコードブロックがちょうど <paramref name="count"/> 個</summary>
    public static MarkdownRule CodeBlockCount(string file, int count) =>
        new CodeBlockCountRule(file, count);

    /// <summary>csharp のコードブロックが呼ぶメソッドの名前に <paramref name="method"/> がある（<paramref name="present"/> が false なら無い）</summary>
    public static MarkdownRule CodeInvokes(string file, string method, bool present = true) =>
        new CodeInvokesRule(file, method, present);

    /// <summary>csharp のコードブロックが作る型（<c>new X(...)</c>・<c>new X { }</c>）の名前の集合が <paramref name="types"/> と完全一致する</summary>
    public static MarkdownRule CodeCreatesExactly(string file, IReadOnlyList<string> types) =>
        new CodeCreatesRule(file, types);

    /// <summary>レベル <paramref name="level"/> の見出しに、文 <paramref name="text"/> のものがちょうど 1 つある（<paramref name="present"/> が false なら 1 つも無い）</summary>
    public static MarkdownRule Heading(string file, int level, string text, bool present = true) =>
        new HeadingRule(file, level, text, present);

    /// <summary>
    /// 表のセルのうち、バックスラッシュのエスケープで書かれた文字（<c>\|</c> の <c>|</c> など）の集合が <paramref name="characters"/> と完全一致する
    /// （「Markdown の安全化はパイプのエスケープだけ」など、読者に見えないエスケープが紛れ込まないこと）
    /// </summary>
    public static MarkdownRule EscapedCharactersInCells(
        string file,
        IReadOnlyList<string> characters
    ) => new EscapedCharactersRule(file, characters);

    /// <summary>
    /// ファイルの内容が、別の生成結果（<paramref name="reference"/>）の <paramref name="referenceFile"/> の内容と全体で一致する
    /// （言語の指定だけが違う生成で、同じ言語のファイルが同じになる、など）
    /// </summary>
    public static MarkdownRule SameContentAs(
        string file,
        Func<CodeGenerationResult> reference,
        string referenceFile
    ) => new SameContentRule(file, reference, referenceFile);

    /// <summary>Markdown の構文木を組む設定（生成器が書くパイプ表を読む）</summary>
    private static MarkdownPipeline Pipeline { get; } =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    /// <summary>表（見出しの行のセルの名前と、本体の行のセルの値）</summary>
    protected sealed record TableData(
        IReadOnlyList<string> Header,
        IReadOnlyList<IReadOnlyList<string>> Rows
    )
    {
        public int IndexOf(string column) =>
            Header
                .ToList()
                .FindIndex(name => string.Equals(name, column, StringComparison.Ordinal));
    }

    /// <summary>文書の表を全部読む</summary>
    protected static IReadOnlyList<TableData> Tables(MarkdownDocument document) =>
        document
            .Descendants<Table>()
            .Select(table =>
            {
                var rows = table.OfType<TableRow>().ToList();
                var header = rows.FirstOrDefault(row => row.IsHeader);
                return new TableData(
                    header is null ? [] : Cells(header),
                    rows.Where(row => !row.IsHeader).Select(Cells).ToList()
                );
            })
            .ToList();

    private static IReadOnlyList<string> Cells(TableRow row) =>
        row.OfType<TableCell>()
            .Select(cell =>
                string.Concat(
                    cell.Descendants<ParagraphBlock>().Select(paragraph => Text(paragraph.Inline))
                )
            )
            .ToList();

    /// <summary>インラインを組み立て直した文（エスケープを外した値）</summary>
    private static string Text(ContainerInline? container)
    {
        if (container is null)
        {
            return "";
        }

        var builder = new System.Text.StringBuilder();

        foreach (var inline in container)
        {
            builder.Append(
                inline switch
                {
                    LiteralInline literal => literal.Content.ToString(),
                    CodeInline code => code.Content,
                    LineBreakInline => "\n",
                    HtmlEntityInline entity => entity.Transcoded.ToString(),
                    HtmlInline html => html.Tag,
                    ContainerInline nested => Text(nested),
                    _ => "",
                }
            );
        }

        return builder.ToString().Trim();
    }

    /// <summary>csharp のコードブロックの構文木の根</summary>
    protected static IReadOnlyList<CompilationUnitSyntax> CodeBlocks(MarkdownDocument document)
    {
        var blocks = document
            .Descendants<FencedCodeBlock>()
            .Where(block => string.Equals(block.Info, "csharp", StringComparison.Ordinal))
            .Select(block => block.Lines.ToString())
            .ToList();

        if (blocks.Count == 0)
        {
            return [];
        }

        // パースは GeneratedCodeCompiler の 1 か所だけで行う（SourceRuleTests の規則 direct-roslyn-parses）。コンパイルはしない
        var compilation = GeneratedCodeCompiler.CreateCompilation(
            blocks.Select((code, index) => ($"Example{index}.cs", code)),
            $"QuickER.CodegenConfig.Markdown.{Guid.NewGuid():N}"
        );
        return compilation
            .SyntaxTrees.Select(tree => (CompilationUnitSyntax)tree.GetRoot())
            .ToList();
    }

    private sealed record CellRule(
        string FileName,
        string KeyColumn,
        string Key,
        string Column,
        string Expected
    ) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var hits = Tables(document)
                .Where(table => table.IndexOf(KeyColumn) >= 0 && table.IndexOf(Column) >= 0)
                .SelectMany(table =>
                    table
                        .Rows.Where(row => row.ElementAtOrDefault(table.IndexOf(KeyColumn)) == Key)
                        .Select(row => row.ElementAtOrDefault(table.IndexOf(Column)))
                )
                .ToList();

            if (hits.Count != 1)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{KeyColumn}={Key} の行が {hits.Count} 個（列 {Column} を持つ表でちょうど 1 つ）"
                );
            }
            else if (hits[0] != Expected)
            {
                yield return $"{KeyColumn}={Key} の行の {Column} が「{hits[0]}」（期待「{Expected}」）";
            }
        }
    }

    private sealed record ColumnRule(
        string FileName,
        string Column,
        IReadOnlyList<string> Values,
        bool Exact,
        bool AgainstFiles
    ) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var tables = Tables(document).Where(table => table.IndexOf(Column) >= 0).ToList();

            if (tables.Count == 0)
            {
                yield return $"列 {Column} を持つ表が無い";
                yield break;
            }

            var cells = tables
                .SelectMany(table =>
                    table.Rows.Select(row => row.ElementAtOrDefault(table.IndexOf(Column)) ?? "")
                )
                .ToHashSet(StringComparer.Ordinal);
            var expected = AgainstFiles
                ? result
                    .Files.Where(file => file.FileName.EndsWith(".g.cs", StringComparison.Ordinal))
                    .Select(file => file.FileName)
                    .ToHashSet(StringComparer.Ordinal)
                : Values.ToHashSet(StringComparer.Ordinal);

            if (Exact ? !cells.SetEquals(expected) : !cells.IsSupersetOf(expected))
            {
                yield return $"列 {Column} のセルが [{string.Join(", ", cells.Order(StringComparer.Ordinal))}]（期待 {(Exact ? "ちょうど" : "少なくとも")} [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]）";
            }
        }
    }

    private sealed record TablesWellFormedRule(string FileName) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var tables = Tables(document);

            if (tables.Count == 0)
            {
                yield return "表が無い";
            }

            foreach (var table in tables)
            {
                foreach (var row in table.Rows.Where(row => row.Count != table.Header.Count))
                {
                    yield return string.Create(
                        CultureInfo.InvariantCulture,
                        $"表 [{string.Join(", ", table.Header)}] の行 [{string.Join(", ", row)}] のセルが {row.Count} 個（見出しは {table.Header.Count} 個）"
                    );
                }
            }
        }
    }

    private sealed record HeadingCountRule(string FileName, int Level, int Count) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var count = document
                .Descendants<HeadingBlock>()
                .Count(heading => heading.Level == Level);

            if (count != Count)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"レベル {Level} の見出しが {count} 個（期待 {Count} 個）"
                );
            }
        }
    }

    private sealed record HeadingRule(string FileName, int Level, string Expected, bool Present)
        : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var count = document
                .Descendants<HeadingBlock>()
                .Count(heading => heading.Level == Level && Text(heading.Inline) == Expected);

            if (Present ? count != 1 : count != 0)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"レベル {Level} の見出し「{Expected}」が {count} 個（期待 {(Present ? "1" : "0")} 個）"
                );
            }
        }
    }

    private sealed record EscapedCharactersRule(string FileName, IReadOnlyList<string> Characters)
        : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var escaped = document
                .Descendants<TableCell>()
                .SelectMany(cell => cell.Descendants<LiteralInline>())
                .Where(literal => literal.IsFirstCharacterEscaped)
                .Select(literal => literal.Content.ToString()[..1])
                .ToHashSet(StringComparer.Ordinal);

            if (!escaped.SetEquals(Characters))
            {
                yield return $"表のセルでエスケープされた文字が [{string.Join(", ", escaped.Order(StringComparer.Ordinal))}]（期待 [{string.Join(", ", Characters.Order(StringComparer.Ordinal))}]）";
            }
        }
    }

    private sealed record SameContentRule(
        string FileName,
        Func<CodeGenerationResult> Reference,
        string ReferenceFile
    ) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var actual = result.Files.Single(file => file.FileName == FileName).Content;
            var other = Reference().Files.Where(file => file.FileName == ReferenceFile).ToList();

            if (other.Count != 1)
            {
                yield return $"比べる先の生成結果に {ReferenceFile} が {other.Count} 個（期待 1 個）";
            }
            else if (!string.Equals(actual, other[0].Content, StringComparison.Ordinal))
            {
                yield return $"{FileName} の内容が、比べる先の {ReferenceFile} と違う";
            }
        }
    }

    private sealed record LinkRule(string FileName, string Url) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            if (!document.Descendants<LinkInline>().Any(link => link.Url == Url))
            {
                yield return $"リンク {Url} が無い";
            }
        }
    }

    private sealed record CodeBlockCountRule(string FileName, int Count) : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var count = document
                .Descendants<FencedCodeBlock>()
                .Count(block => string.Equals(block.Info, "csharp", StringComparison.Ordinal));

            if (count != Count)
            {
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"csharp のコードブロックが {count} 個（期待 {Count} 個）"
                );
            }
        }
    }

    private sealed record CodeInvokesRule(string FileName, string Method, bool Present)
        : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var invoked = CodeBlocks(document)
                .SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                .Select(invocation =>
                    invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                        SimpleNameSyntax name => name.Identifier.ValueText,
                        _ => "",
                    }
                )
                .ToHashSet(StringComparer.Ordinal);

            if (invoked.Contains(Method) != Present)
            {
                yield return $"コードブロックが {Method} を{(Present ? "呼ばない" : "呼ぶ（呼ばないはず）")}（呼ぶもの [{string.Join(", ", invoked.Order(StringComparer.Ordinal))}]）";
            }
        }
    }

    private sealed record CodeCreatesRule(string FileName, IReadOnlyList<string> Types)
        : MarkdownRule
    {
        public override string File => FileName;

        protected override IEnumerable<string> Check(
            MarkdownDocument document,
            CodeGenerationResult result
        )
        {
            var created = CodeBlocks(document)
                .SelectMany(root => root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                .Select(creation =>
                    creation.Type switch
                    {
                        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                        SimpleNameSyntax name => name.Identifier.ValueText,
                        var other => other.ToString(),
                    }
                )
                .ToHashSet(StringComparer.Ordinal);

            if (!created.SetEquals(Types))
            {
                yield return $"コードブロックが作る型が [{string.Join(", ", created.Order(StringComparer.Ordinal))}]（期待 [{string.Join(", ", Types.Order(StringComparer.Ordinal))}]）";
            }
        }
    }
}
