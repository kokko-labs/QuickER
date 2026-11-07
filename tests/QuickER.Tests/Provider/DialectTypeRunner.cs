using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickER.CodeGen.CSharp;
using QuickER.Model;
using QuickER.Provider;

namespace QuickER.Tests.Provider;

/// <summary>1 ケースの結果の写し（状態と、並びの決まった項目）</summary>
/// <param name="Status">状態（<c>ok</c>・<c>fail</c>・<c>converted</c> など）</param>
/// <param name="Fields">項目（キーと値。値の null は <c>-</c>、真偽は <c>0</c> / <c>1</c>）</param>
internal sealed record TypeOutcome(
    string Status,
    IReadOnlyList<KeyValuePair<string, string>> Fields
)
{
    /// <summary>項目の無い写し</summary>
    public static TypeOutcome Of(string status) => new(status, []);

    /// <summary>1 行で書く（<c>ok text="decimal(10,2)"</c>）</summary>
    public override string ToString()
    {
        var builder = new StringBuilder(Status);

        foreach (var (key, value) in Fields)
        {
            builder.Append(' ').Append(key).Append('=');
            builder.Append(
                value.Contains(' ', StringComparison.Ordinal) || value.Length == 0
                    ? $"\"{value}\""
                    : value
            );
        }

        return builder.ToString();
    }
}

/// <summary>
/// 方言の型の表の実行部：行を 1 方言で流して結果を写し、期待の写しと照合する
/// </summary>
/// <remarks>
/// <para>
/// 期待の写しの書き方：先頭の語が状態（完全一致）。続きが <c>key=value</c> の並びなら、書いた項目だけを照合する
/// （書かない項目は見ない＝元のテストが見ていた項目だけを書く）。続きが <c>key=</c> で始まらなければ、続き全体を項目
/// <c>text</c> の値として照合する（<c>ok decimal(10,2)</c>・<c>ok TIMESTAMP(6) WITH TIME ZONE</c>）。
/// 値は完全一致。空白を含む値は <c>"…"</c> で囲む。<c>like:</c> で始まる値だけは <c>*</c> を任意の文字列とするパターン
/// （元のテストが包含で見ていた値に使う。型の表記の <c>*</c>〔<c>NUMBER(*,0)</c>〕は完全一致のまま書ける）。
/// <c>not:</c> で始まる値は「その値でない」（元のテストが否定〔<c>NotBe</c>〕で見ていた値に使う）。
/// </para>
/// <para>写しに無い項目を期待に書くと、書き間違いとして赤にする。</para>
/// </remarks>
internal static class DialectTypeRunner
{
    /// <summary>行を 1 方言で流して照合し、問題を返す（無ければ空）</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言（方言なしの観点は <see cref="DialectTypeAxis.None"/>）</param>
    public static IReadOnlyList<string> Check(DialectTypeCase row, string dialect)
    {
        var problems = new List<string>();
        var outcome = Run(row, dialect);
        var spec = row.Expected[dialect];

        if (row.Facet == DialectTypeFacets.RoundTripText && spec == "same")
        {
            spec = $"ok {(string)row.Input!}";
        }

        problems.AddRange(Match(spec, outcome));

        // 元より強い自動の照合（期待の写しとは別の表明）：正規化の冪等と同期の能力の結線
        if (row.Facet == DialectTypeFacets.Normalize)
        {
            var adapter = DialectTypeAxis.Get(dialect);
            var input = (string)row.Input!;
            var once = adapter.Normalize(input);
            var twice = adapter.Normalize(once);

            if (twice != once)
            {
                problems.Add(
                    $"[自動・冪等] Normalize(\"{once}\") が \"{twice}\" になった（取込の表記は不動点）"
                );
            }

            var wired = adapter.Provider.SyncCapabilities.NormalizeTypeNotation(input);

            if (wired != once)
            {
                problems.Add(
                    $"[自動・結線] 同期の能力の NormalizeTypeNotation が \"{wired}\" を返した（方言の Normalize は \"{once}\"）"
                );
            }
        }

        return problems;
    }

