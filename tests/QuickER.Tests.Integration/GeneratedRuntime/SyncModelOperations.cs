using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuickER.Tests.GeneratedSyncModelFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>モデルの書き手（端末 2 台と、サーバーへ直接書く「他者」）</summary>
internal enum ModelActor
{
    /// <summary>端末 1（ローカル SQLite・ジャーナル記録デコレータ越しに書く）</summary>
    Device1,

    /// <summary>端末 2</summary>
    Device2,

    /// <summary>サーバーへ直接書く他者（版を採番するサーバー役のリポジトリ越し）</summary>
    Server,
}

/// <summary>端末の書き込みの経路（直接の CRUD か、グラフ保存か）</summary>
internal enum ModelWriteVia
{
    /// <summary>InsertAsync / UpdateAsync / DeleteAsync</summary>
    Direct,

    /// <summary>RowState を付けた実体の SaveAsync</summary>
    Graph,
}

/// <summary>同期の呼び方</summary>
internal enum ModelSyncKind
{
    /// <summary>SyncAsync（アップロード→ダウンロード）</summary>
    Sync,

    /// <summary>UploadAsync だけ</summary>
    Upload,

    /// <summary>DownloadAsync だけ</summary>
    Download,
}

/// <summary>モデルの表（FK の順）</summary>
internal static class ModelTables
{
    public const string Folders = "model_folders";
    public const string Labels = "model_labels";
    public const string Items = "model_items";
    public const string Events = "model_events";

    /// <summary>FK の順（親→子）</summary>
    public static IReadOnlyList<string> All { get; } = [Folders, Labels, Items, Events];

    /// <summary>表名から実行の除外に渡すエンティティ型を引く</summary>
    public static Type EntityType(string table) =>
        table switch
        {
            Folders => typeof(ModelFolderEntity),
            Labels => typeof(ModelLabelEntity),
            Items => typeof(ModelItemEntity),
            Events => typeof(ModelEventEntity),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };

    /// <summary>エンティティ型から表名を引く</summary>
    public static string TableOf(Type entityType) =>
        entityType == typeof(ModelFolderEntity) ? Folders
        : entityType == typeof(ModelLabelEntity) ? Labels
        : entityType == typeof(ModelItemEntity) ? Items
        : entityType == typeof(ModelEventEntity) ? Events
        : throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null);

    /// <summary>主キーの値を、モデルの照合に使う格納値の文字列へそろえる（日時は <see cref="EventKeyText"/>）</summary>
    public static string KeyText(object key) =>
        key switch
        {
            int value => ModelDbState.Key(value),
            DateTime value => EventKeyText(value),
            string value => value,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    /// <summary>表の短い名前（操作の記録用）</summary>
    public static string Short(string table) =>
        table switch
        {
            Folders => "folders",
            Labels => "labels",
            Items => "items",
            Events => "events",
            _ => table,
        };

    /// <summary>
    /// 日時のキーを、SQLite に格納される文字列（Microsoft.Data.Sqlite の形式・Kind は落ちる）へそろえる。
    /// モデルの照合はこの文字列で行う（格納値どうしの比較＝端末とサーバーで同じ規則）。
    /// </summary>
    public static string EventKeyText(DateTime value) =>
        value.ToString(@"yyyy\-MM\-dd HH\:mm\:ss.FFFFFFF", CultureInfo.InvariantCulture);
}

/// <summary>モデルの操作 1 つ（人が読める 1 行で記録し、決定的なシナリオへそのまま書き写せる形にする）</summary>
internal abstract record ModelOp
{
    /// <summary>人が読める 1 行（失敗した種の操作の列として出力する）</summary>
    public abstract string Describe();

    /// <summary>書き手の短い名前</summary>
    protected static string Who(ModelActor actor) =>
        actor switch
        {
            ModelActor.Device1 => "D1",
            ModelActor.Device2 => "D2",
            _ => "SV",
        };

    /// <summary>NULL を含む値の表記</summary>
    protected static string Show(object? value) =>
        value switch
        {
            null => "null",
            DateTime dt => DescribeDateTime(dt),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
        };

