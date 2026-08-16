using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// リモートサービス生成（GenerateRemoteServices）による HTTP クライアント／サーバー実装の出力を、
/// 実経路（<see cref="DiagramCodeGenerator"/>）で検証するテストクラス
/// </summary>
/// <remarks>
/// ON では (1) クライアント実装（Http{Entity}RemoteRepository＋AddGeneratedHttpRemoteRepositories）を本体へ同梱し、
/// (2) サーバー実装（MapGeneratedRemoteEndpoints）を別ファイル {ベース名}.RemoteServer.g.cs へ出力する。
/// リモート面（GenerateRemoteContracts）は自動的に含意される。
/// </remarks>
public class RemoteServiceGenerationTests
{
    private readonly Entity _order;

    /// <summary>Order エンティティ（OrderId PK / CustomerId / Memo）を用意する</summary>
    public RemoteServiceGenerationTests()
    {
        _order = new Entity { TableName = "Order" };
        _order.Columns.Add(
            new Column
            {
                Name = "OrderId",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        _order.Columns.Add(
            new Column
            {
                Name = "CustomerId",
                DataType = "int",
                IsNullable = false,
            }
        );
        _order.Columns.Add(new Column { Name = "Memo", DataType = "nvarchar(200)" });
    }

    /// <summary>名前付きクエリ 2 件（DSL 一覧＋パラメータなし単一）付きの図を作る</summary>
    private ErDiagram CreateDiagram()
    {
        var diagram = new ErDiagram { Entities = { _order } };
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = _order.Id,
                Name = "GetByCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "customerId", Type = "int32" },
                },
                Condition = "CustomerId = @customerId",
                HasPaging = true,
            }
        );
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = _order.Id,
                Name = "FindTop",
                Returns = QueryReturnShape.Single,
                Implementation = QueryImplementationKind.Manual,
            }
        );

        return diagram;
    }

    /// <summary>実経路（SqlServer プロバイダ）で生成する</summary>
    private static CodeGenerationResult Generate(ErDiagram diagram, CodeGenerationOptions options)
    {
        var provider = new SqlServerProvider();
        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            options
        );
    }

    /// <summary>診断メッセージを整形して返す（アサーション失敗時の理由表示用）</summary>
    private static string FormatDiagnostics(CodeGenerationResult result) =>
        string.Join(" / ", result.Diagnostics.Select(d => $"{d.Severity}: {d.Message}"));

    /// <summary>ON: サーバー実装が別ファイルへ出力され、本体にクライアント実装が同梱されることを検証する</summary>
    [Fact(DisplayName = "ON: 本体＋{ベース名}.RemoteServer.g.cs の 2 ファイルが出力される")]
    public void Generate_RemoteServices_EmitsClientInMainAndServerInSeparateFile()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                OutputFileName = "Shop.g.cs",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result.Files.Select(f => f.FileName).Should().Equal("Shop.g.cs", "Shop.RemoteServer.g.cs");

        var main = result.Files[0].Content;
        var server = result.Files[1].Content;

        // リモート面（GenerateRemoteContracts）が自動含意される
        main.Should().Contain("public partial interface IOrderRemoteRepository");

        // クライアント固定 infra＋per-entity 実装＋DI が本体へ同梱される
        main.Should().Contain("public static class RemoteJson");
        main.Should().Contain("public sealed class RemoteRepositoryException");
        main.Should().Contain("public abstract partial class HttpRemoteRepository<TEntity, TKey>");
        main.Should()
            .Contain(
                "public sealed partial class HttpOrderRemoteRepository(HttpClient httpClient)"
            );
        main.Should().Contain("AddGeneratedHttpRemoteRepositories(");
        main.Should()
            .Contain(
                "services.AddScoped<IOrderRemoteRepository>(provider => new HttpOrderRemoteRepository("
            );

        // サーバーファイル: 固定エンジン（スキーマ非依存）＋エンドポイントマッピング（スキーマ依存）の 2 層構成
        server.Should().Contain("internal static class RemoteServerEngine");
        server.Should().Contain("public static partial class GeneratedRemoteEndpoints");
        server.Should().Contain("MapGeneratedRemoteEndpoints(");
        server.Should().Contain("string prefix = RemotePaths.DefaultPrefix");
        // per-entity のエンドポイントは固定エンジンの汎用マッピングを修飾付きで呼ぶ（別ファイル／別アセンブリでも同一テキスト）
        server
            .Should()
            .Contain("RemoteServerEngine.MapCrud<OrderEntity, int, IOrderRemoteRepository>(");
        server.Should().Contain("StatusCodes.Status409Conflict");
        server.Should().Contain("\"SaveConflict\"");

        // リクエスト解釈の失敗は 400（BadRequest）・Kestrel の BadHttpRequestException はステータス素通し
        // （エンジンから使う例外・メタデータ型はトップレベルへ出す＝入れ子だとエンジン外から参照できない）
        server.Should().Contain("internal sealed class RemoteBadRequestException : Exception");
        server.Should().Contain("StatusCodes.Status400BadRequest");
        server.Should().Contain("catch (BadHttpRequestException ex)");
        server.Should().Contain("await WriteErrorAsync(context, ex.StatusCode, \"BadRequest\"");

        // 500 の詳細公開は実行時引数で、既定は非公開（汎用文言＋相関 ID）。
        // OnServerError（partial＝アセンブリを跨げない）はラッパー RaiseServerError をデリゲートとして
        // メタデータへ載せ、エンジンが呼び出す
        server.Should().Contain("bool exposeErrorDetails = false");
        server
            .Should()
            .Contain(
                "group.WithMetadata(new RemoteErrorDetailPolicy(exposeErrorDetails, RaiseServerError));"
            );
        server.Should().Contain("internal sealed class RemoteErrorDetailPolicy(");
        server.Should().Contain("public Action<HttpContext, Exception>? OnServerError { get; }");
        server.Should().Contain("policy?.OnServerError?.Invoke(context, ex);");
        server.Should().Contain("private static void RaiseServerError(HttpContext context");
        server.Should().Contain("RemoteServerEngine.LogServerError(context, hookError);");
        server.Should().Contain("static partial void OnServerError(HttpContext context");
        server.Should().Contain("\"An unexpected error occurred on the server.\"");
        server.Should().Contain("correlationId: expose ? null : context.TraceIdentifier");

        // サーバーファイルは ASP.NET Core の using を持ち、本体は持たない
        server.Should().Contain("using Microsoft.AspNetCore.Builder;");
        main.Should().NotContain("Microsoft.AspNetCore");
    }

    /// <summary>名前付きクエリ: クライアントは転送メソッド・サーバーはリクエストレコード＋ハンドラが出ることを検証する</summary>
    [Fact(
        DisplayName = "名前付きクエリが転送メソッド（client）とハンドラ＋レコード（server）になる"
    )]
    public void Generate_RemoteServices_ForwardsNamedQueries()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var main = result.Files[0].Content;
        var server = result.Files[1].Content;

        // クライアント: シグネチャは契約と同一・本体は InvokeAsync への転送（manual クエリも同様＝実装方式に依らない）
        main.Should()
            .Contain(
                "public Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, int take, int skip = 0, CancellationToken cancellationToken = default) =>"
            );
        main.Should()
            .Contain(
                "InvokeAsync<IReadOnlyList<OrderEntity>>(\"GetByCustomer\", new { customerId, take, skip }, cancellationToken);"
            );
        main.Should().Contain("InvokeAsync<OrderEntity?>(\"FindTop\", null, cancellationToken);");

        // サーバー: リクエストレコード（PascalCase プロパティ）＋リモート面への委譲ハンドラ
        server
            .Should()
            .Contain(
                "private sealed record OrderGetByCustomerRequest(int CustomerId, int Take, int Skip);"
            );
        server.Should().Contain("\"Order/GetByCustomer\"");
        server
            .Should()
            .Contain(
                "repository.GetByCustomerAsync(request.CustomerId, request.Take, request.Skip, context.RequestAborted)"
            );

        // パラメータなしクエリはリクエストレコードを作らず本文も読まない
        server.Should().Contain("\"Order/FindTop\"");
        server.Should().NotContain("OrderFindTopRequest");
    }

    /// <summary>OFF（既定）: 出力が 1 ファイルのままで、リモートサービス関連の型が一切出ないことを検証する</summary>
    [Fact(DisplayName = "OFF（既定）: 1 ファイルのまま・HTTP クライアント/サーバー関連は出ない")]
    public void Generate_Default_DoesNotEmitRemoteServices()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions { RootNamespace = "Test.Ns", GenerateRepositories = true }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result.Files.Should().ContainSingle();

        var main = result.Files[0].Content;
        main.Should().NotContain("RemoteJson");
        main.Should().NotContain("HttpRemoteRepository");
        main.Should().NotContain("AddGeneratedHttpRemoteRepositories");
    }

    /// <summary>
    /// HTTP クライアントの DI 登録が非 keyed・keyed の 2 系統（それぞれ baseAddress 版と HttpClient ファクトリ版）で
    /// 出力されることを検証する。
    /// </summary>
    /// <remarks>
    /// keyed 版はハイブリッド構成（キー "server"＝HTTP・キー "local"＝方言エンジン）の必須部品で、方言側の
    /// <c>AddGenerated{方言}Repositories(serviceKey, ...)</c> と対になる。共有 HttpClient も同じキーで登録するため、
    /// 非 keyed 登録とも別キーとも衝突しない（コンテナ所有＝破棄も従来どおり）。
    /// </remarks>
    [Fact(DisplayName = "ON: HTTP クライアント DI に keyed オーバーロード 2 本が追加される")]
    public void Generate_RemoteServices_EmitsKeyedHttpRegistrationOverloads()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));

        // 改行を LF へ正規化して、複数行のシグネチャをそのままの形で照合する
        var main = result.Files[0].Content.ReplaceLineEndings("\n");
        const string Signature =
            "public static IServiceCollection AddGeneratedHttpRemoteRepositories(\n        this IServiceCollection services,\n";

        // 非 keyed の 2 本（従来どおり）
        main.Should().Contain(Signature + "        string baseAddress\n    )");
        main.Should()
            .Contain(
                Signature + "        Func<IServiceProvider, HttpClient> httpClientFactory\n    )"
            );

        // keyed の 2 本
        main.Should()
            .Contain(Signature + "        object? serviceKey,\n        string baseAddress\n    )");
        main.Should()
            .Contain(
                Signature
                    + "        object? serviceKey,\n        Func<IServiceProvider, HttpClient> httpClientFactory\n    )"
            );

        // 共有 HttpClient は同じキーで登録し、per-entity のリモート面は keyed scoped で登録する
        main.Should().Contain("services.AddKeyedSingleton(\n            serviceKey,");
        main.Should()
            .Contain("provider.GetRequiredKeyedService<OwnedHttpClient>(serviceKey).Client");
        main.Should().Contain("services.AddKeyedScoped<IOrderRemoteRepository>(");

        // 非 keyed 版は従来のまま（キー無しの登録が keyed へ置き換わっていない）
        main.Should()
            .Contain(
                "services.AddScoped<IOrderRemoteRepository>(provider => new HttpOrderRemoteRepository("
            );
        main.Should().Contain("services.AddSingleton(_ => new OwnedHttpClient(");
    }

    /// <summary>分割出力: サーバーは RemoteServer.g.cs・専用 namespace で出て、他バケットの namespace を using することを検証する</summary>
    [Fact(DisplayName = "分割出力: RemoteServer.g.cs が専用 namespace＋クロス using で出る")]
    public void Generate_RemoteServicesWithSplit_EmitsServerFileWithCrossUsings()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                SplitFilesByCategory = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var server = result.Files.Single(f => f.FileName == "RemoteServer.g.cs");

        server.Content.Should().Contain("namespace Acme.RemoteServer;");
        server.Content.Should().Contain("using Acme.Entities;");
        server.Content.Should().Contain("using Acme.Repositories;");
        server.Content.Should().Contain("using Acme.Runtime;");
        // 固定部は Runtime.AspNetCore.g.cs へ分かれるため、その namespace も using する
        server.Content.Should().Contain("using Acme.Runtime.AspNetCore;");
    }

    /// <summary>
    /// 分割出力では、サーバー実装が「固定エンジン＝Runtime.AspNetCore.g.cs」と
    /// 「per-entity＝RemoteServer.g.cs」の 2 ファイルへ分かれることを検証する
    /// （他バケットと同じ「固定 infra は Runtime 系・スキーマ依存物は各カテゴリ」の対称構成）。
    /// </summary>
    [Fact(DisplayName = "分割出力: サーバー固定部が Runtime.AspNetCore.g.cs へ分かれる")]
    public void Generate_RemoteServicesWithSplit_SeparatesEngineIntoFixedRuntimeFile()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                SplitFilesByCategory = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));

        var engine = result.Files.Single(f => f.FileName == "Runtime.AspNetCore.g.cs");
        engine.Content.Should().Contain("namespace Acme.Runtime.AspNetCore;");
        // 固定部は共通契約（RemoteJson・エンベロープ）をコア相当ファイルから using で参照する
        engine.Content.Should().Contain("using Acme.Runtime;");
        engine.Content.Should().Contain("internal static class RemoteServerEngine");
        engine.Content.Should().Contain("internal sealed class RemoteBadRequestException");
        engine.Content.Should().Contain("internal sealed class RemoteErrorDetailPolicy(");
        // スキーマ依存物（エンドポイント本体・per-entity）は 1 つも混ざらない
        engine.Content.Should().NotContain("class GeneratedRemoteEndpoints");
        engine.Content.Should().NotContain("MapOrderEndpoints");
        engine.Content.Should().NotContain("OrderEntity");

        // per-entity 側は固定部の型定義を持たず、修飾付きで呼ぶだけ
        var server = result.Files.Single(f => f.FileName == "RemoteServer.g.cs");
        server.Content.Should().Contain("public static partial class GeneratedRemoteEndpoints");
        server.Content.Should().Contain("RemoteServerEngine.MapCrud<");
        server.Content.Should().NotContain("static class RemoteServerEngine");
        server.Content.Should().NotContain("class RemoteBadRequestException");
    }

    /// <summary>
    /// パッケージ参照モードでは固定部ファイルを出さず、per-entity のサーバーファイルが
    /// 固定名前空間（<see cref="RuntimePackages.AspNetCore"/>）を using するだけになることを検証する。
    /// </summary>
    [Fact(DisplayName = "パッケージ参照モード: サーバー固定部は出力されず using だけになる")]
    public void Generate_RemoteServicesWithRuntimePackages_ReferencesAspNetCorePackage()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Acme",
                OutputFileName = "Shop.g.cs",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
                UseRuntimePackages = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));

        var server = result.Files.Single(f => f.FileName == "Shop.RemoteServer.g.cs");
        server.Content.Should().Contain($"using {RuntimePackages.AspNetCore};");
        server.Content.Should().NotContain("static class RemoteServerEngine");
        server.Content.Should().NotContain("class RemoteBadRequestException");
        // per-entity 側のテキストはモードに依らず同一（修飾呼び出しのみ）
        server.Content.Should().Contain("RemoteServerEngine.MapCrud<");

        // 案内にも ASP.NET Core パッケージが載る（安定順の末尾）
        RuntimePackageReferenceGuidance
            .Compute(
                new CodeGenerationOptions
                {
                    GenerateRepositories = true,
                    GenerateRemoteServices = true,
                    UseRuntimePackages = true,
                }
            )
            .Should()
            .Equal(RuntimePackages.Core, RuntimePackages.SqlServer, RuntimePackages.AspNetCore);
    }

    /// <summary>EF Core 単独＋リモートサービス: 契約・クライアント・サーバーが EF Core 版 Repository 基準でも成立することを検証する</summary>
    [Fact(DisplayName = "EF Core 単独でもクライアント／サーバーが出力される")]
    public void Generate_RemoteServicesWithEfCoreOnly_Works()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = false,
                GenerateEfCore = true,
                GenerateRemoteServices = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        result.Files.Should().HaveCount(2);
        result.Files[0].Content.Should().Contain("HttpOrderRemoteRepository");
        result.Files[1].Content.Should().Contain("MapGeneratedRemoteEndpoints");
    }

    /// <summary>
    /// リモートのルート（プレフィックス／health）が公開定数 <c>RemotePaths</c> へ一点集約され、
    /// サーバーの既定引数・health マッピング・クライアントの liveness チェックがいずれも定数を参照することを固定する
    /// </summary>
    /// <remarks>
    /// サーバー既定引数とクライアント DI 登録の双方に <c>"/quicker"</c> がリテラルで散っていた重複の解消。
    /// サーバーファイルにリテラルが残っていないこと（NotContain）まで見て、定数化の抜けを検知する。
    /// </remarks>
    [Fact(DisplayName = "リモートのプレフィックスと health ルートが公開定数へ集約される")]
    public void Generate_RemoteServices_SharesRoutesThroughRemotePaths()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateRemoteServices = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var main = result.Files[0].Content;
        var server = result.Files[1].Content;

        // 定数の正本はクライアント側固定 infra（Core パッケージ収載）
        main.Should().Contain("public static class RemotePaths");
        main.Should().Contain("public const string DefaultPrefix = \"/quicker\";");
        main.Should().Contain("public const string HealthRoute = \"health\";");

        // クライアントの liveness チェックも同じ定数を使う
        main.Should()
            .Contain(
                "public async Task<bool> PingAsync(CancellationToken cancellationToken = default)"
            );
        main.Should().Contain("RemotePaths.HealthRoute,");

        // サーバー: 既定引数も health ルートも定数参照で、リテラルは残っていない
        server.Should().Contain("string prefix = RemotePaths.DefaultPrefix");
        server.Should().Contain("group.MapGet(RemotePaths.HealthRoute, () => Results.Ok());");
        server.Should().NotContain("\"/quicker\"");
        server.Should().NotContain("\"health\"");
    }
}
