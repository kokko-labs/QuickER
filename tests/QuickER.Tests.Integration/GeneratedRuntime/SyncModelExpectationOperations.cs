using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

namespace QuickER.Tests.Integration.GeneratedRuntime;

// 決定的なシナリオだけが使う手（生成器は出さない＝種の指紋に触れない）。生成器が出す手（書き込み・ModelSyncOp・
// ModelRefreshOp・ArmFaultOp）にはプロパティを足さず、新しい振る舞いはここの新しい型にする（SeedSummary が公開
// プロパティを全部書くので、足すと指紋が変わる）。

/// <summary>期待の手が照合する面</summary>
internal static class ExpectFacets
{
    /// <summary>端末の直前の同期・アップロード・ダウンロード・洗い替えの結果（例外で終わったらその例外）</summary>
    public const string Result = "result";

    /// <summary>端末かサーバー役の表の行（<c>{キー} {ExpectLocalOp と同じ行}</c>）</summary>
    public const string Rows = "rows";

    /// <summary>端末の未送信のジャーナル（id の順）</summary>
    public const string Journal = "journal";

    /// <summary>端末の受理記録</summary>
    public const string Acks = "acks";

    /// <summary>端末の blob のコピー待ち</summary>
    public const string BlobPending = "blob-pending";

    /// <summary>端末のミラー版とサーバー役のいまの版の関係</summary>
    public const string Mirror = "mirror";

    /// <summary>After フックの blob の書き込みの結果（<see cref="ArmHookBlobWriteOp"/>）</summary>
    public const string Hook = "hook";
}

/// <summary>
/// シナリオの途中で、書き手の面（<see cref="ExpectFacets"/>）を行の並びとして読み、期待（同期計画の期待の行と同じ照合＝
/// <see cref="SyncPlanExpectation"/>）と比べる。違反は署名 <c>5/expect-{面}[:{表}]</c> で報告する。
/// </summary>
/// <param name="Actor">読む書き手（結果・ジャーナル・受理記録・コピー待ち・ミラー版は端末だけ）</param>
/// <param name="Facet">面</param>
/// <param name="Table">表（行・受理記録・コピー待ち・ミラー版の面だけ）</param>
/// <param name="Checks">期待（1 つ以上）</param>
internal sealed record ExpectFacetOp(
    ModelActor Actor,
    string Facet,
    string? Table,
    IReadOnlyList<SyncPlanExpectation> Checks
) : ModelOp
{
    /// <summary>違反の署名</summary>
    public string Signature => Table is null ? $"5/expect-{Facet}" : $"5/expect-{Facet}:{Table}";

    /// <summary>行がパターンと同じ数・同じ順で合う</summary>
    public ExpectFacetOp Exact(params string[] patterns) => With(ExpectationMatch.Exact, patterns);

    /// <summary>行が 1 つも無い</summary>
    public ExpectFacetOp Empty() => With(ExpectationMatch.Exact, []);

    /// <summary>行がパターンと同じ数で、順序を問わず 1 対 1 に合う</summary>
    public ExpectFacetOp Unordered(params string[] patterns) =>
        With(ExpectationMatch.Unordered, patterns);

    /// <summary>どのパターンにも合う行がある</summary>
    public ExpectFacetOp Contains(params string[] patterns) =>
        With(ExpectationMatch.Contains, patterns);

    /// <summary>どのパターンにも合う行が無い</summary>
    public ExpectFacetOp Absent(params string[] patterns) =>
        With(ExpectationMatch.Absent, patterns);

    /// <summary>パターンに合う行がちょうど 1 つ（パターンごとに）</summary>
    public ExpectFacetOp Single(params string[] patterns) =>
        patterns.Aggregate(this, (op, pattern) => op.Count(pattern, 1));

    /// <summary>パターンに合う行がちょうど <paramref name="count"/> 個</summary>
    public ExpectFacetOp Count(string pattern, int count) =>
        this with
        {
            Checks =
            [
                .. Checks,
                new SyncPlanExpectation(Facet, ExpectationMatch.Count, [pattern], count),
            ],
        };

    private ExpectFacetOp With(ExpectationMatch match, IReadOnlyList<string> patterns) =>
        this with
        {
            Checks = [.. Checks, new SyncPlanExpectation(Facet, match, patterns)],
        };

    public override string Describe() =>
        $"{Who(Actor)} Expect {Facet}{(Table is null ? "" : " " + ModelTables.Short(Table))}: "
        + string.Join("; ", Checks.Select(c => c.Describe()));
}

