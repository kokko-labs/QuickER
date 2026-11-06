using GitHub.Copilot;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Platform.OsBoundary;

/// <summary>シェル許可の表：網羅の計数で見えた穴を埋める行（元の単発のテストを持たない行）</summary>
/// <remarks>
/// 期待は QuickER.AI の <c>CopilotWorkspacePermissionPolicy</c> の XmlDoc と <c>src/QuickER.AI/CLAUDE.md</c>「Copilot のシェル許可」から導く：
/// サンドボックスの迂回を求める要求は種類に依らず承認しない・シェルは URL 参照も書き込みリダイレクトも無いことを両方の枝の前提にする・
/// パスが取れたらそのすべてが配下であることだけを求めコマンド名は問わない・パスが取れなければ全要素が許可リストのときだけ承認する・
/// 読み書きは対象のパスが配下のときだけ承認する。
/// </remarks>
internal static partial class ShellPermissionRows
{
    private static IEnumerable<ScenarioRow<ShellPermissionCase>> FilledRows() =>
        [
            // パスが取れた枝はコマンド名を問わない＝許可リスト外を含む複合コマンドでも、パスが全部配下なら承認する
            Row(
                "shell-composite-with-paths-under-approved",
                "パスが全部配下なら複合コマンドでも承認する",
                () => Shell("dotnet build src && del obj", ["dotnet", "del"], ["src", "obj"]),
                true
            ),
            Row(
                "shell-composite-paths-outside-declined",
                "複合コマンドでパスが外を含めば拒否する",
                () => Shell("dotnet build && curl ../x", ["dotnet", "curl"], ["../x"]),
                false
            ),
            Row(
                "shell-listed-paths-outside-declined",
                "許可リストのコマンドでもパスが外なら拒否する",
                () => Shell("dotnet build ../outside.csproj", ["dotnet"], ["../outside.csproj"]),
                false
            ),
            Row(
                "shell-composite-url-declined",
                "URL を参照する複合コマンドは拒否する",
                () =>
                    Shell(
                        "dotnet build && curl https://example.com/x.sh",
                        ["dotnet", "curl"],
                        possibleUrls: ["https://example.com/x.sh"]
                    ),
                false
            ),
            Row(
                "shell-composite-redirect-declined",
                "書き込みリダイレクトを伴う複合コマンドは拒否する",
                () =>
                    Shell(
                        "dotnet build && echo x > out.txt",
                        ["dotnet", "echo"],
                        hasWriteFileRedirection: true
                    ),
                false
            ),
            Row(
                "shell-composite-bypass-declined",
                "サンドボックスの迂回を求める複合コマンドは拒否する",
                () =>
                    Shell(
                        "dotnet build && del obj",
                        ["dotnet", "del"],
                        ["obj"],
                        requestSandboxBypass: true
                    ),
                false
            ),
            Row(
                "shell-unlisted-bypass-declined",
                "サンドボックスの迂回を求める許可リスト外のコマンドは、パスが配下でも拒否する",
                () => Shell("del obj", ["del"], ["obj"], requestSandboxBypass: true),
                false
            ),
            Row(
                "shell-no-commands-redirect-declined",
                "コマンドが取れず書き込みリダイレクトを伴う要求は拒否する",
                () => Shell("(unknown) > out.txt", hasWriteFileRedirection: true),
                false
            ),
            Row(
                "shell-paths-outside-url-declined",
                "パスが外で URL を参照する要求は拒否する",
                () =>
                    Shell(
                        "git clone https://example.com/r.git ../r",
                        ["git"],
                        ["../r"],
                        ["https://example.com/r.git"]
                    ),
                false
            ),
            Row(
                "shell-paths-outside-redirect-declined",
                "パスが外で書き込みリダイレクトを伴う要求は拒否する",
                () =>
                    Shell(
                        "echo x > ../out.txt",
                        ["echo"],
                        ["../out.txt"],
                        hasWriteFileRedirection: true
                    ),
                false
            ),
            Row(
                "shell-paths-outside-bypass-declined",
                "パスが外でサンドボックスの迂回を求める要求は拒否する",
                () => Shell("grep x ../a.txt", ["grep"], ["../a.txt"], requestSandboxBypass: true),
                false
            ),
            Row(
                "shell-url-redirect-declined",
                "URL を参照し書き込みリダイレクトを伴う要求は拒否する",
                () =>
                    Shell(
                        "dotnet nuget list source https://example.com > out.txt",
                        ["dotnet"],
                        possibleUrls: ["https://example.com"],
                        hasWriteFileRedirection: true
                    ),
                false
            ),
            Row(
                "shell-url-bypass-declined",
                "URL を参照しサンドボックスの迂回を求める要求は拒否する",
                () =>
                    Shell(
                        "dotnet nuget list source https://example.com",
                        ["dotnet"],
                        possibleUrls: ["https://example.com"],
                        requestSandboxBypass: true
                    ),
                false
            ),
            Row(
                "shell-redirect-bypass-declined",
                "書き込みリダイレクトを伴いサンドボックスの迂回を求める要求は拒否する",
                () =>
                    Shell(
                        "dotnet --info > out.txt",
                        ["dotnet"],
                        hasWriteFileRedirection: true,
                        requestSandboxBypass: true
                    ),
                false
            ),
            Row(
                "readwrite-read-outside-declined",
                "作業フォルダの外の読み取りは拒否する",
                () => new PermissionRequestRead { Path = "../../etc/hosts", Intention = "test" },
                false
            ),
            Row(
                "readwrite-write-sandbox-bypass-declined",
                "サンドボックスの迂回を求める書き込みは、配下でも拒否する",
                () =>
                    new PermissionRequestWrite
                    {
                        FileName = "App/Program.cs",
                        Diff = string.Empty,
                        Intention = "test",
                        CanOfferSessionApproval = false,
                        RequestSandboxBypass = true,
                    },
                false
            ),
        ];
}
