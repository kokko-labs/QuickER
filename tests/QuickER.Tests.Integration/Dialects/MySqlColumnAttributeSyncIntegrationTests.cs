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

    /// <summary>列属性を一通り備えた検証用テーブル（固定文は英語が正本）</summary>
    /// <remarks>
    /// 生成列の式に文字列リテラルを含めないのは、MySQL が再宣言時に文字リテラルの文字集合導入子を
    /// <c>_latin1</c> → <c>_utf8mb4</c> へ正規化し（<c>SHOW CREATE TABLE</c> は元から後者を表示する）、
    /// 意味は同じでもカタログ上の文字列が動くため。導入子つきの式は
    /// <see cref="Reorder_PreservesGeneratedExpressionContainingStringLiteral"/> が別途押さえる。
    /// </remarks>
    private const string CreateAttrsTable = """
        CREATE TABLE `attrs` (
          `id` int NOT NULL AUTO_INCREMENT,
          `s` varchar(20) COLLATE utf8mb4_bin DEFAULT 'a''b',
          `n` int NOT NULL DEFAULT -1,
          `d` decimal(5,2) DEFAULT 0.5,
          `ts` timestamp(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
          `u` char(36) DEFAULT (uuid()),
          `b` bit(1) DEFAULT b'1',
          `vb` varbinary(20) DEFAULT 'ab',
          `b4` binary(4) DEFAULT 'ab',
          `e` enum('x','y') DEFAULT 'y',
          `se` set('a','b') DEFAULT 'a',
          `iz` int unsigned zerofill DEFAULT 7,
          `cs` varchar(10) CHARACTER SET latin1 DEFAULT 'x',
          `js` json DEFAULT (json_array()),
          `gv` int GENERATED ALWAYS AS (`n` + 1) VIRTUAL,
          `gs` int GENERATED ALWAYS AS (`n` * 2) STORED,
          `inv` int DEFAULT 7 INVISIBLE,
          `ivu` timestamp(3) NULL DEFAULT NULL ON UPDATE CURRENT_TIMESTAMP(3) INVISIBLE,
          `geo` geometry NOT NULL SRID 4326,
          `nl` varchar(10) NULL,
          `nn` varchar(10) NOT NULL,
          PRIMARY KEY (`id`)
        );
        """;

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
    /// 説明同期（COMMENT 設定）が、全列の実属性を COMMENT 以外まったく変えないことを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: 説明同期は COMMENT 以外の列属性を一切変えない")]
    public async Task CommentSync_PreservesEveryLiveColumnAttribute()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(CreateAttrsTable, Ct);

        var before = await ReadColumnMetadataAsync("attrs");

        // フィクスチャが意図した属性を実際に持っていること（検証の空洞化を防ぐ自己点検）。
        // EXTRA は属性が並ぶ欄で、ON UPDATE の値の後ろに INVISIBLE が続き得る＝再構成は値だけを切り出す
        before["id"]["EXTRA"].Should().Contain("auto_increment");
        before["ivu"]["EXTRA"].Should().Contain("on update CURRENT_TIMESTAMP(3)");
        before["ivu"]["EXTRA"].Should().Contain("INVISIBLE");
        before["geo"]["SRS_ID"].Should().Be("4326");
        before["s"]["COLLATION_NAME"].Should().Be("utf8mb4_bin");
        before["gs"]["EXTRA"].Should().Be("STORED GENERATED");
        // バイナリ系のリテラル既定はカタログでは 16 進テキスト（QUOTE すると文字列に化ける形）
        before["vb"]["COLUMN_DEFAULT"].Should().Be("0x6162");
        before["b4"]["COLUMN_DEFAULT"].Should().Be("0x6162");
        before["cs"]["CHARACTER_SET_NAME"].Should().Be("latin1");
        before["iz"]["COLUMN_TYPE"].Should().Contain("zerofill");

        var items = before
            .Keys.Select(column => ColumnDescriptionItem("attrs", column, $"note for {column}"))
            .ToList();

        await ApplyAsync(items);

        var after = await ReadColumnMetadataAsync("attrs");

        // COMMENT だけが変わり、他は 1 項目も動かない
        AssertMetadataUnchanged(before, after, except: ["COLUMN_COMMENT"]);

        foreach (var column in before.Keys)
        {
            after[column]["COLUMN_COMMENT"].Should().Be($"note for {column}");
        }
    }

    /// <summary>
    /// 列順同期が、位置（<c>ORDINAL_POSITION</c>）以外の列属性を——既存の COMMENT を含めて——
    /// まったく変えないことを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] B: 列順同期は位置以外の列属性と COMMENT を保つ")]
    public async Task ReorderSync_PreservesEveryLiveColumnAttributeAndComment()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(CreateAttrsTable, Ct);

        // 既存コメントを付けておき、列順同期が温存することを確かめる（' と \ を含める）
        await fixture.ExecuteAsync(
            "ALTER TABLE `attrs` MODIFY COLUMN `ts` timestamp(6) NOT NULL "
                + "DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6) "
                + "COMMENT 'stamp ''live'' \\\\ note';",
            Ct
        );

        var before = await ReadColumnMetadataAsync("attrs");
        var liveOrder = ColumnsInPositionOrder(before);

        // ts / b / b4 / vb / inv を先頭側へ寄せる（属性の濃い列を実際に動かす。
        // バイナリ系のリテラル既定は再指定の仕方を誤ると化ける／中断するので移動対象に含める）
        var moved = new[] { "ts", "b", "b4", "vb", "inv" };
        var targetOrder = liveOrder
            .Take(1)
            .Concat(moved)
            .Concat(liveOrder.Skip(1).Where(c => !moved.Contains(c)))
            .ToList();

        await ApplyReorderAsync("attrs", before, liveOrder, targetOrder);

        var after = await ReadColumnMetadataAsync("attrs");

        AssertMetadataUnchanged(before, after, except: ["ORDINAL_POSITION"]);
        ColumnsInPositionOrder(after).Should().Equal(targetOrder);
    }

    /// <summary>
    /// 未選択の AlterColumn（モデル側だけの型変更）が、説明同期に相乗りして適用されないことを検証する。
    /// </summary>
    /// <remarks>
    /// モデルから列定義を復元する実装では、説明を変えるだけの同期が「ついでに」型まで変えてしまう。
    /// </remarks>
    [Fact(DisplayName = "[Integration] B: 未選択の型変更は説明同期で適用されない")]
    public async Task CommentSync_DoesNotApplyUnselectedTypeChange()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`id` int NOT NULL, `memo` varchar(50) NULL DEFAULT 'keep', "
                + "CONSTRAINT `PK_t` PRIMARY KEY (`id`));",
            Ct
        );

        // 図（モデル）側では memo を varchar(200) NOT NULL にしてあるが、AlterColumn は選択していない
        var model = new Entity
        {
            TableName = "t",
            Columns =
            {
                new Column
                {
                    Name = "memo",
                    DataType = "varchar(200)",
                    IsNullable = false,
                },
            },
        };

        await ApplyAsync([ColumnDescriptionItem("t", "memo", "説明だけ", model)]);

        var after = await ReadColumnMetadataAsync("t");
        after["memo"]["COLUMN_TYPE"].Should().Be("varchar(50)");
        after["memo"]["IS_NULLABLE"].Should().Be("YES");
        after["memo"]["COLUMN_DEFAULT"].Should().Be("keep");
        after["memo"]["COLUMN_COMMENT"].Should().Be("説明だけ");
    }

    /// <summary>説明の <c>'</c> / <c>\</c> / 改行が欠落も化けもせず往復することを検証する</summary>
    [Fact(DisplayName = "[Integration] B: 説明の ' と \\ と改行がそのまま往復する")]
    public async Task CommentSync_RoundTripsQuotesBackslashesAndNewlines()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            "CREATE TABLE `t` (`id` int NOT NULL, `memo` varchar(50) NULL DEFAULT 'keep');",
            Ct
        );

        const string Description = "O'Brien \\ 「説明」\n2 行目 -- not a comment\t末尾";

        await ApplyAsync([ColumnDescriptionItem("t", "memo", Description)]);

        var after = await ReadColumnMetadataAsync("t");
        after["memo"]["COLUMN_COMMENT"].Should().Be(Description);
        // 巻き添えで既定が消えていないこと
        after["memo"]["COLUMN_DEFAULT"].Should().Be("keep");
    }

    /// <summary>
    /// 文字列リテラルを含む生成列の式が、列順同期の再構成を通しても壊れない（＝計算結果が変わらない）
    /// ことを検証する。
    /// </summary>
    /// <remarks>
    /// <c>information_schema.GENERATION_EXPRESSION</c> は文字列リテラル用にエスケープされた形で
    /// 格納されるため、そのまま SQL へ埋めると構文エラーになる。復元を外すとこのテストが落ちる。
    /// カタログ上の式は導入子が正規化され得るので、比較は「計算結果」と「式が意味を保つこと」で行う。
    /// </remarks>
    [Fact(DisplayName = "[Integration] B: 列順同期は文字列リテラル入りの生成列式を壊さない")]
    public async Task Reorder_PreservesGeneratedExpressionContainingStringLiteral()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(
            """
            CREATE TABLE `gx` (
              `id` int NOT NULL,
              `s` varchar(20) NULL,
              `g` varchar(60) GENERATED ALWAYS AS (concat(`s`, ' -- it''s \\ ok')) STORED
                COMMENT 'generated'
            );
            """,
            Ct
        );
        await fixture.ExecuteAsync("INSERT INTO `gx` (`id`, `s`) VALUES (1, 'x');", Ct);

        var before = await ReadColumnMetadataAsync("gx");
        var liveOrder = ColumnsInPositionOrder(before);
        // g を先頭へ動かす＝生成列自身が MODIFY 対象になる
        var targetOrder = new List<string> { "g", "id", "s" };

        await ApplyReorderAsync("gx", before, liveOrder, targetOrder);

        var after = await ReadColumnMetadataAsync("gx");
        ColumnsInPositionOrder(after).Should().Equal(targetOrder);
        after["g"]["EXTRA"].Should().Be("STORED GENERATED");
        after["g"]["COLUMN_COMMENT"].Should().Be("generated");

        // 式が生きていること＝生成値が元の定義どおりであること
        await using var conn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = new MySqlCommand("SELECT `g` FROM `gx` WHERE `id` = 1;", conn);
        var value = (string?)await cmd.ExecuteScalarAsync(Ct);
        value.Should().Be(@"x -- it's \ ok");
    }

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
    private static SchemaDiffItem ColumnDescriptionItem(
        string table,
        string column,
        string description,
        Entity? entity = null
    ) =>
        new()
        {
            Kind = SchemaDiffKind.SetColumnDescription,
            TableName = table,
            ColumnName = column,
            Entity = entity,
            NewDescription = description,
            IsSelected = true,
        };

    /// <summary>差分項目から同期スクリプトを組み立てて実行する</summary>
    private async Task ApplyAsync(IEnumerable<SchemaDiffItem> items)
    {
        var plan = new SyncPlanner().BuildPlan(items, new SyncDialectCapabilities());
        await ExecuteAsync(new MySqlSyncScriptBuilder().Build(plan));
    }

    /// <summary>列順の差分項目（live / target の列順）から同期スクリプトを組み立てて実行する</summary>
    private async Task ApplyReorderAsync(
        string table,
        Dictionary<string, Dictionary<string, string?>> metadata,
        IEnumerable<string> liveOrder,
        IEnumerable<string> targetOrder
    )
    {
        var item = new SchemaDiffItem
        {
            Kind = SchemaDiffKind.ReorderColumns,
            TableName = table,
            Entity = BuildEntity(table, metadata, targetOrder),
            IsSelected = true,
        };
        var context = new SyncPlanContext
        {
            LiveEntities = [BuildEntity(table, metadata, liveOrder)],
        };
        var plan = new SyncPlanner().BuildPlan(
            [item],
            new SyncDialectCapabilities { ColumnReorder = ColumnReorderMode.Native },
            context
        );
        plan.Reorders.Should().NotBeEmpty("列順差分が検出されていること");
        await ExecuteAsync(new MySqlSyncScriptBuilder().Build(plan));
    }

    /// <summary>カタログの実型を使い、指定した列順のエンティティを組み立てる</summary>
    private static Entity BuildEntity(
        string table,
        Dictionary<string, Dictionary<string, string?>> metadata,
        IEnumerable<string> order
    )
    {
        var entity = new Entity { TableName = table };

        foreach (var name in order)
        {
            entity.Columns.Add(
                new Column
                {
                    Name = name,
                    DataType = metadata[name]["COLUMN_TYPE"] ?? "int",
                    IsNullable = metadata[name]["IS_NULLABLE"] == "YES",
                }
            );
        }

        return entity;
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

    /// <summary>カタログの位置順に列名を並べる</summary>
    private static List<string> ColumnsInPositionOrder(
        Dictionary<string, Dictionary<string, string?>> metadata
    ) =>
        metadata
            .OrderBy(pair => int.Parse(pair.Value["ORDINAL_POSITION"]!))
            .Select(pair => pair.Key)
            .ToList();

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
