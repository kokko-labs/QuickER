using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary><see cref="QueryParameterTypeResolver"/> の型トークン解決を検証するテストクラス</summary>
public class QueryParameterTypeResolverTests
{
    /// <summary>指定した型トークンのパラメータを、指定順にクエリへ並べた図を作る</summary>
    private static ErDiagram DiagramWithTokens(params string[] tokens)
    {
        var entity = new Entity { TableName = "docs" };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var diagram = new ErDiagram { Entities = { entity } };

        for (var i = 0; i < tokens.Length; i++)
        {
            diagram.Queries.Add(
                new QueryDefinition
                {
                    Name = $"Q{i}",
                    EntityId = entity.Id,
                    Parameters =
                    {
                        new QueryParameter { Name = "p", Type = tokens[i] },
                    },
                }
            );
        }

        return diagram;
    }

    /// <summary>大文字を含む解析できないトークンが先に来ても、正しいトークンの解決を巻き込まないことを検証する</summary>
    /// <remarks>
    /// トークンの解析は小文字しか受けない。集まりを大文字小文字を区別せずに作ると、先に来た <c>String(50)</c> が
    /// <c>string(50)</c> を重複として捨て、正しいほうまで「解決できないトークン」になっていた（並び順で結果が変わる）
    /// </remarks>
    [Theory(
        DisplayName = "大文字を含むトークンは解決せず、並び順に依らず小文字のトークンだけを解決する"
    )]
    [InlineData("String(50)", "string(50)")]
    [InlineData("string(50)", "String(50)")]
    public void Resolve_CaseVariantTokens_OnlyLowerCaseIsResolved(string first, string second)
    {
        var provider = new SqlServerProvider();

        var resolved = QueryParameterTypeResolver.Resolve(
            DiagramWithTokens(first, second),
            provider.TypeMapper,
            provider.TypeCatalog
        );

        resolved.Should().ContainKey("string(50)");
        resolved["string(50)"].TypeName.Should().Be("string");
        resolved.Should().NotContainKey("String(50)");
    }
}
