using System.IO;
using QuickER.AI.Mock;
using QuickER.Tests.AI.Mock.Agents;
using QuickER.Tests.TestSupport;
using MockStrings = QuickER.AI.Mock.Resources.Strings;

namespace QuickER.Tests.AI.Mock.Runner;

/// <summary>ランの結果の分類（<c>MockProjectAgentRunner</c> の結果のメッセージを決める順と同じ並び。ランの外の行は <see cref="NotRun"/>）</summary>
internal enum MockRunResult
{
    /// <summary>成功</summary>
    Success,

    /// <summary>実行器が失敗を申告した（想定外の例外を含む）</summary>
    ClientFailed,

    /// <summary>実行器が未ログインを申告した</summary>
    NotLoggedIn,

    /// <summary>成果物（csproj と UI のファイル）が無い</summary>
    ArtifactsMissing,

    /// <summary>画面の数が足りず、ターゲットの宣言で失敗にした</summary>
    ScreensMissing,

    /// <summary>最終ビルドの前の確認を取り消した</summary>
    BuildDeclined,

    /// <summary>最終ビルドが失敗した（起動できない例外を含む）</summary>
    BuildFailed,

    /// <summary>実行器の実行が全体のタイムアウトで打ち切られた</summary>
    TimedOut,

    /// <summary>最終ビルドが専用のタイムアウトで打ち切られた</summary>
    BuildTimedOut,

    /// <summary>利用者が中断した（外部のトークン・InterruptAsync）</summary>
    Canceled,

    /// <summary>結果へ畳めない例外でランが抜けた</summary>
    Thrown,

    /// <summary>ランを走らせない（委譲だけを見る行）</summary>
    NotRun,
}

/// <summary>ランナーの流れの表の 1 行の入力（ターゲット・期待する分類・本体）</summary>
/// <param name="Target">生成のターゲット（ランナーへ渡すプロファイル）</param>
/// <param name="Result">期待する結果の分類（本体が返したランの結果を分類して照合する）</param>
/// <param name="Body">流して表明する本体（ランの結果を返す。走らせない・例外で抜ける行は null）</param>
internal sealed record MockRunnerScenario(
    MockAgentTarget Target,
    MockRunResult Result,
    Func<MockRunnerContext, Task<MockProjectAgentResult?>> Body
);

/// <summary>ランナーの流れの表の 1 行を流す文脈（作業フォルダ・偽の実行器・偽のビルド検証器・進捗）</summary>
/// <remarks>作業フォルダは <see cref="TestTempFolder"/> に行ごとに作り、破棄で中身ごと消す</remarks>
internal sealed class MockRunnerContext : IDisposable
{
    /// <summary>プロジェクト名（スキャフォールドのレイアウト規則に合わせてフォルダを作る）</summary>
    public const string ProjectName = "AcmeMock";

    private readonly TestTempFolder _folder = new();
    private readonly List<string> _progress = [];
    private readonly object _gate = new();

    /// <summary>文脈を作る</summary>
    /// <param name="scenario">行の入力</param>
    /// <param name="cancellationToken">テストの取り消し</param>
    public MockRunnerContext(MockRunnerScenario scenario, CancellationToken cancellationToken)
    {
        Scenario = scenario;
        CancellationToken = cancellationToken;
    }

    /// <summary>行の入力</summary>
    public MockRunnerScenario Scenario { get; }

    /// <summary>テストの取り消し</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>出力フォルダ</summary>
    public string Folder => _folder.Path;

    /// <summary>プロジェクトフォルダ（<c>{出力フォルダ}/{プロジェクト名}/</c>）</summary>
    public string ProjectDirectory => Path.Combine(Folder, ProjectName);

    /// <summary>偽の実行器</summary>
    public FakeMockProjectAgent Agent { get; } = new();

    /// <summary>偽のビルド検証器</summary>
    public FakeBuildRunner Build { get; } = new();

    /// <summary>進捗へ流れた文章の連結</summary>
    public string ProgressText
    {
        get
        {
            lock (_gate)
            {
                return string.Concat(_progress);
            }
        }
    }

    /// <summary>行のターゲットのプロファイルでランナーを作る</summary>
    /// <param name="timeout">全体のタイムアウト</param>
    /// <param name="buildTimeout">最終ビルドのタイムアウト</param>
    /// <param name="confirmBuild">最終ビルドの前の確認</param>
    public MockProjectAgentRunner CreateRunner(
        TimeSpan? timeout = null,
        TimeSpan? buildTimeout = null,
        Func<IReadOnlyList<string>, bool>? confirmBuild = null
    ) => new(Agent, Build, timeout, Scenario.Target.Profile, buildTimeout, confirmBuild);

