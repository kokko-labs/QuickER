using System.Collections.Generic;
using QuickER.Tests.TestSupport.Engines;
using F = QuickER.Tests.TestSupport.ValuePools.ValueFamily;

namespace QuickER.Tests.TestSupport.ValuePools;

/// <summary>
/// 値のプールの受ける水準の宣言（純粋なデータ）：両アセンブリのエンジンの台帳の全部の部分と単体の表の台帳の全部の表が、値を受けるか（受ける系統と
/// 受けない水準の行）か受けない理由を持つ
/// </summary>
/// <remarks>
/// <para>
/// このファイルは Engines と BCL だけを参照する（Integration へも値のプールの glob で入る）。照合は QuickER.Tests の <c>ValuePoolUseTests</c>
/// （構造・対象の網羅・受け持つ守り・QuickER.Tests で観測する部分の行列）と Integration の <c>ValuePoolUseIntegrationTests</c>（Integration で観測する部分の
/// 行列）。CLAUDE.md の照合の行 Id の本文から名前で外す（<see cref="ValuePoolUseChecks.IsDeclarationFileName"/>）。
/// </para>
/// <para>
/// 受けない水準の行の種類は作らない形の宣言と同じ：Accepted は製品・DB・docs の事実で受けない水準、Pending は水準を足す区切りがある水準。
/// </para>
/// </remarks>
public static class ValuePoolUseDeclarations
{
    /// <summary>同期のモデルの値を棚卸しする区切り（区切り e の後）</summary>
    public const string SyncModelValues = "同期のモデルの値（未起動）";

    /// <summary>同期のキーの値を足す区切り</summary>
    public const string SyncKeyValues = "同期のキーの値（未起動）";

    /// <summary>値の束縛の列の型を足す区切り</summary>
    public const string BindingColumnTypes = "値の束縛の列の型の拡張（未起動）";

    /// <summary>GUI・ファイル・プロセスの操作の列の部分の受けない理由</summary>
    private const string GuiOrOsOperations =
        "GUI・ファイル・プロセス・設定の操作の列（手と状態）を受け、DB の比較・束縛・書き込みの分岐へ型の値を渡さない";

    /// <summary>名前・識別子・文言の部分の受けない理由</summary>
    private const string NamesAndText =
        "名前・識別子・型の表記・文言を受け、DB の比較・束縛・書き込みの分岐へ型の値を渡さない";

    /// <summary>同期のモデルと同期のエンジンの表の受けない理由（範囲の外）</summary>
    private const string SyncModelOutOfScope =
        "（範囲の外）同期のモデルと同期のエンジンの値は、区切り e が同期のエンジンを直している間は触らない。区切り e の後に棚卸しする";

    /// <summary>同期計画の部分の受けない理由</summary>
    private const string SyncPlanShapes =
        "型の値の端でなく、データの水準（行あり・NULL・重複）と列の変更の種類（型の種類・長さ・幅・精度・NULL 許容）を受ける";

