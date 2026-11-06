using System.IO;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>英語の文言の表：Resources（中立 resx の値）</summary>
internal static partial class EnglishTextRows
{
    private static IEnumerable<EnglishTextRow> Resources() =>
        [
            // 日本語は Strings.ja.resx サテライトの担当で、中立側は英語が正本。中立へ日本語を書くと、英語環境や明示カルチャ解決
            // （ResourceManager.GetString(key, InvariantCulture)＝ヘッドレス実行・外部 AI エージェント向け MCP サーバの英語固定経路）で
            // そのまま日本語が出る。対象 resx の列挙は照合の表の行 resx-* と同じ規則（NeutralResxFiles）を共有する
            new(
                "neutral-resx-values",
                "中立 resx（Strings.resx）の値は英語が正本。日本語は Strings.ja.resx へ書く（どこか＝中立 resx のパス: キー）",
                NeutralResxEntries,
                new EnglishException(
                    "src/QuickER.Gui/Resources/Strings.resx: Language_Japanese",
                    1,
                    "言語切替 UI が言語名を母語表記で並べるための値（「日本語」）"
                )
            ),
        ];

    /// <summary>中立 resx の全エントリの値（どこか＝リポジトリのルートからの相対パス: キー）</summary>
    private static IEnumerable<EnglishText> NeutralResxEntries()
    {
        var neutralFiles = NeutralResxFiles.EnumerateNeutral().ToList();

        if (neutralFiles.Count == 0)
        {
            // 走査に失敗しているのに合格にしない
            throw new InvalidOperationException("走査対象の中立 resx が 1 つも見つからない");
        }

        return
        [
            .. neutralFiles.SelectMany(path =>
                NeutralResxFiles
                    .ReadEntries(path)
                    .Select(entry => new EnglishText(
                        $"{RepositoryRelative(path)}: {entry.Name}",
                        entry.Value
                    ))
            ),
        ];
    }

    /// <summary>リポジトリのルートからの相対パス（区切りは /）</summary>
    private static string RepositoryRelative(string path) =>
        Path.GetRelativePath(NeutralResxFiles.FindRepositoryRoot(), path).Replace('\\', '/');
}
