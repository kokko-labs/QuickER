using System.IO;
using System.Reflection;
using FluentAssertions;
using QuickER.Generator;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedPortableFixture;

/// <summary>
/// コミット済みの方言可搬フィクスチャ <c>PortableFixture.g.cs</c> が、現在のテンプレート・型解決から
/// 再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// 基準方言は SQL Server（<see cref="PortableDialect.SqlServer"/>）。生成される C# は方言非依存のため、
/// どの方言の型表記から生成しても出力は一致する（<see cref="PortableFixtureDialectIndependenceTests"/> が保証）。
/// テンプレート変更時はこのテストが乖離を検出し、失敗メッセージに再生成手順を示す。
/// </remarks>
public sealed class PortableFixtureDriftTests
{
    /// <summary>コミット済みフィクスチャファイルの絶対パスを、テストアセンブリの位置から遡って解決する</summary>
    private static string ResolveFixturePath()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "GeneratedFixture",
                PortableFixtureDefinition.OutputFileName
            );
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"コミット済みフィクスチャ {PortableFixtureDefinition.OutputFileName} が見つかりませんでした。"
        );
    }

    /// <summary>
    /// 単一ソースの図（SQL Server 基準）・オプションから再生成した内容が、
    /// コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済み可搬フィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedPortableFixture_MatchesRegeneratedOutput()
    {
        var diagram = PortableFixtureDefinition.Build(PortableDialect.SqlServer);
        var columnTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            PortableFixtureDefinition.Options
        );

        result.HasErrors.Should().BeFalse("可搬フィクスチャ図の生成でエラーが出てはならない");
        result.Files.Should().ContainSingle("Split 無効のため 1 ファイルで生成される");

        var regenerated = result.Files[0].Content;
        var fixturePath = ResolveFixturePath();
        var committed = File.ReadAllText(fixturePath);

        committed
            .Should()
            .Be(
                regenerated,
                "コミット済み可搬フィクスチャが現在のテンプレート出力と乖離しています。"
                    + "テンプレート（QuickER.Generator/Templates/CSharpRuntime.scriban 等）を変更した場合は、"
                    + "PortableFixtureDefinition（SQL Server 基準）から再生成した内容で "
                    + $"{fixturePath} を上書きし直してください。"
            );
    }
}
