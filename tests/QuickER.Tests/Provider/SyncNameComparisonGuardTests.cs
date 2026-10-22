using System.IO;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using QuickER.Tests.Resources;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// 同期の差分と計画の照合（<c>SchemaDiffService</c>・<c>SyncPlanner</c>・<c>NameMatching</c>・<c>UniqueConstraintNaming</c>）が、
/// 大文字小文字・空白を自前で畳まないことを、ソースを走査して確かめるガード
/// </summary>
/// <remarks>
/// <para>
/// 4 ファイルの名前の照合は <c>SchemaNames</c>（<c>AreSame</c> / <c>Comparer</c> / <c>ToStoredForm</c> / <c>Key</c>）だけで行う。
/// 小文字化（<c>ToLowerInvariant</c>）でキーを作ると、<c>OrdinalIgnoreCase</c> で組む表・列と答えが割れる（ケルビン記号
/// U+212A と k のように、小文字化だけで同じになる別の名前）。<c>SchemaNamesGuardTests</c> は「比較・変換」と「モデルの
/// 名前のトークン」の両方を含む文だけを拾うので、比較器をフィールドに入れて使う形や、引数名が
/// モデルの名前でないキーの関数を拾えない。このガードは 4 ファイルに限って、変換と大小を無視する比較の指定そのものを禁じる。
/// </para>
/// <para>
/// <b>禁じるもの</b>: <c>Trim</c> / <c>TrimStart</c> / <c>TrimEnd</c>・<c>ToLower</c> / <c>ToLowerInvariant</c> /
/// <c>ToUpper</c> / <c>ToUpperInvariant</c>・<c>OrdinalIgnoreCase</c> / <c>InvariantCultureIgnoreCase</c> /
/// <c>CurrentCultureIgnoreCase</c>（識別子として現れたもの。コメント・文字列の中は数えない）。例外は無い（名前でない
/// 文字列の変換が要る箇所も、いまは無い）。要るようになったら、理由を書いてこのテストの許可の一覧へ足す。
/// </para>
/// <para>
/// <b>許すもの</b>: <c>StringComparer.Ordinal</c> / <c>StringComparison.Ordinal</c>。ただし綴りの判定に限る＝
/// <c>SchemaNames</c> が同じとみなす相手と綴りまで一致するかを見る判定（DB の綴りへ揃える・<c>NameLookup</c> の序数一致の段
/// ＝同じ名前の候補が片側に複数あるときに綴りまで一致する 1 件を選ぶ優先順位）と、<c>SchemaNames.Key</c> で畳んだキー・
/// シグネチャどうしの比較・並べ替え、説明文など名前でない文字列の比較。名前の同一性を Ordinal で決めてはいけない。
/// </para>
/// </remarks>
public sealed class SyncNameComparisonGuardTests
{
    /// <summary>走査する 4 ファイル（src/QuickER.Provider からの相対パス）</summary>
    private static readonly string[] Files =
    [
        "SchemaDiffService.cs",
        "SyncPlanner.cs",
        "NameMatching.cs",
        "UniqueConstraintNaming.cs",
    ];

    /// <summary>禁じる識別子</summary>
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "Trim",
        "TrimStart",
        "TrimEnd",
        "ToLower",
        "ToLowerInvariant",
        "ToUpper",
        "ToUpperInvariant",
        "OrdinalIgnoreCase",
        "InvariantCultureIgnoreCase",
        "CurrentCultureIgnoreCase",
    };

    [Fact(
        DisplayName = "同期の差分と計画の照合は、大文字小文字・空白を自前で畳まず SchemaNames を通す"
    )]
    public void SyncNameComparisons_UseSchemaNamesOnly()
    {
        var root = Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src", "QuickER.Provider");
        var violations = new List<string>();

        foreach (var file in Files)
        {
            var path = Path.Combine(root, file);
            File.Exists(path).Should().BeTrue(path);
            var variants = ConditionalSourceParse.Parse(
                File.ReadAllText(path),
                cancellationToken: TestContext.Current.CancellationToken
            );

            foreach (
                var token in ConditionalSourceParse.DescendantTokens(
                    variants,
                    TestContext.Current.CancellationToken
                )
            )
            {
                if (token.IsKind(SyntaxKind.IdentifierToken) && Forbidden.Contains(token.Text))
                {
                    var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add($"{file}:{line} {token.Text}");
                }
            }
        }

        violations
            .Should()
            .BeEmpty("照合は SchemaNames（AreSame / Comparer / ToStoredForm / Key）だけで行う");
    }
}
