using System.Text;
using QuickER.Dialects;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>識別子の表の関数（行がどの関数を呼ぶか）</summary>
internal static class DialectIdentifierFunctions
{
    /// <summary>テーブル名のクォート（最初の <c>.</c> で分ける）</summary>
    public const string Quote = "quote";

    /// <summary>単一の識別子のクォート</summary>
    public const string QuoteSimple = "quote-simple";

    /// <summary>終端クォート文字のエスケープ</summary>
    public const string Escape = "escape";

    /// <summary>動的 SQL の文字列リテラルの中へ埋めるクォート</summary>
    public const string Dynamic = "dynamic";

    /// <summary>生成ランタイム（固定フィクスチャ）のテーブル名の引用</summary>
    public const string RuntimeQuote = "runtime-quote";

    /// <summary><see cref="SchemaQualifiedName"/> の分け方（方言なし）</summary>
    public const string Split = "split";

    /// <summary>すべての関数（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [Quote, QuoteSimple, Escape, Dynamic, RuntimeQuote, Split];
}

/// <summary>識別子の表の入力の形</summary>
internal static class DialectIdentifierShapes
{
    /// <summary>区切りも特別な文字も無い名前</summary>
    public const string Plain = "plain";

    /// <summary><c>.</c> を 1 つ含む名前</summary>
    public const string Dot = "dot";

    /// <summary><c>.</c> を 2 つ以上含む名前</summary>
    public const string MultiDot = "multi-dot";

    /// <summary><c>.</c> で分けた部分が空になる名前（<c>.a</c>・<c>a.</c>・<c>..</c>）</summary>
    public const string EmptyPart = "empty-part";

    /// <summary>その方言の終端クォート文字を含む名前</summary>
    public const string ClosingQuote = "closing-quote";

    /// <summary><c>.</c> で分けた両方の部分に終端クォート文字を含む名前</summary>
    public const string DotClosingQuote = "dot-closing-quote";

    /// <summary>クォート開始文字（終端と違う方言だけ）を含む名前</summary>
    public const string OpeningQuote = "opening-quote";

    /// <summary><c>'</c> を含む名前</summary>
    public const string SingleQuote = "single-quote";

    /// <summary>改行を含む名前</summary>
    public const string NewLine = "newline";

    /// <summary>空白・記号・日本語を含む名前</summary>
    public const string NonAscii = "non-ascii";

    /// <summary>空の名前</summary>
    public const string Empty = "empty";

    /// <summary>null</summary>
    public const string Null = "null";

    /// <summary><c>\</c> を含む名前</summary>
    public const string Backslash = "backslash";

    /// <summary>
    /// <c>.</c> で分けた両方の部分に、どれかの方言のクォート文字（<c>]</c>・<c>"</c>）を含む名前（方言によって終端の文字かどうかが違う）
    /// </summary>
    public const string QuoteCharsInParts = "quote-chars-in-parts";

    /// <summary>すべての形（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Plain,
        Dot,
        MultiDot,
        EmptyPart,
        ClosingQuote,
        DotClosingQuote,
        OpeningQuote,
        SingleQuote,
        NewLine,
        NonAscii,
        Empty,
        Null,
        Backslash,
        QuoteCharsInParts,
    ];
}

/// <summary>識別子の表の 1 方言の入力と期待</summary>
/// <param name="Input">関数へ渡す名前（null も渡す）</param>
/// <param name="Expected">関数の出力の期待（完全一致。分け方は <see cref="DialectIdentifierRow.SplitResult"/> の書式）</param>
internal sealed record IdentifierExpectation(string? Input, string Expected);

/// <summary>識別子の表の 1 行の中身</summary>
/// <param name="Function">関数（<see cref="DialectIdentifierFunctions"/>）</param>
/// <param name="Shape">入力の形（<see cref="DialectIdentifierShapes"/>）</param>
/// <param name="Expected">方言（方言なしの関数は <see cref="DialectDdlAxis.None"/>）ごとの入力と期待</param>
internal sealed record DialectIdentifierCase(
    string Function,
    string Shape,
    IReadOnlyDictionary<string, IdentifierExpectation> Expected
);

/// <summary>識別子の表の行の組み立て</summary>
/// <remarks>
/// 入力は方言ごとに違う名前（スキーマ名の慣用・その方言の終端クォート文字）で書く。
/// </remarks>
internal sealed class DialectIdentifierRow
{
    private readonly Dictionary<string, IdentifierExpectation> _expected = new(
        StringComparer.Ordinal
    );

    private DialectIdentifierRow(string id, string description, string function, string shape)
    {
        Id = id;
        Description = description;
        Function = function;
        Shape = shape;
    }

    /// <summary>行の Id</summary>
    public string Id { get; }

    /// <summary>説明（テストの表示名）</summary>
    public string Description { get; }

    /// <summary>関数</summary>
    public string Function { get; }

    /// <summary>入力の形</summary>
    public string Shape { get; }

