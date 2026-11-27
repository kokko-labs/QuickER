using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.TestSupport;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// 値の束縛の生成の表の既知の失敗（範囲は <c>{実装先}/binding</c>・署名は行の Id。載っていない失敗も、載っているのに起きない行も赤）
/// </summary>
/// <remarks>
/// <para>
/// どの行が割れるかは、docs の既知の割り切りか直す前の製品の振る舞いを C# へ写した変え方（DB が値を受け取った形・変換を外した形）で比べる値か
/// 式を変え、同じシードへ当てた C# の答えが変わる行として決める（行を手で並べない。網羅の配列の行が変わっても項目が追従する）。値に依らず必ず
/// 例外になる形は、形だけで決める。1 つの行は 1 つの項目にだけ当てる（規則の並びの前の規則が先に取る）。
/// </para>
/// <list type="bullet">
/// <item>EF Core 版の SQL Server（Accepted）：datetime・smalldatetime の列と比べる DateTime の定数（EF Core は行に依らないメソッド呼び出しの値も
/// 定数として書く）はその列の型のリテラルになり、QuickErDbContext の ExecuteUpdate の WHERE はその列の型のパラメータのまま送られる。どちらも列の
/// 刻み（1/300 秒・分）へ丸めて比べ、範囲の外は断られる（docs/code-generation.md の「Known limitation」2 つ）。</item>
/// <item>EF Core 版の SQLite（Accepted）：QuickErDbContext の ExecuteUpdate の WHERE の整数と decimal の等値・IN は書き換えられず、文字列で比べる
/// （docs/code-generation.md の EF Core モードの SQLite の書き換えは Query() の条件だけ）。</item>
/// <item>EF Core 版の 5 方言（Pending・きっかけ待ち）：<see cref="EfCoreCharValueFinding"/>。整数の列と比べる捕捉した char の値を、EF Core が
/// 整数へ広げずに char のまま（文字列として）送る（区切り a のフェーズ 4 の値の形 implicit-char の水準で見つけた。方言ごとの形は
/// <see cref="CharValueSentAsText"/>）。</item>
/// <item>EF Core 版の SQL Server・PostgreSQL（Accepted）：date の列と比べる DateTime の定数・メソッド呼び出しは date のリテラルになり時刻が落ちる
/// （docs の同じ Known limitation）。SQL Server は日付部品（<c>CONVERT(date, …)</c>）と比べる定数も同じ（予測・Docker で確かめる）。</item>
/// <item>EF Core 版の PostgreSQL（Accepted）：timestamp の列と比べる Kind=Utc の DateTime は Npgsql が断る。割り切るのは、インターセプターが
/// 値を変えない ExecuteUpdate の WHERE（docs の EF Core モードの PostgreSQL の DateTimeKind.Utc の項：a Utc value in the WHERE of an ExecuteUpdate
/// you write on QuickErDbContext is refused）と、EF Core がリテラルで書く定数・メソッド呼び出しの値（docs の EF Core モードの PostgreSQL の列型の項：
/// a Utc DateTime written as a constant and compared with a timestamp column is refused と Known limitation）。</item>
/// <item>EF Core 版の PostgreSQL（Pending・きっかけ待ち）：timestamp の列と比べる DateTime の定数・メソッド呼び出しのリスト・配列は、翻訳が
/// NotSupportedException で断る（docs の同じ Known limitation に変数で渡す回避を書いた）。</item>
/// <item>台帳の結び済みの 5 項目（ha1・ha2・ha4・xb1・xt4）と台帳の外の quicker-in-over-date-part は区切り a のフェーズ 3 で直した（項目は修正済み・
/// 規則は無い）。パラメータで送る Query・ExecuteDelete の Kind=Utc の値（ha1）は、インターセプターが Kind を外して送る。</item>
/// <item>QuickER 版の SQLite・SQL Server（Accepted）：<see cref="QuickErNarrowingColumnCastFinding"/>。列の側の値を変える明示の変換は、翻訳器が変換を
/// 名指しした NotSupportedException で断る（docs/code-generation.md の Known limitation。値に依らず必ず例外なので形だけで決める）。</item>
/// <item>台帳の外の項目（区切り a のフェーズ 2 で水準を足して見つけた）：<see cref="EfCoreNarrowingColumnCastFinding"/>（Pending・
/// きっかけ待ち＝<see cref="TriggerSlice"/>。EF Core 版の列の側の値を変える明示の変換は、予測を DB の CAST の意味で作る〔<see cref="NarrowingColumnCast"/>〕：
/// SQLite・MySQL は値を変えない変換に書いて黙って割れ、SQL Server は int・bigint の列で範囲の外の行のある表で断り、PostgreSQL・Oracle は bigint の列で
/// 断って int の列で -1 の行を折り返さずに黙って割れる。符号だけの変換は 5 方言とも折り返さずに黙って割れ、SQLite の ulong どうしの大小の比較だけは
/// 翻訳が断る〔符号だけの変換の水準の行・規則 <see cref="SignOnlyColumnCast"/>〕。docs の Known limitation に回避を書いた）。</item>
/// <item>EF Core 版の SQLite（Pending・きっかけ待ち）：<see cref="SqliteSignCastDecimalEqualityFinding"/>。符号だけの変換の列と decimal の値の等値・IN を
/// 文字列で比べる（値のプールの正本の区切りのフェーズ 3 で、符号だけの変換と decimal の値の組を作れるようにして見つけた）。</item>
/// </list>
/// </remarks>
internal static class TranslatorParityBindingKnownFailures
{
    /// <summary>EF Core 版の SQL Server の定数の割り切りの指摘の名前</summary>
    public const string SqlServerConstantFinding = "efcore-sqlserver-legacy-datetime-constant";

    /// <summary>EF Core 版の SQL Server の ExecuteUpdate の割り切りの指摘の名前</summary>
    public const string SqlServerExecuteUpdateFinding =
        "efcore-sqlserver-execute-update-legacy-datetime";

    private const string SqlServerConstantReason =
        "EF Core 版は SQL Server の datetime・smalldatetime の列と比べる DateTime の定数（行に依らないメソッド呼び出しの値も EF Core は定数として書く）をその列の型のリテラルとして SQL に書くので、列が持てない値は比べる前に列の刻みへ丸められ、範囲の外は変換のエラーになる（docs/code-generation.md の Known limitation：a constant written in hand-written LINQ is not a parameter）";

