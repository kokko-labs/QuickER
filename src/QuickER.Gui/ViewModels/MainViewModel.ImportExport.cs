using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.Documents;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;
using QuickER.Services;

namespace QuickER.ViewModels;

/// <summary>ER 図のエクスポート形式</summary>
internal enum DiagramExportFormat
{
    /// <summary>PNG 画像</summary>
    Png,

    /// <summary>SVG 画像</summary>
    Svg,

    /// <summary>SQL（DDL）スクリプト</summary>
    Sql,

    /// <summary>Mermaid 記法</summary>
    Mermaid,

    /// <summary>DBML 記法</summary>
    Dbml,

    /// <summary>Excel テーブル定義書</summary>
    Excel,

    /// <summary>HTML テーブル定義書</summary>
    Html,
}

/// <summary>ER 図のインポート形式</summary>
internal enum DiagramImportFormat
{
    /// <summary>Mermaid 記法</summary>
    Mermaid,

    /// <summary>DBML 記法</summary>
    Dbml,

    /// <summary>Excel テーブル定義書</summary>
    Excel,
}

/// <summary>MainViewModel の入出力機能を担う partial クラス</summary>
/// <remarks>
/// 自動保存・復元、各種フォーマットのエクスポート・インポート、SQL Server 連携、
/// AI 生成、C# コード生成のコマンドを担当する
/// </remarks>
public partial class MainViewModel
{
    // ---------------- Auto-save / restore ----------------

    /// <summary>最後に保存／読込した JSON のファイル名（拡張子なし）</summary>
    /// <remarks>
    /// ウィンドウタイトルと印刷ダイアログのタイトル入力欄の初期値に使用する。
    /// 保存フォーマット・Undo 履歴には一切関与しない（未保存のときは null）
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _lastDocumentFileName;

    /// <summary>ウィンドウタイトル（読込／保存済みなら「ファイル名 - QuickER」、未保存なら「QuickER」）</summary>
    public string WindowTitle =>
        string.IsNullOrEmpty(LastDocumentFileName)
            ? "QuickER"
            : $"{LastDocumentFileName} - QuickER";

