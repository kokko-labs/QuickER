using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 楽観排他のランタイムスイートを<b>実 HTTP のリモート 3 階層</b>で流す派生
/// （Kestrel を 127.0.0.1 の空きポートで in-process 起動・Docker 不要＝CI 常時実行）。
/// </summary>
/// <remarks>
/// <para>
/// サーバー実体は BinaryFixture の<b>インメモリ Repository</b>（<c>AddGeneratedInMemoryRepositories</c> はリモート面
/// <c>I{Entity}RemoteRepository</c> への転送登録も行う）。クライアントは生成された HTTP リモート実装のみを使う。
/// バックエンド非依存のシナリオは基底 <see cref="ConcurrencyRuntimeTestsBase{TEntity, TConflictException}"/> が、
/// 応答の経路ごとの版の書き戻しはリモートの共通部 <see cref="ConcurrencyRemoteRuntimeTestsBase{TEntity, TConflictException}"/> が持つ。
/// </para>
/// <para>
/// 本クラスはリモート固有の検証——旧エンベロープ（<c>Mode</c> 欠落）の既定 Optimistic 退化・クライアント検証を迂回した
/// 生 JSON の未定義 <c>Mode</c> を 400 で弾くこと——を持つ（要求の解釈だけで決まり、リポジトリの実装先に依らない）。
/// 除外列（payload / thumb）は値を持ったままだと UPDATE が拒否される既存仕様のため、一貫して未取得状態のまま扱う。
/// </para>
/// </remarks>
[RuntimeCell(RuntimeFeatures.Concurrency, RuntimeTarget.Remote, Server = RuntimeTarget.InMemory)]
public sealed class RemoteConcurrencyRuntimeTests
    : ConcurrencyRemoteRuntimeTestsBase<DocumentEntity, SaveConflictException>
{
    /// <summary>サーバー実体はインメモリ Repository（サンプルデータなし・テストごとに空で起動する）</summary>
    protected override void RegisterServerServices(IServiceCollection services) =>
        services.AddGeneratedInMemoryRepositories(seedSampleData: false);

    protected override void MapRemoteEndpoints(WebApplication app) =>
        app.MapGeneratedRemoteEndpoints(RemoteAccess.AllowAnonymous);

    protected override string RemotePathPrefix => RemotePaths.DefaultPrefix;

    protected override void RegisterClientServices(
        IServiceCollection services,
        Func<IServiceProvider, HttpClient> httpClientFactory
    ) => services.AddGeneratedHttpRemoteRepositories(httpClientFactory);

    /// <summary>クライアント側の文書リモート面を解決する</summary>
    private IDocumentRemoteRepository Documents => Client<IDocumentRemoteRepository>();

    /// <summary>クライアント側のメモリモート面を解決する</summary>
    private IDocumentNoteRemoteRepository Notes => Client<IDocumentNoteRemoteRepository>();

    /// <summary>中立表現の楽観排他ポリシーを、このフィクスチャの <c>ConcurrencyMode</c> へ翻訳する</summary>
    private static ConcurrencyMode Translate(ConcurrencyChoice choice) =>
        choice switch
        {
            ConcurrencyChoice.Optimistic => ConcurrencyMode.Optimistic,
            ConcurrencyChoice.ForceOverwrite => ConcurrencyMode.ForceOverwrite,
            _ => (ConcurrencyMode)99,
        };

    /// <summary>サーバーはテストごとに空で起動するため、リモート経由でシードを投入するだけでよい</summary>
    protected override async Task ResetAndSeedAsync()
    {
        await Documents.InsertAsync(NewEntity(SeededRootId, "alpha"), Ct);
        await Documents.InsertAsync(NewEntity(SeededChildlessRootId, "beta"), Ct);
        await Notes.InsertAsync(
            new DocumentNoteEntity
            {
                NoteId = SeededChildId,
                DocumentId = SeededRootId,
                Note = "first",
            },
            Ct
        );
    }

    protected override DocumentEntity NewEntity(int id, string title) =>
        new() { DocumentId = id, Title = title };

    protected override Task InsertAsync(DocumentEntity entity) => Documents.InsertAsync(entity, Ct);

    protected override Task<DocumentEntity?> GetAsync(int id) => Documents.GetByIdAsync(id, Ct);

    /// <summary>リモート面は <c>Query()</c> を持たないため、親と子を別々に取得してグラフを組み立てる</summary>
    protected override async Task<DocumentEntity?> GetWithChildrenAsync(int id)
    {
        var root = await Documents.GetByIdAsync(id, Ct);

        if (root is null)
        {
            return null;
        }

        var child = await Notes.GetByIdAsync(SeededChildId, Ct);

        if (child is not null)
        {
            root.DocumentNotes.Add(child);
        }

        return root;
    }

    protected override string GetTitle(DocumentEntity entity) => entity.Title;

    protected override void SetTitle(DocumentEntity entity, string title) => entity.Title = title;

    protected override byte[]? GetRowVersion(DocumentEntity entity) => entity.RowVer;

    protected override void SetRowVersion(DocumentEntity entity, byte[]? rowVersion) =>
        entity.RowVer = rowVersion;

    protected override void MarkAdded(DocumentEntity entity) => entity.MarkAdded();

    protected override void MarkUpdated(DocumentEntity entity) => entity.MarkUpdated();

    protected override void MarkRemoved(DocumentEntity entity) => entity.MarkRemoved();

    protected override Task<bool> UpdateAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic
    ) => Documents.UpdateAsync(entity, Translate(mode), Ct);

    protected override Task<int> SaveAsync(
        DocumentEntity entity,
        ConcurrencyChoice mode = ConcurrencyChoice.Optimistic,
        bool insertWhenUpdateMissing = false
    ) =>
        Documents.SaveAsync(
            entity,
            insertWhenUpdateMissing: insertWhenUpdateMissing,
            mode: Translate(mode),
            cancellationToken: Ct
        );

    /// <summary>別途取得した最新インスタンス経由で更新する（手元のインスタンスの版は古いまま残る）</summary>
    protected override async Task BumpByAnotherUserAsync(int id, string title)
    {
        var fresh = await Documents.GetByIdAsync(id, Ct);
        fresh.Should().NotBeNull();

        fresh!.Title = title;
        (await Documents.UpdateAsync(fresh, cancellationToken: Ct)).Should().BeTrue();
    }

    protected override void EditFirstChild(DocumentEntity root, string note)
    {
        var child = root.DocumentNotes.First();
        child.Note = note;
        child.MarkUpdated();
    }

    protected override void AttachNewChild(DocumentEntity root, int childId, string note)
    {
        var child = new DocumentNoteEntity
        {
            NoteId = childId,
            DocumentId = root.DocumentId,
            Note = note,
        };
        child.MarkAdded();
        root.DocumentNotes.Add(child);
    }

    protected override bool IsGraphUnchanged(DocumentEntity root) =>
        root.RowState == RowState.Unchanged
        && root.DocumentNotes.All(note => note.RowState == RowState.Unchanged);

    protected override Task<int> SaveManyAsync(IReadOnlyList<DocumentEntity> roots) =>
        Documents.SaveAsync(roots, cancellationToken: Ct);

    protected override (string Reason, string? EntityTypeName, string? Key) Describe(
        SaveConflictException exception
    ) => (exception.Reason.ToString(), exception.EntityTypeName, exception.Key);

    protected override async Task<string?> ReadChildNoteAsync(int noteId) =>
        (await Notes.GetByIdAsync(noteId, Ct))?.Note;

    // ── リモート固有: 旧エンベロープ互換 ──

    /// <summary>Mode を持たない旧エンベロープは既定の Optimistic として読まれる</summary>
    [Fact(
        DisplayName = "[Concurrency/Remote] Mode なしの旧エンベロープは Optimistic として扱われる"
    )]
    public async Task LegacyEnvelopeWithoutMode_IsReadAsOptimistic()
    {
        await ResetAndSeedAsync();

        var stale = await Documents.GetByIdAsync(SeededRootId, Ct);
        await BumpByAnotherUserAsync(SeededRootId, "by-first");

        using var raw = new HttpClient();

        // Mode フィールドを持たない旧エンベロープ（Insert 用の RemoteEntityRequest がそのまま旧 Update の形）
        stale!.Title = "by-second";
        var conflict = await PostUpdateAsync(raw, new RemoteEntityRequest<DocumentEntity>(stale));

        conflict
            .Should()
            .Be(HttpStatusCode.Conflict, "Mode 欠落は既定の Optimistic＝版チェックが効く");

        // 版が最新なら同じ旧エンベロープで成功する（常に失敗しているわけではないことの対照）
        var fresh = await Documents.GetByIdAsync(SeededRootId, Ct);
        fresh!.Title = "by-legacy";
        var accepted = await PostUpdateAsync(raw, new RemoteEntityRequest<DocumentEntity>(fresh));

        accepted.Should().Be(HttpStatusCode.OK);
        (await ReadTitleAsync(SeededRootId)).Should().Be("by-legacy");
    }

    /// <summary>
    /// クライアント検証を迂回した手書きクライアント（生の JSON で <c>"Mode":99</c>）はサーバーが 400 で拒否する
    /// （enum は JSON で任意の数値を受けるため、素通しすると版チェックが黙って無効化される）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/Remote] 生 JSON の未定義 Mode はサーバーが 400 BadRequest で拒否する"
    )]
    public async Task UndefinedConcurrencyModeOverTheWire_IsRejectedWith400()
    {
        await ResetAndSeedAsync();

        var document = await Documents.GetByIdAsync(SeededRootId, Ct);
        document!.Title = "by-undefined";
        var entityJson = JsonSerializer.Serialize(document, RemoteJson.Options);

        using var raw = new HttpClient();
        var (status, body) = await PostUpdateJsonAsync(
            raw,
            $$"""{"Entity":{{entityJson}},"Mode":99}"""
        );

        status.Should().Be(HttpStatusCode.BadRequest, "リクエスト解釈の失敗＝クライアント起因");

        var error = JsonSerializer.Deserialize<RemoteError>(body, RemoteJson.Options);
        error.Should().NotBeNull();
        error!.Type.Should().Be("BadRequest");

        (await ReadTitleAsync(SeededRootId))
            .Should()
            .Be("alpha", "拒否されたので更新は適用されない");
    }

    /// <summary>生の JSON をそのまま Update エンドポイントへ POST し、応答のステータスコードを返す</summary>
    private async Task<HttpStatusCode> PostUpdateAsync(HttpClient client, object payload)
    {
        var (status, _) = await PostUpdateJsonAsync(
            client,
            JsonSerializer.Serialize(payload, RemoteJson.Options)
        );
        return status;
    }

    /// <summary>組み立て済みの JSON 文字列を Update エンドポイントへ POST し、ステータスと本文を返す</summary>
    private async Task<(HttpStatusCode Status, string Body)> PostUpdateJsonAsync(
        HttpClient client,
        string json
    )
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            $"{BaseUrl}/quicker/Document/Update",
            content,
            Ct
        );
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }
}
