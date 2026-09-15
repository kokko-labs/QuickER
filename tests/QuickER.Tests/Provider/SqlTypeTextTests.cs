using AwesomeAssertions;
using QuickER.Model;
using QuickER.MySql;
using QuickER.Oracle;
using QuickER.PostgreSql;
using QuickER.Provider;
using QuickER.Sqlite;
using QuickER.SqlServer;
using Xunit;

namespace QuickER.Tests.Provider;

/// <summary>
/// <see cref="SqlTypeText"/> の安全パターン検証と、DDL / 同期スクリプト生成がそれを関門にしていることを検証するテストクラス。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Column.DataType"/> は図の自由記述で、DDL・同期スクリプトへクォートもエスケープもせず補間される。
/// ここでは「5 方言の型カタログに載る全表記と、5 方言のスキーマ取込が実 DB から組み立てる表記が全部通ること」
/// （＝セキュリティ修正が実 DB でのデータ欠落へ化けないこと）と、
/// 「文を終わらせる・文字列を開く・コメントを始める文字を含む表記は名指しで失敗すること」を同時に固定する。
/// </para>
/// </remarks>
public class SqlTypeTextTests
{
    // ---------------- 安全パターンの通過（正当表記の全数） ----------------

    /// <summary>5 方言の型カタログ（GUI の候補リスト）に載る全表記が 1 つ残らず通ることを検証する</summary>
    /// <remarks>
    /// 候補リストは利用者が最も普通に選ぶ表記で、1 つでも落ちると「既定の型で DDL が出せない」回帰になる。
    /// カタログ実体を直接読むため、候補が増えてもテストが自動的に追随する。
    /// </remarks>
    [Fact(DisplayName = "5 方言の型カタログの全表記が安全パターンを通る")]
    public void IsSafe_AllCatalogDataTypes_Pass()
    {
        var catalogs = new (string Dialect, IReadOnlyList<string> Types)[]
        {
            ("SqlServer", SqlServerDataTypes.All),
            ("PostgreSql", PostgreSqlDataTypes.All),
            ("MySql", MySqlDataTypes.All),
            ("Oracle", OracleDataTypes.All),
            ("Sqlite", SqliteDataTypes.All),
        };

        foreach (var (dialect, types) in catalogs)
        {
            types.Should().NotBeEmpty($"{dialect} の型カタログが空ではないこと");

            foreach (var type in types)
            {
                SqlTypeText
                    .IsSafe(type)
                    .Should()
                    .BeTrue($"{dialect} のカタログ表記 '{type}' は正当な型なので通るべき");
            }
        }
    }

