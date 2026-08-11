using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQLite 方言の生成 DDL 適用ヘルパー <c>SqliteSchemaBootstrap.ApplyDdlAsync</c> を、実 SQLite
/// （一時ファイル DB・Docker 不要＝CI 常時実行）で検証する。
/// </summary>
/// <remarks>
/// 「DDL 文字列を接続を開いて実行する」定型がサンプル 2 箇所＋テスト 1 箇所に散っていたのを固定 infra へ寄せたもの。
/// 検証の柱は (1) 複数文の DDL が 1 回の呼び出しで通ること（Microsoft.Data.Sqlite はセミコロン区切りの複数文を
/// 1 コマンドで実行できる）、(2) 適用した DB に対して生成リポジトリの CRUD がそのまま動くこと。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteSchemaBootstrapRuntimeTests : IDisposable
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>一時 DB を破棄する</summary>
    public void Dispose() => _db.Dispose();

    /// <summary>テーブル 2 本を含む生成 DDL が 1 回の呼び出しで適用され、以降の CRUD が動く</summary>
    [Fact(DisplayName = "[SQLiteブートストラップ] 複数文 DDL が 1 回で適用され CRUD が動く")]
    public async Task ApplyDdlAsync_AppliesMultiStatementScript()
    {
        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());

        await SqliteSchemaBootstrap.ApplyDdlAsync(_db.ReadWriteCreateConnectionString, ddl, Ct);

        // documents / document_notes の 2 本が 1 回の適用で作られている
        await using (var connection = new SqliteConnection(_db.ReadWriteCreateConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('documents', 'document_notes');";
            var tables = Convert.ToInt32(await command.ExecuteScalarAsync(Ct));
            tables.Should().Be(2);
        }

        // 適用済みスキーマに対して生成リポジトリがそのまま使える（親→子の順で挿入）
        var provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
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

    /// <summary>空・空白の DDL や接続文字列は引数例外で弾かれる（黙って何もしない事故を防ぐ）</summary>
    [Fact(DisplayName = "[SQLiteブートストラップ] 空の接続文字列・DDL は引数例外")]
    public async Task ApplyDdlAsync_RejectsEmptyArguments()
    {
        var emptyConnectionString = async () =>
            await SqliteSchemaBootstrap.ApplyDdlAsync("   ", "CREATE TABLE t (x int);", Ct);
        await emptyConnectionString.Should().ThrowAsync<ArgumentException>();

        var emptyDdl = async () =>
            await SqliteSchemaBootstrap.ApplyDdlAsync(
                _db.ReadWriteCreateConnectionString,
                "   ",
                Ct
            );
        await emptyDdl.Should().ThrowAsync<ArgumentException>();
    }
}
