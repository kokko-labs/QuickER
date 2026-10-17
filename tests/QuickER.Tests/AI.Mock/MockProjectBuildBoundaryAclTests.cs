using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using QuickER.AI.Mock;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 最終ビルド前の変更検知が、一覧を読めないフォルダを黙って飛ばさないこと（フェイルクローズ）を固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// エージェントは出力フォルダの ACL を変えて、自分に「一覧の読み取り」だけを拒否できる。ファイルは名前で開けるので
/// MSBuild は <c>{プロジェクト}.csproj.user</c> を読むが、アクセス拒否を無視する走査では差分が空になり、
/// 確認なしでビルドへ進んでしまう。読めないフォルダ（出力フォルダ自身を含む）は、毎回「変更」として一覧に出す。
/// </para>
/// <para>
/// どのテストも、付けた Deny の ACE を finally で外してから後始末する（外さないと一時フォルダを消せない）。
/// </para>
/// </remarks>
public class MockProjectBuildBoundaryAclTests
{
    /// <summary>テストで使うプロジェクト名（スキャフォールドのレイアウト規則に合わせる）</summary>
    private const string ProjectName = "AcmeMock";

    /// <summary>今のユーザーに一覧の読み取りだけを拒否する ACE（子へは継承しない）</summary>
    private static FileSystemAccessRule DenyList() =>
        new(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny
        );

    /// <summary>フォルダへ一覧の拒否を付ける</summary>
    private static void DenyListing(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(DenyList());
        info.SetAccessControl(security);
    }

    /// <summary>付けた一覧の拒否を外す（付いていなければ何もしない）</summary>
    private static void AllowListing(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.RemoveAccessRule(DenyList());
        info.SetAccessControl(security);
    }

    /// <summary>エージェント役（実行中に出力フォルダへ書く・ACL を変える）</summary>
    private sealed class FakeAgent(Action<string> onRun) : IMockProjectAgent
    {
        public bool IsAvailable() => true;

        public bool FinalBuildEscapesSandbox => true;

        public Task<MockProjectAgentOutcome> RunAsync(
            MockProjectAgentRequest request,
            Action<string> onProgress,
            CancellationToken cancellationToken
        )
        {
            onRun(request.WorkingDirectory);
            return Task.FromResult(new MockProjectAgentOutcome(true, null, false));
        }

        public Task InterruptAsync() => Task.CompletedTask;
    }

    /// <summary>ビルドの呼び出しを数えるビルド検証器</summary>
    private sealed class CountingBuildRunner : IBuildRunner
    {
        public int Calls { get; private set; }

