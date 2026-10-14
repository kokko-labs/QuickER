using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.Oracle;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// Oracle の取込忠実性（型表記・取込範囲・無効化された制約・警告）の統合テスト。
/// </summary>
/// <remarks>
/// <see cref="OracleDdlRoundTripIntegrationTests"/> が「QuickER が作った DDL が往復するか」を見るのに対し、
/// こちらは「QuickER が作ったのではない実 DB」を取り込んだときに何が落ちるかを固定する。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(OracleContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class OracleImportFidelityIntegrationTests(OracleContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>取込んだ型表記が正規型と往復するか、verbatim 併記でしか保てないか</summary>
    private enum Closure
    {
        /// <summary><c>TryParse</c> → <c>TryFormat</c> が同じ表記へ戻る（正規型で表せる）</summary>
        RoundTrips,

        /// <summary>正規型で表せない＝元の表記の保存は verbatim 併記に委ねる</summary>
        VerbatimOnly,
    }

    /// <summary>(列名, 作成する型, 取込で期待される表記, 正規型との関係)</summary>
    private sealed record TypeCase(
        string Column,
        string CreateType,
        string Expected,
        Closure Closure
    );

    /// <summary>
    /// Oracle の「正規型に収まりきらない」型を実 DB で網羅し、取込が返す表記の閉包を固定する。
    /// </summary>
    /// <remarks>
    /// 代表型の網羅は <see cref="OracleDdlRoundTripIntegrationTests"/> 側が持つため、ここは
    /// 従来落ちていた修飾（負のスケール・長さの単位語）を中心に置く。
    /// </remarks>
    private static readonly TypeCase[] TypeCases =
    [
        new("C_NUM_10_2", "NUMBER(10,2)", "NUMBER(10,2)", Closure.RoundTrips),
        new("C_NUM_10", "NUMBER(10)", "NUMBER(10)", Closure.RoundTrips),
        // スケール 0 と未指定は user_tab_columns 上で区別できないため、どちらも NUMBER(p)
        new("C_NUM_10_0", "NUMBER(10,0)", "NUMBER(10)", Closure.RoundTrips),
        // 従来はスケールごと落ちて NUMBER(10) ＝ Int32 に化けていた
        new("C_NUM_NEG", "NUMBER(10,-2)", "NUMBER(10,-2)", Closure.RoundTrips),
        new("C_VC_BYTE", "VARCHAR2(50)", "VARCHAR2(50)", Closure.RoundTrips),
        // 正規型は長さの単位を持たないため、単位語つきの表記は verbatim 併記でしか保てない
        new("C_VC_CHAR", "VARCHAR2(50 CHAR)", "VARCHAR2(50 CHAR)", Closure.VerbatimOnly),
        new("C_CH_CHAR", "CHAR(10 CHAR)", "CHAR(10 CHAR)", Closure.VerbatimOnly),
        new("C_CH_BYTE", "CHAR(10)", "CHAR(10)", Closure.RoundTrips),
        // N 系は常に文字単位だが Oracle の構文が単位語を受けないため付けない
        new("C_NVC", "NVARCHAR2(20)", "NVARCHAR2(20)", Closure.RoundTrips),
        new("C_RAW", "RAW(16)", "RAW(16)", Closure.RoundTrips),
        // 精度なし・スケール 0（INTEGER / SMALLINT / NUMBER(*,0)）は素の NUMBER と区別して取り込む。
        // 素の NUMBER にすると、取り込んだ図から作り直した DDL が小数を受ける列になり整数の制約が落ちる
        new("C_INTEGER", "INTEGER", "NUMBER(*,0)", Closure.RoundTrips),
        new("C_SMALLINT", "SMALLINT", "NUMBER(*,0)", Closure.RoundTrips),
        new("C_NUM_STAR_0", "NUMBER(*,0)", "NUMBER(*,0)", Closure.RoundTrips),
        new("C_NUM_STAR_2", "NUMBER(*,2)", "NUMBER(*,2)", Closure.RoundTrips),
        // NUMBER(*) は精度もスケールも持たない＝素の NUMBER と同じ
        new("C_NUM_STAR", "NUMBER(*)", "NUMBER", Closure.RoundTrips),
    ];

    /// <summary>
    /// 型を実 DB へ作成→取込し、(a) 期待表記であること (b) 正規型との関係が表の宣言どおりであること
    /// を検証する（閉包テスト）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: 取込が返す型表記の閉包（期待表記・正規型と往復するか）を固定する"
    )]
    public async Task TypeClosure_ImportedNotationsMatchTableAndClassification()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var cols = string.Join(",\n", TypeCases.Select(c => $"    {c.Column} {c.CreateType}"));
        await fixture.ExecuteAsync($"CREATE TABLE TYPES_ALL (\n{cols}\n);", Ct);

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var table = result.Entities.Single(e => e.TableName == "TYPES_ALL");
        var catalog = new OracleTypeCatalog();

        foreach (var (columnName, createType, expected, closure) in TypeCases)
        {
            var column = table.Columns.Single(c => c.Name == columnName);
            column.DataType.Should().Be(expected, $"列 {columnName}（{createType}）の取込型表記");

            // 解析すらできないと型トークンが列から丸ごと消えるため、どちらの分類でも TryParse は通ること
            catalog
                .TryParse(column.DataType, out var canonical)
                .Should()
                .BeTrue($"取込型 '{column.DataType}'（列 {columnName}）は TryParse 可能であること");

            var roundTrips =
                catalog.TryFormat(canonical, out var formatted)
                && DbTypeText.AreEquivalent(formatted, column.DataType);

            roundTrips
                .Should()
                .Be(
                    closure == Closure.RoundTrips,
                    $"取込型 '{column.DataType}'（列 {columnName}）の正規型との関係は表の宣言どおりであること"
                );

            SqlTypeText
                .IsSafe(column.DataType)
                .Should()
                .BeTrue($"取込型 '{column.DataType}' は DDL へ出せる表記であること");
        }
    }

    /// <summary>
    /// 利用者が INVISIBLE にした列は取り込み、関数インデックスのシステム生成列は取り込まないことを検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 列の取得は <c>user_tab_cols</c>（仮想列を判別するため）を <c>user_generated = 'YES'</c> で絞る。
    /// <c>hidden_column = 'NO'</c> で絞ると、除きたい <c>SYS_NC…$</c> だけでなく、
    /// <b>利用者が意図して隠した INVISIBLE 列も落ちる</b>（実測: INVISIBLE 列は
    /// <c>hidden_column='YES'</c> / <c>user_generated='YES'</c>、<c>SYS_NC…$</c> は
    /// <c>hidden_column='YES'</c> / <c>user_generated='NO'</c>）。
    /// </para>
    /// <para>
    /// INVISIBLE 列の <c>column_id</c> は <c>NULL</c> なので、<c>ORDER BY column_id</c> では
    /// 可視列の後ろへ並ぶ（Oracle の昇順は NULLS LAST）。図でも末尾の列になることを併せて固定する。
    /// INVISIBLE 列が 2 本以上あるときは、第 2 キーの <c>internal_column_id</c> で作った順に並ぶ。
    /// 作成順と名前の辞書順が逆になるように列を置き、可視列の間に INVISIBLE 列を挟んで宣言する。
    /// </para>
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] Oracle: INVISIBLE 列は取り込み、関数インデックスの隠し列は取り込まない"
    )]
    public async Task Import_InvisibleColumn_IsImportedButSystemGeneratedIsNot()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE PROBE_T (
                ID NUMBER(10) PRIMARY KEY,
                SECRET VARCHAR2(50) INVISIBLE,
                NAME VARCHAR2(50),
                ALPHA_HIDDEN NUMBER(10) INVISIBLE
            );
            CREATE INDEX PROBE_IX ON PROBE_T (UPPER(NAME));
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var probe = result.Entities.Single(e => e.TableName == "PROBE_T");

        // INVISIBLE 列は取り込む。column_id が NULL なので可視列の後ろへ、作った順に並ぶ
        probe.Columns.Select(c => c.Name).Should().Equal("ID", "NAME", "SECRET", "ALPHA_HIDDEN");
        probe.Columns.Single(c => c.Name == "SECRET").DataType.Should().Be("VARCHAR2(50)");

        // 関数インデックスのシステム生成列は取り込まない（図に実在しない列を作らない）
        probe
            .Columns.Should()
            .NotContain(c => c.Name.StartsWith("SYS_NC", StringComparison.Ordinal));
    }

    /// <summary>
    /// DISABLE された主キー・一意制約・外部キーが取り込まれないことを検証する。
    /// </summary>
    /// <remarks>
    /// DISABLE された制約は実際には一意性も参照整合性も強制されない。取り込むと図が
    /// 「DB には無い保証」を宣言することになり、その図から生成した DDL・コードが偽の前提で動く。
    /// </remarks>
    [Fact(DisplayName = "[Integration] Oracle: DISABLE された PK / UNIQUE / FK は取り込まない")]
    public async Task Import_DisabledConstraints_AreNotImported()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE PARENT_T (ID NUMBER(10) PRIMARY KEY);
            CREATE TABLE CHILD_T (
                ID NUMBER(10),
                CODE VARCHAR2(10),
                PID NUMBER(10),
                CONSTRAINT PK_CHILD_T PRIMARY KEY (ID) DISABLE,
                CONSTRAINT UQ_CHILD_T UNIQUE (CODE) DISABLE
            );
            ALTER TABLE CHILD_T ADD CONSTRAINT FK_CHILD_T
                FOREIGN KEY (PID) REFERENCES PARENT_T (ID) DISABLE;
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var child = result.Entities.Single(e => e.TableName == "CHILD_T");
        child.Columns.Single(c => c.Name == "ID").IsPrimaryKey.Should().BeFalse();
        child.UniqueConstraints.Should().BeEmpty();
        result.Relationships.Should().BeEmpty();

        // ENABLED の制約は従来どおり取り込まれる（フィルタが効きすぎていないことの対照）
        result
            .Entities.Single(e => e.TableName == "PARENT_T")
            .Columns.Single(c => c.Name == "ID")
            .IsPrimaryKey.Should()
            .BeTrue();

        // 除外は無言ではなく、3 種の制約それぞれが DisabledConstraintExcluded として名指しされる
        var disabledWarnings = result
            .Warnings.Where(w => w.Kind == SchemaImportWarningKind.DisabledConstraintExcluded)
            .ToList();
        disabledWarnings.Should().HaveCount(3);
        disabledWarnings
            .Should()
            .ContainSingle(w =>
                w.TableName == "CHILD_T" && w.Subject == "PK_CHILD_T" && w.Detail == "PRIMARY KEY"
            );
        disabledWarnings
            .Should()
            .ContainSingle(w =>
                w.TableName == "CHILD_T" && w.Subject == "UQ_CHILD_T" && w.Detail == "UNIQUE"
            );
        disabledWarnings
            .Should()
            .ContainSingle(w =>
                w.TableName == "CHILD_T" && w.Subject == "FK_CHILD_T" && w.Detail == "FOREIGN KEY"
            );
    }

    /// <summary>
    /// 一時表とマテリアライズドビューのコンテナ表が取込対象から除外されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: 一時表とマテリアライズドビューのコンテナ表は取込から除外される"
    )]
    public async Task Import_ExcludesTemporaryTablesAndMaterializedViews()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE MV_BASE (ID NUMBER(10) PRIMARY KEY, V NUMBER(10));
            CREATE GLOBAL TEMPORARY TABLE SCRATCH_T (X NUMBER(10)) ON COMMIT DELETE ROWS;
            CREATE MATERIALIZED VIEW MV_PROBE AS SELECT ID, V FROM MV_BASE;
            """,
            Ct
        );

        try
        {
            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

            // どちらも user_tables には通常テーブルとして現れるが、ER 図のエンティティではない
            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("MV_BASE");
        }
        finally
        {
            // MV のコンテナ表は DROP TABLE できない（ORA-12083）ため、
            // 後続テストの ResetSchemaAsync が失敗しないよう MV として落としておく
            await fixture.ExecuteAsync("DROP MATERIALIZED VIEW MV_PROBE;", Ct);
        }
    }

    /// <summary>
    /// 取込範囲（接続ユーザーの自スキーマ）の外を参照する外部キーが、
    /// リレーションにならず警告として報告されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: 取込範囲外を参照する外部キーは除外し、警告として報告する"
    )]
    public async Task Import_ForeignKeyToOtherSchema_IsExcludedWithWarning()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        // アプリユーザー（CONNECT + RESOURCE 相当）は CREATE USER できないため SYSTEM で用意する
        await fixture.ExecuteAsSystemAsync(
            """
            CREATE USER QEROTHER IDENTIFIED BY qerother QUOTA UNLIMITED ON USERS;
            GRANT CREATE SESSION, CREATE TABLE TO QEROTHER;
            CREATE TABLE QEROTHER.XPARENT (ID NUMBER(10) PRIMARY KEY);
            GRANT REFERENCES ON QEROTHER.XPARENT TO ORACLE;
            """,
            Ct
        );

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE XCHILD (ID NUMBER(10) PRIMARY KEY, PID NUMBER(10));
                ALTER TABLE XCHILD ADD CONSTRAINT FK_XCROSS
                    FOREIGN KEY (PID) REFERENCES QEROTHER.XPARENT (ID);
                """,
                Ct
            );

            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("XCHILD");
            result.Relationships.Should().BeEmpty("親テーブルが図に無いためリレーションは作れない");

            var warning = result.Warnings.Should().ContainSingle().Subject;
            warning.Kind.Should().Be(SchemaImportWarningKind.ForeignKeyOutsideScope);
            warning.TableName.Should().Be("XCHILD");
            warning.Subject.Should().Be("FK_XCROSS");
            warning.Detail.Should().Be("QEROTHER");
        }
        finally
        {
            // 先に子側を落とさないと別スキーマの親を落とせない。ResetSchemaAsync は自スキーマしか消さない
            await fixture.ExecuteAsync("DROP TABLE XCHILD CASCADE CONSTRAINTS PURGE;", Ct);
            await fixture.ExecuteAsSystemAsync("DROP USER QEROTHER CASCADE;", Ct);
        }
    }

    /// <summary>
    /// 取り込まなかったテーブル（大文字小文字の衝突で捨てた側）の外部キーについては、範囲外の警告を出さないことを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] Oracle: 捨てたテーブルの範囲外の外部キーは警告しない")]
    public async Task Import_OutOfScopeForeignKeyOfDiscardedTable_IsNotReported()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsSystemAsync(
            """
            CREATE USER QEROTHER IDENTIFIED BY qerother QUOTA UNLIMITED ON USERS;
            GRANT CREATE SESSION, CREATE TABLE TO QEROTHER;
            CREATE TABLE QEROTHER.XPARENT (ID NUMBER(10) PRIMARY KEY);
            GRANT REFERENCES ON QEROTHER.XPARENT TO ORACLE;
            """,
            Ct
        );

        try
        {
            await fixture.ExecuteAsync(
                """
                CREATE TABLE DUP (ID NUMBER(10) PRIMARY KEY);
                CREATE TABLE "dup" (ID NUMBER(10) PRIMARY KEY, PID NUMBER(10));
                ALTER TABLE "dup" ADD CONSTRAINT FK_DISCARDED
                    FOREIGN KEY (PID) REFERENCES QEROTHER.XPARENT (ID);
                """,
                Ct
            );

            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("DUP");
            result
                .Warnings.Should()
                .ContainSingle("捨てた \"dup\" の外部キーは図に載らないので名指ししない")
                .Which.Kind.Should()
                .Be(SchemaImportWarningKind.TableNameCollision);
        }
        finally
        {
            await fixture.ExecuteAsync("DROP TABLE \"dup\" CASCADE CONSTRAINTS PURGE;", Ct);
            await fixture.ExecuteAsSystemAsync("DROP USER QEROTHER CASCADE;", Ct);
        }
    }

    /// <summary>
    /// マテリアライズドビューのログ表（<c>MLOG$_…</c>）が取込対象から除外されることを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] Oracle: マテリアライズドビューのログ表は取込から除外される")]
    public async Task Import_ExcludesMaterializedViewLogTables()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE LOG_BASE (ID NUMBER(10) PRIMARY KEY, V NUMBER(10));
            CREATE MATERIALIZED VIEW LOG ON LOG_BASE;
            """,
            Ct
        );

        try
        {
            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

            // MLOG$_LOG_BASE は user_tables に通常テーブルとして現れるが、ビューの更新のための変更記録
            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("LOG_BASE");
        }
        finally
        {
            // ログ表は DROP TABLE で落とせないので、後続テストの ResetSchemaAsync のためにログとして落とす
            await fixture.ExecuteAsync("DROP MATERIALIZED VIEW LOG ON LOG_BASE;", Ct);
        }
    }

    /// <summary>
    /// パーティション表を取り込み、パーティション定義が失われることを警告として報告することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: パーティション表は取り込み、パーティション定義の喪失を報告する"
    )]
    public async Task Import_PartitionedTable_IsImportedWithWarning()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE PART_T (ID NUMBER(10) PRIMARY KEY, D DATE)
                PARTITION BY RANGE (D) (PARTITION P1 VALUES LESS THAN (DATE '2030-01-01'));
            CREATE TABLE PLAIN_T (ID NUMBER(10) PRIMARY KEY);
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("PART_T", "PLAIN_T");
        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.PartitionDefinitionLost);
        warning.TableName.Should().Be("PART_T");
    }

    /// <summary>
    /// 大文字小文字だけが違う同名テーブルを、列を混ぜずに片方だけ取り込み、警告として報告することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: 大文字小文字だけ違う同名テーブルは列を混ぜず片方を捨てて報告する"
    )]
    public async Task Import_TableNamesDifferingOnlyInCase_KeepsOneAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE DUP (UPPER_ONLY NUMBER(10));
            CREATE TABLE "dup" (LOWER_ONLY VARCHAR2(10));
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        // table_name 昇順で先に来た DUP を採り、後着の "dup" は捨てる
        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be("DUP");
        entity
            .Columns.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["UPPER_ONLY"], "両テーブルの列が 1 エンティティへ混ざってはいけない");

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.TableNameCollision);
        warning.Subject.Should().Be("dup");
        warning.Detail.Should().Be("DUP");
    }

    /// <summary>
    /// 同じテーブルに <c>SchemaNames</c> の規則で同じ名前の列（大文字小文字だけ違う列）があるとき、
    /// 後着を捨てて報告し、捨てた列の主キー印・説明が採った列へ混ざらないことを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] Oracle: 大文字小文字だけ違う同名の列は片方を捨てて報告し、捨てた列の主キー・説明を混ぜない"
    )]
    public async Task Import_ColumnNamesDifferingOnlyInCase_KeepsOneAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE CT (ID NUMBER(10) NOT NULL, DUP NUMBER(10), "dup" NUMBER(10) NOT NULL, PRIMARY KEY (ID, "dup"));
            COMMENT ON COLUMN CT."dup" IS 'second';
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        // 宣言順（column_id）で先に来た DUP を採り、後着の "dup" は捨てる
        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.Columns.Select(c => c.Name).Should().Equal("ID", "DUP");

        var kept = entity.Columns.Single(c => c.Name == "DUP");
        kept.IsPrimaryKey.Should()
            .BeFalse("捨てた列 dup の主キー印が採った列 DUP へ混ざってはいけない");
        kept.Description.Should().BeEmpty("捨てた列の説明が採った列へ混ざってはいけない");

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.ColumnNameCollision);
        warning.TableName.Should().Be("CT");
        warning.Subject.Should().Be("dup");
        warning.Detail.Should().Be("DUP");
    }

    /// <summary>
    /// 仮想列が <see cref="QuickER.Model.Column.IsComputed"/> つきで取り込まれ、
    /// 式の喪失が報告されることを検証する。
    /// </summary>
    /// <remarks>
    /// 仮想列を判別するため取込は <c>user_tab_columns</c> ではなく <c>user_tab_cols</c> を引く。
    /// 両ビューの差は隠し列の有無だけなので <c>hidden_column = 'NO'</c> を明示して列集合を従来どおりに保つ
    /// ——その担保は <see cref="Import_FunctionBasedIndex_DoesNotAddHiddenColumn"/> が固定する。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] Oracle: 仮想列は IsComputed つきで取り込み、式の喪失を報告する"
    )]
    public async Task Import_VirtualColumn_IsMarkedAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE invoices (
                invoice_id NUMBER(10) PRIMARY KEY,
                qty NUMBER(10) NOT NULL,
                price NUMBER(18,2) NOT NULL,
                total NUMBER(21,2) GENERATED ALWAYS AS (qty * price) VIRTUAL
            )
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.Columns.Where(c => c.IsComputed).Select(c => c.Name).Should().Equal("TOTAL");
        entity.Columns.Single(c => c.Name == "QTY").IsComputed.Should().BeFalse();
        // 型は従来どおり（運ぶのは「書き込めない」事実だけ）
        entity.Columns.Single(c => c.Name == "TOTAL").DataType.Should().Be("NUMBER(21,2)");

        var warning = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.ComputedColumnExpressionLost)
            .Subject;
        warning.TableName.Should().Be("INVOICES");
        warning.Subject.Should().Be("TOTAL");
        // data_default は LONG 型。ODP.NET の既定では取得されない（空文字が返る）ため、
        // 取込側で InitialLONGFetchSize を設定している——それが効いていることを式の中身で確かめる
        warning.Detail.Should().Contain("QTY").And.Contain("PRICE");
    }

    /// <summary>
    /// 関数インデックスがテーブルへ足すシステム生成の隠し列（<c>SYS_NC…$</c>）が取り込まれないことを検証する。
    /// </summary>
    /// <remarks>
    /// <c>user_tab_cols</c> は隠し列も返すため、<c>hidden_column = 'NO'</c> を外すと図に実在しない列が現れる
    /// （しかもそれらは仮想列なので計算列として名指しされる）。
    /// </remarks>
    [Fact(DisplayName = "[Integration] Oracle: 関数インデックスの隠し列は取り込まれない")]
    public async Task Import_FunctionBasedIndex_DoesNotAddHiddenColumn()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE people (person_id NUMBER(10) PRIMARY KEY, name VARCHAR2(50))",
            Ct
        );
        await fixture.ExecuteAsync("CREATE INDEX ix_people_upper ON people (UPPER(name))", Ct);

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);

        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.Columns.Select(c => c.Name).Should().Equal("PERSON_ID", "NAME");
        entity.Columns.Should().OnlyContain(c => !c.IsComputed);
        result
            .Warnings.Should()
            .NotContain(w => w.Kind == SchemaImportWarningKind.ComputedColumnExpressionLost);
    }

    /// <summary>
    /// <c>INTEGER</c> の列を取り込んだ図から DDL を作り直しても、整数の制約が保たれる（1.5 が 2 へ丸められる）
    /// ことを検証する。
    /// </summary>
    /// <remarks>
    /// Oracle は <c>INTEGER</c> を精度なし・スケール 0 の <c>NUMBER</c> として保存する。取込が素の
    /// <c>NUMBER</c>（浮動）として書くと、作り直した列は 1.5 をそのまま保存する別の列になる。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] Oracle: INTEGER の列は取込図から作り直しても整数の制約を保つ"
    )]
    public async Task Import_IntegerColumn_KeepsIntegerConstraintAfterRegeneration()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            "CREATE TABLE INT_PROBE (ID NUMBER(10) PRIMARY KEY, QTY INTEGER);",
            Ct
        );

        ErDiagram imported;

        await using (var conn = await fixture.OpenConnectionAsync(Ct))
        {
            var result = await new OracleSchemaImporter().ImportAsync(conn, Ct);
            imported = new ErDiagram
            {
                Entities = result.Entities.ToList(),
                Relationships = result.Relationships.ToList(),
            };
        }

        imported
            .Entities.Single(e => e.TableName == "INT_PROBE")
            .Columns.Single(c => c.Name == "QTY")
            .DataType.Should()
            .Be("NUMBER(*,0)");

        await fixture.ResetSchemaAsync(Ct);
        await fixture.ExecuteAsync(new OracleDdlGenerator().Build(imported), Ct);
        await fixture.ExecuteAsync("INSERT INTO INT_PROBE (ID, QTY) VALUES (1, 1.5);", Ct);

        await using var reconn = await fixture.OpenConnectionAsync(Ct);
        await using var cmd = reconn.CreateCommand();
        cmd.CommandText = "SELECT TO_CHAR(QTY) FROM INT_PROBE";
        var stored = (string?)await cmd.ExecuteScalarAsync(Ct);

        stored.Should().Be("2", "作り直した列も整数の列であり、1.5 は 2 へ丸められること");
    }
}
