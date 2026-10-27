using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成コードへ埋め込む文字列リテラル（簡易 DSL の条件の文字列・生 SQL・図の名前）が、UTF-16 の全コード単位
/// （U+0000〜U+FFFF・対になっていないサロゲートを含む）について、ファイルへ UTF-8 で書いて読み直したあとも
/// 元の値のままであることと、リテラルの中に制御文字・対になっていないサロゲートを生のまま書かないことを固定する。
/// </summary>
/// <remarks>
/// <para>
/// 生成ファイルは UTF-8 で書かれる。対になっていないサロゲートは UTF-8 で表せず U+FFFD に置き換わるので、
/// 生のまま書くと、コンパイルされる値が図の値と黙って食い違う（条件の比較の相手・SQL・列名が変わる）。
/// </para>
/// <para>
/// 値は生成物を構文解析して、文字列リテラルのトークンの値（コンパイラが使う値と同じ）で読む。コンパイルはしない。
/// </para>
/// <para>既知の失敗は台帳（<see cref="Known"/>）に載せる。突き合わせの規則は <see cref="KnownCaseLedger"/>。</para>
/// </remarks>
public class QueryLiteralEscapeScanTests(ITestOutputHelper output)
{
    /// <summary>既知の失敗（ケース ID → 期待する失敗の理由の断片）</summary>
    private static readonly KnownCaseLedger Known = new("QueryLiteralEscapeScanTests", []);

    /// <summary>
    /// 全コード単位を 1 本の文字列に並べる。上位サロゲートは上位サロゲートの直前に、下位サロゲートは
    /// 下位サロゲートかサロゲートでない文字の直後に置いて、どれも対にならないようにする。最後に正しい対を足す
    /// </summary>
    private static string AllCodeUnits()
    {
        var builder = new StringBuilder(0x10000 + 16);

        for (var c = 0; c < 0xD800; c++)
        {
            builder.Append((char)c);
        }

        for (var c = 0xD800; c <= 0xDBFF; c++)
        {
            builder.Append((char)c);
        }

        builder.Append('X');

        for (var c = 0xDC00; c <= 0xDFFF; c++)
        {
            builder.Append((char)c);
        }

        for (var c = 0xE000; c <= 0xFFFF; c++)
        {
            builder.Append((char)c);
        }

        // 正しい対（絵文字・CJK 拡張 B）は 1 文字として残る
        builder.Append("\U0001F600\U00020000");

        return builder.ToString();
    }

