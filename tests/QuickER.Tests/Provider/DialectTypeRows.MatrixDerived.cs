using QuickER.Dialects;
using QuickER.Provider;
using static QuickER.Tests.Provider.DialectTypeRow;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の型の表：既存の行から導く行（上限ちょうどのパラメータの型の書き出し・解析できない表記・読めない引数・型トークンの
/// 書き出しの境界の往復）
/// </summary>
/// <remarks>
/// 解析できない表記と読めない引数の表記は、解析の行（期待 <c>fail</c>）の入力をそのまま使う（一覧を 2 回書かない）。
/// 導く元の行が 0 件になったら、名指しして断る。
/// </remarks>
internal static partial class DialectTypeRows
{
    /// <summary>
    /// 取込の表記の行に使わない解析できない表記（DB のカタログが返す値ではない。SQLite は宣言をそのまま返すので除かない）
    /// </summary>
    private static (string Dialect, string Native, string Reason)[] NotCatalogValues =>
        [
            (DialectNames.SqlServer, "no_such_type", "架空の型名"),
            (DialectNames.PostgreSql, "no_such_type", "架空の型名"),
            (DialectNames.MySql, "no_such_type", "架空の型名"),
            (DialectNames.Oracle, "no_such_type", "架空の型名"),
            (DialectNames.PostgreSql, "serial", SerialReason),
            (DialectNames.PostgreSql, "bigserial", SerialReason),
            (DialectNames.PostgreSql, "smallserial", SerialReason),
            (DialectNames.Oracle, "INTERVAL DAY TO SECOND", IntervalReason),
            (DialectNames.Oracle, "INTERVAL YEAR TO MONTH", IntervalReason),
        ];

    private const string SerialReason =
        "列を作る略記で、format_type は採番の元の整数型（integer など）を返す";

    private const string IntervalReason =
        "data_type は精度を含む形（INTERVAL DAY(2) TO SECOND(6) など）で返り、宣言の表記と違う";

    private static IEnumerable<DialectTypeRow> MatrixDerivedRows(
        IReadOnlyList<DialectTypeRow> bases
    )
    {
        var cases = bases.Select(row => (Row: row, Case: row.ToScenario().Input)).ToList();

        var atLimit = cases
            .Where(item =>
                item.Case.Facet == DialectTypeFacets.Format
                && item.Case.Boundary == DialectTypeBoundaries.AtLimit
            )
            .ToList();
        var unparsed = FailedParseInputs(cases, DialectTypeBoundaries.None);
        var invalid = FailedParseInputs(cases, DialectTypeBoundaries.InvalidArg);
        var badTokens = cases
            .Where(item =>
                item.Case.Facet == DialectTypeFacets.TokenParse
                && item.Case.Expected[DialectTypeAxis.None] == "fail"
            )
            .ToList();
        var tokenBoundaries = cases
            .Where(item =>
                item.Case.Facet == DialectTypeFacets.Format
                && item.Row.Id.StartsWith("format-token-", StringComparison.Ordinal)
                && item.Case.Boundary
                    is DialectTypeBoundaries.Lengthless
                        or DialectTypeBoundaries.NegativeScale
                        or DialectTypeBoundaries.Precisionless
            )
            .ToList();

        var staleExclusions = NotCatalogValues
            .Where(item => !unparsed.Contains((item.Dialect, item.Native)))
            .Select(item => $"{item.Dialect} の {item.Native}")
            .ToList();

        if (staleExclusions.Count > 0)
        {
            throw new InvalidOperationException(
                "取込の表記の行から除く表記が解析できない表記の一覧に無い："
                    + string.Join("、", staleExclusions)
            );
        }

        if (
            atLimit.Count == 0
            || unparsed.Count == 0
            || invalid.Count == 0
            || badTokens.Count == 0
            || tokenBoundaries.Count == 0
        )
        {
            throw new InvalidOperationException(
                $"導く元の行が無い：上限ちょうどの書き出し {atLimit.Count}・解析できない表記 {unparsed.Count}・"
                    + $"読めない引数 {invalid.Count}・読めないトークン {badTokens.Count}・型トークンの書き出しの境界 {tokenBoundaries.Count}"
            );
        }

        // 上限ちょうどのパラメータの型の書き出し（MySQL・PostgreSQL は上限を外すだけ・SQL Server・Oracle は TryFormat と同じ判定）
        foreach (var (row, item) in atLimit)
        {
            var derived = FormatParam(
                    $"matrix-format-param-at-limit-from-{row.Id}",
                    $"上限ちょうどの値はパラメータの型としても書き出せる（{row.Id} と同じ入力・同じ期待）",
                    (CanonicalType)item.Input!
                )
                .Boundary(DialectTypeBoundaries.AtLimit);

            foreach (var (dialect, spec) in item.Expected)
            {
                derived.Expect(dialect, spec);
            }

            yield return derived;
        }

        foreach (var row in MatrixUnparsedRows(unparsed))
        {
            yield return row;
        }

        foreach (var row in MatrixInvalidArgRows(invalid))
        {
            yield return row;
        }

        // 読めないトークンのパラメータは解決しない（読めない引数のトークンは境界の印を引き継ぐ）
        foreach (var (row, item) in badTokens)
        {
            var token = (string)item.Input!;
            yield return ParamType(
                    $"matrix-param-type-unparsed-from-{row.Id}",
                    $"読めないトークンのパラメータは 5 方言とも解決しない（{row.Id} と同じトークン）",
                    new ParamTypeInput([token], token)
                )
                .Boundary(item.Boundary)
                .ExpectAll("absent");
        }

        foreach (var row in MatrixTokenBoundaryRoundTripRows(tokenBoundaries))
        {
            yield return row;
        }
    }

