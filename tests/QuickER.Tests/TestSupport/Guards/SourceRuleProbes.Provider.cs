namespace QuickER.Tests.TestSupport.Guards;

/// <summary>規則の自己検査の表：DB の取込・同期と DDL の生成（QuickER.Provider*）</summary>
internal static partial class SourceRuleProbes
{
    private static IEnumerable<SourceRuleProbe> Provider() =>
        [
            // 差分の項目のリレーション（素の・! の）を第 1 引数に渡す ResolveForeignKeyName と、差分の項目の名前（素の・?? の左辺）を
            // 第 1 引数に渡す EffectiveForeignKey を拾い、ほかの型のリレーション・リレーションの名前・ほかの名前の呼び出しは拾わない
            new SourceRuleProbe(
                "sync-selected-foreign-key-names-forms",
                "sync-selected-foreign-key-names",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using QuickER.Dialects;
                        using QuickER.Model;
                        using QuickER.Provider;

                        internal sealed class LiveForeignKey
                        {
                            public Relationship Relationship { get; set; } = new();
                        }

                        internal static class Probe
                        {
                            private static string ResolveForeignKeyName(Relationship? relationship, string child) => child;

                            private static string Other(Relationship? relationship, string child) => child;

                            public static string Plain(SchemaDiffItem item) => ResolveForeignKeyName(item.Relationship, "c");

                            public static string Suppressed(SchemaDiffItem item) => ResolveForeignKeyName(item.Relationship!, "c");

                            public static string Live(LiveForeignKey fk) => ResolveForeignKeyName(fk.Relationship, "c");

                            public static string OtherName(SchemaDiffItem item) => Other(item.Relationship, "c");

                            public static string Effective(SchemaDiffItem item) =>
                                ConstraintNames.EffectiveForeignKey(item.ForeignKeyName, "c", "p", IdentifierLengthLimit.Unlimited);

                            public static string EffectiveCoalesce(SchemaDiffItem item) =>
                                ConstraintNames.EffectiveForeignKey(item.ForeignKeyName ?? "n", "c", "p", IdentifierLengthLimit.Unlimited);

                            public static string EffectiveRelationship(SchemaDiffItem item) =>
                                ConstraintNames.EffectiveForeignKey(item.Relationship?.ConstraintName, "c", "p", IdentifierLengthLimit.Unlimited);
                        }
                        """,
                        "Plain",
                        "Suppressed",
                        "Effective",
                        "EffectiveCoalesce"
                    ),
                ]
            ),
            // 差分の項目の Column の DataType を、素の・! の・?. の読み取りで拾い（ラムダの中は外側のメソッドの名前）、OldColumn・
            // live の列・別の変数へ取った列は拾わない
            new SourceRuleProbe(
                "sync-type-deciding-column-copies-forms",
                "sync-type-deciding-column-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;
                        using System.Linq;
                        using QuickER.Model;
                        using QuickER.Provider;

                        internal static class Probe
                        {
                            public static string Plain(SchemaDiffItem item) => item.Column.DataType;

                            public static string? Conditional(SchemaDiffItem item) => item.Column?.DataType;

                            public static string Suppressed(SchemaDiffItem item) => item.Column!.DataType;

                            public static Dictionary<string, string> InLambda(IEnumerable<SchemaDiffItem> items) =>
                                items.ToDictionary(i => i.TableName, i => i.Column!.DataType);

                            public static string Old(SchemaDiffItem item) => item.OldColumn!.DataType;

                            public static string Live(Column column) => column.DataType;

                            public static string Copied(SchemaDiffItem item)
                            {
                                var column = item.Column!;
                                return column.DataType;
                            }
                        }
                        """,
                        "Plain",
                        "Conditional",
                        "Suppressed",
                        "InLambda"
                    ),
                ]
            ),
            // 補間の穴と文字列の連結の数値・日時を型で拾い、InvariantCulture の string.Create と文字列は拾わない
            new SourceRuleProbe(
                "numeric-interpolation-culture-forms",
                "numeric-interpolation-culture",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Globalization;

                        internal static class Probe
                        {
                            public static string Found(int length, decimal scale, int? precision, DateTime at)
                            {
                                return $"varchar({length})"
                                    + "numeric(10," + scale + ")"
                                    + $"time({precision})"
                                    + $"-- {at}";
                            }

                            public static string NotFound(int length, string name)
                            {
                                return string.Create(CultureInfo.InvariantCulture, $"varchar({length})")
                                    + $"{name}({length.ToString(CultureInfo.InvariantCulture)})"
                                    + "x" + name;
                            }
                        }
                        """,
                        "{length}",
                        "scale",
                        "{precision}",
                        "{at}"
                    ),
                ]
            ),
            // シンボルの規則は、条件コンパイルの外れる分岐の中の補間も変種のコンパイルで読む
            new SourceRuleProbe(
                "numeric-interpolation-culture-inactive-branch",
                "numeric-interpolation-culture",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static string Type(int length, string name)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return $"varchar({length})";
                        #else
                                return name;
                        #endif
                            }
                        }
                        """,
                        "{length}"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 素のコマンド生成（CreateCommand() と 5 方言の new XxxCommand(）を 1 行に 1 件で拾い、ヘルパー・ほかの型は拾わない
            new SourceRuleProbe(
                "schema-command-bypasses-forms",
                "schema-command-bypasses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static void Run(DbConnection connection, string sql)
                            {
                                using var a = connection.CreateCommand();
                                using var b = new SqlCommand(sql, connection);
                                using var c = new  NpgsqlCommand (sql);
                                using var d = new OracleCommand(sql); using var e = new MySqlCommand(sql);
                                using var f = DbCommands.Create(connection, sql);
                                var g = new SqlCommandBuilder();
                                var h = connection.CreateCommandAsync();
                            }
                        }
                        """,
                        ".CreateCommand()",
                        "new SqlCommand(",
                        "new NpgsqlCommand (",
                        "new OracleCommand("
                    ),
                ]
            ),
            // ヘルパーの出現を 1 つずつ拾う（必須の規則）
            new SourceRuleProbe(
                "schema-command-helper-uses-forms",
                "schema-command-helper-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static void Run(DbConnection connection)
                            {
                                using var a = DbCommands.Create(connection, "select 1"); using var b = DbCommands.Create(connection, "select 2");
                                using var c = connection.CreateCommand();
                            }
                        }
                        """,
                        "DbCommands.Create",
                        "DbCommands.Create"
                    ),
                ]
            ),
            // 文字列で読む必須の規則は、条件コンパイルの外れる分岐の中の出現も拾う
            new SourceRuleProbe(
                "schema-command-helper-uses-inactive-branch",
                "schema-command-helper-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static object Run(DbConnection connection)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return DbCommands.Create(connection, "select 1");
                        #else
                                return connection;
                        #endif
                            }
                        }
                        """,
                        "DbCommands.Create"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 素の RollbackAsync( を拾い、後始末のヘルパーは拾わない
            new SourceRuleProbe(
                "schema-sync-raw-rollbacks-forms",
                "schema-sync-raw-rollbacks",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static async Task Fail(DbTransaction transaction, CancellationToken ct)
                            {
                                await transaction.RollbackAsync(ct);
                                await DbTransactions.RollbackQuietlyAsync(transaction);
                                transaction.Rollback();
                            }
                        }
                        """,
                        ".RollbackAsync("
                    ),
                ]
            ),
            // 後始末のヘルパーの出現を拾う（必須の規則）
            new SourceRuleProbe(
                "schema-sync-rollback-helper-uses-forms",
                "schema-sync-rollback-helper-uses",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static Task Fail(DbTransaction transaction) =>
                                DbTransactions.RollbackQuietlyAsync(transaction);
                        }
                        """,
                        "DbTransactions.RollbackQuietlyAsync"
                    ),
                ]
            ),
            // -- で始まる文字列リテラルを含む文の補間穴のうち Sanitize を通さないものを式で拾う。連結で組むコメント・; を含む文字列・
            // 複数行の文も文の単位で束ね、コメントの中・-- を含まない文・Sanitize を通した穴は拾わない
            new SourceRuleProbe(
                "sql-comment-unsanitized-holes-forms",
                "sql-comment-unsanitized-holes",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static void Write(StringBuilder sb, Entity entity, Column column, string a, string b, string c, bool x, string kind)
                            {
                                sb.AppendLine($"-- Table: {SqlComment.Sanitize(entity.TableName)}");
                                sb.AppendLine($"-- Column: {column.Name}");
                                sb.AppendLine("-- Rebuild "
                                    + $"{SqlComment.Sanitize(a)} -> "
                                    + $"{b}");
                                if (x) { sb.AppendLine($"-- {kind}"); }
                                // comment "-- {ignored}";
                                var s = $"select {x}";
                                var t = "a;b" + $"-- {c}";
                            }
                        }
                        """,
                        "column.Name",
                        "b",
                        "kind",
                        "c"
                    ),
                ]
            ),
            // 禁じる識別子をトークンで拾い、文字列・コメントの中と序数の比較の指定は拾わない
            new SourceRuleProbe(
                "sync-name-comparison-tokens-forms",
                "sync-name-comparison-tokens",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            // ToUpper
                            public static bool Same(string a, string b) =>
                                string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase)
                                || a.ToLowerInvariant() == "Trim"
                                || string.Equals(a, b, StringComparison.Ordinal);
                        }
                        """,
                        "Trim",
                        "OrdinalIgnoreCase",
                        "ToLowerInvariant"
                    ),
                ]
            ),
            // トークンの規則は、条件コンパイルの外れる分岐の中の識別子も拾う
            new SourceRuleProbe(
                "sync-name-comparison-tokens-inactive-branch",
                "sync-name-comparison-tokens",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static string Key(string a)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return a.ToUpperInvariant();
                        #else
                                return a;
                        #endif
                            }
                        }
                        """,
                        "ToUpperInvariant"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 取込警告の種別で分岐する形（case ラベル・== / != ・switch のアームの定数パターン）を拾い、種別を作る式と
            // 代入は拾わない
            new SourceRuleProbe(
                "schema-import-warning-kind-branches-forms",
                "schema-import-warning-kind-branches",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                            public static string Describe(SchemaImportWarningKind kind, string type)
                            {
                                switch (kind)
                                {
                                    case SchemaImportWarningKind.ViewExcluded:
                                        return "view";
                                }

                                var b = kind == SchemaImportWarningKind.ChildPartitionExcluded;
                                var c = kind != SchemaImportWarningKind.ViewExcluded;
                                var d = kind switch
                                {
                                    SchemaImportWarningKind.PartitionDefinitionLost => "p",
                                    _ => "",
                                };
                                var made = type switch
                                {
                                    "view" => SchemaImportWarningKind.ViewExcluded,
                                    _ => SchemaImportWarningKind.PartitionDefinitionLost,
                                };
                                var k = SchemaImportWarningKind.ViewExcluded;
                                return "";
                            }
                        }
                        """,
                        "case SchemaImportWarningKind.ViewExcluded:",
                        "kind == SchemaImportWarningKind.ChildPartitionExcluded",
                        "kind != SchemaImportWarningKind.ViewExcluded",
                        "SchemaImportWarningKind.PartitionDefinitionLost"
                    ),
                ]
            ),
            // SQLite の接続の生成をシンボルで拾う（完全修飾・別名・型を書かない new・初期化子つき）。拾わないのは、接続文字列のビルダー・
            // コマンド・同じ名前の別の型・SqliteFileConnections.Create の呼び出し
            new SourceRuleProbe(
                "sqlite-file-connection-creations-forms",
                "sqlite-file-connection-creations",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using Microsoft.Data.Sqlite;
                        using C = Microsoft.Data.Sqlite.SqliteConnection;

                        namespace Other
                        {
                            internal sealed class SqliteConnection;
                        }

                        internal static class Probe
                        {
                            public static void Run(string cs)
                            {
                                using var a = new SqliteConnection(cs);
                                using var b = new Microsoft.Data.Sqlite.SqliteConnection();
                                using var c = new C(cs);
                                SqliteConnection d = new(cs);
                                using var e = new SqliteConnection { ConnectionString = cs };
                                var f = new SqliteConnectionStringBuilder(cs) { Pooling = false };
                                using var g = new SqliteCommand();
                                var h = new Other.SqliteConnection();
                            }
                        }
                        """,
                        "new SqliteConnection",
                        "new SqliteConnection",
                        "new SqliteConnection",
                        "new SqliteConnection",
                        "new SqliteConnection"
                    ),
                ]
            ),
            // シンボルの規則は、条件コンパイルの外れる分岐の中も変種のコンパイルで読む
            new SourceRuleProbe(
                "sqlite-file-connection-creations-inactive-branch",
                "sqlite-file-connection-creations",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using Microsoft.Data.Sqlite;

                        internal static class Probe
                        {
                            public static object Open(string cs)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return new SqliteConnection(cs);
                        #else
                                return cs;
                        #endif
                            }
                        }
                        """,
                        "new SqliteConnection"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // Pooling への定数 false の代入だけを拾う（初期化子・後からの代入）。拾わないのは、true・定数でない値・
            // 接続文字列のビルダーでない型の同じ名前のプロパティ
            new SourceRuleProbe(
                "sqlite-file-connection-pooling-off-forms",
                "sqlite-file-connection-pooling-off",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using Microsoft.Data.Sqlite;

                        internal sealed class Options
                        {
                            public bool Pooling { get; set; }
                        }

                        internal static class Probe
                        {
                            public static string Build(string cs, bool pooling)
                            {
                                var a = new SqliteConnectionStringBuilder(cs) { Pooling = false };
                                a.Pooling = !true;
                                a.Pooling = true;
                                a.Pooling = pooling;
                                var o = new Options { Pooling = false };
                                return a.ConnectionString;
                            }
                        }
                        """,
                        "Pooling = false",
                        "Pooling = false"
                    ),
                ]
            ),
        ];
}