/// <summary>期待の手を組み立てる（<c>Expect.Result(D1).Exact(...)</c>）</summary>
internal static class Expect
{
    /// <summary>端末の直前の同期・洗い替えの結果</summary>
    public static ExpectFacetOp Result(ModelActor device) =>
        new(device, ExpectFacets.Result, null, []);

    /// <summary>書き手（端末かサーバー役）の表の行</summary>
    public static ExpectFacetOp Rows(ModelActor actor, string table) =>
        new(actor, ExpectFacets.Rows, table, []);

    /// <summary>端末の未送信のジャーナル</summary>
    public static ExpectFacetOp Journal(ModelActor device) =>
        new(device, ExpectFacets.Journal, null, []);

    /// <summary>端末の受理記録</summary>
    public static ExpectFacetOp Acks(ModelActor device, string table) =>
        new(device, ExpectFacets.Acks, table, []);

    /// <summary>端末の blob のコピー待ち</summary>
    public static ExpectFacetOp BlobPending(ModelActor device, string table) =>
        new(device, ExpectFacets.BlobPending, table, []);

    /// <summary>端末のミラー版</summary>
    public static ExpectFacetOp Mirror(ModelActor device, string table) =>
        new(device, ExpectFacets.Mirror, table, []);

    /// <summary>After フックの blob の書き込みの結果</summary>
    public static ExpectFacetOp Hook(ModelActor device) => new(device, ExpectFacets.Hook, null, []);
}

/// <summary>
/// 端末の次の同期（<see cref="ModelSyncOp"/>）か洗い替え（<see cref="ModelRefreshOp"/>・<see cref="RefreshWithOptionsOp"/>）が
/// 例外で終わることを期待する。例外は違反にせず結果の面（<c>exception {型名}: {メッセージの 1 行目}</c>）へ残す。例外で
/// 終わらなければ違反 <c>op/unexpected-success:{手}</c>
/// </summary>
internal sealed record ExpectThrowOp(ModelActor Device) : ModelOp
{
    public override string Describe() => $"{Who(Device)} ExpectThrow";
}

/// <summary>記録の仕方（一括の書き込みの手）</summary>
internal enum ModelBulkMode
{
    /// <summary>端末のジャーナル記録デコレータの BulkInsertAsync（記録する）</summary>
    Journaled,

    /// <summary>
    /// 端末の記録しないリポジトリの BulkInsertAsync（同期しないローカルの行を作る）。この手が書いたキーは、消失なしの規則
    /// （書き手が 1 人のキーの値・消えた行・作り話の値）の対象から外す（収束・外部キー・止まらないの表明は当たる）
    /// </summary>
    Raw,

    /// <summary>サーバーの他者（版を採番するサーバー役のリポジトリへ 1 行ずつ挿入する）</summary>
    Server,
}

/// <summary>フォルダーを一括で挿入する（500 件を超える一括の形を作る）</summary>
/// <param name="Actor">書き手（<see cref="ModelBulkMode.Server"/> のときはサーバー役）</param>
/// <param name="Mode">記録の仕方</param>
/// <param name="Ids">キー</param>
/// <param name="NamePrefix">名前の頭（名前は <c>{頭}{キー}</c>＝一意）</param>
internal sealed record BulkAddFoldersOp(
    ModelActor Actor,
    ModelBulkMode Mode,
    IReadOnlyList<int> Ids,
    string NamePrefix
) : ModelOp
{
    public override string Describe() =>
        $"{Who(Actor)} BulkAddFolders mode={Mode} ids={ModelIdRanges.Describe(Ids)} name={NamePrefix}{{id}}";
}

