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

        // サーバーファイル: エンドポイントマッピング＋例外変換＋CRUD 汎用マッピング
        server.Should().Contain("public static partial class GeneratedRemoteEndpoints");
        server.Should().Contain("MapGeneratedRemoteEndpoints(");
        server.Should().Contain("string prefix = RemotePaths.DefaultPrefix");
        server.Should().Contain("MapCrud<OrderEntity, int, IOrderRemoteRepository>(");
        server.Should().Contain("StatusCodes.Status409Conflict");
        server.Should().Contain("\"SaveConflict\"");

        // リクエスト解釈の失敗は 400（BadRequest）・Kestrel の BadHttpRequestException はステータス素通し
        server.Should().Contain("private sealed class RemoteBadRequestException");
        server.Should().Contain("StatusCodes.Status400BadRequest");
        server.Should().Contain("catch (BadHttpRequestException ex)");
        server.Should().Contain("await WriteErrorAsync(context, ex.StatusCode, \"BadRequest\"");

        // 500 の詳細公開は実行時引数で、既定は非公開（汎用文言＋相関 ID）
        server.Should().Contain("bool exposeErrorDetails = false");
        server
            .Should()
            .Contain("group.WithMetadata(new RemoteErrorDetailPolicy(exposeErrorDetails));");
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
