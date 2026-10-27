using System.IO;
using AwesomeAssertions;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectGenerator"/>（スキャフォールドと実行器の組み立て）の、実行器を組み立てられない構成での
/// 振る舞いを検証するテストクラス。
/// </summary>
public class MockProjectGeneratorTests
{
    private static DatabaseProviderRegistry BuildRegistry() =>
        new([new SqlServerProvider(), new SqliteProvider()]);

    /// <summary>
    /// エンジンファクトリの無い構成では、API キー方式の可用性の問い合わせが例外でなく false を返すことを検証する
    /// （純粋な問い合わせが投げると、呼び出し側の初期化ごと落ちる）。
    /// </summary>
    [Fact(DisplayName = "エンジンファクトリが無ければ API キー方式は利用できないと答える")]
    public void IsAgentAvailable_ApiKeyWithoutEngineFactory_ReturnsFalse()
    {
        var generator = new MockProjectGenerator(BuildRegistry());

        var query = () => generator.IsAgentAvailable(ErChatBackendKind.ApiKey);

        query.Should().NotThrow().Which.Should().BeFalse();
    }

    /// <summary>
    /// 実行器を組み立てられない構成で生成を始めると、出力フォルダへ何も書き出す前に英語の例外で止まることを検証する。
    /// </summary>
    [Fact(DisplayName = "エンジンファクトリの無い API キー方式の生成は、何も書き出さずに止まる")]
    public async Task GenerateAsync_ApiKeyWithoutEngineFactory_ThrowsBeforeWriting()
    {
        var output = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var generator = new MockProjectGenerator(BuildRegistry());

        try
        {
            var generate = () =>
                generator.GenerateAsync(
                    new ErDiagram { TargetDbms = "sqlite" },
                    mockFolder: output,
                    additionalInstructions: null,
                    output,
                    "AcmeMock",
                    MockProjectTarget.Blazor,
                    ErChatBackendKind.ApiKey,
                    model: string.Empty,
                    modelProvider: string.Empty,
                    onProgress: _ => { },
                    cancellationToken: TestContext.Current.CancellationToken
                );

            var thrown = await generate.Should().ThrowAsync<InvalidOperationException>();
            thrown.Which.Message.Should().MatchRegex(@"^[\x20-\x7E]+$", "英語固定の文言");
            Directory.Exists(output).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    /// <summary>
    /// 画面のページを書き、ビルドの確認が要るファイル（csproj.user）も書く偽の実行器（成否は指定どおり）
    /// </summary>
    private sealed class DeclineProbeAgent(bool succeeds) : IMockProjectAgent
    {
        public bool IsAvailable() => true;

        public string BackendName => "Probe";

        public bool FinalBuildEscapesSandbox => true;

        public Task<MockProjectAgentOutcome> RunAsync(
            MockProjectAgentRequest request,
            Action<string> onProgress,
            CancellationToken cancellationToken
        )
        {
            var project = Path.Combine(request.WorkingDirectory, request.ProjectName);
            var pages = Path.Combine(project, "Components", "Pages");
            Directory.CreateDirectory(pages);
            File.WriteAllText(Path.Combine(pages, "CustomerList.razor"), "@page \"/\"\n<h1/>");
            File.WriteAllText(
                Path.Combine(project, $"{request.ProjectName}.csproj.user"),
                "<Project/>"
            );

            return Task.FromResult(
                new MockProjectAgentOutcome(succeeds, succeeds ? null : "boom", false)
            );
        }

        public Task InterruptAsync() => Task.CompletedTask;
    }

    /// <summary>何もしないビルド検証器（呼ばれない想定）</summary>
    private sealed class NoBuildRunner : IBuildRunner
    {
        public Task<BuildRunResult> BuildAsync(
            string solutionFilePath,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new BuildRunResult(true, string.Empty));

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>
    /// 最終ビルドの確認を取り消したとき、利用者自身の選択として伝えるのは実行器が成功していたときだけで、
    /// 実行器が失敗していれば取り消しがあっても失敗として伝えることを検証する
    /// </summary>
    [Theory(
        DisplayName = "ビルドの確認の取り消しは、実行器が成功したときだけ利用者の選択として伝える"
    )]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task GenerateAsync_BuildDeclined_ReportedOnlyWhenAgentSucceeded(
        bool agentSucceeds,
        bool expectedDeclined
    )
    {
        var output = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var mock = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = MockFolderStore.CreateNew(mock, "AcmeMock", "# schema");
            store.SaveScreen(
                "CustomerList.html",
                "Customer List",
                "role",
                "<!DOCTYPE html><html><body></body></html>",
                Array.Empty<MockTransition>(),
                "init"
            );
            var generator = new MockProjectGenerator(
                new MockProjectScaffoldService(BuildRegistry()),
                new NoBuildRunner(),
                _ => new DeclineProbeAgent(agentSucceeds)
            );

            var result = await generator.GenerateAsync(
                new ErDiagram
                {
                    TargetDbms = "sqlite",
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
                                    DataType = "INTEGER",
                                    IsPrimaryKey = true,
                                    IsNullable = false,
                                },
                            ],
                        },
                    ],
                },
                mock,
                additionalInstructions: null,
                output,
                "AcmeMock",
                MockProjectTarget.Blazor,
                ErChatBackendKind.Codex,
                model: string.Empty,
                modelProvider: string.Empty,
                onProgress: _ => { },
                confirmBuild: _ => false,
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.Success.Should().BeFalse();
            result.BuildDeclined.Should().Be(expectedDeclined);
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(output);
            ReparsePointTestHelper.DeleteTree(mock);
        }
    }
}
