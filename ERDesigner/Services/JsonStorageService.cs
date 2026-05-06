using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERDesigner.Models;
using ERDesigner.ViewModels;

namespace ERDesigner.Services;

/// <summary>
/// ER 図を JSON ファイルに保存・読み込みするトップレベルサービスです。
/// </summary>
/// <remarks>
/// <see cref="System.Text.Json"/> を使用し、WPF 型 (Brush など) を含まない POCO
/// (<see cref="ErDiagram"/>) のみをシリアライズします。
/// </remarks>
public static class JsonStorageService
{
    /// <summary>読みやすさ重視のシリアライズ設定。</summary>
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>現在の <see cref="MainViewModel"/> の状態をファイルに保存します。</summary>
    /// <param name="path">保存先のファイルパス。</param>
    /// <param name="vm">保存する ViewModel。</param>
    public static void Save(string path, MainViewModel vm)
    {
        var diagram = new ErDiagram { Entities = vm.Entities.Select(e => e.ToModel()).ToList(), Relationships = vm.Relationships.Select(r => r.ToModel()).ToList() };

        File.WriteAllText(path, JsonSerializer.Serialize(diagram, Options));
    }

    /// <summary>ファイルから ER 図を読み込みます。</summary>
    /// <param name="path">読み込むファイルパス。</param>
    /// <returns>読み込まれた <see cref="ErDiagram"/>。</returns>
    public static ErDiagram Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ErDiagram>(json, Options) ?? new ErDiagram();
    }
}
