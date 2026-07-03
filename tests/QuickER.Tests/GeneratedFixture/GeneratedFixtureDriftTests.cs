using System.IO;
using System.Reflection;
using FluentAssertions;
using QuickER.Generator;
using QuickER.SqlServer;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// コミット済みの固定フィクスチャ <c>GeneratedFixture.g.cs</c> が、
/// 現在のテンプレート・型解決から再生成したコードと文字列完全一致することを検証するドリフト検知テスト。
/// </summary>
/// <remarks>
/// <para>
/// 実行時テスト (<c>GeneratedRuntimeIntegrationTests</c>) はコミット済みフィクスチャの生成型を直接呼ぶ。
/// テンプレートを変更するとフィクスチャが古くなり得るため、このテストで乖離を検出する。
/// </para>
/// <para>
/// 図・オプションは <see cref="GeneratedFixtureDefinition"/>（単一ソース）を共有しており、
/// 失敗時は「フィクスチャの再生成が必要」であることと再生成手順を示す。
/// </para>
/// </remarks>
public sealed class GeneratedFixtureDriftTests
{
    /// <summary>コミット済みフィクスチャファイルの絶対パスを、テストアセンブリの位置から遡って解決する</summary>
    private static string ResolveFixturePath()
    {
        // テスト実行ディレクトリ（bin/Debug/netX.Y-windows）から、ソースの GeneratedFixture フォルダを探す。
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!
        );
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "GeneratedFixture", "GeneratedFixture.g.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "コミット済みフィクスチャ GeneratedFixture.g.cs が見つかりませんでした。"
        );
    }

    /// <summary>
    /// 単一ソースの図・オプションから再生成した内容が、コミット済みフィクスチャと完全一致することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "コミット済みフィクスチャが現在のテンプレートからの再生成と完全一致する（ドリフト検知）"
    )]
    public void CommittedFixture_MatchesRegeneratedOutput()
    {
        var diagram = GeneratedFixtureDefinition.Build();
        var columnTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            GeneratedFixtureDefinition.Options
        );

        result.HasErrors.Should().BeFalse("フィクスチャ図の生成でエラーが出てはならない");
        result.Files.Should().ContainSingle("Split 無効のため 1 ファイルで生成される");

        var regenerated = result.Files[0].Content;
        var fixturePath = ResolveFixturePath();
        var committed = File.ReadAllText(fixturePath);

        committed
            .Should()
            .Be(
                regenerated,
                "コミット済みフィクスチャが現在のテンプレート出力と乖離しています。"
                    + "テンプレート（QuickER.Generator/Templates/CSharpRuntime.scriban 等）を変更した場合は、"
                    + "GeneratedFixtureDefinition から再生成した内容で "
                    + $"{fixturePath} を上書きし直してください（生成は SqlServerCSharpTypeMapper で型解決 → "
                    + "CSharpCodeGenerationService.Generate、単一ファイルの Content をそのまま書き出す）。"
            );
    }
}
