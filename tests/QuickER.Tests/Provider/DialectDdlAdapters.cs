using QuickER.Dialects;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport.Engines;
using SqliteRuntime = QuickER.Tests.GeneratedSqliteFixture.EntitySaveMetadata;
using SqlServerRuntime = QuickER.Tests.GeneratedFixture.EntitySaveMetadata;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の識別子・DDL の表の、方言ごとのアダプタ（方言による違いは呼び方と出力の読み方だけで、期待は持たない）
/// </summary>
/// <remarks>
/// <para>
/// 表の行と実行部は方言を名指しして分岐しない。期待は行が方言ごとに書く。方言を足したら、アダプタを書くまで
/// <see cref="DialectDdlAxis.Axis"/> の構築が赤になる。
/// </para>
/// <para>
/// 区切り文字（<see cref="OpenQuote"/> / <see cref="CloseQuote"/>）は、DDL の出力を構造の行へ写すときに識別子を読み戻すための
/// テスト側のデータで、製品のクォートの関数を使わない（製品の関数で読み戻すと、エスケープの誤りを同じ関数が見逃す）。
/// </para>
/// </remarks>
internal abstract class DialectDdlAdapter : IMatrixAdapter
{
    /// <summary>受け持つ方言（<see cref="DialectNames"/> の値）</summary>
    public abstract string AxisValue { get; }

    /// <summary>方言のプロバイダ（DDL 生成器・同期スクリプト・同期の能力はこのインスタンスのものを使う）</summary>
    public abstract IDatabaseProvider Provider { get; }

    /// <summary>識別子のクォート開始文字</summary>
    public abstract string OpenQuote { get; }

    /// <summary>識別子のクォート終了文字（識別子の中では二重化される）</summary>
    public abstract string CloseQuote { get; }

    /// <summary>テーブル名のクォート（最初の <c>.</c> で分けてそれぞれクォートする）</summary>
    /// <param name="name">名前</param>
    public abstract string Quote(string? name);

    /// <summary>単一の識別子のクォート（分けない）</summary>
    /// <param name="name">名前</param>
    public abstract string QuoteSimple(string? name);

    /// <summary>識別子の中の終端クォート文字のエスケープ</summary>
    /// <param name="name">名前</param>
    public abstract string Escape(string? name);

    /// <summary>
    /// 動的 SQL の文字列リテラルの中へ埋めるクォート（持たない方言は null。SQLite は動的 SQL を組み立てる経路を持たない）
    /// </summary>
    public abstract Func<string, string>? QuoteForDynamicSql { get; }

    /// <summary>
    /// 生成ランタイム（固定フィクスチャ）のテーブル名の引用（持たない方言は null。QuickER 版 Repository のランタイムは
    /// SQL Server と SQLite だけ）
    /// </summary>
    public abstract Func<string, string>? RuntimeQuoteTableName { get; }

    /// <inheritdoc />
    public override string ToString() => AxisValue;
}

/// <summary>方言の識別子・DDL の表の行列の軸（正本は <see cref="DialectNames.All"/>）</summary>
internal static class DialectDdlAxis
{
    /// <summary>方言を持たない観点の方言の水準</summary>
    public const string None = "-";

    /// <summary>軸</summary>
    public static MatrixAxis<DialectDdlAdapter> Axis { get; } =
        new(
            "DialectDdl",
            DialectNames.All,
            [
                new SqlServerDdlAdapter(),
                new PostgreSqlDdlAdapter(),
                new MySqlDdlAdapter(),
                new OracleDdlAdapter(),
                new SqliteDdlAdapter(),
            ]
        );

    /// <summary>方言のアダプタを引く</summary>
    /// <param name="dialect">方言</param>
    public static DialectDdlAdapter Get(string dialect) => Axis.Get(dialect);

    private sealed class SqlServerDdlAdapter : DialectDdlAdapter
    {
        public override string AxisValue => DialectNames.SqlServer;

        public override IDatabaseProvider Provider { get; } = new SqlServerProvider();

        public override string OpenQuote => "[";

        public override string CloseQuote => "]";

        public override string Quote(string? name) => SqlIdentifier.Bracket(name!);

        public override string QuoteSimple(string? name) => SqlIdentifier.BracketSimple(name!);

        public override string Escape(string? name) => SqlIdentifier.Escape(name!);

        public override Func<string, string>? QuoteForDynamicSql =>
            SqlIdentifier.QuoteForDynamicSql;

        public override Func<string, string>? RuntimeQuoteTableName =>
            SqlServerRuntime.QuoteTableName;
    }

    private sealed class PostgreSqlDdlAdapter : DialectDdlAdapter
    {
        public override string AxisValue => DialectNames.PostgreSql;

        public override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

        public override string OpenQuote => "\"";

        public override string CloseQuote => "\"";

        public override string Quote(string? name) => PgIdentifier.Quote(name!);

        public override string QuoteSimple(string? name) => PgIdentifier.QuoteSimple(name!);

        public override string Escape(string? name) => PgIdentifier.Escape(name!);

        public override Func<string, string>? QuoteForDynamicSql => PgIdentifier.QuoteForDynamicSql;

        public override Func<string, string>? RuntimeQuoteTableName => null;
    }

    private sealed class MySqlDdlAdapter : DialectDdlAdapter
    {
        public override string AxisValue => DialectNames.MySql;

        public override IDatabaseProvider Provider { get; } = new MySqlProvider();

        public override string OpenQuote => "`";

        public override string CloseQuote => "`";

        public override string Quote(string? name) => MySqlIdentifier.Quote(name!);

        public override string QuoteSimple(string? name) => MySqlIdentifier.QuoteSimple(name!);

        public override string Escape(string? name) => MySqlIdentifier.Escape(name!);

        public override Func<string, string>? QuoteForDynamicSql =>
            MySqlIdentifier.QuoteForDynamicSql;

        public override Func<string, string>? RuntimeQuoteTableName => null;
    }

    private sealed class OracleDdlAdapter : DialectDdlAdapter
    {
        public override string AxisValue => DialectNames.Oracle;

        public override IDatabaseProvider Provider { get; } = new OracleProvider();

        public override string OpenQuote => "\"";

        public override string CloseQuote => "\"";

        public override string Quote(string? name) => OracleIdentifier.Quote(name!);

        public override string QuoteSimple(string? name) => OracleIdentifier.QuoteSimple(name!);

        public override string Escape(string? name) => OracleIdentifier.Escape(name!);

        public override Func<string, string>? QuoteForDynamicSql =>
            OracleIdentifier.QuoteForDynamicSql;

        public override Func<string, string>? RuntimeQuoteTableName => null;
    }

    private sealed class SqliteDdlAdapter : DialectDdlAdapter
    {
        public override string AxisValue => DialectNames.Sqlite;

        public override IDatabaseProvider Provider { get; } = new SqliteProvider();

        public override string OpenQuote => "\"";

        public override string CloseQuote => "\"";

        public override string Quote(string? name) => SqliteIdentifier.Quote(name!);

        public override string QuoteSimple(string? name) => SqliteIdentifier.QuoteSimple(name!);

        public override string Escape(string? name) => SqliteIdentifier.Escape(name!);

        public override Func<string, string>? QuoteForDynamicSql => null;

        public override Func<string, string>? RuntimeQuoteTableName => SqliteRuntime.QuoteTableName;
    }
}
