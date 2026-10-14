using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 表の名前が方言の上限ちょうどで、既定の制約名（<c>PK_{表}</c>・<c>FK_{子}_{親}</c>・<c>UQ_{表}_{列}</c>）が上限を超える図の DDL が、
/// 実 DB に適用でき、取り込み直しても差分が出ないことを 4 方言で検証する統合テストの共通基底
/// </summary>
/// <remarks>
/// 既定名は <see cref="IdentifierLengthLimit.Fit"/> で方言の上限に収める（縮めずに出すと SQL Server・MySQL・Oracle は DDL の
/// 適用で断り、PostgreSQL は黙って切り詰めて取り込み直すと別の名前になる）。単体の対応は <c>LongDefaultConstraintNameTests</c>。
/// </remarks>
public abstract class LongDefaultConstraintNameIntegrationTestsBase
{
    /// <summary>テストのキャンセル</summary>
    protected static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>方言のプロバイダ</summary>
    protected abstract IDatabaseProvider Provider { get; }

    /// <summary>DB が使えるか</summary>
    protected abstract bool IsAvailable { get; }

    /// <summary>使えないときの理由</summary>
    protected abstract string UnavailableReason { get; }

    /// <summary>DB を空にする</summary>
    protected abstract Task ResetAsync(CancellationToken ct);

    /// <summary>SQL を適用する</summary>
    protected abstract Task ApplyAsync(string sql, CancellationToken ct);

    /// <summary>接続文字列</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>取り込む</summary>
    protected Task<SchemaImportResult> ImportAsync() =>
        Provider.SchemaImporter.ImportAsync(ConnectionString, DbCommands.DefaultTimeoutSeconds, Ct);

    [Fact(
        DisplayName = "[Integration] 既定名が上限を超える図の DDL は適用でき、制約名は上限以内で、取り込み直すと差分が出ない"
    )]
    public async Task LongDefaultNames_ApplyAndRoundTripWithoutDiff()
    {
        Assert.SkipUnless(IsAvailable, UnavailableReason);
        await ResetAsync(Ct);
        var limit = Provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.Build(limit, Provider.Name);

        await ApplyAsync(Provider.DdlGenerator.Build(diagram), Ct);
        var live = await ImportAsync();

        var expectedForeignKeys = ConstraintNames
            .ResolveForeignKeyNames(diagram.Entities, diagram.Relationships, limit)
            .Values;
        live.Relationships.Select(r => r.ConstraintName)
            .Should()
            .BeEquivalentTo(expectedForeignKeys, "DB の外部キーは縮めた既定名のまま");
        live.Relationships.Should()
            .AllSatisfy(r => limit.Fits(r.ConstraintName!).Should().BeTrue(r.ConstraintName));

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            Provider.SyncCapabilities
        );

        diff.Items.Select(item => item.Description).Should().BeEmpty();
        await ResetAsync(Ct);
    }
}

/// <summary>SQL Server（Testcontainers）</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerLongDefaultConstraintNameIntegrationTests(
    SqlServerContainerFixture fixture
) : LongDefaultConstraintNameIntegrationTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new SqlServerProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override string ConnectionString => fixture.ConnectionString;
}

/// <summary>PostgreSQL（Testcontainers）。旧版が作った外部キー（PostgreSQL が切り詰めた既定名）の吸収も検証する</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlLongDefaultConstraintNameIntegrationTests(
    PostgreSqlContainerFixture fixture
) : LongDefaultConstraintNameIntegrationTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new PostgreSqlProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override string ConnectionString => fixture.ConnectionString;

    /// <summary>
    /// 旧版の QuickER が書いた DDL（縮めない長い既定名）で作った DB を取り込み直しても、外部キーの差分が出ないことを検証する
    /// </summary>
    /// <remarks>
    /// 旧版は <c>FK_{子}_{親}</c> をそのまま DDL へ書き、PostgreSQL が 63 バイトで黙って切り詰めて保存した。差分は名前の無い
    /// リレーションに限り、live の名前が「縮める前の既定名を切ったもの」なら同じ外部キーとみなす
    /// （<see cref="SyncDialectCapabilities.TruncatesLongIdentifiers"/>）。吸収を外すと追加と削除の 2 項目になる（対照も実 DB で確かめる）。
    /// </remarks>
    [Fact(
        DisplayName = "[Integration] PostgreSQL: 旧版の DDL で作った長い既定名の外部キー（PG が切り詰めた名前）は、取り込み直しても差分が出ない"
    )]
    public async Task LegacyTruncatedForeignKeyName_IsAbsorbed()
    {
        Assert.SkipUnless(IsAvailable, UnavailableReason);
        await ResetAsync(Ct);
        var limit = Provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.BuildSingleForeignKey(40, Provider.Name);
        var parent = diagram.Entities[0].TableName;
        var child = diagram.Entities[1].TableName;
        var legacyName = "FK_" + child + "_" + parent;

        // 旧版の DDL と同じ形（外部キーの既定名を縮めずに書く）
        await ApplyAsync(
            $"""
            CREATE TABLE {PgIdentifier.Quote(
                parent
            )} (id integer NOT NULL, CONSTRAINT "PK_{parent}" PRIMARY KEY (id));
            CREATE TABLE {PgIdentifier.Quote(
                child
            )} (id integer NOT NULL, parent_id integer NOT NULL, CONSTRAINT "PK_{child}" PRIMARY KEY (id));
            ALTER TABLE {PgIdentifier.Quote(
                child
            )} ADD CONSTRAINT "{legacyName}" FOREIGN KEY (parent_id) REFERENCES {PgIdentifier.Quote(
                parent
            )} (id);
            """,
            Ct
        );
        var live = await ImportAsync();

        live.Relationships.Single()
            .ConstraintName.Should()
            .Be(limit.Truncate(legacyName), "PostgreSQL は 63 バイトで切り詰める（吸収の前提）");
        live.Relationships.Single()
            .ConstraintName.Should()
            .NotBe(ConstraintNames.ForeignKey(child, parent, limit), "新しい版の既定名とは違う");

        var absorbed = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            Provider.SyncCapabilities
        );
        var notAbsorbed = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            Provider.SyncCapabilities with
            {
                TruncatesLongIdentifiers = false,
            }
        );

        absorbed.Items.Select(item => item.Description).Should().BeEmpty();
        notAbsorbed
            .Items.Select(item => item.Kind)
            .Should()
            .Contain(SchemaDiffKind.AddForeignKey, "吸収しないと別の外部キーに見える（対照）");
        await ResetAsync(Ct);
    }
}

/// <summary>MySQL（Testcontainers）</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlLongDefaultConstraintNameIntegrationTests(MySqlContainerFixture fixture)
    : LongDefaultConstraintNameIntegrationTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new MySqlProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override string ConnectionString => fixture.ConnectionString;
}

/// <summary>Oracle（Testcontainers）</summary>
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleLongDefaultConstraintNameIntegrationTests(OracleContainerFixture fixture)
    : LongDefaultConstraintNameIntegrationTestsBase
{
    protected override IDatabaseProvider Provider { get; } = new OracleProvider();

    protected override bool IsAvailable => fixture.IsAvailable;

    protected override string UnavailableReason => fixture.UnavailableReason;

    protected override Task ResetAsync(CancellationToken ct) => fixture.ResetSchemaAsync(ct);

    protected override Task ApplyAsync(string sql, CancellationToken ct) =>
        fixture.ExecuteAsync(sql, ct);

    protected override string ConnectionString => fixture.ConnectionString;
}
