using System;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace QuickER.Tests.GeneratedRemoteServiceFixture;

/// <summary>
/// 生成サーバー（RemoteServiceFixture.RemoteServer.g.cs）の partial 拡張点 <c>OnServerError</c> のテスト実装。
/// </summary>
/// <remarks>
/// partial クラスの実装部は生成物と同一 namespace・同一アセンブリに置く必要があるため、
/// フォルダ規約（namespace はフォルダ追従）から意図的に外れて GeneratedRemoteServiceFixture
/// namespace を使う。フックが 500 経路で実際に発火することは
/// <c>RemoteServiceRuntimeTestsBase</c> のテスト 10 が検証し、このファイル自体が
/// 「利用者が partial 実装で拡張できる」ことのコンパイルレベルの実証を兼ねる。
/// フックが例外を投げても元の 500 応答が失われないこと（テスト 11）も検証するため、
/// 「投げるモード」を実装するが、静的フラグにすると並列実行される派生スイートへ漏れるので
/// リクエストヘッダでスコープする（＝当該リクエストだけが投げる）。
/// </remarks>
public static partial class GeneratedRemoteEndpoints
{
    /// <summary>このリクエストのフックで例外を投げることを要求するヘッダ名（テスト 11 用のスコープ手段）</summary>
    internal const string ThrowInHookHeaderName = "X-QuickER-Test-Hook-Throw";

    /// <summary>フックが呼ばれた回数（派生スイート間で共有されるため Interlocked で加算する）</summary>
    private static int _serverErrorHookCallCount;

    /// <summary>フックが例外を投げた回数（同上）</summary>
    private static int _serverErrorHookThrowCount;

    /// <summary>フックが呼ばれた回数を読み取る（テスト検証用）</summary>
    internal static int ServerErrorHookCallCount => Volatile.Read(ref _serverErrorHookCallCount);

    /// <summary>フックが例外を投げた回数を読み取る（テスト検証用）</summary>
    internal static int ServerErrorHookThrowCount => Volatile.Read(ref _serverErrorHookThrowCount);

    static partial void OnServerError(HttpContext context, Exception ex)
    {
        Interlocked.Increment(ref _serverErrorHookCallCount);

        if (!context.Request.Headers.ContainsKey(ThrowInHookHeaderName))
        {
            return;
        }

        // 「フック実装が失敗しても元の 500 応答（RemoteError JSON）は失われない」ことを検証するための故意の失敗
        Interlocked.Increment(ref _serverErrorHookThrowCount);
        throw new InvalidOperationException("Test hook failure.");
    }
}
