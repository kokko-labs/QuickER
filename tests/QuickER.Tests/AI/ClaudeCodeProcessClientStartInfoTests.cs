using System.Diagnostics;
using System.IO;
using System.Text;
using AwesomeAssertions;
using QuickER.AI;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="ClaudeCodeProcessClient"/> が <c>.cmd</c> / <c>.bat</c> シムのガード
/// （<see cref="BatchShimProcessGuard"/>）へ claude 固有の文言で配線されていることを検証するテストクラス。
/// </summary>
/// <remarks>
/// ガードのポリシーそのものは <see cref="BatchShimProcessGuardTests"/> が固定する。
/// ここで守るのは「claude の起動経路がガードを通っていること」と「止まったのが claude だと
/// メッセージから分かること」の 2 点。
/// </remarks>
public class ClaudeCodeProcessClientStartInfoTests
{
    [Fact]
    public void ApplyBatchShimGuard_exeは書き換えない()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\tools\claude.exe" };
        startInfo.ArgumentList.Add("-p");

        ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);

        startInfo.FileName.Should().Be(@"C:\tools\claude.exe");
        startInfo.ArgumentList.Should().Equal("-p");
    }

    [Fact]
    public void ApplyBatchShimGuard_cmdシムはCmdExe経由へ包み直す()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("-p");

        ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);

        startInfo.FileName.Should().Be("cmd.exe");
        startInfo.ArgumentList.Should().BeEmpty();
        startInfo.Arguments.Should().Contain(@"""C:\npm\claude.cmd""");
    }

    [Fact]
    public void ApplyBatchShimGuard_引用符を含むパスはClaudeの文言で拒否する()
    {
        const string path = @"C:\tools\evil"" & calc & ""x.cmd";
        var startInfo = new ProcessStartInfo { FileName = path };

        var act = () => ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);

        // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.ClaudeCode_PathHasQuote, path));
    }

    [Fact]
    public void ApplyBatchShimGuard_環境変数展開を含む引数はClaudeの文言で拒否する()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("%EVIL%");

        var act = () => ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.ClaudeCode_ArgHasEnvExpansion, "%EVIL%"));
    }

    [Fact]
    public void ApplyBatchShimGuard_改行を含む引数はClaudeの文言で拒否する()
    {
        const string argument = "line1\r\nline2";
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add(argument);

        var act = () => ClaudeCodeProcessClient.ApplyBatchShimGuard(startInfo);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.ClaudeCode_ArgHasNewline, argument));
    }

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

            startInfo.FileName.Should().Be("cmd.exe");
            startInfo.Arguments.Should().Contain($"\"{path}\"").And.NotContain("\n");
        }
        finally
        {
            ClaudeCodeProcessClient.TryDeleteSystemPromptFile(path);
        }
    }
}
