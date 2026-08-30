using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Sqlite;
using QuickER.SqlServer;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// マルチターゲット生成が <see cref="CodeGenerationOptions.RepositoryDialects"/> の<b>指定順</b>に依存しないことを
/// 表明するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 既存の生成テスト・フィクスチャはいずれも <c>["sqlserver", "sqlite"]</c> の 1 通りでしか生成しないため、この軸は
/// 一度も揺すられていなかった。実際、契約／InMemory スコープが <c>repository_dialect</c>（＝実効方言の先頭）を
/// 参照していた欠陥は「方言の指定順で生成物が変わる」という形で現れ、全テスト緑のまま通過している。
/// </para>
/// <para>
/// ここで固定するのは次の 3 点:
/// </para>
/// <list type="bullet">
///   <item>両順序の出力が<b>ファイル名の集合として一致</b>する（片方にしか出ないファイルが無い）</item>
///   <item>同名ファイルの内容が<b>バイト一致</b>する（契約バケット・共有 Entity・<c>Runtime*.g.cs</c>・方言別実装のすべて）</item>
///   <item>診断（Info を含む）のメッセージ集合が一致する（方言名を含む説明文が順序で入れ替わらない）</item>
/// </list>
/// <para>
/// 差が出てよいのは<b>ファイルの出力順</b>だけで、それは <see cref="CodeGenerationResult.Files"/> の並びとしてのみ現れる
/// （書き出し先が別ファイルである以上、意味を持たない）。rowversion を含む図も対象にするのは、型統合の
/// 「どの方言の解決を採るか」が指定順の先頭決め打ちに退行しないことを押さえるため。
/// </para>
/// </remarks>
public class DialectOrderInvarianceTests
{
    /// <summary>片方の順序でしか出ないと困る、名指しの不変ファイル（存在確認つき＝命名が変わったら気づく）</summary>
    private static readonly string[] OrderInvariantFileNames =
    [
        "Entities.g.cs",
        "Repositories.g.cs",
        "Repositories.SqlServer.g.cs",
        "Repositories.Sqlite.g.cs",
        "Runtime.g.cs",
        "Runtime.SqlServer.g.cs",
        "Runtime.Sqlite.g.cs",
    ];

    /// <summary>rowversion を含まない図（方言可搬フィクスチャと同一）で、方言順の入れ替えが出力を変えないことを検証する</summary>
    [Fact(DisplayName = "方言順を入れ替えてもマルチターゲット生成物が一致する（rowversion なし）")]
    public void SwappedDialectOrder_ShouldProduceIdenticalOutput_WithoutRowVersion()
    {
        AssertOrderInvariant(
            "rowversion なし",
            Tests.GeneratedPortableFixture.PortableFixtureDefinition.Build(
                Tests.GeneratedPortableFixture.PortableDialect.SqlServer
            ),
            generateInMemory: false
        );
    }

    /// <summary>
    /// rowversion を含む図で、方言順の入れ替えが出力を変えないことを検証する
    /// （型統合の採用方言が「指定順の先頭」ではなく「行バージョンと解決した方言」で決まることの固定）。
    /// </summary>
    [Fact(DisplayName = "方言順を入れ替えてもマルチターゲット生成物が一致する（rowversion あり）")]
    public void SwappedDialectOrder_ShouldProduceIdenticalOutput_WithRowVersion()
    {
        AssertOrderInvariant(
            "rowversion あり",
            Tests.GeneratedMultiTargetRowVersionFixture.MultiTargetRowVersionFixtureDefinition.Build(),
            generateInMemory: false
        );
    }

    /// <summary>
    /// インメモリ Repository 併用（<c>Runtime.InMemory.g.cs</c> / <c>Repositories.InMemory.g.cs</c> が出る構成）でも
    /// 方言順の入れ替えが出力を変えないことを検証する。
    /// </summary>
    /// <remarks>
    /// インメモリ実装は方言を持たないのに、テンプレート上は「実効方言の先頭」が読める位置にある
    /// （＝方言名を問うゲートを書くと順序依存が忍び込む唯一のスコープ）。rowversion 列は書き込み除外の方言ゲートに
    /// 触れるため、この交差を rowversion ありの図で押さえる。
    /// </remarks>
    [Fact(DisplayName = "方言順を入れ替えてもマルチターゲット × インメモリ併用の生成物が一致する")]
    public void SwappedDialectOrder_ShouldProduceIdenticalOutput_WithInMemory()
    {
        AssertOrderInvariant(
            "rowversion あり × インメモリ併用",
            Tests.GeneratedMultiTargetRowVersionFixture.MultiTargetRowVersionFixtureDefinition.Build(),
            generateInMemory: true,
            additionalExpectedFiles: ["Repositories.InMemory.g.cs", "Runtime.InMemory.g.cs"]
        );
    }

