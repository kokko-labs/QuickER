using QuickER.Dialects;
using QuickER.Model;
using QuickER.Tests.TestSupport.Engines;

namespace QuickER.Tests.Provider;

/// <summary>
/// 方言の DDL の表（5 方言を行列の軸に、図・差分の項目から作る DDL と同期スクリプトの文の形・識別子・制約名・注記・扱い・
/// 固定文の英語・カルチャを、入力と期待の行で固定する）
/// </summary>
/// <remarks>
/// <para>
/// 主題ごとの行は <c>DialectDdlRows.{主題}.cs</c> の private メソッドが返し、<see cref="AllRows"/> に足す。
/// 行の書き方は <see cref="DialectDdlRow"/>、出力の読み方は <see cref="DialectDdlReader"/>、照合と自動の照合は <see cref="DialectDdlRunner"/> を参照する。
/// </para>
/// <para>期待の書き方の決まり：</para>
/// <list type="bullet">
/// <item><description>断片を含むことの確認は、構造の行（<see cref="DdlExpect.Lines"/>）で書く（同じ行の構造まで照合する）。構造に写さない文（説明・表のオプション・同期の文）は素の行で書く。</description></item>
/// <item><description>断片を含まないことの確認は、素の行の <c>*断片*</c>（<see cref="DdlExpect.NoText"/>）で書く（構造で書くと、別の形で現れた断片を見逃す）。</description></item>
/// <item><description>行の中で方言を名指しして分岐しない。方言ごとに違う型の表記は <see cref="DdlModel"/> の対応表から引き、期待は <see cref="DialectDdlRow.Expect(string, Action{DdlExpect})"/> で方言ごとに書く。</description></item>
/// </list>
/// <para>主題のファイルの静的なデータは式形式のプロパティ（<c>=&gt;</c>）にする（partial の初期化順で null になるため）。</para>
/// </remarks>
internal static partial class DialectDdlRows
{
    private const string Ss = DialectNames.SqlServer;
    private const string Pg = DialectNames.PostgreSql;
    private const string My = DialectNames.MySql;
    private const string Or = DialectNames.Oracle;
    private const string Sl = DialectNames.Sqlite;

    /// <summary>表</summary>
    public static ScenarioTable<DialectDdlCase> Table { get; } =
        new(EngineIds.Dialect, AllRows().Select(row => row.ToScenario()));

    /// <summary>(行の Id, 方言) の組（Theory のデータ）</summary>
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();

        foreach (var row in Table)
        {
            foreach (var dialect in row.Input.Expected.Keys.Order(StringComparer.Ordinal))
            {
                data.Add(row.Id, dialect);
            }
        }

        return data;
    }

    private static IEnumerable<DialectDdlRow> AllRows() =>
        [
            .. UniqueRows(),
            .. GeneratorRows(),
            .. CrossCuttingRows(),
            .. TreatmentRows(),
            .. CoverageRows(),
        ];
}

/// <summary>DDL の表の行が使う図の部品</summary>
internal static class DdlModel
{
    /// <summary>主キーの列（NOT NULL）</summary>
    public static Column Pk(string name, string type) =>
        new()
        {
            Name = name,
            DataType = type,
            IsPrimaryKey = true,
            IsNullable = false,
        };

    /// <summary>列</summary>
    public static Column Col(string name, string type, bool nullable = true) =>
        new()
        {
            Name = name,
            DataType = type,
            IsNullable = nullable,
        };

    /// <summary>表</summary>
    public static Entity Table(string name, params Column[] columns)
    {
        var entity = new Entity { TableName = name };

        foreach (var column in columns)
        {
            entity.Columns.Add(column);
        }

        return entity;
    }

    /// <summary>図</summary>
    public static ErDiagram Diagram(params Entity[] entities)
    {
        var diagram = new ErDiagram();

        foreach (var entity in entities)
        {
            diagram.Entities.Add(entity);
        }

        return diagram;
    }

    /// <summary>方言ごとの整数の型の表記</summary>
    public static IReadOnlyDictionary<string, string> IntType { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DialectNames.SqlServer] = "int",
            [DialectNames.PostgreSql] = "integer",
            [DialectNames.MySql] = "int",
            [DialectNames.Oracle] = "NUMBER(10)",
            [DialectNames.Sqlite] = "INTEGER",
        };

    /// <summary>方言ごとの可変長の文字列の型の表記（長さを受ける）</summary>
    public static string VarcharType(string dialect, int length) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            VarcharFormat[dialect],
            length
        );

    private static IReadOnlyDictionary<string, string> VarcharFormat { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DialectNames.SqlServer] = "nvarchar({0})",
            [DialectNames.PostgreSql] = "varchar({0})",
            [DialectNames.MySql] = "varchar({0})",
            [DialectNames.Oracle] = "VARCHAR2({0})",
            [DialectNames.Sqlite] = "NVARCHAR({0})",
        };
}
