using System.Diagnostics;
using System.IO;
using System.Text;

namespace QuickER.AI;

/// <summary>
/// Windows の <c>.cmd</c> / <c>.bat</c> シム（npm 製 CLI の典型的な入り方）を起動するときの、
/// コマンド挿入に対する共有ガード。各 CLI バックエンド（codex / claude / copilot）が共有する。
/// </summary>
/// <remarks>
/// <para>
/// <c>ProcessStartInfo.FileName</c> が <c>.cmd</c> / <c>.bat</c> のとき、CreateProcess は内部で
/// <c>cmd.exe</c> を経由する。.NET が組み立てるコマンドラインは C ランタイムの引用規約に従うが、
/// cmd はそれを自分の構文規則で**もう一度**解釈するため、引数に含まれる <c>"</c> や <c>&amp;</c> が
/// 引用の外へ抜けて任意コマンドの実行につながる。これを塞ぐため、シムのときは自分で
/// <c>cmd.exe /d /s /c</c> 形式へ包み直す（<c>/d</c> は AutoRun レジストリコマンドの実行を抑止し、
/// <c>/s</c> は外側の引用符で囲んだ全体を 1 つのコマンド行として扱わせて引用の解釈を決定的にする）。
/// </para>
/// <para>
/// 入口が 2 つあるのは**引数の形が違えば取り得る防御も違う**ため:
/// </para>
/// <list type="bullet">
/// <item>
/// <see cref="WrapCommandLine"/> ＝引数が連結済みの 1 本の文字列。トークン境界が分からないので
/// 個別に引用できず、cmd が解釈するメタ文字（<c>" &amp; | &lt; &gt; ^ %</c>）を含んでいたら起動前に拒否する
/// </item>
/// <item>
/// <see cref="Apply"/> ＝<c>ProcessStartInfo.ArgumentList</c>。トークン境界が分かるので各トークンを
/// 二重引用符で包み、内側の <c>"</c> を <c>""</c> へ倍加できる。引用の内側では <c>&amp; | &lt; &gt; ^ ( )</c> は
/// cmd にとってただの文字になるため、残る危険は <c>%</c> の環境変数展開（展開結果が <c>"</c> を
/// 持ち込んで引用を破れる）だけで、これだけを拒否する。遅延展開の <c>!</c> は <c>/v:off</c> を
/// 明示して無効化するので拒否対象にしない
/// </item>
/// </list>
/// <para>
/// パス側はどちらの入口でも <c>"</c> を拒否する（正規の Windows パスには現れない文字で、
/// 含まれていれば引用の切断を狙ったもの）。**改行は引数・パスのどちらでも拒否する**＝npm の
/// cmd-shim（<c>"%~dp0bin\claude.exe" %*</c> 形）では cmd が <c>%*</c> を展開する段階で最初の
/// 改行より後ろを捨てるため、引用しても黙った切り捨てを防げない（<see cref="EnsureNoNewline"/>）。
/// 複数行の内容はファイル経由で渡す（claude なら <c>--append-system-prompt-file</c>）。
/// </para>
/// <para>
/// 文言（resx）は呼び出し側が渡す。どの CLI の起動が止まったのかをメッセージが名指しできるよう、
/// ポリシーだけをここに集約し、表示文字列は各バックエンドの resx に残す。
/// </para>
/// </remarks>
public static class BatchShimProcessGuard
{
    /// <summary>引数を 1 本の文字列として受けるときに拒否する、cmd が解釈するメタ文字</summary>
    private static readonly char[] CmdMetaCharacters = ['"', '&', '|', '<', '>', '^', '%'];

    /// <summary>引数・パスのどちらでも拒否する改行（cmd の <c>%*</c> 展開が最初の 1 つで行を切る）</summary>
    private static readonly char[] NewlineCharacters = ['\r', '\n'];

    /// <summary>
    /// 包み直しに使う <c>cmd.exe</c> のフルパス（システムフォルダ直下）。
    /// </summary>
    /// <remarks>
    /// 素の <c>"cmd.exe"</c> を渡さないのは防御の二重化。実測（.NET 10・Windows 11）では
    /// <see cref="Process.Start()"/> は名前だけの <c>FileName</c> をカレントフォルダ・<c>WorkingDirectory</c> から
    /// 探さないが、同名の exe を置かれ得る場所から拾う余地を探索規則の実装に委ねず、起動する実体を固定する。
    /// </remarks>
    public static string CmdExePath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>指定パスが <c>.cmd</c> / <c>.bat</c> シムか（拡張子で判定・大文字小文字は無視）</summary>
    /// <param name="executablePath">判定する実行ファイルパス</param>
    public static bool IsBatchShim(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return false;
        }

