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
/// <para>
/// 別枠で、<b>内容がバイナリのファイル</b>（先頭 8 KB に NUL バイトを含む）は判定しない。バイナリを UTF-8 として
/// 読むとバイト列が CJK の範囲の文字へ化けて偽の赤になるうえ、読み込みは例外を投げないので読めないことにも気づけない。
/// 名前で除外しないのは、そうしたファイル（Visual Studio が作る <c>.vs/</c>・サンプルを動かすと作られる DB ファイル）の
/// 名前と置き場を予測できず、名前の除外を足すたびに英語で書くべきテキストまで対象から外れ得るため。
/// テキストには NUL が現れないので、この判定でテキストが対象から外れることはない。
/// </para>
/// </remarks>
public sealed class SampleEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン（U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、
    /// U+FF00-U+FFEF＝全角英数記号・半角カナ）。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>バイナリ判定で見る先頭のバイト数</summary>
    private const int BinaryProbeLength = 8192;

    /// <summary>言語切替リンク（英語版 README から日本語版への案内）を含む行か</summary>
    private static bool IsLanguageSwitchLine(string line) => line.Contains("](README.ja.md)");

    /// <summary>先頭 <see cref="BinaryProbeLength"/> バイトに NUL を含む＝内容がバイナリのファイルか</summary>
    private static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[BinaryProbeLength];
        var read = stream.ReadAtLeast(buffer, BinaryProbeLength, throwOnEndOfStream: false);

        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    /// <summary>
    /// <paramref name="root"/> 配下を走査し、CJK を含む行を「相対パス:行番号: 行」の形で返す（除外は型の remarks のとおり）
    /// </summary>
    private static List<string> FindCjkOffenders(string root)
    {
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

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
                // 内容がバイナリのファイルは判定しない（UTF-8 として読むと CJK へ化けて偽の赤になる）
                if (IsBinary(path))
                {
                    continue;
                }

                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                // 読めないもの（ロック中）は判定しない
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

        return offenders;
    }

    [Fact(
        DisplayName = "samples/ 配下に日本語（CJK）が含まれない（README.ja.md と言語切替リンクを除く）"
    )]
    public void Samples_ContainNoCjk()
    {
        var samplesRoot = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "samples");
        Directory.Exists(samplesRoot).Should().BeTrue("走査対象のフォルダが在ること");

        var offenders = FindCjkOffenders(samplesRoot);

        offenders
            .Should()
            .BeEmpty(
                "samples/ は国際向けショーケースなので、日本語は README.ja.md だけに置く"
                    + $"（見つかった箇所: {string.Join(" / ", offenders)}）"
            );
    }

    /// <summary>
    /// 内容がバイナリのファイルは判定せず、テキストのファイルは従来どおり判定することを検証する
    /// </summary>
    /// <remarks>
    /// バイナリ側は、UTF-8 として読めば CJK の文字になるバイト列（「日本語」の UTF-8）を NUL の後ろに持たせる。
    /// バイナリ判定が無ければこのファイルが名指しされる。テキスト側は同じ文字を持ち、判定の対象に残ることを示す。
    /// </remarks>
    [Fact(DisplayName = "内容がバイナリのファイルは判定せず、テキストのファイルは判定する")]
    public void FindCjkOffenders_SkipsBinaryButChecksText()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuickER-SampleGuard-" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        try
        {
            var japanese = System.Text.Encoding.UTF8.GetBytes("日本語");
            File.WriteAllBytes(Path.Combine(root, "data.db"), [0x53, 0x00, .. japanese]);
            File.WriteAllBytes(Path.Combine(root, "note.txt"), japanese);

            FindCjkOffenders(root).Should().Equal("note.txt:1: 日本語");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
