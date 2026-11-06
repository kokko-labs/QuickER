using AwesomeAssertions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 説明文の FORM FEED（U+000C）が、生成テキストのどこにも生のまま残らないことを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 名前の信頼境界（<c>[Table]</c> などの属性の引数・EF Core の Fluent・インメモリのサンプル値式のエスケープ、XmlDoc の XML エスケープ、
/// 改行・制御文字の生成前診断）と、説明の FF の畳み込みの出口ごとの判定は、生成の構成のエンジンの決まった行へ移した
/// （<c>CodegenCaseTests</c> の行 <c>compile-name-quoted-backslash</c>・<c>compile-name-braces-inmemory-seed</c>・
/// <c>model-efcore-quoted-names-escaped</c>・<c>code-xmldoc-name-angle-bracket-fallback</c>・
/// <c>code-xmldoc-stream-accessor-name-angle-bracket</c>・<c>code-xmldoc-form-feed-collapses-and-no-injected-member</c>・
/// <c>diag-name-control-character-*</c>）。
/// </para>
/// <para>
/// FF は C# 言語仕様の new-line ではないが、描画後の <c>ReplaceLineEndings</c> は FF も改行として正規化する。畳み込み
/// （<c>FoldNewLines</c>）の対象から漏れると、説明 1 つで XmlDoc の <c>///</c> 行やリテラルが行をまたぎ、任意のメンバー宣言を挿入できる。
/// 名前と違い説明は入口検証で止めない（改行が正当な値）ため、畳み込みが唯一の防壁になる。
/// </para>
/// </remarks>
public class NameTrustBoundaryGenerationTests
{
    /// <summary>説明文の FF が、生成テキストのどのファイルにも生のまま残らないことを検証する</summary>
    /// <remarks>
    /// 残す理由（P3-6）：「全ファイルのどこにも生の U+000C が無い」は文字の種類の不変条件で、出口を 1 つずつ名指す構造の判定では言えない
    /// （確かめた出口のほかに説明の出口が増えて FF が残っても見えない）。出口ごとの判定（XmlDoc・<c>[DbColumnMeta]</c> の引数・EditModel の
    /// 表示名のリテラルが畳んだ 1 行のまま残ること・注入した名前のメンバーが無いこと）とコンパイルは移した
    /// （移した先：<c>CodegenCaseTests</c> の行 <c>code-xmldoc-form-feed-collapses-and-no-injected-member</c>）。
    /// </remarks>
    [Fact(DisplayName = "説明文の FORM FEED は生成テキストのどこにも生のまま残らない")]
    public void FormFeedInDescription_IsFoldedNotBroken()
    {
        const char FormFeed = (char)0x0C;
        var diagram = new ErDiagram
        {
            Entities =
            [
                new Entity
                {
                    TableName = "items",
                    Columns =
                    [
                        new Column
                        {
                            Name = "item_id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                            Description = $"x{FormFeed}public static int Injected = 42; //",
                        },
                    ],
                },
            ],
        };

        var result = new CSharpCodeGenerationService().Generate(
            diagram,
            new CodeGenerationOptions
            {
                RootNamespace = "Sample.Domain",
                GenerateEditModels = true,
                GenerateMappers = true,
                GenerateRepositories = true,
                GenerateInMemoryRepositories = true,
                IncludeDataAnnotations = true,
            }
        );

        result
            .HasErrors.Should()
            .BeFalse(string.Join(" / ", result.Diagnostics.Select(d => d.Message)));
        result
            .Files.Where(file => file.Content.Contains(FormFeed))
            .Select(file => file.FileName)
            .Should()
            .BeEmpty("FF が残ると描画後の ReplaceLineEndings が実改行へ変える");
    }
}
