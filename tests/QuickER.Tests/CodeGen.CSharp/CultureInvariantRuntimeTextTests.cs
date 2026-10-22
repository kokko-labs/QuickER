using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuickER.Runtime.EntityFrameworkCore;
using QuickER.Runtime.Sync;
using QuickER.Tests.TestSupport;
using Xunit;

namespace QuickER.Tests.CodeGen.CSharp;

/// <summary>
/// 生成ランタイムが数値から組み立てて DB・ジャーナルへ書く文字列（同期のキーのテキストの decimal、SQLite の
/// <c>PRAGMA busy_timeout</c>）が、書式のカルチャ（<see cref="CultureScope.Cultures"/>）に依らないことを確かめる
/// </summary>
/// <remarks>
/// どれも組み立てる側で <see cref="CultureInfo.InvariantCulture"/> を渡しているので、ここはそれを各カルチャで固定するだけ。
/// 対象はランタイムのパッケージの型（パッケージ用ソースはテンプレートの描画そのもの＝ドリフトテストが一致を固定する）。
/// </remarks>
public sealed class CultureInvariantRuntimeTextTests
{
    /// <summary>キーのテキストへ流す decimal（負・指数表記になる小ささ・末尾のゼロ・大きな値）</summary>
    private static readonly decimal[] DecimalKeys =
    [
        0m,
        -1m,
        1.50m,
        -12345.6789m,
        0.000001m,
        -0.0000000001m,
        79228162514264337593543950335m,
        -79228162514264337593543950335m,
    ];

    [Fact(
        DisplayName = "同期のキーのテキストの decimal の書き出し・読み取り・書き直しは、書式のカルチャに依らずインバリアントと同じ"
    )]
    public void SyncKeyTextDecimal_IsCultureInvariant()
    {
        CultureScope
            .FindDifferences(() =>
            {
                var builder = new StringBuilder();

                foreach (var value in DecimalKeys)
                {
                    var text = SyncKeyText.FormatDecimal(value);
                    builder
                        .Append(text)
                        .Append(" => ")
                        .AppendLine(
                            SyncKeyText.ParseDecimal(text).ToString(CultureInfo.InvariantCulture)
                        );
                }

                // 以前の版が書いた指数表記の読み取りと書き直し
                foreach (var legacy in new[] { "1E-06", "-1E-06", "1.5E+3" })
                {
                    builder
                        .Append(legacy)
                        .Append(" => ")
                        .Append(
                            SyncKeyText.ParseDecimal(legacy).ToString(CultureInfo.InvariantCulture)
                        )
                        .Append(" / ")
                        .AppendLine(SyncKeyText.WithoutDecimalExponent(legacy));
                }

                return builder.ToString();
            })
            .Should()
            .BeEmpty();
    }

    [Fact(
        DisplayName = "同期のキーのテキストの負の decimal は、どのカルチャでも ASCII の負号で書く"
    )]
    public void SyncKeyTextDecimal_UsesAsciiMinusInEveryCulture()
    {
        foreach (var name in CultureScope.Cultures)
        {
            using var scope = new CultureScope(name);

            SyncKeyText.FormatDecimal(-0.000001m).Should().Be("-0.000001", name);
            SyncKeyText.ParseDecimal("-0.000001").Should().Be(-0.000001m, name);
        }
    }

    [Theory(
        DisplayName = "SQLite の SqlConnectionFactory が開いた接続の busy timeout は、どのカルチャでも Default Timeout の値"
    )]
    [InlineData(7, 7_000)]
    [InlineData(0, int.MaxValue)]
    public void SqliteConnectionFactory_BusyTimeout_IsCultureInvariant(
        int defaultTimeout,
        int expected
    )
    {
        foreach (var name in CultureScope.Cultures)
        {
            using var scope = new CultureScope(name);
            var factory = new QuickER.Runtime.Sqlite.SqlConnectionFactory(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Data Source=:memory:;Default Timeout={defaultTimeout}"
                )
            );
            using var connection = factory.CreateConnection();
            connection.Open();

            ReadBusyTimeout(connection).Should().Be(expected, name);
        }
    }

    [Theory(
        DisplayName = "EF Core の SqliteBusyTimeoutInterceptor が当てる busy timeout は、どのカルチャでもコマンドタイムアウトの値"
    )]
    [InlineData(7, 7_000)]
    [InlineData(0, int.MaxValue)]
    public async Task SqliteBusyTimeoutInterceptor_IsCultureInvariant(
        int commandTimeout,
        int expected
    )
    {
        foreach (var name in CultureScope.Cultures)
        {
            using var scope = new CultureScope(name);
            var builder = new DbContextOptionsBuilder();
            builder
                .UseSqlite("Data Source=:memory:", sqlite => sqlite.CommandTimeout(commandTimeout))
                .IgnoreServiceProviderCacheLimit();
            builder.AddInterceptors(SqliteBusyTimeoutInterceptor.Instance);
            await using var context = new DbContext(builder.Options);
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

            ReadBusyTimeout((SqliteConnection)context.Database.GetDbConnection())
                .Should()
                .Be(expected, name);
        }
    }

    /// <summary>接続の busy timeout（ミリ秒）を読む</summary>
    private static int ReadBusyTimeout(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout";

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
