using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.CodeGen.CSharp;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.GeneratedTranslatorParityEfCoreFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 述語の表（<see cref="TranslatorParityPredicateRows"/>）を流す EF Core 版の生成物：エンティティとリポジトリの型を名前で引き、
/// リポジトリを反射で呼ぶ（生成物は名前空間ごとに別の型になり、基底から名指しできないため）。
/// </summary>
/// <remarks>
/// チェックイン済みのフィクスチャ（<see cref="CheckedIn"/>＝SQL Server 方言で生成）と、方言切替で写した図から実行時に生成した物
/// （<see cref="TranslatorParityEfCoreGenerated"/>）の両方を同じ形で扱う。
/// </remarks>
public sealed class TranslatorParityEfCoreBinding(Assembly assembly, string ns)
{
    /// <summary>チェックイン済みの固定フィクスチャ（<see cref="TranslatorParityEfCoreFixtureDefinition"/>）</summary>
    public static TranslatorParityEfCoreBinding CheckedIn { get; } =
        new(typeof(ProbeEntity).Assembly, TranslatorParityEfCoreFixtureDefinition.NamespaceName);

    /// <summary>生成物の名前空間の型を名前で引く</summary>
    public Type Type(string name) =>
        assembly.GetType($"{ns}.{name}")
        ?? throw new InvalidOperationException($"生成物に型 {ns}.{name} が無い");

    /// <summary>生成の DI の登録（<c>AddGeneratedEfCoreRepositories</c>）でリポジトリを登録したコンテナを作る</summary>
    public ServiceProvider CreateProvider(Action<DbContextOptionsBuilder> configure)
    {
        var register = Type("GeneratedEfCoreRepositoryServiceCollectionExtensions")
            .GetMethod("AddGeneratedEfCoreRepositories")!;
        var services = new ServiceCollection();
        register.Invoke(null, [services, configure]);

        return services.BuildServiceProvider();
    }

    /// <summary>エンティティを 1 件入れる（<c>InsertAsync</c>）</summary>
    public static async Task InsertAsync(object repository, object entity, CancellationToken ct)
    {
        var insert = RepositoryMethods(repository)
            .First(m =>
                m.Name == "InsertAsync"
                && m.GetParameters() is [var first, var second]
                && first.ParameterType == entity.GetType()
                && second.ParameterType == typeof(CancellationToken)
            );
        await (Task)insert.Invoke(repository, [entity, ct])!;
    }

    /// <summary><c>Query()</c> に述語を（null なら当てずに）掛けて全行を読む</summary>
    public static async Task<List<object>> QueryAsync(
        object repository,
        LambdaExpression? predicate,
        CancellationToken ct
    )
    {
        var query = RepositoryMethods(repository)
            .First(m => m.Name == "Query" && m.GetParameters().Length == 0)
            .Invoke(repository, [])!;

        if (predicate is not null)
        {
            query = query
                .GetType()
                .GetMethod("Where", [predicate.GetType()])!
                .Invoke(query, [predicate])!;
        }

        var task = (Task)
            query
                .GetType()
                .GetMethod("ToListAsync", [typeof(CancellationToken)])!
                .Invoke(query, [ct])!;
        await task;

        return ((IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!)
            .Cast<object>()
            .ToList();
    }

    private static IEnumerable<MethodInfo> RepositoryMethods(object repository) =>
        repository
            .GetType()
            .GetInterfaces()
            .SelectMany(i => i.GetMethods())
            .Concat(repository.GetType().GetMethods());
}

/// <summary>
/// 述語の表の図（<see cref="TranslatorParityEfCoreFixtureDefinition.Build"/>）を方言切替（<see cref="DiagramTypeConverter"/>）で
/// 方言へ写し、その方言の型マッパーで EF Core 版 Repository 単独へ生成・コンパイルした物（方言ごとにプロセスで 1 回）
/// </summary>
/// <remarks>
/// <para>
/// 利用者が方言を切り替えて生成したときに受け取るコードそのものを流すため、チェックイン済みのフィクスチャ（SQL Server 方言）を
/// 使い回さない。PostgreSQL は日時の列に <c>HasColumnType</c> が付き、MySQL は <c>at_offset</c> が DateTime になるなど、
/// 生成の Fluent と CLR の型が方言で変わる。
/// </para>
/// <para>
/// 図から外すもの：rowversion の表 <c>versioned_tags</c>（どの方言にも写せず、述語の表のどの行も使わない）と、
/// 方言切替が写せない列（Oracle の <c>clock</c>＝Oracle の型マッパーに TimeSpan に写す型が無い）。外した列を参照する行は、
/// 派生が <see cref="TranslatorParityEfCoreRuntimeTestsBase.NotApplicable"/> に宣言する。
/// </para>
/// <para>
/// 生成とコンパイルはテストの実行時に今のテンプレートと型マッパーで行うので、どちらを変えても同じコミットで表が流れる
/// （チェックイン済みのフィクスチャの再生成を待たない）。
/// </para>
/// </remarks>
public static class TranslatorParityEfCoreGenerated
{
    /// <summary>方言ごとの生成物</summary>
    /// <param name="Binding">型とリポジトリの引き方</param>
    /// <param name="Ddl">写した図の、その方言の DDL</param>
    /// <param name="DroppedColumns">方言切替が写せず外した列（表.列）</param>
    public sealed record Generated(
        TranslatorParityEfCoreBinding Binding,
        string Ddl,
        IReadOnlyList<string> DroppedColumns
    );

