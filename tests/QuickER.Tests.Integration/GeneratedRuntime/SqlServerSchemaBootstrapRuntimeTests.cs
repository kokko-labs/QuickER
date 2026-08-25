using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedSqlServerBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQL Server 方言の生成 DDL 適用ヘルパー <c>SqlServerSchemaBootstrap.ApplyDdlAsync</c> を、実 SQL Server
/// （Testcontainers・Docker 依存）で検証する。SQLite 版（<see cref="SqliteSchemaBootstrapRuntimeTests"/>）と対称。
/// </summary>
/// <remarks>
/// 検証の要点は「QuickER の SQL Server DDL は <c>GO</c> を出さない単一バッチであり、バッチ分割なしの
/// 1 回の <c>ExecuteNonQuery</c> でスキーマが立つ」ことの実 DB 実証（テーブル 2 本＋FK 制約＋拡張プロパティを含む）。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SqlServerSchemaBootstrapRuntimeTests(SqlServerContainerFixture fixture)
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>生成 DDL 全文が 1 回の呼び出しで適用され、以降の CRUD が動く</summary>
    [Fact(DisplayName = "[SQLServerブートストラップ] 生成 DDL 全文が 1 回で適用され CRUD が動く")]
    public async Task ApplyDdlAsync_AppliesGeneratedScriptInOneCall()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);

        var ddl = new SqlServerDdlGenerator().Build(SqlServerBinaryFixtureDefinition.Build());
        await SqlServerSchemaBootstrap.ApplyDdlAsync(
            _fixture.ConnectionString,
            ddl,
            cancellationToken: Ct
        );

        var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        var documents = provider.GetRequiredService<IDocumentRepository>();
        var notes = provider.GetRequiredService<IDocumentNoteRepository>();

        await documents.InsertAsync(
            new DocumentEntity
            {
                DocumentId = 1,
                Title = "alpha",
                IsPublished = true,
                Thumb = [1],
            },
            Ct
        );
        await notes.InsertAsync(
            new DocumentNoteEntity
            {
                NoteId = 1,
                DocumentId = 1,
                Note = "first",
            },
            Ct
        );

        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("alpha");
        (await notes.GetByIdAsync(1, Ct))!.Note.Should().Be("first");
    }
}
