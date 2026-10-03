using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace QuickER.Services;

/// <summary>
/// 未捕捉例外のクラッシュ処理（緊急保存 → クラッシュログ → 機能モジュールの後始末 → 報告ダイアログ）を統括する静的サービス。
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
    /// <summary>クラッシュログの保存先フォルダ名（<c>%LOCALAPPDATA%\QuickER</c>＝設定ストアと同じ規約）</summary>
    private const string LogFolderName = "QuickER";

    /// <summary>バージョンを解決できなかった場合に記録する代替表記</summary>
    private const string UnknownVersion = "unknown";

    /// <summary>ログファイル名の既定の接頭辞（＝クラッシュの記録）</summary>
    /// <remarks>
    /// 終了処理の失敗など、アプリが落ちていない記録は別の接頭辞で書き分ける
    /// （<c>crash-*.log</c> が増えていると「落ちた」と読み違えるため）。
    /// </remarks>
    public const string CrashLogPrefix = "crash";

    /// <summary>アプリ終了の連鎖で受け止めた失敗を記録するログファイル名の接頭辞</summary>
    public const string ShutdownLogPrefix = "shutdown";

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
        AppendException(builder, ex, depth: 0, member: null);

        return builder.ToString();
    }

    /// <summary>例外連鎖を辿る深さの上限（自己参照する独自例外などで無限に辿らないための保険）</summary>
    private const int MaxExceptionDepth = 64;

    /// <summary>例外 1 段とその内側（<see cref="AggregateException"/> なら全件）を報告用テキストへ追記する</summary>
    /// <remarks>
    /// <see cref="AggregateException"/> は <see cref="Exception.InnerException"/> が 1 件目しか指さないため、
    /// <see cref="AggregateException.InnerExceptions"/> を全件たどる（取りこぼした Task の例外は必ず Aggregate で、
    /// 2 件目以降に根本原因があることがある）。全件のときは見出しに「何件目か / 全件数」を添える。
    /// </remarks>
    /// <param name="builder">追記先</param>
    /// <param name="ex">追記する例外</param>
    /// <param name="depth">外側からの段数（0＝最外段）</param>
    /// <param name="member">Aggregate の何件目か（1 始まり）と全件数。Aggregate の要素でなければ null</param>
    private static void AppendException(
        StringBuilder builder,
        Exception ex,
        int depth,
        (int Index, int Count)? member
    )
    {
        var label = depth == 0 ? "Exception" : $"Inner exception ({depth})";

        if (member is { } position)
        {
            label += $" [{position.Index}/{position.Count}]";
        }

        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"[{label}] {ex.GetType().FullName}: {ex.Message}"
        );
        builder.AppendLine(ex.StackTrace ?? "(no stack trace)");
        builder.AppendLine();

        if (depth + 1 >= MaxExceptionDepth)
        {
            return;
        }

        if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count > 1)
        {
            var count = aggregate.InnerExceptions.Count;

            for (var i = 0; i < count; i++)
            {
                AppendException(builder, aggregate.InnerExceptions[i], depth + 1, (i + 1, count));
            }

            return;
        }

        if (ex.InnerException is not null)
        {
            AppendException(builder, ex.InnerException, depth + 1, member: null);
        }
    }

    /// <summary>診断ログを <c>%LOCALAPPDATA%\QuickER\{接頭辞}-yyyyMMdd-HHmmss-fff.log</c> へ書き出す</summary>
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
    /// <param name="baseDirOverride">保存先フォルダ（テスト隔離用。null なら <c>%LOCALAPPDATA%\QuickER</c>）</param>
    /// <param name="filePrefix">
    /// ファイル名の接頭辞（既定は <see cref="CrashLogPrefix"/>）。本文の形式は共有したまま、
    /// クラッシュ以外の記録（<see cref="ShutdownLogPrefix"/>）を別名で書き分けるために使う
    /// </param>
    /// <returns>書き出したログのフルパス。失敗した場合は null</returns>
    public static string? WriteCrashLog(
        Exception ex,
        string version,
        string? baseDirOverride = null,
        string filePrefix = CrashLogPrefix
    ) =>
        WriteCrashLog(
            ex,
            version,
            baseDirOverride,
            filePrefix,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
        );

    /// <summary>ファイル名の時刻部分を指定して診断ログを書き出す（同じ名前を選ぶ同時の書き手を再現するテスト用）</summary>
    /// <remarks>
    /// 新しいファイルとして作成（<see cref="FileMode.CreateNew"/>）し、既にあれば短縮 GUID 付きの名前でもう一度
    /// 作成する。「存在確認 → 上書き書き込み」の順では、確認と書き込みの間に同じ名前を選んだ別の書き手と
    /// 後勝ちになり、先の記録が消える。
    /// </remarks>
    internal static string? WriteCrashLog(
        Exception ex,
        string version,
        string? baseDirOverride,
        string filePrefix,
        string timestamp
    )
    {
        try
        {
            var folder = baseDirOverride ?? DefaultLogFolder();
            Directory.CreateDirectory(folder);

            var content = FormatDetails(ex, version);
            var path = Path.Combine(folder, $"{filePrefix}-{timestamp}.log");

            if (TryWriteNewFile(path, content))
            {
                return path;
            }

            // 同じ名前を既に誰かが作っていたら、短縮 GUID を足した別の名前へ逃がす
            path = Path.Combine(
                folder,
                $"{filePrefix}-{timestamp}-{Guid.NewGuid().ToString("N")[..8]}.log"
            );

            return TryWriteNewFile(path, content) ? path : null;
        }
        catch
        {
            // 書き込み不能（権限・ディスク etc.）でもクラッシュ処理を継続させる
            return null;
        }
    }

    /// <summary>ファイルを新規作成して本文を書く。同じ名前のファイルが既にあれば何もせず false</summary>
    /// <remarks>それ以外の失敗（権限・ディスク）は例外のまま呼び出し側へ返す</remarks>
    private static bool TryWriteNewFile(string path, string content)
    {
        FileStream stream;

        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }

        // File.WriteAllText と同じ BOM なし UTF-8 で書く
        using (stream)
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content);
        }

        return true;
    }

    /// <summary>
    /// UI スレッドの未捕捉例外を処理し、この呼び出しが処理を担ったときだけ <paramref name="exit"/> でプロセスを終える
    /// </summary>
    /// <remarks>
    /// 報告ダイアログは入れ子のメッセージループを回すため、表示中に UI 例外がもう一度起きると
    /// この経路へ再入する。再入した側は何もせず戻る（<paramref name="exit"/> を呼ばない）。
    /// 呼ぶと、最初の呼び出しが出している報告ダイアログを利用者が読む前に消してしまう。
    /// 終了は最初の呼び出しが、ダイアログを閉じた後に行う。
    /// </remarks>
    /// <param name="ex">発生した未捕捉例外</param>
    /// <param name="version">アプリのバージョン文字列</param>
    /// <param name="emergencySave">編集内容の緊急保存処理</param>
    /// <param name="closeFeatureModules">機能モジュールの後始末（AI の子プロセスの停止など）</param>
    /// <param name="showDialog">報告ダイアログの表示処理（引数はログのフルパス。書けなかった場合は null）</param>
    /// <param name="exit">プロセスを終える処理（戻らない想定）</param>
    /// <param name="logBaseDirOverride">ログの保存先フォルダ（テスト隔離用。null なら既定フォルダ）</param>
    public static void HandleUiCrash(
        Exception ex,
        string version,
        Action emergencySave,
        Action closeFeatureModules,
        Action<string?> showDialog,
        Action exit,
        string? logBaseDirOverride = null
    )
    {
        ArgumentNullException.ThrowIfNull(exit);

        if (
            HandleCrash(
                ex,
                version,
                emergencySave,
                closeFeatureModules,
                showDialog,
                logBaseDirOverride
            )
        )
        {
            exit();
        }
    }

    /// <summary>クラッシュ時の一連の処理（緊急保存 → ログ → 機能モジュールの後始末 → ダイアログ）を既定の順序で実行する</summary>
    /// <remarks>
    /// <para>
    /// 各段は個別に try/catch で包み、二次例外が後続の段を止めないようにする
    /// （ログが書けなくてもダイアログは出す・保存に失敗しても報告はできる）。
    /// 2 回目以降の呼び出しは再入ガードにより何もしない。
    /// </para>
    /// <para>
    /// 機能モジュールの後始末をダイアログより<b>前</b>に置くのは、プロセスを終える経路
    /// （<see cref="Environment.Exit(int)"/>）がメインウィンドウの <c>Closing</c> を通らず、ここで止めないと
    /// AI の子プロセスが孤児として残るため。ダイアログを読んでいる間にタスクマネージャーで落とされても
    /// 残らないよう、先に済ませる。後始末の各段の受け止めと待ちの上限は呼び出し側
    /// （<c>FeatureModuleShutdown</c>・各モジュールの終了処理）が持つ。
    /// </para>
    /// </remarks>
    /// <param name="ex">発生した未捕捉例外</param>
    /// <param name="version">アプリのバージョン文字列</param>
    /// <param name="emergencySave">編集内容の緊急保存処理</param>
    /// <param name="closeFeatureModules">機能モジュールの後始末（AI の子プロセスの停止など）</param>
    /// <param name="showDialog">報告ダイアログの表示処理（引数はログのフルパス。書けなかった場合は null）</param>
    /// <param name="logBaseDirOverride">ログの保存先フォルダ（テスト隔離用。null なら既定フォルダ）</param>
    /// <returns>この呼び出しが処理を担った場合 true。既に処理中（再入）または処理済みなら何もせず false</returns>
    public static bool HandleCrash(
        Exception ex,
        string version,
        Action emergencySave,
        Action closeFeatureModules,
        Action<string?> showDialog,
        string? logBaseDirOverride = null
    )
    {
        // 再入ガード。クラッシュ処理中に発生した二次例外で無限ループしないよう、最初の 1 回だけ通す
        if (Interlocked.Exchange(ref _crashHandled, 1) != 0)
        {
            return false;
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
            closeFeatureModules();
        }
        catch
        {
            // 後始末の各段は呼び出し側で受け止めるが、それでも漏れた失敗で報告を止めない
        }

        try
        {
            showDialog(logPath);
        }
        catch
        {
            // ダイアログを出せない状況（UI が既に壊れている等）でも終了処理へ進ませる
        }

        return true;
    }

    /// <summary>クラッシュログの既定保存先（<c>%LOCALAPPDATA%\QuickER</c>）を返す</summary>
    private static string DefaultLogFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LogFolderName
        );

    /// <summary>再入ガードを初期化する（テスト専用。複数ケースで <see cref="HandleCrash"/> を検証するため）</summary>
    internal static void ResetForTests() => Interlocked.Exchange(ref _crashHandled, 0);
}