        public Task<BuildRunResult> BuildAsync(
            string solutionFilePath,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(new BuildRunResult(true, "build output"));
        }

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>スキャフォールド相当の初期状態（csproj + xaml + sln）を作り、出力フォルダを返す</summary>
    private static string ScaffoldNewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(folder, ProjectName);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(project, "MainWindow.xaml"), "<Window/>");
        File.WriteAllText(Path.Combine(folder, $"{ProjectName}.sln"), "solution");
        return folder;
    }

    /// <summary>拒否を外してから一時フォルダを消す</summary>
    private static void Cleanup(string folder, params string[] deniedFolders)
    {
        foreach (var denied in deniedFolders)
        {
            AllowListing(denied);
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>1 ラン分を流す（確認は取り消す＝確認が出ればビルドしない）</summary>
    private static async Task<(
        int ConfirmCalls,
        IReadOnlyList<string> Paths,
        int BuildCalls
    )> RunAsync(string folder, Action<string> duringRun)
    {
        var confirmCalls = 0;
        IReadOnlyList<string> paths = [];
        var build = new CountingBuildRunner();
        var runner = new MockProjectAgentRunner(
            new FakeAgent(duringRun),
            build,
            timeout: null,
            profile: null,
            buildTimeout: null,
            confirmBuild: listed =>
            {
                confirmCalls++;
                paths = listed;
                return false;
            }
        );

        await runner.RunAsync(
            folder,
            ProjectName,
            additionalInstructions: null,
            model: string.Empty,
            modelProvider: string.Empty,
            onProgress: _ => { },
            TestContext.Current.CancellationToken
        );

        return (confirmCalls, paths, build.Calls);
    }

    [Fact(
        DisplayName = "出力フォルダの一覧を拒否されたら、出力フォルダ自身を「.」として確認に出す"
    )]
    public void Snapshot_DeniedOutputRoot_IsListed()
    {
        var folder = ScaffoldNewFolder();
        var project = Path.Combine(folder, ProjectName);

        try
        {
            var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                folder,
                ProjectName,
                TestContext.Current.CancellationToken
            );
            File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj.user"), "<Project/>");
            DenyListing(folder);

            // ファイルは名前で読める（MSBuild は名前で開く）＝拒否は回避路として成り立つ
            File.Exists(Path.Combine(project, $"{ProjectName}.csproj.user")).Should().BeTrue();

            snapshot.DetectUnexpectedChanges(folder, ProjectName).Should().Equal(".");
        }
        finally
        {
            Cleanup(folder, folder);
        }
    }

    [Fact(DisplayName = "サブフォルダの一覧を拒否されたら、そのフォルダを確認に出す")]
    public void Snapshot_DeniedSubfolder_IsListed()
    {
        var folder = ScaffoldNewFolder();
        var hidden = Path.Combine(folder, ProjectName, "Build");
        Directory.CreateDirectory(hidden);

        try
        {
            var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                folder,
                ProjectName,
                TestContext.Current.CancellationToken
            );
            File.WriteAllText(Path.Combine(hidden, "Directory.Build.props"), "<Project/>");
            DenyListing(hidden);

            snapshot
                .DetectUnexpectedChanges(folder, ProjectName)
                .Should()
                .Equal($"{ProjectName}/Build");
        }
        finally
        {
            Cleanup(folder, hidden);
        }
    }

    [Fact(
        DisplayName = "記録の時点で読めなかったフォルダも、変化が無いとは言えないので毎回確認に出す"
    )]
    public void Snapshot_FolderDeniedFromTheStart_IsListedEveryTime()
    {
        var folder = ScaffoldNewFolder();
        var hidden = Path.Combine(folder, ProjectName, "Build");
        Directory.CreateDirectory(hidden);
        DenyListing(hidden);

        try
        {
            var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                folder,
                ProjectName,
                TestContext.Current.CancellationToken
            );

            snapshot
                .DetectUnexpectedChanges(folder, ProjectName)
                .Should()
                .Equal($"{ProjectName}/Build");
        }
        finally
        {
            Cleanup(folder, hidden);
        }
    }

    [Fact(DisplayName = "一覧を読めないフォルダは、名前が確認の要らない拡張子でも確認に出す")]
    public void Snapshot_DeniedFolderNamedLikeSafeAsset_IsListed()
    {
        var folder = ScaffoldNewFolder();
        var hidden = Path.Combine(folder, ProjectName, "assets.css");
        Directory.CreateDirectory(hidden);
        DenyListing(hidden);

        try
        {
            var snapshot = MockProjectBuildBoundary.Snapshot.Capture(
                folder,
                ProjectName,
                TestContext.Current.CancellationToken
            );

            snapshot
                .DetectUnexpectedChanges(folder, ProjectName)
                .Should()
                .Equal($"{ProjectName}/assets.css");
        }
        finally
        {
            Cleanup(folder, hidden);
        }
    }

    [Fact(DisplayName = "実行中に出力フォルダの一覧を拒否されても、確認なしにビルドしない")]
    public async Task Run_DeniedOutputRoot_AsksBeforeBuilding()
    {
        var folder = ScaffoldNewFolder();

        try
        {
            var (confirmCalls, paths, buildCalls) = await RunAsync(
                folder,
                dir =>
                {
                    File.WriteAllText(
                        Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                        "<Project/>"
                    );
                    DenyListing(dir);
                }
            );

            (confirmCalls, buildCalls).Should().Be((1, 0));
            paths.Should().Contain(".");
        }
        finally
        {
            Cleanup(folder, folder);
        }
    }

    [Fact(DisplayName = "実行中にサブフォルダの一覧を拒否されても、確認なしにビルドしない")]
    public async Task Run_DeniedSubfolder_AsksBeforeBuilding()
    {
        var folder = ScaffoldNewFolder();
        var hidden = Path.Combine(folder, ProjectName, "Build");

        try
        {
            var (confirmCalls, paths, buildCalls) = await RunAsync(
                folder,
                dir =>
                {
                    var target = Path.Combine(dir, ProjectName, "Build");
                    Directory.CreateDirectory(target);
                    File.WriteAllText(Path.Combine(target, "Directory.Build.props"), "<Project/>");
                    DenyListing(target);
                }
            );

            (confirmCalls, buildCalls).Should().Be((1, 0));
            paths.Should().Contain($"{ProjectName}/Build");
        }
        finally
        {
            Cleanup(folder, hidden);
        }
    }
}
