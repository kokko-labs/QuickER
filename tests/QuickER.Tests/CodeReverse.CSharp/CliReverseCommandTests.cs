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
    private static (string sourcePath, string outPath, string root) CreateGeneratedSource()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "QuickERReverseCliTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);

        var diagram = new ErDiagram
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
