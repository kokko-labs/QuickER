using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuickER.Dialects;

namespace QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

/// <summary>
/// 期待の行の実 DB の段が読む、取込では見えないカタログの探針（方言ごとに名前つきの SQL を 1 か所に置く）
/// </summary>
/// <remarks>
/// <para>
/// 探針の SQL は、先頭の列が対象（<c>スキーマ.表.列</c> など）、残りの列が属性を返す。1 行を
/// <c>{名前} {対象} {列名}={値} …</c>（NULL は <c>-</c>）へ写し、行を序数順に並べる（決定的）。
/// </para>
/// <para>
/// 探針のコメントには何を読むかと、探針を使う行の Id の例を書く（移す元の統合テストとの対応は P3-1b の対応表が持つ）。
/// </para>
/// </remarks>
internal static partial class SyncPlanCatalogProbes
{
    /// <summary>方言 → 探針の名前 → SQL（方言ごとの探針は <c>SyncPlanCatalogProbes.{方言}.cs</c> に置く）</summary>
    private static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > Probes { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [DialectNames.SqlServer] = SqlServerProbes,
            [DialectNames.PostgreSql] = PostgreSqlProbes,
            [DialectNames.MySql] = MySqlProbes,
            [DialectNames.Oracle] = OracleProbes,
            [DialectNames.Sqlite] = SqliteProbes,
        };

    /// <summary>探針の名前がその方言にあるか</summary>
    public static bool Has(string dialect, string name) =>
        Probes.TryGetValue(dialect, out var probes) && probes.ContainsKey(name);

    /// <summary>探針を流して行を返す</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(
        SyncPlanFuzzDialect dialect,
        string name,
        CancellationToken ct
    )
    {
        if (
            !Probes.TryGetValue(dialect.Name, out var probes)
            || !probes.TryGetValue(name, out var sql)
        )
        {
            throw new ArgumentException($"[{dialect.Name}] 探針 {name} が無い", nameof(name));
        }

        return
        [
            .. (await ReadAsync(dialect.CreateConnection(), sql, ct).ConfigureAwait(false)).Select(
                l => $"{name} {l}"
            ),
        ];
    }

    /// <summary>行が持つ問い合わせを流して行を返す（<paramref name="admin"/> なら管理者の接続。失敗したら <c>error {型}: {1 行目}</c> の 1 行）</summary>
    public static async Task<IReadOnlyList<string>> QueryAsync(
        SyncPlanFuzzDialect dialect,
        string sql,
        CancellationToken ct,
        bool admin = false
    )
    {
        try
        {
            return await ReadAsync(
                    admin ? dialect.CreateAdminConnection() : dialect.CreateQueryConnection(),
                    sql,
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            return
            [
                $"error {ex.GetType().Name}: {ex.Message.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim()}",
            ];
        }
    }

    private static async Task<List<string>> ReadAsync(
        DbConnection created,
        string sql,
        CancellationToken ct
    )
    {
        var lines = new List<string>();
        await using var connection = created;
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        do
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var parts = new List<string> { Text(reader.GetValue(0)) };

                for (var i = 1; i < reader.FieldCount; i++)
                {
                    parts.Add($"{reader.GetName(i)}={Text(reader.GetValue(i))}");
                }

                lines.Add(string.Join(" ", parts));
            }
        } while (await reader.NextResultAsync(ct).ConfigureAwait(false));

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static string Text(object value) =>
        value is DBNull ? "-" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
}
