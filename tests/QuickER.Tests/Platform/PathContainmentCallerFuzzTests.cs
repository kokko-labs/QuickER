using System.IO;
using AwesomeAssertions;
using GitHub.Copilot;
using QuickER.AI;
using QuickER.AI.Mock;
using QuickER.Platform;

namespace QuickER.Tests.Platform;

/// <summary>
/// 包含の判定の表（<see cref="PathContainmentRow.All"/>）を、フォルダの中かで可否を決める呼び出し元へそれぞれの入力の形で流し、
/// 表と同じ結論になることを固定する
/// </summary>
/// <remarks>
/// 呼び出し元が自分で前方一致を書くと、末尾の区切りの扱いが呼び出し元ごとに食い違う（出力フォルダがドライブ直下のとき、
/// Copilot の自動承認だけが配下を 1 件も承認しなかった）。呼び出し元が <see cref="PathContainment"/> を通していることを、
/// 同じ表で確かめる。
/// </remarks>
public sealed class PathContainmentCallerFuzzTests
{
    public static TheoryData<PathContainmentRow> Rows() => new(PathContainmentRow.All);

    [Theory(DisplayName = "Copilot の自動承認：読み書きの対象が作業フォルダと同じか中なら承認する")]
    [MemberData(nameof(Rows))]
    public void Copilot_FollowsTable(PathContainmentRow row)
    {
        if (string.IsNullOrEmpty(row.Root))
        {
            // 作業フォルダが空なら何も承認しない（基準フォルダを作れない）
            CopilotWorkspacePermissionPolicy
                .IsWithinWorkspace(Write(row.Path), string.Empty)
                .Should()
                .BeFalse();
            return;
        }

        var root = CopilotWorkspacePermissionPolicy.NormalizeRoot(row.Root);

        CopilotWorkspacePermissionPolicy
            .IsWithinWorkspace(Write(row.Path), root)
            .Should()
            .Be(row.SameOrUnder, row.ToString());
        CopilotWorkspacePermissionPolicy
            .IsWithinWorkspace(
                new PermissionRequestRead { Path = row.Path, Intention = "test" },
                root
            )
            .Should()
            .Be(row.SameOrUnder, row.ToString());
    }

    [Theory(
        DisplayName = "削除してよいかの重なりの判定：同じか中なら重なる・正規化できなければ重なる側へ倒す"
    )]
    [MemberData(nameof(Rows))]
    public void PathsOverlap_FollowsTable(PathContainmentRow row)
    {
        if (row.IsUnc)
        {
            // 実体を辿るので、存在しないサーバーの問い合わせで待たされる
            return;
        }

        MockProjectFiles
            .PathsOverlap(row.Path, row.Root)
            .Should()
            .Be(row.SameOrUnder || row.Invalid, row.ToString());
    }

    [Theory(
        DisplayName = "モックのプレビュー：サブリソースはフォルダ直下だけ・遷移は直下の .html だけ"
    )]
    [MemberData(nameof(Rows))]
    public void Preview_FollowsTable(PathContainmentRow row)
    {
        if (!Uri.TryCreate(row.Path, UriKind.Absolute, out var target))
        {
            // URI にできない値はプレビューへ届かない
            return;
        }

        MockPreviewPanel
            .IsResourceAllowed(target, row.Root)
            .Should()
            .Be(row.DirectChild, row.ToString());
        MockPreviewPanel
            .IsNavigationAllowed(target, row.Root)
            .Should()
            .Be(
                row.DirectChild && row.Path.EndsWith(".html", StringComparison.Ordinal),
                row.ToString()
            );
    }

    [Theory(
        DisplayName = "API キー方式の提出：出力フォルダがドライブ直下・UNC の共有の直下でも、中への提出を受け付ける"
    )]
    [MemberData(nameof(Roots))]
    public void EmitPath_AcceptsSubmissionUnderEveryRootForm(string root)
    {
        var result = MockProjectEmitTools.ResolveEmitPath(
            root,
            MockProjectTargetProfile.Blazor,
            "MockApp/Components/Pages/Home.razor"
        );

        result.Ok.Should().BeTrue(result.Error);
        result
            .FullPath.Should()
            .Be(
                Path.Combine(
                    PathContainment.Normalize(root),
                    "MockApp",
                    "Components",
                    "Pages",
                    "Home.razor"
                )
            );
    }

    public static TheoryData<string> Roots() =>
        new(
            PathContainmentRow
                .All.Select(row => row.Root)
                .Where(root => root.Length > 0)
                .Distinct(StringComparer.Ordinal)
        );

    private static PermissionRequestWrite Write(string fileName) =>
        new()
        {
            FileName = fileName,
            Diff = string.Empty,
            Intention = "test",
            CanOfferSessionApproval = false,
        };
}
