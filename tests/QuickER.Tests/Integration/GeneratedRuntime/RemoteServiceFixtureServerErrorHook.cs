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
/// </remarks>
public static partial class GeneratedRemoteEndpoints
{
    /// <summary>フックが呼ばれた回数（派生スイート間で共有されるため Interlocked で加算する）</summary>
    private static int _serverErrorHookCallCount;

    /// <summary>フックが呼ばれた回数を読み取る（テスト検証用）</summary>
    internal static int ServerErrorHookCallCount => Volatile.Read(ref _serverErrorHookCallCount);

    static partial void OnServerError(HttpContext context, Exception ex)
    {
        Interlocked.Increment(ref _serverErrorHookCallCount);
    }
}
