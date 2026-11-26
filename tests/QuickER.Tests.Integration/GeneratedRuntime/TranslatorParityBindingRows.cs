using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>値の束縛の生成の表の行が比べる表（QuickER 版のフィクスチャのエンティティ）</summary>
public enum BindingEntity
{
    /// <summary>probes（日時の 4 つの列と decimal の列 amount）</summary>
    Probe,

    /// <summary>small_child（NULL 許容の int の列 big_parent_id）</summary>
    SmallChild,

    /// <summary>big_parent（bigint の主キー id）</summary>
    BigParent,
}

/// <summary>値の束縛の生成の表の 1 行の入力（因子の水準の割り当てと、そこから組む述語）</summary>
/// <param name="Levels">因子 → 水準（全因子）</param>
public sealed record TranslatorParityBinding(IReadOnlyDictionary<string, string> Levels)
{
    /// <summary>列の型の水準</summary>
    public string ColumnType => Levels[TranslatorParityBindingRows.ColumnType];

    /// <summary>クエリの経路の水準</summary>
    public string QueryPath => Levels[TranslatorParityBindingRows.QueryPath];

    /// <summary>比べる表</summary>
    public BindingEntity Entity => TranslatorParityBindingRows.EntityOf(ColumnType);

    /// <summary>QuickER 版のフィクスチャのエンティティの型</summary>
    public Type EntityType =>
        Entity switch
        {
            BindingEntity.Probe => typeof(ProbeEntity),
            BindingEntity.SmallChild => typeof(SmallChildEntity),
            _ => typeof(BigParentEntity),
        };

    /// <summary>述語（QuickER 版のフィクスチャのエンティティの型の上。EF Core 版は付け替えて使う）</summary>
    public LambdaExpression Predicate =>
        TranslatorParityBindingRows.BuildPredicate(this, null, null);

    /// <summary>
    /// 比べる値を変えた述語（既知の割り切りの写しが、DB が値を受け取った形を C# で評価するのに使う）
    /// </summary>
    /// <param name="single">単一の値の変え方（null なら変えない）</param>
    /// <param name="element">リスト・配列の要素の変え方（null なら変えない）</param>
    public LambdaExpression PredicateWith(
        Func<object, object>? single,
        Func<object, object>? element
    ) => TranslatorParityBindingRows.BuildPredicate(this, single, element);

    /// <summary>
    /// 値を変える明示の変換を外した述語（列の側は変換しない列と、値の側は変換する前の値と比べる。実装先が変換を外して比べた形を C# で評価する）
    /// </summary>
    /// <param name="drop">外す側</param>
    public LambdaExpression PredicateWithout(NarrowingDrop drop) =>
        TranslatorParityBindingRows.BuildPredicate(this, null, null, drop);

    /// <summary>比べる型（列と値を C# の 2 項の昇格でそろえた型。Nullable を外した型）</summary>
    public Type ComparedType => TranslatorParityBindingRows.ComparedTypeOf(this);
}

/// <summary>述語から外す値を変える明示の変換の側（既知の失敗の写しが、変換を外した形の C# の答えを作るのに使う）</summary>
[Flags]
public enum NarrowingDrop
{
    /// <summary>外さない</summary>
    None = 0,

    /// <summary>列の側の変換を外す</summary>
    Column = 1,

    /// <summary>値の側の変換を外す（変換する前の double の値と比べる）</summary>
    Value = 2,

    /// <summary>両方を外す</summary>
    Both = Column | Value,
}

/// <summary>述語の値の側に置く「捕捉した変数」（C# のコンパイラがクロージャのフィールドとして書く形）</summary>
/// <typeparam name="T">値の型</typeparam>
/// <param name="value">値</param>
public sealed class TranslatorParityBindingBox<T>(T value)
{
    /// <summary>値（式の木では定数のフィールドの参照）</summary>
    public readonly T Value = value;
}

/// <summary>述語の値の側に置く「メソッド呼び出し」（行に依らない呼び出し。式の木の評価を通る）</summary>
public static class TranslatorParityBindingValues
{
    /// <summary>受け取った値をそのまま返す（DateTime.UtcNow のような呼び出しの代わりに、決まった値を返す）</summary>
    /// <typeparam name="T">値の型</typeparam>
    /// <param name="value">値</param>
    public static T Same<T>(T value) => value;
}

/// <summary>
/// 値の束縛の生成の表：列の型 × 列の側の形 × 比べ方 × 値の数 × 値の池 × Kind × 値の形 × クエリの経路を、網羅の配列（種は固定）と
/// 必ず含む行で組み、述語の表の基底 2 つ（QuickER 版・インメモリと EF Core 版）が C# の評価と照らす
/// </summary>
/// <remarks>
/// <para>
/// 因子は分岐の軸の台帳の値の束縛の標準の軸（<c>BranchAxisLedgerEntries.Areas</c> の value-binding）と 1:1。行の Id は水準の名前から作る
/// （<c>vb-{列の型}-{列の側}-{比べ方}-{値の数}-{値の池}-{Kind}-{値の形}-{経路}</c>）。値の池の水準の名前は値の池
/// （tests/QuickER.Tests/TestSupport/ValuePools）の値の名前そのもの。図・フィクスチャは変えず、述語の表の図の既存の列を使う。
/// </para>
/// <para>
/// 比べる値と列の型が違うときは C# の 2 項の昇格の写しで片側を変換する（int と decimal の値なら列を decimal へ・long の列と decimal の値なら
/// 列を decimal へ・decimal の列と整数の値なら値を decimal へ）。水準「素の列」はこの暗黙の変換を含み、列の側の明示の変換は水準
/// 「値を保つ変換」だけが足す。
/// </para>
/// <para>
/// 区切り a のフェーズ 2 で足した水準（Kind の Utc・Local、日付部品、値を変える明示の変換〔列の側・値の側〕、ラムダを含む値の式、
/// 比べ方の &lt;=・&gt;、値の池の整数の値の decimal と縮める変換の元の値）と指摘のセルの必ず含む行は、分岐の軸の台帳の項目の札
/// （<c>RowTag.Ledger</c>）を持つ。作らない形は <c>TranslatorParityBindingRowsNotGenerated</c> に宣言する。
/// </para>
/// </remarks>
public static class TranslatorParityBindingRows
{
    /// <summary>行の Id の接頭辞</summary>
    public const string IdPrefix = "vb-";

    /// <summary>台帳の部分の名前</summary>
    public const string Part = "TranslatorParityBindings";

    /// <summary>因子：列の型</summary>
    public const string ColumnType = "ColumnType";

    /// <summary>因子：列の側の形</summary>
    public const string ColumnShape = "ColumnShape";

    /// <summary>因子：比べ方</summary>
    public const string Comparison = "Comparison";

    /// <summary>因子：値の数</summary>
    public const string ValueCount = "ValueCount";

    /// <summary>因子：値の池</summary>
    public const string ValuePool = "ValuePool";

    /// <summary>因子：比べる値の DateTimeKind</summary>
    public const string DateTimeKind = "DateTimeKind";

    /// <summary>因子：値の側の式の形</summary>
    public const string ValueShape = "ValueShape";

    /// <summary>因子：クエリの経路</summary>
    public const string QueryPath = "QueryPath";

    // ---- 水準 ----

    /// <summary>列の型：date（probes.day）</summary>
    public const string Date = "date";

    /// <summary>列の型：datetime2（probes.at）</summary>
    public const string DateTime2 = "datetime2";

    /// <summary>列の型：datetime（probes.legacy_at）</summary>
    public const string LegacyDateTime = "datetime";

