using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Tests.GeneratedBackendParityFixture;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>注文の除外 blob（payload・thumb）を、実装先に依らない文字列へ整える（<see cref="ParityFormat"/> の保存先の行と同じ書式）</summary>
internal static class ParityBlobs
{
    /// <summary>除外 blob の 2 列（<c>payload=0102 thumb=0A</c> の形・null は <c>null</c>・空は <c>empty</c>）</summary>
    public static string Of(OrderEntity order) =>
        $"payload={Hex(order.Payload?.Value)} thumb={Hex(order.Thumb?.Value)}";

    /// <summary>バイト列の 16 進（null は <c>null</c>・空は <c>empty</c>）</summary>
    public static string Hex(byte[]? value) =>
        value is null ? "null"
        : value.Length == 0 ? "empty"
        : Convert.ToHexString(value);

    /// <summary>注文の行を ID の順に <c>[10:payload=… thumb=…,…]</c> の形で並べる（<paramref name="withState"/> のときは RowState も添える）</summary>
    public static string Rows(IEnumerable<OrderEntity> orders, bool withState) =>
        "["
        + string.Join(
            ",",
            orders
                .OrderBy(o => o.OrderId.Value)
                .Select(o =>
                    $"{o.OrderId.Value.ToString(CultureInfo.InvariantCulture)}:{Of(o)}"
                    + (withState ? $" state={o.RowState}" : "")
                )
        )
        + "]";
}

/// <summary>
/// リモート面の <c>GetAllAsync</c> で注文を全件読む（全実装先にある）。戻り値は <c>orders=[10:payload=… thumb=…,…]</c>
/// （読んだ行の除外 blob が未取得かどうかを見るため。EF Core は全列を読む＝docs に書いてある違い）
/// </summary>
public sealed record GetAllOrdersOp : ParityOp
{
    public override string Name => "GetAllOrders";

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct) =>
        $"orders={ParityBlobs.Rows(await backend.Orders.GetAllAsync(ct), withState: false)}";
}

/// <summary>
/// スロットのグラフの注文の除外 blob を書き換える（状態は変えない）。<paramref name="Data"/> が null なら未取得状態へ戻す
/// （payload＝null・thumb＝空の値。QuickER 版は、除外列に null でも空でもない値の入った行の UPDATE を断る＝docs に書いてある契約）
/// </summary>
public sealed record AssignBlobOp(string Slot, int OrderId, string Column, byte[]? Data) : ParityOp
{
    public override string Name => $"AssignBlob:{Column}";

    public override Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var order = (OrderEntity)Find(backend, Slot, $"orders/{OrderId}");

        switch (Column)
        {
            case "payload":
                order.Payload = Data is null ? null : PayloadValue.Create(Data);
                break;
            case "thumb":
                order.Thumb = ThumbValue.Create(Data ?? []);
                break;
            default:
                throw new ArgumentException(Column);
        }

        return Task.FromResult("assigned");
    }
}

/// <summary><see cref="WithBinaryReadOp"/> の読み方</summary>
public enum ParityBinaryRead
{
    /// <summary><c>Query().Where(id).WithUnboundedBinary().FirstOrDefaultAsync</c>（スロットを渡すと結果を置く）</summary>
    First,

    /// <summary><c>Query().WithUnboundedBinary().ToListAsync</c>（全件）</summary>
    ToList,

    /// <summary><c>Query().ToListAsync</c>（全件・<c>WithUnboundedBinary</c> なし＝除外が効く読み取り）</summary>
    PlainToList,

    /// <summary>
    /// <see cref="First"/> で読み、返った payload の配列の先頭を書き換えてから読み直す（<c>reread=…</c> を添える＝返却が保存先と
    /// 配列を共有しない）
    /// </summary>
    FirstMutateReread,

    /// <summary><c>Include(明細)</c> → <c>WithUnboundedBinary()</c> → <c>FirstOrDefaultAsync</c>（併用は断る）</summary>
    IncludeThenWithFirst,

