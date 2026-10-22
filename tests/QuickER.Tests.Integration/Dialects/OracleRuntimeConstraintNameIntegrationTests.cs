using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Oracle.ManagedDataAccess.Client;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 同期スクリプトが実行時にカタログから読む制約名（旧主キー名・名前不明の外部キー名）を、表の所有者で絞ることを
/// 実 Oracle で検証する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// 主キーの付け替えと名前の分からない外部キーの削除は、制約名を実行時に <c>all_constraints</c> から読み、
/// PL/SQL ブロックの動的 SQL で外す。図の表名がスキーマで修飾されていれば、そのスキーマの表の制約だけを引き、
/// 修飾されていなければ現在のスキーマ（<c>CURRENT_SCHEMA</c>＝修飾しない <c>ALTER TABLE</c> と同じ名前の解決）の
/// 表の制約だけを引く。
/// </para>
/// <para>
/// 別のスキーマ <c>OTHER</c> は SYSTEM で作り、接続ユーザーへは対象の表のオブジェクト権限（ALTER・SELECT・REFERENCES）と、
/// 別のスキーマの主キーを外して付け直すのに要る索引のシステム権限（DROP ANY INDEX・CREATE ANY INDEX）を与える
/// （主キーを外すとその索引も外れるので、権限が無ければ ORA-01418 で失敗する）。
/// <see cref="OracleContainerFixture.ResetSchemaAsync"/> は自スキーマしか消さないので、各テストの最後に <c>OTHER</c> を
/// 撤去し、システム権限も取り消す。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleRuntimeConstraintNameIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// スキーマで修飾した表の主キーの付け替えが、自スキーマの同じ名前の表の主キーを取り違えずに成功することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 修飾した表の主キーの付け替えは、自スキーマの同じ名前の表の主キーを取り違えない"
    )]
    public async Task AlterPrimaryKey_QualifiedTable_KeepsSameNamedTableInOwnSchema()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await PrepareOtherSchemaAsync(
            """
            CREATE TABLE OTHER."T" ("ID" NUMBER(10) NOT NULL, "CODE" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_T" PRIMARY KEY ("ID"));
            GRANT ALTER, SELECT ON OTHER."T" TO ORACLE;
            """
        );

        try
        {
            await fixture.ExecuteAsync(
                "CREATE TABLE \"T\" (\"ID\" NUMBER(10) NOT NULL, \"CODE\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_T\" PRIMARY KEY (\"ID\"));",
                Ct
            );

            var script = await ApplyAsync(MovePrimaryKeyToCode("OTHER.T"));

            (await PrimaryKeysAsync())
                .Should()
                .Equal(
                    ["ORACLE.T -> PK_T (ID)", "OTHER.T -> PK_OTHER_T (CODE)"],
                    $"OTHER.T だけが付け替わるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await DropOtherSchemaAsync();
        }
    }

    /// <summary>
    /// 自スキーマに同じ名前の表が無くても、スキーマで修飾した表の旧主キーを外して付け替えることを検証する
    /// （所有者で絞らない逆引きは旧主キーを見つけられず、新しい主キーの追加が ORA-02260 で失敗していた）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 自スキーマに同じ名前の表が無くても、修飾した表の主キーを付け替える"
    )]
    public async Task AlterPrimaryKey_QualifiedTable_WithoutSameNamedTable_Succeeds()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await PrepareOtherSchemaAsync(
            """
            CREATE TABLE OTHER."T" ("ID" NUMBER(10) NOT NULL, "CODE" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_T" PRIMARY KEY ("ID"));
            GRANT ALTER, SELECT ON OTHER."T" TO ORACLE;
            """
        );

        try
        {
            var script = await ApplyAsync(MovePrimaryKeyToCode("OTHER.T"));

            (await PrimaryKeysAsync())
                .Should()
                .Equal(
                    ["OTHER.T -> PK_OTHER_T (CODE)"],
                    $"旧主キーが外れて新主キーへ置き換わるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await DropOtherSchemaAsync();
        }
    }

    /// <summary>
    /// 名前の分からない外部キーの削除が、修飾した子と親の表の外部キーだけを外し、自スキーマの同じ名前の表の
    /// 外部キーは残すことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 名前不明の外部キー削除は修飾した表の外部キーだけを外し、自スキーマの同じ名前の表は触らない"
    )]
    public async Task DropForeignKey_WithoutName_QualifiedTables_KeepsSameNamedTables()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await PrepareOtherSchemaAsync(
            """
            CREATE TABLE OTHER."P" ("ID" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_P" PRIMARY KEY ("ID"));
            CREATE TABLE OTHER."C" ("ID" NUMBER(10) NOT NULL, "PID" NUMBER(10), CONSTRAINT "PK_OTHER_C" PRIMARY KEY ("ID"), CONSTRAINT "FK_OTHER_C_P" FOREIGN KEY ("PID") REFERENCES OTHER."P" ("ID"));
            GRANT ALTER, SELECT ON OTHER."C" TO ORACLE;
            GRANT SELECT ON OTHER."P" TO ORACLE;
            """
        );

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE "P" ("ID" NUMBER(10) NOT NULL, CONSTRAINT "PK_P" PRIMARY KEY ("ID"));
                CREATE TABLE "C" ("ID" NUMBER(10) NOT NULL, "PID" NUMBER(10), CONSTRAINT "PK_C" PRIMARY KEY ("ID"), CONSTRAINT "FK_C_P" FOREIGN KEY ("PID") REFERENCES "P" ("ID"));
                """,
                Ct
            );

            var script = await ApplyAsync(DropForeignKeyWithoutName("OTHER.C", "OTHER.P"));

            (await ForeignKeysAsync())
                .Should()
                .Equal(
                    ["ORACLE.C -> FK_C_P"],
                    $"OTHER.C の外部キーだけが外れるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await DropOtherSchemaAsync();
        }
    }

    /// <summary>
    /// 子が別のスキーマの表、親が自スキーマの表の外部キーでも、名前の分からない外部キーの削除がその外部キーだけを外す
    /// ことを検証する（子の所有者と親の所有者を別々に絞る）。
    /// </summary>
    /// <remarks>
    /// 子の表は、同じ名前の親（自スキーマの <c>P</c> と <c>OTHER.P</c>）を 1 本ずつ参照する。親の所有者で絞らないと
    /// 逆引きが 2 行になって失敗する。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] 名前不明の外部キー削除は、子が別のスキーマで親が自スキーマの外部キーも外す"
    )]
    public async Task DropForeignKey_WithoutName_ChildInOtherSchemaParentInOwnSchema()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await PrepareOtherSchemaAsync(
            """
            CREATE TABLE OTHER."P" ("ID" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_P" PRIMARY KEY ("ID"));
            GRANT SELECT ON OTHER."P" TO ORACLE;
            """
        );

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE "P" ("ID" NUMBER(10) NOT NULL, CONSTRAINT "PK_P" PRIMARY KEY ("ID"));
                CREATE TABLE "C" ("ID" NUMBER(10) NOT NULL, "PID" NUMBER(10), CONSTRAINT "PK_C" PRIMARY KEY ("ID"), CONSTRAINT "FK_C_P" FOREIGN KEY ("PID") REFERENCES "P" ("ID"));
                GRANT REFERENCES ON "P" TO OTHER;
                """,
                Ct
            );
            await fixture.ExecuteAsSystemAsync(
                """
                CREATE TABLE OTHER."C" ("ID" NUMBER(10) NOT NULL, "PID" NUMBER(10), "OPID" NUMBER(10), CONSTRAINT "PK_OTHER_C" PRIMARY KEY ("ID"), CONSTRAINT "FK_OTHER_C_ORACLE_P" FOREIGN KEY ("PID") REFERENCES ORACLE."P" ("ID"), CONSTRAINT "FK_OTHER_C_OTHER_P" FOREIGN KEY ("OPID") REFERENCES OTHER."P" ("ID"));
                GRANT ALTER, SELECT ON OTHER."C" TO ORACLE;
                """,
                Ct
            );

            var script = await ApplyAsync(DropForeignKeyWithoutName("OTHER.C", "P"));

            (await ForeignKeysAsync())
                .Should()
                .Equal(
                    ["ORACLE.C -> FK_C_P", "OTHER.C -> FK_OTHER_C_OTHER_P"],
                    $"OTHER.C から自スキーマの P への外部キーだけが外れるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await DropOtherSchemaAsync();
        }
    }

    /// <summary>
    /// 修飾しない表の主キーの付け替えと名前の分からない外部キーの削除が、別のスキーマに同じ名前の表があっても、
    /// 自スキーマの表だけに効くことを検証する（対照）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 修飾しない表の主キーの付け替えと外部キーの削除は、自スキーマの表だけに効く"
    )]
    public async Task UnqualifiedTables_AffectOwnSchemaOnly()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await PrepareOtherSchemaAsync(
            """
            CREATE TABLE OTHER."T" ("ID" NUMBER(10) NOT NULL, "CODE" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_T" PRIMARY KEY ("ID"));
            CREATE TABLE OTHER."P" ("ID" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_P" PRIMARY KEY ("ID"));
            CREATE TABLE OTHER."C" ("ID" NUMBER(10) NOT NULL, "PID" NUMBER(10), CONSTRAINT "PK_OTHER_C" PRIMARY KEY ("ID"), CONSTRAINT "FK_OTHER_C_P" FOREIGN KEY ("PID") REFERENCES OTHER."P" ("ID"));
            GRANT ALTER, SELECT ON OTHER."T" TO ORACLE;
            GRANT ALTER, SELECT ON OTHER."C" TO ORACLE;
            GRANT SELECT ON OTHER."P" TO ORACLE;
            """
        );

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE "T" ("ID" NUMBER(10) NOT NULL, "CODE" NUMBER(10) NOT NULL, CONSTRAINT "PK_T" PRIMARY KEY ("ID"));
                CREATE TABLE "P" ("ID" NUMBER(10) NOT NULL, CONSTRAINT "PK_P" PRIMARY KEY ("ID"));
                CREATE TABLE "C" ("ID" NUMBER(10) NOT NULL, "PID" NUMBER(10), CONSTRAINT "PK_C" PRIMARY KEY ("ID"), CONSTRAINT "FK_C_P" FOREIGN KEY ("PID") REFERENCES "P" ("ID"));
                """,
                Ct
            );

            var script = await ApplyAsync(
                MovePrimaryKeyToCode("T"),
                DropForeignKeyWithoutName("C", "P")
            );

            (await PrimaryKeysAsync())
                .Where(k => k.Contains(".T "))
                .Should()
                .Equal(
                    ["ORACLE.T -> PK_T (CODE)", "OTHER.T -> PK_OTHER_T (ID)"],
                    $"自スキーマの T だけが付け替わるはず\nSQL:\n{script}"
                );
            (await ForeignKeysAsync())
                .Should()
                .Equal(
                    ["OTHER.C -> FK_OTHER_C_P"],
                    $"自スキーマの C の外部キーだけが外れるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await DropOtherSchemaAsync();
        }
    }

    /// <summary>
    /// 現在のスキーマを変えたセッション（SQL*Plus で <c>ALTER SESSION SET CURRENT_SCHEMA</c> してからスクリプトを流す）でも、
    /// 修飾しない表の主キーの付け替えが、修飾しない <c>ALTER TABLE</c> が指す表（現在のスキーマの表）の主キーを引くことを
    /// 検証する（接続ユーザーのスキーマで引くと、接続ユーザーの同じ名前の表の主キーを別の表から外そうとして失敗する）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] 現在のスキーマを変えたセッションでも、修飾しない表の主キーの付け替えはその表の主キーを引く"
    )]
    public async Task AlterPrimaryKey_Unqualified_FollowsCurrentSchema()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await PrepareOtherSchemaAsync(
            """
            CREATE TABLE OTHER."T" ("ID" NUMBER(10) NOT NULL, "CODE" NUMBER(10) NOT NULL, CONSTRAINT "PK_OTHER_T" PRIMARY KEY ("ID"));
            GRANT ALTER, SELECT ON OTHER."T" TO ORACLE;
            """
        );

        try
        {
            await fixture.ExecuteAsync(
                "CREATE TABLE \"T\" (\"ID\" NUMBER(10) NOT NULL, \"CODE\" NUMBER(10) NOT NULL, CONSTRAINT \"PK_T\" PRIMARY KEY (\"ID\"));",
                Ct
            );
            var script = new OracleSyncScriptBuilder().Build(
                new SyncPlanner().BuildPlan(
                    [MovePrimaryKeyToCode("T")],
                    new OracleProvider().SyncCapabilities
                )
            );

            var (exitCode, output) = await fixture.RunSqlPlusAsync(
                "ALTER SESSION SET CURRENT_SCHEMA = OTHER;\n" + script,
                Ct
            );

            exitCode.Should().Be(0, $"SQL*Plus の出力:\n{output}\nSQL:\n{script}");
            (await PrimaryKeysAsync())
                .Should()
                .Equal(
                    ["ORACLE.T -> PK_T (ID)", "OTHER.T -> PK_T (CODE)"],
                    $"現在のスキーマの OTHER.T だけが付け替わるはず\nSQL:\n{script}"
                );
        }
        finally
        {
            await DropOtherSchemaAsync();
        }
    }

    // ---------------- ヘルパー ----------------

    /// <summary>自スキーマを空にし、SYSTEM で別のスキーマ <c>OTHER</c> を作って、渡した文を SYSTEM で流す</summary>
    private async Task PrepareOtherSchemaAsync(string setupAsSystem)
    {
        await fixture.ResetSchemaAsync(Ct);
        await DropOtherSchemaAsync();
        await fixture.ExecuteAsSystemAsync(
            """
            CREATE USER OTHER IDENTIFIED BY "other_pw" QUOTA UNLIMITED ON USERS;
            GRANT DROP ANY INDEX, CREATE ANY INDEX TO ORACLE;
            """,
            Ct
        );

        if (setupAsSystem.Length > 0)
        {
            await fixture.ExecuteAsSystemAsync(setupAsSystem, Ct);
        }
    }

    /// <summary>別のスキーマ <c>OTHER</c> が在れば中のオブジェクトごと撤去し、与えたシステム権限を取り消す</summary>
    private Task DropOtherSchemaAsync() =>
        fixture.ExecuteAsSystemAsync(
            """
            BEGIN
                EXECUTE IMMEDIATE 'DROP USER OTHER CASCADE';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -1918 THEN
                        RAISE;
                    END IF;
            END;
            /
            BEGIN
                EXECUTE IMMEDIATE 'REVOKE DROP ANY INDEX, CREATE ANY INDEX FROM ORACLE';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -1952 THEN
                        RAISE;
                    END IF;
            END;
            """,
            Ct
        );

    /// <summary>差分項目を計画→スクリプト化して実行し、使ったスクリプトを返す</summary>
    private async Task<string> ApplyAsync(params SchemaDiffItem[] items)
    {
        var plan = new SyncPlanner().BuildPlan(items, new OracleProvider().SyncCapabilities);
        var script = new OracleSyncScriptBuilder().Build(plan);
        var result = await new OracleSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
        return script;
    }

    /// <summary>主キーを ID から CODE へ付け替える差分項目（表 ID / CODE の 2 列）を作る</summary>
    private static SchemaDiffItem MovePrimaryKeyToCode(string tableName)
    {
        var target = new Entity { TableName = tableName };
        target.Columns.Add(
            new Column
            {
                Name = "ID",
                DataType = "NUMBER(10)",
                IsNullable = false,
            }
        );
        target.Columns.Add(
            new Column
            {
                Name = "CODE",
                DataType = "NUMBER(10)",
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

    /// <summary>制約名を持たない外部キー削除の差分項目（名前不明の経路を通す）を作る</summary>
    private static SchemaDiffItem DropForeignKeyWithoutName(string child, string parent) =>
        new()
        {
            Kind = SchemaDiffKind.DropForeignKey,
            TableName = child,
            ChildEntity = new Entity { TableName = child },
            ParentEntity = new Entity { TableName = parent },
            IsSelected = true,
        };

    /// <summary>ORACLE と OTHER の全主キーを「所有者.表 -&gt; 制約名 (構成列)」の形で並べて返す</summary>
    private Task<List<string>> PrimaryKeysAsync() =>
        QueryStringsAsync(
            """
            SELECT c.owner || '.' || c.table_name || ' -> ' || c.constraint_name || ' ('
                || LISTAGG(cc.column_name, ',') WITHIN GROUP (ORDER BY cc.position) || ')'
            FROM all_constraints c
            JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
            WHERE c.constraint_type = 'P' AND c.owner IN ('ORACLE', 'OTHER')
            GROUP BY c.owner, c.table_name, c.constraint_name
            ORDER BY 1
            """
        );

    /// <summary>ORACLE と OTHER の全外部キーを「所有者.子の表 -&gt; 制約名」の形で並べて返す</summary>
    private Task<List<string>> ForeignKeysAsync() =>
        QueryStringsAsync(
            """
            SELECT owner || '.' || table_name || ' -> ' || constraint_name
            FROM all_constraints
            WHERE constraint_type = 'R' AND owner IN ('ORACLE', 'OTHER')
            ORDER BY 1
            """
        );

    /// <summary>1 列の文字列結果を SYSTEM で読み出す（OTHER の制約は接続ユーザーの権限では見えないものがあるため）</summary>
    private async Task<List<string>> QueryStringsAsync(string sql)
    {
        var rows = new List<string>();
        await using var conn = new OracleConnection(fixture.SystemConnectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(Ct);

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
