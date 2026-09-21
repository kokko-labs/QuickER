using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Npgsql;
using QuickER.Provider;
using QuickER.Provider.PostgreSql;
using QuickER.Tests.Integration;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// PostgreSQL の取込忠実性（<c>format_type()</c> 由来の型表記・取込範囲・権限・警告）の統合テスト。
/// </summary>
/// <remarks>
/// <see cref="PostgreSqlDdlRoundTripIntegrationTests"/> が「QuickER が作った DDL が往復するか」を見るのに対し、
/// こちらは「QuickER が作ったのではない実 DB」を取り込んだときに何が落ちるかを固定する。
/// </remarks>
[Trait("Category", "Integration")]
[Collection(PostgreSqlContainerCollection.Name)]
[Trait("RequiresDocker", "true")]
public sealed class PostgreSqlImportFidelityIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>取込んだ型表記が正規型と往復するか、verbatim 併記でしか保てないか</summary>
    private enum Closure
    {
        /// <summary><c>TryParse</c> → <c>TryFormat</c> が同じ表記へ戻る（正規型で表せる）</summary>
        RoundTrips,

        /// <summary>
        /// 正規型で表せない＝型トークンだけでは復元できず、元の表記の保存は
        /// <c>CanonicalTypeTokenAttacher</c> の verbatim 併記に委ねる。
        /// </summary>
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
    /// PostgreSQL の代表型を実 DB で網羅し、取込が返す表記の「閉包」を固定する。
    /// </summary>
    /// <remarks>
    /// 表の各行は (a) 期待表記そのもの と (b) その表記が正規型へ往復するか を同時に主張する。
    /// (b) を明示列挙にしてあるのは、新しい型・新しい正規化規則が「解析はできるが別の表記へ化ける」
    /// 状態で静かに入り込むのを防ぐため＝分類を変えるにはこの表を触るしかない。
    /// </remarks>
    private static readonly TypeCase[] TypeCases =
    [
        // --- 素通し（正規型で表せる） ---
        new("c_boolean", "boolean", "boolean", Closure.RoundTrips),
        new("c_smallint", "smallint", "smallint", Closure.RoundTrips),
        new("c_integer", "integer", "integer", Closure.RoundTrips),
        new("c_bigint", "bigint", "bigint", Closure.RoundTrips),
        new("c_real", "real", "real", Closure.RoundTrips),
        new("c_double", "double precision", "double precision", Closure.RoundTrips),
        new("c_money", "money", "money", Closure.RoundTrips),
        new("c_text", "text", "text", Closure.RoundTrips),
        new("c_bytea", "bytea", "bytea", Closure.RoundTrips),
        new("c_date", "date", "date", Closure.RoundTrips),
        new("c_uuid", "uuid", "uuid", Closure.RoundTrips),
        new("c_xml", "xml", "xml", Closure.RoundTrips),
        new("c_jsonb", "jsonb", "jsonb", Closure.RoundTrips),
        // json は正規型 Json の代表表記が jsonb のため往復しない（従来からの非対称）
        new("c_json", "json", "json", Closure.VerbatimOnly),
        // --- 文字列（character varying / character を短縮語彙へ） ---
        new("c_varchar_50", "varchar(50)", "varchar(50)", Closure.RoundTrips),
        new("c_varchar_bare", "varchar", "varchar", Closure.RoundTrips),
        new("c_char_10", "char(10)", "char(10)", Closure.RoundTrips),
        new("c_char_bare", "char", "char(1)", Closure.RoundTrips),
        new("c_bpchar", "bpchar(5)", "char(5)", Closure.RoundTrips),
        // --- 数値（スケール 0 は畳み、負のスケールは宣言どおり残す） ---
        new("c_numeric_10_2", "numeric(10,2)", "numeric(10,2)", Closure.RoundTrips),
        new("c_numeric_18_0", "numeric(18,0)", "numeric(18)", Closure.RoundTrips),
        new("c_numeric_bare", "numeric", "numeric", Closure.RoundTrips),
        new("c_numeric_10", "numeric(10)", "numeric(10)", Closure.RoundTrips),
        // 従来は numeric(10,2046)（information_schema が符号なしで返す生値）になり、
        // その表記は DDL として再適用すらできなかった
        new("c_numeric_neg", "numeric(10,-2)", "numeric(10,-2)", Closure.RoundTrips),
        // --- 日時（無修飾は既定精度 6・with time zone 側は従来語彙のまま） ---
        new("c_time", "time", "time(6)", Closure.RoundTrips),
        new("c_time_3", "time(3)", "time(3)", Closure.RoundTrips),
        new("c_timestamp", "timestamp", "timestamp(6)", Closure.RoundTrips),
        new("c_timestamp_3", "timestamp(3)", "timestamp(3)", Closure.RoundTrips),
        new("c_timestamptz", "timestamptz", "timestamptz(6)", Closure.RoundTrips),
        new("c_timestamptz_3", "timestamptz(3)", "timestamptz(3)", Closure.RoundTrips),
        // time with time zone は正規型 Time へ寄せるため、書き戻すと時間帯が落ちる
        new("c_timetz", "timetz", "time with time zone", Closure.VerbatimOnly),
        new("c_timetz_3", "time(3) with time zone", "time(3) with time zone", Closure.VerbatimOnly),
        // --- 正規型に対応する概念が無い型（従来は修飾ごと落ちていたものを含む） ---
        // 従来は長さを失って "bit" になり、それを ALTER で当てると実データが壊れた
        new("c_bit_8", "bit(8)", "bit(8)", Closure.VerbatimOnly),
        new("c_bit_bare", "bit", "bit(1)", Closure.VerbatimOnly),
        new("c_varbit_16", "bit varying(16)", "bit varying(16)", Closure.VerbatimOnly),
        new("c_varbit_bare", "bit varying", "bit varying", Closure.VerbatimOnly),
        new("c_interval", "interval", "interval", Closure.VerbatimOnly),
        new(
            "c_interval_dts3",
            "interval day to second(3)",
            "interval day to second(3)",
            Closure.VerbatimOnly
        ),
        new(
            "c_interval_ym",
            "interval year to month",
            "interval year to month",
            Closure.VerbatimOnly
        ),
        new("c_inet", "inet", "inet", Closure.VerbatimOnly),
        new("c_cidr", "cidr", "cidr", Closure.VerbatimOnly),
        new("c_macaddr", "macaddr", "macaddr", Closure.VerbatimOnly),
        new("c_point", "point", "point", Closure.VerbatimOnly),
        new("c_tsvector", "tsvector", "tsvector", Closure.VerbatimOnly),
        // --- 配列（従来は udt_name の _int4 / _varchar になり、後者は長さごと落ちていた） ---
        new("c_int_array", "integer[]", "integer[]", Closure.VerbatimOnly),
        new("c_varchar_array", "varchar(20)[]", "varchar(20)[]", Closure.VerbatimOnly),
        new("c_text_array", "text[]", "text[]", Closure.VerbatimOnly),
        new("c_numeric_array", "numeric(10,2)[]", "numeric(10,2)[]", Closure.VerbatimOnly),
        // --- ユーザー定義型・ドメイン（ドメインは基底型へ平坦化する） ---
        new("c_enum", "mood", "mood", Closure.VerbatimOnly),
        new("c_domain_vc", "email_dom", "varchar(320)", Closure.RoundTrips),
        new("c_domain_int", "pos_int", "integer", Closure.RoundTrips),
        new("c_domain_nested", "nested_dom", "varchar(320)", Closure.RoundTrips),
        new("c_domain_array", "email_dom[]", "varchar(320)[]", Closure.VerbatimOnly),
    ];

    /// <summary>
    /// 代表型を実 DB へ作成→取込し、(a) 期待表記であること (b) 正規型との関係が表の宣言どおりであること
    /// を検証する（閉包テスト）。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PG: 取込が返す型表記の閉包（期待表記・正規型と往復するか）を固定する"
    )]
    public async Task TypeClosure_ImportedNotationsMatchTableAndClassification()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var cols = string.Join(",\n", TypeCases.Select(c => $"    \"{c.Column}\" {c.CreateType}"));
        await fixture.ExecuteAsync(
            $"""
            CREATE TYPE mood AS ENUM ('sad', 'ok', 'happy');
            CREATE DOMAIN email_dom AS varchar(320);
            CREATE DOMAIN pos_int AS integer;
            CREATE DOMAIN nested_dom AS email_dom;
            CREATE TABLE "types_all" (
            {cols}
            );
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        var table = result.Entities.Single(e => e.TableName == "types_all");
        var catalog = new PostgreSqlTypeCatalog();

        foreach (var (columnName, createType, expected, closure) in TypeCases)
        {
            var column = table.Columns.Single(c => c.Name == columnName);
            column.DataType.Should().Be(expected, $"列 {columnName}（{createType}）の取込型表記");

            var roundTrips =
                catalog.TryParse(column.DataType, out var canonical)
                && catalog.TryFormat(canonical, out var formatted)
                && DbTypeText.AreEquivalent(formatted, column.DataType);

            roundTrips
                .Should()
                .Be(
                    closure == Closure.RoundTrips,
                    $"取込型 '{column.DataType}'（列 {columnName}）の正規型との関係は表の宣言どおりであること"
                );
        }
    }

    /// <summary>
    /// 取込が返す全表記が DDL へ素通しで出せる安全な表記であることを検証する。
    /// </summary>
    /// <remarks>
    /// 取込表記は <see cref="SqlTypeText"/> の構造的ホワイトリストを通らないと、その図から DDL も同期も
    /// 生成できなくなる（生成の入口で名指しの例外になる）。型表記を増やす変更が実 DB の列を
    /// 使えなくしていないかを、実際に取り込んだ表記で確かめる。
    /// </remarks>
    [Fact(DisplayName = "[Integration] PG: 取込が返す全型表記が SqlTypeText を通る")]
    public async Task ImportedNotations_AreSafeForDdl()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        var cols = string.Join(",\n", TypeCases.Select(c => $"    \"{c.Column}\" {c.CreateType}"));
        await fixture.ExecuteAsync(
            $"""
            CREATE TYPE mood AS ENUM ('sad', 'ok', 'happy');
            CREATE DOMAIN email_dom AS varchar(320);
            CREATE DOMAIN pos_int AS integer;
            CREATE DOMAIN nested_dom AS email_dom;
            CREATE TABLE "types_all" (
            {cols}
            );
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        foreach (var column in result.Entities.Single(e => e.TableName == "types_all").Columns)
        {
            SqlTypeText
                .IsSafe(column.DataType)
                .Should()
                .BeTrue(
                    $"取込型 '{column.DataType}'（列 {column.Name}）は DDL へ出せる表記であること"
                );
        }
    }

    /// <summary>
    /// 列に何の権限も持たないロールで取り込んでも、列定義が落ちないことを検証する。
    /// </summary>
    /// <remarks>
    /// <c>information_schema.columns</c> は「接続ロールがその列に何らかの権限を持つ行」しか返さないため、
    /// 旧実装ではこの構成が「列ゼロのテーブル」として静かに取り込まれていた（実 PostgreSQL 16 で
    /// information_schema は 0 行・pg_attribute は全列を返すことを確認済み）。
    /// </remarks>
    [Fact(DisplayName = "[Integration] PG: 列権限の無いロールでも列定義を取り込める")]
    public async Task Import_LowPrivilegeRole_StillReadsColumns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE secret (id integer PRIMARY KEY, val varchar(30));
            DROP ROLE IF EXISTS lowpriv;
            CREATE ROLE lowpriv;
            GRANT USAGE ON SCHEMA public TO lowpriv;
            """,
            Ct
        );

        try
        {
            await using var conn = await fixture.OpenConnectionAsync(Ct);

            // SET ROLE で「テーブルへ何の権限も持たないロール」に成り代わって取り込む
            await using (var setRole = conn.CreateCommand())
            {
                setRole.CommandText = "SET ROLE lowpriv;";
                await setRole.ExecuteNonQueryAsync(Ct);
            }

            var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

            var table = result.Entities.Single(e => e.TableName == "secret");
            table
                .Columns.Select(c => (c.Name, c.DataType))
                .Should()
                .BeEquivalentTo([("id", "integer"), ("val", "varchar(30)")]);
            result
                .Warnings.Should()
                .BeEmpty("列が読めている以上、列なしテーブルの警告は出ないこと");
        }
        finally
        {
            await fixture.ExecuteAsync(
                "REVOKE USAGE ON SCHEMA public FROM lowpriv; DROP ROLE IF EXISTS lowpriv;",
                Ct
            );
        }
    }

    /// <summary>
    /// パーティション親テーブルを取り込み、その実体である子パーティションは取り込まないことを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] PG: パーティション親を取り込み、子パーティションは除外する")]
    public async Task Import_IncludesPartitionParent_ExcludesPartitions()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE sales (id integer NOT NULL, amount numeric(10,2)) PARTITION BY RANGE (id);
            CREATE TABLE sales_p1 PARTITION OF sales FOR VALUES FROM (1) TO (100);
            CREATE TABLE sales_p2 PARTITION OF sales FOR VALUES FROM (100) TO (200);
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        // 旧実装は relkind = 'r' で絞っていたため、親（'p'）が丸ごと落ちて図が空になっていた
        result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("sales");

        // パーティション定義は意味モデルに無い＝この図から作る DDL はパーティションなしのテーブルになる
        var partitionWarning = result.Warnings.Should().ContainSingle().Subject;
        partitionWarning.Kind.Should().Be(SchemaImportWarningKind.PartitionDefinitionLost);
        partitionWarning.TableName.Should().Be("sales");
        result
            .Entities.Single()
            .Columns.Select(c => (c.Name, c.DataType))
            .Should()
            .BeEquivalentTo([("id", "integer"), ("amount", "numeric(10,2)")]);
    }

    /// <summary>
    /// ドメイン型の列が基底型として取り込まれ、そのことが警告として報告されることを検証する。
    /// </summary>
    [Fact(DisplayName = "[Integration] PG: ドメイン型は基底型へ平坦化し、警告として報告する")]
    public async Task Import_DomainColumn_FlattensToBaseTypeAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE DOMAIN email_dom AS varchar(320) CHECK (VALUE LIKE '%@%');
            CREATE TABLE person (id integer PRIMARY KEY, mail email_dom);
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        result
            .Entities.Single()
            .Columns.Single(c => c.Name == "mail")
            .DataType.Should()
            .Be("varchar(320)");

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.DomainTypeFlattened);
        warning.TableName.Should().Be("person");
        warning.Subject.Should().Be("mail");
        warning.Detail.Should().Be("email_dom");
    }

    /// <summary>
    /// 取込範囲（public スキーマ）の外を参照する外部キーが、リレーションにならず警告として報告されることを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PG: 取込範囲外を参照する外部キーは除外し、警告として報告する"
    )]
    public async Task Import_ForeignKeyToOtherSchema_IsExcludedWithWarning()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE SCHEMA other_scope;
            CREATE TABLE other_scope.vendor (id integer PRIMARY KEY);
            CREATE TABLE po (
                id integer PRIMARY KEY,
                vendor_id integer REFERENCES other_scope.vendor (id)
            );
            """,
            Ct
        );

        try
        {
            await using var conn = await fixture.OpenConnectionAsync(Ct);
            var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

            result.Entities.Select(e => e.TableName).Should().BeEquivalentTo("po");
            result.Relationships.Should().BeEmpty("親テーブルが図に無いためリレーションは作れない");

            var warning = result.Warnings.Should().ContainSingle().Subject;
            warning.Kind.Should().Be(SchemaImportWarningKind.ForeignKeyOutsideScope);
            warning.TableName.Should().Be("po");
            warning.Detail.Should().Be("other_scope.vendor");
        }
        finally
        {
            // ResetSchemaAsync は public スキーマしか作り直さないため、自分で撤去する
            await fixture.ExecuteAsync("DROP SCHEMA other_scope CASCADE;", Ct);
        }
    }

    /// <summary>
    /// 大文字小文字だけが違う同名テーブルを、列を混ぜずに片方だけ取り込み、警告として報告することを検証する。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] PG: 大文字小文字だけ違う同名テーブルは列を混ぜず片方を捨てて報告する"
    )]
    public async Task Import_TableNamesDifferingOnlyInCase_KeepsOneAndWarns()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TABLE dup (lower_only integer);
            CREATE TABLE "Dup" (upper_only varchar(10));
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        // relname 昇順で先に来た "Dup" を採り、後着の dup は捨てる
        var entity = result.Entities.Should().ContainSingle().Subject;
        entity.TableName.Should().Be("Dup");
        entity
            .Columns.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["upper_only"], "両テーブルの列が 1 エンティティへ混ざってはいけない");

        var warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(SchemaImportWarningKind.TableNameCollision);
        warning.Subject.Should().Be("dup");
        warning.Detail.Should().Be("Dup");
    }

    /// <summary>
    /// DDL へ出せない型表記を持つ列を、取込完了時に名指しで報告することを検証する。
    /// </summary>
    /// <remarks>
    /// 引用が要るユーザー定義型名（<c>"od;d"</c>）は <c>format_type()</c> が引用込みで返す。この表記は
    /// <see cref="SqlTypeText"/> を通らないため、図へ黙って入れると<b>後で DDL を叩いた瞬間に図全体が</b>
    /// 止まる。取込時点が一番早く分かる場所なので、そこで告げる（取込自体は成功させる）。
    /// </remarks>
    [Fact(DisplayName = "[Integration] PG: DDL へ出せない型表記は取込完了時に名指しで報告する")]
    public async Task Import_ColumnTypeNotEmittable_IsReported()
    {
        Assert.SkipUnless(fixture.IsAvailable, fixture.UnavailableReason);
        await fixture.ResetSchemaAsync(Ct);

        await fixture.ExecuteAsync(
            """
            CREATE TYPE public."od;d" AS ENUM ('a', 'b');
            CREATE TABLE weird (id integer PRIMARY KEY, kind public."od;d");
            """,
            Ct
        );

        await using var conn = await fixture.OpenConnectionAsync(Ct);
        var result = await new PostgreSqlSchemaImporter().ImportAsync(conn, Ct);

        // 取込自体は成功する（型を直せば使える図なので、取り込めないことにするほうが損失が大きい）
        var column = result
            .Entities.Single(e => e.TableName == "weird")
            .Columns.Single(c => c.Name == "kind");
        column.DataType.Should().Be("\"od;d\"");
        SqlTypeText.IsSafe(column.DataType).Should().BeFalse();

        var warning = result
            .Warnings.Should()
            .ContainSingle(w => w.Kind == SchemaImportWarningKind.ColumnTypeNotEmittable)
            .Subject;
        warning.TableName.Should().Be("weird");
        warning.Subject.Should().Be("kind");
        warning.Detail.Should().Be("\"od;d\"");
    }
}