    /// <summary>スキーマ取込・方言変換が実際に組み立てる表記（カタログ外を含む）が通ることを検証する</summary>
    /// <remarks>
    /// 5 方言のインポータは実 DB の表記を verbatim に近い形で持ち帰るため、カタログに無い型が図へ入る。
    /// 特に Oracle の括弧後置語（<c>TIMESTAMP(6) WITH TIME ZONE</c>）・MySQL の括弧後修飾子
    /// （<c>decimal(10,2) unsigned</c>）・MySQL の値リスト型（<c>enum('a','b')</c>）は、
    /// 素朴なホワイトリストだと落ちて「実 DB の図が同期できない」回帰になる。
    /// </remarks>
    [Theory(DisplayName = "取込が組み立てる実在の型表記が安全パターンを通る")]
    // SQL Server
    [InlineData("nvarchar(max)")]
    [InlineData("varbinary(max)")]
    [InlineData("decimal(10,2)")]
    [InlineData("decimal(18)")]
    [InlineData("datetime2")]
    [InlineData("uniqueidentifier")]
    [InlineData("sql_variant")]
    [InlineData("hierarchyid")]
    [InlineData("rowversion")]
    // PostgreSQL
    [InlineData("double precision")]
    [InlineData("timestamp with time zone")]
    [InlineData("timestamp without time zone")]
    [InlineData("time with time zone")]
    [InlineData("character varying(255)")]
    [InlineData("bit varying")]
    [InlineData("timestamptz(6)")]
    [InlineData("numeric(10, 2)")]
    // 負のスケール（100 の倍数へ丸める）は PostgreSQL / Oracle の実在宣言で、取込がそのまま持ち帰る
    [InlineData("numeric(10,-2)")]
    [InlineData("bit(8)")]
    [InlineData("bit varying(16)")]
    [InlineData("interval day to second(3)")]
    [InlineData("time(3) with time zone")]
    [InlineData("varchar(20)[]")]
    // 多次元配列（format_type は次元を出さないが、図の型は手入力・他ツール由来でもあり得る）
    [InlineData("integer[][]")]
    // PostGIS の型は括弧引数に語を取る。数値限定だと、この 1 列のせいで図全体の DDL 生成が止まる
    [InlineData("geometry")]
    [InlineData("geometry(Point,4326)")]
    [InlineData("geometry(MultiPolygon)")]
    [InlineData("geography(Point,4326)")]
    [InlineData("geometry(PointZ,4326)")]
    [InlineData("_int4")]
    [InlineData("jsonb")]
    [InlineData("integer[]")]
    // MySQL
    [InlineData("tinyint(1)")]
    [InlineData("int unsigned")]
    [InlineData("decimal(10,2) unsigned")]
    [InlineData("bigint(20) unsigned zerofill")]
    [InlineData("enum('a','b')")]
    [InlineData("set('x', 'y')")]
    [InlineData("longblob")]
    // Oracle
    [InlineData("NUMBER(10,2)")]
    [InlineData("NUMBER(10,-2)")]
    [InlineData("NUMBER(*,2)")]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE")]
    [InlineData("TIMESTAMP(6) WITH LOCAL TIME ZONE")]
    [InlineData("INTERVAL DAY(2) TO SECOND(6)")]
    [InlineData("INTERVAL YEAR(2) TO MONTH")]
    [InlineData("LONG RAW")]
    [InlineData("VARCHAR2(100 CHAR)")]
    [InlineData("CHAR(10 BYTE)")]
    [InlineData("SDO_GEOMETRY")]
    [InlineData("XMLTYPE")]
    // SQLite（外部 DB 由来の宣言型を verbatim に持ち帰る）
    [InlineData("NVARCHAR(MAX)")]
    [InlineData("UNSIGNED BIG INT")]
    [InlineData("VARYING CHARACTER(255)")]
    [InlineData("NATIVE CHARACTER(70)")]
    [InlineData("DOUBLE PRECISION")]
    // 型未設定は「注入」ではないので安全側
    [InlineData("")]
    [InlineData("   ")]
    public void IsSafe_RealWorldDataTypes_Pass(string dataType) =>
        SqlTypeText.IsSafe(dataType).Should().BeTrue();

    // ---------------- 安全パターンの拒否 ----------------

    [Theory(DisplayName = "SQL を壊せる型表記は安全パターンを通らない")]
    [InlineData("int'; DROP TABLE users; --")]
    [InlineData("varchar(50)'")]
    [InlineData("int\r\nDROP TABLE users")]
    [InlineData("int -- comment")]
    [InlineData("int /* comment */")]
    [InlineData("int; DELETE FROM users")]
    [InlineData("[int]")]
    [InlineData("\"char\"")]
    [InlineData("`int`")]
    [InlineData("int\\")]
    [InlineData("user-defined")]
    // 値リスト型に見せかけてリテラルを閉じる形（\ による脱出は許さない）
    [InlineData(@"enum('a\','b')")]
    [InlineData("enum('a'; DROP TABLE users; --')")]
    public void IsSafe_UnsafeDataTypes_Rejected(string dataType) =>
        SqlTypeText.IsSafe(dataType).Should().BeFalse();

