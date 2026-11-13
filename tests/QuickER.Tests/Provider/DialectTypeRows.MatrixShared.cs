using System.Data;
using System.Globalization;
using System.Text;
using QuickER.Dialects;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の型の表：宣言のセルから期待を導く行（<c>matrix-*</c>）が共有する部品（表記ごとの宣言・型トークンの文法の写し・
/// 種類から C# の型への表）
/// </summary>
/// <remarks>
/// <para>
/// 宣言のセルから期待を導く行は、製品が宣言どおりに動くかを確かめるだけで、宣言そのものの誤りは捕まえない（同語反復になる面が
/// ある）。宣言の正しさは docs の方言の表の照合（DialectDocsTableTests）と実 DB の表（DialectTypeRealDb）が受け持つ。
/// マッパーの行の「種類 → C# の型」の表（<see cref="ClrTypeOf"/>）は、5 方言の独立した実装どうしを同じ表へ突き合わせる。
/// </para>
/// <para>
/// 表記を入力にする観点の行は（方言, 表記）ごとに 1 行（同じ方言で同じ表記のセルが複数あれば 1 行にまとめる）。
/// 正規型・型トークンを入力にする観点の行は 1 行を 5 方言で流す。
/// </para>
/// </remarks>
internal static partial class DialectTypeRows
{
    /// <summary>（方言, 表記）ごとの宣言（同じ表記のセルの宣言は一致していなければならない）</summary>
    /// <param name="Dialect">方言</param>
    /// <param name="Native">表記</param>
    /// <param name="Parsed">表記を解析した正規型（セルの読み戻しのトークンを文法の写しで読んだもの）</param>
    /// <param name="Rewritten">解析して書き出し直した表記</param>
    /// <param name="Imported">取込の表記</param>
    /// <param name="Cells">この表記を宣言したセルの種類</param>
    private sealed record MatrixNative(
        string Dialect,
        string Native,
        CanonicalType Parsed,
        string Rewritten,
        string Imported,
        IReadOnlyList<CanonicalTypeKind> Cells
    );

    /// <summary>宣言のセルを（方言, 表記）ごとにまとめる（宣言の順。同じ表記のセルの宣言が食い違えば名指しして断る）</summary>
    private static IReadOnlyList<MatrixNative> MatrixNatives()
    {
        var natives = new List<MatrixNative>();
        var problems = new List<string>();

        foreach (
            var group in CheckedMatrixCells()
                .Where(cell => cell.Native is not null)
                .GroupBy(cell => (cell.Dialect, Native: cell.Native!))
        )
        {
            var cells = group.ToList();
            var first = cells[0];

            if (
                cells.Any(cell =>
                    cell.Reparsed != first.Reparsed
                    || cell.Rewritten != first.Rewritten
                    || cell.Imported != first.Imported
                )
            )
            {
                problems.Add($"{group.Key.Dialect} の {group.Key.Native}");
                continue;
            }

            if (first.Reparsed is null)
            {
                problems.Add(
                    $"{group.Key.Dialect} の {group.Key.Native} に読み戻しのトークンが無い"
                );
                continue;
            }

            natives.Add(
                new MatrixNative(
                    group.Key.Dialect,
                    group.Key.Native,
                    TokenShape(first.Reparsed),
                    first.Rewritten ?? group.Key.Native,
                    first.Imported ?? group.Key.Native,
                    [.. cells.Select(cell => cell.Kind)]
                )
            );
        }

        if (problems.Count > 0 || natives.Count == 0)
        {
            throw new InvalidOperationException(
                "表記ごとの宣言が整っていない（同じ表記のセルの宣言の食い違い・読み戻しの欠け・0 件）："
                    + string.Join("、", problems)
            );
        }

        return natives;
    }

    /// <summary>（方言, 種類）のセル</summary>
    private static MatrixCell CellOf(string dialect, CanonicalTypeKind kind) =>
        CheckedMatrixCells().Single(cell => cell.Dialect == dialect && cell.Kind == kind);

