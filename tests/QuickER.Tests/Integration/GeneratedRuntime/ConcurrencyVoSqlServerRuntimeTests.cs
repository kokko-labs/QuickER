using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// rowversion 列 <b>× 値オブジェクト</b>の楽観排他を、QuickER 版 Repository（SQL Server 方言）で
/// 実 SQL Server（Testcontainers・Docker 依存）に流して検証する。
/// </summary>
/// <remarks>
/// <para>
/// VO 有効時の rowversion プロパティは <c>RowVerValue</c> になる。DB が <c>OUTPUT INSERTED</c> で返すのは生の
/// <c>byte[]</c> なので、書き戻しの <c>PropertyInfo.SetValue</c> は VO へ包み直さなければ <c>ArgumentException</c> に
/// なる（実バグ＝UPDATE はコミット済みなのに保存が例外・手元の版は古いまま＝次回保存が偽の競合）。
/// 本テストは Insert / Update / SaveAsync の 3 経路と、版ガード（競合・ForceOverwrite）を実 DB で固定する。
/// </para>
/// <para>
/// 「他者による更新」は生 SQL（<c>ExecuteSqlAsync</c>）で直接 UPDATE して作る＝Repository を経由しないため
/// 手元のエンティティの <c>RowVer</c> は古いまま残り、実際の競合と同じ状態を決定的に再現できる。
/// Docker 不在時は <see cref="SqlServerContainerFixture"/> の検出でスキップされる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class ConcurrencyVoSqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>QuickER の SQL Server リポジトリ群を登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>スキーマを作成し、gadget 1 件（メモ 1 件つき）をシードする</summary>
    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();

        await Gadgets()
            .InsertAsync(
                new GadgetEntity
                {
                    GadgetId = GadgetIdValue.Create(1),
                    Name = NameValue.Create("alpha"),
                },
                Ct
            );
        await Notes()
            .InsertAsync(
                new GadgetNoteEntity
                {
                    NoteId = NoteIdValue.Create(1),
                    GadgetId = GadgetIdValue.Create(1),
                    Note = NoteValue.Create("first"),
                },
                Ct
            );
    }

    /// <summary>DI コンテナを破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>gadget リポジトリを解決する</summary>
    private IGadgetRepository Gadgets() => _provider.GetRequiredService<IGadgetRepository>();

    /// <summary>メモリポジトリを解決する</summary>
    private IGadgetNoteRepository Notes() => _provider.GetRequiredService<IGadgetNoteRepository>();

    /// <summary>Repository を経由せず生 SQL で直接 1 行更新して「他者による更新」を作る</summary>
    private Task BumpByAnotherUserAsync(int gadgetId) =>
        Gadgets()
            .ExecuteSqlAsync(
                "UPDATE gadgets SET name = 'by-another-user' WHERE gadget_id = @id",
                new { id = gadgetId },
                Ct
            );

    /// <summary>DB 上の現在の名前を読む（先勝ち／上書きの確認用）</summary>
    private Task<string?> ReadNameAsync(int gadgetId) =>
        Gadgets()
            .ExecuteScalarSqlAsync<string>(
                "SELECT name FROM gadgets WHERE gadget_id = @id",
                new { id = gadgetId },
                Ct
            );

    // ── 版の書き戻し（本バグの回帰本体） ──

    /// <summary>1. Insert / Update / SaveAsync の 3 経路とも、DB 採番の版が VO 型で同一インスタンスへ書き戻る</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/SqlServer] 版反映: Insert / Update / Save が VO 型の版を書き戻す"
    )]
    public async Task RowVersion_IsWrittenBack_AsValueObject()
    {
        var gadgets = Gadgets();

        var entity = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(2),
            Name = NameValue.Create("inserted"),
        };

        // 旧実装はここで ArgumentException（Byte[] を RowVerValue へ SetValue できない）になっていた
        await gadgets.InsertAsync(entity, Ct);

        entity.RowVer.Should().NotBeNull("OUTPUT INSERTED で採番された版が VO として入る");
        entity.RowVer.Value.Length.Should().Be(8, "SQL Server の rowversion は 8 バイト");
        var afterInsert = entity.RowVer;

        entity.Name = NameValue.Create("updated");
        (await gadgets.UpdateAsync(entity, cancellationToken: Ct)).Should().BeTrue();
        entity.RowVer.Should().NotBe(afterInsert, "UPDATE のたびに版が進む");
        var afterUpdate = entity.RowVer;

        entity.Name = NameValue.Create("saved");
        entity.MarkUpdated();
        (await gadgets.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        entity.RowVer.Should().NotBe(afterUpdate, "グラフ保存でもコミット後に新版が反映される");

        // 反映された版がそのまま次の更新に使える（再読込せずに更新が通る＝書き戻しが正しい証明）
        entity.Name = NameValue.Create("again");
        (await gadgets.UpdateAsync(entity, cancellationToken: Ct))
            .Should()
            .BeTrue("書き戻された版は DB の現在値と一致する");
    }

    /// <summary>2. グラフ保存では子（カスケード）の版も VO 型で書き戻り、続けて保存できる</summary>
    [Fact(DisplayName = "[Concurrency/VO/SqlServer] 版反映: グラフ保存は子の版も書き戻す")]
    public async Task SaveAsync_WritesBackRowVersion_ForCascadedChildren()
    {
        var gadgets = Gadgets();

        var gadget = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(3),
            Name = NameValue.Create("parent"),
        };
        gadget.MarkAdded();

        var note = new GadgetNoteEntity
        {
            NoteId = NoteIdValue.Create(30),
            GadgetId = GadgetIdValue.Create(3),
            Note = NoteValue.Create("child"),
        };
        note.MarkAdded();
        gadget.GadgetNotes.Add(note);

        (await gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "親 1 件＋子 1 件が保存される");

        gadget.RowVer.Should().NotBeNull("親の版が書き戻る");
        note.RowVer.Should().NotBeNull("子の版も書き戻る");

        // 2 回目: 再読込せずそのまま保存できる＝親子とも版が DB の現在値と一致している
        gadget.Name = NameValue.Create("parent-2");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("child-2");
        note.MarkUpdated();

        (await gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "書き戻された版で版チェックが通る");
    }

    // ── 版ガード ──

    /// <summary>3. 版が古い更新は SaveConflictException（DB は先勝ちのまま）</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/SqlServer] UpdateAsync: 版が古いと SaveConflictException・DB は先勝ちのまま"
    )]
    public async Task UpdateAsync_Throws_WhenRowVersionIsStale()
    {
        var gadgets = Gadgets();

        var stale = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        stale.Should().NotBeNull();
        stale!.RowVer.Should().NotBeNull("取得時点で版が VO として読める");

        await BumpByAnotherUserAsync(1);

        stale.Name = NameValue.Create("by-me");
        var act = async () => await gadgets.UpdateAsync(stale, cancellationToken: Ct);

        await act.Should()
            .ThrowAsync<SaveConflictException>()
            .WithMessage(
                "*modified by another user*",
                "VO の版でも WHERE 条件に載り競合が検出される"
            );

        (await ReadNameAsync(1)).Should().Be("by-another-user", "競合した更新は適用されない");
    }

    /// <summary>4. ForceOverwrite は版条件を外して上書きし、新しい版を VO で書き戻す</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/SqlServer] UpdateAsync: ForceOverwrite は版を無視して上書きし新版を反映する"
    )]
    public async Task UpdateAsync_ForceOverwrite_OverwritesAndRefreshesRowVersion()
    {
        var gadgets = Gadgets();

        var stale = await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct);
        var staleVersion = stale!.RowVer;

        await BumpByAnotherUserAsync(1);

        stale.Name = NameValue.Create("forced");
        (await gadgets.UpdateAsync(stale, ConcurrencyMode.ForceOverwrite, Ct))
            .Should()
            .BeTrue("版条件を外すので更新は成立する");

        (await ReadNameAsync(1)).Should().Be("forced", "last-write-wins で上書きされる");
        stale.RowVer.Should().NotBe(staleVersion, "上書き後の新しい版が反映される");
    }

    /// <summary>5. 競合ノードを含むグラフ保存は SaveConflictException で全ロールバックする</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/SqlServer] SaveAsync: 競合ノードがあると全ロールバックする"
    )]
    public async Task SaveAsync_RollsBackEverything_OnConflict()
    {
        var gadgets = Gadgets();

        var key = GadgetIdValue.Create(1);
        var root = await gadgets
            .Query()
            .Where(g => g.GadgetId == key)
            .Include(g => g.GadgetNotes)
            .FirstOrDefaultAsync(Ct);
        root.Should().NotBeNull();
        root!.GadgetNotes.Should().HaveCount(1, "シードしたメモ 1 件が読める");

        await BumpByAnotherUserAsync(1);

        // 子（メモ）は競合していないが、親が競合するため全体が巻き戻る
        root.Name = NameValue.Create("by-me");
        root.MarkUpdated();
        var note = root.GadgetNotes.First();
        note.Note = NoteValue.Create("note-by-me");
        note.MarkUpdated();

        var act = async () => await gadgets.SaveAsync(root, cancellationToken: Ct);

        await act.Should().ThrowAsync<SaveConflictException>();

        (await ReadNameAsync(1)).Should().Be("by-another-user", "親は先勝ちのまま");
        (await Notes().GetByIdAsync(NoteIdValue.Create(1), Ct))!
            .Note.Value.Should()
            .Be("first", "競合していない子の更新もロールバックされる（all-or-nothing）");
    }
}
