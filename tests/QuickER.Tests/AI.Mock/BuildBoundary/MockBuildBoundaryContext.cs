using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using Microsoft.Win32.SafeHandles;
using QuickER.AI.Mock;
using QuickER.Tests.AI.Mock.Runner;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.AI.Mock.BuildBoundary;

/// <summary>最終ビルドの境界の表の入口（次元表の因子 Entrance の水準）</summary>
internal enum BoundaryEntrance
{
    /// <summary>
    /// 入口 A：変更検知（<c>MockProjectBuildBoundary.Snapshot</c>）。ランナー経由（記録 → 実行器 → obj / bin の削除 → 差分 → 確認 → ビルド）と、
    /// 記録と差分を直接呼ぶ行がある
    /// </summary>
    ChangeDetection,

    /// <summary>入口 B：最終ビルドの直前のビルド出力の削除（<c>MockProjectBuildBoundary.ClearBuildOutputs</c>。ランナー経由）</summary>
    ClearOutputs,

    /// <summary>入口 C：成果物の探索（<c>MockProjectFiles.Enumerate</c>・<c>HasAny</c>・<c>IsLink</c>）</summary>
    Enumerate,

    /// <summary>入口 D：スキャフォールドの上書きの走査（<c>MockProjectOverwriteScanner.Scan</c>）</summary>
    OverwriteScan,
}

/// <summary>行が置くファイル・フォルダの種別（次元表の因子 Kind の水準）</summary>
internal enum BoundaryEntryKind
{
    /// <summary>通常のファイル・フォルダ</summary>
    Plain,

    /// <summary>隠し属性のファイル</summary>
    Hidden,

    /// <summary>読み取り専用の属性のファイル</summary>
    ReadOnly,

    /// <summary>削除の共有を許さないハンドルで開いたままのファイル・ジャンクション（削除が必ず失敗する）</summary>
    Locked,

    /// <summary>外を指すファイルのシンボリックリンク（作成に特権が要る）</summary>
    FileSymlink,

    /// <summary>外を指すディレクトリのジャンクション</summary>
    Junction,

    /// <summary>中を読み書きできる、リンクでない reparse フォルダ（OneDrive の Files On-Demand と同じ形）</summary>
    NonLinkReparse,

    /// <summary>中を列挙できない reparse フォルダ</summary>
    UnlistableReparse,

    /// <summary>一覧の読み取りを拒否する ACE を付けたフォルダ</summary>
    AclDenied,

    /// <summary>起点・対象が無い（存在しないパス・空文字）</summary>
    Absent,
}

/// <summary>最終ビルドの前の確認の手段と答え</summary>
internal enum BoundaryConfirm
{
    /// <summary>確認を出したら承認する</summary>
    Approve,

    /// <summary>確認を出したら取り消す</summary>
    Decline,

    /// <summary>確認の手段を渡さない（<c>confirmBuild</c> が null）</summary>
    Unwired,
}

/// <summary>最終ビルドの境界の表の 1 行の入力（入口・種別・本体）</summary>
/// <param name="Entrance">行が流す入口</param>
/// <param name="Kind">行が置く種別</param>
/// <param name="Body">流して表明する本体</param>
internal sealed record MockBuildBoundaryScenario(
    BoundaryEntrance Entrance,
    BoundaryEntryKind Kind,
    Func<MockBuildBoundaryContext, Task> Body
);

/// <summary>ランナー経由の 1 ランの結果</summary>
/// <param name="Result">ランの結果</param>
/// <param name="ConfirmCalls">確認が呼ばれた回数（確認の手段を渡さないときは 0）</param>
/// <param name="Paths">最後の確認に渡った一覧（出力フォルダからの相対パス・区切りは <c>/</c>）</param>
/// <param name="BuildCalls">最終ビルドが呼ばれた回数</param>
internal sealed record MockBuildBoundaryRun(
    MockProjectAgentResult Result,
    int ConfirmCalls,
    IReadOnlyList<string> Paths,
    int BuildCalls
)
{
    /// <summary>出力フォルダへ書いた実行ログの本文</summary>
    public string Log => File.ReadAllText(Result.LogPath);
}

