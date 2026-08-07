using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// UNIQUE 制約ベースの重複事前チェック（<c>CheckUniquenessAsync</c>・<c>[UniqueConstraint]</c>・
/// <c>ValidateUniqueAsync</c>）の生成を、実経路（<see cref="DiagramCodeGenerator"/>＝型解決込み）で検証する。
/// </summary>
public class UniquenessGenerationTests
{
    private readonly Entity _order;
    private readonly Column _orderId;
    private readonly Column _code;
    private readonly Column _memo;

    /// <summary>Order エンティティ（OrderId PK / Code / Memo）を用意する</summary>
    public UniquenessGenerationTests()
    {
        _orderId = new Column
        {
            Name = "OrderId",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
        };
        _code = new Column
        {
            Name = "Code",
            DataType = "nvarchar(20)",
            IsNullable = false,
        };
        _memo = new Column { Name = "Memo", DataType = "nvarchar(200)" };

        _order = new Entity { TableName = "Order", Columns = { _orderId, _code, _memo } };
    }

    /// <summary>UNIQUE 制約を持たせた図を作る</summary>
    private ErDiagram CreateDiagram(params UniqueConstraint[] constraints)
    {
        foreach (var constraint in constraints)
        {
            _order.UniqueConstraints.Add(constraint);
        }

        return new ErDiagram { Entities = { _order } };
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

    /// <summary>QuickER 版 Repository＋EditModel の標準オプション</summary>
    private static CodeGenerationOptions CreateOptions() =>
        new()
        {
            RootNamespace = "Test.Ns",
            GenerateRepositories = true,
            GenerateEditModels = true,
        };

    /// <summary>全ファイルの内容を連結して返す</summary>
    private static string AllContent(CodeGenerationResult result) =>
        string.Join("\n", result.Files.Select(file => file.Content));

    /// <summary>制約が 1 件も無くてもメソッドとユーザー定義フックは生成される（フックだけで独自判定を足せる）</summary>
    [Fact(DisplayName = "制約 0 件でもチェックメソッドと拡張点が出る")]
    public void Generate_NoConstraints_StillEmitsMethodAndHook()
    {
        var result = Generate(CreateDiagram(), CreateOptions());

        result.HasErrors.Should().BeFalse();
        var content = AllContent(result);

        content
            .Should()
            .Contain("Task<IReadOnlyList<UniquenessViolation>> CheckUniquenessAsync(")
            .And.Contain("partial void CollectCustomUniquenessChecks(");

        // 制約が無いので照合ブロックは 1 つも出ない（違反は必ず空リストかフック由来になる）
        content.Should().NotContain("new UniquenessViolation(");
    }

    /// <summary>単一列制約は NULL 検査つきの照合ブロックと、実名の違反レコードを生成する</summary>
    [Fact(DisplayName = "NULL 許容の単一列制約は NULL 検査つきで照合する")]
    public void Generate_NullableSingleColumn_EmitsNullGuard()
    {
        var diagram = CreateDiagram(
            new UniqueConstraint { Name = "UQ_Order_Memo", ColumnIds = { _memo.Id } }
        );

        var content = AllContent(Generate(diagram, CreateOptions()));

        content.Should().Contain("if (entity.Memo is not null)");
        content.Should().Contain(".Where(candidate => candidate.Memo == entity.Memo)");
        // 同一主キーの行は必ず除外する（挿入・更新のどちらでも同じ呼び方で正しくなる根拠）
        content.Should().Contain(".Where(candidate => candidate.OrderId != entity.OrderId)");
        content.Should().Contain("\"UQ_Order_Memo\"");
    }

    /// <summary>名前なしの複合制約は合成名（UQ_{テーブル}_{列連結}）と全構成列の照合を生成する</summary>
    [Fact(DisplayName = "名前なしの複合制約は合成名で照合する")]
    public void Generate_UnnamedComposite_UsesSynthesizedName()
    {
        var diagram = CreateDiagram(new UniqueConstraint { ColumnIds = { _code.Id, _memo.Id } });

        var content = AllContent(Generate(diagram, CreateOptions()));

        content.Should().Contain("\"UQ_Order_Code_Memo\"");
        content.Should().Contain(".Where(candidate => candidate.Code == entity.Code)");
        content.Should().Contain(".Where(candidate => candidate.Memo == entity.Memo)");
        content.Should().Contain("nameof(OrderEntity.Code)");
        content.Should().Contain("nameof(OrderEntity.Memo)");
    }

    /// <summary>リモート契約が無い構成では契約が全機能面（I{Entity}Repository）に載る</summary>
    [Fact(DisplayName = "リモート契約なしでは全機能面に契約が載る")]
    public void Generate_WithoutRemoteContracts_PlacesContractOnFullFace()
    {
        var content = AllContent(Generate(CreateDiagram(), CreateOptions()));

        var fullFace = ExtractDeclarationBody(
            content,
            "public partial interface IOrderRepository : IRepository<OrderEntity, int>"
        );

        fullFace.Should().Contain("CheckUniquenessAsync(");
        content.Should().NotContain("IOrderRemoteRepository");

        // EditModel の DB 照合糖衣も全機能面を受け取る
        content
            .Should()
            .Contain(
                "public async Task<bool> ValidateUniqueAsync(\n        IOrderRepository repository,".ReplaceLineEndings()
            );
    }

    /// <summary>リモート契約がある構成では契約がリモート面（I{Entity}RemoteRepository）へ移設される</summary>
    [Fact(DisplayName = "リモート契約ありでは契約がリモート面へ移設される")]
    public void Generate_WithRemoteContracts_MovesContractToRemoteFace()
    {
        var options = CreateOptions() with { GenerateRemoteContracts = true };

        var content = AllContent(Generate(CreateDiagram(), options));

        var remoteFace = ExtractDeclarationBody(
            content,
            "public partial interface IOrderRemoteRepository : IRemoteRepository<OrderEntity, int>"
        );

        remoteFace.Should().Contain("CheckUniquenessAsync(");

        // 全機能面はリモート面を継承するだけで、自分では宣言しない（純粋に追加的）
        content
            .Should()
            .Contain(
                "public partial interface IOrderRepository\n    : IOrderRemoteRepository,".ReplaceLineEndings()
            );

        // EditModel の DB 照合糖衣もリモート面を受け取る（Stream アクセサのファイル糖衣と同じ切替）
        content
            .Should()
            .Contain(
                "public async Task<bool> ValidateUniqueAsync(\n        IOrderRemoteRepository repository,".ReplaceLineEndings()
            );
    }

    /// <summary>EditModel クラスへ図の UNIQUE 制約が属性として刻まれる（確定値プロパティ名・宣言順・制約名つき）</summary>
    [Fact(DisplayName = "EditModel へ UNIQUE 制約属性が刻まれる")]
    public void Generate_EditModel_EmitsUniqueConstraintAttributes()
    {
        var diagram = CreateDiagram(
            new UniqueConstraint { Name = "UQ_Order_Memo", ColumnIds = { _memo.Id } },
            new UniqueConstraint { ColumnIds = { _code.Id, _memo.Id } }
        );

        var content = AllContent(Generate(diagram, CreateOptions()));

        content.Should().Contain("[UniqueConstraint(\"Memo\", Name = \"UQ_Order_Memo\")]");
        content
            .Should()
            .Contain("[UniqueConstraint(\"Code\", \"Memo\", Name = \"UQ_Order_Code_Memo\")]");
    }

    /// <summary>Repository 契約を生成しない構成では EditModel の DB 照合糖衣を出さない（呼び出し先が無い）</summary>
    [Fact(DisplayName = "Repository 契約なしでは DB 照合糖衣を出さない")]
    public void Generate_WithoutRepositoryContract_OmitsValidateUniqueAsync()
    {
        var options = new CodeGenerationOptions
        {
            RootNamespace = "Test.Ns",
            GenerateEditModels = true,
        };

        var content = AllContent(Generate(CreateDiagram(), options));

        content.Should().Contain("class OrderEditModel");
        content.Should().NotContain("ValidateUniqueAsync");
        // コレクション内重複検証（属性駆動）は Repository の有無に依らず使える
        content.Should().Contain("class EditModelUniquenessValidator");
    }

    /// <summary>複合主キー（Repository 契約面が無い）のテーブルでは DB 照合糖衣を出さない</summary>
    [Fact(DisplayName = "複合主キーのテーブルでは DB 照合糖衣を出さない")]
    public void Generate_CompositeKey_OmitsValidateUniqueAsync()
    {
        _code.IsPrimaryKey = true;

        var content = AllContent(Generate(CreateDiagram(), CreateOptions()));

        content.Should().Contain("class OrderEditModel");
        content.Should().NotContain("ValidateUniqueAsync");
    }

    /// <summary>名前付きクエリが CheckUniqueness という名前を取ると予約名として拒否される</summary>
    [Fact(DisplayName = "名前付きクエリの CheckUniqueness は予約名として拒否される")]
    public void Generate_QueryNamedCheckUniqueness_IsRejected()
    {
        var diagram = CreateDiagram();
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = _order.Id,
                Name = "CheckUniqueness",
                Returns = QueryReturnShape.List,
            }
        );

        var result = Generate(diagram, CreateOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Select(diagnostic => diagnostic.Message)
            .Should()
            .Contain(message => message.Contains("CheckUniquenessAsync"));
    }

    /// <summary>宣言行から次のトップレベル宣言までを素朴に取り出す（面の中身を見るための補助）</summary>
    private static string ExtractDeclarationBody(string content, string declaration)
    {
        var normalized = declaration.ReplaceLineEndings();
        var start = content.IndexOf(normalized, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"宣言 '{declaration}' が生成されている前提");

        var end = content.IndexOf(
            $"{Environment.NewLine}public ",
            start + normalized.Length,
            StringComparison.Ordinal
        );
        return end > start ? content[start..end] : content[start..];
    }
}
