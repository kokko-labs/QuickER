using System;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// リモートの書き込みの応答が版をクライアントへ返す経路（版の表と列の値）のうち、クライアントに読ませるもの。
/// </summary>
/// <remarks>
/// <para>
/// Insert / Update / Save / SaveMany の応答は、保存で採番された版を 2 つの経路で運ぶ。
/// 版の表（<c>RemoteInsertResult.RowVersion</c>・<c>RemoteUpdateResult.RowVersion</c>・<c>RemoteSaveResult.RowVersions</c>）と、
/// 保存した行の列の値（<c>Values</c>・<c>Saved</c>・<c>Unsaved</c>）。クライアントは版の表を書いた後に列の値を書くので、
/// 同じ版の生成物どうしでは列の値が勝ち、版の表は冗長になる。
/// </para>
/// <para>
/// 版の表は、以前のサーバー（列の値を運ばない）・以前のクライアント（列の値を読み捨てる）とのワイヤ互換の経路
/// （<c>src/QuickER.Runtime.AspNetCore/CLAUDE.md</c>「保存の応答は列の値を運び位置で突き合わせる」）。
/// 片方だけを外す退行は通常の応答では答えが変わらないので、応答から片方を外してもう片方だけで版が届くことを確かめる。
/// </para>
/// </remarks>
public enum RemoteResponseChannel
{
    /// <summary>応答をそのまま読む</summary>
    Both,

    /// <summary>
    /// 列の値（<c>Values</c>・<c>Saved</c>・<c>Unsaved</c>）を外し、版の表だけを読ませる
    /// （以前のサーバーの応答を新しいクライアントが読む形。サーバーの版の表の収集と、クライアントの版の表の書き戻しを通す）。
    /// </summary>
    VersionTableOnly,

    /// <summary>版の表（<c>RowVersions</c>・<c>RowVersion</c>）を空にし、列の値だけを読ませる（列の値が版を運ぶことを確かめる）</summary>
    ValuesOnly,
}

/// <summary>
/// クライアントの HTTP の応答のうち、書き込みの成功の応答から <see cref="RemoteResponseChannel"/> に従って片方の経路を外すハンドラー。
/// </summary>
/// <remarks>
/// 対象は要求のパスの最後の区切りが Insert・Update・Save・SaveMany の 2xx の応答だけ（409 などの失敗の本文と、
/// 読み取りの応答には触らない）。外す欄が実際に応答にあった回数を <see cref="Rewritten"/> に数える
/// （欄の名前が変わって何も外さないまま緑になる空振りを、呼び出し側が表明できるようにする）。
/// </remarks>
public sealed class RemoteResponseChannelHandler : DelegatingHandler
{
    private int _rewritten;

    /// <summary>既定の送信のハンドラーの上に置く</summary>
    public RemoteResponseChannelHandler()
        : base(new SocketsHttpHandler()) { }

    /// <summary>クライアントに読ませる経路（既定はそのまま）</summary>
    public RemoteResponseChannel Channel { get; set; } = RemoteResponseChannel.Both;

    /// <summary>外す欄が応答にあり、実際に外した応答の数</summary>
    public int Rewritten => Volatile.Read(ref _rewritten);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (
            Channel == RemoteResponseChannel.Both
            || !response.IsSuccessStatusCode
            || !IsWrite(request.RequestUri)
        )
        {
            return response;
        }

        var body = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (JsonNode.Parse(body) is not JsonObject json)
        {
            return response;
        }

        if (Strip(json, Channel))
        {
            Interlocked.Increment(ref _rewritten);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        response.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, mediaType);
        return response;
    }

    /// <summary>要求が書き込みのエンドポイント（Insert・Update・Save・SaveMany）か</summary>
    private static bool IsWrite(Uri? uri)
    {
        var path = uri?.AbsolutePath ?? string.Empty;
        var operation = path[(path.LastIndexOf('/') + 1)..];

        return operation is "Insert" or "Update" or "Save" or "SaveMany";
    }

    /// <summary>応答の本文から、読ませない経路の欄を外す（外す欄が 1 つでもあれば true）</summary>
    private static bool Strip(JsonObject json, RemoteResponseChannel channel)
    {
        var stripped = false;

        if (channel == RemoteResponseChannel.VersionTableOnly)
        {
            // 以前のサーバーの応答は列の値の欄を持たない
            foreach (var name in new[] { "Values", "Saved", "Unsaved" })
            {
                stripped |= json.Remove(name);
            }

            return stripped;
        }

        // 版の表は空の一覧（Save / SaveMany）か null（Insert / Update）にする
        if (json.ContainsKey("RowVersions"))
        {
            json["RowVersions"] = new JsonArray();
            stripped = true;
        }

        if (json.ContainsKey("RowVersion"))
        {
            json["RowVersion"] = null;
            stripped = true;
        }

        return stripped;
    }
}
