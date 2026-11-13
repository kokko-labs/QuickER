using System.Globalization;
using System.Text;
using QuickER.Model;

namespace QuickER.Tests.TestSupport;

/// <summary>列の C# 側の種類（オラクルの比較の仕方と、生成器が使える演算子を決める）</summary>
public enum DslClr
{
    /// <summary>可変長の文字列</summary>
    String,

    /// <summary>固定長の文字列（SQLite は詰めない）</summary>
    FixedString,

    /// <summary>long</summary>
    Int64,

    /// <summary>int</summary>
    Int32,

    /// <summary>short</summary>
    Int16,

    /// <summary>byte（符号なし 0〜255）</summary>
    Byte,

    /// <summary>sbyte</summary>
    SByte,

    /// <summary>ushort</summary>
    UInt16,

    /// <summary>uint</summary>
    UInt32,

    /// <summary>ulong</summary>
    UInt64,

    /// <summary>decimal</summary>
    Decimal,

    /// <summary>DateTime</summary>
    DateTime,

    /// <summary>Guid</summary>
    Guid,

    /// <summary>GuidKey の値オブジェクト（文字列の主キー）</summary>
    GuidKey,

    /// <summary>bool（リテラルは書けず、パラメータとの等値・IN・NULL 判定だけ）</summary>
    Boolean,

    /// <summary>byte[]（リテラルは書けず、パラメータとの等値・IN・NULL 判定だけ。バイトの列で比べる）</summary>
    Binary,

    /// <summary>TimeSpan（リテラルは書けず、パラメータとの比較・順序比較・IN・NULL 判定だけ）</summary>
    TimeSpan,
}

