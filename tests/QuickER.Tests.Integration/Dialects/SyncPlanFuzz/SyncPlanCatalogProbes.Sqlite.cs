using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanCatalogProbes
{
    /// <summary>SQLite の探針</summary>
    private static IReadOnlyDictionary<string, string> SqliteProbes =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // sqlite_master の表・索引・トリガー・ビューの CREATE 文（種類 名前 ごと。内部の sqlite_ の物は除く）
            // （例：realdb-rebuild-warns-and-drops-column-attributes・realdb-rebuild-keeps-non-constraint-unique-indexes）
            ["schema"] = """
                SELECT type || ' ' || name AS subject, tbl_name AS tbl, sql
                FROM sqlite_master
                WHERE name NOT LIKE 'sqlite_%';
                """,
        };
}
