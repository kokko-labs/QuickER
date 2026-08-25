using System.IO;
using System.Text;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Resources;

namespace QuickER.Services;

/// <summary>
/// インポートコマンドの実行サービス（ファイル選択→形式解決→確認→置換／マージ取込→完了通知）。
/// </summary>
/// <remarks>
/// <para>
/// MainViewModel.ImportExport から抽出したもので、新しい取込形式の追加・確認規則の変更が
/// VM へ触れずに済む分離点。VM の能力は <see cref="IDiagramTransferHost"/> 経由で借りる。
/// </para>
/// <para>
/// 確認の規則は移設元のとおり: 失うものが無ければ無確認・失うもの（クエリ・未保存編集・
/// 説明/Memo の上書き・壊れクエリ）は内訳を付加し、未保存変更があるときは警告水準
/// （<see cref="DialogServiceExtensions.ConfirmDiscard"/>）で確認する。完了は外部形式の
/// 取込としてモーダルで通知する（CLAUDE.md「完了通知の提示先」節）。
/// </para>
/// </remarks>
internal sealed class DiagramImportService(
    IDiagramTransferHost host,
    IDialogService dialogs,
    IFileDialogService files
)
{
    /// <summary>ファイル選択ダイアログで選択したファイルの形式に応じて ER 図を取り込む（失敗はエラーダイアログで報告する）</summary>
    public void Import()
    {
        var picked = files.PickOpenFile(
            "Mermaid Diagram (*.mmd;*.mermaid)|*.mmd;*.mermaid|DBML Diagram (*.dbml)|*.dbml|Excel Workbook (*.xlsx)|*.xlsx"
        );

        if (picked is null)
        {
            return;
        }

        var format = ResolveFormat(picked.Path, picked.FilterIndex);

        try
        {
            ImportDiagramFile(format, picked.Path);
        }
        catch (Exception ex)
        {
            dialogs.ShowError(
                Strings.Import_Failed + Environment.NewLine + ex.Message,
                Strings.Common_Error
            );
        }
    }

    /// <summary>ファイル拡張子を優先し、無ければフィルター選択から取込形式を判定する</summary>
    internal static DiagramImportFormat ResolveFormat(string path, int filterIndex)
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

    /// <summary>指定形式のダイアグラムファイルを読み込み、確認のうえ現在の図を置換する</summary>
    internal void ImportDiagramFile(DiagramImportFormat format, string path)
    {
        var diagram = format switch
        {
            DiagramImportFormat.Mermaid => MermaidImporter.Load(path),
            DiagramImportFormat.Dbml => DbmlImporter.Load(path),
            DiagramImportFormat.Excel => TableDefinitionDocumentImporter.Load(path),
            _ => throw new InvalidOperationException(Strings.Import_FormatUndetermined),
        };

        var displayName = format switch
        {
            DiagramImportFormat.Mermaid => Strings.Format_Mermaid,
            DiagramImportFormat.Dbml => Strings.Format_Dbml,
            DiagramImportFormat.Excel => Strings.Format_DefinitionDocument,
            _ => Strings.Format_File,
        };

        // Excel 定義書は再取込のマージ（Guid 引継＝クエリ定義・手配置レイアウトの温存）に対応する。
        // Mermaid / DBML は方言情報を持たず定義書用途でもないため、丸ごと置換（クエリ消滅・全体整列）。
        if (format == DiagramImportFormat.Excel)
        {
            ImportExcelMerging(diagram, displayName);
            return;
        }

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

        host.ReplaceWholesale(diagram.Entities, diagram.Relationships);
        dialogs.ShowInformation(
            string.Format(Strings.Import_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>Excel 定義書をマージ取込する（Guid 引継でクエリ定義・レイアウトを温存する）</summary>
    /// <remarks>
    /// 取込結果の Id を現在図へ寄せ、生存クエリ・レイアウト引継をホストのマージ置換
    /// （<see cref="IDiagramTransferHost.ReplaceMerged"/>）に委ねる。Excel 定義書は Memo を保持するため、
    /// 一致エンティティの Memo は取込値を正とする（<c>preserveExistingMemo: false</c>）。
    /// 壊れクエリがあれば確認メッセージへ削除対象名を付加する。
    /// </remarks>
    private void ImportExcelMerging(ErDiagram diagram, string displayName)
    {
        var merged = DiagramMergeReconciler.Reconcile(
            host.BuildModel(),
            diagram.Entities,
            diagram.Relationships,
            preserveExistingMemo: false
        );

        if (
            !ConfirmMergedReplacement(
                merged,
                string.Format(Strings.Import_ReplaceConfirm, displayName)
            )
        )
        {
            return;
        }

        // Excel 定義書は対象 DBMS を保持しているため方言も復元する（マージ置換内で採用）。
        // 生存クエリのみを引き継ぐ（壊れクエリは確認のうえ削除済み）。
        var mergedDiagram = new ErDiagram
        {
            Entities = merged.Entities.ToList(),
            Relationships = merged.Relationships.ToList(),
            TargetDbms = diagram.TargetDbms,
            Queries = merged.SurvivingQueries.ToList(),
        };
        host.ReplaceMerged(mergedDiagram);

        dialogs.ShowInformation(
            string.Format(Strings.Import_Completed, displayName),
            Strings.Common_Complete
        );
    }

    /// <summary>指定スキーマが現在のダイアグラムと構造的に同一かを署名比較で判定する</summary>
    private bool HasSameStructure(
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var current = host.BuildModel();
        var currentSignature = SchemaSignature.Compute(current.Entities, current.Relationships);
        var newSignature = SchemaSignature.Compute(entities, relationships);

        return currentSignature == newSignature;
    }

    /// <summary>置換で失うものがある場合のみ確認ダイアログを表示する（Mermaid / DBML の丸ごと置換用）</summary>
    /// <remarks>
    /// 失うものが無い（<see cref="IDiagramTransferHost.HasNothingToLose"/>）か、構造が同一で失う中身も
    /// 無いときは確認なしで続行する。構造署名（<see cref="HasSameStructure"/>）は名前付きクエリと
    /// 未保存の編集内容（手配置レイアウトを含む）を見ないため、署名一致だけを根拠に無確認で置換すると
    /// それらが無言で消える。クエリがある場合は削除件数を確認メッセージへ付加する
    /// （マージ取込の壊れクエリ列挙と同じ流儀）。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        IReadOnlyList<Entity> entities,
        IReadOnlyList<Relationship> relationships,
        string message
    )
    {
        if (
            host.HasNothingToLose
            || (host.QueryCount == 0 && !host.IsDirty && HasSameStructure(entities, relationships))
        )
        {
            return true;
        }

        // Mermaid / DBML はクエリ定義を持たないため、置換すると現在のクエリはすべて失われる
        var fullMessage =
            host.QueryCount > 0
                ? message
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Format(Strings.Import_QueriesRemovedWarning, host.QueryCount)
                : message;

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return dialogs.ConfirmDiscard(host.IsDirty, fullMessage, Strings.Common_Confirm);
    }

    /// <summary>マージ取込用の置換確認（構造同一かつ失うものが無ければ無確認・失うものは内訳を付加する）</summary>
    /// <remarks>
    /// マージ取込はクエリ・レイアウトを温存するため、構造が同一なら「クエリ・レイアウトは失われない」。
    /// ただし構造署名（<see cref="HasSameStructure"/>）はテーブル・列の説明・Memo を含まないため、
    /// 署名一致だけを根拠に無確認で続行すると未保存の説明・Memo が取込値で無言のうちに消える。
    /// 実差分の件数（<see cref="DiagramMergeResult.DescriptionOverwriteCount"/>）を条件へ加えてこれを防ぐ。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmMergedReplacement(DiagramMergeResult merged, string message)
    {
        var structurallySame =
            host.HasNothingToLose || HasSameStructure(merged.Entities, merged.Relationships);

        // 構造同一かつ壊れクエリ・説明/Memo の上書きなしなら無確認で続行する
        if (
            structurallySame
            && merged.BrokenQueries.Count == 0
            && merged.DescriptionOverwriteCount == 0
        )
        {
            return true;
        }

        // 失うものがあれば内訳（削除対象のクエリ名・上書き件数）を確認メッセージへ付加する（キャンセルで取込中止）
        var builder = new StringBuilder(message);

        if (merged.BrokenQueries.Count > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(
                    Strings.Import_BrokenQueriesWarning,
                    // 件数が多いとダイアログが縦に伸びてボタンが画面外へ出るため、上限で畳む
                    DialogItemList.Format(
                        merged.BrokenQueries.Select(query => "- " + query.Name).ToList(),
                        Strings.Common_MoreItems
                    )
                );
        }

        if (merged.DescriptionOverwriteCount > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(
                    Strings.Import_DescriptionOverwriteWarning,
                    merged.DescriptionOverwriteCount
                );
        }

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return dialogs.ConfirmDiscard(host.IsDirty, builder.ToString(), Strings.Common_Confirm);
    }
}
