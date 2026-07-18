using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Services;

/// <summary><see cref="TableDefinitionDocumentExporter" /> のテーブル定義書ブック生成を検証するテストクラス</summary>
/// <remarks>
/// カルチャ検証はグローバル静的（<c>Thread.CurrentUICulture</c> 等）を一切変更せず、
/// <see cref="TableDefinitionDocumentExporter.BuildWorkbook"/> の culture 引数へ明示注入する
/// （xUnit の並列実行でフレークする実績があるため。tasks/lessons.md 2026-07-08）。
/// 期待値は同じ明示カルチャの ResourceManager 読みで導出する。
/// </remarks>
public class TableDefinitionDocumentExporterTests
{
    /// <summary>英語カルチャ</summary>
    private static readonly CultureInfo English = new("en");

    /// <summary>作成日（決定的に注入する固定値）</summary>
    private static readonly DateTime FixedCreatedDate = new(2026, 7, 18);

    /// <summary>en カルチャで固定文言を解決する</summary>
    private static string En(string key) => GuiStrings.ResourceManager.GetString(key, English)!;

    /// <summary>User / Order の 2 テーブルと 1 リレーションを持つサンプル図を作る</summary>
    private static (ErDiagram Diagram, Guid UserId, Guid OrderId) BuildSampleDiagram()
    {
        var vm = new MainViewModel();
        var user = new EntityViewModel(
            new Entity
            {
                TableName = "User",
                Description = "利用者",
                Memo = "業務利用",
                Columns =
                {
                    new Column
                    {
                        Name = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                        Description = "主キー",
                    },
                    new Column
                    {
                        Name = "Name",
                        DataType = "nvarchar(50)",
                        IsNullable = false,
                        Description = "氏名",
                    },
                },
            }
        );
        var order = new EntityViewModel(
            new Entity
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
                    new Column
                    {
                        Name = "UserId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(user);
        vm.Entities.Add(order);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = user.Id,
                    TargetEntityId = order.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = user.Columns[0].Id,
                    TargetColumnId = order.Columns[1].Id,
                    ConstraintName = "FK_Order_User",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
                user,
                order
            )
        );

        return (vm.ToDiagramModel(), user.Id, order.Id);
    }