    /// <summary>列の型：smalldatetime（probes.small_at）</summary>
    public const string SmallDateTime = "smalldatetime";

    /// <summary>列の型：int（small_child.big_parent_id・NULL 許容）</summary>
    public const string Int = "int";

    /// <summary>列の型：bigint（big_parent.id）</summary>
    public const string BigInt = "bigint";

    /// <summary>列の型：decimal(10,2)（probes.amount・NULL 許容）</summary>
    public const string Decimal = "decimal";

    /// <summary>列の型：NULL 許容の bit（probes.flag・bool?）</summary>
    public const string Bit = "bit";

    /// <summary>列の側の形：素の列</summary>
    public const string Plain = "plain";

    /// <summary>列の側の形：値を保つ変換（int → long・bigint → decimal）</summary>
    public const string PreservingConversion = "preserving-conversion";

    /// <summary>列の側の形：日付部品（日時の列の <c>.Value.Date</c>。NULL の行は <c>!= null</c> で外す）</summary>
    public const string DatePart = "date-part";

    /// <summary>
    /// 列の側の形・値の形：値を変える明示の変換（列の側は int → byte・bigint → short・decimal → int、値の側は double の値を
    /// int の列と decimal の列なら int・bigint の列なら long へ）
    /// </summary>
    public const string NarrowingCast = "narrowing-cast";

    /// <summary>比べ方：&lt;=</summary>
    public const string LessOrEqual = "less-or-equal";

    /// <summary>比べ方：&gt;</summary>
    public const string Greater = "greater";

    /// <summary>値の数：単一の値</summary>
    public const string Single = "single";

    /// <summary>値の数：List の IN</summary>
    public const string List = "list";

    /// <summary>値の数：配列の IN（Enumerable.Contains）</summary>
    public const string Array = "array";

    /// <summary>Kind：Unspecified</summary>
    public const string Unspecified = "unspecified";

    /// <summary>Kind：Utc</summary>
    public const string Utc = "utc";

    /// <summary>Kind：Local</summary>
    public const string Local = "local";

    /// <summary>Kind：日時でない値</summary>
    public const string NoKind = "none";

    /// <summary>値の形：捕捉した変数</summary>
    public const string Captured = "captured";

    /// <summary>値の形：定数</summary>
    public const string Constant = "constant";

    /// <summary>値の形：メソッド呼び出し</summary>
    public const string MethodCall = "method-call";

    /// <summary>
    /// 値の形：ラムダを含む式（単一の値は捕捉したリストの <c>Max(p =&gt; p)</c>、リスト・配列は捕捉したものの
    /// <c>Select(p =&gt; p).ToList()</c>・<c>ToArray()</c>。どれも行に依らない）
    /// </summary>
    public const string Lambda = "lambda";

    /// <summary>値の形：値の側の暗黙の変換（捕捉した char の変数を整数の列と比べる＝C# は char を int・long へ広げる）</summary>
    public const string ImplicitChar = "implicit-char";

    /// <summary>値の形：捕捉した <c>ImmutableArray&lt;T&gt;</c>（値型のコレクション）のインスタンスの <c>Contains</c>（List の IN とだけ組む）</summary>
    public const string ImmutableArrayShape = "immutable-array";

    /// <summary>経路：Query()</summary>
    public const string Query = "query";

    /// <summary>経路：Query().ExecuteDeleteAsync（cascadeDelete なし）</summary>
    public const string ExecuteDelete = "execute-delete";

    /// <summary>経路：QuickErDbContext の ExecuteUpdate（SET は列を自分自身へ代入して値を変えず、WHERE だけを比べる。EF Core 版だけ）</summary>
    public const string ExecuteUpdate = "execute-update";

    /// <summary>比べ方の水準（演算子の水準は単一の値と、IN・NOT IN はリスト・配列と組む）</summary>
    private static readonly string[] Comparisons =
    [
        "equal",
        "not-equal",
        "less",
        "greater-or-equal",
        "in",
        "not-in",
    ];

    /// <summary>台帳の項目の札（区切り a のフェーズ 2 で足した水準と必ず含む行）</summary>
    private static RowTag Ledger(string itemId) => RowTag.Ledger(itemId);

    /// <summary>低の指摘の札（3 回目の効果測定の記録の指摘。区切り a のフェーズ 4 で足した水準）</summary>
    private static RowTag Finding(string reason) => RowTag.Reason(RowTagKind.ReviewFinding, reason);

    /// <summary>3 回目の効果測定の記録</summary>
    private const string Measure3Record = "review-measure3-2026-11-26";

    /// <summary>XT5 の水準（NULL 許容の bool の列と bool の値の池）の札の理由</summary>
    private const string Xt5Reason =
        $"XT5（{Measure3Record} v10）：NULL 許容の bool の列。等値・IN の比べ方 × 値の形の軸で交差させ、順序の比べ方は C# で書けない。.Value の素の述語と並び替えは手書きの述語の表の行が受け持つ";

    /// <summary>数値の値の池のうち、値を変える明示の変換の元の値（縮める変換の形とだけ組む）</summary>
    private static bool IsNarrowingPool(string pool) => NarrowingPool.Names.Contains(pool);

    /// <summary>網羅の配列の種</summary>
    private const int ArraySeed = 20261127;

    /// <summary>日時の列の型</summary>
    private static readonly string[] DateTimeColumns =
    [
        Date,
        DateTime2,
        LegacyDateTime,
        SmallDateTime,
    ];

    /// <summary>数値の列の型</summary>
    private static readonly string[] NumericColumns = [Int, BigInt, Decimal];

    /// <summary>数値の値の池の名前（整数の値・小数の値・列の型の外の値）</summary>
    private static readonly string[] NumericPools =
    [
        DecimalPool.Integral,
        DecimalPool.Fraction,
        DecimalPool.BeyondColumnRange,
    ];