    /// <summary>EF Core 版の SQLite の ExecuteUpdate の整数と decimal の等値の割り切りの指摘の名前</summary>
    public const string SqliteExecuteUpdateFinding =
        "efcore-sqlite-execute-update-integer-decimal-equality";

    /// <summary>EF Core 版の整数の列と比べる char の値の指摘の名前（きっかけ待ち）</summary>
    public const string EfCoreCharValueFinding = "efcore-char-value-sent-as-text";

    private const string SqliteExecuteUpdateReason =
        "整数と decimal の等値・IN を整数の比較へ書き換えるのは Query() の条件だけで、QuickErDbContext へ書く ExecuteUpdate の WHERE は EF Core の SQLite プロバイダが文字列の比較にし、整数の値の decimal はどの行とも等しくない（docs/code-generation.md の EF Core モードの SQLite の書き換えの段落：It applies to the conditions of Query()）";

    private const string SqlServerExecuteUpdateReason =
        "QuickErDbContext へ書く ExecuteUpdate の WHERE は、SqlServerQueryParameterInterceptor が SET と見分けられないので datetime・smalldatetime の値を列の型のパラメータのまま送り、列の刻みへ丸めて比べる（範囲の外は SqlClient が断る。docs/code-generation.md の Known limitation：the WHERE of an ExecuteUpdate you write on QuickErDbContext yourself）";

    /// <summary>EF Core 版の date の列の定数の割り切りの指摘の名前</summary>
    public const string DateConstantFinding = "efcore-date-constant-literal";

    private const string DateConstantReason =
        "EF Core 版は date の列と比べる DateTime の定数（行に依らないメソッド呼び出しの値も EF Core は定数として書く）を列の型の date のリテラルとして SQL に書くので、時刻が落ちて比べられる（docs/code-generation.md「Letter case, whitespace, and date parts」の Known limitation：a constant written in hand-written LINQ is not a parameter）";

    /// <summary>EF Core 版の PostgreSQL の定数のリストの指摘の名前（きっかけ待ち）</summary>
    public const string PostgreSqlConstantListFinding =
        "efcore-postgresql-constant-datetime-list-on-timestamp";

    /// <summary>EF Core 版の PostgreSQL の timestamp の列と Kind=Utc の値の割り切りの指摘の名前</summary>
    public const string PostgreSqlUtcOnTimestampFinding = "efcore-postgresql-utc-on-timestamp";

    private const string PostgreSqlUtcOnTimestampReason =
        "QuickErDbContext へ書く ExecuteUpdate の WHERE は、PostgreSqlQueryParameterInterceptor が SET と見分けられないので値を変えず、Npgsql は Kind=Utc の DateTime を timestamp（without time zone）の値として送らずに ArgumentException で断る（docs/code-generation.md の EF Core モードの PostgreSQL の DateTimeKind.Utc の項：a Utc value in the WHERE of an ExecuteUpdate you write on QuickErDbContext is refused, as a value it writes is）";

    /// <summary>EF Core 版の PostgreSQL の timestamp の列と Kind=Utc の定数の割り切りの指摘の名前</summary>
    public const string PostgreSqlUtcLiteralOnTimestampFinding =
        "efcore-postgresql-utc-literal-on-timestamp";

    private const string PostgreSqlUtcLiteralOnTimestampReason =
        "EF Core は定数（行に依らないメソッド呼び出しの値も）を SQL のリテラルとして書き、Npgsql は Kind=Utc の DateTime から timestamp（without time zone）のリテラルを作れずに ArgumentException で断る。パラメータでないのでインターセプターの外（docs/code-generation.md の EF Core モードの PostgreSQL の列型の項：a Utc DateTime written as a constant and compared with a timestamp column is refused と、Known limitation：a constant written in hand-written LINQ is not a parameter）";

    /// <summary>QuickER 版の列の側の値を変える明示の変換を翻訳器が断る割り切りの指摘の名前</summary>
    public const string QuickErNarrowingColumnCastFinding = "quicker-narrowing-column-cast";

    private const string QuickErNarrowingColumnCastReason =
        "QuickER 版の翻訳器は、列の側に書いた値を変える明示の変換（縮める整数の変換・小数から整数への変換）を、変換を名指しした NotSupportedException で断る。C# は行ごとに値を折り返すか小数部を落とし、SQL の CAST は同じにならないので翻訳しない（docs/code-generation.md「Letter case, whitespace, and date parts」の Known limitation：the QuickER Repository refuses a cast written on a column that can change the column's value）";

    /// <summary>EF Core 版の列の側の値を変える明示の変換の指摘の名前（台帳の外・区切り a のフェーズ 2 で見つけた）</summary>
    public const string EfCoreNarrowingColumnCastFinding = "efcore-narrowing-column-cast";

    /// <summary>直す区切りの決まっていない Pending の直す区切り（<c>RuntimeMatrixRows.Trigger</c> と同じ綴り）</summary>
    public const string TriggerSlice = RuntimeMatrixRows.Trigger;

    /// <summary>実装先の範囲</summary>
    /// <param name="target">実装先</param>
    public static string ScopeOf(RuntimeTarget target) => $"{target}/binding";

    /// <summary>実装先の既知の失敗の項目</summary>
    /// <param name="target">実装先</param>
    public static IReadOnlyList<KnownEntry> EntriesFor(RuntimeTarget target) =>
        Entries.Value.TryGetValue(target, out var entries) ? entries : [];

