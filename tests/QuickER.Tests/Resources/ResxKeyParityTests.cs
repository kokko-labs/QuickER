using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Xml.Linq;
using FluentAssertions;
using QuickER.Resources;
using Xunit;

namespace QuickER.Tests.Resources;

/// <summary>
/// src 配下の resx について、中立リソース（<c>Strings.resx</c>＝英語）と日本語サテライト（<c>.ja.resx</c>）の
/// キー集合が完全一致することを検証するガードテスト。新しい resx が増えても走査で自動的にカバーされる。
/// </summary>
/// <remarks>
/// <para>
/// 中立カルチャは英語（国際標準構成）。日本語は <c>Strings.ja.resx</c> サテライトで賦与する。
/// 「中立 <c>Strings.resx</c> に対して <c>.ja.resx</c> が欠落していたら失敗」の強制モードで検証する
/// （<see cref="AllNeutralResxHaveJapaneseSatellite"/>）。新しい中立 resx を追加したら
/// 対応する日本語サテライトも必ず用意する必要がある。
/// </para>
/// </remarks>
public class ResxKeyParityTests
{
    /// <summary>テスト出力（列挙したペアのログ用）</summary>
    private readonly ITestOutputHelper _output;

    public ResxKeyParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>中立 resx（英語）と対応する日本語サテライトのキー集合が完全一致することを検証する</summary>
    [Fact(DisplayName = "中立 resx と .ja.resx のキー集合が一致する")]
    public void NeutralAndJapaneseResx_HaveIdenticalKeySets()
    {
        var pairs = EnumerateResxPairs().ToList();

        // 基盤 Stage の時点で少なくとも 1 ペア（言語切替 UI の Strings）が存在するはず。
        // ゼロ件のまま緑になると「検証していないのに合格」になるためガードする。
        pairs.Should().NotBeEmpty("検証対象の中立 resx / .ja.resx ペアが 1 つも見つからない");

        foreach (var pair in pairs)
        {
            _output.WriteLine($"検証ペア: {pair.NeutralPath} <-> {pair.SatellitePath}");

            var neutralKeys = ReadKeys(pair.NeutralPath);
            var japaneseKeys = ReadKeys(pair.SatellitePath);

            japaneseKeys
                .Should()
                .BeEquivalentTo(
                    neutralKeys,
                    $"日本語サテライト {pair.SatellitePath} のキー集合は中立 {pair.NeutralPath} と一致すべき"
                );
        }
    }

    /// <summary>
    /// 中立 <c>Strings.resx</c>（英語）はすべて対応する日本語サテライト（<c>.ja.resx</c>）を持つことを強制する。
    /// 日本語訳が揃っているため「サテライト欠落 = 失敗」の強制モードで検証する。
    /// </summary>
    [Fact(DisplayName = "日本語サテライトを持たない中立 resx は存在しない")]
    public void AllNeutralResxHaveJapaneseSatellite()
    {
        var neutralWithoutJapanese = EnumerateNeutralResx()
            .Where(neutral => !File.Exists(ToJapaneseSatellitePath(neutral)))
            .ToList();

        foreach (var path in neutralWithoutJapanese)
        {
            _output.WriteLine($"日本語サテライト未整備の中立 resx: {path}");
        }

        // 中立（英語）と日本語サテライトは対で揃える。日本語サテライト欠落は失敗とする。
        // 新しい中立 resx を追加したら対応する .ja.resx も必ず用意すること。
        neutralWithoutJapanese
            .Should()
            .BeEmpty("すべての中立 Strings.resx は対応する .ja.resx を持つ必要がある");
    }

