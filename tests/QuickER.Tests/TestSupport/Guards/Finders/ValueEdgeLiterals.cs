using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>
/// 値の端の値をプール（<c>TestSupport/ValuePools</c>）の外に直に書いた箇所の判定（規則 <c>value-edge-literal-copies</c> の見つける形）
/// </summary>
/// <remarks>
/// <para>
/// tests のソースはシンボルで組まない（<see cref="SourceSymbolScan"/> は src だけ）ので、名前と構文で読む。型を書かない
/// <c>new(...)</c>（目標型の生成）は型が分からないので拾わない。コメント・文字列の中の綴りは構文の節にならないので拾わない。
/// </para>
/// <para>
/// 何か（名前）は <see cref="DateRangeEdge"/> ほか 7 つ。1 つの節は高々 1 つの何かを返す（引数の中の数のリテラルは節ごとに別の
/// 出現として数え、生成の節・呼び出しの節では数えない）。
/// </para>
/// <para>
/// Kind（<c>DateTimeKind.Utc</c>・<c>DateTimeKind.Local</c>）と型の端（数値の型・日時の型の <c>MaxValue</c> / <c>MinValue</c>）は拾わない。
/// BCL の定数そのものが正本で、プールの名前つきの定数へ包んでも値は変わらず写しの危険が無い（間接の層が増えるだけ）。
/// その水準がエンジンに届いているかは、値のプールの受ける水準の行列（<c>ValuePoolUseTests</c>・<c>ValuePoolUseIntegrationTests</c>）が
/// エンジンの入力を観測して見ている（水準 <c>dt-kind-utc</c>・<c>dt-kind-local</c>・<c>int-type-min</c>・<c>int-type-max</c>・
/// <c>dec-type-end</c>・<c>fp-type-end</c>・<c>dt-calendar-end</c>）。
/// </para>
/// </remarks>
internal static class ValueEdgeLiterals
{
    /// <summary>日時の範囲の端（<c>new DateTime(1752, ...)</c> など）</summary>
    public const string DateRangeEdge = "日時の範囲の端";

    /// <summary>1 tick の端（<c>AddTicks(-1)</c>・<c>AddTicks(1)</c>）</summary>
    public const string OneTickEdge = "1 tick の端";

    /// <summary>datetime の刻み（1/300 秒の刻みの tick 数・<c>TicksPerSecond / 300</c>）</summary>
    public const string DatetimeStep = "datetime の刻み";

    /// <summary>TimeSpan の端</summary>
    public const string TimeSpanEdge = "TimeSpan の端";

    /// <summary>浮動小数点の端（Epsilon・負のゼロ）</summary>
    public const string FloatingEdge = "浮動小数点の端";

    /// <summary>2^24・2^53 の端</summary>
    public const string PowerOfTwoEdge = "2^24・2^53 の端";

    /// <summary>ASCII の外の文字を含む短い文字列</summary>
    public const string NonAsciiText = "ASCII の外の文字";

    /// <summary>非 ASCII の短い文字列とみなす長さの上限（表示名・理由の文は長いので数えない）</summary>
    private const int ShortTextLength = 6;

    /// <summary>日時の生成の最初の引数が端になる年</summary>
    private static readonly HashSet<double> EdgeYears = [1, 1752, 1753, 1899, 1900, 2079, 9999];

    /// <summary>datetime の刻みの tick 数（31000 ほか。アンダースコア区切りの綴りも値は同じ）</summary>
    private static readonly HashSet<double> StepTicks = [31000, 68000, 33333, 66667];

    /// <summary>2^24・2^53 とその次の整数</summary>
    private static readonly HashSet<double> PowersOfTwo =
    [
        16777216,
        16777217,
        9007199254740992,
        9007199254740993,
    ];

    /// <summary>日時の型の名前（生成と MaxValue / MinValue）</summary>
    private static readonly HashSet<string> DateTypes = ["DateTime", "DateTimeOffset", "DateOnly"];

    /// <summary>節が拾う形ならその何か（拾わなければ null）</summary>
    public static string? Describe(SyntaxNode node) =>
        node switch
        {
            ObjectCreationExpressionSyntax creation => DescribeCreation(creation),
            InvocationExpressionSyntax invocation => DescribeInvocation(invocation),
            MemberAccessExpressionSyntax member => DescribeMember(member),
            BinaryExpressionSyntax binary => DescribeTickDivision(binary),
            PrefixUnaryExpressionSyntax prefix => DescribeNegativeZero(prefix),
            LiteralExpressionSyntax literal => DescribeLiteral(literal),
            _ => null,
        };

    private static string? DescribeCreation(ObjectCreationExpressionSyntax creation)
    {
        var type = LastSegment(creation.Type);
        var arguments = creation.ArgumentList?.Arguments ?? [];

        if (DateTypes.Contains(type))
        {
            return
                arguments.Count > 0
                && TryNumber(arguments[0].Expression, out var year)
                && EdgeYears.Contains(year)
                ? DateRangeEdge
                : null;
        }

        return type == "TimeSpan" && arguments.Any(argument => IsNumber(argument.Expression, 838))
            ? TimeSpanEdge
            : null;
    }

