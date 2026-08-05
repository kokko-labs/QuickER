using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace QuickER.Services;

/// <summary>
/// 未捕捉例外のクラッシュ処理（緊急保存 → クラッシュログ → 報告ダイアログ）を統括する静的サービス。
/// </summary>
/// <remarks>
/// <para>
/// UI・DI に依存しないよう、緊急保存とダイアログ表示は <see cref="Action"/> として注入する
/// （App.xaml.cs は購読とデリゲート結線のみを担い、順序・失敗時の振る舞いはここで完結してテストできる）。
/// </para>
/// <para>
/// クラッシュログ本文は不具合報告に添える機械向け診断のため、UI 言語に追従させず英語で固定する。
/// </para>
/// </remarks>
public static class CrashHandlingService
{
    /// <summary>クラッシュログの保存先フォルダ名（<c>%APPDATA%\QuickER</c>＝設定ストアと同じ規約）</summary>
    private const string LogFolderName = "QuickER";

    /// <summary>バージョンを解決できなかった場合に記録する代替表記</summary>
    private const string UnknownVersion = "unknown";

    /// <summary>クラッシュ処理の再入ガード（0 = 未処理・1 = 処理中もしくは処理済み）</summary>
    /// <remarks>
    /// クラッシュ処理そのものが投げた二次例外や、複数スレッドからの同時発火で
    /// 保存・ダイアログが多重に走らないようにする（最初の 1 回だけを通す）。
    /// </remarks>
    private static int _crashHandled;

    /// <summary>実行中アセンブリのバージョン文字列を解決する（取得できない場合は <c>unknown</c>）</summary>
    /// <remarks>
    /// 報告時にコミットまで特定できるよう、ビルドメタデータ（<c>+コミットハッシュ</c>）は
    /// 除去せずそのまま用いる（NuGet 版の案内とは目的が異なる）。
    /// </remarks>
    public static string ResolveAppVersion()
    {
        var informationalVersion = (
            Assembly.GetEntryAssembly() ?? typeof(CrashHandlingService).Assembly
        )
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? UnknownVersion
            : informationalVersion;
    }

