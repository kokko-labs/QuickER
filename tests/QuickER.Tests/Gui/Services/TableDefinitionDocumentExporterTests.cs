using System.Globalization;
using System.IO;
using AwesomeAssertions;
using ClosedXML.Excel;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Gui.Services;

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

    /// <summary>シートが一覧 2 枚・詳細×N の順で生成されることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: シートが決められた順で生成される")]
    public void BuildWorkbook_CreatesSheetsInExpectedOrder()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );

        // エンティティは名前昇順（Order → User）。一覧 2 シートに続いて詳細シートが並ぶ
        workbook
            .Worksheets.Select(sheet => sheet.Name)
            .Should()
            .Equal(
                En(nameof(GuiStrings.TableDoc_Sheet_Summary)),
                En(nameof(GuiStrings.TableDoc_Sheet_Relationships)),
                "Order",
                "User"
            );
    }

    /// <summary>役割タグ（非表示の定義名）2 件が正しいシートを指すことを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: 役割タグ 2 件が非表示で正しいシートを指す")]
    public void BuildWorkbook_AddsHiddenRoleTags()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
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

    /// <summary>対象 DBMS が一覧タイトル直下の表示とカスタムプロパティの両方へ出力されることを検証する</summary>
    [Fact(
        DisplayName = "BuildWorkbook: 対象 DBMS を一覧タイトル直下とカスタムプロパティへ出力する"
    )]
    public void BuildWorkbook_WritesTargetDbms()
    {
        var (diagram, _, _) = BuildSampleDiagram();
        diagram.TargetDbms = "sqlite";

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );

        var summary = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Summary)));
        summary
            .Cell(TableDefinitionDocumentLayout.SummaryDbmsRow, 1)
            .GetString()
            .Should()
            .Be($"{En(nameof(GuiStrings.TableDoc_Cover_TargetDbms))}: sqlite");
        workbook
            .CustomProperties.CustomProperty(TableDefinitionDocumentLayout.TargetDbmsPropertyName)
            .GetValue<string>()
            .Should()
            .Be("sqlite");
    }

    /// <summary>テーブル一覧シートの見出し・行位置・テーブル名リンク・凍結・印刷体裁を検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: テーブル一覧の行位置・書式・リンク・印刷体裁")]
    public void BuildWorkbook_WritesSummarySheet()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );
        var summary = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Summary)));

        // 行1=タイトル・行3=ヘッダ・行4〜=データ。列は No./テーブル名/説明/備考 の 4 列
        summary.Cell(1, 1).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Sheet_Summary)));
        summary.Cell(3, 1).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Header_No)));
        summary
            .Cell(3, 2)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Header_TableName)));
        summary
            .Cell(3, 3)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Header_Description)));
        summary.Cell(3, 4).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_Header_Memo)));
        summary.Cell(4, 2).GetString().Should().Be("Order");
        summary.Cell(5, 2).GetString().Should().Be("User");

        // テーブル名セル自体が該当詳細シートへのリンク（左寄せ）
        var orderCell = summary.Cell(4, 2);
        orderCell.GetHyperlink().InternalAddress.ToString().Should().Be("'Order'!A1");
        orderCell.Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Left);

        // 説明・備考はユーザーデータどおり
        summary.Cell(5, 3).GetString().Should().Be("利用者");
        summary.Cell(5, 4).GetString().Should().Be("業務利用");

        // 見出しスタイル（背景 #1F4E79・白字 Bold）
        var headerCell = summary.Cell(3, 1);
        headerCell.Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#1F4E79"));
        headerCell.Style.Font.FontColor.Should().Be(XLColor.White);
        headerCell.Style.Font.Bold.Should().BeTrue();

        // 操作性・印刷体裁（オートフィルタは設定しない）
        summary.SheetView.SplitRow.Should().Be(3);
        summary.AutoFilter.IsEnabled.Should().BeFalse();
        summary.PageSetup.PagesWide.Should().Be(1);
        summary.PageSetup.LastRowToRepeatAtTop.Should().Be(3);
        summary.PageSetup.PrintAreas.Single().RangeAddress.ToString().Should().Be("A1:D5");
    }

    /// <summary>リレーション一覧シートにオートフィルタが設定されないことを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: リレーション一覧にオートフィルタは無い")]
    public void BuildWorkbook_RelationshipSheetHasNoAutoFilter()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );
        var relationships = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Relationships)));

        relationships.AutoFilter.IsEnabled.Should().BeFalse();
    }

    /// <summary>リレーション一覧の参照元・参照先テーブル名セルが該当詳細シートへリンク化され、文字列値は保たれることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: リレーション一覧のテーブル名セルが詳細シートへリンク化")]
    public void BuildWorkbook_RelationshipTableCellsLinkToDetailSheets()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );
        var relationships = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Relationships)));

        // データ行4：参照元（列3＝Order）・参照先（列5＝User）
        var sourceCell = relationships.Cell(4, 3);
        var targetCell = relationships.Cell(4, 5);

        // 文字列値はテーブル名のまま（インポータの GetString 読みを壊さない）
        sourceCell.GetString().Should().Be("Order");
        targetCell.GetString().Should().Be("User");

        // 各セルは該当詳細シートの A1 へのリンク（左寄せ）
        sourceCell.GetHyperlink().InternalAddress.ToString().Should().Be("'Order'!A1");
        sourceCell.Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Left);
        targetCell.GetHyperlink().InternalAddress.ToString().Should().Be("'User'!A1");
        targetCell.Style.Alignment.Horizontal.Should().Be(XLAlignmentHorizontalValues.Left);
    }

    /// <summary>詳細シートの新レイアウト（タイトル1/説明2/ヘッダ3/データ4）とリンク・キー・必須を検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: 詳細シートの新行位置・戻りリンク・参照リンク・キー・必須")]
    public void BuildWorkbook_WritesDetailSheet()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
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

        // 行2: テーブル説明のプレーン表示（Order は説明なしなので空）
        order.Cell(2, 1).GetString().Should().BeEmpty();

        // カラム見出し（行3）・データ（行4〜）
        order
            .Cell(3, 2)
            .GetString()
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_Header_ColumnName)));
        order.Cell(4, 2).GetString().Should().Be("Id");
        order.Cell(4, 6).GetString().Should().Be("PK");
        order.Cell(5, 2).GetString().Should().Be("UserId");
        order.Cell(5, 6).GetString().Should().Be("FK1");
        // 必須マーカー（非 nullable）
        order.Cell(5, 5).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_RequiredMark)));
        // 参照先セル：単一参照なので User 詳細シートへリンク化
        order.Cell(5, 7).GetString().Should().Be("User.Id");
        order.Cell(5, 7).GetHyperlink().InternalAddress.ToString().Should().Be("'User'!A1");

        // 操作性・印刷体裁
        order.SheetView.SplitRow.Should().Be(3);
        order.PageSetup.LastRowToRepeatAtTop.Should().Be(3);
        order.PageSetup.PrintAreas.Single().RangeAddress.ToString().Should().Be("A1:H5");

        // ユーザーデータ（日本語の説明）は無加工で保持される
        var userSheet = workbook.Worksheet("User");
        userSheet.Cell(2, 1).GetString().Should().Be("利用者");
        userSheet.Cell(4, 3).GetString().Should().Be("主キー");
        userSheet.Cell(4, 5).GetString().Should().Be(En(nameof(GuiStrings.TableDoc_RequiredMark)));
    }

    /// <summary>ページヘッダー中央が文書名のみ（システム名を含まない）であることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: ページヘッダー中央は文書名のみ")]
    public void BuildWorkbook_HeaderShowsDocumentTitleOnly()
    {
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: English
        );
        var summary = workbook.Worksheet(En(nameof(GuiStrings.TableDoc_Sheet_Summary)));

        // AddText(AllPages) は各 Occurrence へ格納されるため OddPages で確認する
        summary
            .PageSetup.Header.Center.GetText(XLHFOccurrence.OddPages)
            .Should()
            .Be(En(nameof(GuiStrings.TableDoc_DocumentTitle)));
    }

    /// <summary>ja カルチャ注入時に見出しが日本語で出力されることを検証する</summary>
    [Fact(DisplayName = "BuildWorkbook: ja カルチャで見出しが日本語になる")]
    public void BuildWorkbook_LocalizesHeadersForJapanese()
    {
        var japanese = new CultureInfo("ja");
        var (diagram, _, _) = BuildSampleDiagram();

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            diagram,
            culture: japanese
        );

        var summaryName = GuiStrings.ResourceManager.GetString(
            nameof(GuiStrings.TableDoc_Sheet_Summary),
            japanese
        )!;
        summaryName.Should().Be("テーブル一覧");
        var summary = workbook.Worksheet(summaryName);
        summary
            .Cell(3, 2)
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
            TableDefinitionDocumentExporter.SaveTo(diagram, path);

            File.Exists(path).Should().BeTrue();
            using var workbook = new XLWorkbook(path);
            // 一覧 2 枚＋詳細 2 枚
            workbook.Worksheets.Count.Should().Be(4);
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