    /// <summary>ランを走らせる（追加の指示なし）</summary>
    /// <param name="runner">ランナー</param>
    /// <param name="model">モデル</param>
    /// <param name="cancellationToken">外部のトークン（省略時はテストの取り消し）</param>
    public Task<MockProjectAgentResult> RunAsync(
        MockProjectAgentRunner runner,
        string model = "",
        CancellationToken? cancellationToken = null
    ) =>
        runner.RunAsync(
            Folder,
            ProjectName,
            additionalInstructions: null,
            model,
            OnProgress,
            cancellationToken ?? CancellationToken
        );

    /// <summary>行のターゲットの成果物（プロジェクトフォルダ配下の csproj と、ターゲットの UI のファイル 1 つ）を書く副作用</summary>
    /// <remarks>ターゲットに依らない流れの行が使う（WPF は App.xaml・Blazor は Home.razor。どちらも成果物の検索パターンに当たる）</remarks>
    public Action<string> WriteTargetArtifacts() =>
        dir => WriteProjectFiles(dir, (Scenario.Target.UiFile, Scenario.Target.UiContent));

    /// <summary>WPF の成果物（プロジェクトフォルダ配下の csproj と MainWindow.xaml）を書く副作用</summary>
    public static Action<string> WriteArtifacts() =>
        dir => WriteProjectFiles(dir, ("MainWindow.xaml", "<Window/>"));

    /// <summary>プロジェクトフォルダ配下に csproj と UI のファイルを書く</summary>
    /// <param name="dir">出力フォルダ</param>
    /// <param name="uiFiles">UI のファイル（プロジェクトフォルダからの相対パスと内容）</param>
    public static void WriteProjectFiles(string dir, params (string Path, string Content)[] uiFiles)
    {
        var project = Path.Combine(dir, ProjectName);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, $"{ProjectName}.csproj"), "<Project/>");

        foreach (var (path, content) in uiFiles)
        {
            var full = Path.Combine(project, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
    }

    /// <summary>
    /// プロジェクトフォルダに、画面を <paramref name="screenCount"/> 個持つ設計（design/mock/mock.json）を置き、
    /// csproj と <paramref name="uiFiles"/> の UI ファイルを書く副作用
    /// </summary>
    /// <param name="screenCount">設計の画面の数</param>
    /// <param name="uiFiles">UI のファイル</param>
    public static Action<string> WriteProjectWithScreens(
        int screenCount,
        params (string Path, string Content)[] uiFiles
    ) =>
        dir =>
        {
            var design = Path.Combine(dir, ProjectName, "design", "mock");
            Directory.CreateDirectory(design);
            var store = MockFolderStore.CreateNew(design, ProjectName, "# schema");

            for (var i = 0; i < screenCount; i++)
            {
                store.SaveScreen(
                    $"Screen{i}.html",
                    $"Screen {i}",
                    "role",
                    "<!DOCTYPE html><html><body></body></html>",
                    Array.Empty<MockTransition>(),
                    "init"
                );
            }

            WriteProjectFiles(dir, uiFiles);
        };

    /// <summary>ランの結果を分類する（結果のメッセージを決める順と同じ優先で、結果の印から読む）</summary>
    /// <param name="result">ランの結果</param>
    public static MockRunResult Classify(MockProjectAgentResult result)
    {
        if (result.Success)
        {
            return MockRunResult.Success;
        }

        if (result.TimedOut)
        {
            return result.Message == MockStrings.Mock_Result_BuildTimedOut
                ? MockRunResult.BuildTimedOut
                : MockRunResult.TimedOut;
        }

        if (result.Canceled)
        {
            return MockRunResult.Canceled;
        }

        if (result.Message == MockStrings.Mock_Result_NotLoggedIn)
        {
            return MockRunResult.NotLoggedIn;
        }

        if (!result.ClientSucceeded)
        {
            return MockRunResult.ClientFailed;
        }

        if (!result.ArtifactsPresent)
        {
            return MockRunResult.ArtifactsMissing;
        }

        if (result.ScreensMissing)
        {
            return MockRunResult.ScreensMissing;
        }

        return result.BuildDeclined ? MockRunResult.BuildDeclined : MockRunResult.BuildFailed;
    }

    private void OnProgress(string text)
    {
        lock (_gate)
        {
            _progress.Add(text);
        }
    }

    /// <summary>出力フォルダを中身ごと消す</summary>
    public void Dispose() => _folder.Dispose();
}
