using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace QuickER.Tests.Resources;

/// <summary>
/// 中立 resx（<c>Strings.resx</c>＝製品の中立言語＝英語）の値に日本語（CJK 文字）が混入していないことを守るガード。
/// </summary>
/// <remarks>
/// <para>
/// 日本語は <c>Strings.ja.resx</c> サテライトの担当で、中立側は英語が正本。中立へ日本語を書いてしまうと、
/// 英語環境や明示カルチャ解決（<c>ResourceManager.GetString(key, InvariantCulture)</c>＝ヘッドレス実行・
/// 外部 AI エージェント向け MCP サーバの英語固定経路）でそのまま日本語が出る。ビルド・型検査では検出できないため
/// 走査で固定する。
/// </para>
/// <para>
/// 対象 resx の列挙は <see cref="ResxKeyParityTests"/> と同じ規則（<see cref="NeutralResxFiles"/>）を共有する。
/// 言語名そのものを値に持つキー（言語切替 UI の「日本語」）だけは正当な例外として
/// <see cref="AllowedCjkKeys"/> のキー名許可リストで除外する。
/// </para>
/// </remarks>
public class NeutralResxEnglishGuardTests
{
    /// <summary>テスト出力（走査した resx のログ用）</summary>
    private readonly ITestOutputHelper _output;

    public NeutralResxEnglishGuardTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// CJK 文字の検出パターン。ErDiagramToolCatalogEnglishGuardTests / GeneratedOutputEnglishGuardTests と同じ範囲
    /// （U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>
    /// 中立 resx に CJK を含んでよいキー名の許可リスト。言語切替 UI が母語表記で言語名を並べるための値
    /// （<c>Language_Japanese</c>＝「日本語」）だけを認める。
    /// </summary>
    private static readonly HashSet<string> AllowedCjkKeys = new(StringComparer.Ordinal)
    {
        "Language_Japanese",
    };

    /// <summary>中立 resx のすべての値が英語（非 CJK）であることを検証する</summary>
    [Fact(DisplayName = "中立 resx の値に日本語（CJK）が含まれない")]
    public void NeutralResx_ValuesContainNoCjk()
    {
        var neutralFiles = NeutralResxFiles.EnumerateNeutral().ToList();

        // ゼロ件のまま緑になると「走査に失敗しているのに合格」になるためガードする。
        neutralFiles.Should().NotBeEmpty("走査対象の中立 resx が 1 つも見つからない");

        var findings = new List<string>();

        foreach (var path in neutralFiles)
        {
            _output.WriteLine($"検証対象: {path}");

            foreach (var (name, value) in NeutralResxFiles.ReadEntries(path))
            {
                if (AllowedCjkKeys.Contains(name) || !CjkPattern.IsMatch(value))
                {
                    continue;
                }

                findings.Add($"{Path.GetFileName(path)} / {name}: 「{value}」");
            }
        }

        findings
            .Should()
            .BeEmpty(
                "中立 resx（Strings.resx）の値は英語が正本です。日本語は Strings.ja.resx へ書いてください"
                    + "（言語名そのものなど正当な例外は AllowedCjkKeys へ追加）"
            );
    }
}