/// <summary>品目を一括で挿入する（payload は <c>{キーの 16 進 8 桁}</c>・code は <c>{頭}{キー}</c>）</summary>
/// <param name="Actor">書き手</param>
/// <param name="Mode">記録の仕方</param>
/// <param name="Rows">キーとフォルダー</param>
/// <param name="CodePrefix">code の頭（一意）</param>
internal sealed record BulkAddItemsOp(
    ModelActor Actor,
    ModelBulkMode Mode,
    IReadOnlyList<(int Id, int FolderId)> Rows,
    string CodePrefix
) : ModelOp
{
    public override string Describe() =>
        $"{Who(Actor)} BulkAddItems mode={Mode} rows=[{string.Join(",", Rows.Select(r => $"{r.Id}@{r.FolderId}"))}] "
        + $"code={CodePrefix}{{id}}";
}

/// <summary>
/// 端末のジャーナルへ、業務の書き込みの無い記録を直接書く（記録の後で落ちて残った記録の再現）
/// </summary>
internal sealed record RecordJournalOp(
    ModelActor Device,
    string Table,
    string KeyText,
    SyncJournalOperation Operation
) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} RecordJournal {ModelTables.Short(Table)} key={KeyText} op={Operation}";
}

/// <summary>
/// 端末のジャーナルを捨てる（表を指定すれば RemoveTableAsync・null なら RemoveAllAsync）。捨てた記録のキーは、消失なしの
/// 規則の対象から外す（利用者が送らないと決めた変更）
/// </summary>
internal sealed record ClearJournalOp(ModelActor Device, string? Table) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ClearJournal {(Table is null ? "all" : ModelTables.Short(Table))}";
}

/// <summary>
/// 端末の洗い替え 1 回（モデルの <see cref="ModelRefreshOp"/> が持たないオプション＝Force・表の除外・blob の扱いを指定する）
/// </summary>
internal sealed record RefreshWithOptionsOp(
    ModelActor Device,
    SyncMode Mode,
    bool IncludeBlobs,
    bool DiscardLocalBlobs,
    bool Force,
    IReadOnlyList<string> Excluded,
    int BatchSize = 2000,
    bool OmitOptions = false
) : ModelOp
{
    /// <summary>
    /// オプションを渡さない洗い替え（<c>RefreshAsync(cancellationToken: …)</c>）。モデルの記録に使う値は
    /// <see cref="SyncRefreshOptions"/> の既定から取る
    /// </summary>
    public static RefreshWithOptionsOp Defaults(ModelActor device)
    {
        var defaults = new SyncRefreshOptions();

        return new(
            device,
            defaults.Mode,
            defaults.IncludeUnboundedBinary,
            defaults.DiscardLocalUnboundedBinaries,
            defaults.Force,
            [.. defaults.ExcludedEntityTypes.Select(ModelTables.TableOf)],
            defaults.BatchSize,
            OmitOptions: true
        );
    }

    /// <summary>エンジンへ渡すオプション</summary>
    public SyncRefreshOptions ToOptions() =>
        new()
        {
            Mode = Mode,
            IncludeUnboundedBinary = IncludeBlobs,
            DiscardLocalUnboundedBinaries = DiscardLocalBlobs,
            Force = Force,
            ExcludedEntityTypes = Excluded.Select(ModelTables.EntityType).ToArray(),
            BatchSize = BatchSize,
        };

    public override string Describe() =>
        $"{Who(Device)} RefreshWith mode={Mode} blobs={IncludeBlobs} discard-blobs={DiscardLocalBlobs} "
        + $"force={Force} excluded=[{string.Join(",", Excluded.Select(ModelTables.Short))}] batch={BatchSize}"
        + (OmitOptions ? " options=omitted" : "");
}

