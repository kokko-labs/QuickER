using System.IO;
using System.Text.Json;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock.Agents;

/// <summary>API キーの実行器が読む素材（スキャフォールド相当のフォルダ）と、提出のツール呼び出しを作る</summary>
/// <remarks>実行器の網（API キーのアダプタ）と <c>ApiKeyMockProjectAgentTests</c> が共有する</remarks>
internal static class ApiKeyMockMaterials
{
    /// <summary>画面の HTML（見出しに目印を書く）</summary>
    /// <param name="marker">目印</param>
    public static string ScreenHtml(string marker) =>
        "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"style.css\"></head>"
        + $"<body><h1>{marker}</h1></body></html>";

    /// <summary>emit_file の呼び出し（ツール名・引数 JSON）を組み立てる</summary>
    /// <param name="path">提出のパス</param>
    /// <param name="content">内容</param>
    public static (string Tool, string Args) Emit(string path, string content = "<x/>") =>
        (MockProjectEmitTools.EmitFileToolName, JsonSerializer.Serialize(new { path, content }));

    /// <summary>
    /// スキャフォールド相当の素材（design/mock/ の mock.json・画面 HTML・style.css＋Generated/ の契約）を用意する
    /// </summary>
    /// <param name="work">出力フォルダ</param>
    /// <param name="project">プロジェクト名</param>
    /// <param name="screens">画面（ファイル名・名前・HTML の目印）</param>
    /// <returns>出力フォルダ</returns>
    public static string Setup(
        string work,
        string project,
        params (string File, string Name, string Marker)[] screens
    )
    {
        var projectDir = Path.Combine(work, project);
        var designDir = Path.Combine(projectDir, "design", "mock");
        Directory.CreateDirectory(designDir);

        var store = MockFolderStore.CreateNew(
            designDir,
            project,
            sourceSchema: "SCHEMA-TEXT: Customer(Id, Name)"
        );

        foreach (var (file, name, marker) in screens)
        {
            store.SaveScreen(
                file,
                name,
                $"role of {name}",
                ScreenHtml(marker),
                Array.Empty<MockTransition>(),
                "init"
            );
        }

        store.SaveStylesheet("body { color: #111; } /* CSS-MARKER */", "css");

        // データ層（契約要約の抽出対象）
        var generatedDir = Path.Combine(projectDir, "Generated");
        Directory.CreateDirectory(generatedDir);
        File.WriteAllText(
            Path.Combine(generatedDir, "Repositories.cs"),
            "namespace Gen;\n"
                + "public interface ICustomerRepository\n{\n"
                + "    Task<Customer> GetByIdAsync(int id);\n"
                + "    Task<IReadOnlyList<Customer>> GetAllAsync();\n}\n"
                + "public sealed class Customer { public int Id { get; set; } }\n"
        );

        return work;
    }
}
