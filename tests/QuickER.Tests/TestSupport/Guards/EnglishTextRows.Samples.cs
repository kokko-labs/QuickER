using System.IO;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表：Samples（samples/ 配下のテキストのファイルの行）</summary>
internal static partial class EnglishTextRows
{
    /// <summary>バイナリ判定で見る先頭のバイト数</summary>
    private const int BinaryProbeLength = 8192;

    private static IEnumerable<EnglishTextRow> Samples() =>
        [
            // サンプルは生成物と同居する国際向けショーケースなので、手書きコード（Program.cs のコメント・出力・データ）も図の Description も
            // csproj のコメントも英語が正本（日本語版は README.ja.md だけ）。生成物（.g.cs / .g.md / .sql）も走査する。
            // 除外は例外の一覧でなく SampleLines の規則として持つ（3 つだけ＋内容がバイナリのファイル。理由は SampleLines の remarks）
            new(
                "sample-files",
                "samples/ は国際向けショーケースなので、日本語は README.ja.md だけに置く（どこか＝samples/ からの相対パス:行）",
                () =>
                {
                    var samplesRoot = Path.Combine(
                        NeutralResxFiles.FindRepositoryRoot(),
                        "samples"
                    );

                    return Directory.Exists(samplesRoot)
                        ? SampleLines(samplesRoot)
                        : throw new InvalidOperationException(
                            $"走査対象のフォルダが無い: {samplesRoot}"
                        );
                }
            ),
        ];

    /// <summary>
    /// <paramref name="root"/> 配下のテキストのファイルの行を返す（どこか＝相対パス:行番号、本文＝前後の空白を除いた行）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>除外は 3 つだけ</b>＝<c>README.ja.md</c>（日本語版そのもの）・言語切替リンクの行（英語版 README が日本語版を指す
    /// <c>](README.ja.md)</c> の行）・<c>bin</c> / <c>obj</c>（ビルド生成物）。除外を広げるときは、ここへ理由を書くこと。
    /// </para>
    /// <para>
    /// 別枠で、<b>内容がバイナリのファイル</b>（先頭 8 KB に NUL バイトを含む）は返さない。バイナリを UTF-8 として
    /// 読むとバイト列が CJK の範囲の文字へ化けて偽の赤になるうえ、読み込みは例外を投げないので読めないことにも気づけない。
    /// 名前で除外しないのは、そうしたファイル（Visual Studio が作る <c>.vs/</c>・サンプルを動かすと作られる DB ファイル）の
    /// 名前と置き場を予測できず、名前の除外を足すたびに英語で書くべきテキストまで対象から外れ得るため。
    /// テキストには NUL が現れないので、この判定でテキストが対象から外れることはない。
    /// 規則の自己検査は <c>Samples/SampleEnglishGuardTests</c>。
    /// </para>
    /// </remarks>
    internal static IEnumerable<EnglishText> SampleLines(string root)
    {
        var lines = new List<EnglishText>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            // ビルド生成物と日本語版 README は対象外
            if (SourceFiles.IsBuildOutput(relative) || Path.GetFileName(path) == "README.ja.md")
            {
                continue;
            }

            string[] fileLines;

            try
            {
                // 内容がバイナリのファイルは判定しない（UTF-8 として読むと CJK へ化けて偽の赤になる）
                if (IsBinary(path))
                {
                    continue;
                }

                fileLines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                // 読めないもの（ロック中）は判定しない
                continue;
            }

            for (var i = 0; i < fileLines.Length; i++)
            {
                if (IsLanguageSwitchLine(fileLines[i]))
                {
                    continue;
                }

                lines.Add(new EnglishText($"{relative}:{i + 1}", fileLines[i].Trim()));
            }
        }

        return lines;
    }

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
}
