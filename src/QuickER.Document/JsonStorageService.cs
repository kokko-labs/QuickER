using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickER.Documents;

/// <summary>ER 図を JSON ファイルへ保存・読み込みするトップレベルサービス</summary>
/// <remarks>
/// <see cref="System.Text.Json"/> を用い、WPF 型（Brush など）を含まない保存文書
/// （<see cref="DiagramDocument"/>: 意味モデル schema ＋ 視覚情報 layout）をシリアライズする
/// </remarks>
public static class JsonStorageService
{
    /// <summary>可読性重視のシリアライズ設定（インデント付与・列挙体は名前で出力・null プロパティは省略）</summary>
    /// <remarks>
    /// null の省略（<see cref="JsonIgnoreCondition.WhenWritingNull"/>）は「値なし」をキーごと出さない
    /// 図ファイルの正準形。読み込み側はキー欠落をプロパティ既定値で吸収するため相互に可換で、
    /// 古い形式（null を明記した図ファイル）もそのまま読める。
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>保存文書をファイルへ単純に書き出す（JSON は <c>{ version, schema, layout }</c> 形式）</summary>
    /// <remarks>
    /// 既存ファイルを切り詰めてから書くため、書き込み途中の中断で保存先が破損し得る。
    /// プロダクションのファイル書き出しはすべて <see cref="SaveAtomic"/> を使うこと
    /// （本メソッドはテストのフィクスチャ書き出し等、破損しても影響のない用途に残している）。
    /// </remarks>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="document">保存対象の文書（意味モデル＋レイアウト）</param>
    public static void Save(string path, DiagramDocument document)
    {
        File.WriteAllText(path, Serialize(document));
    }

    /// <summary>保存文書をアトミックに（書き込み途中の中断で既存ファイルを壊さずに）保存する</summary>
    /// <remarks>
    /// 一時ファイル <c>{path}.tmp</c> へ全量を書き切ってから本体へ差し替える。素の
    /// <see cref="Save"/>（<see cref="File.WriteAllText(string, string?)"/>）は既存ファイルを
    /// 切り詰めてから書くため、途中でプロセスが落ちる・ディスクが満杯になると保存先が破損した JSON になる。
    /// <b>プロダクションのファイル書き出し（GUI の上書き／別名保存・スキーマのみ JSON のエクスポート・
    /// MCP のツール実行・CLI のリバース出力・クラッシュ時の緊急保存）はすべてこちらを使う。</b>
    /// </remarks>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="document">保存対象の文書（意味モデル＋レイアウト）</param>
    public static void SaveAtomic(string path, DiagramDocument document)
    {
        // 一時ファイルは保存先と同じディレクトリに作る（別ボリュームをまたがないため、
        // 差し替え（File.Replace / File.Move）が同一ボリューム内の操作で完結する）
        var temporaryPath = path + ".tmp";

        try
        {
            File.WriteAllText(temporaryPath, Serialize(document));

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

    /// <summary>保存文書を図ファイルの正準形（<see cref="Options"/>）で JSON 文字列へ直列化する</summary>
    /// <remarks><see cref="Save"/> と <see cref="SaveAtomic"/> で出力を完全に一致させるための共有ヘルパ</remarks>
    private static string Serialize(DiagramDocument document) =>
        JsonSerializer.Serialize(document, Options);

    /// <summary>ファイルから保存文書を読み込む</summary>
    /// <param name="path">読み込むファイルパス</param>
    /// <returns>読み込んだ <see cref="DiagramDocument"/>（内容が空の場合は新規インスタンス）</returns>
    public static DiagramDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DiagramDocument>(json, Options) ?? new DiagramDocument();
    }
}
