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
/// きっかけ待ち＝<see cref="TriggerSlice"/>。EF Core 版の列の側の値を変える明示の変換は、SQLite・MySQL が値を変えない変換に書いて黙って割れ、
/// PostgreSQL・Oracle が範囲の外の行のある表で必ず断り、SQL Server もほとんどの形で断る。符号だけの変換は 5 方言とも折り返さずに黙って割れ、
/// SQLite の ulong の大小の比較だけは翻訳が断る〔述語の表の行〕。docs の Known limitation に回避を書いた）。</item>
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
            RuntimeTarget.AdoSqlite => [QuickErNarrowingColumnCast],
            RuntimeTarget.AdoSqlServer => [QuickErNarrowingColumnCast],
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
                NarrowingColumnCast(alwaysThrows: false, NarrowingSqliteReason),
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
                NarrowingColumnCast(alwaysThrows: true, NarrowingSqlServerReason),
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
                NarrowingColumnCast(alwaysThrows: true, NarrowingPostgreSqlReason),
            ],
            RuntimeTarget.EfCoreMySql =>
            [
                CharValueSentAsText(RuntimeTarget.EfCoreMySql),
                NarrowingColumnCast(alwaysThrows: false, NarrowingMySqlReason),
            ],
            RuntimeTarget.EfCoreOracle =>
            [
                CharValueSentAsText(RuntimeTarget.EfCoreOracle),
                NarrowingColumnCast(alwaysThrows: true, NarrowingOracleReason),
            ],
            _ => [],
        };

    /// <summary>
    /// EF Core 版の整数の列と比べる捕捉した char の値（Pending・きっかけ待ち）：EF Core は char を整数へ広げる変換を外し、値を char のまま
    /// （文字列として）送る。どの行が割れるかは方言の扱いで決める（Docker の実行で読んだもの）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>SQLite：ExecuteUpdate の WHERE だけが値を TEXT で送り、SQLite は整数の列をどの TEXT よりも小さいとみなす（例外にならず黙って割れる。
    /// 値を整数の上限へ置き換えた C# の答えになる行）。Query()・ExecuteDelete の条件は C# と一致する。</item>
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

                var update = row.QueryPath == TranslatorParityBindingRows.ExecuteUpdate;
                var preserving =
                    row.Levels[TranslatorParityBindingRows.ColumnShape]
                    == TranslatorParityBindingRows.PreservingConversion;

                return target switch
                {
                    RuntimeTarget.EfCoreSqlite => update && Diverges(row, _ => int.MaxValue, null),
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
        "EF Core は整数の列と比べる捕捉した char の値を整数へ広げずに char のまま（文字列として）送る。SQLite は ExecuteUpdate の WHERE で TEXT と比べて黙って割れ、PostgreSQL は ExecuteUpdate で char を integer のパラメータに書けず、列の側に値を保つ変換を書いた形は 22P02、SQL Server・Oracle はその形で文字列を数へ変換できずに断り、MySQL はその形の書き込みの文が厳格のモードで断り、Query() は警告だけで 0 として比べ黙って割れうる（回避は値を (int)c か int の変数にして渡す：docs/code-generation.md の EF Core 版の Known limitation）";

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
    /// EF Core 版の列の側の値を変える明示の変換：SQLite・MySQL は値を変えない変換に書いて変換を外した形の答えになり、SQL Server・PostgreSQL・
    /// Oracle は変換先の型に入らない行（bigint の列の short への変換＝シードの long の上限ほか）で断る（値を手元で評価する値の側の変換は割れない）。
    /// 直す区切りは決まっていない（docs/code-generation.md の EF Core 版の Known limitation に、列は変換せずに値の側を広げる回避を書いた）
    /// </summary>
    /// <param name="alwaysThrows">変換先の型に入らない行がある表で例外になる方言か（形だけで決める）</param>
    /// <param name="reason">実装先の振る舞い（Docker の実行で読んだもの）</param>
    private static Rule NarrowingColumnCast(bool alwaysThrows, string reason) =>
        new(
            row =>
                row.Levels[TranslatorParityBindingRows.ColumnShape]
                    == TranslatorParityBindingRows.NarrowingCast
                && (
                    alwaysThrows
                        ? row.ColumnType == TranslatorParityBindingRows.BigInt
                        : DivergesFrom(row, row.PredicateWithout(NarrowingDrop.Column))
                ),
            EfCoreNarrowingColumnCastFinding,
            KnownKind.Pending,
            TriggerSlice,
            reason
        );

    private const string NarrowingSqliteReason =
        "EF Core の SQLite プロバイダは列の側の (short) などを CAST(列 AS INTEGER) と書き、値を縮めないので、C# が折り返して当てる行・当てない行と黙って答えが割れる（例外にならない。符号だけの変換の (uint) なども同じく負の値を折り返さない＝述語の表の行 int-column-sign-cast-*・in-uint-*-sign-cast-column）";

    private const string NarrowingMySqlReason =
        "EF Core の MySQL プロバイダは列の側の (short) などを SQL に書かず変換の無い列と比べるので、C# が折り返して当てる行・当てない行と黙って答えが割れる（例外にならない）"
        + SignOnlyCastSilent;

    /// <summary>
    /// 符号だけの変換の振る舞い（2026-11-27 の Docker の実行で 5 方言とも確かめた。SQLite の ulong の大小の比較だけは翻訳が断る）
    /// </summary>
    private const string SignOnlyCastSilent =
        "。符号だけの変換の (uint)・(ulong) も負の値を折り返さず、例外にならずに黙って割れる（述語の表の行 int-column-sign-cast-*・in-uint-*-sign-cast-column・*-ulong-cast-greater）";

    private const string NarrowingSqlServerReason =
        "EF Core の SQL Server プロバイダは列の側の (short) などを CAST(列 AS smallint) と書き、smallint に入らない行（bigint の列の long の上限・下限ほか）があると Arithmetic overflow の例外で失敗する。例外にならない実行計画（捕まえた値との等値で観測）でも、C# が折り返して当てる行を当てない"
        + SignOnlyCastSilent;

    private const string NarrowingPostgreSqlReason =
        "EF Core の PostgreSQL プロバイダは列の側の (short) などを smallint への CAST と書き、smallint に入らない行（bigint の列の long の上限・下限ほか）がある表では 22003: smallint out of range の例外で必ず失敗する"
        + SignOnlyCastSilent;

    private const string NarrowingOracleReason =
        "EF Core の Oracle プロバイダは列の側の (short) などを NUMBER(5) への CAST と書き、5 桁に入らない行（bigint の列の long の上限・下限ほか）がある表では ORA-01438 の例外で必ず失敗する"
        + SignOnlyCastSilent;

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
        && row.Levels[TranslatorParityBindingRows.Comparison]
            is "equal"
                or "not-equal"
                or "in"
                or "not-in";

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
