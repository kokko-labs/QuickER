using System.IO;
using AwesomeAssertions;
using QuickER.AI.Mock;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock;

/// <summary>
/// 最終ビルド（<c>dotnet build</c>）を実行する前に、エージェントが書いた「ビルドが設定として読み得る
/// ファイル」を検知して利用者へ確認する仕組みを固定するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 最終ビルドはエージェントのサンドボックスの外・ユーザー権限で走る唯一の地点なので、検知が効かなく
/// なっても「生成は成功する」＝ビルド・型検査・既存テストのどれでも気づけない。
/// </para>
/// <para>
/// 検知は<b>ホワイトリスト</b>（UI 層のソース・静的資産だけを通す）で、比較は<b>内容のハッシュ</b>で行う。
/// どちらを崩しても、危険な入口（自動 import されるファイル・更新日時を戻した書き換え）が素通りする。
/// </para>
/// </remarks>
public class MockProjectBuildBoundaryTests
{
    /// <summary>テストで使うプロジェクト名（スキャフォールドのレイアウト規則に合わせる）</summary>
    private const string ProjectName = "AcmeMock";

    /// <summary>境界越えを宣言できるフェイクのエージェント</summary>
    private sealed class FakeAgent : IMockProjectAgent
    {
        /// <summary>RunAsync 実行時に走らせる副作用（出力フォルダを受け取る）</summary>
        public Action<string>? OnRun { get; set; }

        /// <summary>最終ビルドが境界越えになると宣言するか</summary>
        public bool EscapesSandbox { get; set; } = true;

        public bool IsAvailable() => true;

        public bool FinalBuildEscapesSandbox => EscapesSandbox;

        public Task<MockProjectAgentOutcome> RunAsync(
            MockProjectAgentRequest request,
            Action<string> onProgress,
            CancellationToken cancellationToken
        )
        {
            OnRun?.Invoke(request.WorkingDirectory);
            return Task.FromResult(new MockProjectAgentOutcome(true, null, false));
        }

        public Task InterruptAsync() => Task.CompletedTask;
    }

    /// <summary>ビルド呼び出しの有無を数えるフェイクのビルド検証器</summary>
    private sealed class CountingBuildRunner : IBuildRunner
    {
        public int BuildCallCount { get; private set; }

        public Task<BuildRunResult> BuildAsync(
            string solutionFilePath,
            CancellationToken cancellationToken = default
        )
        {
            BuildCallCount++;
            return Task.FromResult(new BuildRunResult(true, "build output"));
        }

        public Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>確認の呼び出しを記録するフェイクの確認 seam</summary>
    private sealed class RecordingConfirm
    {
        public RecordingConfirm(bool answer) => Answer = answer;

        public bool Answer { get; }

        public int CallCount { get; private set; }

        public IReadOnlyList<string> LastPaths { get; private set; } = [];

        public bool Confirm(IReadOnlyList<string> paths)
        {
            CallCount++;
            LastPaths = paths;
            return Answer;
        }
    }

    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "QuickERTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Cleanup(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>スキャフォールド相当の初期状態（csproj + xaml + sln）を作る</summary>
    /// <remarks>ランナーは RunAsync の入口で記録するため、記録に載せたい既存物はここで作る。</remarks>
    private static void Scaffold(string outputDirectory)
    {
        var project = Path.Combine(outputDirectory, ProjectName);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(project, "MainWindow.xaml"), "<Window/>");
        File.WriteAllText(Path.Combine(outputDirectory, $"{ProjectName}.sln"), "solution");
    }

    /// <summary>1 ラン分の実行（初期状態は作成済みで、<paramref name="duringRun"/> が実行器の書き込みを模す）</summary>
    private static async Task<(
        MockProjectAgentResult Result,
        RecordingConfirm Confirm,
        CountingBuildRunner Build
    )> RunAsync(
        string outputDirectory,
        Action<string> duringRun,
        bool confirmAnswer = true,
        bool escapesSandbox = true,
        bool wireConfirm = true
    )
    {
        var agent = new FakeAgent { OnRun = duringRun, EscapesSandbox = escapesSandbox };
        var build = new CountingBuildRunner();
        var confirm = new RecordingConfirm(confirmAnswer);
        var runner = new MockProjectAgentRunner(
            agent,
            build,
            timeout: null,
            profile: null,
            buildTimeout: null,
            confirmBuild: wireConfirm ? confirm.Confirm : null
        );

        var result = await runner.RunAsync(
            outputDirectory,
            ProjectName,
            additionalInstructions: null,
            model: string.Empty,
            modelProvider: string.Empty,
            onProgress: _ => { },
            TestContext.Current.CancellationToken
        );

        return (result, confirm, build);
    }