    /// <summary>日時は Kind まで書く（Kind の違いがキーの文字列を変える）</summary>
    public static string DescribeDateTime(DateTime value) =>
        $"{value.ToString("o", CultureInfo.InvariantCulture)}({value.Kind})";
}

/// <summary>書き込みの操作（書き手と表とキーを持つ）</summary>
internal abstract record ModelWriteOp(ModelActor Actor, ModelWriteVia Via) : ModelOp
{
    /// <summary>対象の表</summary>
    public abstract string Table { get; }

    /// <summary>対象の行のキー（格納値の文字列）</summary>
    public abstract string KeyText { get; }

    /// <summary>
    /// 失敗する書き込み（業務の書き込みが失敗し、ジャーナルの記録だけが残る形）。失敗しなければ違反。
    /// <see cref="WritePayloadOp"/> では途中で失敗するストリームを渡す。
    /// </summary>
    public bool ExpectFailure { get; init; }

    /// <summary>経路と失敗の表記（サーバーの他者は経路を持たない）</summary>
    protected string ViaText =>
        (Actor == ModelActor.Server ? "" : $" via={Via}") + (ExpectFailure ? " expect-fail" : "");
}

internal sealed record AddFolderOp(
    ModelActor Actor,
    ModelWriteVia Via,
    int Id,
    int? ParentId,
    string Name
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Folders;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() =>
        $"{Who(Actor)} AddFolder id={Id} parent={Show(ParentId)} name={Name}{ViaText}";
}

internal sealed record UpdateFolderOp(
    ModelActor Actor,
    ModelWriteVia Via,
    int Id,
    int? ParentId,
    string Name
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Folders;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() =>
        $"{Who(Actor)} UpdateFolder id={Id} parent={Show(ParentId)} name={Name}{ViaText}";
}

internal sealed record DeleteFolderOp(ModelActor Actor, ModelWriteVia Via, int Id)
    : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Folders;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() => $"{Who(Actor)} DeleteFolder id={Id}{ViaText}";
}

internal sealed record AddLabelOp(
    ModelActor Actor,
    ModelWriteVia Via,
    string Code,
    int FolderId,
    string Caption
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Labels;

    public override string KeyText => Code;

    public override string Describe() =>
        $"{Who(Actor)} AddLabel code={Code} folder={FolderId} caption={Caption}{ViaText}";
}

internal sealed record UpdateLabelOp(
    ModelActor Actor,
    ModelWriteVia Via,
    string Code,
    int FolderId,
    string Caption
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Labels;

    public override string KeyText => Code;

    public override string Describe() =>
        $"{Who(Actor)} UpdateLabel code={Code} folder={FolderId} caption={Caption}{ViaText}";
}

internal sealed record DeleteLabelOp(ModelActor Actor, ModelWriteVia Via, string Code)
    : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Labels;

    public override string KeyText => Code;

    public override string Describe() => $"{Who(Actor)} DeleteLabel code={Code}{ViaText}";
}

internal sealed record AddItemOp(
    ModelActor Actor,
    ModelWriteVia Via,
    int Id,
    int FolderId,
    string? LabelCode,
    string Code,
    string Title,
    string PayloadHex
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Items;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() =>
        $"{Who(Actor)} AddItem id={Id} folder={FolderId} label={Show(LabelCode)} code={Code} "
        + $"title={Title} payload={PayloadHex}{ViaText}";
}

internal sealed record UpdateItemOp(
    ModelActor Actor,
    ModelWriteVia Via,
    int Id,
    int FolderId,
    string? LabelCode,
    string Code,
    string Title
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Items;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() =>
        $"{Who(Actor)} UpdateItem id={Id} folder={FolderId} label={Show(LabelCode)} code={Code} "
        + $"title={Title}{ViaText}";
}

internal sealed record DeleteItemOp(ModelActor Actor, ModelWriteVia Via, int Id)
    : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Items;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() => $"{Who(Actor)} DeleteItem id={Id}{ViaText}";
}