    private static readonly ConcurrentDictionary<string, Lazy<Generated>> Cache = new(
        StringComparer.Ordinal
    );

    /// <summary>方言の生成物（初回だけ生成・コンパイルする）</summary>
    /// <param name="dbms">PostgreSQL・MySQL・Oracle のどれか（<see cref="DialectNames"/>）</param>
    public static Generated For(string dbms) =>
        Cache.GetOrAdd(dbms, key => new Lazy<Generated>(() => Build(key))).Value;

    private static Generated Build(string dbms)
    {
        var (provider, ddl) = dbms switch
        {
            DialectNames.PostgreSql => (
                (IDatabaseProvider)new PostgreSqlProvider(),
                (Func<ErDiagram, string>)(d => new PostgreSqlDdlGenerator().Build(d))
            ),
            DialectNames.MySql => (new MySqlProvider(), d => new MySqlDdlGenerator().Build(d)),
            DialectNames.Oracle => (new OracleProvider(), d => new OracleDdlGenerator().Build(d)),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, "写す先の方言ではない"),
        };

        var diagram = TranslatorParityEfCoreFixtureDefinition.Build();
        diagram.Entities.Remove(diagram.Entities.Single(e => e.TableName == "versioned_tags"));
        var plan = DiagramTypeConverter.CreatePlan(
            diagram,
            new SqlServerProvider().TypeCatalog,
            provider.TypeCatalog
        );
        DiagramTypeConverter.Apply(diagram, plan);
        var dropped = new List<string>();

        foreach (var column in plan.Unconverted)
        {
            var entity = diagram.Entities.Single(e => e.TableName == column.TableName);
            entity.Columns.Remove(entity.Columns.Single(c => c.Id == column.ColumnId));
            dropped.Add($"{column.TableName}.{column.ColumnName}");
        }

        diagram.TargetDbms = dbms;
        var ns = $"{TranslatorParityEfCoreFixtureDefinition.NamespaceName}.Runtime.{dbms}";
        var generated = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            TranslatorParityEfCoreFixtureDefinition.Options with
            {
                RootNamespace = ns,
            }
        );

        if (generated.HasErrors)
        {
            throw new InvalidOperationException(
                $"{dbms} の生成が断られた: "
                    + string.Join(" / ", generated.Diagnostics.Select(d => d.Message))
            );
        }

        var assembly = GeneratedCodeCompiler.CompileAndLoad(generated, $"{ns}.{Guid.NewGuid():N}");

        return new Generated(
            new TranslatorParityEfCoreBinding(assembly, ns),
            ddl(diagram),
            dropped
        );
    }
}
