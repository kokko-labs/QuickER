using System.IO;
using FluentAssertions;
using QuickER.Documents;
using QuickER.Model;

namespace QuickER.Tests.Model;

/// <summary>
/// <see cref="ErDiagram.TargetDbms"/> の既定値・永続化を検証するテストクラス。
/// </summary>
public class ErDiagramTargetDbmsTests
{
    /// <summary>新規 <see cref="ErDiagram"/> の既定 <see cref="ErDiagram.TargetDbms"/> が sqlserver であることを検証する</summary>
    [Fact(DisplayName = "ErDiagram の既定 TargetDbms は sqlserver")]
    public void ErDiagram_DefaultTargetDbms_IsSqlServer()
    {
        var diagram = new ErDiagram();

        diagram.TargetDbms.Should().Be("sqlserver");
    }

    /// <summary>TargetDbms フィールドを含まない旧形式 JSON を読み込むと sqlserver とみなされることを検証する</summary>
    [Fact(DisplayName = "旧形式 JSON（TargetDbms 欠落）は sqlserver として読み込まれる")]
    public void Load_LegacyJsonWithoutTargetDbms_DefaultsToSqlServer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"er-legacy-{Guid.NewGuid()}.json");
        var legacyJson = """
            {
              "version": 1,
              "schema": {
                "entities": [],
                "relationships": []
              },
              "layout": {}
            }
            """;

        try
        {
            File.WriteAllText(path, legacyJson);

            var loaded = JsonStorageService.Load(path);

            loaded.Schema.TargetDbms.Should().Be("sqlserver");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>保存 → 読込のラウンドトリップで TargetDbms が保持されることを検証する</summary>
    [Fact(DisplayName = "Save → Load で TargetDbms が保持される")]
    public void SaveAndLoad_RoundTrip_PreservesTargetDbms()
    {
        var document = new DiagramDocument { Schema = new ErDiagram { TargetDbms = "postgresql" } };
        var path = Path.Combine(Path.GetTempPath(), $"er-{Guid.NewGuid()}.json");

        try
        {
            JsonStorageService.Save(path, document);
            var loaded = JsonStorageService.Load(path);

            loaded.Schema.TargetDbms.Should().Be("postgresql");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
