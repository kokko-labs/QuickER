using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickER.CodeGen.CSharp;
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

    /// <summary>UI 表示状態の保存ファイルのパス</summary>
    private static readonly string UiStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "QuickER",
        "ui_state.json"
    );

    /// <summary>現在のダイアグラムと UI 表示状態を自動保存ファイルへ書き出す</summary>
    public void AutoSave()
    {
        try
        {
            var dir = Path.GetDirectoryName(AutoSavePath)!;
            Directory.CreateDirectory(dir);
            JsonStorageService.Save(AutoSavePath, ToDocument());
            File.WriteAllText(
                UiStatePath,
                System.Text.Json.JsonSerializer.Serialize(
                    new UiState
                    {
                        ShowColumnDescriptionsInDiagram = ShowColumnDescriptionsInDiagram,
                        ShowNullabilityInDiagram = ShowNullabilityInDiagram,
                        IsCompactViewInDiagram = IsCompactViewInDiagram,
                    }
                )
            );
        }
        catch
        {
            // 自動保存の失敗は操作を妨げないため無視する
        }
    }

    /// <summary>起動時に前回の自動保存ファイルから UI 状態とダイアグラムを復元する</summary>
    private void RestoreLastDiagram()
    {
        try
        {
            if (File.Exists(UiStatePath))
            {
                var uiState = System.Text.Json.JsonSerializer.Deserialize<UiState>(
                    File.ReadAllText(UiStatePath)
                );

                if (uiState is not null)
                {
                    ShowColumnDescriptionsInDiagram = uiState.ShowColumnDescriptionsInDiagram;
                    ShowNullabilityInDiagram = uiState.ShowNullabilityInDiagram;
                    IsCompactViewInDiagram = uiState.IsCompactViewInDiagram;
                }
            }
        }
        catch
        {
            // UI 状態の復元失敗は致命的でないため無視する
        }

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
            "PNG Image (*.png)|*.png|SVG Image (*.svg)|*.svg|SQL Script (*.sql)|*.sql|Mermaid Diagram (*.mmd)|*.mmd|Mermaid Diagram (*.mermaid)|*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx",
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

    /// <summary>名前付きクエリ定義エディタを開き、確定結果で現在の図のクエリを置き換える</summary>
    /// <remarks>
    /// 編集はダイアログ側の複製に対して行われ、OK 確定時のみ結果が返る（キャンセルは無影響）。
    /// 置換後は他の変更経路と同様に自動保存へ反映する。
    /// </remarks>
    [RelayCommand]
    private void OpenQueryDefinitions()
    {
        var result = _appDialogs.ShowQueryDefinitionDialog(ToDiagramModel());

        if (result is null)
        {
            return;
        }

        Queries = result;
        AutoSave();
    }

    /// <summary>現在の ER 図から C# の Entity / EditModel / Mapper / Repository コードを生成する</summary>
    [RelayCommand]
    private void GenerateCSharpCode()
    {
        var dialogResult = _appDialogs.ShowCSharpGenerationDialog(CurrentProvider);

        if (dialogResult is null)
        {
            return;
        }

        try
        {
            var options = dialogResult.Options;
            // 型解決（プロバイダ）→生成（Generator）の結合点は共有ファサードに集約し、CLI とドリフトさせない。
            // 自作 Repository の実効方言ごとに、レジストリから方言別の型マッパを解決して渡す
            // （マルチ方言時は各方言バケットをその方言の型で解決し、単一方言時も同一経路で挙動は変わらない）。
            var diagram = ToDiagramModel();
            var dialectMappers = ResolveDialectTypeMappers(options);
            var result = DiagramCodeGenerator.Generate(
                CurrentProvider.TypeMapper,
                CurrentProvider.TypeCatalog,
                dialectMappers,
                diagram,
                options
            );

            if (result.HasErrors)
            {
                _dialogs.ShowError(
                    BuildGenerationDiagnosticsMessage(result),
                    Strings.Csharp_GenerationErrorTitle
                );
                return;
            }

            // 値オブジェクト生成時に警告（定義競合など）がある場合は、内容を提示して続行可否を確認する
            var warnings = result
                .Diagnostics.Where(diagnostic =>
                    diagnostic.Severity == GenerationDiagnosticSeverity.Warning
                )
                .ToList();
            if (options.GenerateValueObjects && warnings.Count > 0)
            {
                var warningMessage = string.Join(
                    Environment.NewLine,
                    warnings.Select(diagnostic =>
                        string.Format(Strings.Csharp_WarningLine, diagnostic.Message)
                    )
                );
                var confirmed = _dialogs.Confirm(
                    Strings.Csharp_WarningIntro
                        + Environment.NewLine
                        + Environment.NewLine
                        + warningMessage
                        + Environment.NewLine
                        + Environment.NewLine
                        + Strings.Csharp_WarningPrompt,
                    Strings.Csharp_WarningTitle
                );
                if (!confirmed)
                {
                    return;
                }
            }

            var writer = new GeneratedFileWriter();
            writer.WriteFiles(
                string.IsNullOrWhiteSpace(dialogResult.OutputDirectory)
                    ? Environment.CurrentDirectory
                    : dialogResult.OutputDirectory,
                result
            );

            var diagnostics = BuildGenerationDiagnosticsMessage(result);
            var message = string.IsNullOrWhiteSpace(diagnostics)
                ? Strings.Csharp_GeneratedSuccess
                : Strings.Csharp_GeneratedSuccess
                    + Environment.NewLine
                    + Environment.NewLine
                    + diagnostics;

            // パッケージ参照モードのときは、必要な PackageReference をコピー可能な形で続けて提示する
            // （メッセージボックスの本文はドラッグ選択でコピーできるため、新規ダイアログは設けない）
            if (options.UseRuntimePackages)
            {
                var guidance = string.Join(
                    Environment.NewLine,
                    RuntimePackageReferenceGuidance.BuildGuidanceLines(
                        options,
                        RuntimePackages.ResolveGuidanceVersion()
                    )
                );
                message += $"{Environment.NewLine}{Environment.NewLine}{guidance}";
            }

            _dialogs.ShowInformation(message, Strings.Common_Complete);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                Strings.Csharp_GenerationFailed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>
    /// 自作 Repository の実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）ごとに、
    /// プロバイダレジストリから方言別の型マッパを解決する。
    /// </summary>
    /// <remarks>
    /// レジストリに存在しない方言名は除外し（<see cref="DiagramCodeGenerator"/> 側で図の方言の辞書へ代替される）、
    /// 単一方言時も同じ経路を通るため挙動は変わらない。
    /// </remarks>
    private IReadOnlyDictionary<string, IColumnTypeMapper> ResolveDialectTypeMappers(
        CodeGenerationOptions options
    )
    {
        var mappers = new Dictionary<string, IColumnTypeMapper>(StringComparer.OrdinalIgnoreCase);

        foreach (var dialect in options.EffectiveRepositoryDialects)
        {
            if (_providers.TryGet(dialect, out var provider))
            {
                mappers[dialect] = provider.TypeMapper;
            }
        }

        return mappers;
    }

    /// <summary>コード生成の診断（警告・エラー）を 1 つのメッセージ文字列へ整形する</summary>
    private static string BuildGenerationDiagnosticsMessage(CodeGenerationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(diagnostic => $"[{diagnostic.Severity}] {diagnostic.Message}")
        );

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

        ReplaceDiagramWithoutHistory(diagram.Entities, diagram.Relationships, autoLayout: true);
        _dialogs.ShowInformation(
            string.Format(Strings.Import_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>
    /// 保存ファイル名またはフィルター選択から出力形式を判定します。
    /// </summary>
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
            _ => filterIndex switch
            {
                1 => DiagramExportFormat.Png,
                2 => DiagramExportFormat.Svg,
                3 => DiagramExportFormat.Sql,
                4 => DiagramExportFormat.Mermaid,
                5 => DiagramExportFormat.Mermaid,
                6 => DiagramExportFormat.Dbml,
                7 => DiagramExportFormat.Excel,
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

    // ---------------- データベースから取込 ----------------

    /// <summary>データベースへ接続してスキーマを取得し、確認のうえダイアグラムへ反映する</summary>
    /// <remarks>取込ダイアログでは DBMS を選択でき、取込成功時に図の TargetDbms を選択方言へ設定する</remarks>
    [RelayCommand]
    private async Task ImportFromDatabaseAsync()
    {
        var picked = _appDialogs.ShowDbConnectionDialog(
            DbConnectionDialogMode.Import,
            fixedProvider: CurrentProvider,
            title: Strings.Db_ImportTitle
        );

        if (picked is null)
        {
            return;
        }

        try
        {
            var connectionString = picked.Provider.BuildConnectionString(picked.Settings);
            var result = await picked
                .Provider.SchemaImporter.ImportAsync(connectionString)
                .ConfigureAwait(true);

            // 構造差分がある場合のみ置換確認を行う
            if (
                !ConfirmDiagramReplacement(
                    result.Entities,
                    result.Relationships,
                    Strings.Db_ImportReplaceConfirm
                )
            )
            {
                return;
            }

            // 取込先の方言を図の TargetDbms として採用する
            SetCurrentProviderFromDbms(picked.Provider.Name);

            // DB 取込はインポート扱いとし、Undo 履歴へは積まない
            ReplaceDiagramWithoutHistory(result.Entities, result.Relationships, autoLayout: true);
        }
        catch (System.Exception ex)
        {
            _dialogs.ShowError(
                string.Format(Strings.Db_ImportFailed, ex.Message),
                Strings.Common_Error
            );
        }
    }

    // ---------------- DB 書き込み (スキーマ同期) ----------------

    /// <summary>DB 同期に未対応な方言（SQLite）のとき同期ボタンへ表示する理由メッセージ</summary>
    private static string SyncUnsupportedTooltip => Strings.Db_SyncSqliteUnsupported;

    /// <summary>DB 同期ボタンのツールチップ（未対応方言のときは理由、対応方言のときは通常の説明）</summary>
    /// <remarks>方言切替で <see cref="RaiseProviderChanged"/> から変更通知される</remarks>
    public string SyncToDatabaseTooltip =>
        CanSyncToDatabase ? Strings.Db_SyncWriteBack : SyncUnsupportedTooltip;

    /// <summary>DB 同期を実行できるか（SQLite は同期未対応のため実行不可）</summary>
    private bool CanSyncToDatabase =>
        CurrentProvider.Name != QuickER.Sqlite.SqliteProvider.ProviderName;

    /// <summary>データベースへ接続し、現在のダイアグラムとの差分同期ダイアログを開く</summary>
    /// <remarks>同期先の方言は図の TargetDbms に固定する（接続ダイアログでは DBMS を選択できない）</remarks>
    [RelayCommand(CanExecute = nameof(CanSyncToDatabase))]
    private void SyncToDatabase()
    {
        var picked = _appDialogs.ShowDbConnectionDialog(
            DbConnectionDialogMode.Sync,
            fixedProvider: CurrentProvider,
            title: Strings.Db_SyncTitle
        );

        if (picked is null)
        {
            return;
        }

        var target = ToDiagramModel();
        _appDialogs.ShowSchemaSyncDialog(
            picked.Provider,
            picked.Settings,
            target.Entities,
            target.Relationships
        );
    }

    // ---------------- AI チャット ----------------

    /// <summary>AI チャットウィンドウを開く（既存があれば再利用する）</summary>
    [RelayCommand]
    private void OpenAiChat() => _aiChat.Open(this);

    /// <summary>アプリ終了時に AI チャット画面を強制終了する</summary>
    public void CloseAiChatDialog() => _aiChat.Close();

    /// <summary>AI モック生成ウィンドウを開く（既存があれば再利用する）</summary>
    [RelayCommand]
    private void OpenMockGeneration() => _mockGeneration.Open(this);

    /// <summary>アプリ終了時に AI モック生成画面を強制終了する</summary>
    public void CloseMockGenerationDialog() => _mockGeneration.Close();

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

    /// <summary>自動保存対象の UI 表示状態（ダイアグラム上の表示トグル）</summary>
    private sealed class UiState
    {
        /// <summary>ダイアグラム上にカラム説明を表示するかどうか</summary>
        public bool ShowColumnDescriptionsInDiagram { get; init; }

        /// <summary>ダイアグラム上に NULL 許容を表示するかどうか</summary>
        public bool ShowNullabilityInDiagram { get; init; } = true;

        /// <summary>ダイアグラム上で簡易表示（PK/FK カラムのみ）を行うかどうか</summary>
        public bool IsCompactViewInDiagram { get; init; }
    }
}
