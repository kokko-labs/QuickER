using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）を<b>インメモリ Repository</b>（<see cref="InMemoryDataStore"/> 共有）で
/// 検証する。実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// バックエンド非依存のシナリオは基底 <see cref="SaveHookRuntimeTestsBase"/> が持つ。インメモリは実トランザクションを
/// 持たない（3 フェーズ＝Before プリパス → 保存 → After ポストパス）が、copy-on-write ステージングで保存単位を all-or-nothing に
/// するため After 例外時も保存フェーズの変更は残らない。本クラスは InMemory 固有の検証（skip 時の Put 抑止・After の
/// 除外列書き込みがストアへ反映＝実 DB のストリーミングとパリティ・After 例外時は blob 書き込みも巻き戻る・
/// context の生 SQL は NotSupported）を持つ。
/// </remarks>
public sealed class SaveHookInMemoryRuntimeTests : SaveHookRuntimeTestsBase, IDisposable
{
    /// <summary>全リポジトリで共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>指定フック群から Save フックのレジストリを構築する（フックなしは null＝完全 no-op）</summary>
    private ISaveHookRegistry? BuildRegistry(object[] hooks)
    {
        if (hooks.Length == 0)
        {
            return null;
        }

        var services = new ServiceCollection();

        foreach (var hook in hooks)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return new ServiceProviderSaveHookRegistry(provider);
    }

    protected override IDocumentRepository Documents(params object[] hooks) =>
        new InMemoryDocumentRepository(_store, BuildRegistry(hooks));

    protected override IDocumentNoteRepository Notes(params object[] hooks) =>
        new InMemoryDocumentNoteRepository(_store, BuildRegistry(hooks));

    /// <summary>ストアを空にして共通シードを投入する（フックなしのリポジトリ経由）</summary>
    protected override async Task ResetAndSeedAsync()
    {
        _store.Clear();

        var documents = Documents();
        var notes = Notes();

        await documents.InsertAsync(NewDocument(1, "alpha", Doc1Payload, [9, 9]), Ct);
        await documents.InsertAsync(NewDocument(2, "beta", null, [8]), Ct);
        await documents.InsertAsync(NewDocument(3, "gamma", [5, 6], [6]), Ct);
        await notes.InsertAsync(NewNote(100, 1, "note-a"), Ct);
        await notes.InsertAsync(NewNote(101, 1, "note-b"), Ct);
    }

    // ── InMemory 固有 1: After の除外列書き込みがストアへ反映される（実 DB のストリーミングとパリティ） ──

    /// <summary>After が context.WriteBinaryColumnAsync で除外列 blob を書くと、Save 後に WithUnboundedBinary で読める</summary>
    [Fact(
        DisplayName = "[SaveHook/InMemory] After の WriteBinaryColumnAsync がストアへ反映され読める"
    )]
    public async Task After_WritesBinaryColumn_VisibleInStore()
    {
        await ResetAndSeedAsync();

        var newPayload = new byte[64 * 1024];
        new Random(7).NextBytes(newPayload);

        var hook = new RecordingHook<DocumentEntity>([], e => e.DocumentId, "h")
        {
            AfterAction = async (entity, _, context) =>
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream(newPayload),
                    cancellationToken: Ct
                ),
        };
        var documents = Documents(hook);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-hooked";
        doc.MarkUpdated();
        await documents.SaveAsync(doc, cancellationToken: Ct);

        var readBack = await Documents()
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        readBack!.Payload.Should().Equal(newPayload, "After が書いた payload がストアに反映される");
        readBack
            .Title.Should()
            .Be(
                "alpha-hooked",
                "保存フェーズの更新も同時に反映される（After の blob 書き込みは保存中の行の上に積む）"
            );
    }

    // ── InMemory 固有 1-b: After が書いた blob も後続の After 例外で巻き戻る ──

    /// <summary>
    /// 1 つ目の After が除外列 blob を書き、2 つ目の After が例外を投げると、行の更新だけでなく
    /// <c>WriteBinaryColumnAsync</c> が書いた blob もステージング破棄で公開されない（保存単位の all-or-nothing）。
    /// </summary>
    [Fact(DisplayName = "[SaveHook/InMemory] After が書いた blob も後続の After 例外で巻き戻る")]
    public async Task After_WritesBinaryColumn_RolledBackWhenLaterAfterThrows()
    {
        await ResetAndSeedAsync();

        var newPayload = new byte[8 * 1024];
        new Random(11).NextBytes(newPayload);

        var writer = new RecordingHook<DocumentEntity>([], e => e.DocumentId, "w")
        {
            AfterAction = async (entity, _, context) =>
                await context.WriteBinaryColumnAsync(
                    nameof(DocumentEntity.Payload),
                    entity.DocumentId,
                    new MemoryStream(newPayload),
                    cancellationToken: Ct
                ),
        };
        var breaker = new RecordingHook<DocumentEntity>([], e => e.DocumentId, "b")
        {
            AfterAction = (_, _, _) => throw new InvalidOperationException("after-boom"),
        };
        var documents = Documents(writer, breaker);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-doomed";
        doc.MarkUpdated();

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        var reread = await Documents()
            .Query()
            .Where(d => d.DocumentId == 1)
            .WithUnboundedBinary()
            .FirstOrDefaultAsync(Ct);
        reread!.Title.Should().Be("alpha", "行の更新は巻き戻る");
        reread.Payload.Should().Equal(Doc1Payload, "After が書いた blob も保存前の値へ巻き戻る");
    }

    // ── InMemory 固有 2: context の生 SQL は NotSupported ──

    /// <summary>After の context.ExecuteSqlAsync はインメモリでは NotSupportedException を投げる</summary>
    [Fact(DisplayName = "[SaveHook/InMemory] After の ExecuteSqlAsync は NotSupportedException")]
    public async Task After_ExecuteSql_ThrowsNotSupported()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>([], e => e.DocumentId, "h")
        {
            AfterAction = async (_, _, context) =>
                await context.ExecuteSqlAsync(
                    "INSERT INTO audit (note) VALUES (@n)",
                    new { n = "x" },
                    Ct
                ),
        };
        var documents = Documents(hook);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "alpha-mem";
        doc.MarkUpdated();

        var act = () => documents.SaveAsync(doc, cancellationToken: Ct);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // ── InMemory 固有 3: skip されたエンティティの Put は抑止される（ストア実体は据え置き） ──

    /// <summary>Before が false のとき更新の Put は抑止され、ストアの実体（旧値）は変化しない</summary>
    [Fact(DisplayName = "[SaveHook/InMemory] skip された更新は Put を抑止しストアの旧値を保つ")]
    public async Task SkippedUpdate_SuppressesPut_StoreKeepsOldValue()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>([], e => e.DocumentId, "h")
        {
            // 更新（Update）だけをスキップする
            BeforePredicate = (_, op) => op != SaveOperation.Update,
        };
        var documents = Documents(hook);

        var doc = await documents.GetByIdAsync(1, Ct);
        doc!.Title = "changed";
        doc.MarkUpdated();

        await documents.SaveAsync(doc, cancellationToken: Ct);

        // Put が抑止され、ストアには旧タイトルが残る。渡したエンティティの RowState も据え置き
        (await Documents().GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("alpha", "skip された更新は Put されずストアの旧値が保たれる");
        doc.RowState.Should().Be(RowState.Updated, "スキップされた行は状態が据え置かれる");
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }
}