    /// <summary>解析の行で期待が <c>fail</c> の（方言, 表記）（境界の種類で絞る・行の順・重複なし）</summary>
    private static List<(string Dialect, string Native)> FailedParseInputs(
        IEnumerable<(DialectTypeRow Row, DialectTypeCase Case)> cases,
        string boundary
    ) =>
        cases
            .Where(item =>
                item.Case.Facet == DialectTypeFacets.Parse && item.Case.Boundary == boundary
            )
            .SelectMany(item =>
                item.Case.Expected.Where(pair => pair.Value == "fail")
                    .Select(pair => (Dialect: pair.Key, Native: (string)item.Case.Input!))
            )
            .Distinct()
            .ToList();

    /// <summary>解析できない表記：表記の往復は解析で止まり、自分自身とは同じ型で、取込は名前のまま持ち帰る</summary>
    private static IEnumerable<DialectTypeRow> MatrixUnparsedRows(
        IReadOnlyList<(string Dialect, string Native)> unparsed
    )
    {
        foreach (var (dialect, native) in unparsed)
        {
            var slug = $"{dialect}-{MatrixSlug(native)}";

            yield return RoundTripText(
                    $"matrix-roundtrip-text-unparsed-{slug}",
                    "解析できない表記は、表記の往復が解析で止まる",
                    native
                )
                .Expect(dialect, "parse-fail");

            yield return SameType(
                    $"matrix-same-type-unparsed-{slug}",
                    "解析できない表記も自分自身とは同じ型",
                    native,
                    native
                )
                .Expect(dialect, "same");

            if (NotCatalogValues.Any(item => item.Dialect == dialect && item.Native == native))
            {
                continue;
            }

            yield return ImportSpelling(
                    $"matrix-import-spelling-unparsed-{slug}",
                    "解析できない型の DB のカタログの値は、取込が名前のまま持ち帰る（不動点）",
                    DialectTypeAxis.Get(dialect).CatalogValueOf(native)
                )
                .Expect(dialect, $"ok text=\"{native}\" fixed=1");
        }
    }

