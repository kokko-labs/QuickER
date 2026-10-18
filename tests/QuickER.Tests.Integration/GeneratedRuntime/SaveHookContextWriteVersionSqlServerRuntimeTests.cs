using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedConcurrencyFixture;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// グラフ保存の After フックが SQL（<c>ExecuteSqlAsync</c>）で保存した行を書いたとき、版を読み直さず、同じインスタンスの次の
/// 保存が競合になることを、実 SQL Server（ConcurrencyFixture＝親子とも rowversion 列を持つ）の QuickER 版 Repository と
/// EF Core 版で検証する。
/// </summary>
/// <remarks>
/// <para>
/// SQL はどの列でも書けるので、版だけを読み直すと、フックが書いた列を次の保存が古い値で黙って上書きする。版を読み直す
/// のは無制限バイナリ列の書き込み（UPDATE が触らない列）のときだけで、その形は
/// <see cref="SaveHookBinaryWriteSqlServerRuntimeTests"/> が見る。SQL の書き込みでは読み直しの SELECT も出さない。
/// </para>
/// <para>
/// コマンドの観測は SqlClient の DiagnosticListener で行う（プロセス全体に効くので、このフィクスチャの表名を含む
/// コマンドだけを見る）。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(SqlServerContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class SaveHookContextWriteVersionSqlServerRuntimeTests(
    SqlServerContainerFixture fixture
) : IAsyncLifetime
{
    private const int GadgetId = 1;

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly SqlServerContainerFixture _fixture = fixture;
    private readonly List<ServiceProvider> _providers = [];

    public async ValueTask InitializeAsync()
    {
        Assert.SkipUnless(_fixture.IsAvailable, _fixture.UnavailableReason);

        await _fixture.ResetSchemaAsync(Ct);
        await _fixture.ApplyDdlAsync(ConcurrencyFixtureDefinition.Build(), Ct);
        await _fixture.ExecuteAsync(
            "INSERT INTO gadgets (gadget_id, name) VALUES (1, N'alpha');"
                + "INSERT INTO gadget_notes (note_id, gadget_id, note) VALUES (10, 1, N'first');",
            Ct
        );
    }

    public ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>フックを登録した QuickER 版（efCore=false）か EF Core 版（true）の gadget リポジトリを返す</summary>
    private IGadgetRepository Gadgets(bool efCore, params object[] hooks)
    {
        var services = new ServiceCollection();

        if (efCore)
        {
            services.AddGeneratedEfCoreRepositories(options =>
                options.UseSqlServer(_fixture.ConnectionString).IgnoreServiceProviderCacheLimit()
            );
        }
        else
        {
            services.AddGeneratedSqlServerRepositories(_fixture.ConnectionString);
        }

        foreach (var hook in hooks)
        {
            services.AddSaveHook(hook);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<IGadgetRepository>();
    }

    private async Task<GadgetEntity> LoadWithNotesAsync(IGadgetRepository repository)
    {
        var key = GadgetIdValue.Create(GadgetId);
        return (
            await repository
                .Query()
                .Where(g => g.GadgetId == key)
                .Include(g => g.GadgetNotes)
                .ToListAsync(Ct)
        ).Single();
    }

    private async Task<GadgetEntity> StoredGadgetAsync() =>
        (await Gadgets(efCore: false).GetByIdAsync(GadgetIdValue.Create(GadgetId), Ct))!;

    [Theory(
        DisplayName = "[SaveHook/版] After フックが SQL で同じ行を書くと、同じインスタンスの次の保存は競合になり、フックの書いた行を上書きしない"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HookWritesSameRowBySql_NextSaveConflicts(bool efCore)
    {
        var repository = Gadgets(efCore, new StampGadgetOnAfter());
        var gadget = (await repository.GetByIdAsync(GadgetIdValue.Create(GadgetId), Ct))!;

        gadget.Name = NameValue.Create("first");
        gadget.MarkUpdated();
        await repository.SaveAsync(gadget, cancellationToken: Ct);

        var stored = await StoredGadgetAsync();
        stored.Name.Value.Should().Be("stamped", "フックが SQL で書いた値");
        gadget
            .RowVer.Value.Should()
            .NotEqual(stored.RowVer.Value, "SQL の書き込みの後は版を読み直さない");

        gadget.Name = NameValue.Create("second");
        gadget.MarkUpdated();
        var again = () => repository.SaveAsync(gadget, cancellationToken: Ct);

        (await again.Should().ThrowAsync<SaveConflictException>())
            .Which.Reason.Should()
            .Be(SaveConflictReason.Modified);
        (await StoredGadgetAsync()).Name.Value.Should().Be("stamped", "黙って上書きしない");
    }

    [Theory(
        DisplayName = "[SaveHook/版] 子の After フックが同じ保存の親の行を SQL で書くと、親の次の保存は競合になる"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildHookWritesSavedParentBySql_NextParentSaveConflicts(bool efCore)
    {
        var repository = Gadgets(efCore, new StampParentOnAfter());
        var gadget = await LoadWithNotesAsync(repository);
        var note = gadget.GadgetNotes.Single();

        gadget.Name = NameValue.Create("first");
        gadget.MarkUpdated();
        note.Note = NoteValue.Create("second");
        note.MarkUpdated();
        await repository.SaveAsync(gadget, cancellationToken: Ct);

        gadget.Name = NameValue.Create("third");
        gadget.MarkUpdated();
        var again = () => repository.SaveAsync(gadget, cascadeSave: false, cancellationToken: Ct);

        (await again.Should().ThrowAsync<SaveConflictException>())
            .Which.Reason.Should()
            .Be(SaveConflictReason.Modified);
        (await StoredGadgetAsync()).Name.Value.Should().Be("stamped");
    }

    [Theory(
        DisplayName = "[SaveHook/版] フックが context で何も書かなかった保存は、版を読み直す SELECT を出さない"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HookWithoutWrites_DoesNotRereadVersions(bool efCore) =>
        await AssertNoSelectDuringSaveAsync(efCore, new NoWriteOnAfter());

    [Theory(
        DisplayName = "[SaveHook/版] フックが SQL で別の表へ書く保存（監査ログの形）も、版を読み直す SELECT を出さない"
    )]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HookWritingAuditRowBySql_DoesNotRereadVersions(bool efCore) =>
        await AssertNoSelectDuringSaveAsync(efCore, new AuditNoteOnAfter());

    private async Task AssertNoSelectDuringSaveAsync(bool efCore, object hook)
    {
        var repository = Gadgets(efCore, hook);
        var gadget = (await repository.GetByIdAsync(GadgetIdValue.Create(GadgetId), Ct))!;

        using var capture = new SqlCommandObserver("gadget");
        gadget.Name = NameValue.Create("first");
        gadget.MarkUpdated();
        await repository.SaveAsync(gadget, cancellationToken: Ct);

        capture
            .Commands.Where(text =>
                text.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            )
            .Should()
            .BeEmpty("版を読み直すのは無制限バイナリ列を書いたときだけ");
        gadget.RowVer.Value.Should().Equal((await StoredGadgetAsync()).RowVer.Value);
    }

    /// <summary>gadget の After で、同じ行の名前を SQL で書き換える</summary>
    private sealed class StampGadgetOnAfter : ISaveHook<GadgetEntity>
    {
        public Task AfterSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.ExecuteSqlAsync(
                "UPDATE gadgets SET name = N'stamped' WHERE gadget_id = @id",
                new { id = entity.GadgetId.Value },
                cancellationToken
            );
    }

    /// <summary>メモの After で、同じ保存の親の gadget の名前を SQL で書き換える</summary>
    private sealed class StampParentOnAfter : ISaveHook<GadgetNoteEntity>
    {
        public Task AfterSaveAsync(
            GadgetNoteEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.ExecuteSqlAsync(
                "UPDATE gadgets SET name = N'stamped' WHERE gadget_id = @id",
                new { id = entity.GadgetId.Value },
                cancellationToken
            );
    }

    /// <summary>gadget の After で、別の表へ監査の行を SQL で足す（保存した行には触れない）</summary>
    private sealed class AuditNoteOnAfter : ISaveHook<GadgetEntity>
    {
        public Task AfterSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.ExecuteSqlAsync(
                "INSERT INTO gadget_notes (note_id, gadget_id, note) VALUES (99, @id, N'audit')",
                new { id = entity.GadgetId.Value },
                cancellationToken
            );
    }

    /// <summary>After を持つが context で何も書かない</summary>
    private sealed class NoWriteOnAfter : ISaveHook<GadgetEntity>
    {
        public Task AfterSaveAsync(
            GadgetEntity entity,
            SaveOperation operation,
            ISaveHookContext context,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}

/// <summary>
/// SqlClient が発行するコマンドの本文を DiagnosticListener で捕まえる（指定した語を含むものだけ）。終わったコマンドごとに
/// 呼ぶ処理も渡せる。
/// </summary>
internal sealed class SqlCommandObserver
    : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>,
        IDisposable
{
    private readonly string _marker;
    private readonly Action<string>? _afterCommand;
    private readonly List<string> _commands = [];
    private readonly List<IDisposable> _subscriptions = [];

    public SqlCommandObserver(string marker, Action<string>? afterCommand = null)
    {
        _marker = marker;
        _afterCommand = afterCommand;
        _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
    }

    /// <summary>捕まえたコマンドの本文</summary>
    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_commands)
            {
                return _commands.ToList();
            }
        }
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == "SqlClientDiagnosticListener")
        {
            lock (_subscriptions)
            {
                _subscriptions.Add(value.Subscribe(this));
            }
        }
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        var before = value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal);
        var after = value.Key.EndsWith("WriteCommandAfter", StringComparison.Ordinal);

        if (!before && !after)
        {
            return;
        }

        if (
            value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value)
                is not System.Data.Common.DbCommand { CommandText: { } text }
            || !text.Contains(_marker, StringComparison.Ordinal)
        )
        {
            return;
        }

        if (before)
        {
            lock (_commands)
            {
                _commands.Add(text);
            }
        }
        else
        {
            _afterCommand?.Invoke(text);
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose()
    {
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
