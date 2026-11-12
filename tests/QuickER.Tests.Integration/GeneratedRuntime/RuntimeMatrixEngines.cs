using QuickER.Dialects;
using QuickER.Tests.CodeGen.CSharp;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしを 1 つのテストの中で比べるエンジンの登録。エンジンの担当の実装先は手で書かず、エンジンの実装先の軸から導く
/// </summary>
/// <param name="ClassName">エンジンのテストのクラスの名前</param>
/// <param name="Feature">機能の名前</param>
/// <param name="ScenarioGroups">
/// 実装先の一致（<see cref="BackendParityRuntimeTests"/>）のシナリオの群の名前（<see cref="RuntimeMatrixEngines.ScenarioGroups"/>）。
/// 群のシナリオが流れる実装先の和が担当になる。null はエンジンの軸の全部の実装先
/// </param>
/// <param name="Engine">
/// 担当に数える実装先の生成の実装（null は全部）。ほかの実装先は比べる相手として流れるだけで、その機能を持たない
/// （例：EF Core の実行戦略のシナリオは QuickER 版でも流れるが、実行戦略は EF Core の機構）
/// </param>
public sealed record RuntimeEngineRegistration(
    string ClassName,
    string Feature,
    IReadOnlyList<string>? ScenarioGroups = null,
    RuntimeEngineKind? Engine = null
);