    /// <summary>
    /// 読めない引数の表記：表記の往復は解析で止まり、自分自身とは同じ型で別の読めない表記とは違う型で、方言切替では
    /// 変換できない列になる（同じ方言は変換しない）
    /// </summary>
    private static IEnumerable<DialectTypeRow> MatrixInvalidArgRows(
        IReadOnlyList<(string Dialect, string Native)> invalid
    )
    {
        foreach (var (dialect, native) in invalid)
        {
            var slug = $"{dialect}-{MatrixSlug(native)}";
            var sameDialect = invalid.Where(item => item.Dialect == dialect).ToList();
            var next = sameDialect[
                (sameDialect.IndexOf((dialect, native)) + 1) % sameDialect.Count
            ];

            yield return RoundTripText(
                    $"matrix-roundtrip-text-invalid-arg-{slug}",
                    "読めない引数の表記は、表記の往復が解析で止まる",
                    native
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(dialect, "parse-fail");

            yield return SameType(
                    $"matrix-same-type-invalid-arg-{slug}-itself",
                    "読めない引数の表記も自分自身とは同じ型",
                    native,
                    native
                )
                .Boundary(DialectTypeBoundaries.InvalidArg)
                .Expect(dialect, "same");

            if (next.Native != native)
            {
                yield return SameType(
                        $"matrix-same-type-invalid-arg-{slug}-next",
                        "読めない引数の表記は、同じ方言の別の読めない表記とは違う型",
                        native,
                        next.Native
                    )
                    .Boundary(DialectTypeBoundaries.InvalidArg)
                    .Expect(dialect, "different");
            }

            var convert = Convert(
                    $"matrix-convert-invalid-arg-from-{slug}",
                    "読めない引数の表記は、ほかの方言への切替で変換できない列になる（同じ方言は変換しない）",
                    new ConvertInput(dialect, native)
                )
                .Boundary(DialectTypeBoundaries.InvalidArg);

            foreach (var to in DialectNames.All)
            {
                convert.Expect(to, to == dialect ? "none" : $"unconverted old=\"{native}\"");
            }

            yield return convert;
        }
    }

    /// <summary>
    /// 型トークンの書き出しの境界（長さ無し・負のスケール・精度なし）の往復：書き出せない方言は書き出しで止まり、書き出せる方言は
    /// 宣言の表（<see cref="TokenBoundaryRoundTrips"/>）のとおり
    /// </summary>
    private static IEnumerable<DialectTypeRow> MatrixTokenBoundaryRoundTripRows(
        IReadOnlyList<(DialectTypeRow Row, DialectTypeCase Case)> bases
    )
    {
        var inputs = bases
            .Select(item =>
                (
                    Id: item.Row.Id["format-token-".Length..],
                    item.Case.Boundary,
                    Input: (CanonicalType)item.Case.Input!,
                    Refused: item.Case.Expected.Where(pair => pair.Value == "fail")
                        .Select(pair => pair.Key)
                        .ToList()
                )
            )
            .ToList();

        // 精度もスケールも無い Decimal は型トークンの書き出しの行に無いので足す（書き出さない方言は
        // src/QuickER.Provider/CLAUDE.md「TryFormat が書き出さない型」：SQL Server・MySQL）
        inputs.Add(
            (
                "precisionless-decimal",
                DialectTypeBoundaries.Precisionless,
                Ct(CanonicalTypeKind.Decimal),
                [DialectNames.SqlServer, DialectNames.MySql]
            )
        );
        var problems = new List<string>();

        foreach (var (id, boundary, input, refused) in inputs)
        {
            var documented = RoundTripCanonical(
                    $"matrix-roundtrip-canonical-{id}",
                    $"型トークンの書き出しの境界の往復（書き出せない方言は書き出しで止まる。入力 {Describe(input)}）",
                    input
                )
                .Boundary(boundary);
            var characteristic = RoundTripCanonical(
                    $"matrix-roundtrip-canonical-{id}-characteristic",
                    $"特性の固定（文書に定めが無い今の振る舞い）：型トークンの書き出しの境界の往復（入力 {Describe(input)}。"
                        + "根拠は src/QuickER.Provider.PostgreSql/PostgreSqlTypeCatalog.cs:220-235・"
                        + "src/QuickER.Provider.Sqlite/SqliteTypeCatalog.cs:391,403）",
                    input
                )
                .Boundary(boundary);
            var hasCharacteristic = false;

            foreach (var dialect in DialectNames.All)
            {
                if (refused.Contains(dialect))
                {
                    documented.Expect(dialect, "format-fail");
                    continue;
                }

                var declared = TokenBoundaryRoundTrips
                    .Where(item => item.Id == id && item.Dialect == dialect)
                    .ToList();

                if (declared.Count != 1)
                {
                    problems.Add($"{id} の {dialect} の宣言が {declared.Count} 個");
                    continue;
                }

                if (declared[0].Characteristic)
                {
                    characteristic.Expect(dialect, declared[0].Spec);
                    hasCharacteristic = true;
                }
                else
                {
                    documented.Expect(dialect, declared[0].Spec);
                }
            }

            yield return documented;

            if (hasCharacteristic)
            {
                yield return characteristic;
            }
        }

        var extra = TokenBoundaryRoundTrips
            .Where(item =>
                !inputs.Any(input => input.Id == item.Id && !input.Refused.Contains(item.Dialect))
            )
            .Select(item => $"{item.Id} の {item.Dialect}（書き出せない方言か、入力の無い宣言）");
        problems.AddRange(extra);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "型トークンの書き出しの境界の往復の宣言が整っていない："
                    + string.Join("、", problems)
            );
        }
    }

    /// <summary>
    /// 型トークンの書き出しの境界の往復の宣言（書き出せる方言だけ。<c>Characteristic</c> が真の宣言は文書に定めが無い今の振る舞い）
    /// </summary>
    /// <remarks>
    /// <para>文書に基づく宣言の出典：</para>
    /// <list type="bullet">
    ///   <item>Oracle の <c>NUMBER(*,2)</c>：src/QuickER.Provider.Oracle/CLAUDE.md「型表記」（精度なし・スケールありを書き出せるのは
    ///   Oracle だけ）と、読み戻しの行 <c>parse-oracle-number-star-scale-precisionless</c></item>
    ///   <item>負のスケール：src/QuickER.Provider/CLAUDE.md「スケールの符号」（PostgreSQL・Oracle だけが読み書きする）と、往復の行
    ///   <c>roundtrip-postgresql-numeric-negative-scale</c>・<c>roundtrip-oracle-number-negative-scale-same</c>（同じ表記へ戻る）</item>
    ///   <item>精度もスケールも無い Decimal の PostgreSQL の <c>numeric</c>・Oracle の <c>NUMBER</c>：docs/database.md の
    ///   unconvertible-types の表（「decimal without a precision (Oracle NUMBER, PostgreSQL numeric)」が両方言で ✅）と、読み戻しの行
    ///   <c>parse-postgresql-numeric-no-args</c>・<c>parse-oracle-number-star-unconstrained-decimal</c></item>
    /// </list>
    /// <para>特性の固定（文書に定めが無い今の振る舞い）の根拠の実装：</para>
    /// <list type="bullet">
    ///   <item>PostgreSQL の長さ無し：src/QuickER.Provider.PostgreSql/PostgreSqlTypeCatalog.cs:220-235（String・AnsiString は
    ///   <c>varchar</c>・固定長は <c>char</c>・バイナリは <c>bytea</c>）と :104-115（読み戻し：<c>varchar</c> は String・<c>bytea</c> は
    ///   上限なしの Binary）。Ansi の区別と固定長のバイナリは往復で消える</item>
    ///   <item>SQLite の長さ無し：src/QuickER.Provider.Sqlite/SqliteTypeCatalog.cs:391（<c>FormatLength</c> は長さ無しなら名前だけ）と
    ///   TryParseLength（引数なしは長さ無しのまま読む）</item>
    ///   <item>SQLite の精度なし：src/QuickER.Provider.Sqlite/SqliteTypeCatalog.cs:220・:403（<c>FormatPrecisionScale</c> は精度が無ければ
    ///   スケールごと書かない）と :92-93（引数なしの <c>DECIMAL</c> は精度もスケールも無い Decimal）。スケールつきは往復でスケールが消える</item>
    /// </list>
    /// </remarks>
    private static (
        string Id,
        string Dialect,
        string Spec,
        bool Characteristic
    )[] TokenBoundaryRoundTrips =>
        [
            (
                "precisionless-scaled-decimal",
                DialectNames.Oracle,
                "ok text=NUMBER(*,2) same=1",
                false
            ),
            ("precisionless-scaled-decimal", DialectNames.Sqlite, "ok text=DECIMAL same=0", true),
            ("precisionless-decimal", DialectNames.PostgreSql, "ok text=numeric same=1", false),
            ("precisionless-decimal", DialectNames.Oracle, "ok text=NUMBER same=1", false),
            ("precisionless-decimal", DialectNames.Sqlite, "ok text=DECIMAL same=1", true),
            (
                "negative-scale-decimal",
                DialectNames.PostgreSql,
                "ok text=numeric(10,-2) same=1",
                false
            ),
            ("negative-scale-decimal", DialectNames.Oracle, "ok text=NUMBER(10,-2) same=1", false),
            ("lengthless-string", DialectNames.PostgreSql, "ok text=varchar same=1", true),
            ("lengthless-string", DialectNames.Sqlite, "ok text=NVARCHAR same=1", true),
            ("lengthless-ansistring", DialectNames.PostgreSql, "ok text=varchar same=0", true),
            ("lengthless-ansistring", DialectNames.Sqlite, "ok text=VARCHAR same=1", true),
            ("lengthless-fixedstring", DialectNames.PostgreSql, "ok text=char same=1", true),
            ("lengthless-fixedstring", DialectNames.Sqlite, "ok text=NCHAR same=1", true),
            ("lengthless-ansifixedstring", DialectNames.PostgreSql, "ok text=char same=0", true),
            ("lengthless-ansifixedstring", DialectNames.Sqlite, "ok text=CHAR same=1", true),
            ("lengthless-binary", DialectNames.PostgreSql, "ok text=bytea same=0", true),
            ("lengthless-binary", DialectNames.Sqlite, "ok text=VARBINARY same=1", true),
            ("lengthless-fixedbinary", DialectNames.PostgreSql, "ok text=bytea same=0", true),
            ("lengthless-fixedbinary", DialectNames.Sqlite, "ok text=BINARY same=1", true),
        ];
}
