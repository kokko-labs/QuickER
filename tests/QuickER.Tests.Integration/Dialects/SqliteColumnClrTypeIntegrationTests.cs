using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using QuickER.Provider.Sqlite;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// SQLite の宣言型から解決した C# 型が、ドライバ（Microsoft.Data.Sqlite）が返す CLR 型と一致し、値を落とさないことを
/// 実ファイルの SQLite で確かめる統合テスト。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SqliteColumnClrTypeIntegrationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// REAL アフィニティの列（REAL・FLOAT・DOUBLE）はドライバが <c>double</c> で返し、型マッパーも <c>double</c> へ写す。
    /// 単精度の <c>float</c> で受けると、倍精度で格納された値の下位の桁が落ちることを併せて確かめる。
    /// </summary>
    [Fact(
        DisplayName = "[Integration] SQLite の REAL アフィニティの列は double として読め、値を落とさない"
    )]
    public async Task RealAffinityColumns_MatchDriverDoubleAndKeepPrecision()
    {
        using var database = SqliteTempDatabase.Create();
        await database.ApplyDdlAsync(
            """
            CREATE TABLE measures (id INTEGER PRIMARY KEY, r REAL, f FLOAT, d DOUBLE);
            INSERT INTO measures VALUES (1, 0.1234567890123, 0.1234567890123, 0.1234567890123);
            """,
            Ct
        );

        await using var connection = new SqliteConnection(database.ReadWriteConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT r, f, d FROM measures WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();

        var mapper = new SqliteCSharpTypeMapper();
        var declared = new[] { "REAL", "FLOAT", "DOUBLE" };

        for (var i = 0; i < reader.FieldCount; i++)
        {
            reader.GetFieldType(i).Should().Be<double>();
            mapper
                .Map(declared[i])
                .TypeName.Should()
                .Be("double", $"{declared[i]} は double へ写すこと");

            var stored = reader.GetDouble(i);
            stored.Should().Be(0.1234567890123);
            ((double)(float)stored)
                .Should()
                .NotBe(stored, "単精度で受けると倍精度で格納された値の下位の桁が落ちる");
        }
    }
}
