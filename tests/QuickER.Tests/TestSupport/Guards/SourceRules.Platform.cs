using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using QuickER.Platform;

namespace QuickER.Tests.TestSupport.Guards;

/// <summary>ソースの規則の表：OS との境界（QuickER.Platform の正本を外で写さない）</summary>
internal static partial class SourceRules
{
    /// <summary>保存先ではなく、子プロセスの作業フォルダとして使う</summary>
    private const string WorkingDirectory = "子プロセスの作業フォルダ（そこへは何も書かない）";

    /// <summary>一時フォルダの一意の名前のファイル（使い終わったら消す）</summary>
    private const string TemporaryFile =
        "%TEMP% の一意の名前の一時ファイル（子プロセスへ渡すために書き、使い終わったら消す）";

    /// <summary>読めなかった設定の退避</summary>
    private const string CorruptBackup =
        "読めなかった設定ファイルを脇へ写す（失敗しても元のファイルは残る。写し先は退避専用の名前）";

    /// <summary>依存ゼロのランタイムのパッケージ</summary>
    private const string RuntimePackage =
        "依存ゼロのランタイムのパッケージは QuickER.Platform を参照できない（自前で一時ファイル→置き換えをする・同期の一時ファイル）";

    /// <summary>自分が作った一時物の後片付け</summary>
    private const string OwnTemporaryCleanup =
        "自分が作った一時物（一意の名前の一時ファイル・作業フォルダ）を使い終わって消す";

    /// <summary>空の値の保存＝保存済みの秘密を消す</summary>
    private const string SecretRemoval =
        "利用者が空の値を保存した（保存をやめた）ので、保存済みの暗号ファイルを消す";

    /// <summary>保存先になりうるフォルダ（数える <c>GetFolderPath</c> の引数）</summary>
    private static readonly HashSet<string> StorageFolders =
    [
        nameof(Environment.SpecialFolder.LocalApplicationData),
        nameof(Environment.SpecialFolder.ApplicationData),
        nameof(Environment.SpecialFolder.UserProfile),
    ];

    /// <summary>パスの名前の判定に使う BCL の呼び出し</summary>
    private static readonly HashSet<string> PathNameCalls =
    [
        nameof(Path.GetInvalidFileNameChars),
        nameof(Path.GetInvalidPathChars),
        nameof(Path.IsPathRooted),
    ];

