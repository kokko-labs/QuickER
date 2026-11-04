using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>方言の型の表の観点（行が何を呼んで何を写すか）</summary>
internal static class DialectTypeFacets
{
    /// <summary><see cref="ITypeCatalog.TryParse"/>：型の表記 → <c>fail</c> か <c>ok kind= len= p= s=</c></summary>
    public const string Parse = "parse";

    /// <summary><see cref="ITypeCatalog.TryFormat"/>：正規型 → <c>fail</c> か <c>ok {表記}</c></summary>
    public const string Format = "format";

    /// <summary><see cref="ITypeCatalog.TryFormatParameterType"/>：正規型 → <c>fail</c> か <c>ok {表記}</c></summary>
    public const string FormatParam = "format-param";

    /// <summary>型の表記 → 解析 → 書き出し：<c>parse-fail</c>・<c>format-fail</c> か <c>ok {表記}</c>（期待 <c>same</c> は元の表記）</summary>
    public const string RoundTripText = "roundtrip-text";

    /// <summary>正規型 → 書き出し → 解析：<c>format-fail</c>・<c>parse-fail text=</c> か <c>ok text= same=</c></summary>
    public const string RoundTripCanonical = "roundtrip-canonical";

    /// <summary>型カタログの方言ごとの事実（入力なし）：<c>ok default= all-safe=</c></summary>
    public const string Catalog = "catalog";

    /// <summary>取込の表記への正規化：型の表記 → <c>ok {表記}</c>（冪等と能力の結線は全行で自動に照合する）</summary>
    public const string Normalize = "normalize";

    /// <summary>同期の能力の <see cref="SyncDialectCapabilities.AreSameType"/>：表記の組 → <c>same</c> か <c>different</c></summary>
    public const string SameType = "same-type";

    /// <summary>取込が DB のカタログから組む表記：<see cref="ImportedType"/> → <c>ok text= fixed= normalize=</c></summary>
    public const string ImportSpelling = "import-spelling";

    /// <summary>型マッパー：型の表記 → <c>ok type= ref= …</c>（<see cref="DialectTypeRunner"/> の写しの項目）</summary>
    public const string Map = "map";

    /// <summary>方言切替（軸の値は移行先）：<see cref="ConvertInput"/> → <c>converted type= nullable= capped=</c>・<c>unconverted</c>・<c>none</c></summary>
    public const string Convert = "convert";

    /// <summary>クエリのパラメータの型解決：<see cref="ParamTypeInput"/> → <c>absent</c> か <c>ok</c>＋マッパーの項目</summary>
    public const string ParamType = "param-type";

    /// <summary>型トークンの書き出し（方言なし）：正規型 → <c>ok text= roundtrip=</c></summary>
    public const string TokenFormat = "token-format";

    /// <summary>型トークンの解析（方言なし）：トークン → <c>fail</c> か <c>ok kind= len= p= s=</c></summary>
    public const string TokenParse = "token-parse";

    /// <summary>型の表記の安全性（方言なし）：<see cref="SqlTypeText.IsSafe"/> → <c>safe</c> か <c>unsafe</c></summary>
    public const string SafeText = "safe-text";

    /// <summary>方言を持たない観点</summary>
    public static IReadOnlyList<string> Neutral { get; } = [TokenFormat, TokenParse, SafeText];

    /// <summary>すべての観点（次元表の水準の並び）</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Parse,
        Format,
        FormatParam,
        RoundTripText,
        RoundTripCanonical,
        Catalog,
        Normalize,
        SameType,
        ImportSpelling,
        Map,
        Convert,
        ParamType,
        TokenFormat,
        TokenParse,
        SafeText,
    ];
}

/// <summary>境界の種類（上限の「ちょうど」と「1 つ超え」は対で書く）</summary>
internal static class DialectTypeBoundaries
{
    /// <summary>境界ではない</summary>
    public const string None = "none";

    /// <summary>方言の上限ちょうど（書き出せる）</summary>
    public const string AtLimit = "at-limit";

    /// <summary>方言の上限を 1 つ超えた値（書き出さない）</summary>
    public const string OverLimit = "over-limit";

    /// <summary>長さを要する種類の長さ無し</summary>
    public const string Lengthless = "lengthless";

    /// <summary>負のスケール</summary>
    public const string NegativeScale = "negative-scale";

    /// <summary>精度なしの Decimal</summary>
    public const string Precisionless = "precisionless";

    /// <summary>読めない引数（負・int の範囲外・全角の数字）</summary>
    public const string InvalidArg = "invalid-arg";

    /// <summary>すべての水準</summary>
    public static IReadOnlyList<string> All { get; } =
    [None, AtLimit, OverLimit, Lengthless, NegativeScale, Precisionless, InvalidArg];
}