    /// <summary>行を 1 方言で流して結果を写す</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言（方言なしの観点は <see cref="DialectTypeAxis.None"/>）</param>
    public static TypeOutcome Run(DialectTypeCase row, string dialect)
    {
        if (!DialectTypeFacets.All.Contains(row.Facet, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"知らない観点 {row.Facet}");
        }

        var adapter = dialect == DialectTypeAxis.None ? null : DialectTypeAxis.Get(dialect);

        // 製品が投げた例外は結果の写し（throws）にする（期待していない例外は状態の食い違いで赤になる）
        try
        {
            return row.Facet switch
            {
                DialectTypeFacets.Parse => ParseOutcome(adapter!.Catalog, (string)row.Input!),
                DialectTypeFacets.Format => FormatOutcome(
                    adapter!.Catalog.TryFormat((CanonicalType)row.Input!, out var native),
                    native
                ),
                DialectTypeFacets.FormatParam => FormatOutcome(
                    adapter!.Catalog.TryFormatParameterType(
                        (CanonicalType)row.Input!,
                        out var parameter
                    ),
                    parameter
                ),
                DialectTypeFacets.RoundTripText => RoundTripText(
                    adapter!.Catalog,
                    (string)row.Input!
                ),
                DialectTypeFacets.RoundTripCanonical => RoundTripCanonical(
                    adapter!.Catalog,
                    (CanonicalType)row.Input!
                ),
                DialectTypeFacets.Catalog => new TypeOutcome(
                    "ok",
                    [
                        new("default", adapter!.Catalog.DefaultDataType),
                        new("all-safe", Bit(adapter.Catalog.DataTypes.All(SqlTypeText.IsSafe))),
                    ]
                ),
                DialectTypeFacets.Normalize => NormalizeOutcome(adapter!, (string)row.Input!),
                DialectTypeFacets.SameType => SameType(adapter!, (SameTypeInput)row.Input!),
                DialectTypeFacets.ImportSpelling => ImportSpelling(
                    adapter!,
                    (ImportedType)row.Input!
                ),
                DialectTypeFacets.Map => new TypeOutcome(
                    "ok",
                    MapFields(adapter!.Map((string)row.Input!))
                ),
                DialectTypeFacets.Convert => Convert(adapter!, (ConvertInput)row.Input!),
                DialectTypeFacets.ParamType => ParamType(adapter!, (ParamTypeInput)row.Input!),
                DialectTypeFacets.TokenFormat => TokenFormat((CanonicalType)row.Input!),
                DialectTypeFacets.TokenParse => CanonicalTypeToken.TryParse(
                    (string)row.Input!,
                    out var token
                )
                    ? new TypeOutcome("ok", CanonicalFields(token))
                    : TypeOutcome.Of("fail"),
                DialectTypeFacets.SafeText => TypeOutcome.Of(
                    SqlTypeText.IsSafe((string?)row.Input) ? "safe" : "unsafe"
                ),
                _ => throw new UnreachableException(row.Facet),
            };
        }
        catch (Exception ex) when (ex is not UnreachableException)
        {
            return new TypeOutcome(
                "throws",
                [new("type", ex.GetType().Name), new("message", FirstLine(ex.Message))]
            );
        }
    }

    /// <summary>網羅の計数に数える正規型の種類（行が明示していなければ入力から決める）</summary>
    /// <param name="row">行</param>
    /// <param name="dialect">方言</param>
    /// <returns>種類の名前・解析できない表記は <c>unparsed</c>・種類を持たない観点は <c>-</c></returns>
    public static string ObservedKind(DialectTypeCase row, string dialect)
    {
        if (row.Kind is { } declared)
        {
            return declared.ToString();
        }

        var adapter = dialect == DialectTypeAxis.None ? null : DialectTypeAxis.Get(dialect);

        return row.Facet switch
        {
            DialectTypeFacets.Format
            or DialectTypeFacets.FormatParam
            or DialectTypeFacets.RoundTripCanonical
            or DialectTypeFacets.TokenFormat => ((CanonicalType)row.Input!).Kind.ToString(),
            DialectTypeFacets.Parse
            or DialectTypeFacets.RoundTripText
            or DialectTypeFacets.Normalize
            or DialectTypeFacets.Map => KindOf(adapter!.Catalog, (string)row.Input!),
            DialectTypeFacets.SameType => KindOf(
                adapter!.Catalog,
                ((SameTypeInput)row.Input!).Left
            ),
            DialectTypeFacets.ImportSpelling => KindOf(
                adapter!.Catalog,
                adapter.ImportedSpelling((ImportedType)row.Input!)
            ),
            DialectTypeFacets.Convert => KindOf(
                DialectTypeAxis.Get(((ConvertInput)row.Input!).FromDialect).Catalog,
                ((ConvertInput)row.Input!).DataType
            ),
            DialectTypeFacets.ParamType => CanonicalTypeToken.TryParse(
                ((ParamTypeInput)row.Input!).Lookup,
                out var parameter
            )
                ? parameter.Kind.ToString()
                : DialectTypeDimensions.Unparsed,
            DialectTypeFacets.TokenParse => CanonicalTypeToken.TryParse(
                (string)row.Input!,
                out var token
            )
                ? token.Kind.ToString()
                : DialectTypeDimensions.Unparsed,
            _ => DialectTypeDimensions.NoKind,
        };
    }

