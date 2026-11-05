using System.Reflection;
using QuickER.CodeGen.CSharp;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;

/// <summary>
/// 決まった行が生成物を読み込んで動かして表明する期待 1 つ（実行の探針。<see cref="CodegenProbe"/> の決まった種類に収まらない、元のテストごとの振る舞い）
/// </summary>
/// <remarks>
/// <para>
/// 読み込んだアセンブリは行の探針・宣言の期待と同じコンパイルから emit したもの（<see cref="Probes.Load"/>）。反射の補助は <see cref="ProbeKit"/>。
/// 生成テキストは読まない（読むのは型・メンバーを呼んだ結果だけ）。
/// </para>
/// <para>生成物が投げた例外は <see cref="TargetInvocationException"/> を外して名指しする。</para>
/// </remarks>
/// <param name="Name">名前（段の名前 <c>exec:{名前}</c> と問題の文に使う）</param>
/// <param name="Check">読み込んだ生成物を動かして問題を返す</param>
internal sealed record ExecCheck(string Name, Func<Assembly, IEnumerable<string>> Check)
{
    /// <summary>段の名前</summary>
    public string Stage => $"exec:{Name}";

    /// <summary>動かす（問題を返す）</summary>
    /// <param name="assembly">読み込んだ生成物</param>
    public IReadOnlyList<string> Run(Assembly assembly)
    {
        try
        {
            return Check(assembly).Select(problem => $"実行（{Name}）: {problem}").ToList();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            return [$"実行（{Name}）: 生成物が {inner.GetType().Name} を投げた（{inner.Message}）"];
        }
    }
}

/// <summary>
/// 決まった行が生成結果を構文の解析器（C# のリバースのパーサなど）に通して表明する期待 1 つ
/// </summary>
/// <remarks>
/// 生成テキストを部分文字列・正規表現・行で読まない。読むのは、製品の解析器（<c>CSharpReverseParser</c> など）が組み立てた結果だけ。
/// </remarks>
/// <param name="Name">名前（段の名前 <c>parsed:{名前}</c> と問題の文に使う）</param>
/// <param name="Check">生成結果を解析して問題を返す</param>
internal sealed record ParsedCheck(
    string Name,
    Func<CodeGenerationResult, IEnumerable<string>> Check
)
{
    /// <summary>段の名前</summary>
    public string Stage => $"parsed:{Name}";

    /// <summary>確かめる（問題を返す）</summary>
    /// <param name="result">生成結果</param>
    public IReadOnlyList<string> Run(CodeGenerationResult result) =>
        Check(result).Select(problem => $"解析（{Name}）: {problem}").ToList();
}

/// <summary>実行の探針（<see cref="ExecCheck"/>）が生成物の型・メンバーを反射で呼ぶ補助</summary>
internal static class ProbeKit
{
    private const BindingFlags Instance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private const BindingFlags Static =
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Static
        | BindingFlags.FlattenHierarchy;

    /// <summary>
    /// 単純名で型をちょうど 1 つ引く（ジェネリックは <c>名前`型引数の数</c>。<paramref name="ns"/> を指定すればその名前空間に絞る）
    /// </summary>
    public static Type Type(Assembly assembly, string name, string? ns = null)
    {
        var types = assembly
            .GetTypes()
            .Where(type => type.Name == name && (ns is null || type.Namespace == ns))
            .ToList();

        return types.Count == 1
            ? types[0]
            : throw new InvalidOperationException(
                $"型 {name}{(ns is null ? "" : $"（{ns}）")} が {types.Count} 個（ちょうど 1 つに決まること）"
            );
    }

    /// <summary>引数の数と型が合う公開・非公開のコンストラクタでインスタンスを作る</summary>
    public static object Create(Type type, params object?[] arguments) =>
        Activator.CreateInstance(type, Instance, binder: null, arguments, culture: null)
        ?? throw new InvalidOperationException($"{type.Name} を作れない");

    /// <summary>インスタンスのプロパティを読む（継承したものを含む）</summary>
    public static object? Get(object target, string property) =>
        FindProperty(target.GetType(), property, Instance).GetValue(target);

    /// <summary>インスタンスのプロパティへ書く（継承したものを含む）</summary>
    public static void Set(object target, string property, object? value) =>
        FindProperty(target.GetType(), property, Instance).SetValue(target, value);

    /// <summary>静的プロパティを読む（基底の静的メンバーを含む）</summary>
    public static object? GetStatic(Type type, string property) =>
        FindProperty(type, property, Static).GetValue(null);

    /// <summary>静的フィールド（非公開を含む・基底の静的メンバーを含む）を読む</summary>
    public static object GetStaticField(Type type, string field) =>
        type.GetField(field, Static)?.GetValue(null)
        ?? throw new InvalidOperationException(
            $"{type.Name} に静的フィールド {field} が無い（または null）"
        );

