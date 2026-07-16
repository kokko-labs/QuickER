using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI;

namespace QuickER.Tests.Services;

/// <summary><see cref="CSharpGenerationSettingsStore"/> の保存・読込を検証するテストクラス</summary>
public class CSharpGenerationSettingsStoreTests
{
    /// <summary>一意の一時フォルダパスを作る</summary>
    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));

    /// <summary>保存した設定が同じ内容で読み込めることを検証する</summary>
    [Fact(DisplayName = "保存した C# 生成設定を読み込める")]
    public void SaveThenLoad_RoundTrips()
    {
        var folder = TempFolder();

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            store.Save(
                new CSharpGenerationSettings
                {
                    SplitFilesByCategory = true,
                    NamespaceName = "Acme.App",
                    EntityNamespace = "Acme.App.Domain",
                    GenerateRepositories = false,
                    GenerateValueObjects = true,
                    OutputFolderPath = @"C:\out",
                }
            );

            var loaded = store.Load();

            loaded.SplitFilesByCategory.Should().BeTrue();
            loaded.NamespaceName.Should().Be("Acme.App");
            loaded.EntityNamespace.Should().Be("Acme.App.Domain");
            loaded.GenerateRepositories.Should().BeFalse();
            loaded.GenerateValueObjects.Should().BeTrue();
            loaded.OutputFolderPath.Should().Be(@"C:\out");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>設定ファイルが無い場合は工場出荷既定を返すことを検証する</summary>
    [Fact(DisplayName = "未保存なら工場出荷既定を返す")]
    public void Load_WhenMissing_ReturnsDefault()
    {
        var store = new CSharpGenerationSettingsStore(TempFolder());

        var loaded = store.Load();

        loaded.SplitFilesByCategory.Should().BeFalse();
        loaded.NamespaceName.Should().Be(CSharpGenerationSettings.DefaultBaseNamespace);
        loaded.GenerateEntityClasses.Should().BeTrue();
    }

    /// <summary>破損ファイルでも例外を投げず既定値へフォールバックすることを検証する</summary>
    [Fact(DisplayName = "破損ファイルなら既定値へフォールバック")]
    public void Load_WhenCorrupt_ReturnsDefault()
    {
        var folder = TempFolder();

        try
        {
            Directory.CreateDirectory(folder);
            var store = new CSharpGenerationSettingsStore(folder);
            File.WriteAllText(store.SettingsPath, "{ this is not valid json");

            var loaded = store.Load();

            loaded.NamespaceName.Should().Be(CSharpGenerationSettings.DefaultBaseNamespace);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>任意パスへ保存した設定を TryLoadFrom で同じ内容に読み戻せることを検証する（往復）</summary>
    [Fact(DisplayName = "任意パスへ保存した設定を読み戻せる")]
    public void SaveToThenTryLoadFrom_RoundTrips()
    {
        var folder = TempFolder();

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            // 親フォルダがまだ無いパスへ保存し、SaveTo がフォルダを作成することも兼ねて検証する
            var path = Path.Combine(folder, "preset", "codegen-settings.json");
            store.SaveTo(
                path,
                new CSharpGenerationSettings
                {
                    SplitFilesByCategory = true,
                    NamespaceName = "Acme.App",
                    GenerateEfCore = true,
                    GenerateValueObjects = true,
                }
            );

            File.Exists(path).Should().BeTrue();

            var loaded = store.TryLoadFrom(path);

            loaded.Should().NotBeNull();
            loaded!.SplitFilesByCategory.Should().BeTrue();
            loaded.NamespaceName.Should().Be("Acme.App");
            loaded.GenerateEfCore.Should().BeTrue();
            loaded.GenerateValueObjects.Should().BeTrue();

            // 既定保存先（codegen-settings.json）は SaveTo では書かれない
            File.Exists(store.SettingsPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>存在しないパスの TryLoadFrom は null を返すことを検証する（既定値へはフォールバックしない）</summary>
    [Fact(DisplayName = "存在しないパスの TryLoadFrom は null")]
    public void TryLoadFrom_WhenMissing_ReturnsNull()
    {
        var store = new CSharpGenerationSettingsStore(TempFolder());

        var loaded = store.TryLoadFrom(Path.Combine(TempFolder(), "does-not-exist.json"));

        loaded.Should().BeNull();
    }

    /// <summary>破損 JSON の TryLoadFrom は null を返すことを検証する（既定値へはフォールバックしない）</summary>
    [Fact(DisplayName = "破損 JSON の TryLoadFrom は null")]
    public void TryLoadFrom_WhenCorrupt_ReturnsNull()
    {
        var folder = TempFolder();

        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "broken.json");
            File.WriteAllText(path, "{ this is not valid json");

            var store = new CSharpGenerationSettingsStore(folder);

            store.TryLoadFrom(path).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// <see cref="CSharpGenerationSettingsStore.SaveTo"/> が書き出した JSON を、CLI と同じ読み方
    /// （<see cref="JsonNode"/> 解析 → <c>PropertyNameCaseInsensitive</c> で <see cref="CodeGenerationOptions"/> へ
    /// デシリアライズ）で読めることを検証する。GUI が保存した設定ファイルを CLI の <c>--config</c> へ
    /// そのまま渡せる（スキーマ互換）ことの回帰ガード
    /// </summary>
    [Fact(DisplayName = "SaveTo の JSON を CLI と同じ経路で CodeGenerationOptions として読める")]
    public void SaveTo_JsonIsCliConfigCompatible()
    {
        var folder = TempFolder();

        try
        {
            var store = new CSharpGenerationSettingsStore(folder);
            var path = Path.Combine(folder, "codegen-settings.json");
            store.SaveTo(
                path,
                new CSharpGenerationSettings
                {
                    NamespaceName = "Acme.Contracts",
                    SplitFilesByCategory = true,
                    GenerateEfCore = true,
                    GenerateRepositories = true,
                    RepositoryDialects = new() { "sqlserver", "sqlite" },
                    OutputFileName = "Acme.g.cs",
                    GenerateValueObjects = true,
                }
            );

            // CLI（CliApp.LoadOptions）と同じ読み方: JsonNode で解析し、大文字小文字を無視して
            // CodeGenerationOptions へデシリアライズする（camelCase の JSON が PascalCase のプロパティへ一致する）。
            var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var options = node.Deserialize<CodeGenerationOptions>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )!;

            options.NamespaceName.Should().Be("Acme.Contracts");
            options.SplitFilesByCategory.Should().BeTrue();
            options.GenerateEfCore.Should().BeTrue();
            options.GenerateRepositories.Should().BeTrue();
            options.RepositoryDialects.Should().Equal("sqlserver", "sqlite");
            options.OutputFileName.Should().Be("Acme.g.cs");
            options.GenerateValueObjects.Should().BeTrue();
            // GUI で選んだ対象 DB が EffectiveRepositoryDialects（リスト優先）でそのまま有効になる
            options.EffectiveRepositoryDialects.Should().Equal("sqlserver", "sqlite");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
