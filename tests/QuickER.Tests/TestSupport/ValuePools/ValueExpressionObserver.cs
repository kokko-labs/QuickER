using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 述語の式木から、列と比べる値を観測する（比較・IN の <c>Contains</c>・文字列一致・<c>Equals</c> の、行に依らない側を評価する）
/// </summary>
/// <remarks>
/// <para>
/// 列の側は、ラムダの引数のメンバーまで変換・<c>Nullable.Value</c>・日付部品をはがして辿り、メンバーから列の相を呼び出し側の関数で引く。
/// 値を変える明示の変換（C# で明示が要る数値の変換）は、列の側なら比べる値の相の変換先に、値の側なら変換の元の値の相の変換先に付ける。
/// 日付部品の <c>.Date</c> は相を <c>date</c> にし、ほかの日付部品（<c>.Year</c> など）は相を持たない。
/// </para>
/// <para>
/// 値の側は、ラムダの引数を含まない部分式を評価する（乱数を引かない・式を変えない）。評価が例外になる部分式は観測しない。
/// 列どうしの比較は値を持たないので観測しない。
/// </para>
/// </remarks>
public static class ValueExpressionObserver
{
    /// <summary>述語の比べる値を観測する</summary>
    /// <param name="predicate">述語</param>
    /// <param name="columnOf">ラムダの引数のメンバー → 列の相（列でなければ null）</param>
    /// <param name="where">観測の出どころ（行 Id など）</param>
    public static IReadOnlyList<ValueObservation> Observe(
        LambdaExpression predicate,
        Func<MemberInfo, ValueColumn?> columnOf,
        string where
    )
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(columnOf);
        var walker = new Walker(predicate.Parameters, columnOf, where);
        walker.Visit(predicate.Body);

