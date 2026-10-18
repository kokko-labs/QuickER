using System;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.SqlServer;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 除外した無制限バイナリ列を持つ図で、Mapper の <c>LoadSavedColumns</c> の上書き（AcceptSaved の読み戻しの前に
/// Entity の除外列を未取得へ戻す）が、EF Core のリポジトリを生成しない構成でだけ出ることを検証する。
/// </summary>
/// <remarks>
/// QuickER 版とインメモリのリポジトリは除外列を読み書きしないので、未取得へ戻せば次の保存が blob に触れない。
/// EF Core のリポジトリは全列を読み書きするので、戻すと次の保存が空の値で blob を消す。そのため EF Core を
/// 生成する構成（QuickER 版と併せて生成する構成も含む）では上書きを出さず、EditModel が blob を持ち続ける。
/// </remarks>
public sealed class AcceptSavedExcludedBinaryGenerationTests
{
    private const string Override = "protected override void LoadSavedColumns(";

    private static ErDiagram CreateDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "documents",
                    Columns =
                    {
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "document_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "payload",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    private static string Generate(bool repositories, bool efCore, bool inMemory)
    {
        var diagram = CreateDiagram();
        var columnTypes = CanonicalTypeTokenAttacher.Attach(
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            diagram,
            new SqlServerTypeCatalog()
        );

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateRepositories = repositories,
                GenerateEfCoreRepositories = efCore,
                GenerateInMemoryRepositories = inMemory,
                ExcludeUnboundedBinaryColumns = true,
            }
        );

        result.HasErrors.Should().BeFalse();

        return result.Files[0].Content;
    }

    [Theory(DisplayName = "AcceptSaved の除外列の上書きは、EF Core を生成しない構成でだけ出る")]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    public void LoadSavedColumnsOverride_IsGeneratedOnlyWithoutEfCore(
        bool repositories,
        bool efCore,
        bool inMemory,
        bool expected
    )
    {
        var content = Generate(repositories, efCore, inMemory);

        content.Contains(Override, StringComparison.Ordinal).Should().Be(expected);

        if (expected)
        {
            content.Should().Contain("entity.Payload = null;");
        }
    }
}
