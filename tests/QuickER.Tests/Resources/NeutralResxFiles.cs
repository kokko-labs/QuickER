using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace QuickER.Tests.Resources;

/// <summary>
/// src 配下の resx を走査する共有ヘルパ（中立 resx の列挙・日本語サテライトのパス解決・エントリ読み出し）。
/// </summary>
/// <remarks>
/// resx を対象にするガードテスト（<see cref="ResxKeyParityTests"/>＝キー集合パリティ／
/// <see cref="NeutralResxEnglishGuardTests"/>＝中立 resx の英語固定）が同じ列挙規則を共有するための置き場。
/// 列挙規則がテストごとにずれると「走査から漏れた resx が検査されないまま緑になる」ため 1 箇所に集約する。
/// </remarks>
internal static class NeutralResxFiles
{
    /// <summary>
    /// src 配下（obj/bin 除く）の中立 resx（カルチャ接尾辞を持たない <c>*.resx</c>）を列挙する。
    /// </summary>
    internal static IEnumerable<string> EnumerateNeutral()
    {
        var srcRoot = Path.Combine(FindRepositoryRoot(), "src");

        return Directory
            .EnumerateFiles(srcRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path => !IsUnderObjOrBin(path))
            .Where(path => !HasCultureSuffix(path));
    }

    /// <summary>中立 resx パスから対応する日本語サテライト（<c>.ja.resx</c>）のパスを求める</summary>
    internal static string ToJapaneseSatellitePath(string neutralResxPath)
    {
        var directory = Path.GetDirectoryName(neutralResxPath)!;
        var baseName = Path.GetFileNameWithoutExtension(neutralResxPath);

        return Path.Combine(directory, $"{baseName}.ja.resx");
    }

    /// <summary>
    /// resx の <c>&lt;data name="..."&gt;&lt;value&gt;</c> を「キー・値」の対で読み出す
    /// （型付き／バイナリリソースは対象外）。
    /// </summary>
    internal static IEnumerable<(string Name, string Value)> ReadEntries(string resxPath)
    {
        var doc = XDocument.Load(resxPath);

        // ルート直下の（既定名前空間の）<data> 要素のみを対象にする。
        // スキーマ定義（<xsd:element name="data">）は名前空間が異なるため混入しない。
        foreach (var element in doc.Root!.Elements("data"))
        {
            var name = element.Attribute("name")?.Value;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // type / mimetype 付きは文字列以外（アイコン等）のため文言検査の対象外
            if (element.Attribute("type") is not null || element.Attribute("mimetype") is not null)
            {
                continue;
            }

            yield return (name, element.Element("value")?.Value ?? string.Empty);
        }
    }

    /// <summary>テストアセンブリ位置から <c>QuickER.slnx</c> を目印にリポジトリ直下を遡って解決する</summary>
    internal static string FindRepositoryRoot()
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

    /// <summary>ファイル名にカルチャ接尾辞（例 <c>.ja</c>）を持つ resx かどうかを判定する</summary>
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
}