    /// <summary><c>WithUnboundedBinary()</c> → <c>Include(明細)</c> → <c>ToListAsync</c>（併用は断る）</summary>
    WithThenIncludeToList,

    /// <summary><c>Include(明細)</c> → <c>WithUnboundedBinary()</c> → <c>CountAsync</c>（終端に依らず断る）</summary>
    IncludeThenWithCount,
}

/// <summary>
/// 全機能面の <c>Query()</c> で注文を読む（<c>WithUnboundedBinary()</c> の取得と、Include との併用の拒否。リモートには無い）。戻り値は
/// <c>[10:payload=… thumb=… state=Unchanged,…]</c>。EF Core は <c>WithUnboundedBinary()</c> が無くても全列を読む（docs に書いてある違い）
/// </summary>
/// <param name="Read">読み方</param>
/// <param name="OrderId">1 件を読む読み方の注文 ID</param>
/// <param name="Slot">渡すと、<see cref="ParityBinaryRead.First"/> で読んだ行をそのスロットの根に置く（読んだ行の更新・保持を見るため）</param>
public sealed record WithBinaryReadOp(ParityBinaryRead Read, int OrderId = 10, string? Slot = null)
    : ParityOp
{
    public override string Name => $"WithBinaryRead:{Read}";

    public override bool Supports(ParityBackendKind kind) => !ParityBackendKinds.IsRemote(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var id = OrderIdValue.Create(OrderId);
        var orders = backend.FullOrders;

        switch (Read)
        {
            case ParityBinaryRead.First:
            {
                var order = await FirstAsync(orders, id, ct);

                if (Slot is not null && order is not null)
                {
                    backend.Slots[Slot] = order;
                }

                return ParityBlobs.Rows(order is null ? [] : [order], withState: true);
            }
            case ParityBinaryRead.ToList:
                return ParityBlobs.Rows(
                    await orders.Query().WithUnboundedBinary().ToListAsync(ct),
                    withState: true
                );
            case ParityBinaryRead.PlainToList:
                return ParityBlobs.Rows(await orders.Query().ToListAsync(ct), withState: true);
            case ParityBinaryRead.FirstMutateReread:
            {
                var order =
                    await FirstAsync(orders, id, ct)
                    ?? throw new InvalidOperationException($"orders/{OrderId} is missing.");
                var text = ParityBlobs.Rows([order], withState: true);
                order.Payload!.Value[0] = 0xFF;
                var reread = await FirstAsync(orders, id, ct);

                return $"{text} reread={ParityBlobs.Rows(reread is null ? [] : [reread], withState: true)}";
            }
            case ParityBinaryRead.IncludeThenWithFirst:
            {
                var order = await orders
                    .Query()
                    .Where(o => o.OrderId == id)
                    .Include(o => o.OrderLines)
                    .WithUnboundedBinary()
                    .FirstOrDefaultAsync(ct);

                return ParityBlobs.Rows(order is null ? [] : [order], withState: true);
            }
            case ParityBinaryRead.WithThenIncludeToList:
                return ParityBlobs.Rows(
                    await orders
                        .Query()
                        .Where(o => o.OrderId == id)
                        .WithUnboundedBinary()
                        .Include(o => o.OrderLines)
                        .ToListAsync(ct),
                    withState: true
                );
            case ParityBinaryRead.IncludeThenWithCount:
                return $"count={await orders.Query().Include(o => o.OrderLines).WithUnboundedBinary().CountAsync(ct)}";
            default:
                throw new ArgumentOutOfRangeException(nameof(Read));
        }
    }

    private static Task<OrderEntity?> FirstAsync(
        IOrderRepository orders,
        OrderIdValue id,
        CancellationToken ct
    ) => orders.Query().Where(o => o.OrderId == id).WithUnboundedBinary().FirstOrDefaultAsync(ct);
}