/// <summary>除外列（payload）を Write{列}Async で書く（端末はジャーナルの blob 列の記録を通る）</summary>
internal sealed record WritePayloadOp(ModelActor Actor, int Id, string PayloadHex)
    : ModelWriteOp(Actor, ModelWriteVia.Direct)
{
    public override string Table => ModelTables.Items;

    public override string KeyText => Id.ToString(CultureInfo.InvariantCulture);

    public override string Describe() =>
        $"{Who(Actor)} WritePayload id={Id} payload={PayloadHex}{(ExpectFailure ? " expect-fail" : "")}";
}

internal sealed record AddEventOp(
    ModelActor Actor,
    ModelWriteVia Via,
    DateTime At,
    int ItemId,
    string Memo
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Events;

    public override string KeyText => ModelTables.EventKeyText(At);

    public override string Describe() =>
        $"{Who(Actor)} AddEvent at={DescribeDateTime(At)} item={ItemId} memo={Memo}{ViaText}";
}

internal sealed record UpdateEventOp(
    ModelActor Actor,
    ModelWriteVia Via,
    DateTime At,
    int ItemId,
    string Memo
) : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Events;

    public override string KeyText => ModelTables.EventKeyText(At);

    public override string Describe() =>
        $"{Who(Actor)} UpdateEvent at={DescribeDateTime(At)} item={ItemId} memo={Memo}{ViaText}";
}

internal sealed record DeleteEventOp(ModelActor Actor, ModelWriteVia Via, DateTime At)
    : ModelWriteOp(Actor, Via)
{
    public override string Table => ModelTables.Events;

    public override string KeyText => ModelTables.EventKeyText(At);

    public override string Describe() =>
        $"{Who(Actor)} DeleteEvent at={DescribeDateTime(At)}{ViaText}";
}

/// <summary>端末の同期 1 回</summary>
internal sealed record ModelSyncOp(
    ModelActor Device,
    ModelSyncKind Kind,
    SyncMode Mode,
    SyncConflictPolicy Policy,
    IReadOnlyList<string> Excluded,
    bool IncludeBlobs,
    int BatchSize,
    bool PropagateDeletes = true
) : ModelOp
{
    /// <summary>エンジンへ渡すオプション</summary>
    public SyncOptions ToOptions() =>
        new()
        {
            Mode = Mode,
            ConflictPolicy = Policy,
            ExcludedEntityTypes = Excluded.Select(ModelTables.EntityType).ToArray(),
            IncludeUnboundedBinary = IncludeBlobs,
            DownloadBatchSize = BatchSize,
            PropagateDeletes = PropagateDeletes,
        };

    public override string Describe() =>
        $"{Who(Device)} {Kind} mode={Mode} policy={Policy} "
        + $"excluded=[{string.Join(",", Excluded.Select(ModelTables.Short))}] "
        + $"blobs={IncludeBlobs} batch={BatchSize}"
        + (PropagateDeletes ? "" : " propagate=False");
}

/// <summary>端末の洗い替え 1 回（未送信のジャーナルがあれば拒否される＝Force は使わない）</summary>
/// <param name="ExpectScopeRefusal">
/// 範囲外の表の行が範囲内の行を参照しているので <see cref="SyncRefreshScopeException"/> で断られ、端末の DB が変わらない
/// ことを期待するか
/// </param>
internal sealed record ModelRefreshOp(
    ModelActor Device,
    SyncMode Mode,
    bool IncludeBlobs,
    int BatchSize = 2000,
    bool ExpectScopeRefusal = false
) : ModelOp
{
    /// <summary>エンジンへ渡すオプション（blob を運ばないときは、ローカルの blob を捨てることを明示する）</summary>
    public SyncRefreshOptions ToOptions() =>
        new()
        {
            Mode = Mode,
            IncludeUnboundedBinary = IncludeBlobs,
            DiscardLocalUnboundedBinaries = !IncludeBlobs,
            BatchSize = BatchSize,
        };

    public override string Describe() =>
        $"{Who(Device)} Refresh mode={Mode} blobs={IncludeBlobs} batch={BatchSize}"
        + (ExpectScopeRefusal ? " expect=scope-refusal" : "");
}

/// <summary>キーだけのスタブ（新しい実体にキーだけを入れて RowState=Removed）をグラフ保存して削除する</summary>
internal sealed record DeleteStubOp(ModelActor Actor, string StubTable, string StubKeyText)
    : ModelWriteOp(Actor, ModelWriteVia.Graph)
{
    public override string Table => StubTable;

    public override string KeyText => StubKeyText;

    public override string Describe() =>
        $"{Who(Actor)} DeleteStub {ModelTables.Short(StubTable)} key={StubKeyText}";
}

