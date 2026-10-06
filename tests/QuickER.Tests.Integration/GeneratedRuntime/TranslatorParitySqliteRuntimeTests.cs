using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using QuickER.Tests.GeneratedTranslatorParityFixture;
using QuickER.Tests.GeneratedTranslatorParityFixture.Repositories.Sqlite;
using Xunit;

namespace QuickER.Tests.Integration.GeneratedRuntime;

/// <summary>
/// <see cref="TranslatorParityRuntimeTestsBase"/> の共通シナリオを、実ファイルの SQLite の
/// QuickER 版 Repository へ流す派生（Docker 不要＝CI で常時実行）。
/// </summary>
[Trait("Category", "Integration")]
public sealed class TranslatorParitySqliteRuntimeTests
    : TranslatorParityRuntimeTestsBase,
        IDisposable
{
    private readonly SqliteTempDatabase _db = SqliteTempDatabase.Create();

    protected override async Task<ServiceProvider> CreateProviderAsync()
    {
        await _db.ResetSchemaAsync(Ct);
        await _db.ApplyDdlAsync(TranslatorParityFixtureDefinition.Build(), Ct);

        return new ServiceCollection()
            .AddGeneratedSqliteRepositories(_db.ReadWriteCreateConnectionString)
            .BuildServiceProvider();
    }

    /// <summary>
    /// 値オブジェクトなしの SQLite で、格納値の文字列が型の形式に合わないとき（Guid の列に Guid でない文字列）、
    /// 生 SQL のエンティティ取得は素の FormatException でなく、SQLite の文言の InvalidOperationException
    /// （内側が FormatException）で失敗する。CoerceScalar が変換を RawValueConverter へ委ね、失敗を包むため
    /// </summary>
    [Fact(
        DisplayName = "[SQLite] 生 SQL の取得で形式の合わない格納値は InvalidOperationException（内側 FormatException）になる"
    )]
    public async Task RawSqlEntity_MalformedStoredText_ThrowsInvalidOperationWithFormatInner()
    {
        var executor = new SqlExecutor(
            new SqlConnectionFactory(_db.ReadWriteCreateConnectionString)
        );
        await executor.ExecuteSqlAsync(
            "CREATE TABLE \"guid_probes\" (\"probe_id\" INTEGER NOT NULL PRIMARY KEY, \"session_id\" TEXT NOT NULL);",
            cancellationToken: Ct
        );
        await executor.ExecuteSqlAsync(
            "INSERT INTO \"guid_probes\" VALUES (1, 'not-a-guid');",
            cancellationToken: Ct
        );

        var read = async () =>
            await executor.QueryBySqlAsync<GuidProbeEntity>(
                "SELECT \"probe_id\", \"session_id\" FROM \"guid_probes\";",
                cancellationToken: Ct
            );

        var failure = (await read.Should().ThrowExactlyAsync<InvalidOperationException>()).Which;
        failure.Message.Should().Contain("SQLite").And.Contain(nameof(Guid));
        failure.InnerException.Should().BeOfType<FormatException>();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>手書きの、Guid の列を素の CLR 型で受けるエンティティ</summary>
    [System.ComponentModel.DataAnnotations.Schema.Table("guid_probes")]
    public sealed class GuidProbeEntity : EntityBase
    {
        /// <summary>probe_id 列</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [System.ComponentModel.DataAnnotations.Schema.Column("probe_id")]
        public int ProbeId { get; set; }

        /// <summary>session_id 列</summary>
        [System.ComponentModel.DataAnnotations.Schema.Column("session_id")]
        public Guid SessionId { get; set; }
    }
}
