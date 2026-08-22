using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// SQLite 方言の生成 <c>SqlConnectionFactory</c> が<b>外部キー強制を既定 ON</b> にすること、および
/// 接続文字列の明示指定をそのまま尊重することを、実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）で検証する。
/// </summary>
/// <remarks>
/// <para>
/// Microsoft.Data.Sqlite の既定は FK 強制 OFF のため、QuickER が生成した DDL の FK 制約は接続文字列に
/// <c>Foreign Keys=True</c> を書かない限り黙って無効だった（親のない子行が入り、親を消しても子が残る）。
/// DDL が制約を宣言する以上、強制されるのが既定として正しい。
/// </para>
/// <para>
/// 判定は <c>SqliteConnectionStringBuilder.ForeignKeys</c>（<c>bool?</c>・<c>null</c>＝未指定）で行うため、
/// <c>Foreign Keys=False</c> の明示指定は従来挙動のまま残る。本スイートはその両側（既定 ON／明示尊重）を対で固定する。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SqliteForeignKeyDefaultRuntimeTests : IAsyncLifetime
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>各テストが読み書きする一時ファイル DB</summary>
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    /// <summary>存在しない文書を親に持つメモ（＝FK 違反）の文書 ID</summary>
    private const int MissingDocumentId = 999;

    /// <summary>スキーマ（documents ← document_notes の FK 付き）を作成する</summary>
    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);
    }

    /// <summary>一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>指定の接続文字列でメモリポジトリを解決する</summary>
    private static IDocumentNoteRepository NotesFor(string connectionString) =>
        new ServiceCollection()
            .AddGeneratedSqliteRepositories(connectionString)
            .BuildServiceProvider()
            .GetRequiredService<IDocumentNoteRepository>();

    /// <summary>親のない（＝FK 違反となる）メモを組み立てる</summary>
    private static DocumentNoteEntity OrphanNote(int noteId) =>
        new()
        {
            NoteId = noteId,
            DocumentId = MissingDocumentId,
            Note = "orphan",
        };

    /// <summary>
    /// 接続文字列に <c>Foreign Keys</c> の指定がないとき、生成リポジトリ経由の FK 違反 INSERT が拒否される
    /// （従来は黙って成功していた回帰の本体）
    /// </summary>
    [Fact(DisplayName = "[SQLite FK] 未指定の接続文字列でも FK 違反 INSERT が拒否される")]
    public async Task ForeignKeys_Unspecified_EnforcesConstraint()
    {
        // SqliteTempDatabase の接続文字列は DataSource / Mode のみ＝Foreign Keys 未指定
        new SqliteConnectionStringBuilder(_db.ReadWriteCreateConnectionString)
            .ForeignKeys.Should()
            .BeNull();

        var notes = NotesFor(_db.ReadWriteCreateConnectionString);

        var act = async () => await notes.InsertAsync(OrphanNote(1), Ct);
        (await act.Should().ThrowAsync<SqliteException>())
            .And.Message.Should()
            .Contain("FOREIGN KEY");

        // 拒否されたので行は残らない
        (await notes.GetByIdAsync(1, Ct))
            .Should()
            .BeNull();
    }

    /// <summary>
    /// <c>Foreign Keys=False</c> を明示した接続文字列では FK が強制されない（明示指定の尊重）
    /// </summary>
    [Fact(DisplayName = "[SQLite FK] 明示 Foreign Keys=False は尊重され通る")]
    public async Task ForeignKeys_ExplicitlyDisabled_IsRespected()
    {
        var connectionString = new SqliteConnectionStringBuilder(
            _db.ReadWriteCreateConnectionString
        )
        {
            ForeignKeys = false,
        }.ConnectionString;

        var notes = NotesFor(connectionString);

        await notes.InsertAsync(OrphanNote(2), Ct);

        var stored = await notes.GetByIdAsync(2, Ct);
        stored.Should().NotBeNull();
        stored!.DocumentId.Should().Be(MissingDocumentId);
    }
}
