using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 値オブジェクト有効時に「リレーションの子側（dependent）列は親側（principal）列の VO 型を共有する」
/// 統一規則を固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// VO は列名ごとに 1 型へ集約されるため、統一が無いと「FK 列名 ≠ 参照先 PK 列名」の図で同じ識別子が
/// 2 つの型に割れる。EF Core はこれをモデル検証で拒否し（FK プロパティの CLR 型は主キーと互換である必要がある・
/// 値コンバータは判定に関与しない）、他バックエンドでも「参照先のキーを FK 列へ代入できない」という形で現れる。
/// </para>
/// <para>
/// 統一は「列 → VO 型」解決の単一箇所（<c>CSharpGenerationModelBuilder.ValueObjects</c>）で行うため、
/// Entity・EditModel・Mapper・EF Core Fluent・DSL クエリのすべてが自動で追従する。ここではその追従先を
/// 名指しで表明し、退化ケース（同じ子列が型の違う親を複数参照）と循環（相互 FK）の扱いも固定する。
/// </para>
/// </remarks>
public class ValueObjectForeignKeyUnificationTests
{
    /// <summary>実経路（SqlServer プロバイダで型解決）で生成する</summary>
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

    /// <summary>VO 有効・全カテゴリ生成の標準オプション</summary>
    private static CodeGenerationOptions VoOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateValueObjects = true,
            GenerateRepositories = true,
            GenerateEditModels = true,
            GenerateMappers = true,
        };

    /// <summary>全ファイルの内容を連結して返す</summary>
    private static string AllContent(CodeGenerationResult result) =>
        string.Join("\n", result.Files.Select(file => file.Content));

    /// <summary>主キー列（int）を作る</summary>
    private static Column Key(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>外部キー列（int・NULL 許容）を作る</summary>
    private static Column Fk(string name) =>
        new()
        {
            Name = name,
            DataType = "int",
            IsForeignKey = true,
            IsNullable = true,
        };

    /// <summary>1 対多のリレーション（親 principal → 子 dependent）を作る</summary>
    private static Relationship OneToMany(
        Entity principal,
        Column principalColumn,
        Entity dependent,
        Column dependentColumn
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = RelationshipType.OneToMany,
            SourceEntityId = principal.Id,
            TargetEntityId = dependent.Id,
            ColumnPairs = { new(principalColumn.Id, dependentColumn.Id) },
        };

    /// <summary>customers.customer_id ← orders.ship_customer_id（FK 列名 ≠ 参照先 PK 列名）の図</summary>
    private static ErDiagram MismatchedNameDiagram()
    {
        var customerId = Key("customer_id");
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = Key("order_id");
        var shipCustomerId = Fk("ship_customer_id");
        var orders = new Entity { TableName = "orders", Columns = { orderId, shipCustomerId } };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { OneToMany(customers, customerId, orders, shipCustomerId) },
        };
    }

    /// <summary>自己参照（nodes.parent_node_id → nodes.node_id）の図</summary>
    private static ErDiagram SelfReferenceDiagram()
    {
        var nodeId = Key("node_id");
        var parentNodeId = Fk("parent_node_id");
        var nodes = new Entity { TableName = "nodes", Columns = { nodeId, parentNodeId } };

        return new ErDiagram
        {
            Entities = { nodes },
            Relationships = { OneToMany(nodes, nodeId, nodes, parentNodeId) },
        };
    }

    // ===== 統一の本体 =====

    /// <summary>子側 FK 列のプロパティ型は参照先 PK の VO 型になり、列名由来の VO 型は生成されない</summary>
    [Fact(
        DisplayName = "FK 列名 ≠ PK 列名: 子列は親の VO 型を共有し、子列名由来の VO は生成されない"
    )]
    public void 子列は親のVO型を共有する()
    {
        var result = Generate(MismatchedNameDiagram(), VoOptions());

        result.HasErrors.Should().BeFalse();
        var content = AllContent(result);

        // Entity プロパティ（NULL 許容 FK）
        content.Should().Contain("public CustomerIdValue? ShipCustomerId { get; set; }");
        // 子列名由来の VO 型は生成されない（使う場所が無くなるため）
        content.Should().NotContain("ShipCustomerIdValue");
        // 親側の VO は従来どおり 1 型だけ生成される
        content.Should().Contain("public sealed partial class CustomerIdValue");
    }

    /// <summary>EditModel の確定値プロパティも親の VO 型を共有する</summary>
    [Fact(DisplayName = "EditModel の確定値プロパティも親の VO 型（Mapper も同型で往復する）")]
    public void EditModelも親のVO型を共有する()
    {
        var result = Generate(MismatchedNameDiagram(), VoOptions());
        var content = AllContent(result);

        content.Should().Contain("public CustomerIdValue? ShipCustomerId");
        content.Should().Contain("private CustomerIdValue? _shipCustomerId;");
    }

    /// <summary>EF Core の Fluent 変換も親の VO 型で構成される（モデル検証が通る前提条件）</summary>
    [Fact(DisplayName = "EF Core Fluent の HasConversion も親の VO 型を使う")]
    public void EfCoreFluentも親のVO型を使う()
    {
        var options = VoOptions() with { GenerateEfCore = true, GenerateRepositories = false };
        var result = Generate(MismatchedNameDiagram(), options);

        result.HasErrors.Should().BeFalse();
        AllContent(result)
            .Should()
            .Contain(
                "entity.Property(e => e.ShipCustomerId).HasColumnName(\"ship_customer_id\").HasConversion(v => v!.Value, v => CustomerIdValue.Create(v!));"
            );
    }

    /// <summary>DSL クエリの条件式も親の VO 型でリテラルを包む</summary>
    [Fact(DisplayName = "DSL クエリの条件式は親の VO 型で Create する")]
    public void DSL条件式も親のVO型でCreateする()
    {
        var diagram = MismatchedNameDiagram();
        var orders = diagram.Entities.First(entity => entity.TableName == "orders");

        diagram.Queries.Add(
            new QueryDefinition
            {
                Id = Guid.NewGuid(),
                EntityId = orders.Id,
                Name = "GetByShipCustomer",
                Returns = QueryReturnShape.List,
                Parameters =
                {
                    new QueryParameter { Name = "shipCustomerId", Type = "int32" },
                },
                Condition = "ship_customer_id = @shipCustomerId",
            }
        );

        var result = Generate(diagram, VoOptions());

        result.HasErrors.Should().BeFalse();
        AllContent(result).Should().Contain("CustomerIdValue.Create(shipCustomerId)");
    }

    /// <summary>自己参照でも親（主キー）の VO 型を共有する</summary>
    [Fact(DisplayName = "自己参照: parent_node_id は node_id の VO 型を共有する")]
    public void 自己参照も親のVO型を共有する()
    {
        var result = Generate(SelfReferenceDiagram(), VoOptions());

        result.HasErrors.Should().BeFalse();
        var content = AllContent(result);

        content.Should().Contain("public NodeIdValue? ParentNodeId { get; set; }");
        content.Should().NotContain("ParentNodeIdValue");
    }

    /// <summary>FK チェーン（FK の FK）は不動点まで辿る</summary>
    [Fact(DisplayName = "FK チェーン: A.x → B.y → C.z は C.z の VO 型へ収束する")]
    public void FKチェーンは不動点まで辿る()
    {
        var codeId = Key("code_id");
        var codes = new Entity { TableName = "codes", Columns = { codeId } };

        var midId = Key("mid_id");
        var midRef = Fk("mid_ref");
        var mids = new Entity { TableName = "mids", Columns = { midId, midRef } };

        var leafId = Key("leaf_id");
        var leafRef = Fk("leaf_ref");
        var leafs = new Entity { TableName = "leafs", Columns = { leafId, leafRef } };

        var diagram = new ErDiagram
        {
            Entities = { codes, mids, leafs },
            Relationships =
            {
                OneToMany(codes, codeId, mids, midRef),
                // 中間列（それ自身が FK）を親に持つ FK＝チェーン
                OneToMany(mids, midRef, leafs, leafRef),
            },
        };

        var result = Generate(diagram, VoOptions());

        result.HasErrors.Should().BeFalse();
        var content = AllContent(result);

        content.Should().Contain("public CodeIdValue? MidRef { get; set; }");
        content.Should().Contain("public CodeIdValue? LeafRef { get; set; }");
        content.Should().NotContain("MidRefValue");
        content.Should().NotContain("LeafRefValue");
    }

    /// <summary>相互 FK（循環）は無限ループせず、循環に入った列は自分の VO 型のまま</summary>
    [Fact(DisplayName = "相互 FK（循環）: 無限ループせず、循環列は自分の VO 型のまま（決定的）")]
    public void 相互FKは循環しても決定的に解決する()
    {
        var alphaId = Key("alpha_id");
        var peerId = Fk("peer_id");
        var alpha = new Entity { TableName = "alpha", Columns = { alphaId, peerId } };

        var betaId = Key("beta_id");
        var peerRef = Fk("peer_ref");
        var beta = new Entity { TableName = "beta", Columns = { betaId, peerRef } };

        var diagram = new ErDiagram
        {
            Entities = { alpha, beta },
            Relationships =
            {
                // alpha.peer_id を親に beta.peer_ref が、beta.peer_ref を親に alpha.peer_id がぶら下がる循環
                OneToMany(alpha, peerId, beta, peerRef),
                OneToMany(beta, peerRef, alpha, peerId),
            },
        };

        var result = Generate(diagram, VoOptions());

        result.HasErrors.Should().BeFalse();
        var content = AllContent(result);

        content.Should().Contain("public PeerIdValue? PeerId { get; set; }");
        content.Should().Contain("public PeerRefValue? PeerRef { get; set; }");
    }

    /// <summary>下地の CLR 型が食い違う列ペアは統一しない（黙って型を変えない）</summary>
    [Fact(DisplayName = "下地の CLR 型が親子で違う列ペアは統一せず、子は自分の VO 型のまま")]
    public void 下地の型が違う列ペアは統一しない()
    {
        var customerId = new Column
        {
            Name = "customer_id",
            DataType = "nvarchar(20)",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = Key("order_id");
        // 参照先は string なのに子は int＝スキーマ自体が歪んでいるケース
        var shipCustomerId = Fk("ship_customer_id");
        var orders = new Entity { TableName = "orders", Columns = { orderId, shipCustomerId } };

        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { OneToMany(customers, customerId, orders, shipCustomerId) },
        };

        var result = Generate(diagram, VoOptions());

        result.HasErrors.Should().BeFalse();
        AllContent(result).Should().Contain("public ShipCustomerIdValue? ShipCustomerId");
    }

    // ===== 診断 =====

    /// <summary>統一が起きた列は Info 診断で一覧通知される</summary>
    [Fact(DisplayName = "統一が起きた列は Info 診断で通知される")]
    public void 統一はInfo診断で通知される()
    {
        var result = Generate(MismatchedNameDiagram(), VoOptions());

        var info = result
            .Diagnostics.Where(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
            )
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        info.Should()
            .ContainSingle(message => message.Contains("orders.ship_customer_id"))
            .Which.Should()
            .Contain("CustomerIdValue");
    }

    /// <summary>列名が一致する図（統一が起きない）では Info 診断を出さない</summary>
    [Fact(DisplayName = "列名が一致する図では統一の Info 診断を出さない")]
    public void 統一が起きない図では診断を出さない()
    {
        var customerId = Key("customer_id");
        var customers = new Entity { TableName = "customers", Columns = { customerId } };

        var orderId = Key("order_id");
        var orderCustomerId = Fk("customer_id");
        var orders = new Entity { TableName = "orders", Columns = { orderId, orderCustomerId } };

        var diagram = new ErDiagram
        {
            Entities = { customers, orders },
            Relationships = { OneToMany(customers, customerId, orders, orderCustomerId) },
        };

        var result = Generate(diagram, VoOptions());

        result
            .Diagnostics.Where(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Info
            )
            .Should()
            .NotContain(diagnostic => diagnostic.Message.Contains("customer_id"));
    }

    /// <summary>VO 無効の図では統一そのものが働かない（診断も型変更も起きない）</summary>
    [Fact(DisplayName = "VO 無効の図では統一が働かない（素の型のまま・診断なし）")]
    public void VO無効では統一しない()
    {
        var options = VoOptions() with { GenerateValueObjects = false };
        var result = Generate(MismatchedNameDiagram(), options);

        result.HasErrors.Should().BeFalse();
        AllContent(result).Should().Contain("public int? ShipCustomerId { get; set; }");
        result
            .Diagnostics.Should()
            .NotContain(diagnostic => diagnostic.Message.Contains("ship_customer_id"));
    }

    /// <summary>退化ケース（型の違う親を複数参照）は生成時診断エラー</summary>
    [Fact(DisplayName = "退化ケース: 型の違う親を複数参照する子列は生成時エラー")]
    public void 型の違う親を複数参照する子列はエラー()
    {
        var personId = Key("person_id");
        var people = new Entity { TableName = "people", Columns = { personId } };

        var companyId = Key("company_id");
        var companies = new Entity { TableName = "companies", Columns = { companyId } };

        var itemId = Key("item_id");
        var ownerId = Fk("owner_id");
        var items = new Entity { TableName = "items", Columns = { itemId, ownerId } };

        var diagram = new ErDiagram
        {
            Entities = { people, companies, items },
            Relationships =
            {
                OneToMany(people, personId, items, ownerId),
                OneToMany(companies, companyId, items, ownerId),
            },
        };

        var result = Generate(diagram, VoOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("items.owner_id")
            );
        result.Files.Should().BeEmpty();
    }

    /// <summary>同じ VO 型へ解決する親を複数参照するのは正常（エラーにしない）</summary>
    [Fact(DisplayName = "同じ VO 型へ解決する親を複数参照するのは正常")]
    public void 同じ型の親を複数参照するのは正常()
    {
        var personId = Key("person_id");
        var people = new Entity { TableName = "people", Columns = { personId } };

        // 別テーブルだが列名が同じ＝同一 VO 型へ解決する
        var otherPersonId = Key("person_id");
        var archived = new Entity { TableName = "archived_people", Columns = { otherPersonId } };

        var itemId = Key("item_id");
        var ownerId = Fk("owner_id");
        var items = new Entity { TableName = "items", Columns = { itemId, ownerId } };

        var diagram = new ErDiagram
        {
            Entities = { people, archived, items },
            Relationships =
            {
                OneToMany(people, personId, items, ownerId),
                OneToMany(archived, otherPersonId, items, ownerId),
            },
        };

        var result = Generate(diagram, VoOptions());

        result.HasErrors.Should().BeFalse();
        AllContent(result).Should().Contain("public PersonIdValue? OwnerId { get; set; }");
    }
}
