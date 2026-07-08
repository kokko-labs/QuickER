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
        string workingDirectory,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // 警告も検出したいので警告をエラー扱いにはせず、通常のビルドで終了コードを見る。
        // -nologo と -clp:NoSummary は付けず、ログはそのまま保全する（診断性優先）。
        var startInfo = CreateStartInfo(workingDirectory);
        startInfo.ArgumentList.Add("build");

        return await RunAsync(startInfo, cancellationToken).ConfigureAwait(false);
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
