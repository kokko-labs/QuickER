using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>簡易 DSL の条件の総当たりで流す単位（図の列・値オブジェクトの有無・方言）</summary>
/// <remarks>
/// テーブル名はどの単位も <c>items</c>（生成物の型は <c>ItemEntity</c> / <c>IItemRepository</c>）。
/// SQLite の単位は QuickER 版 Repository・EF Core・インメモリの 3 実装先で実行し、MySQL の単位は符号なし整数・sbyte の列を
/// 持つ図をインメモリでだけ実行する（MySQL の QuickER 版 Repository は無く、実 DB の検証は区切り 23a の範囲外）。
/// SQL Server の単位は Integration の派生が実コンテナの QuickER 版 Repository・EF Core で実行する。
/// </remarks>
public static class DslFuzzUnits
{
    /// <summary>テーブル名</summary>
    public const string TableName = "items";

    /// <summary>行の数</summary>
    private const int RowCount = 24;

    /// <summary>単位あたりの合成のクエリの数</summary>
    private const int Compounds = 30;

    /// <summary>SQLite の単位（値オブジェクトなし・あり・GuidKey の主キー）</summary>
    public static IEnumerable<DslFuzzUnit> Sqlite(int seed)
    {
        yield return Build("sqlite-plain", seed, SqliteColumns(), valueObjects: false);
        yield return Build("sqlite-vo", seed, SqliteColumns(), valueObjects: true);
        yield return Build(
            "sqlite-vo-guidkey",
            seed,
            [
                new("id", "TEXT", DslClr.GuidKey, Nullable: false, PrimaryKey: true),
                new("s", "TEXT", DslClr.String, Nullable: true),
                new("i32", "int", DslClr.Int32, Nullable: true),
            ],
            valueObjects: true
        );
    }

    /// <summary>SQL Server の単位（値オブジェクトなし・あり・GuidKey の主キー。Integration の派生が実 DB で流す）</summary>
    public static IEnumerable<DslFuzzUnit> SqlServer(int seed)
    {
        yield return Build("sqlserver-plain", seed, SqlServerColumns(), valueObjects: false);
        yield return Build("sqlserver-vo", seed, SqlServerColumns(), valueObjects: true);
        yield return Build(
            "sqlserver-vo-guidkey",
            seed,
            [
                new("id", "nvarchar(36)", DslClr.GuidKey, Nullable: false, PrimaryKey: true),
                new("s", "nvarchar(50)", DslClr.String, Nullable: true),
                new("i32", "int", DslClr.Int32, Nullable: true),
            ],
            valueObjects: true
        );
    }

    /// <summary>MySQL の単位（符号なし整数・sbyte。インメモリでだけ実行する）</summary>
    public static IEnumerable<DslFuzzUnit> MySql(int seed)
    {
        yield return Build("mysql-plain", seed, MySqlColumns(), valueObjects: false);
        yield return Build("mysql-vo", seed, MySqlColumns(), valueObjects: true);
    }

    /// <summary>網羅の単位の乱数の種（網羅の単位は種の列の外で、いつも同じ値とパラメータ名を作る）</summary>
    private const int CoverageSeed = 0;

    /// <summary>網羅の単位の名前の接頭辞（名指しの検査と失敗のキーに出る）</summary>
    public const string CoveragePrefix = "coverage-";

