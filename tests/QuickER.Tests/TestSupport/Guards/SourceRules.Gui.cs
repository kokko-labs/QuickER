using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：アプリ（App・Program の配線・ダイアログ・XAML）</summary>
/// <remarks>
/// App・Program はエントリポイントと WPF の Application 派生で、テストから実体化・実行できないので、配線をソースで見張る。
/// 文字列で読む必須の規則は、コメント・条件コンパイルの外れる分岐の中に書いた表記でも満たされる。UI 例外の受け皿（トークンの並び）と
/// 単一インスタンスの配線（構文の節）の必須の規則は構文で読むので、コメントの中だけの表記では満たされない（自己検査の行 *-comment-only）。
/// </remarks>
internal static partial class SourceRules
{
    private const string AppXaml = "src/QuickER.Gui/App.xaml";

    private const string AppCode = "src/QuickER.Gui/App.xaml.cs";

    private const string ProgramCode = "src/QuickER.Gui/Program.cs";

    private const string FileDialogServiceCode = "src/QuickER.Gui.Common/WpfFileDialogService.cs";

    private const string MessageBoxDialogServiceCode =
        "src/QuickER.Gui.Common/MessageBoxDialogService.cs";

    private const string BehaviorInputArea = "QuickER.Gui/Behaviors/BehaviorInput.cs";

    private const string BehaviorInputCode = "src/" + BehaviorInputArea;

    private const string VisualAncestorsArea = "QuickER.Gui/Behaviors/VisualAncestors.cs";

    private const string VisualAncestorsCode = "src/" + VisualAncestorsArea;

    private const string BehaviorInputDefault =
        "読み口の既定（差し替えが無いときの WPF の値そのもの。走査が空振りしていないことの確認）";

    private static readonly (string What, Regex Pattern)[] DialogOwnerResolveForms =
    [
        FixedText("DialogOwner.Resolve()"),
    ];

    private static readonly (string What, Regex Pattern)[] OwnerlessFileDialogForms =
    [
        TextPattern("dialog.ShowDialog() ==", @"dialog\.ShowDialog\(\)\s*=="),
    ];

    private static readonly (string What, Regex Pattern)[] OwnerResolverCopyForms =
    [
        FixedText("private static Window? ResolveOwner"),
    ];

    private static readonly (string What, Regex Pattern)[] ShutdownModeDeclarationForms =
    [
        FixedText("ShutdownMode=\"OnMainWindowClose\""),
    ];

    private static readonly (string What, Regex Pattern)[] ShutdownModeOverrideForms =
    [
        TextPattern("ShutdownMode =", @"ShutdownMode\s*="),
    ];

    private static readonly (string What, Regex Pattern)[] MainWindowAssignmentForms =
    [
        TextPattern("MainWindow = window;", @"\bMainWindow\s*=\s*window\s*;"),
    ];

    private static readonly (string What, Regex Pattern)[] FeatureModuleShutdownForms =
    [
        FixedText("new FeatureModuleShutdownOnce("),
        FixedText("window.Closing += (_, _) => featureModuleShutdown.CloseAll();"),
    ];

    private static readonly (string What, Regex Pattern)[] FeatureModuleShutdownBypassForms =
    [
        TextPattern(".OnMainWindowClosing(", @"\.OnMainWindowClosing\s*\("),
    ];

    /// <summary>UI 例外の受け皿の必須の形（トークンの並び。何かは並びをつないだ文字列）</summary>
    private static readonly string[][] UiCrashHandlerTokenForms =
    [
        ["CrashHandlingService", ".", "HandleUiCrash", "("],
        ["_featureModuleShutdown", "?", ".", "CloseAll", "(", ")"],
    ];

    private static readonly (string What, Regex Pattern)[] UiCrashHandlerBypassForms =
    [
        FixedText("CrashHandlingService.HandleCrash("),
        TextPattern("行頭の Environment.Exit(", @"^\s*Environment\.Exit\(", RegexOptions.Multiline),
    ];

