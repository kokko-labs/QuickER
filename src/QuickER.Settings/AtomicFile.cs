using System.IO;
using System.Text;

namespace QuickER.Settings;

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
/// 設定ファイル（<see cref="JsonSettingsStore{TSettings}"/>）・接続プロファイル
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
/// <para>
/// <b>収容先について:</b> 依存ゼロ・net10.0 でどの層からも参照できる汎用永続化ユーティリティ層として
/// このプロジェクトへ置いた（40 行のユーティリティのために新規プロジェクトを立てるのは過剰と判断）。
/// このため <c>QuickER.Document</c>（文書層）も「設定」プロジェクトを参照する形になるが、参照するのは
/// 本クラスのみで設定固有の型（<see cref="JsonSettingsStore{TSettings}"/> 等）には依存しない。
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

    /// <summary>
    /// 文字コード未指定時の既定。<see cref="File.WriteAllText(string, string?)"/> の既定と同一
    /// （BOM なし UTF-8・不正なサロゲートは置換せず例外）にして、書き出し方の変更で挙動が変わらないようにする。
    /// </summary>
    private static readonly Encoding DefaultEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    /// <summary>文字列を原子的にファイルへ書き出す（全量を書き切ってから保存先へ差し替える）</summary>
    /// <remarks>
    /// 保存先フォルダの作成は行わない（呼び出し側の責務）。差し替えに失敗した場合は一時ファイルを
    /// 掃除したうえで例外をそのまま呼び出し側へ伝える（このとき保存先は書き込み前のまま無傷）。
    /// 文字コードは <see cref="File.WriteAllText(string, string?)"/> の既定と同じ BOM なし UTF-8。
    /// </remarks>
    /// <param name="path">書き込み先のファイルパス</param>
    /// <param name="contents">書き込む内容</param>
    public static void WriteAllText(string path, string contents) =>
        WriteAllTextCore(path, contents, encoding: null);

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
        WriteAllTextCore(path, contents, encoding);

    /// <summary>原子的書き込みの本体（<paramref name="encoding"/> が null なら既定＝BOM なし UTF-8）</summary>
    private static void WriteAllTextCore(string path, string contents, Encoding? encoding)
    {
        // 一時ファイルは保存先と同じディレクトリに作る（別ボリュームをまたがないため、
        // 差し替え（File.Replace / File.Move）が同一ボリューム内の操作で完結する）。
        // 名前に GUID を挟むのは、同じファイルを複数プロセス（GUI と MCP サーバ）が同時に
        // 保存したとき tmp が衝突して「書き途中の混線した内容を本体へ差し替える」破損へ
        // 昇格するのを防ぐため。
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            WriteAndFlushToDisk(temporaryPath, contents, encoding);

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
    /// <para>
    /// 文字コードの既定（<paramref name="encoding"/> が null）は
    /// <see cref="File.WriteAllText(string, string?)"/> と同一の「BOM なし UTF-8・不正なサロゲートは例外」。
    /// BOM（プリアンブル）は新規作成した一時ファイルの先頭へ書かれるため、明示指定時の出力も変わらない。
    /// </para>
    /// </remarks>
    private static void WriteAndFlushToDisk(string path, string contents, Encoding? encoding)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

        using (var writer = new StreamWriter(stream, encoding ?? DefaultEncoding, leaveOpen: true))
        {
            writer.Write(contents);
        }

        // StreamWriter の破棄でエンコード済みバイトはストリームへ出ている。ここでディスクへ確定させる
        stream.Flush(flushToDisk: true);
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
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                contents = File.ReadAllText(path);

                return FileReadStatus.Loaded;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // 保存されたものが無いだけ（IOException の派生なので下のリトライより先に捌く）
                contents = null;

                return FileReadStatus.Missing;
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException
                    && attempt < RetryAttemptCount
                )
            {
                // 競合相手が保存を終えるまで少し待つ（10 / 20 / 30 / 40ms）
                Thread.Sleep(RetryDelayStepMilliseconds * attempt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 粘っても読めない。中身は無事なので「空」ではなく「読めない」として返す
                contents = null;

                return FileReadStatus.Unavailable;
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
                File.Move(temporaryPath, path, overwrite: true);
            }
        }
        else
        {
            File.Move(temporaryPath, path, overwrite: true);
        }
    }
}