/// <summary>キーの並びを短く書く（連続する範囲は <c>a..b</c>）</summary>
internal static class ModelIdRanges
{
    /// <summary><paramref name="from"/> から <paramref name="count"/> 個の連続するキー</summary>
    public static IReadOnlyList<int> Range(int from, int count) =>
        Enumerable.Range(from, count).ToArray();

    public static string Describe(IReadOnlyList<int> ids)
    {
        var parts = new List<string>();
        var i = 0;

        while (i < ids.Count)
        {
            var j = i;

            while (j + 1 < ids.Count && ids[j + 1] == ids[j] + 1)
            {
                j++;
            }

            parts.Add(
                j == i
                    ? ids[i].ToString(CultureInfo.InvariantCulture)
                    : string.Create(CultureInfo.InvariantCulture, $"{ids[i]}..{ids[j]}")
            );
            i = j + 1;
        }

        return "[" + string.Join(",", parts) + "]";
    }
}

/// <summary>
/// 割り込みを仕掛ける（サーバーの他者の書き込み版）：端末の次の同期がその点に来たときに 1 回だけ、サーバーへ書く。
/// サーバー役の口を使う（実 SQL Server では流さない）
/// </summary>
internal sealed record ArmServerInterruptOp(
    ModelActor Device,
    ModelInterruptPoint Point,
    ModelWriteOp Write
) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ArmServerInterrupt {Point}: {Write.Describe()}";
}

/// <summary>端末の次の同期で、その表の最初の差分の取得を空（続きなし）で返す（サーバー役の口を使う）</summary>
internal sealed record ArmEmptyFetchOp(ModelActor Device, string Table) : ModelOp
{
    public override string Describe() => $"{Who(Device)} ArmEmptyFetch {ModelTables.Short(Table)}";
}

/// <summary>端末の同期がその行をローカルへ適用するとき、ローカルの Before フックが 1 回だけ保存を飛ばす</summary>
internal sealed record ArmApplySkipOp(ModelActor Device, string Table, string KeyText) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ArmApplySkip {ModelTables.Short(Table)} key={KeyText}";
}

/// <summary>
/// 端末の次の品目の保存の After フックが、context の WriteBinaryColumnAsync で blob を書く（対象の品目を指定しなければ
/// 保存した品目。キーを long で渡せば主キーの型と違う型のキー）。後で投げると保存ごと失敗する。結果はフックの面
/// （<see cref="ExpectFacets.Hook"/>）に残る
/// </summary>
internal sealed record ArmHookBlobWriteOp(
    ModelActor Device,
    int? TargetItemId,
    string Hex,
    bool KeyAsInt64 = false,
    bool ThrowAfter = false
) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ArmHookBlobWrite target={(TargetItemId is { } t ? t.ToString(CultureInfo.InvariantCulture) : "self")} "
        + $"payload={Hex}{(KeyAsInt64 ? " key=long" : "")}{(ThrowAfter ? " throw-after" : "")}";
}

/// <summary>グラフ保存の行の状態</summary>
internal enum ModelRowState
{
    Added,
    Updated,
    Removed,
    Unchanged,
}

/// <summary>グラフ保存のフォルダー（根）</summary>
internal sealed record GraphFolder(int Id, ModelRowState State, string? Name = null);

/// <summary>グラフ保存の品目（根のフォルダーの子・code は c-{キー}・payload は 0A0B{キーの 16 進 4 桁}）</summary>
internal sealed record GraphItem(int Id, ModelRowState State, string? Title = null);

/// <summary>グラフ保存のラベル（根のフォルダーの子）</summary>
internal sealed record GraphLabel(string Code, ModelRowState State, string? Caption = null);

