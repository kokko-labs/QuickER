using System.Text;
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
/// 置換確認ロジック（<see cref="ConfirmDiagramReplacement"/> / <see cref="HasSameStructure"/>）は、
/// ファイル取込で使う <c>MainViewModel</c> 側の同名メソッドと同一の署名比較ロジックを本サービスへ移植した
/// （ファイル取込は VM 側、DB 取込は本サービスと入口が分かれるため少量の重複を許容する）。
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
            var merged = DiagramMergeReconciler.Reconcile(
                _host.GetDiagram(),
                result.Entities,
                result.Relationships,
                preserveExistingMemo: true
            );

            // 構造差分がある場合のみ置換確認を行う（壊れクエリがあれば削除対象名を確認メッセージへ付加する）
            if (!ConfirmDiagramReplacement(merged, Strings.Db_ImportReplaceConfirm))
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
    /// 空の図、または構造が同一（かつ壊れクエリ・説明の上書きなし）の場合は確認なしで続行する。
    /// 構造署名（<see cref="SchemaSignature"/>）はテーブル・列の説明を含まないため、署名一致だけを
    /// 根拠に無確認で続行すると、未保存の説明が取込値で無言のうちに消える。実差分の件数
    /// （<see cref="DiagramMergeResult.DescriptionOverwriteCount"/>）を条件へ加えてこれを防ぐ。
    /// 壊れクエリがある場合は削除対象のクエリ名一覧を確認メッセージへ付加する。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(DiagramMergeResult merged, string message)
    {
        var current = _host.GetDiagram();

        // エンティティ数だけで「空」と見なすと、クエリだけの図・未保存の編集内容を無確認で捨ててしまう
        // （GUI 側 MainViewModel.HasNothingToLose と同じ判定。ホスト契約の IsEmpty は意味が異なるため使わない）
        var structurallySame =
            (current.Entities.Count == 0 && current.Queries.Count == 0 && !_host.IsDirty)
            || HasSameStructure(current, merged.Entities, merged.Relationships);

        // 構造同一かつ壊れクエリ・説明の上書きなしなら無確認で続行する
        if (
            structurallySame
            && merged.BrokenQueries.Count == 0
            && merged.DescriptionOverwriteCount == 0
        )
        {
            return true;
        }

        // 失うものがあれば内訳を確認メッセージへ付加する（キャンセルで取込中止）
        var fullMessage = AppendLossWarnings(message, merged);

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return _dialogs.ConfirmDiscard(_host.IsDirty, fullMessage, Strings.Common_Confirm);
    }

    /// <summary>確認メッセージへ「失うもの」の内訳（壊れクエリ名・説明の上書き件数）を追記する</summary>
    private static string AppendLossWarnings(string message, DiagramMergeResult merged)
    {
        var builder = new StringBuilder(message);

        if (merged.BrokenQueries.Count > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(
                    Strings.Db_ImportBrokenQueriesWarning,
                    FormatQueryNames(merged.BrokenQueries)
                );
        }

        if (merged.DescriptionOverwriteCount > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(
                    Strings.Db_ImportDescriptionOverwriteWarning,
                    merged.DescriptionOverwriteCount
                );
        }

        return builder.ToString();
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
        // スクロールしない標準 MessageBox 用＝コード取込・エクスポート欠落告知と同じ流儀）
        var details = string.Join(
            Environment.NewLine,
            warnings.Select(warning => "  • " + DescribeImportWarning(warning))
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

    /// <summary>言語中立の取込警告を UI 言語の 1 行へ整形する</summary>
    /// <remarks>
    /// <see cref="SchemaImportWarning.Subject"/> / <see cref="SchemaImportWarning.Detail"/> の意味は
    /// <see cref="SchemaImportWarningKind"/> ごとに決まる（各 Kind の XmlDoc が正本）。
    /// </remarks>
    private static string DescribeImportWarning(SchemaImportWarning warning)
    {
        // 名前は DB 由来の任意文字列で改行を含み得る。1 行 1 件の内訳を崩さないよう畳んでおく
        var table = SqlComment.Sanitize(warning.TableName);
        var subject = SqlComment.Sanitize(warning.Subject);
        var detail = SqlComment.Sanitize(warning.Detail);

        return warning.Kind switch
        {
            SchemaImportWarningKind.DomainTypeFlattened => string.Format(
                Strings.Db_ImportWarningDomainTypeFlattened,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.TableColumnsUnavailable => string.Format(
                Strings.Db_ImportWarningTableColumnsUnavailable,
                table
            ),
            SchemaImportWarningKind.ForeignKeyOutsideScope => string.Format(
                Strings.Db_ImportWarningForeignKeyOutsideScope,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.TableNameCollision => string.Format(
                Strings.Db_ImportWarningTableNameCollision,
                subject,
                detail
            ),
            SchemaImportWarningKind.ColumnTypeNotEmittable => string.Format(
                Strings.Db_ImportWarningColumnTypeNotEmittable,
                table,
                subject,
                detail
            ),
            SchemaImportWarningKind.PartitionDefinitionLost => string.Format(
                Strings.Db_ImportWarningPartitionDefinitionLost,
                table
            ),
            // 未知の種別を黙って空行にしない（ConcurrencyModes.Validated と同じ流儀）
            _ => throw new ArgumentOutOfRangeException(
                nameof(warning),
                warning.Kind,
                "Unknown schema import warning kind."
            ),
        };
    }

    /// <summary>壊れクエリの名前を 1 行 1 件で列挙した文字列へ整形する（件数が多いときは上限で畳む）</summary>
    private static string FormatQueryNames(IReadOnlyList<QueryDefinition> queries) =>
        DialogItemList.Format(
            queries.Select(query => "- " + query.Name).ToList(),
            Strings.Common_MoreItems
        );

    /// <summary>指定スキーマが現在のダイアグラムと構造的に同一かを署名比較で判定する</summary>
    /// <remarks>ファイル取込で使う <c>MainViewModel.HasSameStructure</c> と同一の <see cref="SchemaSignature"/> 比較</remarks>
    private static bool HasSameStructure(
        ErDiagram current,
        IEnumerable<Entity> entities,
        IEnumerable<Relationship> relationships
    )
    {
        var currentSignature = SchemaSignature.Compute(current.Entities, current.Relationships);
        var newSignature = SchemaSignature.Compute(entities, relationships);

        return currentSignature == newSignature;
    }
}