    private static IEnumerable<SourceRule> Platform() =>
        [
            // アプリ設定・作業状態と一時ファイルの置き場所は AppDataPaths だけが決める。置き場所の算出が呼び出し側に写されると、
            // 1 か所だけローミング（%APPDATA%）へ戻る・フォルダ名が変わる食い違いが、どのテストにも引っかからずに入る
            // （保存先を差し替えるテストの経路は、既定の保存先を通らない）。GetFolderPath は保存先になりうるフォルダだけを数え
            // （Desktop・MyDocuments のような既定の表示フォルダは数えない）、Path.GetTempPath は全部数える
            SourceRule.Forbid(
                "app-data-path-lookups",
                "保存先・一時フォルダの算出は AppDataPaths を使い、例外は理由つきで一覧に載せる",
                SrcOutsidePlatform,
                GuardFinder.Nodes(DescribeFolderLookup),
                new(
                    "src/QuickER.AI/CodexConfigTomlReader.cs",
                    "GetFolderPath(UserProfile)",
                    1,
                    "利用者のホームにある codex の設定を読む（QuickER の保存先ではない）"
                ),
                new("src/QuickER.AI/ClaudeCodeClient.cs", "GetTempPath", 1, WorkingDirectory),
                new("src/QuickER.AI.Mock/DotnetBuildRunner.cs", "GetTempPath", 2, WorkingDirectory),
                new(
                    "src/QuickER.Runtime.Sync/Runtime.Sync.g.cs",
                    "GetTempPath",
                    1,
                    "依存ゼロのランタイムのパッケージは QuickER.Platform を参照できない（同期の blob の一時ファイル）"
                )
            ),
            // パスの 1 区間・相対パスの妥当性の判定は PathSegments だけが持つ。「フルパスか」を見る IsPathFullyQualified と、
            // 正規化した結果がフォルダの中かを見る GetFullPath は名前の判定ではないので数えない
            SourceRule.Forbid(
                "path-name-checks",
                "パスの名前の判定は PathSegments の CheckSegment / CheckRelativePath を使う",
                SrcOutsidePlatform,
                GuardFinder.Nodes(node =>
                    node
                        is InvocationExpressionSyntax
                        {
                            Expression: MemberAccessExpressionSyntax member
                        }
                    && PathNameCalls.Contains(member.Name.Identifier.ValueText)
                        ? member.Name.Identifier.ValueText
                        : null
                )
            ),
            // UTF-8 の文字コードのインスタンスは TextEncodings だけが作る。呼び出し側で作ると 1 か所だけ BOM の有無や不正な
            // サロゲートの扱いが食い違い、書き出しと同じバイト列を作る側（書き出した内容のハッシュ）が黙って一致しなくなる。
            // BOM 付きの Encoding.UTF8 は BCL の既存のインスタンスなので数えない
            SourceRule.Forbid(
                "utf8-encoding-creations",
                "UTF-8 の文字コードは TextEncodings の Utf8NoBom か Utf8NoBomLenient を使う",
                SrcOutsidePlatform,
                GuardFinder.Nodes(node => CreatesUtf8Encoding(node) ? "new UTF8Encoding" : null)
            ),
            // パスがフォルダの中にあるかを、文字列の前方一致として書かない（PathContainment を通す）。拾う形は PathPrefixCalls。
            // 空振りの検知：QuickER.Platform の PathContainment 自身の前方一致はちょうど 1 つ拾える
            SourceRule.Forbid(
                "path-prefix-comparisons",
                "フォルダの中にあるかは PathContainment で判定する",
                SrcOutsidePlatform,
                GuardFinder.Symbols(PathPrefixCalls.Describe)
            ) with
            {
                Witness = new(
                    SourceScope.Files("src", "src/QuickER.Platform/PathContainment.cs"),
                    [
                        new(
                            "src/QuickER.Platform/PathContainment.cs",
                            "string.StartsWith",
                            1,
                            "包含の判定の正本そのものの前方一致（走査が空振りしていないことの確認）"
                        ),
                    ]
                ),
            },
            // 外部のプロセス・URL・フォルダを開く処理は ExternalLauncher を通す。拾う形は ProcessLaunchCalls。
            // QuickER がプロセスを作らないもの（GitHub.Copilot.SDK の CLI の起動・Velopack の更新・WebView2 の子プロセス）は対象外
            SourceRule.Forbid(
                "process-launches",
                "プロセス・URL・フォルダを開くのは ExternalLauncher を通す",
                SrcOutsidePlatform,
                GuardFinder.Symbols(ProcessLaunchCalls.Describe)
            ),
            // 走査は ExternalLauncher.CreateProcess の戻り値の起動を許す（起動の前の規則は CreateProcess が確かめる）。
            // いまの 4 か所を許す側で拾えていること（許す判定が空振りしていない）
            SourceRule.Require(
                "process-launch-create-process-starts",
                "CreateProcess の戻り値の起動は、いまの 4 か所を許す側で拾う",
                SrcOutsidePlatform,
                GuardFinder.Symbols(
                    (model, node) =>
                        node is InvocationExpressionSyntax invocation
                        && ProcessLaunchCalls.IsLauncherProcessStart(model, invocation)
                            ? "start"
                            : null
                ),
                new(
                    "src/QuickER.AI/ClaudeCodeClient.cs",
                    "start",
                    2,
                    "CLI の起動（CreateProcess の戻り値を起動する）"
                ),
                new(
                    "src/QuickER.AI/CodexAppServerClient.cs",
                    "start",
                    1,
                    "app-server の起動（CreateProcess の戻り値を起動する）"
                ),
                new(
                    "src/QuickER.AI.Mock/DotnetBuildRunner.cs",
                    "start",
                    1,
                    "dotnet build の起動（CreateProcess の戻り値を起動する）"
                )
            ),
            // ファイルへの書き込み・削除は AtomicFile を通す。拾う形は FileWriteCalls
            SourceRule.Forbid(
                "raw-file-writes",
                "ファイルへの書き込みは AtomicFile を通し、例外は理由つきで一覧に載せる",
                SrcOutsidePlatform,
                GuardFinder.Symbols(FileWriteCalls.Describe),
                new(
                    "src/QuickER.AI/ClaudeCodeChatEngine.cs",
                    "File.WriteAllBytes",
                    1,
                    TemporaryFile
                ),
                new(
                    "src/QuickER.AI/ClaudeCodeChatEngine.cs",
                    "File.WriteAllText",
                    1,
                    TemporaryFile
                ),
                new("src/QuickER.AI/ClaudeCodeClient.cs", "File.WriteAllText", 1, TemporaryFile),
                new(
                    "src/QuickER.Gui/Services/CrashHandlingService.cs",
                    "new FileStream",
                    1,
                    "クラッシュログは一意の名前を CreateNew で作る（既存を上書きしない＝壊す相手が無い）"
                ),
                new(
                    "src/QuickER.Db.UI/SqlConnectionProfileStore.cs",
                    "File.Copy",
                    1,
                    CorruptBackup
                ),
                new("src/QuickER.Settings/JsonSettingsStore.cs", "File.Copy", 1, CorruptBackup),
                new("src/QuickER.Runtime/Runtime.g.cs", "new FileStream", 1, RuntimePackage),
                new("src/QuickER.Runtime/Runtime.g.cs", "File.Move", 2, RuntimePackage),
                new("src/QuickER.Runtime/Runtime.g.cs", "File.Replace", 1, RuntimePackage),
                new(
                    "src/QuickER.Runtime.Sync/Runtime.Sync.g.cs",
                    "new FileStream",
                    1,
                    RuntimePackage
                ),
                new("src/QuickER.Runtime/Runtime.g.cs", "File.Delete", 1, RuntimePackage),
                new("src/QuickER.Runtime.Sync/Runtime.Sync.g.cs", "File.Delete", 1, RuntimePackage),
                new(
                    "src/QuickER.AI/BackendWorkingDirectory.cs",
                    "Directory.Delete",
                    1,
                    OwnTemporaryCleanup
                ),
                new("src/QuickER.AI/ClaudeCodeClient.cs", "File.Delete", 1, OwnTemporaryCleanup),
                new(
                    "src/QuickER.AI.Mock/MockFolderStore.cs",
                    "File.Delete",
                    1,
                    "remove_screen が、ツールの名指しした画面の HTML をモックフォルダから消す（名前は ValidateScreenFileName を通る）"
                ),
                new(
                    "src/QuickER.AI.Mock/MockProjectFiles.cs",
                    "DirectoryInfo.Delete",
                    2,
                    "最終ビルドの前に、プロジェクトの obj / bin を消す（リンクはリンクだけを外す。ビルド前の変更検知の範囲と揃える）"
                ),
                new("src/QuickER.AI.UI/ApiKeyStore.cs", "File.Delete", 1, SecretRemoval),
                new(
                    "src/QuickER.Db.UI/SqlConnectionProfileStore.cs",
                    "File.Delete",
                    1,
                    SecretRemoval
                )
            ),
        ];