/// <summary>最終ビルドの境界の表の 1 行を流す文脈（出力フォルダ・外のフォルダ・ACL とハンドルの後始末・ランナー）</summary>
/// <remarks>
/// <para>
/// フォルダは行ごとに <see cref="TestTempFolder"/> で作る。破棄では、開いたままのハンドルを閉じ、付けた拒否の ACE を外し
/// （外さないとフォルダを消せない）、<see cref="ReparsePointTestHelper.DeleteTree"/> で reparse point・リンク・読み取り専用を外しながら
/// 消してから、<see cref="TestTempFolder.Dispose"/> を呼ぶ。行の本体が失敗して抜けても、この順で戻してから消す。
/// </para>
/// <para>
/// ランナー経由の行（<see cref="RunAsync"/>）には、全行に掛かる照合（境界越えを宣言しない実行器は確認しない・確認を出したら答えのとおりにビルドする・
/// 未検証で終えたらビルドしていない）を掛ける。
/// </para>
/// </remarks>
internal sealed class MockBuildBoundaryContext : IDisposable
{
    /// <summary>プロジェクト名（スキャフォールドのレイアウト規則に合わせてフォルダを作る）</summary>
    public const string ProjectName = "AcmeMock";

    /// <summary>出力フォルダからの相対パスで書いた、プロジェクトの obj 直下のターゲット</summary>
    public const string ObjTargetsRelative = $"{ProjectName}/obj/{ProjectName}.csproj.evil.targets";

    private readonly List<TestTempFolder> _folders = [];
    private readonly List<(string Folder, FileSystemAccessRule Rule)> _denied = [];
    private readonly List<SafeFileHandle> _handles = [];

    /// <summary>文脈を作る（出力フォルダを 1 つ作る）</summary>
    /// <param name="cancellationToken">テストの取り消し</param>
    public MockBuildBoundaryContext(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
        Folder = NewFolder();
    }

    /// <summary>テストの取り消し</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>出力フォルダ（行が使う主な一時フォルダ）</summary>
    public string Folder { get; }

    /// <summary>プロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）</summary>
    public string ProjectDirectory => Path.Combine(Folder, ProjectName);

    /// <summary>もう 1 つ一時フォルダを作る（リンク先・出力フォルダの外の置き場所）</summary>
    public string NewFolder()
    {
        var folder = new TestTempFolder();
        _folders.Add(folder);
        return folder.Path;
    }

