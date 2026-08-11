using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedConcurrencyFixture;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// rowversion 列 <b>× 値オブジェクト</b>の楽観排他を、<b>EF Core 版 Repository</b>で実 SQL Server
/// （Testcontainers・Docker 依存）に流して検証する。
/// </summary>
/// <remarks>
/// <para>
/// Fluent 構成が <c>.IsRowVersion().HasConversion(v =&gt; v!.Value, v =&gt; RowVerValue.Create(v!))</c> と
/// 併記される唯一の構成（既存の <c>EfCoreConcurrencyRuntimeTests</c> は VO なしの素の <c>byte[]</c>）。
/// 「並行性トークン × 値コンバータ」が実 DB で成立すること＝EF Core が DB 採番の版を VO へ復元し、
/// 版比較（<c>WHERE row_ver = @original</c> 相当）が効くことを実証する。
/// </para>
/// <para>
/// EF Core はリフレクション代入を通らない（変更追跡が型付きで行う）ため、本テストは書き戻し不具合そのものの
/// 回帰ではなく「VO × rowversion で EF Core の並行性機構が壊れていない」ことの担保。
/// 「他者による更新」は生 SQL で直接 UPDATE して作る。Docker 不在時はスキップされる。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class ConcurrencyVoEfCoreRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    /// <summary>共有する SQL Server コンテナ</summary>
    private readonly SqlServerContainerFixture _fixture = fixture;

    /// <summary>EF Core 版リポジトリ群を登録した DI コンテナ（UseSqlServer）</summary>
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
            .AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString)
            )
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

    /// <summary>EF Core を経由せず生 SQL で直接 1 行更新して「他者による更新」を作る</summary>
    private Task BumpByAnotherUserAsync(int gadgetId) =>
        _fixture.ExecuteAsync(
            $"UPDATE gadgets SET name = 'by-another-user' WHERE gadget_id = {gadgetId}",
            Ct
        );

    // ── 版の復元・反映 ──

    /// <summary>1. DB 採番の版が VO 型として読め、Insert / Update / Save のたびに進む</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/EFCore] 版反映: Insert / Update / Save で VO 型の版が進む"
    )]
    public async Task RowVersion_IsMaterializedAsValueObject_AndAdvances()
    {
        var gadgets = Gadgets();

        var entity = new GadgetEntity
        {
            GadgetId = GadgetIdValue.Create(2),
            Name = NameValue.Create("inserted"),
        };
        await gadgets.InsertAsync(entity, Ct);

        entity.RowVer.Should().NotBeNull("DB 採番の版が VO へ復元される（値コンバータ経由）");
        entity.RowVer.Value.Length.Should().Be(8, "SQL Server の rowversion は 8 バイト");
        var afterInsert = entity.RowVer;

        entity.Name = NameValue.Create("updated");
        (await gadgets.UpdateAsync(entity, cancellationToken: Ct)).Should().BeTrue();
        entity.RowVer.Should().NotBe(afterInsert, "UPDATE のたびに版が進む");
        var afterUpdate = entity.RowVer;

        entity.Name = NameValue.Create("saved");
        entity.MarkUpdated();
        (await gadgets.SaveAsync(entity, cancellationToken: Ct)).Should().Be(1);
        entity.RowVer.Should().NotBe(afterUpdate, "グラフ保存でも新版が反映される");

        entity.Name = NameValue.Create("again");
        (await gadgets.UpdateAsync(entity, cancellationToken: Ct))
            .Should()
            .BeTrue("反映された版は DB の現在値と一致するので再読込なしで更新できる");
    }

    // ── 版ガード ──

    /// <summary>2. 版が古い更新は SaveConflictException（DB は先勝ちのまま）</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/EFCore] UpdateAsync: 版が古いと SaveConflictException・DB は先勝ちのまま"
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
                "値コンバータ越しでも並行性トークンの比較が効く"
            );

        (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("by-another-user", "競合した更新は適用されない（先勝ち）");
    }

    /// <summary>3. ForceOverwrite は版条件を外して上書きし、新しい版を反映する</summary>
    [Fact(
        DisplayName = "[Concurrency/VO/EFCore] UpdateAsync: ForceOverwrite は版を無視して上書きし新版を反映する"
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

        (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!
            .Name.Value.Should()
            .Be("forced", "last-write-wins で上書きされる");
        stale.RowVer.Should().NotBe(staleVersion, "上書き後の新しい版が反映される");
    }

    /// <summary>4. グラフ保存は親子（ともに rowversion 列あり）の版を反映し、続けて保存できる</summary>
    [Fact(DisplayName = "[Concurrency/VO/EFCore] SaveAsync: 親子の版が反映され続けて保存できる")]
    public async Task SaveAsync_RefreshesRowVersion_ForCascadedChildren()
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

        gadget.RowVer.Should().NotBeNull("親の版が反映される");
        note.RowVer.Should().NotBeNull("子の版も反映される");

        gadget.Name = NameValue.Create("parent-2");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("child-2");
        note.MarkUpdated();

        (await gadgets.SaveAsync(gadget, cancellationToken: Ct))
            .Should()
            .Be(2, "反映された版で版チェックが通る");
    }
}
