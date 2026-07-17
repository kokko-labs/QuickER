using System.Text.RegularExpressions;
using FluentAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;
using QuickER.SqlServer;
using QuickER.Tests.GeneratedBinaryFixture;
using QuickER.Tests.GeneratedQueryFixture;
using Xunit;

namespace QuickER.Tests.Generator;

/// <summary>
/// 生成 C# コード（.g.cs）の固定文（XML ドキュメントコメント・行コメント・例外メッセージなど）が
/// 英語で統一されていること＝日本語（CJK 文字）が紛れ込んでいないことを守る回帰防止ガード。
/// </summary>
/// <remarks>
/// <para>
/// 固定文は <c>Templates/CSharpRuntime/_00〜_09.scriban</c>（連結して 1 本のテンプレートになる）と、
/// C# 側の生成物埋め込み文字列に由来する。ここへ日本語が混入しても型検査・ビルドは通ってしまい静かに回帰するため、
/// 「日本語を含まない入力から生成した出力に CJK が 1 文字も無い」ことをテストで固定する。
/// </para>
/// <para>
/// <b>ユーザーデータ由来の日本語は正当</b>（ER 図の説明・メモは生成メソッドの XML コメント等に反映される）。
/// そのため本テストは、既存フィクスチャ図の自由テキスト（<see cref="Entity.Description"/> / <see cref="Entity.Memo"/> /
/// <see cref="Column.Description"/> / <see cref="QueryDefinition.Description"/>）をすべて空にしてから生成し、
/// 残る CJK＝固定文への日本語混入とみなす。構造・識別子（テーブル名・列名・メソッド名・型トークン・制約名）は
/// フィクスチャ側で英語のため触らない。
/// </para>
/// </remarks>
public sealed class GeneratedOutputEnglishGuardTests
{
    /// <summary>
    /// CJK 文字の検出パターン。タスク指定の範囲（U+3000-U+9FFF＝CJK 記号・ひらがな・カタカナ・CJK 統合漢字、
    /// U+FF00-U+FFEF＝全角英数記号・半角カナ）を対象にする。
    /// </summary>
    private static readonly Regex CjkPattern = new("[　-鿿＀-￯]", RegexOptions.Compiled);

    /// <summary>
    /// ランタイムパッケージ用ソース（<see cref="RuntimePackageSourceRenderer"/> の 4 レンダリング＝
    /// Core / SqlServer / Sqlite / EfCore）の出力に CJK 文字が含まれないことを検証する。
    /// これらは空図＋固定名前空間でレンダリングされるため、ユーザーデータ由来の日本語は元々混入し得ない。
    /// </summary>
    [Fact(DisplayName = "ランタイムパッケージ用ソース 4 本に日本語（CJK）が含まれない")]
    public void RuntimePackageSources_ContainNoCjk()
    {
        var renderer = new RuntimePackageSourceRenderer();

        var rendered = new (string FileName, string Content)[]
        {
            ("QuickERRuntime.g.cs (Core)", renderer.RenderCore()),
            ("QuickERRuntime.SqlServer.g.cs", renderer.RenderSqlServer()),
            ("QuickERRuntime.Sqlite.g.cs", renderer.RenderSqlite()),
            ("QuickERRuntime.EntityFrameworkCore.g.cs", renderer.RenderEfCore()),
        };

        AssertNoCjk(rendered);
    }