    /// <summary>
    /// 網羅の単位（方言ごとに 1 つ・種に依らない。網羅しない組の一覧の Pending を埋めるために、総当たりのテストが種の前に
    /// 必ず流す。単独の種では流さない）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 主キーを GuidKey にし（値オブジェクトあり）、主キーを参照する NULL 許容の自己参照の外部キー列 <c>parent</c>（外部キーの
    /// 値オブジェクトの統一で GuidKey になる）を足す＝NULL を許す GuidKey の列の NULL 判定と射影の非 NULL 指定を作る。
    /// クエリは <see cref="DslFuzzGenerator.CoverageQueries"/>（戻り形ごと・射影の非 NULL 指定の、原子の形・自身の否定・列の種類・
    /// NOT の向きを全水準含む合成のクエリ）と、網羅の単位で初めて現れる列の種類の列の原子のクエリ。
    /// MySQL の単位は、種の単位（符号なし整数・sbyte・byte と文字列）が持たない列の種類（固定長の文字列・符号つき整数・
    /// decimal・日時・GuidKey）も持つ。
    /// </para>
    /// <para>
    /// 単位のクエリは、その構成のどの実装先でも照合したクエリにならなければならない（<see cref="DslFuzzCoverage.UncheckedProblems"/>
    /// が名指しする）。そのため、実装先ごとの照合したクエリの下限（DslFuzzRunner の MinimumCheckedQueries）は掛けない。
    /// </para>
    /// </remarks>
    /// <param name="dbms">方言（<see cref="DialectNames"/> の SQLite・SQL Server・MySQL）</param>
    public static DslFuzzUnit Coverage(string dbms)
    {
        var (key, text, columns, atomColumns) = dbms switch
        {
            DialectNames.Sqlite => ("TEXT", "TEXT", SqliteColumns().Skip(1), new[] { "parent" }),
            DialectNames.SqlServer => (
                "nvarchar(36)",
                "nvarchar(36)",
                SqlServerColumns().Skip(1),
                new[] { "parent" }
            ),
            DialectNames.MySql => (
                "char(36)",
                "char(36)",
                MySqlColumns()
                    .Skip(1)
                    .Concat([
                        new("f", "char(10)", DslClr.FixedString, Nullable: true),
                        new("i64", "bigint", DslClr.Int64, Nullable: true),
                        new("i32", "int", DslClr.Int32, Nullable: true),
                        new("i16", "smallint", DslClr.Int16, Nullable: true),
                        new("d", "decimal(10,2)", DslClr.Decimal, Nullable: true),
                        new("dt", "datetime", DslClr.DateTime, Nullable: true),
                    ]),
                new[] { "id", "parent", "f", "i64", "i32", "i16", "d", "dt" }
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, null),
        };
        DslColumn[] all =
        [
            new("id", key, DslClr.GuidKey, Nullable: false, PrimaryKey: true),
            new("parent", text, DslClr.GuidKey, Nullable: true),
            .. columns,
        ];
        var generator = new DslFuzzGenerator(CoverageSeed, valueObjects: true);
        var rows = generator.Rows(all, RowCount);

        // 自己参照の外部キーは、その行より前の行のキーか NULL（3 行に 1 行は NULL・1 行目は必ず NULL）
        for (var i = 0; i < rows.Count; i++)
        {
            ((Dictionary<string, object?>)rows[i])["parent"] =
                i % 3 == 0 ? null : rows[i / 2]["id"];
        }

        // 序数で比べる文字列一致の値は SQLite の単位だけ（SQL Server は照合順序で合成済みと分解形が等しくなる）
        var ordinal = dbms == DialectNames.Sqlite;

        if (ordinal)
        {
            PutOrdinalValues(rows);
        }

        return new DslFuzzUnit(
            $"{CoveragePrefix}{dbms}",
            TableName,
            all,
            rows,
            [
                .. generator.CoverageQueries(all, atomColumns),
                .. generator.FixedQueries(all, ordinalStrings: ordinal),
            ],
            ValueObjects: true,
            CoverageSeed,
            SelfReference: "parent"
        );
    }

    /// <summary>
    /// 値オブジェクトなしの SQLite の網羅の単位（種の単位と同じ列・種に依らない）。網羅の単位（<see cref="Coverage"/>）は
    /// 値オブジェクトありだけなので、素の string の列・素の値型の列の経路（インメモリの文字列一致の序数への書き換え・
    /// NOT の押し下げ）を決まった形のクエリ（<see cref="DslFuzzGenerator.FixedQueries"/>）で流す
    /// </summary>
    public static DslFuzzUnit PlainCoverage()
    {
        var columns = SqliteColumns();
        var generator = new DslFuzzGenerator(CoverageSeed, valueObjects: false);
        var rows = generator.Rows(columns, RowCount);
        PutOrdinalValues(rows);

        return new DslFuzzUnit(
            $"{CoveragePrefix}{DialectNames.Sqlite}-plain",
            TableName,
            columns,
            rows,
            generator.FixedQueries(columns, ordinalStrings: true),
            ValueObjects: false,
            CoverageSeed
        );
    }

    /// <summary>
    /// 列 <c>s</c> の 3〜7 行目に、カルチャで比べると序数と結果が変わる値を入れる：合成済みの「が」で始まる・分解形で始まる・
    /// 合成済みで終わる・分解形で終わる・ソフトハイフンを無視すると <c>a</c> で始まる（1 行目の NULL と 2 行目の値は保つ）
    /// </summary>
    private static void PutOrdinalValues(List<IReadOnlyDictionary<string, object?>> rows)
    {
        string[] values =
        [
            DslFuzzGenerator.OrdinalComposed + "-x",
            DslFuzzGenerator.OrdinalDecomposed + "-x",
            "x-" + DslFuzzGenerator.OrdinalComposed,
            "x-" + DslFuzzGenerator.OrdinalDecomposed,
            "abc",
        ];

        for (var i = 0; i < values.Length; i++)
        {
            ((Dictionary<string, object?>)rows[2 + i])["s"] = values[i];
        }
    }