    /// <summary>
    /// 手書きの厳密型アクセサ（<see cref="Strings"/> 等）の public static string プロパティ集合が、
    /// 対応する中立リソース（コンパイル済み ResourceSet）のキー集合と完全一致することを検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// resx にキーを足したのに Designer プロパティを足し忘れた（またはその逆）を検出するガード。
    /// リフレクションで「静的 <c>ResourceManager</c> プロパティを持つ型」を走査するため、
    /// resx を持つアセンブリが今後増えても自動的に対象へ加わる。
    /// </para>
    /// <para>
    /// リソースキーはファイルではなくコンパイル済み ResourceManager（不変カルチャの ResourceSet）から
    /// 取得するため、resx → satellite の生成パイプラインまで含めて実体で照合できる。
    /// </para>
    /// </remarks>
    [Fact(DisplayName = "厳密型アクセサの public プロパティ集合が中立リソースのキー集合と一致する")]
    public void StronglyTypedAccessors_MatchTheirResourceKeys()
    {
        var accessors = FindResourceAccessorTypes().ToList();

        // 少なくとも 1 つ（言語切替 UI の Strings）は検出されるはず。ゼロ件のまま緑になると
        // 「走査に失敗しているのに合格」になるためガードする。
        accessors
            .Should()
            .NotBeEmpty(
                "厳密型リソースアクセサ（静的 ResourceManager を持つ型）が 1 つも見つからない"
            );

        foreach (var type in accessors)
        {
            _output.WriteLine($"検証アクセサ: {type.FullName}");

            var resourceManager = (ResourceManager)
                type.GetProperty("ResourceManager", BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;

            var resourceKeys = ReadStringResourceKeys(resourceManager);
            var propertyNames = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => p.Name)
                .ToHashSet();

            propertyNames
                .Should()
                .BeEquivalentTo(
                    resourceKeys,
                    $"アクセサ {type.FullName} の public static string プロパティ集合は "
                        + "中立リソースのキー集合と一致すべき"
                );
        }
    }

    /// <summary>
    /// QuickER 系アセンブリから、厳密型リソースアクセサ（静的 <c>ResourceManager</c> プロパティと
    /// 1 つ以上の public static string プロパティを持つ型）を列挙する。
    /// </summary>
    private static IEnumerable<Type> FindResourceAccessorTypes()
    {
        // Strings を参照済みのため QuickER.Gui はロード済み。将来 resx を持つ他アセンブリが
        // 増えてもロード済みなら自動的に対象へ含める。
        // ただし本走査はアセンブリのロード有無に依存するため、resx を持つ機能 UI アセンブリは
        // typeof(...) で明示シード（参照）してロードを保証する（単独実行でも取りこぼさない）。
        var seededAssemblies = new[]
        {
            typeof(Strings).Assembly,
            typeof(QuickER.AI.UI.Resources.Strings).Assembly,
            typeof(QuickER.AI.Chat.Resources.Strings).Assembly,
            typeof(QuickER.AI.Mock.Resources.Strings).Assembly,
            typeof(QuickER.CodeGen.UI.Resources.Strings).Assembly,
            typeof(QuickER.Db.UI.Resources.Strings).Assembly,
            typeof(QuickER.AI.Resources.Strings).Assembly,
            typeof(QuickER.CodeGen.CSharp.Resources.Strings).Assembly,
            typeof(QuickER.Provider.Resources.Strings).Assembly,
            typeof(QuickER.Cli.Resources.Strings).Assembly,
        };

        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Concat(seededAssemblies)
            .Where(a => a.GetName().Name?.StartsWith("QuickER", StringComparison.Ordinal) == true)
            .Distinct();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var managerProperty = type.GetProperty(
                    "ResourceManager",
                    BindingFlags.Public | BindingFlags.Static
                );

                if (managerProperty?.PropertyType != typeof(ResourceManager))
                {
                    continue;
                }

                var hasStringProperty = type.GetProperties(
                        BindingFlags.Public | BindingFlags.Static
                    )
                    .Any(p => p.PropertyType == typeof(string));

                if (hasStringProperty)
                {
                    yield return type;
                }
            }
        }
    }

    /// <summary>ResourceManager の不変カルチャ ResourceSet から文字列リソースのキー集合を読み出す</summary>
    /// <remarks>
    /// ResourceSet は ResourceManager が内部でキャッシュ・所有するため <c>Dispose</c> しない
    /// （破棄すると以降の <see cref="Strings"/> 参照が「closed resource set」で失敗する）。
    /// </remarks>
    private static HashSet<string> ReadStringResourceKeys(ResourceManager resourceManager)
    {
        var set = resourceManager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: true
        )!;

        return set.Cast<DictionaryEntry>()
            .Where(entry => entry.Value is string)
            .Select(entry => (string)entry.Key)
            .ToHashSet();
    }

    /// <summary>中立 resx（英語）とその日本語サテライトのペア</summary>
    private sealed record ResxPair(string NeutralPath, string SatellitePath);

    /// <summary>src 配下の全 resx から、日本語サテライトを持つ中立 resx のペアを列挙する</summary>
    private static IEnumerable<ResxPair> EnumerateResxPairs()
    {
        foreach (var neutral in EnumerateNeutralResx())
        {
            var japanese = ToJapaneseSatellitePath(neutral);

            if (File.Exists(japanese))
            {
                yield return new ResxPair(neutral, japanese);
            }
        }
    }

    /// <summary>
    /// src 配下（obj/bin 除く）の中立 resx（カルチャ接尾辞を持たない <c>*.resx</c>）を列挙する。
    /// </summary>
    private static IEnumerable<string> EnumerateNeutralResx()
    {
        var srcRoot = Path.Combine(FindRepositoryRoot(), "src");

        return Directory
            .EnumerateFiles(srcRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path => !IsUnderObjOrBin(path))
            .Where(path => !HasCultureSuffix(path));
    }

    /// <summary>中立 resx パスから対応する日本語サテライト（<c>.ja.resx</c>）のパスを求める</summary>
    private static string ToJapaneseSatellitePath(string neutralResxPath)
    {
        var directory = Path.GetDirectoryName(neutralResxPath)!;
        var baseName = Path.GetFileNameWithoutExtension(neutralResxPath);

        return Path.Combine(directory, $"{baseName}.ja.resx");
    }

    /// <summary>resx の <c>&lt;data name="..."&gt;</c> エントリのキー集合を読み出す</summary>
    private static HashSet<string> ReadKeys(string resxPath)
    {
        var doc = XDocument.Load(resxPath);

        // ルート直下の（既定名前空間の）<data> 要素のみを対象にする。
        // スキーマ定義（<xsd:element name="data">）は名前空間が異なるため混入しない。
        return doc.Root!.Elements("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToHashSet();
    }

    /// <summary>ファイル名にカルチャ接尾辞（例 <c>.en</c>）を持つ resx かどうかを判定する</summary>
    private static bool HasCultureSuffix(string resxPath)
    {
        // 例: "Strings.ja.resx" → GetFileNameWithoutExtension は "Strings.ja"。
        // 内側の拡張子（".ja"）が既知カルチャなら中立ではない（サテライト）。
        var withoutResx = Path.GetFileNameWithoutExtension(resxPath);
        var innerExtension = Path.GetExtension(withoutResx);

        if (string.IsNullOrEmpty(innerExtension))
        {
            return false;
        }

        var candidateCulture = innerExtension.TrimStart('.');

        return IsKnownCulture(candidateCulture);
    }

    /// <summary>指定文字列が実在するカルチャ名かどうかを判定する</summary>
    private static bool IsKnownCulture(string name)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(name);

            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>obj / bin フォルダ配下のパスかどうかを判定する</summary>
    private static bool IsUnderObjOrBin(string path)
    {
        var normalized = path.Replace('\\', '/');

        return normalized.Contains("/obj/") || normalized.Contains("/bin/");
    }

    /// <summary>テストアセンブリ位置から <c>QuickER.slnx</c> を目印にリポジトリ直下を遡って解決する</summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuickER.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "リポジトリ直下（QuickER.slnx）が見つかりませんでした。"
        );
    }
}

