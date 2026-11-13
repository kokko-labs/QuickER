using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>決定的なシナリオだけが使う手（期待・例外の期待・一括の書き込み・ジャーナルの手・オプションつきの洗い替え）</summary>
internal sealed partial class SyncModelRun
{
    /// <summary>端末ごとの直前の同期・洗い替えの結果の面（静止化の同期は書き換えない）</summary>
    private readonly Dictionary<ModelActor, IReadOnlyList<string>> _lastResult = [];

    /// <summary>次の同期・洗い替えが例外で終わることを期待する端末（<see cref="ExpectThrowOp"/>）</summary>
    private readonly HashSet<ModelActor> _expectThrow = [];

    /// <summary>
    /// 消失なしの規則の対象から外すキー（記録しない書き込みの手が書いたキーと、ジャーナルの手が記録を捨てたキー）。
    /// 印は手ごとに付け、ほかのキーの規則は変えない
    /// </summary>
    private readonly HashSet<(string Table, string Key)> _untrackedKeys = [];

    /// <summary>端末ごとの、直前の同期（静止化でない）の開始の時点にあったジャーナルの id（ジャーナルの面の age が見る）</summary>
    private readonly Dictionary<ModelActor, HashSet<long>> _journalAtLastSync = [];

    /// <summary>消した表の定義（表と索引の CREATE 文）</summary>
    private readonly Dictionary<(ModelActor Device, string Table), List<string>> _droppedTables =
    [];

    /// <summary>控えたミラー版（<see cref="CaptureMirrorOp"/>）</summary>
    private readonly Dictionary<
        (ModelActor Device, string Table, string Key),
        (string Name, byte[]? Version)
    > _mirrorCaptures = [];

    private static readonly Dictionary<string, string> PrimaryKeyColumns = new(
        StringComparer.Ordinal
    )
    {
        [ModelTables.Folders] = "folder_id",
        [ModelTables.Labels] = "label_code",
        [ModelTables.Items] = "item_id",
        [ModelTables.Events] = "event_at",
    };