    /// <summary>射影の結果の型名を、生成物の既存の型名と衝突させる単位（行なし＝コンパイルだけ）</summary>
    public static IEnumerable<DslFuzzUnit> Collisions()
    {
        yield return Collision("collision-entity", "ItemEntity", valueObjects: false);
        yield return Collision("collision-repository", "ItemRepository", valueObjects: false);
        yield return Collision("collision-value-object", "MemoValue", valueObjects: true);
        yield return Collision("collision-task", "Task", valueObjects: false);
    }

    private static DslFuzzUnit Build(
        string name,
        int seed,
        IReadOnlyList<DslColumn> columns,
        bool valueObjects
    )
    {
        var generator = new DslFuzzGenerator(seed, valueObjects);

        return new DslFuzzUnit(
            name,
            TableName,
            columns,
            generator.Rows(columns, RowCount),
            generator.Queries(columns, Compounds),
            valueObjects,
            seed
        );
    }

    private static DslFuzzUnit Collision(string name, string typeName, bool valueObjects)
    {
        DslColumn[] columns =
        [
            new("id", "INTEGER", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("memo", "TEXT", DslClr.String, Nullable: true),
        ];

        return new DslFuzzUnit(
            name,
            TableName,
            columns,
            [],
            [
                new DslQuery
                {
                    Name = "Pick",
                    Condition = new DslNullCheck(IsNot: true) { Column = columns[1] },
                    Returns = QueryReturnShape.Projection,
                    IsAtomQuery = false,
                    ProjectedColumn = columns[1],
                    ResultTypeName = typeName,
                    TypeNameCollision = typeName,
                },
            ],
            valueObjects
        );
    }

    private static DslColumn[] SqliteColumns() =>
        [
            new("id", "INTEGER", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("s", "TEXT", DslClr.String, Nullable: true),
            new("sn", "TEXT", DslClr.String, Nullable: false),
            new("f", "char(10)", DslClr.FixedString, Nullable: true),
            new("i8", "tinyint", DslClr.Byte, Nullable: true),
            new("i16", "smallint", DslClr.Int16, Nullable: true),
            new("i32", "int", DslClr.Int32, Nullable: true),
            new("i32n", "int", DslClr.Int32, Nullable: false),
            new("i64", "bigint", DslClr.Int64, Nullable: true),
            new("d", "decimal(10,2)", DslClr.Decimal, Nullable: true),
            new("dt", "datetime", DslClr.DateTime, Nullable: true),
            new("g", "uniqueidentifier", DslClr.Guid, Nullable: true),
        ];

    private static DslColumn[] SqlServerColumns() =>
        [
            new("id", "bigint", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("s", "nvarchar(50)", DslClr.String, Nullable: true),
            new("sn", "nvarchar(50)", DslClr.String, Nullable: false),
            new("f", "nchar(10)", DslClr.FixedString, Nullable: true),
            new("i8", "tinyint", DslClr.Byte, Nullable: true),
            new("i16", "smallint", DslClr.Int16, Nullable: true),
            new("i32", "int", DslClr.Int32, Nullable: true),
            new("i32n", "int", DslClr.Int32, Nullable: false),
            new("i64", "bigint", DslClr.Int64, Nullable: true),
            new("d", "decimal(10,2)", DslClr.Decimal, Nullable: true),
            new("dt", "datetime2", DslClr.DateTime, Nullable: true),
            new("g", "uniqueidentifier", DslClr.Guid, Nullable: true),
        ];

    private static DslColumn[] MySqlColumns() =>
        [
            new("id", "bigint", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("u8", "tinyint unsigned", DslClr.Byte, Nullable: true),
            new("s8", "tinyint", DslClr.SByte, Nullable: true),
            new("u16", "smallint unsigned", DslClr.UInt16, Nullable: true),
            new("u32", "int unsigned", DslClr.UInt32, Nullable: true),
            new("u32n", "int unsigned", DslClr.UInt32, Nullable: false),
            new("u64", "bigint unsigned", DslClr.UInt64, Nullable: true),
            new("u64n", "bigint unsigned", DslClr.UInt64, Nullable: false),
            new("s", "varchar(20)", DslClr.String, Nullable: true),
        ];
}