    /// <summary>
    /// 型トークンの文法の写し（<see cref="CanonicalTypeToken"/> の XmlDoc：種類名の小文字＋括弧の引数。長さの -1 は
    /// <c>max</c>・精度なしのスケールは <c>*</c>）で正規型を書く（製品の書き出しを入力にしないため、テストの側で組む）
    /// </summary>
    private static string GrammarToken(CanonicalType canonical)
    {
        var name = canonical.Kind.ToString().ToLowerInvariant();

        static string Num(int value) =>
            value == -1 ? "max" : value.ToString(CultureInfo.InvariantCulture);

        return canonical switch
        {
            { Length: { } length } => $"{name}({Num(length)})",
            { Precision: { } precision, Scale: { } scale } =>
                $"{name}({Num(precision)},{scale.ToString(CultureInfo.InvariantCulture)})",
            { Precision: null, Scale: { } scale } =>
                $"{name}(*,{scale.ToString(CultureInfo.InvariantCulture)})",
            { Precision: { } precision } => $"{name}({Num(precision)})",
            _ => name,
        };
    }

    /// <summary>型トークンを文法の写しで読む（宣言のセルの読み戻しのトークン用。読めなければ宣言の誤りとして断る）</summary>
    private static CanonicalType TokenShape(string token)
    {
        var (name, arguments, suffix) = DialectTypeAdapter.SplitType(token);
        var kind = Enum.GetValues<CanonicalTypeKind>()
            .Where(value => value.ToString().ToLowerInvariant() == name)
            .Cast<CanonicalTypeKind?>()
            .SingleOrDefault();

        if (kind is null || suffix.Length > 0)
        {
            throw new InvalidOperationException($"宣言のトークン {token} を読めない");
        }

        if (arguments.Count == 0)
        {
            return new CanonicalType(kind.Value);
        }

        if (CanonicalTypeToken.HasLengthArgument(kind.Value))
        {
            return new CanonicalType(kind.Value, Length: DialectTypeAdapter.Argument(arguments[0]));
        }

        if (kind == CanonicalTypeKind.Decimal)
        {
            return new CanonicalType(
                kind.Value,
                Precision: arguments[0] == "*" ? null : DialectTypeAdapter.Argument(arguments[0]),
                Scale: arguments.Count > 1 ? DialectTypeAdapter.Argument(arguments[1]) : null
            );
        }

        return new CanonicalType(kind.Value, Precision: DialectTypeAdapter.Argument(arguments[0]));
    }

    /// <summary>正規型の項目の期待（<see cref="DialectTypeRunner.CanonicalFields"/> と同じ並び）</summary>
    private static string CanonicalSpec(CanonicalType canonical) =>
        $"kind={canonical.Kind} len={SpecNum(canonical.Length)} p={SpecNum(canonical.Precision)} s={SpecNum(canonical.Scale)}";

    private static string SpecNum(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    /// <summary>
    /// 種類から C# の型への表（5 方言のマッパーが同じ表に従う。docs/code-generation.md の csharp-types の表は、型の名前だけでは
    /// 分からない例外を書く＝この表の外の写し方はしない）
    /// </summary>
    private static string ClrTypeOf(CanonicalTypeKind kind) =>
        kind switch
        {
            CanonicalTypeKind.Boolean => "bool",
            CanonicalTypeKind.TinyInt => "byte",
            CanonicalTypeKind.SmallInt => "short",
            CanonicalTypeKind.Int32 => "int",
            CanonicalTypeKind.Int64 => "long",
            CanonicalTypeKind.Decimal or CanonicalTypeKind.Money => "decimal",
            CanonicalTypeKind.Float32 => "float",
            CanonicalTypeKind.Float64 => "double",
            CanonicalTypeKind.String
            or CanonicalTypeKind.AnsiString
            or CanonicalTypeKind.FixedString
            or CanonicalTypeKind.AnsiFixedString
            or CanonicalTypeKind.Xml
            or CanonicalTypeKind.Json => "string",
            CanonicalTypeKind.Binary
            or CanonicalTypeKind.FixedBinary
            or CanonicalTypeKind.RowVersion => "byte[]",
            CanonicalTypeKind.Date or CanonicalTypeKind.DateTime => "DateTime",
            CanonicalTypeKind.Time => "TimeSpan",
            CanonicalTypeKind.DateTimeOffset => "DateTimeOffset",
            CanonicalTypeKind.Guid => "Guid",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "C# の型の表に無い種類"),
        };