/// <summary><see cref="WriteBlobStreamOp"/> に渡す Stream</summary>
public enum ParityBlobSource
{
    /// <summary>シークできる Stream（<c>MemoryStream</c>）</summary>
    Seekable,

    /// <summary>シークできない Stream（長さを渡さないと断る）</summary>
    NonSeekable,

    /// <summary>長さの食い違い 1：<see cref="BinaryLengthMismatchCases"/> の 1 つ目（シーク可能で length が残りと違う）</summary>
    MismatchSeekable,

    /// <summary>長さの食い違い 2：<see cref="BinaryLengthMismatchCases"/> の 2 つ目（非シークで length より短い）</summary>
    MismatchShort,

    /// <summary>長さの食い違い 3：<see cref="BinaryLengthMismatchCases"/> の 3 つ目（非シークで length より長い）</summary>
    MismatchLong,
}

/// <summary>
/// 注文の除外 blob をリモート面の Stream アクセサで書く（EF Core は NotSupported＝docs に書いてある違い）。<see cref="WriteBlobOp"/> と
/// 違い、Stream の種類と長さを選べ、書いた後に渡した Stream が開いたままか（所有権は呼び出し側）を戻り値に出す。
/// </summary>
/// <param name="OrderId">注文 ID</param>
/// <param name="Column">列（payload / thumb）</param>
/// <param name="Source">Stream の種類。長さの食い違いの 3 つは <see cref="BinaryLengthMismatchCases"/> の Stream と length を使う（Data と Length は渡さない）</param>
/// <param name="Data">書くバイト列（<see cref="ParityBlobSource.Seekable"/> と <see cref="ParityBlobSource.NonSeekable"/>）</param>
/// <param name="Length">渡す length（null は渡さない）</param>
/// <param name="ResendTo">渡すと、書いた後に同じ Stream を先頭へ巻き戻してこの列へもう一度書く（<c>resent=…</c>。閉じていないことの実用の確認）</param>
public sealed record WriteBlobStreamOp(
    int OrderId,
    string Column,
    ParityBlobSource Source,
    byte[]? Data = null,
    long? Length = null,
    string? ResendTo = null
) : ParityOp
{
    public override string Name =>
        $"WriteBlobStream:{Column}:{Source}:length={Length?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

    public override bool Supports(ParityBackendKind kind) => ParityBackendKinds.HasBlobStream(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var (source, length) = Open();
        var id = OrderIdValue.Create(OrderId);

        using (source)
        {
            var written = await WriteAsync(backend, id, Column, source, length, ct);
            var text = $"written={written}";

            if (Source == ParityBlobSource.Seekable)
            {
                // 破棄した MemoryStream は CanRead が false になる＝開いたままであることの直接の証拠
                text +=
                    $" open={source.CanRead}"
                    + (
                        source.CanRead
                            ? $" position={source.Position.ToString(CultureInfo.InvariantCulture)}"
                            : ""
                    );
            }

            if (ResendTo is not null)
            {
                source.Position = 0;
                text += $" resent={await WriteAsync(backend, id, ResendTo, source, null, ct)}";
            }

            return text;
        }
    }

    /// <summary>渡す Stream と length を作る</summary>
    private (Stream Source, long? Length) Open()
    {
        switch (Source)
        {
            case ParityBlobSource.Seekable:
                return (new MemoryStream(Data ?? throw MissingData()), Length);
            case ParityBlobSource.NonSeekable:
                return (
                    new BinaryLengthMismatchCases.NonSeekableStream(Data ?? throw MissingData()),
                    Length
                );
            default:
            {
                if (Data is not null || Length is not null)
                {
                    throw new InvalidOperationException(
                        $"{Source} takes its stream and length from BinaryLengthMismatchCases."
                    );
                }

                var index = Source - ParityBlobSource.MismatchSeekable;
                var (_, stream, length) = BinaryLengthMismatchCases.Create().ElementAt(index);

                return (stream, length);
            }
        }
    }

    private InvalidOperationException MissingData() => new($"{Source} needs data to write.");

    private static Task<bool> WriteAsync(
        ParityBackend backend,
        OrderIdValue id,
        string column,
        Stream source,
        long? length,
        CancellationToken ct
    ) =>
        column switch
        {
            "payload" => backend.Orders.WritePayloadAsync(id, source, length, ct),
            "thumb" => backend.Orders.WriteThumbAsync(id, source, length, ct),
            _ => throw new ArgumentException(column),
        };
}

