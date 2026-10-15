using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Npgsql;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 同期スクリプトが実行時にカタログから読む制約名（旧主キー名・名前不明の外部キー名）の扱いを、実 PostgreSQL で検証する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// 主キーの付け替えと、名前の分からない外部キーの削除は、制約名を実行時に <c>pg_constraint</c> から読み、
/// DO ブロックの動的 SQL で外す。ここで確かめるのは次の 2 つ。
/// </para>
/// <list type="bullet">
///   <item><c>"</c> や <c>'</c> を含む正当な制約名でも同期が成功する（読んだ名前を <c>quote_ident</c> で埋めている）。
///     QuickER 自身も表 <c>a"b</c> に主キー名 <c>PK_a"b</c> を付けるので、特別な DB でなくても起きる</item>
///   <item>対象のテーブルが無ければ何もせず、エラーにもならない</item>
/// </list>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlRuntimeConstraintNameIntegrationTests(
    PostgreSqlContainerFixture fixture
)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>表名・主キー名に <c>"</c> を含む表でも、主キーを付け替える同期が成功することを検証する</summary>
    [Fact(DisplayName = "[Integration] 表名と主キー名に \" を含む表でも主キーの付け替えが成功する")]
    public async Task AlterPrimaryKey_NameWithDoubleQuote_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // QuickER 自身の DDL がこの表に付ける主キー名と同じ形（PK_a"b）
        await fixture.ExecuteAsync(
            "CREATE TABLE \"a\"\"b\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                + "CONSTRAINT \"PK_a\"\"b\" PRIMARY KEY (\"id\"));",
            Ct
        );

        var script = await SyncPrimaryKeyToCodeAsync("a\"b");

        (await PrimaryKeysAsync())
            .Should()
            .Equal(
                ["a\"b -> PK_a\"b (code)"],
                $"主キーが id から code へ付け替わるはず\nSQL:\n{script}"
            );
    }

    /// <summary><c>'</c> を含む主キー名でも、主キーを付け替える同期が成功することを検証する</summary>
    [Fact(DisplayName = "[Integration] 主キー名に ' を含む表でも主キーの付け替えが成功する")]
    public async Task AlterPrimaryKey_NameWithSingleQuote_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE \"odd_orders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                + "CONSTRAINT \"pk_odd'name\" PRIMARY KEY (\"id\"));",
            Ct
        );

        var script = await SyncPrimaryKeyToCodeAsync("odd_orders");

        (await PrimaryKeysAsync())
            .Should()
            .Equal(
                ["odd_orders -> PK_odd_orders (code)"],
                $"旧主キーが外れて新主キーへ置き換わるはず\nSQL:\n{script}"
            );
    }

    /// <summary>
    /// 名前の分からない外部キーの削除で、<c>"</c> と <c>'</c> を含む外部キー名が外れ、
    /// 同じ親を参照する別の表の外部キーは残ることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 名前不明の外部キー削除は \" と ' を含む名前を外し他の表は触らない"
    )]
    public async Task DropForeignKey_WithoutName_NameWithQuotes_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE \"parent\" (\"id\" integer NOT NULL, CONSTRAINT \"pk_parent\" PRIMARY KEY (\"id\"));"
                + "CREATE TABLE \"child\" (\"id\" integer NOT NULL, \"parent_id\" integer NOT NULL, "
                + "CONSTRAINT \"pk_child\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"fk_odd\"\"name'x\" FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\"));"
                + "CREATE TABLE \"child2\" (\"id\" integer NOT NULL, \"parent_id\" integer NOT NULL, "
                + "CONSTRAINT \"pk_child2\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"fk_child2\" FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\"));",
            Ct
        );

        var script = await ApplyAsync(DropForeignKeyWithoutName("child", "parent"));

        (await ForeignKeysAsync())
            .Should()
            .Equal(["child2 -> fk_child2"], $"child の外部キーだけが外れるはず\nSQL:\n{script}");
    }

    /// <summary>対象のテーブルが無いとき、主キー・外部キーの動的 DROP が何もせずエラーにもならないことを検証する</summary>
    /// <remarks>逆引きが 0 行（＝変数が NULL のまま）の経路を通す</remarks>
    [Fact(DisplayName = "[Integration] 対象の表が無いとき動的 DROP は何もせず成功する")]
    public async Task DynamicDrop_MissingTable_IsNoOp()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // 主キーの解除のみ（新しい主キー列なし）＝付与文は出ず、動的 DROP だけが走る
        var dropPrimaryKey = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = "missing_table",
            Entity = new Entity { TableName = "missing_table" },
            IsSelected = true,
        };

        await ApplyAsync(
            dropPrimaryKey,
            DropForeignKeyWithoutName("missing_child", "missing_parent")
        );
    }

    /// <summary>
    /// ドット付きの表（別スキーマの表）でも主キーの付け替えが成功し、public の同名の表を取り違えないことを、
    /// 両方向（スキーマ付きの表を変える・修飾の無い表を変える）で検証する（以前は逆引きが public 固定で、
    /// ドット付きの表の旧主キーを見つけられずに失敗した）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] ドット付きの表の主キーの付け替えが成功し、別スキーマの同名の表を取り違えない"
    )]
    public async Task AlterPrimaryKey_DottedTableName_SucceedsAndKeepsSameNamedTable()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "DROP SCHEMA IF EXISTS \"sales\" CASCADE; CREATE SCHEMA \"sales\";"
                + "CREATE TABLE \"sales\".\"orders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                + "CONSTRAINT \"pk_sales_orders\" PRIMARY KEY (\"id\"));"
                + "CREATE TABLE \"orders\" (\"id\" integer NOT NULL, \"code\" integer NOT NULL, "
                + "CONSTRAINT \"pk_orders\" PRIMARY KEY (\"id\"));",
            Ct
        );

        try
        {
            var script = await ApplyAsync(MovePrimaryKeyToCode("sales.orders"));

            (await AllPrimaryKeysAsync())
                .Should()
                .Equal(
                    ["orders -> pk_orders (id)", "sales.orders -> PK_sales_orders (code)"],
                    $"sales.orders だけが付け替わるはず\nSQL:\n{script}"
                );

            script = await ApplyAsync(MovePrimaryKeyToCode("orders"));

            (await AllPrimaryKeysAsync())
                .Should()
                .Equal(
                    ["orders -> PK_orders (code)", "sales.orders -> PK_sales_orders (code)"],
                    $"public の orders だけが付け替わるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await fixture.ExecuteAsync("DROP SCHEMA IF EXISTS \"sales\" CASCADE;", Ct);
        }
    }

    /// <summary>
    /// 名前の分からない外部キーの削除が、ドット付きの表の外部キーだけを外し、public の同名の表の外部キーは残すことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 名前不明の外部キー削除はドット付きの表の外部キーだけを外し、同名の表は触らない"
    )]
    public async Task DropForeignKey_WithoutName_DottedTables_KeepsSameNamedTables()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "DROP SCHEMA IF EXISTS \"sales\" CASCADE; CREATE SCHEMA \"sales\";"
                + "CREATE TABLE \"sales\".\"parent\" (\"id\" integer NOT NULL, CONSTRAINT \"pk_sp\" PRIMARY KEY (\"id\"));"
                + "CREATE TABLE \"sales\".\"child\" (\"id\" integer NOT NULL, \"parent_id\" integer NOT NULL, "
                + "CONSTRAINT \"pk_sc\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"fk_sales\" FOREIGN KEY (\"parent_id\") REFERENCES \"sales\".\"parent\" (\"id\"));"
                + "CREATE TABLE \"parent\" (\"id\" integer NOT NULL, CONSTRAINT \"pk_parent\" PRIMARY KEY (\"id\"));"
                + "CREATE TABLE \"child\" (\"id\" integer NOT NULL, \"parent_id\" integer NOT NULL, "
                + "CONSTRAINT \"pk_child\" PRIMARY KEY (\"id\"), "
                + "CONSTRAINT \"fk_public\" FOREIGN KEY (\"parent_id\") REFERENCES \"parent\" (\"id\"));",
            Ct
        );

        try
        {
            var script = await ApplyAsync(DropForeignKeyWithoutName("sales.child", "sales.parent"));

            (
                await QueryStringsAsync(
                    """
                    SELECT con.conrelid::regclass::text || ' -> ' || con.conname
                    FROM pg_constraint con
                    WHERE con.contype = 'f' AND con.connamespace IN ('public'::regnamespace, 'sales'::regnamespace)
                    ORDER BY 1;
                    """
                )
            ).Should().Equal(["child -> fk_public"], $"sales の外部キーだけが外れるはず\nSQL:\n{script}");
        }
        finally
        {
            await fixture.ExecuteAsync("DROP SCHEMA IF EXISTS \"sales\" CASCADE;", Ct);
        }
    }

    /// <summary>ドット付きの名前のスキーマが無いときも、主キー・外部キーの動的 DROP は何もせず成功することを検証する</summary>
    [Fact(
        DisplayName = "[Integration] スキーマが無いドット付きの表でも動的 DROP は何もせず成功する"
    )]
    public async Task DynamicDrop_MissingSchema_IsNoOp()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await ApplyAsync(
            new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterPrimaryKey,
                TableName = "nosuch.missing_table",
                Entity = new Entity { TableName = "nosuch.missing_table" },
                IsSelected = true,
            },
            DropForeignKeyWithoutName("nosuch.missing_child", "nosuch.missing_parent")
        );
    }

    // ---------------- ヘルパー ----------------

    /// <summary>取込→主キーを <c>code</c> へ付け替えた目標→差分→同期、を実行し、使ったスクリプトを返す</summary>
    private async Task<string> SyncPrimaryKeyToCodeAsync(string tableName)
    {
        var capabilities = new PostgreSqlProvider().SyncCapabilities;
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var live = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        // 全テーブルを目標へ写し、対象のテーブルだけ主キーを id → code へ付け替える
        var targets = live.Entities.Select(e => e.Clone(preserveId: true)).ToList();
        var target = targets.Single(e => e.TableName == tableName);
        target.Columns.Single(c => c.Name == "id").IsPrimaryKey = false;
        target.Columns.Single(c => c.Name == "code").IsPrimaryKey = true;
        target.PrimaryKeyColumnIds.Clear();

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            targets,
            live.Relationships,
            capabilities
        );
        var alterPrimaryKey = diff
            .Items.Should()
            .ContainSingle(i => i.Kind == SchemaDiffKind.AlterPrimaryKey)
            .Which;
        alterPrimaryKey.IsSelected = true;

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            capabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
            }
        );

        return await ExecuteAsync(new PostgreSqlSyncScriptBuilder().Build(plan));
    }

    /// <summary>差分項目を計画→スクリプト化して実行し、使ったスクリプトを返す</summary>
    private async Task<string> ApplyAsync(params SchemaDiffItem[] items)
    {
        var plan = new SyncPlanner().BuildPlan(items, new PostgreSqlProvider().SyncCapabilities);
        return await ExecuteAsync(new PostgreSqlSyncScriptBuilder().Build(plan));
    }

    /// <summary>スクリプトを同期実行器で流し、コミットされたことを表明する</summary>
    private async Task<string> ExecuteAsync(string script)
    {
        var result = await new PostgreSqlSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
        return script;
    }

    /// <summary>主キーを id から code へ付け替える差分項目（表 id / code の 2 列）を作る</summary>
    private static SchemaDiffItem MovePrimaryKeyToCode(string tableName)
    {
        var target = new Entity { TableName = tableName };
        target.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "integer",
                IsNullable = false,
            }
        );
        target.Columns.Add(
            new Column
            {
                Name = "code",
                DataType = "integer",
                IsNullable = false,
                IsPrimaryKey = true,
            }
        );

        return new SchemaDiffItem
        {
            Kind = SchemaDiffKind.AlterPrimaryKey,
            TableName = tableName,
            Entity = target,
            IsSelected = true,
        };
    }

    /// <summary>public と sales の主キーを「表（検索パスの外はスキーマ付き） -&gt; 制約名 (構成列)」の形で並べて返す</summary>
    private Task<List<string>> AllPrimaryKeysAsync() =>
        QueryStringsAsync(
            """
            SELECT con.conrelid::regclass::text || ' -> ' || con.conname || ' (' || string_agg(a.attname, ',' ORDER BY a.attnum) || ')'
            FROM pg_constraint con
            JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = ANY (con.conkey)
            WHERE con.contype = 'p' AND con.connamespace IN ('public'::regnamespace, 'sales'::regnamespace)
            GROUP BY con.conrelid, con.conname
            ORDER BY 1;
            """
        );

    /// <summary>制約名を持たない外部キー削除の差分項目（名前不明パスを通す）を作る</summary>
    private static SchemaDiffItem DropForeignKeyWithoutName(string child, string parent) =>
        new()
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = child,
            ChildEntity = new Entity { TableName = child },
            ParentEntity = new Entity { TableName = parent },
            IsSelected = true,
        };

    /// <summary>public の全主キーを「テーブル -&gt; 制約名 (構成列)」の形で並べて返す</summary>
    private Task<List<string>> PrimaryKeysAsync() =>
        QueryStringsAsync(
            """
            SELECT c.relname || ' -> ' || con.conname || ' (' || string_agg(a.attname, ',' ORDER BY a.attnum) || ')'
            FROM pg_constraint con
            JOIN pg_class c ON con.conrelid = c.oid
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (con.conkey)
            WHERE con.contype = 'p' AND c.relnamespace = 'public'::regnamespace
            GROUP BY c.relname, con.conname
            ORDER BY 1;
            """
        );

    /// <summary>public の全外部キーを「子テーブル -&gt; 制約名」の形で並べて返す</summary>
    private Task<List<string>> ForeignKeysAsync() =>
        QueryStringsAsync(
            """
            SELECT c.relname || ' -> ' || con.conname
            FROM pg_constraint con
            JOIN pg_class c ON con.conrelid = c.oid
            WHERE con.contype = 'f' AND c.relnamespace = 'public'::regnamespace
            ORDER BY 1;
            """
        );

    /// <summary>1 列の文字列結果を読み出す</summary>
    private async Task<List<string>> QueryStringsAsync(string sql)
    {
        var rows = new List<string>();
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
