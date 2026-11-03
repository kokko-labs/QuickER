using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）が<b>リモート構成のサーバー側</b>でスキップした行を、クライアントが同じインスタンスの
/// まま再保存できることを、実 HTTP（Kestrel を 127.0.0.1 の空きポートで in-process 起動）＋実 SQLite（一時ファイル DB・Docker 不要＝
/// CI 常時実行）で検証する。サーバー実体は QuickER の <c>SqliteRepository</c>（<c>AddGeneratedSqliteRepositories</c>）で、
/// フックは<b>サーバー側の DI</b>に登録する。あわせて、<c>Skipped</c> を持たない旧応答ボディの読み方を確かめる。
/// </summary>
/// <remarks>
/// サーバー側フックの発火・スキップした行の RowState の据え置き（子のスキップを含む）・After の blob の書き込みと After 例外での
/// 巻き戻りは、実装先の一致のシナリオ（<c>hook-skip-insert-others-saved</c>・<c>hook-skip-new-child-keeps-it-added</c>・
/// <c>hook-after-writes-blobs</c>・<c>hook-after-writes-blob-then-later-after-throws</c> ほか）がリモートの実装先でも持つ。
/// スキップの原因を解いてから同じインスタンスを再保存する形は、途中でフックの規則を差し替える手が要るのでここに残す。
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SaveHookRemoteRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private InProcessRemoteServer? _server;
    private ServiceProvider? _clientProvider;

    /// <summary>スキーマを作成する（サーバーは各テストがフックを指定して起動する）</summary>
    public async ValueTask InitializeAsync()
    {
        await _db.ApplyDdlAsync(BinaryFixtureDefinition.Build(), Ct);
    }

    /// <summary>
    /// 指定した Save フック群をサーバー側 DI に登録して Kestrel を起動し、HTTP クライアントの文書リモート面を返す。
    /// </summary>
    private async Task<IDocumentRemoteRepository> StartServerAsync(params object[] hooks)
    {
        _server = await InProcessRemoteServer.StartAsync(
            services =>
            {
                services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString);

                // フックはサーバー側の DI に登録する（in-process のため単一インスタンスの共有ログをテストから観測できる）
                foreach (var hook in hooks)
                {
                    services.AddSaveHook(hook);
                }
            },
            app => app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous),
            Ct
        );

        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories(_server.BaseAddress(RemotePaths.DefaultPrefix))
            .BuildServiceProvider();

        return _clientProvider.GetRequiredService<IDocumentRemoteRepository>();
    }

    /// <summary>文書エンティティを組み立てる</summary>
    private static DocumentEntity NewDocument(
        int id,
        string title,
        byte[]? payload,
        byte[] thumb
    ) =>
        new()
        {
            DocumentId = id,
            Title = title,
            Payload = payload,
            Thumb = thumb,
        };

    // ── 7. スキップ解除後に同じインスタンスをそのまま再保存できる ──

    /// <summary>
    /// 7. RowState が据え置かれる結果として、スキップの原因が解消したあとに<b>同じインスタンス</b>をそのまま再保存できる
    /// （旧挙動では Unchanged に確定していたため、再保存しても何も起きなかった）。
    /// </summary>
    [Fact(DisplayName = "[SaveHook/Remote] 7: スキップ解除後に同じインスタンスを再保存できる")]
    public async Task SkippedRow_CanBeSavedAgainAfterSkipCleared()
    {
        var hook = new RecordingHook<DocumentEntity>([], e => e.DocumentId)
        {
            BeforePredicate = (_, _) => false,
        };
        var documents = await StartServerAsync(hook);

        var doc = NewDocument(8, "theta", null, [8]);
        doc.MarkAdded();

        await documents.SaveAsync(doc, cancellationToken: Ct);
        (await documents.GetByIdAsync(8, Ct)).Should().BeNull("1 回目はスキップされる");

        // スキップの原因が解消したものとしてフックを通すモードへ切り替え、同じインスタンスを再保存する
        hook.BeforePredicate = null;

        await documents.SaveAsync(doc, cancellationToken: Ct);

        doc.RowState.Should().Be(RowState.Unchanged, "2 回目は保存され確定する");
        (await documents.GetByIdAsync(8, Ct)).Should().NotBeNull("再保存で行が作られる");
    }

    // ── 8. 旧サーバー互換: Skipped を持たない応答ボディは「スキップなし」として読める ──

    /// <summary>
    /// 8. <c>Skipped</c> を載せる前のサーバーが返す応答ボディ（当該フィールドなし）でも、クライアントは
    /// 「スキップなし」として解釈する（<c>RemoteSaveResult.Skipped</c> は既定 null・<c>SkippedLookup(null)</c> は null）。
    /// </summary>
    [Fact(
        DisplayName = "[SaveHook/Remote] 8: Skipped 欠落の旧応答ボディはスキップなしとして読める"
    )]
    public void LegacySaveResponseWithoutSkipped_IsReadAsNoSkips()
    {
        // Skipped フィールドを持たない旧サーバーの応答ボディ
        var legacy = System.Text.Json.JsonSerializer.Deserialize<RemoteSaveResult>(
            """{"Affected":1,"RowVersions":[]}""",
            RemoteJson.Options
        );

        legacy!.Affected.Should().Be(1);
        legacy.Skipped.Should().BeNull("既定値付きのため欠落したフィールドは null になる");
        RemoteEntityGraph
            .SkippedLookup(legacy.Skipped)
            .Should()
            .BeNull("スキップなしとして扱われ、AcceptChanges は全件を確定する");
    }

    /// <summary>使い終えたクライアント DI・サーバー・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        _db.Dispose();
    }
}