    /// <summary>インスタンスのメソッドを名前と引数の数で選んで呼ぶ（同じ数の多重定義が複数あるときは引数の型で選ぶ）</summary>
    public static object? Call(object target, string method, params object?[] arguments) =>
        FindMethod(target.GetType(), method, Instance, arguments).Invoke(target, arguments);

    /// <summary>静的メソッドを名前と引数の数で選んで呼ぶ（基底の静的メンバーを含む）</summary>
    public static object? CallStatic(Type type, string method, params object?[] arguments) =>
        FindMethod(type, method, Static, arguments).Invoke(null, arguments);

    /// <summary>タスクの完了を待って結果（<c>Task</c> なら null）を返す</summary>
    public static object? Await(object? task)
    {
        if (task is not Task awaited)
        {
            return task;
        }

        awaited.GetAwaiter().GetResult();
        return awaited.GetType().GetProperty("Result")?.GetValue(awaited);
    }

    /// <summary>
    /// 空の <c>ServiceCollection</c> へ、生成物の DI の拡張メソッド（名前で引く・最初の引数が <c>IServiceCollection</c>）を
    /// <paramref name="arguments"/> つきで呼び、登録を返す
    /// </summary>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection Services(
        Assembly assembly,
        string extensionMethod,
        params object?[] arguments
    )
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var all = new object?[] { services }
            .Concat(arguments)
            .ToArray();
        var method = assembly
            .GetTypes()
            .Where(type => type.IsAbstract && type.IsSealed)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(candidate =>
                candidate.Name == extensionMethod
                && candidate.GetParameters().Length == all.Length
                && Accepts(candidate.GetParameters(), all)
            )
            .ToList();

        if (method.Count != 1)
        {
            throw new InvalidOperationException(
                $"拡張メソッド {extensionMethod}（引数 {all.Length} 個）が {method.Count} 個"
            );
        }

        method[0].Invoke(null, all);
        return services;
    }

    /// <summary>
    /// 生成物の QuickER 版 Repository（SQLite 方言）を実際の SQLite のファイル DB で動かす：一時フォルダに DB を作り、
    /// 図から SQLite の DDL（製品の <c>SqliteDdlGenerator</c>）を流し、生成物の <c>AddGeneratedSqliteRepositories(接続文字列)</c> で
    /// 組んだ DI から <paramref name="run"/> を呼ぶ（終われば DB とフォルダを消す）
    /// </summary>
    /// <remarks>
    /// 生成物の SQL（ページング・日付部品・<c>RETURNING</c>・IN 句・生 SQL）を、文字列の断片でなく SQLite が実際に受け付けて正しい行を返すことで
    /// 確かめるための土台。図は行の生成に使ったものと同じものを渡す。接続はプールしない（片付けでファイルを消せるように）。
    /// </remarks>
    /// <param name="assembly">読み込んだ生成物（SQLite 方言の QuickER 版 Repository を含む）</param>
    /// <param name="diagram">行の図（DDL を作る）</param>
    /// <param name="run">組んだ DI と接続文字列を受け取って問題を返す</param>
    public static IReadOnlyList<string> WithSqlite(
        Assembly assembly,
        QuickER.Model.ErDiagram diagram,
        Func<IServiceProvider, string, Task<IEnumerable<string>>> run
    )
    {
        using var folder = new QuickER.Tests.TestSupport.TestTempFolder();
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = System.IO.Path.Combine(folder.Path, "probe.db"),
            Pooling = false,
        }.ToString();

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = new QuickER.Provider.Sqlite.SqliteDdlGenerator().Build(diagram);
            command.ExecuteNonQuery();
        }

        var services = Services(assembly, "AddGeneratedSqliteRepositories", connectionString);
        using var provider =
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
                services
            );
        return run(provider, connectionString).GetAwaiter().GetResult().ToList();
    }

    private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);

            if (property is not null)
            {
                return property;
            }
        }

        throw new InvalidOperationException($"{type.Name} にプロパティ {name} が無い");
    }

    private static MethodInfo FindMethod(
        Type type,
        string name,
        BindingFlags flags,
        object?[] arguments
    )
    {
        var candidates = type.GetMethods(flags)
            .Where(method =>
                method.Name == name && method.GetParameters().Length == arguments.Length
            )
            .ToList();

        if (candidates.Count > 1)
        {
            candidates = candidates
                .Where(method => Accepts(method.GetParameters(), arguments))
                .ToList();
        }

        return candidates.Count == 1
            ? candidates[0]
            : throw new InvalidOperationException(
                $"{type.Name}.{name}（引数 {arguments.Length} 個）が {candidates.Count} 個"
            );
    }

    private static bool Accepts(ParameterInfo[] parameters, object?[] arguments) =>
        parameters
            .Zip(arguments)
            .All(pair =>
                pair.Second is null
                    ? !pair.First.ParameterType.IsValueType
                        || Nullable.GetUnderlyingType(pair.First.ParameterType) is not null
                    : pair.First.ParameterType.IsInstanceOfType(pair.Second)
            );
}
