using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Gui.ViewModels;

/// <summary>
/// 図のファイルの状態機械の手の列の生成器（<see cref="DocumentFileGenerator"/>）が作らない形の宣言。決まった形のシナリオと乱数の手の列が
/// 同じハーネスを使う
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class DocumentFileNotGenerated
{
    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "DocumentFileGenerator",
            [
                ProtectionNames.EnginePart(EngineIds.DocumentFile, "Random"),
                ProtectionNames.EnginePart(EngineIds.DocumentFile, "Scenarios"),
            ],
            [
                NotGenerated.Pending(
                    "unreadable-or-unwritable-file",
                    "読めない・書けないファイル（ロック・権限・読み取り専用）",
                    "G5",
                    "書けないファイルは親フォルダが無い形だけを作る。保存と外部変更の検知は、内容のハッシュが取れない（TryCompute が null）形で分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "external-diagram-with-layout",
                    "配置つきの外部の図・一部の表だけ配置の欠けた外部の図",
                    "G5",
                    "外部の図は配置を持たず表が 1 つ。取込は配置の有無と欠けで分岐する",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Accepted(
                    "real-file-watcher",
                    "実物の FileSystemWatcher とデバウンスの時間",
                    "監視は注入で置き換え、時間に依らない決定的な手の列にする。実物の監視の追従は単発のテストが受け持つ",
                    "`MainViewModelLoadHashRaceTests`"
                ),
                NotGenerated.Accepted(
                    "empty-file",
                    "空のファイル",
                    "空のファイルを読み込みが断ることは、図のファイルの形式の表が受け持つ",
                    "`DocumentFile/Format`（行 `tryload-empty-*`）"
                ),
                NotGenerated.Pending(
                    "encoding-size-and-path-forms",
                    "UTF-16 のファイル・巨大なファイル・パスの形（相対・UNC・長いパス）",
                    "図ファイルの残りの区切り（未起動）",
                    "ファイルはいつも UTF-8 の小さな内容で、パスは一時フォルダの下の絶対パス。読み込み（JsonStorageService.TryLoad）とパスの正規化はこれらで分岐する",
                    NotGenerated.Unguarded
                ),
            ]
        );
}
