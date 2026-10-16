using System.IO;
using System.Security.AccessControl;
using System.Text;

namespace QuickER.Platform;

/// <summary>ファイル読み取りの結果分類（<see cref="AtomicFile.TryReadAllText"/> が返す）</summary>
/// <remarks>
/// <b>「読めなかった」を「空だった」と取り違えないための型。</b>設定・接続プロファイルの保存は
/// 「読み込み → 変更 → 書き込み」で、読みが失敗したときに既定値・空データへ黙ってフォールバックすると、
/// その空データがそのまま保存先へ書き戻って<b>登録済みの内容が全件消える</b>。
/// ファイルが無いこと（＝本当に空）と、あるのに読めなかったこと（＝中身は無事）は
/// 呼び出し側が必ず区別できなければならない。
/// </remarks>
public enum FileReadStatus
{
    /// <summary>読み取りに成功した</summary>
    Loaded,

    /// <summary>
    /// ファイル（またはその親フォルダ）が存在しない＝保存されたものが無い。
    /// 既定値・空データへフォールバックしてよい唯一の分類。
    /// </summary>
    Missing,

    /// <summary>
    /// ファイルはあるが読み取れなかった（別プロセスの排他・権限不足等）。
    /// <b>中身は無事なので、この分類を「空」と扱ってはいけない。</b>
    /// </summary>
    Unavailable,
}

