using System.Text.Json;
using FluentAssertions;
using QuickER.AI.Mock;
using QuickER.Mcp;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectEmitTools"/> のツール定義（英語・スキーマ形状）とパス検証（相対のみ・スキャフォールド成果物の保護）を
/// 検証するテストクラス。
/// </summary>
public class MockProjectEmitToolsTests
{
    private const string Work = @"C:\work\out";

    /// <summary>emit_file の 1 ツールが即時ロードで揃うことを検証する</summary>
    [Fact(DisplayName = "emit_file 1 ツールが DeferLoading=false で揃う")]
    public void GetDefinitions_ContainsEmitFileImmediatelyLoaded()
    {
        var tools = MockProjectEmitTools.GetDefinitions();

        tools.Select(t => t.Name).Should().BeEquivalentTo(new[] { "emit_file" });
        MockProjectEmitTools.EmitFileToolName.Should().Be("emit_file");
        tools.Should().OnlyContain(t => t.DeferLoading == false);
    }

    /// <summary>説明が英語（非 CJK）で、提出規約（唯一の手段・全文・上書き）を含むことを検証する</summary>
    [Fact(DisplayName = "説明は英語で提出規約を含む")]
    public void EmitFile_Description_IsEnglishAndStatesRules()
    {
        var tool = MockProjectEmitTools.GetDefinitions().Single();

        tool.Description.Should().NotBeNullOrWhiteSpace();
        tool.Description.Should().NotContainAny("提出", "ファイル", "全文");
        tool.Description.Should().Contain("only way");
        tool.Description.Should().Contain("entire file");
        tool.Description.Should().Contain("overwrites");
    }

    /// <summary>入力スキーマ形状（type=object・必須 path/content）を検証する</summary>
    [Fact(DisplayName = "emit_file のスキーマ形状（必須 path/content）")]
    public void EmitFile_SchemaShape_IsCorrect()
    {
        var tool = MockProjectEmitTools.GetDefinitions().Single();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema));
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("object");

        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        required.Should().BeEquivalentTo(new[] { "path", "content" });

        var properties = root.GetProperty("properties");
        properties.TryGetProperty("path", out _).Should().BeTrue();
        properties.TryGetProperty("content", out _).Should().BeTrue();
    }

    /// <summary>正常な相対パスは受理され、出力フォルダ配下の絶対パスへ解決されることを検証する</summary>
    [Theory(DisplayName = "正常な相対パスは受理される")]
    [InlineData("MockApp/App.xaml")]
    [InlineData("MockApp/Views/OrderListView.xaml")]
    [InlineData("MockApp\\ViewModels\\MainViewModel.cs")]
    public void ResolveEmitPath_AllowsRelativeProjectPaths(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, path);

        result.Ok.Should().BeTrue();
        result.RelativePath.Should().NotContain("\\");
        result.FullPath.Should().StartWith(Work);
    }

    /// <summary>絶対パス・ドライブ文字・先頭スラッシュ・空は拒否されることを検証する</summary>
    [Theory(DisplayName = "絶対・ドライブ・先頭スラッシュ・空は拒否")]
    [InlineData(@"C:\Windows\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("D:relative.cs")]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEmitPath_RejectsAbsoluteAndEmpty(string path)
    {
        MockProjectEmitTools.ResolveEmitPath(Work, path).Ok.Should().BeFalse();
    }

    /// <summary>".." によるトラバーサルは拒否されることを検証する</summary>
    [Fact(DisplayName = "'..' トラバーサルは拒否")]
    public void ResolveEmitPath_RejectsTraversal()
    {
        MockProjectEmitTools.ResolveEmitPath(Work, "MockApp/../../evil.cs").Ok.Should().BeFalse();
    }

    /// <summary>スキャフォールド成果物（Generated/・design/・README・.csproj/.sln）への書き込みは拒否されることを検証する</summary>
    [Theory(DisplayName = "スキャフォールド成果物への書き込みは拒否")]
    [InlineData("MockApp/Generated/Entities.cs")]
    [InlineData("MockApp/design/mock/OrderList.html")]
    [InlineData("MockApp/README-QuickER.md")]
    [InlineData("MockApp/MockApp.csproj")]
    [InlineData("MockApp.sln")]
    public void ResolveEmitPath_RejectsScaffoldOwnedPaths(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, path);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }
}
