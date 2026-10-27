using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// スキャフォールドの README が案内する名前空間が、実際に <c>Generated/</c> へ書き出した <c>.g.cs</c> の
/// <c>namespace</c> 宣言と一致することを検証するテストクラス。
/// </summary>
/// <remarks>
/// README の値は生成の計画から求め、期待値は書き出したファイルの本文から読む（製品コードの同じ関数を通さない）。
/// </remarks>
public class MockProjectReadmeNamespaceTests
{
    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>顧客テーブル 1 つの図（方言は引数）</summary>
    private static ErDiagram BuildDiagram(string targetDbms) =>
        new()
        {
            TargetDbms = targetDbms,
            Entities =
            [
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

    /// <summary>
    /// README の DI の節・実 DB への切替・名前空間の節が、書き出したファイルの実際の名前空間を案内し、
    /// 型を持たない <c>{ルート}.Generated</c> を登録の名前空間として案内しないことを検証する。
    /// </summary>
    [Theory(DisplayName = "README の名前空間は生成されたファイルの namespace 宣言と一致する")]
    [InlineData("blazor", "sqlserver", "Repositories.SqlServer.g.cs")]
    [InlineData("blazor", "sqlite", "Repositories.Sqlite.g.cs")]
    [InlineData("blazor", "postgresql", null)]
    [InlineData("wpf", "sqlserver", "Repositories.SqlServer.g.cs")]
    [InlineData("wpf", "sqlite", "Repositories.Sqlite.g.cs")]
    [InlineData("wpf", "postgresql", null)]
    public void Readme_NamespacesMatchGeneratedFiles(
        string target,
        string targetDbms,
        string? dialectFile
    )
    {
        var output = NewTempFolder();
        var mock = NewTempFolder();

        try
        {
            MockFolderStore.CreateNew(mock, "AcmeMock", "# schema");
            var profile =
                target == "blazor" ? MockProjectTargetProfile.Blazor : MockProjectTargetProfile.Wpf;
            var result = new MockProjectScaffoldService(
                new DatabaseProviderRegistry([new SqlServerProvider(), new SqliteProvider()])
            ).Scaffold(BuildDiagram(targetDbms), output, "AcmeMock", mock, profile);

            var readme = File.ReadAllText(result.ReadmePath).ReplaceLineEndings("\n");
            var generated = result.GeneratedDirectory;
            var declared = GeneratedNamespaceDeclarations.Read(generated);
            var inMemory = GeneratedNamespaceDeclarations.OfFileContaining(
                generated,
                "IServiceCollection AddGeneratedInMemoryRepositories("
            );
            var entities = GeneratedNamespaceDeclarations.OfFileContaining(
                generated,
                "class CustomerEntity"
            );
            var contracts = GeneratedNamespaceDeclarations.OfFileContaining(
                generated,
                "interface ICustomerRepository"
            );

            readme.Should().Contain($"using {inMemory};");
            readme.Should().Contain($"is declared in the\n`{inMemory}` namespace.");

            if (target == "blazor")
            {
                readme.Should().Contain($"@using {entities}\n@using {contracts}");
            }
            else
            {
                readme
                    .Should()
                    .Contain($"`using {entities};`")
                    .And.Contain($"`using {contracts};`");
            }

            if (dialectFile is not null)
            {
                var database = declared[dialectFile];
                readme.Should().Contain($"`{database}` namespace (add `using {database};`)");
            }

            // 名前空間の節は書き出した全ファイルを、そのファイルの実際の名前空間で並べる
            foreach (var (file, declaredNamespace) in declared)
            {
                readme.Should().Contain($"- `Generated/{file}`: `{declaredNamespace}`");
            }

            // 型を持たない親の名前空間を、登録の名前空間として案内しない
            readme.Should().NotContain("from `AcmeMock.Generated`");
            readme.Should().NotContain("using AcmeMock.Generated;");
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }
}