    [Fact(DisplayName = "長さ上限を超える型表記は安全パターンを通らない")]
    public void IsSafe_TooLong_Rejected() =>
        SqlTypeText.IsSafe(new string('a', SqlTypeText.MaxLength + 1)).Should().BeFalse();

    /// <summary><c>#</c> は MySQL の行末コメント開始なので語の構成文字から外れていることを検証する</summary>
    /// <remarks>
    /// <c>int#</c> が通ると、MySQL の <c>MODIFY COLUMN</c> で型の直後に続く <c>NOT NULL</c> / <c>COMMENT</c> /
    /// <c>AFTER</c> が行末コメントとして黙って消える（構文エラーにならないまま意味だけが変わる）。
    /// </remarks>
    [Theory(DisplayName = "# を含む型表記は安全パターンを通らない（MySQL の行末コメント）")]
    [InlineData("int#")]
    [InlineData("int# comment")]
    [InlineData("#int")]
    public void IsSafe_HashInDataType_Rejected(string dataType) =>
        SqlTypeText.IsSafe(dataType).Should().BeFalse();

    /// <summary>
    /// 前後に空白・改行が付いた型表記は安全パターンを通らないことを検証する（検証は Trim 後・出力は生、の不一致）。
    /// </summary>
    /// <remarks>
    /// 生成側は <see cref="Column.DataType"/> を<b>そのまま</b>補間するため、Trim 後の姿だけを見て通すと
    /// 実際に出力される文字列（改行つき）は検証されていないことになる。<c>"\nGO\n"</c> は Trim 後は語 1 つの
    /// <c>GO</c> に見えるが、出力されると SQL Server のバッチ分割（<c>^\s*GO\s*$</c>）を誤作動させ得る。
    /// </remarks>
    [Theory(DisplayName = "前後に空白・改行が付いた型表記は安全パターンを通らない")]
    [InlineData("\nGO\n")]
    [InlineData(" int")]
    [InlineData("int ")]
    [InlineData(" int ")]
    [InlineData("\tint")]
    public void IsSafe_UntrimmedDataType_Rejected(string dataType) =>
        SqlTypeText.IsSafe(dataType).Should().BeFalse();

    /// <summary>
    /// 型の 2 語目以降に列定義の句（<c>NOT NULL</c> 等）が現れる表記は安全パターンを通らないことを検証する。
    /// </summary>
    /// <remarks>
    /// 語の並びは <c>double precision</c> / <c>LONG RAW</c> のような実在の型のために許しているが、その隙間から
    /// <c>int NOT NULL</c> / <c>int DEFAULT 0</c> のような<b>句</b>を密輸できる（型の直後は列定義の続きなので
    /// そのまま有効な DDL になり、NULL 許容・既定値・照合順序が図の宣言と食い違ったまま通ってしまう）。
    /// 実在の型表記（<c>TIMESTAMP WITH TIME ZONE</c> 等）とは衝突しない語だけを拒否する。
    /// </remarks>
    [Theory(DisplayName = "2 語目以降に列定義の句を含む型表記は安全パターンを通らない")]
    [InlineData("int NOT NULL")]
    [InlineData("int DEFAULT 0")]
    [InlineData("int PRIMARY KEY")]
    [InlineData("int REFERENCES users")]
    [InlineData("int GENERATED ALWAYS")]
    [InlineData("int IDENTITY")]
    [InlineData("varchar(50) COLLATE Latin1_General_BIN")]
    [InlineData("int UNIQUE")]
    [InlineData("int CHECK")]
    public void IsSafe_SmuggledColumnClause_Rejected(string dataType) =>
        SqlTypeText.IsSafe(dataType).Should().BeFalse();

    // ---------------- DDL 生成の関門 ----------------

