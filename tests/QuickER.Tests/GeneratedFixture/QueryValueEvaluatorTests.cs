using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AwesomeAssertions;
using Xunit;
using QueryFixtureCustomerId = QuickER.Tests.GeneratedQueryFixture.CustomerIdValue;
using QueryFixtureEvaluator = QuickER.Tests.GeneratedQueryFixture.QueryValueEvaluator;
using QueryFixtureMemo = QuickER.Tests.GeneratedQueryFixture.MemoValue;
using QueryFixtureOrder = QuickER.Tests.GeneratedQueryFixture.OrderEntity;
using QueryFixtureParam = QuickER.Tests.GeneratedQueryFixture.SqlQueryParameter;
using QueryFixtureTranslator = QuickER.Tests.GeneratedQueryFixture.SqlExpressionTranslator;
using QueryFixtureUnwrap = QuickER.Tests.GeneratedQueryFixture.SqlParameterValue;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 生成ランタイムの <c>QueryValueEvaluator</c> が、述語の値側をコンパイルせずに解釈すること、および
/// 解釈経路がコンパイル経路と観測上同一（例外の型まで）であることを検証する単体テスト。
/// </summary>
/// <remarks>
/// <para>
/// 値側の評価はもともと「定数・メンバー参照だけ直読み、それ以外は式ツリーをコンパイルして実行」だった。
/// 値オブジェクト有効の図では生成クエリの値側が <c>XxxValue.Create(arg)</c>＝メソッド呼び出しになるため、
/// 全クエリの全呼び出しがこのフォールバックを踏んでいた（動的メソッドの生成は値の読み出しより桁違いに高い）。
/// </para>
/// <para>
/// フォールバック回数は <c>QueryValueEvaluator.FallbackCompileCount</c>（internal・スレッドローカル）で観測する。
/// 生成フィクスチャはテストプロジェクトと同一アセンブリなので internal が直接見え、スレッドローカルなので
/// 並列実行中の他テストの評価が混ざらない。
/// </para>
/// <para>
/// 解釈経路の追加で壊れてはならないのは「観測上の同一性」。とくにメソッド呼び出しの失敗は、リフレクション
/// 既定の <c>TargetInvocationException</c> 包みではなく元の例外のまま伝播しなければならない（値オブジェクトの
/// 検証違反＝このパスが存在する理由そのものを呼び出し側が catch できなくなる）。同じ理由で、null レシーバへの
/// インスタンス呼び出しは <c>TargetException</c> ではなく <c>NullReferenceException</c> でなければならない。
/// </para>
/// </remarks>
public sealed class QueryValueEvaluatorTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    private sealed class Probe
    {
        public int A { get; set; }

        public string? Name { get; set; }
    }

    /// <summary>テスト用の例外（型そのままの伝播を確かめるための目印）</summary>
    private sealed class ProbeException : Exception
    {
        public ProbeException()
            : base("probe") { }
    }

    /// <summary>ユーザー定義の変換演算子を持つ型（<c>Convert</c> ノードの <c>Method</c> が非 null になる）</summary>
    private readonly struct Celsius(int degrees)
    {
        public int Degrees { get; } = degrees;

        public static implicit operator int(Celsius value) => value.Degrees + 100;
    }

    /// <summary>必ず投げるファクトリ（値側のメソッド呼び出しとして式ツリーに載る）</summary>
    private static int Throwing() => throw new ProbeException();

    /// <summary>引数をそのまま返すファクトリ（値側のメソッド呼び出しとして式ツリーに載る）</summary>
    private static int Identity(int value) => value;

    /// <summary>フォールバック回数の増分を測りながら述語を条件へ翻訳する（図の方言は SQLite＝識別子は二重引用符）</summary>
    private static (string Sql, List<QueryFixtureParam> Parameters, long Fallbacks) Translate(
        Expression<Func<Probe, bool>> predicate
    )
    {
        var parameters = new List<QueryFixtureParam>();
        var before = QueryFixtureEvaluator.FallbackCompileCount;
        var sql = QueryFixtureTranslator.ToCondition(predicate.Body, parameters);

        return (sql, parameters, QueryFixtureEvaluator.FallbackCompileCount - before);
    }

    /// <summary>評価 1 回分のフォールバック増分と結果を返す</summary>
    private static (object? Value, long Fallbacks) Evaluate(Expression expression)
    {
        var before = QueryFixtureEvaluator.FallbackCompileCount;
        var value = QueryFixtureEvaluator.Evaluate(expression);

        return (value, QueryFixtureEvaluator.FallbackCompileCount - before);
    }

    /// <summary>式ツリーをコンパイルして評価する（解釈経路との観測上の同一性を確かめる基準側）</summary>
    private static object? CompileAndInvoke(Expression expression) =>
        Expression
            .Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)))
            .Compile()();

    /// <summary>
    /// 値オブジェクト有効の図が毎回踏む形（値側の <c>XxxValue.Create(arg)</c>）が、コンパイルへ落ちずに
    /// 解釈されることを検証する（F3 の主目的）。
    /// </summary>
    [Fact(DisplayName = "値側の値オブジェクト Create はフォールバックへ落ちない")]
    public void Evaluate_ValueObjectFactoryCall_DoesNotFallBack()
    {
        var customerId = 42;
        Expression<Func<QueryFixtureOrder, bool>> predicate = e =>
            e.CustomerId == QueryFixtureCustomerId.Create(customerId);

        var (value, fallbacks) = Evaluate(((BinaryExpression)predicate.Body).Right);

        fallbacks
            .Should()
            .Be(
                0,
                "メソッド呼び出しを解釈できなければ、VO 有効の図は全クエリの全呼び出しで式ツリーをコンパイルする"
            );
        value.Should().Be(QueryFixtureCustomerId.Create(42));
    }

    /// <summary>入れ子のメソッド呼び出し（引数の再帰評価）も解釈で完結する</summary>
    [Fact(DisplayName = "入れ子のメソッド呼び出しも解釈で完結する")]
    public void Evaluate_NestedMethodCalls_DoNotFallBack()
    {
        var seed = 3;
        var (sql, parameters, fallbacks) = Translate(p => p.A == Identity(Identity(seed)));

        fallbacks.Should().Be(0);
        sql.Should().Be("\"A\" = @p0");
        parameters[0].Value.Should().Be(3);
    }

    /// <summary>
    /// メソッドが投げた例外は型そのまま伝播しなければならない（リフレクション既定の
    /// <c>TargetInvocationException</c> 包みになってはならない）。
    /// </summary>
    [Fact(DisplayName = "値側メソッドの例外は包まれずそのまま伝播する")]
    public void Evaluate_ThrowingMethod_PropagatesOriginalException()
    {
        Expression<Func<Probe, bool>> predicate = p => p.A == Throwing();
        var call = ((BinaryExpression)predicate.Body).Right;

        var act = () => QueryFixtureEvaluator.Evaluate(call);

        act.Should().Throw<ProbeException>();
        act.Should().NotThrow<TargetInvocationException>();
    }

    /// <summary>
    /// 値オブジェクトの検証違反も同じ扱い（この経路が存在する理由そのもの）。解釈経路の例外型は
    /// コンパイル経路の例外型と一致しなければならない。
    /// </summary>
    [Fact(DisplayName = "値オブジェクトの検証違反はコンパイル経路と同じ型で伝播する")]
    public void Evaluate_InvalidValueObject_ThrowsSameExceptionTypeAsCompiled()
    {
        // MemoValue は最大 50 文字。超過値の Create は検証例外になる
        var create = Expression.Call(
            typeof(QueryFixtureMemo).GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                binder: null,
                [typeof(string)],
                modifiers: null
            )!,
            Expression.Constant(new string('x', 200))
        );

        var compiled = Record.Exception(() => CompileAndInvoke(create));
        var interpreted = Record.Exception(() => QueryFixtureEvaluator.Evaluate(create));

        compiled.Should().NotBeNull("検証違反が出ない値ではこのテストが何も確かめられない");
        interpreted.Should().NotBeNull();
        interpreted!.GetType().Should().Be(compiled!.GetType());
        interpreted.Should().NotBeOfType<TargetInvocationException>();
    }

    /// <summary>
    /// null レシーバへのインスタンス呼び出しは、コンパイル経路と同じ <c>NullReferenceException</c> で
    /// 失敗しなければならない（リフレクションの <c>TargetException</c> ではない）。
    /// </summary>
    [Fact(DisplayName = "null レシーバのインスタンス呼び出しは NullReferenceException になる")]
    public void Evaluate_InstanceCallOnNullReceiver_ThrowsNullReference()
    {
        string? text = null;
        Expression<Func<Probe, bool>> predicate = p => p.Name == text!.Trim();
        var call = ((BinaryExpression)predicate.Body).Right;

        var act = () => QueryFixtureEvaluator.Evaluate(call);

        act.Should().Throw<NullReferenceException>();
        act.Should().NotThrow<TargetException>();
    }

    /// <summary>インラインの配列初期化子（IN 検索のコレクション位置で通る形）は解釈で組み立てる</summary>
    [Fact(DisplayName = "インライン配列の IN はフォールバックへ落ちない")]
    public void Evaluate_InlineArrayInClause_DoesNotFallBack()
    {
        var (sql, parameters, fallbacks) = Translate(p => new[] { 1, 2, 3 }.Contains(p.A));

        fallbacks.Should().Be(0);
        sql.Should().Be("\"A\" IN (@p0, @p1, @p2)");
        parameters
            .Select(parameter => QueryFixtureUnwrap.Unwrap(parameter.Value))
            .Should()
            .Equal(1, 2, 3);
    }

    /// <summary>ボクシング変換は値を変えないため素通しできる</summary>
    [Fact(DisplayName = "ボクシング変換は解釈で素通しする")]
    public void Evaluate_BoxingConvert_DoesNotFallBack()
    {
        var (result, fallbacks) = Evaluate(
            Expression.Convert(Expression.Constant(5), typeof(object))
        );

        fallbacks.Should().Be(0);
        result.Should().Be(5);
    }

    /// <summary>参照型のアップキャストも値を変えないため素通しできる</summary>
    [Fact(DisplayName = "参照型のアップキャストは解釈で素通しする")]
    public void Evaluate_ReferenceUpcast_DoesNotFallBack()
    {
        var (result, fallbacks) = Evaluate(
            Expression.Convert(Expression.Constant("abc", typeof(string)), typeof(IComparable))
        );

        fallbacks.Should().Be(0);
        result.Should().Be("abc");
    }

    /// <summary>
    /// 数値変換はフォールバックへ落とす（値が変わるため素通しできない）。落としたうえで結果は
    /// コンパイル経路と同じでなければならない。
    /// </summary>
    [Fact(DisplayName = "数値変換はフォールバックへ落ちて正しい値になる")]
    public void Evaluate_NumericConvert_FallsBackWithCorrectValue()
    {
        var (result, fallbacks) = Evaluate(
            Expression.Convert(Expression.Constant(5), typeof(long))
        );

        fallbacks.Should().Be(1, "数値変換を素通しすると int のまま long 列へバインドされる");
        result.Should().Be(5L);
        result.Should().BeOfType<long>();
    }

    /// <summary>ユーザー定義の変換演算子もフォールバックへ落とす（演算子の本体を飛ばしてはならない）</summary>
    [Fact(DisplayName = "ユーザー定義変換演算子はフォールバックへ落ちて演算子どおりの値になる")]
    public void Evaluate_UserDefinedConvert_FallsBackWithCorrectValue()
    {
        var (result, fallbacks) = Evaluate(
            Expression.Convert(Expression.Constant(new Celsius(1)), typeof(int))
        );

        fallbacks.Should().Be(1, "変換演算子を素通しすると +100 の変換が黙って消える");
        result.Should().Be(101);
    }

    /// <summary>従来どおり直読みできる形（定数・クロージャのメンバー参照）はフォールバックへ落ちない</summary>
    [Fact(DisplayName = "定数・クロージャのメンバー参照は従来どおり直読みする")]
    public void Evaluate_ConstantAndClosureMember_DoNotFallBack()
    {
        var captured = 9;
        var (sql, parameters, fallbacks) = Translate(p => p.A == captured);

        fallbacks.Should().Be(0);
        sql.Should().Be("\"A\" = @p0");
        parameters[0].Value.Should().Be(9);
    }

    /// <summary>解釈できない形（算術演算）は従来どおりコンパイルへ落ち、結果は変わらない</summary>
    [Fact(DisplayName = "算術演算は従来どおりコンパイルへ落ちる")]
    public void Evaluate_Arithmetic_StillFallsBack()
    {
        var left = 2;
        var right = 3;
        var (sql, parameters, fallbacks) = Translate(p => p.A == left + right);

        fallbacks.Should().Be(1);
        sql.Should().Be("\"A\" = @p0");
        parameters[0].Value.Should().Be(5);
    }

    /// <summary>
    /// null の受け手に対するインスタンスメンバー参照は、メソッド呼び出し枝と同じ規則でフォールバックへ回り、
    /// コンパイル経路と同じ <see cref="NullReferenceException"/> になる（リフレクション直読みだと TargetException に化ける）
    /// </summary>
    [Fact(
        DisplayName = "null 受け手のメンバー参照はコンパイル経路と同じ NullReferenceException になる"
    )]
    public void Evaluate_MemberOnNullReceiver_ThrowsNullReferenceLikeCompiledPath()
    {
        // 捕捉変数が null のときの customer.Name と同じ形（受け手は型付き null 定数で代役）
        var member = Expression.Property(
            Expression.Constant(null, typeof(Probe)),
            nameof(Probe.Name)
        );
        var before = QueryFixtureEvaluator.FallbackCompileCount;

        var act = () => QueryFixtureEvaluator.Evaluate(member);

        act.Should().Throw<NullReferenceException>();
        (QueryFixtureEvaluator.FallbackCompileCount - before).Should().Be(1);
    }

    /// <summary>
    /// null の Nullable&lt;T&gt; に対する HasValue は、コンパイル経路と同じく例外にならず false になる
    /// （リフレクション直読みだと boxed null への GetValue で TargetException になる）
    /// </summary>
    [Fact(DisplayName = "null の Nullable<T>.HasValue はコンパイル経路と同じ false になる")]
    public void Evaluate_HasValueOnNullNullable_ReturnsFalseLikeCompiledPath()
    {
        var member = Expression.Property(
            Expression.Constant(null, typeof(int?)),
            nameof(Nullable<int>.HasValue)
        );
        var before = QueryFixtureEvaluator.FallbackCompileCount;

        var result = QueryFixtureEvaluator.Evaluate(member);

        result.Should().Be(false);
        (QueryFixtureEvaluator.FallbackCompileCount - before).Should().Be(1);
    }
}
