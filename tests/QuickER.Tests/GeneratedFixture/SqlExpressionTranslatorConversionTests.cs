using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.TestSupport.Engines;
using QuickER.Tests.TestSupport.ValuePools;
using Xunit;
using Evaluator = QuickER.Tests.GeneratedTranslatorParityFixture.QueryValueEvaluator;
using SqliteMetadata = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.Sqlite.EntitySaveMetadata;
using SqliteParam = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.Sqlite.SqlQueryParameter;
using SqliteTranslator = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.Sqlite.SqlExpressionTranslator;
using SqlServerMetadata = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.SqlServer.EntitySaveMetadata;
using SqlServerParam = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.SqlServer.SqlQueryParameter;
using SqlServerTranslator = QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.SqlServer.SqlExpressionTranslator;

namespace QuickER.Tests.GeneratedFixture;

/// <summary>
/// 翻訳器の変換の表（単体の表の台帳の <c>table:TranslatorConversions</c>）：生成ランタイムの <c>SqlExpressionTranslator</c> が、比べる 2 つの側に
/// 書いた変換を C# と同じに扱うこと（値の側は変換を当てて束縛し、列の側の値を変えうる数の変換は名指しで断る）と、日付部品の IN と、
/// 列名の無い値の束縛（<c>EntitySaveMetadata.AddQueryParameter</c>）を、両方言の形で固定する。
/// </summary>
/// <remarks>
/// <para>
/// 行集合としての答えは値の束縛の生成の表（<c>GeneratedRuntime/TranslatorParityBindings</c>）が実装先ごとに C# と突き合わせる。この表は、
/// その表が実 SQL Server でしか見られない SQL Server の翻訳器と束縛を、コンテナなしで形（SQL・束縛した値と型・断る例外・式木のコンパイルの
/// 回数・SqlParameter の型）で名指しし、生成の表の水準に無い変換の種類（小数どうし・符号だけの変換・列挙・char・Nullable を外す変換・
/// 丸めうる暗黙の変換）を持つ。行の説明は、その行が守る製品の腕。
/// </para>
/// <para>
/// 翻訳器と束縛はチェックイン済みの <c>TranslatorParityFixture</c>（sqlserver と sqlite のマルチターゲット）の 2 つの方言の実装を使う。
/// 札の対象の表（<c>RowBaselineTables</c>）に載り、減るだけの写しに無い新しい行は札を持つ。
/// </para>
/// </remarks>
public sealed class SqlExpressionTranslatorConversionTests
{
    /// <summary>列判定用のプローブ。プロパティ名がそのまま列名として使われる（[Column] 属性なし）。</summary>
    internal sealed class Shapes
    {
        public int Id { get; set; }
        public int? Opt { get; set; }
        public long Big { get; set; }
        public decimal Price { get; set; }
        public double Ratio { get; set; }
        public float Single { get; set; }
        public DateTime? At { get; set; }
        public Kind Level { get; set; }
        public short Small { get; set; }
        public sbyte Tiny { get; set; }
        public uint Count { get; set; }

        /// <summary>
        /// 封印されていない参照型の列（生成のエンティティの列は値型か封印された型だけで、手書きの <c>[Column]</c> のプロパティだけがこの形を持てる）
        /// </summary>
        public object? Loose { get; set; }
    }

    /// <summary>列挙から数への変換の行の列挙</summary>
    internal enum Kind
    {
        A = 1,
        B = 2,
    }

    /// <summary>列名の無い値と列名のある値の束縛の期待（SQL Server と SQLite の束縛の型）</summary>
    /// <param name="ColumnName">比べる列の名前（列名の無い値は null）</param>
    /// <param name="Value">束縛する値</param>
    /// <param name="SqlServerType">SQL Server の SqlParameter の型</param>
    /// <param name="SqliteType">SQLite の SqliteParameter の型</param>
    /// <param name="SqlServerValue">SQL Server の SqlParameter の値（型も比べる。null なら <paramref name="Value"/> のまま）</param>
    internal sealed record BindingCase(
        string? ColumnName,
        object Value,
        SqlDbType SqlServerType,
        SqliteType SqliteType,
        object? SqlServerValue = null
    );

