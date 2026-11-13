using QuickER.Dialects;
using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>簡易 DSL の条件の総当たりで流す単位（図の列・値オブジェクトの有無・方言）</summary>
/// <remarks>
/// テーブル名はどの単位も <c>items</c>（生成物の型は <c>ItemEntity</c> / <c>IItemRepository</c>）。
/// SQLite の単位は QuickER 版 Repository・EF Core・インメモリの 3 実装先で実行し、MySQL の単位は符号なし整数・sbyte の列を
/// 持つ図をインメモリでだけ実行する（MySQL の QuickER 版 Repository は無く、実 DB の検証は区切り 23a の範囲外）。
/// SQL Server の単位は Integration の派生が実コンテナの QuickER 版 Repository・EF Core で実行する。 PostgreSQL・Oracle の単位と MySQL の単位は、Integration の派生が実コンテナの EF Core 版で実行する。
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

        foreach (var unit in Families("sqlite", DialectNames.Sqlite, seed))
        {
            yield return unit;
        }

        foreach (var unit in TimeSpans("sqlite", DialectNames.Sqlite, seed))
        {
            yield return unit;
        }
    }

    /// <summary>
    /// SQL Server の単位（値オブジェクトなし・あり・GuidKey の主キー・Unicode でない文字列の列。Integration の派生が実 DB で流す）
    /// </summary>
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
        yield return Build("sqlserver-ansi", seed, SqlServerAnsiColumns(), valueObjects: false);

        foreach (var unit in Families("sqlserver", DialectNames.SqlServer, seed))
        {
            yield return unit;
        }

        foreach (var unit in TimeSpans("sqlserver", DialectNames.SqlServer, seed))
        {
            yield return unit;
        }
    }

    /// <summary>
    /// SQL Server の Unicode でない文字列の列の網羅の単位（種に依らない・値オブジェクトなし）：varchar・char の列の、ASCII の外の文字を
    /// 含む値の原子（<see cref="DslFuzzGenerator.OutOfTypeCoverageQueries"/>）を使う合成のクエリと、その並び替えの写し
    /// </summary>
    /// <remarks>
    /// varchar の列の相方（ほかの列で使い回す形のため）と、並び替えの写しのために主キーより前に置ける NOT NULL の整数の列を 2 本持つ
    /// （NULL を許す整数の列は、列の型に入らない値の原子を作るので持たない）。パラメータの使い回しは、ASCII の外の文字を含む値を必ず
    /// 使い回すクエリ（KXR0）だけを作る。
    /// </remarks>
    public static DslFuzzUnit SqlServerAnsiCoverage()
    {
        var ansi = SqlServerAnsiColumns();
        DslColumn[] columns =
        [
            .. ansi,
            // varchar の列の相方（ほかの列でパラメータを使い回す形のため。with で写すとモデルの列まで共有するので作り直す）
            new(ansi[1].Name + PartnerSuffix, ansi[1].DataType, ansi[1].Clr, ansi[1].Nullable)
            {
                NonUnicode = true,
                Values = ansi[1].Values,
            },
            new("o1", "int", DslClr.Int32, Nullable: false),
            new("o2", "smallint", DslClr.Int16, Nullable: false),
        ];
        var rows = new DslFuzzGenerator(CoverageSeed, valueObjects: false).Rows(columns, RowCount);
        var queries = new DslFuzzGenerator(
            DslFuzzGenerator.OutOfTypeSeed(CoverageSeed),
            valueObjects: false,
            DslFuzzGenerator.OutOfTypeParameterPrefix
        ).OutOfTypeCoverageQueries(columns, decimalOrdered: true);
        var ordering = new DslFuzzGenerator(
            CoverageSeed,
            valueObjects: false,
            DslFuzzGenerator.SideParameterPrefix
        ).OrderingCoverageQueries(columns, queries);

        return new DslFuzzUnit(
            $"{CoveragePrefix}{DialectNames.SqlServer}-ansi",
            TableName,
            columns,
            rows,
            [.. queries, .. ordering],
            ValueObjects: false,
            CoverageSeed
        );
    }

    /// <summary>
    /// SQL Server の money・smallmoney の列の網羅の単位（種に依らない・値オブジェクトなし）：小数 4 桁の外・範囲の外の値と比べる原子 1 つずつの
    /// クエリ <c>KM*</c>（経路ごとの結果を読む）と、それを使う合成のクエリ <c>KX*</c>
    /// </summary>
    /// <remarks>
    /// money の値の範囲は 10 の累乗の桁では表せないので、列の型に入るかは <see cref="DslMoneyType"/> で決める。行の値の池は型の両端を持つ
    /// （smallmoney の列は、種類ごとの decimal の池の ±99999999.99 が入らないので範囲の中の値だけ）。種の単位は money の列を持たない
    /// （種の生成器の入力と指紋を変えないため）。
    /// </remarks>
    public static DslFuzzUnit SqlServerMoneyCoverage()
    {
        DslColumn[] columns =
        [
            new("id", "bigint", DslClr.Int64, Nullable: false, PrimaryKey: true),
            // 射影の非 NULL 指定の合成のクエリが射影する、NULL を許す文字列の列
            new("s", "nvarchar(50)", DslClr.String, Nullable: true),
            new("m", "money", DslClr.Decimal, Nullable: true)
            {
                Money = DslMoneyType.Money,
                Values =
                [
                    DslMoneyType.Money.Min,
                    -1.5m,
                    0m,
                    0.01m,
                    1.5m,
                    2.25m,
                    10m,
                    DslMoneyType.Money.Max,
                ],
            },
            new("sm", "smallmoney", DslClr.Decimal, Nullable: true)
            {
                Money = DslMoneyType.SmallMoney,
                Values =
                [
                    DslMoneyType.SmallMoney.Min,
                    -1.5m,
                    0m,
                    0.01m,
                    1.5m,
                    2.25m,
                    10m,
                    DslMoneyType.SmallMoney.Max,
                ],
            },
        ];
        var rows = new DslFuzzGenerator(CoverageSeed, valueObjects: false).Rows(columns, RowCount);
        var atoms = new DslFuzzGenerator(
            DslFuzzGenerator.OutOfTypeSeed(CoverageSeed),
            valueObjects: false,
            DslFuzzGenerator.OutOfTypeParameterPrefix
        ).OutOfTypeAtomQueries(columns, "KM");
        var queries = new DslFuzzGenerator(
            DslFuzzGenerator.OutOfTypeSeed(CoverageSeed),
            valueObjects: false,
            DslFuzzGenerator.OutOfTypeParameterPrefix
        ).OutOfTypeCoverageQueries(columns, decimalOrdered: true);

        return new DslFuzzUnit(
            $"{CoveragePrefix}{DialectNames.SqlServer}-money",
            TableName,
            columns,
            rows,
            [.. atoms, .. queries],
            ValueObjects: false,
            CoverageSeed
        );
    }

    /// <summary>
    /// PostgreSQL の単位（値オブジェクトなし・あり・GuidKey の主キー。Integration の派生が実 DB の EF Core 版で流す）
    /// </summary>
    public static IEnumerable<DslFuzzUnit> PostgreSql(int seed)
    {
        yield return Build("postgresql-plain", seed, PostgreSqlColumns(), valueObjects: false);
        yield return Build("postgresql-vo", seed, PostgreSqlColumns(), valueObjects: true);
        yield return Build(
            "postgresql-vo-guidkey",
            seed,
            [
                new("id", "varchar(36)", DslClr.GuidKey, Nullable: false, PrimaryKey: true),
                new("s", "varchar(50)", DslClr.String, Nullable: true),
                new("i32", "integer", DslClr.Int32, Nullable: true),
            ],
            valueObjects: true
        );

        foreach (var unit in Families("postgresql", DialectNames.PostgreSql, seed))
        {
            yield return unit;
        }

        foreach (var unit in TimeSpans("postgresql", DialectNames.PostgreSql, seed))
        {
            yield return unit;
        }
    }

    /// <summary>
    /// Oracle の単位（値オブジェクトなし・あり・GuidKey の主キー。Integration の派生が実 DB の EF Core 版で流す）
    /// </summary>
    public static IEnumerable<DslFuzzUnit> Oracle(int seed)
    {
        yield return Build("oracle-plain", seed, OracleColumns(), valueObjects: false);
        yield return Build("oracle-vo", seed, OracleColumns(), valueObjects: true);
        yield return Build(
            "oracle-vo-guidkey",
            seed,
            [
                new("id", "NVARCHAR2(36)", DslClr.GuidKey, Nullable: false, PrimaryKey: true),
                new("s", "NVARCHAR2(50)", DslClr.String, Nullable: true),
                new("i32", "NUMBER(10)", DslClr.Int32, Nullable: true),
            ],
            valueObjects: true
        );

        foreach (var unit in Families("oracle", DialectNames.Oracle, seed))
        {
            yield return unit;
        }
    }

    /// <summary>MySQL の単位（符号なし整数・sbyte。インメモリと、Integration の派生が実 DB の EF Core 版で流す）</summary>
    public static IEnumerable<DslFuzzUnit> MySql(int seed)
    {
        yield return Build("mysql-plain", seed, MySqlColumns(), valueObjects: false);
        yield return Build("mysql-vo", seed, MySqlColumns(), valueObjects: true);

        foreach (var unit in Families("mysql", DialectNames.MySql, seed))
        {
            yield return unit;
        }

        foreach (var unit in TimeSpans("mysql", DialectNames.MySql, seed))
        {
            yield return unit;
        }
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
    /// <param name="dbms">方言（<see cref="DialectNames"/> の SQLite・SQL Server・MySQL・PostgreSQL・Oracle）</param>
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
            // MySQL の GuidKey は可変長の varchar(36)（固定長の char(36) は型マッパーが Guid へ写す＝下の g の列）
            DialectNames.MySql => (
                "varchar(36)",
                "varchar(36)",
                MySqlColumns().Skip(1).Concat(MySqlCoverageExtraColumns()),
                new[] { "id", "parent", "f", "i64", "i32", "i16", "d", "dt", "g" }
            ),
            DialectNames.PostgreSql => (
                "varchar(36)",
                "varchar(36)",
                PostgreSqlColumns().Skip(1),
                new[] { "parent" }
            ),
            DialectNames.Oracle => (
                "NVARCHAR2(36)",
                "NVARCHAR2(36)",
                OracleColumns().Skip(1),
                new[] { "parent" }
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

        List<DslQuery> queries =
        [
            .. generator.CoverageQueries(all, atomColumns),
            .. generator.FixedQueries(all, ordinalStrings: ordinal),
        ];
        var withPartners = AddPartners(all, rows);

        return new DslFuzzUnit(
            $"{CoveragePrefix}{dbms}",
            TableName,
            withPartners,
            rows,
            [.. queries, .. ReuseAndOrdering(withPartners, queries, valueObjects: true)],
            ValueObjects: true,
            CoverageSeed,
            SelfReference: "parent"
        );
    }

    /// <summary>種の単位のパラメータを使い回すクエリの数</summary>
    private const int SideReuseQueries = 6;

    /// <summary>種の単位の列で並べ替えるクエリの数</summary>
    private const int SideOrderingQueries = 6;

    /// <summary>相方の列の名前の接尾辞</summary>
    public const string PartnerSuffix = "_r";

    /// <summary>
    /// 同じ種類の列がほかに無いフィルタの列ごとに、同じ型の相方の列を足す（ほかの列でパラメータを使い回す形を、どの種類の列でも
    /// 作るため）。相方の値は乱数を引かずに、元の列の 7 行先の値を写す（網羅の単位のほかのクエリと行の値の引き方を変えない）
    /// </summary>
    private static List<DslColumn> AddPartners(
        IReadOnlyList<DslColumn> columns,
        List<IReadOnlyDictionary<string, object?>> rows
    )
    {
        var filterable = columns
            .Where(column => !column.PrimaryKey || column.Clr == DslClr.GuidKey)
            .ToList();
        var partners = filterable
            .Where(column =>
                !column.PrimaryKey && filterable.Count(other => other.Clr == column.Clr) == 1
            )
            .Select(column =>
                (
                    Source: column,
                    Partner: new DslColumn(
                        column.Name + PartnerSuffix,
                        column.DataType,
                        column.Clr,
                        column.Nullable
                    )
                )
            )
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var (source, partner) in partners)
            {
                ((Dictionary<string, object?>)rows[i])[partner.Name] = rows[(i + 7) % rows.Count][
                    source.Name
                ];
            }
        }

        return [.. columns, .. partners.Select(pair => pair.Partner)];
    }

    /// <summary>網羅の単位のパラメータの使い回しと並び替えのクエリ（別の生成器のインスタンス＝ほかのクエリの値の引き方を変えない）</summary>
    private static List<DslQuery> ReuseAndOrdering(
        IReadOnlyList<DslColumn> columns,
        IReadOnlyList<DslQuery> queries,
        bool valueObjects
    ) =>
        new DslFuzzGenerator(
            CoverageSeed,
            valueObjects,
            DslFuzzGenerator.SideParameterPrefix
        ).ReuseAndOrderingCoverageQueries(columns, queries);

    /// <summary>
    /// 値オブジェクトなしの網羅の単位（方言ごとに 1 つ・種に依らない）。網羅の単位（<see cref="Coverage"/>）は値オブジェクトありだけ
    /// なので、値オブジェクトなしでだけ書ける形（整数の列と小数のリテラルの比較＝値オブジェクトの列は型の検査が断る）の
    /// 組を <see cref="DslFuzzGenerator.CoverageQueries"/> で埋める。SQLite の単位は、素の string の列・素の値型の列の経路
    /// （インメモリの文字列一致の序数への書き換え・NOT の押し下げ）の決まった形のクエリ（<see cref="DslFuzzGenerator.FixedQueries"/>）も流す
    /// </summary>
    /// <remarks>
    /// 列は種の単位と同じ。MySQL は網羅の単位と同じく、種の単位に無い固定長の文字列・符号つき整数・decimal・日時・Guid も持ち、
    /// その列の原子のクエリを作る（主キーは種の単位と同じ bigint）。
    /// 列の型に入らない値の原子を使う合成のクエリ（<see cref="DslFuzzGenerator.OutOfTypeCoverageQueries"/>）も、別の乱数の列で作って足す
    /// （列の型に入らない値は値オブジェクトなしでだけ書けるので、この単位が持つ）。
    /// </remarks>
    /// <param name="dbms">方言（<see cref="DialectNames"/> の SQLite・SQL Server・MySQL・PostgreSQL・Oracle）</param>
    public static DslFuzzUnit PlainCoverage(string dbms)
    {
        var (columns, atomColumns) = dbms switch
        {
            DialectNames.Sqlite => (SqliteColumns(), Array.Empty<string>()),
            DialectNames.SqlServer => (SqlServerColumns(), Array.Empty<string>()),
            DialectNames.MySql => (
                [.. MySqlColumns(), .. MySqlCoverageExtraColumns()],
                MySqlCoverageExtraColumns().Select(column => column.Name).ToArray()
            ),
            DialectNames.PostgreSql => (PostgreSqlColumns(), Array.Empty<string>()),
            DialectNames.Oracle => (OracleColumns(), Array.Empty<string>()),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, null),
        };
        var generator = new DslFuzzGenerator(CoverageSeed, valueObjects: false);
        var rows = generator.Rows(columns, RowCount);
        var ordinal = dbms == DialectNames.Sqlite;

        if (ordinal)
        {
            PutOrdinalValues(rows);
        }

        // 列の型に入らない値の合成のクエリは別の乱数の列で作る（ほかのクエリの値の引き方を変えない）
        List<DslQuery> queries =
        [
            .. generator.CoverageQueries(columns, atomColumns),
            .. ordinal ? generator.FixedQueries(columns, ordinalStrings: true) : [],
            .. new DslFuzzGenerator(
                DslFuzzGenerator.OutOfTypeSeed(CoverageSeed),
                valueObjects: false,
                DslFuzzGenerator.OutOfTypeParameterPrefix
            ).OutOfTypeCoverageQueries(columns, decimalOrdered: dbms != DialectNames.Sqlite),
            .. new DslFuzzGenerator(
                DslFuzzGenerator.BigintDecimalSeed(CoverageSeed),
                valueObjects: false,
                DslFuzzGenerator.BigintDecimalParameterPrefix
            ).BigintDecimalCoverageQueries(columns),
        ];
        var withPartners = AddPartners(columns, rows);

        return new DslFuzzUnit(
            $"{CoveragePrefix}{dbms}-plain",
            TableName,
            withPartners,
            rows,
            [.. queries, .. ReuseAndOrdering(withPartners, queries, valueObjects: false)],
            ValueObjects: false,
            CoverageSeed
        );
    }

    /// <summary>MySQL の網羅の単位が種の単位に足す列（種の単位に無い列の種類）</summary>
    private static DslColumn[] MySqlCoverageExtraColumns() =>
        [
            new("f", "char(10)", DslClr.FixedString, Nullable: true),
            new("i64", "bigint", DslClr.Int64, Nullable: true),
            new("i32", "int", DslClr.Int32, Nullable: true),
            new("i16", "smallint", DslClr.Int16, Nullable: true),
            new("d", "decimal(10,2)", DslClr.Decimal, Nullable: true),
            new("dt", "datetime", DslClr.DateTime, Nullable: true),
            new("g", "char(36)", DslClr.Guid, Nullable: true),
        ];

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

    /// <summary>
    /// 列の型の族（bool・バイナリ）の種の単位（値オブジェクトなし・あり）。各方言の種の単位の列挙の末尾に足す
    /// </summary>
    /// <remarks>
    /// 族の列は既存の種の単位の列に足さず、この単位だけが持つ（既存の単位の行・クエリの乱数の引き方と、種の指紋の要約の既存の行を
    /// 変えないため）。列は <see cref="FamiliesColumns"/>。
    /// </remarks>
    private static IEnumerable<DslFuzzUnit> Families(string prefix, string dbms, int seed)
    {
        yield return Build($"{prefix}-families", seed, FamiliesColumns(dbms), valueObjects: false);
        yield return Build(
            $"{prefix}-families-vo",
            seed,
            FamiliesColumns(dbms),
            valueObjects: true
        );
    }

    /// <summary>
    /// 列の型の族（bool・バイナリ）の網羅の単位（値オブジェクトあり・なし。種に依らない。名前は <c>coverage-{方言}-families</c> と
    /// <c>coverage-{方言}-families-plain</c>）：族の列の原子のクエリと <see cref="DslFuzzGenerator.CoverageQueries"/>・パラメータの
    /// 使い回しと並び替えのクエリで、族の列の種類の水準を種に依らず埋める
    /// </summary>
    /// <remarks>
    /// 既存の網羅の単位（<see cref="Coverage"/>・<see cref="PlainCoverage"/>）の列は変えない（行の乱数の引き方が変わるため）。
    /// </remarks>
    /// <param name="dbms">方言（<see cref="DialectNames"/> の SQLite・SQL Server・MySQL・PostgreSQL・Oracle）</param>
    public static IEnumerable<DslFuzzUnit> FamiliesCoverage(string dbms)
    {
        foreach (var valueObjects in new[] { true, false })
        {
            var columns = FamiliesColumns(dbms);
            var generator = new DslFuzzGenerator(CoverageSeed, valueObjects);
            var rows = generator.Rows(columns, RowCount);
            List<DslQuery> queries =
            [
                .. generator.CoverageQueries(
                    columns,
                    columns.Skip(1).Select(column => column.Name).ToArray()
                ),
            ];
            var withPartners = AddPartners(columns, rows);

            yield return new DslFuzzUnit(
                $"{CoveragePrefix}{dbms}-families{(valueObjects ? "" : "-plain")}",
                TableName,
                withPartners,
                rows,
                [.. queries, .. ReuseAndOrdering(withPartners, queries, valueObjects)],
                valueObjects,
                CoverageSeed
            );
        }
    }

    /// <summary>
    /// 列の型の族の列：主キー（bigint）・bool（NULL 許容と NOT NULL）・可変長のバイナリ（NULL 許容と NOT NULL）・固定長のバイナリ
    /// （NULL 許容。固定長を持たない PostgreSQL・Oracle は可変長）・並び替えと合成の相方の整数の列
    /// </summary>
    /// <remarks>
    /// バイナリの列の長さは 4（値の池はそれより短い）。SQL Server・MySQL の固定長の列は値を 0x00 で詰めて読み戻す（docs のバイナリの
    /// 段落）。Oracle の NOT NULL のバイナリの列の値の池は空の配列を持たない（Oracle は空のバイナリを NULL として持ち、NOT NULL の列は
    /// 断る＝文字列の空文字列と同じ。NULL を許す列は空を持ち、読み戻すと NULL になる）。
    /// </remarks>
    private static DslColumn[] FamiliesColumns(string dbms)
    {
        var (key, boolean, varbinary, fixedBinary, integer) = dbms switch
        {
            DialectNames.Sqlite => ("INTEGER", "BIT", "VARBINARY(4)", "BINARY(4)", "int"),
            DialectNames.SqlServer => ("bigint", "bit", "varbinary(4)", "binary(4)", "int"),
            DialectNames.PostgreSql => ("bigint", "boolean", "bytea", "bytea", "integer"),
            DialectNames.MySql => ("bigint", "tinyint(1)", "varbinary(4)", "binary(4)", "int"),
            DialectNames.Oracle => ("NUMBER(19)", "NUMBER(1)", "RAW(4)", "RAW(4)", "NUMBER(10)"),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, null),
        };
        return
        [
            new("id", key, DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("b", boolean, DslClr.Boolean, Nullable: true),
            new("bn", boolean, DslClr.Boolean, Nullable: false),
            new("bin", varbinary, DslClr.Binary, Nullable: true),
            new("binf", fixedBinary, DslClr.Binary, Nullable: true)
            {
                // 比べる値には列の長さを超える値も使う（超えた分が 0x00 だけの値と、そうでない値。どちらも行には書かない）
                OperandValues =
                [
                    .. DslFuzz.ValuePool(DslClr.Binary),
                    new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 },
                    new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01 },
                ],
            },
            dbms == DialectNames.Oracle
                ? new("binn", varbinary, DslClr.Binary, Nullable: false)
                {
                    Values =
                    [
                        .. DslFuzz
                            .ValuePool(DslClr.Binary)
                            .Where(value => value is not byte[] { Length: 0 }),
                    ],
                }
                : new("binn", varbinary, DslClr.Binary, Nullable: false),
            new("i32", integer, DslClr.Int32, Nullable: true),
        ];
    }

    /// <summary>
    /// TimeSpan の列の種の単位（値オブジェクトなし・あり）。各方言の種の単位の列挙の末尾に足す（Oracle は TimeSpan へ写す型を持たない）
    /// </summary>
    /// <remarks>
    /// TimeSpan の列は既存の単位（族の単位を含む）の列に足さず、この単位だけが持つ（既存の単位の行・クエリの乱数の引き方と、種の指紋の
    /// 要約の既存の行を変えないため）。列は <see cref="TimeSpanColumns"/>。
    /// </remarks>
    private static IEnumerable<DslFuzzUnit> TimeSpans(string prefix, string dbms, int seed)
    {
        yield return Build($"{prefix}-timespan", seed, TimeSpanColumns(dbms), valueObjects: false);
        yield return Build(
            $"{prefix}-timespan-vo",
            seed,
            TimeSpanColumns(dbms),
            valueObjects: true
        );
    }

    /// <summary>
    /// TimeSpan の列の網羅の単位（値オブジェクトあり・なし。種に依らない。名前は <c>coverage-{方言}-timespan</c> と
    /// <c>coverage-{方言}-timespan-plain</c>）：TimeSpan の列の原子のクエリと <see cref="DslFuzzGenerator.CoverageQueries"/>・パラメータの
    /// 使い回しと並び替えのクエリ
    /// </summary>
    /// <remarks>
    /// <paramref name="ordered"/> が偽なら、TimeSpan の列の順序比較と並び替えを作らない（<see cref="DslFuzzGenerator.TimeSpanOrdered"/>。
    /// EF Core〔SQLite〕が流さないので、SQLite の 3 実装先で流す単位はこちら）。真の SQLite の単位は名前に <c>-ordered</c> を足し、
    /// QuickER 版とインメモリだけで流す。
    /// </remarks>
    /// <param name="dbms">方言（<see cref="DialectNames"/> の SQLite・SQL Server・PostgreSQL・MySQL）</param>
    /// <param name="ordered">TimeSpan の列の順序比較と並び替えを作るか</param>
    public static IEnumerable<DslFuzzUnit> TimeSpanCoverage(string dbms, bool ordered = true)
    {
        var suffix = dbms == DialectNames.Sqlite && ordered ? "-ordered" : "";

        foreach (var valueObjects in new[] { true, false })
        {
            var columns = TimeSpanColumns(dbms);
            var generator = new DslFuzzGenerator(CoverageSeed, valueObjects)
            {
                TimeSpanOrdered = ordered,
            };
            var rows = generator.Rows(columns, RowCount);
            List<DslQuery> queries =
            [
                .. generator.CoverageQueries(
                    columns,
                    columns.Skip(1).Select(column => column.Name).ToArray()
                ),
            ];
            var withPartners = AddPartners(columns, rows);
            var side = new DslFuzzGenerator(
                CoverageSeed,
                valueObjects,
                DslFuzzGenerator.SideParameterPrefix
            )
            {
                TimeSpanOrdered = ordered,
            };

            yield return new DslFuzzUnit(
                $"{CoveragePrefix}{dbms}-timespan{suffix}{(valueObjects ? "" : "-plain")}",
                TableName,
                withPartners,
                rows,
                [.. queries, .. side.ReuseAndOrderingCoverageQueries(withPartners, queries)],
                valueObjects,
                CoverageSeed
            );
        }
    }

    /// <summary>
    /// SQL Server の time の列と、列の型の範囲（0〜24 時間未満）の外の値の網羅の単位（種に依らない・値オブジェクトなし。名前は
    /// <c>coverage-sqlserver-timespan-out-of-day</c>）：範囲の外の値と比べる原子 1 つずつのクエリ <c>KT*</c> と、それを使う合成のクエリ
    /// <c>KX*</c>
    /// </summary>
    /// <remarks>
    /// QuickER 版 Repository だけで流す。EF Core 版 Repository は範囲の外の値を送れず例外にする（docs の TimeSpan の既知の割り切り。
    /// 作らない形の宣言）。行は範囲の中の値だけ。
    /// </remarks>
    public static DslFuzzUnit SqlServerOutOfDayCoverage()
    {
        IReadOnlyList<object> operands = [.. DslFuzz.TimeOfDayTickPool, .. DslFuzz.OutOfDayPool];
        DslColumn[] columns =
        [
            new("id", "bigint", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("t", "time", DslClr.TimeSpan, Nullable: true)
            {
                Values = DslFuzz.TimeOfDayTickPool,
                OperandValues = operands,
                TimeRange = DslTimeRange.SqlServerTime,
            },
            new("tn", "time", DslClr.TimeSpan, Nullable: false)
            {
                Values = DslFuzz.TimeOfDayTickPool,
                OperandValues = operands,
                TimeRange = DslTimeRange.SqlServerTime,
            },
            // 射影の非 NULL 指定の合成のクエリが射影する、NULL を許す参照型の列
            new("bin", "varbinary(4)", DslClr.Binary, Nullable: true),
        ];
        var rows = new DslFuzzGenerator(CoverageSeed, valueObjects: false).Rows(columns, RowCount);
        var atoms = new DslFuzzGenerator(
            DslFuzzGenerator.OutOfTypeSeed(CoverageSeed),
            valueObjects: false,
            DslFuzzGenerator.OutOfTypeParameterPrefix
        ).OutOfTypeAtomQueries(columns, "KT");
        var queries = new DslFuzzGenerator(
            DslFuzzGenerator.OutOfTypeSeed(CoverageSeed),
            valueObjects: false,
            DslFuzzGenerator.OutOfTypeParameterPrefix
        ).OutOfTypeCoverageQueries(columns, decimalOrdered: true);

        return new DslFuzzUnit(
            $"{CoveragePrefix}{DialectNames.SqlServer}-timespan-out-of-day",
            TableName,
            columns,
            rows,
            [.. atoms, .. queries],
            ValueObjects: false,
            CoverageSeed
        );
    }

    /// <summary>
    /// TimeSpan の列：主キー（bigint）・TimeSpan（NULL 許容と NOT NULL。PostgreSQL は interval の列も）・射影の非 NULL 指定に使う NULL 許容の
    /// 可変長のバイナリ・並び替えと合成の相方の整数の列
    /// </summary>
    /// <remarks>
    /// 行と比べる値の池は docs の TimeSpan の既知の割り切りの写し：SQLite（文字列で持つ）と SQL Server（time は 0〜24 時間未満）は日の中だけ、
    /// PostgreSQL の time の列も日の中だけ（EF Core 版は 24 時間以上の値を日を落として書くので行に書かず、比べる値も日の中に限る）、
    /// PostgreSQL の interval の列は負の値と 1 日超の値と範囲の外の値、MySQL の time は ±838:59:59 の中の負の値と 1 日超の値（範囲の外の値は
    /// 端へ丸めて比べられるので作らない）。PostgreSQL・MySQL の値はマイクロ秒の粒度（端数の切り詰めは docs の既存の割り切り）。
    /// </remarks>
    private static DslColumn[] TimeSpanColumns(string dbms)
    {
        DslColumn Time(string name, string dataType, bool nullable) =>
            dbms switch
            {
                DialectNames.Sqlite => new(name, dataType, DslClr.TimeSpan, nullable)
                {
                    Values = DslFuzz.TimeOfDayTickPool,
                },
                DialectNames.SqlServer => new(name, dataType, DslClr.TimeSpan, nullable)
                {
                    Values = DslFuzz.TimeOfDayTickPool,
                    TimeRange = DslTimeRange.SqlServerTime,
                },
                DialectNames.PostgreSql => new(name, dataType, DslClr.TimeSpan, nullable)
                {
                    TimeRange = DslTimeRange.PostgreSqlTime,
                },
                DialectNames.MySql => new(name, dataType, DslClr.TimeSpan, nullable)
                {
                    Values = DslFuzz.WideTimeSpanPool,
                    TimeRange = DslTimeRange.MySqlTime,
                },
                _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, null),
            };

        var (key, time, varbinary, integer) = dbms switch
        {
            DialectNames.Sqlite => ("INTEGER", "TIME", "VARBINARY(4)", "int"),
            DialectNames.SqlServer => ("bigint", "time", "varbinary(4)", "int"),
            DialectNames.PostgreSql => ("bigint", "time", "bytea", "integer"),
            DialectNames.MySql => ("bigint", "time(6)", "varbinary(4)", "int"),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, null),
        };
        List<DslColumn> columns =
        [
            new("id", key, DslClr.Int64, Nullable: false, PrimaryKey: true),
            Time("t", time, nullable: true),
            Time("tn", time, nullable: false),
        ];

        if (dbms == DialectNames.PostgreSql)
        {
            columns.Add(
                new("ti", "interval", DslClr.TimeSpan, Nullable: true)
                {
                    Values = DslFuzz.WideTimeSpanPool,
                    OperandValues = [.. DslFuzz.WideTimeSpanPool.Union(DslFuzz.OutOfDayPool)],
                }
            );
        }

        columns.Add(new("bin", varbinary, DslClr.Binary, Nullable: true));
        columns.Add(new("i32", integer, DslClr.Int32, Nullable: true));

        return [.. columns];
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
        var rows = generator.Rows(columns, RowCount);
        var queries = generator.Queries(columns, Compounds);

        // パラメータを使い回すクエリと列で並べ替えるクエリは、別の乱数の列で作って末尾に足す（本流の引き方を変えない）
        var side = new DslFuzzGenerator(
            DslFuzzGenerator.SideSeed(seed),
            valueObjects,
            DslFuzzGenerator.SideParameterPrefix
        );

        // 列の型に入らない値と比べる原子は、さらに別の乱数の列で作って最後に足す（本流と追加のクエリの引き方を変えない）
        var outOfType = new DslFuzzGenerator(
            DslFuzzGenerator.OutOfTypeSeed(seed),
            valueObjects,
            DslFuzzGenerator.OutOfTypeParameterPrefix
        );

        // bigint の列と decimal のパラメータの原子は、4 本目の乱数の列で作って最後に足す（ほかの列の引き方を変えない）
        var bigintDecimal = new DslFuzzGenerator(
            DslFuzzGenerator.BigintDecimalSeed(seed),
            valueObjects,
            DslFuzzGenerator.BigintDecimalParameterPrefix
        );

        return new DslFuzzUnit(
            name,
            TableName,
            columns,
            rows,
            [
                .. queries,
                .. side.SideQueries(columns, SideReuseQueries, SideOrderingQueries),
                .. outOfType.OutOfTypeQueries(columns),
                .. bigintDecimal.BigintDecimalQueries(columns),
            ],
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

    /// <summary>
    /// SQL Server の Unicode でない文字列の列（varchar・char）。値の池は列の長さに収まり、ASCII の外の文字を含む値（<c>'</c> を含むものと
    /// 含まないもの）と、それが列の文字集合で <c>?</c> に化けた形を含む
    /// </summary>
    /// <remarks>
    /// 末尾の空白で終わる値（varchar の <c>'a '</c> など・char の列の 4 文字に満たない値は詰めた空白で返る）と、先頭の空白で始まる値を持つ。
    /// SQL Server の Unicode でない列の文字列一致は列の値の末尾の空白を数えず、パターンの空白は数える（docs の「大文字小文字・空白・日付部品」の
    /// 表の SQL Server の行。オラクルの <c>DslFuzz.MatchValue</c>）。値の池はパターンにも使うので、末尾の空白で終わるパターンも作る。
    /// </remarks>
    private static DslColumn[] SqlServerAnsiColumns() =>
        [
            new("id", "bigint", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("s", "varchar(20)", DslClr.String, Nullable: true)
            {
                NonUnicode = true,
                Values =
                [
                    "",
                    "a",
                    "A",
                    "ab",
                    "a b",
                    "a%b",
                    "_",
                    "'",
                    "??",
                    "?",
                    "a?",
                    "?'",
                    "日本",
                    "日",
                    "a日",
                    "日'",
                    "a ",
                    "a  ",
                    " a",
                    "日 ",
                ],
            },
            new("f", "char(4)", DslClr.FixedString, Nullable: true)
            {
                NonUnicode = true,
                Values =
                [
                    "",
                    "abcd",
                    "ABCD",
                    "a%bc",
                    "a_bc",
                    "'abc",
                    "????",
                    "a?bc",
                    "?'ab",
                    "日本日本",
                    "a日bc",
                    "日'ab",
                    "a",
                    "ab ",
                    " a",
                    "日",
                ],
            },
        ];

    /// <summary>
    /// PostgreSQL の列（SQL Server の列の組を PostgreSQL の型へ写したもの。PostgreSQL は 1 バイトの整数の型を持たないので byte の列は無い）
    /// </summary>
    /// <remarks>
    /// <c>money</c> の列 <c>m</c> は、型マッパーが精度なしの decimal に写す型で、EF Core が比べる値をどの型で束縛するかを試す
    /// （値の池の decimal は小数 2 桁までなので、money の列に丸めずに入る）。
    /// <c>m</c> と比べる数のリテラルは負の値を作らない（行の値・パラメータは負の値も持つ）。Npgsql の EF Core のプロバイダは負の定数を
    /// <c>-99999999.99::money</c> と書き、PostgreSQL は money の単項の負号を持たないので断る（42883。docs の「大文字小文字・空白・日付部品」の
    /// 節の既知の割り切り）。
    /// </remarks>
    private static DslColumn[] PostgreSqlColumns() =>
        [
            new("id", "bigint", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("s", "varchar(50)", DslClr.String, Nullable: true),
            new("sn", "varchar(50)", DslClr.String, Nullable: false),
            new("f", "char(10)", DslClr.FixedString, Nullable: true),
            new("i16", "smallint", DslClr.Int16, Nullable: true),
            new("i32", "integer", DslClr.Int32, Nullable: true),
            new("i32n", "integer", DslClr.Int32, Nullable: false),
            new("i64", "bigint", DslClr.Int64, Nullable: true),
            new("d", "numeric(10,2)", DslClr.Decimal, Nullable: true),
            new("dt", "timestamp", DslClr.DateTime, Nullable: true),
            new("g", "uuid", DslClr.Guid, Nullable: true),
            new("m", "money", DslClr.Decimal, Nullable: true) { NonNegativeLiterals = true },
        ];

    /// <summary>
    /// Oracle の列（SQL Server の列の組を Oracle の型へ写したもの。Oracle の型マッパーは RAW を byte[] に写すので Guid の列は無い）
    /// </summary>
    /// <remarks>
    /// NOT NULL の文字列の列 <c>sn</c> の値の池は空文字列を持たない（Oracle は空文字列を NULL として持ち、NOT NULL の列は断る＝
    /// docs の「大文字小文字・空白・日付部品」の節）。NULL を許す列は空文字列を持ち、読み戻すと NULL になる。
    /// </remarks>
    private static DslColumn[] OracleColumns() =>
        [
            new("id", "NUMBER(19)", DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("s", "NVARCHAR2(50)", DslClr.String, Nullable: true),
            new("sn", "NVARCHAR2(50)", DslClr.String, Nullable: false)
            {
                Values = [.. DslFuzz.ValuePool(DslClr.String).Where(value => !Equals(value, ""))],
            },
            new("f", "NCHAR(10)", DslClr.FixedString, Nullable: true),
            new("i8", "NUMBER(3)", DslClr.Byte, Nullable: true),
            new("i16", "NUMBER(5)", DslClr.Int16, Nullable: true),
            new("i32", "NUMBER(10)", DslClr.Int32, Nullable: true),
            new("i32n", "NUMBER(10)", DslClr.Int32, Nullable: false),
            new("i64", "NUMBER(19)", DslClr.Int64, Nullable: true),
            new("d", "NUMBER(10,2)", DslClr.Decimal, Nullable: true),
            new("dt", "TIMESTAMP", DslClr.DateTime, Nullable: true),
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