    /// <summary>
    /// 1 行の結果を、その行の項目だけの一覧と照合する（一致しない行が一覧に無ければ赤・一覧にある行が一致すれば赤）
    /// </summary>
    /// <param name="target">実装先</param>
    /// <param name="id">行の Id</param>
    /// <param name="outcome">結果</param>
    public static void AssertRow(RuntimeTarget target, string id, BindingRunOutcome outcome)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{target} {id}\n  述語: {TranslatorParityBindingRows.Rows[id].Input.Predicate}\n{outcome.Describe()}"
        );

        var set = new KnownFailureSet(
            EngineIds.GeneratedRuntime,
            [.. EntriesFor(target).Where(entry => entry.Signature == id)]
        );
        IReadOnlyList<EngineFailure> failures = outcome.Matched
            ? []
            : [new EngineFailure(ScopeOf(target), id, outcome.Describe(), Source: outcome)];

        set.Classify(failures, _ => true).AssertClean($"値の束縛の生成の表（{target}）");
    }

    /// <summary>一覧の項目の問題（実装先が流さない行・表に無い行を名指す。項目の整合は一覧を作るときに検査する）</summary>
    /// <param name="target">実装先</param>
    public static IEnumerable<string> Problems(RuntimeTarget target)
    {
        _ = new KnownFailureSet(EngineIds.GeneratedRuntime, EntriesFor(target));

        foreach (var entry in EntriesFor(target))
        {
            if (entry.Scope != ScopeOf(target))
            {
                yield return $"{entry.Signature}: 範囲 {entry.Scope} が {ScopeOf(target)} でない";
            }

            if (
                entry.Signature is not { } id
                || !TranslatorParityBindingRows.Rows.Any(row =>
                    row.Id == id && TranslatorParityBindingRows.RunsOn(target, row.Input.Levels)
                )
            )
            {
                yield return $"{entry.Signature}: {target} が流す生成の表の行に無い";
            }
        }
    }

    // ===== 規則 =====

    /// <summary>1 つの規則（当てはまる行・指摘の名前・種類・直す区切りか理由）</summary>
    private sealed record Rule(
        Func<TranslatorParityBinding, bool> Applies,
        string Finding,
        KnownKind Kind,
        string? FixedIn,
        string? Reason
    );

    /// <summary>実装先ごとの規則（前の規則が先に行を取る）</summary>
    private static IReadOnlyList<Rule> RulesFor(RuntimeTarget target) =>
        target switch
        {
            RuntimeTarget.AdoSqlite => [QuickErNarrowingColumnCast, QuickErUlongColumnCast],
            RuntimeTarget.AdoSqlServer => [QuickErNarrowingColumnCast, QuickErUlongColumnCast],
            RuntimeTarget.EfCoreSqlite =>
            [
                new(
                    row =>
                        row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate
                        && IsIntegerDecimalEquality(row)
                        && Diverges(row, NeverEqual, NeverEqual),
                    SqliteExecuteUpdateFinding,
                    KnownKind.Accepted,
                    null,
                    SqliteExecuteUpdateReason
                ),
                CharValueSentAsText(RuntimeTarget.EfCoreSqlite),
                // UInt64 どうしの大小の比較は、EF Core の SQLite プロバイダが翻訳できずに断る（値に依らず必ず例外なので形だけで決める。
                // 比べる型が decimal〔decimal・符号つきの値と比べる〕なら ef_compare で数として比べ、断らない）
                new(
                    row =>
                        row.Levels[TranslatorParityBindingRows.ColumnShape]
                            == TranslatorParityBindingRows.ULongCast
                        && row.ComparedType == typeof(ulong)
                        && IsOrdering(row),
                    SqliteUlongOrderingFinding,
                    KnownKind.Accepted,
                    null,
                    SqliteUlongOrderingReason
                ),
                // 符号だけの変換の列と decimal の値の等値・IN は、Query() の条件の書き換え（SqliteIntegralDecimalEquality）が変換のある列を
                // 書き換えず、EF Core の SQLite プロバイダが文字列で比べる（整数の値の decimal はどの行とも等しくない）
                new(
                    row =>
                        TranslatorParityBindingRows.IsSignCastShape(
                            row.Levels[TranslatorParityBindingRows.ColumnShape]
                        )
                        && row.ComparedType == typeof(decimal)
                        && IsEquality(row)
                        && Diverges(row, NeverEqual, NeverEqual),
                    SqliteSignCastDecimalEqualityFinding,
                    KnownKind.Pending,
                    TriggerSlice,
                    SqliteSignCastDecimalEqualityReason
                ),
                SignOnlyColumnCast(SignOnlySqliteReason, sqlite: true),
                NarrowingColumnCast(_ => false, WithoutColumnCast, NarrowingSqliteReason),
            ],
            RuntimeTarget.EfCoreSqlServer =>
            [
                new(
                    row =>
                        row.Levels[TranslatorParityBindingRows.ColumnShape]
                            != TranslatorParityBindingRows.DatePart
                        && row.ColumnType
                            is TranslatorParityBindingRows.LegacyDateTime
                                or TranslatorParityBindingRows.SmallDateTime
                        && IsLiteralOnEfCore(row)
                        && Diverges(row, SqlServerRounding(row), SqlServerRounding(row)),
                    SqlServerConstantFinding,
                    KnownKind.Accepted,
                    null,
                    SqlServerConstantReason
                ),
                // ExecuteUpdate の WHERE は列の型のパラメータのまま送る（日付部品と比べるパラメータも、元の列の型で送られる。日付部品と比べる
                // 定数は date のリテラルになるので、後ろの date の定数の規則が取る）
                new(
                    row =>
                        (
                            row.Levels[TranslatorParityBindingRows.ColumnShape]
                                != TranslatorParityBindingRows.DatePart
                            || !IsLiteralOnEfCore(row)
                        )
                        && row.ColumnType
                            is TranslatorParityBindingRows.LegacyDateTime
                                or TranslatorParityBindingRows.SmallDateTime
                        && row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate
                        && Diverges(row, SqlServerRounding(row), SqlServerRounding(row)),
                    SqlServerExecuteUpdateFinding,
                    KnownKind.Accepted,
                    null,
                    SqlServerExecuteUpdateReason
                ),
                DateConstant(datePartIsDate: true),
                CharValueSentAsText(RuntimeTarget.EfCoreSqlServer),
                SignOnlyColumnCast(SignOnlySqlServerReason),
                // int の列の (byte) は tinyint への CAST で -1 の行が、bigint の列の (short) は smallint への CAST で long の上限・下限の行が
                // 範囲の外になり、どの形も断る（decimal の列の (int) は小数部を切り捨て、C# と同じ値になる）
                NarrowingColumnCast(
                    row =>
                        row.ColumnType
                            is TranslatorParityBindingRows.Int
                                or TranslatorParityBindingRows.BigInt,
                    WithoutColumnCast,
                    NarrowingSqlServerReason
                ),
            ],
            RuntimeTarget.EfCorePostgreSql =>
            [
                DateConstant(datePartIsDate: false),
                // PostgreSQL の timestamp の列（datetime・datetime2・smalldatetime を写した列）と比べる DateTime の定数・メソッド呼び出しの
                // リスト・配列は、EF Core の Npgsql の翻訳が timestamptz の配列とみなして NotSupportedException で断る（値に依らず必ず割れる。
                // 捕まえた変数のリストはパラメータになって通る）。新しい指摘・きっかけ待ち
                new(
                    row =>
                        IsTimestampColumn(row)
                        && row.Levels[TranslatorParityBindingRows.ValueCount]
                            != TranslatorParityBindingRows.Single
                        && IsLiteralOnEfCore(row),
                    PostgreSqlConstantListFinding,
                    KnownKind.Pending,
                    TriggerSlice,
                    null
                ),
                // timestamp の列と Kind=Utc の値：ExecuteUpdate の WHERE（インターセプターは SET と見分けられないので値を変えない）は書く規則の
                // 割り切り、定数・メソッド呼び出しの値は EF Core が書くリテラル（インターセプターの外）の割り切り。パラメータで送る Query・
                // ExecuteDelete はインターセプターが Kind を外して送る（ha1・区切り a で直した）
                new(
                    row =>
                        IsTimestampColumn(row)
                        && row.Levels[TranslatorParityBindingRows.DateTimeKind]
                            == TranslatorParityBindingRows.Utc
                        && row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate,
                    PostgreSqlUtcOnTimestampFinding,
                    KnownKind.Accepted,
                    null,
                    PostgreSqlUtcOnTimestampReason
                ),
                new(
                    row =>
                        IsTimestampColumn(row)
                        && row.Levels[TranslatorParityBindingRows.DateTimeKind]
                            == TranslatorParityBindingRows.Utc
                        && IsLiteralOnEfCore(row),
                    PostgreSqlUtcLiteralOnTimestampFinding,
                    KnownKind.Accepted,
                    null,
                    PostgreSqlUtcLiteralOnTimestampReason
                ),
                CharValueSentAsText(RuntimeTarget.EfCorePostgreSql),
                SignOnlyColumnCast(SignOnlyPostgreSqlReason),
                // bigint の列の (short) は smallint に入らない行で断り、int の列の (byte) は smallint への CAST で -1 の行を折り返さない
                NarrowingColumnCast(
                    row => row.ColumnType == TranslatorParityBindingRows.BigInt,
                    WithoutColumnCast,
                    NarrowingPostgreSqlReason
                ),
            ],
            RuntimeTarget.EfCoreMySql =>
            [
                CharValueSentAsText(RuntimeTarget.EfCoreMySql),
                SignOnlyColumnCast(SignOnlyMySqlReason),
                NarrowingColumnCast(_ => false, WithoutColumnCast, NarrowingMySqlReason),
            ],
            RuntimeTarget.EfCoreOracle =>
            [
                CharValueSentAsText(RuntimeTarget.EfCoreOracle),
                SignOnlyColumnCast(SignOnlyOracleReason),
                // bigint の列の (short) は NUMBER(5) に入らない行で断り、int の列の (byte) は TO_NUMBER(列, 'xx') で列の 10 進の綴りを
                // 16 進として読む
                NarrowingColumnCast(
                    row => row.ColumnType == TranslatorParityBindingRows.BigInt,
                    row =>
                        row.ColumnType == TranslatorParityBindingRows.Int
                            ? DivergesOnColumnImage(row, OracleHexImage)
                            : WithoutColumnCast(row),
                    NarrowingOracleReason
                ),
            ],
            _ => [],
        };

    /// <summary>
    /// EF Core 版の整数の列と比べる捕捉した char の値（Pending・きっかけ待ち）：EF Core は char を整数へ広げる変換を外し、値を char のまま
    /// （文字列として）送る。どの行が割れるかは方言の扱いで決める（Docker の実行で読んだもの）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>SQLite：ExecuteUpdate の WHERE と ExecuteDelete の条件で、変換を書かない int の列（素の列）と比べる値を TEXT で送り、SQLite は
    /// 整数の列をどの TEXT よりも小さいとみなす（例外にならず黙って割れる。値を整数の上限へ置き換えた C# の答えになる行。ExecuteDelete は
    /// 値のプールの正本の区切りで網羅の配列の並びが変わり、初めて出た組で確かめた）。列の側に変換を書いた形は EF Core が
    /// <c>CAST(unicode(@Value) AS INTEGER)</c> で数にして送るので C# と一致する。Query() の条件も C# と一致する。</item>
    /// <item>bigint の列（5 方言）：C# が char を long へ広げ（値を保つ変換では decimal へ）、EF Core はその型のパラメータ（Int64・decimal）で
    /// 送るので C# と一致する（値の束縛の生成の表で、bigint の列の ExecuteUpdate・値を保つ変換の ExecuteDelete の行を確かめた）。以下の方言の
    /// 振る舞いはどれも int の列のもの。</item>
    /// <item>PostgreSQL：ExecuteUpdate の WHERE は integer のパラメータに char を書けず InvalidCastException、列の側に値を保つ変換を書いた形は
    /// 文字列のパラメータを integer へ変換して 22P02（どちらも値に依らず必ず例外なので形だけで決める）。</item>
    /// <item>SQL Server・Oracle：列の側に値を保つ変換を書いた形は文字列のパラメータを int・NUMBER へ変換して断る（変換のエラー・ORA-01722。
    /// 形だけで決める）。変換の無い列の ExecuteUpdate は C# と一致する。</item>
    /// <item>MySQL：列の側に値を保つ変換を書いた形は、文字列のパラメータを数へ変換する。書き込みの文（ExecuteDelete・ExecuteUpdate）は厳格の
    /// モードで Truncated incorrect DOUBLE value の例外、Query() は警告だけで値を 0 として比べ黙って割れうる（値を 0 へ置き換えた C# の答えに
    /// なる行。今のシードでは 0 を持つ行が無く一致する）。</item>
    /// </list>
    /// 回避は docs/code-generation.md の EF Core 版の Known limitation（値を <c>(int)c</c> か int の変数にして渡す）。
    /// </remarks>
    /// <param name="target">EF Core の実装先</param>
    private static Rule CharValueSentAsText(RuntimeTarget target) =>
        new(
            row =>
            {
                if (
                    row.Levels[TranslatorParityBindingRows.ValueShape]
                    != TranslatorParityBindingRows.ImplicitChar
                )
                {
                    return false;
                }

                // bigint の列は、C# が char を long（値を保つ変換なら decimal）へ広げた値を EF Core がそのまま Int64・decimal のパラメータで
                // 送るので割れない（2026-11-27 の Docker の実行で 5 方言とも C# と一致した：@p Int64/Bigint 1・@p Decimal/Numeric 1 ほか）
                if (row.ColumnType != TranslatorParityBindingRows.Int)
                {
                    return false;
                }

                var update = row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate;
                var preserving =
                    row.Levels[TranslatorParityBindingRows.ColumnShape]
                    == TranslatorParityBindingRows.PreservingConversion;

                return target switch
                {
                    RuntimeTarget.EfCoreSqlite => (
                        update || row.QueryPath == TranslatorParityBindingRows.ExecuteDelete
                    )
                        && row.Levels[TranslatorParityBindingRows.ColumnShape]
                            == TranslatorParityBindingRows.Plain
                        && Diverges(row, _ => int.MaxValue, null),
                    RuntimeTarget.EfCorePostgreSql => update || preserving,
                    RuntimeTarget.EfCoreSqlServer or RuntimeTarget.EfCoreOracle => preserving,
                    RuntimeTarget.EfCoreMySql => preserving
                        && (
                            row.QueryPath != TranslatorParityBindingRows.Query
                            || Diverges(row, _ => 0, null)
                        ),
                    _ => false,
                };
            },
            EfCoreCharValueFinding,
            KnownKind.Pending,
            TriggerSlice,
            CharValueReason
        );

    private const string CharValueReason =
        "EF Core は int の列と比べる捕捉した char の値を整数へ広げずに char のまま（文字列として）送る（bigint の列は C# が広げた long・decimal のパラメータで送り割れない）。SQLite は ExecuteUpdate の WHERE と ExecuteDelete の条件で変換を書かない int の列と TEXT で比べて黙って割れ、PostgreSQL は ExecuteUpdate で char を integer のパラメータに書けず、列の側に値を保つ変換を書いた形は 22P02、SQL Server・Oracle はその形で文字列を数へ変換できずに断り、MySQL はその形の書き込みの文が厳格のモードで断り、Query() は警告だけで 0 として比べ黙って割れうる（回避は値を (int)c か int の変数にして渡す：docs/code-generation.md の EF Core 版の Known limitation）";

    /// <summary>
    /// QuickER 版の列の側の値を変える明示の変換（Accepted）：翻訳器が変換を名指しした NotSupportedException で断る（値に依らず必ず例外なので
    /// 形だけで決める。値の側の変換は C# と同じに評価して束縛するので割れない）
    /// </summary>
    private static readonly Rule QuickErNarrowingColumnCast = new(
        row =>
            row.Levels[TranslatorParityBindingRows.ColumnShape]
            == TranslatorParityBindingRows.NarrowingCast,
        QuickErNarrowingColumnCastFinding,
        KnownKind.Accepted,
        null,
        QuickErNarrowingColumnCastReason
    );

    /// <summary>
    /// QuickER 版の列の側の ulong への変換（Accepted）：翻訳器が変換を名指しした NotSupportedException で断る（値に依らず必ず例外なので形だけで
    /// 決める。述語の表の QuickER 版の KnownRefusals から移した）
    /// </summary>
    private static readonly Rule QuickErUlongColumnCast = new(
        row =>
            row.Levels[TranslatorParityBindingRows.ColumnShape]
            == TranslatorParityBindingRows.ULongCast,
        QuickErUlongColumnCastFinding,
        KnownKind.Accepted,
        null,
        QuickErUlongColumnCastReason
    );

    /// <summary>QuickER 版の列の側の ulong への変換を翻訳器が断る割り切りの指摘の名前</summary>
    public const string QuickErUlongColumnCastFinding = "quicker-ulong-column-cast";

    private const string QuickErUlongColumnCastReason =
        "QuickER 版の翻訳器は、列の側に書いた ulong への変換を、変換を名指しした NotSupportedException で断る。C# が折り返した値は bigint に入らず、SQL で同じ値を作れないので翻訳しない（docs/code-generation.md「Letter case, whitespace, and date parts」の Known limitation：A cast to ulong is refused because its wrapped value does not fit a bigint）";

    /// <summary>
    /// EF Core 版の列の側の符号だけの変換（(uint)・(ulong)）：5 方言とも負の値を折り返さない変換に書く（例外にならない）ので、変換を外した形の
    /// 答えになり、C# が折り返して当てる行と黙って割れる（述語の表の EF Core の派生の Known から移した。縮める変換と同じ指摘・きっかけ待ち）
    /// </summary>
    /// <param name="reason">実装先の振る舞い（2026-11-27 の Docker の実行で読んだもの）</param>
    /// <param name="sqlite">
    /// EF Core の SQLite の形か：ulong どうしの等値・IN は ulong の値を long へ写して送るので負の値の行と当たって C# と一致し（大小の比較は
    /// 前の規則が断る）、decimal の値との等値・IN は前の規則の文字列の比較になるので、どちらもこの規則に当てない
    /// </param>
    private static Rule SignOnlyColumnCast(string reason, bool sqlite = false) =>
        new(
            row =>
                TranslatorParityBindingRows.IsSignCastShape(
                    row.Levels[TranslatorParityBindingRows.ColumnShape]
                )
                && !(
                    sqlite
                    && (
                        (
                            row.Levels[TranslatorParityBindingRows.ColumnShape]
                                == TranslatorParityBindingRows.ULongCast
                            && row.ComparedType == typeof(ulong)
                        ) || (row.ComparedType == typeof(decimal) && IsEquality(row))
                    )
                )
                && WithoutColumnCast(row),
            EfCoreNarrowingColumnCastFinding,
            KnownKind.Pending,
            TriggerSlice,
            reason
        );

    /// <summary>EF Core 版の SQLite の UInt64 の大小の比較の割り切りの指摘の名前</summary>
    public const string SqliteUlongOrderingFinding = "efcore-sqlite-ulong-ordering";

    private const string SqliteUlongOrderingReason =
        "EF Core の SQLite プロバイダは UInt64 の大小の比較を翻訳できず、InvalidOperationException で断る（プロバイダの文書の Query limitations：SQLite が持たない型の比較と並び替えはクライアントの評価が要る）";

    /// <summary>EF Core 版の SQLite の符号だけの変換の列と decimal の値の等値・IN の指摘の名前（きっかけ待ち）</summary>
    public const string SqliteSignCastDecimalEqualityFinding =
        "efcore-sqlite-sign-cast-decimal-equality";

    private const string SqliteSignCastDecimalEqualityReason =
        "EF Core の SQLite プロバイダは、列の側に (uint)・(ulong) を書いた整数の列と decimal の値の等値・IN を CAST(CAST(列 AS INTEGER) AS TEXT) IN (@Value1, @Value2) と書き、decimal のパラメータを TEXT（Decimal/Text の 1・2、-1・0 ほか）で送るので文字列で比べ、整数の値の decimal がどの行とも等しくならない（2026-11-27 の Docker の実行で Query() と ExecuteDelete の 6 行。Query() の条件の整数と decimal の等値の書き換えは変換のある列を書き換えない）";

    private const string SignOnlySqliteReason =
        "EF Core の SQLite プロバイダは列の側の (uint) などを値を変えない変換に書き、負の値を折り返さないので、C# が折り返して当てる行と黙って答えが割れる（例外にならない）";

    private const string SignOnlySqlServerReason =
        "EF Core の SQL Server プロバイダは列の側の (uint)・(ulong) を負の値を折り返さない変換に書くので、C# が折り返して当てる行と黙って答えが割れる（例外にならない。縮める変換と違い、変換先の型の範囲に入らない行が無い）";

    private const string SignOnlyPostgreSqlReason =
        "EF Core の PostgreSQL プロバイダは列の側の (uint)・(ulong) を負の値を折り返さない変換に書くので、C# が折り返して当てる行と黙って答えが割れる（例外にならない。縮める変換と違い、変換先の型の範囲に入らない行が無い）";

    private const string SignOnlyMySqlReason =
        "EF Core の MySQL プロバイダは列の側の (uint)・(ulong) を値を変えない形で比べ、負の値を折り返さない（MySQL の CAST(… AS unsigned) の 64 ビットの折り返しも起きない）ので、C# が折り返して当てる行と黙って答えが割れる（例外にならない）";

    private const string SignOnlyOracleReason =
        "EF Core の Oracle プロバイダは列の側の (uint)・(ulong) を負の値を折り返さない変換に書くので、C# が折り返して当てる行と黙って答えが割れる（例外にならない。縮める変換と違い、変換先の型の範囲に入らない行が無い）";

    /// <summary>
    /// EF Core 版の列の側の値を変える明示の変換：DB の CAST の意味で答えを作る。シードの行を変換先の DB の型に入れられない方言・列の型は
    /// 例外になり（形だけで決める）、入れられる行は DB が CAST した値（多くは元の値のまま＝折り返さない）で比べた答えになる（値を手元で評価する
    /// 値の側の変換は割れない）。直す区切りは決まっていない（docs/code-generation.md の EF Core 版の Known limitation に、列は変換せずに値の側を
    /// 広げる回避を書いた）
    /// </summary>
    /// <param name="refuses">変換先の DB の型に入らない行（int の列の -1・bigint の列の long の上限と下限）で例外になる列の型か</param>
    /// <param name="diverges">例外にならない列の型で、DB の CAST の答えが C# の答えと違うか</param>
    /// <param name="reason">実装先の振る舞い（Docker の実行で読んだもの）</param>
    private static Rule NarrowingColumnCast(
        Func<TranslatorParityBinding, bool> refuses,
        Func<TranslatorParityBinding, bool> diverges,
        string reason
    ) =>
        new(
            row =>
                row.Levels[TranslatorParityBindingRows.ColumnShape]
                    == TranslatorParityBindingRows.NarrowingCast
                && (refuses(row) || diverges(row)),
            EfCoreNarrowingColumnCastFinding,
            KnownKind.Pending,
            TriggerSlice,
            reason
        );

    /// <summary>
    /// 列の側の変換を外した形の答え（DB の CAST がシードの値を変えない形）が C# の答えと違うか（シードの decimal の列の値はどれも整数で、
    /// 小数部の切り捨て〔SQL Server・SQLite〕と丸め〔PostgreSQL・Oracle〕のどちらでも値が変わらない）
    /// </summary>
    private static bool WithoutColumnCast(TranslatorParityBinding row) =>
        DivergesFrom(row, row.PredicateWithout(NarrowingDrop.Column));

    /// <summary>
    /// 列の値を DB の CAST の値 <paramref name="image"/> に置き換えたシードへ、列の側の変換を外した述語を当てた答えが C# の答えと違うか
    /// （int の列＝small_child の big_parent_id の行だけ）
    /// </summary>
    private static bool DivergesOnColumnImage(TranslatorParityBinding row, Func<int, int> image)
    {
        if (row.Entity != BindingEntity.SmallChild)
        {
            throw new InvalidOperationException(
                $"{row.ColumnType} の列は列の値の置き換えを持たない（int の列だけ）"
            );
        }

        var imaged = TranslatorParityRuntimeTestsBase
            .CrossTypeChildren()
            .Select(seed => new SmallChildEntity
            {
                Id = seed.Id,
                BigParentId = seed.BigParentId is { } value ? image(value) : null,
            })
            .ToList<object>();
        var compiled = row.PredicateWithout(NarrowingDrop.Column).Compile();
        var hits = imaged
            .Where(seed => (bool)compiled.DynamicInvoke(seed)!)
            .Select(seed => (long)((SmallChildEntity)seed).Id)
            .ToList();
        var answer =
            row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate
                ? $"rows={hits.Count}"
                : BindingRunOutcome.Keys(hits);

        return AnswerOf(row, row.Predicate) != answer;
    }

    /// <summary>
    /// EF Core の Oracle プロバイダが int の列の (byte) に書く <c>TO_NUMBER(列, 'xx')</c> の値：0 以上の値は 10 進の綴りを 16 進として読み
    /// （シードの 1・2 は変わらない）、負の値の行は折り返した値（255）でなく、比べるどの値（1・2・255・256）とも等しくなく 2 より大きい値に
    /// なる（2026-11-27 の Docker の実行の観測。値そのものは確かめていないので、byte の範囲の外の int の上限で表す）
    /// </summary>
    private static int OracleHexImage(int value) =>
        value >= 0
            ? int.Parse(
                value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture
            )
            : int.MaxValue;

    private const string NarrowingSqliteReason =
        "EF Core の SQLite プロバイダは列の側の (short) などを CAST(列 AS INTEGER) と書き、値を縮めないので、C# が折り返して当てる行・当てない行と黙って答えが割れる（例外にならない。符号だけの変換の (uint) なども同じく負の値を折り返さない＝符号だけの変換の水準の行・規則 SignOnlyColumnCast）";

    private const string NarrowingMySqlReason =
        "EF Core の MySQL プロバイダは列の側の (short) などを SQL に書かず変換の無い列と比べるので、C# が折り返して当てる行・当てない行と黙って答えが割れる（例外にならない）"
        + SignOnlyCastSilent;

    /// <summary>
    /// 符号だけの変換の振る舞い（2026-11-27 の Docker の実行で 5 方言とも確かめた。SQLite の ulong の大小の比較だけは翻訳が断る）
    /// </summary>
    private const string SignOnlyCastSilent =
        "。符号だけの変換の (uint)・(ulong) も負の値を折り返さず、例外にならずに黙って割れる（符号だけの変換の水準の行・規則 SignOnlyColumnCast）";

    private const string NarrowingSqlServerReason =
        "EF Core の SQL Server プロバイダは列の側の (byte)・(short) を CAST(列 AS tinyint)・CAST(列 AS smallint) と書き、変換先の型に入らない行（int の列の -1 の行・bigint の列の long の上限と下限の行）があると Arithmetic overflow の例外で失敗する（2026-11-27 の Docker の実行：Arithmetic overflow error for data type tinyint, value = -1。例外にならない実行計画〔捕まえた値との等値で観測〕でも、C# が折り返して当てる行を当てない）。decimal の列の (int) は小数部を切り捨て、C# と同じ値になる"
        + SignOnlyCastSilent;

    private const string NarrowingPostgreSqlReason =
        "EF Core の PostgreSQL プロバイダは列の側の (byte)・(short) を ::smallint と書く。smallint に入らない行（bigint の列の long の上限・下限）がある表では 22003: smallint out of range の例外で必ず失敗し、int の列の (byte) は -1 の行を 255 へ折り返さず -1 のまま比べて黙って割れる（2026-11-27 の Docker の実行：DELETE … WHERE s.big_parent_id::smallint < 1 が -1 の行を消す）"
        + SignOnlyCastSilent;

    private const string NarrowingOracleReason =
        "EF Core の Oracle プロバイダは列の側の (short) を NUMBER(5) への CAST と書き、5 桁に入らない行（bigint の列の long の上限・下限）がある表では ORA-01438 の例外で必ず失敗する。int の列の (byte) は TO_NUMBER(列, 'xx') と書いて列の 10 進の綴りを 16 進として読み、-1 の行は 255 にならず黙って割れる（2026-11-27 の Docker の実行：= 255・IN (255, 256) が -1 の行に当たらず、< 1 にも <= 1 にも当たらない）"
        + SignOnlyCastSilent;

    /// <summary>等値・IN の比べ方か</summary>
    private static bool IsEquality(TranslatorParityBinding row) =>
        row.Levels[TranslatorParityBindingRows.Comparison]
            is "equal"
                or "not-equal"
                or "in"
                or "not-in";

    /// <summary>大小の比べ方か</summary>
    private static bool IsOrdering(TranslatorParityBinding row) =>
        row.Levels[TranslatorParityBindingRows.Comparison]
            is "less"
                or "greater-or-equal"
                or TranslatorParityBindingRows.LessOrEqual
                or TranslatorParityBindingRows.Greater;

    /// <summary>
    /// date の列（SQL Server は日付部品も）と比べる定数・メソッド呼び出しの値で、値の時刻を落とすと C# の答えが変わる行（EF Core が date の
    /// リテラルで書く）
    /// </summary>
    private static Rule DateConstant(bool datePartIsDate) =>
        new(
            row =>
                (
                    row.ColumnType == TranslatorParityBindingRows.Date
                    || (
                        datePartIsDate
                        && row.Levels[TranslatorParityBindingRows.ColumnShape]
                            == TranslatorParityBindingRows.DatePart
                    )
                )
                && IsLiteralOnEfCore(row)
                && Diverges(row, DropTime, DropTime),
            DateConstantFinding,
            KnownKind.Accepted,
            null,
            DateConstantReason
        );

    private static readonly Lazy<
        IReadOnlyDictionary<RuntimeTarget, IReadOnlyList<KnownEntry>>
    > Entries = new(() =>
        TranslatorParityBindingRows
            .Targets.Select(Enum.Parse<RuntimeTarget>)
            .ToDictionary(target => target, target => Assign(target, RulesFor(target)))
    );

    /// <summary>実装先が流す行を、規則の並びの最初に当てはまった規則の項目にする</summary>
    private static IReadOnlyList<KnownEntry> Assign(RuntimeTarget target, IReadOnlyList<Rule> rules)
    {
        var entries = new List<KnownEntry>();

        foreach (
            var row in TranslatorParityBindingRows.Rows.Where(row =>
                TranslatorParityBindingRows.RunsOn(target, row.Input.Levels)
            )
        )
        {
            var rule = rules.FirstOrDefault(rule => rule.Applies(row.Input));

            if (rule is not null)
            {
                entries.Add(
                    new KnownEntry(
                        rule.Finding,
                        rule.Kind,
                        rule.FixedIn,
                        rule.Reason,
                        ScopeOf(target),
                        KnownMatch.ExactSignature,
                        Signature: row.Id
                    )
                );
            }
        }

        return entries;
    }

    // ===== 行の形と値の変え方 =====

    /// <summary>
    /// EF Core が値を SQL のリテラルとして書く値の形（定数と、行に依らないメソッド呼び出し＝EF Core は呼び出しを評価した値を定数として書く）
    /// </summary>
    private static bool IsLiteralOnEfCore(TranslatorParityBinding row) =>
        row.Levels[TranslatorParityBindingRows.ValueShape]
            is TranslatorParityBindingRows.Constant
                or TranslatorParityBindingRows.MethodCall;

    /// <summary>PostgreSQL で timestamp になる列（datetime・datetime2・smalldatetime を写した列）</summary>
    private static bool IsTimestampColumn(TranslatorParityBinding row) =>
        row.ColumnType
            is TranslatorParityBindingRows.LegacyDateTime
                or TranslatorParityBindingRows.DateTime2
                or TranslatorParityBindingRows.SmallDateTime;

    /// <summary>整数の列（int・bigint）と decimal の値の等値・IN（EF Core の SQLite プロバイダが文字列で比べる形）</summary>
    private static bool IsIntegerDecimalEquality(TranslatorParityBinding row) =>
        row.ColumnType is TranslatorParityBindingRows.Int or TranslatorParityBindingRows.BigInt
        && row.ComparedType == typeof(decimal)
        && IsEquality(row);

    /// <summary>SQL Server の datetime・smalldatetime が値を受け取った形</summary>
    private static Func<object, object> SqlServerRounding(TranslatorParityBinding row) =>
        row.ColumnType == TranslatorParityBindingRows.LegacyDateTime
            ? value => SqlServerDateTimes.ToDateTime((DateTime)value)
            : value => SqlServerDateTimes.ToSmallDateTime((DateTime)value);

    /// <summary>date のリテラルが落とす時刻</summary>
    private static object DropTime(object value) => ((DateTime)value).Date;

    /// <summary>
    /// EF Core の SQLite プロバイダが整数と decimal の等値を文字列で比べた形：整数の値の decimal はどの整数の行とも等しくない
    /// （小数の値は元からどの行とも等しくない）ので、値を整数でない値へずらす
    /// </summary>
    private static object NeverEqual(object value)
    {
        var number = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);

        return number == decimal.Truncate(number) ? number + 0.5m : number;
    }

    /// <summary>
    /// 比べる値を DB が受け取った形へ変えると、同じシードへ当てた C# の答えが変わるか（変えると送れない値は例外＝変わる）
    /// </summary>
    private static bool Diverges(
        TranslatorParityBinding row,
        Func<object, object>? single,
        Func<object, object>? element
    )
    {
        try
        {
            return DivergesFrom(row, row.PredicateWith(single, element));
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    /// <summary>行の述語と、変えた述語 <paramref name="changed"/> の答え（同じシードへ当てた C# の答え）が違うか</summary>
    private static bool DivergesFrom(TranslatorParityBinding row, LambdaExpression changed) =>
        AnswerOf(row, row.Predicate) != AnswerOf(row, changed);

    /// <summary>行の経路の答えを、シード（実装先から読み戻す前の値）へ述語を当てて作る</summary>
    private static string AnswerOf(TranslatorParityBinding row, LambdaExpression predicate)
    {
        var (seeds, key) = SeedsOf(row.Entity);
        var compiled = predicate.Compile();
        var hits = seeds.Where(seed => (bool)compiled.DynamicInvoke(seed)!).Select(key).ToList();

        return row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate
            ? $"rows={hits.Count}"
            : BindingRunOutcome.Keys(hits);
    }

    private static (IReadOnlyList<object> Seeds, Func<object, long> Key) SeedsOf(
        BindingEntity entity
    ) =>
        entity switch
        {
            BindingEntity.Probe => (
                [.. TranslatorParityRuntimeTestsBase.SeedProbes()],
                seed => ((ProbeEntity)seed).ProbeId
            ),
            BindingEntity.SmallChild => (
                [.. TranslatorParityRuntimeTestsBase.CrossTypeChildren()],
                seed => ((SmallChildEntity)seed).Id
            ),
            _ => (
                [.. TranslatorParityRuntimeTestsBase.WideBigParents()],
                seed => ((BigParentEntity)seed).Id
            ),
        };
}

/// <summary>SQL Server の datetime・smalldatetime が値を受け取った形（docs の datetime・smalldatetime の段落の写し）</summary>
internal static class SqlServerDateTimes
{
    private static readonly DateTime DateTimeMin = new(1753, 1, 1);
    private static readonly DateTime DateTimeMax = new(9999, 12, 31, 23, 59, 59, 997);
    private static readonly DateTime SmallDateTimeMin = new(1900, 1, 1);
    private static readonly DateTime SmallDateTimeMax = new(2079, 6, 6, 23, 59, 0);

    /// <summary>
    /// datetime：1/300 秒の刻みへ丸め、読み戻す値（.000・.003・.007）で返す。丸めた値が範囲の外なら <see cref="OverflowException"/>
    /// </summary>
    public static DateTime ToDateTime(DateTime value)
    {
        var second = value.Ticks - value.Ticks % TimeSpan.TicksPerSecond;
        var units = (long)
            Math.Round(
                value.Ticks % TimeSpan.TicksPerSecond * 300m / TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero
            );
        var milliseconds = (long)Math.Round(units * 1000m / 300m, MidpointRounding.AwayFromZero);
        var rounded = second + milliseconds * TimeSpan.TicksPerMillisecond;

        return rounded < DateTimeMin.Ticks || rounded > DateTimeMax.Ticks
            ? throw new OverflowException($"{value:o} は datetime の範囲の外")
            : new DateTime(rounded, value.Kind);
    }

    /// <summary>
    /// smalldatetime：29.998 秒以下は切り下げ、29.999 秒以上は切り上げて分へ丸める。丸めた値が範囲の外なら <see cref="OverflowException"/>
    /// </summary>
    public static DateTime ToSmallDateTime(DateTime value)
    {
        var minute = value.Ticks - value.Ticks % TimeSpan.TicksPerMinute;
        var rest = value.Ticks % TimeSpan.TicksPerMinute;
        var rounded =
            rest >= 29_999 * TimeSpan.TicksPerMillisecond
                ? minute + TimeSpan.TicksPerMinute
                : minute;

        return rounded < SmallDateTimeMin.Ticks || rounded > SmallDateTimeMax.Ticks
            ? throw new OverflowException($"{value:o} は smalldatetime の範囲の外")
            : new DateTime(rounded, value.Kind);
    }
}