/// <summary>総当たりの図の列</summary>
/// <param name="Name">列名</param>
/// <param name="DataType">方言の宣言型</param>
/// <param name="Clr">C# 側の種類</param>
/// <param name="Nullable">NULL を許すか</param>
/// <param name="PrimaryKey">主キーか</param>
public sealed record DslColumn(
    string Name,
    string DataType,
    DslClr Clr,
    bool Nullable,
    bool PrimaryKey = false
)
{
    /// <summary>文字列一致を使える列か</summary>
    public bool IsString => Clr is DslClr.String or DslClr.FixedString or DslClr.GuidKey;

    /// <summary>整数の列か</summary>
    public bool IsInteger =>
        Clr
            is DslClr.Int64
                or DslClr.Int32
                or DslClr.Int16
                or DslClr.Byte
                or DslClr.SByte
                or DslClr.UInt16
                or DslClr.UInt32
                or DslClr.UInt64;

    /// <summary>順序比較（&lt; など）を使える列か（型の検査が文字列・Guid の順序比較を断る）</summary>
    public bool IsOrdered =>
        IsInteger || Clr is DslClr.Decimal or DslClr.DateTime or DslClr.TimeSpan;

    /// <summary>
    /// Unicode でない文字列の列か（SQL Server の varchar・char＝照合順序のコードページで持ち、ASCII の外の文字が <c>?</c> に化ける列。
    /// SQLite・MySQL の varchar・char は Unicode で持つので立てない）
    /// </summary>
    public bool NonUnicode { get; init; }

    /// <summary>列の値の候補（null なら種類ごとの値の池 <see cref="DslFuzz.ValuePool(DslClr)"/>）</summary>
    public IReadOnlyList<object>? Values { get; init; }

    /// <summary>
    /// パラメータと IN のリストにだけ使う値の候補（null なら <see cref="Values"/> と同じ）。行に入らない値（固定長のバイナリの列より長い値）で
    /// 比べる形を作るときに持たせる
    /// </summary>
    public IReadOnlyList<object>? OperandValues { get; init; }

    /// <summary>
    /// 固定長のバイナリの列（宣言型 <c>binary(n)</c>）の長さ n（ほかの列は null。可変長の <c>varbinary(n)</c> は含まない）
    /// </summary>
    public int? FixedBinaryLength =>
        Clr == DslClr.Binary
        && DataType.StartsWith("binary(", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(
            DataType.AsSpan(7, DataType.Length - 8),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var length
        )
            ? length
            : null;

    /// <summary>
    /// 数のリテラルを負の値にしないか（値の池から選んだ負の値は符号を外す。乱数の引き方・行の値・パラメータの値は変えない）
    /// </summary>
    public bool NonNegativeLiterals { get; init; }

    /// <summary>
    /// SQL Server の money・smallmoney の列の値の範囲（null ならそうでない列。PostgreSQL の money は立てない＝比べる値は money で送られて
    /// 丸められる docs の既知の割り切りで、列の型に入らない値の原子を作らない）
    /// </summary>
    public DslMoneyType? Money { get; init; }

    /// <summary>
    /// TimeSpan の列が持てる値の範囲（null なら範囲を持たない列＝SQLite の TEXT・PostgreSQL の interval。比べる値がこの外なら
    /// 次元表の因子 ValueFit の beyond-range に数える）
    /// </summary>
    public DslTimeRange? TimeRange { get; init; }

    /// <summary>モデルの列（Id は図を組み立てるときに決まる）</summary>
    public Column Model { get; } =
        new()
        {
            Name = Name,
            DataType = DataType,
            IsNullable = Nullable && !PrimaryKey,
            IsPrimaryKey = PrimaryKey,
        };
}

/// <summary>SQL Server の money・smallmoney の型（小数 4 桁と値の範囲。範囲は 10 の累乗の桁では表せない）</summary>
/// <param name="Min">最小値</param>
/// <param name="Max">最大値</param>
public sealed record DslMoneyType(decimal Min, decimal Max)
{
    /// <summary>money（-922,337,203,685,477.5808〜922,337,203,685,477.5807）</summary>
    public static DslMoneyType Money { get; } =
        new(-922_337_203_685_477.5808m, 922_337_203_685_477.5807m);

    /// <summary>smallmoney（-214,748.3648〜214,748.3647）</summary>
    public static DslMoneyType SmallMoney { get; } = new(-214_748.3648m, 214_748.3647m);

    /// <summary>小数の桁（money・smallmoney とも 4）</summary>
    public const int Scale = 4;

    /// <summary>値が範囲の中か</summary>
    public bool InRange(decimal value) => value >= Min && value <= Max;

    /// <summary>値が列の型に丸めずに入るか（範囲の中で、小数が 4 桁以下）</summary>
    public bool Fits(decimal value) => InRange(value) && decimal.Round(value, Scale) == value;
}

/// <summary>TimeSpan の列の型が持てる値の範囲（両端を含む）</summary>
/// <param name="Min">最小値</param>
/// <param name="Max">最大値</param>
public sealed record DslTimeRange(TimeSpan Min, TimeSpan Max)
{
    /// <summary>SQL Server の time（00:00:00〜23:59:59.9999999）</summary>
    public static DslTimeRange SqlServerTime { get; } =
        new(TimeSpan.Zero, TimeSpan.FromDays(1) - TimeSpan.FromTicks(1));

    /// <summary>PostgreSQL の time（00:00:00〜24:00:00）</summary>
    public static DslTimeRange PostgreSqlTime { get; } = new(TimeSpan.Zero, TimeSpan.FromDays(1));

    /// <summary>MySQL の time（-838:59:59〜838:59:59）</summary>
    public static DslTimeRange MySqlTime { get; } =
        new(-new TimeSpan(34, 22, 59, 59), new TimeSpan(34, 22, 59, 59));

    /// <summary>値が範囲の中か</summary>
    public bool Contains(TimeSpan value) => value >= Min && value <= Max;
}

/// <summary>比較演算子</summary>
public enum DslOp
{
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
}

/// <summary>文字列一致の種類</summary>
public enum DslMatchKind
{
    Contains,
    StartsWith,
    EndsWith,
}

/// <summary>比較の右辺</summary>
public abstract record DslOperand;

/// <summary>リテラル（数値・文字列）</summary>
/// <param name="Value">C# の値</param>
/// <param name="Text">DSL の綴り</param>
public sealed record DslLiteral(object Value, string Text) : DslOperand;

/// <summary>パラメータ</summary>
/// <param name="Name">パラメータ名</param>
/// <param name="Values">値（リストでなければ 1 つ。リストは空・null を含むこともある）</param>
/// <param name="IsList">リストか</param>
/// <param name="ByColumn">source_column で型を決めるか（false は型トークン）</param>
/// <param name="Column">型を決める列</param>
public sealed record DslParam(
    string Name,
    IReadOnlyList<object?> Values,
    bool IsList,
    bool ByColumn,
    DslColumn Column
) : DslOperand
{
    /// <summary>
    /// 型トークン（<see cref="ByColumn"/> が偽のとき。null なら列の種類の型トークン <see cref="DslFuzz.TypeToken"/>）。
    /// 列の型に入らない値を、列より広い型で渡すときに指定する
    /// </summary>
    public string? TypeToken { get; init; }
}

/// <summary>条件の構文木</summary>
public abstract record DslNode;

/// <summary>1 つの条件（原子）</summary>
public abstract record DslAtom : DslNode
{
    /// <summary>対象の列</summary>
    public required DslColumn Column { get; init; }

    /// <summary>失敗のキーに使う原子の種類（演算子の形と列の種類）</summary>
    public abstract string Kind { get; }

    /// <summary>原子自身の否定（NOT LIKE・NOT IN・IS NOT NULL など）</summary>
    public virtual bool InherentNegation => false;

    /// <summary>
    /// 型の検査が断ると決まっている原子か（生成時に警告して、その原子を含むクエリを飛ばす）。
    /// 列の型の範囲外の整数リテラル・GuidKey の列への完全一致以外の文字列一致
    /// </summary>
    public virtual bool IsRejectedByTypeCheck => false;

    /// <summary>このクエリが起こし得る、既知の原因で説明できるコンパイラの診断（コード → 原因の名前）</summary>
    public virtual IEnumerable<(string Code, string Cause)> KnownDiagnostics(bool valueObjects) =>
        [];
}

/// <summary>比較（列 演算子 右辺）</summary>
public sealed record DslCompare(DslOp Op, DslOperand Operand) : DslAtom
{
    public override string Kind =>
        $"compare-{Op.ToString().ToLowerInvariant()}-{OperandKind}:{Column.Clr.ToString().ToLowerInvariant()}";

    private string OperandKind =>
        Operand is DslParam ? "param"
        : Fractional
            ? RejectedByTypeCheck ? "literal-fractional-rejected"
                : "literal-fractional"
        : RejectedByTypeCheck ? "literal-rejected"
        : "literal";

    /// <summary>整数の列と小数のリテラルの比較か（docs：素の整数の列は数として比べ、整数の値オブジェクトの列は断る）</summary>
    public bool Fractional =>
        Column.IsInteger && Operand is DslLiteral literal && literal.Text.Contains('.');

    /// <summary>
    /// 型の検査が断るリテラルか（C# の整数リテラルにならない値と、列の型〔値オブジェクトなら内包型〕の範囲外。
    /// 生成時に警告で飛ばされることを確かめる）
    /// </summary>
    public bool RejectedByTypeCheck { get; init; }

    public override bool IsRejectedByTypeCheck => RejectedByTypeCheck;
}

/// <summary>IS [NOT] NULL</summary>
public sealed record DslNullCheck(bool IsNot) : DslAtom
{
    public override string Kind => $"null-check:{Column.Clr.ToString().ToLowerInvariant()}";

    public override bool InherentNegation => IsNot;
}

/// <summary>[NOT] LIKE（リテラルは % の位置で分解・パラメータは部分一致）</summary>
public sealed record DslLike(bool Negated, DslOperand Operand) : DslAtom
{
    public override string Kind =>
        Operand switch
        {
            DslLiteral literal =>
                $"like-{LikeShape(literal.Text)}:{Column.Clr.ToString().ToLowerInvariant()}",
            _ => $"like-param:{Column.Clr.ToString().ToLowerInvariant()}",
        };

    public override bool InherentNegation => Negated;

    /// <summary>GuidKey の列は部分一致のメソッドを持たないので、完全一致（ワイルドカードなしのリテラル）以外は断られる</summary>
    public override bool IsRejectedByTypeCheck =>
        Column.Clr == DslClr.GuidKey
        && !(Operand is DslLiteral literal && LikeShape(literal.Text) == "exact");

    /// <summary>リテラルのパターンの形（exact・prefix・suffix・contains）</summary>
    public static string LikeShape(string quoted)
    {
        var pattern = DslFuzz.Unquote(quoted);
        var starts = pattern.StartsWith('%');
        var ends = pattern.EndsWith('%') && pattern.Length > 1;

        return (starts, ends) switch
        {
            (true, true) => "contains",
            (true, false) => "suffix",
            (false, true) => "prefix",
            _ => "exact",
        };
    }
}

/// <summary>[NOT] IN</summary>
public sealed record DslIn(bool Negated, DslParam List) : DslAtom
{
    /// <summary>値の並ぶリスト（空でなく null を含まない）</summary>
    public const string ListValues = "values";

    /// <summary>空のリスト</summary>
    public const string ListEmpty = "empty";

    /// <summary>値と null を含むリスト</summary>
    public const string ListWithNull = "with-null";

    /// <summary>null だけのリスト</summary>
    public const string ListAllNull = "all-null";

    /// <summary>リストの形の水準（docs の Null compensation 節の IN の箇条が意味を決める 4 つ）</summary>
    public static IReadOnlyList<string> ListShapes { get; } =
    [ListValues, ListEmpty, ListWithNull, ListAllNull];

    /// <summary>リストの形</summary>
    public string ListShape =>
        List.Values.Count == 0 ? ListEmpty
        : List.Values.All(value => value is null) ? ListAllNull
        : List.Values.Any(value => value is null) ? ListWithNull
        : ListValues;

    /// <summary>原子の形の名前（値の並ぶリストは <c>in</c>、ほかは <c>in-{リストの形}</c>）</summary>
    public string ShapeName => ListShape == ListValues ? "in" : $"in-{ListShape}";

    public override string Kind => $"{ShapeName}:{Column.Clr.ToString().ToLowerInvariant()}";

    public override bool InherentNegation => Negated;

    public override IEnumerable<(string Code, string Cause)> KnownDiagnostics(bool valueObjects) =>
        valueObjects && Column.Nullable ? [("CS8604", "in-nullable-vo")] : [];
}

/// <summary>[NOT] CONTAINS / STARTSWITH / ENDSWITH</summary>
public sealed record DslMatch(DslMatchKind Match, bool Negated, DslOperand Operand) : DslAtom
{
    public override string Kind =>
        $"{Match.ToString().ToLowerInvariant()}-{(Operand is DslLiteral ? "literal" : "param")}:{Column.Clr.ToString().ToLowerInvariant()}";

    public override bool InherentNegation => Negated;

    /// <summary>GuidKey の列は部分一致のメソッドを持たないので、CONTAINS 系は断られる</summary>
    public override bool IsRejectedByTypeCheck => Column.Clr == DslClr.GuidKey;
}

/// <summary>NOT</summary>
public sealed record DslNot(DslNode Inner) : DslNode;

/// <summary>AND</summary>
public sealed record DslAnd(IReadOnlyList<DslNode> Items) : DslNode;

/// <summary>OR</summary>
public sealed record DslOr(IReadOnlyList<DslNode> Items) : DslNode;

/// <summary>並び替えの 1 項目（主キーより前に置く列と向き）</summary>
/// <param name="Column">並べる列（整数・decimal・日時の列だけ）</param>
/// <param name="Descending">降順か</param>
public sealed record DslOrdering(DslColumn Column, bool Descending);

/// <summary>並び替えで NULL を置く位置（docs のインメモリの節「実 DB との既知の乖離」の写し）</summary>
/// <remarks>
/// SQLite・SQL Server・MySQL とインメモリは NULL を最小の値として並べる（昇順で先頭・降順で末尾）。PostgreSQL・Oracle は最大の値として
/// 並べる（昇順で末尾・降順で先頭）。EF Core は接続先の DB に従う。
/// </remarks>
public enum DslNullOrder
{
    /// <summary>昇順で先頭・降順で末尾</summary>
    FirstWhenAscending,

    /// <summary>昇順で末尾・降順で先頭</summary>
    LastWhenAscending,
}

/// <summary>条件の括弧の書き方</summary>
public enum DslGrouping
{
    /// <summary>原子でない項目（NOT・AND・OR）を、AND / OR の項目と NOT の対象になるたびに括弧で包む</summary>
    Explicit,

    /// <summary>
    /// 優先順位（docs：<c>NOT</c> が <c>AND</c> より、<c>AND</c> が <c>OR</c> より強く結び付く）で決まる括弧を省く。
    /// 括弧を残すのは、AND の項目の OR と、NOT の対象の AND / OR だけ
    /// </summary>
    Minimal,
}

/// <summary>キーワードと不等号の綴り（docs：キーワードは大文字小文字を区別せず、<c>!=</c> は <c>&lt;&gt;</c> と同じ）</summary>
public enum DslSpelling
{
    /// <summary>大文字のキーワードと <c>&lt;&gt;</c></summary>
    Upper,

    /// <summary>小文字のキーワードと <c>!=</c></summary>
    Lower,

    /// <summary>先頭だけ大文字のキーワード（<c>And</c>・<c>Startswith</c>）と <c>&lt;&gt;</c></summary>
    Mixed,
}

/// <summary>総当たりの 1 クエリ</summary>
public sealed class DslQuery
{
    public required string Name { get; init; }

    public required DslNode Condition { get; init; }

    /// <summary>条件の括弧の書き方（構文木は同じで、DSL の文字列だけが変わる）</summary>
    public DslGrouping Grouping { get; init; }

    /// <summary>キーワードと不等号の綴り（構文木は同じで、DSL の文字列だけが変わる）</summary>
    public DslSpelling Spelling { get; init; }

    /// <summary>条件の DSL の文字列</summary>
    public string Text => DslFuzz.Render(Condition, Grouping, Spelling);

    /// <summary>描画の書き方を変えたクエリ（構文木の形は同じ。名前は <paramref name="name"/>）</summary>
    /// <param name="name">新しいクエリの名前</param>
    /// <param name="grouping">括弧の書き方</param>
    /// <param name="spelling">キーワードと不等号の綴り</param>
    /// <param name="condition">条件（同じものか、パラメータ名を付け直したもの）</param>
    public DslQuery WithRendering(
        string name,
        DslGrouping grouping,
        DslSpelling spelling,
        DslNode condition
    ) =>
        new()
        {
            Name = name,
            Condition = condition,
            Grouping = grouping,
            Spelling = spelling,
            Returns = Returns,
            IsAtomQuery = IsAtomQuery,
            OrderDescending = OrderDescending,
            ProjectedColumn = ProjectedColumn,
            ProjectedNonNull = ProjectedNonNull,
            ResultTypeName =
                ResultTypeName is null ? null
                : name == Name ? ResultTypeName
                : name + "Row",
            TypeNameCollision = TypeNameCollision,
            Orderings = Orderings,
            ListOrdered = ListOrdered,
        };

    /// <summary>並びだけを変えたクエリ（構文木・名前・描画は同じ）</summary>
    /// <param name="orderings">主キーより前に置く並び</param>
    /// <param name="listOrdered">一覧を並べるか</param>
    /// <param name="descending">主キーの並びの向き</param>
    public DslQuery WithOrdering(
        IReadOnlyList<DslOrdering> orderings,
        bool listOrdered,
        bool descending
    ) =>
        new()
        {
            Name = Name,
            Condition = Condition,
            Grouping = Grouping,
            Spelling = Spelling,
            Returns = Returns,
            IsAtomQuery = IsAtomQuery,
            OrderDescending = descending,
            ProjectedColumn = ProjectedColumn,
            ProjectedNonNull = ProjectedNonNull,
            ResultTypeName = ResultTypeName,
            TypeNameCollision = TypeNameCollision,
            Orderings = orderings,
            ListOrdered = listOrdered,
        };

    public required QueryReturnShape Returns { get; init; }

    /// <summary>主キーより前に置く並び（並べるクエリだけ。最後には必ず主キーを置くので同点は残らない）</summary>
    public IReadOnlyList<DslOrdering> Orderings { get; init; } = [];

    /// <summary>一覧を並べるか（単一・射影はいつも並べる。件数は並べない）</summary>
    public bool ListOrdered { get; init; }

    /// <summary>並び替えを持つクエリか（単一・射影と、<see cref="ListOrdered"/> の一覧）</summary>
    public bool IsOrdered =>
        Returns is QueryReturnShape.Single or QueryReturnShape.Projection
        || (Returns == QueryReturnShape.List && ListOrdered);

    /// <summary>原子だけのクエリか（食い違いを原子の種類へ帰着させるため）</summary>
    public required bool IsAtomQuery { get; init; }

    /// <summary>主キーの並びの向き（降順なら true。並び替えを持つクエリだけが使う）</summary>
    public bool OrderDescending { get; init; }

    /// <summary>射影の 2 つめのフィールドの列（射影のときだけ）</summary>
    public DslColumn? ProjectedColumn { get; init; }

    /// <summary>射影の 2 つめのフィールドを非 NULL と指定するか（NULL 許容の参照型・VO の列で CS8601＝A8）</summary>
    public bool ProjectedNonNull { get; init; }

    /// <summary>射影の結果の型名</summary>
    public string? ResultTypeName { get; init; }

    /// <summary>射影の型名を既存の型と衝突させた種類（衝突させないなら null）</summary>
    public string? TypeNameCollision { get; init; }

    /// <summary>原子の一覧</summary>
    public IEnumerable<DslAtom> Atoms => DslFuzz.AtomsOf(Condition);

    /// <summary>パラメータの一覧（出現順・重複なし）</summary>
    public IEnumerable<DslParam> Parameters =>
        Atoms.Select(DslFuzz.ParameterOf).OfType<DslParam>().DistinctBy(p => p.Name);

    /// <summary>2 つ以上の原子が同じパラメータを参照するか</summary>
    public bool SharesParameter =>
        Atoms
            .Select(DslFuzz.ParameterOf)
            .OfType<DslParam>()
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Any(group => group.Count() > 1);

    /// <summary>このクエリが起こし得る、既知の原因の診断</summary>
    public IEnumerable<(string Code, string Cause)> KnownDiagnostics(bool valueObjects)
    {
        foreach (var known in Atoms.SelectMany(atom => atom.KnownDiagnostics(valueObjects)))
        {
            yield return known;
        }

        if (ProjectedNonNull)
        {
            yield return ("CS8601", "projection-nonnull-field");
        }
    }
}

/// <summary>実装先ごとの文字列一致の大文字小文字の扱い（docs の表の写し）</summary>
public enum DslCaseRule
{
    /// <summary>序数（大文字小文字を区別する）</summary>
    Ordinal,

    /// <summary>ASCII の英字だけを畳む（SQLite の組み込みの LIKE）</summary>
    AsciiFold,

    /// <summary>格納先任せ（序数と畳んだ結果が違う行は判定しない）</summary>
    Indeterminate,

    /// <summary>
    /// 列の照合順序（SQL Server。既定の <c>..._CI_AS</c> は大文字小文字を区別せず、<c>=</c> は末尾の空白を無視する）。
    /// 大文字小文字か末尾の空白だけで結果が変わる行は判定しない（NULL の行と、照合順序に依らず決まる行は判定する）
    /// </summary>
    /// <remarks>
    /// docs の表の SQL Server の行（「列の照合順序に従う」）と、同じ節の「<c>= ''</c> は末尾の空白を無視する」の写し。
    /// 文字列一致（LIKE のワイルドカードつき・Contains / StartsWith / EndsWith）は Unicode の列では末尾の空白を区別するので、
    /// 大文字小文字だけを畳む（同じ節の「<c>EndsWith</c> は詰めた値に対して比べる」）。Unicode でない列（<c>char</c>・<c>varchar</c>）の
    /// 文字列一致は列の値の末尾の空白を数えない（同じ表の SQL Server の行。<c>MatchValue</c>）。
    /// </remarks>
    Collation,

    /// <summary>
    /// PostgreSQL（EF Core）。序数で比べ、固定長の文字列の列（<c>char(n)</c>）は詰めた値を返し、<c>=</c>・<c>&lt;&gt;</c>・IN・
    /// ワイルドカードの無い LIKE は末尾の空白を無視する（読み戻した詰めた値の末尾の空白を外して比べる）
    /// </summary>
    /// <remarks>
    /// docs の「大文字小文字・空白・日付部品」の表の EF Core（PostgreSQL）の行と、同じ節の PostgreSQL の <c>char(n)</c> の段落の写し。
    /// 固定長の列のワイルドカードつきの LIKE と Contains / StartsWith / EndsWith は docs が約束していないので、末尾の空白を外すかで
    /// 結果が変わる行を判定しない。
    /// </remarks>
    PostgreSql,

    /// <summary>
    /// MySQL（EF Core・既定の照合順序 <c>utf8mb4_0900_ai_ci</c>）。大文字小文字・アクセント・合成済みの文字と分解形・<c>ß</c> と
    /// <c>ss</c> を畳むと結果が変わる行を判定しない（<c>=</c>・<c>&lt;&gt;</c>・IN・LIKE・Contains / StartsWith / EndsWith のどれも）。
    /// StartsWith / EndsWith はパターンの末尾の空白を外すと結果が変わる行も判定しない
    /// </summary>
    /// <remarks>
    /// docs の表の EF Core（MySQL）の行と、同じ節の「MySQL では等値の比較も照合順序が決める」の段落の写し。素の string の列の
    /// StartsWith / EndsWith は大文字小文字を区別し、値オブジェクトの列は区別しない（docs の同じ行）が、オラクルは列が値オブジェクトかを
    /// 持たないので、どちらの列でも畳むと変わる行を判定しない側に倒す。<c>char(n)</c> の末尾の空白は読み戻した値から既に落ちている。
    /// </remarks>
    MySql,

    /// <summary>
    /// Oracle（EF Core）。序数で比べ、空文字列は NULL として扱う：<c>""</c> と比べる <c>=</c>・<c>&lt;&gt;</c> は NULL でない行のどれにも
    /// 一致せず（否定でも一致しない）、<c>""</c> を含む IN・NOT IN のリストは、ほかの要素に一致しない NULL でない行のどれにも一致しない
    /// </summary>
    /// <remarks>
    /// docs の表の EF Core（Oracle）の行と、同じ節の Oracle の空文字列の段落（変数に入れた空文字列との比較は NULL との比較になる）の写し。
    /// 簡易 DSL の条件の文字列のリテラルは変数（パラメータ）として生成する（docs の同じ段落の最後の文）ことが前提で、そのためリテラルの
    /// <c>''</c> もパラメータの <c>""</c> と同じに扱う。手書きの LINQ のリテラルの <c>""</c>（<c>IS NULL</c> へ変換される）は総当たりの
    /// 外で、docs の事実として書くだけ。NULL の行は C# の意味のまま（EF Core が NULL の行の補償を付ける）。<c>""</c> のパターンの
    /// 文字列一致は docs が約束していないので、NULL でない行を判定しない。読み戻した値の空文字列は既に NULL になっている。
    /// 空のバイナリも同じ規則に当たる（docs のバイナリの段落：Oracle は空のバイナリを NULL として持つ）。
    /// </remarks>
    Oracle,
}

/// <summary>バイナリの列の比べ方（実装先ごと。docs の「大文字小文字・空白・日付部品」の節のバイナリの段落の写し）</summary>
public enum DslBinaryRule
{
    /// <summary>バイトの列のまま比べる（長さも比べる）</summary>
    Bytes,

    /// <summary>
    /// 末尾の 0x00 だけが違う値を等しいとみなす（SQL Server の EF Core 版 Repository。SQL Server は短い側を 0x00 で詰めて比べる）。
    /// 読み戻した値と比べる値の両方の末尾の 0x00 を外して比べる
    /// </summary>
    TrailingZerosIgnored,

    /// <summary>
    /// 固定長の列（<c>binary(n)</c>）とは、比べる値をその列へ保存したときの値（0x00 で詰める・超えた分が 0x00 だけなら切る）に直してから
    /// バイトの列で比べ、可変長の列とはバイトの列のまま比べる（SQL Server の QuickER 版 Repository）。読み戻した値は詰めてある
    /// </summary>
    FixedLengthAsStored,
}

/// <summary>
/// 簡易 DSL の条件の総当たり（生成器・描画・オラクル）
/// </summary>
/// <remarks>
/// <para>
/// オラクル（<see cref="Evaluate"/>）は生成器の構文木を、エミッタを使わずに docs の意味で評価する独立実装。正本は
/// docs/code-generation(.ja).md の「クエリ（式木 → SQL 変換）」節（NULL の扱い・文字列一致の NULL の前提・
/// 「NOT と &lt;&gt; は C# の否定と同じ、文字列一致だけは NULL の行がどちら向きでも一致しない」）と、
/// 同じ節の「大文字小文字・空白・日付部品」の表（SQLite の組み込みの LIKE は ASCII の英字だけを畳む・EF Core は格納先次第・
/// インメモリは序数・SQL Server は列の照合順序）。実装の挙動は写さない（実装と一緒に間違えるため）。
/// </para>
/// <para>
/// 大文字小文字の表で「格納先次第」「照合順序次第」の実装先は、序数と畳んだ結果が違う行を判定しない（<see cref="DslCaseRule.Indeterminate"/>）。
/// 完全一致の LIKE（ワイルドカードの無いリテラル）は表に載っていないため、SQLite でも判定しない側に倒す。
/// </para>
/// </remarks>
public static class DslFuzz
{
    /// <summary>原子が参照するパラメータ（リテラル・NULL 判定は null）</summary>
    public static DslParam? ParameterOf(DslAtom atom) =>
        atom switch
        {
            DslCompare { Operand: DslParam p } => p,
            DslLike { Operand: DslParam p } => p,
            DslMatch { Operand: DslParam p } => p,
            DslIn @in => @in.List,
            _ => null,
        };

    /// <summary>原子を出現順に列挙する</summary>
    public static IEnumerable<DslAtom> AtomsOf(DslNode node) =>
        node switch
        {
            DslAtom atom => [atom],
            DslNot not => AtomsOf(not.Inner),
            DslAnd and => and.Items.SelectMany(AtomsOf),
            DslOr or => or.Items.SelectMany(AtomsOf),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    // ---------------- 描画 ----------------

    /// <summary>条件を DSL の文字列にする</summary>
    /// <param name="node">条件</param>
    /// <param name="grouping">括弧の書き方</param>
    /// <param name="spelling">キーワードと不等号の綴り</param>
    public static string Render(
        DslNode node,
        DslGrouping grouping = DslGrouping.Explicit,
        DslSpelling spelling = DslSpelling.Upper
    ) =>
        node switch
        {
            DslAtom atom => RenderAtom(atom, spelling),
            DslNot not => Keyword("NOT", spelling) + " " + Wrap(not, not.Inner, grouping, spelling),
            DslAnd and => string.Join(
                " " + Keyword("AND", spelling) + " ",
                and.Items.Select(item => Wrap(and, item, grouping, spelling))
            ),
            DslOr or => string.Join(
                " " + Keyword("OR", spelling) + " ",
                or.Items.Select(item => Wrap(or, item, grouping, spelling))
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    /// <summary>項目を、親の中で括弧が要るときだけ括弧で包む</summary>
    private static string Wrap(
        DslNode parent,
        DslNode child,
        DslGrouping grouping,
        DslSpelling spelling
    ) =>
        NeedsParentheses(parent, child, grouping)
            ? "(" + Render(child, grouping, spelling) + ")"
            : Render(child, grouping, spelling);

    /// <summary>
    /// 括弧が要るか（Explicit は原子以外を必ず包む。Minimal は docs の優先順位で決まらない組だけ＝NOT の対象の AND / OR と、
    /// AND の項目の OR。同じ演算子の入れ子は結合の向きで意味が変わらない）
    /// </summary>
    private static bool NeedsParentheses(DslNode parent, DslNode child, DslGrouping grouping) =>
        child is not DslAtom
        && (
            grouping == DslGrouping.Explicit
            || (parent, child) switch
            {
                (DslNot, DslAnd or DslOr) => true,
                (DslAnd, DslOr) => true,
                _ => false,
            }
        );

    /// <summary>
    /// 親と項目の組が、優先順位が括弧の代わりをし得る組か（OR の項目の AND・AND / OR の項目の NOT。Minimal の描画は
    /// この組の括弧を省き、Explicit の描画は括弧で書く）
    /// </summary>
    public static bool IsPrecedenceEdge(DslNode parent, DslNode child) =>
        (parent, child) switch
        {
            (DslOr, DslAnd or DslNot) => true,
            (DslAnd, DslNot) => true,
            _ => false,
        };

    private static string RenderAtom(DslAtom atom, DslSpelling spelling)
    {
        var column = atom.Column.Name;
        string K(string word) => Keyword(word, spelling);
        string Not(bool negated) => negated ? K("NOT") + " " : "";

        return atom switch
        {
            DslCompare c => $"{column} {OpText(c.Op, spelling)} {RenderOperand(c.Operand)}",
            DslNullCheck n => $"{column} {K("IS")} {Not(n.IsNot)}{K("NULL")}",
            DslLike l => $"{column} {Not(l.Negated)}{K("LIKE")} {RenderOperand(l.Operand)}",
            DslIn i => $"{column} {Not(i.Negated)}{K("IN")} @{i.List.Name}",
            DslMatch m =>
                $"{column} {Not(m.Negated)}{K(m.Match.ToString().ToUpperInvariant())} {RenderOperand(m.Operand)}",
            _ => throw new ArgumentOutOfRangeException(nameof(atom)),
        };
    }

    /// <summary>キーワードを綴りの水準で書く（大文字・小文字・先頭だけ大文字）</summary>
    private static string Keyword(string upper, DslSpelling spelling) =>
        spelling switch
        {
            DslSpelling.Upper => upper,
            DslSpelling.Lower => upper.ToLowerInvariant(),
            DslSpelling.Mixed => upper[..1] + upper[1..].ToLowerInvariant(),
            _ => throw new ArgumentOutOfRangeException(nameof(spelling)),
        };

    private static string RenderOperand(DslOperand operand) =>
        operand switch
        {
            DslLiteral literal => literal.Text,
            DslParam param => "@" + param.Name,
            _ => throw new ArgumentOutOfRangeException(nameof(operand)),
        };

    private static string OpText(DslOp op, DslSpelling spelling) =>
        op switch
        {
            DslOp.Eq => "=",
            DslOp.Ne => spelling == DslSpelling.Lower ? "!=" : "<>",
            DslOp.Lt => "<",
            DslOp.Le => "<=",
            DslOp.Gt => ">",
            DslOp.Ge => ">=",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    /// <summary>文字列のリテラルの綴り（docs：リテラルの中の <c>'</c> は <c>''</c> と 2 つ重ねて書く）</summary>
    public static string QuoteText(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>文字列のリテラルの綴りから値を読む（<see cref="QuoteText"/> の逆）</summary>
    public static string Unquote(string quoted) =>
        quoted[1..^1].Replace("''", "'", StringComparison.Ordinal);

    /// <summary>原子の文字列のリテラルの水準（none＝文字列のリテラルを持たない・plain・escaped＝<c>'</c> を含む）</summary>
    public static string QuoteOf(DslAtom atom)
    {
        var literal = atom switch
        {
            DslCompare { Operand: DslLiteral l } when atom.Column.IsString => l,
            DslLike { Operand: DslLiteral l } => l,
            DslMatch { Operand: DslLiteral l } => l,
            _ => null,
        };

        return literal is null ? "none"
            : literal.Value is string text && text.Contains('\'') ? "escaped"
            : "plain";
    }

    // ---------------- オラクル ----------------

    /// <summary>行に条件を当てる（true / false、判定しない行は null）</summary>
    /// <param name="node">条件</param>
    /// <param name="row">列名 → 実装先から読み戻した値（VO は開いた値）</param>
    /// <param name="caseRule">文字列一致の大文字小文字の扱い</param>
    /// <param name="negate">外側の NOT（ド・モルガンで押し下げる）</param>
    /// <param name="binaryRule">バイナリの列の比べ方（実装先ごと。既定はバイトの列のまま）</param>
    public static bool? Evaluate(
        DslNode node,
        IReadOnlyDictionary<string, object?> row,
        DslCaseRule caseRule,
        bool negate = false,
        DslBinaryRule binaryRule = DslBinaryRule.Bytes
    ) =>
        node switch
        {
            DslNot not => Evaluate(not.Inner, row, caseRule, !negate, binaryRule),
            DslAnd and => negate
                ? Any(and.Items.Select(item => Evaluate(item, row, caseRule, true, binaryRule)))
                : All(and.Items.Select(item => Evaluate(item, row, caseRule, false, binaryRule))),
            DslOr or => negate
                ? All(or.Items.Select(item => Evaluate(item, row, caseRule, true, binaryRule)))
                : Any(or.Items.Select(item => Evaluate(item, row, caseRule, false, binaryRule))),
            DslAtom atom => EvaluateAtom(
                atom,
                BinaryUnder(row[atom.Column.Name], atom.Column, binaryRule),
                caseRule,
                negate,
                binaryRule
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    /// <summary>
    /// バイナリの列の値を、実装先の比べ方の形へ写す（<see cref="DslBinaryRule.TrailingZerosIgnored"/> は末尾の 0x00 を外す。
    /// ほかの列と規則はそのまま）
    /// </summary>
    private static object? BinaryUnder(object? value, DslColumn column, DslBinaryRule rule) =>
        (rule, value, column.FixedBinaryLength) switch
        {
            (DslBinaryRule.TrailingZerosIgnored, byte[] bytes, _)
                when column.Clr == DslClr.Binary => bytes.AsSpan().TrimEnd((byte)0).ToArray(),
            (DslBinaryRule.FixedLengthAsStored, byte[] bytes, int length) => AsStored(
                bytes,
                length
            ),
            _ => value,
        };

    /// <summary>
    /// 固定長のバイナリの列（<c>binary(n)</c>）へ保存したときの値（短い値は 0x00 で n バイトに詰め、超えた分が 0x00 だけの長い値は
    /// n バイトに切る。ほかの長い値は保存できないのでそのまま＝長さが違うのでどの行とも等しくない）。docs のバイナリの段落の写し
    /// </summary>
    private static byte[] AsStored(byte[] bytes, int length)
    {
        if (bytes.Length < length)
        {
            var padded = new byte[length];
            bytes.CopyTo(padded, 0);
            return padded;
        }

        return bytes.Length > length && bytes.AsSpan(length).IndexOfAnyExcept((byte)0) < 0
            ? bytes[..length]
            : bytes;
    }

    /// <summary>Oracle が NULL として持つ空の値か（空文字列と空のバイナリ）</summary>
    private static bool IsOracleEmpty(object? value) => value is "" or byte[] { Length: 0 };

    /// <summary>Kleene の AND</summary>
    private static bool? All(IEnumerable<bool?> values)
    {
        var unknown = false;

        foreach (var value in values)
        {
            if (value == false)
            {
                return false;
            }

            unknown |= value is null;
        }

        return unknown ? null : true;
    }

    /// <summary>Kleene の OR</summary>
    private static bool? Any(IEnumerable<bool?> values)
    {
        var unknown = false;

        foreach (var value in values)
        {
            if (value == true)
            {
                return true;
            }

            unknown |= value is null;
        }

        return unknown ? null : false;
    }

    private static bool? EvaluateAtom(
        DslAtom atom,
        object? value,
        DslCaseRule caseRule,
        bool negate,
        DslBinaryRule binaryRule
    )
    {
        switch (atom)
        {
            // 比較と NOT は C# の lifted な意味（NULL は非 null 値と等しくない＝<> は真・順序比較は偽。NOT はそのまま否定）
            case DslCompare compare:
            {
                var operand = BinaryUnder(OperandValue(compare.Operand), atom.Column, binaryRule)!;

                // Oracle：空文字列・空のバイナリとの比較は NULL との比較（UNKNOWN）で、NULL でない行はどちらの向きでも一致しない
                if (caseRule == DslCaseRule.Oracle && value is not null && IsOracleEmpty(operand))
                {
                    return false;
                }

                return Xor(CompareUnder(value, compare.Op, operand, caseRule, atom.Column), negate);
            }

            case DslNullCheck check:
                return (value is null) ^ check.IsNot ^ negate;

            // IN は C# の List.Contains（docs の Null compensation 節の IN の箇条：NULL の列は null を含むリストにだけ
            // 一致する・空のリストは IN が偽で NOT IN が真）
            case DslIn @in:
            {
                var values = @in
                    .List.Values.Select(v => BinaryUnder(v, atom.Column, binaryRule))
                    .ToList();

                // Oracle：空文字列・空のバイナリの要素は NULL で、ほかの要素に一致しない NULL でない行は IN でも NOT IN でも一致しない
                if (
                    caseRule == DslCaseRule.Oracle
                    && value is not null
                    && values.Any(IsOracleEmpty)
                    && !values.Any(v => v is not null && !IsOracleEmpty(v) && SameValue(value, v))
                )
                {
                    return false;
                }

                return Xor(InUnder(value, values, caseRule, atom.Column), @in.Negated ^ negate);
            }

            // 文字列一致は NULL の行がどちらの向きでも一致しない
            case DslLike like:
            {
                if (value is null)
                {
                    return false;
                }

                var (kind, pattern) = like.Operand switch
                {
                    DslLiteral literal => LikePattern(literal.Text),
                    DslParam param => ("contains", (string)param.Values[0]!),
                    _ => throw new ArgumentOutOfRangeException(nameof(atom)),
                };

                // Oracle の空文字列のパターン（NULL として届く）の一致は docs が約束していないので判定しない
                var raw = like.Operand switch
                {
                    DslLiteral literal => Unquote(literal.Text),
                    _ => pattern,
                };

                if (caseRule == DslCaseRule.Oracle && raw.Length == 0)
                {
                    return null;
                }

                // 完全一致の LIKE は docs の大文字小文字の表に載っていない＝序数以外の実装先では判定しない側に倒す
                var rule =
                    kind == "exact" && caseRule == DslCaseRule.AsciiFold
                        ? DslCaseRule.Indeterminate
                        : caseRule;

                return Xor(
                    MatchValue((string)value, atom.Column, kind, pattern, rule, caseRule),
                    like.Negated ^ negate
                );
            }

            case DslMatch match:
            {
                if (value is null)
                {
                    return false;
                }

                var kind = match.Match switch
                {
                    DslMatchKind.Contains => "contains",
                    DslMatchKind.StartsWith => "prefix",
                    _ => "suffix",
                };
                var pattern = (string)OperandValue(match.Operand);

                // Oracle の空文字列のパターン（NULL として届く）の一致は docs が約束していないので判定しない
                if (caseRule == DslCaseRule.Oracle && pattern.Length == 0)
                {
                    return null;
                }

                return Xor(
                    MatchValue((string)value, atom.Column, kind, pattern, caseRule, caseRule),
                    match.Negated ^ negate
                );
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(atom));
        }
    }

    private static bool? Xor(bool? value, bool flip) => value is null ? null : value.Value ^ flip;

    private static object OperandValue(DslOperand operand) =>
        operand switch
        {
            DslLiteral literal => literal.Value,
            DslParam param => param.Values[0]!,
            _ => throw new ArgumentOutOfRangeException(nameof(operand)),
        };

    private static (string Kind, string Pattern) LikePattern(string quoted)
    {
        var shape = DslLike.LikeShape(quoted);
        var pattern = Unquote(quoted);

        return shape switch
        {
            "contains" => (shape, pattern[1..^1]),
            "suffix" => (shape, pattern[1..]),
            "prefix" => (shape, pattern[..^1]),
            _ => (shape, pattern),
        };
    }

    /// <summary>
    /// 列の値への文字列一致。照合順序の実装先（SQL Server）の Unicode でない列（<c>char</c>・<c>varchar</c>）は、列の値の末尾の空白を
    /// 数えない（docs の「大文字小文字・空白・日付部品」の表の SQL Server の行）ので、末尾の空白を 0 個から全部まで外したどの値かが
    /// 一致すれば一致とする。パターンの空白は数える（<c>'a '</c> で始まる値は <c>'a  '</c> に一致する）。
    /// </summary>
    private static bool? MatchValue(
        string value,
        DslColumn column,
        string kind,
        string pattern,
        DslCaseRule rule,
        DslCaseRule caseRule
    )
    {
        // PostgreSQL の固定長の列（読み戻した値は詰めてある）：ワイルドカードの無い LIKE は末尾の空白を無視する（docs）。パターンの
        // 末尾の空白の扱いと、ほかの一致での列の値の末尾の空白の扱いは docs が約束していないので、どちらに読んでも同じ行だけを判定する
        if (caseRule == DslCaseRule.PostgreSql && column.Clr == DslClr.FixedString)
        {
            var unpadded = value.TrimEnd(' ');
            var first =
                kind == "exact"
                    ? Match(unpadded, kind, pattern.TrimEnd(' '), DslCaseRule.Ordinal)
                    : Match(value, kind, pattern, DslCaseRule.Ordinal);
            var second = Match(unpadded, kind, pattern, DslCaseRule.Ordinal);

            return first == second ? first : null;
        }

        // MySQL：StartsWith / EndsWith（と LIKE の前方・後方一致）はパターンの末尾の空白を数えないので、外すと結果が変わる行は判定しない
        if (caseRule == DslCaseRule.MySql)
        {
            var result = Match(value, kind, pattern, rule);

            if (
                result is not null
                && kind is "prefix" or "suffix"
                && pattern.TrimEnd(' ').Length != pattern.Length
                && Match(value, kind, pattern.TrimEnd(' '), rule) != result
            )
            {
                return null;
            }

            return result;
        }

        if (caseRule != DslCaseRule.Collation || !column.NonUnicode)
        {
            return Match(value, kind, pattern, rule);
        }

        var trimmed = value.TrimEnd(' ').Length;
        var unknown = false;

        for (var length = value.Length; length >= trimmed; length--)
        {
            var result = Match(value[..length], kind, pattern, rule);

            if (result == true)
            {
                return true;
            }

            unknown |= result is null;
        }

        return unknown ? null : false;
    }

    /// <summary>文字列一致（大文字小文字の扱いは実装先ごと）</summary>
    private static bool? Match(string value, string kind, string pattern, DslCaseRule rule)
    {
        bool Test(Func<string, string> fold)
        {
            var v = fold(value);
            var p = fold(pattern);

            return kind switch
            {
                "contains" => v.Contains(p, StringComparison.Ordinal),
                "prefix" => v.StartsWith(p, StringComparison.Ordinal),
                "suffix" => v.EndsWith(p, StringComparison.Ordinal),
                _ => string.Equals(v, p, StringComparison.Ordinal),
            };
        }

        var ordinal = Test(s => s);

        return rule switch
        {
            DslCaseRule.Ordinal or DslCaseRule.PostgreSql or DslCaseRule.Oracle => ordinal,
            DslCaseRule.MySql => Test(MySqlKey) == ordinal ? ordinal : null,
            DslCaseRule.AsciiFold => Test(FoldAscii),
            DslCaseRule.Collation => Test(s => CollationKey(s, trimTrailing: kind == "exact"))
            == ordinal
                ? ordinal
                : null,
            _ => Test(FoldAscii) == ordinal && Test(s => s.ToLowerInvariant()) == ordinal
                ? ordinal
                : null,
        };
    }

    /// <summary>照合順序で同じとみなされる形（大文字小文字を畳み、等号の比較なら末尾の空白も除く）</summary>
    private static string CollationKey(string value, bool trimTrailing) =>
        trimTrailing ? value.ToLowerInvariant().TrimEnd(' ') : value.ToLowerInvariant();

    /// <summary>
    /// MySQL の既定の照合順序 <c>utf8mb4_0900_ai_ci</c> で同じとみなされる形の近似（<c>ß</c> を <c>ss</c> へ・分解して結合文字を外し・
    /// 大文字へ）。オラクルは畳んだ形で結果が変わる行を判定しないだけなので、近似で足りる（docs の表の EF Core（MySQL）の行）
    /// </summary>
    private static string MySqlKey(string value) =>
        string.Concat(
                value
                    .Replace("ß", "ss", StringComparison.Ordinal)
                    .Normalize(NormalizationForm.FormD)
                    .Where(c =>
                        CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                    )
            )
            .ToUpperInvariant();

    /// <summary>
    /// 比較（照合順序の実装先の文字列の =・&lt;&gt; は、照合順序で結果が変わる行を判定しない。PostgreSQL の固定長の列は末尾の空白を
    /// 無視する）
    /// </summary>
    private static bool? CompareUnder(
        object? value,
        DslOp op,
        object operand,
        DslCaseRule rule,
        DslColumn column
    )
    {
        var ordinal = Compare(value, op, operand);

        if (value is not string text || operand is not string other)
        {
            return ordinal;
        }

        switch (rule)
        {
            case DslCaseRule.Collation:
            {
                var folded = Compare(CollationKey(text, true), op, CollationKey(other, true));

                return folded == ordinal ? ordinal : null;
            }

            case DslCaseRule.MySql:
                return Compare(MySqlKey(text), op, MySqlKey(other)) == ordinal ? ordinal : null;

            // 読み戻した詰めた値の末尾の空白を外して比べる。比べる値の末尾の空白の扱いは docs が約束していないので、外しても外さなくても
            // 同じ結果の行だけを判定する
            case DslCaseRule.PostgreSql when column.Clr == DslClr.FixedString:
            {
                var bothTrimmed = Compare(text.TrimEnd(' '), op, other.TrimEnd(' '));

                return bothTrimmed == Compare(text.TrimEnd(' '), op, other) ? bothTrimmed : null;
            }

            default:
                return ordinal;
        }
    }

    /// <summary>IN（照合順序の実装先の文字列は、序数で一致しないが照合順序で一致する値があれば判定しない）</summary>
    /// <remarks>
    /// docs の Null compensation 節の IN の箇条の写し：リストの null は列の NULL にだけ一致し（C# の <c>Contains</c> と同じく
    /// null を含むリストは NULL の行に一致する）、null だけのリストは NULL の行だけ・空のリストはどの行にも一致しない。
    /// </remarks>
    private static bool? InUnder(
        object? value,
        IReadOnlyList<object?> values,
        DslCaseRule rule,
        DslColumn column
    )
    {
        if (value is null)
        {
            return values.Any(v => v is null);
        }

        // PostgreSQL の固定長の列：IN は = と同じく末尾の空白を無視する（CompareUnder と同じ読み方）
        if (
            rule == DslCaseRule.PostgreSql
            && column.Clr == DslClr.FixedString
            && value is string padded
        )
        {
            var trimmed = padded.TrimEnd(' ');
            var bothTrimmed = values.Any(v => v is string other && trimmed == other.TrimEnd(' '));
            var valueTrimmed = values.Any(v => v is string other && trimmed == other);

            return bothTrimmed == valueTrimmed ? bothTrimmed : null;
        }

        if (values.Any(v => v is not null && SameValue(value, v)))
        {
            return true;
        }

        var foldedMatch =
            value is string text
            && rule switch
            {
                DslCaseRule.Collation => values.Any(v =>
                    v is string other && CollationKey(text, true) == CollationKey(other, true)
                ),
                DslCaseRule.MySql => values.Any(v =>
                    v is string other && MySqlKey(text) == MySqlKey(other)
                ),
                _ => false,
            };

        return foldedMatch ? null : false;
    }

    /// <summary>ASCII の英字だけを小文字へ畳む</summary>
    private static string FoldAscii(string value) =>
        string.Create(
            value.Length,
            value,
            (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var c = source[i];
                    span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
                }
            }
        );

    /// <summary>C# の lifted な比較（NULL の列：= は偽・&lt;&gt; は真・順序比較は偽）</summary>
    private static bool Compare(object? value, DslOp op, object operand)
    {
        if (value is null)
        {
            return op == DslOp.Ne;
        }

        var order = CompareValues(value, operand);

        return op switch
        {
            DslOp.Eq => order == 0,
            DslOp.Ne => order != 0,
            DslOp.Lt => order < 0,
            DslOp.Le => order <= 0,
            DslOp.Gt => order > 0,
            _ => order >= 0,
        };
    }

    private static bool SameValue(object value, object other) => CompareValues(value, other) == 0;

    /// <summary>読み戻した値どうしが等しいか（NULL どうしは等しい）</summary>
    public static bool SameStoredValue(object? value, object? other) =>
        value is null || other is null ? value is null && other is null : SameValue(value, other);

    /// <summary>
    /// 並び替えの昇順での 2 つの値の順序（NULL は <paramref name="nulls"/> の位置・数値は数値・日時は日時・文字列は序数）
    /// </summary>
    /// <remarks>
    /// 文字列の比べ方は主キー（GuidKey）の並びでだけ使う。実装先をまたぐ文字列の並びは docs が約束していない（インメモリは序数・
    /// SQL Server は照合順序）ので、主キーより前の並びには文字列の列を置かない。
    /// </remarks>
    /// <param name="value">左の値（読み戻した値）</param>
    /// <param name="other">右の値（読み戻した値）</param>
    /// <param name="nulls">NULL を置く位置</param>
    public static int CompareForOrdering(object? value, object? other, DslNullOrder nulls)
    {
        if (value is null || other is null)
        {
            if (value is null && other is null)
            {
                return 0;
            }

            var first = nulls == DslNullOrder.FirstWhenAscending ? -1 : 1;

            return value is null ? first : -first;
        }

        return CompareValues(value, other);
    }

    /// <summary>値どうしの順序（数値は decimal へ・文字列は序数・Guid と日時はそのまま）</summary>
    private static int CompareValues(object value, object other) =>
        (value, other) switch
        {
            (string a, string b) => string.CompareOrdinal(a, b),
            (Guid a, Guid b) => a.Equals(b) ? 0 : 1,
            (bool a, bool b) => a.CompareTo(b),
            // バイナリは等しいかだけを比べる（順序の比較は型の検査が断り、並び替えの列にもしない）
            (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b) ? 0 : 1,
            (DateTime a, DateTime b) => a.CompareTo(b),
            (TimeSpan a, TimeSpan b) => a.CompareTo(b),
            _ => Convert
                .ToDecimal(value, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDecimal(other, CultureInfo.InvariantCulture)),
        };

    // ---------------- データの値 ----------------

    /// <summary>列の種類ごとの値の候補（境界値を含む）</summary>
    public static IReadOnlyList<object> ValuePool(DslClr clr) =>
        clr switch
        {
            DslClr.String =>
            [
                "",
                "a",
                "A",
                "abc",
                "ABC",
                "Abc",
                "xabcx",
                "a%b",
                "a_b",
                "%",
                "_",
                "ä",
                "Ä",
                "日本",
                "a b",
                "abc ",
                "ab",
                "a'b",
                "'",
                "''",
            ],
            DslClr.FixedString =>
            [
                "",
                "a",
                "A",
                "abc",
                "ABC",
                "a%b",
                "a_b",
                "x",
                "abcdefghij",
                "a'b",
                "'",
            ],
            DslClr.Int64 => [long.MinValue, -1L, 0L, 1L, 2L, 10L, 100L, long.MaxValue],
            DslClr.Int32 => [int.MinValue, -1, 0, 1, 5, int.MaxValue],
            DslClr.Int16 => [short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue],
            DslClr.Byte => [(byte)0, (byte)1, (byte)127, (byte)128, (byte)255],
            DslClr.SByte => [sbyte.MinValue, (sbyte)-1, (sbyte)0, (sbyte)1, sbyte.MaxValue],
            DslClr.UInt16 => [(ushort)0, (ushort)1, (ushort)32768, ushort.MaxValue],
            DslClr.UInt32 => [0u, 1u, 2147483648u, uint.MaxValue],
            DslClr.UInt64 => [0UL, 1UL, 9223372036854775808UL, ulong.MaxValue],
            DslClr.Decimal => [-99999999.99m, -1.5m, 0m, 0.01m, 1.5m, 2.25m, 10m, 99999999.99m],
            DslClr.DateTime =>
            [
                new DateTime(1999, 12, 31, 23, 59, 59),
                new DateTime(2000, 1, 1),
                new DateTime(2024, 2, 29),
                new DateTime(2024, 2, 29, 12, 34, 56),
            ],
            DslClr.Guid =>
            [
                new Guid("00000000-0000-0000-0000-000000000001"),
                new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
                new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            ],
            DslClr.GuidKey =>
            [
                "00000000-0000-0000-0000-000000000001",
                "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                "6F9619FF-8B86-D011-B42D-00C04FC964FF",
                "ffffffff-ffff-ffff-ffff-ffffffffffff",
                "6f9619ff-8b86-d011-b42d-00c04fc964f'",
            ],
            DslClr.Boolean => [false, true],
            // 末尾の 0x00 だけが違う組（01・0100・010000、空と 00）を持つ（SQL Server は短い側を 0x00 で詰めて比べる＝docs の
            // バイナリの段落）。長さは族の単位のバイナリの列（4 バイト）に収まる
            DslClr.Binary =>
            [
                Array.Empty<byte>(),
                new byte[] { 0x00 },
                new byte[] { 0x01 },
                new byte[] { 0x01, 0x00 },
                new byte[] { 0x01, 0x00, 0x00 },
                new byte[] { 0x00, 0x01 },
                new byte[] { 0xFF },
                new byte[] { 0x01, 0x02, 0x03 },
                new byte[] { 0x41 },
            ],
            DslClr.TimeSpan => TimeOfDayPool,
            _ => throw new ArgumentOutOfRangeException(nameof(clr)),
        };

    /// <summary>
    /// 日の中（0〜24 時間未満）の TimeSpan の値の池（マイクロ秒の粒度＝PostgreSQL・MySQL が端数を落とさない値。docs の
    /// マイクロ秒の既知の割り切りに当たらない）
    /// </summary>
    public static IReadOnlyList<object> TimeOfDayPool { get; } =
    [
        TimeSpan.Zero,
        TimeSpan.FromTicks(10),
        TimeSpan.FromSeconds(1),
        new TimeSpan(0, 9, 59, 59).Add(TimeSpan.FromTicks(9_999_990)),
        TimeSpan.FromHours(10),
        TimeSpan.FromHours(12),
        new TimeSpan(0, 12, 0, 0, 500),
        new TimeSpan(0, 23, 59, 59),
        new TimeSpan(0, 23, 59, 59).Add(TimeSpan.FromTicks(9_999_990)),
    ];

    /// <summary>
    /// 日の中の値の池に、100 ナノ秒の端数を持つ値（SQL Server の time の上限を含む）を足したもの（SQL Server・SQLite は 7 桁の端数を持つ）
    /// </summary>
    public static IReadOnlyList<object> TimeOfDayTickPool { get; } =
    [
        .. TimeOfDayPool,
        TimeSpan.FromHours(10).Add(TimeSpan.FromTicks(1)),
        TimeSpan.FromDays(1) - TimeSpan.FromTicks(1),
    ];

    /// <summary>
    /// 日の中の値の池に、負の値と 1 日以上の値を足したもの（MySQL の time の範囲 ±838:59:59 の中・マイクロ秒の粒度）
    /// </summary>
    public static IReadOnlyList<object> WideTimeSpanPool { get; } =
    [
        .. TimeOfDayPool,
        TimeSpan.FromDays(1),
        new TimeSpan(1, 1, 0, 0),
        -TimeSpan.FromTicks(10),
        -TimeSpan.FromHours(1),
        new TimeSpan(2, 3, 4, 5),
        new TimeSpan(34, 22, 59, 59),
        -new TimeSpan(34, 22, 59, 59),
    ];

    /// <summary>
    /// 0〜24 時間未満の外の比べる値（上の端ちょうど・端のすぐ外・負・1 日超・TimeSpan の両端をマイクロ秒に丸めた値）。行には書かない
    /// </summary>
    public static IReadOnlyList<object> OutOfDayPool { get; } =
    [
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(1) + TimeSpan.FromTicks(10),
        -TimeSpan.FromTicks(10),
        -TimeSpan.FromHours(1),
        new TimeSpan(2, 3, 4, 5),
        TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks / 10 * 10),
        TimeSpan.FromTicks(TimeSpan.MinValue.Ticks / 10 * 10),
    ];

    /// <summary>列の値の候補（列が値の池を持てばそれ、無ければ種類ごとの値の池）</summary>
    public static IReadOnlyList<object> ValuePool(DslColumn column) =>
        column.Values ?? ValuePool(column.Clr);

    /// <summary>パラメータと IN のリストの値の候補（列がパラメータだけの値の池を持てばそれ、無ければ列の値の池）</summary>
    public static IReadOnlyList<object> OperandPool(DslColumn column) =>
        column.OperandValues ?? ValuePool(column);

    /// <summary>数値のリテラルの綴り</summary>
    public static string NumberText(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture)!;

    /// <summary>型トークン（型トークンでパラメータを宣言するとき）</summary>
    public static string TypeToken(DslClr clr) =>
        clr switch
        {
            DslClr.String or DslClr.FixedString or DslClr.GuidKey => "string",
            DslClr.Int64 => "int64",
            DslClr.Int32 => "int32",
            DslClr.Int16 => "smallint",
            DslClr.Byte => "tinyint",
            DslClr.Decimal => "decimal(10,2)",
            DslClr.DateTime => "datetime",
            DslClr.Guid => "guid",
            DslClr.Boolean => "boolean",
            DslClr.Binary => "binary(4)",
            DslClr.TimeSpan => "time",
            _ => throw new ArgumentOutOfRangeException(nameof(clr), clr, "型トークンの無い種類"),
        };

    /// <summary>型トークンを持つ種類か（符号なしと sbyte は列から型を決める）</summary>
    public static bool HasTypeToken(DslClr clr) =>
        clr is not (DslClr.SByte or DslClr.UInt16 or DslClr.UInt32 or DslClr.UInt64);
}
