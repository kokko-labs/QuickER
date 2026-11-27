using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.ValuePools;

namespace QuickER.Tests.TestSupport;

/// <summary>
/// 値のプールの端の網羅の単位（方言ごとに 1 つ・種に依らない・値オブジェクトなし。名前は <c>coverage-{方言}-pool-edges</c>）：
/// DateTime の Kind と翌日の 1 tick 前・整数の列と範囲の外の int64・末尾が半角の空白でない空白類の文字列・固定長のバイナリの列の長さちょうどの値
/// </summary>
/// <remarks>
/// <para>
/// 種の単位と既存の網羅の単位の列・乱数の引き方を変えないため、別の単位として持つ（種の指紋の要約の外）。クエリは原子 1 つずつで、
/// どの実装先でも照合したクエリにならなければならない（<see cref="DslFuzzCoverage.UncheckedProblems"/>）。値は値のプール
/// （<see cref="DateTimePool"/>・<see cref="IntegerPool"/>・<see cref="TextEdges"/>）から引く。
/// </para>
/// <para>
/// 日時：行の値は Kind が Unspecified で、比べる値（パラメータと IN のリストだけ）に Utc・Local の同じ tick の値と、行のある日の翌日の 1 tick 前
/// （マイクロ秒より細かい端数を持つ）を持つ。C# の DateTime の比較は Kind を見ないので、オラクルは tick の比較のまま。PostgreSQL・MySQL・Oracle の
/// 日時の列はマイクロ秒の粒度で、ドライバは細かい端数を落として送る（docs の既知の割り切り）。行の日（2024-02-29）の翌日に行の値を置かず、マイクロ秒の列には 1 tick 前の行も置かないので、
/// 端数を落としても丸めて翌日の 0 時になっても、行との答えは変わらない。100 ナノ秒の桁まで持つ列（SQLite・SQL Server の datetime2）は、1 tick 前の行も置く。
/// </para>
/// <para>
/// 整数：型トークン <c>int64</c> のパラメータだけ（リテラルは型の検査が断る）。列の型の範囲の上の外（上限 + 1）と下の外（最小 - 1）。
/// NULL を許す列の IN のリストは要素型が列の型と合わず型の検査が断る（<see cref="DslIn.RejectedByTypeCheck"/>＝生成時に警告で飛ばされることを確かめる）。NOT NULL の列は int64 のリストを暗黙変換で書ける（IN・NOT IN を実装先が答える）。
/// 固定長のバイナリの列（<c>binary(4)</c>）は SQL Server・SQLite・MySQL だけ（PostgreSQL・Oracle は固定長の型を持たない）。
/// </para>
/// </remarks>
public static class DslFuzzPoolEdges
{
    /// <summary>網羅の単位の乱数の種（種の列の外）</summary>
    private const int Seed = 0;

    /// <summary>行の数</summary>
    private const int RowCount = 24;

    /// <summary>パラメータ名の接頭辞（ほかの生成器の <c>p</c>・<c>q</c>・<c>r</c>・<c>w</c>・<c>z</c> と重ねない）</summary>
    private const string ParameterPrefix = "e";

    /// <summary>単位を作る</summary>
    /// <param name="dbms">方言（<see cref="DialectNames"/> の SQLite・SQL Server・PostgreSQL・MySQL・Oracle）</param>
    public static DslFuzzUnit Coverage(string dbms)
    {
        var columns = Columns(dbms);
        var rows = new DslFuzzGenerator(Seed, valueObjects: false).Rows(columns, RowCount);

        foreach (var column in columns.Skip(1))
        {
            PutValues(rows, column);
        }

        var names = new Names();
        var atoms = new List<DslAtom>();

        foreach (var column in columns.Skip(1))
        {
            atoms.AddRange(
                column.Clr switch
                {
                    DslClr.DateTime => DateTimeAtoms(column, names),
                    DslClr.String => TextAtoms(column, names),
                    DslClr.Binary => BinaryAtoms(column, names),
                    _ => IntegerAtoms(column, names),
                }
            );
        }

        var queries = atoms
            .Select(
                (atom, i) =>
                    new DslQuery
                    {
                        Name = $"KE{i}",
                        Condition = atom,
                        Returns = QueryReturnShape.List,
                        IsAtomQuery = true,
                    }
            )
            .ToList();

        return new DslFuzzUnit(
            $"{DslFuzzUnits.CoveragePrefix}{dbms}-pool-edges",
            DslFuzzUnits.TableName,
            columns,
            rows,
            queries,
            ValueObjects: false,
            Seed
        );
    }

