using System;
using System.Collections.Generic;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanCatalogProbes
{
    /// <summary>SQL Server の探針</summary>
    private static IReadOnlyDictionary<string, string> SqlServerProbes =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 列の型・長さ（n 系は文字数）・NULL 許容・照合順序・DEFAULT 制約の名前と定義
            // （例：realdb-column-alter-keeps-collation・realdb-column-alter-readds-default-*）
            ["columns"] = """
                SELECT SCHEMA_NAME(o.schema_id) + N'.' + o.name + N'.' + c.name AS subject,
                       t.name AS type,
                       CASE WHEN t.name LIKE N'n%' AND c.max_length > 0 THEN c.max_length / 2 ELSE c.max_length END AS len,
                       c.is_nullable AS nullable,
                       c.collation_name AS collation,
                       dc.name AS [default],
                       dc.definition AS definition
                FROM sys.columns c
                JOIN sys.tables o ON o.object_id = c.object_id
                JOIN sys.types t ON t.user_type_id = c.user_type_id
                LEFT JOIN sys.default_constraints dc
                  ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id;
                """,
            // 主キー・外部キーの名前（スキーマ.表ごと。例：realdb-pk-alter-constraint-name-with-bracket-and-quote・
            // realdb-pk-alter-ignores-same-name-in-other-schema）
            ["keys"] = """
                SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name AS subject, N'PK' AS kind, kc.name AS name
                FROM sys.key_constraints kc JOIN sys.tables t ON kc.parent_object_id = t.object_id
                WHERE kc.type = 'PK'
                UNION ALL
                SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name, N'FK', fk.name
                FROM sys.foreign_keys fk JOIN sys.tables t ON fk.parent_object_id = t.object_id;
                """,
            // DEFAULT 制約の名前と object_id（外して付け直すと object_id が変わる＝触れていないことを前後の一致で見る。
            // 例：realdb-column-alter-nullability-only-int-keeps-default）
            ["defaults"] = """
                SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name + N'.' + c.name AS subject,
                       dc.name AS name,
                       dc.object_id AS object_id
                FROM sys.default_constraints dc
                JOIN sys.tables t ON t.object_id = dc.parent_object_id
                JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id;
                """,
            // 説明（MS_Description・スキーマ.表[.列]ごと）
            ["descriptions"] = """
                SELECT SCHEMA_NAME(t.schema_id) + N'.' + t.name + ISNULL(N'.' + c.name, N'') AS subject,
                       CAST(ep.value AS nvarchar(4000)) AS value
                FROM sys.extended_properties ep
                JOIN sys.tables t ON ep.major_id = t.object_id
                LEFT JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.minor_id > 0
                WHERE ep.class = 1 AND ep.name = N'MS_Description';
                """,
        };
}
