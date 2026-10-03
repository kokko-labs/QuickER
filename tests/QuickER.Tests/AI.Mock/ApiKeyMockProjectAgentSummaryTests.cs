using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// API キー方式がモデルへ渡す材料（データ層の契約要約・修正ターンのビルド出力）の組み立てを検証するテストクラス。
/// </summary>
public class ApiKeyMockProjectAgentSummaryTests
{
    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// 共有の基底契約（CRUD を宣言する <c>IRemoteRepositoryCore</c> / <c>IRepositoryCore</c>）の署名が要約へ入り、
    /// 複数行にわたる署名は 1 行へまとまり、既定実装の本体は拾わないことを検証する。
    /// </summary>
    [Fact(DisplayName = "契約要約は基底契約の CRUD 署名を含み、複数行の署名をまとめる")]
    public void Summary_IncludesCoreContractsAndJoinsMultilineSignatures()
    {
        var folder = NewTempFolder();

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "Runtime.g.cs"),
                """
                public interface IRemoteRepositoryCore<TEntity, TKey>
                    where TEntity : class
                {
                    /// <summary>Gets one row.</summary>
                    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

                    Task<bool> UpdateAsync(
                        TEntity entity,
                        CancellationToken cancellationToken = default
                    );

                    Task<int> CountAsync()
                    {
                        return Task.FromResult(0);
                    }
                }
                """
            );
            File.WriteAllText(
                Path.Combine(folder, "Repositories.g.cs"),
                """
                public partial interface ICustomerRepository : IRepository<Customer, int>
                {
                    Task<Customer?> FindByNameAsync(string name);
                }
                """
            );

            var summary = ApiKeyMockProjectAgent.SummarizeGeneratedContracts(folder);

            summary.Should().Contain("IRemoteRepositoryCore<TEntity, TKey>");
            summary
                .Should()
                .Contain(
                    "Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);"
                );
            summary
                .Should()
                .Contain(
                    "Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);"
                );
            summary.Should().Contain("Task<Customer?> FindByNameAsync(string name);");
            summary.Should().NotContain("return Task.FromResult");
            summary.Should().NotContain("CountAsync");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// テーブルごとの契約が大きく要約が上限で切られても、共有の基底契約（CRUD）は先頭に置かれて残ることを検証する
    /// （ファイル順では基底契約の Runtime.g.cs が Repositories.g.cs より後ろになる）。
    /// </summary>
    [Fact(DisplayName = "要約が上限で切られても基底契約は残る")]
    public void Summary_Truncated_KeepsCoreContracts()
    {
        var folder = NewTempFolder();

        try
        {
            var perTable = string.Concat(
                Enumerable
                    .Range(0, 100)
                    .Select(i =>
                        $$"""
                            public partial interface ITable{{i}}Repository : IRepository<Table{{i}}, int>
                            {
                                Task<IReadOnlyList<Table{{i}}>> FindByVeryLongNamedQueryNumber{{i}}Async(string keyword);
                            }

                            """
                    )
            );
            File.WriteAllText(Path.Combine(folder, "Repositories.g.cs"), perTable);
            File.WriteAllText(
                Path.Combine(folder, "Runtime.g.cs"),
                """
                public interface IRemoteRepositoryCore<TEntity, TKey>
                {
                    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
                }
                """
            );

            var summary = ApiKeyMockProjectAgent.SummarizeGeneratedContracts(folder);

            summary.Should().Contain("was truncated");
            summary.Should().Contain("IRemoteRepositoryCore<TEntity, TKey>");
            summary.Should().Contain("GetByIdAsync(");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// <c>record struct</c> / <c>record class</c> の型名を、続く <c>struct</c> / <c>class</c> と取り違えないことを検証する。
    /// </summary>
    [Fact(DisplayName = "record struct の型名を struct と取り違えない")]
    public void Summary_RecordStructTypeName_IsCaptured()
    {
        var folder = NewTempFolder();

        try
        {
            File.WriteAllText(
                Path.Combine(folder, "Keys.g.cs"),
                """
                public readonly record struct OrderKey(int Id);
                public sealed record class CustomerSnapshot(string Name);
                """
            );

            var summary = ApiKeyMockProjectAgent.SummarizeGeneratedContracts(folder);
            var publicTypes = summary
                .Split('\n')
                .Single(line => line.StartsWith("Public types: ", StringComparison.Ordinal));

            publicTypes.Should().Be("Public types: OrderKey, CustomerSnapshot");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// 実際のスキャフォールドが出す <c>Generated/</c> で、CRUD の署名（基底契約）が要約へ届くことを検証する。
    /// テーブルが多く要約が上限で切られる図でも、基底契約は先頭に置かれるので届く。
    /// </summary>
    [Theory(DisplayName = "実際の Generated/ の要約に CRUD の署名が届く（上限で切られる図でも）")]
    [InlineData(1, false)]
    [InlineData(200, true)]
    public void Summary_OfRealScaffold_ContainsCrudSignatures(int tableCount, bool truncated)
    {
        var output = NewTempFolder();
        var mock = NewTempFolder();

        try
        {
            var store = MockFolderStore.CreateNew(mock, "AcmeMock", "# schema");
            store.SaveStylesheet("body {}", "first");
            var diagram = new ErDiagram
            {
                TargetDbms = SqliteProvider.ProviderName,
                Entities =
                [
                    .. Enumerable
                        .Range(0, tableCount)
                        .Select(i => new Entity
                        {
                            Id = Guid.NewGuid(),
                            TableName = $"table{i}",
                            Columns =
                            [
                                new Column
                                {
                                    Id = Guid.NewGuid(),
                                    Name = "id",
                                    DataType = "INTEGER",
                                    IsPrimaryKey = true,
                                    IsNullable = false,
                                },
                            ],
                        }),
                ],
            };
            var result = new MockProjectScaffoldService(
                new DatabaseProviderRegistry([new SqlServerProvider(), new SqliteProvider()])
            ).Scaffold(diagram, output, "AcmeMock", mock, MockProjectTargetProfile.Blazor);

            var summary = ApiKeyMockProjectAgent.SummarizeGeneratedContracts(
                result.GeneratedDirectory
            );

            summary.Should().Contain("IRemoteRepositoryCore<TEntity, TKey>");
            summary.Should().Contain("GetByIdAsync(");
            summary.Should().Contain("SaveAsync(");
            summary.Contains("was truncated", StringComparison.Ordinal).Should().Be(truncated);
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }

    /// <summary>短いビルド出力はそのまま渡すことを検証する</summary>
    [Fact(DisplayName = "上限以内のビルド出力はそのまま渡す")]
    public void ExcerptBuildOutput_ShortOutput_IsUnchanged()
    {
        const string output = "Build FAILED.";

        ApiKeyMockProjectAgent.ExcerptBuildOutput(output, 8000).Should().Be(output);
    }

    /// <summary>
    /// 長いビルド出力は上限の長さに収まり、エラーの行が先頭へ集まり（重複は 1 回）、省略した旨が示されることを検証する。
    /// </summary>
    [Fact(DisplayName = "長いビルド出力は上限に収め、エラー行を優先する")]
    public void ExcerptBuildOutput_LongOutput_IsBoundedAndKeepsErrors()
    {
        var noise = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 2000).Select(i => $"  Restoring step {i} for AcmeMock")
        );
        const string error =
            @"C:\out\AcmeMock\Pages\Home.razor(3,5): error CS0103: The name 'Foo' does not exist";
        var output = string.Join(
            Environment.NewLine,
            error,
            noise,
            error,
            "Build FAILED.",
            "    1 Error(s)"
        );

        var excerpt = ApiKeyMockProjectAgent.ExcerptBuildOutput(output, 2000);

        excerpt.Length.Should().BeLessThanOrEqualTo(2000);
        excerpt.Should().StartWith("(the build output was shortened");
        excerpt.Should().Contain(error);
        excerpt.IndexOf(error, StringComparison.Ordinal).Should().BeLessThan(300);
        excerpt.Should().EndWith("1 Error(s)");
    }
}
