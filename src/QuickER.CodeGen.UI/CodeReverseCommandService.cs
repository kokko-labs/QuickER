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

    /// <summary>構造変更、または壊れクエリの削除を伴う場合のみ確認ダイアログを表示する（DB 取込と同一規則）</summary>
    /// <returns>置換を続行してよい場合 true</returns>
    private bool ConfirmDiagramReplacement(
        ErDiagram current,
        DiagramMergeResult merged,
        IReadOnlyList<Relationship> finalRelationships
    )
    {
        var structurallySame =
            current.Entities.Count == 0
            || HasSameStructure(current, merged.Entities, finalRelationships);

        // 構造同一かつ壊れクエリなしなら従来どおり無確認で続行する
        if (structurallySame && merged.BrokenQueries.Count == 0)
        {
            return true;
        }

        // 壊れクエリがあれば削除対象のクエリ名を確認メッセージへ付加する（キャンセルで取込中止）
        var fullMessage =
            merged.BrokenQueries.Count > 0
                ? Strings.Reverse_ReplaceConfirm
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Format(
                        Strings.Reverse_BrokenQueriesWarning,
                        FormatQueryNames(merged.BrokenQueries)
                    )
                : Strings.Reverse_ReplaceConfirm;

        // 未保存変更があるときは置換で編集内容が失われるため警告水準（Warning）で確認する
        return _dialogs.ConfirmDiscard(_host.IsDirty, fullMessage, Strings.Common_Confirm);
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