        return walker.Observations;
    }

    /// <summary>C# で明示の変換が要る数値の変換か（値が変わりうる変換。暗黙の拡大と Nullable の持ち上げは含まない）</summary>
    /// <param name="from">元の型</param>
    /// <param name="to">変換先の型</param>
    public static bool IsNarrowing(Type from, Type to)
    {
        from = Nullable.GetUnderlyingType(from) ?? from;
        to = Nullable.GetUnderlyingType(to) ?? to;

        if (from == to || !IsNumber(from) || !IsNumber(to))
        {
            return false;
        }

        if (IsInteger(to))
        {
            // 小数から整数・範囲の入らない整数どうし
            return !IsInteger(from) || !Contains(to, from);
        }

        // 整数から小数は暗黙（double・float・decimal）、float → double は暗黙
        if (IsInteger(from))
        {
            return false;
        }

        return !(from == typeof(float) && to == typeof(double));
    }

    private static bool IsNumber(Type type) =>
        IsInteger(type)
        || type == typeof(float)
        || type == typeof(double)
        || type == typeof(decimal);

    private static bool IsInteger(Type type) =>
        type == typeof(sbyte)
        || type == typeof(byte)
        || type == typeof(short)
        || type == typeof(ushort)
        || type == typeof(int)
        || type == typeof(uint)
        || type == typeof(long)
        || type == typeof(ulong)
        || type == typeof(char);

    /// <summary>整数の型 <paramref name="outer"/> の範囲が <paramref name="inner"/> の範囲を含むか</summary>
    private static bool Contains(Type outer, Type inner) =>
        RangeOf(outer).Min <= RangeOf(inner).Min && RangeOf(inner).Max <= RangeOf(outer).Max;

    private static (decimal Min, decimal Max) RangeOf(Type type) =>
        type == typeof(sbyte) ? (sbyte.MinValue, sbyte.MaxValue)
        : type == typeof(byte) ? (byte.MinValue, byte.MaxValue)
        : type == typeof(short) ? (short.MinValue, short.MaxValue)
        : type == typeof(ushort) ? (ushort.MinValue, ushort.MaxValue)
        : type == typeof(char) ? (char.MinValue, char.MaxValue)
        : type == typeof(int) ? (int.MinValue, int.MaxValue)
        : type == typeof(uint) ? (uint.MinValue, uint.MaxValue)
        : type == typeof(long) ? (long.MinValue, long.MaxValue)
        : (ulong.MinValue, ulong.MaxValue);

    private sealed class Walker(
        IReadOnlyCollection<ParameterExpression> parameters,
        Func<MemberInfo, ValueColumn?> columnOf,
        string where
    ) : ExpressionVisitor
    {
        public List<ValueObservation> Observations { get; } = [];

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (
                node.NodeType
                is ExpressionType.Equal
                    or ExpressionType.NotEqual
                    or ExpressionType.LessThan
                    or ExpressionType.LessThanOrEqual
                    or ExpressionType.GreaterThan
                    or ExpressionType.GreaterThanOrEqual
            )
            {
                var leftRow = UsesParameter(node.Left);
                var rightRow = UsesParameter(node.Right);

                if (leftRow != rightRow)
                {
                    var (column, value) = leftRow
                        ? (node.Left, node.Right)
                        : (node.Right, node.Left);
                    ObserveValue(value, ColumnOf(column));
                }
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;

            if (method.Name == "Contains" && method.DeclaringType != typeof(string))
            {
                // Enumerable.Contains(値の並び, 列) か 値の並び.Contains(列)
                var (source, item) =
                    node.Object is null && node.Arguments.Count == 2
                        ? (node.Arguments[0], node.Arguments[1])
                    : node.Object is not null && node.Arguments.Count == 1
                        ? (node.Object, node.Arguments[0])
                    : (null, null);

                if (
                    source is not null
                    && item is not null
                    && !UsesParameter(source)
                    && UsesParameter(item)
                )
                {
                    ObserveValue(source, ColumnOf(item));
                }
            }
            else if (
                node.Object is not null
                && node.Arguments.Count >= 1
                && method.Name is "Contains" or "StartsWith" or "EndsWith" or "Equals"
                && UsesParameter(node.Object)
                && !UsesParameter(node.Arguments[0])
            )
            {
                ObserveValue(node.Arguments[0], ColumnOf(node.Object));
            }

            return base.VisitMethodCall(node);
        }

        /// <summary>値の側を評価して観測する（値の側の値を変える明示の変換は、元の値に変換先を付けて観測する）</summary>
        private void ObserveValue(Expression value, ValueColumn? column)
        {
            var stripped = StripObjectBox(value);

            if (
                stripped
                    is UnaryExpression
                    {
                        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
                    } convert
                && IsNarrowing(convert.Operand.Type, convert.Type)
            )
            {
                Add(
                    convert.Operand,
                    (column ?? new ValueColumn()) with
                    {
                        CastTarget = Nullable.GetUnderlyingType(convert.Type) ?? convert.Type,
                    }
                );
                return;
            }

            Add(value, column);
        }

        private void Add(Expression expression, ValueColumn? column)
        {
            object? value;

            try
            {
                value = Expression
                    .Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)))
                    .Compile(preferInterpretation: true)();
            }
            catch (Exception)
            {
                return;
            }

            // 文字列とバイト列以外の並びは要素の並びとして持つ（後で評価が変わらないよう写す）
            if (value is IEnumerable items and not string and not byte[])
            {
                value = items.Cast<object?>().ToList();
            }

            Observations.Add(new ValueObservation(value, column, where));
        }

        /// <summary>列の側の相（列でなければ null）。列の側の値を変える明示の変換は相の変換先にする</summary>
        private ValueColumn? ColumnOf(Expression expression)
        {
            Type? cast = null;
            var datePart = (string?)null;
            var current = expression;

            while (true)
            {
                switch (current)
                {
                    case UnaryExpression
                    {
                        NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
                    } convert:
                        if (cast is null && IsNarrowing(convert.Operand.Type, convert.Type))
                        {
                            cast = Nullable.GetUnderlyingType(convert.Type) ?? convert.Type;
                        }

                        current = convert.Operand;
                        continue;

                    case MemberExpression { Expression: ParameterExpression parameter } member
                        when parameters.Contains(parameter):
                    {
                        var column =
                            datePart is null ? columnOf(member.Member)
                            : datePart == nameof(DateTime.Date) ? ValueColumn.FromSqlType("date")
                            : null;

                        return cast is null
                            ? column
                            : (column ?? new ValueColumn()) with
                            {
                                CastTarget = cast,
                            };
                    }

                    case MemberExpression { Member.Name: "Value" } nullable
                        when nullable.Expression is not null
                            && Nullable.GetUnderlyingType(nullable.Expression.Type) is not null:
                        current = nullable.Expression;
                        continue;

                    case MemberExpression part
                        when part.Expression is not null
                            && (
                                Nullable.GetUnderlyingType(part.Expression.Type)
                                ?? part.Expression.Type
                            )
                                is var owner
                            && (owner == typeof(DateTime) || owner == typeof(DateTimeOffset)):
                        datePart ??= part.Member.Name;
                        current = part.Expression;
                        continue;

                    default:
                        return cast is null ? null : new ValueColumn { CastTarget = cast };
                }
            }
        }

        private static Expression StripObjectBox(Expression expression) =>
            expression is UnaryExpression { NodeType: ExpressionType.Convert } box
            && box.Type == typeof(object)
                ? box.Operand
                : expression;

        private bool UsesParameter(Expression expression)
        {
            var finder = new ParameterFinder(parameters);
            finder.Visit(expression);

            return finder.Found;
        }
    }

    private sealed class ParameterFinder(IReadOnlyCollection<ParameterExpression> parameters)
        : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= parameters.Contains(node);

            return node;
        }
    }
}
