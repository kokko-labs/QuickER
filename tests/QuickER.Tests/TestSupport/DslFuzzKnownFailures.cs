namespace QuickER.Tests.TestSupport;

/// <summary>簡易 DSL の条件の総当たりの、既知の失敗の一覧（区切り 23 で空にする）</summary>
/// <remarks>
/// キーの入口は <c>dsl-generate:{単位}</c>・<c>dsl-compile:{単位}</c>・<c>dsl-exec:{実装先}:{単位}</c>。
/// 一覧に無い失敗も、載っているのに起きない失敗も赤になる（直したらここから外す）。
/// </remarks>
public static class DslFuzzKnownFailures
{
    private const string FixedIn = "区切り 23";

    /// <summary>既知の失敗</summary>
    public static IReadOnlyList<KnownFuzzFailure> All { get; } =
    [
        // 指摘 29: NOT と完全一致の LIKE が NULL の行も返す（文字列一致は NULL の行がどちら向きでも一致しない）
        Known("dsl-exec:ado:sqlite-plain", "mismatch", "like-exact:fixedstring:not-pos", "29"),
        Known("dsl-exec:ado:sqlite-plain", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:ado:sqlite-vo", "mismatch", "like-exact:fixedstring:not-pos", "29"),
        Known("dsl-exec:ado:sqlite-vo", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:ado:sqlite-vo-guidkey", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:efcore:sqlite-plain", "mismatch", "like-exact:fixedstring:not-pos", "29"),
        Known("dsl-exec:efcore:sqlite-plain", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:efcore:sqlite-vo", "mismatch", "like-exact:fixedstring:not-pos", "29"),
        Known("dsl-exec:efcore:sqlite-vo", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:efcore:sqlite-vo-guidkey", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:inmemory:mysql-plain", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:inmemory:mysql-vo", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:inmemory:sqlite-plain", "mismatch", "like-exact:fixedstring:not-pos", "29"),
        Known("dsl-exec:inmemory:sqlite-plain", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:inmemory:sqlite-vo", "mismatch", "like-exact:fixedstring:not-pos", "29"),
        Known("dsl-exec:inmemory:sqlite-vo", "mismatch", "like-exact:string:not-pos", "29"),
        Known("dsl-exec:inmemory:sqlite-vo-guidkey", "mismatch", "like-exact:string:not-pos", "29"),
        // 指摘 30: 射影の結果の型名が生成物の既存の型と衝突してコンパイルできない
        Known(
            "dsl-compile:collision-entity",
            "compile-error",
            "projection-type-name-collision:ItemEntity",
            "30"
        ),
        Known(
            "dsl-compile:collision-repository",
            "compile-error",
            "projection-type-name-collision:ItemRepository",
            "30"
        ),
        Known(
            "dsl-compile:collision-task",
            "compile-error",
            "projection-type-name-collision:Task",
            "30"
        ),
        Known(
            "dsl-compile:collision-value-object",
            "compile-error",
            "projection-type-name-collision:MemoValue",
            "30"
        ),
        // 指摘 31: GuidKey の値オブジェクトの列に文字列一致を書くとコンパイルできない（CS1929）
        Known("dsl-compile:sqlite-vo-guidkey", "compile-error", "CS1929:stringmatch-guidkey", "31"),
        // 指摘 A8: 射影のフィールドを非 NULL と指定すると NULL 許容の参照型・値オブジェクトの列で CS8601
        Known(
            "dsl-compile:mysql-plain",
            "compile-warning",
            "CS8601:projection-nonnull-field",
            "A8"
        ),
        Known("dsl-compile:mysql-vo", "compile-warning", "CS8601:projection-nonnull-field", "A8"),
        Known(
            "dsl-compile:sqlite-plain",
            "compile-warning",
            "CS8601:projection-nonnull-field",
            "A8"
        ),
        Known("dsl-compile:sqlite-vo", "compile-warning", "CS8601:projection-nonnull-field", "A8"),
        Known(
            "dsl-compile:sqlite-vo-guidkey",
            "compile-warning",
            "CS8601:projection-nonnull-field",
            "A8"
        ),
        // 指摘 B4: NULL 許容の値オブジェクトの列の IN で CS8604
        Known("dsl-compile:mysql-vo", "compile-warning", "CS8604:in-nullable-vo", "B4"),
        Known("dsl-compile:sqlite-vo", "compile-warning", "CS8604:in-nullable-vo", "B4"),
        Known("dsl-compile:sqlite-vo-guidkey", "compile-warning", "CS8604:in-nullable-vo", "B4"),
        // 指摘 B5: 型の範囲外の整数リテラルとの比較で CS0652
        Known("dsl-compile:mysql-plain", "compile-warning", "CS0652:literal-out-of-range", "B5"),
        Known("dsl-compile:sqlite-plain", "compile-warning", "CS0652:literal-out-of-range", "B5"),
        // 指摘 NA6: SQLite の Repository 構成の XmlDoc の cref が解決できない（CS1574）
        Known("dsl-compile:collision-entity", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:collision-repository", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:collision-task", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:collision-value-object", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:mysql-plain", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:mysql-vo", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:sqlite-plain", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:sqlite-vo", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        Known("dsl-compile:sqlite-vo-guidkey", "compile-warning", "CS1574:xmldoc-cref", "NA6"),
        // ---- SQL Server（Integration の DslConditionFuzzSqlServerRuntimeTests）----
        // 指摘 29
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "mismatch",
            "like-exact:fixedstring:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "mismatch",
            "like-exact:string:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-vo",
            "mismatch",
            "like-exact:fixedstring:not-pos",
            "29"
        ),
        Known("dsl-sqlserver-exec:ado:sqlserver-vo", "mismatch", "like-exact:string:not-pos", "29"),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-vo-guidkey",
            "mismatch",
            "like-exact:string:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "mismatch",
            "like-exact:fixedstring:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "mismatch",
            "like-exact:string:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-vo",
            "mismatch",
            "like-exact:fixedstring:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-vo",
            "mismatch",
            "like-exact:string:not-pos",
            "29"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-vo-guidkey",
            "mismatch",
            "like-exact:string:not-pos",
            "29"
        ),
        // 指摘 31
        Known(
            "dsl-sqlserver-compile:sqlserver-vo-guidkey",
            "compile-error",
            "CS1929:stringmatch-guidkey",
            "31"
        ),
        // 指摘 A8
        Known(
            "dsl-sqlserver-compile:sqlserver-plain",
            "compile-warning",
            "CS8601:projection-nonnull-field",
            "A8"
        ),
        Known(
            "dsl-sqlserver-compile:sqlserver-vo",
            "compile-warning",
            "CS8601:projection-nonnull-field",
            "A8"
        ),
        Known(
            "dsl-sqlserver-compile:sqlserver-vo-guidkey",
            "compile-warning",
            "CS8601:projection-nonnull-field",
            "A8"
        ),
        // 指摘 B4
        Known(
            "dsl-sqlserver-compile:sqlserver-vo",
            "compile-warning",
            "CS8604:in-nullable-vo",
            "B4"
        ),
        Known(
            "dsl-sqlserver-compile:sqlserver-vo-guidkey",
            "compile-warning",
            "CS8604:in-nullable-vo",
            "B4"
        ),
        // 指摘 B5
        Known(
            "dsl-sqlserver-compile:sqlserver-plain",
            "compile-warning",
            "CS0652:literal-out-of-range",
            "B5"
        ),
        // 指摘 DF1: QuickER 版 Repository（SQL Server）で、列の型の範囲外の整数リテラルとの比較が、列の型で束縛したパラメータの変換で OverflowException になる
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:byte:not-pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:byte:pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:int16:not-pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:int16:pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:int32:not-pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:int32:pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:byte:not-pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:byte:pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:int16:not-pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:int16:pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:int32:not-pos:OverflowException",
            "DF1"
        ),
        Known(
            "dsl-sqlserver-exec:ado:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:int32:pos:OverflowException",
            "DF1"
        ),
        // 指摘 DF2: EF Core（SQL Server）で、列の型の範囲外の整数リテラルとの比較が、サーバー側の算術オーバーフロー（SqlException）になる
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:byte:not-pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:byte:pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:int16:not-pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-eq-literal-out-of-range:int16:pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:byte:not-pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:byte:pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:int16:not-pos:SqlException",
            "DF2"
        ),
        Known(
            "dsl-sqlserver-exec:efcore:sqlserver-plain",
            "exception",
            "compare-lt-literal-out-of-range:int16:pos:SqlException",
            "DF2"
        ),
    ];

    private static KnownFuzzFailure Known(
        string entrance,
        string property,
        string difference,
        string finding,
        string fixedIn = FixedIn
    ) => new(new(entrance, property, difference), finding, fixedIn);
}
