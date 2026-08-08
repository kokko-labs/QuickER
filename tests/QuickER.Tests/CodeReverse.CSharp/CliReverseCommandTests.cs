using System.IO;
using AwesomeAssertions;
using QuickER.Cli;
using QuickER.CodeGen.CSharp;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeReverse.CSharp;

/// <summary>
/// CLI の <c>reverse</c> コマンドが、C# ソースからスキーマのみの ER 図 JSON（layout キーなし）を書き出すことを検証する。
/// </summary>
public class CliReverseCommandTests
{
    /// <summary>SQL Server 生成の本体 .g.cs を一時フォルダへ書き出し、パス一式を返す</summary>
    /// <param name="source">生成元の図（省略時は customers 1 テーブルの最小構成）</param>
    private static (string sourcePath, string outPath, string root) CreateGeneratedSource(
        ErDiagram? source = null
    )
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);

        var diagram =
            source
            ?? new ErDiagram
            {
                Entities =
                {
                    new Entity
                    {
                        TableName = "customers",
                        Columns =
                        {
                            new Column
                            {
                                Name = "customer_id",
                                DataType = "int",
                                IsPrimaryKey = true,
                                IsNullable = false,
                            },
                            new Column
                            {
                                Name = "name",
                                DataType = "nvarchar(100)",
                                IsNullable = false,
                            },
                        },
                    },
                },
            };

        var provider = new SqlServerProvider();
        var result = DiagramCodeGenerator.Generate(
            provider.TypeMapper,
            provider.TypeCatalog,
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Reverse.Cli.Sample",
                GenerateValueObjects = true,
                SplitFilesByCategory = false,
            }
        );

        var sourcePath = Path.Combine(root, "Model.g.cs");
        File.WriteAllText(sourcePath, result.Files.Single().Content);

        return (sourcePath, Path.Combine(root, "diagram.json"), root);
    }

    /// <summary>reverse は {version, schema} を出力し layout キーを含まない・--provider の方言を TargetDbms に採る</summary>
    [Fact(DisplayName = "reverse は schema のみの JSON を出力し layout キーを持たない")]
    public async Task Reverse_WritesSchemaOnlyJson_WithProviderDialect()
    {
        var (sourcePath, outPath, root) = CreateGeneratedSource();

        try
        {
            var exit = await CliApp.InvokeAsync([
                "reverse",
                "--source",
                sourcePath,
                "--out",
                outPath,
                "--provider",
                "sqlite",
            ]);

            exit.Should().Be(0);
            File.Exists(outPath).Should().BeTrue();

            // 生の JSON に version / schema はあり、layout キーは無い（スキーマのみ文書）
            var json = File.ReadAllText(outPath);
            json.Should().Contain("\"Version\"").And.Contain("\"Schema\"");
            json.Should().NotContain("\"Layout\"");

            // 保存 JSON に layout キーが無い（上の NotContain で確認済み）。読み戻すと初期化子で空辞書になる（仕様）。
            var document = JsonStorageService.Load(outPath);
            document.Layout.Should().BeEmpty();
            // --provider の方言を TargetDbms に採用する
            document.Schema.TargetDbms.Should().Be("sqlite");
            document
                .Schema.Entities.Should()
                .ContainSingle()
                .Which.TableName.Should()
                .Be("customers");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// マージのない CLI リバースでも、UNIQUE 制約と外部キーメタデータ（制約名・参照アクション）が
    /// 新規図へ載る（コード上の <c>[UniqueConstraint]</c> / <c>[NavigationReference]</c> の名前付き引数が情報源）
    /// </summary>
    [Fact(DisplayName = "reverse は UNIQUE 制約と FK メタデータを新規図へ載せる")]
    public async Task Reverse_CarriesUniqueConstraintsAndForeignKeyMetadata()
    {
        var (sourcePath, outPath, root) = CreateGeneratedSource(WithConstraintsDiagram());

        try
        {
            var exit = await CliApp.InvokeAsync([
                "reverse",
                "--source",
                sourcePath,
                "--out",
                outPath,
                "--provider",
                "sqlserver",
            ]);

            exit.Should().Be(0);

            var schema = JsonStorageService.Load(outPath).Schema;
            var customers = schema.Entities.Single(entity => entity.TableName == "customers");
            var columnNameById = customers.Columns.ToDictionary(
                column => column.Id,
                column => column.Name
            );

            // UNIQUE 制約（実名・構成列）が復元される
            var unique = customers.UniqueConstraints.Should().ContainSingle().Subject;
            unique.Name.Should().Be("UQ_customers_code");
            unique.ColumnIds.Select(id => columnNameById[id]).Should().Equal("code");

            // 外部キーの制約名・参照アクションが復元される
            var relationship = schema.Relationships.Should().ContainSingle().Subject;
            relationship.ConstraintName.Should().Be("FK_orders_customers");
            relationship.OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
            relationship.OnUpdate.Should().Be(ForeignKeyReferentialAction.SetNull);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>UNIQUE 制約と参照アクション付き外部キーを持つ図（リバース対象コードの生成元）</summary>
    private static ErDiagram WithConstraintsDiagram()
    {
        var customerId = Guid.NewGuid();
        var customerPk = Guid.NewGuid();
        var customerCode = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderPk = Guid.NewGuid();
        var orderCustomerFk = Guid.NewGuid();

        var customers = new Entity
        {
            Id = customerId,
            TableName = "customers",
            Columns =
            {
                new Column
                {
                    Id = customerPk,
                    Name = "customer_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = customerCode,
                    Name = "code",
                    DataType = "varchar(20)",
                    IsNullable = false,
                },
            },
            UniqueConstraints =
            {
                new UniqueConstraint { Name = "UQ_customers_code", ColumnIds = { customerCode } },
            },
        };

        var orders = new Entity
        {
            Id = orderId,
            TableName = "orders",
            Columns =
            {
                new Column
                {
                    Id = orderPk,
                    Name = "order_id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Id = orderCustomerFk,
                    Name = "customer_id",
                    DataType = "int",
                    IsForeignKey = true,
                    IsNullable = true,
                },
            },
        };

        return new ErDiagram
        {
            Entities = { customers, orders },
            Relationships =
            {
                new Relationship
                {
                    Type = RelationshipType.OneToMany,
                    SourceEntityId = customerId,
                    TargetEntityId = orderId,
                    ColumnPairs = [new(customerPk, orderCustomerFk)],
                    ConstraintName = "FK_orders_customers",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.SetNull,
                },
            },
        };
    }

    /// <summary>解析対象クラスが無いソースは終了コード 1 で中断し、出力ファイルを作らない</summary>
    [Fact(DisplayName = "対象クラス 0 件のソースは終了コード 1")]
    public async Task Reverse_NoTargetClasses_ReturnsExitCodeOne()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Plain.cs");
        var outPath = Path.Combine(root, "diagram.json");
        File.WriteAllText(
            sourcePath,
            "namespace Sample; public class Plain { public int Id { get; set; } }"
        );

        try
        {
            var exit = await CliApp.InvokeAsync([
                "reverse",
                "--source",
                sourcePath,
                "--out",
                outPath,
            ]);

            exit.Should().Be(1);
            File.Exists(outPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// 構文エラーのあるソース（途中で切れた .g.cs）は終了コード 1 で中断し、図ファイルを書き出さない
    /// （エラー回復のまま続行すると、列が黙って欠落した図が保存されてしまうため）
    /// </summary>
    [Fact(DisplayName = "構文エラーのあるソースは終了コード 1・出力を書かない")]
    public async Task Reverse_SyntaxErrorSource_ReturnsExitCodeOne_AndWritesNothing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "Truncated.g.cs");
        var outPath = Path.Combine(root, "diagram.json");

        // 末尾が途中で切れた生成コード（コピペ欠け・コンフリクトマーカー残りの再現）
        File.WriteAllText(
            sourcePath,
            """
            namespace Sample;

            [Table("customers")]
            public partial class CustomerEntity
            {
                [Key]
                [Column("customer_id")]
                [DbColumnMeta("int32")]
                public int CustomerId { get; set; }

                [Column("name")]
                [DbColumnMeta("string(50)")]
                public string Name { get; se
            """
        );

        // 出力は注入版オーバーロードで捕捉する（Console を差し替えるとクラス並列実行で競合するため）
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        try
        {
            var exit = await CliApp.InvokeAsync(
                ["reverse", "--source", sourcePath, "--out", outPath],
                stdout,
                stderr
            );

            exit.Should().Be(1);
            File.Exists(outPath).Should().BeFalse("構文エラーでは部分的な図を書き出さない");
            // 文言はロケール依存のため、Roslyn の診断 ID が載ることで検証する
            stderr.ToString().Should().Contain("CS");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
