using FluentAssertions;
using QuickER.Generator;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.Generator;

/// <summary>
/// 表示名（DisplayName）機能の生成内容を検証するテストクラス。
/// </summary>
/// <remarks>
/// VO / Entity の静的 <c>DisplayName</c> 既定値、VO 無効時の <c>GetDisplayName</c> ヘルパ配線、
/// 列名衝突時の省略＋警告診断、C# リテラルエスケープ（<c>[DbColumnMeta]</c> / <c>[DbTableMeta]</c> 含む）を確認する。
/// フック上書きがエラーメッセージへ反映されることは固定フィクスチャの partial 拡張で別途検証する
/// （<see cref="QuickER.Tests.GeneratedFixture.DisplayNameCustomizeHookTests"/>）。
/// </remarks>
public class DisplayNameGenerationTests
{
    /// <summary>単一エンティティの ER 図を組み立てる。列は (名前, 型, PK, NULL 可, 説明) で指定する</summary>
    private static ErDiagram SingleEntity(
        string tableName,
        string? tableDescription,
        params (
            string Name,
            string DataType,
            bool IsPk,
            bool IsNullable,
            string? Description
        )[] columns
    ) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = tableName,
                    Description = tableDescription ?? string.Empty,
                    Columns = columns
                        .Select(column => new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = column.Name,
                            DataType = column.DataType,
                            IsPrimaryKey = column.IsPk,
                            IsNullable = column.IsNullable,
                            Description = column.Description ?? string.Empty,
                        })
                        .ToList(),
                },
            ],
        };

    // ===== VO の DisplayName 既定値 =====

    /// <summary>VO の DisplayName は列の Description があればそれ、無ければプロパティ名にフォールバックする</summary>
    [Fact]
    public void Generate_ValueObjectDisplayName_UsesDescriptionOrPropertyNameFallback()
    {
        var diagram = SingleEntity(
            "customers",
            tableDescription: null,
            ("customer_id", "int", true, false, null),
            ("name", "nvarchar(50)", false, false, "顧客名")
        );

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateValueObjects = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // Description あり: DisplayName 既定値は Description
        content.Should().Contain("var displayName = \"顧客名\";");
        // Description なし: DisplayName 既定値はプロパティ名（クラス名 CustomerIdValue ではなく CustomerId）
        content.Should().Contain("var displayName = \"CustomerId\";");
        // 全 VO に上書きフックが出る
        content
            .Should()
            .Contain("static partial void CustomizeDisplayName(ref string displayName);");
    }

    // ===== Entity の DisplayName 既定値 =====

    /// <summary>Entity の DisplayName は Description があればそれ、無ければクラス名にフォールバックする</summary>
    [Fact]
    public void Generate_EntityDisplayName_UsesDescriptionOrClassNameFallback()
    {
        var withDescription = new CSharpCodeGenerationService()
            .Generate(
                SingleEntity(
                    "customers",
                    tableDescription: "顧客マスタ",
                    ("customer_id", "int", true, false, null)
                ),
                new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
            )
            .Files[0]
            .Content;

        // Description あり: Entity.DisplayName 既定値は Description
        withDescription.Should().Contain("var displayName = \"顧客マスタ\";");

        var withoutDescription = new CSharpCodeGenerationService()
            .Generate(
                SingleEntity(
                    "customers",
                    tableDescription: null,
                    ("customer_id", "int", true, false, null)
                ),
                new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
            )
            .Files[0]
            .Content;

        // Description なし: Entity.DisplayName 既定値はクラス名（CustomerEntity）
        withoutDescription.Should().Contain("var displayName = \"CustomerEntity\";");
        withoutDescription
            .Should()
            .Contain("static partial void CustomizeDisplayName(ref string displayName);");
    }

    // ===== VO 無効時の GetDisplayName 配線 =====

    /// <summary>VO 無効時は GetDisplayName ヘルパ＋CustomizePropertyDisplayName フックを出し、検証メッセージへ表示名を渡す</summary>
    [Fact]
    public void Generate_WithoutValueObjects_WiresGetDisplayNameHelperAndHook()
    {
        var diagram = SingleEntity(
            "customers",
            tableDescription: null,
            ("customer_id", "int", true, false, null),
            ("name", "nvarchar(50)", false, false, "顧客名")
        );

        var content = new CSharpCodeGenerationService()
            .Generate(diagram, new CodeGenerationOptions { NamespaceName = "Sample.Domain" })
            .Files[0]
            .Content;

        // ヘルパとフックが 1 回ずつ出る
        content
            .Should()
            .Contain(
                "private static string GetDisplayName(string propertyName, string defaultDisplayName)"
            );
        content
            .Should()
            .Contain(
                "static partial void CustomizePropertyDisplayName(string propertyName, ref string displayName);"
            );

        // 必須メッセージ: Description ありの列は表示名（顧客名）、無い列はプロパティ名にフォールバック
        content
            .Should()
            .Contain("BuildRequiredErrorMessage(GetDisplayName(nameof(Name), \"顧客名\"))");
        // 入力変換メッセージ: PK（int）は GetDisplayName を第 1 引数に渡す（Description 無指定はプロパティ名）
        content
            .Should()
            .Contain(
                "ResolveParseErrorMessage(GetDisplayName(nameof(CustomerId), \"CustomerId\"), normalized, \"int\")"
            );
    }

    // ===== Entity 列名衝突 =====

    /// <summary>display_name 列を持つエンティティは DisplayName / CustomizeDisplayName を省略し警告する。他エンティティは正常に出す</summary>
    [Fact]
    public void Generate_EntityWithDisplayNameColumn_OmitsMembersWarnsAndKeepsOtherEntities()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                // display_name 列 → プロパティ名 DisplayName が静的メンバーと衝突する
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "labels",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "label_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "display_name",
                            DataType = "nvarchar(50)",
                            IsNullable = false,
                        },
                    ],
                },
                // 衝突しない別エンティティは通常どおり DisplayName を持つ
                new Entity
                {
                    Id = Guid.NewGuid(),
                    TableName = "customers",
                    Columns =
                    [
                        new Column
                        {
                            Id = Guid.NewGuid(),
                            Name = "customer_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions { NamespaceName = "Sample.Domain" }
        );

        result.HasErrors.Should().BeFalse("衝突は警告であり生成は完走する");
        var content = result.Files[0].Content;

        // 警告診断が出る
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                && diagnostic.Message.Contains("LabelEntity")
                && diagnostic.Message.Contains("DisplayName / CustomizeDisplayName")
            );

        // 衝突エンティティは列プロパティ DisplayName を持ち、静的 DisplayName プロパティは持たない
        content.Should().Contain("public string DisplayName { get; set; }");
        content.Should().NotContain("var displayName = \"LabelEntity\";");

        // 他エンティティ（customers）は静的 DisplayName を通常どおり生成する
        content.Should().Contain("var displayName = \"CustomerEntity\";");
    }

    // ===== C# リテラルエスケープ =====

    /// <summary>" / \ / 改行を含む Description が DisplayName・[DbColumnMeta]・[DbTableMeta] へ安全にエスケープされる</summary>
    [Fact]
    public void Generate_DescriptionWithSpecialChars_EscapesForCSharpLiteral()
    {
        // ダブルクオート・バックスラッシュ・改行を含む説明
        var tableDescription = "行1\r\n\"引用\"\\パス";
        var columnDescription = "列\"名\"\\end";

        var diagram = SingleEntity(
            "customers",
            tableDescription,
            ("customer_id", "int", true, false, null),
            ("name", "nvarchar(50)", false, false, columnDescription)
        );

        // 実生成経路と同じく型カタログ由来の canonical トークンを付加する（[DbColumnMeta] の付与条件を満たすため）
        var columnTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        columnTypes = CanonicalTypeTokenAttacher.Attach(
            columnTypes,
            diagram,
            new SqlServerTypeCatalog()
        );

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            new CodeGenerationOptions
            {
                NamespaceName = "Sample.Domain",
                GenerateValueObjects = true,
                IncludeDataAnnotations = true,
            }
        );

        result.HasErrors.Should().BeFalse();
        var content = result.Files[0].Content;

        // Entity.DisplayName 既定値（テーブル Description）が C# リテラルへエスケープされる（改行は空白 1 つへ畳む）
        content.Should().Contain("var displayName = \"行1 \\\"引用\\\"\\\\パス\";");
        // VO.DisplayName 既定値（列 Description）も同様にエスケープされる
        content.Should().Contain("var displayName = \"列\\\"名\\\"\\\\end\";");

        // [DbTableMeta] / [DbColumnMeta] の Description も同じヘルパでエスケープされる（前タスクの潜在バグ修正）
        content.Should().Contain("[DbTableMeta(Description = \"行1 \\\"引用\\\"\\\\パス\")]");
        content.Should().Contain(", Description = \"列\\\"名\\\"\\\\end\")]");

        // 生成物が Roslyn でコンパイルできる（エスケープ漏れがあればコンパイル不能になる）ことは
        // GeneratedCodeCompilationTests がフィクスチャ経由で担保するため、ここでは文字列一致で確認する
    }
}
