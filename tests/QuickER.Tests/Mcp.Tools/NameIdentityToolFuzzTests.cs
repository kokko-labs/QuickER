using System.IO;
using System.Text.Json;
using QuickER.CodeGen.CSharp.Queries;
using QuickER.Documents;
using QuickER.Mcp.Tools;
using QuickER.Model;
using QuickER.Services;
using QuickER.Tests.TestSupport;
using QuickER.ViewModels;

namespace QuickER.Tests.Mcp.Tools;

/// <summary>
/// 図を編集するツール（GUI 側 <see cref="ErDiagramDynamicTools"/> と MCP の <see cref="DocumentErDiagramToolHost"/>・
/// 名前付きクエリの共有コア <see cref="QueryToolCore"/>）へ名前の総当たり（<see cref="NameFuzz"/>）を流し、
/// 重複の判定が <see cref="NameIdentityRule"/> と同じ答えになることを確かめるテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// 名前の組 (先, 後) を順に入れ、規則が「同じ名前」と言う組は後を拒否（クエリ名は同じ定義の上書き）、「別の名前」と言う組は
/// 後を別の要素として受け付けることを表明する。受け付けた後は、後の名前で次のツールを呼び、受け付けた要素そのものに
/// 届くことも確かめる（届かない＝以後どのツールからも引けない要素ができている）。
/// </para>
/// <para>
/// 1 つだけ入れても拒否される名前（入口の名前の規則で弾かれるもの）は、重複の判定とは別の理由なので組ごと流さない。
/// 結果は既知の失敗の一覧（<see cref="NameFuzzKnownFailures"/>）と厳密に照合する。
/// </para>
/// </remarks>
public sealed class NameIdentityToolFuzzTests
{
    /// <summary>ツールを実行するホスト</summary>
    public enum Host
    {
        /// <summary>GUI 側（<see cref="ErDiagramDynamicTools"/>＋<see cref="MainViewModel"/>）</summary>
        Gui,

        /// <summary>MCP 側（<see cref="DocumentErDiagramToolHost"/>＋一時ファイル）</summary>
        Mcp,
    }

