using System;
using System.Collections.Generic;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// DB 定義メタ属性（<c>[DbColumnMeta]</c> / <c>[DbTableMeta]</c>）の付与条件・内容を検証するテストクラス。
/// </summary>
/// <remarks>
/// 付与は <c>IncludeDataAnnotations</c> と Entity 生成に連動し、対象 DB・Repository/EF 設定に依らない。
/// トークンは方言中立（canonical 由来）で、解析不能な自由記述型は属性を省略する。説明（Description）は
/// モデル（Column/Entity）から流れ、空なら named 引数ごと省略する。
/// </remarks>
public class DbDefinitionMetadataTests
{
    private static readonly Guid CustomerId = new("11110000-0000-0000-0000-000000000001");
    private static readonly Guid PkColId = new("11110000-0000-0000-0000-000000000002");
    private static readonly Guid NameColId = new("11110000-0000-0000-0000-000000000003");
    private static readonly Guid FreeColId = new("11110000-0000-0000-0000-000000000004");

    /// <summary>説明・自由記述型を織り込んだ検証用の図を構築する</summary>
    private static ErDiagram BuildDiagram() =>
        new()
        {
            Entities =
            {
                new Entity
                {
                    Id = CustomerId,
                    TableName = "customers",
                    Description = "顧客マスタ",
                    Columns =
                    {
                        new Column
                        {
                            Id = PkColId,
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = NameColId,
                            Name = "name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                            Description = "顧客名",
                        },
                        // 型カタログが解析できない自由記述型（属性は省略されるべき）
                        new Column
                        {
                            Id = FreeColId,
                            Name = "extra",
                            DataType = "sql_variant",
                            IsNullable = true,
                        },
                    },
                },
            },
        };

    /// <summary>SQL Server 経路（マッパ＋カタログ）で生成した Entity ファイルの中身を返す</summary>
    private static string GenerateSqlServer(ErDiagram diagram, CodeGenerationOptions options)
    {
        var columnTypes = CanonicalTypeTokenAttacher.Attach(
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            diagram,
            new SqlServerTypeCatalog()
        );
        var result = new CSharpCodeGenerationService().Generate(diagram, columnTypes, options);
        result.HasErrors.Should().BeFalse();

        return result.Files.Should().ContainSingle().Subject.Content;
    }

    /// <summary>既定オプション（Entity/EditModel/Mapper・DataAnnotations ON）</summary>
    private static CodeGenerationOptions DefaultOptions() =>
        new()
        {
            NamespaceName = "Sample.Domain",
            GenerateRepositories = false,
            GenerateEfCore = false,
        };

    [Fact(DisplayName = "DataAnnotations ON で [DbColumnMeta]/[DbTableMeta] が付与される")]
    public void IncludeDataAnnotations_On_EmitsMetaAttributes()
    {
        var content = GenerateSqlServer(BuildDiagram(), DefaultOptions());

        // 属性定義（Runtime バケット）
        content.Should().Contain("public sealed class DbColumnMetaAttribute : Attribute");
        content.Should().Contain("public sealed class DbTableMetaAttribute : Attribute");

        // テーブル説明はクラスレベル [DbTableMeta]
        content.Should().Contain("[DbTableMeta(Description = \"顧客マスタ\")]");

        // 中立トークン: int → int32、nvarchar(50) → string(50)
        content.Should().Contain("[DbColumnMeta(\"int32\")]");
        content.Should().Contain("[DbColumnMeta(\"string(50)\", Description = \"顧客名\")]");
    }

    [Fact(DisplayName = "DataAnnotations OFF では DB 定義メタ属性は一切出ない")]
    public void IncludeDataAnnotations_Off_OmitsMetaAttributes()
    {
        var options = new CodeGenerationOptions
        {
            NamespaceName = "Sample.Domain",
            GenerateRepositories = false,
            GenerateEfCore = false,
            IncludeDataAnnotations = false,
        };

        var content = GenerateSqlServer(BuildDiagram(), options);

        content.Should().NotContain("DbColumnMetaAttribute");
        content.Should().NotContain("DbTableMetaAttribute");
        content.Should().NotContain("[DbColumnMeta(");
        content.Should().NotContain("[DbTableMeta(");
    }

    [Fact(DisplayName = "説明が空の列は Description 引数ごと省略される")]
    public void EmptyDescription_OmitsDescriptionArgument()
    {
        var content = GenerateSqlServer(BuildDiagram(), DefaultOptions());

        // 説明なしの PK 列は Description 引数を持たない
        content.Should().Contain("[DbColumnMeta(\"int32\")]");
        content.Should().NotContain("[DbColumnMeta(\"int32\", Description");
    }

    [Fact(DisplayName = "型カタログが解析できない自由記述型は [DbColumnMeta] を省略する")]
    public void UnparseableType_OmitsColumnMeta()
    {
        var content = GenerateSqlServer(BuildDiagram(), DefaultOptions());

        // extra 列（sql_variant）は canonical 化できないためトークンなし＝属性を付けない。
        // extra プロパティ自体は生成されるが、その直前に [DbColumnMeta( が無いことを確認する
        content.Should().Contain("public string? Extra");
        content.Should().NotContain("sql_variant");
    }

    [Fact(
        DisplayName = "DB 定義メタは対象 DB（sqlserver 単独 / sqlite 単独 / マルチ）に依らず同一"
    )]
    public void Metadata_IsIdentical_AcrossTargetDatabases()
    {
        var diagram = BuildDiagram();

        // sqlserver 単独（Repository (QuickER)）
        var sqlServerOnly = GenerateEntityMetaLines(
            diagram,
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialect = "sqlserver",
            }
        );

        // sqlite 単独（Repository (QuickER)）
        var sqliteOnly = GenerateEntityMetaLines(
            diagram,
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialect = "sqlite",
            }
        );

        // マルチターゲット（sqlserver + sqlite）
        var multi = GenerateEntityMetaLines(
            diagram,
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateRepositories = true,
                RepositoryDialects = ["sqlserver", "sqlite"],
            }
        );

        // どの構成でも DB 定義メタは同一（属性定義＋付与行の集合）
        sqliteOnly.Should().Equal(sqlServerOnly);
        multi.Should().Equal(sqlServerOnly);

        // 実際にメタが含まれていること（空集合の一致で緑になる事故を防ぐ）
        sqlServerOnly.Should().Contain("[DbTableMeta(Description = \"顧客マスタ\")]");
        sqlServerOnly.Should().Contain("[DbColumnMeta(\"string(50)\", Description = \"顧客名\")]");
    }

    /// <summary>
    /// 主辞書（図の方言＝SQL Server）へトークンを付加し、マルチ辞書オーバーロードで生成した中の
    /// DB 定義メタ関連行だけを抽出して返す（対象 DB 差の影響を受けない共有 Entity メタを比較するため）。
    /// </summary>
    private static IReadOnlyList<string> GenerateEntityMetaLines(
        ErDiagram diagram,
        CodeGenerationOptions options
    )
    {
        var primary = CanonicalTypeTokenAttacher.Attach(
            SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            diagram,
            new SqlServerTypeCatalog()
        );
        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["sqlserver"] = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram),
            ["sqlite"] = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram),
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            primary,
            byDialect,
            options
        );
        result.HasErrors.Should().BeFalse();

        var lines = new List<string>();

        foreach (var line in result.Files[0].Content.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();

            if (
                trimmed.Contains("DbColumnMeta", StringComparison.Ordinal)
                || trimmed.Contains("DbTableMeta", StringComparison.Ordinal)
            )
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }
}
