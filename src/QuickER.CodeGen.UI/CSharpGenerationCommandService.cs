using System.Globalization;
using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.UI.Resources;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Provider;

namespace QuickER.CodeGen.UI;

/// <summary>
/// 現在の ER 図から C# の Entity / EditModel / Mapper / Repository コードを生成するコマンドサービス。
/// </summary>
/// <remarks>
/// アプリ本体 <c>MainViewModel</c> の <c>GenerateCSharpCode</c> から移設したフィーチャーモジュール本体。
/// ER 図の取得・プロバイダ解決はホスト契約（<see cref="IErDiagramHost"/>）越しに行い、
/// ダイアログ提示は <see cref="ICSharpGenerationDialogPresenter"/> のシーム越しに行う。
/// </remarks>
public sealed class CSharpGenerationCommandService
{
    /// <summary>ER 図の取得・プロバイダ解決を提供するホスト契約</summary>
    private readonly IErDiagramHost _host;

    /// <summary>確認・通知ダイアログの表示先</summary>
    private readonly IDialogService _dialogs;

    /// <summary>C# コード生成ダイアログの提示シーム</summary>
    private readonly ICSharpGenerationDialogPresenter _presenter;

    /// <summary>依存を注入して生成する</summary>
    public CSharpGenerationCommandService(
        IErDiagramHost host,
        IDialogService dialogs,
        ICSharpGenerationDialogPresenter presenter
    )
    {
        _host = host;
        _dialogs = dialogs;
        _presenter = presenter;
    }

    /// <summary>現在の ER 図から C# コードを生成する（ツールバーボタンから実行）</summary>
    public void Run()
    {
        // 現在のプロバイダは図の TargetDbms からレジストリで解決する
        // （GetDiagram() の TargetDbms は CurrentProvider.Name を保証するため通常は必ず成功する）。
        var providerDiagram = _host.GetDiagram();

        if (!_host.Providers.TryGet(providerDiagram.TargetDbms, out var provider))
        {
            // 防御的フォールバック: 解決不能なら何もしない（通常は到達しない）
            return;
        }

        var dialogResult = _presenter.Show(provider);

        if (dialogResult is null)
        {
            return;
        }

        try
        {
            var options = dialogResult.Options;
            // 型解決（プロバイダ）→生成（Generator）の結合点は共有ファサードに集約し、CLI とドリフトさせない。
            // QuickER 版 Repository の実効方言ごとに、レジストリから方言別の型マッパを解決して渡す
            // （マルチ方言時は各方言バケットをその方言の型で解決し、単一方言時も同一経路で挙動は変わらない）。
            // 図の取得はダイアログ OK 後に行い、現行と同じタイミングを保つ。
            var diagram = _host.GetDiagram();
            var dialectMappers = ResolveDialectTypeMappers(options);
            var result = DiagramCodeGenerator.Generate(
                provider.TypeMapper,
                provider.TypeCatalog,
                dialectMappers,
                diagram,
                options
            );

            if (result.HasErrors)
            {
                // 導入文（message）と診断一覧（details）を分けて、広い詳細領域を持つエラーダイアログで提示する
                _dialogs.ShowErrorDetails(
                    Strings.Csharp_GenerationFailedIntro,
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

            // 診断一覧と PackageReference 案内は「詳細」としてまとめ、空行区切りで連結する。
            // パッケージ参照モードのときは、必要な PackageReference をコピー可能な形で詳細へ続けて載せる。
            var detailSections = new List<string>();
            var diagnostics = BuildGenerationDiagnosticsMessage(result);
            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                detailSections.Add(diagnostics);
            }

            if (options.UseRuntimePackages)
            {
                detailSections.Add(
                    string.Join(
                        Environment.NewLine,
                        RuntimePackageReferenceGuidance.BuildGuidanceLines(
                            options,
                            RuntimePackages.ResolveGuidanceVersion(),
                            CultureInfo.CurrentUICulture
                        )
                    )
                );
            }

            // 詳細（診断一覧・PackageReference 案内）がある場合はコピー可能な専用ダイアログで提示し、
            // 詳細が無い（診断ゼロかつパッケージ案内なし）場合は単文の完了通知で知らせる
            // （単文の完了通知に大型ダイアログは出さない）。
            if (detailSections.Count > 0)
            {
                _dialogs.ShowInformationDetails(
                    Strings.Csharp_GeneratedSuccess,
                    string.Join(Environment.NewLine + Environment.NewLine, detailSections),
                    Strings.Common_Complete
                );
            }
            else
            {
                _dialogs.ShowInformation(Strings.Csharp_GeneratedSuccess, Strings.Common_Complete);
            }
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
    /// QuickER 版 Repository の実効方言（<see cref="CodeGenerationOptions.EffectiveRepositoryDialects"/>）ごとに、
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
            if (_host.Providers.TryGet(dialect, out var provider))
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
}