/// <summary>
/// この端末が挿入したときの実体（同期の後もメモリに持っていたもの）に RowState=Removed を付けてグラフ保存する
/// </summary>
internal sealed record DeleteHeldOp(ModelActor Actor, string HeldTable, string HeldKeyText)
    : ModelWriteOp(Actor, ModelWriteVia.Graph)
{
    public override string Table => HeldTable;

    public override string KeyText => HeldKeyText;

    public override string Describe() =>
        $"{Who(Actor)} DeleteHeld {ModelTables.Short(HeldTable)} key={HeldKeyText}";
}

/// <summary>失敗を仕掛ける（対象の端末の、該当する動作で 1 回だけ起きる）</summary>
internal sealed record ArmFaultOp(ModelActor Device, ModelFaultKind Kind) : ModelOp
{
    public override string Describe() => $"{Who(Device)} Arm {Kind}";
}

/// <summary>
/// 割り込みを仕掛ける：端末の次の同期（洗い替えでない）がその点に来たときに 1 回だけ、実行と別の流れ（同期の抑止も書き込みロックの
/// リースも引き継がない）から、その端末のローカルの書き込みを 1 つ流す（<see cref="SyncModelFaults.InterruptAsync"/>）。
/// 割り込みの書き込みは書き手の操作として数える（収束・消失なしの期待に入る）。点に来ないまま静止化に入ったら違反
/// （<c>interrupt/not-fired:{点}</c>）
/// </summary>
internal sealed record ArmInterruptOp(
    ModelActor Device,
    ModelInterruptPoint Point,
    ModelWriteOp Write
) : ModelOp
{
    public override string Describe() => $"{Who(Device)} ArmInterrupt {Point}: {Write.Describe()}";
}

/// <summary>
/// 端末のジャーナル・受理記録・blob のコピー待ちのうち、その表のキーのテキストの末尾へ文字列を足す（以前の版が書いた
/// 形の再現。日時の主キーは Kind つき＝<c>Z</c> やオフセットつきで書いていた）
/// </summary>
internal sealed record LegacyKeyTextsOp(ModelActor Device, string Table, string Suffix) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} LegacyKeyTexts {ModelTables.Short(Table)} suffix={Suffix}";
}

/// <summary>シナリオの途中で、書き手（端末かサーバー役）の品目の blob を確かめる（違反は 5/expect-payload）</summary>
internal sealed record ExpectPayloadOp(ModelActor Actor, int ItemId, string? PayloadHex) : ModelOp
{
    public override string Describe() =>
        $"{Who(Actor)} ExpectPayload id={ItemId} payload={PayloadHex ?? "(none)"}";
}

/// <summary>シナリオの途中で、端末がそのキーの受理記録を持たないことを確かめる（違反は 5/expect-no-ack）</summary>
internal sealed record ExpectNoAckOp(ModelActor Device, string Table, string KeyText) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ExpectNoAck {ModelTables.Short(Table)} key={KeyText}";
}

/// <summary>
/// シナリオの途中で、端末の直前の同期（SyncAsync）が、その行をその理由で自分から変えたと報告したかを確かめる
/// （違反は 5/expect-row-change。例外で終わった同期は、例外の Data の一覧を見る）
/// </summary>
internal sealed record ExpectLocalRowChangeOp(
    ModelActor Device,
    string Table,
    string KeyText,
    SyncLocalRowChangeReason Reason
) : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ExpectLocalRowChange {ModelTables.Short(Table)} key={KeyText} reason={Reason}";
}

/// <summary>シナリオの途中で、端末のローカルの行を確かめる（違反は 5/expect-local）</summary>
internal sealed record ExpectLocalOp(ModelActor Device, string Table, string KeyText, string? Row)
    : ModelOp
{
    public override string Describe() =>
        $"{Who(Device)} ExpectLocal {ModelTables.Short(Table)} key={KeyText} row={Row ?? "(none)"}";
}
