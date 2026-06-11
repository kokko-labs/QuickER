using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using ERDesigner.Generator;
using ERDesigner.Models;
using ERDesigner.Services;
using Microsoft.Win32;

namespace ERDesigner.ViewModels;

internal enum DiagramExportFormat
{
    Png,
    Svg,
    Sql,
    Mermaid,
    Dbml,
    Excel,
}

internal enum DiagramImportFormat
{
    Mermaid,
    Dbml,
    Excel,
}

/// <summary>
/// MainViewModel の入出力機能 (partial)。
/// 自動保存/復元、各種フォーマットのエクスポート・インポート、SQL Server 連携、
/// AI 生成、C# コード生成のコマンドを担当します。
/// </summary>
public partial class MainViewModel
{
    // ---------------- Auto-save / restore ----------------

    private static readonly string AutoSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner", "last_diagram.json");
    private static readonly string UiStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ERDesigner", "ui_state.json");

    /// <summary>現在のダイアグラムを自動保存ファイルに書き出します。</summary>
    public void AutoSave()
    {
        try
        {
            var dir = Path.GetDirectoryName(AutoSavePath)!;
            Directory.CreateDirectory(dir);
            JsonStorageService.Save(AutoSavePath, this);
            File.WriteAllText(
                UiStatePath,
                System.Text.Json.JsonSerializer.Serialize(
                    new UiState { ShowColumnDescriptionsInDiagram = ShowColumnDescriptionsInDiagram, ShowNullabilityInDiagram = ShowNullabilityInDiagram }
                )
            );
        }
        catch
        { /* 自動保存の失敗は無視 */
        }
    }

