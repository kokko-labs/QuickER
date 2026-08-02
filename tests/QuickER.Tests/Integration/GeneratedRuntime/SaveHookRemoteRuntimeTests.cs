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
/// <b>既知の制限（テスト 5 で固定）</b>: Before false でサーバー側でスキップされた行でも、クライアント側の
/// <c>AcceptChanges</c> はスキップを知り得ないため RowState が <c>Unchanged</c> に確定する。この現挙動を回帰防止として固定する。
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

    // ── 5. 既知の制限: Before false でスキップされた行のクライアント側 RowState は Unchanged になる ──

    /// <summary>
    /// 5. <b>既知の制限の固定</b>: Before false でサーバー側でスキップされた行でも、クライアント側の <c>AcceptChanges</c> は
    /// スキップを知り得ないため RowState が <c>Unchanged</c> に確定する。DB に行が無いこととあわせて現挙動を回帰防止で固定する。
    /// </summary>
    /// <remarks>
    /// この非対称（サーバーはスキップ・クライアントは Unchanged 確定）は設計上の既知の制限であり、将来 Save 応答へ
    /// スキップキー集合を載せるプロトコル拡張で解消可能（tasks/todo.md のバックログ参照）。直結（ADO/EF Core/InMemory）では
    /// スキップ行の RowState は据え置かれる（<see cref="SaveHookRuntimeTestsBase.Before_False_SkipsSingleEntity_OthersSaved"/>）。
    /// </remarks>
    [Fact(
        DisplayName = "[SaveHook/Remote] 5: 既知の制限＝スキップ行のクライアント RowState は Unchanged になる"
    )]
    public async Task Before_False_ClientRowStateBecomesUnchanged_KnownLimitation()
    {
        var hook = new RemoteRecordingHook([]) { BeforePredicate = (_, _) => false };
        var documents = await StartServerAsync(hook);

        var doc = NewDocument(5, "epsilon", null, [5]);
        doc.MarkAdded();
        doc.IsAdded.Should().BeTrue();

        await documents.SaveAsync(doc, cancellationToken: Ct);

        // 既知の制限: サーバーはスキップしたが、クライアントの AcceptChanges はそれを知り得ず Unchanged に確定する
        doc.RowState.Should()
            .Be(
                RowState.Unchanged,
                "既知の制限＝クライアント側 AcceptChanges はサーバーのスキップを反映しない"
            );

        // 一方、サーバー側ではスキップされ DB に行は存在しない（真実は DB 側にある）
        (await documents.GetByIdAsync(5, Ct))
            .Should()
            .BeNull("サーバー側ではスキップされ保存されていない");
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
        public Func<DocumentEntity, SaveOperation, bool>? BeforePredicate { get; init; }

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
}