    /// <summary>部分の宣言</summary>
    public static IReadOnlyList<ValuePoolSubject> All { get; } =
    [
        // ---- 値を受ける部分：簡易 DSL の総当たり ----
        ValuePoolSubject.Receives(
            "Dsl/SqliteMySql",
            [
                F.DateTime,
                F.DateTimeOffset,
                F.TimeSpan,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Guid,
                F.Boolean,
            ],
            DecimalTypeEnd(),
            DslSqlServerLegacy(),
            DslCalendarEnd(),
            DslOffsetScale(),
            ValueEdgeNotDrawn.Accepted(
                "dsl-timespan-scale",
                ["ts-beyond-column-scale"],
                "SQLite の TimeSpan は TEXT で小数秒の桁を宣言せず、MySQL の time の値はマイクロ秒の粒度（docs の TimeSpan の割り切りの写しの値のプール）なので、列の桁より細かい値は無い",
                "`Dialect/TypesRealDb`"
            ),
            DslMySqlTimeRange(),
            DslSurrogate(),
            DslLeadingSpace(),
            DslBinaryGuidNotBranched(["bin-long", "guid-empty"])
        ),
        ValuePoolSubject.Receives(
            "Dsl/SqlServer",
            [
                F.DateTime,
                F.DateTimeOffset,
                F.TimeSpan,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Guid,
                F.Boolean,
            ],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Accepted(
                "dsl-sqlserver-seven-digit-scale",
                ["dt-beyond-column-scale", "ts-beyond-column-scale"],
                "SQL Server の単位の DateTime の列は datetime2・date・datetime・smalldatetime、TimeSpan の列は time で、小数秒の桁を宣言する列は 7 桁（tick と同じ桁）なので、C# の DateTime・TimeSpan にそれより細かい値は無い",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "dsl-sqlserver-mysql-time-end",
                ["ts-mysql-range-end"],
                "±838:59:59 は MySQL の time だけの範囲の端。SQL Server の time は 0〜24 時間未満で、その外の値は網羅の単位 coverage-sqlserver-timespan-out-of-day が作る",
                "`Dsl/MySql`"
            ),
            DslSurrogate(),
            DslBinaryGuidNotBranched(["bin-long", "guid-empty"])
        ),
        ValuePoolSubject.Receives(
            "Dsl/PostgreSql",
            [
                F.DateTime,
                F.DateTimeOffset,
                F.TimeSpan,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Guid,
                F.Boolean,
            ],
            DecimalTypeEnd(),
            DslSqlServerLegacy(),
            DslCalendarEnd(),
            DslOffsetScale(),
            ValueEdgeNotDrawn.Accepted(
                "dsl-postgresql-offset-zero",
                ["dto-local-date-differs"],
                "PostgreSQL の単位の DateTimeOffset は時差 0 の値だけ（timestamptz は Npgsql が時差 0 でない値を断る＝tests/QuickER.Tests/TestSupport/CLAUDE.md の簡易 DSL の総当たり）なので、局所の日付と UTC の日付は割れない",
                "`Dsl/SqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "dsl-postgresql-microseconds",
                ["dto-sub-microsecond", "ts-sub-microsecond", "ts-beyond-column-scale"],
                "PostgreSQL の timestamptz・time・interval はマイクロ秒の粒度で持ち、単位の値のプールもマイクロ秒の粒度に揃えてある（docs の TimeSpan の割り切りの写し）",
                "`Dsl/SqliteMySql`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "dsl-postgresql-no-unsigned",
                ["int-sign-boundary"],
                "PostgreSQL は符号なしの整数の型を持たず、型マッパーに byte・符号なしの整数へ写す型が無い（網羅しない組の受け入れと同じ事実）",
                "`Dsl/SqliteMySql`"
            ),
            DslSurrogate(),
            DslLeadingSpace(),
            DslUnsizedBinary("PostgreSQL の bytea"),
            DslBinaryGuidNotBranched(["bin-long", "guid-empty"])
        ),
        ValuePoolSubject.Receives(
            "Dsl/MySql",
            [
                F.DateTime,
                F.TimeSpan,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Guid,
                F.Boolean,
            ],
            DecimalTypeEnd(),
            DslSqlServerLegacy(),
            DslCalendarEnd(),
            ValueEdgeNotDrawn.Accepted(
                "dsl-mysql-microseconds",
                ["ts-sub-microsecond", "ts-beyond-column-scale"],
                "MySQL の time の値はマイクロ秒の粒度（docs の TimeSpan の割り切りの写しの値のプール）で、time(6) の桁に収まる",
                "`Dsl/SqliteMySql`"
            ),
            DslMySqlTimeRange(),
            DslSurrogate(),
            DslLeadingSpace(),
            DslBinaryGuidNotBranched(["bin-long", "guid-empty"])
        ),
        ValuePoolSubject.Receives(
            "Dsl/Oracle",
            [
                F.DateTime,
                F.DateTimeOffset,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Boolean,
            ],
            DecimalTypeEnd(),
            DslSqlServerLegacy(),
            DslCalendarEnd(),
            DslOffsetScale(),
            ValueEdgeNotDrawn.Accepted(
                "dsl-oracle-microseconds",
                ["dto-sub-microsecond"],
                "Oracle の単位の DateTimeOffset の値は 100 ナノ秒の端数を持たない（端数を持つのは SQL Server の単位だけ＝tests/QuickER.Tests/TestSupport/CLAUDE.md の簡易 DSL の総当たり）",
                "`Dsl/SqlServer`"
            ),
            DslSurrogate(),
            DslLeadingSpace(),
            DslBinaryGuidNotBranched(["bin-long"])
        ),
        // ---- 値を受ける部分：生成コードのランタイム ----
        ValuePoolSubject.Receives(
            "GeneratedRuntime/TranslatorParityBindings",
            [F.DateTime, F.Decimal, F.Integer, F.Floating, F.Boolean, F.Narrowing],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Pending(
                "binding-datetime2-finer-than-column-scale",
                ["dt-beyond-column-scale"],
                BindingColumnTypes,
                "生成の表の datetime2 の列 at は datetime2（7 桁＝tick と同じ桁）で、DateTime にそれより細かい値は無い。図の datetime2(2) の列 at2 を列の型の水準に足す区切りで受ける（作らない形の Pending datetime2-finer-than-column-scale）。それまでは手書きの述語の表の行が受け持つ",
                "`table:TranslatorParityPredicates`（行 `datetime2-scaled-equals-finer`・`datetime2-scaled-less-finer`）"
            ),
            ValueEdgeNotDrawn.Accepted(
                "binding-leap-day-not-read",
                ["dt-leap-day"],
                "値の束縛の判定（QueryValueFits・FitsDateTime・BetweenDateTimeReadBacks・PostgreSQL の Retype）は範囲・刻み・時刻の有無・Kind だけを読み、月日を読まない",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "binding-floating-source-only",
                ["fp-negative-zero", "fp-subnormal", "fp-type-end", "fp-integer-beyond-2pow24"],
                "浮動小数点の値は値の側の縮める変換の元の値（double）だけで、表の列の型に浮動小数点の列は無い（元の値の 2^53 を超える整数は bigint の列の範囲の外の元の値で出る）。浮動小数点の列と値の端は簡易 DSL の総当たりの浮動小数点の単位が受ける",
                "`Dsl/SqlServer`"
            )
        ),
        ValuePoolSubject.Receives(
            "table:TranslatorParityPredicates",
            [
                F.DateTime,
                F.DateTimeOffset,
                F.TimeSpan,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Boolean,
                F.Narrowing,
            ],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Accepted(
                "predicates-binding-table-levels",
                [
                    "dt-kind-utc",
                    "dt-kind-local",
                    "dt-tick-before-midnight",
                    "cast-cuts-fraction",
                    "cast-beyond-range",
                ],
                "Kind・翌日の 1 tick 前・値を変える明示の変換の元の値（小数・範囲の外）は、値の束縛の生成の表の水準（値のプールの Kind と tick-before-midnight と縮める変換の元の値）が全実装先で受ける。手書きの述語の表へ同じ値を足さない",
                "`GeneratedRuntime/TranslatorParityBindings`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-sign-cast-source-is-seed",
                ["cast-sign-only"],
                "負の値を折り返す符号だけの変換の行は、値の束縛の生成の表の必ず含む行へ移した（値のプールの正本・列の側の形 uint-cast・ulong-cast）。述語の表に残る符号だけの変換（in-uint-list-beyond-int）は負の値の無い列 small_child.id の上にあり、折り返す元の値を持たない",
                "`GeneratedRuntime/TranslatorParityBindings`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-seven-digit-offset",
                ["dto-beyond-column-scale"],
                "述語の表の DateTimeOffset の列 at_offset は datetimeoffset（7 桁＝tick と同じ桁）で、C# の DateTimeOffset にそれより細かい値は無い",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-mysql-time",
                ["ts-mysql-range-end", "ts-beyond-mysql-range"],
                "±838:59:59 は MySQL の time だけの範囲。述語の表の time の列は SQL Server の time(2) で、範囲の外の比較は time-out-of-day の行（負の値・24 時間）が持つ",
                "`Dsl/MySql`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-no-floating-column",
                [
                    "fp-negative-zero",
                    "fp-subnormal",
                    "fp-type-end",
                    "fp-non-dyadic",
                    "fp-integer-beyond-2pow24",
                    "fp-integer-beyond-2pow53",
                    "fp-beyond-float",
                ],
                "述語の表の図に浮動小数点の列は無い（浮動小数点の値は整数・decimal の列と比べる値の側の式だけ）。浮動小数点の列と値の端は簡易 DSL の総当たりの浮動小数点の単位が受ける",
                "`Dsl/SqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-fixed-length-binary",
                ["bin-long"],
                "述語の表のバイナリの列は varbinary(4)・binary(4) で、長いバイト列は列に入らない。長いバイト列の往復は値の往復の除外 blob の列が受ける",
                "`GeneratedRuntime/ParityValuesSqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-datetime-decimal-in-binding-table",
                [
                    "dt-sql-datetime-range-end",
                    "dt-leap-day",
                    "dt-before-sql-datetime-min",
                    "dt-before-smalldatetime-min",
                    "dt-past-sql-datetime-max",
                    "dt-calendar-end",
                    "dec-zero",
                    "dec-column-range-end",
                    "dec-smallest-unit",
                ],
                "日時と decimal の列の値の端は、同じ列の型（probes の date・datetime2・datetime・smalldatetime・amount）で値の束縛の生成の表が受ける（その部分の行列に観測か受けない行がある。datetime・smalldatetime の範囲の外の行は値のプールの正本の区切りで生成の表の必ず含む行へ移した）。述語の表は NULL の補償・否定・文字列一致・バイナリ・TimeSpan・DateTimeOffset の形を持つ表として残す",
                "`GeneratedRuntime/TranslatorParityBindings`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "predicates-like-escape-in-dsl",
                ["str-like-wildcard", "str-quote"],
                "文字列一致の LIKE のエスケープと引用符は、簡易 DSL の総当たりが同じ翻訳器の経路で受ける（DSL の条件は C# のラムダへ訳されて同じ翻訳器を通る）",
                "`Dsl/SqlServer`"
            ),
            ValueEdgeNotDrawn.Pending(
                "predicates-column-types-outside-binding-table",
                [
                    "dto-offset-extreme",
                    "dto-sub-microsecond",
                    "dto-calendar-end",
                    "ts-zero",
                    "ts-day-end",
                    "ts-beyond-day",
                    "str-surrogate",
                    "str-beyond-column-length",
                    "str-exact-column-length",
                    "bin-empty",
                    "bin-leading-zero",
                ],
                "値の束縛の列の型の拡張（未起動）",
                "TimeSpan・DateTimeOffset・varchar・binary の列の値の端は、値の束縛の生成の表にこの 4 つの列の型を足す区切りで受ける（作らない形の Pending column-types-outside-binding-table）。列の型 4 つで網羅の配列の行が倍近くになり、8 実装先（うち 5 つは実コンテナ）の時間を測ってから足す必要がある",
                "`table:TranslatorParityPredicates`（行 `time-out-of-day-equals`・`offset-equals-same-instant-other-offset`・`varchar-equals-non-ascii`・`binary-equals-value`）"
            )
        ),
        ValuePoolSubject.Receives(
            "GeneratedRuntime/ParityValues",
            [F.DateTime, F.Decimal, F.String, F.Binary],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Accepted(
                "parity-values-leap-day-not-read",
                ["dt-leap-day"],
                "書く経路と読み戻し（SQLite は TEXT・インメモリは値の写し・リモートは JSON の往復の書式）に月日で分かれる腕が無い",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "parity-values-no-sql-text",
                [
                    "str-leading-space",
                    "str-trailing-space",
                    "str-other-whitespace",
                    "str-like-wildcard",
                    "str-quote",
                    "bin-leading-zero",
                ],
                "値の幅は値をパラメータと JSON で送り、SQL のリテラルも LIKE も組まない。SQLite の TEXT は空白を詰めも外しもせず（固定長の列を持たない）、BLOB・インメモリの写し・JSON の base64 は先頭の 0x00 で分かれない",
                "`Dsl/SqliteMySql`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "parity-values-sqlite-dialect",
                [
                    "dt-beyond-column-scale",
                    "dt-legacy-read-back",
                    "dt-legacy-between-read-back-and-step",
                    "dt-small-rounding-boundary",
                    "dt-sql-datetime-range-end",
                    "dt-smalldatetime-range-end",
                ],
                "BackendParityFixture の日時の列は datetime2（7 桁）だけで、datetime・smalldatetime の列を持たない（SQLite 方言の図）。SQL Server の日時の型の丸めと範囲は SQL Server の系統の値の往復が受ける（暦の最初と最後の値は datetime・smalldatetime の範囲の外としても観測に出るが、列は datetime2 なので範囲で分かれる腕は無い）",
                "`GeneratedRuntime/ParityValuesSqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "parity-values-within-column",
                [
                    "dec-beyond-long",
                    "dec-beyond-column-scale",
                    "dec-beyond-column-range",
                    "str-beyond-column-length",
                    "str-exact-column-length",
                ],
                "値の幅は列の型に入る値が実装先の型変換と往復で変わらないことを確かめるもので、列の型の外の値を書かない（書くと DB が断るか丸める。列の型の外の値の往復は SQL Server の系統の値の往復が受ける）",
                "`GeneratedRuntime/ParityValuesSqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "parity-values-unbounded-blob",
                ["bin-beyond-column-length", "bin-exact-column-length", "bin-zero-excess"],
                "値の幅のバイト列は除外 blob の列（varbinary(max)）にだけ書き、列は長さを宣言しない",
                "`GeneratedRuntime/ParityValuesSqlServer`"
            )
        ),
        ValuePoolSubject.Receives(
            "GeneratedRuntime/ParityValuesSqlServer",
            [F.DateTime, F.DateTimeOffset, F.TimeSpan, F.Decimal, F.String, F.Binary],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-values-leap-day-not-read",
                ["dt-leap-day"],
                "書く 5 経路の値の整え方（小数秒の四捨五入・範囲の端で止める・超えた分の切り）は月日を読まない",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-values-mysql-time",
                ["ts-mysql-range-end", "ts-beyond-mysql-range"],
                "±838:59:59 は MySQL の time だけの範囲で、value_probes の time(2) の列の範囲（0〜24 時間未満）と関係しない",
                "`Dialect/TypesRealDb`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-values-narrow-decimal",
                ["dec-beyond-long"],
                "value_probes の decimal の列は decimal(10,2)・money・smallmoney で long の範囲より狭く、範囲の外は列の範囲の外（dec-beyond-column-range）として作る",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-values-no-sql-text",
                ["str-like-wildcard", "str-quote", "str-leading-space"],
                "書く経路は値をパラメータと SqlBulkCopy で送り、LIKE のパターンも SQL のリテラルも組まないので、ワイルドカードと引用符で分岐しない。空白は超えた分の末尾の空白だけを数え（先頭の空白では分かれない。乱数の行の超過の値が先頭の空白を持つことはあるが、決まった形では観測しない）",
                "`Dsl/SqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-values-trailing-zero-only",
                ["bin-leading-zero"],
                "SQL Server がバイナリの値で分岐するのは末尾の 0x00（固定長の詰め・超えた分の切り）で、先頭の 0x00 は書く経路と読み戻しの分岐に無い",
                NotGenerated.Unguarded
            )
        ),
        // ---- 値を受ける部分：EF Core のインターセプターの表 ----
        ValuePoolSubject.Receives(
            "table:SqlServerQueryParameters",
            [F.DateTime, F.Decimal, F.Integer, F.String],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-parameters-datetime-judgement",
                ["dt-beyond-column-scale", "dt-leap-day"],
                "インターセプターの datetime・smalldatetime の判定は範囲と刻み（1/300 秒の読み戻し・分）だけを読み、日付の部品と小数秒の桁を読まない。表の日時の列は datetime・smalldatetime で、小数秒の桁を宣言しない",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-parameters-decimal-digits",
                ["dec-zero", "dec-smallest-unit", "dec-beyond-long"],
                "decimal の判定（FitsDecimal）は値の小数の桁と整数部の桁を列の精度・スケールと比べるだけで、0（整数部 0 桁の腕は 0.999 の行が通る）・最小の単位・long の範囲で腕が分かれない",
                "`table:SqlServerQueryParameters`（行 `int-column-decimal-rounding-to-integer-is-bound-by-value`）"
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-parameters-integers-untouched",
                [
                    "int-negative",
                    "int-type-min",
                    "int-type-max",
                    "int-sign-boundary",
                    "int-beyond-column-range",
                    "int-beyond-float-precision",
                    "int-beyond-double-precision",
                ],
                "インターセプターは整数のパラメータを直さない（EF Core は整数・浮動小数点の列と比べる値を C# の型で送る＝src/QuickER.Runtime.EntityFrameworkCore/CLAUDE.md「SQL Server のクエリのパラメータの型」）。表の整数の値は SaveChanges の行の値だけ",
                "`Dsl/SqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "sqlserver-parameters-ascii-only",
                [
                    "str-empty",
                    "str-surrogate",
                    "str-leading-space",
                    "str-trailing-space",
                    "str-other-whitespace",
                    "str-quote",
                    "str-beyond-column-length",
                    "str-exact-column-length",
                ],
                "Unicode でない列の判定は値が ASCII の外の文字を含むかだけで（サロゲートペアは ASCII の外の腕）、空・空白・引用符・長さで腕が分かれない",
                "`Dsl/SqlServer`"
            )
        ),
        ValuePoolSubject.Receives(
            "table:PostgreSqlQueryParameters",
            [F.DateTime, F.TimeSpan, F.Integer],
            ValueEdgeNotDrawn.Accepted(
                "postgresql-parameters-no-legacy-types",
                [
                    "dt-legacy-read-back",
                    "dt-legacy-between-read-back-and-step",
                    "dt-small-rounding-boundary",
                    "dt-before-sql-datetime-min",
                    "dt-before-smalldatetime-min",
                    "dt-past-sql-datetime-max",
                    "dt-past-smalldatetime-max",
                    "dt-sql-datetime-range-end",
                    "dt-smalldatetime-range-end",
                ],
                "datetime・smalldatetime の刻みと範囲は SQL Server の型で、PostgreSQL のインターセプターの判定に無い（翌日の 1 tick 前の行は刻みの外・分でない値として観測されるが、刻みや分で分かれる腕は無い）",
                "`table:SqlServerQueryParameters`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "postgresql-parameters-date-judgement",
                ["dt-beyond-column-scale", "dt-calendar-end", "dt-leap-day"],
                "PostgreSQL のインターセプターは date の列と比べる値の時刻の有無と、timestamp で送る値の Kind と、time の列と比べる値の範囲だけを読み、小数秒・暦の端・日付の部品を読まない（翌日の 1 tick 前の行は時刻の有無の腕の対照で、小数秒の水準が観測に含まれるが、小数秒の桁で分かれる腕は無い）",
                NotGenerated.Unguarded
            ),
            ValueEdgeNotDrawn.Accepted(
                "postgresql-parameters-time-range",
                ["ts-day-end", "ts-sub-microsecond", "ts-beyond-column-scale"],
                "time の判定は 0 と 24:00:00 の範囲だけで、1 日の最後のマイクロ秒と小数秒は範囲の中の行と同じ腕",
                "`table:PostgreSqlQueryParameters`（行 `time-in-day-keeps-column-type`）"
            ),
            ValueEdgeNotDrawn.Accepted(
                "postgresql-parameters-mysql-time",
                ["ts-mysql-range-end", "ts-beyond-mysql-range"],
                "±838:59:59 は MySQL の time だけの範囲で、PostgreSQL の time の範囲（0〜24:00:00）と関係しない",
                "`Dsl/MySql`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "postgresql-parameters-integers-untouched",
                [
                    "int-zero",
                    "int-negative",
                    "int-type-min",
                    "int-type-max",
                    "int-sign-boundary",
                    "int-beyond-column-range",
                    "int-beyond-float-precision",
                    "int-beyond-double-precision",
                ],
                "表の整数の値は SaveChanges・ExecuteUpdate の行の主キーだけで、インターセプターは整数のパラメータを直さない",
                NotGenerated.Unguarded
            )
        ),
        // ---- 値を受ける部分：方言の型・同期のキー・翻訳器の変換 ----
        ValuePoolSubject.Receives(
            "Dialect/TypesRealDb",
            [
                F.DateTime,
                F.DateTimeOffset,
                F.TimeSpan,
                F.Decimal,
                F.Integer,
                F.Floating,
                F.String,
                F.Binary,
                F.Guid,
                F.Boolean,
            ],
            ValueEdgeNotDrawn.Accepted(
                "realdb-temporal-representative",
                [
                    "dt-kind-utc",
                    "dt-kind-local",
                    "dt-tick-before-midnight",
                    "dt-sub-microsecond",
                    "dt-beyond-column-scale",
                    "dt-legacy-read-back",
                    "dt-legacy-between-read-back-and-step",
                    "dt-before-sql-datetime-min",
                    "dt-past-sql-datetime-max",
                    "dt-past-smalldatetime-max",
                    "dt-sql-datetime-range-end",
                    "dt-smalldatetime-range-end",
                    "dt-calendar-end",
                    "dt-leap-day",
                    "dto-offset-positive",
                    "dto-offset-extreme",
                    "dto-sub-microsecond",
                    "dto-beyond-column-scale",
                ],
                "書き込みの範囲の手順は TimeSpan（time の範囲）と decimal（桁と整数部）だけで、日時・時差つきの日時は EF Core の手順で読み書きできる型かを確かめる代表の値だけを書く。SQL Server の日時の型の丸めと範囲は SQL Server の系統の値の往復が受ける",
                "`GeneratedRuntime/ParityValuesSqlServer`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "realdb-scalar-representative",
                [
                    "int-sign-boundary",
                    "int-beyond-column-range",
                    "int-beyond-float-precision",
                    "int-beyond-double-precision",
                    "fp-negative-zero",
                    "fp-subnormal",
                    "fp-type-end",
                    "fp-non-dyadic",
                    "fp-integer-beyond-2pow24",
                    "fp-integer-beyond-2pow53",
                    "fp-beyond-float",
                    "str-empty",
                    "str-non-ascii",
                    "str-non-latin1",
                    "str-surrogate",
                    "str-leading-space",
                    "str-trailing-space",
                    "str-other-whitespace",
                    "str-like-wildcard",
                    "str-quote",
                    "str-beyond-column-length",
                    "str-exact-column-length",
                    "bin-empty",
                    "bin-leading-zero",
                    "bin-beyond-column-length",
                    "bin-exact-column-length",
                    "bin-zero-excess",
                    "bin-long",
                    "guid-empty",
                    "guid-max",
                    "bool-true",
                ],
                "整数・浮動小数点・文字列・バイナリ・Guid・bool は、EF Core の手順でマッパーの型として読み書きできるかを確かめる代表の値だけを書く。値の端は簡易 DSL の総当たりが方言ごとに受ける",
                "`Dsl/SqlServer`"
            )
        ),
        ValuePoolSubject.Receives(
            "table:SyncKeyPaging",
            [F.DateTime, F.DateTimeOffset, F.Guid, F.Integer, F.Decimal, F.String]
        ),
        ValuePoolSubject.Receives(
            "table:TranslatorConversions",
            [F.DateTime, F.Decimal, F.Integer, F.Floating, F.Narrowing],
            DecimalTypeEnd(),
            ValueEdgeNotDrawn.Accepted(
                "conversions-translation-shape",
                [
                    "dt-kind-utc",
                    "dt-kind-local",
                    "dt-tick-before-midnight",
                    "dt-sub-microsecond",
                    "dt-beyond-column-scale",
                    "dt-legacy-off-grid",
                    "dt-legacy-between-read-back-and-step",
                    "dt-small-rounding-boundary",
                    "dt-before-sql-datetime-min",
                    "dt-before-smalldatetime-min",
                    "dt-past-sql-datetime-max",
                    "dt-past-smalldatetime-max",
                    "dt-sql-datetime-range-end",
                    "dt-smalldatetime-range-end",
                    "dt-calendar-end",
                    "dt-leap-day",
                    "dec-zero",
                    "dec-negative",
                    "dec-beyond-long",
                    "dec-beyond-column-scale",
                    "dec-beyond-column-range",
                    "dec-column-range-end",
                    "dec-smallest-unit",
                    "int-zero",
                    "int-type-min",
                    "int-beyond-column-range",
                    "int-beyond-double-precision",
                    "fp-negative-zero",
                    "fp-subnormal",
                    "fp-type-end",
                    "fp-non-dyadic",
                    "fp-beyond-float",
                    "cast-beyond-range",
                ],
                "翻訳器の変換の表は、比べる 2 つの側に書いた変換の種類（縮める・符号だけ・列挙・char・Nullable を外す・丸めうる暗黙の変換）を翻訳器がどう扱うか（SQL の形・束縛の型・断る例外）を見る表で、値は変換の腕ごとの代表 1 つ。プローブの列は CLR の型だけで宣言の型を持たない。値の端は値の束縛の生成の表と述語の表が受ける",
                "`GeneratedRuntime/TranslatorParityBindings`"
            ),
            ValueEdgeNotDrawn.Accepted(
                "conversions-sign-cast-source-is-row",
                ["cast-sign-only"],
                "符号だけの変換は列の側の変換で、変換する元の値は行の値（表は翻訳した SQL の形を見て、行を流さない）。観測は比べる値に変換先を付けるので、元の負の値が水準に出ない",
                "`table:TranslatorConversions`（行 `column-sign-only-cast`）"
            )
        ),
        // ---- 値を受けない部分（QuickER.Tests の台帳） ----
        ValuePoolSubject.NotReceiving("Name/Entrances", NamesAndText),
        ValuePoolSubject.NotReceiving(
            "DiagramEdit/ToolArgument",
            "ツールの引数の JSON の形（型違い・欠け・余分）を受け、DB の比較・束縛の分岐へ型の値を渡さない"
        ),
        ValuePoolSubject.NotReceiving("DiagramEdit/Undo", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving(
            "Dialect/Types",
            "型の表記と型の対応（解析・書き出し・正規化・マッパー）を受け、値を比較・束縛の分岐へ渡さない（行は型の表記と正規型だけを持ち、型の値を持たない）"
        ),
        ValuePoolSubject.NotReceiving("Dialect/Identifiers", NamesAndText),
        ValuePoolSubject.NotReceiving(
            "Dialect/DdlText",
            "DDL・同期スクリプトの文の形（識別子・制約名・注記）を受け、行の値を比較・束縛・書き込みの分岐へ渡さない"
        ),
        ValuePoolSubject.NotReceiving("Dialect/ImportWarnings", NamesAndText),
        ValuePoolSubject.NotReceiving(
            "CodegenConfig/Errors",
            "生成の構成（オプションと図の形）を受け、生成コードへ型の値を渡して実行しない（値の端は生成コードのランタイムの部分が受ける）"
        ),
        ValuePoolSubject.NotReceiving(
            "CodegenConfig/Success",
            "生成の構成（オプションと図の形）を受け、生成コードへ型の値を渡して実行しない（値の端は生成コードのランタイムの部分が受ける）"
        ),
        ValuePoolSubject.NotReceiving(
            "CodegenConfig/Cases",
            "生成の構成の決まった行（図・オプション・期待の構造）を受け、探針の実行は形を確かめる代表の値だけで値の端の水準を選ばない"
        ),
        ValuePoolSubject.NotReceiving("DiagramEdit/Tools", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DiagramEdit/UndoOps", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DiagramEdit/Selection", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DiagramEdit/Layout", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DiagramEdit/CanvasInput", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DocumentFile/Scenarios", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DocumentFile/Random", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DocumentFile/Format", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("DocumentFile/Settings", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving(
            "FormatRoundTrip/RoundTrip",
            "図の形（表・列・制約・型の表記・名前）と出力形式の往復を受け、行の値を DB の比較・束縛の分岐へ渡さない"
        ),
        ValuePoolSubject.NotReceiving(
            "FormatRoundTrip/Omissions",
            "図の形（表・列・制約・型の表記・名前）と出力形式の往復を受け、行の値を DB の比較・束縛の分岐へ渡さない"
        ),
        ValuePoolSubject.NotReceiving(
            "FormatRoundTrip/Rules",
            "図の形（表・列・制約・型の表記・名前）と出力形式の往復を受け、行の値を DB の比較・束縛の分岐へ渡さない"
        ),
        ValuePoolSubject.NotReceiving(
            "FormatRoundTrip/Merge",
            "図の形（表・列・制約・型の表記・名前）と出力形式の往復を受け、行の値を DB の比較・束縛の分岐へ渡さない"
        ),
        ValuePoolSubject.NotReceiving("Ai/ChatEngines", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/Drivers", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/Attachments", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/ConnectionSettings", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/ApiKeyStore", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/MarkdownRender", NamesAndText),
        ValuePoolSubject.NotReceiving("Ai/ChatDialog", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/ProcessLaunch", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/ShellPermission", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/PathEntrances", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/AtomicFile", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/Containment", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/Segments", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/Lifecycle", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/PostExitExhaustive", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/ShutdownChainExhaustive", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/FeatureModules", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("OsBoundary/ProfileStore", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving(
            "Guard/SourceRules",
            "ソース・設定のファイルの文字列と構文を受け、DB の比較・束縛の分岐へ型の値を渡さない"
        ),
        ValuePoolSubject.NotReceiving(
            "Guard/Parity",
            "正本の一覧と写し（docs・ワークフロー・宣言）の照合を受け、DB の比較・束縛の分岐へ型の値を渡さない"
        ),
        ValuePoolSubject.NotReceiving("Guard/Phrases", NamesAndText),
        ValuePoolSubject.NotReceiving("Ai/MockAgents", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/MockRunner", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/MockBuildBoundary", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/MockScaffold", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/MockFolder", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/MockOutputs", NamesAndText),
        ValuePoolSubject.NotReceiving("Ai/MockDialog", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/DbConnectionDialog", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("Ai/DbImportSync", GuiOrOsOperations),
        // ---- 値を受けない部分（QuickER.Tests の単体の表） ----
        ValuePoolSubject.NotReceiving(
            "table:EditModelScenarios",
            "生成した EditModel・Mapper の C# の中の判定（入力の書式・空欄・範囲の削除・Removed・null）を受け、DB の比較・束縛の分岐へ値の端を渡さない（代表の値は書式と状態の遷移を見るためのもの）"
        ),
        ValuePoolSubject.NotReceiving(
            "table:ValueObjectScenarios",
            "値オブジェクトの C# の中の判定（呼び出し・同一性・等値・作成時の検証）を受け、DB の比較・束縛の分岐へ値の端を渡さない（精度の検証の値は値オブジェクトの検証の腕を見る代表）"
        ),
        ValuePoolSubject.NotReceiving("table:ForeignKeyReferentialActions", NamesAndText),
        ValuePoolSubject.NotReceiving("table:Converters", GuiOrOsOperations),
        ValuePoolSubject.NotReceiving("table:QueryToolFormatter", NamesAndText),
        // ---- 値を受けない部分（Integration の台帳） ----
        ValuePoolSubject.Deferred("SyncModel/Random", SyncModelOutOfScope, SyncModelValues),
        ValuePoolSubject.Deferred("SyncModel/Outcomes", SyncModelOutOfScope, SyncModelValues),
        ValuePoolSubject.Deferred("SyncModel/Config", SyncModelOutOfScope, SyncModelValues),
        ValuePoolSubject.NotReceiving("SyncPlan/Fuzz", SyncPlanShapes),
        ValuePoolSubject.NotReceiving("SyncPlan/CapabilityNet", SyncPlanShapes),
        ValuePoolSubject.NotReceiving("SyncPlan/ShapeRows", SyncPlanShapes),
        ValuePoolSubject.NotReceiving("SyncPlan/HistoryRows", SyncPlanShapes),
        ValuePoolSubject.NotReceiving(
            "GeneratedRuntime/Parity",
            "本流の操作の列（手の種類・対象の行・順）を受ける。書く値の幅は同じ生成器の別の乱数の列で、値の列の部分 GeneratedRuntime/ParityValues が受ける"
        ),
        ValuePoolSubject.NotReceiving(
            "GeneratedRuntime/ParityOutcomes",
            "本流の操作の列で Save フックが受けた段・操作・規則の振る舞いを数え、型の値を受けない"
        ),
        ValuePoolSubject.NotReceiving(
            "GeneratedRuntime/ParitySqlServerSaves",
            "保存の経路と行の役（代替キー・版・他者の変更・フックの飛ばし・渡す順）を受ける。値の幅は SQL Server の系統の値の往復 GeneratedRuntime/ParityValuesSqlServer が受ける"
        ),
        ValuePoolSubject.NotReceiving("Name/DdlRoundTrip", NamesAndText),
        // ---- 値を受けない部分（Integration の単体の表） ----
        ValuePoolSubject.NotReceiving(
            "table:NamedQueryRawSql",
            "自由 SQL の戻り形（スカラー・単一・件数・射影・IN の展開の置換の境界）を受け、シードは決まった代表の値で値の端の水準を選ばない"
        ),
        ValuePoolSubject.Deferred(
            "table:SyncTriggerRecording",
            SyncModelOutOfScope,
            SyncModelValues
        ),
        ValuePoolSubject.Deferred("table:SyncTriggerGuard", SyncModelOutOfScope, SyncModelValues),
        ValuePoolSubject.Deferred("table:SyncEngineFixed", SyncModelOutOfScope, SyncModelValues),
        ValuePoolSubject.NotReceiving("table:SyncPlanExpectations", SyncPlanShapes),
    ];

    /// <summary>QuickER.Tests で観測する部分（<c>ValuePoolUseTests</c> が流す）</summary>
    public static IReadOnlyList<string> ObservedInUnitTests { get; } =
    [
        "Dsl/SqliteMySql",
        "Dsl/SqlServer",
        "Dsl/PostgreSql",
        "Dsl/MySql",
        "Dsl/Oracle",
        "table:SqlServerQueryParameters",
        "table:PostgreSqlQueryParameters",
        "table:TranslatorConversions",
    ];

    /// <summary>Integration で観測する部分（<c>ValuePoolUseIntegrationTests</c> が流す）</summary>
    public static IReadOnlyList<string> ObservedInIntegration { get; } =
    [
        "GeneratedRuntime/TranslatorParityBindings",
        "table:TranslatorParityPredicates",
        "GeneratedRuntime/ParityValues",
        "GeneratedRuntime/ParityValuesSqlServer",
        "Dialect/TypesRealDb",
    ];

    /// <summary>値を受けるのに DB なしで観測できない部分（理由と、観測を足す区切り）</summary>
    public static IReadOnlyList<ValuePoolUnobserved> Unobserved { get; } =
    [
        new(
            "table:SyncKeyPaging",
            "行の値はテストの閉包の中で作られ、実 SQL Server へ書くハーネス（SyncKeyPagingHarness）を通してしか取り出せない。DB なしで読むには行の入力を値のデータへ分けるか、ハーネスに観測の口を足す（同期の区切り e の近くなので、決めずに残す）",
            SyncKeyValues
        ),
    ];

    // ---- 部分をまたいで共有する受けない水準の行 ----

    /// <summary>decimal の型の端（decimal.MaxValue・MinValue）</summary>
    private static ValueEdgeNotDrawn DecimalTypeEnd() =>
        ValueEdgeNotDrawn.Accepted(
            "decimal-type-end-same-branch",
            ["dec-type-end"],
            "decimal.MaxValue・MinValue は列の精度の外の値（dec-beyond-column-range）か long の範囲の外の値（dec-beyond-long）と同じ分岐（列の型に入らないので値の型で束縛し直すか、列の型で束縛すると桁あふれ）に入り、製品は型の端だけで答えが変わる分岐を持たない",
            "`Dialect/TypesRealDb`"
        );

    // ---- 簡易 DSL の総当たりの部分が共有する受けない水準の行 ----

    /// <summary>SQL Server の datetime・smalldatetime の刻みと範囲（SQL Server でない方言）</summary>
    private static ValueEdgeNotDrawn DslSqlServerLegacy() =>
        ValueEdgeNotDrawn.Accepted(
            "dsl-sqlserver-legacy",
            [
                "dt-legacy-read-back",
                "dt-legacy-between-read-back-and-step",
                "dt-small-rounding-boundary",
                "dt-before-sql-datetime-min",
                "dt-before-smalldatetime-min",
                "dt-past-sql-datetime-max",
                "dt-past-smalldatetime-max",
                "dt-sql-datetime-range-end",
                "dt-smalldatetime-range-end",
            ],
            "datetime・smalldatetime の刻み（1/300 秒・分）と範囲の端の分岐は SQL Server の datetime・smalldatetime の列にだけあり、この方言はその型を持たない。簡易 DSL の値は SQL Server の網羅の単位 coverage-sqlserver-legacy-datetime が持つ",
            "`Dsl/SqlServer`"
        );

    /// <summary>暦の端の年</summary>
    private static ValueEdgeNotDrawn DslCalendarEnd() =>
        ValueEdgeNotDrawn.Pending(
            "dsl-calendar-end",
            ["dt-calendar-end"],
            "簡易 DSL の日時の型の範囲（未起動）",
            "暦の端の年（0001・9999）の DateTime は、この方言の単位の値のプールに無い。方言ごとに DateTime の列の範囲が違う（MySQL の datetime は 1000 年から・Oracle の DATE は 9999 年まで・SQLite は TEXT）ので、足すには DslLegacyDateTimeType と同じ形の方言ごとの範囲の宣言と、範囲の外を ValueFit の beyond-range に数える読み替えと、オラクルへ写す docs の割り切り（範囲の外の扱い）が要る",
            "`Dsl/SqlServer`"
        );

    /// <summary>DateTimeOffset の列の小数秒の桁（SQL Server でない方言）</summary>
    private static ValueEdgeNotDrawn DslOffsetScale() =>
        ValueEdgeNotDrawn.Accepted(
            "dsl-offset-scale",
            ["dto-beyond-column-scale"],
            "小数秒の桁を宣言する DateTimeOffset の列は SQL Server の単位（datetimeoffset(2)）だけで、この方言の単位の列は桁を宣言しない（tests/QuickER.Tests/TestSupport/CLAUDE.md の簡易 DSL の総当たりの網羅しない組の受け入れ）",
            "`Dsl/SqlServer`"
        );

    /// <summary>MySQL の time の範囲の外</summary>
    private static ValueEdgeNotDrawn DslMySqlTimeRange() =>
        ValueEdgeNotDrawn.Accepted(
            "dsl-mysql-time-range",
            ["ts-beyond-mysql-range"],
            "範囲の外の TimeSpan は EF Core 版が送れず例外にするので（docs の TimeSpan の既知の割り切り）、MySQL の単位の値のプールは ±838:59:59 の中だけ。範囲の外の書き込みは方言の型の表の実 DB の段が持つ",
            "`Dialect/TypesRealDb`"
        );

    /// <summary>先頭の空白</summary>
    private static ValueEdgeNotDrawn DslLeadingSpace() =>
        ValueEdgeNotDrawn.Accepted(
            "dsl-leading-space-not-branched",
            ["str-leading-space"],
            "製品も DB も先頭の空白で分かれない（docs の空白の扱いは末尾の空白だけで、比べ方の規則 DslCaseRule も先頭を読まない）",
            NotGenerated.Unguarded
        );

    /// <summary>サロゲートペア</summary>
    private static ValueEdgeNotDrawn DslSurrogate() =>
        ValueEdgeNotDrawn.Pending(
            "dsl-surrogate",
            ["str-surrogate"],
            "簡易 DSL のサロゲートペア（未起動）",
            "LIKE の _ がサロゲートペアを 1 文字と数えるか 2 単位と数えるかが方言で違い、docs に定めが無い。足すには docs に方言ごとの扱いを書いてからオラクルへ写す必要がある",
            "`table:TranslatorParityPredicates`"
        );

    /// <summary>条件の比較が分かれないバイナリと Guid の値</summary>
    private static ValueEdgeNotDrawn DslBinaryGuidNotBranched(string[] edges) =>
        ValueEdgeNotDrawn.Accepted(
            "dsl-binary-guid-not-branched",
            edges,
            "条件の比較・束縛はバイト列の長さと Guid の値で分かれない（長さで変わる経路は無制限バイナリ列の書き込みのストリーミングで条件の比較に無く、Guid.Empty も 16 バイトの値として送る。GuidKey の値オブジェクトの生成の規則は値オブジェクトの表が受ける）",
            NotGenerated.Unguarded
        );

    /// <summary>長さを宣言しないバイナリの列</summary>
    private static ValueEdgeNotDrawn DslUnsizedBinary(string type) =>
        ValueEdgeNotDrawn.Accepted(
            "dsl-unsized-binary",
            ["bin-beyond-column-length", "bin-exact-column-length", "bin-zero-excess"],
            $"{type} は長さを宣言しない（固定長のバイナリを持たない）ので、列の長さと比べる水準が無い",
            "`Dsl/SqlServer`"
        );
}
