using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Services;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Gui.Services;

/// <summary><see cref="TableDefinitionHtmlExporter" /> の HTML テーブル定義書生成を検証するテストクラス</summary>
/// <remarks>
/// カルチャ検証はグローバル静的（<c>Thread.CurrentUICulture</c> 等）を一切変更せず、
/// <see cref="TableDefinitionHtmlExporter.Build"/> の culture 引数へ明示注入する
/// （xUnit の並列実行でフレークする実績があるため。tasks/lessons.md 2026-07-08）。
/// 期待値は同じ明示カルチャの ResourceManager 読みで導出する。
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

    /// <summary>サイドバー・テーブルセクション・アンカーリンク・リレーション表内容が Excel 版と同値であることを検証する</summary>
    [Fact(DisplayName = "Build: ナビ・アンカー・リレーション表が Excel 版と同値")]
    public void Build_ProducesNavigationAnchorsAndRelationshipContent()
    {
        var diagram = BuildSampleDiagram();

        var html = TableDefinitionHtmlExporter.Build(diagram, culture: English);

        // 固定サイドバーと各テーブル詳細セクション（名前昇順 Order=1 / User=2）
        html.Should().Contain("<nav id=\"sidebar\">");
        html.Should().Contain("id=\"table-1\"");
        html.Should().Contain("id=\"table-2\"");
        html.Should().Contain("<h2>1. Order</h2>");
        html.Should().Contain("<h2>2. User</h2>");

        // テーブル一覧からの詳細アンカーリンク
        html.Should().Contain("<a href=\"#table-1\">Order</a>");
        html.Should().Contain("<a href=\"#table-2\">User</a>");

        // 概要（対象 DBMS・テーブル数・リレーション数）
        html.Should().Contain(En(nameof(GuiStrings.TableDoc_Cover_TargetDbms)));
        html.Should().Contain($"<dd>{diagram.TargetDbms}</dd>");

        // リレーション表・キー表記は Excel 版 BuildWorkbook の対応セルと同値
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );
        var relationshipSheet = workbook.Worksheet(
            En(nameof(GuiStrings.TableDoc_Sheet_Relationships))
        );
        var relationLabel = relationshipSheet.Cell(4, 7).GetString();
        var onDeleteLabel = relationshipSheet.Cell(4, 8).GetString();
        // 詳細シートの新レイアウトでは UserId 列は行5（ヘッダ行3・データ行4〜）
        var foreignKeyLabel = workbook.Worksheet("Order").Cell(5, 6).GetString();

        relationLabel.Should().Be("N:1");
        onDeleteLabel.Should().Be("CASCADE");
        foreignKeyLabel.Should().Be("FK1");

        html.Should().Contain($"<td>{relationLabel}</td>");
        html.Should().Contain($"<td>{onDeleteLabel}</td>");
        html.Should().Contain($"<td>{foreignKeyLabel}</td>");

        // 参照先セルは単一参照なので User 詳細セクション（#table-2）へリンク化
        html.Should().Contain("<a href=\"#table-2\">User.Id</a>");
    }

    /// <summary>タイトル・概要にシステム名・作成日を持たず（文書名のみ）、備考列・戻りリンクが無いことを検証する</summary>
    [Fact(DisplayName = "Build: 文書名のみ・システム名/作成日/備考列/戻りリンクなし")]
    public void Build_OmitsSystemNameCreatedDateMemoAndBackLink()
    {
        var diagram = BuildSampleDiagram();
        var documentTitle = En(nameof(GuiStrings.TableDoc_DocumentTitle));

        var html = TableDefinitionHtmlExporter.Build(diagram, culture: English);

        // タイトルは文書名のみ（システム名の前置なし）
        html.Should().Contain($"<title>{documentTitle}</title>");

        // 概要（dl）にシステム名・作成日のラベルを持たない
        var overview = ExtractSection(html, "<header id=\"overview\">", "</header>");
        overview.Should().NotContain("System Name");
        overview.Should().NotContain("Created Date");

        // 各テーブル詳細に「テーブル一覧に戻る」リンクを持たない
        html.Should().NotContain("class=\"back-link\"");
        html.Should().NotContain(En(nameof(GuiStrings.TableDoc_BackToSummary)));

        // リレーション一覧は 9 列（備考列なし）
        var relationshipSection = ExtractSection(
            html,
            "<section id=\"relationship-list\">",
            "</section>"
        );
        Regex.Matches(relationshipSection, "<th>").Count.Should().Be(9);

        // 各テーブルのカラム表は 7 列（備考列なし）
        var detailSection = ExtractSection(
            html,
            "<section class=\"table-detail\" id=\"table-1\">",
            "</section>"
        );
        Regex.Matches(detailSection, "<th>").Count.Should().Be(7);
    }

    /// <summary>開始タグから直後の終了タグまでの区間を切り出す（セクションは入れ子にならない前提）</summary>
    private static string ExtractSection(string html, string startTag, string endTag)
    {
        var start = html.IndexOf(startTag, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = html.IndexOf(endTag, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return html.Substring(start, end - start + endTag.Length);
    }

    /// <summary>ユーザーデータがすべて HTML エスケープされ、生の危険文字列が現れないことを検証する</summary>
    [Fact(DisplayName = "Build: ユーザーデータがエスケープされる")]
    public void Build_EscapesUserData()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                new Entity
                {
                    TableName = "A<B>&\"C\"",
                    Description = "<script>alert(1)</script>",
                    Columns =
                    {
                        new Column { Name = "Id", DataType = "int" },
                    },
                },
            },
        };

        var html = TableDefinitionHtmlExporter.Build(diagram, culture: English);

        // 生の危険文字列は現れない
        html.Should().NotContain("<script>");
        html.Should().NotContain("A<B>");

        // エスケープ済みの表現が現れる
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.Should().Contain("A&lt;B&gt;&amp;&quot;C&quot;");
    }

    /// <summary>紛らわしい同名系のテーブルでも id 属性値がすべて相異なることを検証する</summary>
    [Fact(DisplayName = "Build: アンカー id が一意になる")]
    public void Build_ProducesUniqueAnchorIds()
    {
        var diagram = new ErDiagram
        {
            Entities =
            {
                MakeEntity("A B"),
                MakeEntity("A_B"),
                MakeEntity("AB"),
                MakeEntity("a b"),
            },
        };

        var html = TableDefinitionHtmlExporter.Build(diagram, culture: English);

        // すべての id 属性値を抽出し、重複がないことを確認する
        var ids = Regex
            .Matches(html, "id=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

        ids.Should().Contain(["table-1", "table-2", "table-3", "table-4"]);
        ids.Should().OnlyHaveUniqueItems();
    }

    /// <summary>単純な 1 カラムのエンティティを作る（アンカー一意性テスト用）</summary>
    private static Entity MakeEntity(string tableName) =>
        new()
        {
            TableName = tableName,
            Columns =
            {
                new Column { Name = "Id", DataType = "int" },
            },
        };

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

    /// <summary>外部リソース参照を一切含まない自己完結 HTML であることを検証する</summary>
    [Fact(DisplayName = "Build: 外部参照を含まない自己完結 HTML")]
    public void Build_IsSelfContained()
    {
        var diagram = BuildSampleDiagram();

        var html = TableDefinitionHtmlExporter.Build(diagram, culture: English);

        html.Should().NotContain("src=");
        html.Should().NotContain("<link");
        html.Should().NotContain("http://");
        html.Should().NotContain("https://");
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
