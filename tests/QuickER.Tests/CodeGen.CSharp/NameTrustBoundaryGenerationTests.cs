using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 図の名前（テーブル・列・制約・クエリ）が生成 C# コードの文字列リテラルへ入るときの信頼境界を検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 名前は GUI の自由入力・DB 取込・DBML/Excel 取込・MCP/AI 経由の任意文字列で、<c>"</c> や <c>\</c> は
/// DB 取込で実在し得る。エスケープせずに <c>[Table("…")]</c> 等へ埋めると生成コードがコンパイル不能になる。
/// </para>
/// <para>
/// 一方、改行・制御文字は「エスケープすれば通る」類ではない（識別子として受け付ける DB が無く、
/// DDL のコメント行も壊す）ため、生成前診断の Error として入口で止める。この 2 つの線引きを固定する。
/// </para>
/// </remarks>
public class NameTrustBoundaryGenerationTests
{
    /// <summary>C# リテラルのエスケープが必要な名前（二重引用符とバックスラッシュ）を含む図を組み立てる</summary>
    private static ErDiagram BuildQuotedNameDiagram()
    {
        var customer = new Entity
        {
            TableName = "we\"ird\\customers",
            Columns =
            [
                new Column
                {
                    Name = "cus\"tomer\\id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "na\"me",
                    DataType = "nvarchar(100)",
                    IsNullable = true,
                },
            ],
        };
        var order = new Entity
        {
            TableName = "or\"ders",
            Columns =
            [
                new Column
                {
                    Name = "or\"der_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "cus\"tomer\\id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };

        customer.UniqueConstraints.Add(
            new UniqueConstraint { Name = "UQ_\"weird\\", ColumnIds = [customer.Columns[1].Id] }
        );

        return new ErDiagram
        {
            Entities = [customer, order],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = customer.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = "FK_\"weird\\",
                    ColumnPairs = [new(customer.Columns[0].Id, order.Columns[1].Id)],
                },
            ],
        };
    }

    /// <summary>Entity / EditModel / Mapper / QuickER 版 Repository / インメモリを出すオプション</summary>
    private static CodeGenerationOptions FullOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateRepositories = true,
            GenerateInMemoryRepositories = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>EF Core 単独出力のオプション（QuickER 版 Repository・インメモリは出さない）</summary>
    private static CodeGenerationOptions EfCoreOnlyOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateEfCoreRepositories = true,
            IncludeDataAnnotations = true,
        };

    /// <summary>Entity / EditModel / Mapper だけを出すオプション（インメモリのシーダーは含める）</summary>
    private static CodeGenerationOptions InMemoryOnlyOptions() =>
        new()
        {
            RootNamespace = "Sample.Domain",
            GenerateEditModels = true,
            GenerateMappers = true,
            GenerateInMemoryRepositories = true,
            IncludeDataAnnotations = true,
        };

    [Fact(DisplayName = "名前に \" や \\ を含む図の生成コードはコンパイルできる")]
    public void QuotedNames_ProduceCompilableCode()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildQuotedNameDiagram(),
            FullOptions()
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var compilation = GeneratedCodeCompiler.Compile(result, "NameTrustBoundary");

