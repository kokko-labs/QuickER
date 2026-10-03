using System.IO;
using QuickER.CodeGen.UI.Resources;
using QuickER.CodeReverse.CSharp;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.CodeGen.UI;

/// <summary>
/// C# ファイルをリバース解析し、確認のうえ現在の図へマージ取込するコマンドサービス。
/// </summary>
/// <remarks>
/// DB 取込（<c>DbImportCommandService</c>）のフローを踏襲する。ファイル選択 → リバース解析 →
/// <see cref="DiagramMergeReconciler"/> による Guid 引継 → <see cref="ReverseMergePostProcessor"/> で
/// コードが語っていない情報（未指定の参照アクション・制約名・多対多）を現在図から温存 → 置換確認 →
/// <see cref="IErDiagramHost.ReplaceDiagram"/> へ渡す。コードは方言中立のため TargetDbms は現在図の方言を維持する。
/// </remarks>
public sealed class CodeReverseCommandService
{
    /// <summary>ER 図の取得・置換・プロバイダ解決を提供するホスト契約</summary>
    private readonly IErDiagramHost _host;

    /// <summary>確認・エラーダイアログの表示先</summary>
    private readonly IDialogService _dialogs;

    /// <summary>取込元 C# ファイルの選択に使うファイルダイアログ</summary>
    private readonly IFileDialogService _files;

    /// <summary>依存を注入して生成する</summary>
    public CodeReverseCommandService(
        IErDiagramHost host,
        IDialogService dialogs,
        IFileDialogService files
    )
    {
        _host = host;
        _dialogs = dialogs;
        _files = files;
    }

    /// <summary>C# ファイルを選択してリバース解析し、確認のうえ現在の図へマージ取込する（ツールバーボタンから実行）</summary>
    public void Run()
    {
        var picked = _files.PickOpenFile(Strings.Reverse_FileFilter);

        if (picked is null)
        {
            return;
        }

        // 型トークンの展開・TargetDbms 維持のため、現在図の方言の型カタログを解決する
        // （解決不能なら何もしない＝現在図の TargetDbms は必ずレジストリに存在するため通常は到達しない）。
        if (!_host.Providers.TryGet(_host.TargetDbms, out var provider))
        {
            return;
        }

        var current = _host.GetDiagram();

        CodeReverseResult reversed;
        try
        {
            var sourceText = File.ReadAllText(picked.Path);
            reversed = new CSharpReverseParser().Parse(sourceText, provider.TypeCatalog);
        }
        catch (CodeReverseException ex)
        {
            // 解析対象クラス 0 件などの致命的な問題（メッセージはローカライズ済み・案内込み）
            _dialogs.ShowError(ex.Message, Strings.Common_Error);

            return;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                string.Format(Strings.Reverse_Failed, ex.Message),
                Strings.Common_Error
            );

            return;
        }

        // Guid 引継マージ: 取込結果の Id を現在図の Guid へ寄せ、クエリ定義・レイアウトを温存できるようにする。
        // コードは Memo を持たないため、一致エンティティの Memo は現在図の値を温存する。
        var merged = DiagramMergeReconciler.Reconcile(
            current,
            reversed.Entities.ToList(),
            reversed.Relationships.ToList(),
            preserveExistingMemo: true
        );

        // リバース専用後処理: コードが指定していない参照アクション・制約名と、多対多を現在図から温存する
        var finalRelationships = ReverseMergePostProcessor.Apply(
            current,
            merged.Entities,
            merged.Relationships,
            reversed.RelationshipMetadata
        );

        // 主キーの順序も同じ規則で温存する（古い生成コードは順序を書けず、取り込むと宣言順へ戻るため）
        ReverseMergePostProcessor.ApplyPrimaryKeyOrder(
            current,
            merged.Entities,
            reversed.TablesWithPrimaryKeyOrder
        );

        // 構造差分・壊れクエリがある場合のみ置換確認を行う（DB 取込と同じ規則）
        if (!ConfirmDiagramReplacement(current, merged, finalRelationships))
        {
            return;
        }

        // TargetDbms は現在図の方言を維持する（コードは方言中立のため）。生存クエリのみ引き継ぐ。
        var diagram = new ErDiagram
        {
            Entities = merged.Entities.ToList(),
            Relationships = finalRelationships,
            TargetDbms = current.TargetDbms,
            Queries = merged.SurvivingQueries.ToList(),
        };
        _host.ReplaceDiagram(diagram);
        NotifyImportCompleted(reversed);
    }

    /// <summary>取込完了を通知する（解析警告があれば内訳を添える）</summary>
    /// <remarks>
    /// 外部（C# コード）からの取込はファイル取込と同水準の出来事なのでモーダルで知らせる
    /// （ER 図ファイル自身の保存・開くはステータスバー、との使い分け）。
    /// 属性の無い列のスキップや解決できない一意制約などの警告は見逃せないため、
    /// あるときだけ詳細ダイアログで内訳を添える（コード生成の診断提示と同じ規則）。
    /// </remarks>
    private void NotifyImportCompleted(CodeReverseResult reversed)
    {
        if (reversed.Warnings.Count == 0)
        {
            _dialogs.ShowInformation(Strings.Reverse_ImportCompleted, Strings.Common_Complete);
            return;
        }

        var details = string.Join(
            Environment.NewLine,
            reversed.Warnings.Select(warning => string.Format(Strings.Csharp_WarningLine, warning))
        );

        _dialogs.ShowInformationDetails(
            Strings.Reverse_ImportCompleted
                + Environment.NewLine
                + Environment.NewLine
                + Strings.Csharp_WarningIntro,
            details,
            Strings.Common_Complete
        );
    }

    /// <summary>構造変更・壊れクエリの削除・説明の上書きを伴う場合のみ確認ダイアログを表示する（DB 取込と同一規則）</summary>
    /// <remarks>
    /// 確認を省く条件は <see cref="DiagramReplacementCheck.CanSkipConfirmation"/>（構造署名は説明を含まないため、
    /// 署名の一致だけでなく説明の上書き件数も見る）。構造は、現在の図から参照アクション等を補った後の
    /// リレーション（<paramref name="finalRelationships"/>）で比べる。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        ErDiagram current,
        DiagramMergeResult merged,
        IReadOnlyList<Relationship> finalRelationships
    )
    {
        if (
            DiagramReplacementCheck.CanSkipConfirmation(
                DiagramReplacementCheck.HasNothingToLose(current, _host.IsDirty),
                current,
                merged,
                finalRelationships
            )
        )
        {
            return true;
        }

        var fullMessage = ReplacementLossMessage.Build(
            Strings.Reverse_ReplaceConfirm,
            merged.BrokenQueries.Select(query => query.Name).ToList(),
            merged.DescriptionOverwriteCount,
            new ReplacementLossTexts(
                Strings.Reverse_BrokenQueriesWarning,
                Strings.Reverse_DescriptionOverwriteWarning,
                Strings.Common_MoreItems
            )
        );

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return _dialogs.ConfirmDiscard(_host.IsDirty, fullMessage, Strings.Common_Confirm);
    }
}
