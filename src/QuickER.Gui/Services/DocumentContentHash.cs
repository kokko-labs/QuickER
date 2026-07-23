using System.IO;
using System.Security.Cryptography;

namespace QuickER.Services;

/// <summary>ファイル内容の SHA-256（16 進文字列）を計算する共通ヘルパ</summary>
/// <remarks>
/// 外部変更検知（ステージ B）で「最終既知ハッシュ」と現ファイルの内容一致を判定する唯一の基準。
/// 読込・上書き保存直後のハッシュ記録（<see cref="ViewModels.MainViewModel"/>）と、
/// 監視サービス（<see cref="DocumentFileWatcher"/>）の変更検知の双方が同じ算出規則を共有する。
/// </remarks>
public static class DocumentContentHash
{
    /// <summary>ファイル内容の SHA-256（16 進大文字）を計算する（IO エラー時は null）</summary>
    /// <param name="path">対象ファイルのパス</param>
    /// <returns>16 進文字列のハッシュ。読み取りに失敗した場合は null</returns>
    public static string? TryCompute(string path)
    {
        try
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch
        {
            // ハッシュ計算の失敗は呼び出し側の主処理（保存・読込・監視）を妨げない
            return null;
        }
    }

    /// <summary>短いリトライを挟みつつファイル内容の SHA-256 を計算する（書き込み途中の共有違反対策）</summary>
    /// <param name="path">対象ファイルのパス</param>
    /// <param name="attempts">試行回数（既定 5）</param>
    /// <param name="delayMilliseconds">試行間の待機（既定 40ms）</param>
    /// <returns>16 進文字列のハッシュ。全試行が失敗した場合は null</returns>
    /// <remarks>
    /// 外部プロセス（MCP サーバ等）の保存直後は書き込みが未完了で共有違反（<see cref="IOException"/>）が
    /// 起こり得るため、監視サービスの発火時はこのリトライ付きで算出する。ファイルが存在しない場合は
    /// リトライしても無駄なため即座に null を返す。
    /// </remarks>
    public static string? TryComputeWithRetry(
        string path,
        int attempts = 5,
        int delayMilliseconds = 40
    )
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            // ファイルが消えている（削除・リネーム）ならリトライしても無駄なので即座に諦める
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch (IOException)
            {
                // 書き込み途中の共有違反。少し待って再試行する
                Thread.Sleep(delayMilliseconds);
            }
            catch (UnauthorizedAccessException)
            {
                // 一時的なアクセス拒否も同様にリトライ対象とする
                Thread.Sleep(delayMilliseconds);
            }
        }

        return null;
    }
}
