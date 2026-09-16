using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using QuickER.AI;

namespace QuickER.Tests.AI;

/// <summary>
/// <see cref="BatchShimProcessGuard"/>（.cmd / .bat シム起動のコマンド挿入ガード）を検証するテストクラス。
/// </summary>
/// <remarks>
/// 引数が 1 本の文字列で来る入口（<see cref="BatchShimProcessGuard.WrapCommandLine"/>＝メタ文字は拒否）と、
/// ArgumentList で来る入口（<see cref="BatchShimProcessGuard.Apply"/>＝各トークンを引用し、引用の内側でも
/// 効く <c>%</c> を拒否）の 2 系統を、ポリシーごとに固定する。改行はどちらの入口でも拒否する
/// （引用では防げず cmd が <c>%*</c> 展開で行ごと切るため）。
/// </remarks>
public class BatchShimProcessGuardTests
{
    private const string PathQuoteFormat = "path has quote: {0}";
    private const string MetaCharFormat = "arg has meta '{0}': {1}";
    private const string EnvExpansionFormat = "arg has percent: {0}";
    private const string NewlineFormat = "arg has newline: {0}";

    /// <summary>包み直しの起動先として期待する、システムフォルダ直下の cmd.exe のフルパス</summary>
    /// <remarks>製品側のプロパティを参照せず独立に組み立てる（プロパティ自体の変異を検出するため）。</remarks>
    private static string SystemCmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    // ---- CmdExePath ----

    [Fact]
    public void CmdExePath_システムフォルダ直下のcmdをフルパスで指す()
    {
        // 名前だけの "cmd.exe" を渡さない＝起動する実体を探索規則に委ねず固定する（防御の二重化）
        BatchShimProcessGuard.CmdExePath.Should().Be(SystemCmdExe);
        Path.IsPathFullyQualified(BatchShimProcessGuard.CmdExePath).Should().BeTrue();
        File.Exists(BatchShimProcessGuard.CmdExePath).Should().BeTrue();
    }

    // ---- IsBatchShim ----

    [Theory]
    [InlineData(@"C:\tools\codex.cmd", true)]
    [InlineData(@"C:\tools\codex.BAT", true)]
    [InlineData(@"C:\tools\codex.exe", false)]
    [InlineData(@"/usr/local/bin/codex", false)]
    [InlineData(null, false)]
    public void IsBatchShim_拡張子でシムを判定する(string? path, bool expected)
    {
        BatchShimProcessGuard.IsBatchShim(path).Should().Be(expected);
    }

    // ---- WrapCommandLine（引数が 1 本の文字列） ----

    [Fact]
    public void WrapCommandLine_シムでなければ素通しする()
    {
        var (fileName, arguments) = BatchShimProcessGuard.WrapCommandLine(
            @"C:\tools\codex.exe",
            "app-server",
            PathQuoteFormat,
            MetaCharFormat,
            NewlineFormat
        );

        fileName.Should().Be(@"C:\tools\codex.exe");
        arguments.Should().Be("app-server");
    }

    [Fact]
    public void WrapCommandLine_シムは決定的な引用形式でラップする()
    {
        var (fileName, arguments) = BatchShimProcessGuard.WrapCommandLine(
            @"C:\Program Files\nodejs\codex.cmd",
            "app-server --listen stdio://",
            PathQuoteFormat,
            MetaCharFormat,
            NewlineFormat
        );

        fileName.Should().Be(SystemCmdExe);
        arguments
            .Should()
            .Be(
                "/d /s /c \"\"C:\\Program Files\\nodejs\\codex.cmd\" app-server --listen stdio://\""
            );
    }

