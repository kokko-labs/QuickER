using System.IO;
using AwesomeAssertions;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Guards;

namespace QuickER.Tests.Samples;

/// <summary>
/// <c>samples/</c> 配下のファイルが英語で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守るガードの、走査の規則の自己検査。
/// </summary>
/// <remarks>
/// <para>
/// サンプルは生成物と同居する国際向けショーケースなので、手書きコード（<c>Program.cs</c> のコメント・出力・データ）も
/// 図の Description も csproj のコメントも英語が正本（日本語版は <c>README.ja.md</c> だけ）。この方針は文章でしか
/// 書かれておらず、ビルドも型検査も日本語の混入を止めないため、実際に csproj のコメント 4 本が日本語のまま残っていた。
/// </para>
/// <para>
/// 走査そのものは英語の文言の表の行 <c>sample-files</c>（<c>PhraseTests</c>）に移した。走査はテキストのファイル全部が対象で、
/// 生成物（<c>.g.cs</c> / <c>.g.md</c> / <c>.sql</c>）も含める。将来テンプレート側から混入したときは
/// <c>QuickER.Tests.CodeGen.CSharp.GeneratedOutputEnglishGuardTests</c> や
/// <c>QuickER.Tests.Provider.DialectDdlTableTests</c>（全行に掛かる自動の英語の照合）と二重に拾える。
/// </para>
/// <para>
/// <b>除外は 3 つだけ</b>＝<c>README.ja.md</c>・言語切替リンクの行（<c>](README.ja.md)</c>）・<c>bin</c> / <c>obj</c>。
/// 別枠で、<b>内容がバイナリのファイル</b>（先頭 8 KB に NUL バイトを含む）は判定しない。除外の理由と、除外を広げるときに理由を書く場所は
/// <see cref="EnglishTextRows.SampleLines"/> の remarks。ここでは、その規則どおりに除外し、それ以外を判定することを確かめる。
/// </para>
/// </remarks>
public sealed class SampleEnglishGuardTests
{
    /// <summary>
    /// 走査の規則：除外の 3 つと内容がバイナリのファイルは判定せず、それ以外のテキストのファイルは行ごとに判定することを検証する
    /// </summary>
    /// <remarks>
    /// バイナリ側は、UTF-8 として読めば CJK の文字になるバイト列（「日本語」の UTF-8）を NUL の後ろに持たせる。
    /// バイナリ判定が無ければこのファイルが名指しされる。テキスト側は同じ文字を持ち、判定の対象に残ることを示す。
    /// README.ja.md・bin / obj の中・言語切替リンクの行は同じ文字を持っても名指しされず、同じファイルのほかの行は名指しされる。
    /// </remarks>
    [Fact(
        DisplayName = "サンプルの走査は除外の 3 つと内容がバイナリのファイルを判定せず、テキストのファイルは判定する"
    )]
    public void SampleLines_SkipExclusionsAndBinaryButCheckText()
    {
        using var folder = new TestTempFolder();
        var root = folder.Path;
        var japanese = System.Text.Encoding.UTF8.GetBytes("日本語");
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "app", "obj"));
        File.WriteAllBytes(Path.Combine(root, "data.db"), [0x53, 0x00, .. japanese]);
        File.WriteAllBytes(Path.Combine(root, "note.txt"), japanese);
        File.WriteAllBytes(Path.Combine(root, "README.ja.md"), japanese);
        File.WriteAllBytes(Path.Combine(root, "bin", "out.txt"), japanese);
        File.WriteAllBytes(Path.Combine(root, "app", "obj", "out.txt"), japanese);
        File.WriteAllText(
            Path.Combine(root, "README.md"),
            "English\n[日本語](README.ja.md)\n日本語 here\n"
        );

        EnglishTextRows
            .SampleLines(root)
            .Where(line => CjkText.Pattern.IsMatch(line.Text))
            .Select(line => $"{line.Where}: {line.Text}")
            .Order(StringComparer.Ordinal)
            .Should()
            .Equal("README.md:3: 日本語 here", "note.txt:1: 日本語");
    }
}