        var extension = Path.GetExtension(executablePath);

        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 連結済みの引数文字列を持つ起動を、必要なら <c>cmd.exe</c> 経由へ包み直す
    /// （シムでなければ引数をそのまま返す）。
    /// </summary>
    /// <param name="resolvedPath">解決済みの実行ファイルパス（拡張子で判定するため実体のパスを渡す）</param>
    /// <param name="arguments">連結済みの引数文字列</param>
    /// <param name="pathQuoteMessageFormat">パスに引用符があるときのメッセージ書式（<c>{0}</c>＝パス）</param>
    /// <param name="metaCharMessageFormat">
    /// 引数にメタ文字があるときのメッセージ書式（<c>{0}</c>＝検出文字・<c>{1}</c>＝引数全体）
    /// </param>
    /// <param name="newlineMessageFormat">
    /// 引数またはパスに改行があるときのメッセージ書式（<c>{0}</c>＝該当する値）
    /// </param>
    /// <returns>起動に使う（実行ファイル名, 引数文字列）</returns>
    /// <exception cref="InvalidOperationException">
    /// パスに引用符、引数に cmd のメタ文字、または引数・パスに改行が含まれる場合
    /// </exception>
    public static (string FileName, string Arguments) WrapCommandLine(
        string resolvedPath,
        string arguments,
        string pathQuoteMessageFormat,
        string metaCharMessageFormat,
        string newlineMessageFormat
    )
    {
        if (!IsBatchShim(resolvedPath))
        {
            return (resolvedPath, arguments);
        }

        EnsurePathHasNoQuote(resolvedPath, pathQuoteMessageFormat);
        EnsureNoNewline(resolvedPath, newlineMessageFormat);

        // 改行は引用しても防げない（cmd が %* を展開する段階で行ごと切る）ため、メタ文字より先に見る
        EnsureNoNewline(arguments, newlineMessageFormat);

        // 引数側は cmd のメタ文字（引用符・連結・リダイレクト・エスケープ・環境変数展開）が
        // 引用の外で解釈されコマンド挿入につながるため、含まれていたら起動前に拒否する
        var metaIndex = arguments.IndexOfAny(CmdMetaCharacters);

        if (metaIndex >= 0)
        {
            throw new InvalidOperationException(
                string.Format(metaCharMessageFormat, arguments[metaIndex], arguments)
            );
        }

        // バッチファイルは cmd.exe /c 経由で起動しないとリダイレクトが機能しない
        return (CmdExePath, $"/d /s /c \"\"{resolvedPath}\" {arguments}\"");
    }

    /// <summary>
    /// <see cref="ProcessStartInfo.ArgumentList"/> 形式の起動情報を、<c>FileName</c> がシムのときだけ
    /// <c>cmd.exe</c> 経由へ書き換える（シムでなければ何もしない）。
    /// </summary>
    /// <remarks>
    /// 書き換え後は <see cref="ProcessStartInfo.Arguments"/> に文字列を設定するため
    /// <see cref="ProcessStartInfo.ArgumentList"/> を空にする（両方を設定すると
    /// <see cref="Process.Start()"/> が例外を投げる）。
    /// </remarks>
    /// <param name="startInfo">書き換える起動情報</param>
    /// <param name="pathQuoteMessageFormat">パスに引用符があるときのメッセージ書式（<c>{0}</c>＝パス）</param>
    /// <param name="envExpansionMessageFormat">
    /// 引数に <c>%</c> があるときのメッセージ書式（<c>{0}</c>＝該当引数）
    /// </param>
    /// <param name="newlineMessageFormat">
    /// 引数またはパスに改行があるときのメッセージ書式（<c>{0}</c>＝該当する値）
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// パスに引用符、引数に <c>%</c>、または引数・パスに改行が含まれる場合
    /// </exception>
    public static void Apply(
        ProcessStartInfo startInfo,
        string pathQuoteMessageFormat,
        string envExpansionMessageFormat,
        string newlineMessageFormat
    )
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (!IsBatchShim(startInfo.FileName))
        {
            return;
        }

