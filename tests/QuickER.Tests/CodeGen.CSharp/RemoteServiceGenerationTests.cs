using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

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
    // 残す理由（P3-4b）：500 の汎用文言（生成器の固定の英文）と相関 ID の三項式（式本体）。構造の表明は行 code-remote-service-main-client-and-server-engine-declarations へ移した
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

        // 出力が本体＋サーバーの 2 ファイルであること（名前と並び）は、決まった行 files-remote-service-main-and-server-files が見る。
        // 型・メンバーの宣言・using・呼び出しの構造（リモート面の契約・クライアント固定 infra・DI・固定エンジン・エンドポイントのマッピング・
        // 競合 409 / 要求の不備 400 の分類・500 の詳細公開とフック）は、決まった行 code-remote-service-main-client-and-server-engine-declarations へ移した
        var server = result.Files[1].Content;

        server.Should().Contain("\"An unexpected error occurred on the server.\"");
        server.Should().Contain("correlationId: expose ? null : context.TraceIdentifier");
    }

    /// <summary>
    /// 重複事前チェックのエンドポイントが固定エンジンの汎用マッピング側で張られ、per-entity には
    /// ハンドラもリクエストレコードも残らないことを検証する。
    /// </summary>
    /// <remarks>
    /// 契約がランタイム共通面（<c>IRemoteRepository</c>）へ上がった以上、ハンドラは型引数だけで立つ。
    /// リクエストは Insert と完全に同型なので、per-entity の <c>{Entity}CheckUniquenessRequest</c> は
    /// 廃止して <c>RemoteEntityRequest&lt;TEntity&gt;</c> を共有する（ワイヤ上は同じ 1 フィールドの JSON）。
    /// </remarks>
    // 残す理由（P3-4b）：ルートの補間文字列と、複数行に整形した式本体（書式）。構造の表明（per-entity のレコード・経路が残らないこと・MapCrud が CheckUniquenessAsync を呼ぶこと）は行 code-remote-service-uniqueness-check-is-mapped-by-generic-crud へ移した
    [Fact(DisplayName = "重複事前チェックは MapCrud が張り per-entity のレコードは残らない")]
    public void Generate_RemoteServices_MapsUniquenessCheckFromTheGenericCrudMapping()
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
        var server = result.Files[1].Content.ReplaceLineEndings("\n");

        // ルートは不変（ワイヤ互換）で、Insert と同じエンベロープを読む
        server.Should().Contain("$\"{entityRoute}/CheckUniqueness\"");
        server
            .Should()
            .Contain(
                "var request = await ReadRequestAsync<RemoteEntityRequest<TEntity>>(context).ConfigureAwait(false);\n"
                    + "                        return (object?)\n"
                    + "                            await Repository<TRepository>(context)\n"
                    + "                                .CheckUniquenessAsync(\n"
                    + "                                    Required(request.Entity, \"Entity\"),"
            );
    }

    /// <summary>
    /// HTTP クライアントの DI 登録が非 keyed・keyed の 2 系統（それぞれ baseAddress 版と HttpClient ファクトリ版）で
    /// 出力されることを検証する。
    /// </summary>
    /// <remarks>
    /// keyed 版はハイブリッド構成（キー "server"＝HTTP・キー "local"＝方言エンジン）の必須部品で、方言側の
    /// <c>AddGenerated{方言}Repositories(serviceKey, ...)</c> と対になる。共有 HttpClient も同じキーで登録するため、
    /// 非 keyed 登録とも別キーとも衝突しない（コンテナ所有＝破棄もコンテナが行う）。
    /// </remarks>
    // 残す理由（P3-4b）：登録のラムダの本体（式本体）。シグネチャ 4 本・keyed / 非 keyed の呼び出しの構造は行 code-remote-service-http-registration-keyed-and-plain-overloads へ移した
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

        var main = result.Files[0].Content.ReplaceLineEndings("\n");

        main.Should()
            .Contain("provider.GetRequiredKeyedService<OwnedHttpClient>(serviceKey).Client");
        main.Should()
            .Contain(
                "services.AddKeyedScoped(serviceKey, (provider, _) => create(httpClientFactory(provider)));"
            );
        main.Should()
            .Contain("services.AddScoped(provider => create(httpClientFactory(provider)));");
    }

    /// <summary>
    /// リモートのルート（プレフィックス／health）が公開定数 <c>RemotePaths</c> へ一点集約され、
    /// サーバーの既定引数・health マッピング・クライアントの liveness チェックがいずれも定数を参照することを固定する
    /// </summary>
    /// <remarks>
    /// サーバー既定引数とクライアント DI 登録の双方に <c>"/quicker"</c> がリテラルで散っていた重複の解消。
    /// サーバーファイルにリテラルが残っていないこと（NotContain）まで見て、定数化の抜けを検知する。
    /// </remarks>
    // 残す理由（P3-4b）：リテラルの不在（定数は定数式なので意味モデルでは参照とリテラルを区別できない）と health のラムダ（式本体）。定数の宣言・値・参照の構造は行 code-remote-service-routes-shared-through-remote-paths / exec-remote-paths-constants へ移した
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
        var server = result.Files[1].Content;

        server.Should().Contain("group.MapGet(RemotePaths.HealthRoute, () => Results.Ok());");
        server.Should().NotContain("\"/quicker\"");
        server.Should().NotContain("\"health\"");
    }
}
