using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// リモート契約生成（GenerateRemoteContracts）によるリモート面の追加を、
/// 実経路（<see cref="DiagramCodeGenerator"/>）で検証するテストクラス
/// </summary>
/// <remarks>
/// ON では I{Entity}RemoteRepository（IRemoteRepository 継承＋名前付きクエリ）が追加され、
/// I{Entity}Repository はそれと IRepository を継承する全機能面のまま。実装クラス・DI 実装登録は従来どおりで、
/// リモート面の転送 DI 登録だけが増える（純粋に追加的）。OFF（既定）は従来どおりの一体型契約。
/// </remarks>
public class RemoteContractGenerationTests
{
    private readonly Entity _order;

    /// <summary>Order エンティティ（OrderId PK / CustomerId / Memo）を用意する</summary>
    public RemoteContractGenerationTests()
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

    /// <summary>名前付きクエリ 1 件（DSL 一覧）付きの図を作る</summary>
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

    /// <summary>全ファイルの内容を連結して返す</summary>
    private static string AllContent(CodeGenerationResult result) =>
        string.Join("\n", result.Files.Select(file => file.Content));

    /// <summary>診断メッセージを整形して返す（アサーション失敗時の理由表示用）</summary>
    private static string FormatDiagnostics(CodeGenerationResult result) =>
        string.Join(" / ", result.Diagnostics.Select(d => $"{d.Severity}: {d.Message}"));

    /// <summary>ON: リモート面が追加され、名前付きクエリはリモート面に載り、全機能面がそれを継承することを検証する</summary>
    [Fact(
        DisplayName = "ON: I{E}RemoteRepository=リモート面（クエリ込み）・I{E}Repository=全機能面（継承）"
    )]
    public void Generate_RemoteContracts_AddsRemoteFace()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateRemoteContracts = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // リモート面: IRemoteRepository を継承し、名前付きクエリの契約メソッドを本体に持つ
        content
            .Should()
            .Contain(
                "public partial interface IOrderRemoteRepository : IRemoteRepository<OrderEntity, int>"
            );
        content
            .Should()
            .Contain(
                "Task<IReadOnlyList<OrderEntity>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);"
            );

        // 全機能面: リモート面＋IRepository（Query()・生 SQL・一括追加）の合成で、既存名のまま
        content.Should().Contain("public partial interface IOrderRepository");
        content.Should().Contain("IRepository<OrderEntity, int> { }");

        // 実装クラス・DI 実装登録は従来どおり（全機能面を実装・登録）
        content.Should().Contain("(connectionFactory, saveHooks), IOrderRepository");
        content.Should().Contain("services.AddScoped<IOrderRepository, OrderRepository>();");

        // リモート面は同一インスタンスへの転送として追加登録される
        content.Should().Contain("services.AddScoped<IOrderRemoteRepository>(provider =>");
        content.Should().Contain("provider.GetRequiredService<IOrderRepository>()");
    }

    /// <summary>OFF（既定）: 従来どおりの一体型契約で、リモート面が出ないことを検証する</summary>
    [Fact(DisplayName = "OFF（既定）: 一体型契約のまま・リモート面は出ない・基底分割は常時出る")]
    public void Generate_Default_KeepsUnifiedContract()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions { RootNamespace = "Test.Ns", GenerateRepositories = true }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // per-entity 契約は従来どおり IRepository 継承の一体型
        content
            .Should()
            .Contain("public partial interface IOrderRepository : IRepository<OrderEntity, int>");
        content.Should().NotContain("IOrderRemoteRepository");

        // 基底分割（IRemoteRepository ← IRepository）はオプションに依らず常時出力される（非破壊）
        content.Should().Contain("public partial interface IRemoteRepository<TEntity, TKey>");
        content
            .Should()
            .Contain(
                "public partial interface IRepository<TEntity, TKey> : IRemoteRepository<TEntity, TKey>"
            );

        // DI は従来どおり単一登録
        content.Should().Contain("services.AddScoped<IOrderRepository, OrderRepository>();");
        content.Should().NotContain("GetRequiredService<IOrderRepository>");
    }

    /// <summary>ON×EF Core: EF Core の DI にもリモート面の転送登録が増えることを検証する</summary>
    [Fact(
        DisplayName = "ON×EF Core: EF Core 版 Repository は従来どおり・リモート面の転送 DI が増える"
    )]
    public void Generate_RemoteContractsWithEfCore_AddsForwardingRegistration()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = false,
                GenerateEfCore = true,
                GenerateRemoteContracts = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // EF Core 版 Repository・実装登録は従来どおり全機能面基準
        content.Should().Contain("public sealed partial class EfCoreOrderRepository(");
        content.Should().Contain("services.AddScoped<IOrderRepository, EfCoreOrderRepository>();");

        // リモート面の転送登録が追加される
        content.Should().Contain("services.AddScoped<IOrderRemoteRepository>(provider =>");
    }

    /// <summary>ON×インメモリ: InMemory の DI にもリモート面の転送登録が増えることを検証する</summary>
    [Fact(DisplayName = "ON×インメモリ: InMemory 実装は従来どおり・リモート面の転送 DI が増える")]
    public void Generate_RemoteContractsWithInMemory_AddsForwardingRegistration()
    {
        var result = Generate(
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                GenerateInMemoryRepositories = true,
                GenerateRemoteContracts = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        content
            .Should()
            .Contain("services.AddScoped<IOrderRepository, InMemoryOrderRepository>();");
        content.Should().Contain("services.AddScoped<IOrderRemoteRepository>(provider =>");
    }

    /// <summary>ON×マルチターゲット: keyed DI にもリモート面の転送登録が増えることを検証する</summary>
    [Fact(DisplayName = "ON×マルチターゲット: keyed DI にリモート面の転送登録が増える")]
    public void Generate_RemoteContractsWithMultiTarget_AddsKeyedForwarding()
    {
        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            CreateDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Test.Ns",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
                GenerateRemoteContracts = true,
            }
        );

        result.HasErrors.Should().BeFalse(FormatDiagnostics(result));
        var content = AllContent(result);

        // 非 keyed・keyed の双方で全機能面が実装登録され、リモート面が転送登録される
        content.Should().Contain("services.AddScoped<IOrderRepository, OrderRepository>();");
        content.Should().Contain("services.AddKeyedScoped<IOrderRepository>(");
        content.Should().Contain("services.AddKeyedScoped<IOrderRemoteRepository>(");
        content
            .Should()
            .Contain("(provider, key) => provider.GetRequiredKeyedService<IOrderRepository>(key)");

        // 契約（リモート面・全機能面）は中立 namespace に 1 回だけ出る
        content
            .Split("public partial interface IOrderRemoteRepository")
            .Length.Should()
            .Be(2, "リモート面の契約は契約 namespace に 1 回だけ出る");
    }
}