    /// <summary>1 行の入力（翻訳の行は述語と SQL か断る例外・束縛した値・コンパイルの回数、束縛の行は束縛の期待）</summary>
    /// <param name="Predicate">述語（束縛の行は null）</param>
    /// <param name="SqlServer">SQL Server の条件（断る行と束縛の行は null）</param>
    /// <param name="Sqlite">SQLite の条件（断る行と束縛の行は null）</param>
    /// <param name="Refusal">断る例外の型（翻訳する行は null）</param>
    /// <param name="RefusalNames">断る例外の文が名指す断片</param>
    /// <param name="Bound">束縛した値（型も比べる。値を持たない行は空）</param>
    /// <param name="Compiles">翻訳が式木をコンパイルした回数（コンパイルの回数を見ない行は null）</param>
    /// <param name="Binding">束縛の期待（翻訳の行は null）</param>
    /// <param name="SqliteBound">SQLite だけ束縛する値が違う行の、SQLite の束縛した値（null なら <paramref name="Bound"/>）</param>
    internal sealed record ConversionCase(
        Expression<Func<Shapes, bool>>? Predicate,
        string? SqlServer,
        string? Sqlite,
        Type? Refusal,
        string[] RefusalNames,
        object[] Bound,
        int? Compiles,
        BindingCase? Binding,
        object[]? SqliteBound = null
    );

    private static ScenarioRow<ConversionCase> Translated(
        string id,
        string arm,
        Expression<Func<Shapes, bool>> predicate,
        string sqlServer,
        string sqlite,
        object[] bound,
        int? compiles = null,
        object[]? sqliteBound = null
    ) =>
        new(
            id,
            null,
            arm,
            new(predicate, sqlServer, sqlite, null, [], bound, compiles, null, sqliteBound)
        );

    private static ScenarioRow<ConversionCase> Refused(
        string id,
        string arm,
        Expression<Func<Shapes, bool>> predicate,
        Type refusal,
        params string[] names
    ) => new(id, null, arm, new(predicate, null, null, refusal, names, [], null, null));

    private static ScenarioRow<ConversionCase> Bound(
        string id,
        string arm,
        string? columnName,
        object value,
        SqlDbType sqlServerType,
        SqliteType sqliteType,
        object? sqlServerValue = null
    ) =>
        new(
            id,
            null,
            arm,
            new(
                null,
                null,
                null,
                null,
                [],
                [],
                null,
                new(columnName, value, sqlServerType, sqliteType, sqlServerValue)
            )
        );

    /// <summary>区切り a の XT4 の符号だけの変換で足した行の札</summary>
    private static ScenarioRow<ConversionCase> Xt4(ScenarioRow<ConversionCase> row) =>
        row with
        {
            Tag = RowTag.Ledger("xt4"),
        };

    /// <summary>3 回目の効果測定の XT2（Equals(object) の型の違う値を常に偽へ畳む）で足した行の札</summary>
    private static ScenarioRow<ConversionCase> Xt2(
        ScenarioRow<ConversionCase> row,
        string reason
    ) =>
        row with
        {
            Tag = RowTag.Reason(
                RowTagKind.ReviewFinding,
                $"XT2（review-measure3-2026-11-26 v10）：{reason}"
            ),
        };

    /// <summary>符号だけの変換の折り返しの SQL（SQL Server と SQLite で同じ形）</summary>
    private static string Wrap(string column, long modulus) =>
        $"(CASE WHEN {column} < 0 THEN CAST({column} AS bigint) + {modulus} ELSE {column} END)";

    /// <summary>行の表</summary>
    internal static ScenarioTable<ConversionCase> Table { get; } =
        new(EngineIds.GeneratedRuntime, Rows());

    /// <summary>札の対象の表の Id と札（減るだけの写しと照合する＝<c>RowBaselineTables</c>）</summary>
    internal static IReadOnlyList<BaselineEntry> BaselineEntries =>
        [.. Table.Select(row => new BaselineEntry(row.Id, row.Tag))];

    /// <summary>行の Id</summary>
    public static TheoryData<string> Ids() => Table.Ids();