    /// <summary>シートが表紙・履歴・一覧 2 枚・詳細×N の順で生成されることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: シートが決められた順で生成される")]
    public void BuildWorkbook_CreatesSheetsInExpectedOrder()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            documentTitle: "受注管理システム",
            createdDate: FixedCreatedDate,
            culture: English
        );

        // エンティティは名前昇順（Order → User）。役割 4 シートに続いて詳細シートが並ぶ
        workbook
            .Worksheets.Select(sheet => sheet.Name)
            .Should()
            .Equal(
                En(nameof(GuiStrings.TableDoc_Sheet_Cover)),
                En(nameof(GuiStrings.TableDoc_Sheet_History)),
                En(nameof(GuiStrings.TableDoc_Sheet_Summary)),
                En(nameof(GuiStrings.TableDoc_Sheet_Relationships)),
                "Order",
                "User"
            );
    }

    /// <summary>役割タグ（非表示の定義名）4 件が正しいシートを指すことを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: 役割タグ 4 件が非表示で正しいシートを指す")]
    public void BuildWorkbook_AddsHiddenRoleTags()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            createdDate: FixedCreatedDate,
            culture: English
        );

        AssertRoleTag(
            workbook,
            TableDefinitionDocumentLayout.CoverDefinedName,
            En(nameof(GuiStrings.TableDoc_Sheet_Cover))
        );
        AssertRoleTag(
            workbook,
            TableDefinitionDocumentLayout.HistoryDefinedName,
            En(nameof(GuiStrings.TableDoc_Sheet_History))
        );
        AssertRoleTag(
            workbook,
            TableDefinitionDocumentLayout.SummaryDefinedName,
            En(nameof(GuiStrings.TableDoc_Sheet_Summary))
        );
        AssertRoleTag(
            workbook,
            TableDefinitionDocumentLayout.RelationshipsDefinedName,
            En(nameof(GuiStrings.TableDoc_Sheet_Relationships))
        );

        workbook
            .CustomProperties.CustomProperty(
                TableDefinitionDocumentLayout.FormatVersionPropertyName
            )
            .GetValue<string>()
            .Should()
            .Be(TableDefinitionDocumentLayout.FormatVersionValue);
    }

    /// <summary>定義名が非表示かつ指定シートの A1 を指すことを検証する</summary>
    private static void AssertRoleTag(
        XLWorkbook workbook,
        string definedName,
        string expectedSheetName
    )
    {
        workbook.DefinedNames.TryGetValue(definedName, out var defined).Should().BeTrue();
        defined!.Visible.Should().BeFalse();
        defined.Ranges.First().Worksheet.Name.Should().Be(expectedSheetName);
    }

    /// <summary>表紙シートの書誌情報が注入値どおりに配置されることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: 表紙に文書名・書誌情報が配置される")]
    public void BuildWorkbook_WritesCoverSheet()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            documentTitle: "受注管理システム",
            createdDate: FixedCreatedDate,
            culture: English
        );
        var cover = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Cover)));

        cover.ShowGridLines.Should().BeFalse();
        cover.Cell(4, 2).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_DocumentTitle)));
        cover.Cell(6, 2).GetString().Should().Be("受注管理システム");

        cover.Cell(9, 2).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Cover_TargetDbms)));
        cover.Cell(9, 3).GetString().Should().Be(diagram.TargetDbms);
        cover.Cell(10, 2).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Cover_Version)));
        cover.Cell(10, 3).GetString().Should().Be("1.0");
        cover
            .Cell(11, 2)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Cover_CreatedDate)));
        cover.Cell(11, 3).GetDateTime().Should().Be(FixedCreatedDate);
        cover.Cell(12, 2).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Cover_TableCount)));
        cover.Cell(12, 3).GetValue<int>().Should().Be(2);
        cover
            .Cell(13, 2)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Cover_RelationshipCount)));
        cover.Cell(13, 3).GetValue<int>().Should().Be(1);
    }

    /// <summary>改訂履歴シートの初版行が生成されることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: 改訂履歴に初版行が生成される")]
    public void BuildWorkbook_WritesHistorySheet()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            createdDate: FixedCreatedDate,
            culture: English
        );
        var history = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_History)));

        history.Cell(3, 1).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_History_Version)));
        history.Cell(3, 3).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_History_Content)));
        history.Cell(4, 1).GetString().Should().Be("1.0");
        history.Cell(4, 2).GetDateTime().Should().Be(FixedCreatedDate);
        history
            .Cell(4, 3)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_History_InitialEntry)));
    }

    /// <summary>テーブル一覧シートの見出し・行位置・見出しスタイル・凍結・フィルタ・印刷体裁を検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: テーブル一覧の行位置・書式・操作性・印刷体裁")]
    public void BuildWorkbook_WritesSummarySheet()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            createdDate: FixedCreatedDate,
            culture: English
        );
        var summary = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Summary)));

        // 行1=タイトル・行3=ヘッダ・行4〜=データ
        summary.Cell(1, 1).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Sheet_Summary)));
        summary.Cell(3, 1).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Header_No)));
        summary
            .Cell(3, 3)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Header_TableName)));
        summary.Cell(4, 3).GetString().Should().Be("Order");
        summary.Cell(5, 3).GetString().Should().Be("User");

        // 見出しスタイル（背景 #1F4E79・白字 Bold）
        var headerCell = summary.Cell(3, 1);
        headerCell.Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#1F4E79"));
        headerCell.Style.Font.FontColor.Should().Be(XLColor.White);
        headerCell.Style.Font.Bold.Should().BeTrue();

        // 操作性・印刷体裁
        summary.SheetView.SplitRow.Should().Be(3);
        summary.AutoFilter.IsEnabled.Should().BeTrue();
        summary.PageSetup.PagesWide.Should().Be(1);
        summary.PageSetup.LastRowToRepeatAtTop.Should().Be(3);
        summary.PageSetup.PrintAreas.Single().RangeAddress.ToString().Should().Be("A1:E5");
    }

    /// <summary>詳細シートの上部戻りリンク・参照先ハイパーリンク・キー表記・必須マーカーを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: 詳細シートの戻りリンク・参照リンク・キー・必須")]
    public void BuildWorkbook_WritesDetailSheet()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            createdDate: FixedCreatedDate,
            culture: English
        );
        var summaryName = En(nameof(GuiStrings.TableDoc_Sheet_Summary));
        var order = workbook.Worksheet("Order");

        // 行1: A1 タイトル ＋ H1 上部戻りリンク（右寄せ）
        order.Cell(1, 1).GetString().Should().Be("Order");
        var backCell = order.Cell(1, 8);
        backCell.GetString().Should().Be(En(nameof(GuiStrings.TableDoc_BackToSummary)));
        backCell.GetHyperlink().InternalAddress.ToString().Should().Be($"'{summaryName}'!A1");
        backCell.Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Right);

        // テーブル情報（行3 ヘッダ・行4 データ）
        order.Cell(3, 2).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Header_TableName)));
        order.Cell(4, 2).GetString().Should().Be("Order");

        // カラム見出し（行6）・データ（行7〜）
        order
            .Cell(6, 2)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Header_ColumnName)));
        order.Cell(7, 2).GetString().Should().Be("Id");
        order.Cell(7, 6).GetString().Should().Be("PK");
        order.Cell(8, 2).GetString().Should().Be("UserId");
        order.Cell(8, 6).GetString().Should().Be("FK1");
        // 必須マーカー（非 nullable）
        order.Cell(8, 5).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_RequiredMark)));
        // 参照先セル：単一参照なので User 詳細シートへリンク化
        order.Cell(8, 7).GetString().Should().Be("User.Id");
        order.Cell(8, 7).GetHyperlink().InternalAddress.ToString().Should().Be("'User'!A1");

        // 操作性・印刷体裁
        order.SheetView.SplitRow.Should().Be(6);
        order.PageSetup.LastRowToRepeatAtTop.Should().Be(6);
        order.PageSetup.PrintAreas.Single().RangeAddress.ToString().Should().Be("A1:H8");

        // ユーザーデータ（日本語の説明）は無加工で保持される
        var userSheet = workbook.Worksheet("User");
        userSheet.Cell(7, 3).GetString().Should().Be("主キー");
        userSheet.Cell(7, 5).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_RequiredMark)));
    }

    /// <summary>ja カルチャ注入時に見出しが日本語で出力されることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: ja カルチャで見出しが日本語になる")]
    public void BuildWorkbook_LocalizesHeadersForJapanese()
    {
        var japanese = new CultureInfo("ja");
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            createdDate: FixedCreatedDate,
            culture: japanese
        );

        var summaryName = GuiStrings.ResourceManager.GetString(
            nameof(GuiStrings.TableDoc_Sheet_Summary),
            japanese
        )!;
        summaryName.Should().Be("テーブル一覧");
        var summary = workbook.Worksheet(summaryName);
        summary
            .Cell(3, 3)
            .GetString()
            .Should()
            .Be(
                GuiStrings.ResourceManager.GetString(
                    nameof(GuiStrings.TableDoc_Header_TableName),
                    japanese
                )
            );
    }

    /// <summary>SaveTo が実ファイルへ保存し再読込できることを検証する</summary>
    [Fact(DisplayName = "SaveTo: Excel ファイルとして保存できる")]
    public void SaveTo_WritesFile()
    {
        var (diagram, _, _) = BuildSampleDiagram();
        var path = Path.Combine(Path.GetTempPath(), $"quicker-tabledoc-{Guid.NewGuid():N}.xlsx");

        try
        {
            TableDefinitionDocumentExporter.SaveTo(diagram, path, "受注管理システム");

            File.Exists(path).Should().BeTrue();
            using var workbook = new XLWorkbook(path);
            workbook.Worksheets.Count.Should().Be(6);
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