    /// <summary>
    /// <c>{プロジェクト}.csproj.user</c> は <c>Microsoft.Common.CurrentVersion.targets</c> が import する
    /// （＝検証ビルドが中身を実行する）ため、書かれていたら確認する。
    /// </summary>
    [Fact(DisplayName = "csproj.user が書かれていたら最終ビルド前に確認する")]
    public async Task CsprojUser_TriggersConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (result, confirm, build) = await RunAsync(
                folder,
                dir =>
                    File.WriteAllText(
                        Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                        "<Project/>"
                    )
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().ContainSingle().Which.Should().EndWith(".csproj.user");
            build.BuildCallCount.Should().Be(1);
            result.Success.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// <c>global.json</c> は MSBuild より前に dotnet が読む（SDK の解決先が変わる）ため、
    /// 出力フォルダ直下に書かれていたら確認する。
    /// </summary>
    [Fact(DisplayName = "出力フォルダの global.json は最終ビルド前に確認する")]
    public async Task GlobalJson_TriggersConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (_, confirm, _) = await RunAsync(
                folder,
                dir => File.WriteAllText(Path.Combine(dir, "global.json"), "{}")
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().ContainSingle().Which.Should().Be("global.json");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>UI 層のソース・静的資産だけが増えたランでは何も尋ねない（確認の形骸化を防ぐ）</summary>
    [Theory(DisplayName = "UI 層のソース・静的資産だけなら確認しない")]
    [InlineData("Pages/Orders.razor")]
    [InlineData("Views/OrderView.xaml")]
    [InlineData("ViewModels/OrderViewModel.cs")]
    [InlineData("wwwroot/app.css")]
    [InlineData("wwwroot/site.js")]
    [InlineData("wwwroot/logo.png")]
    public async Task UiSourcesOnly_DoNotTriggerConfirmation(string relativePath)
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (result, confirm, build) = await RunAsync(
                folder,
                dir =>
                {
                    var full = Path.Combine(dir, ProjectName, relativePath.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, "content");
                }
            );

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
            result.Success.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 内容を書き換えたあと更新日時を元へ戻した csproj も検知する（メタデータでなく内容で比べる）。
    /// </summary>
    /// <remarks>
    /// エージェントは書き換え後に <see cref="File.SetLastWriteTimeUtc(string, DateTime)"/> を呼べるため、
    /// 更新日時・サイズでの比較は回避できる（サイズも同じ長さに揃えられる）。
    /// </remarks>
    [Fact(DisplayName = "更新日時を元へ戻した csproj の書き換えも検知する")]
    public async Task RewrittenCsprojWithRestoredTimestamp_TriggersConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var csproj = Path.Combine(folder, ProjectName, $"{ProjectName}.csproj");
            var originalWriteTime = File.GetLastWriteTimeUtc(csproj);
            var originalLength = new FileInfo(csproj).Length;

            var (_, confirm, _) = await RunAsync(
                folder,
                _ =>
                {
                    // 長さを変えずに内容だけ差し替える（"<Project/>" と同じ 10 文字）
                    File.WriteAllText(csproj, "<Evil___/>");
                    File.SetLastWriteTimeUtc(csproj, originalWriteTime);
                }
            );

            // 更新日時もサイズも元のまま＝メタデータ比較では差分にならないことを、テスト自身が確かめる
            File.GetLastWriteTimeUtc(csproj).Should().Be(originalWriteTime);
            new FileInfo(csproj).Length.Should().Be(originalLength);

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().ContainSingle().Which.Should().EndWith(".csproj");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 境界越えを宣言しない実行器（Claude Code＝無制限 Bash／API キー＝提出ホワイトリスト）では確認しない。
    /// </summary>
    [Fact(DisplayName = "境界越えを宣言しない実行器では確認しない")]
    public async Task AgentThatDoesNotEscapeSandbox_SkipsConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (result, confirm, build) = await RunAsync(
                folder,
                dir =>
                {
                    File.WriteAllText(Path.Combine(dir, "global.json"), "{}");
                    File.WriteAllText(
                        Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                        "<Project/>"
                    );
                },
                escapesSandbox: false
            );

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
            result.Success.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>確認を取り消したら最終ビルドを実行せず、未検証として完了する</summary>
    [Fact(DisplayName = "確認を取り消すと最終ビルドを実行せず未検証で完了する")]
    public async Task DeclinedConfirmation_SkipsBuild()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (result, confirm, build) = await RunAsync(
                folder,
                dir => File.WriteAllText(Path.Combine(dir, "global.json"), "{}"),
                confirmAnswer: false
            );

            confirm.CallCount.Should().Be(1);
            build.BuildCallCount.Should().Be(0);
            result.BuildDeclined.Should().BeTrue();
            result.BuildSucceeded.Should().BeFalse();
            result.Success.Should().BeFalse();
            result.TimedOut.Should().BeFalse();
            result.Canceled.Should().BeFalse();
            result.Message.Should().Be(MockStrings.Mock_Result_BuildNotVerified);

            // 何を書かれたのかはログにも残す（後から追えるようにする）
            File.ReadAllText(result.LogPath)
                .Should()
                .Contain("global.json")
                .And.Contain(MockStrings.Mock_Run_BuildDeclined);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>確認の手段が配線されていないときは、承認へ倒さずビルドを見送る</summary>
    [Fact(DisplayName = "確認 seam 未配線なら最終ビルドを実行しない")]
    public async Task MissingConfirmSeam_SkipsBuild()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (result, _, build) = await RunAsync(
                folder,
                dir => File.WriteAllText(Path.Combine(dir, "global.json"), "{}"),
                wireConfirm: false
            );

            build.BuildCallCount.Should().Be(0);
            result.BuildDeclined.Should().BeTrue();
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 隠し属性を付けたファイルも検知する（既定の列挙は Hidden / System を飛ばす＝検知の回避路になる）。
    /// </summary>
    [Fact(DisplayName = "隠し属性を付けた csproj.user も検知する")]
    public async Task HiddenFile_TriggersConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (_, confirm, _) = await RunAsync(
                folder,
                dir =>
                {
                    var path = Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user");
                    File.WriteAllText(path, "<Project/>");
                    File.SetAttributes(path, FileAttributes.Hidden);
                }
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().ContainSingle().Which.Should().EndWith(".csproj.user");
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// ジャンクション（ディレクトリの reparse point）を作る。
    /// </summary>
    /// <remarks>
    /// ジャンクションの作成に公開 API は無く、シンボリックリンクは特権（開発者モード）を要するため、
    /// 特権不要な <c>mklink /J</c> を使う。テストは Windows 専用プロジェクトにある。
    /// </remarks>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );
        process!.WaitForExit();
        process.ExitCode.Should().Be(0, "テスト前提のジャンクションを作成できる必要がある");
    }

    /// <summary>
    /// 出力フォルダの外を指すジャンクションは中へ辿らず、リンクそのものを 1 件として確認に出すことを検証する。
    /// </summary>
    /// <remarks>
    /// 辿ると <c>C:\</c> を指す 1 つでディスク全体をハッシュし続け（中断トークンを見ない）、しかも外のファイルが
    /// 出力フォルダ内の相対パスで一覧に紛れ込む。リンク先に置いた <c>Evil.csproj</c> が一覧に出ないことで
    /// 「辿っていない」を、名前を <c>styles.css</c>（ホワイトリストの拡張子）にしても出ることで
    /// 「リンクは名前に依らず比較する」を同時に固定する。
    /// </remarks>
    [Fact(DisplayName = "外を指すジャンクションは辿らずリンク自体を確認に出す")]
    public async Task DirectoryJunction_IsReportedWithoutTraversal()
    {
        var folder = NewTempFolder();
        var outside = NewTempFolder();

        try
        {
            Scaffold(folder);
            Directory.CreateDirectory(Path.Combine(outside, "nested"));
            File.WriteAllText(Path.Combine(outside, "Evil.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(outside, "nested", "global.json"), "{}");

            var link = Path.Combine(folder, ProjectName, "styles.css");

            var (_, confirm, build) = await RunAsync(folder, _ => CreateJunction(link, outside));

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().Equal($"{ProjectName}/styles.css");
            build.BuildCallCount.Should().Be(1);
        }
        finally
        {
            // リンクだけを先に外す（リンク先の中身は消さない・再帰削除はジャンクションで拒否されることがある）
            var junction = Path.Combine(folder, ProjectName, "styles.css");

            if (Directory.Exists(junction))
            {
                Directory.Delete(junction, recursive: false);
            }

            Cleanup(folder);
            Cleanup(outside);
        }
    }

    /// <summary>
    /// 外を指すファイルのシンボリックリンク（<c>.csproj.user</c>）も検知することを検証する。
    /// </summary>
    /// <remarks>
    /// reparse point を列挙の属性で飛ばす方式にすると、リンクの中への再帰と一緒にファイルのリンクまで消え、
    /// 検知の回避路になる。シンボリックリンクの作成には特権（開発者モード・管理者）が要る。
    /// </remarks>
    [Fact(DisplayName = "外を指すファイルのシンボリックリンクの csproj.user も検知する")]
    public async Task FileSymbolicLink_TriggersConfirmation()
    {
        var folder = NewTempFolder();
        var outside = NewTempFolder();

        try
        {
            Scaffold(folder);
            var target = Path.Combine(outside, "evil.targets");
            File.WriteAllText(target, "<Project/>");

            var (_, confirm, _) = await RunAsync(
                folder,
                dir =>
                    File.CreateSymbolicLink(
                        Path.Combine(dir, ProjectName, $"{ProjectName}.csproj.user"),
                        target
                    )
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().Equal($"{ProjectName}/{ProjectName}.csproj.user");
        }
        finally
        {
            Cleanup(folder);
            Cleanup(outside);
        }
    }

    /// <summary>
    /// 最終ビルド直前に消される <c>obj</c> / <c>bin</c> は検知の対象にしない（削除する範囲と揃える）。
    /// </summary>
    [Fact(DisplayName = "obj / bin へ書かれたものは確認の対象にしない（直前に消すため）")]
    public async Task BuildOutputDirectories_AreNotReported()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            var (_, confirm, build) = await RunAsync(
                folder,
                dir =>
                {
                    var obj = Path.Combine(dir, ProjectName, "obj");
                    Directory.CreateDirectory(obj);
                    File.WriteAllText(
                        Path.Combine(obj, $"{ProjectName}.csproj.evil.targets"),
                        "<Project/>"
                    );
                }
            );

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
        }
        finally
        {
            Cleanup(folder);
        }
    }

    /// <summary>
    /// 危険な入口の拡張子がホワイトリストへ紛れ込んでいないことを、実測で得た集合と突き合わせて固定する。
    /// </summary>
    /// <remarks>
    /// MSBuild／dotnet が自動で読む入口は版とともに増えるため、判定はホワイトリストで行う。ここで見るのは
    /// 「既に分かっている危険な拡張子が通らないこと」＝ホワイトリストが静かに広がっていないことの網。
    /// </remarks>
    [Theory(DisplayName = "自動で読まれる設定ファイルの拡張子はホワイトリストに入らない")]
    [InlineData(".props")]
    [InlineData(".targets")]
    [InlineData(".user")]
    [InlineData(".pubxml")]
    [InlineData(".json")]
    [InlineData(".config")]
    [InlineData(".rsp")]
    [InlineData(".csproj")]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public void SafeExtensions_DoNotIncludeBuildInputs(string extension) =>
        MockProjectBuildBoundary.SafeExtensions.Should().NotContain(extension);

    /// <summary>本番の実行器 4 実装が宣言する境界越えの有無を、実装の全数とともに固定する</summary>
    /// <remarks>
    /// <para>
    /// 期待値の根拠: Claude Code は許可ツールに無制限の <c>Bash</c> を含むため最終ビルドで新しく越える
    /// 境界が無く、API キー方式は提出できる拡張子が UI 層のソースだけなのでビルド設定を書けない。
    /// Codex（<c>workspace-write</c>）と Copilot（出力フォルダ配下の自動承認）は出力フォルダの csproj を
    /// 書ける一方その外でコマンドを実行できないため、サンドボックス外の最終ビルドが境界越えになる。
    /// </para>
    /// <para>
    /// 実装の列挙はアセンブリ走査で行う＝バックエンドを増やしたときに、この表の更新（＝どちらなのかの
    /// 判断）を強制する。インスタンス化はコンストラクタ引数（クライアント実装）を避けるため
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject"/> を使う
    /// （どの実装もこのプロパティを定数として返すため、フィールドを参照しない）。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "実行器 4 実装の境界越え宣言を固定する")]
    public void Agents_DeclareFinalBuildEscapesSandbox()
    {
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["ClaudeCodeMockProjectAgent"] = false,
            ["ApiKeyMockProjectAgent"] = false,
            ["CodexMockProjectAgent"] = true,
            ["CopilotMockProjectAgent"] = true,
        };

        var agentTypes = typeof(IMockProjectAgent)
            .Assembly.GetTypes()
            .Where(type =>
                type.IsClass && !type.IsAbstract && typeof(IMockProjectAgent).IsAssignableFrom(type)
            )
            .ToArray();

        agentTypes.Select(type => type.Name).Should().BeEquivalentTo(expected.Keys);

        foreach (var type in agentTypes)
        {
            var agent = (IMockProjectAgent)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);

            agent
                .FinalBuildEscapesSandbox.Should()
                .Be(expected[type.Name], "実行器 {0} の宣言", type.Name);
        }
    }
}