    /// <summary>次元表</summary>
    public static DimensionTable Table { get; } =
        new(
            EngineIds.GeneratedRuntime,
            [
                new Factor(
                    ColumnType,
                    [
                        .. DateTimeColumns.Concat(NumericColumns).Select(name => new Level(name)),
                        new Level(Bit) { Tag = Finding(Xt5Reason) },
                    ],
                    "比べる列の型（述語の表の図の列）"
                ),
                new Factor(
                    ColumnShape,
                    [
                        new Level(Plain),
                        new Level(PreservingConversion),
                        new Level(DatePart) { Tag = Ledger("xb1") },
                        new Level(NarrowingCast) { Tag = Ledger("xt4") },
                    ],
                    "列の側の式の形（素の列・値を保つ明示の変換・日付部品・値を変える明示の変換）"
                ),
                new Factor(
                    Comparison,
                    [
                        .. Comparisons.Select(name => new Level(name)),
                        new Level(LessOrEqual) { Tag = Ledger("xb1") },
                        new Level(Greater) { Tag = Ledger("xb1") },
                    ],
                    "比べ方（等値・否定・順序の 4 つ・IN・NOT IN）"
                ),
                new Factor(
                    ValueCount,
                    [new Level(Single), new Level(List), new Level(Array)],
                    "値の数（単一の値・List・配列）"
                ),
                new Factor(
                    ValuePool,
                    [
                        .. DateTimePool.Names.Concat(NumericPools).Select(name => new Level(name)),
                        new Level(DecimalPool.IntegralDecimal) { Tag = Ledger("ha4") },
                        .. NarrowingPool.Names.Select(name => new Level(name)
                        {
                            Tag = Ledger("xt4"),
                        }),
                        .. BooleanPool.Names.Select(name => new Level(name)
                        {
                            Tag = Finding(Xt5Reason),
                        }),
                    ],
                    "値の池の値（tests/QuickER.Tests/TestSupport/ValuePools の名前）"
                ),
                new Factor(
                    DateTimeKind,
                    [
                        new Level(Unspecified),
                        new Level(Utc) { Tag = Ledger("ha1") },
                        new Level(Local) { Tag = Ledger("ha1") },
                        new Level(NoKind),
                    ],
                    "比べる値の DateTimeKind（日時でない値は none）"
                ),
                new Factor(
                    ValueShape,
                    [
                        new Level(Captured),
                        new Level(Constant),
                        new Level(MethodCall),
                        new Level(Lambda) { Tag = Ledger("ha4") },
                        new Level(NarrowingCast) { Tag = Ledger("xt4") },
                        new Level(ImplicitChar)
                        {
                            Tag = Finding(
                                $"XB3（{Measure3Record} v10）：整数の列と char の値。値の側の評価が char を int へ広げて束縛する（区切り a の XT4 の直しと同じ腕）。列の型（int・bigint）× 比べ方の軸で交差させ、char のリスト・配列は C# で書けない"
                            ),
                        },
                        new Level(ImmutableArrayShape)
                        {
                            Tag = Finding(
                                $"HA3（{Measure3Record} v7）：値型のコレクション（ImmutableArray）の Contains。SQLite の EF Core 版の整数と decimal の書き換えが値型を IEnumerable へ通さず例外になった。列の型 × 値の池の軸で交差させ、配列の静的な Contains は既存の配列の水準が受け持つ"
                            ),
                        },
                    ],
                    "値の側の式の形（捕捉した変数・定数・メソッド呼び出し・ラムダを含む式・値を変える明示の変換・char の暗黙の変換・ImmutableArray の Contains）"
                ),
                new Factor(
                    QueryPath,
                    [new Level(Query), new Level(ExecuteDelete), new Level(ExecuteUpdate)],
                    "クエリの経路（Query・ExecuteDelete・EF Core 版の ExecuteUpdate の WHERE）"
                ),
            ],
            [
                new FactorGroup(ColumnType, ColumnShape, ValuePool),
                new FactorGroup(ColumnType, ValueCount, ValuePool),
                new FactorGroup(ColumnType, DateTimeKind, ValuePool),
                new FactorGroup(ColumnType, ValueShape, ValuePool),
            ],
            IsValid
        );

    /// <summary>
    /// 網羅の配列に必ず含める完全な行（3 回目の効果測定の指摘のセル＝分岐の軸の台帳の ha1・ha2・ha4・xb1・xt4 の再現の入力。どれも台帳の札を持つ）
    /// </summary>
    public static IReadOnlyList<RequiredRow> RequiredRows { get; } =
    [
        // HA1：PostgreSQL の EF Core 版で、date の列と Kind=Utc の時刻つきの値を比べると、インターセプターが timestamp で送り直し、Npgsql が断る
        Required(
            "ha1-date-less-utc-time-of-day",
            "ha1",
            Date,
            Plain,
            "less",
            Single,
            DateTimePool.TimeOfDay,
            Utc,
            Captured,
            Query
        ),
        Required(
            "ha1-date-equal-utc-time-of-day",
            "ha1",
            Date,
            Plain,
            "equal",
            Single,
            DateTimePool.TimeOfDay,
            Utc,
            Captured,
            Query
        ),
        // HA1 の隣（再現の入力でない）：順序の向き・値の池・経路を変えても同じ腕で断る（網羅の配列は ha1 の水準と ExecuteDelete の組を持たない）
        Required(
            "ha1-date-greater-or-equal-utc-tick-before-midnight-execute-delete",
            "ha1",
            Date,
            Plain,
            "greater-or-equal",
            Single,
            DateTimePool.TickBeforeMidnight,
            Utc,
            Captured,
            ExecuteDelete
        ),
        // HA2：PostgreSQL の EF Core 版で、date の列への時刻つきの値のリストは配列のパラメータで送り直されず、時刻が落ちる
        Required(
            "ha2-date-in-list-time-of-day",
            "ha2",
            Date,
            Plain,
            "in",
            List,
            DateTimePool.TimeOfDay,
            Unspecified,
            Captured,
            Query
        ),
        Required(
            "ha2-date-not-in-list-time-of-day",
            "ha2",
            Date,
            Plain,
            "not-in",
            List,
            DateTimePool.TimeOfDay,
            Unspecified,
            Captured,
            Query
        ),
        // HA4：SQLite の EF Core 版で、比べる値の式がラムダを含むと書き換えを飛ばし、整数の列と整数の値の decimal の IN を文字列で比べる
        Required(
            "ha4-int-in-decimal-lambda-list",
            "ha4",
            Int,
            Plain,
            "in",
            List,
            DecimalPool.IntegralDecimal,
            NoKind,
            Lambda,
            Query
        ),
        // HA4 の隣（再現の入力でない）：bigint の列と、ラムダを含む式で作った単一の値の等値（網羅の配列はラムダの値と整数の値の decimal の
        // 等値を、セルの IN のほかに持たない）
        Required(
            "ha4-bigint-equal-decimal-lambda-single",
            "ha4",
            BigInt,
            Plain,
            "equal",
            Single,
            DecimalPool.IntegralDecimal,
            NoKind,
            Lambda,
            Query
        ),
        // XB1：SQL Server の QuickER 版で、日付部品と比べる値が datetime で束縛され、1/300 秒へ丸められる（1753 年より前は例外）
        Required(
            "xb1-date-part-equal-tick-before-midnight",
            "xb1",
            DateTime2,
            DatePart,
            "equal",
            Single,
            DateTimePool.TickBeforeMidnight,
            Unspecified,
            Captured,
            Query
        ),
        Required(
            "xb1-date-part-less-or-equal-end-of-day",
            "xb1",
            DateTime2,
            DatePart,
            LessOrEqual,
            Single,
            DateTimePool.TickBeforeMidnight,
            Unspecified,
            Captured,
            Query
        ),
        Required(
            "xb1-date-part-greater-before-1753",
            "xb1",
            DateTime2,
            DatePart,
            Greater,
            Single,
            DateTimePool.BeforeSqlDateTimeMin,
            Unspecified,
            Captured,
            Query
        ),
        // XB1 の隣（再現の入力でない）：日付部品 × 値の数（単一・List・配列）× 値の形（捕捉・定数・メソッド呼び出し・ラムダ）の組を、QuickER 版が
        // 流す経路（Query・ExecuteDelete）にそろえる。網羅の配列が持たない組は配列 × メソッド呼び出しだけ（IN の日付部品は区切り a のフェーズ 3 で翻訳できるようにした）
        Required(
            "xb1-date-part-in-array-method-call",
            "xb1",
            DateTime2,
            DatePart,
            "in",
            Array,
            DateTimePool.TimeOfDay,
            Unspecified,
            MethodCall,
            Query
        ),
        // XT4：式の翻訳器が値を変える明示の変換を外す（値の側の (int)ratio・列の側の (short)x.Id）
        Required(
            "xt4-value-narrowing-cast-equal",
            "xt4",
            Int,
            Plain,
            "equal",
            Single,
            NarrowingPool.FractionCutByCast,
            NoKind,
            NarrowingCast,
            Query
        ),
        Required(
            "xt4-column-narrowing-cast-equal",
            "xt4",
            BigInt,
            NarrowingCast,
            "equal",
            Single,
            NarrowingPool.BeyondCastRange,
            NoKind,
            Captured,
            Query
        ),
        // XT4 の隣（再現の入力でない・今は緑）：IN の並び（List の初期化子・配列の初期化子）の要素に書いた値の側の変換は、並びの式ごと
        // 評価するので外さない。直すときに並びの中の変換を外す退行を見張る
        Required(
            "xt4-value-narrowing-cast-in-list",
            "xt4",
            Int,
            Plain,
            "in",
            List,
            NarrowingPool.FractionCutByCast,
            NoKind,
            NarrowingCast,
            Query
        ),
    ];

