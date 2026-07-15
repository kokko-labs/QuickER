using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）を<b>インメモリ Repository</b>（<see cref="InMemoryDataStore"/> 共有）で
/// 検証する。実 DB を使わないため Docker 不要＝CI 常時実行。
/// </summary>
/// <remarks>
/// バックエンド非依存のシナリオは基底 <see cref="SaveHookRuntimeTestsBase"/> が持つ。インメモリは実トランザクションを
/// 持たない（3 フェーズ＝Before プリパス → 保存 → After ポストパス）ため、<see cref="AfterExceptionLeavesResidue"/>=true＝
/// After が例外を投げても保存フェーズの変更は残る（ベストエフォート）。本クラスは InMemory 固有の検証（skip 時の Put 抑止・
/// After の除外列書き込みがストアへ反映＝実 DB のストリーミングとパリティ・context の生 SQL は NotSupported）を持つ。
/// </remarks>
public sealed class SaveHookInMemoryRuntimeTests : SaveHookRuntimeTestsBase, IDisposable
{
    /// <summary>全リポジトリで共有するインメモリストア（実 DB のファイルに相当する永続点）</summary>
    private readonly InMemoryDataStore _store = new();
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>インメモリは実トランザクションを持たないため After 例外で保存フェーズの変更が残る</summary>
    protected override bool AfterExceptionLeavesResidue => true;

    /// <summary>指定フック群から Save フックのレジストリを構築する（フックなしは null＝完全 no-op）</summary>
    private ISaveHookRegistry? BuildRegistry(object[] hooks)
    {
        if (hooks.Length == 0)
        {
            return null;
        }

        var services = new ServiceCollection();
        SaveHookAdoRuntimeTests.RegisterHooks(services, hooks);

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

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
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
    }

    // ── InMemory 固有 2: context の生 SQL は NotSupported ──

    /// <summary>After の context.ExecuteSqlAsync はインメモリでは NotSupportedException を投げる</summary>
    [Fact(DisplayName = "[SaveHook/InMemory] After の ExecuteSqlAsync は NotSupportedException")]
    public async Task After_ExecuteSql_ThrowsNotSupported()
    {
        await ResetAndSeedAsync();

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
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

        var hook = new RecordingHook<DocumentEntity>("h", [], e => e.DocumentId)
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