    /// <summary>保存先・一時フォルダの算出の呼び出しなら、その表記（例 <c>GetFolderPath(UserProfile)</c>）を返す</summary>
    private static string? DescribeFolderLookup(SyntaxNode node)
    {
        if (
            node
            is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax member
            } invocation
        )
        {
            return null;
        }

        var name = member.Name.Identifier.ValueText;

        if (name == nameof(Path.GetTempPath))
        {
            return name;
        }

        if (name != nameof(Environment.GetFolderPath))
        {
            return null;
        }

        var folder = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            _ => null,
        };

        return folder is not null && StorageFolders.Contains(folder) ? $"{name}({folder})" : null;
    }

    /// <summary>節が UTF8Encoding のインスタンスを作るか</summary>
    private static bool CreatesUtf8Encoding(SyntaxNode node) =>
        node switch
        {
            ObjectCreationExpressionSyntax creation => creation.Type.ToString().Split('.')[^1]
                == nameof(UTF8Encoding),
            ImplicitObjectCreationExpressionSyntax implicitCreation => DeclaredType(
                implicitCreation
            )
                ?.ToString()
                .Split('.')[^1] == nameof(UTF8Encoding),
            _ => false,
        };

    /// <summary>型を省いた <c>new(</c> が初期化する変数・プロパティの宣言の型</summary>
    private static TypeSyntax? DeclaredType(ImplicitObjectCreationExpressionSyntax creation) =>
        creation.Parent?.Parent switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } =>
                declaration.Type,
            PropertyDeclarationSyntax property => property.Type,
            _ => null,
        };
}
