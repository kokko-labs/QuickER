using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 述語の表（<see cref="TranslatorParityPredicateRows"/>）を EF Core 版 Repository へ流し、同じ実装先から読み戻した全行へ
/// C# のラムダを当てた結果と比べる共通基底。派生はプロバイダ（スキーマの用意と <c>Use*</c>）・生成物（<see cref="Binding"/>）と、
/// 割れると宣言する行・流せない行だけを差し込む。
/// </summary>
/// <remarks>
/// <para>
/// EF Core はマルチターゲットと生成時エラーで排他になるので、<see cref="TranslatorParityRuntimeTestsBase"/> の派生にできない
/// （エンティティの型が別の名前空間に生成される）。同じ図を EF Core 単独で生成した物へ、述語の式の木を付け替えて流す
/// （<see cref="Retarget"/>＝引数と、エンティティのプロパティの参照を同じ名前のプロパティへ写す。捕捉した変数・定数はそのまま
/// 残すので、EF Core が受け取る木の形は元の行と同じ）。シードも同じ値をプロパティごとに写す（<see cref="SeedValue"/>）。
/// 生成物の型は名前空間ごとに別なので、リポジトリは <see cref="TranslatorParityEfCoreBinding"/> が名前で引いて反射で呼ぶ。
/// </para>
/// <para>
/// 生成物は 2 通り。SQL Server・SQLite の派生はチェックイン済みのフィクスチャ（SQL Server 方言で生成。SQLite の型マッパーの
/// 生成物と Fluent が同じ）を流し、PostgreSQL・MySQL・Oracle の派生は方言切替で写した図を実行時に生成した物
/// （<see cref="TranslatorParityEfCoreGenerated"/>）を流す。
/// </para>
/// <para>
/// 表の行のうち EF Core の答えが C# と割れるものは、派生の <see cref="Known"/> に理由つきで宣言する（宣言した行が一致したら赤）。
/// 宣言した行が DB の例外か EF Core の翻訳の失敗（<see cref="InvalidOperationException"/>）で終わったときも、割れたとみなす。
/// 生成物にその行の列が無い（方言切替が写せない）か型が違う行は、派生の <see cref="NotApplicable"/> に理由つきで宣言する
/// （宣言と付け替えられない行の集合が食い違えば赤＝黙って飛ばさない）。
/// </para>
/// </remarks>
public abstract class TranslatorParityEfCoreRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly IReadOnlyDictionary<string, string> None =
        new Dictionary<string, string>();

    /// <summary>
    /// EF Core の答えが C# と割れると分かっている行と、その理由。宣言した行は C# と割れることを表明する（一致したら赤）。
    /// </summary>
    protected abstract IReadOnlyDictionary<string, string> Known { get; }

    /// <summary>
    /// 生成物にその行の列が無いか型が違うので流せない行と、その理由（製品の事実）。付け替えられない行の集合と同じでなければ赤。
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string> NotApplicable => None;

    /// <summary>流す生成物（既定はチェックイン済みのフィクスチャ）</summary>
    protected virtual TranslatorParityEfCoreBinding Binding =>
        TranslatorParityEfCoreBinding.CheckedIn;

    /// <summary>スキーマを作り直し、生成の EF Core 版 Repository を登録した DI コンテナを返す</summary>
    protected abstract Task<ServiceProvider> CreateProviderAsync();

    /// <summary>
    /// シードの値を、生成物のプロパティへ入れる値へ直す（既定はそのまま）。方言のプロバイダの規則に合わせるときだけ差し込む。
    /// </summary>
    /// <param name="target">入れる先のプロパティ</param>
    /// <param name="value">翻訳器の一致のフィクスチャのシードの値</param>
    protected virtual object? SeedValue(PropertyInfo target, object? value) => value;

    /// <summary>宣言した行が表に実在し、割れる行と流せない行が重ならないことを表明する（行の改名で宣言が黙って効かなくなるのを防ぐ）</summary>
    [Fact(DisplayName = "[翻訳パリティ/EF Core] 割れる・流せないと宣言した行は述語の表に実在する")]
    public void Known_NamesExistingRows()
    {
        var ids = TranslatorParityPredicateRows.Table.Select(row => row.Id).ToHashSet();
        Known.Keys.Concat(NotApplicable.Keys).Where(id => !ids.Contains(id)).Should().BeEmpty();
        Known.Keys.Intersect(NotApplicable.Keys).Should().BeEmpty();
    }

    /// <summary>流せないと宣言した行が、生成物へ付け替えられない行とちょうど同じことを表明する</summary>
    [Fact(
        DisplayName = "[翻訳パリティ/EF Core] 流せないと宣言した行は、生成物へ付け替えられない行とちょうど同じ"
    )]
    public void NotApplicable_IsExactlyTheRowsThatCannotBeRetargeted()
    {
        var unretargetable = TranslatorParityPredicateRows
            .Table.Where(row =>
                Retarget(Input(row.Id).Predicate, Binding.Type(Input(row.Id).Entity)).Problem
                    is not null
            )
            .Select(row => row.Id);

        unretargetable.Should().BeEquivalentTo(NotApplicable.Keys);
    }

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
        if (NotApplicable.TryGetValue(id, out var notApplicable))
        {
            // 流せないことは NotApplicable_IsExactlyTheRowsThatCannotBeRetargeted が表明する
            TestContext.Current.TestOutputHelper?.WriteLine($"行 {id} は流せない: {notApplicable}");

            return;
        }

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

    /// <summary>行の入力：対象のエンティティとリポジトリの名前・シード・述語</summary>
    private static (
        string Entity,
        string Repository,
        IEnumerable Seeds,
        LambdaExpression Predicate
    ) Input(string id) =>
        TranslatorParityPredicateRows.Table[id].Input switch
        {
            ProbePredicate probe => (
                "ProbeEntity",
                "IProbeRepository",
                TranslatorParityRuntimeTestsBase.SeedProbes(),
                probe.Predicate
            ),
            SmallChildPredicate child => (
                "SmallChildEntity",
                "ISmallChildRepository",
                TranslatorParityRuntimeTestsBase.CrossTypeChildren(),
                child.Predicate
            ),
            BigParentPredicate parent => (
                "BigParentEntity",
                "IBigParentRepository",
                TranslatorParityRuntimeTestsBase.WideBigParents(),
                parent.Predicate
            ),
            WideProbePredicate wide => (
                "ProbeEntity",
                "IProbeRepository",
                TranslatorParityRuntimeTestsBase
                    .SeedProbes()
                    .Concat(TranslatorParityRuntimeTestsBase.WideProbes())
                    .ToList(),
                wide.Predicate
            ),
            _ => throw new InvalidOperationException($"行 {id} の入力の型を扱えない"),
        };

    /// <summary>
    /// 述語の表の 1 行のシードを写して入れ、全行を読み戻して C# のラムダを当てた結果（期待）と、Query() の結果（実際）をキーで返す
    /// </summary>
    private async Task<(List<long>, List<long>, LambdaExpression)> RunRowAsync(
        ServiceProvider provider,
        string id
    )
    {
        var (entityName, repositoryName, seeds, source) = Input(id);
        var entityType = Binding.Type(entityName);
        var (predicate, problem) = Retarget(source, entityType);

        if (predicate is null)
        {
            throw new InvalidOperationException($"行 {id} を付け替えられない: {problem}");
        }

        // small_child の外部キーは big_parent を指すので、親を先に入れる
        if (TranslatorParityPredicateRows.Table[id].Input is SmallChildPredicate)
        {
            var parents = provider.GetRequiredService(Binding.Type("IBigParentRepository"));

            foreach (var parent in TranslatorParityRuntimeTestsBase.CrossTypeParents())
            {
                await TranslatorParityEfCoreBinding.InsertAsync(
                    parents,
                    Copy(parent, Binding.Type("BigParentEntity")),
                    Ct
                );
            }
        }

        var repository = provider.GetRequiredService(Binding.Type(repositoryName));
        var count = 0;

        foreach (var seed in seeds)
        {
            await TranslatorParityEfCoreBinding.InsertAsync(repository, Copy(seed, entityType), Ct);
            count++;
        }

        var stored = await TranslatorParityEfCoreBinding.QueryAsync(repository, null, Ct);
        stored.Should().HaveCount(count);

        var compiled = predicate.Compile();
        var expected = stored.Where(e => (bool)compiled.DynamicInvoke(e)!).Select(KeyOf).ToList();
        var actual = (await TranslatorParityEfCoreBinding.QueryAsync(repository, predicate, Ct))
            .Select(KeyOf)
            .ToList();

        return (expected, actual, predicate);
    }

    /// <summary>行のキー（probes は probe_id・ほかは id）</summary>
    private static long KeyOf(object entity) =>
        Convert.ToInt64(
            (
                entity.GetType().GetProperty("ProbeId") ?? entity.GetType().GetProperty("Id")
            )!.GetValue(entity),
            System.Globalization.CultureInfo.InvariantCulture
        );

    /// <summary>翻訳器の一致のフィクスチャのエンティティを、同じ名前の列のプロパティへ写して生成物のエンティティにする</summary>
    /// <remarks>生成物に無い列（方言切替が写せず外した列）は写さない。その列を参照する行は流さない（<see cref="NotApplicable"/>）。</remarks>
    private object Copy(object source, Type targetType)
    {
        var target = Activator.CreateInstance(targetType)!;

        foreach (var property in ColumnProperties(source.GetType()))
        {
            if (targetType.GetProperty(property.Name) is { } destination)
            {
                destination.SetValue(target, SeedValue(destination, property.GetValue(source)));
            }
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
    /// 述語の引数の型を <paramref name="target"/> へ替え、引数のプロパティの参照を同じ名前のプロパティへ写す。
    /// 写せない（同じ名前のプロパティが無いか型が違う）ときは述語が null で、理由を返す。
    /// </summary>
    internal static (LambdaExpression? Lambda, string? Problem) Retarget(
        LambdaExpression predicate,
        Type target
    )
    {
        var parameter = Expression.Parameter(target, predicate.Parameters[0].Name);
        var retargeter = new EntityRetargeter(predicate.Parameters[0], parameter);
        var body = retargeter.Visit(predicate.Body);

        return retargeter.Problem is { } problem
            ? (null, problem)
            : (Expression.Lambda(body, parameter), null);
    }

    /// <summary>引数とそのプロパティの参照だけを付け替える。捕捉した変数・定数・メソッド呼び出しはそのまま残す</summary>
    private sealed class EntityRetargeter(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        public string? Problem { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != from)
            {
                return base.VisitMember(node);
            }

            var sourceType = ((PropertyInfo)node.Member).PropertyType;

            if (to.Type.GetProperty(node.Member.Name) is not { } property)
            {
                Problem ??= $"生成物に列 {node.Member.Name} が無い";
            }
            else if (property.PropertyType != sourceType)
            {
                Problem ??=
                    $"列 {node.Member.Name} の型が違う（{sourceType.Name} → {property.PropertyType.Name}）";
            }
            else
            {
                return Expression.Property(to, property);
            }

            return Expression.Default(sourceType);
        }
    }
}