        compilation
            .Success.Should()
            .BeTrue(
                "名前に \" や \\ を含む図でも生成コードはコンパイルできるべき: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    [Fact(DisplayName = "[Table] / [Column] は名前の \" と \\ をエスケープして出力する")]
    public void QuotedNames_AreEscapedInAttributes()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildQuotedNameDiagram(),
            FullOptions()
        );
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should().Contain("[Table(\"we\\\"ird\\\\customers\")]");
        code.Should().Contain("[Column(\"cus\\\"tomer\\\\id\")]");
    }

    [Fact(DisplayName = "[NavigationReference] は親子のテーブル名・列名をエスケープして出力する")]
    public void QuotedNames_AreEscapedInNavigationReference()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildQuotedNameDiagram(),
            FullOptions()
        );
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should()
            .Contain("[NavigationReference(\"we\\\"ird\\\\customers\", \"cus\\\"tomer\\\\id\"");
    }

    [Fact(DisplayName = "EF Core の ToTable / HasColumnName は名前をエスケープして出力する")]
    public void QuotedNames_AreEscapedInEfCoreFluent()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildQuotedNameDiagram(),
            EfCoreOnlyOptions()
        );
        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should().Contain("ToTable(\"we\\\"ird\\\\customers\")");
        code.Should().Contain("HasColumnName(\"cus\\\"tomer\\\\id\")");

        GeneratedCodeCompiler
            .Compile(result, "NameTrustBoundaryEfCore")
            .Success.Should()
            .BeTrue("EF Core 単独構成でもコンパイルできるべき");
    }

    [Fact(DisplayName = "インメモリのサンプル値式は補間文字列の波括弧を二重化する")]
    public void BracesInColumnName_AreDoubledInInterpolatedSampleValue()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "co{de}",
                            DataType = "nvarchar(20)",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    ],
                },
            ],
        };
        var result = new CSharpCodeGenerationService().Generate(diagram, InMemoryOnlyOptions());

        // 波括弧を二重化しないと $"CO{DE}-00{index}" が「存在しない識別子 DE の補間」になりコンパイル不能
        GeneratedCodeCompiler
            .Compile(result, "NameTrustBoundaryBraces")
            .Success.Should()
            .BeTrue("列名の波括弧は補間文字列のリテラル部として二重化されるべき");
    }

    // ---------------- XmlDoc へ流れる名前（XML エスケープ） ----------------

    /// <summary>
    /// 説明が無い列・テーブルの XmlDoc は名前をそのまま載せるフォールバック文になる。名前の <c>&lt;</c> を
    /// エスケープしないと <c>///</c> コメントが不正な XML になり、利用者が
    /// <c>GenerateDocumentationFile=true</c> でビルドした瞬間に CS1570 が出る。
    /// </summary>
    /// <remarks>
    /// 対象は説明フォールバックを持つ 5 箇所（Entity クラス・Entity プロパティ・値オブジェクト・
    /// EditModel クラス・インメモリのシーダー）。テストのコンパイルは
    /// <c>DocumentationMode.Diagnose</c> で走るため CS1570 が警告として拾える。
    /// </remarks>
    [Fact(DisplayName = "説明フォールバックの XmlDoc は名前の < をエスケープする")]
    public void AngleBracketInName_IsEscapedInXmlDocFallback()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildAngleBracketNameDiagram(),
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = true,
                GenerateRepositories = true,
                GenerateInMemoryRepositories = true,
                GenerateValueObjects = true,
                IncludeDataAnnotations = true,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var code = string.Join("\n", result.Files.Select(file => file.Content));

        // 名前をそのまま載せる 5 つのフォールバック文が、いずれも XML エスケープ済みであること
        code.Should().Contain("Entity for the it&lt;ems table");
        code.Should().Contain("Property for the co&lt;de column");
        code.Should().Contain("Value object for the co&lt;de column");
        code.Should().Contain("Edit model for on-screen editing of the it&lt;ems table.");
        code.Should().Contain("Seeds sample data for it&lt;ems.");

        var compilation = GeneratedCodeCompiler.Compile(result, "NameTrustBoundaryXmlDoc");

        compilation
            .Warnings.Should()
            .NotContain(
                diagnostic => diagnostic.Id == "CS1570",
                "名前を載せた XmlDoc が不正な XML になってはいけない: "
                    + string.Join(
                        " / ",
                        compilation
                            .Warnings.Where(w => w.Id == "CS1570")
                            .Take(5)
                            .Select(w => w.ToString())
                    )
            );
        compilation.Success.Should().BeTrue(string.Join(" / ", compilation.Errors.Take(5)));
    }

    /// <summary>
    /// 無制限バイナリ列の Stream アクセサ（契約・ファイル糖衣）の XmlDoc 定型文も、列名を XML エスケープして
    /// 載せることを検証する（生のまま載せると <c>&lt;</c> で CS1570）。
    /// </summary>
    [Fact(DisplayName = "Stream アクセサの XmlDoc は列名の < をエスケープする")]
    public void AngleBracketInBinaryColumnName_IsEscapedInStreamAccessorXmlDoc()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "vaults",
                    Columns =
                    [
                        new Column
                        {
                            Name = "vault_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "da<ta",
                            DataType = "varbinary(max)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateRepositories = true,
                ExcludeUnboundedBinaryColumns = true,
                IncludeDataAnnotations = true,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var code = string.Join("\n", result.Files.Select(file => file.Content));

        code.Should().Contain("Reads the da&lt;ta column");
        code.Should().Contain("Writes the da&lt;ta column");

        var compilation = GeneratedCodeCompiler.Compile(result, "NameTrustBoundaryStreamXmlDoc");

        compilation
            .Warnings.Should()
            .NotContain(
                diagnostic => diagnostic.Id == "CS1570",
                "Stream アクセサの XmlDoc が不正な XML になってはいけない"
            );
        compilation.Success.Should().BeTrue(string.Join(" / ", compilation.Errors.Take(5)));
    }

    /// <summary>名前に <c>&lt;</c> を含む最小の図（説明は付けずフォールバック文を通す）</summary>
    private static ErDiagram BuildAngleBracketNameDiagram()
    {
        var entity = new Entity
        {
            TableName = "it<ems",
            Columns =
            [
                new Column
                {
                    Name = "it<em_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "co<de",
                    DataType = "nvarchar(20)",
                    IsNullable = true,
                },
            ],
        };

        return new ErDiagram { Entities = [entity] };
    }

    // ---------------- 生成前診断（改行・制御文字は Error） ----------------

    [Theory(DisplayName = "名前に改行・制御文字を含む図は生成前診断の Error になる")]
    [InlineData("TABLE")]
    [InlineData("COLUMN")]
    [InlineData("UNIQUE")]
    [InlineData("FOREIGN KEY")]
    [InlineData("QUERY")]
    public void ControlCharacterInName_IsError(string expectedLocationKeyword)
    {
        var diagram = BuildControlCharacterDiagram(expectedLocationKeyword);

        var result = new CSharpCodeGenerationService().Generate(diagram, FullOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains(expectedLocationKeyword)
            );
    }

    /// <summary>
    /// C0 の外側にある Unicode の行区切り（NEL / LINE SEPARATOR / PARAGRAPH SEPARATOR）も Error になることを検証する。
    /// </summary>
    /// <remarks>
    /// これらは C# 言語仕様上の new-line なので、名前に含まれると <c>///</c> コメントや 1 行リテラルが行をまたいで
    /// 壊れ、<b>名前 1 つで生成コードへ任意の C# を注入できる</b>。C0＋DEL だけを見る判定では素通りしていた。
    /// </remarks>
    [Theory(DisplayName = "Unicode の行区切りを含む名前も生成前診断の Error になる")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void UnicodeLineSeparatorInColumnName_IsError(string separator)
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = $"memo{separator}#error injected",
                            DataType = "nvarchar(50)",
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, FullOptions());

        result.HasErrors.Should().BeTrue();
        result
            .Diagnostics.Should()
            .Contain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("COLUMN")
            );
    }

    /// <summary>
    /// 説明文の FORM FEED（U+000C）が生成コードの実改行へ化けないことを検証する。
    /// </summary>
    /// <remarks>
    /// FF は C# 言語仕様の new-line ではないが、描画後の <c>ReplaceLineEndings</c> は FF も改行として
    /// 正規化する。畳み込み（<c>FoldNewLines</c>）の対象から漏れると、説明 1 つで XmlDoc の
    /// <c>///</c> 行や <c>[DbColumnMeta]</c> のリテラルが行をまたぎ、任意のメンバー宣言を挿入できる。
    /// 名前と違い説明は入口検証で止めない（改行が正当な値）ため、畳み込みが唯一の防壁になる。
    /// </remarks>
    [Fact(DisplayName = "説明文の FORM FEED は空白へ畳まれ実改行にならない")]
    public void FormFeedInDescription_IsFoldedNotBroken()
    {
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            Description = "x\fpublic static int Injected = 42; //",
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(diagram, FullOptions());

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));

        var code = string.Join("\n", result.Files.Select(file => file.Content));

        // FF は空白 1 つへ畳まれて 1 行のまま残る（XmlDoc summary と [DbColumnMeta] のリテラルの両方）
        code.Should().Contain("x public static int Injected = 42; //");
        code.Should().NotContain("\f", "FF が残ると描画後の ReplaceLineEndings が実改行へ変える");

        var compilation = GeneratedCodeCompiler.Compile(result, "NameTrustBoundaryFormFeed");

        compilation
            .Success.Should()
            .BeTrue(
                "説明の FF がリテラルを行またぎに壊してはいけない: "
                    + string.Join(" / ", compilation.Errors.Take(5))
            );
    }

    [Fact(DisplayName = "クォートを含むだけの名前は診断の Error にしない（DB 取込で実在し得る）")]
    public void QuoteInName_IsNotError()
    {
        var result = new CSharpCodeGenerationService().Generate(
            BuildQuotedNameDiagram(),
            FullOptions()
        );

        result
            .Diagnostics.Should()
            .NotContain(diagnostic =>
                diagnostic.Severity == GenerationDiagnosticSeverity.Error
                && diagnostic.Message.Contains("control character")
            );
    }

    /// <summary>指定した場所だけに改行入りの名前を置いた図を組み立てる</summary>
    private static ErDiagram BuildControlCharacterDiagram(string location)
    {
        const string Injected = "a\nb";

        var parent = new Entity
        {
            TableName = location == "TABLE" ? Injected : "customers",
            Columns =
            [
                new Column
                {
                    Name = location == "COLUMN" ? Injected : "customer_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            ],
        };
        var child = new Entity
        {
            TableName = "orders",
            Columns =
            [
                new Column
                {
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = false,
                },
            ],
        };

        parent.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = location == "UNIQUE" ? Injected : "UQ_customers",
                ColumnIds = [parent.Columns[0].Id],
            }
        );

        return new ErDiagram
        {
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ConstraintName = location == "FOREIGN KEY" ? Injected : "FK_orders_customers",
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
            ],
            Queries =
            [
                new QueryDefinition
                {
                    EntityId = child.Id,
                    Name = location == "QUERY" ? Injected : "GetAll",
                },
            ],
        };
    }
}
