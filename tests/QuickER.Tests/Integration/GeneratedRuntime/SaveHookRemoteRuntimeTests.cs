using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuickER.Sqlite;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）が<b>リモート構成のサーバー側</b>で発火することを、実 HTTP
/// （Kestrel を 127.0.0.1 の空きポートで in-process 起動）＋実 SQLite（一時ファイル DB・Docker 不要＝CI 常時実行）で
/// end-to-end 検証する。サーバー実体はQuickER の <c>SqliteRepository</c>（<c>AddGeneratedSqliteRepositories</c>）で、
/// フックは<b>サーバー側の DI</b>に登録する。クライアントは生成された HTTP リモート実装
/// （<c>AddGeneratedHttpRemoteRepositories</c>）だけを使う＝利用者が組む 3 階層とまったく同じ経路で検証する。
/// </summary>
/// <remarks>
/// <para>
/// このスイートは<b>生成コードの変更を伴わない検証</b>である（フック発火の配線はサーバー側 Repository で既に成立している）。
/// 柱: (1) HTTP 越しの SaveAsync でサーバー側フックが Before/After ともに発火、(2) Before false 行は DB に残らずクライアントの
/// Save は成功として返る、(3) After の <c>WriteBinaryColumnAsync</c> で書いた blob がコミット済み＝バイナリ GET
/// エンドポイント（クライアントの <c>ReadPayloadAsync</c>）で読める、(4) After 例外→サーバー 500→クライアントで
/// <see cref="RemoteRepositoryException"/>・行も blob も残らない（サーバー側トランザクションのロールバック実証）。
/// </para>
/// <para>
/// <b>スキップの伝搬（テスト 5〜8）</b>: Before false でサーバー側がスキップした行は保存応答の <c>Skipped</c> に載って戻り、
/// クライアントの <c>AcceptChanges</c> がその行を <c>MarkUnchanged</c> しない＝直結（ADO / EF Core / インメモリ）と同じく
/// RowState が据え置かれ、次回の保存で再試行できる（旧「クライアントは Unchanged に確定する」既知の制限の解消）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SaveHookRemoteRuntimeTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();
    private WebApplication? _app;
    private ServiceProvider? _clientProvider;

    /// <summary>スキーマを作成する（サーバーは各テストがフックを指定して起動する）</summary>
    public async ValueTask InitializeAsync()
    {
        var ddl = new SqliteDdlGenerator().Build(BinaryFixtureDefinition.Build());
        await _db.ApplyDdlAsync(ddl, Ct);
    }

    /// <summary>
    /// 指定した Save フック群をサーバー側 DI に登録して Kestrel を起動し、HTTP クライアントの文書リモート面を返す。
    /// </summary>
    private async Task<IDocumentRemoteRepository> StartServerAsync(params object[] hooks)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString);

        // フックはサーバー側の DI に登録する（in-process のため単一インスタンスの共有ログをテストから観測できる）
        foreach (var hook in hooks)
        {
            if (hook is ISaveHook<DocumentEntity> documentHook)
            {
                builder.Services.AddSingleton(documentHook);
            }
            else if (hook is ISaveHook<DocumentNoteEntity> noteHook)
            {
                builder.Services.AddSingleton(noteHook);
            }
            else
            {
                throw new InvalidOperationException($"未知のフック型: {hook.GetType()}");
            }
        }

        _app = builder.Build();
        _app.MapGeneratedRemoteEndpoints();
        await _app.StartAsync(Ct);

        var baseUrl = _app.Urls.First();
        _clientProvider = new ServiceCollection()
            .AddGeneratedHttpRemoteRepositories($"{baseUrl}/quicker")
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

    // ── 1. HTTP 越しの SaveAsync でサーバー側フックが Before/After ともに発火する ──

    /// <summary>1. サーバー DI に登録したフックが、クライアント→サーバーの SaveAsync で Before/After ともに発火する</summary>
    [Fact(DisplayName = "[SaveHook/Remote] 1: HTTP 越しの SaveAsync でサーバー側フックが発火する")]
    public async Task Save_OverHttp_FiresServerSideHooks()
    {
        var log = new List<string>();
        var hook = new RemoteRecordingHook(log);
        var documents = await StartServerAsync(hook);

        var doc = NewDocument(1, "alpha", null, [9, 9]);
        doc.MarkAdded();

        await documents.SaveAsync(doc, cancellationToken: Ct);

        // サーバー側で Before→After が発火している（in-process のため共有ログで確認できる）
        log.Should().Equal("before:Insert:1", "after:Insert:1");
        (await documents.GetByIdAsync(1, Ct)).Should().NotBeNull("フックが通した行は保存される");
    }

    // ── 2. Before false 行は DB に残らず、クライアントの Save は成功として返る ──

    /// <summary>2. Before が false を返す行はサーバーでスキップされ DB に存在しない（クライアントの Save は例外にならない）</summary>
    [Fact(DisplayName = "[SaveHook/Remote] 2: Before false 行は DB になく Save は成功として返る")]
    public async Task Before_False_SkipsRow_SaveReturnsSuccessfully()
    {
        var log = new List<string>();
        var hook = new RemoteRecordingHook(log) { BeforePredicate = (_, _) => false };
        var documents = await StartServerAsync(hook);

        var doc = NewDocument(2, "beta", null, [8]);
        doc.MarkAdded();

        // クライアントの Save は例外にならず成功として返る（スキップは正常系）
        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        await act.Should().NotThrowAsync();

        // Before は発火するが After は発火せず（スキップ）、行は DB に存在しない
        log.Should().Contain("before:Insert:2").And.NotContain("after:Insert:2");
        (await documents.GetByIdAsync(2, Ct))
            .Should()
            .BeNull("Before false でスキップされた行は保存されない");
    }

    // ── 3. After の WriteBinaryColumnAsync で書いた blob がコミット済み＝バイナリ GET で読める ──

    /// <summary>3. After が context.WriteBinaryColumnAsync で書いた除外列 blob が、バイナリ GET エンドポイント経由で読める</summary>
    [Fact(
        DisplayName = "[SaveHook/Remote] 3: After が書いた blob がバイナリ GET エンドポイントで読める"
    )]
    public async Task After_WritesBinaryColumn_ReadableViaBinaryEndpoint()
    {
        var newPayload = new byte[128 * 1024];
        new Random(7).NextBytes(newPayload);

        var hook = new RemoteRecordingHook([])
        {
            AfterAction = async (entity, _, context) =>
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream(newPayload),
                    cancellationToken: Ct
                ),
        };
        var documents = await StartServerAsync(hook);

        // 新規挿入（payload は null）→ After が同一トランザクションで blob を書く
        var doc = NewDocument(3, "gamma", null, [6]);
        doc.MarkAdded();
        await documents.SaveAsync(doc, cancellationToken: Ct);

        // コミット後、After が書いた blob をバイナリ GET エンドポイント（クライアントの ReadPayloadAsync）で読める
        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(3, destination, Ct))
            .Should()
            .BeTrue("After が書いた blob は存在する");
        destination
            .ToArray()
            .Should()
            .Equal(newPayload, "After がコミット前に書いた blob が HTTP 越しに読める");
    }

    // ── 4. After 例外 → サーバー 500 → クライアントで RemoteRepositoryException・行も blob も残らない ──

    /// <summary>
    /// 4. After が（blob 書き込みの後で）例外を投げると、サーバーは 500 を返しクライアントで
    /// <see cref="RemoteRepositoryException"/> になる。サーバー側は 1 トランザクションのため行も blob も残らない。
    /// </summary>
    [Fact(
        DisplayName = "[SaveHook/Remote] 4: After 例外→500→RemoteRepositoryException・行も blob も残らない"
    )]
    public async Task After_Throws_ServerRollsBack_NoRowNoBlob()
    {
        var hook = new RemoteRecordingHook([])
        {
            AfterAction = async (entity, _, context) =>
            {
                // 同一トランザクションで blob を書いた後に例外を投げる
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream([42, 42, 42, 42, 42]),
                    cancellationToken: Ct
                );
                throw new InvalidOperationException("after-boom");
            },
        };
        var documents = await StartServerAsync(hook);

        var doc = NewDocument(4, "delta", null, [7]);
        doc.MarkAdded();

        // After 例外 → サーバー 500 → クライアントで RemoteRepositoryException
        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        await act.Should().ThrowAsync<RemoteRepositoryException>();

        // サーバー側トランザクションのロールバックにより、行も blob も残らない
        (await documents.GetByIdAsync(4, Ct))
            .Should()
            .BeNull("After 例外で行はロールバックされる");
        using var destination = new MemoryStream();
        (await documents.ReadPayloadAsync(4, destination, Ct))
            .Should()
            .BeFalse("行がないため blob も存在しない（404→false）");
    }

    // ── 5. スキップ行のクライアント側 RowState は据え置かれ、非スキップ行だけが確定する ──

    /// <summary>
    /// 5. サーバー側でスキップされた行は保存応答の <c>Skipped</c> に載って戻り、クライアントの <c>AcceptChanges</c> が
    /// その行を確定しない＝ RowState が据え置かれる。同じ保存単位の非スキップ行は従来どおり <c>Unchanged</c> へ確定する。
    /// </summary>
    /// <remarks>
    /// 直結（ADO / EF Core / インメモリ）と同じ挙動
    /// （<see cref="SaveHookRuntimeTestsBase.Before_False_SkipsSingleEntity_OthersSaved"/>）へ揃える回帰ガード。
    /// </remarks>
    [Fact(
        DisplayName = "[SaveHook/Remote] 5: スキップ行の RowState は据え置かれ非スキップ行だけ確定する"
    )]
    public async Task Before_False_SkippedRowKeepsRowState_OthersAreAccepted()
    {
        // 5 番だけスキップし、6 番は通す
        var hook = new RemoteRecordingHook([])
        {
            BeforePredicate = (entity, _) => entity.DocumentId != 5,
        };
        var documents = await StartServerAsync(hook);

        var skipped = NewDocument(5, "epsilon", null, [5]);
        var saved = NewDocument(6, "zeta", null, [6]);
        skipped.MarkAdded();
        saved.MarkAdded();

        await documents.SaveAsync([skipped, saved], cancellationToken: Ct);

        // スキップ行は保存されていないので Added のまま（次回の保存で再試行できる）
        skipped
            .RowState.Should()
            .Be(RowState.Added, "サーバーのスキップが応答で伝わり RowState は据え置かれる");

        // 同じ保存単位の非スキップ行は従来どおり確定する
        saved.RowState.Should().Be(RowState.Unchanged, "スキップされていない行は確定する");

        (await documents.GetByIdAsync(5, Ct)).Should().BeNull("スキップ行は保存されない");
        (await documents.GetByIdAsync(6, Ct)).Should().NotBeNull("非スキップ行は保存される");
    }

    // ── 6. 子（カスケード先）のスキップも同じ走査で伝わる ──

    /// <summary>
    /// 6. カスケード先の子だけがスキップされた場合も、親は確定・子は据え置きになる（応答の走査が
    /// [NavigationReference(Cascade)] 経路をたどり、クライアントの <c>AcceptChanges</c> と一致することの検証）。
    /// </summary>
    [Fact(DisplayName = "[SaveHook/Remote] 6: 子のスキップも伝わる（親は確定・子は据え置き）")]
    public async Task Before_False_OnChild_PropagatesThroughCascade()
    {
        var noteHook = new RemoteNoteHook { BeforePredicate = (_, _) => false };
        var documents = await StartServerAsync(noteHook);

        var doc = NewDocument(7, "eta", null, [7]);
        var note = new DocumentNoteEntity
        {
            NoteId = 70,
            DocumentId = 7,
            Note = "child",
        };
        doc.DocumentNotes.Add(note);
        doc.MarkAdded();
        note.MarkAdded();

        await documents.SaveAsync(doc, cancellationToken: Ct);

        doc.RowState.Should().Be(RowState.Unchanged, "親はスキップされていないので確定する");
        note.RowState.Should()
            .Be(RowState.Added, "子のスキップがカスケード走査を通って伝わり据え置かれる");

        (await documents.GetByIdAsync(7, Ct)).Should().NotBeNull("親は保存される");
    }

    // ── 7. スキップ解除後に同じインスタンスをそのまま再保存できる ──

    /// <summary>
    /// 7. RowState が据え置かれる結果として、スキップの原因が解消したあとに<b>同じインスタンス</b>をそのまま再保存できる
    /// （旧挙動では Unchanged に確定していたため、再保存しても何も起きなかった）。
    /// </summary>
    [Fact(DisplayName = "[SaveHook/Remote] 7: スキップ解除後に同じインスタンスを再保存できる")]
    public async Task SkippedRow_CanBeSavedAgainAfterSkipCleared()
    {
        var hook = new RemoteRecordingHook([]) { BeforePredicate = (_, _) => false };
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
            .BeNull("スキップなしとして扱われ、AcceptChanges は従来どおり全件を確定する");
    }

    /// <summary>使い終えたクライアント DI・サーバー・一時 DB を破棄する</summary>
    public async ValueTask DisposeAsync()
    {
        _clientProvider?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _db.Dispose();
    }

    /// <summary>
    /// サーバー側の発火を共有ログへ記録するテスト用フック。<see cref="BeforePredicate"/> で Before の返り値（スキップ）、
    /// <see cref="AfterAction"/> で After の副作用（context 経由の blob 書き込み・例外）を差し込める。
    /// </summary>
    private sealed class RemoteRecordingHook(List<string> log) : ISaveHook<DocumentEntity>
    {
        /// <summary>Before の返り値を決める述語（テストの途中で差し替えられるよう set 可能にしてある）</summary>
        public Func<DocumentEntity, SaveOperation, bool>? BeforePredicate { get; set; }

        public Func<
            DocumentEntity,
            SaveOperation,
            ISaveHookContext,
            Task
        >? AfterAction { get; init; }

        public Task<bool> BeforeSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"before:{operation}:{entity.DocumentId}");
            return Task.FromResult(BeforePredicate?.Invoke(entity, operation) ?? true);
        }

        public async Task AfterSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            log.Add($"after:{operation}:{entity.DocumentId}");

            if (AfterAction is not null)
            {
                await AfterAction(entity, operation, context);
            }
        }
    }

    /// <summary>子（メモ）側のスキップを差し込むためのテスト用フック</summary>
    private sealed class RemoteNoteHook : ISaveHook<DocumentNoteEntity>
    {
        public Func<DocumentNoteEntity, SaveOperation, bool>? BeforePredicate { get; set; }

        public Task<bool> BeforeSaveAsync(
            DocumentNoteEntity entity,
            SaveOperation operation,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(BeforePredicate?.Invoke(entity, operation) ?? true);
    }
}
