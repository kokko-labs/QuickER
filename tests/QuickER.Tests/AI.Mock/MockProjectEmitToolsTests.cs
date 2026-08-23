using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using QuickER.AI.Mock;
using QuickER.Mcp;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// <see cref="MockProjectEmitTools"/> のツール定義（英語・スキーマ形状）とパス検証（相対のみ・拡張子ホワイトリスト・
/// 保護フォルダ／スキャフォールド成果物の保護）を検証するテストクラス。
/// </summary>
public class MockProjectEmitToolsTests
{
    private const string Work = @"C:\work\out";

    private static readonly MockProjectTargetProfile Wpf = MockProjectTargetProfile.Wpf;
    private static readonly MockProjectTargetProfile Blazor = MockProjectTargetProfile.Blazor;

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

    /// <summary>WPF の UI 層ソース（.xaml / .cs）は受理され、出力フォルダ配下の絶対パスへ解決されることを検証する</summary>
    [Theory(DisplayName = "WPF: 許可拡張子の相対パスは受理される")]
    [InlineData("MockApp/App.xaml")]
    [InlineData("MockApp/App.xaml.cs")]
    [InlineData("MockApp/Views/OrderListView.xaml")]
    [InlineData("MockApp\\ViewModels\\MainViewModel.cs")]
    public void ResolveEmitPath_Wpf_AllowsUiSourcePaths(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, Wpf, path);

        result.Ok.Should().BeTrue();
        result.RelativePath.Should().NotContain("\\");
        result.FullPath.Should().StartWith(Work);
    }

    /// <summary>Blazor の UI 層ソース（.razor / .css / .cs）は受理されることを検証する</summary>
    [Theory(DisplayName = "Blazor: 許可拡張子の相対パスは受理される")]
    [InlineData("MockApp/Components/Pages/OrderList.razor")]
    [InlineData("MockApp/Components/_Imports.razor")]
    [InlineData("MockApp/wwwroot/style.css")]
    [InlineData("MockApp/Program.cs")]
    public void ResolveEmitPath_Blazor_AllowsUiSourcePaths(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, Blazor, path);

        result.Ok.Should().BeTrue();
        result.FullPath.Should().StartWith(Work);
    }

    /// <summary>絶対パス・ドライブ文字・先頭スラッシュ・空は拒否されることを検証する</summary>
    [Theory(DisplayName = "絶対・ドライブ・先頭スラッシュ・空は拒否")]
    [InlineData(@"C:\Windows\evil.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("D:relative.cs")]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEmitPath_RejectsAbsoluteAndEmpty(string path)
    {
        MockProjectEmitTools.ResolveEmitPath(Work, Wpf, path).Ok.Should().BeFalse();
    }

    /// <summary>".." によるトラバーサルは拒否されることを検証する</summary>
    [Fact(DisplayName = "'..' トラバーサルは拒否")]
    public void ResolveEmitPath_RejectsTraversal()
    {
        MockProjectEmitTools
            .ResolveEmitPath(Work, Wpf, "MockApp/../../evil.cs")
            .Ok.Should()
            .BeFalse();
    }

    /// <summary>スキャフォールド成果物（Generated/・design/・README・.csproj/.sln・NuGet.Config）への書き込みは拒否されることを検証する</summary>
    [Theory(DisplayName = "スキャフォールド成果物への書き込みは拒否")]
    [InlineData("MockApp/Generated/Entities.cs")]
    [InlineData("MockApp/design/mock/OrderList.html")]
    [InlineData("MockApp/README-QuickER.md")]
    [InlineData("MockApp/MockApp.csproj")]
    [InlineData("MockApp.sln")]
    [InlineData("NuGet.Config")]
    [InlineData("MockApp/nuget.config")]
    public void ResolveEmitPath_RejectsScaffoldOwnedPaths(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, Wpf, path);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// MSBuild が自動 import する制御ファイル（＝ビルド検証がそのままユーザー権限のコード実行になるもの）が
    /// 拒否されることを検証する。
    /// </summary>
    [Theory(DisplayName = "MSBuild 制御ファイル・設定ファイルは拒否")]
    [InlineData("Directory.Build.props")]
    [InlineData("MockApp/Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    [InlineData("Directory.Build.rsp")]
    [InlineData("MockApp/appsettings.json")]
    [InlineData("MockApp/Setup.ps1")]
    public void ResolveEmitPath_RejectsBuildControlFiles(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, Wpf, path);

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>ビルドの入出力フォルダ（obj/・bin/）は拡張子に依らず拒否されることを検証する</summary>
    [Theory(DisplayName = "obj/・bin/ 配下は拒否")]
    [InlineData("MockApp/obj/project.assets.json")]
    [InlineData("MockApp/obj/Debug/Evil.cs")]
    [InlineData("MockApp/bin/x.cs")]
    [InlineData("bin/App.xaml")]
    public void ResolveEmitPath_RejectsBuildFolders(string path)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(Work, Wpf, path);

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("folder");
    }

    /// <summary>拡張子のないファイルは拒否されることを検証する</summary>
    [Theory(DisplayName = "拡張子なしのファイルは拒否")]
    [InlineData("MockApp/Dockerfile")]
    [InlineData("Makefile")]
    public void ResolveEmitPath_RejectsExtensionlessFile(string path)
    {
        MockProjectEmitTools.ResolveEmitPath(Work, Wpf, path).Ok.Should().BeFalse();
    }

    /// <summary>ターゲット外の UI 拡張子（WPF の .razor／Blazor の .xaml）は拒否されることを検証する</summary>
    [Fact(DisplayName = "ターゲット外の UI 拡張子は拒否")]
    public void ResolveEmitPath_RejectsExtensionOfAnotherTarget()
    {
        MockProjectEmitTools
            .ResolveEmitPath(Work, Wpf, "MockApp/Components/Pages/OrderList.razor")
            .Ok.Should()
            .BeFalse();

        MockProjectEmitTools
            .ResolveEmitPath(Work, Blazor, "MockApp/App.xaml")
            .Ok.Should()
            .BeFalse();
    }

    /// <summary>拒否メッセージが許可拡張子を列挙する（AI が次の提出を正せる）ことを検証する</summary>
    [Fact(DisplayName = "拒否メッセージは許可拡張子を列挙する")]
    public void ResolveEmitPath_RejectionMessage_ListsAllowedExtensions()
    {
        var wpf = MockProjectEmitTools.ResolveEmitPath(Work, Wpf, "Directory.Build.props");

        wpf.Ok.Should().BeFalse();
        wpf.Error.Should().Contain(".cs").And.Contain(".xaml");
        wpf.Error.Should().NotContain(".razor");

        var blazor = MockProjectEmitTools.ResolveEmitPath(Work, Blazor, "Directory.Build.props");

        blazor.Ok.Should().BeFalse();
        blazor.Error.Should().Contain(".cs").And.Contain(".css").And.Contain(".razor");
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
