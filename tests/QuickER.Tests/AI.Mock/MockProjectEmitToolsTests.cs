using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectEmitTools"/> のツール定義（英語・スキーマ形状）と、ターゲットプロファイルの許可拡張子の上限を検証するテストクラス。
/// </summary>
/// <remarks>
/// 提出パスの判定の行は `Platform/OsBoundary/PathEntranceRows.Emit.cs` へ移した。ここに残すのはツール定義とプロファイルの構造。
/// </remarks>
public class MockProjectEmitToolsTests
{
    /// <summary>emit_file の 1 ツールが即時ロードで揃うことを検証する</summary>
    [Fact(DisplayName = "emit_file 1 ツールが DeferLoading=false で揃う")]
    public void GetDefinitions_ContainsEmitFileImmediatelyLoaded()
    {
        var tools = MockProjectEmitTools.GetDefinitions();

        tools.Select(t => t.Name).Should().BeEquivalentTo(new[] { "emit_file" });
        MockProjectEmitTools.EmitFileToolName.Should().Be("emit_file");
        tools.Should().OnlyContain(t => t.DeferLoading == false);
    }

    /// <summary>説明が英語（非 CJK）で、提出規約（唯一の手段・全文・上書き・許可されるのは UI 層ソースだけ）を含むことを検証する</summary>
    [Fact(DisplayName = "説明は英語で提出規約（ホワイトリスト）を含む")]
    public void EmitFile_Description_IsEnglishAndStatesRules()
    {
        var tool = MockProjectEmitTools.GetDefinitions().Single();

        tool.Description.Should().NotBeNullOrWhiteSpace();
        tool.Description.Should().NotContainAny("提出", "ファイル", "全文");
        tool.Description.Should().Contain("only way");
        tool.Description.Should().Contain("entire file");
        tool.Description.Should().Contain("overwrites");

        // ホワイトリスト規則（提出できるのはターゲットの UI 層ソースだけ）を明示している
        tool.Description.Should().Contain("Only UI-layer source files");
        tool.Description.Should().Contain("Directory.Build.props");
        tool.Description.Should().Contain("obj/");
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

    /// <summary>
    /// 全ターゲットプロファイルを機械列挙し、許可拡張子が中央の上限集合の部分集合であることを検証する。
    /// </summary>
    /// <remarks>
    /// 新しいターゲットを足すと、抽象メンバーの実装がコンパイル時に強制され、宣言が上限を超えればここで落ちる
    /// （プロファイル側の宣言だけでは穴が開かない）。
    /// </remarks>
    [Fact(DisplayName = "全プロファイルの許可拡張子は中央の上限集合の部分集合")]
    public void AllProfiles_AllowedEmitExtensions_AreSubsetOfSupported()
    {
        var profileTypes = typeof(MockProjectTargetProfile)
            .Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(MockProjectTargetProfile).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        // WPF / Blazor の 2 実装は最低でも存在する（列挙が空振りしていないことの確認）
        profileTypes.Count.Should().BeGreaterThan(1);

        foreach (var type in profileTypes)
        {
            var profile = (MockProjectTargetProfile)
                Activator.CreateInstance(type, nonPublic: true)!;

            profile
                .AllowedEmitExtensions.Should()
                .NotBeEmpty($"{type.Name} must declare at least one emittable extension");

            profile
                .AllowedEmitExtensions.Should()
                .BeSubsetOf(
                    MockProjectEmitTools.SupportedEmitExtensions,
                    $"{type.Name} must not widen the central cap of emittable extensions"
                );

            profile
                .AllowedEmitExtensions.Should()
                .OnlyContain(
                    extension => extension.StartsWith('.'),
                    $"{type.Name} must declare extensions with a leading dot"
                );
        }
    }

    /// <summary>公開されている全ターゲットにプロファイルが対応していることを検証する（未対応は例外で赤くなる）</summary>
    [Fact(DisplayName = "全ターゲットにプロファイルが対応する")]
    public void AllTargets_ResolveToProfile()
    {
        var targets = typeof(MockProjectTarget)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MockProjectTarget))
            .Select(f => (MockProjectTarget)f.GetValue(null)!)
            .ToList();

        targets.Count.Should().BeGreaterThan(1);

        foreach (var target in targets)
        {
            var profile = MockProjectTargetProfile.Resolve(target);

            profile.Target.Id.Should().Be(target.Id);
            profile.AllowedEmitExtensions.Should().NotBeEmpty();
        }
    }
}