    /// <summary>
    /// 名前付きクエリを網羅するフィクスチャ図（SQLite 方言のQuickER 版 Repository＋EF Core・VO 有効）から、
    /// ユーザーデータの日本語を空にして全機能を生成し、出力に CJK が含まれないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "名前付きクエリ網羅フィクスチャの生成出力に日本語（CJK）が含まれない（ユーザーデータ空化）"
    )]
    public void QueryFixtureGeneratedOutput_ContainsNoCjk()
    {
        var diagram = QueryFixtureDefinition.Build();
        BlankUserSuppliedText(diagram);

        var files = Generate(diagram, QueryFixtureDefinition.Options);

        AssertNoCjk(files.Select(file => (file.FileName, file.Content)));
    }

    /// <summary>
    /// 無制限バイナリ除外フィクスチャ図（SQLite 方言のQuickER 版 Repository＋EF Core＋インメモリ＋リモートサービス・
    /// 無制限バイナリ除外・名前付きクエリ入り）から、ユーザーデータの日本語を空にして全機能を生成し、
    /// 出力（本体＋RemoteServer の 2 ファイル）に CJK が含まれないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "無制限バイナリ除外フィクスチャの生成出力（本体＋サーバー）に日本語（CJK）が含まれない（ユーザーデータ空化）"
    )]
    public void BinaryFixtureGeneratedOutput_ContainsNoCjk()
    {
        var diagram = BinaryFixtureDefinition.Build();
        BlankUserSuppliedText(diagram);

        var files = Generate(diagram, BinaryFixtureDefinition.Options);

        // リモートサービス生成のため本体＋RemoteServer の 2 ファイルが返る（両方を検査する）
        files
            .Should()
            .HaveCount(2, "リモートサービス有効時は本体とサーバー実装の 2 ファイルが出力される");
        AssertNoCjk(files.Select(file => (file.FileName, file.Content)));
    }

    /// <summary>
    /// 図中のユーザー入力（自由テキスト）をすべて空にする。生成される固定文だけが CJK 検査の対象になるようにするため。
    /// テーブル名・列名・メソッド名・型トークン・制約名などの構造・識別子はフィクスチャ側で英語のため触らない。
    /// </summary>
    private static void BlankUserSuppliedText(ErDiagram diagram)
    {
        foreach (var entity in diagram.Entities)
        {
            entity.Description = string.Empty;
            entity.Memo = string.Empty;

            foreach (var column in entity.Columns)
            {
                column.Description = string.Empty;
            }
        }

        foreach (var query in diagram.Queries)
        {
            query.Description = string.Empty;
        }
    }

    /// <summary>
    /// 実生成経路（<c>DiagramCodeGenerator</c> / <see cref="FixtureDriftHarness"/>）と同じ流儀で、
    /// 型解決・DB 定義メタトークン付加・名前付きクエリの型トークン解決を行ってからコードを生成する。
    /// </summary>
    private static IReadOnlyList<GeneratedFile> Generate(
        ErDiagram diagram,
        CodeGenerationOptions options
    )
    {
        var columnTypes = SqlServerCSharpTypeMapper.ResolveColumnTypes(diagram);
        columnTypes = CanonicalTypeTokenAttacher.Attach(
            columnTypes,
            diagram,
            new SqlServerTypeCatalog()
        );
        var provider = new SqlServerProvider();
        var queryParameterTypes = QueryParameterTypeResolver.Resolve(
            diagram,
            provider.TypeMapper,
            provider.TypeCatalog
        );
        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            columnTypes,
            options,
            queryParameterTypes
        );

        result
            .HasErrors.Should()
            .BeFalse("ガードテストの入力図は生成エラーなく生成できる必要がある");

        return result.Files;
    }

    /// <summary>
    /// 与えられた各ファイルの内容に CJK 文字が含まれないことを検証する。
    /// 検出時は「どのファイルの何行目・該当行の内容」を列挙し、原因（テンプレートまたは C# 側の埋め込み文字列への
    /// 日本語混入）へ誘導するメッセージで失敗させる。
    /// </summary>
    private static void AssertNoCjk(IEnumerable<(string FileName, string Content)> files)
    {
        var findings = new List<string>();

        foreach (var (fileName, content) in files)
        {
            var lines = content.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                if (CjkPattern.IsMatch(line))
                {
                    findings.Add($"{fileName}:{i + 1} 「{line.Trim()}」");
                }
            }
        }

        findings
            .Should()
            .BeEmpty(
                "生成 C# コードの固定文は英語で統一する必要があります（ユーザーデータ由来の日本語は入力で空にしてあります）。"
                    + "検出＝テンプレート（src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_00〜_09.scriban）または "
                    + "C# 側の生成物埋め込み文字列に日本語が混入しています。上記の 該当ファイル:行番号・該当行 を確認してください"
            );
    }
}