/// <summary>方言切替の行の入力（軸の値＝移行先の方言）</summary>
/// <param name="FromDialect">移行元の方言</param>
/// <param name="DataType">列の型の表記</param>
/// <param name="IsNullable">列が NULL を許すか</param>
/// <param name="IsPrimaryKey">列が主キーか</param>
/// <param name="Apply">計画を図へ当てて、当てた後の列（<c>applied-type=</c>・<c>applied-null=</c>）も写すか</param>
/// <param name="BackTo">
/// 変換した型をさらにこの方言へ変換し戻す（往復。1 段目の出力をそのまま 2 段目の入力にし、<c>back=</c>〔2 段目の状態〕・
/// <c>back-type=</c> を写す）。null なら戻さない
/// </param>
internal sealed record ConvertInput(
    string FromDialect,
    string DataType,
    bool IsNullable = true,
    bool IsPrimaryKey = false,
    bool Apply = false,
    string? BackTo = null
);

/// <summary>クエリのパラメータの型解決の行の入力</summary>
/// <param name="Tokens">図のクエリに並べるパラメータの型トークン（この順で 1 クエリずつ）</param>
/// <param name="Lookup">結果から引くトークン</param>
internal sealed record ParamTypeInput(IReadOnlyList<string> Tokens, string Lookup);

/// <summary>同期の能力で比べる型の表記の組</summary>
/// <param name="Left">左</param>
/// <param name="Right">右</param>
internal sealed record SameTypeInput(string Left, string Right);

/// <summary>方言の型の表の 1 行の中身</summary>
/// <param name="Facet">観点（<see cref="DialectTypeFacets"/>）</param>
/// <param name="Boundary">境界の種類（<see cref="DialectTypeBoundaries"/>）</param>
/// <param name="Input">入力（観点ごとの型：表記の文字列・<see cref="CanonicalType"/>・<see cref="SameTypeInput"/> など）</param>
/// <param name="Expected">方言（方言なしの観点は <see cref="DialectTypeAxis.None"/>）ごとの期待の写し</param>
/// <param name="Kind">網羅の計数に数える正規型の種類（null なら実行部が入力から決める）</param>
internal sealed record DialectTypeCase(
    string Facet,
    string Boundary,
    object? Input,
    IReadOnlyDictionary<string, string> Expected,
    CanonicalTypeKind? Kind
)
{
    /// <summary>入力を 1 行で書く（失敗の文と一覧に使う）</summary>
    public string DescribeInput() =>
        Input switch
        {
            null => "(なし)",
            string text => $"\"{text}\"",
            CanonicalType canonical => DialectTypeRow.Describe(canonical),
            _ => Input.ToString() ?? "(null)",
        };
}

/// <summary>方言の型の表の行の組み立て（主題ごとの行のファイルが使う）</summary>
/// <remarks>
/// 期待の写しの書き方は <see cref="DialectTypeRunner"/> を参照する。期待は元のテストが見ていた項目だけを書く。
/// </remarks>
internal sealed class DialectTypeRow
{
    private readonly Dictionary<string, string> _expected = new(StringComparer.Ordinal);
    private string _boundary = DialectTypeBoundaries.None;
    private CanonicalTypeKind? _kind;

    private DialectTypeRow(string id, string description, string facet, object? input)
    {
        Id = id;
        Description = description;
        Facet = facet;
        Input = input;
    }

    /// <summary>行の Id</summary>
    public string Id { get; }

    /// <summary>説明（元のテストの DisplayName）</summary>
    public string Description { get; }

    /// <summary>観点</summary>
    public string Facet { get; }

    /// <summary>入力</summary>
    public object? Input { get; }

    /// <summary>型の表記を解析する行</summary>
    public static DialectTypeRow Parse(string id, string description, string nativeType) =>
        new(id, description, DialectTypeFacets.Parse, nativeType);

    /// <summary>正規型を書き出す行</summary>
    public static DialectTypeRow Format(string id, string description, CanonicalType canonical) =>
        new(id, description, DialectTypeFacets.Format, canonical);

    /// <summary>正規型をパラメータの型として書き出す行</summary>
    public static DialectTypeRow FormatParam(
        string id,
        string description,
        CanonicalType canonical
    ) => new(id, description, DialectTypeFacets.FormatParam, canonical);

    /// <summary>型の表記を解析して書き出し直す行</summary>
    public static DialectTypeRow RoundTripText(string id, string description, string nativeType) =>
        new(id, description, DialectTypeFacets.RoundTripText, nativeType);

    /// <summary>正規型を書き出して解析し直す行</summary>
    public static DialectTypeRow RoundTripCanonical(
        string id,
        string description,
        CanonicalType canonical
    ) => new(id, description, DialectTypeFacets.RoundTripCanonical, canonical);

