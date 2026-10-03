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
}
