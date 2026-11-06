using System.IO;
using System.Reflection;
using System.Resources;
using QuickER.Tests.Resources;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>照合の表：Resources（中立 resx と日本語サテライト・厳密型アクセサ）</summary>
internal static partial class ParityRows
{
    /// <summary>いまの中立 resx の数（日本語サテライトの照合の空振りの下限）</summary>
    private const int NeutralResxCount = 13;

    private static IEnumerable<ParityRow> Resources() =>
        [
            new(
                "resx-ja-key-sets",
                "日本語サテライト（.ja.resx）のキー集合は、中立 resx（英語）のキー集合と一致する（項目は中立 resx のパス: キー）",
                ParityCompare.Set,
                () => ResxPairKeys(pair => pair.NeutralPath),
                () => ResxPairKeys(pair => pair.SatellitePath)
            ),
            new(
                "resx-ja-satellites",
                "すべての中立 Strings.resx は対応する .ja.resx を持つ（中立と日本語サテライトは対で揃える。項目は中立 resx のパス）",
                ParityCompare.CanonWithinCopy,
                () => [.. NeutralResxFiles.EnumerateNeutral().Select(RepositoryRelative)],
                () =>
                    [
                        .. Directory
                            .EnumerateFiles(
                                Path.Combine(NeutralResxFiles.FindRepositoryRoot(), "src"),
                                "*.ja.resx",
                                SearchOption.AllDirectories
                            )
                            .Select(RepositoryRelative)
                            .Where(path => !SourceFiles.IsBuildOutput(path))
                            .Select(path => path[..^".ja.resx".Length] + ".resx"),
                    ]
            )
            {
                MinimumCanon = NeutralResxCount,
            },
            new(
                "resx-accessor-properties",
                "厳密型アクセサの public static string プロパティ集合は、中立リソース（コンパイル済みの不変カルチャの ResourceSet）の"
                    + "キー集合と一致する（項目はアクセサの型: キー）",
                ParityCompare.Set,
                () =>
                    [
                        .. ResourceAccessors.Value.SelectMany(type =>
                            ResxKeyParityTests
                                .ReadStringResourceKeys(
                                    (ResourceManager)
                                        type.GetProperty(
                                                    "ResourceManager",
                                                    BindingFlags.Public | BindingFlags.Static
                                                )!
                                            .GetValue(null)!
                                )
                                .Select(key => $"{type.FullName}: {key}")
                        ),
                    ],
                () =>
                    [
                        .. ResourceAccessors.Value.SelectMany(type =>
                            type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                                .Where(property => property.PropertyType == typeof(string))
                                .Select(property => $"{type.FullName}: {property.Name}")
                        ),
                    ]
            ),
        ];

    /// <summary>日本語サテライトを持つ中立 resx の組ごとに、片側のキーを「中立 resx のパス: キー」で並べる</summary>
    private static IReadOnlyList<string> ResxPairKeys(
        Func<ResxKeyParityTests.ResxPair, string> side
    )
    {
        var pairs = ResxKeyParityTests.EnumerateResxPairs().ToList();

        if (pairs.Count == 0)
        {
            throw new InvalidOperationException(
                "検証対象の中立 resx / .ja.resx ペアが 1 つも見つからない"
            );
        }

        return
        [
            .. pairs.SelectMany(pair =>
                ResxKeyParityTests
                    .ReadKeys(side(pair))
                    .Select(key => $"{RepositoryRelative(pair.NeutralPath)}: {key}")
            ),
        ];
    }

    /// <summary>厳密型リソースアクセサ（見つからなければ例外）</summary>
    /// <remarks>
    /// 正本と写しで同じ一覧を使うため 1 回だけ列挙する（走査はロード済みのアセンブリも拾うので、正本を読む間に
    /// 依存のアセンブリがロードされると、写しの側だけにアクセサが増える）。
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<Type>> ResourceAccessors = new(() =>
    {
        var accessors = ResxKeyParityTests.FindResourceAccessorTypes().ToList();

        return accessors.Count > 0
            ? accessors
            : throw new InvalidOperationException(
                "厳密型リソースアクセサ（静的 ResourceManager を持つ型）が 1 つも見つからない"
            );
    });

    /// <summary>リポジトリのルートからの相対パス（区切りは /）</summary>
    private static string RepositoryRelative(string path) =>
        Path.GetRelativePath(NeutralResxFiles.FindRepositoryRoot(), path).Replace('\\', '/');
}