/// <summary>ファイルを原子的に（書き込み途中の中断で保存先を壊さずに）書き出すユーティリティ</summary>
/// <remarks>
/// <para>
/// <b>原子的書き込みの単一正本。</b>図ファイル（<c>QuickER.Documents.JsonStorageService.SaveAtomic</c>）・
/// 設定ファイル（<c>QuickER.Settings.JsonSettingsStore</c>）・接続プロファイル
/// （<c>QuickER.Db.UI.SqlConnectionProfileStore</c>）はいずれもここを経由する。同じアルゴリズムを
/// 各所へ逐語コピーすると、一時ファイル名の付け方のような細部が 1 箇所だけ取り残されても
/// 揃っているかを保証する仕組みが無いため、実装はここ 1 箇所に閉じる。
/// </para>
/// <para>
/// 素の <see cref="File.WriteAllText(string, string?)"/> は既存ファイルを切り詰めてから書くため、
/// 途中でプロセスが落ちる・ディスクが満杯になると保存先が中途半端な内容（壊れた JSON）で残る。
/// 本クラスは一時ファイルへ全量を書き切り、<b>ディスクへフラッシュしてから</b>本体へ差し替えるため、
/// 保存先の内容は「書き込み前の内容そのまま」か「書き込み後の内容」のどちらかにしかならない
/// （＝中途半端な内容が残らない）。フラッシュを挟むのは、書き込みが OS のキャッシュに留まったまま
/// 差し替えだけが永続化されると、電源断後に「空または途中までの新ファイル」が残り得るため。
/// </para>
/// <para>
/// <b>耐久性の範囲:</b> 主張するのは<b>内容が中途半端な状態で残らない</b>ことまで。差し替え自体
/// （<see cref="File.Replace(string, string, string?)"/> / <see cref="File.Move(string, string, bool)"/> の
/// メタデータ更新）がいつ永続化されるかは OS の裁量なので、電源断の直後に残るのが新旧どちらの内容かは
/// 保証しない（どちらであっても完全な内容である、が保証の中身）。
/// </para>
/// <para>
/// <b>防げないもの:</b> 同一ファイルへの同時保存そのものは防がない。後から差し替えた側が勝つ
/// （ロストアップデート＝後勝ち）。目的はあくまで<b>破損の回避</b>であって、排他制御ではない。
/// 差し替え時の短時間リトライ（下記）も、他プロセスと衝突したときの<b>失敗率を下げる</b>だけで、
/// どちらの内容を残すかを調停するものではない（＝並行保存は後勝ちのまま）。
/// </para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>一過性の失敗に対する試行回数（初回＋リトライ 4 回）</summary>
    /// <remarks>
    /// 差し替え（<see cref="ReplaceWithRetry"/>）と読み取り（<see cref="TryReadAllText"/>）で共有する。
    /// 両者が相手にするのは同じ現象（別プロセスが同じファイルを掴んでいる短い瞬間）なので、
    /// 粘り方が片方だけ変わると「書けたのに読めない」のような非対称が静かに生まれる。
    /// </remarks>
    private const int RetryAttemptCount = 5;

    /// <summary>リトライの待ち時間（ミリ秒・試行ごとに 10ms ずつ延ばす＝合計 100ms 程度）</summary>
    /// <remarks>回数と同じ理由で差し替えと読み取りで共有する</remarks>
    private const int RetryDelayStepMilliseconds = 10;

    /// <summary>一時ファイル名の、元の名前の後ろに足す部分の長さ（<c>.</c>＋GUID の 32 桁＋<c>.tmp</c>）</summary>
    private const int TemporarySuffixLength = 37;

    /// <summary>ファイル名の 1 区間の上限（NTFS などの 255 文字）</summary>
    private const int MaxFileNameLength = 255;

    /// <summary>
    /// 保存先と同じフォルダに置く一時ファイルのパス（<c>{元の名前}.{GUID}.tmp</c>。元の名前は 1 区間の上限に収まるよう先頭を残して切る）
    /// </summary>
    /// <remarks>
    /// 元の名前をそのまま前に付けると、218 文字を超える名前の保存先では一時ファイルの名前が 255 文字を超え、
    /// 保存先そのものは作れるのに書き込みが必ず失敗する。切ったあとも、残った一時ファイルが何の保存のものかは
    /// 名前の先頭で分かる。名前の判定は <see cref="IsTemporaryFileName"/>。
    /// </remarks>
    /// <param name="path">保存先のパス</param>
    /// <returns>一時ファイルのパス</returns>
    public static string TemporaryPathFor(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var folder = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        var keep = MaxFileNameLength - TemporarySuffixLength;

        if (name.Length > keep)
        {
            // サロゲートペアの途中で切らない
            keep -= char.IsHighSurrogate(name[keep - 1]) ? 1 : 0;
            name = name[..keep];
        }

        var temporaryName = name + "." + Guid.NewGuid().ToString("N") + ".tmp";
        return string.IsNullOrEmpty(folder) ? temporaryName : Path.Combine(folder, temporaryName);
    }

    /// <summary>名前が <see cref="TemporaryPathFor"/> の作る一時ファイルの形（<c>….{32 桁の 16 進}.tmp</c>）か</summary>
    /// <remarks>
    /// 書き込みの途中で落ちると一時ファイルが残る。フォルダの中身を丸ごと写す処理（モックフォルダの同梱など）は、
    /// これで残骸を除く。
    /// </remarks>
    /// <param name="fileName">ファイル名（パスでなく名前だけ）</param>
    /// <returns>一時ファイルの形なら <c>true</c></returns>
    public static bool IsTemporaryFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (
            fileName.Length <= TemporarySuffixLength
            || !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var start = fileName.Length - TemporarySuffixLength;

        return fileName[start] == '.'
            && fileName.AsSpan(start + 1, 32).IndexOfAnyExcept("0123456789abcdef") < 0;
    }

    /// <summary>文字列を原子的にファイルへ書き出す（全量を書き切ってから保存先へ差し替える）</summary>
    /// <remarks>
    /// 保存先フォルダの作成は行わない（呼び出し側の責務）。差し替えに失敗した場合は一時ファイルを
    /// 掃除したうえで例外をそのまま呼び出し側へ伝える（このとき保存先は書き込み前のまま無傷）。
    /// 文字コードは <see cref="File.WriteAllText(string, string?)"/> の既定と同じ BOM なし UTF-8。
    /// </remarks>
    /// <param name="path">書き込み先のファイルパス</param>
    /// <param name="contents">書き込む内容</param>
    public static void WriteAllText(string path, string contents) =>
        WriteAllText(path, contents, TextEncodings.Utf8NoBom);

    /// <summary>文字コードを明示して文字列を原子的にファイルへ書き出す</summary>
    /// <remarks>
    /// 意味論は <see cref="WriteAllText(string, string)"/> と同一で、違いは書き出しの文字コードだけ。
    /// BOM 付き UTF-8（<see cref="Encoding.UTF8"/>）のように、素の <see cref="File.WriteAllText(string, string?)"/>
    /// の既定（BOM なし UTF-8）と食い違う符号化が求められる出力（DDL スクリプト等）向け。
    /// </remarks>
    /// <param name="path">書き込み先のファイルパス</param>
    /// <param name="contents">書き込む内容</param>
    /// <param name="encoding">書き出しに用いる文字コード</param>
    public static void WriteAllText(string path, string contents, Encoding encoding) =>
        Write(path, stream => WriteText(stream, contents, encoding));

    /// <summary>バイト列を原子的にファイルへ書き出す</summary>
    /// <remarks>意味論は <see cref="WriteAllText(string, string)"/> と同一で、違いは書き出す内容がバイト列であることだけ。</remarks>
    /// <param name="path">書き込み先のファイルパス</param>
    /// <param name="bytes">書き込む内容</param>
    public static void WriteAllBytes(string path, byte[] bytes) =>
        Write(path, stream => stream.Write(bytes));

    /// <summary>ストリームへ書き出す処理の結果を、原子的にファイルへ書き出す</summary>
    /// <remarks>
    /// <para>
    /// 意味論は <see cref="WriteAllText(string, string)"/> と同一。<paramref name="write"/> は一時ファイルのストリームへ
    /// 全量を書く（ストリームを閉じてはいけない。ディスクへの確定はこちらが行う）。画像のエンコーダや
    /// ブックの保存のように、書き出し先をストリームで受け取る出力向け。
    /// </para>
    /// <para>
    /// <paramref name="write"/> が例外を投げたときは、一時ファイルを掃除して例外をそのまま伝える（保存先は無傷）。
    /// </para>
    /// </remarks>
    /// <param name="path">書き込み先のファイルパス</param>
    /// <param name="write">一時ファイルのストリームへ全量を書く処理</param>
    public static void Write(string path, Action<Stream> write)
    {
        // 一時ファイルは保存先と同じディレクトリに作る（別ボリュームをまたがないため、
        // 差し替え（File.Replace / File.Move）が同一ボリューム内の操作で完結する）。
        // 名前に GUID を挟むのは、同じファイルを複数プロセス（GUI と MCP サーバ）が同時に
        // 保存したとき tmp が衝突して「書き途中の混線した内容を本体へ差し替える」破損へ
        // 昇格するのを防ぐため。
        var temporaryPath = TemporaryPathFor(path);

        try
        {
            WriteAndFlushToDisk(temporaryPath, write);

            // 保存先への差し替え。別プロセス（GUI と MCP サーバ）が同じファイルを同時に保存すると
            // 一過性の失敗（相手が保存先を開いている・保存先の有無が入れ替わる）が起きるため、
            // 短いバックオフで数回リトライする。
            ReplaceWithRetry(temporaryPath, path);
        }
        finally
        {
            // 正常終了時は置換/移動済みで存在しない。例外発生時のみ tmp の残骸を掃除する
            // （掃除自体の失敗で元の例外を握り潰さないよう黙殺する）
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 掃除に失敗した tmp は GUID 名のため次回保存で再利用されず、そのまま残り続ける
                // （＝自動では消えない）。ただし本体の内容には一切影響しないため、
                // 元の例外を握り潰してまで対処はしない。
            }
        }
    }

    /// <summary>一時ファイルへ全量を書き出し、ディスクへ確定させてから戻る</summary>
    /// <remarks>
    /// <para>
    /// <see cref="File.WriteAllText(string, string?)"/> は OS のファイルキャッシュへ書いた時点で戻るため、
    /// 差し替えだけが先に永続化され「中身が空（または途中まで）の新ファイル」が残り得る。
    /// <see cref="FileStream.Flush(bool)"/> でディスクへ確定させてから差し替えることで、
    /// 保存先に現れる内容が常に「書き切った 1 回分」になる。
    /// </para>
    /// </remarks>
    private static void WriteAndFlushToDisk(string path, Action<Stream> write)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        write(stream);

        // 書き出し処理が渡したバイトはストリームへ出ている。ここでディスクへ確定させる
        stream.Flush(flushToDisk: true);
    }

    /// <summary>文字列をストリームへ符号化して書く（ストリームは閉じない）</summary>
    /// <remarks>
    /// BOM（プリアンブル）は新規作成した一時ファイルの先頭へ書かれるため、BOM 付きの符号化を指定すれば
    /// 出力の先頭に BOM が付く。
    /// </remarks>
    private static void WriteText(Stream stream, string contents, Encoding encoding)
    {
        using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        writer.Write(contents);
    }

    /// <summary>一時ファイルを保存先へ差し替える（同時保存による一過性の失敗は短いバックオフでリトライする）</summary>
    /// <remarks>
    /// リトライ対象は <see cref="IOException"/> と <see cref="UnauthorizedAccessException"/>。
    /// 後者は「<see cref="File.Replace(string, string, string?)"/> が競合で失敗したときに実際に飛んでくる型」で、
    /// <see cref="IOException"/> だけを見ていると素通りする。恒久的な失敗（保存先がディレクトリ・権限なし等）も
    /// 同じ型で来るが、待ち時間の合計は 100ms 程度で、最後の試行の例外はそのまま呼び出し側へ伝播する。
    /// </remarks>
    private static void ReplaceWithRetry(string temporaryPath, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                ReplaceOnce(temporaryPath, path);

                return;
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException
                    && attempt < RetryAttemptCount
                )
            {
                // 競合相手が差し替えを終えるまで少し待つ（10 / 20 / 30 / 40ms）
                Thread.Sleep(RetryDelayStepMilliseconds * attempt);
            }
        }
    }

    /// <summary>ファイルの内容を読み出し、失敗したときは「無い」のか「読めない」のかを分類して返す</summary>
    /// <remarks>
    /// <para>
    /// <b>読み取り側の単一正本。</b><see cref="WriteAllText(string, string)"/> と対になる。
    /// 設定・接続プロファイルの保存はどれも「読み込み → 変更 → 書き込み」なので、読み取りの失敗を
    /// <c>catch</c> して既定値・空データへ落とすと、その空データが直後の書き込みで保存先へ焼き付き、
    /// <b>登録済みの内容が黙って全件消える</b>。防ぐには呼び出し側が
    /// 「保存されたものが無い（<see cref="FileReadStatus.Missing"/>）」と
    /// 「あるのに今は読めない（<see cref="FileReadStatus.Unavailable"/>）」を区別できる必要があり、
    /// その区別をここ 1 箇所で与える。
    /// </para>
    /// <para>
    /// <b>存在確認をしてから読まない理由:</b> <see cref="File.Exists(string)"/> と読み取りの間に
    /// ファイルが消える・現れることがある（TOCTOU）。判定は実際の読み取りの結果だけで行い、
    /// <see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/> を
    /// <see cref="FileReadStatus.Missing"/> として扱う（この 2 つは
    /// <see cref="IOException"/> の派生なので、分類の順序を入れ替えると
    /// 「無いだけ」が「読めない」へ化けて保存が止まる）。
    /// </para>
    /// <para>
    /// <b>リトライ:</b> 差し替え（<see cref="ReplaceWithRetry"/>）と同じ回数・同じバックオフで粘る。
    /// 相手にしている現象が同じ（別プロセスの保存中・ウイルス対策ソフトの一瞬のロック）ためで、
    /// 粘っても駄目だったものだけを <see cref="FileReadStatus.Unavailable"/> として返す。
    /// 内容の妥当性（JSON として解釈できるか）は判定しない＝それは呼び出し側の関心事。
    /// </para>
    /// </remarks>
    /// <param name="path">読み出すファイルのパス</param>
    /// <param name="contents">
    /// 読み出した内容。<see cref="FileReadStatus.Loaded"/> 以外のときは <c>null</c>
    /// </param>
    /// <returns>読み取りの結果分類</returns>
    public static FileReadStatus TryReadAllText(string path, out string? contents)
    {
        try
        {
            contents = ReadWithRetry(path, File.ReadAllText);

            return FileReadStatus.Loaded;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // 保存されたものが無いだけ（IOException の派生なので下の分類より先に捌く）
            contents = null;

            return FileReadStatus.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 粘っても読めない。中身は無事なので「空」ではなく「読めない」として返す
            contents = null;

            return FileReadStatus.Unavailable;
        }
    }

    /// <summary>
    /// ファイルの内容をバイト列で読み出す（別プロセスの保存と重なった一瞬の失敗は、差し替えと同じだけ粘る）
    /// </summary>
    /// <remarks>
    /// <see cref="TryReadAllText"/> と同じ読み取りの本体（<see cref="ReadWithRetry{T}"/>）を使う。分類せずに例外で返すのは、
    /// 呼び出し側（図の読み込み）が失敗の原因の文を利用者へ見せるため。ファイルが無い・フォルダが無いときは粘らず
    /// すぐに例外を返す。
    /// </remarks>
    /// <param name="path">読み出すファイルのパス</param>
    /// <returns>読み出した内容</returns>
    public static byte[] ReadAllBytes(string path) => ReadWithRetry(path, File.ReadAllBytes);

    /// <summary>
    /// 読み取りの本体。<see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> の一過性の失敗は、差し替え
    /// （<see cref="ReplaceWithRetry"/>）と同じ回数・同じバックオフで読み直し、それでも駄目なら最後の例外をそのまま返す
    /// </summary>
    /// <remarks>
    /// ファイル・フォルダが無いこと（<see cref="FileNotFoundException"/> / <see cref="DirectoryNotFoundException"/>＝
    /// <see cref="IOException"/> の派生）は待っても変わらないので、粘らずにすぐ返す。
    /// </remarks>
    private static T ReadWithRetry<T>(string path, Func<string, T> read)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return read(path);
            }
            catch (Exception ex)
                when (ex is not (FileNotFoundException or DirectoryNotFoundException)
                    && ex is IOException or UnauthorizedAccessException
                    && attempt < RetryAttemptCount
                )
            {
                // 競合相手が保存を終えるまで少し待つ（10 / 20 / 30 / 40ms）
                Thread.Sleep(RetryDelayStepMilliseconds * attempt);
            }
        }
    }

    /// <summary>
    /// 文字列を新しいファイルとして原子的に書き出す。保存先が既にあれば何も変えずに <see cref="IOException"/> を投げる
    /// </summary>
    /// <remarks>
    /// <see cref="WriteAllText(string, string)"/> と同じく一時ファイルへ書き切ってから置くが、置くのは
    /// <c>overwrite: false</c> の <see cref="File.Move(string, string, bool)"/> だけ（置き換えない）。存在を確かめてから
    /// 書く形では、その隙間に別のプロセスが作ったファイルを上書きしてしまう。保存先フォルダの作成は行わない。
    /// </remarks>
    /// <param name="path">作るファイルのパス</param>
    /// <param name="contents">書き込む内容（BOM なし UTF-8）</param>
    public static void CreateText(string path, string contents) =>
        CreateText(path, contents, TextEncodings.Utf8NoBom);

    /// <summary>文字コードを明示して、文字列を新しいファイルとして原子的に書き出す</summary>
    /// <remarks>意味論は <see cref="CreateText(string, string)"/> と同一で、違いは書き出しの文字コードだけ。</remarks>
    /// <param name="path">作るファイルのパス</param>
    /// <param name="contents">書き込む内容</param>
    /// <param name="encoding">書き出しに用いる文字コード</param>
    public static void CreateText(string path, string contents, Encoding encoding)
    {
        var temporaryPath = TemporaryPathFor(path);

        try
        {
            WriteAndFlushToDisk(temporaryPath, stream => WriteText(stream, contents, encoding));
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 掃除の失敗で元の例外を握り潰さない（WriteAllText と同じ）
            }
        }
    }

    /// <summary>一時ファイルを保存先へ 1 回だけ差し替える</summary>
    /// <remarks>
    /// 保存先の有無で分岐するのは <see cref="File.Replace(string, string, string?)"/> が保存先の不在で
    /// 失敗するため。ただし有無の判定と差し替えの間に他プロセスが保存先を作ることがある（TOCTOU）ので、
    /// 不在側も <c>overwrite: true</c> の <see cref="File.Move(string, string, bool)"/> を使い、
    /// 「判定した直後に保存先が現れた」だけで失敗しないようにする。
    /// </remarks>
    private static void ReplaceOnce(string temporaryPath, string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
            {
                // クラウド同期フォルダ等、File.Replace が使えない環境向けのフォールバック
                // （OS 水準の原子性は落ちるが「全量を書き切ってから差し替える」保護は保たれる）
                MoveOverPreservingMetadata(temporaryPath, path);
            }
        }
        else
        {
            File.Move(temporaryPath, path, overwrite: true);
        }
    }

    /// <summary>
    /// 一時ファイルを既存の保存先へ移して置き換え、保存先が持っていた属性と ACL を移した先へ写す
    /// （<see cref="File.Replace(string, string, string?)"/> が使えないときの代わり）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="File.Replace(string, string, string?)"/> は保存先の属性（隠し・システムなど）と ACL を引き継ぐが、
    /// <see cref="File.Move(string, string, bool)"/> は一時ファイルのもの（既定の属性・親フォルダから継いだ ACL）を
    /// 持ち込む。そのため移す前に保存先のものを読み、移した後に写す。
    /// </para>
    /// <para>
    /// 写すのは最善努力：読み取り・写しに失敗しても保存そのもの（内容の置き換え）は成功させる。ACL は Windows でだけ写し、
    /// それ以外の OS では何もしない（属性は OS を問わず写す）。読み取り専用の属性は写さない（読み取り専用の保存先は
    /// 置き換えの前に失敗するのでここへは来ないが、写すと次の保存が自分の付けた印で止まる）。
    /// </para>
    /// </remarks>
    /// <param name="temporaryPath">書き切った一時ファイルのパス</param>
    /// <param name="path">置き換える保存先のパス</param>
    internal static void MoveOverPreservingMetadata(string temporaryPath, string path)
    {
        FileAttributes? attributes = null;
        string? accessSddl = null;

        try
        {
            attributes = File.GetAttributes(path) & ~FileAttributes.ReadOnly;

            if (OperatingSystem.IsWindows())
            {
                accessSddl = new FileInfo(path)
                    .GetAccessControl(AccessControlSections.Access)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読めなければ写さない（保存は続ける）
        }

        File.Move(temporaryPath, path, overwrite: true);

        try
        {
            if (attributes is { } kept)
            {
                File.SetAttributes(path, kept);
            }

            // 読み取ったオブジェクトをそのまま渡しても、変更の印が無いので何も書かれない。
            // SDDL から作り直した記述子を渡す（DACL と継承の保護の有無を写す）
            if (accessSddl is not null && OperatingSystem.IsWindows())
            {
                var security = new FileSecurity();
                security.SetSecurityDescriptorSddlForm(accessSddl, AccessControlSections.Access);
                new FileInfo(path).SetAccessControl(security);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写せなくても内容は置き換わっている（保存は成功）
        }
    }
}
