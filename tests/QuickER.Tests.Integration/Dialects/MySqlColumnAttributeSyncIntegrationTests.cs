using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using MySqlConnector;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Tests.Integration;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// B: MySQL の「末尾句だけを変える」同期（説明同期・列順同期）が、実 DB の列属性を落とさないことを
/// 実 MySQL で検証する統合テスト。
/// </summary>
/// <remarks>
/// <para>
/// MySQL には列の COMMENT / 位置だけを変える構文が無く、どちらも <c>MODIFY COLUMN</c> による
/// 列定義の完全再指定になる。定義をモデル（図）から復元すると、意味モデルが持たない
/// DEFAULT / AUTO_INCREMENT / 照合順序 / ON UPDATE / INVISIBLE / SRID / 生成列が消える。
/// MySQL の DDL は暗黙コミットで取り消せないため、消えた属性は復旧できない。
/// </para>
/// <para>
/// 表明の形は「同期の前後で <c>information_schema.COLUMNS</c> の該当行を、変えたはずの列以外すべて
/// 行ごと比較する」。項目別の表明だと再構成の 1 句を落としても該当の表明が無ければ緑のままになるため、
/// 「何を落としても必ず赤」になるこの形にする。
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
[Collection(MySqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class MySqlColumnAttributeSyncIntegrationTests(MySqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>列属性を一通り備えた検証用テーブル（行の部品と同じ表）</summary>
    private const string CreateAttrsTable = RealMySqlScenarios.AttrsTableSql;

    /// <summary>比較対象のカタログ項目（<c>ORDINAL_POSITION</c> / <c>COLUMN_COMMENT</c> を含む全項目）</summary>
    private static readonly string[] MetadataFields =
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

    /// <summary>
    /// 実在する列と実在しない列が 1 本のスクリプトに混ざっても、実在列だけが正しく更新され、
    /// 不在列の分がユーザー変数の持ち越しで別の列を汚さないことを検証する。
    /// </summary>
    /// <remarks>
    /// <c>SELECT ... INTO</c> は該当行が無いとユーザー変数を書き換えないため、検索前の初期化を
    /// 落とすと不在列の <c>MODIFY</c> が直前の列の定義を焼き付ける。
    /// </remarks>
    [Fact(DisplayName = "[Integration] B: 存在列と不在列が混在しても実在列だけが更新される")]
    public async Task CommentSync_MixedPresentAndMissingColumns_TouchesOnlyExisting()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(CreateAttrsTable, Ct);

        var before = await ReadColumnMetadataAsync("attrs");

        // 実在（s）→ 不在（gone）→ 実在（b4）の順に並べ、不在の分が次へ持ち越されないことを見る
        await ApplyAsync([
            ColumnDescriptionItem("attrs", "s", "first"),
            ColumnDescriptionItem("attrs", "gone", "missing"),
            ColumnDescriptionItem("attrs", "b4", "third"),
        ]);

        var after = await ReadColumnMetadataAsync("attrs");

        AssertMetadataUnchanged(before, after, except: ["COLUMN_COMMENT"]);
        after["s"]["COLUMN_COMMENT"].Should().Be("first");
        after["b4"]["COLUMN_COMMENT"].Should().Be("third");

        // 説明を指定していない列のコメントは動かない
        foreach (var column in before.Keys.Where(c => c is not ("s" or "b4")))
        {
            after[column]["COLUMN_COMMENT"].Should().Be(before[column]["COLUMN_COMMENT"]);
        }
    }

    /// <summary>
    /// スキーマ修飾名の説明同期が、カレント DB の同名テーブルに一切触れず、対象テーブル自身の
    /// 属性を温存して説明だけを変えることを検証する。
    /// </summary>
    /// <remarks>
    /// カタログ検索を <c>DATABASE()</c> 固定にすると、<c>other.t</c> を対象にした文がカレント DB の
    /// 同名テーブルの定義（型・NULL 許容・DEFAULT）を引き当て、それを <c>other.t</c> へ焼き付ける。
    /// 同名が無ければ引き当て失敗で「説明が付かないまま成功」になる。
    /// </remarks>
    [Fact(DisplayName = "[Integration] B: 修飾名の説明同期は同名の別 DB テーブルを巻き込まない")]
    public async Task CommentSync_QualifiedTable_UsesItsOwnSchema()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // カレント DB 側の同名テーブル（巻き込まれてはいけない側。定義をわざと食い違わせる）
        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`memo` varchar(50) NULL DEFAULT 'current');",
            Ct
        );

        await fixture.ExecuteAsRootAsync("CREATE DATABASE IF NOT EXISTS `quicker_other`;", Ct);

        try
        {
            await fixture.ExecuteAsRootAsync(
                "CREATE TABLE `quicker_other`.`t` (`memo` int NOT NULL DEFAULT 9);",
                Ct
            );
            await GrantOtherSchemaAsync("quicker_other");

            var currentBefore = await ReadColumnMetadataAsync("t");
            var otherBefore = await ReadColumnMetadataAsync("t", "quicker_other");

            await ApplyAsync([ColumnDescriptionItem("quicker_other.t", "memo", "other note")]);

            var currentAfter = await ReadColumnMetadataAsync("t");
            var otherAfter = await ReadColumnMetadataAsync("t", "quicker_other");

            // カレント DB 側は説明も含めて 1 項目も動かない
            AssertMetadataUnchanged(currentBefore, currentAfter, except: []);

            // 対象は説明だけが変わり、自分自身の型・NULL 許容・既定を保つ
            AssertMetadataUnchanged(otherBefore, otherAfter, except: ["COLUMN_COMMENT"]);
            otherAfter["memo"]["COLUMN_COMMENT"].Should().Be("other note");
            otherAfter["memo"]["COLUMN_TYPE"].Should().Be("int");
            otherAfter["memo"]["IS_NULLABLE"].Should().Be("NO");
            otherAfter["memo"]["COLUMN_DEFAULT"].Should().Be("9");
        }
        finally
        {
            await fixture.ExecuteAsRootAsync("DROP DATABASE IF EXISTS `quicker_other`;", Ct);
        }
    }

    /// <summary>
    /// セッションに <c>NO_BACKSLASH_ESCAPES</c> が立っていても、説明が化けずに往復することを検証する。
    /// </summary>
    /// <remarks>
    /// 同期スクリプトのリテラルは「バックスラッシュもエスケープ文字」という MySQL 既定を前提に
    /// 組み立てられている。実行器がセッションからこのモードを外さないと、説明の <c>\</c> が
    /// 二重のまま格納されて無言で化ける。グローバルを変えて新しいセッションの既定を動かすため、
    /// 接続プールを破棄してから実行し、後始末で必ず元へ戻す。
    /// </remarks>
    [Fact(DisplayName = "[Integration] B: NO_BACKSLASH_ESCAPES のセッションでも説明が化けない")]
    public async Task CommentSync_SurvivesNoBackslashEscapesSessionMode()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`memo` varchar(50) NULL DEFAULT 'keep');",
            Ct
        );

        var original = await ReadGlobalSqlModeAsync();

        try
        {
            await SetGlobalSqlModeAsync($"{original},NO_BACKSLASH_ESCAPES");

            // 新しいセッションが実際にそのモードで始まることを確かめる（検証の空洞化を防ぐ自己点検）
            (await ReadSessionSqlModeAsync())
                .Should()
                .Contain("NO_BACKSLASH_ESCAPES");

            const string Description = @"a\b 'q' \\ end";
            await ApplyAsync([ColumnDescriptionItem("t", "memo", Description)]);

            var after = await ReadColumnMetadataAsync("t");
            after["memo"]["COLUMN_COMMENT"].Should().Be(Description);
            after["memo"]["COLUMN_DEFAULT"].Should().Be("keep");
        }
        finally
        {
            await SetGlobalSqlModeAsync(original);
        }
    }

    // ---------------- ヘルパー ----------------

    /// <summary>統合テスト用の別データベースを、フィクスチャの通常ユーザーからも触れるようにする</summary>
    /// <remarks>
    /// 同期実行器は通常ユーザーで接続するため、権限が無いと「修飾名の巻き込み」ではなく
    /// アクセス拒否で落ちてしまい、検証したい性質に届かない。
    /// </remarks>
    private async Task GrantOtherSchemaAsync(string schema)
    {
        var user = fixture.ToDbConnectionSettings().UserId.Replace("'", "''");
        await fixture.ExecuteAsRootAsync(
            $"GRANT ALL PRIVILEGES ON `{schema}`.* TO '{user}'@'%';",
            Ct
        );
        await fixture.ExecuteAsRootAsync("FLUSH PRIVILEGES;", Ct);
        // プールに残った接続は権限の再評価が保証されないため捨てる
        await MySqlConnection.ClearAllPoolsAsync(Ct);
    }

    /// <summary>グローバルの <c>sql_mode</c> を読み出す</summary>
    private async Task<string> ReadGlobalSqlModeAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new MySqlCommand("SELECT @@GLOBAL.sql_mode;", conn);
        return (string)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>現在のセッションの <c>sql_mode</c> を（新しい接続で）読み出す</summary>
    private async Task<string> ReadSessionSqlModeAsync()
    {
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new MySqlCommand("SELECT @@SESSION.sql_mode;", conn);
        return (string)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>グローバルの <c>sql_mode</c> を設定し、既存のプール接続を捨てて即座に効かせる</summary>
    private async Task SetGlobalSqlModeAsync(string mode)
    {
        await fixture.ExecuteAsRootAsync($"SET GLOBAL sql_mode = '{mode.Replace("'", "''")}';", Ct);
        // プールに残った接続は作成時のモードを引き継ぐため、毎回捨てて新しいセッションを引かせる
        await MySqlConnection.ClearAllPoolsAsync(Ct);
    }

    /// <summary>列説明の差分項目を組み立てる（列定義はライブ再構成のため Entity は使われない）</summary>
    /// <summary>
    /// 列の変更（型・NULL 許容）が、図の型と NULL 許容だけを差し替え、ほかの列属性（DEFAULT・AUTO_INCREMENT・照合順序と
    /// 文字集合・ON UPDATE・INVISIBLE・SRID・COMMENT）を保つことを検証する（以前は MODIFY を図から組み、これらを落としていた）。
    /// 文字列型から JSON 型へ変える列は、照合順序を書き添えずに通る（JSON は COLLATE を受け付けない）。変えない列は 1 項目も動かない。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: 列の変更は型と NULL 許容だけを変え、ほかの列属性を保つ")]
    public async Task AlterColumn_ChangesOnlyTypeAndNullability()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(CreateAttrsTable, Ct);
        await fixture.ExecuteAsync(
            "ALTER TABLE `attrs` MODIFY COLUMN `n` int NOT NULL DEFAULT -1 COMMENT 'keep me';",
            Ct
        );

        var before = await ReadColumnMetadataAsync("attrs");

        // 列名 → (新しい型, NULL 許容, 主キーか)
        var changes = new Dictionary<string, (string Type, bool Nullable, bool Key)>
        {
            ["id"] = ("bigint", false, true),
            ["s"] = ("varchar(40)", true, false),
            ["n"] = ("bigint", false, false),
            ["ts"] = ("timestamp(6)", true, false),
            ["cs"] = ("varchar(20)", true, false),
            ["inv"] = ("bigint", true, false),
            ["ivu"] = ("timestamp(3)", true, false),
            ["geo"] = ("geometry", false, false),
            ["nl"] = ("json", true, false),
        };

        await ApplyAsync(
            changes.Select(c => new SchemaDiffItem
            {
                Kind = SchemaDiffKind.AlterColumn,
                TableName = "attrs",
                ColumnName = c.Key,
                Column = new Column
                {
                    Name = c.Key,
                    DataType = c.Value.Type,
                    IsNullable = c.Value.Nullable,
                    IsPrimaryKey = c.Value.Key,
                },
                IsSelected = true,
            })
        );

        var after = await ReadColumnMetadataAsync("attrs");
        string[] typeFields =
        [
            "COLUMN_TYPE",
            "DATA_TYPE",
            "CHARACTER_MAXIMUM_LENGTH",
            "NUMERIC_PRECISION",
            "NUMERIC_SCALE",
            "IS_NULLABLE",
        ];

        // 変えない列は 1 項目も動かない
        AssertMetadataUnchanged(
            before.Where(c => !changes.ContainsKey(c.Key)).ToDictionary(),
            after.Where(c => !changes.ContainsKey(c.Key)).ToDictionary(),
            except: []
        );

        foreach (var (column, change) in changes)
        {
            after[column]["COLUMN_TYPE"].Should().StartWith(change.Type, column);
            after[column]["IS_NULLABLE"].Should().Be(change.Nullable ? "YES" : "NO", column);

            // 文字列型から JSON 型へ変える列は、照合順序・文字集合が無くなるのが正しい
            var except =
                column == "nl"
                    ? typeFields.Concat(["CHARACTER_SET_NAME", "COLLATION_NAME"]).ToArray()
                    : typeFields;

            foreach (var (field, value) in before[column])
            {
                if (!except.Contains(field))
                {
                    after[column]
                        [field]
                        .Should()
                        .Be(value, $"{column}.{field} は列の変更で変わらないこと");
                }
            }
        }

        // 自己点検：保つべき属性を実際に持っていたこと
        after["id"]["EXTRA"].Should().Contain("auto_increment");
        after["s"]["COLLATION_NAME"].Should().Be("utf8mb4_bin");
        after["s"]["COLUMN_DEFAULT"].Should().Be("a'b");
        after["n"]["COLUMN_COMMENT"].Should().Be("keep me");
        after["cs"]["CHARACTER_SET_NAME"].Should().Be("latin1");
        after["ts"]["EXTRA"].Should().Contain("on update CURRENT_TIMESTAMP(6)");
        after["inv"]["EXTRA"].Should().Contain("INVISIBLE");
        after["geo"]["SRS_ID"].Should().Be("4326");
    }

    /// <summary>
    /// 列の変更は、実行時に列が無ければ失敗させることを検証する（選んだ列の変更が黙って何もしないのは誤り）。
    /// 説明・列順の同期は、列が無ければ何もしない（<see cref="CommentSync_MixedPresentAndMissingColumns_TouchesOnlyExisting"/>）。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: 列の変更は、実行時に列が無ければ失敗する")]
    public async Task AlterColumn_MissingColumn_Fails()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`id` int NOT NULL, CONSTRAINT `PK_t` PRIMARY KEY (`id`));",
            Ct
        );

        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "t",
                    ColumnName = "ghost",
                    Column = new Column { Name = "ghost", DataType = "int" },
                    IsSelected = true,
                },
            ],
            TestDialectCapabilities.Neutral
        );
        var script = new MySqlSyncScriptBuilder().Build(plan);
        var result = await new MySqlSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );

        result.Committed.Should().BeFalse(script);
        result.Error.Should().Contain("t.ghost");
    }

    private static SchemaDiffItem ColumnDescriptionItem(
        string table,
        string column,
        string description
    ) =>
        new()
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = table,
            ColumnName = column,
            NewDescription = description,
            IsSelected = true,
        };

    /// <summary>差分項目から同期スクリプトを組み立てて実行する</summary>
    private async Task ApplyAsync(IEnumerable<SchemaDiffItem> items)
    {
        var plan = new SyncPlanner().BuildPlan(items, TestDialectCapabilities.Neutral);
        await ExecuteAsync(new MySqlSyncScriptBuilder().Build(plan));
    }

    /// <summary>同期スクリプトを実行し、コミットされたことを確かめる</summary>
    private async Task ExecuteAsync(string script)
    {
        var result = await new MySqlSchemaSyncExecutor().ExecuteAsync(
            fixture.ToDbConnectionSettings(),
            script,
            Ct
        );
        result.Committed.Should().BeTrue($"同期に失敗: {result.Error}\nSQL:\n{script}");
    }

    /// <summary>対象テーブルの列カタログを「列名 → 項目名 → 値」で読み出す</summary>
    /// <param name="table">テーブル名</param>
    /// <param name="schema">検索するスキーマ（<c>null</c> ならカレント DB）</param>
    private async Task<Dictionary<string, Dictionary<string, string?>>> ReadColumnMetadataAsync(
        string table,
        string? schema = null
    )
    {
        var scope = schema is null ? "DATABASE()" : "@s";
        var sql =
            $"SELECT COLUMN_NAME, {string.Join(", ", MetadataFields)} "
            + "FROM information_schema.COLUMNS "
            + $"WHERE TABLE_SCHEMA = {scope} AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION;";

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", table);

        if (schema is not null)
        {
            cmd.Parameters.AddWithValue("@s", schema);
        }

        var rows = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(Ct);

        while (await reader.ReadAsync(Ct))
        {
            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);

            foreach (var field in MetadataFields)
            {
                var ordinal = reader.GetOrdinal(field);
                fields[field] = reader.IsDBNull(ordinal)
                    ? null
                    : reader.GetValue(ordinal).ToString();
            }

            rows[reader.GetString(reader.GetOrdinal("COLUMN_NAME"))] = fields;
        }

        rows.Should().NotBeEmpty($"テーブル {table} の列が読めること");
        return rows;
    }

    /// <summary>指定した項目以外のカタログ値が同期の前後で 1 つも変わっていないことを表明する</summary>
    private static void AssertMetadataUnchanged(
        Dictionary<string, Dictionary<string, string?>> before,
        Dictionary<string, Dictionary<string, string?>> after,
        string[] except
    )
    {
        after.Keys.Should().BeEquivalentTo(before.Keys, "列が増減していないこと");

        foreach (var (column, fields) in before)
        {
            foreach (var (field, value) in fields)
            {
                if (except.Contains(field))
                {
                    continue;
                }

                after[column][field].Should().Be(value, $"{column}.{field} は同期で変わらないこと");
            }
        }
    }
}
