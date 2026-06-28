using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuickER.Model;

namespace QuickER.Services;

/// <summary>ER 図を JSON ファイルへ保存・読み込みするトップレベルサービス</summary>
/// <remarks>
/// <see cref="System.Text.Json"/> を用い、WPF 型（Brush など）を含まない POCO
/// （<see cref="ErDiagram"/>）のみをシリアライズする
/// </remarks>
public static class JsonStorageService
{
    /// <summary>可読性重視のシリアライズ設定（インデント付与・列挙体は名前で出力）</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>ER 図定義をファイルへ保存する</summary>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="diagram">保存対象の ER 図定義（POCO）</param>
    public static void Save(string path, ErDiagram diagram)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(diagram, Options));
    }

    /// <summary>ファイルから ER 図を読み込む</summary>
    /// <param name="path">読み込むファイルパス</param>
    /// <returns>読み込んだ <see cref="ErDiagram"/>（内容が空の場合は新規インスタンス）</returns>
    public static ErDiagram Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ErDiagram>(json, Options) ?? new ErDiagram();
    }
}
