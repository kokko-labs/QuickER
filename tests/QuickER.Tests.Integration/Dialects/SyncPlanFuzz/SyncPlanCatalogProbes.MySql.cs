using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

internal static partial class SyncPlanCatalogProbes
{
    /// <summary>
    /// <c>columns</c> の探針が読む <c>information_schema.COLUMNS</c> の項目（列ごとに 1 項目 1 行へ展開する）
    /// </summary>
    private static string[] MySqlColumnFields =>
        [
            "ORDINAL_POSITION",
            "COLUMN_DEFAULT",
            "IS_NULLABLE",
            "DATA_TYPE",
            "CHARACTER_MAXIMUM_LENGTH",
            "NUMERIC_PRECISION",
            "NUMERIC_SCALE",
            "DATETIME_PRECISION",
            "CHARACTER_SET_NAME",
            "COLLATION_NAME",
            "COLUMN_TYPE",
            "COLUMN_KEY",
            "EXTRA",
            "GENERATION_EXPRESSION",
            "SRS_ID",
            "COLUMN_COMMENT",
        ];

    /// <summary>MySQL の探針</summary>
    private static IReadOnlyDictionary<string, string> MySqlProbes =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 接続先 DB の全列の information_schema.COLUMNS の 16 項目を、列と項目ごとに 1 行（表.列.項目 value=値）へ展開する
            // （項目ごとに比べられるよう 1 行 1 項目にする。例：realdb-mysql-column-description-sync-keeps-every-attribute・
            // realdb-mysql-pk-move-auto-increment-removes-it）
            ["columns"] =
                string.Join(
                    "\nUNION ALL\n",
                    MySqlColumnFields.Select(name =>
                        $"SELECT CONCAT(TABLE_NAME, '.', COLUMN_NAME, '.{name}') AS subject, "
                        + $"CAST({name} AS CHAR) AS value "
                        + "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()"
                    )
                ) + ";",
            // 接続先 DB の索引の名前と先頭の列（information_schema.STATISTICS の SEQ_IN_INDEX = 1。表.索引 first_column=列。
            // 例：realdb-mysql-fk-drop-does-not-rename-index-to-other-column-fk）
            ["indexes"] = """
                SELECT CONCAT(TABLE_NAME, '.', INDEX_NAME) AS subject, COLUMN_NAME AS first_column
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND SEQ_IN_INDEX = 1;
                """,
            // 接続先 DB の外部キーの構成列（information_schema.KEY_COLUMN_USAGE。表.制約.序数 column=子の列 referenced=親の列。
            // 例：realdb-mysql-fk-composite-add-keeps-pairs・realdb-mysql-fk-composite-survives-pk-change）
            ["fk-columns"] = """
                SELECT CONCAT(TABLE_NAME, '.', CONSTRAINT_NAME, '.', ORDINAL_POSITION) AS subject,
                       COLUMN_NAME AS `column`, REFERENCED_COLUMN_NAME AS referenced
                FROM information_schema.KEY_COLUMN_USAGE
                WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL;
                """,
            // 接続先 DB の外部キーの参照アクション（information_schema.REFERENTIAL_CONSTRAINTS。表.制約 delete_rule=… update_rule=…。
            // 例：realdb-mysql-computed-fk-column-type-change-rebuilds-fk）
            ["referential"] = """
                SELECT CONCAT(TABLE_NAME, '.', CONSTRAINT_NAME) AS subject,
                       DELETE_RULE AS delete_rule, UPDATE_RULE AS update_rule
                FROM information_schema.REFERENTIAL_CONSTRAINTS
                WHERE CONSTRAINT_SCHEMA = DATABASE();
                """,
        };
}
