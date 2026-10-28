using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using Xunit;
using GF = QuickER.Tests.GeneratedFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致テストの SQL Server の系統：行バージョンを持つ表（<c>ConcurrencyFixture</c>）で、同じ操作を
/// QuickER 版（SQL Server）と EF Core（SQL Server）へ流し、戻り値と保存先がそろうことを表明する（Docker 依存）。
/// </summary>
/// <remarks>
/// <para>
/// SQLite の系統（<see cref="BackendParityRuntimeTests"/>）では、行バージョンを採番しないため並行性トークンを使う経路
/// （ForceOverwrite のやり直し）に届かず、自動セーブポイントを切る MARS も無い。ここはその 2 つが要るケースだけを持つ。
/// </para>
/// <para>
/// 今わかっている違いは <see cref="Known"/> が名指しし、厳格に扱う（載っているケースが一致したら失敗＝直したら外す）。
/// </para>
/// </remarks>
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class BackendParitySqlServerRuntimeTests(SqlServerContainerFixture fixture)
    : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>今わかっている違い（範囲＝ケースの名前・<see cref="KnownMatch.KeyOnly"/>＝一致しないことだけを見る）</summary>
    /// <remarks>
    /// #33・#34・F7 は区切り 25 で直した（やり直しの上限は行ごと・分類の後、フックありの経路は試行ごとに
    /// トランザクションを張り直す）。
    /// </remarks>
    private static readonly IReadOnlyList<KnownEntry> Known = [];

    /// <summary>今わかっている違いの一覧（照合の規則は <see cref="KnownFailureSet"/> が持つ）</summary>
    private static readonly KnownFailureSet KnownSet = new("BackendParity.sqlserver", Known);

    private readonly SqlServerContainerFixture _fixture = fixture;

    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>ケースの名前（xunit の MemberData）</summary>
    public static TheoryData<string> Cases() =>
        new(
            "force-overwrite-9-rows",
            "force-overwrite-10-rows",
            "force-overwrite-10-rows-no-hooks",
            "mars-added-then-missing",
            "mars-deleted-then-missing",
            "no-mars-added-then-missing",
            "attempt-limit-then-row-gone-update",
            "attempt-limit-then-row-gone-graph-save",
            "move-stale-note-and-delete-old-gadget",
            "move-stale-note-and-delete-old-gadget-no-hooks"
        );

    [Theory(
        DisplayName = "[Parity/SqlServer] ケースの結果は、既知の違いを除いて QuickER 版と EF Core でそろう"
    )]
    [MemberData(nameof(Cases))]
    public async Task Case_MatchesBaselineExceptKnownDifferences(string name)
    {
        var baseline = await RunAsync(name, efCore: false);
        var efCore = await RunAsync(name, efCore: true);
        var report =
            $"QuickER:\n  {string.Join("\n  ", baseline)}\nEF Core:\n  {string.Join("\n  ", efCore)}";

        // 一致しなければ失敗 1 件（載っていれば既知・載っているのに一致すれば「起きなかった」で赤）
        EngineFailure[] observed = efCore.SequenceEqual(baseline, StringComparer.Ordinal)
            ? []
            : [new EngineFailure(name, "differs", report)];
        KnownSet
            .Classify(observed, scope => scope == name)
            .AssertClean($"[Parity/SqlServer] ケース {name}");
    }

    [Fact(DisplayName = "[Parity/SqlServer] 既知の違いは、存在するケースを名指しする")]
    public void Known_NamesExistingCases()
    {
        var cases = Cases().Select(row => (string)row.Data).ToHashSet();
        KnownSet.Entries.Select(entry => entry.Scope).Should().BeSubsetOf(cases);
    }

    /// <summary>
    /// EF Core のカスケードの一括削除は、ナビゲーションの条件に合う根のキーを読んでから消す。根が SQL Server の 1 文あたりの
    /// パラメータの上限（2,100）より多くても、DB の互換性レベルが 130 未満（OPENJSON が使えない）でも動く。
    /// </summary>
    /// <remarks>
    /// キーの集合を 1 つの条件に入れると、EF Core 10 は既定でキーを 1 つずつのパラメータへ展開する（上限を超えると失敗する）。
    /// 版や設定によっては 1 つのコレクションのパラメータ（OPENJSON）で送り、互換性レベル 130 未満の DB で失敗する。どちらにも
    /// 頼らないよう、キーは 500 件ずつのかたまりに分ける。互換性レベルを変えるため、専用の DB を作って流し、終わったら消す。
    /// </remarks>
    [Fact(
        DisplayName = "[Parity/SqlServer] EF Core のナビゲーションの条件のカスケード一括削除は、根がパラメータの上限より多くても、互換性レベル 120 の DB でも動く"
    )]
    public async Task EfCoreNavigationPredicateCascadeDelete_WorksOnCompatibilityLevel120()
    {
        const string database = "quicker_parity_compat120";
        await _fixture.ExecuteAsync(
            $"IF DB_ID(N'{database}') IS NOT NULL DROP DATABASE [{database}]; CREATE DATABASE [{database}]; ALTER DATABASE [{database}] SET COMPATIBILITY_LEVEL = 120;",
            Ct
        );

        try
        {
            var connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
                _fixture.ConnectionString
            )
            {
                InitialCatalog = database,
            }.ConnectionString;
            await using (
                var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString)
            )
            {
                await connection.OpenAsync(Ct);
                await using var command = connection.CreateCommand();
                command.CommandText = new QuickER.Provider.SqlServer.SqlServerDdlGenerator().Build(
                    ConcurrencyFixtureDefinition.Build()
                );
                await command.ExecuteNonQueryAsync(Ct);
            }

            // ConcurrencyFixture（gadgets → gadget_notes）の EF Core を使う。ほかのテストと同じオプションの組み合わせなので、
            // EF Core の内部サービスプロバイダを新しく作らない（フィクスチャごとに値オブジェクトの翻訳の拡張の型が違い、
            // 新しいフィクスチャ × プロバイダの組み合わせは内部プロバイダを 1 つずつ足す）
            await using var provider = new ServiceCollection()
                .AddGeneratedEfCoreRepositories(builder =>
                    builder.UseSqlServer(connectionString).IgnoreServiceProviderCacheLimit()
                )
                .BuildServiceProvider();
            var gadgets = provider.GetRequiredService<IGadgetRepository>();
            var notes = provider.GetRequiredService<IGadgetNoteRepository>();
            var ids = Enumerable.Range(1, 5000).ToList();
            await gadgets.BulkInsertAsync(ids.Select(id => NewGadget(id, $"g{id}")).ToList(), Ct);
            await notes.BulkInsertAsync(
                ids.Where(id => id % 2 == 0)
                    .Select(id => new GadgetNoteEntity
                    {
                        NoteId = NoteIdValue.Create(id),
                        GadgetId = GadgetIdValue.Create(id),
                        Note = NoteValue.Create("x"),
                    })
                    .ToList(),
                Ct
            );

            var rows = await gadgets
                .Query()
                .Where(g => g.GadgetNotes.Any())
                .ExecuteDeleteAsync(cascadeDelete: true, Ct);

            rows.Should().Be(5000, "根 2,500 件と子 2,500 件");
            (await gadgets.GetAllAsync(Ct)).Should().HaveCount(2500);
            (await notes.GetAllAsync(Ct)).Should().BeEmpty();
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            await _fixture.ExecuteAsync(
                $"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",
                CancellationToken.None
            );
        }
    }

    /// <summary>
    /// グラフ保存が削除した単一のカスケードの子（1 対 1）は、保存の後もナビゲーションに残り、RowState は Removed のまま。
    /// QuickER 版と EF Core で同じ（決定 2026-10-14＝コレクションの子は外すが、単一の子は外すコレクションが無いので残す）。
    /// </summary>
    /// <remarks>
    /// EF Core は削除したエントリを外すときの fix-up で、親のナビゲーションを null にする。生成コードはそれを戻す。
    /// 1 対 1 のカスケードを持ち EF Core もある図は GeneratedFixture（SQL Server）だけなので、ここで流す。
    /// </remarks>
    [Theory(
        DisplayName = "[Parity/SqlServer] 1 対 1 の削除した単一の子は、保存の後もナビゲーションに残る（QuickER 版と EF Core）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedSingleChild_StaysOnItsNavigation(bool efCore)
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(GF.GeneratedFixtureDefinition.Build(), Ct);
        var services = new ServiceCollection();

        if (efCore)
        {
            GF.GeneratedEfCoreRepositoryServiceCollectionExtensions.AddGeneratedEfCoreRepositories(
                services,
                builder =>
                    builder
                        .UseSqlServer(_fixture.ConnectionString)
                        .IgnoreServiceProviderCacheLimit()
            );
        }
        else
        {
            GF.GeneratedSqlServerRepositoryServiceCollectionExtensions.AddGeneratedSqlServerRepositories(
                services,
                _fixture.ConnectionString
            );
        }

        await using var provider = services.BuildServiceProvider();
        var customers = provider.GetRequiredService<GF.ICustomerRepository>();
        await customers.InsertAsync(
            new GF.CustomerEntity
            {
                CustomerId = GF.CustomerIdValue.Create(1),
                Name = GF.NameValue.Create("a"),
                IsActive = GF.IsActiveValue.Create(true),
            },
            Ct
        );
        await provider
            .GetRequiredService<GF.ICustomerProfileRepository>()
            .InsertAsync(
                new GF.CustomerProfileEntity
                {
                    ProfileId = GF.ProfileIdValue.Create(1),
                    CustomerId = GF.CustomerIdValue.Create(1),
                },
                Ct
            );
        var customerId = GF.CustomerIdValue.Create(1);
        var customer = (
            await customers
                .Query()
                .Where(c => c.CustomerId == customerId)
                .Include(c => c.CustomerProfile)
                .FirstOrDefaultAsync(Ct)
        )!;
        var profile = customer.CustomerProfile!;
        profile.MarkRemoved();

        var rows = await customers.SaveAsync(customer, cancellationToken: Ct);

        rows.Should().Be(1);
        customer.CustomerProfile.Should().BeSameAs(profile);
        profile.RowState.Should().Be(GF.RowState.Removed);
        (
            await provider
                .GetRequiredService<GF.ICustomerProfileRepository>()
                .GetByIdAsync(GF.ProfileIdValue.Create(1), Ct)
        )
            .Should()
            .BeNull();
    }

    /// <summary>
    /// 区切り 43（外1）：SQL Server でも、Count・Any は Take・Skip を取得と同じ意味で当て（OFFSET / FETCH の副問い合わせ）、
    /// Take・Skip つきの ExecuteDelete は NotSupportedException で断って何も消さない（QuickER 版と EF Core）
    /// </summary>
    [Theory(
        DisplayName = "[Parity/SqlServer] Take・Skip つきの Count・Any は取得の件数とそろい、ExecuteDelete は断る（QuickER 版と EF Core）"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PagedCountAnyAndDelete_FollowFetch(bool efCore)
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
        await using var provider = Build(efCore, withHooks: false);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();

        foreach (var id in new[] { 1, 2, 3 })
        {
            await gadgets.InsertAsync(NewGadget(id, $"g{id}"), Ct);
        }

        SqlQuery<GadgetEntity> Paged(int? skip, int? take)
        {
            var query = gadgets.Query().OrderBy(g => g.GadgetId);

            if (skip is { } s)
            {
                query = query.Skip(s);
            }

            if (take is { } t)
            {
                query = query.Take(t);
            }

            return query;
        }

        (await Paged(null, 1).CountAsync(Ct)).Should().Be(1);
        (await Paged(1, null).CountAsync(Ct)).Should().Be(2);
        (await Paged(1, 1).CountAsync(Ct)).Should().Be(1);
        (await Paged(3, null).CountAsync(Ct)).Should().Be(0);
        (await Paged(2, null).AnyAsync(Ct)).Should().BeTrue();
        (await Paged(3, null).AnyAsync(Ct)).Should().BeFalse();

        var delete = () => Paged(null, 1).ExecuteDeleteAsync(cancellationToken: Ct);
        await delete.Should().ThrowAsync<NotSupportedException>();
        (await StateAsync()).Should().HaveCount(3);
    }

    /// <summary>1 つのケースを空のスキーマから流し、戻り値と保存先を並べて返す</summary>
    private async Task<IReadOnlyList<string>> RunAsync(string name, bool efCore)
    {
        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);

        return name switch
        {
            "force-overwrite-9-rows" => await ForceOverwriteManyAsync(efCore, 9, withHooks: true),
            "force-overwrite-10-rows" => await ForceOverwriteManyAsync(efCore, 10, withHooks: true),
            "force-overwrite-10-rows-no-hooks" => await ForceOverwriteManyAsync(
                efCore,
                10,
                withHooks: false
            ),
            "mars-added-then-missing" => await FirstThenMissingAsync(
                efCore,
                mars: true,
                removeFirst: false
            ),
            "mars-deleted-then-missing" => await FirstThenMissingAsync(
                efCore,
                mars: true,
                removeFirst: true
            ),
            "no-mars-added-then-missing" => await FirstThenMissingAsync(
                efCore,
                mars: false,
                removeFirst: false
            ),
            "attempt-limit-then-row-gone-update" => await AttemptLimitThenGoneAsync(
                efCore,
                graphSave: false
            ),
            "attempt-limit-then-row-gone-graph-save" => await AttemptLimitThenGoneAsync(
                efCore,
                graphSave: true
            ),
            "move-stale-note-and-delete-old-gadget" => await MoveStaleNoteAsync(
                efCore,
                withHooks: true
            ),
            "move-stale-note-and-delete-old-gadget-no-hooks" => await MoveStaleNoteAsync(
                efCore,
                withHooks: false
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    /// <summary>
    /// #33（ForceOverwrite の形）：<paramref name="count"/> 行を読み、他者がすべての版を進めた後で、名前を変えた行の列を
    /// ForceOverwrite のグラフ保存 1 回で保存する（QuickER 版は全行を後勝ちで書く）
    /// </summary>
    private async Task<IReadOnlyList<string>> ForceOverwriteManyAsync(
        bool efCore,
        int count,
        bool withHooks
    )
    {
        await using var provider = Build(efCore, withHooks: withHooks);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var ids = Enumerable.Range(1, count).ToList();

        foreach (var id in ids)
        {
            await gadgets.InsertAsync(NewGadget(id, $"g{id}"), Ct);
        }

        var loaded = new List<GadgetEntity>();

        foreach (var id in ids)
        {
            loaded.Add((await gadgets.GetByIdAsync(GadgetIdValue.Create(id), Ct))!);
        }

        await _fixture.ExecuteAsync("UPDATE gadgets SET name = name + N'!'", Ct);

        foreach (var gadget in loaded)
        {
            gadget.Name = NameValue.Create($"e{gadget.GadgetId.Value}");
            gadget.MarkUpdated();
        }

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync(loaded, mode: ConcurrencyMode.ForceOverwrite, cancellationToken: Ct)
        );

        return [outcome, .. await StateAsync()];
    }

    /// <summary>
    /// #34（MARS の形）：先に流れる行（Added の注文 20、または読み込んで削除の印を付けた 20）と、他者が消した行 21
    /// （読み込み後に消され、Updated）を、Save フックありで insertWhenUpdateMissing のグラフ保存 1 回で保存する。
    /// MARS のときは EF Core が自動セーブポイントを切る
    /// </summary>
    private async Task<IReadOnlyList<string>> FirstThenMissingAsync(
        bool efCore,
        bool mars,
        bool removeFirst
    )
    {
        await using var provider = Build(efCore, withHooks: true, mars: mars);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();

        if (removeFirst)
        {
            await gadgets.InsertAsync(NewGadget(20, "first"), Ct);
        }

        await gadgets.InsertAsync(NewGadget(21, "second"), Ct);
        GadgetEntity first;

        if (removeFirst)
        {
            first = (await gadgets.GetByIdAsync(GadgetIdValue.Create(20), Ct))!;
            first.MarkRemoved();
        }
        else
        {
            first = NewGadget(20, "added");
            first.MarkAdded();
        }

        var second = (await gadgets.GetByIdAsync(GadgetIdValue.Create(21), Ct))!;
        await _fixture.ExecuteAsync("DELETE FROM gadgets WHERE gadget_id = 21", Ct);
        second.Name = NameValue.Create("back");
        second.MarkUpdated();

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync([first, second], insertWhenUpdateMissing: true, cancellationToken: Ct)
        );

        return [outcome, .. await StateAsync()];
    }

    /// <summary>
    /// F7：ForceOverwrite の更新が 9 回続けて当たらず、10 回目の前に行が消える。契約は「行なし」（UpdateAsync は false・
    /// グラフ保存は NotFound）。QuickER 版はやり直さないので、最初から行が消えている同じ呼び出しを基準にする
    /// </summary>
    private async Task<IReadOnlyList<string>> AttemptLimitThenGoneAsync(bool efCore, bool graphSave)
    {
        var interceptor = MissThenDelete.Instance;
        interceptor.Arm(_fixture, misses: 9);
        await using var provider = Build(
            efCore,
            withHooks: false,
            interceptor: efCore ? interceptor : null
        );
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        var gadget = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        gadget.Name = NameValue.Create("edited");
        gadget.MarkUpdated();

        if (!efCore)
        {
            await _fixture.ExecuteAsync("DELETE FROM gadgets WHERE gadget_id = 1", Ct);
        }

        var outcome = graphSave
            ? await OutcomeAsync(() =>
                gadgets.SaveAsync(
                    gadget,
                    mode: ConcurrencyMode.ForceOverwrite,
                    cancellationToken: Ct
                )
            )
            : await OutcomeAsync(async () =>
                await gadgets.UpdateAsync(gadget, ConcurrencyMode.ForceOverwrite, Ct) ? 1 : 0
            );

        return [outcome, .. await StateAsync()];
    }

    /// <summary>
    /// 区切り 43（判断 D・E）：メモ 10 を gadget 1 から 2 へ付け替え、gadget 1 を消す（gadget → メモは CASCADE）。メモは読んだ後に
    /// 他者が更新したので版が古く、ForceOverwrite の保存は EF Core では 1 回当たらずにやり直す。やり直しの後も付け替えを
    /// 削除より先に書く（QuickER 版は版を照合せず後勝ちで書く）
    /// </summary>
    private async Task<IReadOnlyList<string>> MoveStaleNoteAsync(bool efCore, bool withHooks)
    {
        await using var provider = Build(efCore, withHooks: withHooks);
        var gadgets = provider.GetRequiredService<IGadgetRepository>();
        var notes = provider.GetRequiredService<IGadgetNoteRepository>();
        await gadgets.InsertAsync(NewGadget(1, "g1"), Ct);
        await gadgets.InsertAsync(NewGadget(2, "g2"), Ct);
        await notes.InsertAsync(
            new GadgetNoteEntity
            {
                NoteId = NoteIdValue.Create(10),
                GadgetId = GadgetIdValue.Create(1),
                Note = NoteValue.Create("n"),
            },
            Ct
        );

        var oldParent = (await gadgets.GetByIdAsync(GadgetIdValue.Create(1), Ct))!;
        var newParent = (await gadgets.GetByIdAsync(GadgetIdValue.Create(2), Ct))!;
        var note = (await notes.GetByIdAsync(NoteIdValue.Create(10), Ct))!;
        await _fixture.ExecuteAsync(
            "UPDATE gadget_notes SET note = N'other' WHERE note_id = 10",
            Ct
        );

        note.GadgetId = GadgetIdValue.Create(2);
        note.Note = NoteValue.Create("moved");
        note.MarkUpdated();
        newParent.GadgetNotes.Add(note);
        oldParent.MarkRemoved();

        var outcome = await OutcomeAsync(() =>
            gadgets.SaveAsync(
                [oldParent, newParent],
                mode: ConcurrencyMode.ForceOverwrite,
                cancellationToken: Ct
            )
        );

        return [outcome, .. await StateAsync(), .. await NotesStateAsync()];
    }

    /// <summary>保存先のメモの全行（キー順・版は比べない）</summary>
    private async Task<IReadOnlyList<string>> NotesStateAsync()
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var all = await provider.GetRequiredService<IGadgetNoteRepository>().GetAllAsync(Ct);

        return
        [
            .. all.OrderBy(n => n.NoteId.Value)
                .Select(n =>
                    $"gadget_notes/{n.NoteId.Value} gadget={n.GadgetId.Value} note={n.Note.Value}"
                ),
        ];
    }

    /// <summary>実装先のリポジトリを組み立てる</summary>
    private ServiceProvider Build(
        bool efCore,
        bool withHooks,
        bool mars = false,
        IInterceptor? interceptor = null
    )
    {
        var connectionString = mars
            ? _fixture.ConnectionString + ";MultipleActiveResultSets=True"
            : _fixture.ConnectionString;
        var services = efCore
            ? new ServiceCollection().AddGeneratedEfCoreRepositories(builder =>
            {
                if (interceptor is not null)
                {
                    builder.UseSqlServerWith(connectionString, interceptor);
                }
                else
                {
                    builder.UseSqlServer(connectionString).IgnoreServiceProviderCacheLimit();
                }
            })
            : new ServiceCollection().AddGeneratedSqlServerRepositories(connectionString);

        if (withHooks)
        {
            services.AddSaveHook(new NoOpHook());
        }

        return services.BuildServiceProvider();
    }

    /// <summary>呼び出しの戻り値（または例外の型と競合の理由）</summary>
    private static async Task<string> OutcomeAsync(Func<Task<int>> call)
    {
        try
        {
            return $"rows={await call()}";
        }
        catch (SaveConflictException ex)
        {
            return $"throws SaveConflictException reason={ex.Reason}";
        }
        catch (Exception ex)
        {
            return $"throws {ex.GetType().Name}";
        }
    }

    /// <summary>保存先の全行（キー順・版は比べない）</summary>
    private async Task<IReadOnlyList<string>> StateAsync()
    {
        await using var provider = new ServiceCollection()
            .AddGeneratedSqlServerRepositories(_fixture.ConnectionString)
            .BuildServiceProvider();
        var all = await provider.GetRequiredService<IGadgetRepository>().GetAllAsync(Ct);

        return
        [
            .. all.OrderBy(g => g.GadgetId.Value)
                .Select(g => $"gadgets/{g.GadgetId.Value} name={g.Name.Value}"),
        ];
    }

    private static GadgetEntity NewGadget(int id, string name) =>
        new() { GadgetId = GadgetIdValue.Create(id), Name = NameValue.Create(name) };

    /// <summary>何もしない Save フック（EF Core の保存をフックありの経路へ回す）</summary>
    private sealed class NoOpHook : ISaveHook<GadgetEntity>, ISaveHook<GadgetNoteEntity>;

    /// <summary>gadgets の UPDATE を最初の決めた回数だけ当たらなくし、その次の UPDATE の前に行を消す</summary>
    /// <remarks>
    /// インスタンスは 1 つを使い回し、使う前に <see cref="Arm"/> で状態を入れ直す。EF Core はインターセプターのインスタンスが
    /// 違うと内部のサービスプロバイダを別に作り、プロセス全体で 20 を超えると、たまたま 21 個目を作った DbContext が
    /// ManyServiceProvidersCreatedWarning で失敗する（ほかのテストを巻き込む）。このクラスのケースは SQL Server のコレクションで
    /// 1 つずつ流れるので、状態を共有しても重ならない。
    /// </remarks>
    private sealed class MissThenDelete : DbCommandInterceptor
    {
        public static readonly MissThenDelete Instance = new();

        private SqlServerContainerFixture? _fixture;
        private int _misses;
        private int _updates;

        /// <summary>行を消す先と、当たらなくする回数を入れ、数え直す</summary>
        public void Arm(SqlServerContainerFixture fixture, int misses)
        {
            _fixture = fixture;
            _misses = misses;
            _updates = 0;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.Contains("UPDATE [gadgets]", StringComparison.Ordinal))
            {
                var count = Interlocked.Increment(ref _updates);

                if (count <= _misses)
                {
                    command.CommandText = command.CommandText.Replace(
                        "WHERE [gadget_id] =",
                        "WHERE 1 = 0 AND [gadget_id] =",
                        StringComparison.Ordinal
                    );
                }
                else if (count == _misses + 1)
                {
                    await _fixture!.ExecuteAsync(
                        "DELETE FROM gadgets WHERE gadget_id = 1",
                        cancellationToken
                    );
                }
            }

            return result;
        }
    }
}
