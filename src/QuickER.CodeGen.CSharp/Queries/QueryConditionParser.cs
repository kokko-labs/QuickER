using QuickER.CodeGen.CSharp.Resources;
using QuickER.Model;

namespace QuickER.CodeGen.CSharp.Queries;

/// <summary>
/// 名前付きクエリ条件（ミニ DSL）のパーサ兼検証器
/// </summary>
/// <remarks>
/// <para>
/// 文法（キーワードは大文字小文字を区別しない）:
/// </para>
/// <code>
/// condition := or
/// or        := and ( OR and )*
/// and       := unary ( AND unary )*
/// unary     := NOT unary | primary
/// primary   := '(' condition ')' | predicate
/// predicate := 列名 ( 比較演算子 operand
///                   | IS [NOT] NULL
///                   | [NOT] LIKE operand
///                   | [NOT] IN @パラメータ
///                   | [NOT] (CONTAINS | STARTSWITH | ENDSWITH) operand )
/// operand   := @パラメータ | 数値リテラル | '文字列リテラル'
/// </code>
/// <para>
/// 構文エラーは最初の 1 件で打ち切り（<see cref="ConditionParseResult.Root"/> は null）、
/// 検証エラー（列・パラメータの突合）は複数件を収集する。診断メッセージはローカライズ済み。
/// </para>
/// </remarks>
public static class QueryConditionParser
{
    /// <summary>条件式をパースする（検証なし。リネーム書き換え等の構文用途向け）</summary>
    /// <param name="conditionText">ミニ DSL の条件式</param>
    public static ConditionParseResult Parse(string conditionText)
    {
        ArgumentNullException.ThrowIfNull(conditionText);

        var result = new ConditionParseResult();
        var tokens = Tokenize(conditionText, result.Diagnostics);

        if (result.Diagnostics.Count > 0)
        {
            return result;
        }

        var parser = new Parser(tokens, result);
        var root = parser.ParseCondition();

        if (root is not null && !parser.IsAtEnd)
        {
            parser.ReportUnexpectedToken();
            root = null;
        }

        return root is null ? result : CloneWithRoot(result, root);
    }

    /// <summary>条件式をパースし、エンティティの列とクエリのパラメータ定義に対して検証する</summary>
    /// <param name="conditionText">ミニ DSL の条件式</param>
    /// <param name="entity">クエリが属するエンティティ（列名の解決先）</param>
    /// <param name="parameters">クエリのパラメータ定義（@参照の解決先）</param>
    public static ConditionParseResult ParseAndValidate(
        string conditionText,
        Entity entity,
        IReadOnlyList<QueryParameter> parameters
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(parameters);

        var result = Parse(conditionText);

        if (result.Root is null)
        {
            return result;
        }

        // 列参照の解決（大文字小文字は区別せず、モデル上の正準名へ寄せる）
        foreach (var column in result.ColumnReferences)
        {
            var resolved = entity.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, column.Text, StringComparison.OrdinalIgnoreCase)
            );

