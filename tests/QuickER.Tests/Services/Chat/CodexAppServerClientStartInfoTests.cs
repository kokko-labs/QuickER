using FluentAssertions;
using QuickER.AI;
using AiStrings = QuickER.AI.Resources.Strings;

namespace QuickER.Tests.Services.Chat;

/// <summary>
/// <see cref="CodexAppServerClient"/> のプロセス起動情報決定（.cmd/.bat の cmd.exe ラップと
/// コマンド挿入につながる入力の拒否）を検証するテストクラス
/// </summary>
public class CodexAppServerClientStartInfoTests
{
    private const string Arguments = "app-server --listen stdio://";

    [Fact]
    public void ResolveStartInfo_exeは直接起動する()
    {
        var (fileName, arguments) = CodexAppServerClient.ResolveStartInfo(
            @"C:\tools\codex.exe",
            Arguments
        );

        fileName.Should().Be(@"C:\tools\codex.exe");
        arguments.Should().Be(Arguments);
    }

    [Fact]
    public void ResolveStartInfo_cmdシムは決定的な引用形式でラップする()
    {
        var (fileName, arguments) = CodexAppServerClient.ResolveStartInfo(
            @"C:\Program Files\nodejs\codex.cmd",
            Arguments
        );

        fileName.Should().Be("cmd.exe");
        arguments.Should().Be($"/d /s /c \"\"C:\\Program Files\\nodejs\\codex.cmd\" {Arguments}\"");
    }

    [Fact]
    public void ResolveStartInfo_引用符を含むパスは起動前に拒否する()
    {
        const string path = @"C:\tools\evil"" & calc & ""x.cmd";
        var act = () => CodexAppServerClient.ResolveStartInfo(path, Arguments);

        // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(string.Format(AiStrings.Codex_PathHasQuote, path));
    }

    [Theory]
    [InlineData("app-server & calc")]
    [InlineData("app-server | calc")]
    [InlineData("app-server > out.txt")]
    [InlineData("app-server < in.txt")]
    [InlineData("app-server ^& calc")]
    [InlineData("app-server %TEMP%")]
    [InlineData("app-server \"quoted\"")]
    public void ResolveStartInfo_cmdメタ文字を含む引数は起動前に拒否する(string arguments)
    {
        var act = () => CodexAppServerClient.ResolveStartInfo(@"C:\tools\codex.cmd", arguments);

        // 検出文字の再現は実装ロジックの重複になるため、resx の書式引数より前の固定部分（プレフィックス）で照合する
        var prefix = AiStrings.Codex_ArgHasCmdMeta.Split("{0}")[0];
        act.Should().Throw<InvalidOperationException>().WithMessage(prefix + "*");
    }

    [Fact]
    public void ResolveStartInfo_メタ文字を含むパスでもexeなら素通しする()
    {
        // .exe は cmd を経由しないためガード対象外（ProcessStartInfo が安全に扱う）
        var (fileName, _) = CodexAppServerClient.ResolveStartInfo(
            @"C:\tools\a & b\codex.exe",
            Arguments
        );

        fileName.Should().Be(@"C:\tools\a & b\codex.exe");
    }
}
