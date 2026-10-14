using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 型ファミリが未知の NULL 許容の値型の列（PostgreSQL の <c>cidr</c> は <c>System.Net.IPNetwork?</c>）への IN で、
/// 要素型が列の型と一致しないリストを断ることを検証するテストクラス。
/// </summary>
/// <remarks>
/// NULL 許容の値型の列は IN のリストを <c>Cast&lt;T?&gt;()</c> で持ち上げる。<c>Cast</c> は参照変換とボックス解除しか通さないため、
/// 型の違うリスト（<c>string</c> など）はコンパイルできても実行時に <c>InvalidCastException</c> になる（インメモリ）。
/// 型ファミリが未知だと検査が「どちらか未知なら通す」に先に当たり、この完全一致の検査へ届いていなかった。
/// </remarks>
public class QueryInNullableUnknownValueTypeTests
{
    private static (ErDiagram Diagram, Entity Hosts) BuildDiagram()
    {
        var hosts = new Entity
        {
            TableName = "hosts",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "integer",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "backup_net",
                    DataType = "cidr",
                    IsNullable = true,
                },
            },
        };

        return (new ErDiagram { TargetDbms = "postgresql", Entities = { hosts } }, hosts);
    }

    private static CodeGenerationResult Generate(ErDiagram diagram)
    {
        var provider = new PostgreSqlProvider();

        return DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = false,
                GenerateEfCoreRepositories = true,
                GenerateInMemoryRepositories = true,
                GenerateEditModels = false,
                GenerateMappers = false,
            }
        );
    }

    [Fact(
        DisplayName = "型ファミリが未知の NULL 許容の値型の列への IN は、型の違うリストを警告して飛ばす"
    )]
    public void Generate_InOnNullableUnknownValueType_WithMismatchedList_SkipsQuery()
    {
        var (diagram, hosts) = BuildDiagram();
        diagram.Queries.Add(
            new QueryDefinition
            {
                EntityId = hosts.Id,
                Name = "FindByNets",
                Returns = QueryReturnShape.List,
                Condition = "backup_net IN @nets",
                Parameters =
                {
                    new QueryParameter
                    {
                        Name = "nets",
                        Type = "string",
                        IsList = true,
                    },
                },
            }
        );

        var result = Generate(diagram);

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        result
            .Diagnostics.Should()
            .Contain(d =>
                d.Severity == GenerationDiagnosticSeverity.Warning
                && d.Message.Contains("FindByNets")
                && d.Message.Contains("backup_net")
            );
        string.Join("\n", result.Files.Select(file => file.Content))
            .Should()
            .NotContain(
                "FindByNetsAsync",
                "実行時に必ず InvalidCastException になるクエリは出さない"
            );
    }
}
