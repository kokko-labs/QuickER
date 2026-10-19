using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// ジャーナル記録デコレータの GetById・Delete も、null のキーを ArgumentNullException（ParamName = "id"）で断り、ジャーナルへ
/// 何も記録しないことを検証する（SyncModelFixture の文字列キーの model_labels）。
/// </summary>
public sealed class SyncNullKeyRuntimeTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    public void Dispose() => _db.Dispose();

    [Fact(
        DisplayName = "[Sync/キー] ジャーナル記録デコレータの GetById・Delete は null のキーを ArgumentNullException(id) で断り、何も記録しない"
    )]
    public async Task NullKey_IsRefusedWithoutRecording()
    {
        await _db.ApplyDdlAsync(SyncModelFixtureDefinition.BuildSqliteMirror(), Ct);
        var factory = new LightJournalSyncModelConnectionFactory(
            _db.ReadWriteCreateConnectionString
        );
        var journal = new SyncJournal(new SqlExecutor(factory));
        await journal.EnsureCreatedAsync(Ct);
        var labels = new JournalingModelLabelRepository(new ModelLabelRepository(factory), journal);

        var get = () => labels.GetByIdAsync(null!, Ct);
        var delete = () => labels.DeleteAsync(null!, Ct);

        (await get.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("id");
        (await delete.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should()
            .Be("id");
        (await journal.ReadAllAsync(Ct)).Should().BeEmpty();
    }
}