    private static string? DescribeInvocation(InvocationExpressionSyntax invocation)
    {
        var name = InvokedName(invocation);
        var arguments = invocation.ArgumentList.Arguments;

        if (
            name == "AddTicks"
            && arguments.Count == 1
            && TryNumber(arguments[0].Expression, out var ticks)
            && Math.Abs(ticks) == 1
        )
        {
            return OneTickEdge;
        }

        if (
            invocation.Expression is MemberAccessExpressionSyntax member
            && LastSegment(member.Expression) == "TimeSpan"
            && name is { } method
            && method.StartsWith("From", StringComparison.Ordinal)
            && (
                arguments.Any(argument => IsNumber(argument.Expression, 838))
                || (
                    method == "FromDays"
                    && arguments.Count > 0
                    && IsNumber(arguments[0].Expression, 1)
                )
            )
        )
        {
            return TimeSpanEdge;
        }

        return null;
    }

    private static string? DescribeMember(MemberAccessExpressionSyntax member)
    {
        var owner = LastSegment(member.Expression);

        return member.Name.Identifier.ValueText switch
        {
            "MaxValue" or "MinValue" when owner == "TimeSpan" => TimeSpanEdge,
            "Epsilon" when owner is "float" or "double" or "Single" or "Double" => FloatingEdge,
            _ => null,
        };
    }

    private static string? DescribeTickDivision(BinaryExpressionSyntax binary) =>
        binary.IsKind(SyntaxKind.DivideExpression)
        && IsNumber(binary.Right, 300)
        && binary
            .Left.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(name => name.Identifier.ValueText == "TicksPerSecond")
            ? DatetimeStep
            : null;

    private static string? DescribeNegativeZero(PrefixUnaryExpressionSyntax prefix) =>
        prefix.IsKind(SyntaxKind.UnaryMinusExpression)
        && prefix.Operand is LiteralExpressionSyntax literal
        && literal.IsKind(SyntaxKind.NumericLiteralExpression)
        && literal.Token.Value is float or double
        && TryNumber(literal, out var value)
        && value == 0
            ? FloatingEdge
            : null;

    /// <summary>
    /// 日本語の文言にだけ現れる文字（平仮名・中黒・和文の約物・全角の括弧と句読点）。これを含む短い文字列は失敗の報告・見出し・
    /// 区切りの文言で、値の端（ASCII の外の値）でない。値の端は漢字・片仮名・ラテン文字の拡張・全角の英数字・サロゲートで書く
    /// </summary>
    private static bool IsProseCharacter(char c) =>
        c is >= (char)0x3000 and <= (char)0x309F
        || c == (char)0x30FB
        || c
            is (char)0xFF01
                or (char)0xFF08
                or (char)0xFF09
                or (char)0xFF0C
                or (char)0xFF1A
                or (char)0xFF1B
                or (char)0xFF1F;

    private static string? DescribeLiteral(LiteralExpressionSyntax literal)
    {
        if (literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var text = literal.Token.ValueText;

            return
                text.Length is > 0 and <= ShortTextLength
                && text.Any(c => c > 0x7F)
                && !text.Any(IsProseCharacter)
                ? NonAsciiText
                : null;
        }

        if (!TryNumber(literal, out var value))
        {
            return null;
        }

        if (PowersOfTwo.Contains(value))
        {
            return PowerOfTwoEdge;
        }

        return StepTicks.Contains(value) && IsTickArgument(literal) ? DatetimeStep : null;
    }

    /// <summary>リテラルのいちばん近い引数が、<c>AddTicks</c>・<c>FromTicks</c>・<c>new TimeSpan</c> の引数か</summary>
    private static bool IsTickArgument(LiteralExpressionSyntax literal) =>
        literal.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault()?.Parent?.Parent switch
        {
            InvocationExpressionSyntax invocation => InvokedName(invocation)
                is "AddTicks"
                    or "FromTicks",
            ObjectCreationExpressionSyntax creation => LastSegment(creation.Type) == "TimeSpan",
            _ => false,
        };

    /// <summary>呼び出しのメソッド名（受け手つき・条件付きアクセス・受け手なし。それ以外の形は null）</summary>
    private static string? InvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };

    /// <summary>型・式の表記の最後の語（<c>System.DateTime</c> なら <c>DateTime</c>）</summary>
    private static string LastSegment(SyntaxNode node) => node.ToString().Split('.')[^1];

    private static bool IsNumber(ExpressionSyntax expression, double expected) =>
        TryNumber(expression, out var value) && value == expected;

    /// <summary>数のリテラル（括弧・単項のマイナスを許す）の値を倍精度で読む</summary>
    private static bool TryNumber(ExpressionSyntax expression, out double value)
    {
        value = 0;

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return TryNumber(parenthesized.Expression, out value);
            case PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.UnaryMinusExpression):
                if (!TryNumber(prefix.Operand, out value))
                {
                    return false;
                }

                value = -value;
                return true;
            case LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.NumericLiteralExpression):
                if (literal.Token.Value is not { } raw)
                {
                    return false;
                }

                try
                {
                    value = Convert.ToDouble(
                        raw,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException)
                {
                    return false;
                }
            default:
                return false;
        }
    }
}
