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

            // 既存ファイルがあれば置換（バックアップは残さない）、無ければ単純に移動する。
            // File.Replace は保存先が存在しないと例外になるため、両者を明示的に分岐する。
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
                File.Move(temporaryPath, path);
            }
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
                // tmp の残骸は次回保存時に上書きされるため無害
            }
        }
    }
}
