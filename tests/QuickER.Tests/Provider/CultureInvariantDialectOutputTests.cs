using System.Globalization;
using System.Text;
using AwesomeAssertions;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// 型の変換と正規化・取込が組み立てる型の表記が、書式のカルチャ（<see cref="CultureScope.Cultures"/>）に依らず
/// インバリアントで作ったものと一致することを、5 方言の総当たりで確かめる
/// </summary>
/// <remarks>
/// DDL と同期スクリプトのカルチャ（固定フィクスチャの定義と方言の型の選択肢を載せた図・生成日時の暦）は、方言の DDL の表
/// （<c>DialectDdlRows</c>）の全行に掛かる自動の照合が確かめる（<c>DialectDdlRows.Culture.cs</c> の行がその図を流す）。
/// </remarks>
public sealed class CultureInvariantDialectOutputTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>方言の選択肢に加えて流す型の表記（負のスケール・精度なし・単位語・配列など）</summary>
    private static readonly string[] ExtraTypeNotations =
    [
        "numeric(10,-2)",
        "NUMBER(10,-2)",
        "NUMBER(*,2)",
        "decimal(18,4)",
        "decimal(38,10)",
        "numeric(18,0)",
        "timestamp(3)",
        "timestamp",
        "TIMESTAMP(9)",
        "VARCHAR2(50 CHAR)",
        "varchar(255)",
        "nvarchar(max)",
        "integer[]",
        "time(3) with time zone",
        "float(24)",
        "datetime2(3)",
        "bit(8)",
    ];

    [Fact(
        DisplayName = "型の解析・方言の間の変換・表記の正規化・型トークンは、書式のカルチャに依らずインバリアントと同じ"
    )]
    public void TypeConversion_IsCultureInvariant()
    {
        CultureScope.FindDifferences(ConvertAllTypes).Should().BeEmpty();
    }

    /// <summary>
    /// 5 方言の取込が DB のカタログから型の表記を組み立てる関数（SQL Server・Oracle の <c>FormatDataType</c>、PostgreSQL の
    /// <c>NormalizeFormatType</c>、MySQL の <c>COLUMN_TYPE</c> の正規化）は、書式のカルチャに依らずインバリアントと同じ
    /// </summary>
    /// <remarks>
    /// SQLite は宣言の型をそのまま図へ入れる（組み立てる関数が無い）ので対象に無い。数値の引数には負の値も渡す＝カタログは
    /// 長さ・桁数に負の値を返さないが、関数の出力は渡した値に依らずカルチャから独立していなければならない（スウェーデン語の
    /// 負号 U+2212 が型の表記へ入ると、その図の DDL を DB が断る）。
    /// </remarks>
    [Fact(
        DisplayName = "取込が組み立てる型の表記は 5 方言とも、書式のカルチャに依らずインバリアントと同じ"
    )]
    public void ImportedTypeSpelling_IsCultureInvariant()
    {
        var differences = new List<string>();

        foreach (
            var (dialect, produce) in new (string, Func<string>)[]
            {
                ("SqlServer", FormatSqlServerImportedTypes),
                ("Oracle", FormatOracleImportedTypes),
                ("PostgreSql", FormatPostgreSqlImportedTypes),
                ("MySql", FormatMySqlImportedTypes),
            }
        )
        {
            differences.AddRange(
                CultureScope.FindDifferences(produce).Select(d => $"{dialect}: {d}")
            );
        }

        differences.Should().BeEmpty(string.Join(" / ", differences));
    }

    /// <summary>取込の型の表記へ流す長さ（文字列・バイナリ）</summary>
    private static readonly int?[] ImportedLengths = [null, -2, -1, 0, 50, 8000];

    /// <summary>取込の型の表記へ流す精度（数値・小数秒）</summary>
    private static readonly int?[] ImportedPrecisions = [null, -3, 0, 10, 38];

    /// <summary>取込の型の表記へ流すスケール</summary>
    private static readonly int?[] ImportedScales = [null, -2, 0, 2];

    /// <summary>SQL Server の <c>FormatDataType</c> へカタログの値の組み合わせを総当たりで流した結果</summary>
    private static string FormatSqlServerImportedTypes()
    {
        var builder = new StringBuilder();

        foreach (
            var dataType in new[]
            {
                "char",
                "varchar",
                "nchar",
                "nvarchar",
                "binary",
                "varbinary",
                "decimal",
                "numeric",
                "datetime2",
                "time",
                "datetimeoffset",
                "int",
            }
        )
        {
            foreach (var length in ImportedLengths)
            {
                foreach (var precision in ImportedPrecisions)
                {
                    foreach (var scale in ImportedScales)
                    {
                        foreach (var datetimePrecision in ImportedPrecisions)
                        {
                            builder.AppendLine(
                                Attempt(() =>
                                    SqlServerSchemaImporter.FormatDataType(
                                        dataType,
                                        length,
                                        precision,
                                        scale,
                                        datetimePrecision
                                    )
                                )
                            );
                        }
                    }
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Oracle の <c>FormatDataType</c> へカタログの値の組み合わせを総当たりで流した結果</summary>
    private static string FormatOracleImportedTypes()
    {
        var builder = new StringBuilder();

        foreach (
            var dataType in new[]
            {
                "NUMBER",
                "VARCHAR2",
                "CHAR",
                "NVARCHAR2",
                "NCHAR",
                "RAW",
                "FLOAT",
                "TIMESTAMP(6) WITH TIME ZONE",
                "DATE",
            }
        )
        {
            foreach (var precision in ImportedPrecisions)
            {
                foreach (var scale in ImportedScales)
                {
                    foreach (var length in ImportedLengths)
                    {
                        foreach (var charUsed in new[] { null, "B", "C" })
                        {
                            builder.AppendLine(
                                Attempt(() =>
                                    OracleSchemaImporter.FormatDataType(
                                        dataType,
                                        precision,
                                        scale,
                                        length,
                                        length,
                                        charUsed
                                    )
                                )
                            );
                        }
                    }
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>PostgreSQL の <c>format_type</c> の出力の正規化（取込が通す関数）の結果</summary>
    private static string FormatPostgreSqlImportedTypes()
    {
        var builder = new StringBuilder();

        foreach (
            var formatType in new[]
            {
                "numeric(10,-2)",
                "numeric(18,0)",
                "character varying(50)",
                "character(10)",
                "timestamp(3) without time zone",
                "timestamp with time zone",
                "time(3) with time zone",
                "bit(8)",
                "bit varying(16)",
                "interval second(3)",
                "integer[]",
                "character varying(20)[]",
            }
        )
        {
            builder.AppendLine(
                Attempt(() => PostgreSqlSchemaImporter.NormalizeFormatType(formatType))
            );
        }

        return builder.ToString();
    }

    /// <summary>MySQL の <c>COLUMN_TYPE</c> の正規化（取込が通す関数）の結果</summary>
    private static string FormatMySqlImportedTypes()
    {
        var builder = new StringBuilder();

        foreach (
            var columnType in new[]
            {
                "decimal(12)",
                "decimal(10,2)",
                "varchar(255)",
                "int(11)",
                "bigint unsigned",
                "datetime(3)",
                "enum('a','b')",
            }
        )
        {
            builder.AppendLine(Attempt(() => MySqlTypeNotation.Normalize(columnType)));
        }

        return builder.ToString();
    }

    /// <summary>関数を呼んだ結果（例外ならその型）。例外の有無もカルチャに依らないことを比べる</summary>
    private static string Attempt(Func<string> format)
    {
        try
        {
            return format();
        }
        catch (Exception ex)
        {
            return "!" + ex.GetType().Name;
        }
    }

    /// <summary>全方言の型の選択肢と追加の表記を、解析・全方言への変換・正規化・型トークンへ通した結果</summary>
    private static string ConvertAllTypes()
    {
        var builder = new StringBuilder();

        foreach (var source in Providers)
        {
            foreach (var type in source.TypeCatalog.DataTypes.Concat(ExtraTypeNotations))
            {
                builder.Append(source.Name).Append(':').Append(type).Append(" normalize=");
                builder.Append(source.SyncCapabilities.NormalizeTypeNotation(type));

                if (!source.TypeCatalog.TryParse(type, out var canonical))
                {
                    builder.AppendLine(" parse=false");
                    continue;
                }

                builder.Append(" token=").Append(CanonicalTypeToken.Format(canonical));

                foreach (var target in Providers)
                {
                    builder
                        .Append(' ')
                        .Append(target.Name)
                        .Append('=')
                        .Append(
                            target.TypeCatalog.TryFormat(canonical, out var native) ? native : "-"
                        );
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
