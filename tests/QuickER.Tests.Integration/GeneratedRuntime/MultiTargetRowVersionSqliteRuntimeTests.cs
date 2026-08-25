using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture;
using QuickER.Tests.GeneratedMultiTargetRowVersionFixture.Repositories.Sqlite;
using QuickER.Tests.Integration;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// マルチターゲット（SQL Server ＋ SQLite）で生成した rowversion 列を、ローカル側の SQLite で
/// 「通常のバイナリ列（サーバー版のミラー置き場）」として読み書きできることを実 DB で検証する。
/// </summary>
/// <remarks>
/// <para>
/// SQL Server 実装では <c>row_ver</c> が INSERT / UPDATE の対象から外れる（DB が採番するため）。
/// SQLite には採番する主体がいないので、同じ列を外したままにするとローカルの行は版を持てず、
/// サーバーへ送り返す値が無くなって同期が成立しない。そのため SQLite エンジンは除外を適用しない
/// （<c>EntitySaveMetadata</c> の方言ゲート）。ここではその結果を実ファイル DB で固定する。
/// </para>
/// <para>
/// スキーマは <see cref="MultiTargetRowVersionFixtureDefinition.BuildSqliteMirror"/>（実運用と同じ
/// <c>DiagramTypeConverter</c> 経由の方言変換＝<c>rowversion</c> → <c>BLOB</c>・NULL 許容）から
/// SQLite 方言 DDL を生成して作る。Docker 不要のため CI でも常時実行される。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class MultiTargetRowVersionSqliteRuntimeTests : IAsyncLifetime
{
    /// <summary>ローカル側の一時ファイル SQLite DB</summary>
    private readonly SqliteTempDatabase _sqlite = SqliteTempDatabase.Create();

    /// <summary>SQLite リポジトリを登録した DI コンテナ</summary>
    private ServiceProvider _provider = null!;

    /// <summary>テスト全体で使うキャンセルトークン</summary>
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>方言変換した図から SQLite スキーマを作り、DI を組む</summary>
    public async ValueTask InitializeAsync()
    {
        await _sqlite.ApplyDdlAsync(MultiTargetRowVersionFixtureDefinition.BuildSqliteMirror(), Ct);

        _provider = new ServiceCollection()
            .AddGeneratedSqliteRepositories(_sqlite.ReadWriteCreateConnectionString)
            .BuildServiceProvider();
    }

    /// <summary>DI コンテナと一時 DB を破棄する</summary>
    public ValueTask DisposeAsync()
    {
        _provider?.Dispose();
        _sqlite.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>リポジトリを解決する</summary>
    private ISyncItemRepository Items() => _provider.GetRequiredService<ISyncItemRepository>();

    /// <summary>方言変換した DDL が BLOB かつ NULL 許容の列を作っていることを確認する</summary>
    [Fact(
        DisplayName = "[MultiTarget/RowVersion/SQLite] 方言変換した DDL は row_ver を NULL 許容の BLOB で作る"
    )]
    public async Task ConvertedDdl_CreatesNullableBlobColumn()
    {
        await using var connection = new SqliteConnection(_sqlite.ReadWriteCreateConnectionString);
        await connection.OpenAsync(Ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT type, \"notnull\" FROM pragma_table_info('sync_items') WHERE name = 'row_ver';";

        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue("row_ver 列が存在するべき");
        reader.GetString(0).Should().Be("BLOB");
        reader.GetInt32(1).Should().Be(0, "ローカルでは未同期の行が空になるため NULL 許容");
    }

    /// <summary>INSERT が row_ver を書き込むこと（SQL Server 側の版をそのまま格納できること）を検証する</summary>
    [Fact(
        DisplayName = "[MultiTarget/RowVersion/SQLite] INSERT が row_ver へサーバー版を格納できる"
    )]
    public async Task Insert_WritesRowVersionColumn()
    {
        var serverVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0x07, 0xD1 };

        await Items()
            .InsertAsync(
                new SyncItemEntity
                {
                    ItemId = 1,
                    Name = "alpha",
                    RowVer = serverVersion,
                },
                Ct
            );

        var loaded = await Items().GetByIdAsync(1, Ct);
        loaded.Should().NotBeNull();
        loaded!.RowVer.Should().Equal(serverVersion, "書き込んだサーバー版がそのまま読み戻せる");
    }

    /// <summary>UPDATE も row_ver を書き換えること（版ガードで拒否されないこと）を検証する</summary>
    /// <remarks>
    /// SQL Server 実装なら「読んだときの版と違う」時点で <c>SaveConflictException</c> になるが、SQLite 側は
    /// 版ガードを持たないため、手元の版と DB の版が食い違っていても素通しで上書きされる。
    /// これはミラー列として意図した挙動で、同期処理が新しいサーバー版を書き戻せることを意味する。
    /// </remarks>
    [Fact(
        DisplayName = "[MultiTarget/RowVersion/SQLite] UPDATE が row_ver を上書きし版ガードは働かない"
    )]
    public async Task Update_OverwritesRowVersionWithoutGuard()
    {
        await Items()
            .InsertAsync(
                new SyncItemEntity
                {
                    ItemId = 2,
                    Name = "beta",
                    RowVer = [1, 2, 3, 4, 5, 6, 7, 8],
                },
                Ct
            );

        // 「手元の版が古い」状態を作る（DB 側だけを別の版へ進める）
        await Items()
            .ExecuteSqlAsync(
                "UPDATE \"sync_items\" SET \"row_ver\" = @v WHERE \"item_id\" = 2;",
                new { v = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 } },
                Ct
            );

        var stale = new SyncItemEntity
        {
            ItemId = 2,
            Name = "beta-updated",
            RowVer = [1, 2, 3, 4, 5, 6, 7, 8],
        };
        var updated = await Items().UpdateAsync(stale, cancellationToken: Ct);

        updated.Should().BeTrue("SQLite 側に版ガードは無いため古い版でも更新できる");

        var loaded = await Items().GetByIdAsync(2, Ct);
        loaded!.Name.Should().Be("beta-updated");
        loaded.RowVer.Should().Equal([1, 2, 3, 4, 5, 6, 7, 8], "UPDATE は row_ver も書き込む");
    }

    /// <summary>BulkInsert も row_ver を書き込む（INSERT と同じ列集合を使う）ことを検証する</summary>
    [Fact(DisplayName = "[MultiTarget/RowVersion/SQLite] BulkInsert も row_ver を書き込む")]
    public async Task BulkInsert_WritesRowVersionColumn()
    {
        var rows = await Items()
            .BulkInsertAsync(
                [
                    new SyncItemEntity
                    {
                        ItemId = 3,
                        Name = "gamma",
                        RowVer = [0, 0, 0, 0, 0, 0, 0, 3],
                    },
                ],
                Ct
            );

        rows.Should().Be(1);
        (await Items().GetByIdAsync(3, Ct))!.RowVer.Should().Equal([0, 0, 0, 0, 0, 0, 0, 3]);
    }

    /// <summary>未同期の行（版なし）はサーバー版が空のまま作れることを検証する</summary>
    /// <remarks>
    /// ローカルで新規作成した行はまだサーバー版を持たない。列が NOT NULL のままだとこの行が作れず、
    /// 「ローカル発の行をあとでサーバーへ送る」という同期の片側が成立しなくなる。
    /// </remarks>
    [Fact(DisplayName = "[MultiTarget/RowVersion/SQLite] サーバー版が未設定（空）の行も作成できる")]
    public async Task Insert_WithoutServerVersion_Succeeds()
    {
        await Items().InsertAsync(new SyncItemEntity { ItemId = 4, Name = "delta" }, Ct);

        (await Items().GetByIdAsync(4, Ct))!.RowVer.Should().BeEmpty();
    }
}