    /// <summary>ダイアグラム自動保存ファイルのパス</summary>
    private static readonly string AutoSavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickER",
        "last_diagram.json"
    );

    /// <summary>GUI 全体設定（UI 表示状態を含む）を gui-settings.json へ永続化するストア</summary>
    private readonly GuiAppSettingsStore _guiSettingsStore = new();

    /// <summary>現在のダイアグラムと UI 表示状態を自動保存ファイルへ書き出す</summary>
    public void AutoSave()
    {
        try
        {
            var dir = Path.GetDirectoryName(AutoSavePath)!;
            Directory.CreateDirectory(dir);
            JsonStorageService.Save(AutoSavePath, ToDocument());

            // UI 表示状態は GUI 全体設定の 1 セクション。他のセクション（言語など）を消さないよう
            // Load → 該当セクションのみ差し替え → Save の read-modify-write で書き込む
            var settings = _guiSettingsStore.Load();
            settings.DiagramView = new DiagramViewSettings
            {
                ShowColumnDescriptions = ShowColumnDescriptionsInDiagram,
                ShowNullability = ShowNullabilityInDiagram,
                IsCompactView = IsCompactViewInDiagram,
            };
            _guiSettingsStore.Save(settings);
        }
        catch
        {
            // 自動保存の失敗は操作を妨げないため無視する
        }
    }

    /// <summary>起動時に前回の自動保存ファイルから UI 状態とダイアグラムを復元する</summary>
    private void RestoreLastDiagram()
    {
        // UI 表示状態を GUI 全体設定から反映する（ファイル無し・破損時は既定値が返り、
        // その既定値は VM 側の初期値と一致するため常時反映しても挙動は変わらない）
        var diagramView = _guiSettingsStore.Load().DiagramView;
        ShowColumnDescriptionsInDiagram = diagramView.ShowColumnDescriptions;
        ShowNullabilityInDiagram = diagramView.ShowNullability;
        IsCompactViewInDiagram = diagramView.IsCompactView;

        if (!File.Exists(AutoSavePath))
        {
            return;
        }

        try
        {
            var document = JsonStorageService.Load(AutoSavePath);

            SetCurrentProviderFromDbms(document.Schema.TargetDbms);
            ReplaceDiagram(
                document.Schema.Entities,
                document.Schema.Relationships,
                clearUndoHistory: true,
                document.Layout,
                document.Schema.Queries
            );
        }
        catch
        {
            // 復元失敗時は空のダイアグラムで起動する
        }
    }

    // ---------------- Export ----------------

    /// <summary>保存ダイアログで選択した形式に応じて ER 図を書き出す</summary>
    /// <param name="visual">PNG 出力時に使用するキャンバスの Visual</param>
    [RelayCommand]
    private void ExportDiagram(object? visual)
    {
        var picked = _files.PickSaveFile(
            "PNG Image (*.png)|*.png|SVG Image (*.svg)|*.svg|SQL Script (*.sql)|*.sql|Mermaid Diagram (*.mmd)|*.mmd|Mermaid Diagram (*.mermaid)|*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx|HTML Document (*.html)|*.html",
            ".png"
        );

        if (picked is null)
        {
            return;
        }

        var format = GetExportFormat(picked.Path, picked.FilterIndex);

        try
        {
            SaveDiagram(format, picked.Path, visual);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Export_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>図全体を用紙 1 ページへ印刷する（縮小フィット／原寸大を選択）</summary>
    /// <remarks>
    /// 図はキャンバスの Visual を写すのではなく、VM から直接ベクタ描画する
    /// （<see cref="DiagramPrintService"/> → <see cref="DiagramVectorRenderer"/>）。
    /// 選択枠・減光など画面状態の影響を受けないため、キャンバス参照の受け渡しや
    /// IsSelected / IsDimmed のスナップショット・復元は不要
    /// </remarks>
    [RelayCommand]
    private void PrintDiagram()
    {
        // 印刷オプション（サイズモード・タイトル・日時印字）を選択させる。キャンセル時は何もしない
        // タイトル欄の初期値には最後に保存／読込した文書名を提示する
        var options = _appDialogs.ShowPrintOptionsDialog(LastDocumentFileName);

        if (options is null)
        {
            return;
        }

        try
        {
            DiagramPrintService.Print(
                this,
                options.Title,
                options.IncludeTimestamp,
                options.SizeMode
            );
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Print_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>現在の ER 図を意味モデル（<see cref="ErDiagram"/>・視覚情報なし）へ変換する</summary>
    /// <remarks>名前付きクエリ定義（<see cref="Queries"/>）も保存単位として含める</remarks>
    public ErDiagram ToDiagramModel() =>
        new()
        {
            Entities = Entities.Select(entity => entity.ToModel()).ToList(),
            Relationships = Relationships.Select(relationship => relationship.ToModel()).ToList(),
            TargetDbms = CurrentProvider.Name,
            Queries = Queries,
        };

    /// <summary>現在の ER 図を保存文書（意味モデル＋レイアウトサイドカー）へ変換する</summary>
    public DiagramDocument ToDocument() =>
        new()
        {
            Schema = ToDiagramModel(),
            Layout = Entities.ToDictionary(entity => entity.Id, entity => entity.ToLayout()),
        };

    /// <summary>指定スキーマが現在のダイアグラムと構造的に同一かを署名比較で判定する</summary>
    private bool HasSameStructure(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var current = ToDiagramModel();
        var currentSignature = SchemaSignature.Compute(current.Entities, current.Relationships);
        var newSignature = SchemaSignature.Compute(entities, relationships);

        return currentSignature == newSignature;
    }

    /// <summary>構造変更を伴う置換の場合のみ確認ダイアログを表示する</summary>
    /// <remarks>空の図、または構造が同一の場合は確認なしで続行する</remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships,
        string message
    )
    {
        if (Entities.Count == 0 || HasSameStructure(entities, relationships))
        {
            return true;
        }

        return _dialogs.Confirm(message, Strings.Common_Confirm);
    }

    /// <summary>ファイル選択ダイアログで選択したファイルの形式に応じて ER 図を取り込む</summary>
    [RelayCommand]
    private void ImportDiagram()
    {
        var picked = _files.PickOpenFile(
            "Mermaid Diagram (*.mmd;*.mermaid)|*.mmd;*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx"
        );

        if (picked is null)
        {
            return;
        }

        var format = GetImportFormat(picked.Path, picked.FilterIndex);

        try
        {
            ImportDiagramFile(format, picked.Path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Import_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>指定形式でダイアグラムをファイルへ書き出し、完了を通知する</summary>
    private void SaveDiagram(DiagramExportFormat format, string path, object? visual)
    {
        var displayName = format switch
        {
            DiagramExportFormat.Png => Strings.ExportFormat_Png,
            DiagramExportFormat.Svg => Strings.ExportFormat_Svg,
            DiagramExportFormat.Sql => "SQL DDL",
            DiagramExportFormat.Mermaid => "Mermaid",
            DiagramExportFormat.Dbml => "DBML",
            DiagramExportFormat.Excel => Strings.Format_DefinitionDocument,
            DiagramExportFormat.Html => Strings.Format_DefinitionDocumentHtml,
            _ => Strings.Format_File,
        };

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
                ImageExportService.ExportSvg(this, path);
                break;

            case DiagramExportFormat.Sql:
                File.WriteAllText(
                    path,
                    CurrentProvider.DdlGenerator.Build(ToDiagramModel()),
                    System.Text.Encoding.UTF8
                );
                break;

            case DiagramExportFormat.Mermaid:
                MermaidExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.Dbml:
                DbmlExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.Excel:
                TableDefinitionDocumentExporter.SaveTo(ToDiagramModel(), path);
                break;

            case DiagramExportFormat.Html:
                TableDefinitionHtmlExporter.SaveTo(ToDiagramModel(), path);
                break;
        }

        _dialogs.ShowInformation(
            string.Format(Strings.Export_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>指定形式のダイアグラムファイルを読み込み、確認のうえ現在の図を置換する</summary>
    private void ImportDiagramFile(DiagramImportFormat format, string path)
    {
        var diagram = format switch
        {
            DiagramImportFormat.Mermaid => MermaidImporter.Load(path),
            DiagramImportFormat.Dbml => DbmlImporter.Load(path),
            DiagramImportFormat.Excel => TableDefinitionDocumentImporter.Load(path),
            _ => throw new InvalidOperationException(Strings.Import_UnsupportedFormat),
        };

        var displayName = format switch
        {
            DiagramImportFormat.Mermaid => "Mermaid",
            DiagramImportFormat.Dbml => "DBML",
            DiagramImportFormat.Excel => Strings.Format_DefinitionDocument,
            _ => Strings.Format_File,
        };

        if (
            !ConfirmDiagramReplacement(
                diagram.Entities,
                diagram.Relationships,
                string.Format(Strings.Import_ReplaceConfirm, displayName)
            )
        )
        {
            return;
        }

        // Excel 定義書は対象 DBMS を保持しているため方言も復元する
        // （Mermaid / DBML は方言情報を持たないため現在のプロバイダを維持する）
        if (format == DiagramImportFormat.Excel)
        {
            SetCurrentProviderFromDbms(diagram.TargetDbms);
        }

        ReplaceDiagramWithoutHistory(diagram.Entities, diagram.Relationships, autoLayout: true);
        _dialogs.ShowInformation(
            string.Format(Strings.Import_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>ファイル拡張子を優先し、無ければフィルター選択から出力形式を判定する</summary>
    private static DiagramExportFormat GetExportFormat(string path, int filterIndex)
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
            _ => filterIndex switch
            {
                1 => DiagramExportFormat.Png,
                2 => DiagramExportFormat.Svg,
                3 => DiagramExportFormat.Sql,
                4 => DiagramExportFormat.Mermaid,
                5 => DiagramExportFormat.Mermaid,
                6 => DiagramExportFormat.Dbml,
                7 => DiagramExportFormat.Excel,
                8 => DiagramExportFormat.Html,
                _ => throw new InvalidOperationException(Strings.Export_FormatUndetermined),
            },
        };
    }

    /// <summary>ファイル拡張子を優先し、無ければフィルター選択から取込形式を判定する</summary>
    private static DiagramImportFormat GetImportFormat(string path, int filterIndex)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".mmd" => DiagramImportFormat.Mermaid,
            ".mermaid" => DiagramImportFormat.Mermaid,
            ".dbml" => DiagramImportFormat.Dbml,
            ".xlsx" => DiagramImportFormat.Excel,
            _ => filterIndex switch
            {
                1 => DiagramImportFormat.Mermaid,
                2 => DiagramImportFormat.Dbml,
                3 => DiagramImportFormat.Excel,
                _ => throw new InvalidOperationException(Strings.Import_FormatUndetermined),
            },
        };
    }

    // ---------------- Save / Load ----------------

    /// <summary>保存ダイアログでパスを選び、現在のダイアグラムを JSON 形式で保存する</summary>
    [RelayCommand]
    private void Save()
    {
        var picked = _files.PickSaveFile("ER Diagram (*.json)|*.json", ".json");

        if (picked is not null)
        {
            JsonStorageService.Save(picked.Path, ToDocument());

            // ウィンドウタイトル・印刷ダイアログのタイトル初期値用。保存フォーマット・Undo には関与しない
            LastDocumentFileName = Path.GetFileNameWithoutExtension(picked.Path);
        }
    }

    /// <summary>JSON ファイルからダイアグラムを読み込み、現在の図と置換する（ダイアログ表示）</summary>
    [RelayCommand]
    private void Open()
    {
        var picked = _files.PickOpenFile("ER Diagram (*.json)|*.json");

        if (picked is null)
        {
            return;
        }

        var document = JsonStorageService.Load(picked.Path);

        // 新しいフォーマットの文書は未対応のデータが失われる可能性があるため、開く前に確認する
        if (document.IsNewerFormat)
        {
            var message = string.Format(
                Strings.Confirm_NewerDocumentFormat,
                document.Version,
                DiagramDocument.CurrentVersion
            );

            if (!_dialogs.ConfirmWarning(message, Strings.Common_Confirm))
            {
                return;
            }
        }

        SetCurrentProviderFromDbms(document.Schema.TargetDbms);
        ReplaceDiagram(
            document.Schema.Entities,
            document.Schema.Relationships,
            clearUndoHistory: true,
            document.Layout,
            document.Schema.Queries
        );

        // ウィンドウタイトル・印刷ダイアログのタイトル初期値用。保存フォーマット・Undo には関与しない
        LastDocumentFileName = Path.GetFileNameWithoutExtension(picked.Path);
    }
}