    /// <summary>期待の写しと結果を照合する（問題を返す・無ければ空）</summary>
    /// <param name="spec">期待の写し</param>
    /// <param name="outcome">結果</param>
    public static IReadOnlyList<string> Match(string spec, TypeOutcome outcome)
    {
        var (status, fields) = ParseSpec(spec);
        var problems = new List<string>();

        if (status != outcome.Status)
        {
            problems.Add($"状態が違う：期待 {spec} ／ 結果 {outcome}");
            return problems;
        }

        foreach (var (key, expected) in fields)
        {
            var actual = outcome
                .Fields.Where(field => field.Key == key)
                .Select(field => field.Value)
                .ToList();

            if (actual.Count == 0)
            {
                problems.Add(
                    $"写しに無い項目 {key} を期待している（書き間違い）：期待 {spec} ／ 結果 {outcome}"
                );
                continue;
            }

            if (!ValueMatches(expected, actual[0]))
            {
                problems.Add(
                    $"{key} が違う：期待 {expected} ／ 結果 {actual[0]}（全体 {outcome}）"
                );
            }
        }

        return problems;
    }

    /// <summary>期待の写しを状態と項目に分ける</summary>
    private static (string Status, IReadOnlyList<KeyValuePair<string, string>> Fields) ParseSpec(
        string spec
    )
    {
        var space = spec.IndexOf(' ', StringComparison.Ordinal);

        if (space < 0)
        {
            return (spec, []);
        }

        var status = spec[..space];
        var rest = spec[(space + 1)..];

        if (!StartsWithKey(rest))
        {
            return (status, [new("text", rest)]);
        }

        var fields = new List<KeyValuePair<string, string>>();
        var i = 0;

        while (i < rest.Length)
        {
            while (i < rest.Length && rest[i] == ' ')
            {
                i++;
            }

            if (i >= rest.Length)
            {
                break;
            }

            var equals = rest.IndexOf('=', i);

            if (equals < 0)
            {
                throw new ArgumentException($"期待の写しの項目が key=value でない：{spec}");
            }

            var key = rest[i..equals];
            i = equals + 1;
            string value;

            if (i < rest.Length && rest[i] == '"')
            {
                var close = rest.IndexOf('"', i + 1);

                if (close < 0)
                {
                    throw new ArgumentException($"期待の写しの引用符が閉じていない：{spec}");
                }

                value = rest[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                var end = rest.IndexOf(' ', i);
                end = end < 0 ? rest.Length : end;
                value = rest[i..end];
                i = end;
            }

            fields.Add(new(key, value));
        }

        return (status, fields);
    }

    private static bool StartsWithKey(string rest)
    {
        var equals = rest.IndexOf('=', StringComparison.Ordinal);

        if (equals <= 0)
        {
            return false;
        }

        return rest[..equals].All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    }

    private static bool ValueMatches(string expected, string actual)
    {
        if (expected.StartsWith("not:", StringComparison.Ordinal))
        {
            return actual != expected["not:".Length..];
        }

        if (!expected.StartsWith("like:", StringComparison.Ordinal))
        {
            return expected == actual;
        }

        var pattern =
            "^"
            + string.Join(".*", expected["like:".Length..].Split('*').Select(Regex.Escape))
            + "$";

        return Regex.IsMatch(
            actual,
            pattern,
            RegexOptions.Singleline | RegexOptions.CultureInvariant
        );
    }

    private static TypeOutcome ParseOutcome(ITypeCatalog catalog, string nativeType) =>
        catalog.TryParse(nativeType, out var canonical)
            ? new TypeOutcome("ok", CanonicalFields(canonical))
            : TypeOutcome.Of("fail");

    private static TypeOutcome FormatOutcome(bool formatted, string native) =>
        formatted ? new TypeOutcome("ok", [new("text", native)]) : TypeOutcome.Of("fail");

    private static TypeOutcome RoundTripText(ITypeCatalog catalog, string nativeType)
    {
        if (!catalog.TryParse(nativeType, out var canonical))
        {
            return TypeOutcome.Of("parse-fail");
        }

        return catalog.TryFormat(canonical, out var native)
            ? new TypeOutcome("ok", [new("text", native)])
            : new TypeOutcome("format-fail", CanonicalFields(canonical));
    }

    private static TypeOutcome RoundTripCanonical(ITypeCatalog catalog, CanonicalType canonical)
    {
        if (!catalog.TryFormat(canonical, out var native))
        {
            return TypeOutcome.Of("format-fail");
        }

        if (!catalog.TryParse(native, out var back))
        {
            return new TypeOutcome("parse-fail", [new("text", native)]);
        }

        return new TypeOutcome(
            "ok",
            [
                new("text", native),
                new("same", Bit(back == canonical)),
                new("back", CanonicalTypeToken.Format(back)),
                .. CanonicalFields(back),
            ]
        );
    }

    /// <summary>正規化の結果（<c>text</c>）と、正規化した表記を解析した種類（<c>kind</c>。解析できなければ <c>unparsed</c>）</summary>
    private static TypeOutcome NormalizeOutcome(DialectTypeAdapter adapter, string nativeType)
    {
        var text = adapter.Normalize(nativeType);

        return new TypeOutcome(
            "ok",
            [new("text", text), new("kind", KindOf(adapter.Catalog, text))]
        );
    }

    private static TypeOutcome SameType(DialectTypeAdapter adapter, SameTypeInput input) =>
        TypeOutcome.Of(
            adapter.Provider.SyncCapabilities.AreSameType(input.Left, input.Right)
                ? "same"
                : "different"
        );

    private static TypeOutcome ImportSpelling(DialectTypeAdapter adapter, ImportedType imported)
    {
        var text = adapter.ImportedSpelling(imported);

        return new TypeOutcome(
            "ok",
            [
                new("text", text),
                new("fixed", Bit(adapter.Normalize(text) == text)),
                new("normalize", Bit(text == adapter.Normalize(imported.Name))),
            ]
        );
    }

    private static TypeOutcome Convert(DialectTypeAdapter to, ConvertInput input)
    {
        var from = DialectTypeAxis.Get(input.FromDialect);
        var column = new Column
        {
            Name = "c",
            DataType = input.DataType,
            IsNullable = input.IsNullable,
            IsPrimaryKey = input.IsPrimaryKey,
        };
        var entity = new Entity { TableName = "t" };
        entity.Columns.Add(column);
        var diagram = new ErDiagram { Entities = { entity } };
        var plan = DiagramTypeConverter.CreatePlan(diagram, from.Catalog, to.Catalog);
        var converted = plan.Converted.SingleOrDefault(item => item.ColumnId == column.Id);
        var unconverted = plan.Unconverted.SingleOrDefault(item => item.ColumnId == column.Id);
        var fields = new List<KeyValuePair<string, string>>();
        string status;

        if (converted is not null)
        {
            status = "converted";
            fields.Add(new("type", converted.NewType ?? "-"));
            fields.Add(new("nullable", Bit(converted.MakeNullable)));
            fields.Add(new("capped", Bit(converted.PrecisionCapped)));
        }
        else if (unconverted is not null)
        {
            status = "unconverted";
            fields.Add(new("old", unconverted.OldType));
        }
        else
        {
            status = "none";
        }

        if (input.BackTo is not null && converted?.NewType is { } intermediate)
        {
            var back = Convert(
                DialectTypeAxis.Get(input.BackTo),
                new ConvertInput(to.AxisValue, intermediate)
            );
            fields.Add(new("back", back.Status));
            fields.Add(
                new(
                    "back-type",
                    back.Fields.FirstOrDefault(field => field.Key == "type").Value ?? "-"
                )
            );
        }

        if (input.Apply)
        {
            DiagramTypeConverter.Apply(diagram, plan);
            fields.Add(new("applied-type", column.DataType));
            fields.Add(new("applied-null", Bit(column.IsNullable)));
        }

        return new TypeOutcome(status, fields);
    }

    private static TypeOutcome ParamType(DialectTypeAdapter adapter, ParamTypeInput input)
    {
        var entity = new Entity { TableName = "docs" };
        entity.Columns.Add(
            new Column
            {
                Name = "id",
                DataType = "int",
                IsPrimaryKey = true,
                IsNullable = false,
            }
        );
        var diagram = new ErDiagram { Entities = { entity } };

        for (var i = 0; i < input.Tokens.Count; i++)
        {
            diagram.Queries.Add(
                new QueryDefinition
                {
                    Name = $"Q{i.ToString(CultureInfo.InvariantCulture)}",
                    EntityId = entity.Id,
                    Parameters =
                    {
                        new QueryParameter { Name = "p", Type = input.Tokens[i] },
                    },
                }
            );
        }

        var resolved = QueryParameterTypeResolver.Resolve(
            diagram,
            adapter.Provider.TypeMapper,
            adapter.Catalog
        );

        return resolved.TryGetValue(input.Lookup, out var info)
            ? new TypeOutcome("ok", MapFields(info))
            : TypeOutcome.Of("absent");
    }

    private static TypeOutcome TokenFormat(CanonicalType canonical)
    {
        var text = CanonicalTypeToken.Format(canonical);
        var roundTrip = CanonicalTypeToken.TryParse(text, out var back) && back == canonical;

        return new TypeOutcome("ok", [new("text", text), new("roundtrip", Bit(roundTrip))]);
    }

    /// <summary>正規型の項目（kind・len・p・s）</summary>
    internal static List<KeyValuePair<string, string>> CanonicalFields(CanonicalType canonical) =>
        [
            new("kind", canonical.Kind.ToString()),
            new("len", Num(canonical.Length)),
            new("p", Num(canonical.Precision)),
            new("s", Num(canonical.Scale)),
        ];

    /// <summary>マッパーの結果の項目（並びは固定）</summary>
    internal static List<KeyValuePair<string, string>> MapFields(CSharpTypeInfo info) =>
        [
            new("type", info.TypeName),
            new("ref", Bit(info.IsReferenceType)),
            new("maxlen", Num(info.MaxLength)),
            new("p", Num(info.Precision)),
            new("s", Num(info.Scale)),
            new("sqldb", info.SqlDbTypeName ?? "-"),
            new("declen", Num(info.SqlDeclaredLength)),
            new("rowver", Bit(info.IsRowVersion)),
            new("unbounded", Bit(info.IsUnboundedBinary)),
            new("token", info.CanonicalTypeToken ?? "-"),
            new("verbatim", info.VerbatimDbType ?? "-"),
            new("rt", info.CanonicalRoundTripDbType ?? "-"),
            new("fallback", Bit(info.IsFallbackType)),
            new("array", Bit(info.IsArray)),
            new("nonunicode", Bit(info.IsNonUnicodeString)),
            new("fixed", Bit(info.IsFixedLengthString)),
            new("textin", Bit(info.IsTextInputUnsupported)),
            new("excluded", Bit(info.IsExcludedFromEditModel)),
        ];

    private static string KindOf(ITypeCatalog catalog, string nativeType) =>
        catalog.TryParse(nativeType, out var canonical)
            ? canonical.Kind.ToString()
            : DialectTypeDimensions.Unparsed;

    private static string Bit(bool value) => value ? "1" : "0";

    private static string Num(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FirstLine(string text)
    {
        var newline = text.IndexOfAny(['\r', '\n']);
        return newline < 0 ? text : text[..newline];
    }
}
