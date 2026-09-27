using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Extensibility;
using QuickER.Services;
using QuickER.Tests.Resources;
using QuickER.Tests.TestDoubles;

namespace QuickER.Tests.Gui.Services;

/// <summary>
/// メインウィンドウ終了時のモジュール後始末（<see cref="FeatureModuleShutdown"/>）を検証するテストクラス。
/// </summary>
/// <remarks>
/// モジュールの後始末は互いに独立なので、1 つが失敗しても残りへ必ず届かなければならない。
/// 届かないと、そのモジュールが抱えた常駐プロセス（codex app-server・copilot ランタイム・
/// claude / dotnet の子プロセス）が孤児として残る。
/// </remarks>
public class FeatureModuleShutdownTests
{
    /// <summary>後始末の呼び出しを記録するだけの検証用モジュール</summary>
    private sealed class RecordingModule : IFeatureModule
    {
        private readonly List<string> _log;
        private readonly bool _throws;

        public RecordingModule(string id, List<string> log, bool throws = false)
        {
            Id = id;
            _log = log;
            _throws = throws;
        }

        public string Id { get; }

        public void ConfigureServices(IServiceCollection services) { }

        public IReadOnlyList<FeatureToolbarItem> CreateToolbarItems(IServiceProvider services) =>
            [];

        public void OnMainWindowClosing(IServiceProvider services)
        {
            _log.Add(Id);

            if (_throws)
            {
                throw new InvalidOperationException($"{Id} の後始末に失敗");
            }
        }
    }

    /// <summary>全モジュールへ後始末が通知されることを検証する</summary>
    [Fact(DisplayName = "全モジュールへ後始末が通知される")]
    public void CloseAll_NotifiesEveryModule()
    {
        var log = new List<string>();
        var reporter = new RecordingShutdownFailureReporter();
        var modules = new IFeatureModule[]
        {
            new RecordingModule("a", log),
            new RecordingModule("b", log),
        };

        FeatureModuleShutdown.CloseAll(modules, services: null!, reporter);

        log.Should().Equal("a", "b");
        reporter.Reports.Should().BeEmpty();
    }

    /// <summary>
    /// 途中のモジュールが投げても後続のモジュールが呼ばれ、失敗が段の名前つきで記録されることを検証する。
    /// </summary>
    [Fact(DisplayName = "1 つのモジュールが投げても後続のモジュールは呼ばれる")]
    public void CloseAll_WhenModuleThrows_ContinuesToRemainingModules()
    {
        var log = new List<string>();
        var reporter = new RecordingShutdownFailureReporter();
        var modules = new IFeatureModule[]
        {
            new RecordingModule("ai-chat", log, throws: true),
            new RecordingModule("mock-generation", log, throws: true),
            new RecordingModule("code-generation", log),
        };

        var act = () => FeatureModuleShutdown.CloseAll(modules, services: null!, reporter);

        act.Should().NotThrow();
        log.Should().Equal("ai-chat", "mock-generation", "code-generation");
        reporter
            .Steps.Should()
            .Equal(
                "FeatureModule.ai-chat.OnMainWindowClosing",
                "FeatureModule.mock-generation.OnMainWindowClosing"
            );
    }

    /// <summary>
    /// <c>App.xaml.cs</c> の終了処理が、受け止めを持つ <see cref="FeatureModuleShutdown"/> を
    /// 経由していることをソース上で固定する。
    /// </summary>
    /// <remarks>
    /// <c>App</c> は WPF の <c>Application</c> 派生でテストから実体化できないため、ループを切り出した
    /// 共有ヘルパー側を直接検証する。その形が意味を持つのは <c>App</c> が確かにそれを使っているときだけなので、
    /// 素のループ（<c>OnMainWindowClosing</c> の直接呼び出し）へ戻っていないことをここで見張る。
    /// </remarks>
    [Fact(DisplayName = "App の終了処理は FeatureModuleShutdown を経由する")]
    public void App_UsesFeatureModuleShutdown()
    {
        var source = File.ReadAllText(
            Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Gui", "App.xaml.cs")
        );

        source.Should().Contain("FeatureModuleShutdown.CloseAll(");
        Regex
            .Matches(source, @"\.OnMainWindowClosing\s*\(")
            .Should()
            .BeEmpty("受け止めを持たない直接呼び出しへ戻っていないこと");
    }
}