    /// <summary>単一インスタンスの配線の必須の呼び出し（メソッド名と引数リスト。受け手は問わない）</summary>
    private static readonly HashSet<string> SingleInstanceCallForms = new(StringComparer.Ordinal)
    {
        "ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose)",
        "RelaunchWith(WpfApplicationRestartService.StartNewInstance)",
    };

    /// <summary>座を開けないときの案内の分岐の比較</summary>
    private const string SingleInstanceInaccessibleForm =
        "outcome == SingleInstanceOutcome.Inaccessible";

    private static readonly (string What, Regex Pattern)[] SingleInstanceBypassForms =
    [
        TextPattern("SingleInstanceGuard.TryAcquire()", @"SingleInstanceGuard\.TryAcquire\(\)"),
    ];

    private static IEnumerable<SourceRule> Gui() =>
        [
            // src の XAML の DataGrid は、利用者の行の追加・削除を明示で切る（既定のままだと Delete キーや末尾の新規行が正本の
            // ViewModel のコレクションを直接書き換え、履歴にも未保存の判定にも残らず、制約の後始末も通らない）。
            // 属性が無いのも赤（既定値に頼らない）。空振りの検知：範囲の中の DataGrid が 1 つ以上
            SourceRule.Forbid(
                "datagrid-row-edits",
                "DataGrid は CanUserAddRows=\"False\" と CanUserDeleteRows=\"False\" を明示し、行の追加・削除はコマンドを通す",
                SourceScope.Under("src", ".xaml"),
                GuardFinder.File(GuardReader.Xml, file => FindDataGrids(file, editableOnly: true))
            ) with
            {
                Floor = new(
                    GuardFinder.File(
                        GuardReader.Xml,
                        file => FindDataGrids(file, editableOnly: false)
                    ),
                    1,
                    "DataGrid"
                ),
            },
            // キャンバスのビヘイビアは、マウスのボタン・位置・修飾キーとドラッグ＆ドロップの開始を BehaviorInput から読む
            // （入力の列のエンジンは読み口を差し替えて流すので、迂回して WPF から直に読むと、エンジンが見る値と製品が読む値が
            // 食い違ったまま緑になる）。拾う形は BehaviorInputReads。空振りの検知：読み口そのものの 5 つの読み方を拾える
            SourceRule.Forbid(
                "behavior-input-reads",
                "キャンバスのビヘイビアはマウスのボタン・位置・修飾キーとドラッグ＆ドロップの開始を BehaviorInput から読む",
                Src.Within("QuickER.Gui/Behaviors/")
                    .Except(
                        BehaviorInputArea,
                        "読み口そのもの（差し替えが無いときに WPF の値をそのまま返す）"
                    ),
                GuardFinder.Symbols(BehaviorInputReads.Describe)
            ) with
            {
                Witness = new(
                    SourceScope.Files("src", BehaviorInputCode),
                    [
                        new(
                            BehaviorInputCode,
                            "MouseEventArgs.LeftButton",
                            1,
                            BehaviorInputDefault
                        ),
                        new(
                            BehaviorInputCode,
                            "MouseEventArgs.MiddleButton",
                            1,
                            BehaviorInputDefault
                        ),
                        new(
                            BehaviorInputCode,
                            "MouseEventArgs.GetPosition",
                            1,
                            BehaviorInputDefault
                        ),
                        new(BehaviorInputCode, "Keyboard.Modifiers", 1, BehaviorInputDefault),
                        new(BehaviorInputCode, "DragDrop.DoDragDrop", 1, BehaviorInputDefault),
                    ]
                ),
            },
            // キャンバスのビヘイビアは、視覚ツリーを上へたどる処理を VisualAncestors の 1 か所に置き、自前でたどる写しを
            // 持たない（写しごとに止まり方・ContentElement の扱いが食い違う）。拾う形は VisualAncestorWalks。
            // 空振りの検知：VisualAncestors そのものの 1 つを拾える
            SourceRule.Forbid(
                "behavior-visual-ancestor-copies",
                "キャンバスのビヘイビアは視覚ツリーを上へたどる処理を VisualAncestors に置き、自前でたどらない",
                Src.Within("QuickER.Gui/Behaviors/")
                    .Except(VisualAncestorsArea, "たどる処理の正本そのもの"),
                GuardFinder.Symbols(VisualAncestorWalks.Describe)
            ) with
            {
                Witness = new(
                    SourceScope.Files("src", VisualAncestorsCode),
                    [
                        new(
                            VisualAncestorsCode,
                            "VisualTreeHelper.GetParent",
                            1,
                            "たどる処理の正本（走査が空振りしていないことの確認）"
                        ),
                    ]
                ),
            },
            // モーダルの親ウィンドウの解決は、2 つのダイアログサービスが DialogOwner.Resolve() を共有する（オーナーの無いモーダルは
            // モードレスの機能ウィンドウの背面へ回り込み、呼び出し元が応答待ちのまま止まる。解決そのものは UI スレッドの持ち物で
            // テストから実行できないので、両者が同じ解決を通すことだけを見張る）
            SourceRule.Require(
                "dialog-owner-resolve-uses",
                "ファイル選択とメッセージボックスは共有のオーナー解決 DialogOwner.Resolve() を通す",
                SourceScope.Files("src", FileDialogServiceCode, MessageBoxDialogServiceCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, DialogOwnerResolveForms)
                ),
                new(
                    FileDialogServiceCode,
                    "DialogOwner.Resolve()",
                    1,
                    "ファイル選択のオーナー（オーナーなしの表示へ戻らない）"
                ),
                new(
                    MessageBoxDialogServiceCode,
                    "DialogOwner.Resolve()",
                    3,
                    "警告の確認の詳細つきダイアログ・情報とエラーの詳細つきダイアログ・MessageBox の 3 つの表示の入口のオーナー"
                )
            ) with
            {
                MinimumFiles = 2,
            },
            // ファイル選択は、オーナーなしの表示（dialog.ShowDialog() == …）を残さない
            SourceRule.Forbid(
                "file-dialog-ownerless-shows",
                "ファイル選択はオーナーを渡して表示する（オーナーなしの表示を残さない）",
                SourceScope.Files("src", FileDialogServiceCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, OwnerlessFileDialogForms)
                )
            ),
            // メッセージボックスは、オーナーの解決の自前の複製を持たない（解決を 2 か所に分けると片方だけ直し忘れる）
            SourceRule.Forbid(
                "message-box-owner-resolver-copies",
                "オーナーの解決は DialogOwner.Resolve() の 1 か所に置き、自前の複製を持たない",
                SourceScope.Files("src", MessageBoxDialogServiceCode),
                GuardFinder.File(GuardReader.Text, file => FindForms(file, OwnerResolverCopyForms))
            ),
            // アプリの終了条件は App.xaml の ShutdownMode="OnMainWindowClose" の宣言（AI のウィンドウは閉じずに Hide するので、
            // 既定の OnLastWindowClose では閉じ残りで見えるウィンドウの無いプロセスが残る）
            SourceRule.Require(
                "app-shutdown-mode-declaration",
                "メインウィンドウが閉じたらアプリを終了する（閉じ残りで居座らない）",
                SourceScope.Files("src", AppXaml),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, ShutdownModeDeclarationForms)
                ),
                new GuardKnown(
                    AppXaml,
                    "ShutdownMode=\"OnMainWindowClose\"",
                    1,
                    "終了条件の正本（App.xaml の宣言 1 か所）"
                )
            ),
            // 終了条件をコード側で別の値へ上書きしない（宣言だけ正しくても実行時に変えられては意味がない）
            SourceRule.Forbid(
                "app-shutdown-mode-overrides",
                "終了条件の正本は App.xaml の宣言 1 か所（コードで ShutdownMode へ代入しない）",
                SourceScope.Files("src", AppCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, ShutdownModeOverrideForms)
                )
            ),
            // メインウィンドウを Application.MainWindow へ明示する（OnMainWindowClose が見るのは MainWindow で、WPF は最初に
            // 構築された Window を自動で入れる。初期化中に別の Window が先に作られると、それを閉じた瞬間にアプリが終わる）
            SourceRule.Require(
                "app-main-window-assignment",
                "終了条件は最初に構築された Window という暗黙の前提に乗らない（MainWindow を明示で代入する）",
                SourceScope.Files("src", AppCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, MainWindowAssignmentForms)
                ),
                new GuardKnown(
                    AppCode,
                    "MainWindow = window;",
                    1,
                    "メインウィンドウの明示の代入（ちょうど 1 か所）"
                )
            ),
            // メインウィンドウの終了時のモジュールの後始末は、受け止めを持つ FeatureModuleShutdownOnce を経由する（App は実体化
            // できないので、切り出した共有の部品を直接検証し、App がそれを使っていることをここで見張る）
            SourceRule.Require(
                "app-feature-module-shutdown-uses",
                "App の終了処理は FeatureModuleShutdownOnce を経由する",
                SourceScope.Files("src", AppCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, FeatureModuleShutdownForms)
                ),
                new(
                    AppCode,
                    "new FeatureModuleShutdownOnce(",
                    1,
                    "後始末の共有インスタンス（1 回だけ実行・失敗の受け止め）"
                ),
                new(
                    AppCode,
                    "window.Closing += (_, _) => featureModuleShutdown.CloseAll();",
                    1,
                    "メインウィンドウの Closing で共有インスタンスの後始末を呼ぶ"
                )
            ),
            // 受け止めを持たない直接の呼び出し（素のループの OnMainWindowClosing）へ戻らない
            SourceRule.Forbid(
                "app-feature-module-shutdown-bypasses",
                "モジュールの後始末は FeatureModuleShutdownOnce を通す（受け止めを持たない直接の呼び出しへ戻らない）",
                SourceScope.Files("src", AppCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, FeatureModuleShutdownBypassForms)
                )
            ),
            // UI 例外の受け皿は、再入を判断する HandleUiCrash と後始末の共有インスタンスを使う
            SourceRule.Require(
                "app-ui-crash-handler-uses",
                "App の UI 例外の受け皿は HandleUiCrash と後始末の共有インスタンスを使う",
                SourceScope.Files("src", AppCode),
                GuardFinder.Tokens(token => DescribeTokenSequence(token, UiCrashHandlerTokenForms)),
                new(
                    AppCode,
                    "CrashHandlingService.HandleUiCrash(",
                    1,
                    "UI 例外の受け皿（ダイアログ表示中の再入は終了させない）"
                ),
                new(
                    AppCode,
                    "_featureModuleShutdown?.CloseAll()",
                    1,
                    "クラッシュ経路も後始末の共有インスタンスを通す"
                )
            ),
            // 再入を判断しない HandleCrash と、無条件の Environment.Exit へ戻らない（終了は HandleUiCrash が処理を担ったときだけ）
            SourceRule.Forbid(
                "app-ui-crash-handler-bypasses",
                "終了は HandleUiCrash が処理を担ったときだけ行う（HandleCrash・無条件の Environment.Exit へ戻らない）",
                SourceScope.Files("src", AppCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, UiCrashHandlerBypassForms)
                )
            ),
            // Program は再起動の前の単一インスタンスの座の手放しを登録し、更新の適用の失敗時の代わりの再起動も起動時に登録した
            // 起動処理を使い、座を「開けない」ときは案内する（エントリポイントはテストから実行できないので、配線をソースで見張る）
            SourceRule.Require(
                "program-single-instance-uses",
                "Program は再起動の前の座の手放しを登録し、開けないときは案内する",
                SourceScope.Files("src", ProgramCode),
                GuardFinder.Nodes(DescribeSingleInstanceNode),
                new(
                    ProgramCode,
                    "ReleaseSingleInstanceBeforeRelaunch(singleInstance.Dispose)",
                    1,
                    "再起動の直前に座を手放す登録"
                ),
                new(
                    ProgramCode,
                    "RelaunchWith(WpfApplicationRestartService.StartNewInstance)",
                    1,
                    "更新の適用に失敗したときの代わりの再起動も、起動時に登録した起動処理を使う"
                ),
                new(
                    ProgramCode,
                    "outcome == SingleInstanceOutcome.Inaccessible",
                    1,
                    "座を開けないときの案内"
                )
            ),
            // 結果を受け取らない TryAcquire() の形へ戻らない
            SourceRule.Forbid(
                "program-single-instance-bypasses",
                "単一インスタンスの取得は結果（取得・既存・開けない）を受け取る",
                SourceScope.Files("src", ProgramCode),
                GuardFinder.File(
                    GuardReader.Text,
                    file => FindForms(file, SingleInstanceBypassForms)
                )
            ),
        ];

    /// <summary>固定の文字列の形（何かはその文字列）</summary>
    /// <summary>トークンから始まる並びが形のどれかと一致すれば、並びをつないだ文字列を返す（トリビア＝コメントは並びに入らない）</summary>
    private static string? DescribeTokenSequence(SyntaxToken token, string[][] forms)
    {
        foreach (var form in forms)
        {
            var current = token;
            var matched = true;

            foreach (var text in form)
            {
                if (!string.Equals(current.Text, text, StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }

                current = current.GetNextToken();
            }

            if (matched)
            {
                return string.Concat(form);
            }
        }

        return null;
    }

    /// <summary>単一インスタンスの配線の必須の呼び出しと比較の節なら、その表記を返す</summary>
    private static string? DescribeSingleInstanceNode(SyntaxNode node)
    {
        switch (node)
        {
            case InvocationExpressionSyntax invocation:
            {
                var name = invocation.Expression is MemberAccessExpressionSyntax access
                    ? access.Name
                    : invocation.Expression;
                var call = TokenText(name) + TokenText(invocation.ArgumentList);

                return SingleInstanceCallForms.Contains(call) ? call : null;
            }
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression):
            {
                var comparison = $"{TokenText(binary.Left)} == {TokenText(binary.Right)}";

                return string.Equals(
                    comparison,
                    SingleInstanceInaccessibleForm,
                    StringComparison.Ordinal
                )
                    ? comparison
                    : null;
            }
            default:
                return null;
        }
    }

    /// <summary>節のトークンを空白なしでつなぐ（トリビア＝コメント・空白を読まない）</summary>
    private static string TokenText(SyntaxNode node) =>
        string.Concat(node.DescendantTokens().Select(token => token.Text));

    private static (string What, Regex Pattern) FixedText(string text) =>
        (text, new Regex(Regex.Escape(text), RegexOptions.CultureInvariant));

    /// <summary>正規表現の形（何かは読みやすい表記）</summary>
    private static (string What, Regex Pattern) TextPattern(
        string what,
        string pattern,
        RegexOptions options = RegexOptions.None
    ) => (what, new Regex(pattern, options | RegexOptions.CultureInvariant));

    /// <summary>本文の形の出現（重ならない数え方・本文の位置の順。何かは形の表記）</summary>
    private static IEnumerable<GuardFinding> FindForms(
        GuardFile file,
        IEnumerable<(string What, Regex Pattern)> forms
    ) =>
        forms
            .SelectMany(form =>
                form.Pattern.Matches(file.Text).Select(match => (match.Index, form.What))
            )
            .OrderBy(hit => hit.Index)
            .Select(hit => new GuardFinding(hit.What, file.LineAt(hit.Index)));

    /// <summary>XAML の DataGrid（<paramref name="editableOnly"/> なら行の追加・削除を明示で切っていないものだけ）</summary>
    private static IEnumerable<GuardFinding> FindDataGrids(GuardFile file, bool editableOnly) =>
        XDocument
            .Parse(file.Text, LoadOptions.SetLineInfo)
            .Descendants()
            .Where(element => element.Name.LocalName == "DataGrid")
            .Where(element =>
                !editableOnly
                || (string?)element.Attribute("CanUserAddRows") != "False"
                || (string?)element.Attribute("CanUserDeleteRows") != "False"
            )
            .Select(element => new GuardFinding(
                editableOnly
                    ? $"行の追加・削除を切っていない DataGrid（x:Name={(string?)element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")}）"
                    : "DataGrid",
                ((IXmlLineInfo)element).LineNumber
            ));
}
