using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>ER 図を JSON ファイルへ保存・読み込みするトップレベルサービス</summary>
/// <remarks>
/// <see cref="System.Text.Json"/> を用い、WPF 型（Brush など）を含まない POCO
/// （<see cref="ErDiagram"/>）のみをシリアライズする
/// </remarks>
public static class JsonStorageService
{
    /// <summary>可読性重視のシリアライズ設定（インデント付与・列挙体は名前で出力）</summary>
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>現在の <see cref="MainViewModel"/> の状態をファイルへ保存する</summary>
    /// <param name="path">保存先のファイルパス</param>
    /// <param name="vm">保存対象の ViewModel</param>
    public static void Save(string path, MainViewModel vm)
    {
        // ViewModel から永続化用の POCO へ変換してからシリアライズする
        var diagram = new ErDiagram { Entities = vm.Entities.Select(e => e.ToModel()).ToList(), Relationships = vm.Relationships.Select(r => r.ToModel()).ToList() };

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