    /// <summary>行を作る</summary>
    /// <param name="id">行の Id</param>
    /// <param name="description">説明</param>
    /// <param name="function">関数（<see cref="DialectIdentifierFunctions"/>）</param>
    /// <param name="shape">入力の形（<see cref="DialectIdentifierShapes"/>）</param>
    public static DialectIdentifierRow Of(
        string id,
        string description,
        string function,
        string shape
    ) => new(id, description, function, shape);

    /// <summary>分け方の期待の書式（<c>schema= name= qualified= try= try-schema= try-name=</c>）</summary>
    /// <param name="schema">スキーマ（分けられなければ null）</param>
    /// <param name="name">表の名前</param>
    public static string SplitResult(string? schema, string name)
    {
        var qualified = schema is not null;
        return $"schema={Show(schema)} name={Show(name)} qualified={qualified} try={qualified} try-schema={Show(schema)} try-name={Show(name)}";
    }

    /// <summary>方言の入力と期待を書く</summary>
    /// <param name="dialect">方言（<see cref="DialectNames"/> の定数か <see cref="DialectDdlAxis.None"/>）</param>
    /// <param name="input">入力</param>
    /// <param name="expected">期待（完全一致）</param>
    public DialectIdentifierRow Expect(string dialect, string? input, string expected)
    {
        if (!_expected.TryAdd(dialect, new IdentifierExpectation(input, expected)))
        {
            throw new InvalidOperationException($"行 {Id} の方言 {dialect} の期待が重複している");
        }

        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<DialectIdentifierCase> ToScenario() =>
        new(
            Id,
            null,
            Description,
            new DialectIdentifierCase(
                Function,
                Shape,
                new Dictionary<string, IdentifierExpectation>(_expected, StringComparer.Ordinal)
            )
        );

    /// <summary>値を 1 行で見せる（null は <c>(null)</c>・制御文字はエスケープ表記）</summary>
    /// <param name="value">値</param>
    public static string Show(string? value)
    {
        if (value is null)
        {
            return "(null)";
        }

        var builder = new StringBuilder("\"");

        foreach (var c in value)
        {
            builder.Append(
                c switch
                {
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                    _ => c.ToString(),
                }
            );
        }

        return builder.Append('"').ToString();
    }
}

/// <summary>識別子の表の実行部：行を 1 方言で流して出力を返す</summary>
internal static class DialectIdentifierRunner
{
    /// <summary>行を 1 方言で流して照合し、問題を返す（無ければ空）</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言（方言なしの関数は <see cref="DialectDdlAxis.None"/>）</param>
    public static IReadOnlyList<string> Check(DialectIdentifierCase row, string dialect)
    {
        var expectation = row.Expected[dialect];
        var actual = Run(row.Function, dialect, expectation.Input);

        return actual == expectation.Expected
            ? []
            :
            [
                $"期待 {DialectIdentifierRow.Show(expectation.Expected)}・実際 {DialectIdentifierRow.Show(actual)}",
            ];
    }

    /// <summary>関数を 1 方言で呼ぶ（製品が投げた例外は <c>throws {型名}</c> にする）</summary>
    /// <param name="function">関数</param>
    /// <param name="dialect">方言</param>
    /// <param name="input">入力</param>
    public static string Run(string function, string dialect, string? input)
    {
        try
        {
            if (function == DialectIdentifierFunctions.Split)
            {
                return SplitOutcome(input!);
            }

            var adapter = DialectDdlAxis.Get(dialect);

            return function switch
            {
                DialectIdentifierFunctions.Quote => adapter.Quote(input),
                DialectIdentifierFunctions.QuoteSimple => adapter.QuoteSimple(input),
                DialectIdentifierFunctions.Escape => adapter.Escape(input),
                DialectIdentifierFunctions.Dynamic => (
                    adapter.QuoteForDynamicSql
                    ?? throw new InvalidOperationException(
                        $"{dialect} は動的 SQL 用のクォートを持たない（行の期待の書き間違い）"
                    )
                )(input!),
                DialectIdentifierFunctions.RuntimeQuote => (
                    adapter.RuntimeQuoteTableName
                    ?? throw new InvalidOperationException(
                        $"{dialect} は生成ランタイムを持たない（行の期待の書き間違い）"
                    )
                )(input!),
                _ => throw new InvalidOperationException($"知らない関数 {function}"),
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return $"throws {ex.GetType().Name}";
        }
    }

    private static string SplitOutcome(string tableName)
    {
        var (schema, name) = SchemaQualifiedName.Split(tableName);
        var qualified = SchemaQualifiedName.IsQualified(tableName);
        var tried = SchemaQualifiedName.TrySplit(tableName, out var trySchema, out var tryName);

        return $"schema={DialectIdentifierRow.Show(schema)} name={DialectIdentifierRow.Show(name)} qualified={qualified} try={tried} try-schema={DialectIdentifierRow.Show(trySchema)} try-name={DialectIdentifierRow.Show(tryName)}";
    }
}