    /// <summary>
    /// 宣言のセルの表記のうち、マッパーが正規型の種類の C# 型（<see cref="ClrTypeOf"/>）と違う型へ写すもの
    /// （（方言, 表記）→ C# の型）
    /// </summary>
    /// <remarks>
    /// どれも docs/code-generation.md の csharp-types の表に同じ行がある（型の名前だけでは分からない写し方）。docs の表は
    /// DialectDocsTableTests.CSharpTypesTable_MatchesTypeMappers がマッパーと独立に照合するので、ここは docs から読まずに別に持つ
    /// （読むと宣言どうしの同語反復になる）。理由はどちらも、読み取る経路（生成 EF Core のコードとそのプロバイダ）が受け取れる型。
    /// 静的フィールドにしないのは、partial の別ファイルの静的な表（Table）の初期化がこれより先に走るため（未初期化の null を読む）。
    /// </remarks>
    private static Dictionary<(string Dialect, string Native), string> MapTypeExceptions =>
        new()
        {
            // MySQL の timestamp は時間帯を持たず、両ドライバとも DateTime で返す（MySql.Data の
            // GetFieldValue<DateTimeOffset> は小数秒を落とす）。正規型は DateTimeOffset のまま（方言切替・トークンは変えない）
            [(DialectNames.MySql, "timestamp(6)")] = "DateTime",
            // MySQL の char(36) は Guid の書き出しの表記。両ドライバとも既定でこの列を Guid として返す（解析すると fixedstring(36)）
            [(DialectNames.MySql, "char(36)")] = "Guid",
        };

    /// <summary>
    /// 宣言のセルの表記のうち、生成 EF Core の DbContext が列の型（HasColumnType）を教えるもの（（方言, 表記）→ 列の型）
    /// </summary>
    /// <remarks>
    /// 正規型の種類からは導けない（PostgreSQL の timestamptz と timetz はどちらも DateTimeOffset で、前者は教えず後者は教える。
    /// json と jsonb はどちらも Json で、教える型が違う）。宣言のセルに無い表記（json・timetz・精度なしの timestamp など）は
    /// PostgreSQL のマッパーの手書きの行が見る。書けることは実 DB の表の EF Core の手順が確かめる。
    /// </remarks>
    private static Dictionary<(string Dialect, string Native), string> EfCoreColumnTypes =>
        new()
        {
            [(DialectNames.PostgreSql, "date")] = "date",
            [(DialectNames.PostgreSql, "timestamp(6)")] = "timestamp without time zone",
            [(DialectNames.PostgreSql, "xml")] = "xml",
            [(DialectNames.PostgreSql, "jsonb")] = "jsonb",
            // money・smallmoney（SQL Server）と money（PostgreSQL）は、既定の対応だと小数 4 桁を落とすか比べる演算子が無い
            [(DialectNames.SqlServer, "money")] = "money",
            [(DialectNames.PostgreSql, "money")] = "money",
        };

