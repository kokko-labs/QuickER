using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanCatalogProbes
{
    /// <summary>PostgreSQL の探針</summary>
    /// <remarks>
    /// 読むスキーマは public と sales（取込の範囲の public と、別スキーマの同じ名前の表を置く行が使う sales）。
    /// </remarks>
    private static IReadOnlyDictionary<string, string> PostgreSqlProbes =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 列の照合順序（pg_attribute.attcollation を pg_collation へ LEFT JOIN した collname）と型（format_type）を、
            // スキーマ.表.列ごとに（例：realdb-pg-column-alter-keeps-non-default-collation-public-table）
            ["columns"] = """
                SELECT n.nspname || '.' || c.relname || '.' || a.attname AS subject,
                       co.collname AS collation,
                       format_type(a.atttypid, a.atttypmod) AS type
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_collation co ON co.oid = a.attcollation
                WHERE c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
                  AND n.nspname IN ('public', 'sales');
                """,
            // 主キー・外部キーの名前と構成列（pg_constraint の conkey を pg_attribute の attnum の順に並べる）を、
            // スキーマ.表ごとに（例：realdb-pg-pk-alter-name-with-double-quote・
            // realdb-pg-pk-alter-public-table-keeps-same-named-table-in-other-schema）
            ["keys"] = """
                SELECT n.nspname || '.' || c.relname AS subject,
                       CASE con.contype WHEN 'p' THEN 'PK' ELSE 'FK' END AS kind,
                       con.conname AS name,
                       (SELECT string_agg(a.attname, ',' ORDER BY a.attnum)
                        FROM pg_attribute a
                        WHERE a.attrelid = con.conrelid AND a.attnum = ANY (con.conkey)) AS columns
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE con.contype IN ('p', 'f') AND n.nspname IN ('public', 'sales');
                """,
        };
}