    /// <summary>2 つの方言順で生成し、ファイル名集合・同名ファイルの内容・診断が一致することを表明する</summary>
    private static void AssertOrderInvariant(
        string caseName,
        ErDiagram diagram,
        bool generateInMemory,
        IReadOnlyList<string>? additionalExpectedFiles = null
    )
    {
        var forward = Generate(diagram, ["sqlserver", "sqlite"], generateInMemory);
        var reversed = Generate(diagram, ["sqlite", "sqlserver"], generateInMemory);

        forward
            .HasErrors.Should()
            .BeFalse($"「{caseName}」の生成（sqlserver→sqlite）は成功するべき");
        reversed
            .HasErrors.Should()
            .BeFalse($"「{caseName}」の生成（sqlite→sqlserver）は成功するべき");

        var forwardFiles = forward.Files.ToDictionary(
            file => file.FileName,
            file => file.Content,
            StringComparer.Ordinal
        );
        var reversedFiles = reversed.Files.ToDictionary(
            file => file.FileName,
            file => file.Content,
            StringComparer.Ordinal
        );

        // 名指しの不変ファイルが両方に実在すること（命名変更で表明が空振りするのを防ぐ）
        foreach (var expected in OrderInvariantFileNames.Concat(additionalExpectedFiles ?? []))
        {
            forwardFiles
                .Should()
                .ContainKey(
                    expected,
                    $"「{caseName}」の分割生成に '{expected}' が出るはず（出力ファイル: "
                        + string.Join(", ", forwardFiles.Keys.Order(StringComparer.Ordinal))
                        + "）"
                );
        }

        // (a) ファイル名の集合が一致する（順序は問わない）
        var onlyForward = forwardFiles
            .Keys.Except(reversedFiles.Keys, StringComparer.Ordinal)
            .ToList();
        var onlyReversed = reversedFiles
            .Keys.Except(forwardFiles.Keys, StringComparer.Ordinal)
            .ToList();

        onlyForward
            .Should()
            .BeEmpty(
                $"「{caseName}」で sqlserver→sqlite の順でしか出ないファイルがある: "
                    + string.Join(", ", onlyForward)
            );
        onlyReversed
            .Should()
            .BeEmpty(
                $"「{caseName}」で sqlite→sqlserver の順でしか出ないファイルがある: "
                    + string.Join(", ", onlyReversed)
            );

        // (b) 同名ファイルの内容がバイト一致する
        var differing = forwardFiles
            .Where(entry =>
                reversedFiles.TryGetValue(entry.Key, out var other)
                && !string.Equals(entry.Value, other, StringComparison.Ordinal)
            )
            .Select(entry =>
                $"{entry.Key}（初めて食い違う位置: {FirstDifference(entry.Value, reversedFiles[entry.Key])}）"
            )
            .ToList();

        differing
            .Should()
            .BeEmpty(
                $"「{caseName}」で方言の指定順によって内容が変わるファイルがある"
                    + "（マルチターゲット生成は指定順に依存してはならない）: "
                    + string.Join(" / ", differing)
            );

        // (c) 診断（Info を含む）も一致する
        DescribeDiagnostics(reversed)
            .Should()
            .BeEquivalentTo(
                DescribeDiagnostics(forward),
                $"「{caseName}」の診断が方言の指定順で変わってはならない"
            );
    }

    /// <summary>指定した方言順でマルチ辞書オーバーロードを呼ぶ（方言辞書の挿入順も指定順に合わせる）</summary>
    private static CodeGenerationResult Generate(
        ErDiagram diagram,
        IReadOnlyList<string> dialects,
        bool generateInMemory
    )
    {
        var sqlServerTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var sqliteTypes = SqliteCSharpTypeMapper.ResolveColumnTypes(diagram);

        var byDialect = new Dictionary<string, IReadOnlyDictionary<Guid, CSharpTypeInfo>>(
            StringComparer.OrdinalIgnoreCase
        );

        // 辞書の挿入順も指定順に合わせる（順序依存が options 側だけでなく辞書側にも忍び込まないことの検証）
        foreach (var dialect in dialects)
        {
            byDialect[dialect] = string.Equals(
                dialect,
                "sqlserver",
                StringComparison.OrdinalIgnoreCase
            )
                ? sqlServerTypes
                : sqliteTypes;
        }

        var options = new CodeGenerationOptions
        {
            RootNamespace = "Sample.Domain",
            RepositoryDialects = dialects,
            GenerateRepositories = true,
            GenerateEfCoreRepositories = false,
            GenerateInMemoryRepositories = generateInMemory,
            SplitFilesByCategory = true,
        };

        // 主辞書は「図の方言」由来で固定（入れ替えるのはターゲット方言の順序だけ）
        return new CSharpCodeGenerationService().Generate(
            diagram,
            sqlServerTypes,
            byDialect,
            options
        );
    }

    /// <summary>診断を「重要度＋メッセージ」の集合へ落とす（比較順は問わない）</summary>
    private static IReadOnlyList<string> DescribeDiagnostics(CodeGenerationResult result) =>
        result
            .Diagnostics.Select(diagnostic => $"{diagnostic.Severity}: {diagnostic.Message}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

    /// <summary>2 つの文字列が初めて食い違う位置と、その前後の抜粋を返す（失敗時の原因特定用）</summary>
    private static string FirstDifference(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);

        for (var index = 0; index < limit; index++)
        {
            if (left[index] != right[index])
            {
                var start = Math.Max(0, index - 60);
                var leftExcerpt = left[start..Math.Min(left.Length, index + 60)];
                var rightExcerpt = right[start..Math.Min(right.Length, index + 60)];

                return $"{index} 文字目 …[{leftExcerpt}] ⇔ [{rightExcerpt}]";
            }
        }

        return $"長さが違う（{left.Length} ⇔ {right.Length}）";
    }
}