    /// <summary>例外の詳細（発生時刻・バージョン・例外連鎖＋スタックトレース）を報告用テキストへ整形する</summary>
    /// <remarks>副作用を持たない純関数（ダイアログの詳細欄とログファイルで同じ本文を使う）</remarks>
    /// <param name="ex">整形対象の例外</param>
    /// <param name="version">アプリのバージョン文字列</param>
    /// <returns>報告用の整形済みテキスト（英語固定）</returns>
    public static string FormatDetails(Exception ex, string version)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var builder = new StringBuilder();
        builder.AppendLine("QuickER crash report");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Time: {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}"
        );
        builder.AppendLine(CultureInfo.InvariantCulture, $"Version: {version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Runtime: {Environment.Version}");
        builder.AppendLine();

        // 例外連鎖を外側から内側へ辿って全段を出す（根本原因は最内段にあることが多い）
        var current = ex;
        var depth = 0;

        while (current is not null)
        {
            var label = depth == 0 ? "Exception" : $"Inner exception ({depth})";
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"[{label}] {current.GetType().FullName}: {current.Message}"
            );
            builder.AppendLine(current.StackTrace ?? "(no stack trace)");
            builder.AppendLine();

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }

    /// <summary>クラッシュログを <c>%APPDATA%\QuickER\crash-yyyyMMdd-HHmmss-fff.log</c> へ書き出す</summary>
    /// <remarks>
    /// <para>
    /// クラッシュ処理の途中で失敗しても後続（ダイアログ表示・終了）を止めないよう、
    /// 例外は外へ漏らさず null を返す。
    /// </para>
    /// <para>
    /// ファイル名はミリ秒まで含め、それでも衝突する場合は短縮 GUID を付けて別名にする。
    /// <see cref="HandleCrash"/> 経由は再入ガードで 1 回きりだが、未観測タスク例外
    /// （<c>TaskScheduler.UnobservedTaskException</c>）はガード外で 1 回の GC から連続発火しうるため、
    /// 秒精度のままだと同一秒の後勝ち上書きで証跡が失われる（＝こちらが主経路）。
    /// </para>
    /// </remarks>
    /// <param name="ex">記録する例外</param>
    /// <param name="version">アプリのバージョン文字列</param>
    /// <param name="baseDirOverride">保存先フォルダ（テスト隔離用。null なら <c>%APPDATA%\QuickER</c>）</param>
    /// <returns>書き出したログのフルパス。失敗した場合は null</returns>
    public static string? WriteCrashLog(
        Exception ex,
        string version,
        string? baseDirOverride = null
    )
    {
        try
        {
            var folder = baseDirOverride ?? DefaultLogFolder();
            Directory.CreateDirectory(folder);

            var timestamp = DateTime.Now.ToString(
                "yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture
            );
            var path = Path.Combine(folder, $"crash-{timestamp}.log");

            // 同一ミリ秒での連続発火に備え、既存パスなら短縮 GUID を足して別ファイルへ逃がす
            if (File.Exists(path))
            {
                path = Path.Combine(
                    folder,
                    $"crash-{timestamp}-{Guid.NewGuid().ToString("N")[..8]}.log"
                );
            }

            File.WriteAllText(path, FormatDetails(ex, version));

            return path;
        }
        catch
        {
            // 書き込み不能（権限・ディスク etc.）でもクラッシュ処理を継続させる
            return null;
        }
    }

    /// <summary>クラッシュ時の一連の処理（緊急保存 → ログ → ダイアログ）を既定の順序で実行する</summary>
    /// <remarks>
    /// 各段は個別に try/catch で包み、二次例外が後続の段を止めないようにする
    /// （ログが書けなくてもダイアログは出す・保存に失敗しても報告はできる）。
    /// 2 回目以降の呼び出しは再入ガードにより何もしない。
    /// </remarks>
    /// <param name="ex">発生した未捕捉例外</param>
    /// <param name="version">アプリのバージョン文字列</param>
    /// <param name="emergencySave">編集内容の緊急保存処理</param>
    /// <param name="showDialog">報告ダイアログの表示処理（引数はログのフルパス。書けなかった場合は null）</param>
    /// <param name="logBaseDirOverride">ログの保存先フォルダ（テスト隔離用。null なら既定フォルダ）</param>
    public static void HandleCrash(
        Exception ex,
        string version,
        Action emergencySave,
        Action<string?> showDialog,
        string? logBaseDirOverride = null
    )
    {
        // 再入ガード。クラッシュ処理中に発生した二次例外で無限ループしないよう、最初の 1 回だけ通す
        if (Interlocked.Exchange(ref _crashHandled, 1) != 0)
        {
            return;
        }

        // 編集内容の退避を最優先で行う（この後の段で何が起きてもユーザーの作業は残る）
        try
        {
            emergencySave();
        }
        catch
        {
            // 壊れた状態からの保存失敗は許容し、ログ・報告へ進む
        }

        string? logPath = null;

        try
        {
            logPath = WriteCrashLog(ex, version, logBaseDirOverride);
        }
        catch
        {
            // WriteCrashLog は自前で握り潰すが、契約が変わっても後続を止めないよう二重に守る
        }

        try
        {
            showDialog(logPath);
        }
        catch
        {
            // ダイアログを出せない状況（UI が既に壊れている等）でも終了処理へ進ませる
        }
    }

    /// <summary>クラッシュログの既定保存先（<c>%APPDATA%\QuickER</c>）を返す</summary>
    private static string DefaultLogFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            LogFolderName
        );

    /// <summary>再入ガードを初期化する（テスト専用。複数ケースで <see cref="HandleCrash"/> を検証するため）</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _crashHandled, 0);
}