    /// <summary>型カタログの方言ごとの事実の行</summary>
    public static DialectTypeRow Catalog(string id, string description) =>
        new(id, description, DialectTypeFacets.Catalog, null);

    /// <summary>取込の表記への正規化の行</summary>
    public static DialectTypeRow Normalize(string id, string description, string nativeType) =>
        new(id, description, DialectTypeFacets.Normalize, nativeType);

    /// <summary>同期の能力で同じ型かを比べる行</summary>
    public static DialectTypeRow SameType(
        string id,
        string description,
        string left,
        string right
    ) => new(id, description, DialectTypeFacets.SameType, new SameTypeInput(left, right));

    /// <summary>取込が DB のカタログから組む表記の行</summary>
    public static DialectTypeRow ImportSpelling(
        string id,
        string description,
        ImportedType imported
    ) => new(id, description, DialectTypeFacets.ImportSpelling, imported);

    /// <summary>型マッパーの行</summary>
    public static DialectTypeRow Map(string id, string description, string nativeType) =>
        new(id, description, DialectTypeFacets.Map, nativeType);

    /// <summary>方言切替の行（期待の方言＝移行先）</summary>
    public static DialectTypeRow Convert(string id, string description, ConvertInput input) =>
        new(id, description, DialectTypeFacets.Convert, input);

    /// <summary>クエリのパラメータの型解決の行</summary>
    public static DialectTypeRow ParamType(string id, string description, ParamTypeInput input) =>
        new(id, description, DialectTypeFacets.ParamType, input);

    /// <summary>型トークンを書き出す行（方言なし）</summary>
    public static DialectTypeRow TokenFormat(
        string id,
        string description,
        CanonicalType canonical
    ) => new(id, description, DialectTypeFacets.TokenFormat, canonical);

    /// <summary>型トークンを解析する行（方言なし）</summary>
    public static DialectTypeRow TokenParse(string id, string description, string token) =>
        new(id, description, DialectTypeFacets.TokenParse, token);

    /// <summary>型の表記の安全性の行（方言なし）</summary>
    public static DialectTypeRow SafeText(string id, string description, string? dataType) =>
        new(id, description, DialectTypeFacets.SafeText, dataType);

    /// <summary>正規型を短く作る</summary>
    public static CanonicalType Ct(
        CanonicalTypeKind kind,
        int? length = null,
        int? precision = null,
        int? scale = null
    ) => new(kind, length, precision, scale);

    /// <summary>正規型を 1 行で書く（<c>Decimal(p=10,s=2)</c>）</summary>
    public static string Describe(CanonicalType canonical)
    {
        var parts = new List<string>();

        if (canonical.Length is { } length)
        {
            parts.Add($"len={length.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (canonical.Precision is { } precision)
        {
            parts.Add($"p={precision.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        if (canonical.Scale is { } scale)
        {
            parts.Add($"s={scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return parts.Count == 0
            ? canonical.Kind.ToString()
            : $"{canonical.Kind}({string.Join(",", parts)})";
    }

    /// <summary>方言の期待を書く（方言なしの観点は <see cref="Expect(string)"/>）</summary>
    /// <param name="dialect">方言（<see cref="DialectNames"/> の定数）</param>
    /// <param name="spec">期待の写し</param>
    public DialectTypeRow Expect(string dialect, string spec)
    {
        if (!_expected.TryAdd(dialect, spec))
        {
            throw new InvalidOperationException($"行 {Id} の方言 {dialect} の期待が重複している");
        }

        return this;
    }

    /// <summary>方言なしの観点の期待を書く</summary>
    /// <param name="spec">期待の写し</param>
    public DialectTypeRow Expect(string spec) => Expect(DialectTypeAxis.None, spec);

    /// <summary>5 方言すべてに同じ期待を書く</summary>
    /// <param name="spec">期待の写し</param>
    public DialectTypeRow ExpectAll(string spec)
    {
        foreach (var dialect in DialectNames.All)
        {
            Expect(dialect, spec);
        }

        return this;
    }

    /// <summary>境界の種類を付ける</summary>
    /// <param name="boundary">境界の種類（<see cref="DialectTypeBoundaries"/>）</param>
    public DialectTypeRow Boundary(string boundary)
    {
        _boundary = boundary;
        return this;
    }

    /// <summary>網羅の計数に数える正規型の種類を明示する（入力から決まらないときだけ）</summary>
    /// <param name="kind">正規型の種類</param>
    public DialectTypeRow Kind(CanonicalTypeKind kind)
    {
        _kind = kind;
        return this;
    }

    /// <summary>表の行にする</summary>
    public ScenarioRow<DialectTypeCase> ToScenario() =>
        new(
            Id,
            null,
            Description,
            new DialectTypeCase(
                Facet,
                _boundary,
                Input,
                new Dictionary<string, string>(_expected, StringComparer.Ordinal),
                _kind
            )
        );
}