    [Theory(DisplayName = "図のツールの重複の判定は名前の規則と同じ答えになる（両ホスト）")]
    [InlineData(Host.Gui)]
    [InlineData(Host.Mcp)]
    public void DiagramTools_AgreeWithNameRule(Host host)
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);
        var prefix = host == Host.Gui ? "gui-tool:" : "mcp-tool:";

        NameFuzzRunner.Run(
            ledger,
            prefix + "add_entity",
            NameKind.Table,
            name =>
                Session.With(host, s => s.Exec("add_entity", new { table_name = name }).Success),
            pair => Session.With(host, s => AddTwoTables(s, pair))
        );
        NameFuzzRunner.Run(
            ledger,
            prefix + "set_entity_property",
            NameKind.Table,
            name =>
                Session.With(host, s => s.Exec("add_entity", new { table_name = name }).Success),
            pair => Session.With(host, s => RenameTable(s, pair))
        );
        NameFuzzRunner.Run(
            ledger,
            prefix + "add_column",
            NameKind.Column,
            name => Session.With(host, s => AddColumns(s, name).Last().Success),
            pair => Session.With(host, s => AddTwoColumns(s, pair))
        );
        NameFuzzRunner.Run(
            ledger,
            prefix + "set_unique_constraint",
            NameKind.Constraint,
            name => Session.With(host, s => AddUniqueConstraints(s, name).Last().Success),
            pair =>
                Session.With(
                    host,
                    s =>
                    {
                        var results = AddUniqueConstraints(s, pair.First, pair.Second);
                        var stored = s
                            .Schema.Entities.Single(entity => entity.TableName == "T")
                            .UniqueConstraints.Select(constraint => constraint.Name ?? "")
                            .ToList();

                        return Stored(results[1], pair.Second, stored.ElementAtOrDefault(1));
                    }
                )
        );
        NameFuzzRunner.Run(
            ledger,
            prefix + "add_relationship",
            NameKind.Constraint,
            name => Session.With(host, s => AddRelationships(s, name).Last().Success),
            pair =>
                Session.With(
                    host,
                    s =>
                    {
                        var results = AddRelationships(s, pair.First, pair.Second);
                        var stored = s
                            .Schema.Relationships.Select(r => r.ConstraintName ?? "")
                            .ToList();

                        return Stored(results[1], pair.Second, stored.ElementAtOrDefault(1));
                    }
                )
        );

        ledger.AssertMatchesKnown(prefix);
    }

    [Fact(DisplayName = "名前付きクエリのツールの重複の判定は名前の規則と同じ答えになる")]
    public void QueryTool_AgreesWithNameRule()
    {
        var ledger = new FuzzFailureLog(NameFuzzKnownFailures.Set);

        NameFuzzRunner.Run(
            ledger,
            "query-tool:query_name",
            NameKind.Query,
            name => SetQueries(QueryTarget(), new { query_name = name }).Last().Success,
            pair =>
            {
                var diagram = QueryTarget();
                var results = SetQueries(
                    diagram,
                    new { query_name = pair.First },
                    new { query_name = pair.Second }
                );
                var detail =
                    $"{QueryToolEnglishFormatter.FormatSetQuery(results[1])} / queries={diagram.Queries.Count}";

                // クエリ名は (テーブル, 名前) 照合の上書き＝同じ名前なら 1 件のまま成功し、別の名前なら 2 件になる
                if (diagram.Queries.Count != 2)
                {
                    var sameButRejected =
                        !results[1].Success
                        && NameIdentityRule.AreSame(NameKind.Query, pair.First, pair.Second);

                    return new PairOutcome(
                        false,
                        sameButRejected ? ["upsert-rejected"] : [],
                        detail
                    );
                }

                // 別の名前として受け付けたら、後の名前で消すと後のクエリだけが消える
                var storedSecond = diagram.Queries[1].Name;
                var removed = QueryToolCore.RemoveQuery(
                    diagram,
                    JsonSerializer.SerializeToElement(
                        new { table_name = "T", query_name = pair.Second }
                    )
                );
                var reached =
                    removed.Success
                    && diagram
                        .Queries.Select(query => query.Name.Trim())
                        .SequenceEqual([pair.First.Trim()]);
                List<string> violations = [.. StoredViolation(storedSecond, pair.Second)];

                if (!reached)
                {
                    violations.Add("lookup-wrong-target");
                }

                return new PairOutcome(
                    true,
                    violations,
                    $"{detail} / stored [{NameFuzz.Describe(storedSecond)}] / remove: {QueryToolEnglishFormatter.FormatRemoveQuery(removed)}"
                );
            },
            QueryDefinitionRules.ToMethodName
        );
        NameFuzzRunner.Run(
            ledger,
            "query-tool:parameter",
            NameKind.Parameter,
            name => SetQueries(QueryTarget(), ParametersArgs(name)).Last().Success,
            pair =>
            {
                var diagram = QueryTarget();
                var result = SetQueries(diagram, ParametersArgs(pair.First, pair.Second))[0];
                var stored = diagram.Queries.SelectMany(q => q.Parameters).Select(p => p.Name);

                return StoredQueryPart(result, pair.Second, stored);
            }
        );
        NameFuzzRunner.Run(
            ledger,
            "query-tool:field",
            NameKind.ProjectionField,
            name => SetQueries(QueryTarget(), FieldsArgs(name)).Last().Success,
            pair =>
            {
                var diagram = QueryTarget();
                var result = SetQueries(diagram, FieldsArgs(pair.First, pair.Second))[0];
                var stored = diagram.Queries.SelectMany(q => q.Fields).Select(f => f.Name);

                return StoredQueryPart(result, pair.Second, stored);
            }
        );

        ledger.AssertMatchesKnown("query-tool:");
    }

    /// <summary>保存した名前が規則の保存形（前後の空白を除いた形）でなければ性質の破れを返す</summary>
    private static IEnumerable<string> StoredViolation(string? stored, string input) =>
        stored == NameIdentityRule.StoredForm(input) ? [] : ["stored-form-differs"];

    /// <summary>ツールの結果に、受け付けた名前の保存形の表明を添える</summary>
    private static PairOutcome Stored(
        (string Result, bool Success) result,
        string input,
        string? stored
    ) =>
        result.Success
            ? new PairOutcome(
                true,
                [.. StoredViolation(stored, input)],
                $"{result.Result} / stored [{NameFuzz.Describe(stored ?? "(none)")}]"
            )
            : PairOutcome.Of(false, result.Result);

    /// <summary>set_query の結果に、2 つめの名前（パラメータ・フィールド）の保存形の表明を添える</summary>
    private static PairOutcome StoredQueryPart(
        QueryToolOutcome result,
        string input,
        IEnumerable<string> storedNames
    )
    {
        var stored = storedNames.ElementAtOrDefault(1);
        var detail = QueryToolEnglishFormatter.FormatSetQuery(result);

        return result.Success
            ? new PairOutcome(
                true,
                [.. StoredViolation(stored, input)],
                $"{detail} / stored [{NameFuzz.Describe(stored ?? "(none)")}]"
            )
            : PairOutcome.Of(false, detail);
    }

    /// <summary>テーブルを 2 つ足し、後のテーブル名で列を足して後のテーブルへ届くかを見る</summary>
    private static PairOutcome AddTwoTables(Session s, NamePair pair)
    {
        s.Exec("add_entity", new { table_name = pair.First });
        var second = s.Exec("add_entity", new { table_name = pair.Second });

        return second.Success
            ? WithLookup(s, second, pair.Second)
            : PairOutcome.Of(false, second.Result);
    }

    /// <summary>テーブルを 2 つ足し、2 つめを後の名前へ改名する</summary>
    private static PairOutcome RenameTable(Session s, NamePair pair)
    {
        s.Exec("add_entity", new { table_name = pair.First });
        s.Exec("add_entity", new { table_name = "Zz" });
        var renamed = s.Exec(
            "set_entity_property",
            new { table_name = "Zz", new_table_name = pair.Second }
        );

        return renamed.Success
            ? WithLookup(s, renamed, pair.Second)
            : PairOutcome.Of(false, renamed.Result);
    }

    /// <summary>
    /// 受け付けたテーブル名で列を足し、その名前で受け付けたテーブル（空白を除いた名前が序数一致するもの）へ届くかを見る
    /// </summary>
    private static PairOutcome WithLookup(
        Session s,
        (string Result, bool Success) accepted,
        string name
    )
    {
        var probe = s.Exec(
            "add_column",
            new
            {
                table_name = name,
                column_name = "LookupProbe",
                data_type = "int",
            }
        );
        var target = s
            .Schema.Entities.Where(entity => entity.Columns.Any(c => c.Name == "LookupProbe"))
            .Select(entity => entity.TableName)
            .ToList();
        var reached = probe.Success && target.SequenceEqual([name.Trim()]);

        return new PairOutcome(
            true,
            reached ? [] : ["lookup-wrong-target"],
            $"{accepted.Result} / probe: {probe.Result} \u2192 [{string.Join(", ", target.Select(NameFuzz.Describe))}]"
        );
    }

    /// <summary>テーブル T を作り、列を順に足す（結果は足した列ごと）</summary>
    private static List<(string Result, bool Success)> AddColumns(Session s, params string[] names)
    {
        s.Exec("add_entity", new { table_name = "T" });

        return names
            .Select(name =>
                s.Exec(
                    "add_column",
                    new
                    {
                        table_name = "T",
                        column_name = name,
                        data_type = "int",
                    }
                )
            )
            .ToList();
    }

    /// <summary>列を 2 つ足し、後の列名で列の説明を変えて後の列へ届くかを見る</summary>
    private static PairOutcome AddTwoColumns(Session s, NamePair pair)
    {
        var results = AddColumns(s, pair.First, pair.Second);

        if (!results[1].Success)
        {
            return PairOutcome.Of(false, results[1].Result);
        }

        var probe = s.Exec(
            "set_column_property",
            new
            {
                table_name = "T",
                column_name = pair.Second,
                description = "LookupProbe",
            }
        );
        var columns = s.Schema.Entities.Single(entity => entity.TableName == "T").Columns;
        var target = columns
            .Where(column => column.Description == "LookupProbe")
            .Select(column => column.Name.Trim())
            .ToList();
        var reached = probe.Success && target.SequenceEqual([pair.Second.Trim()]);
        var stored = columns.Select(column => column.Name).ElementAtOrDefault(1);
        List<string> violations = [.. StoredViolation(stored, pair.Second)];

        if (!reached)
        {
            violations.Add("lookup-wrong-target");
        }

        return new PairOutcome(
            true,
            violations,
            $"{results[1].Result} / stored [{NameFuzz.Describe(stored ?? "(none)")}] / probe: {probe.Result}"
        );
    }

    /// <summary>列 C1・C2 を持つテーブル T に、名前を指定した一意制約を列ごとに足す</summary>
    private static List<(string Result, bool Success)> AddUniqueConstraints(
        Session s,
        params string[] names
    )
    {
        AddColumns(s, "C1", "C2");

        return names
            .Select(
                (name, index) =>
                    s.Exec(
                        "set_unique_constraint",
                        new
                        {
                            table_name = "T",
                            columns = new[] { $"C{index + 1}" },
                            name,
                        }
                    )
            )
            .ToList();
    }

    /// <summary>P(Id) を参照する列 C1・C2 を持つ T に、名前を指定した外部キーを列ごとに足す</summary>
    private static List<(string Result, bool Success)> AddRelationships(
        Session s,
        params string[] names
    )
    {
        s.Exec("add_entity", new { table_name = "P" });
        s.Exec(
            "add_column",
            new
            {
                table_name = "P",
                column_name = "Id",
                data_type = "int",
                is_primary_key = true,
            }
        );
        AddColumns(s, "C1", "C2");

        return names
            .Select(
                (name, index) =>
                    s.Exec(
                        "add_relationship",
                        new
                        {
                            source_table = "P",
                            target_table = "T",
                            source_columns = new[] { "Id" },
                            target_columns = new[] { $"C{index + 1}" },
                            relationship_type = "OneToMany",
                            constraint_name = name,
                        }
                    )
            )
            .ToList();
    }

    /// <summary>名前付きクエリの対象の図（テーブル T(Id 主キー, A)）</summary>
    private static ErDiagram QueryTarget() =>
        new()
        {
            TargetDbms = "sqlite",
            Entities =
            [
                new Entity
                {
                    TableName = "T",
                    Columns =
                    [
                        new Column
                        {
                            Name = "Id",
                            DataType = "INTEGER",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column { Name = "A", DataType = "INTEGER" },
                    ],
                },
            ],
        };

    /// <summary>set_query を順に呼ぶ（引数はテーブル T・一覧の戻り形を既定に、渡したプロパティで上書きする）</summary>
    private static List<QueryToolOutcome> SetQueries(
        ErDiagram diagram,
        params object[] overrides
    ) =>
        overrides
            .Select(args =>
            {
                var merged = new Dictionary<string, object?>
                {
                    ["table_name"] = "T",
                    ["query_name"] = "Probe",
                    ["returns"] = "list",
                };

                foreach (var property in JsonSerializer.SerializeToElement(args).EnumerateObject())
                {
                    merged[property.Name] = property.Value;
                }

                return QueryToolCore.SetQuery(diagram, JsonSerializer.SerializeToElement(merged));
            })
            .ToList();

    /// <summary>パラメータだけを並べた set_query の引数</summary>
    private static object ParametersArgs(params string[] names) =>
        new { parameters = names.Select(name => new { name, type = "int32" }).ToArray() };

    /// <summary>射影のフィールドだけを並べた set_query の引数</summary>
    private static object FieldsArgs(params string[] names) =>
        new
        {
            returns = "projection",
            result_type_name = "ProbeRow",
            fields = names.Select(name => new { name, source_column = "A" }).ToArray(),
        };

    /// <summary>1 つの図に対してツールを流すセッション（ホストごと）</summary>
    private sealed class Session : IDisposable
    {
        private readonly Host _host;
        private readonly MainViewModel? _vm;
        private readonly string? _dir;
        private readonly string? _file;

        private Session(Host host)
        {
            _host = host;

            if (host == Host.Gui)
            {
                _vm = new MainViewModel();
                return;
            }

            _dir = Path.Combine(
                Path.GetTempPath(),
                "quicker-name-fuzz-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "diagram.json");
            DocumentErDiagramToolHost.Execute(
                DocumentErDiagramToolHost.CreateDiagramToolName,
                _file,
                JsonSerializer.SerializeToElement(new { target_dbms = "sqlite" })
            );
        }

        /// <summary>新しい図でセッションを作り、処理の結果を返す</summary>
        public static T With<T>(Host host, Func<Session, T> body)
        {
            using var session = new Session(host);
            return body(session);
        }

        /// <summary>ツールを実行する</summary>
        public (string Result, bool Success) Exec(string tool, object args)
        {
            var element = JsonSerializer.SerializeToElement(args);

            return _host == Host.Gui
                ? ErDiagramDynamicTools.Execute(tool, element, _vm!)
                : DocumentErDiagramToolHost.Execute(tool, _file!, element);
        }

        /// <summary>現在の図</summary>
        public ErDiagram Schema =>
            _host == Host.Gui ? _vm!.ToDiagramModel() : JsonStorageService.Load(_file!).Schema;

        public void Dispose()
        {
            if (_dir is null)
            {
                return;
            }

            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // 後始末の失敗はテストの結果に影響させない
            }
        }
    }
}