    /// <summary>
    /// 表記をマッパーへ通した結果の期待（解析した正規型から決まる。SqlDbType の列挙名と宣言長は SQL Server だけが持つ。
    /// C# の型の例外と EF Core に教える列の型は（方言, 表記）ごとの宣言から決まる）
    /// </summary>
    /// <param name="dialect">方言</param>
    /// <param name="native">表記</param>
    /// <param name="parsed">表記を解析した正規型</param>
    private static string MapSpec(string dialect, string native, CanonicalType parsed)
    {
        var type = MapTypeExceptions.GetValueOrDefault((dialect, native)) ?? ClrTypeOf(parsed.Kind);
        var efCoreColumnType = EfCoreColumnTypes.GetValueOrDefault((dialect, native));
        var isString = type == "string";
        var isDecimal = parsed.Kind == CanonicalTypeKind.Decimal;
        var sqlServer = dialect == DialectNames.SqlServer;
        var builder = new StringBuilder("ok");

        builder.Append($" type={type}");
        builder.Append($" ref={Bit(type is "string" or "byte[]")}");
        builder.Append($" maxlen={(isString && parsed.Length > 0 ? SpecNum(parsed.Length) : "-")}");
        builder.Append($" p={(isDecimal ? SpecNum(parsed.Precision) : "-")}");
        builder.Append($" s={(isDecimal ? SpecNum(parsed.Scale) : "-")}");
        builder.Append($" sqldb={(sqlServer ? SqlDbTypeNameOf(native) : "-")}");
        builder.Append(
            $" declen={(sqlServer ? SqlServerDeclaredLength(native) : 0).ToString(CultureInfo.InvariantCulture)}"
        );
        builder.Append(
            $" frac={(sqlServer ? SqlServerFractionalSecondsScale(native, parsed) : "-")}"
        );
        builder.Append($" rowver={Bit(parsed.Kind == CanonicalTypeKind.RowVersion)}");
        builder.Append(
            $" unbounded={Bit(parsed.Kind == CanonicalTypeKind.Binary && parsed.Length == -1)}"
        );
        builder.Append(" fallback=0");
        builder.Append(efCoreColumnType is null ? " efcol=-" : $" efcol=\"{efCoreColumnType}\"");

        return builder.ToString();
    }

    /// <summary>SQL Server の表記の SqlDbType の列挙名（型の名前と同じ名前。rowversion は同義の timestamp）</summary>
    private static string SqlDbTypeNameOf(string native)
    {
        var name = DialectTypeAdapter.SplitType(native).Name;
        name = name.Equals("rowversion", StringComparison.OrdinalIgnoreCase) ? "timestamp" : name;

        return Enum.TryParse<SqlDbType>(name, ignoreCase: true, out var value)
            ? value.ToString()
            : throw new InvalidOperationException($"{native} に同じ名前の SqlDbType が無い");
    }

    /// <summary>SQL Server の表記の宣言長（文字列・バイナリの n・max は -1・それ以外は 0）</summary>
    /// <summary>
    /// SQL Server の表記の小数秒の桁（datetime2・time・datetimeoffset は括弧の桁、指定なしは 7。ほかの型は「-」）。
    /// SqlBulkCopy へ渡す値をこの桁へ丸めるので、datetime2(0) の 0 と指定なしの 7 を区別して運ぶ
    /// </summary>
    private static string SqlServerFractionalSecondsScale(string native, CanonicalType parsed) =>
        SqlDbTypeNameOf(native) is "DateTime2" or "Time" or "DateTimeOffset"
            ? SpecNum(parsed.Precision ?? 7)
            : "-";

    private static int SqlServerDeclaredLength(string native)
    {
        var (name, arguments, _) = DialectTypeAdapter.SplitType(native);

        return
            name.ToLowerInvariant()
                is "char"
                    or "varchar"
                    or "nchar"
                    or "nvarchar"
                    or "binary"
                    or "varbinary"
            && arguments.Count == 1
            ? DialectTypeAdapter.Argument(arguments[0])
            : 0;
    }

    private static string Bit(bool value) => value ? "1" : "0";

    /// <summary>
    /// 行の Id に使う、型の表記の短い形（英小文字・数字・ハイフン。大文字を含めば <c>-upper</c>、全角などの数字は <c>fw</c>＋値）
    /// </summary>
    private static string MatrixSlug(string text)
    {
        var builder = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsDigit(c))
            {
                builder
                    .Append("-fw")
                    .Append(char.GetNumericValue(c).ToString(CultureInfo.InvariantCulture))
                    .Append('-');
            }
            else
            {
                builder.Append('-');
            }
        }

        var slug = string.Join(
            '-',
            builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries)
        );

        return (slug.Length == 0 ? "blank" : slug)
            + (text.Any(char.IsAsciiLetterUpper) ? "-upper" : string.Empty);
    }
}
