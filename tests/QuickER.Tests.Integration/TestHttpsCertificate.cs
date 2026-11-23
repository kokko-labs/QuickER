using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace QuickER.Tests.Integration;

/// <summary>
/// in-process の Kestrel を HTTPS で待ち受けるための、テストの中で作る自己署名の証明書と、それだけを受け入れるクライアントの設定。
/// </summary>
/// <remarks>
/// <para>
/// HTTPS で Kestrel の既定（HTTP/1.1 と HTTP/2）を ALPN で交渉する構成を流すために使う（利用者が HTTPS のサーバーで HTTP/2 を使う形）。
/// <c>dotnet dev-certs</c> の開発用の証明書は使わない（CI の ubuntu に無く、開発機の証明書ストアと信頼の操作に触れるため）。
/// 証明書はプロセスで 1 つを作って使い回し、ストアには入れない。クライアントはその証明書の拇印だけを受け入れる。
/// </para>
/// <para>
/// <see cref="CertificateRequest.CreateSelfSigned"/> の証明書は鍵が一時鍵で、Windows の SslStream はそのままでは
/// サーバーの証明書に使えないので、PFX へ書き出して読み直す。
/// </para>
/// </remarks>
internal static class TestHttpsCertificate
{
    private static readonly Lazy<X509Certificate2> LazyCertificate = new(Create);

    /// <summary>サーバーの証明書（SAN は <c>127.0.0.1</c> と <c>localhost</c>・有効期間は作った時点の前後 1 日）</summary>
    public static X509Certificate2 Certificate => LazyCertificate.Value;

    /// <summary>
    /// Kestrel を <c>https://127.0.0.1:0</c> で待ち受け、HTTPS の既定の証明書をこの証明書にする（<c>Protocols</c> は設定しない＝
    /// Kestrel の既定の HTTP/1.1 と HTTP/2 を ALPN で交渉する）。<see cref="InProcessRemoteServer.StartAsync"/> の
    /// <c>configure</c> から呼ぶ。
    /// </summary>
    public static void UseOn(WebApplicationBuilder builder)
    {
        builder.WebHost.UseUrls("https://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = Certificate)
        );
    }

    /// <summary>この証明書だけを受け入れるハンドラーを作る</summary>
    public static SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();
        Trust(handler);
        return handler;
    }

    /// <summary>ハンドラーが、サーバーの証明書としてこの証明書だけを受け入れるようにする</summary>
    public static void Trust(SocketsHttpHandler handler)
    {
        var thumbprint = Certificate.Thumbprint;
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null
            && string.Equals(
                certificate.GetCertHashString(),
                thumbprint,
                StringComparison.OrdinalIgnoreCase
            );
    }

    /// <summary>この証明書だけを受け入れ、要求を HTTP/2 で出す（交渉できなければ HTTP/1.1 に戻る）HttpClient を作る</summary>
    /// <param name="baseAddress">ベースアドレス</param>
    public static HttpClient CreateHttp2Client(Uri baseAddress) =>
        new(CreateHandler())
        {
            BaseAddress = baseAddress,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

    private static X509Certificate2 Create()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=QuickER test server",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], // サーバー認証
                false
            )
        );

        var now = DateTimeOffset.UtcNow;
        using var ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));

        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
    }
}
