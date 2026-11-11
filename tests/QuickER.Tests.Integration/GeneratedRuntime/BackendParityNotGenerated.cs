using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 実装先どうしの一致の乱数の操作の列の生成器（<c>BackendParityGenerator</c>）が作らない形の宣言。乱数の列のテストと結果の表が同じ生成器の
/// 入力を使う（QuickER.Tests にもリンクで入るので、Integration の型を cref でも参照しない）
/// </summary>
/// <remarks>純粋なデータ（Engines の型と BCL だけを参照する）。</remarks>
internal static class BackendParityNotGenerated
{
    private const string Parity = "`GeneratedRuntime/Parity`";
    private const string Rest = "実装先の一致の残りの区切り（未起動）";

    /// <summary>宣言</summary>
    public static NotGeneratedDeclaration Declaration { get; } =
        new(
            "BackendParityGenerator",
            [
                ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "Parity"),
                ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "ParityOutcomes"),
                ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, "ParityValues"),
            ],
            [
                NotGenerated.Accepted(
                    "rebuilt-row-update",
                    "既存の行を組み立て直した UPDATE（読み込んでいない行の更新）",
                    "EF Core は全列を書くので除外した blob を上書きする（docs の EF Core の節）。UPDATE は読み込んだ行だけにする",
                    Parity
                ),
                NotGenerated.Accepted(
                    "fixed-shape-surfaces",
                    "insertWhenUpdateMissing・生 SQL・ナビゲーションの条件・Include の深さ・BulkInsert・一括削除",
                    "実装先ごとの契約の値（行の数・フックの順・戻り値）を期待の手で書く必要があり、決まった形のシナリオが受け持つ",
                    Parity
                        + "（行 `bulk-insert-contract` / `include-grandparent` / `after-sql-row`）"
                ),
                NotGenerated.Accepted(
                    "mid-sequence-deviations",
                    "列の途中の、子を持つ親の DeleteAsync・親の無い行の挿入・参照されている顧客の削除・代替キーの更新・Save フックの規則",
                    "インメモリは外部キーを検査せず参照動作も再現しない（docs に書いた受け入れた差）。手の差を宣言する手は列の末尾にだけ置き、その実装先をその手から後の比較から外す",
                    "`BackendParityRuntimeTests.AcceptedDeviation_CutsOnlyTheDeclaredBackend`"
                ),
                NotGenerated.Accepted(
                    "delete-missing-or-added-row",
                    "他者が消した行・まだ挿入していない（Added の）行への削除",
                    "行の無い削除の戻り値は実装先で割れることを受け入れている（docs）。スロットの行が消されたら読み直すまで使わない",
                    Parity + "（行 `crud-update-missing-and-delete-twice`）"
                ),
                NotGenerated.Accepted(
                    "foreign-key-navigation-mismatch",
                    "外部キーとナビゲーションが食い違うグラフの保存",
                    "どの実装先も保存の前に断るので、作ると保存の手が断られるだけで後の列が何も比べなくなる。決まった形のシナリオが受け持つ",
                    Parity + "（行 `n2-navigation-overrides-foreign-key`）"
                ),
                NotGenerated.Accepted(
                    "cascade-options",
                    "cascadeSave・cascadeDelete を外した保存と、ExecuteDelete のカスケードの切り替え",
                    "保存は既定の引数、ExecuteDelete はカスケードありに固定する。外した形は決まった形のシナリオが受け持つ",
                    Parity
                        + "（行 `delete-root-without-cascade-save-rows` / `cascade-delete-rows-left`）"
                ),
                NotGenerated.Accepted(
                    "row-version-guard",
                    "行バージョンの版ガード",
                    "BackendParityFixture は行バージョンを持たない（インメモリが擬似の版を採番するので、版の値が実装先で割れる）。版の照合は SQL Server の系統が受け持つ",
                    "`BackendParitySqlServerRuntimeTests`（行 `own-write-*`）"
                ),
                NotGenerated.Pending(
                    "null-into-not-null-excluded-blob",
                    "NOT NULL の除外した blob への NULL の書き込み",
                    Rest,
                    "インメモリが NOT NULL の除外列を検査するかを測っていない。実 DB と食い違うかを確かめてから、受け入れた差か水準かを決める",
                    NotGenerated.Unguarded
                ),
                NotGenerated.Pending(
                    "other-dialects",
                    "SQLite 以外の方言の乱数の列",
                    "実装先の一致の SQL Server の乱数の列（未起動）",
                    "乱数の列は SQLite の BackendParityFixture だけで流す。QuickER 版と EF Core の SQL は方言で分岐する（SQL Server の系統は乱数の保存だけを持つ）",
                    "`GeneratedRuntime/ParitySqlServerSaves`"
                ),
            ]
        );
}