    [Theory]
    [InlineData("app-server & calc")]
    [InlineData("app-server | calc")]
    [InlineData("app-server > out.txt")]
    [InlineData("app-server %TEMP%")]
    [InlineData("app-server \"quoted\"")]
    public void WrapCommandLine_メタ文字を含む引数は拒否する(string arguments)
    {
        var act = () =>
            BatchShimProcessGuard.WrapCommandLine(
                @"C:\tools\codex.cmd",
                arguments,
                PathQuoteFormat,
                MetaCharFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("arg has meta *");
    }

    [Fact]
    public void WrapCommandLine_引用符を含むパスは拒否する()
    {
        const string path = @"C:\tools\evil"" & calc & ""x.cmd";

        var act = () =>
            BatchShimProcessGuard.WrapCommandLine(
                path,
                "app-server",
                PathQuoteFormat,
                MetaCharFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage($"path has quote: {path}");
    }

    /// <summary>
    /// 改行を含む引数はこちらの入口でも拒否する（メタ文字より先に見るため専用の文言が出る）。
    /// </summary>
    [Fact]
    public void WrapCommandLine_改行を含む引数は拒否する()
    {
        var act = () =>
            BatchShimProcessGuard.WrapCommandLine(
                @"C:\tools\codex.cmd",
                "app-server\r\n--listen stdio://",
                PathQuoteFormat,
                MetaCharFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("arg has newline: *");
    }

    [Fact]
    public void WrapCommandLine_改行を含むパスは拒否する()
    {
        var act = () =>
            BatchShimProcessGuard.WrapCommandLine(
                "C:\\tools\\co\r\ndex.cmd",
                "app-server",
                PathQuoteFormat,
                MetaCharFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("arg has newline: *");
    }

    // ---- Apply（ArgumentList） ----

    [Fact]
    public void Apply_シムでなければ何もしない()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\tools\claude.exe" };
        startInfo.ArgumentList.Add("-p");

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        startInfo.FileName.Should().Be(@"C:\tools\claude.exe");
        startInfo.ArgumentList.Should().Equal("-p");
        startInfo.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Apply_シムは各トークンを引用してCmd経由へ包み直す()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add("sonnet");

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        startInfo.FileName.Should().Be(SystemCmdExe);

        // ArgumentList と Arguments の併用は Process.Start が拒否するため、必ず片方だけになっている
        startInfo.ArgumentList.Should().BeEmpty();
        startInfo
            .Arguments.Should()
            .Be("/d /s /v:off /c \"\"C:\\npm\\claude.cmd\" \"-p\" \"--model\" \"sonnet\"\"");
    }

    /// <summary>
    /// cmd のメタ文字（引用の内側では素の文字になるもの）は拒否せず、引用して通すことを固定する。
    /// ここを拒否側に倒すと、`&lt;Routes /&gt;` のような記述を含むシステムプロンプトを引数で渡す
    /// 既存の用途（モックプロジェクト生成）が .cmd 導入の環境で丸ごと動かなくなる。
    /// </summary>
    [Theory]
    [InlineData("a & b")]
    [InlineData("a | b")]
    [InlineData("<Routes />")]
    [InlineData("a ^ b")]
    public void Apply_引用の内側で無害になるメタ文字は拒否せず通す(string argument)
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add(argument);

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        startInfo.Arguments.Should().Contain($"\"{argument}\"");
    }

    /// <summary>
    /// 改行を含む引数は拒否する（引用しても防げないため、黙った切り捨てを明示エラーへ倒す）。
    /// </summary>
    /// <remarks>
    /// cmd は npm cmd-shim の <c>%*</c> を展開する段階で最初の改行から後ろを捨てる。捨てられるのは
    /// その引数の残りだけでなく後続の引数もすべてで、実機のプローブで「複数行のシステムプロンプトが
    /// 1 行目で切れ、後ろに積んだ <c>--model</c> / <c>--resume</c> ごと消える」ことを確認している。
    /// 複数行の内容はファイル経由で渡すこと（claude は <c>--append-system-prompt-file</c>）。
    /// </remarks>
    [Theory]
    [InlineData("line1\r\nline2")]
    [InlineData("line1\nline2")]
    [InlineData("line1\rline2")]
    public void Apply_改行を含む引数は拒否する(string argument)
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add(argument);

        var act = () =>
            BatchShimProcessGuard.Apply(
                startInfo,
                PathQuoteFormat,
                EnvExpansionFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("arg has newline: *");
    }

    /// <summary>パス側の改行も拒否する（正規のパスには現れず、あれば切り捨てを招くだけ）</summary>
    [Fact]
    public void Apply_改行を含むパスは拒否する()
    {
        var startInfo = new ProcessStartInfo { FileName = "C:\\npm\\cl\r\naude.cmd" };

        var act = () =>
            BatchShimProcessGuard.Apply(
                startInfo,
                PathQuoteFormat,
                EnvExpansionFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("arg has newline: *");
    }

    /// <summary>引数中の引用符は "" へ倍加され、引用の外へ抜けないことを検証する</summary>
    [Fact]
    public void Apply_引数の引用符は倍加して閉じ込める()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("say \"hi\"");

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        startInfo
            .Arguments.Should()
            .Be("/d /s /v:off /c \"\"C:\\npm\\claude.cmd\" \"say \"\"hi\"\"\"\"");
    }

    /// <summary>引用符の直前の連続バックスラッシュは C ランタイム規約に合わせて倍にする</summary>
    [Fact]
    public void Apply_引用符直前のバックスラッシュを倍にする()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add(@"a\""b");

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        // a \ \ "" b → 引用符の前の \ が 1 本から 2 本へ、引用符自身は "" へ
        startInfo.Arguments.Should().Contain(@"""a\\""""b""");
    }

    /// <summary>閉じ引用符の直前の連続バックスラッシュも倍にする（そのままだと閉じ引用符を潰す）</summary>
    [Fact]
    public void Apply_末尾のバックスラッシュを倍にする()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add(@"C:\dir\");

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        startInfo.Arguments.Should().Contain(@"""C:\dir\\""");
    }

    /// <summary>空の引数も 1 トークンとして残る（引用しないと消える）</summary>
    [Fact]
    public void Apply_空の引数も引用して残す()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("--flag");
        startInfo.ArgumentList.Add(string.Empty);

        BatchShimProcessGuard.Apply(startInfo, PathQuoteFormat, EnvExpansionFormat, NewlineFormat);

        startInfo.Arguments.Should().EndWith("\"--flag\" \"\"\"");
    }

    /// <summary>
    /// <c>%</c> は引用の内側でも展開され、展開結果が引用符を持ち込めば引用を破れるため拒否する。
    /// </summary>
    [Fact]
    public void Apply_環境変数展開を含む引数は拒否する()
    {
        var startInfo = new ProcessStartInfo { FileName = @"C:\npm\claude.cmd" };
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add("%EVIL%");

        var act = () =>
            BatchShimProcessGuard.Apply(
                startInfo,
                PathQuoteFormat,
                EnvExpansionFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("arg has percent: %EVIL%");
    }

    [Fact]
    public void Apply_引用符を含むパスは拒否する()
    {
        const string path = @"C:\tools\evil"" & calc & ""x.cmd";
        var startInfo = new ProcessStartInfo { FileName = path };

        var act = () =>
            BatchShimProcessGuard.Apply(
                startInfo,
                PathQuoteFormat,
                EnvExpansionFormat,
                NewlineFormat
            );

        act.Should().Throw<InvalidOperationException>().WithMessage($"path has quote: {path}");
    }
}
