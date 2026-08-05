using System.IO;
using System.Text;
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
/// コードに無い情報（参照アクション・制約名・多対多）を現在図から温存 → 置換確認 →
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

        // リバース専用後処理: コードに無い参照アクション・制約名・多対多を現在図から温存する
        var finalRelationships = ReverseMergePostProcessor.Apply(
            current,
            merged.Entities,
            merged.Relationships
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
    }

    /// <summary>構造変更・壊れクエリの削除・説明の上書きを伴う場合のみ確認ダイアログを表示する（DB 取込と同一規則）</summary>
    /// <remarks>
    /// 構造署名（<see cref="SchemaSignature"/>）はテーブル・列の説明を含まないため、署名一致だけを根拠に
    /// 無確認で続行すると、未保存の説明がコード由来の値で無言のうちに消える。実差分の件数
    /// （<see cref="DiagramMergeResult.DescriptionOverwriteCount"/>）を条件へ加えてこれを防ぐ。
    /// </remarks>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        ErDiagram current,
        DiagramMergeResult merged,
        IReadOnlyList<Relationship> finalRelationships
    )
    {
        // エンティティ数だけで「空」と見なすと、クエリだけの図・未保存の編集内容を無確認で捨ててしまう
        // （GUI 側 MainViewModel.HasNothingToLose と同じ判定。ホスト契約の IsEmpty は意味が異なるため使わない）
        var structurallySame =
            (current.Entities.Count == 0 && current.Queries.Count == 0 && !_host.IsDirty)
            || HasSameStructure(current, merged.Entities, finalRelationships);

        // 構造同一かつ壊れクエリ・説明の上書きなしなら従来どおり無確認で続行する
        if (
            structurallySame
            && merged.BrokenQueries.Count == 0
            && merged.DescriptionOverwriteCount == 0
        )
        {
            return true;
        }

        // 失うものがあれば内訳を確認メッセージへ付加する（キャンセルで取込中止）
        var fullMessage = AppendLossWarnings(Strings.Reverse_ReplaceConfirm, merged);

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
                    Strings.Reverse_BrokenQueriesWarning,
                    FormatQueryNames(merged.BrokenQueries)
                );
        }

        if (merged.DescriptionOverwriteCount > 0)
        {
            builder
                .Append(Environment.NewLine)
                .Append(Environment.NewLine)
                .AppendFormat(
                    Strings.Reverse_DescriptionOverwriteWarning,
                    merged.DescriptionOverwriteCount
                );
        }

        return builder.ToString();
    }

    /// <summary>壊れクエリの名前を 1 行 1 件で列挙した文字列へ整形する</summary>
    private static string FormatQueryNames(IReadOnlyList<QueryDefinition> queries) =>
        string.Join(Environment.NewLine, queries.Select(query => "- " + query.Name));

    /// <summary>指定スキーマが現在のダイアグラムと構造的に同一かを署名比較で判定する</summary>
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
