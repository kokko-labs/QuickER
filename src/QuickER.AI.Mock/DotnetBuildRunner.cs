using System.Diagnostics;
using System.IO;
using System.Text;
using QuickER.AI.Mock.Resources;

namespace QuickER.AI.Mock;

/// <summary>
/// <c>dotnet</c> CLI を起動して <c>dotnet build</c> を実行する本番の <see cref="IBuildRunner"/>。
/// </summary>
/// <remarks>
/// 出力（stdout/stderr）を BOM なし UTF-8 で結合して返し、モック生成の最終ビルド検証・ログ保全に使う。
/// 生成コードの Roslyn 検証とは別で、実プロジェクトの <c>dotnet build</c> をそのまま回す。
/// </remarks>
public sealed class DotnetBuildRunner : IBuildRunner
{
    /// <inheritdoc />
    public async Task<BuildRunResult> BuildAsync(
        string solutionFilePath,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionFilePath);

        return await RunAsync(CreateBuildStartInfo(solutionFilePath), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 最終ビルド（<c>dotnet build {ソリューション}</c>）の起動情報を組み立てる。
    /// </summary>
    /// <remarks>
    /// 引数の並びはガードテスト（<c>DotnetBuildRunnerArgumentsTests</c>）が固定する。
    /// 警告も検出したいので警告をエラー扱いにはせず、通常のビルドで終了コードを見る。
    /// <c>-nologo</c> と <c>-clp:NoSummary</c> は付けず、ログはそのまま保全する（診断性優先）。
    /// </remarks>
    internal static ProcessStartInfo CreateBuildStartInfo(string solutionFilePath)
    {
        // cwd はソリューションの置き場（＝出力フォルダ）にする。対象はパスで明示するため、
        // cwd に何があってもビルド対象は自分のソリューション 1 つに決まる。
        var workingDirectory =
            Path.GetDirectoryName(Path.GetFullPath(solutionFilePath)) ?? Path.GetTempPath();
        var startInfo = CreateStartInfo(workingDirectory);
        startInfo.ArgumentList.Add("build");
        // ビルド対象のソリューションを明示する（フォルダ指定だと、出力フォルダに別のソリューション・
        // プロジェクトがあるときに MSB1011 で落ちる）。
        startInfo.ArgumentList.Add(solutionFilePath);
        AddSelfContainedBuildProperties(startInfo);
        AddIsolationArguments(startInfo);

        return startInfo;
    }

    /// <summary>
    /// 生成したモックプロジェクトを「置かれた場所から独立した成果物」としてビルドするための
    /// MSBuild プロパティを付与する。
    /// </summary>
    /// <remarks>
    /// MSBuild は既定でプロジェクト・ソリューションの祖先フォルダにある
    /// <c>Directory.Build.props</c> / <c>Directory.Build.targets</c> /
    /// <c>Directory.Solution.props</c> / <c>Directory.Solution.targets</c> /
    /// <c>Directory.Packages.props</c> を自動 import する。モックプロジェクトは README が
    /// 独立したソリューションとして説明する自己完結な成果物なので、出力先がたまたま
    /// 別リポジトリの配下にあってもそのビルドカスタマイズを継承しないようにする
    /// （中央パッケージ管理の強制やアナライザ設定の混入で、成果物単体では再現しない
    /// ビルド結果になるのを防ぐ）。
    /// <para>
    /// 5 つとも実測でプロジェクト単体ビルド・ソリューションビルドの双方に効くことを確認済み。
    /// バージョン確認（<c>dotnet --version</c>）にはプロジェクト評価が無いため付けない。
    /// </para>
    /// <para>
    /// 祖先の <c>global.json</c> / <c>NuGet.Config</c> は無効化しない（SDK のピンと
    /// 社内フィードの喪失という副作用のほうが大きいため）。この非対称は docs/ai-chat.md の注意節が正本。
    /// </para>
    /// </remarks>
    private static void AddSelfContainedBuildProperties(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add("-p:ImportDirectoryBuildProps=false");
        startInfo.ArgumentList.Add("-p:ImportDirectoryBuildTargets=false");
        startInfo.ArgumentList.Add("-p:ImportDirectorySolutionProps=false");
        startInfo.ArgumentList.Add("-p:ImportDirectorySolutionTargets=false");
        startInfo.ArgumentList.Add("-p:ImportDirectoryPackagesProps=false");
    }

    /// <summary>
    /// AI が書いたコードを「ビルド検証の瞬間に実行させない」ための隔離引数を付与する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>-noAutoResponse</c>: MSBuild は既定で <c>MSBuild.rsp</c> / <c>Directory.Build.rsp</c> を
    /// 自動で読み込み、そこに書かれた引数を実際のコマンドラインの前へ差し込む。実測（SDK 10.0.401）では、
    /// 出力フォルダに置いた <c>Directory.Build.rsp</c> の <c>-logger:&lt;DLL&gt;</c> が採用され、
    /// 指定したアセンブリをビルドプロセスへ読み込ませられることを確認した（MSBuild 自身が
    /// 「この switch は Directory.Build.rsp 由来」と報告する）。<c>-noAutoResponse</c> を付けると
    /// rsp は 1 つも読まれない。
    /// <para>
    /// なお上の 5 プロパティ自体は rsp から打ち消せない（同じ <c>-p</c> は後勝ちで、こちらの指定が後ろに来る＝
    /// 実測で確認）。塞いでいるのは <c>-logger</c> のような**加算される** switch と、こちらが固定していない
    /// プロパティの経路。
    /// </para>
    /// </para>
    /// <para>
    /// <c>-nodeReuse:false</c> / <c>-p:UseSharedCompilation=false</c>: MSBuild ノードと Roslyn の
    /// コンパイラサーバーはビルド後も常駐して次のビルドへ再利用される。AI が書いたタスク DLL・
    /// アナライザをその常駐プロセスに残さないため、いずれもプロセスを使い捨てにする。
    /// 実測のコストは 1 回のビルドあたり約 1.7 秒 → 約 3.3 秒（キャッシュ温・<c>obj</c>/<c>bin</c> 削除後の
    /// 3 回平均）で、生成 1 回につき 1 度しか走らないため許容する。
    /// </para>
    /// </remarks>
    private static void AddIsolationArguments(ProcessStartInfo startInfo)
    {
        startInfo.ArgumentList.Add("-noAutoResponse");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
    }

    /// <inheritdoc />
    public async Task<bool> IsDotnetAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = CreateStartInfo(Path.GetTempPath());
            startInfo.ArgumentList.Add("--version");

            var result = await RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
            return result.Success;
        }
        catch (Exception)
        {
            // dotnet が PATH に無い等で起動自体に失敗した場合は「利用不可」とみなす
            return false;
        }
    }

    /// <summary>dotnet 起動用の <see cref="ProcessStartInfo"/> を BOM なし UTF-8 出力で生成する</summary>
    /// <remarks>
    /// <c>dotnet</c> は名前のまま渡し、codex / claude / copilot のように PATH から自前で解決したフルパスにはしない。
    /// 名前だけの起動では <see cref="Process.Start()"/> が <c>.exe</c> を補って探すため <c>.cmd</c> / <c>.bat</c> を
    /// 拾わない（実測で確認）のに対し、共有の PATH 走査（<c>PathExecutableResolver</c>）は <c>.cmd</c> も候補にする＝
    /// PATH の前方に <c>dotnet.cmd</c> があるとバッチ経由の起動へ化け、引数ガードの無い経路が開く。
    /// SDK の解決は <c>dotnet.exe</c>（muxer）自身の置き場所から行われるので、フルパス化で得るものも無い。
    /// </remarks>
    private static ProcessStartInfo CreateStartInfo(string workingDirectory)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        return new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = utf8NoBom,
            StandardErrorEncoding = utf8NoBom,
        };
    }

    /// <summary>プロセスを実行し、結合出力と成否（終了コード 0）を返す</summary>
    private static async Task<BuildRunResult> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            return new BuildRunResult(false, Strings.Mock_DotnetStartFailed);
        }

        var output = new StringBuilder();

        // stdout / stderr を並行に読み切ってから終了を待つ（バッファ詰まりによるデッドロック回避）
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            output.Append(stdout);

            if (!string.IsNullOrEmpty(stderr))
            {
                output.Append(stderr);
            }

            return new BuildRunResult(process.ExitCode == 0, output.ToString());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    /// <summary>プロセスを安全に終了する</summary>
    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // 既に終了しているなどの競合は無視する
        }
    }
}
