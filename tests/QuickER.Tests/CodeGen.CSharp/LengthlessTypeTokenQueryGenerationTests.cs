using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 長さの無い型トークン（<c>string</c> / <c>ansistring</c> / <c>binary</c>）を使う名前付きクエリが、
/// 長さを要する方言でも従来どおり生成できることを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 列としての長さ無しは SQL Server / MySQL / Oracle では書き出せない（<see cref="ITypeCatalog.TryFormat"/> が
/// <c>false</c>）。しかしクエリのパラメータ・スカラー戻り値・射影フィールドは列を作らないため長さ制約が無く、
/// ここまで巻き込むと<b>現に動いている名前付きクエリが生成時エラーへ落ちる</b>。
/// <c>QueryParameterTypeResolver</c> が可変長の 3 種別に限って「無制限」として解決し直すことでこれを塞ぐ。
/// </para>
/// <para>
/// 固定長（<c>fixedstring</c> / <c>ansifixedstring</c> / <c>fixedbinary</c>）の長さ無しはフォールバックしない
/// ＝従来どおり解決不能で生成時エラー。<c>char</c> に「無制限」は無く、長さ <c>-1</c> を渡すと方言側が
/// <c>nchar(max)</c> のような実在しない表記を返してしまうため。
/// </para>
/// </remarks>
public class LengthlessTypeTokenQueryGenerationTests
{
    /// <summary>長さ無しトークンをパラメータ・スカラー・射影の 3 経路で使う図を作る</summary>
    /// <param name="keyType">主キー列の DB 型（方言ごとの表記）</param>
    /// <param name="memoType">文字列列の DB 型（方言ごとの表記）</param>
    private static ErDiagram BuildDiagram(string keyType, string memoType)
    {
        var entity = new Entity { TableName = "docs" };
        entity.Columns.Add(
            new Column
            {
                Name = "doc_id",
                DataType = keyType,
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        entity.Columns.Add(
            new Column
            {
                Name = "memo",
                DataType = memoType,
                IsNullable = true,
            }
        );

        var diagram = new ErDiagram { Entities = { entity } };

        // パラメータ経路（DSL 一覧）: 長さ無し string
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "FindByMemo",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Dsl,
                Condition = "memo = @memoText",
                Parameters =
                {
                    new QueryParameter { Name = "memoText", Type = "string" },
                },
            }
        );

        // パラメータ経路（DSL 件数）: 長さ無し ansistring
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "CountByMemo",
                Returns = QueryReturnShape.Count,
                Implementation = QueryImplementationKind.Dsl,
                Condition = "memo = @memoAnsi",
                Parameters =
                {
                    new QueryParameter { Name = "memoAnsi", Type = "ansistring" },
                },
            }
        );

        // スカラー戻り値の経路: 長さ無し binary
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "GetBlob",
                Returns = QueryReturnShape.Scalar,
                ScalarType = "binary",
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT TOP 1 memo FROM docs WHERE doc_id = @id",
                    ["sqlite"] = "SELECT memo FROM docs WHERE doc_id = @id LIMIT 1",
                },
                Parameters =
                {
                    new QueryParameter { Name = "id", Type = "int32" },
                },
            }
        );

        // 射影フィールドの経路: 長さ無し string / ansistring / binary
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = entity.Id,
                Name = "Summarize",
                Returns = QueryReturnShape.Projection,
                ResultTypeName = "DocSummaryRow",
                Implementation = QueryImplementationKind.Sql,
                Sql =
                {
                    ["sqlserver"] = "SELECT memo AS Label, memo AS Code, memo AS Payload FROM docs",
                    ["sqlite"] = "SELECT memo AS Label, memo AS Code, memo AS Payload FROM docs",
                },
                Fields =
                {
                    new ProjectionField { Name = "Label", Type = "string" },
                    new ProjectionField { Name = "Code", Type = "ansistring" },
                    new ProjectionField { Name = "Payload", Type = "binary" },
                },
            }
        );

        return diagram;
    }

    /// <summary>方言プロバイダ・生成オプションの 3 構成を返す</summary>
    public static TheoryData<string> Configurations() => new() { "sqlserver", "sqlite", "efcore" };

    private static CodeGenerationResult Generate(string configuration)
    {
        IDatabaseProvider provider =
            configuration == "sqlite" ? new SqliteProvider() : new SqlServerProvider();
        var diagram =
            configuration == "sqlite"
                ? BuildDiagram("INTEGER", "TEXT")
                : BuildDiagram("int", "nvarchar(200)");
        var options = configuration switch
        {
            "sqlserver" => new CodeGenerationOptions
            {
                RootNamespace = "Sample.SqlServer",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver"],
                IncludeDataAnnotations = true,
            },
            "sqlite" => new CodeGenerationOptions
            {
                RootNamespace = "Sample.Sqlite",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlite"],
                IncludeDataAnnotations = true,
            },
            _ => new CodeGenerationOptions
            {
                RootNamespace = "Sample.EfCore",
                GenerateEfCoreRepositories = true,
                IncludeDataAnnotations = true,
            },
        };

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            options
        );
    }

    /// <summary>
    /// 長さ無しトークンのクエリが、3 構成とも診断エラーなしで従来どおりの C# 型を生成する。
    /// </summary>
    /// <remarks>
    /// C# 型は長さの有無で変わらない（<c>string</c> / <c>byte[]</c>）。この表明が落ちるときは、
    /// パラメータ用のフォールバックが効いていないか、フォールバックが別の種別へ落ちている。
    /// </remarks>
    [Theory(
        DisplayName = "長さ無しトークンの名前付きクエリは 3 構成とも診断エラーなしで同じ C# 型を生成する"
    )]
    [MemberData(nameof(Configurations))]
    public void Generate_LengthlessTokens_ResolveToSameCSharpTypes(string configuration)
    {
        var result = Generate(configuration);

        result
            .HasErrors.Should()
            .BeFalse(
                string.Join("\n", result.Diagnostics.Select(d => $"[{d.Severity}] {d.Message}"))
            );

        var content = string.Join("\n", result.Files.Select(file => file.Content));

        // パラメータ: string / ansistring とも C# の string
        content.Should().Contain("FindByMemoAsync(string memoText");
        content.Should().Contain("CountByMemoAsync(string memoAnsi");

        // スカラー戻り値: binary は byte[]（NULL 許容）
        content.Should().Contain("Task<byte[]?> GetBlobAsync(int id");

        // 射影フィールド: 自由フィールドは既定 NULL 許容
        content.Should().Contain("public string? Label { get; set; }");
        content.Should().Contain("public string? Code { get; set; }");
        content.Should().Contain("public byte[]? Payload { get; set; }");
    }

    /// <summary>
    /// 固定長の長さ無しトークンは従来どおり解決不能で、生成時エラーになる（長さの指定を促す文言つき）。
    /// </summary>
    [Theory(DisplayName = "固定長の長さ無しトークンは生成時エラー（長さの指定を促す）")]
    [InlineData("fixedstring")]
    [InlineData("ansifixedstring")]
    [InlineData("fixedbinary")]
    public void Generate_LengthlessFixedToken_IsGenerationError(string token)
    {
        var diagram = BuildDiagram("int", "nvarchar(200)");
        diagram.Queries.Clear();
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = diagram.Entities[0].Id,
                Name = "FindByCode",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Dsl,
                Condition = "memo = @code",
                Parameters =
                {
                    new QueryParameter { Name = "code", Type = token },
                },
            }
        );

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.SqlServer",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver"],
            }
        );

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Error
                && d.Message.Contains(token)
                && d.Message.Contains("fixedstring(10)")
            );
    }

    /// <summary>長さ付きトークンの生成物は従来どおり（フォールバックが長さ付きへ波及していないことの対照）</summary>
    [Fact(DisplayName = "長さ付きトークンのクエリは従来どおり生成される")]
    public void Generate_TokenWithLength_IsUnaffected()
    {
        var diagram = BuildDiagram("int", "nvarchar(200)");
        diagram.Queries.Clear();
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = diagram.Entities[0].Id,
                Name = "FindByCode",
                Returns = QueryReturnShape.List,
                Implementation = QueryImplementationKind.Dsl,
                Condition = "memo = @code",
                Parameters =
                {
                    new QueryParameter { Name = "code", Type = "string(50)" },
                },
            }
        );

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.SqlServer",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver"],
            }
        );

        result.HasErrors.Should().BeFalse();
        string.Join("\n", result.Files.Select(file => file.Content))
            .Should()
            .Contain("FindByCodeAsync(string code");
    }
}
