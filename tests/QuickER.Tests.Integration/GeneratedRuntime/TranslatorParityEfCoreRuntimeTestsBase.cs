using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityEfCoreFixture;
using Xunit;
using Source = QuickER.Tests.GeneratedTranslatorParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 述語の表（<see cref="TranslatorParityPredicateRows"/>）を EF Core 版 Repository へ流し、同じ実装先から読み戻した全行へ
/// C# のラムダを当てた結果と比べる共通基底。派生はプロバイダ（スキーマの用意と <c>Use*</c>）と、割れると宣言する行だけを差し込む。
/// </summary>
/// <remarks>
/// <para>
/// EF Core はマルチターゲットと生成時エラーで排他になるので、<see cref="TranslatorParityRuntimeTestsBase"/> の派生にできない
/// （エンティティの型が別の名前空間に生成される）。同じ図を EF Core 単独で生成した
/// <see cref="TranslatorParityEfCoreFixtureDefinition"/> のフィクスチャへ、述語の式の木を付け替えて流す
/// （<see cref="Retarget{TSource, TTarget}"/>＝引数と、エンティティのプロパティの参照を同じ名前のプロパティへ写す。
/// 捕捉した変数・定数はそのまま残すので、EF Core が受け取る木の形は元の行と同じ）。シードも同じ値をプロパティごとに写す。
/// 生成の EF Core のコードはプロバイダに依らないので、SQL Server 方言で生成したフィクスチャをどのプロバイダでも流せる。
/// </para>
/// <para>
/// 表の行のうち EF Core の答えが C# と割れるものは、派生の <see cref="Known"/> に理由つきで宣言する（宣言した行が一致したら赤）。
/// 宣言した行が DB の例外か EF Core の翻訳の失敗（<see cref="InvalidOperationException"/>）で終わったときも、割れたとみなす。
/// </para>
/// </remarks>
public abstract class TranslatorParityEfCoreRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// EF Core の答えが C# と割れると分かっている行と、その理由。宣言した行は C# と割れることを表明する（一致したら赤）。
    /// </summary>
    protected abstract IReadOnlyDictionary<string, string> Known { get; }

    /// <summary>スキーマを作り直し、生成の EF Core 版 Repository を登録した DI コンテナを返す</summary>
    protected abstract Task<ServiceProvider> CreateProviderAsync();

    /// <summary>宣言した行が表に実在することを表明する（行の改名で宣言が黙って効かなくなるのを防ぐ）</summary>
    [Fact(DisplayName = "[翻訳パリティ/EF Core] 割れると宣言した行は述語の表に実在する")]
    public void Known_NamesExistingRows() =>
        Known
            .Keys.Except(TranslatorParityPredicateRows.Table.Select(row => row.Id))
            .Should()
            .BeEmpty();

    /// <summary>
    /// 述語の表の 1 行が、EF Core 版でも C# と同じ行を返すことを検証する（<see cref="Known"/> の行は割れることを検証する）。
    /// </summary>
    [Theory(DisplayName = "[翻訳パリティ/EF Core] 述語の表の行が C# と同じ行を返す")]
    [MemberData(
        nameof(TranslatorParityPredicateRows.Ids),
        MemberType = typeof(TranslatorParityPredicateRows)
    )]
    public async Task Predicate_MatchesCSharp(string id)
    {
        await using var provider = await CreateProviderAsync();

        List<long> expected;
        List<long> actual;
        LambdaExpression predicate;

        try
        {
            (expected, actual, predicate) = await RunRowAsync(provider, id);
        }
        catch (Exception exception)
            when (Known.ContainsKey(id) && exception is DbException or InvalidOperationException)
        {
            // 宣言した行が DB の例外か翻訳の失敗で割れた（C# は例外にしない）
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"行 {id} は宣言どおり割れた: {exception.Message}"
            );

            return;
        }

        if (Known.TryGetValue(id, out var reason))
        {
            actual
                .Should()
                .NotBeEquivalentTo(
                    expected,
                    "行 {0} は EF Core で割れると宣言している（{1}）。一致するようになったら宣言を消す",
                    id,
                    reason
                );

            return;
        }

        actual
            .Should()
            .BeEquivalentTo(expected, "条件 {0} は C# の意味論と同じ行を返すべき", predicate);
    }

    /// <summary>述語の表の 1 行のシードを入れ、期待（C# のラムダ）と実際（Query()）の行のキーを返す</summary>
    private static async Task<(List<long>, List<long>, LambdaExpression)> RunRowAsync(
        ServiceProvider provider,
        string id
    ) =>
        TranslatorParityPredicateRows.Table[id].Input switch
        {
            ProbePredicate probe => await RunAsync(
                provider.GetRequiredService<IProbeRepository>(),
                TranslatorParityRuntimeTestsBase.SeedProbes(),
                Retarget<Source.ProbeEntity, ProbeEntity>(probe.Predicate),
                p => p.ProbeId
            ),
            SmallChildPredicate child => await RunSmallChildAsync(provider, child),
            BigParentPredicate parent => await RunAsync(
                provider.GetRequiredService<IBigParentRepository>(),
                TranslatorParityRuntimeTestsBase.WideBigParents(),
                Retarget<Source.BigParentEntity, BigParentEntity>(parent.Predicate),
                p => p.Id
            ),
            WideProbePredicate wide => await RunAsync(
                provider.GetRequiredService<IProbeRepository>(),
                [
                    .. TranslatorParityRuntimeTestsBase.SeedProbes(),
                    .. TranslatorParityRuntimeTestsBase.WideProbes(),
                ],
                Retarget<Source.ProbeEntity, ProbeEntity>(wide.Predicate),
                p => p.ProbeId
            ),
            _ => throw new InvalidOperationException($"行 {id} の入力の型を扱えない"),
        };

    private static async Task<(List<long>, List<long>, LambdaExpression)> RunSmallChildAsync(
        ServiceProvider provider,
        SmallChildPredicate child
    )
    {
        var parents = provider.GetRequiredService<IBigParentRepository>();
        foreach (var parent in TranslatorParityRuntimeTestsBase.CrossTypeParents())
        {
            await parents.InsertAsync(Copy<Source.BigParentEntity, BigParentEntity>(parent), Ct);
        }

        return await RunAsync(
            provider.GetRequiredService<ISmallChildRepository>(),
            TranslatorParityRuntimeTestsBase.CrossTypeChildren(),
            Retarget<Source.SmallChildEntity, SmallChildEntity>(child.Predicate),
            c => c.Id
        );
    }

    /// <summary>
    /// シードを写して入れ、全行を読み戻して C# のラムダを当てた結果（期待）と、Query() の結果（実際）をキーで返す。
    /// </summary>
    private static async Task<(List<long>, List<long>, LambdaExpression)> RunAsync<
        TSource,
        TEntity,
        TKey
    >(
        IRepository<TEntity, TKey> repository,
        IReadOnlyList<TSource> seeds,
        Expression<Func<TEntity, bool>> predicate,
        Func<TEntity, long> key
    )
        where TSource : class
        where TEntity : EntityBaseCore, new()
    {
        foreach (var seed in seeds)
        {
            await repository.InsertAsync(Copy<TSource, TEntity>(seed), Ct);
        }

        var stored = await repository.Query().ToListAsync(Ct);
        stored.Should().HaveCount(seeds.Count);

        var expected = stored.Where(predicate.Compile()).Select(key).ToList();
        var actual = (await repository.Query().Where(predicate).ToListAsync(Ct))
            .Select(key)
            .ToList();

        return (expected, actual, predicate);
    }

    /// <summary>翻訳器の一致のフィクスチャのエンティティを、同じ名前の列のプロパティへ写して EF Core のフィクスチャのエンティティにする</summary>
    private static TTarget Copy<TSource, TTarget>(TSource source)
        where TTarget : new()
    {
        var target = new TTarget();

        foreach (var property in ColumnProperties(typeof(TSource)))
        {
            typeof(TTarget).GetProperty(property.Name)!.SetValue(target, property.GetValue(source));
        }

        return target;
    }

    private static IEnumerable<PropertyInfo> ColumnProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property =>
                property.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ColumnAttribute>()
                    is not null
            );

    /// <summary>
    /// 述語の引数の型を <typeparamref name="TTarget"/> へ替え、引数のプロパティの参照を同じ名前のプロパティへ写す。
    /// </summary>
    internal static Expression<Func<TTarget, bool>> Retarget<TSource, TTarget>(
        Expression<Func<TSource, bool>> predicate
    )
    {
        var parameter = Expression.Parameter(typeof(TTarget), predicate.Parameters[0].Name);
        var body = new EntityRetargeter(predicate.Parameters[0], parameter).Visit(predicate.Body);

        return Expression.Lambda<Func<TTarget, bool>>(body, parameter);
    }

    /// <summary>引数とそのプロパティの参照だけを付け替える。捕捉した変数・定数・メソッド呼び出しはそのまま残す</summary>
    private sealed class EntityRetargeter(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == from)
            {
                return Expression.Property(to, node.Member.Name);
            }

            return base.VisitMember(node);
        }
    }
}