/// <summary>エンジンの登録と、エンジンの実装先の軸から担当のセルを導く関数</summary>
/// <remarks>
/// 実装先の一致は <see cref="ParityBackendAdapter.Target"/>、簡易 DSL の総当たりは構成の方言と実装先の軸の名前
/// （<see cref="DslFuzzBackends"/>）から実装先を引く。軸に実装先を足すと、アダプタが実装先を書くまで軸が作れない
/// （<c>MatrixAxis</c>）ので、担当の実装先を手で書き足す場所は無い。
/// </remarks>
public static class RuntimeMatrixEngines
{
    /// <summary>実装先の一致のシナリオの群（名前 → シナリオの名前）</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ScenarioGroups { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // 一意制約に断られる書き込み（挿入・更新・グラフ保存）が何も書かない
            ["UniquenessEnforcement"] =
            [
                "constraint-violations",
                "unique-update-collides-or-keeps-own-value",
                "unique-graph-save-duplicate-is-refused",
            ],
            // 版の無い表で文が当たらない書き込み
            ["IgnoredWrite"] =
            [
                "ignored-write-update-returns-false-optimistic",
                "ignored-write-update-returns-false-force-overwrite",
                "ignored-write-save-reports-not-found-optimistic",
                "ignored-write-save-reports-not-found-force-overwrite",
            ],
            // Save フック（5 つの実装先で流れる）
            ["SaveHook"] =
            [
                "hook-skip-insert-others-saved",
                "hook-skip-update-keeps-stored-row",
                "hook-skip-new-child-keeps-it-added",
                "hook-skip-root-delete-children-deleted",
                "hook-save-many-fires-each-entity",
                "hook-direct-crud-does-not-fire",
                "hook-after-write-blob-not-supported-on-ef-core",
                "hook-after-writes-blob-then-later-after-throws",
                "hook-after-executes-sql",
                "hook-after-sql-then-later-after-throws",
                "hook-throw-after-update",
                "insert-when-missing-single",
                "n2-new-children-take-parent-keys",
            ],
            // 保存の応答がサーバーのフックの書いた値・決めたキーを呼び出し側へ書き戻す
            ["SaveHookWriteBack"] =
            [
                "remote-save-writes-back-hook-values",
                "remote-write-back-by-position",
                "n2-hook-assigned-keys-reach-new-children",
            ],
            // リモート面の CRUD・グラフ保存・保存の競合・null の引数
            ["CrudParityRemote"] =
            [
                "remote-crud-round-trip",
                "remote-save-round-trip",
                "save-missing-row-conflicts-unless-insert-when-missing",
                "null-argument-is-refused",
            ],
            // Save フックのうち全機能面の手（BulkInsert）を使うもの
            ["SaveHookFullSurface"] = ["hook-bulk-insert-does-not-fire"],
            // Save フックのうち除外 blob の Stream アクセサで読むもの
            ["SaveHookBlobStream"] = ["hook-after-writes-blobs"],
            // Save フックのうち外部キーで断られるもの
            ["SaveHookForeignKey"] = ["hook-skip-parent-orphans-child"],
            // 無制限バイナリ列のうち全実装先で流れるもの
            ["BinaryColumn"] =
            [
                "blob-excluded-columns-read-and-write-guard",
                "blob-stream-not-supported-on-ef-core",
                "blob-unset-update-from-new-entity-keeps-blobs",
            ],
            // 無制限バイナリ列のうち全機能面（Query()・BulkInsert・射影）を使うもの
            ["BinaryColumnFullSurface"] = ["blob-excluded-columns-full-surface"],
            // 無制限バイナリ列のうち生 SQL のエンティティ取得
            ["BinaryColumnRawSql"] = ["blob-raw-sql-entity-retrieval"],
            // 無制限バイナリ列のうち Stream アクセサを使うもの
            ["BinaryColumnBlobStream"] = ["blob-stream-contract"],
            // 無制限バイナリ列のうち Stream アクセサと全機能面の両方を使うもの
            ["BinaryColumnBlobStreamFullSurface"] =
            [
                "blob-stream-write-then-with-unbounded-binary",
            ],
            // 削除を含む保存で新しい行を後回しにしないグラフ保存の書く順
            ["GraphSaveOrder"] =
            [
                "graph-move-child-and-delete-old-parent-removed-first",
                "graph-move-child-and-delete-old-parent-kept-first",
                "graph-move-child-to-new-parent-and-delete-old-parent",
                "graph-new-row-referenced-by-another-root-deletes-old-parent",
                "graph-new-row-referenced-by-another-root-deletes-unrelated-row",
            ],
            // グラフ保存がカスケードの閉包だけを保存する（Include で読む形＝全機能面の手）
            ["GraphSaveRuntime"] =
            [
                "graph-include-delete-child-saved-from-child",
                "graph-include-delete-child-with-parent-included",
                "graph-include-delete-child-through-root-keeps-siblings",
                "graph-include-save-roots-sharing-parent-key",
                "graph-include-save-child-does-not-write-parent",
                "graph-include-delete-root-without-cascade-save",
            ],
            // グラフ保存がカスケードの閉包だけを保存する（リモートでも流れる）
            ["GraphSaveRuntimeRemote"] =
            [
                "graph-delete-root-without-cascade-save-attached",
                "graph-move-order-and-delete-old-customer-removed-first",
                "graph-move-order-and-delete-old-customer-kept-first",
                "graph-updated-row-takes-unique-value-of-deleted-row-with-children-updated-first",
                "graph-updated-row-takes-unique-value-of-deleted-row-with-children-removed-first",
            ],
            // EF Core の再試行する実行戦略
            ["EfCoreExecutionStrategy"] =
            [
                "ef-retry-move-order-and-delete-old-customer",
                "ef-retry-save-with-hooks-runs-hooks-per-attempt",
                "ef-retry-execute-delete-cascade",
                "contact-backup-delete-retried-commit",
            ],
        };

    /// <summary>実装先の一致のエンジンの登録</summary>
    public static IReadOnlyList<RuntimeEngineRegistration> BackendParity { get; } =
    [
        new(nameof(BackendParityRuntimeTests), RuntimeFeatures.BackendParity),
        new(nameof(BackendParityRuntimeTests), RuntimeFeatures.CrudParity, ["CrudParityRemote"]),
        new(
            nameof(BackendParityRuntimeTests),
            RuntimeFeatures.SaveHook,
            [
                "SaveHook",
                "SaveHookFullSurface",
                "SaveHookBlobStream",
                "SaveHookForeignKey",
                "SaveHookWriteBack",
            ]
        ),
        new(
            nameof(BackendParityRuntimeTests),
            RuntimeFeatures.BinaryColumn,
            [
                "BinaryColumn",
                "BinaryColumnFullSurface",
                "BinaryColumnRawSql",
                "BinaryColumnBlobStream",
                "BinaryColumnBlobStreamFullSurface",
            ]
        ),
        new(
            nameof(BackendParityRuntimeTests),
            RuntimeFeatures.UniquenessEnforcement,
            ["UniquenessEnforcement"]
        ),
        new(
            nameof(BackendParityRuntimeTests),
            RuntimeFeatures.EfCoreExecutionStrategy,
            ["EfCoreExecutionStrategy"],
            RuntimeEngineKind.EfCore
        ),
        new(nameof(BackendParityRuntimeTests), RuntimeFeatures.IgnoredWrite, ["IgnoredWrite"]),
        new(
            nameof(BackendParityRuntimeTests),
            RuntimeFeatures.GraphSave,
            ["GraphSaveOrder", "GraphSaveRuntime", "GraphSaveRuntimeRemote"]
        ),
    ];

    /// <summary>簡易 DSL の総当たりのエンジンの登録（構成の方言と実装先の軸）</summary>
    private static IReadOnlyList<(
        string ClassName,
        string Feature,
        string Dialect,
        Func<IReadOnlyList<DslFuzzBackend>> Backends
    )> DslFuzz { get; } =
    [
        (
            nameof(DslConditionFuzzSqlServerRuntimeTests),
            RuntimeFeatures.NamedQuery,
            DialectNames.SqlServer,
            // 軸を作るだけでフィクスチャに触れない（MatrixAxisGuardTests と同じ）
            () => DslConditionFuzzSqlServerRuntimeTests.BackendAxis(null!).Adapters
        ),
    ];

    /// <summary>エンジンの登録のクラスの名前（逆方向の照合に使う）</summary>
    public static IReadOnlyList<string> ClassNames =>
        BackendParity
            .Select(registration => registration.ClassName)
            .Concat(DslFuzz.Select(entry => entry.ClassName))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>エンジンの登録から担当のセルを導く（導けない登録は問題に並べる）</summary>
    public static (IReadOnlyList<RuntimeCoverage> Cells, IReadOnlyList<string> Problems) Derive()
    {
        var cells = new List<RuntimeCoverage>();
        var problems = new List<string>();
        var scenarios = BackendParityScenarios.All.ToDictionary(
            scenario => scenario.Name,
            StringComparer.Ordinal
        );

        foreach (var registration in BackendParity)
        {
            var label = $"[{registration.ClassName} × {registration.Feature}]";
            IEnumerable<ParityBackendKind> kinds;
            string detail;

            if (registration.ScenarioGroups is null)
            {
                kinds = BackendParityAxis.Kinds;
                detail = "全シナリオ";
            }
            else
            {
                var found = new List<ParityBackendKind>();

                if (registration.ScenarioGroups.Count == 0)
                {
                    problems.Add($"{label} のシナリオの群が空");
                }

                foreach (var group in registration.ScenarioGroups)
                {
                    if (!ScenarioGroups.TryGetValue(group, out var names))
                    {
                        problems.Add($"{label} のシナリオの群 {group} が無い");
                        continue;
                    }

                    if (names.Count == 0)
                    {
                        problems.Add($"{label} のシナリオの群 {group} が空");
                    }

                    foreach (var name in names)
                    {
                        if (scenarios.TryGetValue(name, out var scenario))
                        {
                            found.AddRange(scenario.Backends);
                        }
                        else
                        {
                            problems.Add(
                                $"{label} のシナリオの群 {group} のシナリオ '{name}' が実装先の一致に無い"
                            );
                        }
                    }
                }

                kinds = found;
                detail =
                    "シナリオの群 "
                    + string.Join("・", registration.ScenarioGroups)
                    + (
                        registration.Engine is null
                            ? ""
                            : $"（{registration.Engine} の実装先だけを数える）"
                    );
            }

            foreach (
                var adapter in kinds
                    .Distinct()
                    .Select(kind => BackendParityAxis.Axis.Get(kind.ToString()))
                    .Where(adapter =>
                        registration.Engine is null
                        || RuntimeTargets.Of(adapter.Target).Engine == registration.Engine
                    )
            )
            {
                cells.Add(
                    new(
                        registration.Feature,
                        adapter.Target,
                        adapter.Server,
                        ServerNone: false,
                        registration.ClassName,
                        detail
                    )
                );
            }
        }

        foreach (var (className, feature, dialect, backends) in DslFuzz)
        {
            var adapters = backends();

            foreach (var backend in adapters)
            {
                var target = DslTarget(dialect, backend.Name);

                if (target is null)
                {
                    problems.Add(
                        $"[{className} × {feature}] の実装先 {backend.Name}（方言 {dialect}）に当たる実装先が無い"
                    );
                    continue;
                }

                cells.Add(
                    new(
                        feature,
                        target.Value,
                        Server: null,
                        ServerNone: false,
                        className,
                        $"実装先の軸 {string.Join("・", adapters.Select(adapter => adapter.Name))}（方言 {dialect}）"
                    )
                );
            }
        }

        return (cells, problems);
    }

    /// <summary>簡易 DSL の総当たりの実装先の名前と構成の方言から実装先を引く（無ければ null）</summary>
    /// <param name="dialect">構成の方言</param>
    /// <param name="backend">実装先の名前（<see cref="DslFuzzBackends.Names"/>）</param>
    public static RuntimeTarget? DslTarget(string dialect, string backend) =>
        backend switch
        {
            DslFuzzBackends.Ado => RuntimeTargets.Find(RuntimeEngineKind.Ado, dialect),
            DslFuzzBackends.EfCore => RuntimeTargets.Find(RuntimeEngineKind.EfCore, dialect),
            DslFuzzBackends.InMemory => RuntimeTarget.InMemory,
            _ => null,
        };
}
