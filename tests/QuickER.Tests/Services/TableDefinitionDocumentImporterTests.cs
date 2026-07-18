using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using FluentAssertions;
using QuickER.Model;
using QuickER.Services;
using QuickER.ViewModels;
using GuiStrings = QuickER.Resources.Strings;

namespace QuickER.Tests.Services;

/// <summary><see cref="TableDefinitionDocumentImporter" /> の定義書取込と整合性検証を検証するテストクラス</summary>
/// <remarks>
/// 取込は役割タグ（非表示の定義名）でシートを特定するためカルチャ非依存。
/// エクスポータのカルチャは <c>BuildWorkbook</c> の culture 引数へ明示注入する
/// （グローバル静的は変更しない。tasks/lessons.md 2026-07-08）。
/// </remarks>
public class TableDefinitionDocumentImporterTests
{
    /// <summary>Category（親）と Product（子・FK）を持つサンプル図を作る</summary>
    private static ErDiagram BuildSampleDiagram()
    {
        var vm = new MainViewModel();
        var parent = new EntityViewModel(
            new Entity
            {
                TableName = "Category",
                Description = "カテゴリ",
                Memo = "分類用",
                Columns =
                {
                    new Column
                    {
                        Name = "CategoryId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                        Description = "主キー",
                    },
                    new Column
                    {
                        Name = "CategoryName",
                        DataType = "nvarchar(50)",
                        IsNullable = false,
                        Description = "名称",
                    },
                },
            }
        );
        var child = new EntityViewModel(
            new Entity
            {
                TableName = "Product",
                Description = "商品",
                Columns =
                {
                    new Column
                    {
                        Name = "ProductId",
                        DataType = "int",
                        IsPrimaryKey = true,
                        IsNullable = false,
                    },
                    new Column
                    {
                        Name = "CategoryId",
                        DataType = "int",
                        IsForeignKey = true,
                        IsNullable = false,
                    },
                },
            }
        );
        vm.Entities.Add(parent);
        vm.Entities.Add(child);
        vm.Relationships.Add(
            new RelationshipViewModel(
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    SourceColumnId = parent.Columns[0].Id,
                    TargetColumnId = child.Columns[1].Id,
                    ConstraintName = "FK_Product_Category",
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                    OnUpdate = ForeignKeyReferentialAction.NoAction,
                },
                parent,
                child
            )
        );