            if (resolved is null)
            {
                result.Diagnostics.Add(
                    new ConditionDiagnostic(
                        new QueryDiagnosticText(
                            nameof(Strings.CodeGen_Query_UnknownColumn),
                            column.Text,
                            entity.TableName
                        ),
                        column.Position,
                        column.Length
                    )
                );
            }
            else
            {
                column.ResolvedName = resolved.Name;
                column.ResolvedColumnId = resolved.Id;
            }
        }

        // パラメータ参照の解決
        foreach (var parameter in result.ParameterReferences)
        {
            var resolved = parameters.FirstOrDefault(p =>
                string.Equals(p.Name, parameter.Text, StringComparison.OrdinalIgnoreCase)
            );

            if (resolved is null)
            {
                result.Diagnostics.Add(
                    new ConditionDiagnostic(
                        new QueryDiagnosticText(
                            nameof(Strings.CodeGen_Query_UnknownParameter),
                            parameter.Text
                        ),
                        parameter.Position,
                        parameter.Length
                    )
                );
            }
            else
            {
                parameter.ResolvedName = resolved.Name;
            }
        }

        // 意味検証（IN×リスト型の整合・NULL 非許容列への IS NULL 禁止）
        ValidateSemantics(result.Root, entity, parameters, result.Diagnostics);

        return result;
    }

    /// <summary>構文木全体の意味検証（リストパラメータの用途整合・null 判定の列整合）を行う</summary>
    private static void ValidateSemantics(
        ConditionNode node,
        Entity entity,
        IReadOnlyList<QueryParameter> parameters,
        List<ConditionDiagnostic> diagnostics
    )
    {
        switch (node)
        {
            case LogicalNode logical:
                ValidateSemantics(logical.Left, entity, parameters, diagnostics);
                ValidateSemantics(logical.Right, entity, parameters, diagnostics);
                break;

            case NotNode not:
                ValidateSemantics(not.Operand, entity, parameters, diagnostics);
                break;

            case NullCheckNode nullCheck:
                // NULL 非許容列への IS NULL は意味がなく、値型列ではコンパイル不能なコードになるため弾く
                if (
                    nullCheck.Column.ResolvedColumnId is { } columnId
                    && entity.Columns.FirstOrDefault(c => c.Id == columnId) is { IsNullable: false }
                )
                {
                    diagnostics.Add(
                        new ConditionDiagnostic(
                            new QueryDiagnosticText(
                                nameof(Strings.CodeGen_Query_NullCheckOnNonNullableColumn),
                                nullCheck.Column.ResolvedName
                            ),
                            nullCheck.Column.Position,
                            nullCheck.Column.Length
                        )
                    );
                }

                break;

            case InNode inNode:
                CheckListParameter(inNode.Parameter, parameters, expectList: true, diagnostics);
                break;

            case ComparisonNode comparison:
                if (comparison.Operand is ParameterOperand comparisonParameter)
                {
                    CheckListParameter(
                        comparisonParameter,
                        parameters,
                        expectList: false,
                        diagnostics
                    );
                }

                break;

            case StringMatchNode match:
                if (match.Operand is ParameterOperand matchParameter)
                {
                    CheckListParameter(matchParameter, parameters, expectList: false, diagnostics);
                }

                break;
        }
    }

    /// <summary>パラメータのリスト型と使用箇所（IN か否か）の整合を検証する</summary>
    private static void CheckListParameter(
        ParameterOperand operand,
        IReadOnlyList<QueryParameter> parameters,
        bool expectList,
        List<ConditionDiagnostic> diagnostics
    )
    {
        var definition = parameters.FirstOrDefault(p =>
            string.Equals(p.Name, operand.Text, StringComparison.OrdinalIgnoreCase)
        );

        if (definition is null)
        {
            // 未定義は解決フェーズで報告済み
            return;
        }

        if (expectList && !definition.IsList)
        {
            diagnostics.Add(
                new ConditionDiagnostic(
                    new QueryDiagnosticText(
                        nameof(Strings.CodeGen_Query_InRequiresListParameter),
                        operand.Text
                    ),
                    operand.Position,
                    operand.Length
                )
            );
        }
        else if (!expectList && definition.IsList)
        {
            diagnostics.Add(
                new ConditionDiagnostic(
                    new QueryDiagnosticText(
                        nameof(Strings.CodeGen_Query_ListParameterOnlyWithIn),
                        operand.Text
                    ),
                    operand.Position,
                    operand.Length
                )
            );
        }
    }

    /// <summary>参照一覧・診断を引き継いだままルートを差し込んだ結果を作る</summary>
    private static ConditionParseResult CloneWithRoot(
        ConditionParseResult source,
        ConditionNode root
    )
    {
        var result = new ConditionParseResult { Root = root };
        result.Diagnostics.AddRange(source.Diagnostics);
        result.ColumnReferences.AddRange(source.ColumnReferences);
        result.ParameterReferences.AddRange(source.ParameterReferences);
        return result;
    }

    // ---------------- トークナイザ ----------------

    /// <summary>トークン種別</summary>
    private enum TokenKind
    {
        Identifier,
        Parameter,
        Number,
        String,
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
        LeftParen,
        RightParen,
        Minus,
        And,
        Or,
        Not,
        Is,
        Null,
        Like,
        In,
        Contains,
        StartsWith,
        EndsWith,
        End,
    }

    /// <summary>トークン 1 件（原文位置つき）</summary>
    private readonly record struct Token(TokenKind Kind, string Text, int Position, int Length);

    /// <summary>キーワードの対応表（大文字小文字を区別しない）</summary>
    private static readonly Dictionary<string, TokenKind> Keywords = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["AND"] = TokenKind.And,
        ["OR"] = TokenKind.Or,
        ["NOT"] = TokenKind.Not,
        ["IS"] = TokenKind.Is,
        ["NULL"] = TokenKind.Null,
        ["LIKE"] = TokenKind.Like,
        ["IN"] = TokenKind.In,
        ["CONTAINS"] = TokenKind.Contains,
        ["STARTSWITH"] = TokenKind.StartsWith,
        ["ENDSWITH"] = TokenKind.EndsWith,
    };

    /// <summary>条件式をトークン列へ分解する（エラーは診断へ追加して打ち切る）</summary>
    private static List<Token> Tokenize(string text, List<ConditionDiagnostic> diagnostics)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new Token(TokenKind.LeftParen, "(", i, 1));
                i++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new Token(TokenKind.RightParen, ")", i, 1));
                i++;
                continue;
            }

            if (c == '-')
            {
                tokens.Add(new Token(TokenKind.Minus, "-", i, 1));
                i++;
                continue;
            }

            if (c == '=')
            {
                tokens.Add(new Token(TokenKind.Equal, "=", i, 1));
                i++;
                continue;
            }

            if (c == '<')
            {
                if (i + 1 < text.Length && text[i + 1] == '>')
                {
                    tokens.Add(new Token(TokenKind.NotEqual, "<>", i, 2));
                    i += 2;
                }
                else if (i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenKind.LessOrEqual, "<=", i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Less, "<", i, 1));
                    i++;
                }

                continue;
            }

            if (c == '>')
            {
                if (i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenKind.GreaterOrEqual, ">=", i, 2));
                    i += 2;
                }
                else
                {
                    tokens.Add(new Token(TokenKind.Greater, ">", i, 1));
                    i++;
                }

                continue;
            }

            if (c == '!')
            {
                if (i + 1 < text.Length && text[i + 1] == '=')
                {
                    tokens.Add(new Token(TokenKind.NotEqual, "!=", i, 2));
                    i += 2;
                    continue;
                }

                diagnostics.Add(
                    new ConditionDiagnostic(
                        new QueryDiagnosticText(
                            nameof(Strings.CodeGen_Query_UnexpectedCharacter),
                            c
                        ),
                        i,
                        1
                    )
                );
                return tokens;
            }

            if (c == '@')
            {
                var start = i;
                i++;
                var nameStart = i;

                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                if (i == nameStart)
                {
                    diagnostics.Add(
                        new ConditionDiagnostic(
                            new QueryDiagnosticText(
                                nameof(Strings.CodeGen_Query_UnexpectedCharacter),
                                c
                            ),
                            start,
                            1
                        )
                    );
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Parameter, text[nameStart..i], start, i - start));
                continue;
            }

            if (c == '\'')
            {
                var start = i;
                i++;
                var value = new System.Text.StringBuilder();
                var closed = false;

                while (i < text.Length)
                {
                    if (text[i] == '\'')
                    {
                        // '' は ' のエスケープ
                        if (i + 1 < text.Length && text[i + 1] == '\'')
                        {
                            value.Append('\'');
                            i += 2;
                            continue;
                        }

                        i++;
                        closed = true;
                        break;
                    }

                    value.Append(text[i]);
                    i++;
                }

                if (!closed)
                {
                    diagnostics.Add(
                        new ConditionDiagnostic(
                            new QueryDiagnosticText(
                                nameof(Strings.CodeGen_Query_UnterminatedString)
                            ),
                            start,
                            text.Length - start
                        )
                    );
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.String, value.ToString(), start, i - start));
                continue;
            }

            if (char.IsDigit(c))
            {
                var start = i;

                while (i < text.Length && char.IsDigit(text[i]))
                {
                    i++;
                }

                if (
                    i < text.Length
                    && text[i] == '.'
                    && i + 1 < text.Length
                    && char.IsDigit(text[i + 1])
                )
                {
                    i++;

                    while (i < text.Length && char.IsDigit(text[i]))
                    {
                        i++;
                    }
                }

                tokens.Add(new Token(TokenKind.Number, text[start..i], start, i - start));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;

                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                var word = text[start..i];
                var kind = Keywords.TryGetValue(word, out var keyword)
                    ? keyword
                    : TokenKind.Identifier;
                tokens.Add(new Token(kind, word, start, i - start));
                continue;
            }

            diagnostics.Add(
                new ConditionDiagnostic(
                    new QueryDiagnosticText(nameof(Strings.CodeGen_Query_UnexpectedCharacter), c),
                    i,
                    1
                )
            );
            return tokens;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length, 0));
        return tokens;
    }

    // ---------------- 再帰下降パーサ ----------------

    /// <summary>トークン列から構文木を組み立てる再帰下降パーサ（構文エラーは最初の 1 件で打ち切り）</summary>
    private sealed class Parser(List<Token> tokens, ConditionParseResult result)
    {
        private int _index;

        /// <summary>現在のトークン</summary>
        private Token Current => tokens[_index];

        /// <summary>すべて消費し終えたか（End のみ残っているか）</summary>
        public bool IsAtEnd => Current.Kind == TokenKind.End;

        /// <summary>現在位置のトークンを「想定外」として診断に追加する</summary>
        public void ReportUnexpectedToken()
        {
            var message = IsAtEnd
                ? new QueryDiagnosticText(nameof(Strings.CodeGen_Query_UnexpectedEnd))
                : new QueryDiagnosticText(
                    nameof(Strings.CodeGen_Query_UnexpectedToken),
                    Current.Text
                );
            result.Diagnostics.Add(
                new ConditionDiagnostic(message, Current.Position, Current.Length)
            );
        }

        /// <summary>現在のトークン（Parameter 前提）を <see cref="ParameterOperand"/> として消費し、参照一覧へ登録する</summary>
        private ParameterOperand ConsumeParameter()
        {
            var parameter = new ParameterOperand
            {
                Text = Current.Text,
                Position = Current.Position,
                Length = Current.Length,
            };
            result.ParameterReferences.Add(parameter);
            _index++;
            return parameter;
        }

        /// <summary>現在のトークン（String 前提）を <see cref="StringOperand"/> として消費する</summary>
        private StringOperand ConsumeStringLiteral()
        {
            var literal = new StringOperand { Value = Current.Text };
            _index++;
            return literal;
        }

        /// <summary>condition := or</summary>
        public ConditionNode? ParseCondition() => ParseOr();

        /// <summary>or := and ( OR and )*</summary>
        private ConditionNode? ParseOr()
        {
            var left = ParseAnd();

            while (left is not null && Current.Kind == TokenKind.Or)
            {
                _index++;
                var right = ParseAnd();

                if (right is null)
                {
                    return null;
                }

                left = new LogicalNode
                {
                    Operator = LogicalOperator.Or,
                    Left = left,
                    Right = right,
                };
            }

            return left;
        }

        /// <summary>and := unary ( AND unary )*</summary>
        private ConditionNode? ParseAnd()
        {
            var left = ParseUnary();

            while (left is not null && Current.Kind == TokenKind.And)
            {
                _index++;
                var right = ParseUnary();

                if (right is null)
                {
                    return null;
                }

                left = new LogicalNode
                {
                    Operator = LogicalOperator.And,
                    Left = left,
                    Right = right,
                };
            }

            return left;
        }

        /// <summary>unary := NOT unary | primary</summary>
        private ConditionNode? ParseUnary()
        {
            if (Current.Kind == TokenKind.Not)
            {
                _index++;
                var operand = ParseUnary();
                return operand is null ? null : new NotNode { Operand = operand };
            }

            return ParsePrimary();
        }

        /// <summary>primary := '(' condition ')' | predicate</summary>
        private ConditionNode? ParsePrimary()
        {
            if (Current.Kind == TokenKind.LeftParen)
            {
                _index++;
                var inner = ParseCondition();

                if (inner is null)
                {
                    return null;
                }

                if (Current.Kind != TokenKind.RightParen)
                {
                    ReportUnexpectedToken();
                    return null;
                }

                _index++;
                return inner;
            }

            if (Current.Kind != TokenKind.Identifier)
            {
                result.Diagnostics.Add(
                    new ConditionDiagnostic(
                        new QueryDiagnosticText(
                            nameof(Strings.CodeGen_Query_ExpectedColumnOrParen),
                            IsAtEnd
                                ? (object)
                                    new QueryDiagnosticText(
                                        nameof(Strings.CodeGen_Query_EndOfInput)
                                    )
                                : Current.Text
                        ),
                        Current.Position,
                        Current.Length
                    )
                );
                return null;
            }

            var column = new ColumnReference
            {
                Text = Current.Text,
                Position = Current.Position,
                Length = Current.Length,
            };
            result.ColumnReferences.Add(column);
            _index++;

            return ParsePredicateTail(column);
        }

        /// <summary>列名に続く述語の残り（比較・IS NULL・LIKE・IN・文字列一致）を読む</summary>
        private ConditionNode? ParsePredicateTail(ColumnReference column)
        {
            // 比較演算子
            var comparison = Current.Kind switch
            {
                TokenKind.Equal => ComparisonOperator.Equal,
                TokenKind.NotEqual => ComparisonOperator.NotEqual,
                TokenKind.Less => ComparisonOperator.Less,
                TokenKind.LessOrEqual => ComparisonOperator.LessOrEqual,
                TokenKind.Greater => ComparisonOperator.Greater,
                TokenKind.GreaterOrEqual => ComparisonOperator.GreaterOrEqual,
                _ => (ComparisonOperator?)null,
            };

            if (comparison is not null)
            {
                _index++;
                var operand = ParseOperand();

                if (operand is null)
                {
                    return null;
                }

                return new ComparisonNode
                {
                    Column = column,
                    Operator = comparison.Value,
                    Operand = operand,
                };
            }

            // IS [NOT] NULL
            if (Current.Kind == TokenKind.Is)
            {
                _index++;
                var isNot = false;

                if (Current.Kind == TokenKind.Not)
                {
                    isNot = true;
                    _index++;
                }

                if (Current.Kind != TokenKind.Null)
                {
                    result.Diagnostics.Add(
                        new ConditionDiagnostic(
                            new QueryDiagnosticText(
                                nameof(Strings.CodeGen_Query_ExpectedNullAfterIs)
                            ),
                            Current.Position,
                            Current.Length
                        )
                    );
                    return null;
                }

                _index++;
                return new NullCheckNode { Column = column, IsNotNull = isNot };
            }

            // [NOT] LIKE / IN / CONTAINS / STARTSWITH / ENDSWITH
            var negated = false;

            if (Current.Kind == TokenKind.Not)
            {
                negated = true;
                _index++;
            }

            switch (Current.Kind)
            {
                case TokenKind.Like:
                    _index++;
                    return ParseLike(column, negated);

                case TokenKind.In:
                    _index++;
                    return ParseIn(column, negated);

                case TokenKind.Contains:
                case TokenKind.StartsWith:
                case TokenKind.EndsWith:
                    var kind = Current.Kind switch
                    {
                        TokenKind.Contains => StringMatchKind.Contains,
                        TokenKind.StartsWith => StringMatchKind.StartsWith,
                        _ => StringMatchKind.EndsWith,
                    };
                    _index++;
                    return ParseStringMatch(column, negated, kind);

                default:
                    result.Diagnostics.Add(
                        new ConditionDiagnostic(
                            new QueryDiagnosticText(
                                nameof(Strings.CodeGen_Query_ExpectedComparison),
                                column.Text
                            ),
                            Current.Position,
                            Current.Length
                        )
                    );
                    return null;
            }
        }

        /// <summary>LIKE の右辺を読み、意味論（リテラルは % 位置で分解・パラメータは部分一致）へ写像する</summary>
        private ConditionNode? ParseLike(ColumnReference column, bool negated)
        {
            var operand = ParseStringOperand();

            if (operand is null)
            {
                return null;
            }

            if (operand is ParameterOperand)
            {
                // パラメータ値は実行時までパターンが不明のため、部分一致（値はリテラル扱い）に固定する
                return new StringMatchNode
                {
                    Column = column,
                    Negated = negated,
                    Kind = StringMatchKind.Contains,
                    Operand = operand,
                };
            }

            var pattern = ((StringOperand)operand).Value;
            var startsWithWildcard = pattern.StartsWith('%');
            var endsWithWildcard = pattern.EndsWith('%');
            var core = pattern.Trim('%');

            // 内部の % や _ はミニ DSL では表現できない（自由 SQL / manual の担当）
            if (core.Contains('%') || pattern.Contains('_'))
            {
                result.Diagnostics.Add(
                    new ConditionDiagnostic(
                        new QueryDiagnosticText(
                            nameof(Strings.CodeGen_Query_UnsupportedLikePattern),
                            pattern
                        ),
                        column.Position,
                        column.Length
                    )
                );
                return null;
            }

            var coreOperand = new StringOperand { Value = core };

            if (startsWithWildcard && endsWithWildcard)
            {
                return new StringMatchNode
                {
                    Column = column,
                    Negated = negated,
                    Kind = StringMatchKind.Contains,
                    Operand = coreOperand,
                };
            }

            if (endsWithWildcard)
            {
                return new StringMatchNode
                {
                    Column = column,
                    Negated = negated,
                    Kind = StringMatchKind.StartsWith,
                    Operand = coreOperand,
                };
            }

            if (startsWithWildcard)
            {
                return new StringMatchNode
                {
                    Column = column,
                    Negated = negated,
                    Kind = StringMatchKind.EndsWith,
                    Operand = coreOperand,
                };
            }

            // ワイルドカードなしのリテラルは等値比較と同じ
            var equality = new ComparisonNode
            {
                Column = column,
                Operator = ComparisonOperator.Equal,
                Operand = coreOperand,
            };
            return negated ? new NotNode { Operand = equality } : equality;
        }

        /// <summary>IN の右辺（リストパラメータ）を読む</summary>
        private ConditionNode? ParseIn(ColumnReference column, bool negated)
        {
            if (Current.Kind != TokenKind.Parameter)
            {
                result.Diagnostics.Add(
                    new ConditionDiagnostic(
                        new QueryDiagnosticText(nameof(Strings.CodeGen_Query_ExpectedInParameter)),
                        Current.Position,
                        Current.Length
                    )
                );
                return null;
            }

            var parameter = ConsumeParameter();

            return new InNode
            {
                Column = column,
                Negated = negated,
                Parameter = parameter,
            };
        }

        /// <summary>CONTAINS / STARTSWITH / ENDSWITH の右辺を読む</summary>
        private ConditionNode? ParseStringMatch(
            ColumnReference column,
            bool negated,
            StringMatchKind kind
        )
        {
            var operand = ParseStringOperand();

            if (operand is null)
            {
                return null;
            }

            return new StringMatchNode
            {
                Column = column,
                Negated = negated,
                Kind = kind,
                Operand = operand,
            };
        }

        /// <summary>文字列一致系の右辺（パラメータまたは文字列リテラル）を読む</summary>
        private ConditionOperand? ParseStringOperand()
        {
            if (Current.Kind == TokenKind.Parameter)
            {
                return ConsumeParameter();
            }

            if (Current.Kind == TokenKind.String)
            {
                return ConsumeStringLiteral();
            }

            result.Diagnostics.Add(
                new ConditionDiagnostic(
                    new QueryDiagnosticText(nameof(Strings.CodeGen_Query_StringMatchRequiresText)),
                    Current.Position,
                    Current.Length
                )
            );
            return null;
        }

        /// <summary>比較の右辺（パラメータ・数値・文字列）を読む</summary>
        private ConditionOperand? ParseOperand()
        {
            if (Current.Kind == TokenKind.Parameter)
            {
                return ConsumeParameter();
            }

            if (Current.Kind == TokenKind.Minus)
            {
                _index++;

                if (Current.Kind != TokenKind.Number)
                {
                    result.Diagnostics.Add(
                        new ConditionDiagnostic(
                            new QueryDiagnosticText(nameof(Strings.CodeGen_Query_ExpectedOperand)),
                            Current.Position,
                            Current.Length
                        )
                    );
                    return null;
                }

                var negative = new NumberOperand { Literal = "-" + Current.Text };
                _index++;
                return negative;
            }

            if (Current.Kind == TokenKind.Number)
            {
                var number = new NumberOperand { Literal = Current.Text };
                _index++;
                return number;
            }

            if (Current.Kind == TokenKind.String)
            {
                return ConsumeStringLiteral();
            }

            result.Diagnostics.Add(
                new ConditionDiagnostic(
                    new QueryDiagnosticText(nameof(Strings.CodeGen_Query_ExpectedOperand)),
                    Current.Position,
                    Current.Length
                )
            );
            return null;
        }
    }
}
