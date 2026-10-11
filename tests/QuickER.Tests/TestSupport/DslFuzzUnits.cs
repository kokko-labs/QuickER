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
        yield return Build(
            "sqlite-plain",
            seed,
            SqliteColumns(),
            valueObjects: false,
            outOfRange: true
        );
        yield return Build(
            "sqlite-vo",
            seed,
            SqliteColumns(),
            valueObjects: true,
            outOfRange: false
        );
        yield return Build(
            "sqlite-vo-guidkey",
            seed,
            [
                new("id", "TEXT", DslClr.GuidKey, Nullable: false, PrimaryKey: true),
                new("s", "TEXT", DslClr.String, Nullable: true),
                new("i32", "int", DslClr.Int32, Nullable: true),
            ],
            valueObjects: true,
            outOfRange: false
        );
    }

    /// <summary>SQL Server の単位（値オブジェクトなし・あり・GuidKey の主キー。Integration の派生が実 DB で流す）</summary>
    public static IEnumerable<DslFuzzUnit> SqlServer(int seed)
    {
        yield return Build(
            "sqlserver-plain",
            seed,
            SqlServerColumns(),
            valueObjects: false,
            outOfRange: true
        );
        yield return Build(
            "sqlserver-vo",
            seed,
            SqlServerColumns(),
            valueObjects: true,
            outOfRange: false
        );
        yield return Build(
            "sqlserver-vo-guidkey",
            seed,
            [
                new("id", "nvarchar(36)", DslClr.GuidKey, Nullable: false, PrimaryKey: true),
                new("s", "nvarchar(50)", DslClr.String, Nullable: true),
                new("i32", "int", DslClr.Int32, Nullable: true),
            ],
            valueObjects: true,
            outOfRange: false
        );
    }

    /// <summary>MySQL の単位（符号なし整数・sbyte。インメモリでだけ実行する）</summary>
    public static IEnumerable<DslFuzzUnit> MySql(int seed)
    {
        yield return Build(
            "mysql-plain",
            seed,
            MySqlColumns(),
            valueObjects: false,
            outOfRange: true
        );
        yield return Build("mysql-vo", seed, MySqlColumns(), valueObjects: true, outOfRange: false);
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
        bool valueObjects,
        bool outOfRange
    )
    {
        var generator = new DslFuzzGenerator(seed, valueObjects, outOfRange);

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
