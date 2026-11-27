using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 台帳の控え「遅れている blob」と、遅れている blob の行を主キーの付け替えで動かしたときの blob の期待（判定役の J1）。
/// </summary>
/// <remarks>
/// <para>
/// 遅れている blob：端末の品目の行はサーバーから届いたが、blob はまだ写していない（写し待ち）品目。端末の blob はサーバーの
/// blob の代わりの値（取得が落ちた新しい行は空、書き直された行は前の blob）で、端末の書いた内容ではない。台帳の規則どおり、
/// 端末の状態の表は読まず、実行の前後の DB・持ち分・失敗の注入の控えだけで決める。
/// </para>
/// <list type="bullet">
///   <item>立てる：端末の実行の終わり（例外で終わった実行を含む）に、その実行の範囲に品目の表があり、端末とサーバーの両方に
///   その品目があって blob が違い、端末がその品目の行にも blob にも持ち分を持たず、次のどちらかが当たったとき。(a) 降ろした行の
///   blob を写す実行（blob を運ぶ同期・blob を捨てない洗い替え）の取得がその品目の行を返し、実行の後の端末の行がサーバーの行と
///   同じ（行は届いたが、blob の写しが落ちたか、別の品目の写しの失敗で試される前に止まった）。取得が返した行は、サーバー役の
///   取得の口（<see cref="SyncModelFaults.FetchedItemsSince"/>）で控える（転送に依らない）。(b) 版のガードを外してサーバーへ書く
///   実行（LocalWins・後勝ち）が、実行の始めに端末が持ち分を持っていたその品目の行を届けた（上書きは送らなかった blob を写し待ちに
///   する）。</item>
///   <item>外す：端末の実行の終わりに端末とサーバーの blob が同じになった／端末にその品目が無い／端末が記録つきの書き込みで
///   その品目の blob を書いた・行を消した。</item>
///   <item>付け替え：遅れている blob の行（写す元のキーを控えに持つ）を動かすと、付け替え先のキーの blob の履歴へ、端末の値で
///   なく、付け替えの時点のサーバーの写す元のキーの blob を、付け替えた端末の書き込みとして積む（エンジンは送る前にサーバーの
///   写す元のキーから写す）。サーバーに写す元の品目が無ければ端末の値を積む。付け替え先は、同じ写す元のキーで遅れている blob に
///   なる（付け替えを重ねても元のキーが運ばれる）。付け替え先の blob の持ち分の内容も、端末の値でなくその送るべき blob に
///   する（端末の値は端末が書いた内容ではない）。</item>
///   <item>付け替えの後：ほかの書き手が写す元のキーへ書いた blob も、付け替え先の履歴へ積む（エンジンは写す時点の最新を写す）。
///   ほかの書き手が写す元のキーの品目を消したら、付け替えの時点の端末の値も積む（写す元が無く、端末の値を送る）。どちらも、
///   付け替えた端末の付け替え先の blob の持ち分の内容をその値へ変える。</item>
/// </list>
/// </remarks>
internal sealed partial class SyncModelRun
{
    /// <summary>（端末・品目のキー）→ 端末の blob の写す元のサーバーのキー（付け替えていなければ自分のキー）</summary>
    private readonly Dictionary<(ModelActor Device, string Key), string> _blobBehind = [];

    /// <summary>
    /// 遅れている blob の行の付け替え先のキー → 付け替えた端末・写す元のキー・付け替えの時点の端末の blob
    /// </summary>
    private readonly Dictionary<
        string,
        (ModelActor Device, string Origin, string? LocalHex)
    > _movedBehind = new(StringComparer.Ordinal);

    /// <summary>付け替えが動かす行の組（元のキー → 付け替え先のキー）。入れ替えは 2 組</summary>
    private static IReadOnlyList<(string From, string To)> RekeyMoves(RekeyItemOp rekey)
    {
        var from = ModelDbState.Key(rekey.From);
        var to = ModelDbState.Key(rekey.To);

        return rekey.Shape == RekeyShape.Swap ? [(from, to), (to, from)] : [(from, to)];
    }

