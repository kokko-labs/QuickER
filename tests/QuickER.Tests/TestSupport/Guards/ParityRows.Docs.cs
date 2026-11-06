using System.IO;
using QuickER.Tests.Docs;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：Docs（ライセンス文書の列挙と公開パッケージのライセンスメタデータ）</summary>
internal static partial class ParityRows
{
    private static IEnumerable<ParityRow> Docs() =>
        [
            LicensingMitRow("licensing-mit-row", "LICENSING.md"),
            LicensingMitRow("licensing-ja-mit-row", "LICENSING.ja.md"),
            new(
                "license-nc-scope-holds-nc-packages",
                "LICENSE-NC.md のスコープ列挙は、PackageLicenseFile でライセンス文書を同梱する公開パッケージをすべて載せる"
                    + "（スコープ外は MIT 扱いになるため、同梱した条件が及ばない）",
                ParityCompare.CanonWithinCopy,
                () =>
                    [
                        .. LicenseDocumentParityTests
                            .PublishedPackages()
                            .Where(package => !package.IsMit)
                            .Select(package => package.ScopePath),
                    ],
                NcScopeDirectories
            ),
            new(
                "license-nc-scope-lists-existing-non-mit-projects",
                "LICENSE-NC.md が列挙するのは、実在する src のプロジェクトで、MIT で公開するパッケージではないものだけ"
                    + "（改名・削除したらライセンス文書の列挙も更新する。MIT のものが載ると、商用利用できるはずのものを利用者が諦める）",
                ParityCompare.CopyWithinCanon,
                () =>
                {
                    var mit = LicenseDocumentParityTests
                        .PublishedPackages()
                        .Where(package => package.IsMit)
                        .Select(package => package.ScopePath)
                        .ToHashSet(StringComparer.Ordinal);

                    return
                    [
                        .. Directory
                            .GetDirectories(Path.Combine(LicenseDocumentParityTests.Root, "src"))
                            .Select(directory => "src/" + Path.GetFileName(directory))
                            .Where(scope => !mit.Contains(scope)),
                    ];
                },
                NcScopeDirectories
            ),
        ];

    /// <summary>LICENSING の MIT 行の照合（正本は MIT で公開する公開パッケージ）</summary>
    private static ParityRow LicensingMitRow(string id, string fileName) =>
        new(
            id,
            $"{fileName} の MIT 行は、PackageLicenseExpression が MIT の公開パッケージを過不足なく並べる"
                + "（読者はこの行で自分のパッケージが MIT かを確認する）",
            ParityCompare.Multiset,
            () =>
                [
                    .. LicenseDocumentParityTests
                        .PublishedPackages()
                        .Where(package => package.IsMit)
                        .Select(package => package.PackageId),
                ],
            () =>
                LicenseDocumentParityTests.ExpandPackageNames(
                    LicenseDocumentParityTests.FindMitRow(fileName)
                        ?? throw new InvalidOperationException(
                            $"{fileName} に MIT のパッケージを並べた表の行（`QuickER.Runtime` を含み `| MIT |` で終わる行）が"
                                + "見つからない。表の書き方を変えたなら、変更後の記述が実際のライセンスメタデータと"
                                + "一致することを確かめたうえで行の読み取りを更新すること"
                                + "（見つからないときに照合を飛ばすと、行を書き換えた瞬間にこのガードが静かに無効化される）"
                        )
                )
        );

    /// <summary>LICENSE-NC.md のスコープ列挙（読めなければ例外）</summary>
    private static IReadOnlyList<string> NcScopeDirectories()
    {
        var scope = LicenseDocumentParityTests.NcScopeDirectories();

        return scope.Count > 0
            ? scope
            : throw new InvalidOperationException(
                "LICENSE-NC.md の冒頭にスコープの箇条書きが見つからない"
            );
    }
}