/// <summary>
/// フォルダーを根に、子の品目・ラベルを RowState つきで 1 回の SaveAsync で保存する（カスケードの閉包を記録する経路）。
/// 追加の行は新しく作り、ほかは端末の行を読んで値を変える
/// </summary>
internal sealed record SaveFolderGraphOp(
    ModelActor Actor,
    GraphFolder Root,
    IReadOnlyList<GraphItem> Items,
    IReadOnlyList<GraphLabel> Labels
) : ModelWriteOp(Actor, ModelWriteVia.Graph)
{
    public override string Table => ModelTables.Folders;

    public override string KeyText => Root.Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() =>
        $"{Who(Actor)} SaveFolderGraph root={Root.Id}:{Root.State}"
        + (Root.Name is null ? "" : $" name={Root.Name}")
        + $" items=[{string.Join(",", Items.Select(i => $"{i.Id}:{i.State}{(i.Title is null ? "" : "=" + i.Title)}"))}]"
        + $" labels=[{string.Join(",", Labels.Select(l => $"{l.Code}:{l.State}{(l.Caption is null ? "" : "=" + l.Caption)}"))}]"
        + ViaText;
}

/// <summary>端末のローカル DB へ生の SQL を流す（記録しない・外部キーの検査を切って流す）</summary>
/// <remarks>
/// 元のテストの前提（ジャーナル記録デコレータを通らないローカルの書き込み・ローカルだけの表・トリガー）を作るためだけに使う。
/// 書き込みの履歴に残らない（同期のモデルの汎用の表明〔消失なし〕が根拠にする書き手の値にならない）。使うシナリオは blob-refresh-discard-drops-local-blobs・
/// blob-refresh-refuses-when-blobs-would-be-lost・blob-default-preserves-existing-local-blobs-new-rows-arrive-empty・
/// refresh-excluding-referencing-child-refuses-under-cascade・refresh-failed-wipe-keeps-acks・
/// refresh-leaves-local-only-tables-alone。
/// </remarks>
internal sealed record LocalSqlOp(ModelActor Device, string Sql) : ModelOp
{
    public override string Describe() => $"{Who(Device)} LocalSql {Sql}";
}

/// <summary>端末のローカル DB から表を消す（作り直すための定義を控える。<see cref="RestoreLocalTableOp"/> で戻す）</summary>
internal sealed record DropLocalTableOp(ModelActor Device, string Table) : ModelOp
{
    public override string Describe() => $"{Who(Device)} DropLocalTable {ModelTables.Short(Table)}";
}

/// <summary>消した表を、控えた定義で空のまま作り直す（静止化の前に戻す）</summary>
internal sealed record RestoreLocalTableOp(ModelActor Device, string Table) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} RestoreLocalTable {ModelTables.Short(Table)}";
}

/// <summary>ローカルの子の表の、親を指す外部キーを ON DELETE CASCADE へ書き換える（行は写す）</summary>
internal sealed record MakeLocalCascadeOp(ModelActor Device, string ChildTable, string ParentTable)
    : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} MakeLocalCascade {ModelTables.Short(ChildTable)}->{ModelTables.Short(ParentTable)}";
}

/// <summary>端末のミラー版を名前で控える（ミラー版の面が <c>{名前}=same|changed</c> を書く）</summary>
internal sealed record CaptureMirrorOp(ModelActor Device, string Table, string KeyText, string Name)
    : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} CaptureMirror {ModelTables.Short(Table)} key={KeyText} as={Name}";
}

/// <summary>端末のローカル DB でスカラーの問い合わせ 1 つを流し、値の文字列を比べる（違反は 5/expect-sql）</summary>
internal sealed record ExpectLocalSqlOp(ModelActor Device, string Sql, string? Value) : ModelOp
{
    public override string Describe() => $"{Who(Device)} ExpectLocalSql {Sql} = {Value ?? "null"}";
}
