using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Oracle;
using QuickER.Provider;
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
}