    /// <summary>
    /// 成功した付け替えのうち、遅れている blob の行を動かす組（付け替え先のキー → 写す元のキーと送るべき blob）。写す元のキーは
    /// 書き込みの前の控えで決め、送るべき blob は付け替えの時点のサーバーの写す元のキーの blob（サーバーに写す元の品目が無ければ
    /// 付け替えた後の端末の値）
    /// </summary>
    /// <param name="actor">書き手</param>
    /// <param name="op">成功した書き込みの手（失敗した手は null）</param>
    /// <param name="after">書き込みの後の書き手の DB</param>
    private async Task<Dictionary<string, (string Origin, string Blob)>> BehindMoveBlobsAsync(
        ModelActor actor,
        ModelWriteOp? op,
        ModelDbState after
    )
    {
        var moves = new Dictionary<string, (string Origin, string Blob)>(StringComparer.Ordinal);

        if (op is not RekeyItemOp rekey || actor == ModelActor.Server)
        {
            return moves;
        }

        var origins = RekeyMoves(rekey)
            .Where(move => _blobBehind.ContainsKey((actor, move.From)))
            .Select(move => (move.To, Origin: _blobBehind[(actor, move.From)]))
            .ToList();

        if (origins.Count == 0)
        {
            return moves;
        }

        var serverPayloads = (await _world.ReadStateAsync(ModelActor.Server, _ct)).Payloads();
        var localPayloads = after.Payloads();

        foreach (var (to, origin) in origins)
        {
            moves[to] = (
                origin,
                serverPayloads.TryGetValue(origin, out var hex)
                    ? hex
                    : localPayloads.GetValueOrDefault(to) ?? ""
            );
        }

        return moves;
    }

    /// <summary>
    /// 書き込みの手の後の blob の履歴：遅れている blob の行の付け替え先へ写す元のキーのサーバーの blob を積み、控えを動かす。
    /// ほかの書き手の写す元のキーへの書き込み・削除を付け替え先へ写す（<see cref="RecordChanges"/> から呼ぶ）
    /// </summary>
    /// <param name="actor">書き手</param>
    /// <param name="op">成功した書き込みの手（付け替えだけを見る。失敗した手は null）</param>
    /// <param name="moves">遅れている blob の行を動かす組（<see cref="BehindMoveBlobsAsync"/>）</param>
    /// <param name="before">書き込みの前の書き手の DB</param>
    /// <param name="after">書き込みの後の書き手の DB</param>
    private void NoteBlobBehindWrite(
        ModelActor actor,
        ModelWriteOp? op,
        IReadOnlyDictionary<string, (string Origin, string Blob)> moves,
        ModelDbState before,
        ModelDbState after
    )
    {
        var oldPayloads = before.Payloads();
        var newPayloads = after.Payloads();

        if (actor != ModelActor.Server)
        {
            // 端末が記録つきの書き込みで blob を書いた・行を消した品目は、端末の内容になる（付け替え先は下で立て直す）
            foreach (var key in oldPayloads.Keys.Union(newPayloads.Keys, StringComparer.Ordinal))
            {
                if (
                    !string.Equals(
                        oldPayloads.GetValueOrDefault(key),
                        newPayloads.GetValueOrDefault(key),
                        StringComparison.Ordinal
                    )
                )
                {
                    _blobBehind.Remove((actor, key));
                }
            }

            if (op is RekeyItemOp rekey)
            {
                foreach (var (from, _) in RekeyMoves(rekey))
                {
                    _blobBehind.Remove((actor, from));
                }

                foreach (var (to, moved) in moves)
                {
                    Append(_blobs, to, (actor, moved.Blob));
                    _blobBehind[(actor, to)] = moved.Origin;
                    _movedBehind[to] = (actor, moved.Origin, newPayloads.GetValueOrDefault(to));
                }
            }
        }

        // ほかの書き手が写す元のキーへ書いた blob・写す元の品目の削除を、付け替え先の履歴へ写す
        foreach (var (to, moved) in _movedBehind.Where(p => p.Value.Device != actor).ToList())
        {
            var oldHex = oldPayloads.GetValueOrDefault(moved.Origin);
            var newHex = newPayloads.GetValueOrDefault(moved.Origin);

            if (string.Equals(oldHex, newHex, StringComparison.Ordinal))
            {
                continue;
            }

            // 送るべき blob が変わる（付け替えた端末の付け替え先の blob の持ち分の内容も変える）
            var blob = newHex ?? moved.LocalHex;

            if (blob is null)
            {
                continue;
            }

            Append(_blobs, to, (newHex is not null ? actor : moved.Device, blob));

            if (_holdings.TryGetValue((moved.Device, PayloadTable, to), out var holding))
            {
                _holdings[(moved.Device, PayloadTable, to)] = holding with { Content = blob };
            }
        }
    }

