using System.IO;
using System.Windows.Media;
using QuickER.Documents;
using QuickER.Gui.Abstractions;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>
/// エクスポートコマンドの実行サービス（保存ダイアログ→形式解決→書き出し→完了通知）。
/// </summary>
/// <remarks>
/// <para>
/// MainViewModel.ImportExport から抽出したもので、新しい出力形式の追加・完了通知の変更が
/// VM へ触れずに済む分離点。VM の能力は <see cref="IDiagramTransferHost"/> 経由で借りる。
/// </para>
/// <para>
/// 完了通知の規則は CLAUDE.md「完了通知の提示先」「出力形式の情報欠落告知」節のとおり:
/// 外部形式の出力完了はモーダル・欠落があれば形式ごとにセッション 1 回だけ内訳付き
/// （<see cref="ShowInformationDetails"/> 形式）で提示する。告知済み記録の寿命は本サービスの
/// 寿命（＝VM と同じ）で、旧実装のフィールドと同じセッション意味論を保つ。
/// </para>
/// </remarks>
internal sealed class DiagramExportService(
    IDiagramTransferHost host,
    IDialogService dialogs,
    IFileDialogService files
)
{
    /// <summary>出力形式ごとに「落ちる情報の告知」を済ませたかの記録（セッション中 1 回だけ内訳を見せるため）</summary>
    private readonly HashSet<DiagramExportFormat> _omissionNotifiedFormats = [];

    /// <summary>保存ダイアログで選択した形式に応じて ER 図を書き出す（失敗はエラーダイアログで報告する）</summary>
    /// <param name="visual">PNG 出力時に使用するキャンバスの Visual</param>
    public void Export(object? visual)
    {
        // 並び順は「画像 → DB 構築 → スキーマ交換（可逆な Schema JSON を先頭）→ 定義書」の用途グループ。
        // 標準ダイアログのフィルタは見出し行を持てないため、接頭辞（Image/Database/Schema/Document）で
        // グループを可視化する。先頭＝既定形式は PNG
        var picked = files.PickSaveFile(
            "Image - PNG (*.png)|*.png|Image - SVG (*.svg)|*.svg|Database - SQL Script (*.sql)|*.sql|Schema - JSON (*.json)|*.json|Schema - Mermaid (*.mmd)|*.mmd|Schema - Mermaid (*.mermaid)|*.mermaid|Schema - DBML (*.dbml)|*.dbml|Document - Excel Workbook (*.xlsx)|*.xlsx|Document - HTML (*.html)|*.html",
            ".png"
        );

        if (picked is null)
        {
            return;
        }

        var format = ResolveFormat(picked.Path, picked.FilterIndex);

        try
        {
            SaveDiagram(format, picked.Path, visual);
        }
        catch (Exception ex)
        {
            dialogs.ShowError(
                Strings.Export_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>ファイル拡張子を優先し、無ければフィルター選択から出力形式を判定する</summary>
    internal static DiagramExportFormat ResolveFormat(string path, int filterIndex)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".png" => DiagramExportFormat.Png,
            ".svg" => DiagramExportFormat.Svg,
            ".sql" => DiagramExportFormat.Sql,
            ".mmd" => DiagramExportFormat.Mermaid,
            ".mermaid" => DiagramExportFormat.Mermaid,
            ".dbml" => DiagramExportFormat.Dbml,
            ".xlsx" => DiagramExportFormat.Excel,
            ".html" => DiagramExportFormat.Html,
            ".htm" => DiagramExportFormat.Html,
            ".json" => DiagramExportFormat.SchemaJson,
            _ => filterIndex switch
            {
                1 => DiagramExportFormat.Png,
                2 => DiagramExportFormat.Svg,
                3 => DiagramExportFormat.Sql,
                4 => DiagramExportFormat.SchemaJson,
                5 => DiagramExportFormat.Mermaid,
                6 => DiagramExportFormat.Mermaid,
                7 => DiagramExportFormat.Dbml,
                8 => DiagramExportFormat.Excel,
                9 => DiagramExportFormat.Html,
                _ => throw new InvalidOperationException(Strings.Export_FormatUndetermined),
            },
        };
    }

    /// <summary>指定形式でダイアグラムをファイルへ書き出し、完了を通知する</summary>
    internal void SaveDiagram(DiagramExportFormat format, string path, object? visual)
    {
        var displayName = format switch
        {
            DiagramExportFormat.Png => Strings.Format_Png,
            DiagramExportFormat.Svg => Strings.Format_Svg,
            DiagramExportFormat.Sql => Strings.Format_SqlDdl,
            DiagramExportFormat.Mermaid => Strings.Format_Mermaid,
            DiagramExportFormat.Dbml => Strings.Format_Dbml,
            DiagramExportFormat.Excel => Strings.Format_DefinitionDocument,
            DiagramExportFormat.Html => Strings.Format_DefinitionDocumentHtml,
            DiagramExportFormat.SchemaJson => Strings.Format_SchemaJson,
            _ => Strings.Format_File,
        };

        // この形式では表現できず落ちた情報（Mermaid / DBML のみ検出する。他形式は常に空）
        IReadOnlyList<ExportOmissionKind> omissions = [];

        switch (format)
        {
            case DiagramExportFormat.Png:
                if (visual is not Visual pngVisual)
                {
                    throw new InvalidOperationException(Strings.Export_PngCanvasInfoMissing);
                }

                ImageExportService.ExportPng(pngVisual, path);
                break;

            case DiagramExportFormat.Svg:
                host.RenderSvg(path);
                break;

            case DiagramExportFormat.Sql:
                File.WriteAllText(
                    path,
                    host.CurrentProvider.DdlGenerator.Build(host.BuildModel()),
                    System.Text.Encoding.UTF8
                );
                break;

            case DiagramExportFormat.Mermaid:
                omissions = MermaidExporter.SaveTo(host.BuildModel(), path);
                break;

            case DiagramExportFormat.Dbml:
                omissions = DbmlExporter.SaveTo(host.BuildModel(), path);
                break;

            case DiagramExportFormat.Excel:
                TableDefinitionDocumentExporter.SaveTo(host.BuildModel(), path);
                break;

            case DiagramExportFormat.Html:
                TableDefinitionHtmlExporter.SaveTo(host.BuildModel(), path);
                break;

            case DiagramExportFormat.SchemaJson:
                // 配置情報（layout）を持たないスキーマのみ文書。Layout = null で保存すると
                // layout キー自体が出力されず、読込時に自動整列される可逆形式になる。
                // 保存ダイアログで既存ファイルを選べるため、原子的に差し替えて上書き破損を防ぐ
                JsonStorageService.SaveAtomic(
                    path,
                    new DiagramDocument { Schema = host.BuildModel(), Layout = null }
                );
                break;
        }

        NotifyExportCompleted(format, displayName, omissions);
    }

    /// <summary>出力完了を通知する（落ちた情報があれば、その形式で初回のときだけ内訳を添える）</summary>
    /// <remarks>
    /// Mermaid は NOT NULL 列がある限りほぼ必ず告知対象になるため、毎回内訳を出すと通知が形骸化する。
    /// 未対応方言のフォールバック警告と同じく、形式ごとに初回だけ見せる。
    /// 内訳の提示形式（要約＋詳細）は型変換警告と揃える
    /// </remarks>
    private void NotifyExportCompleted(
        DiagramExportFormat format,
        string displayName,
        IReadOnlyList<ExportOmissionKind> omissions
    )
    {
        var completed = string.Format(Strings.Export_Completed, displayName);

        // 落ちた情報が無い、またはこの形式では既に告知済み（Add が false）なら完了文だけを出す
        if (omissions.Count == 0 || !_omissionNotifiedFormats.Add(format))
        {
            dialogs.ShowInformation(completed, Strings.Common_Complete);
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            omissions.Select(kind =>
                string.Format(Strings.ExportOmission_Line, DescribeOmission(kind))
            )
        );

        dialogs.ShowInformationDetails(
            completed + Environment.NewLine + Environment.NewLine + Strings.Export_OmissionsHeader,
            details,
            Strings.Common_Complete
        );
    }

    /// <summary>落ちた情報の種類を表示文言へ変換する</summary>
    private static string DescribeOmission(ExportOmissionKind kind) =>
        kind switch
        {
            ExportOmissionKind.TableDescription => Strings.ExportOmission_TableDescription,
            ExportOmissionKind.TableMemo => Strings.ExportOmission_TableMemo,
            ExportOmissionKind.ColumnDescription => Strings.ExportOmission_ColumnDescription,
            ExportOmissionKind.ColumnNullability => Strings.ExportOmission_ColumnNullability,
            ExportOmissionKind.CompositeUniqueConstraint =>
                Strings.ExportOmission_CompositeUniqueConstraint,
            ExportOmissionKind.UniqueConstraintName => Strings.ExportOmission_UniqueConstraintName,
            ExportOmissionKind.ForeignKeyColumnPairs =>
                Strings.ExportOmission_ForeignKeyColumnPairs,
            ExportOmissionKind.ReferentialAction => Strings.ExportOmission_ReferentialAction,
            ExportOmissionKind.NamedQuery => Strings.ExportOmission_NamedQuery,
            _ => kind.ToString(),
        };
}
