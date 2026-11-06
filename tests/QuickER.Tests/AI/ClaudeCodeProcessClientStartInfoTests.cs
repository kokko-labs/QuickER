using System.Diagnostics;
using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ClaudeCodeProcessClient"/> のシステムプロンプトのファイル渡し（実ファイル）と、解決の呼び出し回数を検証するテストクラス。
/// </summary>
/// <remarks>
/// シムのガード（<see cref="BatchShimProcessGuard"/>）への配線と claude 固有の文言は、OS との境界のエンジンの起動の表
/// （<c>Platform/OsBoundary/ProcessLaunchRows.Shim.cs</c> の行 <c>shim-claude-*</c>）へ移した。
/// ここに残すのは、保存先に差し込み点の無いシステムプロンプトのファイル（内容・BOM・削除）を実ファイルで見るものと、
/// 解決をキャッシュしない呼び出し回数の振る舞い。
/// </remarks>
public class ClaudeCodeProcessClientStartInfoTests
{
    /// <summary>
    /// 複数行のシステムプロンプトが <c>--append-system-prompt-file</c> のファイル渡しになり、
    /// 引数には改行が 1 つも載らないことを固定する。
    /// </summary>
    /// <remarks>
    /// 引数で渡していた頃は、npm 導入の <c>.cmd</c> シム経由で cmd が <c>%*</c> を展開する段階で
    /// 1 行目より後ろが黙って捨てられ、後続の <c>--model</c> / <c>--resume</c> ごと消えていた。
    /// ここが引数渡しへ戻ると、シム環境でだけ resume が効かなくなる形で静かに回帰する。
    /// </remarks>
    [Fact]
    public void AppendSystemPromptArguments_複数行プロンプトはファイル渡しになる()
    {
        const string prompt = "line1\r\nline2\nline3 <Routes @rendermode=\"InteractiveServer\" />";
        var startInfo = new ProcessStartInfo();

        var path = ClaudeCodeProcessClient.AppendSystemPromptArguments(startInfo, prompt);

        try
        {
            startInfo.ArgumentList.Should().Equal("--append-system-prompt-file", path);

            // パスは改行も cmd のメタ文字も含まない＝シム経由でも 1 トークンのまま届く
            path.Should().NotContainAny("\r", "\n", "%", "\"", "&", "|", "<", ">", "^");

            // 本文はそのまま（改行も含めて）ファイルへ渡る
            File.ReadAllText(path).Should().Be(prompt);

            // BOM なし UTF-8（BOM を本文の一部として読ませない）
            File.ReadAllBytes(path).Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        }
        finally
        {
            ClaudeCodeProcessClient.TryDeleteSystemPromptFile(path);
        }
    }

    /// <summary>非 ASCII のプロンプトも UTF-8 で往復する（ER 設計ルールは日本語を含み得る）</summary>
    [Fact]
    public void AppendSystemPromptArguments_非ASCIIのプロンプトも往復する()
    {
        const string prompt = "テーブル名はパスカルケース単数形\n列名も同様";
        var startInfo = new ProcessStartInfo();

        var path = ClaudeCodeProcessClient.AppendSystemPromptArguments(startInfo, prompt);

        try
        {
            File.ReadAllText(path, Encoding.UTF8).Should().Be(prompt);
        }
        finally
        {
            ClaudeCodeProcessClient.TryDeleteSystemPromptFile(path);
        }
    }

    /// <summary>ターン終了後の後片付けで一時ファイルが消えることを固定する</summary>
    [Fact]
    public void TryDeleteSystemPromptFile_一時ファイルを削除する()
    {
        var startInfo = new ProcessStartInfo();
        var path = ClaudeCodeProcessClient.AppendSystemPromptArguments(startInfo, "system prompt");

        File.Exists(path).Should().BeTrue();

        ClaudeCodeProcessClient.TryDeleteSystemPromptFile(path);

        File.Exists(path).Should().BeFalse();
    }

    /// <summary>削除の失敗・null はターンの結果へ影響させない（ベストエフォート）</summary>
    [Fact]
    public void TryDeleteSystemPromptFile_存在しないパスとnullは無視する()
    {
        var act = () =>
        {
            ClaudeCodeProcessClient.TryDeleteSystemPromptFile(null);
            ClaudeCodeProcessClient.TryDeleteSystemPromptFile(
                Path.Combine(Path.GetTempPath(), $"quicker-missing-{Guid.NewGuid():N}.txt")
            );
        };

        act.Should().NotThrow();
    }

    /// <summary>
    /// ファイル渡しにしたシステムプロンプトは、<c>.cmd</c> シムのガードを通っても切り捨てられない。
    /// </summary>
    [Fact]
    public void ApplyBatchShimGuard_ファイル渡しのシステムプロンプトはシム経由でも通る()
    {
        const string prompt = "line1\r\nline2";
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add("sonnet");

        var path = ClaudeCodeProcessClient.AppendSystemPromptArguments(startInfo, prompt);

        try
        {
            ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);

            startInfo.FileName.Should().Be(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            startInfo.Arguments.Should().Contain($"\"{path}\"").And.NotContain("\n");
        }
        finally
        {
            ClaudeCodeProcessClient.TryDeleteSystemPromptFile(path);
        }
    }

    /// <summary>
    /// claude の解決結果をキャッシュせず、呼ぶたびに解決し直すことを検証する
    /// （アプリの起動後に入れた claude を「再確認」で検出できるように）。
    /// </summary>
    [Fact(DisplayName = "claude の検出は呼ぶたびに解決し直す")]
    public void IsAvailable_ResolvesOnEveryCall()
    {
        string? installed = null;
        var resolveCount = 0;
        var client = new ClaudeCodeProcessClient(
            () =>
            {
                resolveCount++;
                return installed;
            },
            ClaudeCodeProcessClient.DefaultProbeTimeout
        );

        client.IsAvailable().Should().BeFalse();

        installed = @"C:\tools\claude.exe";

        client.IsAvailable().Should().BeTrue("後から入れた claude も検出できる");
        resolveCount.Should().Be(2);
    }
}