        var resolvedPath = startInfo.FileName;
        EnsurePathHasNoQuote(resolvedPath, pathQuoteMessageFormat);
        EnsureNoNewline(resolvedPath, newlineMessageFormat);

        var commandLine = new StringBuilder();
        commandLine.Append("/d /s /v:off /c \"\"").Append(resolvedPath).Append('"');

        foreach (var argument in startInfo.ArgumentList)
        {
            // 改行は引用の内側でも防げない（cmd が %* を展開する段階で行ごと切る）ため拒否する
            EnsureNoNewline(argument, newlineMessageFormat);

            // % は引用の内側でも展開され、展開結果が引用符を持ち込めば外へ抜けられるため拒否する
            if (argument.Contains('%', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    string.Format(envExpansionMessageFormat, argument)
                );
            }

            commandLine.Append(' ');
            AppendQuotedArgument(commandLine, argument);
        }

        commandLine.Append('"');

        startInfo.ArgumentList.Clear();
        startInfo.FileName = CmdExePath;
        startInfo.Arguments = commandLine.ToString();
    }

    /// <summary>実行ファイルパスに引用符が無いことを確かめる（あれば起動前に拒否する）</summary>
    /// <remarks>
    /// 引用符は正規の Windows パスには現れない文字で、含まれていれば引用の切断＝コマンド挿入を狙ったもの。
    /// </remarks>
    private static void EnsurePathHasNoQuote(string path, string messageFormat)
    {
        if (path.Contains('"', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(string.Format(messageFormat, path));
        }
    }

    /// <summary>引数・パスに改行が無いことを確かめる（あれば起動前に拒否する）</summary>
    /// <remarks>
    /// <para>
    /// npm の cmd-shim は <c>"%~dp0bin\claude.exe" %*</c> の形をしており、cmd はこの <c>%*</c> を
    /// 展開する段階で**最初の改行から後ろを捨てる**。捨てられるのはその引数の残りだけでなく、
    /// 後続の引数もすべてで、エラーにもならず黙って消える（実機のプローブで確認）。
    /// 引用しても止められない＝<c>cmd.exe /d /s /v:off /c</c> へ包み直す前の段階で起きるため、
    /// この層で拒否するしかない。
    /// </para>
    /// <para>
    /// 実害は「切り捨て」であってコマンド挿入ではないが、黙って落ちる分むしろ危うい
    /// （claude では複数行のシステムプロンプトが 1 行目で切れ、後ろに積んだ <c>--model</c> /
    /// <c>--resume</c> ごと消える＝毎ターン新規セッションになる）。複数行の内容を渡したいときは
    /// ファイル経由にする（claude なら <c>--append-system-prompt-file</c>）。
    /// </para>
    /// </remarks>
    private static void EnsureNoNewline(string value, string messageFormat)
    {
        if (value.IndexOfAny(NewlineCharacters) >= 0)
        {
            throw new InvalidOperationException(string.Format(messageFormat, value));
        }
    }

    /// <summary>
    /// 引数 1 つを、cmd と C ランタイムの両方が同じ 1 トークンとして読む形へ引用して追記する。
    /// </summary>
    /// <remarks>
    /// 二重引用符で必ず包む（空文字・空白を含む引数もそのまま 1 トークンになる）。内側の <c>"</c> は
    /// <c>""</c> へ倍加する＝cmd にとっては「引用を閉じて即座に開き直す」ので引用の外にメタ文字が
    /// 露出せず、C ランタイムにとっては引用符 1 文字のエスケープとして読める。<c>"</c> の直前の
    /// 連続する <c>\</c> は C ランタイムの規約に合わせて倍にする（倍にしないと <c>\"</c> が
    /// エスケープと解釈されて引用が 1 つずれる）。
    /// </remarks>
    private static void AppendQuotedArgument(StringBuilder builder, string argument)
    {
        builder.Append('"');

        var backslashes = 0;

        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                builder.Append(character);
                continue;
            }

            if (character == '"')
            {
                // 直前の \ を倍にしてから "" を出す（1 つ目はこの Append、2 つ目は下の Append）
                builder.Append('\\', backslashes);
                builder.Append('"');
            }

            backslashes = 0;
            builder.Append(character);
        }

        // 閉じ引用符の直前の \ も倍にする（そのままだと閉じ引用符がエスケープされる）
        builder.Append('\\', backslashes);
        builder.Append('"');
    }
}
