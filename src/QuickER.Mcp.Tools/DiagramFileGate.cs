namespace QuickER.Mcp.Tools;

/// <summary>
/// 同じプロセス内で、MCP ツールによる図ファイルの読み書きを 1 本ずつに並べるためのロック。
/// </summary>
/// <remarks>
/// <para>
/// MCP クライアントは 1 ターン内の独立したツール呼び出しを並列に送り、SDK はリクエストを別々のタスクで処理する
/// （stdio でも直列化の保証は無い）。変更系ツールは「読込 → 変更 → 保存」で完結するため、同じファイルへの
/// 呼び出しが重なると後から保存したほうが先の更新を消し、しかも両方が成功を返す。
/// </para>
/// <para>
/// 読み取りも同じロックに入れる。図の保存（<c>AtomicFile</c> による差し替え）と同じファイルの読み取りが重なると、
/// 読み取り側が共有違反（「別のプロセスが使用中」）で失敗するため（実測で確認）。
/// </para>
/// <para>
/// 粒度はファイル単位でなくプロセス全体で 1 つ。単一利用者の stdio サーバで並列度に意味が無く、ファイル単位にすると
/// パスの正規化（大文字小文字・相対パス・短い名前・リンク）を誤ったときに直列化が黙って外れるため。
/// 別プロセス（GUI・別の MCP サーバ）との競合は対象外（GUI は外部変更の検知で追従する）。
/// </para>
/// </remarks>
public static class DiagramFileGate
{
    /// <summary>プロセス全体で共有するロック</summary>
    private static readonly Lock Gate = new();

    /// <summary>ロックを取って処理を実行する（図ファイルを読む・書く処理はすべてこの中で行う）</summary>
    /// <typeparam name="T">処理の戻り値の型</typeparam>
    /// <param name="action">図ファイルを読み書きする処理</param>
    /// <returns>処理の戻り値</returns>
    public static T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (Gate)
        {
            return action();
        }
    }
}