    /// <summary>
    /// 値のプールの観測：翻訳の行の述語の比べる値（行に依らない側を評価する）と、束縛の行の値。列は CLR の型だけのプローブなので、列の相は
    /// 値を変える明示の変換の変換先だけを持つ（値は変えない）
    /// </summary>
    internal static IReadOnlyList<TestSupport.ValuePools.ValueObservation> ObserveValues() =>
        [
            .. Table.SelectMany(row =>
                row.Input.Predicate is { } predicate
                    ? TestSupport.ValuePools.ValueExpressionObserver.Observe(
                        predicate,
                        _ => null,
                        $"行 {row.Id}"
                    )
                : row.Input.Binding is { } binding
                    ?
                    [
                        new TestSupport.ValuePools.ValueObservation(
                            binding.Value,
                            null,
                            $"行 {row.Id}"
                        ),
                    ]
                : []
            ),
        ];

    private static IReadOnlyList<ScenarioRow<ConversionCase>> Rows()
    {
        var ratio = 10.75;
        var captured = 9;
        var bigInt = PowerOfTwoEdges.TwoPow24Plus1;
        var bigLong = PowerOfTwoEdges.TwoPow53Plus1;
        var letter = 'a';
        var kind = Kind.B;
        int? none = null;
        var shorts = new List<short> { 1, 2 };
        var days = new List<DateTime> { new(2024, 1, 15), new(2024, 1, 16) };
        var dayArray = days.ToArray();
        var legacyReadBack = new DateTime(2024, 1, 15, 23, 59, 59, 997);
        var uints = new List<uint> { 10u, 4294967295u };

        return
        [
            // ---- 列の側：列の値を変えうる数の変換は、変換を名指しして断る ----
            Refused(
                "column-narrowing-integral",
                "列の側の確かめ ChangesColumnValue の整数どうしの腕：大きさを縮める整数の変換を断る",
                x => (short)x.Big == 1,
                typeof(NotSupportedException),
                "Int64",
                "Int16"
            ),
            Refused(
                "column-decimal-to-integral",
                "列の側の確かめ ChangesColumnValue の整数への腕：decimal から整数への変換を断る",
                x => (int)x.Price == 10,
                typeof(NotSupportedException),
                "Decimal",
                "Int32"
            ),
            Refused(
                "column-double-to-same-size-integral",
                "列の側の確かめ ChangesColumnValue の整数への腕の小数の項：大きさが同じでも double から long への変換を断る",
                x => (long)x.Ratio == 1L,
                typeof(NotSupportedException),
                "Double",
                "Int64"
            ),
            Refused(
                "column-double-to-float",
                "列の側の確かめ ChangesColumnValue の小数への腕：小数どうしの縮める変換を断る",
                x => (float)x.Ratio == 1.5f,
                typeof(NotSupportedException),
                "Double",
                "Single"
            ),
            Refused(
                "column-double-to-decimal",
                "列の側の確かめ ChangesColumnValue の小数への腕：double から decimal への変換を断る",
                x => (decimal)x.Ratio == 1.5m,
                typeof(NotSupportedException),
                "Double",
                "Decimal"
            ),
            Refused(
                "column-narrowing-in-item",
                "IN の要素の解決 TryGetIn（OperandSql）：要素の側の縮める変換も断る",
                x => shorts.Contains((short)x.Big),
                typeof(NotSupportedException),
                "Int64",
                "Int16"
            ),
            Refused(
                "column-narrowing-around-date-part",
                "日付部品の解決 TryGetDatePart の確かめの呼び出し：日付部品を包む縮める変換を断る",
                x => x.At != null && (short)x.At.Value.Year == 2024,
                typeof(NotSupportedException),
                "Int32",
                "Int16"
            ),
            Refused(
                "column-narrowing-nullable",
                "列の側の確かめ NumericCode の Nullable の見通し：Nullable の列の縮める変換を断る",
                x => (short?)x.Opt == 1,
                typeof(NotSupportedException),
                "Int32",
                "Int16"
            ),
            Refused(
                "column-narrowing-enum",
                "列の側の確かめ NumericCode の列挙の扱い：列挙の基の型から縮める変換を断る",
                x => (byte)x.Level == 2,
                typeof(NotSupportedException),
                "Kind",
                "Byte"
            ),
            // ---- 列の側：列の値を保つ変換は外して列のまま比べる ----
            Translated(
                "column-implicit-widening",
                "列の側の確かめ：C# の暗黙の数値の変換（int から long）は外して列のまま比べる",
                x => (long)x.Id == 3_000_000_000L,
                "[Id] = @p0",
                "\"Id\" = @p0",
                [3_000_000_000L]
            ),
            Translated(
                "column-sign-only-cast",
                "列の側の折り返し SignWrapModulus の uint の腕：int から uint は C# の折り返し（負の値に 2^32 を足す）の CASE で書く",
                x => (uint)x.Id == 5u,
                $"{Wrap("[Id]", 4294967296)} = @p0",
                $"{Wrap("\"Id\"", 4294967296)} = @p0",
                [5u]
            ),
            Xt4(
                Translated(
                    "column-sign-cast-short-to-ushort",
                    "列の側の折り返し SignWrapModulus の ushort の腕：short から ushort は 2^16 を足す",
                    x => (ushort)x.Small == (ushort)65535,
                    $"{Wrap("[Small]", 65536)} = @p0",
                    $"{Wrap("\"Small\"", 65536)} = @p0",
                    [65535]
                )
            ),
            Xt4(
                Translated(
                    "column-sign-cast-sbyte-to-byte",
                    "列の側の折り返し SignWrapModulus の byte の腕：sbyte から byte は 2^8 を足す",
                    x => (byte)x.Tiny == (byte)255,
                    $"{Wrap("[Tiny]", 256)} = @p0",
                    $"{Wrap("\"Tiny\"", 256)} = @p0",
                    [255]
                )
            ),
            Xt4(
                Translated(
                    "column-sign-cast-to-larger-unsigned",
                    "列の側の折り返し：大きい符号なしの型への変換（short から uint）は、変換先の幅の 2^32 を足す",
                    x => (uint)x.Small == 4294967295u,
                    $"{Wrap("[Small]", 4294967296)} = @p0",
                    $"{Wrap("\"Small\"", 4294967296)} = @p0",
                    [4294967295u]
                )
            ),
            Xt4(
                Translated(
                    "column-sign-cast-nullable",
                    "列の側の折り返しは Nullable の列の変換（int? から uint?）にも掛かり、NULL の列は ELSE の枝で NULL のまま",
                    x => (uint?)x.Opt == 5u,
                    $"{Wrap("[Opt]", 4294967296)} = @p0",
                    $"{Wrap("\"Opt\"", 4294967296)} = @p0",
                    [5u]
                )
            ),
            Xt4(
                Translated(
                    "column-sign-cast-then-widening",
                    "列の側の変換は列に近い方から当てる：(long)(uint)x.Id は折り返した後の暗黙の変換を外す",
                    x => (long)(uint)x.Id == 4294967295L,
                    $"{Wrap("[Id]", 4294967296)} = @p0",
                    $"{Wrap("\"Id\"", 4294967296)} = @p0",
                    [4294967295L]
                )
            ),
            Xt4(
                Translated(
                    "column-sign-cast-in-item",
                    "IN の要素の解決 TryGetIn（OperandSql）：要素の側の符号だけの変換も折り返しで書き、要素の値は列名の無いパラメータ",
                    x => uints.Contains((uint)x.Id),
                    $"{Wrap("[Id]", 4294967296)} IN (@p0, @p1)",
                    $"{Wrap("\"Id\"", 4294967296)} IN (@p0, @p1)",
                    [10u, 4294967295u]
                )
            ),
            Xt4(
                Refused(
                    "column-int-to-ulong-refused",
                    "列の側の確かめ ChangesColumnValue の ulong の腕：int から ulong の折り返しの値は bigint に入らないので断る",
                    x => (ulong)x.Id == 5ul,
                    typeof(NotSupportedException),
                    "Int32",
                    "UInt64"
                )
            ),
            Xt4(
                Refused(
                    "column-long-to-ulong-refused",
                    "列の側の確かめ ChangesColumnValue の ulong の腕：long から ulong も断る",
                    x => (ulong)x.Big == 5ul,
                    typeof(NotSupportedException),
                    "Int64",
                    "UInt64"
                )
            ),
            Xt4(
                Refused(
                    "column-unsigned-to-same-size-signed-refused",
                    "列の側の確かめ ChangesColumnValue の符号なしの腕：uint から同じ大きさの int は上半分を負の値へ折り返すので断る",
                    x => (int)x.Count == -1,
                    typeof(NotSupportedException),
                    "UInt32",
                    "Int32"
                )
            ),
            Translated(
                "column-float-to-double",
                "列の側の確かめ ChangesColumnValue の小数への腕の例外：float から double は外す",
                x => (double)x.Single == 1.5,
                "[Single] = @p0",
                "\"Single\" = @p0",
                [1.5]
            ),
            Translated(
                "column-integral-to-decimal",
                "列の側の確かめ ChangesColumnValue の小数への腕：整数から decimal は外す（SQLite は列の型の範囲の中の整数の decimal を long で束縛する＝IntegralDecimalComparison）",
                x => (decimal)x.Big == 2m,
                "[Big] = @p0",
                "\"Big\" = @p0",
                [2m],
                sqliteBound: [2L]
            ),
            // ---- 値の側：値を変える変換は C# と同じに当てて束縛する ----
            Translated(
                "value-narrowing-cast",
                "値の側の評価 EvaluateValue（VisitComparison の右）：明示の変換を当てた値を束縛する",
                x => x.Id == (int)ratio,
                "[Id] = @p0",
                "\"Id\" = @p0",
                [10],
                compiles: 1
            ),
            Translated(
                "value-narrowing-cast-on-left",
                "値の側の評価 EvaluateValue（VisitComparison の左）：左にある値の明示の変換も当てる",
                x => (int)ratio == x.Id,
                "@p0 = [Id]",
                "@p0 = \"Id\"",
                [10],
                compiles: 1
            ),
            Translated(
                "value-lossy-int-to-float",
                "値の側の外す変換 KeepsBoundValue の失わずに広げる表：丸めうる int から float は外さずに当てる",
                x => x.Single == bigInt,
                "[Single] = @p0",
                "\"Single\" = @p0",
                [(float)PowerOfTwoEdges.TwoPow24],
                compiles: 1
            ),
            Translated(
                "value-lossy-long-to-double",
                "値の側の外す変換 KeepsBoundValue の失わずに広げる表：丸めうる long から double は外さずに当てる",
                x => x.Ratio == bigLong,
                "[Ratio] = @p0",
                "\"Ratio\" = @p0",
                [(double)bigLong],
                compiles: 1
            ),
            Translated(
                "value-char-to-int",
                "値の側の外す変換 KeepsBoundValue：char から数への変換は外さずに当てる",
                x => x.Id == letter,
                "[Id] = @p0",
                "\"Id\" = @p0",
                [97],
                compiles: 1
            ),
            Translated(
                "value-enum-to-long",
                "値の側の外す変換 KeepsBoundValue の列挙の腕：列挙から数への変換は外さずに当てる",
                x => x.Big == (long)kind,
                "[Big] = @p0",
                "\"Big\" = @p0",
                [2L],
                compiles: 1
            ),
            Refused(
                "value-nullable-unlift-of-null",
                "値の側の外す変換 KeepsBoundValue の Nullable を外す腕：外さずに評価し、null は C# と同じに例外",
                x => x.Id == (int)none!,
                typeof(InvalidOperationException)
            ),
            // ---- 値の側：束縛する値を変えない変換は外し、式木をコンパイルしない ----
            Translated(
                "value-nullable-lift",
                "値の側の外す変換 KeepsBoundValue の同じ型の腕：Nullable の持ち上げはコンパイルせずに読む",
                x => x.Opt == captured,
                "[Opt] = @p0",
                "\"Opt\" = @p0",
                [9],
                compiles: 0
            ),
            Translated(
                "value-lossless-widening",
                "値の側の外す変換 KeepsBoundValue の失わずに広げる表：int から long はコンパイルせずに読む",
                x => x.Big == captured,
                "[Big] = @p0",
                "\"Big\" = @p0",
                [9],
                compiles: 0
            ),
            Translated(
                "value-lossless-widening-to-decimal",
                "値の側の外す変換 KeepsBoundValue：decimal の演算子メソッドを持つ int から decimal もコンパイルせずに読む",
                x => x.Price == captured,
                "[Price] = @p0",
                "\"Price\" = @p0",
                [9],
                compiles: 0
            ),
            // ---- 日付部品の IN（要素の値は列名の無いパラメータ） ----
            Translated(
                "in-over-date-part",
                "IN の要素の解決 TryGetIn の instance の形（List.Contains）：日付部品の IN",
                x => days.Contains(x.At!.Value.Date),
                "CAST([At] AS date) IN (@p0, @p1)",
                "datetime(date(\"At\")) IN (@p0, @p1)",
                [new DateTime(2024, 1, 15), new DateTime(2024, 1, 16)]
            ),
            Translated(
                "not-in-over-date-part",
                "IN の要素の解決 TryGetIn の instance の形と BuildInClause の否定：日付部品の NOT IN の NULL の補償",
                x => !days.Contains(x.At!.Value.Date),
                "(CAST([At] AS date) NOT IN (@p0, @p1) OR CAST([At] AS date) IS NULL)",
                "(datetime(date(\"At\")) NOT IN (@p0, @p1) OR datetime(date(\"At\")) IS NULL)",
                [new DateTime(2024, 1, 15), new DateTime(2024, 1, 16)]
            ),
            Translated(
                "in-over-date-part-array",
                "IN の要素の解決 TryGetIn の static の形（配列の Contains）：日付部品の IN",
                x => dayArray.Contains(x.At!.Value.Date),
                "CAST([At] AS date) IN (@p0, @p1)",
                "datetime(date(\"At\")) IN (@p0, @p1)",
                [new DateTime(2024, 1, 15), new DateTime(2024, 1, 16)]
            ),
            // ---- 列名の無い値の束縛（AddQueryParameter） ----
            Bound(
                "bind-columnless-datetime-as-datetime2",
                "AddQueryParameter の列名の無い腕の DateTime の枝（SQL Server だけ）：datetime2 で束縛する。SQLite は AddWithValue のまま",
                null,
                legacyReadBack,
                SqlDbType.DateTime2,
                SqliteType.Text
            ),
            Bound(
                "bind-columnless-other-type-by-value",
                "AddQueryParameter の列名の無い腕のほかの型：DateTime でない値は AddWithValue のまま",
                null,
                2024,
                SqlDbType.Int,
                SqliteType.Integer
            ),
            Bound(
                "bind-column-datetime-keeps-column-type",
                "AddQueryParameter の列名のある腕：datetime の列が読み戻せる値は列の型（datetime）を借りる",
                "legacy_at",
                legacyReadBack,
                SqlDbType.DateTime,
                SqliteType.Text
            ),
            Xt4(
                Bound(
                    "bind-columnless-unsigned-as-bigint",
                    "AddQueryParameter の列名の無い腕の符号なしの枝（SQL Server だけ）：SqlClient が送れない uint を bigint で束縛する。SQLite は AddWithValue のまま",
                    null,
                    3_000_000_000u,
                    SqlDbType.BigInt,
                    SqliteType.Integer,
                    sqlServerValue: 3_000_000_000L
                )
            ),
            // ---- Equals(object)：型の違う値は C# でどの行とも等しくないので常に偽へ畳む ----
            Xt2(
                Translated(
                    "equals-object-other-type-folds",
                    "Equals の腕の IsOtherTypeForEqualsObject：値型の列と実行時の型の違う値（long）は常に偽へ畳み、値を束縛しない",
                    x => x.Id.Equals(10L),
                    "1 = 0",
                    "1 = 0",
                    []
                ),
                "値型の受け手の腕。両方言の翻訳器の形を固定する"
            ),
            Xt2(
                Translated(
                    "equals-object-boxed-receiver-folds",
                    "IsOtherTypeForEqualsObject の受け手の変換の剥がし：object への箱詰めを外して列の型（int）を読み、long の値を畳む",
                    x => ((object)x.Id).Equals(10L),
                    "1 = 0",
                    "1 = 0",
                    []
                ),
                "受け手の箱詰めを外す腕。外さないと受け手が object のまま畳めず等値へ翻訳する"
            ),
            Xt2(
                Translated(
                    "equals-object-interface-receiver-folds",
                    "IsOtherTypeForEqualsObject の受け手の変換の剥がし：インターフェイス（IComparable）への変換を外して列の型を読む",
                    x => ((IComparable)x.Id).Equals(10L),
                    "1 = 0",
                    "1 = 0",
                    []
                ),
                "受け手のインターフェイスへの変換を外す腕（object と別の条件の項）"
            ),
            Xt2(
                Translated(
                    "equals-object-unsealed-receiver-compares",
                    "IsOtherTypeForEqualsObject の封印の条件：封印されていない参照型の列（object）は別の実行時の型の値と等しくありうるので畳まずに等値で比べる",
                    x => x.Loose!.Equals(10L),
                    "[Loose] = @p0",
                    "\"Loose\" = @p0",
                    [10L]
                ),
                "封印の条件の腕。生成の列は値型か封印された型だけなので、手書きの [Column] のプロパティの形をこの表で持つ"
            ),
        ];
    }

    [Theory(
        DisplayName = "翻訳器の変換の表：比べる側の変換・日付部品の IN・列名の無い値の束縛を両方言で C# と同じに扱う"
    )]
    [MemberData(nameof(Ids))]
    public void Conversion_FollowsCSharp(string id)
    {
        var row = Table[id].Input;

        if (row.Binding is { } binding)
        {
            CheckBinding(id, binding);
            return;
        }

        var predicate = row.Predicate!;
        var sqlServerParameters = new List<SqlServerParam>();
        var sqliteParameters = new List<SqliteParam>();
        string? sqlServerSql = null;
        string? sqliteSql = null;
        var before = Evaluator.FallbackCompileCount;
        var sqlServer = Record.Exception(() =>
            sqlServerSql = SqlServerTranslator.ToCondition(predicate.Body, sqlServerParameters)
        );
        var sqlServerCompiles = Evaluator.FallbackCompileCount - before;
        before = Evaluator.FallbackCompileCount;
        var sqlite = Record.Exception(() =>
            sqliteSql = SqliteTranslator.ToCondition(predicate.Body, sqliteParameters)
        );
        var sqliteCompiles = Evaluator.FallbackCompileCount - before;

        if (row.Refusal is { } refusal)
        {
            foreach (var thrown in new[] { sqlServer, sqlite })
            {
                thrown.Should().NotBeNull($"{id} は断る");
                thrown!.GetType().Should().Be(refusal, id);

                foreach (var name in row.RefusalNames)
                {
                    thrown.Message.Should().Contain(name, $"{id} の文は変換を名指す");
                }
            }

            return;
        }

        sqlServer.Should().BeNull(id);
        sqlite.Should().BeNull(id);
        sqlServerSql.Should().Be(row.SqlServer, id);
        sqliteSql.Should().Be(row.Sqlite, id);
        sqlServerParameters
            .Select(parameter => parameter.Value)
            .Should()
            .Equal(row.Bound, (actual, expected) => Equals(actual, expected), id);
        sqliteParameters
            .Select(parameter => parameter.Value)
            .Should()
            .Equal(
                row.SqliteBound ?? row.Bound,
                (actual, expected) => Equals(actual, expected),
                id
            );

        if (row.Compiles is { } compiles)
        {
            sqlServerCompiles.Should().Be(compiles, $"{id}（SQL Server）");
            sqliteCompiles.Should().Be(compiles, $"{id}（SQLite）");
        }
    }

    /// <summary>束縛の行：probes の EntitySaveMetadata の AddQueryParameter へ渡し、両方言のパラメータの型と値を比べる</summary>
    private static void CheckBinding(string id, BindingCase binding)
    {
        var sqlServer = new SqlCommand();
        SqlServerMetadata
            .For(typeof(ProbeEntity))
            .AddQueryParameter(sqlServer, "@p0", binding.ColumnName, binding.Value);

        sqlServer.Parameters[0].SqlDbType.Should().Be(binding.SqlServerType, $"{id}（SQL Server）");
        var sqlServerValue = sqlServer.Parameters[0].Value;
        var expectedSqlServerValue = binding.SqlServerValue ?? binding.Value;

        sqlServerValue.Should().Be(expectedSqlServerValue, $"{id}（SQL Server）");
        sqlServerValue
            .GetType()
            .Should()
            .Be(expectedSqlServerValue.GetType(), $"{id}（SQL Server の値の型）");

        var sqlite = new SqliteCommand();
        SqliteMetadata
            .For(typeof(ProbeEntity))
            .AddQueryParameter(sqlite, "@p0", binding.ColumnName, binding.Value);

        sqlite.Parameters[0].SqliteType.Should().Be(binding.SqliteType, $"{id}（SQLite）");
        sqlite.Parameters[0].Value.Should().Be(binding.Value, $"{id}（SQLite）");
    }
}
