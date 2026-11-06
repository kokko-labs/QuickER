namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：src・ワークフロー・スクリプト・生成テンプレートが名指すテストの名前と行 Id の実在</summary>
/// <remarks>
/// 規則は tests/ の実体（宣言とソースの文字列）と照合するので、実在する名前はこの表の外にある本物を使い、架空の行 Id は
/// tests/ のソースに現れないよう語をハイフンなしで分けて実行時につなぐ（このファイルも tests/ のソースに入る）。
/// </remarks>
internal static partial class SourceRuleProbes
{
    /// <summary>tests/ のどこにも無い行 Id（接頭辞は実在の行 Id と同じ <c>sync</c>）</summary>
    private static readonly string FictitiousRowId = string.Join(
        "-",
        "sync",
        "dialog",
        "zz",
        "missing"
    );

    /// <summary>実在する行 Id</summary>
    private const string RealRowId =
        "sync-dialog-execute-success-then-refresh-failure-stays-open-and-shows-error";

    private static IEnumerable<SourceRuleProbe> Comments() =>
        [
            // コメントの架空のテストの名前・メソッド・行 Id を拾い、実在する名前・行 Id・製品のテスト用メンバー（…ForTests）・
            // パターン・コードの中の名前は拾わない
            new SourceRuleProbe(
                "comment-test-names-src-forms",
                "comment-test-names-src",
                [
                    new ProbeFile(
                        "Probe.cs",
                        "/// <summary>固定は <c>SourceRuleTests</c>・<c>NoSuchGuardTests</c>・<c>ResetForTests</c>・*AnyPatternTests</summary>\n"
                            + "/// <remarks>行 <c>"
                            + RealRowId
                            + "</c> と行 <c>"
                            + FictitiousRowId
                            + "</c>・<c>SourceRuleTests.NoSuchMethod</c></remarks>\n"
                            + "internal static class Probe\n{\n"
                            + "    // 照合は {Source,Missing}RuleTests\n"
                            + "    private const string Name = \"NoSuchCodeTests\";\n"
                            + "    /* ブロック NoSuchBlockTests */\n}\n",
                        "NoSuchGuardTests",
                        // 同じ行の中では、テストの名前を先に、行 Id を後に返す
                        "SourceRuleTests.NoSuchMethod",
                        "行 " + FictitiousRowId,
                        "MissingRuleTests",
                        "NoSuchBlockTests"
                    ),
                ]
            ),
            // 条件コンパイルの外れる分岐の中のコメントも拾う
            new SourceRuleProbe(
                "comment-test-names-src-inactive-branch",
                "comment-test-names-src",
                [
                    new ProbeFile(
                        "Probe.cs",
                        "internal static class Probe\n{\n#if QUICKER_PROBE_NEVER\n"
                            + "    // 固定は NoSuchInactiveTests\n"
                            + "    private const int X = 1;\n#endif\n}\n",
                        "NoSuchInactiveTests"
                    ),
                ]
            )
            {
                InactiveBranch = true,
            },
            // 本文の全部を読む規則（ワークフロー）は、コメントと絞り込みの文字列の架空のテストの名前を拾い、
            // QuickER.Tests のようなプロジェクト名は拾わない
            new SourceRuleProbe(
                "comment-test-names-workflows-forms",
                "comment-test-names-workflows",
                [
                    new ProbeFile(
                        "ci.yml",
                        "# 固定は ParityTests と NoSuchWorkflowTests\n"
                            + "run: dotnet test tests/QuickER.Tests/QuickER.Tests.csproj --filter \"FullyQualifiedName~NoSuchFilterTests\"\n",
                        "NoSuchWorkflowTests",
                        "NoSuchFilterTests"
                    ),
                ]
            ),
        ];
}
