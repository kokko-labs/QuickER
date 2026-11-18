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
            // 選んだ項目を外す規則（SelectionRules の Exclude*）への参照（呼び出しとメソッドグループ）のうち、計画器の BuildPlan からの直接の
            // 呼び出し・メソッドの中でデリゲートへ取るメソッドグループ・規則の表でないフィールドの初期化子のメソッドグループ・規則でない
            // メソッドからの呼び出し・ローカル関数とラムダの中の呼び出し・ほかのクラスからの SelectionRules. で修飾した呼び出しと、周の外の
            // 規則の表の読みを拾い、規則の表（BeforeRounds・EachRound）の初期化子のメソッドグループ・周（ExcludeUntilStable）の中の表の読み・
            // ほかの規則の中からの呼び出し・積むだけの ExcludeSelected・周そのもの・警告だけの関数（Warn*）・nameof の中の名前・Excluded で
            // 始まるプロパティ・ほかのクラスの修飾しない Exclude* は拾わない
            new SourceRuleProbe(
                "sync-exclusions-inside-stable-loop-forms",
                "sync-exclusions-inside-stable-loop",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System;
                        using System.Collections.Generic;

                        internal delegate List<int> SelectionRule(List<int> items);

                        internal static class SelectionRules
                        {
                            internal static readonly SelectionRule[] BeforeRounds = [ExcludeInside];

                            internal static readonly SelectionRule[] EachRound = [ExcludeNested, ExcludeDirect];

                            internal static readonly SelectionRule[] OtherRules = [ExcludeNested];

                            internal static List<int> ExcludeInside(List<int> items) => ExcludeNested(items);

                            internal static List<int> ExcludeNested(List<int> items) => items;

                            internal static List<int> ExcludeDirect(List<int> items) => items;

                            internal static List<int> Shared(List<int> items) => ExcludeDirect(items);
                        }

                        internal sealed class SyncPlanner
                        {
                            private List<int> ExcludedItems { get; } = [];

                            public List<int> BuildPlan(List<int> items)
                            {
                                var stable = ExcludeUntilStable(items);
                                var direct = SelectionRules.ExcludeDirect(stable);
                                WarnOnly(direct);
                                SelectionRule held = SelectionRules.ExcludeNested;
                                var count = nameof(SelectionRules.ExcludeDirect).Length + ExcludedItems.Count;
                                count += SelectionRules.EachRound.Length;
                                return held(Helper(this.ExcludeQualified(direct))).Count > count ? direct : items;
                            }

                            private static List<int> ExcludeUntilStable(List<int> items)
                            {
                                var current = items;

                                foreach (var rule in SelectionRules.BeforeRounds)
                                {
                                    current = rule(current);
                                }

                                foreach (var rule in SelectionRules.EachRound)
                                {
                                    current = rule(current);
                                }

                                ExcludeSelected(current);
                                return current;
                            }

                            private List<int> ExcludeQualified(List<int> items) => items;

                            private static void ExcludeSelected(List<int> items) { }

                            private static void WarnOnly(List<int> items) { }

                            private static List<int> Helper(List<int> items)
                            {
                                List<int> Local() => SelectionRules.ExcludeDirect(items);
                                Func<List<int>> lambda = () => SelectionRules.ExcludeNested(items);
                                Func<List<int>, List<int>> group = new SyncPlanner().ExcludeQualified;
                                return lambda().Count > 0 ? Local() : group(items);
                            }
                        }

                        internal sealed class OtherService
                        {
                            public static List<int> Run(List<int> items) =>
                                ExcludeElsewhere(SelectionRules.ExcludeDirect(items));

                            public static int Count() => SelectionRules.BeforeRounds.Length;

                            private static List<int> ExcludeElsewhere(List<int> items) => items;
                        }
                        """,
                        "OtherRules: ExcludeNested",
                        "Shared: ExcludeDirect",
                        "BuildPlan: ExcludeDirect",
                        "BuildPlan: ExcludeNested",
                        "BuildPlan: EachRound",
                        "BuildPlan: ExcludeQualified",
                        "Helper: ExcludeDirect",
                        "Helper: ExcludeNested",
                        "Helper: ExcludeQualified",
                        "Run: ExcludeDirect",
                        "Count: BeforeRounds"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の呼び出しも読む
            new SourceRuleProbe(
                "sync-exclusions-inside-stable-loop-inactive-branch",
                "sync-exclusions-inside-stable-loop",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;

                        internal static class SelectionRules
                        {
                            internal static List<int> ExcludeOutside(List<int> items) => items;
                        }

                        internal sealed class SyncPlanner
                        {
                            public List<int> BuildPlan(List<int> items)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return SelectionRules.ExcludeOutside(items);
                        #else
                                return items;
                        #endif
                            }
                        }
                        """,
                        "BuildPlan: ExcludeOutside"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 印を付ける所（オブジェクトの初期化子の左辺）と読む所（素の・?. の読み。項目を作る所の外・別の型からも）を拾い、
            // プロパティの宣言・XmlDoc の cref・文字列・コメント・名前の一部だけが同じメンバーは拾わない
            new SourceRuleProbe(
                "sync-plan-mark-owners-forms",
                "sync-plan-mark-owners",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed class Item
                        {
                            internal object? PlanMark { get; init; }

                            internal object? PlanMarks { get; init; }

                            public string Name { get; init; } = "";
                        }

                        internal static class Planner
                        {
                            /// <summary><see cref="Item.PlanMark"/> を付けた項目を作る</summary>
                            public static Item Make() => new Item { Name = "a", PlanMark = new object() };

                            // item.PlanMark
                            public static Item Other() => new Item { PlanMarks = "PlanMark" };
                        }

                        internal static class Renderer
                        {
                            public static bool Read(Item item) => item.PlanMark is null;

                            public static bool Conditional(Item? item) => item?.PlanMark is null;

                            public static Item Copy(Item item) => new Item { PlanMark = item.PlanMark };
                        }
                        """,
                        "Make: init",
                        "Read: read",
                        "Conditional: read",
                        "Copy: init",
                        "Copy: read"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の印の読みも拾う
            new SourceRuleProbe(
                "sync-plan-mark-owners-inactive-branch",
                "sync-plan-mark-owners",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed class Item
                        {
                            internal object? PlanMark { get; init; }
                        }

                        internal static class Renderer
                        {
                            public static bool IsPlannerMade(Item item)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return item.PlanMark is not null;
                        #else
                                return false;
                        #endif
                            }
                        }
                        """,
                        "IsPlannerMade: read"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 文脈の live の表・リレーションの読み（素の・?. ・ラムダの中）と外部キーの解決の呼び出しを拾い、live の状態の読み・
            // 初期化子の左辺・プロパティの宣言・nameof でない名前だけの一致（別の型の ResolveAll・文字列・コメント）は拾わない
            new SourceRuleProbe(
                "sync-live-schema-single-source-forms",
                "sync-live-schema-single-source",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;
                        using System.Linq;

                        internal sealed class Context
                        {
                            public List<int> LiveEntities { get; init; } = [];

                            public List<int> LiveRelationships { get; init; } = [];
                        }

                        internal sealed class LiveState
                        {
                            public List<int> Entities { get; } = [];
                        }

                        internal static class ForeignKeyColumnPairResolver
                        {
                            public static List<int> ResolveAll(List<int> a, List<int> b) => a;
                        }

                        internal static class Other
                        {
                            public static List<int> ResolveAll(List<int> a) => a;
                        }

                        internal static class Planner
                        {
                            // context.LiveEntities
                            public static int Plain(Context context) => context.LiveEntities.Count;

                            public static int Conditional(Context? context) =>
                                context?.LiveRelationships.Count ?? 0;

                            public static List<int> Resolve(List<int> a) =>
                                ForeignKeyColumnPairResolver.ResolveAll(a, a);

                            public static int Lambda(List<Context> all) =>
                                all.Sum(c => c.LiveEntities.Count);

                            public static int Allowed(LiveState liveSchema, List<int> a)
                            {
                                var made = new Context { LiveEntities = a, LiveRelationships = a };
                                var text = "context.LiveEntities";
                                return liveSchema.Entities.Count + Other.ResolveAll(a).Count + text.Length;
                            }
                        }
                        """,
                        "Plain: LiveEntities",
                        "Conditional: LiveRelationships",
                        "Resolve: ForeignKeyColumnPairResolver.ResolveAll",
                        "Lambda: LiveEntities"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の読みも拾う
            new SourceRuleProbe(
                "sync-live-schema-single-source-inactive-branch",
                "sync-live-schema-single-source",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;

                        internal sealed class Context
                        {
                            public List<int> LiveEntities { get; init; } = [];
                        }

                        internal static class Planner
                        {
                            public static int Count(Context context)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return context.LiveEntities.Count;
                        #else
                                return 0;
                        #endif
                            }
                        }
                        """,
                        "Count: LiveEntities"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 同期後の事実を求める呼び出し（From の普通の形・改行をまたぐ形・ラムダの中・修飾した受け手・時点を PlanStage.X で書かない From・
            // ほかの静的なメソッドの直接の呼び出し・プロパティの中）を拾い、インスタンスのメンバーの読み・別の型の From・文字列・コメントは拾わない
            new SourceRuleProbe(
                "sync-post-sync-schema-single-source-forms",
                "sync-post-sync-schema-single-source",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;
                        using System.Linq;

                        internal enum PlanStage
                        {
                            Selected,
                            Arranged,
                        }

                        internal sealed class PostSyncSchema
                        {
                            public int Count => 0;

                            public static PostSyncSchema From(PlanStage stage, List<int> items) => new();

                            public static int PostSyncPrimaryKeyOf(int table) => table;
                        }

                        internal static class Other
                        {
                            public static int From(PlanStage stage, List<int> items) => 0;
                        }

                        internal static class Planner
                        {
                            // PostSyncSchema.From(PlanStage.Arranged, items)
                            public static int Plain(List<int> items) =>
                                PostSyncSchema.From(PlanStage.Selected, items).Count;

                            public static int Wrapped(List<int> items) =>
                                PostSyncSchema
                                    .From(PlanStage.Arranged, items)
                                    .Count;

                            public static int Lambda(List<List<int>> all) =>
                                all.Sum(items => PostSyncSchema.From(PlanStage.Selected, items).Count);

                            public static int Qualified(List<int> items) =>
                                global::PostSyncSchema.From(PlanStage.Arranged, items).Count;

                            public static int Variable(List<int> items, PlanStage stage) =>
                                PostSyncSchema.From(stage, items).Count;

                            public static int Direct(int table) => PostSyncSchema.PostSyncPrimaryKeyOf(table);

                            public static int Property => PostSyncSchema.From(PlanStage.Selected, []).Count;

                            public static int Allowed(PostSyncSchema postSync, List<int> items)
                            {
                                var text = "PostSyncSchema.From(PlanStage.Selected, items)";
                                return postSync.Count + Other.From(PlanStage.Selected, items) + text.Length;
                            }
                        }
                        """,
                        "Plain: Selected",
                        "Wrapped: Arranged",
                        "Lambda: Selected",
                        "Qualified: Arranged",
                        "Variable: ?",
                        "Direct: PostSyncPrimaryKeyOf",
                        "Property: Selected"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の呼び出しも拾う
            new SourceRuleProbe(
                "sync-post-sync-schema-single-source-inactive-branch",
                "sync-post-sync-schema-single-source",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;

                        internal enum PlanStage
                        {
                            Selected,
                        }

                        internal sealed class PostSyncSchema
                        {
                            public int Count => 0;

                            public static PostSyncSchema From(PlanStage stage, List<int> items) => new();
                        }

                        internal static class Planner
                        {
                            public static int Count(List<int> items)
                            {
                        #if QUICKER_PROBE_BRANCH
                                return PostSyncSchema.From(PlanStage.Selected, items).Count;
                        #else
                                return 0;
                        #endif
                            }
                        }
                        """,
                        "Count: Selected"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 列の NULL 許容の印の読み取り（素の・否定・?. ・プロパティパターン・代入の右辺・ラムダの中）を拾い、書き込み（代入の左辺・
            // 初期化子）・nameof・ほかの型の IsNullable は拾わない
            new SourceRuleProbe(
                "provider-column-nullability-raw-reads-forms",
                "provider-column-nullability-raw-reads",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Collections.Generic;
                        using System.Linq;
                        using QuickER.Model;

                        internal sealed class OtherShape
                        {
                            public bool IsNullable { get; set; }
                        }

                        internal static class Probe
                        {
                            public static bool Plain(Column column) => column.IsNullable;

                            public static bool Negated(Column column) => !column.IsNullable;

                            public static bool? Conditional(Column? column) => column?.IsNullable;

                            public static bool Pattern(Column column) => column is { IsNullable: true };

                            public static void CopiedFrom(Column target, Column source) => target.IsNullable = source.IsNullable;

                            public static bool InLambda(IEnumerable<Column> columns) => columns.Any(c => c.IsNullable);

                            public static void Written(Column column) => column.IsNullable = false;

                            public static Column Initialized() => new Column { IsNullable = false };

                            public static string Named() => nameof(Column.IsNullable);

                            public static bool Other(OtherShape shape) => shape.IsNullable;
                        }
                        """,
                        "Plain",
                        "Negated",
                        "Conditional",
                        "Pattern",
                        "CopiedFrom",
                        "InLambda"
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
            // 受け手が names の Add を 1 本ずつ拾い、所属のメソッド・出どころの式・表の位置か・置き場所の表記にする（置き場所の関数は
            // ラムダの中でも名前で読む）。引数の形が違う names.Add（素の値・引数の数の違う生成）は「形の違う」として拾い、names 以外の
            // 受け手の Add・名前の違う変数の Add は拾わない
            new SourceRuleProbe(
                "sql-name-text-name-sources-forms",
                "sql-name-text-name-sources",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class SqlNameText
                        {
                            private static void Collect(string table, Item item, List<NameSource> names, List<NameSource> others, List<NameSource> nameList)
                            {
                                names.Add(new(table, true, TableLocation));
                                names.Add(new NameSource(item.ColumnName, false, name => ColumnLocation(table, name)));
                                others.Add(new(table, true, TableLocation));
                                nameList.Add(new(table, true, TableLocation));
                                names.Add(table);
                                names.Add(new(table, true, TableLocation, item));
                            }
                        }
                        """,
                        "Collect: names.Add(table, true, TABLE)",
                        "Collect: names.Add(item.ColumnName, false, COLUMN)",
                        "Collect: 形の違う names.Add names.Add(table)",
                        "Collect: 形の違う names.Add names.Add(new(table, true, TableLocation, item))"
                    ),
                ]
            ),
            // 構文の節で読む規則は、条件コンパイルの外れる分岐の中の names.Add も拾う
            new SourceRuleProbe(
                "sql-name-text-name-sources-inactive-branch",
                "sql-name-text-name-sources",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class SqlNameText
                        {
                            private static void Collect(string constraint, List<NameSource> names)
                            {
                        #if QUICKER_PROBE_BRANCH
                                names.Add(new(constraint, false, UniqueLocation));
                        #endif
                            }
                        }
                        """,
                        "Collect: names.Add(constraint, false, UNIQUE)"
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
            // ADO.NET のコマンドの ExecuteNonQuery / ExecuteNonQueryAsync をシンボルで拾う（SqliteCommand・DbCommand・IDbCommand の
            // どの静的な型の受け手でも）。拾わないのは、結果を読み切る ExecuteReader(Async)・ExecuteScalar・コマンドでない型の同じ名前の
            // メソッド
            new SourceRuleProbe(
                "sqlite-script-executions-forms",
                "sqlite-script-executions",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using System.Data;
                        using System.Data.Common;
                        using System.Threading.Tasks;
                        using Microsoft.Data.Sqlite;

                        internal sealed class Runner
                        {
                            public int ExecuteNonQuery() => 0;
                        }

                        internal static class Probe
                        {
                            public static async Task Run(SqliteCommand a, DbCommand b, IDbCommand c, Runner r)
                            {
                                a.ExecuteNonQuery();
                                await b.ExecuteNonQueryAsync();
                                c.ExecuteNonQuery();
                                await using var reader = await b.ExecuteReaderAsync();
                                a.ExecuteScalar();
                                r.ExecuteNonQuery();
                            }
                        }
                        """,
                        "ExecuteNonQuery",
                        "ExecuteNonQuery",
                        "ExecuteNonQuery"
                    ),
                ]
            ),
            // シンボルの規則は、条件コンパイルの外れる分岐の中も変種のコンパイルで読む
            new SourceRuleProbe(
                "sqlite-script-executions-inactive-branch",
                "sqlite-script-executions",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        using Microsoft.Data.Sqlite;

                        internal static class Probe
                        {
                            public static void Run(SqliteCommand command)
                            {
                        #if QUICKER_PROBE_BRANCH
                                command.ExecuteNonQuery();
                        #else
                                command.ExecuteReader().Dispose();
                        #endif
                            }
                        }
                        """,
                        "ExecuteNonQuery"
                    ),
                ]
            ),
            // CREATE TABLE・CREATE [UNIQUE] INDEX（IF NOT EXISTS つきも・大文字小文字を問わない）の名前のうち、前置きで始まらないもの
            // （大文字の前置き・引用符で囲んだ前置きは補助の物）と、名前の代わりに補間・連結が続くものを拾う。
            // 文の綴りだけを書いた XmlDoc（IF NOT EXISTS の直後に名前が無い）と、CREATE VIEW と、CREATE TRIGGER（別の規則が読む）は拾わない
            new SourceRuleProbe(
                "sync-auxiliary-names-in-template-forms",
                "sync-auxiliary-names-in-template",
                [
                    new ProbeFile(
                        "Probe.scriban",
                        """
                        /// <c>CREATE TABLE IF NOT EXISTS</c>.
                        "CREATE TABLE IF NOT EXISTS quicker_sync_journal ("
                        "CREATE TABLE QUICKER_SYNC_ACK ("
                        "create table if not exists \"quicker_sync_blob_pending\" ("
                        "CREATE TABLE IF NOT EXISTS sync_journal ("
                        "CREATE UNIQUE INDEX IF NOT EXISTS ix_journal ON quicker_sync_journal (id)"
                        "CREATE INDEX quicker_sync_ix ON quicker_sync_ack (key_text)"
                        "CREATE TRIGGER IF NOT EXISTS trg_{{ table.name }} AFTER INSERT"
                        "CREATE TABLE " + name + " ("
                        "CREATE VIEW v AS SELECT 1"
                        """,
                        "TABLE sync_journal",
                        "INDEX ix_journal",
                        "TABLE \""
                    ),
                ]
            ),
            // CREATE TRIGGER（IF NOT EXISTS つきも・大文字小文字を問わない）の名前のうち、前置きで始まらないもの（引用符で囲んだ前置き・
            // 補間の穴の前に前置きを直書きした名前は補助の物）と、名前が補間の穴・Scriban の式で始まるものを拾う。
            // 文の綴りだけを書いた XmlDoc と、CREATE TABLE・CREATE INDEX（別の規則が読む）は拾わない
            new SourceRuleProbe(
                "sync-auxiliary-trigger-names-in-template-forms",
                "sync-auxiliary-trigger-names-in-template",
                [
                    new ProbeFile(
                        "Probe.scriban",
                        """
                        /// <c>CREATE TRIGGER IF NOT EXISTS</c>.
                        "CREATE TRIGGER IF NOT EXISTS trg_{{ table.name }} AFTER INSERT"
                        "CREATE TRIGGER quicker_sync_trg AFTER INSERT"
                        "create trigger QUICKER_SYNC_TRG_X after delete"
                        $"CREATE TRIGGER \"quicker_sync_trg_{escapedTable}_{suffix}\" {timing} ON"
                        $"CREATE TRIGGER \"{name}\" {timing} ON"
                        "CREATE TRIGGER sync_trg_orders AFTER UPDATE"
                        "CREATE TABLE IF NOT EXISTS sync_journal ("
                        "CREATE INDEX ix_journal ON quicker_sync_journal (id)"
                        """,
                        "TRIGGER trg_{{",
                        "TRIGGER \\\"{name}\\\"",
                        "TRIGGER sync_trg_orders"
                    ),
                ]
            ),
            // 文字列リテラル（普通・逐語・生・UTF-8）と補間の文字の部分の、前置きを大文字小文字を無視して含む値を拾い、コメント・XmlDoc・
            // 前置きの途中で切れた綴り・識別子は拾わない。.g.cs は読まない
            new SourceRuleProbe(
                "sync-auxiliary-name-prefix-copies-forms",
                "sync-auxiliary-name-prefix-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """"
                        /// <summary>quicker_sync_journal を外す</summary>
                        internal static class Probe
                        {
                            // "quicker_sync_ack"
                            public static string A() => "quicker_sync_journal";
                            public static string B() => @"DELETE FROM QUICKER_SYNC_ACK";
                            public static string C(string t) => $"SELECT * FROM quicker_sync_{t}";
                            public static string D() => """quicker_Sync_row_pending""";
                            public static System.ReadOnlySpan<byte> E() => "quicker_sync_x"u8;
                            public static string F() => "quicker_syncjournal";
                            public static string G() => "sync_journal";
                            public static string H() => nameof(quicker_sync_x);
                            private static int quicker_sync_x;
                        }
                        """",
                        "quicker_sync_journal",
                        "DELETE FROM QUICKER_SYNC_ACK",
                        "SELECT * FROM quicker_sync_",
                        "quicker_Sync_row_pending",
                        "quicker_sync_x"
                    ),
                    new ProbeFile(
                        "Probe.g.cs",
                        "internal static class P { const string A = \"quicker_sync_x\"; }"
                    ),
                ]
            ),
            // 構文の節の規則は、条件コンパイルの外れる分岐の中も読む
            new SourceRuleProbe(
                "sync-auxiliary-name-prefix-copies-inactive-branch",
                "sync-auxiliary-name-prefix-copies",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal static class Probe
                        {
                        #if QUICKER_PROBE_BRANCH
                            public const string Table = "quicker_sync_journal";
                        #else
                            public const string Table = "journal";
                        #endif
                        }
                        """,
                        "quicker_sync_journal"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 入れ物を作る式（素の名前・名前空間で修飾した名前・初期化子つき）を拾い、型の宣言・引数の型・XmlDoc の cref・文字列・
            // コメント・名前の一部だけが同じ型・型を書かない new() は拾わない
            new SourceRuleProbe(
                "sync-plan-probe-only-in-tests-forms",
                "sync-plan-probe-only-in-tests",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        namespace Scheduling
                        {
                            internal sealed class SyncPlanProbe
                            {
                                public int Count { get; set; }
                            }

                            internal sealed class SyncPlanProbes { }
                        }

                        internal static class Planner
                        {
                            /// <summary><see cref="Scheduling.SyncPlanProbe"/> を作る</summary>
                            public static object A() => new Scheduling.SyncPlanProbe();

                            public static object B() => new Scheduling.SyncPlanProbe { Count = 1 };

                            // new SyncPlanProbe()
                            public static object C() => new Scheduling.SyncPlanProbes();

                            public static string D() => "new SyncPlanProbe()";

                            public static Scheduling.SyncPlanProbe E(Scheduling.SyncPlanProbe? probe)
                            {
                                Scheduling.SyncPlanProbe made = new();
                                return probe ?? made;
                            }
                        }
                        """,
                        "new SyncPlanProbe",
                        "new SyncPlanProbe"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の生成も拾う
            new SourceRuleProbe(
                "sync-plan-probe-only-in-tests-inactive-branch",
                "sync-plan-probe-only-in-tests",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed class SyncPlanProbe { }

                        internal static class Planner
                        {
                            public static object? Make()
                            {
                        #if QUICKER_PROBE_BRANCH
                                return new SyncPlanProbe();
                        #else
                                return null;
                        #endif
                            }
                        }
                        """,
                        "new SyncPlanProbe"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
            // 差分の項目・警告を作る式と、差分の項目の複製を作る呼び出し（素の・?. の）を拾い、文の生成・with 式・型の宣言・
            // メソッドの宣言・XmlDoc・文字列・名前の一部だけが同じ型と呼び出しは拾わない
            new SourceRuleProbe(
                "sync-scheduling-builds-steps-only-forms",
                "sync-scheduling-builds-steps-only",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed class SchemaDiffItem
                        {
                            public string Name { get; init; } = "";

                            public SchemaDiffItem WithForeignKeyColumnPairs(int pairs) => this;

                            public SchemaDiffItem WithRearrangedForeignKeyColumnPairs(int pairs) => this;

                            public SchemaDiffItem WithForeignKeyColumnPairsKept() => this;
                        }

                        internal sealed class SchemaDiffItems { }

                        internal sealed record SyncPlanWarning(string Kind);

                        internal sealed record Step(string Name);

                        internal static class Scheduler
                        {
                            /// <summary><see cref="SchemaDiffItem"/> を作らない</summary>
                            public static object A() => new SchemaDiffItem { Name = "a" };

                            public static object B() => new SyncPlanWarning("kind");

                            public static object C(SchemaDiffItem item) => item.WithForeignKeyColumnPairs(1);

                            public static object? D(SchemaDiffItem? item) =>
                                item?.WithRearrangedForeignKeyColumnPairs(2);

                            // new SchemaDiffItem()
                            public static object E() => new Step("new SyncPlanWarning");

                            public static object F(Step step) => step with { Name = "b" };

                            public static object G() => new SchemaDiffItems();

                            public static object H(SchemaDiffItem item) => item.WithForeignKeyColumnPairsKept();
                        }
                        """,
                        "new SchemaDiffItem",
                        "new SyncPlanWarning",
                        "WithForeignKeyColumnPairs",
                        "WithRearrangedForeignKeyColumnPairs"
                    ),
                ]
            ),
            // 構文の規則は、条件コンパイルの外れる分岐の中の生成も拾う
            new SourceRuleProbe(
                "sync-scheduling-builds-steps-only-inactive-branch",
                "sync-scheduling-builds-steps-only",
                [
                    new ProbeFile(
                        "Probe.cs",
                        """
                        internal sealed record SyncPlanWarning(string Kind);

                        internal static class Scheduler
                        {
                            public static object? Warn()
                            {
                        #if QUICKER_PROBE_BRANCH
                                return new SyncPlanWarning("kind");
                        #else
                                return null;
                        #endif
                            }
                        }
                        """,
                        "new SyncPlanWarning"
                    ),
                ]
            ) with
            {
                InactiveBranch = true,
            },
        ];
}
