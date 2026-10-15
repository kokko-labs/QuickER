using QuickER.Db.UI.Resources;
using QuickER.Extensibility;
using QuickER.Gui.Abstractions;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Db.UI;

/// <summary>
/// データベースへ接続してスキーマを取得し、確認のうえ現在の図へ反映するコマンドサービス。
/// </summary>
/// <remarks>
/// ER 図の取得・置換・プロバイダ解決はホスト契約（<see cref="IErDiagramHost"/>）越しに行い、
/// 接続ダイアログ提示は <see cref="IDbConnectionDialogPresenter"/> のシーム越しに行う。
/// 置換確認の判定は <see cref="DiagramReplacementCheck"/>、確認文の内訳は <see cref="ReplacementLossMessage"/> を
/// C# コードの取込・Excel 定義書のマージ取込と共有する（文言だけが各機能の resx にある）。
/// </remarks>
public sealed class DbImportCommandService
{
    /// <summary>ER 図の取得・置換・プロバイダ解決を提供するホスト契約</summary>
    private readonly IErDiagramHost _host;

    /// <summary>確認・エラーダイアログの表示先</summary>
    private readonly IDialogService _dialogs;

    /// <summary>DB 接続ダイアログの提示シーム</summary>
    private readonly IDbConnectionDialogPresenter _presenter;

    /// <summary>依存を注入して生成する</summary>
    public DbImportCommandService(
        IErDiagramHost host,
        IDialogService dialogs,
        IDbConnectionDialogPresenter presenter
    )
    {
        _host = host;
        _dialogs = dialogs;
        _presenter = presenter;
    }

    /// <summary>データベースへ接続してスキーマを取得し、確認のうえ現在の図へ反映する（ツールバーボタンから実行）</summary>
    /// <remarks>取込ダイアログでは DBMS を選択でき、取込成功時に図の TargetDbms を選択方言へ設定する</remarks>
    public async Task RunAsync()
    {
        // 初期選択の方言は現在の対象 DBMS から解決する（解決不能なら未指定＝先頭方言が初期選択になる）
        var fixedProvider = _host.Providers.TryGet(_host.TargetDbms, out var provider)
            ? provider
            : null;

        var picked = _presenter.Show(
            DbConnectionDialogMode.Import,
            fixedProvider: fixedProvider,
            title: Strings.Db_ImportTitle
        );

        if (picked is null)
        {
            return;
        }

        // 取込が最後まで成功したときだけ非 null になる（確認キャンセル・例外では null のまま）
        IReadOnlyList<SchemaImportWarning>? completedWarnings = null;

        try
        {
            var connectionString = picked.Provider.BuildConnectionString(picked.Settings);
            var result = await picked
                .Provider.SchemaImporter.ImportAsync(
                    connectionString,
                    picked.Settings.CommandTimeoutSeconds
                )
                .ConfigureAwait(true);

            // Guid 引継マージ: 取込結果の Id を現在図の Guid へ寄せ、クエリ定義・レイアウトを温存できるようにする。
            // DB には Memo に対応する情報がないため、一致エンティティの Memo は現在図の値を温存する。
            // 署名比較は「マージ後」に行う（新規 Guid のままだとリレーションのある図で署名が常に不一致になるため）。
            var current = _host.GetDiagram();
            var merged = DiagramMergeReconciler.Reconcile(
                current,
                result.Entities,
                result.Relationships,
                preserveExistingMemo: true
            );

            // 構造差分がある場合のみ置換確認を行う（壊れクエリがあれば削除対象名を確認メッセージへ付加する）
            if (!ConfirmDiagramReplacement(current, merged, Strings.Db_ImportReplaceConfirm))
            {
                return;
            }

            // 取込結果を意味モデルへ束ね、取込先の方言を TargetDbms として採用して図を丸ごと差し替える
            // （方言採用・Undo なし置換・レイアウト引継はホスト実装 ReplaceDiagram の責務）。
            // 生存クエリのみを引き継ぐ（壊れクエリは確認のうえ削除済み）。
            var diagram = new ErDiagram
            {
                Entities = merged.Entities.ToList(),
                Relationships = merged.Relationships.ToList(),
                TargetDbms = picked.Provider.Name,
                Queries = merged.SurvivingQueries.ToList(),
            };
            _host.ReplaceDiagram(diagram);
            completedWarnings = result.Warnings;
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                string.Format(Strings.Db_ImportFailed, ex.Message),
                Strings.Common_Error
            );
            return;
        }

        // 外部（DB）からの取込はファイル取込と同水準の出来事なので、完了もモーダルで知らせる
        // （ER 図ファイル自身の保存・開くはステータスバー、との使い分け）。
        // 通知を try の外へ置くのは、警告整形の不具合（未知の種別など）が catch に拾われて
        // 「取込に失敗しました」へ化けるのを防ぐため＝取込は既に成功している
        if (completedWarnings is not null)
        {
            NotifyImportCompleted(completedWarnings);
        }
    }

    /// <summary>構造変更・壊れクエリの削除・説明の上書きを伴う場合のみ確認ダイアログを表示する</summary>
    /// <remarks>
    /// 確認を省く条件は <see cref="DiagramReplacementCheck.CanSkipConfirmation"/>（構造署名は説明を含まないため、
    /// 署名の一致だけでなく説明の上書き件数も見る）。失うものがあれば内訳を確認文へ付ける。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        ErDiagram current,
        DiagramMergeResult merged,
        string message
    )
    {
        if (
            DiagramReplacementCheck.CanSkipConfirmation(
                DiagramReplacementCheck.HasNothingToLose(current, _host.IsDirty),
                current,
                merged,
                merged.Relationships
            )
        )
        {
            return true;
        }

        var fullMessage = ReplacementLossMessage.Build(
            message,
            merged.BrokenQueries.Select(query => query.Name).ToList(),
            merged.DescriptionOverwriteCount,
            new ReplacementLossTexts(
                Strings.Db_ImportBrokenQueriesWarning,
                Strings.Db_ImportDescriptionOverwriteWarning,
                Strings.Common_MoreItems
            )
        );

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return _dialogs.ConfirmDiscard(_host.IsDirty, fullMessage, Strings.Common_Confirm);
    }

    /// <summary>取込の完了を知らせる。宣言どおりに写し取れなかった箇所があれば内訳を添える</summary>
    /// <remarks>
    /// 単文の完了は <c>ShowInformation</c>、内訳・警告を添えるときは <c>ShowInformationDetails</c>
    /// （<see cref="IDialogService"/> の XmlDoc が正本の規則）。警告ゼロの取込は従来どおり単文。
    /// </remarks>
    private void NotifyImportCompleted(IReadOnlyList<SchemaImportWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            _dialogs.ShowInformation(Strings.Db_ImportCompleted, Strings.Common_Complete);
            return;
        }

        // 詳細領域はスクロール可能なため全件を畳まず載せる（DialogItemList.Format の 30 件畳みは
        // スクロールしない標準 MessageBox 用＝コード取込・エクスポート欠落告知と同じ流儀）。
        // 種別ごとに見出し（件数入り）を置き、その下に箇所を並べる
        var details = string.Join(
            Environment.NewLine,
            SchemaImportWarningText
                .Group(warnings)
                .SelectMany(group =>
                    group.Items.Select(item => "  • " + item).Prepend(group.Heading)
                )
        );

        _dialogs.ShowInformationDetails(
            Strings.Db_ImportCompleted
                + Environment.NewLine
                + Environment.NewLine
                + Strings.Db_ImportWarningsHeader,
            details,
            Strings.Common_Complete
        );
    }
}