        return vm.ToDiagramModel();
    }

    /// <summary>役割タグからシートを解決する（テスト用の簡易実装）</summary>
    private static IXLWorksheet ResolveRoleSheet(XLWorkbook workbook, string definedName)
    {
        workbook.DefinedNames.TryGetValue(definedName, out var defined).Should().BeTrue();

        return defined!.Ranges.First().Worksheet;
    }

    /// <summary>復元した図が Category / Product / リレーションを保持することを検証する共通アサート</summary>
    private static void AssertSampleDiagram(ErDiagram diagram)
    {
        diagram.Entities.Should().HaveCount(2);
        diagram.Relationships.Should().ContainSingle();
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Category"
                && entity.Description == "カテゴリ"
                && entity.Memo == "分類用"
            );
        diagram
            .Entities.Should()
            .ContainSingle(entity =>
                entity.TableName == "Product"
                && entity.Columns.Any(column => column.Name == "CategoryId" && column.IsForeignKey)
            );
        diagram.Relationships[0].ConstraintName.Should().Be("FK_Product_Category");
        diagram.Relationships[0].Type.Should().Be(RelationshipType.OneToMany);
        diagram.Relationships[0].OnDelete.Should().Be(ForeignKeyReferentialAction.Cascade);
        diagram.Relationships[0].OnUpdate.Should().Be(ForeignKeyReferentialAction.NoAction);
    }

    /// <summary>本アプリが出力した定義書を再取込し、エンティティ・列・リレーションが往復保持されることを検証する</summary>
    [Fact(DisplayName = "このアプリが出力した定義書をそのまま再取込できる")]
    public void Load_RoundTripsExportedWorkbook()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(BuildSampleDiagram());
        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        AssertSampleDiagram(diagram);
    }

    /// <summary>en カルチャで出力した定義書が往復一致することを検証する</summary>
    [Fact(DisplayName = "en カルチャ出力を往復取込できる")]
    public void Load_RoundTripsEnglishWorkbook()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildSampleDiagram(),
            culture: new CultureInfo("en")
        );
        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        AssertSampleDiagram(diagram);
    }

    /// <summary>ja カルチャ出力（シート名が日本語）でも取込が成功するクロスカルチャを検証する</summary>
    [Fact(DisplayName = "ja カルチャ出力もカルチャ非依存で取込できる")]
    public void Load_ImportsJapaneseWorkbook()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildSampleDiagram(),
            culture: new CultureInfo("ja")
        );
        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        AssertSampleDiagram(diagram);
    }

    /// <summary>一覧シートをリネームしても定義名タグが追随して取込できることを検証する</summary>
    [Fact(DisplayName = "一覧シートをリネームしても取込できる")]
    public void Load_ImportsAfterSheetRename()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildSampleDiagram(),
            culture: new CultureInfo("en")
        );

        // ユーザーが一覧シートをリネームする状況を再現（定義名はセル参照なので追随する）
        ResolveRoleSheet(workbook, TableDefinitionDocumentLayout.SummaryDefinedName).Name =
            "Renamed Summary";
        ResolveRoleSheet(workbook, TableDefinitionDocumentLayout.RelationshipsDefinedName).Name =
            "Renamed Relationships";

        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        AssertSampleDiagram(diagram);
    }

    /// <summary>対象 DBMS がカスタムプロパティ経由で往復保持されることを検証する</summary>
    [Fact(DisplayName = "対象 DBMS を往復取込で復元できる")]
    public void Load_RestoresTargetDbms()
    {
        var source = BuildSampleDiagram();
        source.TargetDbms = "sqlite";

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            source,
            culture: new CultureInfo("en")
        );
        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        diagram.TargetDbms.Should().Be("sqlite");
    }

    /// <summary>対象 DBMS プロパティが無いブックでも既定値のまま取込が成功する寛容仕様を検証する</summary>
    [Fact(DisplayName = "対象 DBMS プロパティが無くても既定値で取込できる")]
    public void Load_KeepsDefaultTargetDbmsWhenPropertyIsMissing()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildSampleDiagram(),
            culture: new CultureInfo("en")
        );

        // ユーザーがプロパティを削除した状況を再現（表示セルは残っていても取込は既定値へフォールバック）
        workbook.CustomProperties.Delete(TableDefinitionDocumentLayout.TargetDbmsPropertyName);

        var diagram = TableDefinitionDocumentImporter.Load(workbook);

        diagram.TargetDbms.Should().Be(new ErDiagram().TargetDbms);
    }

    /// <summary>役割タグが無いブック（旧形式・他アプリ出力）を取込エラーにすることを検証する</summary>
    [Fact(DisplayName = "役割タグが無いと取り込みをエラーにする")]
    public void Load_ThrowsWhenRoleTagIsMissing()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildSampleDiagram(),
            culture: new CultureInfo("en")
        );

        // テーブル一覧タグを剥がすと必須役割が解決できずエラーになる
        workbook.DefinedNames.Delete(TableDefinitionDocumentLayout.SummaryDefinedName);

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        act.Should().Throw<InvalidDataException>().WithMessage(GuiStrings.TableDoc_MissingRoleTag);
    }

    /// <summary>一覧に対応する詳細シートが欠落している場合に取込が例外となることを検証する</summary>
    [Fact(DisplayName = "詳細シートが不足していると取り込みをエラーにする")]
    public void Load_ThrowsWhenDetailSheetIsMissing()
    {
        var vm = new MainViewModel();
        vm.Entities.Add(
            new EntityViewModel(
                new Entity
                {
                    TableName = "Users",
                    Columns =
                    {
                        new Column
                        {
                            Name = "Id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                    },
                }
            )
        );

        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            vm.ToDiagramModel(),
            culture: new CultureInfo("en")
        );
        // 唯一の詳細シートを削除すると一覧 1 件・詳細 0 件で件数不一致になる
        workbook.Worksheet("Users").Delete();

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        // 製品コードと同じ resx キーから期待値を導出し、カルチャに依らず完全一致で検証する
        act.Should().Throw<InvalidDataException>().WithMessage(GuiStrings.TableDoc_CountMismatch);
    }

    /// <summary>リレーション一覧の参照カラムが実在しない場合に取込が例外となることを検証する</summary>
    [Fact(DisplayName = "リレーション一覧の参照カラムが存在しないと取り込みをエラーにする")]
    public void Load_ThrowsWhenRelationshipColumnDoesNotExist()
    {
        using var workbook = TableDefinitionDocumentExporter.BuildWorkbook(
            BuildSampleDiagram(),
            culture: new CultureInfo("en")
        );

        // 参照元カラム（列4）をデータ開始行で実在しない名前へ改変する
        var relationshipSheet = ResolveRoleSheet(
            workbook,
            TableDefinitionDocumentLayout.RelationshipsDefinedName
        );
        relationshipSheet.Cell(TableDefinitionDocumentLayout.RelationshipDataStartRow, 4).Value =
            "MissingColumn";

        var act = () => TableDefinitionDocumentImporter.Load(workbook);

        // 製品コードと同じ resx キーからフォーマット済みメッセージを導出し、カルチャに依らず完全一致で検証する
        act.Should()
            .Throw<InvalidDataException>()
            .WithMessage(
                string.Format(
                    GuiStrings.TableDoc_RelChildColumnNotFound,
                    "Product",
                    "MissingColumn"
                )
            );
    }
}