    /// <summary>必ず含む行（全因子の水準を宣言の順に並べ、台帳の札を付ける）</summary>
    private static RequiredRow Required(
        string id,
        string ledgerItem,
        string columnType,
        string columnShape,
        string comparison,
        string count,
        string pool,
        string kind,
        string valueShape,
        string path
    ) =>
        new(
            id,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ColumnType] = columnType,
                [ColumnShape] = columnShape,
                [Comparison] = comparison,
                [ValueCount] = count,
                [ValuePool] = pool,
                [DateTimeKind] = kind,
                [ValueShape] = valueShape,
                [QueryPath] = path,
            }
        )
        {
            Tag = Ledger(ledgerItem),
        };

    /// <summary>必ず含む行の Id から、その行の水準の割り当て（網羅の配列の行の Id）を引く</summary>
    /// <param name="requiredId">必ず含む行の Id</param>
    public static string? RowIdOfRequired(string requiredId) =>
        RequiredRows.FirstOrDefault(row => row.Id == requiredId) is { } required
            ? IdOf(required.Assignment)
            : null;

    /// <summary>網羅の配列（全行）。配列に含まれない必ず含む行は後ろへ足す</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> AllRows { get; } =
        WithRequiredRows(CoveringArray.Generate(Table, ArraySeed));

    /// <summary>行の表（Id は水準の名前から作る）</summary>
    public static ScenarioTable<TranslatorParityBinding> Rows { get; } =
        new(
            EngineIds.GeneratedRuntime,
            AllRows.Select(levels => new ScenarioRow<TranslatorParityBinding>(
                IdOf(levels),
                null,
                Describe(levels),
                new TranslatorParityBinding(levels)
            ))
        );

    /// <summary>表を流す実装先（RuntimeTarget の名前・述語の表の派生を持つ 8 つ）</summary>
    public static IReadOnlyList<string> Targets { get; } =
    [
        nameof(RuntimeTarget.AdoSqlServer),
        nameof(RuntimeTarget.AdoSqlite),
        nameof(RuntimeTarget.InMemory),
        nameof(RuntimeTarget.EfCoreSqlServer),
        nameof(RuntimeTarget.EfCoreSqlite),
        nameof(RuntimeTarget.EfCorePostgreSql),
        nameof(RuntimeTarget.EfCoreMySql),
        nameof(RuntimeTarget.EfCoreOracle),
    ];

    /// <summary>台帳の部分の名前（<c>GeneratedRuntime/TranslatorParityBindings</c>）</summary>
    public static string TablePart { get; } =
        ProtectionNames.EnginePart(EngineIds.GeneratedRuntime, Part);

    /// <summary>台帳の行（宣言は全行の後に置く＝静的な初期化の順）</summary>
    public static EngineDescriptor Descriptor { get; } =
        new(
            EngineIds.GeneratedRuntime,
            Part,
            "値の束縛の生成の表（列の型・列の側の形・比べ方・値の数・値の池・Kind・値の形・クエリの経路を、QuickER 版・インメモリ・EF Core 版の 5 方言で C# の評価と照らす）",
            Table,
            AllRows.Count,
            [
                Runtime + "TranslatorParityBindingRows.cs",
                Runtime + "TranslatorParityBindingRun.cs",
                Runtime + "TranslatorParityBindingKnownFailures.cs",
                Runtime + "TranslatorParityBindingEvidence.cs",
                Runtime + "TranslatorParityRuntimeTestsBase.cs",
                Runtime + "TranslatorParityAdoSqlServerRuntimeTests.cs",
                Runtime + "TranslatorParityAdoSqliteRuntimeTests.cs",
                Runtime + "TranslatorParityInMemoryRuntimeTests.cs",
                Runtime + "TranslatorParityEfCoreRuntimeTestsBase.cs",
                Runtime + "TranslatorParityEfCoreBinding.cs",
                Runtime + "TranslatorParityEfCoreSqlServerRuntimeTests.cs",
                Runtime + "TranslatorParityEfCoreSqliteRuntimeTests.cs",
                Runtime + "TranslatorParityEfCorePostgreSqlRuntimeTests.cs",
                Runtime + "TranslatorParityEfCoreMySqlRuntimeTests.cs",
                Runtime + "TranslatorParityEfCoreOracleRuntimeTests.cs",
                "tests/QuickER.Tests/TestSupport/ValuePools/PoolValue.cs",
                "tests/QuickER.Tests/TestSupport/ValuePools/DateTimePool.cs",
                "tests/QuickER.Tests/TestSupport/ValuePools/IntegerPool.cs",
                "tests/QuickER.Tests/TestSupport/ValuePools/DecimalPool.cs",
                "tests/QuickER.Tests/TestSupport/ValuePools/NarrowingPool.cs",
            ],
            [
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_05_QueryPipeline.scriban",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_06_RemoteSaveInfraAndDI.scriban",
                "src/QuickER.CodeGen.CSharp/Templates/CSharpRuntime/_08_EfCore.scriban",
                "src/QuickER.Runtime",
                "src/QuickER.Runtime.EntityFrameworkCore",
                "src/QuickER.Runtime.InMemory",
                "src/QuickER.Runtime.SqlServer",
                "src/QuickER.Runtime.Sqlite",
            ]
        );

    private const string Runtime = "tests/QuickER.Tests.Integration/GeneratedRuntime/";

    /// <summary>QuickER 版とインメモリの派生が流す行（ExecuteUpdate の経路は EF Core 版だけ）</summary>
    public static TheoryData<string> QuickErIds() => IdsFor(RuntimeTarget.AdoSqlite);

    /// <summary>EF Core 版の派生が流す行（全行）</summary>
    public static TheoryData<string> EfCoreIds() => IdsFor(RuntimeTarget.EfCoreSqlite);

    /// <summary>実装先がその行を流すか（ExecuteUpdate の経路は EF Core 版の QuickErDbContext だけが持つ）</summary>
    /// <param name="target">実装先</param>
    /// <param name="levels">行の水準</param>
    public static bool RunsOn(RuntimeTarget target, IReadOnlyDictionary<string, string> levels) =>
        levels[QueryPath] != ExecuteUpdate || IsEfCore(target);

    /// <summary>
    /// 実装先が流す行のうち、水準の組（因子 → 水準）をすべて持つ行があるか（分岐の軸の台帳の「項目の水準が出る実装先」）
    /// </summary>
    /// <param name="target">実装先の名前（<see cref="Targets"/> のどれか）</param>
    /// <param name="levels">水準の組</param>
    public static bool CreatesLevels(string target, IReadOnlyDictionary<string, string> levels)
    {
        var runtimeTarget = Enum.Parse<RuntimeTarget>(target);

        return AllRows.Any(row =>
            RunsOn(runtimeTarget, row)
            && levels.All(pair => row.TryGetValue(pair.Key, out var level) && level == pair.Value)
        );
    }

    /// <summary>行の Id（水準を因子の宣言の順に並べる）</summary>
    /// <param name="levels">全因子の水準</param>
    public static string IdOf(IReadOnlyDictionary<string, string> levels) =>
        IdPrefix + string.Join("-", Table.Factors.Select(factor => levels[factor.Name]));

    /// <summary>列の型が比べる表</summary>
    /// <param name="columnType">列の型の水準</param>
    public static BindingEntity EntityOf(string columnType) =>
        columnType switch
        {
            Int => BindingEntity.SmallChild,
            BigInt => BindingEntity.BigParent,
            _ => BindingEntity.Probe,
        };

    private static bool IsEfCore(RuntimeTarget target) =>
        target.ToString().StartsWith("EfCore", StringComparison.Ordinal);

    private static TheoryData<string> IdsFor(RuntimeTarget target)
    {
        var data = new TheoryData<string>();

        foreach (var row in Rows.Where(row => RunsOn(target, row.Input.Levels)))
        {
            data.Add(row.Id);
        }

        return data;
    }

    /// <summary>行の説明</summary>
    private static string Describe(IReadOnlyDictionary<string, string> levels) =>
        $"{levels[ColumnType]} の列（{levels[ColumnShape]}）と {levels[ValueCount]} の {levels[ValuePool]}（{levels[DateTimeKind]}・{levels[ValueShape]}）を {levels[Comparison]} で比べ、{levels[QueryPath]} の経路で C# の評価と照らす";

    // ===== 起こり得ない組 =====

    /// <summary>
    /// 起こり得ない組（部分の割り当てでも、判断に要る因子が無ければ真）
    /// </summary>
    private static bool IsValid(IReadOnlyDictionary<string, string> assignment)
    {
        // 値の池・Kind・列の側と値の側の形は列の型の系統（日時・数・bool）を決める：系統の違う列と値は C# で比べられず、Kind は日時の値だけが
        // 持ち、値を保つ変換（int → long・bigint → decimal）・縮める明示の変換・char の暗黙の変換は数の上に、日付部品は日時の列の上にしか無い。
        // 系統を決める因子どうしが食い違う組は起こり得ない
        var families = new List<IReadOnlySet<string>>();

        if (assignment.TryGetValue(ColumnType, out var column))
        {
            families.Add(Only(FamilyOfColumn(column)));
        }

        string? pool = assignment.TryGetValue(ValuePool, out var poolLevel) ? poolLevel : null;

        if (pool is not null)
        {
            families.Add(Only(FamilyOfPool(pool)));
        }

        if (assignment.TryGetValue(DateTimeKind, out var kind))
        {
            families.Add(kind != NoKind ? Only(DateTimeFamily) : NotDateTime);
        }

        string? shape = assignment.TryGetValue(ColumnShape, out var shapeLevel) ? shapeLevel : null;
        string? valueShape = assignment.TryGetValue(ValueShape, out var valueShapeLevel)
            ? valueShapeLevel
            : null;

        if (shape == PreservingConversion)
        {
            families.Add(Only(NumberFamily));

            // 値を保って広げる変換が無い列の型（decimal の列はより広い数の型を持たず、日時の列は上の系統で外れる）
            if (column == Decimal)
            {
                return false;
            }
        }

        if (shape == DatePart)
        {
            families.Add(Only(DateTimeFamily));
        }

        if (shape == NarrowingCast || valueShape is NarrowingCast or ImplicitChar)
        {
            families.Add(Only(NumberFamily));
        }

        if (
            families.Count > 0
            && !families
                .Skip(1)
                .Aggregate(
                    new HashSet<string>(families[0], StringComparer.Ordinal),
                    (common, next) =>
                    {
                        common.IntersectWith(next);
                        return common;
                    }
                )
                .Any()
        )
        {
            return false;
        }

        // 縮める変換の元の値の池は、縮める変換の元の値という定義なので、列の側か値の側に縮める変換がある組にだけあり、
        // 縮める変換は元の値をこの池から取る（ほかの池の値は変換の元の値でない）
        if (pool is not null)
        {
            var narrowingPool = IsNarrowingPool(pool);

            if (!narrowingPool && (shape == NarrowingCast || valueShape == NarrowingCast))
            {
                return false;
            }

            if (
                narrowingPool
                && shape is not null
                && valueShape is not null
                && shape != NarrowingCast
                && valueShape != NarrowingCast
            )
            {
                return false;
            }
        }

        assignment.TryGetValue(ValueCount, out var count);
        assignment.TryGetValue(Comparison, out var comparison);

        // 単一の値は演算子とだけ、リスト・配列は IN・NOT IN とだけ組む（リストを == で比べる・単一の値の Contains は書けない）
        if (
            count is not null
            && comparison is not null
            && (count == Single) == (comparison is "in" or "not-in")
        )
        {
            return false;
        }

        // bool は順序で比べられない（C# の bool に < は無い）
        if (
            (column == Bit || (pool is not null && BooleanPool.Names.Contains(pool)))
            && comparison is not (null or "equal" or "not-equal" or "in" or "not-in")
        )
        {
            return false;
        }

        // char の暗黙の変換は、char を広げられる整数の列（int・bigint）と char に入る整数の値（行に当たる整数）の単一の値の上にしか無い
        // （char のリストを整数の並びとして Contains に渡すことは C# で書けない）
        if (valueShape == ImplicitChar)
        {
            if (column is not (null or Int or BigInt))
            {
                return false;
            }

            if (pool is not (null or IntegerPool.Integral))
            {
                return false;
            }

            if (count is not (null or Single))
            {
                return false;
            }

            if (comparison is "in" or "not-in")
            {
                return false;
            }

            // 列の側の縮める変換は元の値を縮める変換の値の池から取るので、char の値（行に当たる整数）とは組めない
            if (shape == NarrowingCast)
            {
                return false;
            }
        }

        // ImmutableArray の Contains はインスタンスの Contains（List の IN と同じ呼び出しの形）とだけ組む
        if (
            valueShape == ImmutableArrayShape
            && (count is not (null or List) || comparison is not (null or "in" or "not-in"))
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>列の型の系統：日時</summary>
    private const string DateTimeFamily = "datetime";

    /// <summary>列の型の系統：数</summary>
    private const string NumberFamily = "number";

    /// <summary>列の型の系統：bool</summary>
    private const string BoolFamily = "bool";

    /// <summary>日時でない系統（Kind が無い値。式の本体で返す＝静的な初期化の順に依らない）</summary>
    private static IReadOnlySet<string> NotDateTime =>
        new HashSet<string>([NumberFamily, BoolFamily], StringComparer.Ordinal);

    private static IReadOnlySet<string> Only(string family) =>
        new HashSet<string>([family], StringComparer.Ordinal);

    private static string FamilyOfColumn(string column) =>
        DateTimeColumns.Contains(column) ? DateTimeFamily
        : column == Bit ? BoolFamily
        : NumberFamily;

    private static string FamilyOfPool(string pool) =>
        DateTimePool.Names.Contains(pool) ? DateTimeFamily
        : BooleanPool.Names.Contains(pool) ? BoolFamily
        : NumberFamily;

    /// <summary>網羅の配列に、含まれていない必ず含む行を後ろへ足す（足す行が起こり得ない割り当てなら宣言の誤り）</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, string>> WithRequiredRows(
        IReadOnlyList<IReadOnlyDictionary<string, string>> generated
    )
    {
        var rows = generated.ToList();

        foreach (var required in RequiredRows.Select(row => row.Assignment))
        {
            if (
                required.Count != Table.Factors.Count
                || Table.Factors.Any(factor => !required.ContainsKey(factor.Name))
                || !Table.IsValid(required)
            )
            {
                throw new InvalidOperationException(
                    $"必ず含む行は全因子の起こり得る割り当て: {string.Join(", ", required.Select(p => $"{p.Key}={p.Value}"))}"
                );
            }

            if (!rows.Any(row => required.All(pair => row[pair.Key] == pair.Value)))
            {
                rows.Add(
                    Table.Factors.ToDictionary(
                        factor => factor.Name,
                        factor => required[factor.Name]
                    )
                );
            }
        }

        return rows;
    }

    // ===== 述語 =====

    /// <summary>列の型の列（QuickER 版のエンティティのプロパティ名）</summary>
    private static string PropertyOf(string columnType) =>
        columnType switch
        {
            Date => nameof(ProbeEntity.Day),
            DateTime2 => nameof(ProbeEntity.At),
            LegacyDateTime => nameof(ProbeEntity.LegacyAt),
            SmallDateTime => nameof(ProbeEntity.SmallAt),
            Decimal => nameof(ProbeEntity.Amount),
            Bit => nameof(ProbeEntity.Flag),
            Int => nameof(SmallChildEntity.BigParentId),
            BigInt => nameof(BigParentEntity.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(columnType), columnType, null),
        };

    /// <summary>
    /// 列の型と値の池の水準から、比べる値（日時は列のシードの日を基準の日にする：day・legacy_at・small_at は 2024-01-01、at は 2024-01-15）
    /// </summary>
    private static object PoolValueOf(string columnType, string pool, string kind)
    {
        if (pool == DecimalPool.IntegralDecimal)
        {
            return DecimalPool.IntegralDecimalOf(HitOf(columnType)).Value;
        }

        if (columnType == Bit)
        {
            return BooleanPool.Named(pool).Value;
        }

        object value = columnType switch
        {
            Date or LegacyDateTime or SmallDateTime => DateTimePool
                .Named(new DateTime(2024, 1, 1), pool)
                .WithKind(KindOf(kind))
                .Value,
            DateTime2 => DateTimePool
                .Named(new DateTime(2024, 1, 15), pool)
                .WithKind(KindOf(kind))
                .Value,
            // 腕を object へそろえる（そろえないと switch の型が decimal になり、整数の値が decimal へ広がる）
            Int => pool switch
            {
                IntegerPool.Integral => (object)(int)IntegerPool.Named(1, int.MaxValue, pool).Value,
                IntegerPool.BeyondColumnRange => (object)
                    IntegerPool.Named(1, int.MaxValue, pool).Value,
                _ => (object)DecimalPool.Named(1m, 0, pool).Value,
            },
            BigInt => pool switch
            {
                IntegerPool.Integral => (object)IntegerPool.Named(1, long.MaxValue, pool).Value,
                DecimalPool.BeyondColumnRange => (object)DecimalPool.BeyondLong.Value,
                _ => (object)DecimalPool.Named(1m, 0, pool).Value,
            },
            Decimal => DecimalPool.Named(100m, 2, pool).Value,
            _ => throw new ArgumentOutOfRangeException(nameof(columnType), columnType, null),
        };

        return value;
    }

    /// <summary>数の列の行に当たる整数の値（int の列 big_parent_id と bigint の列 id は 1・decimal の列 amount は 100）</summary>
    private static decimal HitOf(string columnType) => columnType == Decimal ? 100m : 1m;

    private static System.DateTimeKind KindOf(string kind) =>
        kind switch
        {
            Unspecified => System.DateTimeKind.Unspecified,
            Utc => System.DateTimeKind.Utc,
            Local => System.DateTimeKind.Local,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "日時の値の Kind ではない"
            ),
        };

    // ===== 値を変える明示の変換 =====

    /// <summary>列の側の縮める変換の型（int の列は byte・bigint の列は short・decimal の列は int）</summary>
    private static Type ColumnCastOf(string columnType) =>
        columnType switch
        {
            Int => typeof(byte),
            BigInt => typeof(short),
            Decimal => typeof(int),
            _ => throw new ArgumentOutOfRangeException(nameof(columnType), columnType, null),
        };

    /// <summary>値の側の縮める変換の型（double の値を、int の列と decimal の列は int・bigint の列は long へ）</summary>
    private static Type ValueCastOf(string columnType) =>
        columnType == BigInt ? typeof(long) : typeof(int);

    /// <summary>
    /// 縮める変換の元の値（池の値）。範囲の外の値は、列の側は列の型の上限（bigint の long の上限はシードの行。decimal(10,2) は int の範囲を
    /// 超えないので列の上限）、値の側は変換先の型の上限を超える値（double から整数への変換は範囲の端に止まる）
    /// </summary>
    private static decimal NarrowingSourceOf(string columnType, string pool, bool valueSide)
    {
        var beyond = (valueSide, columnType) switch
        {
            (true, BigInt) => 100000000000000000000m,
            (true, _) => HitOf(columnType) + 4294967296m,
            (false, Int) => int.MaxValue,
            (false, BigInt) => long.MaxValue,
            _ => 99999999.99m,
        };

        return NarrowingPool.Named(HitOf(columnType), beyond, pool).Value;
    }

    /// <summary>列の側の縮める変換が行の値 <paramref name="source"/> を写す値（C# の unchecked の変換と同じ。比べる値にする）</summary>
    private static int ColumnCastImage(string columnType, decimal source) =>
        columnType switch
        {
            Int => unchecked((byte)(long)decimal.Truncate(source)),
            BigInt => unchecked((short)(long)decimal.Truncate(source)),
            _ => (int)decimal.Truncate(source),
        };

    // ===== 型の昇格 =====

    /// <summary>C# の 2 項の昇格の順位（同じ系統の型だけを比べる。byte・short は int へ昇格する）</summary>
    private static int RankOf(Type type) =>
        type == typeof(byte) || type == typeof(short) ? 0
        : type == typeof(int) ? 1
        : type == typeof(long) ? 2
        : type == typeof(decimal) ? 3
        : type == typeof(double) ? 4
        : type == typeof(DateTime) ? 10
        : type == typeof(bool) ? 20
        : throw new ArgumentOutOfRangeException(nameof(type), type, null);

    /// <summary>2 つの型を C# の 2 項の昇格でそろえた型（decimal と double は変換を外した形の C# の評価だけが使い、double で比べる）</summary>
    private static Type Promote(Type left, Type right)
    {
        var promoted = RankOf(left) >= RankOf(right) ? left : right;

        return RankOf(promoted) == 0 ? typeof(int) : promoted;
    }

    /// <summary>行の値の側の組み立て（比べる値と型。値の側の縮める変換は元の値の double を持つ）</summary>
    private sealed record ValueSide(object Value, Type Type, bool CastsValue);

    /// <summary>列の側の縮める変換の型（列の側に縮める変換が無い行は null）</summary>
    /// <param name="row">行</param>
    internal static Type? ColumnCastTypeOf(TranslatorParityBinding row) =>
        row.Levels[ColumnShape] == NarrowingCast ? ColumnCastOf(row.ColumnType) : null;

    /// <summary>値の側の縮める変換が作る値（単一の値は 1 つ・リスト・配列は要素の 2 つ。値の側に縮める変換が無い行は空）</summary>
    /// <param name="row">行</param>
    internal static IReadOnlyList<object> CastValuesOf(TranslatorParityBinding row)
    {
        var side = ValueSideOf(row, NarrowingDrop.None);

        if (!side.CastsValue)
        {
            return [];
        }

        return row.Levels[ValueCount] == Single
            ? [CastValue(side.Value, side.Type)]
            : [CastValue(side.Value, side.Type), CastValue(NextOf(side.Value), side.Type)];
    }

    /// <summary>行の値の側（<paramref name="drop"/> が値の側を含むなら値の側の変換を外し、元の値の double をそのまま比べる）</summary>
    private static ValueSide ValueSideOf(TranslatorParityBinding row, NarrowingDrop drop)
    {
        var levels = row.Levels;
        var pool = levels[ValuePool];

        if (!IsNarrowingPool(pool))
        {
            var value = PoolValueOf(levels[ColumnType], pool, levels[DateTimeKind]);
            return new ValueSide(value, value.GetType(), false);
        }

        if (levels[ValueShape] == NarrowingCast)
        {
            var source = (double)NarrowingSourceOf(levels[ColumnType], pool, valueSide: true);
            return drop.HasFlag(NarrowingDrop.Value)
                ? new ValueSide(source, typeof(double), false)
                : new ValueSide(source, ValueCastOf(levels[ColumnType]), true);
        }

        var image = ColumnCastImage(
            levels[ColumnType],
            NarrowingSourceOf(levels[ColumnType], pool, valueSide: false)
        );

        return new ValueSide(image, typeof(int), false);
    }

    /// <summary>列の側の式（列と、列の側の形。日付部品は NULL の行を外す条件 <paramref name="guard"/> を返す）</summary>
    private static Expression ColumnExpression(
        TranslatorParityBinding row,
        ParameterExpression parameter,
        NarrowingDrop drop,
        out Expression? guard
    )
    {
        Expression column = Expression.Property(parameter, PropertyOf(row.ColumnType));
        guard = null;

        switch (row.Levels[ColumnShape])
        {
            case PreservingConversion:
            {
                var widened = row.ColumnType == Int ? typeof(long) : typeof(decimal);

                return Expression.Convert(column, Lifted(column.Type, widened));
            }

            case DatePart:
                guard = Expression.NotEqual(column, Expression.Constant(null, column.Type));

                return Expression.Property(
                    Expression.Property(column, nameof(Nullable<DateTime>.Value)),
                    nameof(DateTime.Date)
                );

            case NarrowingCast when !drop.HasFlag(NarrowingDrop.Column):
                return Expression.Convert(
                    column,
                    Lifted(column.Type, ColumnCastOf(row.ColumnType))
                );

            default:
                return column;
        }
    }

    /// <summary>元の型が Nullable なら <paramref name="type"/> の Nullable、そうでなければ <paramref name="type"/></summary>
    private static Type Lifted(Type source, Type type) =>
        Nullable.GetUnderlyingType(source) is not null
            ? typeof(Nullable<>).MakeGenericType(type)
            : type;

    /// <summary>比べる型（列の側の式の型と値の型を C# の 2 項の昇格でそろえた型。Nullable を外した型）</summary>
    internal static Type ComparedTypeOf(TranslatorParityBinding row) =>
        ComparedTypeOf(row, NarrowingDrop.None);

    private static Type ComparedTypeOf(TranslatorParityBinding row, NarrowingDrop drop)
    {
        var column = ColumnExpression(row, Expression.Parameter(row.EntityType, "x"), drop, out _);

        return Promote(
            Nullable.GetUnderlyingType(column.Type) ?? column.Type,
            ValueSideOf(row, drop).Type
        );
    }

    /// <summary>double の値を C# の明示の変換で <paramref name="castType"/> へ変える（浮動小数点から整数への変換は範囲の端に止まる）</summary>
    private static object CastValue(object source, Type castType) =>
        Expression
            .Lambda(Expression.Convert(Expression.Constant(source, typeof(double)), castType))
            .Compile()
            .DynamicInvoke()!;

    /// <summary>
    /// 行の述語を組む（値を変える関数を渡すと、変えた値で組む。<paramref name="drop"/> の側の値を変える明示の変換を外して組む）
    /// </summary>
    /// <remarks>
    /// 値の側の縮める変換の行に値を変える関数を渡すと、変換した値に関数を当て、捕捉した値として比べる（DB が受け取った値の写し）。
    /// </remarks>
    internal static LambdaExpression BuildPredicate(
        TranslatorParityBinding row,
        Func<object, object>? single,
        Func<object, object>? element,
        NarrowingDrop drop = NarrowingDrop.None
    )
    {
        var levels = row.Levels;
        var parameter = Expression.Parameter(row.EntityType, "x");
        var column = ColumnExpression(row, parameter, drop, out var guard);
        var nullable = Nullable.GetUnderlyingType(column.Type) is not null;
        var valueSide = ValueSideOf(row, drop);
        var target = ComparedTypeOf(row, drop);
        var operandType = nullable ? typeof(Nullable<>).MakeGenericType(target) : target;

        if (column.Type != operandType)
        {
            column = Expression.Convert(column, operandType);
        }

        Expression body;

        if (levels[ValueCount] == Single)
        {
            Expression valueExpression;

            if (valueSide.CastsValue && single is not null)
            {
                // DB が受け取った値を写す：変換した値（C# の変換と同じ）を変えて、捕捉した値として比べる
                var changed = single(CastValue(valueSide.Value, valueSide.Type));
                valueExpression = CapturedField(changed, changed.GetType());
            }
            else if (valueSide.CastsValue)
            {
                valueExpression = Expression.Convert(
                    CapturedField(valueSide.Value, typeof(double)),
                    valueSide.Type
                );
            }
            else
            {
                var bound = single is null ? valueSide.Value : single(valueSide.Value);
                valueExpression = ValueExpression(
                    bound,
                    bound.GetType(),
                    CapturedIfCast(levels[ValueShape])
                );
            }

            if (valueExpression.Type != operandType)
            {
                valueExpression = Expression.Convert(valueExpression, operandType);
            }

            body = levels[Comparison] switch
            {
                "equal" => Expression.Equal(column, valueExpression),
                "not-equal" => Expression.NotEqual(column, valueExpression),
                "less" => Expression.LessThan(column, valueExpression),
                LessOrEqual => Expression.LessThanOrEqual(column, valueExpression),
                Greater => Expression.GreaterThan(column, valueExpression),
                "greater-or-equal" => Expression.GreaterThanOrEqual(column, valueExpression),
                var other => throw new InvalidOperationException(
                    $"単一の値と比べ方 {other} は組まない"
                ),
            };
        }
        else
        {
            var sources = new[] { valueSide.Value, NextOf(valueSide.Value) };
            var contains =
                valueSide.CastsValue && element is null
                    ? CastElementsContains(
                        sources,
                        valueSide.Type,
                        operandType,
                        levels[ValueCount],
                        column
                    )
                    : CapturedContains(
                        [
                            .. sources
                                .Select(item =>
                                    valueSide.CastsValue ? CastValue(item, valueSide.Type) : item
                                )
                                .Select(item => element is null ? item : element(item))
                                .Select(item => ConvertTo(item, target)),
                        ],
                        operandType,
                        levels[ValueCount],
                        CapturedIfCast(levels[ValueShape]),
                        column
                    );

            body = levels[Comparison] switch
            {
                "in" => contains,
                "not-in" => Expression.Not(contains),
                var other => throw new InvalidOperationException(
                    $"リストと比べ方 {other} は組まない"
                ),
            };
        }

        return Expression.Lambda(guard is null ? body : Expression.AndAlso(guard, body), parameter);
    }

    /// <summary>
    /// 値の式を組む形（値の側の縮める変換を式に書かない組み立て＝変換を外した形と、変換した値を変えて比べる形は、捕捉した変数として組む）
    /// </summary>
    private static string CapturedIfCast(string shape) => shape == NarrowingCast ? Captured : shape;

    /// <summary>値の並び（List か配列）を値の形で組み、列を含むかを問う式</summary>
    private static Expression CapturedContains(
        IReadOnlyList<object> items,
        Type operandType,
        string count,
        string shape,
        Expression column
    )
    {
        if (count == List && shape == ImmutableArrayShape)
        {
            var elementList = (IList)
                Activator.CreateInstance(typeof(List<>).MakeGenericType(operandType))!;

            foreach (var item in items)
            {
                elementList.Add(item);
            }

            var immutable = typeof(System.Collections.Immutable.ImmutableArray)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == nameof(System.Collections.Immutable.ImmutableArray.CreateRange)
                    && method.GetParameters() is [{ ParameterType.IsGenericType: true } parameter]
                    && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                )
                .MakeGenericMethod(operandType)
                .Invoke(null, [elementList])!;
            var immutableType = immutable.GetType();

            return Expression.Call(
                CapturedField(immutable, immutableType),
                immutableType.GetMethod(
                    nameof(System.Collections.Immutable.ImmutableArray<int>.Contains),
                    [operandType]
                )!,
                column
            );
        }

        if (count == List)
        {
            var listType = typeof(List<>).MakeGenericType(operandType);
            var list = (IList)Activator.CreateInstance(listType)!;

            foreach (var item in items)
            {
                list.Add(item);
            }

            return Expression.Call(
                ValueExpression(list, listType, shape),
                listType.GetMethod(nameof(List<int>.Contains), [operandType])!,
                column
            );
        }

        var array = System.Array.CreateInstance(operandType, items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            array.SetValue(items[index], index);
        }

        return Expression.Call(
            EnumerableContains.MakeGenericMethod(operandType),
            ValueExpression(array, array.GetType(), shape),
            column
        );
    }

    /// <summary>
    /// 要素ごとに値の側の縮める変換を書いた並び（<c>new List&lt;int?&gt; { (int)r0, (int)r1 }</c>・<c>new int?[] { … }</c>。r は捕捉した double）が、
    /// 列を含むかを問う式
    /// </summary>
    private static Expression CastElementsContains(
        IReadOnlyList<object> sources,
        Type castType,
        Type operandType,
        string count,
        Expression column
    )
    {
        var elements = sources
            .Select(source =>
            {
                Expression cast = Expression.Convert(
                    CapturedField(source, typeof(double)),
                    castType
                );

                return cast.Type == operandType ? cast : Expression.Convert(cast, operandType);
            })
            .ToList();

        if (count == List)
        {
            var listType = typeof(List<>).MakeGenericType(operandType);

            return Expression.Call(
                Expression.ListInit(Expression.New(listType), elements),
                listType.GetMethod(nameof(List<int>.Contains), [operandType])!,
                column
            );
        }

        return Expression.Call(
            EnumerableContains.MakeGenericMethod(operandType),
            Expression.NewArrayInit(operandType, elements),
            column
        );
    }

    /// <summary>リスト・配列の 2 つ目の要素（同じ形の値を 1 単位ずらす：日時は 1 日〔最後の日なら前の日〕・数は 1）</summary>
    private static object NextOf(object value) =>
        value switch
        {
            DateTime day when day.Date < DateTime.MaxValue.Date => day.AddDays(1),
            DateTime day => day.AddDays(-1),
            int number => number + 1,
            long number => number + 1,
            decimal number => number + 1m,
            double number => number + 1d,
            bool flag => !flag,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

    /// <summary>Enumerable.Contains(IEnumerable&lt;T&gt;, T)</summary>
    private static readonly MethodInfo EnumerableContains = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(Enumerable.Contains) && method.GetParameters().Length == 2
        );

    /// <summary>Enumerable.Select(IEnumerable&lt;TSource&gt;, Func&lt;TSource, TResult&gt;)</summary>
    private static readonly MethodInfo EnumerableSelect = typeof(Enumerable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(Enumerable.Select)
            && method.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>)
        );

    /// <summary>捕捉した変数（C# のコンパイラがクロージャのフィールドとして書く形）</summary>
    private static MemberExpression CapturedField(object value, Type type) =>
        Expression.Field(
            Expression.Constant(
                Activator.CreateInstance(
                    typeof(TranslatorParityBindingBox<>).MakeGenericType(type),
                    value
                )
            ),
            nameof(TranslatorParityBindingBox<int>.Value)
        );

    /// <summary>値の側の式（捕捉した変数・定数・メソッド呼び出し・ラムダを含む式）</summary>
    private static Expression ValueExpression(object value, Type type, string shape) =>
        shape switch
        {
            Captured => CapturedField(value, type),
            Constant => Expression.Constant(value, type),
            MethodCall => Expression.Call(
                typeof(TranslatorParityBindingValues)
                    .GetMethod(nameof(TranslatorParityBindingValues.Same))!
                    .MakeGenericMethod(type),
                Expression.Constant(value, type)
            ),
            Lambda => LambdaExpressionOf(value, type),
            // char に入らない値（既知の失敗の写しが DB の受け取った形へ変えた値）は捕捉した値のまま比べる
            ImplicitChar
                when Convert.ToInt64(value, CultureInfo.InvariantCulture)
                    is >= char.MinValue
                        and <= char.MaxValue => Expression.Convert(
                CapturedField(
                    (char)Convert.ToInt32(value, CultureInfo.InvariantCulture),
                    typeof(char)
                ),
                type
            ),
            ImplicitChar => CapturedField(value, type),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    /// <summary>
    /// ラムダを含む値の式：リストは <c>captured.Select(p =&gt; p).ToList()</c>、配列は <c>captured.Select(p =&gt; p).ToArray()</c>、単一の値は
    /// 値 1 つの捕捉したリストの <c>captured.Select(p =&gt; p).First()</c>（集計 <c>Max(p =&gt; p)</c> は作らない＝作らない形の宣言
    /// <c>lambda-aggregate-over-captured-collection</c>）
    /// </summary>
    private static Expression LambdaExpressionOf(object value, Type type)
    {
        if (type.IsArray)
        {
            var element = type.GetElementType()!;

            return Expression.Call(
                typeof(Enumerable)
                    .GetMethod(nameof(Enumerable.ToArray))!
                    .MakeGenericMethod(element),
                SelectIdentity(CapturedField(value, type), element)
            );
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var element = type.GetGenericArguments()[0];

            return Expression.Call(
                typeof(Enumerable).GetMethod(nameof(Enumerable.ToList))!.MakeGenericMethod(element),
                SelectIdentity(CapturedField(value, type), element)
            );
        }

        var listType = typeof(List<>).MakeGenericType(type);
        var list = (IList)Activator.CreateInstance(listType)!;
        list.Add(value);

        return Expression.Call(
            typeof(Enumerable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method =>
                    method.Name == nameof(Enumerable.First) && method.GetParameters().Length == 1
                )
                .MakeGenericMethod(type),
            SelectIdentity(CapturedField(list, listType), type)
        );
    }

    /// <summary><c>source.Select(p =&gt; p)</c></summary>
    private static MethodCallExpression SelectIdentity(Expression source, Type element)
    {
        var p = Expression.Parameter(element, "p");

        return Expression.Call(
            EnumerableSelect.MakeGenericMethod(element, element),
            source,
            Expression.Lambda(p, p)
        );
    }

    /// <summary>リストの要素を昇格した型の値にする（C# の暗黙の数値の変換と同じ値）</summary>
    private static object ConvertTo(object value, Type target) =>
        value.GetType() == target
            ? value
            : Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
}
