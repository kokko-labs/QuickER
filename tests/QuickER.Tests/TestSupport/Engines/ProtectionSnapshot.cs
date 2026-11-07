using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace QuickER.Tests.TestSupport.Engines;

/// <summary>写しのエンジンの部分（台帳の行のうち、影響の地図が読む項目だけ）</summary>
/// <param name="Id">エンジンの ID</param>
/// <param name="Part">エンジンの部分</param>
/// <param name="Title">表示の名前</param>
/// <param name="SourceFiles">テストのソース</param>
/// <param name="Covers">対象の src の範囲</param>
public sealed record SnapshotEngine(
    string Id,
    string Part,
    string Title,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string> Covers
);

/// <summary>写しの全体（エンジンの部分と単体の表）</summary>
/// <param name="Engines">エンジンの部分（台帳の順）</param>
/// <param name="Tables">単体の表（台帳の順）</param>
public sealed record ProtectionSnapshotContent(
    IReadOnlyList<SnapshotEngine> Engines,
    IReadOnlyList<TableDescriptor> Tables
);

/// <summary>
/// アセンブリの守りの台帳（エンジンの台帳と単体の表の台帳）の写し（JSON・改行は LF）
/// </summary>
/// <remarks>
/// QuickER.Tests は Integration を参照できないので、Integration の台帳はこの写し
/// （<c>tests/QuickER.Tests.Integration/protection-registry.json</c>）にして、QuickER.Tests の影響の地図が読む。
/// 写しは Integration の <c>ProtectionSnapshotDriftTests</c> が台帳と照合する（台帳を変えて写しを書き直し忘れると、Integration の
/// そのテストが赤になる）。次元表・シナリオの数は写さない（地図は範囲だけを読み、数を入れると行を足すたびに写しが変わる）。
/// </remarks>
public static class ProtectionSnapshot
{
    /// <summary>Integration の台帳の写しのファイル（リポジトリ直下からの相対パス）</summary>
    public const string IntegrationPath =
        "tests/QuickER.Tests.Integration/protection-registry.json";

    /// <summary>写しのテキストを作る</summary>
    /// <param name="engines">エンジンの台帳</param>
    /// <param name="tables">単体の表の台帳</param>
    public static string Render(
        IReadOnlyList<EngineDescriptor> engines,
        IReadOnlyList<TableDescriptor> tables
    )
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(tables);

        using var stream = new MemoryStream();

        using (
            var writer = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions
                {
                    Indented = true,
                    NewLine = "\n",
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            )
        )
        {
            writer.WriteStartObject();
            writer.WriteStartArray("engines");

            foreach (var engine in engines)
            {
                writer.WriteStartObject();
                writer.WriteString("id", engine.Id);
                writer.WriteString("part", engine.Part);
                writer.WriteString("title", engine.Title);
                WriteList(writer, "sourceFiles", engine.SourceFiles);
                WriteList(writer, "covers", engine.Covers);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("tables");

            foreach (var table in tables)
            {
                writer.WriteStartObject();
                writer.WriteString("name", table.Name);
                writer.WriteString("title", table.Title);
                WriteList(writer, "sourceFiles", table.SourceFiles);
                WriteList(writer, "covers", table.Covers);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>写しのテキストを読む</summary>
    /// <param name="json">写しのテキスト</param>
    /// <exception cref="InvalidDataException">形が違う</exception>
    public static ProtectionSnapshotContent Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var engines = Array(root, "engines")
            .Select(item => new SnapshotEngine(
                Text(item, "id"),
                Text(item, "part"),
                Text(item, "title"),
                List(item, "sourceFiles"),
                List(item, "covers")
            ))
            .ToList();
        var tables = Array(root, "tables")
            .Select(item => new TableDescriptor(
                Text(item, "name"),
                Text(item, "title"),
                List(item, "sourceFiles"),
                List(item, "covers")
            ))
            .ToList();

        return new ProtectionSnapshotContent(engines, tables);
    }

    private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string> items)
    {
        writer.WriteStartArray(name);

        foreach (var item in items)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : throw new InvalidDataException($"写しに配列 {name} が無い");

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"写しの項目に文字列 {name} が無い");

    private static IReadOnlyList<string> List(JsonElement element, string name) =>
        Array(element, name)
            .Select(item =>
                item.ValueKind == JsonValueKind.String
                    ? item.GetString()!
                    : throw new InvalidDataException($"写しの {name} に文字列でない項目がある")
            )
            .ToList();
}