    /// <summary>シナリオの手を流す（扱わない手なら false）</summary>
    private async Task<bool> ExecuteScenarioOpAsync(ModelOp op)
    {
        switch (op)
        {
            case ExpectFacetOp expect:
                await CheckExpectAsync(expect);
                return true;

            case ExpectThrowOp expectThrow:
                _expectThrow.Add(expectThrow.Device);
                return true;

            case WhenServerSameUniqueOp choice:
            {
                var same = _world.Server.UniqueValues.Equals(choice.A, choice.B);
                _log.Add(
                    $"  -> the server treats them as {(same ? "the same" : "different")} values"
                );

                foreach (var inner in same ? choice.IfSame : choice.IfDifferent)
                {
                    if (!await ExecuteAsync(inner))
                    {
                        break;
                    }
                }

                return true;
            }

            case BulkAddFoldersOp folders:
                await BulkWriteAsync(
                    folders.Actor,
                    folders.Mode,
                    () =>
                        folders
                            .Ids.Select(id => new ModelFolderEntity
                            {
                                FolderId = id,
                                Name = folders.NamePrefix + ModelDbState.Key(id),
                            })
                            .ToList(),
                    (device, rows) => device.RawFolders.BulkInsertAsync(rows, _ct),
                    (device, rows) => device.Folders.BulkInsertAsync(rows, _ct),
                    async rows =>
                    {
                        foreach (var row in rows)
                        {
                            await _world.Server.Folders.InsertAsync(row, _ct);
                        }
                    }
                );
                return true;

            case BulkAddItemsOp items:
                await BulkWriteAsync(
                    items.Actor,
                    items.Mode,
                    () =>
                        items
                            .Rows.Select(r => new ModelItemEntity
                            {
                                ItemId = r.Id,
                                FolderId = r.FolderId,
                                Code = items.CodePrefix + ModelDbState.Key(r.Id),
                                Title = "t" + ModelDbState.Key(r.Id),
                                Payload = BulkPayload(r.Id),
                            })
                            .ToList(),
                    (device, rows) => device.RawItems.BulkInsertAsync(rows, _ct),
                    (device, rows) => device.Items.BulkInsertAsync(rows, _ct),
                    async rows =>
                    {
                        foreach (var row in rows)
                        {
                            await _world.Server.Items.InsertAsync(row, _ct);
                        }
                    }
                );
                return true;

            case RecordJournalOp record:
                await _world
                    .Device(record.Device)
                    .Journal.RecordAsync(record.Table, record.KeyText, record.Operation, null, _ct);
                return true;

            case RefuseJournalRemovalOp refuse:
            {
                // 表とキーはシナリオが書いた定数で、文字列リテラルとして引用して埋める
                var sql = refuse.Refuse
                    ? "CREATE TRIGGER model_refuse_journal_removal BEFORE DELETE ON quicker_sync_journal "
                        + $"WHEN OLD.table_name = '{refuse.Table.Replace("'", "''", StringComparison.Ordinal)}' "
                        + $"AND OLD.key_text = '{refuse.KeyText.Replace("'", "''", StringComparison.Ordinal)}' "
                        + "BEGIN SELECT RAISE(ABORT, 'journal removal refused by the scenario'); END"
                    : "DROP TRIGGER IF EXISTS model_refuse_journal_removal";
                await _world.Device(refuse.Device).Sql.ExecuteSqlAsync(sql, null, _ct);

                return true;
            }

            case ClearJournalOp clear:
            {
                var journal = _world.Device(clear.Device).Journal;

                foreach (
                    var entry in (await journal.ReadAllAsync(_ct)).Where(e =>
                        clear.Table is null || e.TableName == clear.Table
                    )
                )
                {
                    _untrackedKeys.Add(
                        (entry.TableName, CanonicalKey(entry.TableName, entry.KeyText))
                    );
                }

                if (clear.Table is null)
                {
                    await journal.RemoveAllAsync(_ct);
                }
                else
                {
                    await journal.RemoveTableAsync(clear.Table, _ct);
                }

                return true;
            }

            case RefreshWithOptionsOp refresh:
                await RefreshAsync(
                    refresh,
                    refresh.Device,
                    refresh.ToOptions(),
                    refresh.OmitOptions
                );
                return true;

            case ArmEmptyFetchOp empty:
                _world.Faults.ArmEmptyFetch(empty.Device, empty.Table);
                return true;

            case ArmApplySkipOp skip:
                _world.Faults.ArmApplySkip(skip.Device, skip.Table, skip.KeyText);
                return true;

            case ArmHookBlobWriteOp hook:
                _world.Faults.ArmHookBlobWrite(
                    hook.Device,
                    hook.TargetItemId,
                    Convert.FromHexString(hook.Hex),
                    hook.KeyAsInt64,
                    hook.ThrowAfter
                );
                return true;

            case LocalSqlOp sql:
                await LocalSqlAsync(sql.Device, [sql.Sql]);
                return true;

            case OpenNewJournalOp open:
                await new SyncJournal(_world.Device(open.Device).Sql).EnsureCreatedAsync(_ct);
                return true;

            case DropLocalTableOp drop:
            {
                var definitions = await LocalDefinitionsAsync(drop.Device, drop.Table);
                _droppedTables[(drop.Device, drop.Table)] = definitions;
                await LocalSqlAsync(drop.Device, [$"DROP TABLE \"{drop.Table}\""]);
                return true;
            }

            case RestoreLocalTableOp restore:
                await LocalSqlAsync(
                    restore.Device,
                    _droppedTables[(restore.Device, restore.Table)]
                );
                _droppedTables.Remove((restore.Device, restore.Table));
                return true;

            case MakeLocalCascadeOp cascade:
            {
                var definitions = await LocalDefinitionsAsync(cascade.Device, cascade.ChildTable);
                var table = definitions[0];
                var rewritten = Regex.Replace(
                    table,
                    $"REFERENCES\\s+\"?{cascade.ParentTable}\"?\\s*\\(\\s*\"?{PrimaryKeyColumns[cascade.ParentTable]}\"?\\s*\\)",
                    match => match.Value + " ON DELETE CASCADE",
                    RegexOptions.CultureInvariant
                );

                if (rewritten == table)
                {
                    throw new InvalidOperationException(
                        $"{cascade.Describe()}: no foreign key to rewrite."
                    );
                }

                await LocalSqlAsync(
                    cascade.Device,
                    [
                        $"CREATE TABLE \"cascade_copy\" AS SELECT * FROM \"{cascade.ChildTable}\"",
                        $"DROP TABLE \"{cascade.ChildTable}\"",
                        rewritten,
                        .. definitions.Skip(1),
                        $"INSERT INTO \"{cascade.ChildTable}\" SELECT * FROM \"cascade_copy\"",
                        "DROP TABLE \"cascade_copy\"",
                    ]
                );
                return true;
            }

            case CaptureMirrorOp capture:
            {
                var versions = await LocalVersionsAsync(
                    _world.Device(capture.Device),
                    capture.Table
                );
                _mirrorCaptures[(capture.Device, capture.Table, capture.KeyText)] = (
                    capture.Name,
                    versions.GetValueOrDefault(capture.KeyText)
                );
                return true;
            }

            case ExpectLocalSqlOp expectSql:
            {
                await using var connection = new SqliteConnection(
                    _world.Device(expectSql.Device).ConnectionString
                );
                await connection.OpenAsync(_ct);
                await using var command = connection.CreateCommand();
                command.CommandText = expectSql.Sql;
                var value = await command.ExecuteScalarAsync(_ct);
                var text = value is null or DBNull
                    ? null
                    : Convert.ToString(value, CultureInfo.InvariantCulture);

                if (text != expectSql.Value)
                {
                    _failures.Add(
                        new("5/expect-sql", $"{expectSql.Describe()}: got {text ?? "null"}")
                    );
                }

                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>端末のローカル DB へ、外部キーの検査を切って文を順に流す（記録しない）</summary>
    private async Task LocalSqlAsync(ModelActor device, IEnumerable<string> statements)
    {
        await using var connection = new SqliteConnection(_world.Device(device).ConnectionString);
        await connection.OpenAsync(_ct);

        foreach (
            var sql in statements
                .Prepend("PRAGMA foreign_keys = OFF")
                .Append("PRAGMA foreign_keys = ON")
        )
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(_ct);
        }
    }

    /// <summary>表の CREATE 文（先頭）と、その表の索引の CREATE 文</summary>
    private async Task<List<string>> LocalDefinitionsAsync(ModelActor device, string table)
    {
        await using var connection = new SqliteConnection(_world.Device(device).ConnectionString);
        await connection.OpenAsync(_ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE tbl_name = $table AND sql IS NOT NULL "
            + "ORDER BY CASE type WHEN 'table' THEN 0 ELSE 1 END, name";
        command.Parameters.AddWithValue("$table", table);
        var definitions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(_ct);

        while (await reader.ReadAsync(_ct))
        {
            definitions.Add(reader.GetString(0));
        }

        return definitions.Count > 0
            ? definitions
            : throw new InvalidOperationException($"The local database has no table {table}.");
    }

    /// <summary>一括の書き込みを流し、変わった行を書き手の履歴へ積む（記録しない書き込みはキーに印を付ける）</summary>
    private async Task BulkWriteAsync<TEntity>(
        ModelActor actor,
        ModelBulkMode mode,
        Func<List<TEntity>> build,
        Func<SyncModelDevice, List<TEntity>, Task<int>> raw,
        Func<SyncModelDevice, List<TEntity>, Task<int>> journaled,
        Func<List<TEntity>, Task> server
    )
    {
        if ((mode == ModelBulkMode.Server) != (actor == ModelActor.Server))
        {
            throw new ArgumentException("The server mode writes as the server, and only it does.");
        }

        var before = await _world.ReadStateAsync(actor, _ct);
        var rows = build();

        switch (mode)
        {
            case ModelBulkMode.Journaled:
                await journaled(_world.Device(actor), rows);
                break;

            case ModelBulkMode.Raw:
                await raw(_world.Device(actor), rows);
                break;

            case ModelBulkMode.Server:
                await server(rows);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        var after = await _world.ReadStateAsync(actor, _ct);

        if (mode == ModelBulkMode.Raw)
        {
            foreach (var table in ModelTables.All)
            {
                var was = before.Rows(table);

                foreach (var (key, row) in after.Rows(table))
                {
                    if (!was.TryGetValue(key, out var old) || old != row)
                    {
                        _untrackedKeys.Add((table, key));
                    }
                }
            }
        }

        RecordChanges(actor, before, after);
        await CheckForeignKeysAsync(actor, $"bulk {mode}");
    }

    /// <summary>一括で挿入する品目の payload（キーの 4 バイトの big-endian）</summary>
    public static byte[] BulkPayload(int id)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, id);

        return bytes;
    }

    /// <summary>オプションを指定した洗い替え（例外の期待・結果の面を扱う）</summary>
    /// <remarks><paramref name="omitOptions"/> のときエンジンへはオプションを渡さない（<paramref name="options"/> はモデルの記録に使う既定の値）</remarks>
    private async Task RefreshAsync(
        ModelOp op,
        ModelActor device,
        SyncRefreshOptions options,
        bool omitOptions = false
    )
    {
        // Force の洗い替えが捨てる範囲内の未送信の記録（利用者が明示して破棄した変更＝ClearJournalOp と同じく、成功したら
        // そのキーを消失なしの規則の対象から外す）
        var discarded = options.Force
            ? (await _world.Device(device).Journal.ReadAllAsync(_ct))
                .Where(e => InRefreshScope(e.TableName, options))
                .Select(e => (e.TableName, CanonicalKey(e.TableName, e.KeyText)))
                .ToList()
            : [];

        try
        {
            var result = await _world.RefreshAsync(device, omitOptions ? null : options, _ct);
            _untrackedKeys.UnionWith(discarded);
            RecordRefreshResult(op, device, result);
        }
        catch (Exception ex) when (ConsumeExpectedThrow(device, ex))
        {
            // 期待した例外（結果の面に残した）
        }
        catch (Exception ex) when (InjectedFaultException.IsInjected(ex))
        {
            _log.Add($"  -> injected fault: {ex.GetType().Name}");
            _lastResult[device] = ExceptionLines(ex);
        }
        catch (Exception ex)
        {
            _lastResult[device] = ExceptionLines(ex);
            _failures.Add(new($"1/throw:{Phase(ex)}:{ErrorText(ex)}", $"{op.Describe()}: {ex}"));
        }
    }

    /// <summary>洗い替えの範囲の表か（Versioned は版なしの表を含まない・除外した表を含まない）</summary>
    private static bool InRefreshScope(string table, SyncRefreshOptions options) =>
        !(options.Mode == SyncMode.Versioned && table == ModelTables.Labels)
        && !options.ExcludedEntityTypes.Contains(ModelTables.EntityType(table));

    /// <summary>洗い替えが例外で終わらなかったときに、結果の面を残し、例外の期待が残っていれば違反にする</summary>
    private void RecordRefreshResult(ModelOp op, ModelActor device, SyncRefreshResult result)
    {
        var lines = new List<string>
        {
            $"deleted={result.Deleted}",
            $"inserted={result.Inserted}",
            $"discarded-changes={result.DiscardedChanges}",
            result.Elapsed > TimeSpan.Zero ? "elapsed>0" : "elapsed=0",
        };
        lines.AddRange(
            result.Tables.Select(t =>
                $"table {t.TableName} deleted={t.Deleted} inserted={t.Inserted}"
            )
        );
        _lastResult[device] = lines;
        CheckUnexpectedSuccess(device, op);
    }

    /// <summary>同期の結果の面を残す（静止化の同期は呼ばない）</summary>
    private void RecordSyncResult(ModelSyncOp op, ModelSyncOutcome outcome)
    {
        var lines = new List<string>();
        Count("uploaded", outcome.Uploaded);
        Count("downloaded", outcome.Downloaded);
        Count("deleted-locally", outcome.DeletedLocally);
        Count("discarded", outcome.Discarded);

        lines.AddRange(
            outcome.Conflicts.Select(c =>
                $"conflict {c.TableName} key={CanonicalKey(c.TableName, c.KeyText)} op={c.Operation} "
                + $"reason={c.Reason} local={ModelEntityRows.Of(c.LocalEntity)} "
                + $"server={ModelEntityRows.Of(c.ServerEntity)}"
            )
        );
        lines.AddRange(
            outcome.Truncations.Select(t =>
                $"truncation {t.TableName} "
                + $"pending={(t.PendingKeyText is { } key ? CanonicalKey(t.TableName, key) : "-")} "
                + $"missing-parent={t.MissingParentTableName ?? "-"} "
                + $"skipped-by-hook={(t.SkippedBySaveHook ? 1 : 0)} "
                + $"truncated-by={t.TruncatedByTableName ?? "-"}"
            )
        );
        lines.AddRange(LocalChangeLines(outcome.LocalRowChanges));
        _lastResult[op.Device] = lines;
        CheckUnexpectedSuccess(op.Device, op);

        void Count(string name, int? value)
        {
            if (value is { } v)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{name}={v}"));
            }
        }
    }

    private static IEnumerable<string> LocalChangeLines(IEnumerable<SyncLocalRowChange> changes) =>
        changes.Select(c =>
            $"local-change {c.TableName} key={CanonicalKey(c.TableName, c.KeyText)} reason={c.Reason}"
        );

    /// <summary>例外で終わった同期・洗い替えの結果の面</summary>
    private static List<string> ExceptionLines(Exception ex)
    {
        var lines = new List<string>
        {
            $"exception {ex.GetType().Name}: {ex.Message.Split('\n')[0].TrimEnd('\r')}",
        };

        switch (ex)
        {
            case SyncPendingChangesException pending:
                lines.Add($"pending-count={pending.PendingCount}");
                lines.AddRange(
                    pending.PendingChanges.Select(p => $"pending {p.TableName} count={p.Count}")
                );
                break;

            case SyncRefreshScopeException scope:
                lines.AddRange(
                    scope.References.Select(r =>
                        $"scope-reference {r.ReferencingTableName}->{r.ReferencedTableName}"
                    )
                );
                break;

            case SyncUnboundedBinaryLossException loss:
                lines.AddRange(
                    loss.Columns.Select(c =>
                        $"blob-loss {c.TableName} cols=({string.Join(",", c.ColumnNames)})"
                    )
                );
                break;
        }

        if (ex.Data[SyncEngine.LocalRowChangesDataKey] is IReadOnlyList<SyncLocalRowChange> changes)
        {
            lines.AddRange(LocalChangeLines(changes));
        }

        return lines;
    }

    /// <summary>例外の期待が残っていれば消費し、結果の面へ例外を残す（期待していなければ false）</summary>
    private bool ConsumeExpectedThrow(ModelActor device, Exception ex)
    {
        if (!_expectThrow.Remove(device))
        {
            return false;
        }

        _log.Add($"  -> threw as expected: {ex.GetType().Name}");
        _lastResult[device] = ExceptionLines(ex);

        return true;
    }

    private void CheckUnexpectedSuccess(ModelActor device, ModelOp op)
    {
        if (_expectThrow.Remove(device))
        {
            _failures.Add(
                new($"op/unexpected-success:{op.GetType().Name}", $"{op.Describe()} did not throw")
            );
        }
    }

    /// <summary>期待の手を照合する</summary>
    private async Task CheckExpectAsync(ExpectFacetOp expect)
    {
        if (expect.Checks.Count == 0)
        {
            throw new ArgumentException(
                $"{expect.Describe()} has no expectations.",
                nameof(expect)
            );
        }

        var lines = await FacetLinesAsync(expect);

        foreach (var check in expect.Checks)
        {
            if (check.Check(lines) is { } problem)
            {
                _failures.Add(new(expect.Signature, $"{expect.Describe()}: {problem}"));
            }
        }
    }

    private async Task<IReadOnlyList<string>> FacetLinesAsync(ExpectFacetOp expect)
    {
        switch (expect.Facet)
        {
            case ExpectFacets.Result:
                return _lastResult.GetValueOrDefault(expect.Actor) ?? [];

            case ExpectFacets.Rows:
            {
                var state = await _world.ReadStateAsync(expect.Actor, _ct);

                return state
                    .Rows(TableOf(expect))
                    .OrderBy(p => p.Key, ModelKeyComparer.Instance)
                    .Select(p => $"{p.Key} {p.Value}")
                    .ToList();
            }

            case ExpectFacets.Journal:
            {
                var old = _journalAtLastSync.GetValueOrDefault(expect.Actor) ?? [];

                return (await _world.Device(expect.Actor).Journal.ReadAllAsync(_ct))
                    .OrderBy(e => e.Id)
                    .Select(e =>
                        $"{e.TableName} key={CanonicalKey(e.TableName, e.KeyText)} op={e.Operation} "
                        + $"ver={(e.OriginalRowVersion is { Length: > 0 } ? "set" : "null")} "
                        + $"blobs={(string.IsNullOrEmpty(e.UnboundedBinaryColumns) ? "-" : e.UnboundedBinaryColumns)} "
                        + $"age={(old.Contains(e.Id) ? "old" : "new")}"
                    )
                    .ToList();
            }

            case ExpectFacets.Acks:
            {
                var table = TableOf(expect);
                var server = await ServerVersionsAsync(table);

                return (await _world.Device(expect.Actor).Journal.ReadAcksAsync(table, _ct))
                    .Select(p => (Key: CanonicalKey(table, p.Key), Version: p.Value))
                    .OrderBy(p => p.Key, ModelKeyComparer.Instance)
                    .Select(p => $"key={p.Key} ver={Relation(p.Version, server, p.Key)}")
                    .ToList();
            }

            case ExpectFacets.BlobPending:
            {
                var table = TableOf(expect);

                return (await _world.Device(expect.Actor).Journal.ReadBlobCopiesAsync(table, _ct))
                    .Select(r => (Key: CanonicalKey(table, r.KeyText), r.ColumnName))
                    .OrderBy(r => r.Key, ModelKeyComparer.Instance)
                    .Select(r => $"key={r.Key} col={r.ColumnName}")
                    .ToList();
            }

            case ExpectFacets.Mirror:
            {
                var table = TableOf(expect);
                var server = await ServerVersionsAsync(table);
                var local = await LocalVersionsAsync(_world.Device(expect.Actor), table);

                return local
                    .OrderBy(p => p.Key, ModelKeyComparer.Instance)
                    .Select(p =>
                        $"key={p.Key} mirror={Relation(p.Value, server, p.Key)}"
                        + CapturedText(expect.Actor, table, p.Key, p.Value)
                    )
                    .ToList();
            }

            case ExpectFacets.Hook:
                return _world.Faults.HookBlobWrites;

            default:
                throw new ArgumentOutOfRangeException(nameof(expect), expect.Facet, null);
        }
    }

    /// <summary>控えたミラー版との比較（控えが無ければ空）</summary>
    private string CapturedText(ModelActor device, string table, string key, byte[]? version) =>
        _mirrorCaptures.TryGetValue((device, table, key), out var captured)
            ? $" {captured.Name}="
                + (
                    (captured.Version ?? []).AsSpan().SequenceEqual(version ?? [])
                        ? "same"
                        : "changed"
                )
            : "";

    private static string TableOf(ExpectFacetOp expect) =>
        expect.Table
        ?? throw new ArgumentException($"{expect.Describe()} needs a table.", nameof(expect));

    /// <summary>端末の版とサーバー役のいまの版の関係（null・server＝同じ・other＝違う・gone＝サーバーに行が無い）</summary>
    private static string Relation(
        byte[]? version,
        IReadOnlyDictionary<string, byte[]?> server,
        string key
    ) =>
        version is not { Length: > 0 } ? "null"
        : !server.TryGetValue(key, out var current) ? "gone"
        : current is not null && current.AsSpan().SequenceEqual(version) ? "server"
        : "other";

    private async Task<IReadOnlyDictionary<string, byte[]?>> ServerVersionsAsync(string table) =>
        table switch
        {
            ModelTables.Folders => (await _world.Server.Folders.GetAllAsync(_ct)).ToDictionary(
                e => ModelDbState.Key(e.FolderId),
                e => e.RowVer,
                StringComparer.Ordinal
            ),
            ModelTables.Items => (await _world.Server.Items.GetAllAsync(_ct)).ToDictionary(
                e => ModelDbState.Key(e.ItemId),
                e => e.RowVer,
                StringComparer.Ordinal
            ),
            ModelTables.Events => (await _world.Server.Events.GetAllAsync(_ct)).ToDictionary(
                e => ModelTables.EventKeyText(e.EventAt),
                e => e.RowVer,
                StringComparer.Ordinal
            ),
            _ => throw new ArgumentException($"{table} has no row version.", nameof(table)),
        };

    private async Task<IReadOnlyDictionary<string, byte[]?>> LocalVersionsAsync(
        SyncModelDevice device,
        string table
    ) =>
        table switch
        {
            ModelTables.Folders => (await device.Folders.GetAllAsync(_ct)).ToDictionary(
                e => ModelDbState.Key(e.FolderId),
                e => e.RowVer,
                StringComparer.Ordinal
            ),
            ModelTables.Items => (await device.Items.GetAllAsync(_ct)).ToDictionary(
                e => ModelDbState.Key(e.ItemId),
                e => e.RowVer,
                StringComparer.Ordinal
            ),
            ModelTables.Events => (await device.Events.GetAllAsync(_ct)).ToDictionary(
                e => ModelTables.EventKeyText(e.EventAt),
                e => e.RowVer,
                StringComparer.Ordinal
            ),
            _ => throw new ArgumentException($"{table} has no row version.", nameof(table)),
        };
}

/// <summary>実体を ExpectLocalOp と同じ行の書式で書く（競合の行の面）</summary>
internal static class ModelEntityRows
{
    public static string Of(object? entity) =>
        entity switch
        {
            null => "null",
            ModelFolderEntity f =>
                $"parent={(f.ParentId is { } p ? ModelDbState.Key(p) : "null")} name={f.Name}{ModelDbState.Pinned(f.PinnedItemId)}",
            ModelLabelEntity l => $"folder={ModelDbState.Key(l.FolderId)} caption={l.Caption}",
            ModelItemEntity i =>
                $"folder={ModelDbState.Key(i.FolderId)} label={i.LabelCode ?? "null"} code={i.Code} title={i.Title}",
            ModelEventEntity e => $"item={ModelDbState.Key(e.ItemId)} memo={e.Memo}",
            _ => entity.GetType().Name,
        };
}

/// <summary>実体の表とキー（モデルの照合に使う格納値の文字列）</summary>
internal static class ModelEntityKeys
{
    public static (string Table, string Key) Of(object entity) =>
        entity switch
        {
            ModelFolderEntity f => (ModelTables.Folders, ModelDbState.Key(f.FolderId)),
            ModelLabelEntity l => (ModelTables.Labels, l.LabelCode),
            ModelItemEntity i => (ModelTables.Items, ModelDbState.Key(i.ItemId)),
            ModelEventEntity e => (ModelTables.Events, ModelTables.EventKeyText(e.EventAt)),
            _ => throw new ArgumentOutOfRangeException(nameof(entity), entity.GetType().Name, null),
        };
}

/// <summary>キーの並び（両方が整数なら数の順・それ以外は序数）</summary>
internal sealed class ModelKeyComparer : IComparer<string>
{
    public static ModelKeyComparer Instance { get; } = new();

    public int Compare(string? x, string? y) =>
        int.TryParse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var a)
        && int.TryParse(y, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b)
            ? a.CompareTo(b)
            : string.CompareOrdinal(x, y);
}
