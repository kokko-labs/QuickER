using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanCatalogProbes
{
    /// <summary>Oracle の探針</summary>
    /// <remarks>
    /// 読むのは接続ユーザーの自スキーマ（取込の範囲）。別のユーザーの制約は管理者の問い合わせ（<c>AdminQuery</c>）で読む。
    /// Oracle の列の別名は引用しないと大文字になるので、引用して小文字の列名で返す。
    /// </remarks>
    private static IReadOnlyDictionary<string, string> OracleProbes =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 主キー・外部キーの名前と構成列（user_cons_columns の position の順）を、表ごとに
            // （ごみ箱の表 BIN$… は除く。例：realdb-oracle-script-pk-single-to-composite・
            // realdb-oracle-script-pk-change-table-name-with-quote）
            ["keys"] = """
                SELECT c.table_name AS "subject",
                       CASE c.constraint_type WHEN 'P' THEN 'PK' ELSE 'FK' END AS "kind",
                       c.constraint_name AS "name",
                       LISTAGG(cc.column_name, ',') WITHIN GROUP (ORDER BY cc.position) AS "columns"
                FROM user_constraints c
                JOIN user_cons_columns cc
                  ON cc.constraint_name = c.constraint_name AND cc.table_name = c.table_name
                WHERE c.constraint_type IN ('P', 'R') AND c.table_name NOT LIKE 'BIN$%'
                GROUP BY c.table_name, c.constraint_type, c.constraint_name
                """,
        };
}
