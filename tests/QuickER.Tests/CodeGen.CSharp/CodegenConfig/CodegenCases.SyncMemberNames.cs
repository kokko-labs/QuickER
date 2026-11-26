using QuickER.CodeGen.CSharp;
using QuickER.CodeGen.CSharp.Resources;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.CodeGen.CSharp.CodegenConfig.Judges;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.CodeGen.CSharp.CodegenConfig;

/// <summary>
/// 決まった行：同期の記述子の一覧 <c>GeneratedSyncTables</c> のメンバー名のぶつかり。記述子のプロパティ名（リポジトリ名）が
/// 固定のメンバー（一覧 <c>All</c> と、object から継ぐメンバー）と同じになる表は、同期を生成するときだけ名指しのエラーにする
/// </summary>
internal static partial class CodegenCases
{
    /// <summary>
    /// 記述子のプロパティ名が固定のメンバーと同じになる表（表の名前・行の Id の接尾辞）。<c>all</c> と <c>alls</c> は
    /// 単数化で同じリポジトリ名 <c>All</c> になる（止めないと CS0102）。残りは object から継ぐメンバーの名前になる表
    /// （止めないと CS0108 の警告）
    /// </summary>
    /// <remarks>
    /// object の <c>Equals</c>・<c>ReferenceEquals</c> は、単数化（末尾の s を外し、ss は残す）がリポジトリ名に作らないので
    /// 載せない（<c>equals</c> は <c>Equal</c>、<c>equalss</c> は <c>Equalss</c> になる）。
    /// </remarks>
    private static IReadOnlyList<(string Table, string Suffix)> SyncMember_ReservedTables =>
        [
            ("all", "all"),
            ("alls", "alls"),
            ("to_string", "to-string"),
            ("get_hash_code", "get-hash-code"),
            ("get_type", "get-type"),
            ("memberwise_clone", "memberwise-clone"),
            ("finalize", "finalize"),
        ];

    /// <summary>
    /// 型名の行と同じ構成（<see cref="Service_TypeNameOptions"/>）から、EF Core を外して（マルチターゲットと排他）
    /// sqlserver+sqlite の QuickER 版 Repository と同期を足したもの
    /// </summary>
    private static CodeGenerationOptions SyncMember_Options(string output) =>
        Service_TypeNameOptions(output) with
        {
            GenerateEfCoreRepositories = false,
            RepositoryDialects = [DialectNames.SqlServer, DialectNames.Sqlite],
            GenerateSyncSupport = true,
        };

    /// <summary>
    /// 同期の記述子のメンバー名の行：固定のメンバーとぶつかる表は出力の形に依らずエラーで名指しし、同じ表でも同期を生成しない
    /// 構成と、ぶつからない名前の表は、エラーなしでコンパイルできる（警告 0）
    /// </summary>
    private static IEnumerable<ScenarioRow<CodegenCase>> SyncMemberNameRows()
    {
        foreach (var output in Service_Outputs)
        {
            foreach (var (table, suffix) in SyncMember_ReservedTables)
            {
                yield return Service_NameRow(
                    $"diag-sync-member-name-reserved-{suffix}-{output}",
                    $"同期の記述子の名前: 表 {table} の記述子のプロパティ名は GeneratedSyncTables の固定のメンバーと同じになるので、"
                        + $"同期を生成するときは表を名指しするエラーになる（出力 {output}）",
                    () =>
                        Service_SqlServerDiagram(Service_IdTable("orders"), Service_IdTable(table)),
                    SyncMember_Options(output),
                    DiagnosticExpectation.Contains(
                        ExpectedDiagnostic.ErrorMentioning(
                            nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision),
                            "GeneratedSyncTables",
                            $"'{table}'"
                        )
                    )
                );
            }

            yield return Service_NameRow(
                $"compile-sync-member-names-distinct-{output}",
                "同期の記述子の名前: 固定のメンバーに近いがぶつからない名前の表（all_items・alley・to_strings_log・get_types_log）は、"
                    + $"同期を生成してもメンバー名の診断なしでコンパイルできる（出力 {output}）",
                () =>
                    Service_SqlServerDiagram(
                        Service_IdTable("all_items"),
                        Service_IdTable("alley"),
                        Service_IdTable("to_strings_log"),
                        Service_IdTable("get_types_log")
                    ),
                SyncMember_Options(output),
                DiagnosticExpectation
                    .Contains()
                    .Without(
                        ExpectedDiagnostic.ErrorAnyItems(
                            nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision)
                        )
                    ),
                compile: true
            );
        }

        yield return Service_NameRow(
            "compile-sync-member-reserved-names-without-sync-support",
            "同期の記述子の名前: 固定のメンバーとぶつかる名前の表も、同期を生成しない構成ではメンバー名の診断なしでコンパイルできる",
            () =>
                Service_SqlServerDiagram([
                    Service_IdTable("orders"),
                    .. SyncMember_ReservedTables
                        .Where(reserved => reserved.Table != "alls")
                        .Select(reserved => Service_IdTable(reserved.Table)),
                ]),
            SyncMember_Options("single") with
            {
                GenerateSyncSupport = false,
            },
            DiagnosticExpectation
                .Contains()
                .Without(
                    ExpectedDiagnostic.ErrorAnyItems(
                        nameof(Strings.CodeGen_Error_GeneratedMemberNameCollision)
                    )
                ),
            compile: true
        );
    }
}