    /// <summary>
    /// 遅れている blob の行の付け替え先の blob の持ち分で、その内容（送るべき blob）がまだサーバーの写す元のキーにあるか
    /// （照合 3 は消えたとみなさない。写しが落ちて例外で終わった実行の後も、次の実行が写して送る）
    /// </summary>
    /// <param name="device">端末</param>
    /// <param name="key">持ち分のキー</param>
    /// <param name="held">持ち分の内容</param>
    /// <param name="serverAfter">実行の後のサーバーの内容</param>
    private bool AwaitsBehindCopy(
        ModelActor device,
        (string Table, string Key) key,
        string? held,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter
    ) =>
        key.Table == PayloadTable
        && held is not null
        && _movedBehind.TryGetValue(key.Key, out var moved)
        && moved.Device == device
        && string.Equals(
            serverAfter.GetValueOrDefault((PayloadTable, moved.Origin)),
            held,
            StringComparison.Ordinal
        );

    /// <summary>
    /// 端末の実行の終わりに、遅れている blob の控えを立てて外す（<see cref="EndRunAsync"/> の最後に呼ぶ）
    /// </summary>
    /// <param name="frame">実行の前に控えた入力</param>
    /// <param name="serverAfter">実行の後のサーバーの内容</param>
    /// <param name="localAfter">実行の後の端末の内容</param>
    private void NoteBlobsBehind(
        RunFrame frame,
        IReadOnlyDictionary<(string Table, string Key), string> serverAfter,
        IReadOnlyDictionary<(string Table, string Key), string> localAfter
    )
    {
        var device = frame.Device;

        foreach (var key in _blobBehind.Keys.Where(k => k.Device == device).ToList())
        {
            var blob = (PayloadTable, key.Key);

            if (
                !localAfter.TryGetValue(blob, out var localHex)
                || string.Equals(
                    localHex,
                    serverAfter.GetValueOrDefault(blob),
                    StringComparison.Ordinal
                )
            )
            {
                _blobBehind.Remove(key);
            }
        }

        if (!frame.InScope(ModelTables.Items))
        {
            return;
        }

        // 版のガードを外してサーバーへ書く実行（LocalWins・後勝ち）は、届けた行の送らなかった blob を写し待ちにする
        var forced = frame.WritesServer && !frame.GuardsVersions && !frame.VersionedServerWins;

        // その実行の取得が端末へ返した品目（行の写しの前に失敗が当たって写しが試されなかった行も含む）
        var fetched = _world
            .Faults.FetchedItemsSince(frame.FetchedItemIndex)
            .Where(hit => hit.Device == device)
            .Select(hit => hit.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var ((table, key), localHex) in localAfter)
        {
            if (
                table != PayloadTable
                || !serverAfter.TryGetValue((PayloadTable, key), out var serverHex)
                || string.Equals(localHex, serverHex, StringComparison.Ordinal)
                || _holdings.ContainsKey((device, ModelTables.Items, key))
                || _holdings.ContainsKey((device, PayloadTable, key))
            )
            {
                continue;
            }

            if (
                (
                    frame.CopiesBlobs
                    && fetched.Contains(key)
                    && localAfter.TryGetValue((ModelTables.Items, key), out var localRow)
                    && string.Equals(
                        localRow,
                        serverAfter.GetValueOrDefault((ModelTables.Items, key)),
                        StringComparison.Ordinal
                    )
                ) || (forced && frame.Holdings.ContainsKey((ModelTables.Items, key)))
            )
            {
                _blobBehind.TryAdd((device, key), key);
            }
        }
    }
}
