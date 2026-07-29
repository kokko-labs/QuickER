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
/// アプリ本体 <c>MainViewModel</c> の <c>ImportFromDatabaseAsync</c> から移設したフィーチャーモジュール本体。
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

        try
        {
            var connectionString = picked.Provider.BuildConnectionString(picked.Settings);
            var result = await picked
                .Provider.SchemaImporter.ImportAsync(connectionString)
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
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(
                string.Format(Strings.Db_ImportFailed, ex.Message),
                Strings.Common_Error
            );
        }
    }

    /// <summary>構造変更を伴う置換、または壊れクエリの削除を伴う場合のみ確認ダイアログを表示する</summary>
    /// <remarks>
    /// 空の図、または構造が同一（かつ壊れクエリなし）の場合は確認なしで続行する。
    /// 壊れクエリがある場合は削除対象のクエリ名一覧を確認メッセージへ付加する。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(DiagramMergeResult merged, string message)
    {
        var current = _host.GetDiagram();

        var structurallySame =
            current.Entities.Count == 0
            || HasSameStructure(current, merged.Entities, merged.Relationships);

        // 構造同一かつ壊れクエリなしなら従来どおり無確認で続行する
        if (structurallySame && merged.BrokenQueries.Count == 0)
        {
            return true;
        }

        // 壊れクエリがあれば削除対象のクエリ名を確認メッセージへ付加する（キャンセルで取込中止）
        var fullMessage =
            merged.BrokenQueries.Count > 0
                ? message
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Format(
                        Strings.Db_ImportBrokenQueriesWarning,
                        FormatQueryNames(merged.BrokenQueries)
                    )
                : message;

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return _dialogs.ConfirmDiscard(_host.IsDirty, fullMessage, Strings.Common_Confirm);
    }

    /// <summary>壊れクエリの名前を 1 行 1 件で列挙した文字列へ整形する</summary>
    private static string FormatQueryNames(IReadOnlyList<QueryDefinition> queries) =>
        string.Join(Environment.NewLine, queries.Select(query => "- " + query.Name));

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
