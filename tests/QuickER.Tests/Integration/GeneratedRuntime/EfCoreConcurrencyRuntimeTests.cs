using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedBinaryFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// rowversion 列を持つテーブルの楽観排他（<c>ConcurrencyMode</c>）を、<b>EF Core 版 Repository</b>で
/// 実 SQL Server（Testcontainers・Docker 依存）に流して検証する。
/// </summary>
/// <remarks>
/// <para>
/// EF Core は方言非依存のため、バイナリフィクスチャ（<see cref="BinaryFixtureDefinition"/>＝SQLite 方言のQuickER 版
/// Repository と併存生成）の EF Core リポジトリをそのまま SQL Server へ接続して使う。スキーマは同じ図から
/// <see cref="SqlServerDdlGenerator"/> で作る＝<c>row_ver</c> が実際に <c>rowversion</c> として自動採番され、
/// EF Core の <c>IsRowVersion()</c> による並行性トークンが実際に効く唯一の構成（既存の
/// <c>BinaryColumnEfCoreRuntimeTests</c> は SQLite のため <c>row_ver</c> が常に NULL のまま）。
/// </para>
/// <para>
/// EF Core は「影響行数 0」を <c>DbUpdateConcurrencyException</c> として 1 つにまとめて報告するため、
/// 生成コードは報告されたエントリごとに DB の現在値を読み直して次の 2 つを区別する:
/// </para>
/// <list type="bullet">
///   <item>行が消えている＝従来契約（単一 <c>UpdateAsync</c> は <c>false</c>・<c>insertWhenUpdateMissing</c> は INSERT へ切替）</item>
///   <item>行は在るが版が進んでいる＝<c>SaveConflictException</c>（<c>ForceOverwrite</c> なら DB 側の値を
///     オリジナルへ取り込んで再試行＝last-write-wins）</item>
/// </list>
/// <para>
/// とくに <c>insertWhenUpdateMissing</c> × 版違いは、区別しないと INSERT へ倒れて主キー重複に化けるため
/// 回帰テストとして重要（テスト 6）。
/// </para>
/// <para>
/// 「他者による更新」は生 SQL で直接 UPDATE して作る（EF Core を経由しないため手元のエンティティの
/// <c>RowVer</c> は古いまま残り、実際の競合と同じ状態を決定的に再現できる）。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class EfCoreConcurrencyRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlServer）</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>スキーマを作成し、文書 1 件（メモ 1 件つき）をシードする</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ExecuteAsync(
            new SqlServerDdlGenerator().Build(BinaryFixtureDefinition.Build()),
            Ct
        );

        _provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString)
            )
            .BuildServiceProvider();

        await Documents()
            .InsertAsync(
                new DocumentEntity
                {
                    DocumentId = 1,
                    Title = "alpha",
                    Thumb = [1],
                },
                Ct
            );
        await Notes()
            .InsertAsync(
                new DocumentNoteEntity
                {
                    NoteId = 100,
                    DocumentId = 1,
                    Note = "first",
                },
                Ct
            );
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        foreach (var provider in _hookProviders)
        {
            provider.Dispose();
        }

        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>Save フックを登録した専用 DI コンテナ（テストごとに 1 つ作り、最後にまとめて破棄する）</summary>
    private readonly List<ServiceProvider> _hookProviders = [];

    /// <summary>Save フックを 1 つ登録した EF Core 版文書リポジトリを解決する</summary>
    private IDocumentRepository DocumentsWithHook(ISaveHook<DocumentEntity> hook)
    {
        var provider = new ServiceCollection()
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString)
            )
            .AddSingleton(hook)
            .BuildServiceProvider();

        _hookProviders.Add(provider);
        return provider.GetRequiredService<IDocumentRepository>();
    }

    /// <summary>文書リポジトリを解決する</summary>
    private IDocumentRepository Documents() => _provider.GetRequiredService<IDocumentRepository>();

    /// <summary>メモリポジトリを解決する</summary>
    private IDocumentNoteRepository Notes() =>
        _provider.GetRequiredService<IDocumentNoteRepository>();

    /// <summary>EF Core を経由せず生 SQL で直接 1 行更新して「他者による更新」を作る</summary>
    private Task BumpByAnotherUserAsync(int documentId) =>
        _fixture.ExecuteAsync(
            $"UPDATE documents SET title = 'by-another-user' WHERE document_id = {documentId}",
            Ct
        );

    // ── 単一 UpdateAsync ──

    /// <summary>1. 版が古い更新は SaveConflictException（従来の「握り潰して false」ではない）</summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] UpdateAsync: 版が古いと SaveConflictException・DB は先勝ちのまま"
    )]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        var documents = Documents();

        var stale = await documents.GetByIdAsync(1, Ct);
        stale.Should().NotBeNull();
        stale!.RowVer.Should().NotBeNull("SQL Server の rowversion は取得時点で読める");

        await BumpByAnotherUserAsync(1);

        stale.Title = "by-me";
        var act = async () => await documents.UpdateAsync(stale, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*", "版が一致せず競合として弾かれる");

        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-another-user", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>2. 存在しない行の更新は従来どおり false（競合ではない）</summary>
    [Fact(DisplayName = "[Concurrency/EFCore] UpdateAsync: 行が存在しなければ従来どおり false")]
    public async Task UpdateAsync_ReturnsFalse_WhenRowIsMissing()
    {
        var documents = Documents();

        var missing = new DocumentEntity
        {
            DocumentId = 999,
            Title = "ghost",
            Thumb = [],
            RowVer = [0, 0, 0, 0, 0, 0, 0, 1],
        };

        (await documents.UpdateAsync(missing, cancellationToken: Ct))
            .Should()
            .BeFalse("行なしは競合ではなく「更新対象なし」＝従来契約の false");
    }

    /// <summary>3. ForceOverwrite は版条件を外して上書きし、新しい版を反映する</summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] UpdateAsync: ForceOverwrite は版を無視して上書きし新版を反映する"
    )]
    public async Task UpdateAsync_ForceOverwrite_OverwritesAndRefreshesRowVersion()
    {
        var documents = Documents();

        var stale = await documents.GetByIdAsync(1, Ct);
        var staleVersion = stale!.RowVer;

        await BumpByAnotherUserAsync(1);

        stale.Title = "forced";
        (await documents.UpdateAsync(stale, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("版条件を外すので更新は成立する");

        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("forced", "last-write-wins で上書きされる");
        stale.RowVer.Should().NotEqual(staleVersion, "上書き後の新しい版が反映される");
    }

    // ── グラフ保存 ──

    /// <summary>4. 競合ノードを含むグラフ保存は SaveConflictException で全ロールバックする</summary>
    [Fact(DisplayName = "[Concurrency/EFCore] SaveAsync: 競合ノードがあると全ロールバックする")]
    public async Task SaveAsync_RollsBackEverything_OnConflict()
    {
        var documents = Documents();

        var root = await documents
            .Query()
            .Where(d => d.DocumentId == 1)
            .Include(d => d.DocumentNotes)
            .FirstOrDefaultAsync(Ct);
        root.Should().NotBeNull();
        root!.DocumentNotes.Should().HaveCount(1, "シードしたメモ 1 件が読める");

        await BumpByAnotherUserAsync(1);

        // 子（メモ）は競合していないが、親が競合するため全体が巻き戻る
        root.Title = "by-me";
        root.MarkUpdated();
        var note = root.DocumentNotes.First();
        note.Note = "note-by-me";
        note.MarkUpdated();

        var act = async () => await documents.SaveAsync(root, cancellationToken: Ct);

        await act.Should().ThrowAsync<SaveConflictException>().WithMessage("*modified*");

        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("by-another-user", "親は先勝ち");
        (await Notes().GetByIdAsync(100, Ct))!
            .Note.Should()
            .Be("first", "競合していない子の更新もロールバックされる（all-or-nothing）");
    }

    /// <summary>5. ForceOverwrite のグラフ保存は版を無視して成立する</summary>
    [Fact(DisplayName = "[Concurrency/EFCore] SaveAsync: ForceOverwrite は版を無視して保存する")]
    public async Task SaveAsync_ForceOverwrite_Succeeds()
    {
        var documents = Documents();

        var root = await documents.GetByIdAsync(1, Ct);
        await BumpByAnotherUserAsync(1);

        root!.Title = "forced";
        root.MarkUpdated();

        (
            await documents.SaveAsync(
                root,
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        )
            .Should()
            .Be(1);
        (await documents.GetByIdAsync(1, Ct))!.Title.Should().Be("forced");
    }

    /// <summary>
    /// 6. insertWhenUpdateMissing は「行なし→INSERT」「版が古い→競合」を区別する
    /// （区別しないと版違いが INSERT へ倒れて主キー重複に化ける＝本 WP の回帰テスト本体）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] SaveAsync: insertWhenUpdateMissing は行なしと版違いを区別する"
    )]
    public async Task SaveAsync_InsertWhenUpdateMissing_DistinguishesMissingRowFromStaleVersion()
    {
        var documents = Documents();

        // 行なし: INSERT へ切り替わる
        var missing = new DocumentEntity
        {
            DocumentId = 3,
            Title = "switched-to-insert",
            Thumb = [],
        };
        missing.MarkUpdated();

        (await documents.SaveAsync(missing, insertWhenUpdateMissing: true, cancellationToken: Ct))
            .Should()
            .Be(1, "行が無いので INSERT へ切り替わる");
        (await documents.GetByIdAsync(3, Ct)).Should().NotBeNull();

        // 版が古い: INSERT へ倒さず競合として弾く（倒すと主キー重複の DbUpdateException に化ける）
        var stale = await documents.GetByIdAsync(1, Ct);
        await BumpByAnotherUserAsync(1);
        stale!.Title = "by-me";
        stale.MarkUpdated();

        var act = async () =>
            await documents.SaveAsync(stale, insertWhenUpdateMissing: true, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage(
                "*modified by another user*",
                "版違いは INSERT へ倒さず競合として報告する（主キー重複に化けない）"
            );
    }

    /// <summary>7. 削除も版条件で守られ、ForceOverwrite なら通る</summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] SaveAsync: 削除も版条件で守られ ForceOverwrite なら通る"
    )]
    public async Task SaveAsync_Delete_IsGuardedByRowVersion()
    {
        var documents = Documents();

        // 子を先に消しておき、親単体の削除で版条件だけを見る
        await _fixture.ExecuteAsync("DELETE FROM document_notes", Ct);

        var stale = await documents.GetByIdAsync(1, Ct);
        await BumpByAnotherUserAsync(1);

        stale!.MarkRemoved();
        var act = async () => await documents.SaveAsync(stale, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage("*modified by another user*", "版の古い行は削除できない");
        (await documents.GetByIdAsync(1, Ct)).Should().NotBeNull("削除は行われない");

        (
            await documents.SaveAsync(
                stale,
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        )
            .Should()
            .BeGreaterThan(0);
        (await documents.GetByIdAsync(1, Ct)).Should().BeNull("版条件を外せば削除される");
    }

    /// <summary>
    /// 8. 列挙に無い値は入口で ArgumentOutOfRangeException（内部の 2 値分岐は「Optimistic なら競合として報告・
    /// さもなくば DB 側の値を取り込んで再試行」のため、検証しないと未定義値が黙って last-write-wins へ落ちる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] 未定義の ConcurrencyMode は ArgumentOutOfRangeException"
    )]
    public async Task UndefinedConcurrencyMode_IsRejected()
    {
        var documents = Documents();

        var stale = await documents.GetByIdAsync(1, Ct);
        await BumpByAnotherUserAsync(1);

        stale!.Title = "by-undefined";
        var update = async () => await documents.UpdateAsync(stale, (ConcurrencyMode)99, Ct);

        await update.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        stale.MarkUpdated();
        var save = async () =>
            await documents.SaveAsync(stale, mode: (ConcurrencyMode)99, cancellationToken: Ct);

        await save.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("mode");

        (await documents.GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("by-another-user", "未定義値の保存は 1 件も適用されない");
    }

    // ── Save フック × 版の反映タイミング ──

    /// <summary>
    /// 9. フックあり経路では、EF が SaveChanges で書いた新しい版をコミット後まで反映しない。
    /// After はコミット前に走るので保存前の版を見て、After 例外でロールバックしてもエンティティには
    /// 「DB に存在しない版」が残らない（残ると同一インスタンスの再保存が偽の競合になる）。
    /// </summary>
    [Fact(
        DisplayName = "[Concurrency/EFCore] SaveAsync: After は旧版を見る・例外でロールバックしても幻の版が残らない"
    )]
    public async Task SaveAsync_WithHook_KeepsRowVersionUntilCommit()
    {
        var hook = new RowVersionCapturingHook();
        var documents = DocumentsWithHook(hook);

        var document = await documents.GetByIdAsync(1, Ct);
        var beforeSave = document!.RowVer;
        beforeSave.Should().NotBeNull("SQL Server の rowversion は取得時点で読める");

        document.Title = "by-me";
        document.MarkUpdated();

        var act = async () => await documents.SaveAsync(document, cancellationToken: Ct);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*after-boom*");

        hook.SeenRowVersion.Should()
            .Equal(beforeSave, "After はコミット前に走るので保存前の版が見える");
        document
            .RowVer.Should()
            .Equal(beforeSave, "ロールバックされたので DB に存在しない版は残らない");
        (await Documents().GetByIdAsync(1, Ct))!
            .Title.Should()
            .Be("alpha", "行更新もロールバックされている");

        // 幻の版が残っていれば、同一インスタンスのこの再保存は偽の競合になる
        hook.ThrowOnAfter = false;
        (await documents.SaveAsync(document, cancellationToken: Ct)).Should().Be(1);

        document.RowVer.Should().NotEqual(beforeSave, "コミット成功後は新しい版が反映される");
        (await Documents().GetByIdAsync(1, Ct))!.Title.Should().Be("by-me");
    }

    /// <summary>After が見た版を記録し、任意で例外を投げる Save フック（版の反映タイミング検証用）</summary>
    private sealed class RowVersionCapturingHook : ISaveHook<DocumentEntity>
    {
        /// <summary>After で例外を投げるか（true の間は保存が丸ごとロールバックされる）</summary>
        public bool ThrowOnAfter { get; set; } = true;

        /// <summary>After が呼ばれた時点でエンティティが持っていた版</summary>
        public byte[]? SeenRowVersion { get; private set; }

        public Task AfterSaveAsync(
            DocumentEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        )
        {
            SeenRowVersion = entity.RowVer;

            if (ThrowOnAfter)
            {
                throw new InvalidOperationException("after-boom");
            }

            return Task.CompletedTask;
        }
    }
}
