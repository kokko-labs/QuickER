using System.IO;

namespace QuickER.Settings;

/// <summary>ファイルを原子的に（書き込み途中の中断で保存先を壊さずに）書き出すユーティリティ</summary>
/// <remarks>
/// <para>
/// <b>原子的書き込みの単一正本。</b>かつては図ファイル（<c>QuickER.Documents.JsonStorageService.SaveAtomic</c>）・
/// 設定ファイル（<see cref="JsonSettingsStore{TSettings}"/>）・接続プロファイル
/// （<c>QuickER.Db.UI.SqlConnectionProfileStore</c>）の 3 箇所へ同じアルゴリズムを逐語コピーしており、
/// 一時ファイル名が 1 箇所だけ固定名のまま取り残されるドリフトが実際に起きた。同期を守る仕組みのない
/// コピーを解消するため、ここへ一元化した（2026-08-05）。
/// </para>
/// <para>
/// 素の <see cref="File.WriteAllText(string, string?)"/> は既存ファイルを切り詰めてから書くため、
/// 途中でプロセスが落ちる・ディスクが満杯になると保存先が中途半端な内容（壊れた JSON）で残る。
/// 本クラスは一時ファイルへ全量を書き切ってから本体へ差し替えるため、どこで中断しても保存先は
/// 「書き込み前の内容そのまま」か「書き込み後の内容」のどちらかにしかならない。
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
    /// <summary>差し替え（Replace / Move）の試行回数（初回＋リトライ 4 回）</summary>
    private const int ReplaceAttemptCount = 5;

    /// <summary>差し替えリトライの待ち時間（ミリ秒・試行ごとに 10ms ずつ延ばす）</summary>
    private const int ReplaceRetryDelayStepMilliseconds = 10;

    /// <summary>文字列を原子的にファイルへ書き出す（全量を書き切ってから保存先へ差し替える）</summary>
    /// <remarks>
    /// 保存先フォルダの作成は行わない（呼び出し側の責務）。差し替えに失敗した場合は一時ファイルを
    /// 掃除したうえで例外をそのまま呼び出し側へ伝える（このとき保存先は書き込み前のまま無傷）。
    /// 文字コードは <see cref="File.WriteAllText(string, string?)"/> の既定（BOM なし UTF-8）。
    /// </remarks>
    /// <param name="path">書き込み先のファイルパス</param>
    /// <param name="contents">書き込む内容</param>
    public static void WriteAllText(string path, string contents)
    {
        // 一時ファイルは保存先と同じディレクトリに作る（別ボリュームをまたがないため、
        // 差し替え（File.Replace / File.Move）が同一ボリューム内の操作で完結する）。
        // 名前に GUID を挟むのは、同じファイルを複数プロセス（GUI と MCP サーバ）が同時に
        // 保存したとき tmp が衝突して「書き途中の混線した内容を本体へ差し替える」破損へ
        // 昇格するのを防ぐため。
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, contents);

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
                // （＝自動では消えない）。ただし本体の内容には一切影響せず、実測でも掃除の失敗は
                // 観測されていないため、元の例外を握り潰してまで対処はしない。
            }
        }
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
                    && attempt < ReplaceAttemptCount
                )
            {
                // 競合相手が差し替えを終えるまで少し待つ（10 / 20 / 30 / 40ms）
                Thread.Sleep(ReplaceRetryDelayStepMilliseconds * attempt);
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