    /// <summary>生成ファイルを UTF-8 で書いて読み直したのと同じ内容にする</summary>
    private static string ThroughUtf8File(string content) =>
        Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(content));

    /// <summary>
    /// 生成物の中の文字列リテラル（連結を含む）の値を、構文解析で読む。<paramref name="selector"/> が対象の式を選ぶ
    /// </summary>
    private static (string Value, string SourceText) ReadLiteral(
        CodeGenerationResult result,
        Func<SyntaxNode, ExpressionSyntax?> selector
    )
    {
        foreach (
            var file in result.Files.Where(file =>
                file.FileName.EndsWith(".cs", StringComparison.Ordinal)
            )
        )
        {
            var root = CSharpSyntaxTree.ParseText(ThroughUtf8File(file.Content)).GetRoot();

            if (selector(root) is { } expression)
            {
                var literals = expression
                    .DescendantNodesAndSelf()
                    .OfType<LiteralExpressionSyntax>()
                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                    .ToList();

                return (
                    string.Concat(literals.Select(literal => literal.Token.ValueText)),
                    string.Concat(literals.Select(literal => literal.Token.Text))
                );
            }
        }

        throw new InvalidOperationException("対象の式が生成物に見つからない");
    }

    /// <summary>リテラルの元の綴りに、生のままの制御文字・対になっていないサロゲートがあれば最初の位置を返す</summary>
    private static string? FirstRawUnsafeChar(string sourceText)
    {
        for (var i = 0; i < sourceText.Length; i++)
        {
            var c = sourceText[i];

            if (
                char.IsHighSurrogate(c)
                && i + 1 < sourceText.Length
                && char.IsLowSurrogate(sourceText[i + 1])
            )
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c) || c < 0x20 || c == 0x7F)
            {
                return $"U+{(int)c:X4} at {i}";
            }
        }

        return null;
    }

    /// <summary>期待値と最初に食い違う位置の説明（一致すれば null）</summary>
    private static string? FirstMismatch(string expected, string actual)
    {
        var length = Math.Min(expected.Length, actual.Length);

        for (var i = 0; i < length; i++)
        {
            if (expected[i] != actual[i])
            {
                return $"index {i}: expected U+{(int)expected[i]:X4}, actual U+{(int)actual[i]:X4}";
            }
        }

        return expected.Length == actual.Length
            ? null
            : $"length: expected {expected.Length}, actual {actual.Length}";
    }

    /// <summary>memo（nvarchar(max)）を持つ Notes の図と生成のための部品</summary>
    private static (ErDiagram Diagram, Entity Notes) CreateDiagram()
    {
        var notes = new Entity { TableName = "Notes" };
        notes.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        notes.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = "nvarchar(max)",
                IsNullable = true,
            }
        );

        return (new ErDiagram { Entities = { notes } }, notes);
    }

    /// <summary>SQL Server の QuickER 版 Repository で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        var provider = new SqlServerProvider();

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                IncludeDataAnnotations = true,
            }
        );
    }

    [Fact(
        DisplayName = "簡易 DSL の条件の文字列は、全コード単位が UTF-8 のファイルを通っても元の値のままで、制御文字を生で書かない"
    )]
    public void DslStringLiteral_PreservesEveryCodeUnit() =>
        Known.Run(
            "dsl-literal",
            () =>
            {
                var watch = Stopwatch.StartNew();
                var value = AllCodeUnits();
                var (diagram, notes) = CreateDiagram();
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = notes.Id,
                        Name = "ByMemo",
                        Implementation = QueryImplementationKind.Dsl,
                        Condition = $"memo = '{value.Replace("'", "''")}'",
                    }
                );

                var result = Generate(diagram);
                result
                    .HasErrors.Should()
                    .BeFalse(string.Join("\n", result.Diagnostics.Select(d => d.Message)));

                var (actual, sourceText) = ReadLiteral(
                    result,
                    root =>
                        root.DescendantNodes()
                            .OfType<LiteralExpressionSyntax>()
                            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                            .OrderByDescending(literal => literal.Token.ValueText.Length)
                            .FirstOrDefault(literal => literal.Token.ValueText.Length > 1000)
                );
                output.WriteLine($"DSL: {watch.ElapsedMilliseconds} ms");

                FirstMismatch(value, actual)
                    .Should()
                    .BeNull("UTF-8 のファイルを通っても元の値のまま");
                FirstRawUnsafeChar(sourceText)
                    .Should()
                    .BeNull("リテラルに制御文字・対になっていないサロゲートを生で書かない");
            }
        );

    [Fact(
        DisplayName = "生 SQL は、全コード単位が UTF-8 のファイルを通っても元の値のままで、制御文字を生で書かない"
    )]
    public void RawSqlLiteral_PreservesEveryCodeUnit() =>
        Known.Run(
            "sql-literal",
            () =>
            {
                var watch = Stopwatch.StartNew();
                var sql =
                    "SELECT * FROM Notes WHERE memo = N'" + AllCodeUnits().Replace("'", "''") + "'";
                var (diagram, notes) = CreateDiagram();
                diagram.Queries.Add(
                    new QueryDefinition
                    {
                        EntityId = notes.Id,
                        Name = "ByMemo",
                        Implementation = QueryImplementationKind.Sql,
                        Sql = { ["sqlserver"] = sql },
                    }
                );

                var result = Generate(diagram);
                result
                    .HasErrors.Should()
                    .BeFalse(string.Join("\n", result.Diagnostics.Select(d => d.Message)));

                var (actual, sourceText) = ReadLiteral(
                    result,
                    root =>
                        root.DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .FirstOrDefault(invocation =>
                                invocation
                                    .Expression.ToString()
                                    .EndsWith("QueryBySqlAsync", StringComparison.Ordinal)
                            )
                            ?.ArgumentList.Arguments[0]
                            .Expression
                );
                output.WriteLine($"SQL: {watch.ElapsedMilliseconds} ms");

                FirstMismatch(sql, actual)
                    .Should()
                    .BeNull("UTF-8 のファイルを通っても元の値のまま");
                FirstRawUnsafeChar(sourceText)
                    .Should()
                    .BeNull("リテラルに制御文字・対になっていないサロゲートを生で書かない");
            }
        );

    [Fact(
        DisplayName = "図の名前（列名）の対になっていないサロゲートは、UTF-8 のファイルを通っても元の値のまま"
    )]
    public void ColumnNameLiteral_PreservesLoneSurrogates() =>
        Known.Run(
            "name-literal",
            () =>
            {
                var name = "a\uD800b\uDC00c";
                var (diagram, notes) = CreateDiagram();
                notes.Columns.Add(
                    new Column
                    {
                        Name = name,
                        DataType = "int",
                        IsNullable = true,
                    }
                );

                var result = Generate(diagram);
                result
                    .HasErrors.Should()
                    .BeFalse(string.Join("\n", result.Diagnostics.Select(d => d.Message)));

                var (actual, _) = ReadLiteral(
                    result,
                    root =>
                        root.DescendantNodes()
                            .OfType<AttributeSyntax>()
                            .Where(attribute =>
                                attribute.Name.ToString() is "Column" or "ColumnAttribute"
                            )
                            .Select(attribute => attribute.ArgumentList?.Arguments[0].Expression)
                            .FirstOrDefault(expression =>
                                expression is LiteralExpressionSyntax literal
                                && literal.Token.ValueText.StartsWith('a')
                                && literal.Token.ValueText.EndsWith('c')
                            )
                );

                FirstMismatch(name, actual).Should().BeNull("UTF-8 のファイルを通っても列名のまま");
            }
        );
}
