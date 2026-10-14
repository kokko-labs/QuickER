using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値オブジェクトの文字列一致（Contains / StartsWith / EndsWith）を、コンパイラが組む形でない式木でも
/// 4 つの実装先が同じに扱うことを確かめる共通基底。
/// </summary>
/// <remarks>
/// <para>
/// コンパイラが組む式木の <see cref="System.Reflection.MethodInfo"/> は、宣言した型（生成側の
/// <c>ValueObjectStringBase&lt;TSelf&gt;</c>）から引いたものになる。<c>typeof(NameValue).GetMethod("Contains")</c>
/// のように具象の値オブジェクト型から引くと、同じメソッドでも ReflectedType の違う別のインスタンスになるので、
/// 判定を参照の一致で行うと文字列一致として認識されない（翻訳できない・null の引数を先に断れない）。
/// </para>
/// <para>
/// 判定の写しは 4 つある（Core の null ガード・SQL Server と SQLite の翻訳器・EF Core の翻訳器）。
/// 同じ式を 4 つの実装先へ流し、写しがずれないことを固定する。
/// </para>
/// </remarks>
/// <typeparam name="TCustomer">フィクスチャの顧客エンティティ（Name が文字列の値オブジェクト）</typeparam>
public abstract class StringMatchMethodRuntimeTestsBase<TCustomer>
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>保存先を空にし、顧客 Alice(1)・Bob(2)・Carol(3) を入れる</summary>
    protected abstract Task ResetAndSeedAsync();

    /// <summary>述語に合う顧客の ID を返す（実装先の Query().Where(...).ToListAsync を通す）</summary>
    protected abstract Task<IReadOnlyList<int>> QueryIdsAsync(
        Expression<Func<TCustomer, bool>> predicate
    );

    /// <summary>
    /// <c>c =&gt; c.Name.{method}(argument)</c> を、具象の値オブジェクト型から引いた MethodInfo で組む。
    /// </summary>
    /// <remarks>
    /// 引数は、コンパイラがラムダの外の変数を渡すときと同じく、オブジェクトのメンバーの参照で渡す（SQL ではパラメータになる）。
    /// 定数で渡すと、null の引数は翻訳器がパターンを組む時点で断るので、先に断る null ガードの有無が見えなくなる。
    /// </remarks>
    private static Expression<Func<TCustomer, bool>> ReflectedMatch(
        string method,
        Type argumentType,
        object? argument
    )
    {
        var customer = Expression.Parameter(typeof(TCustomer), "c");
        var name = Expression.Property(customer, "Name");
        var target =
            name.Type.GetMethod(method, [argumentType])
            ?? throw new InvalidOperationException($"{name.Type.Name}.{method} が見つからない");
        target
            .ReflectedType.Should()
            .Be(name.Type, "具象の値オブジェクト型から引いた MethodInfo で組む");
        target.DeclaringType.Should().NotBe(name.Type, "メソッドは生成側の基底で宣言されている");

        return Expression.Lambda<Func<TCustomer, bool>>(
            Expression.Call(
                name,
                target,
                Expression.Convert(
                    Expression.Field(
                        Expression.Constant(new StrongBox<object?>(argument)),
                        nameof(StrongBox<object?>.Value)
                    ),
                    argumentType
                )
            ),
            customer
        );
    }

    /// <summary>Name の値オブジェクト型の Create(string)（基底で宣言した静的メソッド）を呼んで値を作る</summary>
    private static object CreateName(string value) =>
        typeof(TCustomer)
            .GetProperty("Name")!
            .PropertyType.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                [typeof(string)]
            )!
            .Invoke(null, [value])!;

    [Fact(
        DisplayName = "[StringMatch] 1: 具象の値オブジェクト型から引いた MethodInfo の文字列一致も列の一致になる"
    )]
    public async Task ReflectedMethod_IsTranslatedAsColumnMatch()
    {
        await ResetAndSeedAsync();

        (await QueryIdsAsync(ReflectedMatch("Contains", typeof(string), "li")))
            .Should()
            .BeEquivalentTo([1]);
        (await QueryIdsAsync(ReflectedMatch("StartsWith", typeof(string), "B")))
            .Should()
            .BeEquivalentTo([2]);

        var nameType = typeof(TCustomer).GetProperty("Name")!.PropertyType;
        (await QueryIdsAsync(ReflectedMatch("EndsWith", nameType, CreateName("ol"))))
            .Should()
            .BeEquivalentTo([3]);
    }

    [Fact(
        DisplayName = "[StringMatch] 2: 具象の値オブジェクト型から引いた MethodInfo でも null の引数は先に断る"
    )]
    public async Task ReflectedMethod_NullArgument_IsRejected()
    {
        await ResetAndSeedAsync();

        var act = () => QueryIdsAsync(ReflectedMatch("Contains", typeof(string), null));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
