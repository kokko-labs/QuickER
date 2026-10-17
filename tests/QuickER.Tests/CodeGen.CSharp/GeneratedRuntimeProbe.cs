using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 読み込んだ生成物を反射で動かし、コンパイルでは分からない実行時の失敗（JSON の設定の組み立て・エンドポイントの重なり）を探す補助。
/// </summary>
/// <remarks>
/// エンティティは JSON で読み書きされ、リモートの転送（<c>RemoteJson.Options</c>）と SQL Server の Include の読み込み
/// （<c>SqlServerSqlQueryExecutor&lt;TEntity&gt;</c> の FOR JSON の設定）はどちらもプロパティ名の大文字小文字を区別しない。
/// 大文字小文字だけ違うメンバーがあると、コンパイルは通っても型の情報を組み立てる時点で例外になる。
/// エンドポイントはルーティングが大文字小文字を区別しないので、同じ HTTP メソッドで大小だけ違うパターンがあると衝突する。
/// </remarks>
internal static class GeneratedRuntimeProbe
{
    /// <summary>生成物の型を名前で 1 つ探す</summary>
    public static Type TypeNamed(Assembly assembly, string name) =>
        assembly.GetTypes().Single(type => type.Name == name);

    /// <summary>生成物の公開の静的メソッド（拡張メソッド）を名前で探して呼ぶ（引数の型を渡せば多重定義をその型で選ぶ）</summary>
    public static object? InvokeExtension(
        Assembly assembly,
        string name,
        Type[]? parameterTypes,
        params object?[] arguments
    )
    {
        var method = assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(method =>
                method.Name == name
                && (
                    parameterTypes is null
                    || method
                        .GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .SequenceEqual(parameterTypes)
                )
            );

        return method.Invoke(null, arguments);
    }

    /// <summary>
    /// 全エンティティについて、リモートの JSON の設定で直列化して読み戻し、SQL Server の FOR JSON の設定で型の情報を組み立てる。
    /// 失敗したものを（種類, 説明）で返す
    /// </summary>
    public static IEnumerable<(string Kind, string Detail)> FindJsonProblems(Assembly assembly)
    {
        var entityBase = TypeNamed(assembly, "EntityBaseCore");
        var entities = assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && entityBase.IsAssignableFrom(type))
            .ToList();
        var remote = (JsonSerializerOptions)
            TypeNamed(assembly, "RemoteJson").GetProperty("Options")!.GetValue(null)!;
        var forJson = assembly
            .GetTypes()
            .SingleOrDefault(type => type.Name == "SqlServerSqlQueryExecutor`1");

        foreach (var entity in entities)
        {
            string? remoteFailure = null;

            try
            {
                var json = JsonSerializer.Serialize(
                    Activator.CreateInstance(entity),
                    entity,
                    remote
                );
                JsonSerializer.Deserialize(json, entity, remote);
            }
            catch (Exception ex)
            {
                remoteFailure = $"{entity.Name}: {ex.GetBaseException().Message}";
            }

            if (remoteFailure is not null)
            {
                yield return ("json-remote", remoteFailure);
            }

            if (forJson is null)
            {
                continue;
            }

            string? forJsonFailure = null;

            try
            {
                var options = (JsonSerializerOptions)
                    forJson
                        .MakeGenericType(entity)
                        .GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static)!
                        .GetValue(null)!;
                options.GetTypeInfo(entity);
            }
            catch (Exception ex)
            {
                forJsonFailure = $"{entity.Name}: {ex.GetBaseException().Message}";
            }

            if (forJsonFailure is not null)
            {
                yield return ("json-for-json", forJsonFailure);
            }
        }
    }

    /// <summary>
    /// リモートのエンドポイントを実際に登録し、同じ HTTP メソッドで大文字小文字だけ違うパターンの組を返す（起動はしない）
    /// </summary>
    public static IEnumerable<string> FindRouteCaseCollisions(Assembly assembly)
    {
        var app = WebApplication.CreateSlimBuilder().Build();

        try
        {
            var access = Enum.Parse(TypeNamed(assembly, "RemoteAccess"), "AllowAnonymous");
            InvokeExtension(
                assembly,
                "MapGeneratedRemoteEndpoints",
                null,
                app,
                access,
                "/quicker",
                false
            );

            var routes = ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(endpoint =>
                    (
                        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]
                    ).Select(method =>
                        (Method: method, Pattern: endpoint.RoutePattern.RawText ?? string.Empty)
                    )
                );

            return routes
                .GroupBy(
                    route => $"{route.Method} {route.Pattern}",
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(group =>
                    group.Select(route => route.Pattern).Distinct(StringComparer.Ordinal).Count()
                    > 1
                )
                .Select(group =>
                    string.Join(" | ", group.Select(route => $"{route.Method} {route.Pattern}"))
                )
                .ToList();
        }
        finally
        {
            ((IDisposable)app).Dispose();
        }
    }
}
