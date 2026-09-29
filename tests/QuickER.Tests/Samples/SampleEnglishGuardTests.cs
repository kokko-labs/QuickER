using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Tests.Resources;

namespace QuickER.Tests.Samples;

/// <summary>
/// <c>samples/</c> 配下のファイルが英語で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// <para>
/// サンプルは生成物と同居する国際向けショーケースなので、手書きコード（<c>Program.cs</c> のコメント・出力・データ）も
/// 図の Description も csproj のコメントも英語が正本（日本語版は <c>README.ja.md</c> だけ）。この方針は文章でしか
/// 書かれておらず、ビルドも型検査も日本語の混入を止めないため、実際に csproj のコメント 4 本が日本語のまま残っていた。
/// </para>
/// <para>
/// 走査はテキストのファイル全部が対象で、生成物（<c>.g.cs</c> / <c>.g.md</c> / <c>.sql</c>）も含める。いま英語なので緑で、
/// 将来テンプレート側から混入したときは
/// <c>QuickER.Tests.CodeGen.CSharp.GeneratedOutputEnglishGuardTests</c> や
/// <c>QuickER.Tests.Provider.DdlOutputEnglishGuardTests</c> と二重に拾える。
/// </para>
/// <para>
/// <b>除外は 3 つだけ</b>＝<c>README.ja.md</c>（日本語版そのもの）・言語切替リンクの行（英語版 README が日本語版を指す
/// <c>](README.ja.md)</c> の行）・<c>bin</c> / <c>obj</c>（ビルド生成物）。除外を広げるときは、ここへ理由を書くこと。
/// </para>
/// </remarks>
public sealed class SampleEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン（U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、
    /// U+FF00-U+FFEF＝全角英数記号・半角カナ）。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>言語切替リンク（英語版 README から日本語版への案内）を含む行か</summary>
    private static bool IsLanguageSwitchLine(string line) => line.Contains("](README.ja.md)");

    [Fact(
        DisplayName = "samples/ 配下に日本語（CJK）が含まれない（README.ja.md と言語切替リンクを除く）"
    )]
    public void Samples_ContainNoCjk()
    {
        var samplesRoot = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "samples");
        Directory.Exists(samplesRoot).Should().BeTrue("走査対象のフォルダが在ること");

        var offenders = new List<string>();

        foreach (
            var path in Directory.EnumerateFiles(samplesRoot, "*", SearchOption.AllDirectories)
        )
        {
            var relative = Path.GetRelativePath(samplesRoot, path).Replace('\\', '/');

            // ビルド生成物と日本語版 README は対象外
            if (
                relative.Split('/').Any(segment => segment is "bin" or "obj")
                || Path.GetFileName(path) == "README.ja.md"
            )
            {
                continue;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                // 読めないもの（バイナリ・ロック中）は判定しない
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (CjkPattern.IsMatch(lines[i]) && !IsLanguageSwitchLine(lines[i]))
                {
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "samples/ は国際向けショーケースなので、日本語は README.ja.md だけに置く"
                    + $"（見つかった箇所: {string.Join(" / ", offenders)}）"
            );
    }
}
