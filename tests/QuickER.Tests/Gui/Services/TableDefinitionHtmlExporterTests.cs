using System.Globalization;
using System.IO;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="TableDefinitionHtmlExporter" /> の HTML テーブル定義書生成を検証するテストクラス</summary>
/// <remarks>
/// カルチャ検証はグローバル静的（<c>Thread.CurrentUICulture</c> 等）を一切変更せず、
/// <see cref="TableDefinitionHtmlExporter.Build"/> の culture 引数へ明示注入する
/// （xUnit の並列実行でフレークする実績があるため）。
/// 期待値は同じ明示カルチャの ResourceManager 読みで導出する。
/// 出力の記法（ナビ・アンカー・リレーション表・エスケープ・アンカー id の一意性・自己完結）は形式の往復のエンジンの形の行
/// （excel-html-user-order・html-escapes-user-data・html-unique-anchor-ids の HTML の期待）が見る。
/// </remarks>
public class TableDefinitionHtmlExporterTests
{
    /// <summary>英語カルチャ</summary>
    private static readonly CultureInfo English = new("en");

    /// <summary>日本語カルチャ</summary>
    private static readonly CultureInfo Japanese = new("ja");

    /// <summary>指定カルチャで固定文言を解決する</summary>
    private static string L(string key, CultureInfo culture) =>
        GuiStrings.ResourceManager.GetString(key, culture)!;

    /// <summary>en カルチャで固定文言を解決する</summary>
    private static string En(string key) => L(key, English);

    /// <summary>User / Order の 2 テーブルと 1 リレーションを持つサンプル図を作る</summary>
    private static ErDiagram BuildSampleDiagram()
    {
        var userId = new Column
        {
            Name = "Id",
            DataType = "int",
            IsPrimaryKey = true,
            IsNullable = false,
            Description = "主キー",
        };
        var user = new Entity
        {
            TableName = "User",
            Description = "利用者",
            Memo = "業務利用",
            Columns =
            {
                userId,
                new Column
                {
                    Name = "Name",
                    DataType = "nvarchar(50)",
                    IsNullable = false,
                    Description = "氏名",
                },
            },
        };
        var orderUserId = new Column
        {
            Name = "UserId",
            DataType = "int",
            IsForeignKey = true,
            IsNullable = false,
        };
        var order = new Entity
        {
            TableName = "Order",
            Columns =
            {
                new Column
                {
                    Name = "Id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                orderUserId,
            },
        };

        return new ErDiagram
        {
            Entities = { user, order },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = user.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(userId.Id, orderUserId.Id)],
                    ConstraintName = "FK_Order_User",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            },
        };
    }

    /// <summary>en / ja 注入で見出し文言・lang 属性が切り替わることを検証する</summary>
    [Fact(DisplayName = "Build: en / ja で見出しと lang 属性が切り替わる")]
    public void Build_LocalizesForCulture()
    {
        var diagram = BuildSampleDiagram();

        var englishHtml = TableDefinitionHtmlExporter.Build(diagram, culture: English);
        var japaneseHtml = TableDefinitionHtmlExporter.Build(diagram, culture: Japanese);

        // lang 属性
        englishHtml.Should().Contain("<html lang=\"en\">");
        japaneseHtml.Should().Contain("<html lang=\"ja\">");

        // 見出し（文書名）
        englishHtml.Should().Contain($"<h1>{En(nameof(GuiStrings.TableDoc_DocumentTitle))}</h1>");
        japaneseHtml
            .Should()
            .Contain($"<h1>{L(nameof(GuiStrings.TableDoc_DocumentTitle), Japanese)}</h1>");

        // 表ヘッダ文言（テーブル名）
        englishHtml
            .Should()
            .Contain($"<th>{En(nameof(GuiStrings.TableDoc_Header_TableName))}</th>");
        japaneseHtml
            .Should()
            .Contain($"<th>{L(nameof(GuiStrings.TableDoc_Header_TableName), Japanese)}</th>");
        japaneseHtml.Should().Contain("テーブル名");
    }

    /// <summary>SaveTo が UTF-8 で実ファイルへ保存し Build と同内容になることを検証する</summary>
    [Fact(DisplayName = "SaveTo: UTF-8 で保存し Build と同内容")]
    public void SaveTo_WritesUtf8FileMatchingBuild()
    {
        var diagram = BuildSampleDiagram();
        var path = Path.Combine(Path.GetTempPath(), $"quicker-tabledoc-{Guid.NewGuid():N}.html");

        try
        {
            TableDefinitionHtmlExporter.SaveTo(diagram, path);

            File.Exists(path).Should().BeTrue();
            var written = File.ReadAllText(path);
            written.Should().Be(TableDefinitionHtmlExporter.Build(diagram));
            written.Should().Contain(GuiStrings.TableDoc_DocumentTitle);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