/// <summary>厳密型リソースアクセサ（<see cref="Strings"/>）のローカライズ解決を検証するテストクラス</summary>
public class StringsLocalizationTests
{
    // 注意: 検証はグローバル静的 Strings.Culture を変更せず、ResourceManager.GetString(key, culture) で
    // 明示カルチャ指定して読む。Strings.Culture は プロセス全体で共有される静的のため、これを一時変更すると
    // xUnit のクラス並列実行で他テスト（Strings.X を参照する VM 等）が変更を観測しフレークする（順序/並列依存）。

    /// <summary>日本語サテライトから日本語文言が返ることを検証する（resx パイプライン全体の疎通確認）</summary>
    [Fact(DisplayName = "ja カルチャで日本語文言を返す")]
    public void Strings_JapaneseCulture_ReturnsJapanese()
    {
        var ja = new CultureInfo("ja");

        Strings.ResourceManager.GetString("Language_Caption", ja).Should().Be("言語");
        Strings.ResourceManager.GetString("Language_English", ja).Should().Be("English");
    }

    /// <summary>中立カルチャ（英語）から英語文言が返ることを検証する（resx パイプライン全体の疎通確認）</summary>
    [Fact(DisplayName = "en カルチャで英語文言を返す")]
    public void Strings_EnglishCulture_ReturnsEnglish()
    {
        var en = new CultureInfo("en");

        Strings.ResourceManager.GetString("Language_Caption", en).Should().Be("Language");
        Strings
            .ResourceManager.GetString("Language_RestartConfirm", en)
            .Should()
            .Be("The display language has been changed. Restart the app now to apply it?");
    }
}
