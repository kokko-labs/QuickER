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

        public string BackendName => "FakeBackend";

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

        /// <summary>ビルド開始時点の状態を観測するフック（obj の中身の有無など）</summary>
        public Action? OnBuild { get; set; }

        public Task<BuildRunResult> BuildAsync(
            string solutionFilePath,
            CancellationToken cancellationToken = default
        )
        {
            BuildCallCount++;
            OnBuild?.Invoke();
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
        bool wireConfirm = true,
        Action? onBuild = null
    )
    {
        var agent = new FakeAgent { OnRun = duringRun, EscapesSandbox = escapesSandbox };
        var build = new CountingBuildRunner { OnBuild = onBuild };
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

            var (_, confirm, build) = await RunAsync(
                folder,
                _ => ReparsePointTestHelper.CreateJunction(link, outside)
            );

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

    /// <summary>出力フォルダからの相対パスで書いた、プロジェクトの obj 直下のターゲット</summary>
    private static readonly string ObjTargetsRelative =
        $"{ProjectName}/obj/{ProjectName}.csproj.evil.targets";

    /// <summary>出力フォルダ配下の相対パス（<c>/</c> 区切り）を絶対パスへ</summary>
    private static string FullPath(string outputDirectory, string relative) =>
        Path.Combine(outputDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>ファイルを書いて読み取り専用にする（フォルダも作る）</summary>
    private static void WriteReadOnly(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project/>");
        File.SetAttributes(path, FileAttributes.ReadOnly);
    }

    /// <summary>ファイルを書いて、削除の共有を許さないハンドルで開いたままにする（削除が必ず失敗する）</summary>
    private static Microsoft.Win32.SafeHandles.SafeFileHandle WriteAndLock(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project/>");
        return ReparsePointTestHelper.OpenWithoutDeleteSharing(path);
    }

    /// <summary>
    /// 読み取り専用の <c>obj/*.targets</c> が、確認なしに最終ビルドへ渡らないこと（安全側の性質）。
    /// </summary>
    /// <remarks>
    /// Windows の <see cref="Directory.Delete(string, bool)"/> は読み取り専用を外さないので、素の削除では
    /// このファイルが残り、ワイルドカード import で最終ビルドに実行される。ここで固定するのは実装の手段でなく
    /// 結果＝「ビルドの時点でファイルが無い」か「確認一覧に載る（確認手段が無ければビルドしない）」のどちらか。
    /// 読み取り専用の解除を外しても、残存検査が拾うのでこのテストは緑のまま（二重化の確認）。
    /// </remarks>
    [Theory(DisplayName = "読み取り専用の obj/*.targets を確認なしに最終ビルドへ渡さない")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadOnlyObjTargets_NeverReachBuildUnconfirmed(bool wireConfirm)
    {
        var folder = NewTempFolder();
        var targets = FullPath(folder, ObjTargetsRelative);

        try
        {
            Scaffold(folder);
            bool? existedAtBuild = null;

            var (result, confirm, build) = await RunAsync(
                folder,
                _ => WriteReadOnly(targets),
                wireConfirm: wireConfirm,
                onBuild: () => existedAtBuild = File.Exists(targets)
            );

            if (build.BuildCallCount == 0)
            {
                // ビルドしなかったのなら、確認する手段が無かった場合に限る
                result.BuildDeclined.Should().BeTrue();
                wireConfirm.Should().BeFalse();
            }
            else if (existedAtBuild == true)
            {
                // 残ったままビルドしたのなら、利用者がそれを一覧で見て承認していなければならない
                wireConfirm.Should().BeTrue();
                confirm.LastPaths.Should().Contain(ObjTargetsRelative);
            }
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>
    /// 読み取り専用の属性を外してから消すので、読み取り専用の <c>obj/*.targets</c> は確認なしに消えて
    /// そのままビルドする（現行の実装の結果＝上の性質のうち「ファイルが無い」側）。
    /// </summary>
    [Fact(DisplayName = "読み取り専用の obj/*.targets も属性を外して消し、確認なしでビルドする")]
    public async Task ReadOnlyObjTargets_AreDeletedBeforeBuild()
    {
        var folder = NewTempFolder();
        var targets = FullPath(folder, ObjTargetsRelative);

        try
        {
            Scaffold(folder);
            bool? existedAtBuild = null;

            var (result, confirm, build) = await RunAsync(
                folder,
                _ => WriteReadOnly(targets),
                onBuild: () => existedAtBuild = File.Exists(targets)
            );

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
            existedAtBuild.Should().BeFalse();
            result.Success.Should().BeTrue();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>
    /// 削除できずに残った <c>obj</c> のファイルは確認一覧へ回り、承認したときだけビルドする（フェイルクローズ）。
    /// </summary>
    /// <remarks>
    /// 削除の共有を許さないハンドルで開いたままにして、削除を確実に失敗させる（読み取り専用と違い、
    /// QuickER の側からは外せない）。変更検知は obj / bin を見ないので、拾えるのは残存検査だけ。
    /// </remarks>
    [Theory(DisplayName = "削除できずに残った obj のファイルは確認一覧に出す")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UndeletableObjFile_IsListedForConfirmation(bool confirmAnswer)
    {
        var folder = NewTempFolder();
        var targets = FullPath(folder, ObjTargetsRelative);
        Microsoft.Win32.SafeHandles.SafeFileHandle? lockHandle = null;

        try
        {
            Scaffold(folder);

            var (result, confirm, build) = await RunAsync(
                folder,
                _ => lockHandle = WriteAndLock(targets),
                confirmAnswer: confirmAnswer
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().Equal(ObjTargetsRelative);
            build.BuildCallCount.Should().Be(confirmAnswer ? 1 : 0);
            result.BuildDeclined.Should().Be(!confirmAnswer);
            File.ReadAllText(result.LogPath)
                .Should()
                .Contain(MockStrings.Mock_Run_BuildOutputsNotDeleted)
                .And.Contain(ObjTargetsRelative);
        }
        finally
        {
            lockHandle?.Dispose();
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>削除できずに残った obj があり、確認する手段が無ければビルドしない</summary>
    [Fact(
        DisplayName = "削除できずに残った obj があり確認 seam が未配線なら最終ビルドを実行しない"
    )]
    public async Task UndeletableObjFile_WithoutConfirmSeam_SkipsBuild()
    {
        var folder = NewTempFolder();
        var targets = FullPath(folder, ObjTargetsRelative);
        Microsoft.Win32.SafeHandles.SafeFileHandle? lockHandle = null;

        try
        {
            Scaffold(folder);

            var (result, _, build) = await RunAsync(
                folder,
                _ => lockHandle = WriteAndLock(targets),
                wireConfirm: false
            );

            build.BuildCallCount.Should().Be(0);
            result.BuildDeclined.Should().BeTrue();
        }
        finally
        {
            lockHandle?.Dispose();
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>
    /// 境界越えを宣言しない実行器では、残存は確認に回さずログへ残すだけ（黙らせない）。
    /// </summary>
    [Fact(DisplayName = "境界越えを宣言しない実行器では残った obj をログへ残して確認しない")]
    public async Task UndeletableObjFile_NonEscapingAgent_IsLoggedOnly()
    {
        var folder = NewTempFolder();
        var targets = FullPath(folder, ObjTargetsRelative);
        Microsoft.Win32.SafeHandles.SafeFileHandle? lockHandle = null;

        try
        {
            Scaffold(folder);

            var (result, confirm, build) = await RunAsync(
                folder,
                _ => lockHandle = WriteAndLock(targets),
                escapesSandbox: false
            );

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
            File.ReadAllText(result.LogPath)
                .Should()
                .Contain(MockStrings.Mock_Run_BuildOutputsNotDeleted)
                .And.Contain(ObjTargetsRelative);
        }
        finally
        {
            lockHandle?.Dispose();
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>
    /// 外せなかった obj のジャンクションは、フォルダそのものを 1 件として確認一覧に出す（中へ入らない・重複しない）。
    /// </summary>
    /// <remarks>
    /// 差分（変更検知）と残存は同じ表記の 1 つの一覧へまとめる。obj / bin はフォルダ自身も差分の対象外なので
    /// 2 つの出どころは重ならないが、まとめる側でも重複を除く（二重化）。リンク先の中身は消さず、一覧にも出さない。
    /// </remarks>
    [Fact(DisplayName = "外せなかった obj のジャンクションはフォルダ自体を 1 件だけ確認に出す")]
    public async Task UndeletableObjJunction_IsListedOnceWithoutTraversal()
    {
        var folder = NewTempFolder();
        var outside = NewTempFolder();
        var obj = Path.Combine(folder, ProjectName, "obj");
        Microsoft.Win32.SafeHandles.SafeFileHandle? lockHandle = null;

        try
        {
            Scaffold(folder);
            var outsideTargets = Path.Combine(outside, $"{ProjectName}.csproj.evil.targets");
            File.WriteAllText(outsideTargets, "<Project/>");

            var (_, confirm, build) = await RunAsync(
                folder,
                _ =>
                {
                    ReparsePointTestHelper.CreateJunction(obj, outside);
                    lockHandle = ReparsePointTestHelper.OpenWithoutDeleteSharing(obj);
                }
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().OnlyHaveUniqueItems();
            confirm.LastPaths.Should().Equal($"{ProjectName}/obj");
            build.BuildCallCount.Should().Be(1);
            File.Exists(outsideTargets).Should().BeTrue("リンク先の中身には触れない");
        }
        finally
        {
            lockHandle?.Dispose();
            ReparsePointTestHelper.DeleteTree(folder);
            ReparsePointTestHelper.DeleteTree(outside);
        }
    }

    /// <summary>
    /// リンクでない reparse フォルダ（OneDrive の Files On-Demand と同じ形）は中へ入って比較する。
    /// </summary>
    /// <remarks>
    /// 属性ビットで「辿らない」と決めると、中へ書かれたファイルが変更検知から見えなくなる。
    /// 判定は <see cref="FileSystemInfo.LinkTarget"/>（<see cref="MockProjectFiles.IsLink"/>）で行う。
    /// </remarks>
    [Fact(DisplayName = "リンクでない reparse フォルダの中へ書かれたファイルも確認に出す")]
    public async Task NonLinkReparseFolder_IsTraversed()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);
            var wwwroot = Path.Combine(folder, ProjectName, "wwwroot");
            Directory.CreateDirectory(wwwroot);
            File.WriteAllText(Path.Combine(wwwroot, "site.css"), "body {}");
            ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(wwwroot);

            var (_, confirm, build) = await RunAsync(
                folder,
                _ => File.WriteAllText(Path.Combine(wwwroot, "evil.targets"), "<Project/>")
            );

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().Equal($"{ProjectName}/wwwroot/evil.targets");
            build.BuildCallCount.Should().Be(1);
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>リンクでない reparse フォルダは、中身が変わっていなければ確認に出さない（誤検知しない）</summary>
    [Fact(DisplayName = "リンクでない reparse フォルダは変化が無ければ確認しない")]
    public async Task NonLinkReparseFolder_Unchanged_NoConfirmation()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);
            var wwwroot = Path.Combine(folder, ProjectName, "wwwroot");
            Directory.CreateDirectory(wwwroot);
            File.WriteAllText(Path.Combine(wwwroot, "appsettings.json"), "{}");
            ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(wwwroot);

            var (_, confirm, build) = await RunAsync(folder, _ => { });

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>
    /// obj がリンクでない reparse フォルダでも、中身ごと消してから確認なしでビルドする。
    /// </summary>
    /// <remarks>
    /// 属性ビットでリンク扱いにすると、非再帰の削除が「空でない」で失敗して obj が残る。
    /// </remarks>
    [Fact(DisplayName = "obj がリンクでない reparse フォルダでも中身ごと消してビルドする")]
    public async Task ObjAsNonLinkReparseFolder_IsDeletedWithContents()
    {
        var folder = NewTempFolder();
        var obj = Path.Combine(folder, ProjectName, "obj");

        try
        {
            Scaffold(folder);
            bool? objExistedAtBuild = null;

            var (_, confirm, build) = await RunAsync(
                folder,
                dir =>
                {
                    Directory.CreateDirectory(obj);
                    File.WriteAllText(FullPath(dir, ObjTargetsRelative), "<Project/>");
                    ReparsePointTestHelper.MakeReadableNonLinkReparseDirectory(obj);
                },
                onBuild: () => objExistedAtBuild = Directory.Exists(obj)
            );

            confirm.CallCount.Should().Be(0);
            build.BuildCallCount.Should().Be(1);
            objExistedAtBuild.Should().BeFalse();
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(folder);
        }
    }

    /// <summary>
    /// 中を列挙できない reparse フォルダは中へ入らず（ランは落ちない）、変化が無くても毎回確認一覧に出す。
    /// </summary>
    /// <remarks>
    /// 中身を確かめられない以上「変わっていない」とは言えないので、スキャフォールド時点から在って何も
    /// 変わっていなくても毎回一覧に出る（フェイルクローズとして受け入れた挙動）。中へ再帰しようとすると
    /// 列挙が <see cref="IOException"/> で止まり、ランごと落ちる。
    /// </remarks>
    [Fact(DisplayName = "中を列挙できない reparse フォルダは毎回確認に出し、ランは落ちない")]
    public async Task UnlistableReparseFolder_IsAlwaysListed()
    {
        var folder = NewTempFolder();

        try
        {
            Scaffold(folder);

            // 置き場所は出力フォルダ直下（プロジェクトフォルダの外）。成果物検証（MockProjectFiles）は
            // プロジェクトフォルダ配下だけを見るので、変更検知の挙動だけを切り出して確かめられる
            var cache = Path.Combine(folder, "cache");
            Directory.CreateDirectory(cache);
            ReparsePointTestHelper.MakeUnlistableReparseDirectory(cache);

            // 実行器は何も書かない（スキャフォールド時点から在るフォルダが変わらないまま）
            var (_, confirm, build) = await RunAsync(folder, _ => { });

            confirm.CallCount.Should().Be(1);
            confirm.LastPaths.Should().Equal("cache");
            build.BuildCallCount.Should().Be(1);
        }
        finally
        {
            ReparsePointTestHelper.DeleteTree(folder);
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
