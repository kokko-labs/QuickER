using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// Save フック（<see cref="ISaveHook{TEntity}"/>）の、複数のフックを登録したときの順序と短絡を、実装先（QuickER 版 Repository の
/// SQLite・EF Core・インメモリ）を跨いで検証する共通基底。各派生はリポジトリ生成・シードだけを差し込む。
/// </summary>
/// <remarks>
/// <para>
/// 入力はバイナリフィクスチャ（<see cref="BinaryFixtureDefinition"/>）の <c>documents</c>。状態の確認はリポジトリ API
/// （<c>GetByIdAsync</c>）で行うため、生 SQL を持たないインメモリでも同じアサーションが成立する。
/// </para>
/// <para>
/// ほかの Save フックの意味論（スキップ・After 例外での巻き戻り・insertWhenUpdateMissing・サブツリー削除・直接の書き込みの素通り・
/// 未登録・複数形の保存・After の context の書き込み）は、実装先の一致のシナリオ（<c>BackendParityScenarios.SaveHook.cs</c>）が
/// 5 つの実装先で持つ。複数のフックの登録順と短絡は、実装先の一致の記録フックが 1 つだけなので、ここに残す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class SaveHookRuntimeTestsBase
{
    /// <summary>テスト全体で使うキャンセルトークン</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>文書 1 の本体バイナリ（payload。除外列だが INSERT では DB に書かれる）</summary>
    protected static readonly byte[] Doc1Payload = [1, 2, 3, 4];

    /// <summary>スキーマ（または空ストア）を用意し、共通のシードを投入する</summary>
    /// <remarks>documents: 1="alpha"（payload あり）・2="beta"（payload なし）・3="gamma"。document_notes: (100,1)・(101,1)</remarks>
    protected abstract Task ResetAndSeedAsync();

    /// <summary>指定した Save フック群を登録した文書リポジトリを生成する（フックなしなら状態確認用の素の実装）</summary>
    protected abstract IDocumentRepository Documents(params object[] hooks);

    /// <summary>指定した Save フック群を登録した文書メモ（子）リポジトリを生成する</summary>
    protected abstract IDocumentNoteRepository Notes(params object[] hooks);

    /// <summary>文書エンティティを組み立てる</summary>
    protected static DocumentEntity NewDocument(
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

    /// <summary>文書メモ（子）エンティティを組み立てる</summary>
    protected static DocumentNoteEntity NewNote(int id, int documentId, string note) =>
        new()
        {
            NoteId = id,
            DocumentId = documentId,
            Note = note,
        };

    /// <summary>文書が存在するか（バックエンド非依存＝GetById で確認）</summary>
    protected async Task<bool> DocumentExistsAsync(int id) =>
        await Documents().GetByIdAsync(id, Ct) is not null;

    // ── 5. 複数フックの短絡と順序 ──

    /// <summary>5. Before は登録順で最初の false で短絡（残りは呼ばれない）、全 true のとき After も登録順に呼ばれる</summary>
    [Fact(DisplayName = "[SaveHook] 5: 複数フックは登録順・最初の false で短絡・After も登録順")]
    public async Task MultipleHooks_ShortCircuitAndOrder()
    {
        await ResetAndSeedAsync();

        // 短絡: s2 が Insert を止める → s3 の Before は呼ばれない・After は誰も呼ばれない
        var shortLog = new List<string>();
        var s1 = new RecordingHook<DocumentEntity>(shortLog, e => e.DocumentId, "s1");
        var s2 = new RecordingHook<DocumentEntity>(shortLog, e => e.DocumentId, "s2")
        {
            BeforePredicate = (_, _) => false,
        };
        var s3 = new RecordingHook<DocumentEntity>(shortLog, e => e.DocumentId, "s3");
        var shortDocs = Documents(s1, s2, s3);

        var blocked = NewDocument(20, "blocked", null, [1]);
        blocked.MarkAdded();
        await shortDocs.SaveAsync(blocked, cancellationToken: Ct);

        shortLog.Should().Equal("s1:before:Insert:20", "s2:before:Insert:20");
        (await DocumentExistsAsync(20)).Should().BeFalse();

        // 順序: 全 true → Before を登録順、After を登録順に呼ぶ
        var orderLog = new List<string>();
        var o1 = new RecordingHook<DocumentEntity>(orderLog, e => e.DocumentId, "o1");
        var o2 = new RecordingHook<DocumentEntity>(orderLog, e => e.DocumentId, "o2");
        var o3 = new RecordingHook<DocumentEntity>(orderLog, e => e.DocumentId, "o3");
        var orderDocs = Documents(o1, o2, o3);

        var passed = NewDocument(21, "passed", null, [1]);
        passed.MarkAdded();
        await orderDocs.SaveAsync(passed, cancellationToken: Ct);

        orderLog
            .Should()
            .Equal(
                "o1:before:Insert:21",
                "o2:before:Insert:21",
                "o3:before:Insert:21",
                "o1:after:Insert:21",
                "o2:after:Insert:21",
                "o3:after:Insert:21"
            );
    }
}