    /// <summary>安全でない型を含む図では 5 方言とも DDL 生成が失敗し、テーブル.列と型を名指しすることを検証する</summary>
    [Fact(DisplayName = "5 方言の DDL 生成は安全でない型をテーブル.列つきで拒否する")]
    public void DdlGenerators_RejectUnsafeDataType()
    {
        var diagram = BuildDiagram("int'; DROP TABLE users; --");

        var generators = new IDdlGenerator[]
        {
            new SqlServerDdlGenerator(),
            new PostgreSqlDdlGenerator(),
            new MySqlDdlGenerator(),
            new OracleDdlGenerator(),
            new SqliteDdlGenerator(),
        };

        foreach (var generator in generators)
        {
            var act = () => generator.Build(diagram);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*orders.memo*")
                .WithMessage("*DROP TABLE users*");
        }
    }

    [Fact(DisplayName = "5 方言の DDL 生成は安全な型の図をそのまま生成する")]
    public void DdlGenerators_AcceptSafeDataType()
    {
        var diagram = BuildDiagram("nvarchar(max)");

        new SqlServerDdlGenerator().Build(diagram).Should().Contain("CREATE TABLE");
        new PostgreSqlDdlGenerator().Build(diagram).Should().Contain("CREATE TABLE");
        new MySqlDdlGenerator().Build(diagram).Should().Contain("CREATE TABLE");
        new OracleDdlGenerator().Build(diagram).Should().Contain("CREATE TABLE");
        new SqliteDdlGenerator().Build(diagram).Should().Contain("CREATE TABLE");
    }

    // ---------------- 同期スクリプト生成の関門 ----------------

    /// <summary>安全でない型を含む差分では 5 方言とも同期スクリプト生成が失敗することを検証する</summary>
    [Fact(DisplayName = "5 方言の同期スクリプト生成は安全でない型をテーブル.列つきで拒否する")]
    public void SyncScriptBuilders_RejectUnsafeDataType()
    {
        var diagram = BuildDiagram("int'; DROP TABLE users; --");
        var entity = diagram.Entities[0];
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AddTable,
                    TableName = entity.TableName,
                    Entity = entity,
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities()
        );

        var builders = new ISyncScriptBuilder[]
        {
            new SqlServerSyncScriptBuilder(),
            new PostgreSqlSyncScriptBuilder(),
            new MySqlSyncScriptBuilder(),
            new OracleSyncScriptBuilder(),
            new SqliteSyncScriptBuilder(),
        };

        foreach (var builder in builders)
        {
            var act = () => builder.Build(plan);

            act.Should().Throw<InvalidOperationException>().WithMessage("*orders.memo*");
        }
    }

    /// <summary>単一列の差分（AlterColumn）でも関門が効くことを検証する</summary>
    [Fact(DisplayName = "同期スクリプト生成は AlterColumn 単体の安全でない型も拒否する")]
    public void SyncScriptBuilders_RejectUnsafeDataTypeOnSingleColumn()
    {
        var plan = new SyncPlanner().BuildPlan(
            [
                new SchemaDiffItem
                {
                    Kind = SchemaDiffKind.AlterColumn,
                    TableName = "orders",
                    ColumnName = "memo",
                    Column = new Column
                    {
                        Name = "memo",
                        DataType = "nvarchar(50)'; DROP TABLE users; --",
                        IsNullable = true,
                    },
                    IsSelected = true,
                },
            ],
            new SyncDialectCapabilities { SupportsAlterColumn = true }
        );

        var act = () => new SqlServerSyncScriptBuilder().Build(plan);

        act.Should().Throw<InvalidOperationException>().WithMessage("*orders.memo*");
    }

    /// <summary>orders テーブル（int 主キー＋指定型の memo 列）の最小の図を組み立てる</summary>
    private static ErDiagram BuildDiagram(string memoDataType) =>
        new()
        {
            Entities =
            [
                new Entity
                {
                    TableName = "orders",
                    Columns =
                    [
                        new Column
                        {
                            Name = "id",
                            DataType = "int",
                            IsPrimaryKey = true,
                            IsNullable = false,
                        },
                        new Column
                        {
                            Name = "memo",
                            DataType = memoDataType,
                            IsNullable = true,
                        },
                    ],
                },
            ],
        };
}