/// <summary>
/// 注文の除外 blob をファイル糖衣（<c>Write…FromFileAsync</c> / <c>Read…ToFileAsync</c>）で書くか読む（EF Core は NotSupported）。
/// 一時ファイルは手の中で作って消す。読む戻り値は <c>read=True bytes=…</c>（書かれたファイルの 16 進。false のときファイルは作られない）
/// </summary>
public sealed record BlobFileOp(int OrderId, string Column, bool Write, byte[]? Data = null)
    : ParityOp
{
    public override string Name => $"BlobFile:{Column}:{(Write ? "Write" : "Read")}";

    public override bool Supports(ParityBackendKind kind) => ParityBackendKinds.HasBlobStream(kind);

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var directory = Directory.CreateTempSubdirectory("quicker-parity-blob-file");
        var path = Path.Combine(directory.FullName, "blob.bin");
        var id = OrderIdValue.Create(OrderId);

        try
        {
            if (Write)
            {
                await File.WriteAllBytesAsync(path, Data ?? [], ct);
                var written = Column switch
                {
                    "payload" => await backend.Orders.WritePayloadFromFileAsync(id, path, ct),
                    "thumb" => await backend.Orders.WriteThumbFromFileAsync(id, path, ct),
                    _ => throw new ArgumentException(Column),
                };

                return $"written={written}";
            }

            // 保存先のファイルを先に置く（読み取りは既にあるファイルを置き換える＝File.Replace の経路。古い中身が残らないことは
            // 読んだバイト列の期待が見る）
            await File.WriteAllBytesAsync(path, [0xEE, 0xEE], ct);

            var read = Column switch
            {
                "payload" => await backend.Orders.ReadPayloadToFileAsync(id, path, ct),
                "thumb" => await backend.Orders.ReadThumbToFileAsync(id, path, ct),
                _ => throw new ArgumentException(Column),
            };
            var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : [];

            return $"read={read} bytes={Convert.ToHexString(bytes)}";
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

/// <summary>
/// 注文 10 の payload の Stream アクセサを、EF Core の実装先でも呼ぶ（<see cref="ReadBlobOp"/> / <see cref="WriteBlobOp"/> は EF Core を
/// 外す。この手は EF Core が NotSupportedException で断ることそのものを見る）。EF Core 単独のサーバーのリモートでは流さない
/// （元のテストは直結の EF Core だけを見ていた。そのサーバーの blob のエンドポイントの応答は docs が決めていない）
/// </summary>
public sealed record ProbeBlobStreamOp(bool Write) : ParityOp
{
    public override string Name => $"ProbeBlobStream:{(Write ? "Write" : "Read")}";

    public override bool Supports(ParityBackendKind kind) =>
        kind != ParityBackendKind.RemoteEfCoreOnly;

    public override async Task<string> RunAsync(ParityBackend backend, CancellationToken ct)
    {
        var id = OrderIdValue.Create(10);

        if (Write)
        {
            using var source = new MemoryStream([0x01, 0x02, 0x03]);

            return $"written={await backend.Orders.WritePayloadAsync(id, source, null, ct)}";
        }

        using var destination = new MemoryStream();

        return $"read={await backend.Orders.ReadPayloadAsync(id, destination, ct)} bytes={Convert.ToHexString(destination.ToArray())}";
    }
}