    /// <summary>起動時に前回の自動保存ファイルを復元します。</summary>
    private void RestoreLastDiagram()
    {
        try
        {
            if (File.Exists(UiStatePath))
            {
                var uiState = System.Text.Json.JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiStatePath));

                if (uiState is not null)
                {
                    ShowColumnDescriptionsInDiagram = uiState.ShowColumnDescriptionsInDiagram;
                    ShowNullabilityInDiagram = uiState.ShowNullabilityInDiagram;
                }
            }
        }
        catch { }

        if (!File.Exists(AutoSavePath))
        {
            return;
        }

        try
        {
            var diagram = JsonStorageService.Load(AutoSavePath);

            ReplaceDiagram(diagram.Entities, diagram.Relationships, clearUndoHistory: true);
        }
        catch
        { /* 復元失敗時は空で起動 */
        }
    }

    // ---------------- Export ----------------

    /// <summary>
    /// 保存ダイアログで選択した形式に応じて ER 図を書き出します。
    /// </summary>
    /// <param name="visual">PNG 出力時に使用するキャンバスの Visual。</param>
    [RelayCommand]
    private void ExportDiagram(object? visual)
    {
        var dlg = new SaveFileDialog
        {
            Filter =
                "PNG Image (*.png)|*.png|SVG Image (*.svg)|*.svg|SQL Script (*.sql)|*.sql|Mermaid Diagram (*.mmd)|*.mmd|Mermaid Diagram (*.mermaid)|*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".png",
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var format = GetExportFormat(dlg.FileName, dlg.FilterIndex);

        try
        {
            SaveDiagram(format, dlg.FileName, visual);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"出力できませんでした。{Environment.NewLine}{ex.Message}", "エラー");
        }
    }

    /// <summary>現在の ER 図から C# の Entity / EditModel / Mapper コードを生成します。</summary>
    [RelayCommand]
    private void GenerateCSharpCode()
    {
        var dialog = new Views.CSharpGenerationDialog(CSharpGenerationNamespace, "ErDesignerEntities.g.cs") { Owner = Application.Current?.MainWindow };

        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null)
        {
            return;
        }

        try
        {
            CSharpGenerationNamespace = dialog.ViewModel.Result.NamespaceName;
            var service = new CSharpCodeGenerationService();
            var options = new CodeGenerationOptions
            {
                NamespaceName = string.IsNullOrWhiteSpace(CSharpGenerationNamespace) ? DefaultCSharpNamespace : CSharpGenerationNamespace.Trim(),
                OutputFileName = Path.GetFileName(dialog.ViewModel.Result.OutputFilePath),
                GenerateEntityClasses = dialog.ViewModel.Result.GenerateEntityClasses,
                GenerateEditModels = dialog.ViewModel.Result.GenerateEditModels,
                GenerateMappers = dialog.ViewModel.Result.GenerateMappers,
                GenerateRepositories = dialog.ViewModel.Result.GenerateRepositories,
            };
            var result = service.Generate(ToGeneratorDiagram(), options);

            if (result.HasErrors)
            {
                _dialogs.ShowError(BuildGenerationDiagnosticsMessage(result), "C# 生成エラー");
                return;
            }

            var writer = new GeneratedFileWriter();
            writer.WriteFiles(Path.GetDirectoryName(dialog.ViewModel.Result.OutputFilePath) ?? Environment.CurrentDirectory, result);

            var diagnostics = BuildGenerationDiagnosticsMessage(result);
            var message = string.IsNullOrWhiteSpace(diagnostics)
                ? "C# コードの生成が完了しました。"
                : $"C# コードの生成が完了しました。{Environment.NewLine}{Environment.NewLine}{diagnostics}";
            _dialogs.ShowInformation(message, "完了");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"C# コードを生成できませんでした。{Environment.NewLine}{ex.Message}", "エラー");
        }
    }

    private DiagramDefinition ToGeneratorDiagram() =>
        new()
        {
            Entities = Entities
                .Select(entity => new EntityDefinition
                {
                    Id = entity.Id,
                    TableName = entity.TableName,
                    Columns = entity
                        .Columns.Select(column => new ColumnDefinition
                        {
                            Id = column.Id,
                            Name = column.Name,
                            DataType = column.DataType,
                            IsPrimaryKey = column.IsPrimaryKey,
                            IsForeignKey = column.IsForeignKey,
                            IsNullable = column.IsNullable,
                        })
                        .ToList(),
                })
                .ToList(),
            Relationships = Relationships
                .Select(relationship => new RelationshipDefinition
                {
                    Id = relationship.Id,
                    SourceEntityId = relationship.Source.Id,
                    TargetEntityId = relationship.Target.Id,
                    Type = relationship.Type switch
                    {
                        RelationshipType.OneToOne => RelationshipMultiplicity.OneToOne,
                        RelationshipType.OneToMany => RelationshipMultiplicity.OneToMany,
                        RelationshipType.ManyToMany => RelationshipMultiplicity.ManyToMany,
                        _ => RelationshipMultiplicity.OneToMany,
                    },
                    SourceColumnId = relationship.SourceColumnId,
                    TargetColumnId = relationship.TargetColumnId,
                })
                .ToList(),
        };

    private static string BuildGenerationDiagnosticsMessage(CodeGenerationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => $"[{diagnostic.Severity}] {diagnostic.Message}"));

    /// <summary>現在の ER 図をシリアライズ可能なモデル (<see cref="ErDiagram"/>) へ変換します。</summary>
    private ErDiagram ToDiagramModel() =>
        new()
        {
            Entities = Entities.Select(entity => entity.ToModel()).ToList(),
            Relationships = Relationships.Select(relationship => relationship.ToModel()).ToList(),
        };

    /// <summary>指定スキーマが現在のダイアグラムと構造的に同一かを判定します。</summary>
    private bool HasSameStructure(IEnumerable<Entity> entities, IEnumerable<Relationship> relationships)
    {
        var current = ToDiagramModel();
        var currentSignature = SqlServerSchemaImporter.ComputeSignature(current.Entities, current.Relationships);
        var newSignature = SqlServerSchemaImporter.ComputeSignature(entities, relationships);

        return currentSignature == newSignature;
    }

    /// <summary>
    /// 構造変更を伴う置換の場合のみ確認ダイアログを表示します。
    /// </summary>
    /// <returns>置換を続行してよい場合 true。</returns>
    private bool ConfirmDiagramReplacement(IReadOnlyList<Entity> entities, IReadOnlyList<Relationship> relationships, string message)
    {
        if (Entities.Count == 0 || HasSameStructure(entities, relationships))
        {
            return true;
        }

        return _dialogs.Confirm(message, "確認");
    }

    /// <summary>
    /// ファイル選択ダイアログで選択した形式に応じて ER 図を取り込みます。
    /// </summary>
    [RelayCommand]
    private void ImportDiagram()
    {
        var dlg = new OpenFileDialog { Filter = "Mermaid Diagram (*.mmd;*.mermaid)|*.mmd;*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx" };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var format = GetImportFormat(dlg.FileName, dlg.FilterIndex);

        try
        {
            ImportDiagramFile(format, dlg.FileName);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"取り込めませんでした。{Environment.NewLine}{ex.Message}", "エラー");
        }
    }

    /// <summary>
    /// 指定形式でダイアグラムを書き出します。
    /// </summary>
    private void SaveDiagram(DiagramExportFormat format, string path, object? visual)
    {
        var displayName = format switch
        {
            DiagramExportFormat.Png => "PNG 画像",
            DiagramExportFormat.Svg => "SVG 画像",
            DiagramExportFormat.Sql => "SQL DDL",
            DiagramExportFormat.Mermaid => "Mermaid",
            DiagramExportFormat.Dbml => "DBML",
            DiagramExportFormat.Excel => "定義書",
            _ => "ファイル",
        };

        switch (format)
        {
            case DiagramExportFormat.Png:
                if (visual is not Visual pngVisual)
                {
                    throw new InvalidOperationException("PNG 出力に必要なキャンバス情報を取得できませんでした。");
                }

                ImageExportService.ExportPng(pngVisual, path);
                break;

            case DiagramExportFormat.Svg:
                ImageExportService.ExportSvg(this, path);
                break;

            case DiagramExportFormat.Sql:
                DdlExporter.SaveTo(this, path);
                break;

            case DiagramExportFormat.Mermaid:
                MermaidExporter.SaveTo(this, path);
                break;

            case DiagramExportFormat.Dbml:
                DbmlExporter.SaveTo(this, path);
                break;

            case DiagramExportFormat.Excel:
                TableDefinitionDocumentExporter.SaveTo(this, path);
                break;
        }

        _dialogs.ShowInformation($"{displayName}の出力が完了しました。", "完了");
    }

    /// <summary>
    /// 指定形式のダイアグラムファイルを読み込みます。
    /// </summary>
    private void ImportDiagramFile(DiagramImportFormat format, string path)
    {
        var diagram = format switch
        {
            DiagramImportFormat.Mermaid => MermaidImporter.Load(path),
            DiagramImportFormat.Dbml => DbmlImporter.Load(path),
            DiagramImportFormat.Excel => TableDefinitionDocumentImporter.Load(path),
            _ => throw new InvalidOperationException("未対応の取込形式です。"),
        };

        var displayName = format switch
        {
            DiagramImportFormat.Mermaid => "Mermaid",
            DiagramImportFormat.Dbml => "DBML",
            DiagramImportFormat.Excel => "定義書",
            _ => "ファイル",
        };

        if (!ConfirmDiagramReplacement(diagram.Entities, diagram.Relationships, $"現在のダイアグラムを{displayName}の内容で置換します。よろしいですか？"))
        {
            return;
        }

        ReplaceDiagramWithoutHistory(diagram.Entities, diagram.Relationships, autoLayout: true);
        _dialogs.ShowInformation($"{displayName}の取り込みが完了しました。", "完了");
    }

    /// <summary>
    /// 保存ファイル名またはフィルター選択から出力形式を判定します。
    /// </summary>
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
            _ => filterIndex switch
            {
                1 => DiagramExportFormat.Png,
                2 => DiagramExportFormat.Svg,
                3 => DiagramExportFormat.Sql,
                4 => DiagramExportFormat.Mermaid,
                5 => DiagramExportFormat.Mermaid,
                6 => DiagramExportFormat.Dbml,
                7 => DiagramExportFormat.Excel,
                _ => throw new InvalidOperationException("出力形式を判定できませんでした。"),
            },
        };
    }

    /// <summary>
    /// 読み込みファイル名またはフィルター選択から取込形式を判定します。
    /// </summary>
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
                _ => throw new InvalidOperationException("取込形式を判定できませんでした。"),
            },
        };
    }

    // ---------------- SQL Server 取込 ----------------

    /// <summary>SQL Server に接続してスキーマを取得し、ダイアグラムに反映します。</summary>
    [RelayCommand]
    private async Task ImportFromSqlServerAsync()
    {
        var dialog = new Views.SqlConnectionDialog { Owner = Application.Current?.MainWindow };

        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null)
        {
            return;
        }

        try
        {
            var importer = new SqlServerSchemaImporter();
            var result = await importer.ImportAsync(dialog.ViewModel.Result).ConfigureAwait(true);

            // 既存と差分があるかチェックして置換確認
            if (!ConfirmDiagramReplacement(result.Entities, result.Relationships, "現在のダイアグラムを取得結果で置換します。よろしいですか？"))
            {
                return;
            }

            // 取込結果の反映は履歴対象外にします。
            ReplaceDiagramWithoutHistory(result.Entities, result.Relationships, autoLayout: true);
        }
        catch (System.Exception ex)
        {
            _dialogs.ShowError("取り込みに失敗しました: " + ex.Message, "エラー");
        }
    }

    // ---------------- DB 書き込み (スキーマ同期) ----------------

    /// <summary>SQL Server に接続し、現在のダイアグラムとの差分を ALTER 文で書き戻します。</summary>
    [RelayCommand]
    private void SyncToSqlServer()
    {
        var connDlg = new Views.SqlConnectionDialog { Owner = Application.Current?.MainWindow, Title = "SQL Server へ同期" };

        if (connDlg.ShowDialog() != true || connDlg.ViewModel.Result is null)
        {
            return;
        }

        var target = ToDiagramModel();
        var vm = new SchemaSyncDialogViewModel(connDlg.ViewModel.Result, target.Entities, target.Relationships);
        var dlg = new Views.SchemaSyncDialog(vm) { Owner = Application.Current?.MainWindow };

        dlg.ShowDialog();
    }

    // ---------------- AI 生成 ----------------

    /// <summary>ChatGPT/Ollama にスキーマ生成を依頼し、ダイアグラムへ反映します。</summary>
    [RelayCommand]
    private void GenerateFromAi()
    {
        var dialog = new Views.AiGenerateDialog(ToDiagramModel()) { Owner = Application.Current?.MainWindow };

        if (dialog.ShowDialog() != true || dialog.ViewModel.Result is null)
        {
            return;
        }

        if (dialog.ViewModel.GenerationMode == AiGenerationMode.UpdateExisting)
        {
            ApplyAiUpdateResult(dialog.ViewModel.Result);
            return;
        }

        var (entities, relationships) = dialog.ViewModel.Result.ToDomain();

        if (entities.Count == 0)
        {
            _dialogs.ShowInformation("AI 応答にテーブルが含まれていませんでした。", "情報");
            return;
        }

        if (!ConfirmDiagramReplacement(entities, relationships, "現在のダイアグラムを AI 生成結果で置換します。よろしいですか？"))
        {
            return;
        }

        // AI 生成結果の反映は履歴対象外にします。
        ReplaceDiagramWithoutHistory(entities, relationships, autoLayout: true);
    }

    /// <summary>Codex App Server 対話ウィンドウのシングルトンインスタンスです。</summary>
    private Views.CodexAppServerDialog? _codexDialog;

    /// <summary>Codex App Server の接続設定ダイアログを開きます。</summary>
    [RelayCommand]
    private void OpenCodexAppServer()
    {
        if (_codexDialog is null)
        {
            _codexDialog = new Views.CodexAppServerDialog(this);
        }

        _codexDialog.Owner = null;
        _codexDialog.Show();
        _codexDialog.Activate();
    }

    /// <summary>アプリ終了時に Codex チャット画面を強制終了します。</summary>
    public void CloseCodexDialog()
    {
        _codexDialog?.ForceClose();
        _codexDialog = null;
    }

    /// <summary>AI が返した更新後スキーマを既存 ER 図へ反映します。</summary>
    private void ApplyAiUpdateResult(AiSchemaJson schema)
    {
        var (entities, relationships) = schema.ToDomain();

        if (entities.Count == 0)
        {
            _dialogs.ShowInformation("AI 応答にテーブルが含まれていませんでした。", "情報");
            return;
        }

        if (HasSameStructure(entities, relationships))
        {
            _dialogs.ShowInformation("AI 更新による変更はありませんでした。", "情報");
            return;
        }

        var currentDiagram = ToDiagramModel();
        var updatedDiagram = new ErDiagram { Entities = entities, Relationships = relationships };
        var diff = new AiUpdateDiffService().Compute(currentDiagram, updatedDiagram);
        var previewDialog = new Views.AiUpdatePreviewDialog(new AiUpdatePreviewDialogViewModel(diff)) { Owner = Application.Current?.MainWindow };

        if (previewDialog.ShowDialog() != true)
        {
            return;
        }

        ReplaceDiagramWithoutHistory(entities, relationships, autoLayout: true);
        ApplyRelationshipColumnRules();
    }

    // ---------------- Save / Load ----------------

    [RelayCommand]
    private void Save()
    {
        var dlg = new SaveFileDialog { Filter = "ER Diagram (*.json)|*.json", DefaultExt = ".json" };

        if (dlg.ShowDialog() == true)
        {
            JsonStorageService.Save(dlg.FileName, this);
        }
    }

    /// <summary>JSON ファイルからダイアグラムを読み込みます（ダイアログ表示）。</summary>
    [RelayCommand]
    private void Open()
    {
        var dlg = new OpenFileDialog { Filter = "ER Diagram (*.json)|*.json" };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var diagram = JsonStorageService.Load(dlg.FileName);

        ReplaceDiagram(diagram.Entities, diagram.Relationships, clearUndoHistory: true);
    }

    private sealed class UiState
    {
        public bool ShowColumnDescriptionsInDiagram { get; init; }

        public bool ShowNullabilityInDiagram { get; init; } = true;
    }
}
