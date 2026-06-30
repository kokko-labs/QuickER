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
    /// <summary>可読性重視のシリアライズ設定（インデント付与・列挙体は名前で出力）</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>保存文書をファイルへ保存する（JSON は <c>{ version, schema, layout }</c> 形式）</summary>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="document">保存対象の文書（意味モデル＋レイアウト）</param>
    public static void Save(string path, DiagramDocument document)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options));
    }

    /// <summary>ファイルから保存文書を読み込む</summary>
    /// <param name="path">読み込むファイルパス</param>
    /// <returns>読み込んだ <see cref="DiagramDocument"/>（内容が空の場合は新規インスタンス）</returns>
    public static DiagramDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DiagramDocument>(json, Options) ?? new DiagramDocument();
    }
}
