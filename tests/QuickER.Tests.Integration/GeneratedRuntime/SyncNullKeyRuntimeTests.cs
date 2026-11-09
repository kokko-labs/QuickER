using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.GeneratedSyncModelFixture.Repositories.Sqlite;
using Xunit;
using SV = QuickER.Tests.GeneratedSyncVoFixture;
using SVR = QuickER.Tests.GeneratedSyncVoFixture.Repositories.Sqlite;

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

    /// <summary>
    /// ジャーナル記録デコレータの Update も、キーが null の行をキーの書式で落とさず（NullReferenceException にしない）、
    /// 内側のリポジトリの結果のまま終え、記録を残さない（キーの決まらない記録は書き込みの後の突き合わせかアップロードの判定が消す）
    /// </summary>
    [Fact(
        DisplayName = "[Sync/キー] ジャーナル記録デコレータの Update はキーが null の行をキーの書式で落とさず、記録を残さない"
    )]
    public async Task NullKey_UpdateIsNotBrokenByTheKeyText()
    {
        await _db.ApplyDdlAsync(SyncModelFixtureDefinition.BuildSqliteMirror(), Ct);
        var factory = new LightJournalSyncModelConnectionFactory(
            _db.ReadWriteCreateConnectionString
        );
        var sql = new SqlExecutor(factory);
        var journal = new SyncJournal(sql);
        await journal.EnsureCreatedAsync(Ct);
        var labels = new JournalingModelLabelRepository(new ModelLabelRepository(factory), journal);
        var plain = new ModelLabelRepository(factory);

        var thrown = await Record(() =>
            labels.UpdateAsync(new ModelLabelEntity { LabelCode = null! }, cancellationToken: Ct)
        );
        var plainThrown = await Record(() =>
            plain.UpdateAsync(new ModelLabelEntity { LabelCode = null! }, cancellationToken: Ct)
        );

        // 値オブジェクトのキー（SyncVoFixture）も同じ
        using var voDb = SqliteTempDatabase.Create();
        await voDb.ApplyDdlAsync(SV.SyncVoFixtureDefinition.BuildSqliteMirror(), Ct);
        var voFactory = new SVR.SqlConnectionFactory(voDb.ReadWriteCreateConnectionString);
        var voJournal = new SV.SyncJournal(new SVR.SqlExecutor(voFactory));
        await voJournal.EnsureCreatedAsync(Ct);
        var orders = new SV.JournalingSyncvoOrderRepository(
            new SVR.SyncvoOrderRepository(voFactory),
            voJournal
        );
        var plainOrders = new SVR.SyncvoOrderRepository(voFactory);

        var voThrown = await Record(() =>
            orders.UpdateAsync(new SV.SyncvoOrderEntity(), cancellationToken: Ct)
        );
        var voPlainThrown = await Record(() =>
            plainOrders.UpdateAsync(new SV.SyncvoOrderEntity(), cancellationToken: Ct)
        );
        // 例外の型は内側のリポジトリだけのときと同じ（デコレータのキーの書式が先に投げない）
        new[] { thrown?.GetType().Name, voThrown?.GetType().Name }
            .Should()
            .Equal(plainThrown?.GetType().Name, voPlainThrown?.GetType().Name);
        (await journal.ReadAllAsync(Ct)).Should().BeEmpty();
        (await voJournal.ReadAllAsync(Ct)).Should().BeEmpty();

        static async Task<Exception?> Record(Func<Task<bool>> action)
        {
            try
            {
                await action();

                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    /// <summary>
    /// 除外 blob の Stream アクセサの Write も、記録する前に null のキーを断る（SyncVoFixture の値オブジェクトのキー
    /// ＝ジャーナルのキーの書式で NullReferenceException になっていた）。Read は内側へ素通しで、内側が断る
    /// </summary>
    [Fact(
        DisplayName = "[Sync/キー] ジャーナル記録デコレータの blob の Read・Write は null のキーを ArgumentNullException(id) で断り、何も記録しない"
    )]
    public async Task NullKey_BlobAccessorsAreRefusedWithoutRecording()
    {
        await _db.ApplyDdlAsync(SV.SyncVoFixtureDefinition.BuildSqliteMirror(), Ct);
        var factory = new SVR.SqlConnectionFactory(_db.ReadWriteCreateConnectionString);
        var journal = new SV.SyncJournal(new SVR.SqlExecutor(factory));
        await journal.EnsureCreatedAsync(Ct);
        var orders = new SV.JournalingSyncvoOrderRepository(
            new SVR.SyncvoOrderRepository(factory),
            journal
        );

        var read = () => orders.ReadPhotoAsync(null!, new MemoryStream(), Ct);
        var write = () => orders.WritePhotoAsync(null!, new MemoryStream([1]), null, Ct);

        (await read.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("id");
        (await write.Should().ThrowAsync<ArgumentNullException>())
            .Which.ParamName.Should()
            .Be("id");
        (await journal.ReadAllAsync(Ct)).Should().BeEmpty();
    }
}