    /// <summary>パラメータ名の連番</summary>
    private sealed class Names
    {
        private int _index;

        public string Next() => ParameterPrefix + _index++;
    }

    /// <summary>方言の列（主キー・日時・文字列・smallint・int・NOT NULL の int・固定長のバイナリ〔持つ方言だけ〕）</summary>
    private static List<DslColumn> Columns(string dbms)
    {
        (
            string Key,
            string Text,
            string DateTime,
            string Small,
            string Integer,
            string? FixedBinary
        ) types = dbms switch
        {
            DialectNames.Sqlite => ("INTEGER", "TEXT", "datetime", "smallint", "int", "BINARY(4)"),
            DialectNames.SqlServer => (
                "bigint",
                "nvarchar(50)",
                "datetime2",
                "smallint",
                "int",
                "binary(4)"
            ),
            DialectNames.PostgreSql => (
                "bigint",
                "varchar(50)",
                "timestamp",
                "smallint",
                "integer",
                null
            ),
            DialectNames.MySql => (
                "bigint",
                "varchar(20)",
                "datetime(6)",
                "smallint",
                "int",
                "binary(4)"
            ),
            DialectNames.Oracle => (
                "NUMBER(19)",
                "NVARCHAR2(50)",
                "TIMESTAMP",
                "NUMBER(5)",
                "NUMBER(10)",
                "RAW(4)"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, null),
        };
        var (key, text, dateTime, small, integer, fixedBinary) = types;
        var day = Day();
        var timeOfDay = DateTimePool.Named(day, DateTimePool.TimeOfDay).Value;
        // 100 ナノ秒の桁まで持つ列（SQLite の TEXT・SQL Server の datetime2）だけ、行の日の翌日の 1 tick 前を行にも置く
        // （マイクロ秒の列は端数を落として書くので同じ値で読み戻せず、行には置かない）
        List<object> dayEndRows = dbms is DialectNames.Sqlite or DialectNames.SqlServer
            ? [DateTimePool.Named(day, DateTimePool.TickBeforeMidnight).Value]
            : [];
        IReadOnlyList<object> dateTimeRows =
        [
            .. DslFuzz.ValuePool(DslClr.DateTime),
            timeOfDay,
            .. dayEndRows,
        ];
        List<DslColumn> columns =
        [
            new("id", key, DslClr.Int64, Nullable: false, PrimaryKey: true),
            new("dt", dateTime, DslClr.DateTime, Nullable: true)
            {
                Values = dateTimeRows,
                OperandValues = [.. dateTimeRows, .. DateTimeOperands(day)],
            },
            new("s", text, DslClr.String, Nullable: true) { Values = Texts() },
            new("i16", small, DslClr.Int16, Nullable: true),
            new("i32", integer, DslClr.Int32, Nullable: true),
            new("i32n", integer, DslClr.Int32, Nullable: false),
        ];

        if (fixedBinary is not null)
        {
            IReadOnlyList<object> binaryRows =
            [
                .. DslFuzz.ValuePool(DslClr.Binary),
                new byte[] { 0x01, 0x02, 0x03, 0x04 },
                new byte[] { 0x00, 0x00, 0x00, 0x00 },
                new byte[] { 0x01, 0x00, 0x00, 0x00 },
            ];
            columns.Add(
                new("binf", fixedBinary, DslClr.Binary, Nullable: true)
                {
                    Values = binaryRows,
                    // 比べる値には列の長さを超える値も使う（超えた分が 0x00 だけの値と、そうでない値。行には書かない）
                    OperandValues =
                    [
                        .. binaryRows,
                        new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 },
                        new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01 },
                    ],
                }
            );
        }

        return columns;
    }

    /// <summary>日時の列の行の日（値のプールの日時の 3 つめ＝時刻を持たない日）</summary>
    private static DateTime Day() => (DateTime)DslFuzz.ValuePool(DslClr.DateTime)[2];

    /// <summary>比べる日時：行の値と同じ tick の Kind 違い（Utc・Local）と、行の日の翌日の 1 tick 前（Unspecified・Utc・Local）</summary>
    private static IEnumerable<object> DateTimeOperands(DateTime day)
    {
        var timeOfDay = DateTimePool.Named(day, DateTimePool.TimeOfDay);
        var dayEnd = DateTimePool.Named(day, DateTimePool.TickBeforeMidnight);

        return
        [
            timeOfDay.WithKind(DateTimeKind.Utc).Value,
            timeOfDay.WithKind(DateTimeKind.Local).Value,
            dayEnd.Value,
            dayEnd.WithKind(DateTimeKind.Utc).Value,
            dayEnd.WithKind(DateTimeKind.Local).Value,
        ];
    }

    /// <summary>
    /// 文字列の列の行の値：末尾が半角の空白・全角の空白・タブの値と、空白類を途中に持つ値と、全角の英数字とそれと同じ形の半角の英数字
    /// </summary>
    private static IReadOnlyList<object> Texts() =>
        [
            "a",
            "ab",
            "a ",
            "a" + TextEdges.IdeographicSpace,
            "a" + TextEdges.Tab,
            TextEdges.IdeographicSpace,
            TextEdges.Tab,
            "a" + TextEdges.IdeographicSpace + "b",
            "a" + TextEdges.Tab + " ",
            "a" + TextEdges.IdeographicSpace + " ",
            "x" + TextEdges.FullwidthCapitalA + TextEdges.FullwidthDigitOne,
            "xA1",
        ];

    /// <summary>列の値のプールを行へ並べて入れる（NULL 許容の列は 2 行目から。1 行目の NULL は保つ）</summary>
    private static void PutValues(List<IReadOnlyDictionary<string, object?>> rows, DslColumn column)
    {
        var values = DslFuzz.ValuePool(column);
        var first = column.Nullable ? 1 : 0;

        for (var i = 0; i < values.Count && first + i < rows.Count; i++)
        {
            ((Dictionary<string, object?>)rows[first + i])[column.Name] = values[i];
        }
    }

    /// <summary>パラメータ（列で型付けするか、型トークンで型付けする）</summary>
    private static DslParam Param(
        Names names,
        DslColumn column,
        bool isList,
        string? typeToken,
        params object?[] values
    ) =>
        new(names.Next(), values, isList, ByColumn: typeToken is null, column)
        {
            TypeToken = typeToken,
        };

    /// <summary>日時の列と、Kind 違いの値・翌日の 1 tick 前の値との比較（6 つの演算子）と IN・NOT IN</summary>
    private static IEnumerable<DslAtom> DateTimeAtoms(DslColumn column, Names names)
    {
        var operands = DateTimeOperands(Day()).ToList();

        foreach (var op in Enum.GetValues<DslOp>())
        {
            foreach (var value in operands)
            {
                yield return new DslCompare(op, Param(names, column, false, null, value))
                {
                    Column = column,
                };
            }
        }

        // 型トークン datetime で型付けしたパラメータ（列で型付けしない経路）
        foreach (var value in operands)
        {
            yield return new DslCompare(
                DslOp.Eq,
                Param(names, column, false, DslFuzz.TypeToken(DslClr.DateTime), value)
            )
            {
                Column = column,
            };
        }

        var inside = DslFuzz.ValuePool(column)[^1];

        foreach (var negated in new[] { false, true })
        {
            foreach (var byColumn in new[] { true, false })
            {
                yield return new DslIn(
                    negated,
                    Param(
                        names,
                        column,
                        true,
                        byColumn ? null : DslFuzz.TypeToken(DslClr.DateTime),
                        operands[0],
                        inside,
                        operands[4]
                    )
                )
                {
                    Column = column,
                };
            }
        }
    }

    /// <summary>
    /// 整数の列と、列の型の範囲の外の int64（上の外・下の外）のパラメータとの比較（6 つの演算子）と IN・NOT IN（NULL を許す列の IN は型の検査が断る）
    /// </summary>
    private static IEnumerable<DslAtom> IntegerAtoms(DslColumn column, Names names)
    {
        (long Min, long Max, long Hit) range =
            column.Clr == DslClr.Int16
                ? (short.MinValue, short.MaxValue, 1L)
                : (int.MinValue, int.MaxValue, 5L);
        var (min, max, hit) = range;
        var above = IntegerPool.Named(hit, max, IntegerPool.BeyondColumnRange).Value;
        var below = IntegerPool.BelowTypeMinOf(min).Value;
        var token = DslFuzz.TypeToken(DslClr.Int64);

        foreach (var op in Enum.GetValues<DslOp>())
        {
            foreach (var value in new[] { above, below })
            {
                yield return new DslCompare(op, Param(names, column, false, token, value))
                {
                    Column = column,
                };
            }
        }

        foreach (var negated in new[] { false, true })
        {
            yield return new DslIn(negated, Param(names, column, true, token, above, hit))
            {
                Column = column,
                RejectedByTypeCheck = column.Nullable,
            };
            yield return new DslIn(negated, Param(names, column, true, token, below, above, hit))
            {
                Column = column,
                RejectedByTypeCheck = column.Nullable,
            };
        }
    }

    /// <summary>
    /// 文字列の列と、末尾が半角の空白でない空白類（全角の空白・タブ）の値との比較・LIKE・文字列一致・IN（それぞれ自身の否定も）
    /// </summary>
    private static IEnumerable<DslAtom> TextAtoms(DslColumn column, Names names)
    {
        var wide = "a" + TextEdges.IdeographicSpace;
        var tab = "a" + TextEdges.Tab;
        var fullwidth = "x" + TextEdges.FullwidthCapitalA + TextEdges.FullwidthDigitOne;
        string[] literals = [wide, tab, TextEdges.IdeographicSpace, TextEdges.Tab, fullwidth];

        foreach (var op in new[] { DslOp.Eq, DslOp.Ne })
        {
            foreach (var value in literals)
            {
                yield return new DslCompare(op, new DslLiteral(value, DslFuzz.QuoteText(value)))
                {
                    Column = column,
                };
            }

            foreach (var token in new string?[] { null, DslFuzz.TypeToken(DslClr.String) })
            {
                foreach (var value in new[] { wide, tab, "a" })
                {
                    yield return new DslCompare(op, Param(names, column, false, token, value))
                    {
                        Column = column,
                    };
                }
            }
        }

        string[] patterns =
        [
            wide,
            "a%",
            "%" + TextEdges.IdeographicSpace,
            "%" + TextEdges.Tab + "%",
        ];

        foreach (var negated in new[] { false, true })
        {
            foreach (var pattern in patterns)
            {
                yield return new DslLike(
                    negated,
                    new DslLiteral(pattern, DslFuzz.QuoteText(pattern))
                )
                {
                    Column = column,
                };
            }

            yield return new DslLike(negated, Param(names, column, false, null, wide))
            {
                Column = column,
            };

            foreach (var match in Enum.GetValues<DslMatchKind>())
            {
                foreach (
                    var value in new[]
                    {
                        TextEdges.IdeographicSpace,
                        TextEdges.Tab,
                        TextEdges.FullwidthCapitalA,
                    }
                )
                {
                    yield return new DslMatch(
                        match,
                        negated,
                        new DslLiteral(value, DslFuzz.QuoteText(value))
                    )
                    {
                        Column = column,
                    };
                }

                yield return new DslMatch(match, negated, Param(names, column, false, null, wide))
                {
                    Column = column,
                };
            }

            yield return new DslIn(negated, Param(names, column, true, null, wide, tab, "a"))
            {
                Column = column,
            };
            yield return new DslIn(negated, Param(names, column, true, null, fullwidth))
            {
                Column = column,
            };
        }
    }

    /// <summary>固定長のバイナリの列と、列の長さちょうどの値（と長さが違う値）との等値・不等値・IN・NOT IN</summary>
    private static IEnumerable<DslAtom> BinaryAtoms(DslColumn column, Names names)
    {
        byte[] exact = [0x01, 0x02, 0x03, 0x04];
        byte[] exactWithZeros = [0x01, 0x00, 0x00, 0x00];
        byte[] shorter = [0x01];

        foreach (var op in new[] { DslOp.Eq, DslOp.Ne })
        {
            foreach (var value in new[] { exact, exactWithZeros, shorter })
            {
                yield return new DslCompare(op, Param(names, column, false, null, value))
                {
                    Column = column,
                };
            }
        }

        foreach (var negated in new[] { false, true })
        {
            yield return new DslIn(negated, Param(names, column, true, null, exact, shorter))
            {
                Column = column,
            };
            yield return new DslIn(negated, Param(names, column, true, null, exactWithZeros, exact))
            {
                Column = column,
            };
        }
    }
}
