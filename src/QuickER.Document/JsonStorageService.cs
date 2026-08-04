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

    /// <summary>保存文書をファイルへ保存する（JSON は <c>{ version, schema, layout }</c> 形式）</summary>
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
    /// 切り詰めてから書くため、途中でプロセスが落ちると保存先が破損した JSON になる。
    /// クラッシュ時の緊急保存など「落ちる可能性のある文脈」からの書き込みはこちらを使う。
    /// </remarks>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="document">保存対象の文書（意味モデル＋レイアウト）</param>
    public static void SaveAtomic(string path, DiagramDocument document)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, Serialize(document));

        // 既存ファイルがあれば置換（バックアップは残さない）、無ければ単純に移動する。
        // File.Replace は保存先が存在しないと例外になるため、両者を明示的に分岐する。
        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temporaryPath, path);
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