    /// <summary>出力フォルダからの相対パス（<c>/</c> 区切り）を絶対パスへ</summary>
    /// <param name="relative">相対パス</param>
    public string FullPath(string relative) =>
        Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>スキャフォールド相当の初期状態（csproj・MainWindow.xaml・sln）を作る</summary>
    /// <remarks>ランナーは RunAsync の入口で記録するため、記録に載せたい既存物はここで作る。</remarks>
    public void Scaffold()
    {
        Directory.CreateDirectory(ProjectDirectory);
        File.WriteAllText(Path.Combine(ProjectDirectory, $"{ProjectName}.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(ProjectDirectory, "MainWindow.xaml"), "<Window/>");
        File.WriteAllText(Path.Combine(Folder, $"{ProjectName}.sln"), "solution");
    }

    /// <summary>ファイルを書く（フォルダも作る）</summary>
    /// <param name="path">絶対パス</param>
    /// <param name="content">内容</param>
    public static void Write(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>ファイルを書いて読み取り専用にする（フォルダも作る）</summary>
    /// <param name="path">絶対パス</param>
    public static void WriteReadOnly(string path)
    {
        Write(path, "<Project/>");
        File.SetAttributes(path, FileAttributes.ReadOnly);
    }

    /// <summary>ファイル・フォルダを、削除の共有を許さないハンドルで開いたままにする（破棄で閉じる）</summary>
    /// <param name="path">絶対パス</param>
    public void Lock(string path) =>
        _handles.Add(ReparsePointTestHelper.OpenWithoutDeleteSharing(path));

    /// <summary>ファイルを書いて、削除の共有を許さないハンドルで開いたままにする（削除が必ず失敗する）</summary>
    /// <param name="path">絶対パス</param>
    public void WriteAndLock(string path)
    {
        Write(path, "<Project/>");
        Lock(path);
    }

    /// <summary>フォルダへ、今のユーザーに対する拒否の ACE を付ける（破棄で外す）</summary>
    /// <param name="folder">フォルダ</param>
    /// <param name="rights">拒否する権利</param>
    /// <param name="inheritance">子への継承</param>
    public void Deny(
        string folder,
        FileSystemRights rights = FileSystemRights.ListDirectory,
        InheritanceFlags inheritance = InheritanceFlags.None
    )
    {
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            rights,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Deny
        );
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        _denied.Add((folder, rule));
    }

    /// <summary>
    /// ランナーで 1 ラン流す（初期状態は作成済みで、<paramref name="duringRun"/> が実行器の書き込みを模す）。
    /// 返す前に、全行に掛かる照合を掛ける
    /// </summary>
    /// <param name="duringRun">実行器の実行中の副作用（出力フォルダを受け取る）</param>
    /// <param name="escapes">実行器が最終ビルドの境界越えを宣言するか</param>
    /// <param name="confirm">確認の手段と答え</param>
    /// <param name="onBuild">最終ビルドの開始時点の観測</param>
    public async Task<MockBuildBoundaryRun> RunAsync(
        Action<string> duringRun,
        bool escapes = true,
        BoundaryConfirm confirm = BoundaryConfirm.Approve,
        Action? onBuild = null
    )
    {
        var agent = new FakeMockProjectAgent { OnRun = duringRun, EscapesSandbox = escapes };
        var build = new FakeBuildRunner { OnBuild = onBuild };
        var confirmCalls = 0;
        IReadOnlyList<string> paths = [];
        Func<IReadOnlyList<string>, bool>? confirmBuild =
            confirm == BoundaryConfirm.Unwired
                ? null
                : listed =>
                {
                    confirmCalls++;
                    paths = listed;
                    return confirm == BoundaryConfirm.Approve;
                };
        var runner = new MockProjectAgentRunner(
            agent,
            build,
            timeout: null,
            profile: null,
            buildTimeout: null,
            confirmBuild: confirmBuild
        );

        var result = await runner.RunAsync(
            Folder,
            ProjectName,
            additionalInstructions: null,
            model: string.Empty,
            modelProvider: string.Empty,
            onProgress: _ => { },
            CancellationToken
        );
        var run = new MockBuildBoundaryRun(result, confirmCalls, paths, build.BuildCallCount);

        // 全行に掛かる照合（src/QuickER.AI.Mock/CLAUDE.md「最終ビルド前の変更検知」・MockProjectAgentRunner.ConfirmBuildBoundary）
        run.ConfirmCalls.Should().BeLessThanOrEqualTo(1, "確認は 1 ランに高々 1 回");

        if (!escapes)
        {
            run.ConfirmCalls.Should().Be(0, "境界越えを宣言しない実行器では確認しない");
        }

        if (run.ConfirmCalls > 0)
        {
            run.BuildCalls.Should()
                .Be(
                    confirm == BoundaryConfirm.Approve ? 1 : 0,
                    "確認を出したら答えのとおりにビルドする"
                );
            run.Result.BuildDeclined.Should().Be(confirm != BoundaryConfirm.Approve);
        }

        if (run.Result.BuildDeclined)
        {
            run.BuildCalls.Should().Be(0, "未検証で終えたらビルドしていない");
        }

        return run;
    }

    /// <summary>ハンドルを閉じ、拒否の ACE を外し、一時フォルダを消す（どれかが失敗しても残りを続ける）</summary>
    public void Dispose()
    {
        foreach (var handle in _handles)
        {
            handle.Dispose();
        }

        for (var i = _denied.Count - 1; i >= 0; i--)
        {
            var (folder, rule) = _denied[i];

            if (!Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                var info = new DirectoryInfo(folder);
                var security = info.GetAccessControl();
                security.RemoveAccessRule(rule);
                info.SetAccessControl(security);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 後始末はベストエフォート（残りの ACE とフォルダの削除を続ける）
            }
        }

        foreach (var folder in _folders)
        {
            try
            {
                ReparsePointTestHelper.DeleteTree(folder.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 後始末はベストエフォート（次のフォルダの削除を続ける）
            }

            folder.Dispose();
        }
    }
}
