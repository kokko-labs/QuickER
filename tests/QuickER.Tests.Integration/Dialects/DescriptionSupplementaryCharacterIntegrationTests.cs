using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Tests.Integration.Dialects.SyncPlanFuzz;

namespace QuickER.Tests.Integration.Dialects;

/// <summary>
/// 説明の BMP の外の文字（絵文字など）を、5 方言の実 DB が保存する形と、差分・同期の計画・DDL が方言の宣言
/// （<see cref="IdentifierLengthLimit.RepresentsSupplementaryCharacters"/>）どおりに扱うことを検証する統合テストの共通基底
/// </summary>
/// <remarks>
/// MySQL は説明の BMP の外の文字を断らずに 1 文字ずつ <c>?</c> へ置き換えて保存する。差分は置き換えた形
/// （<see cref="IdentifierLengthLimit.StoredText"/>）で比べるので、DDL を適用した後も同期した後も偽の差分は出ず、
/// 同期の計画は警告し、DDL は注記する。ほかの方言はそのまま保存するので、置き換えも警告も注記も無い
/// （宣言が実 DB と食い違えば、取り込み直した説明か差分の表明が赤になる）。単体の対応は <c>DescriptionStoredTextTests</c>。
/// </remarks>
[Trait("Category", "Integration")]
public abstract class DescriptionSupplementaryCharacterIntegrationTestsBase
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    /// <summary>絵文字（サロゲート対）を含む説明</summary>
    private const string Emoji = "a😀b😀😀c";

    /// <summary>BMP の中だけの説明（対照）</summary>
    private const string Bmp = "aébあc";

    /// <summary>DB が使えないときの理由（使えるなら null）</summary>
    protected abstract string? UnavailableReason { get; }

    /// <summary>方言の部品</summary>
    internal abstract SyncPlanFuzzDialect Dialect { get; }

    /// <summary>
    /// 説明の図を DDL で作って取り込み直すと、説明は方言が保存する形になり、元の図との差分が出ないことと、
    /// DDL の注記が宣言どおりであることを検証する
    /// </summary>
    [Theory(
        DisplayName = "[Integration] 説明の BMP の外の文字: DDL を適用して取り込み直しても差分が出ず、注記は宣言どおり"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DdlRoundTrip_HasNoDiff(bool withEmoji)
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        var description = withEmoji ? Emoji : Bmp;
        await Dialect.ResetAsync(Ct);
        var diagram = new ErDiagram { TargetDbms = Dialect.Provider.Name };
        diagram.Entities.Add(Table(description));
        var ddl = Dialect.Provider.DdlGenerator.Build(diagram);
        await Dialect.ExecuteSetupAsync(ddl, Ct);

        var live = await ImportAsync();

        Diff(live, Table(description)).Should().BeEmpty();
        ShouldHaveStoredDescriptions(live, description);
        var folds =
            withEmoji
            && !IdentifierLengthLimits
                .ForDbms(Dialect.Provider.Name)
                .RepresentsSupplementaryCharacters;
        ddl.Split('\n')
            .Count(l => l.Contains("Basic Multilingual Plane", StringComparison.Ordinal))
            .Should()
            .Be(folds ? 2 : 0, ddl);
    }

    /// <summary>
    /// 説明の無い DB へ説明を足す同期は、保存できない方言でだけ警告し、成功した直後に取り込み直しても差分が出ないことを
    /// 検証する
    /// </summary>
    [Theory(
        DisplayName = "[Integration] 説明の BMP の外の文字: 説明を足す同期は宣言どおりに警告し、直後に偽の差分が出ない"
    )]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncAddingDescription_WarnsAndLeavesNoDiff(bool withEmoji)
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? string.Empty);
        var description = withEmoji ? Emoji : Bmp;
        await Dialect.ResetAsync(Ct);
        var diagram = new ErDiagram { TargetDbms = Dialect.Provider.Name };
        diagram.Entities.Add(Table(string.Empty));
        await Dialect.ExecuteSetupAsync(Dialect.Provider.DdlGenerator.Build(diagram), Ct);
        var live = await ImportAsync();
        var capabilities = Dialect.Provider.SyncCapabilities;
        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            [Table(description)],
            [],
            capabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = item.IsSelectable;
        }

        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            capabilities,
            new SyncPlanContext
            {
                LiveEntities = live.Entities,
                LiveRelationships = live.Relationships,
                AuxiliaryObjects = live.AuxiliaryObjects,
                TableCreateSql = live.TableCreateSql,
            }
        );
        var script = Dialect.Provider.SyncScriptBuilder.Build(plan);
        var result = await Dialect.Provider.SyncExecutor.ExecuteAsync(Dialect.Settings, script, Ct);

        result.Committed.Should().BeTrue($"{result.Error}\nSQL:\n{script}");
        var folds =
            withEmoji
            && capabilities.SupportsDescriptions
            && !capabilities.IdentifierLengthLimit.RepresentsSupplementaryCharacters;
        plan.Warnings.Where(w => w.Kind == SyncPlanWarningKind.DescriptionCharactersReplaced)
            .Select(w => w.Detail)
            .Should()
            .Equal(folds ? ["t", "t.v"] : Array.Empty<string>());

        var after = await ImportAsync();
        ShouldHaveStoredDescriptions(after, description);
        Diff(after, Table(description)).Should().BeEmpty();
    }

    /// <summary>
    /// 取り込んだ説明が、方言が保存する形（<see cref="IdentifierLengthLimit.StoredText"/>）と一致すること
    /// （説明の機構が無い方言では空）を確かめる。MySQL ではこれが置き換えの規則の実測になる。
    /// </summary>
    private void ShouldHaveStoredDescriptions(SchemaImportResult schema, string description)
    {
        var capabilities = Dialect.Provider.SyncCapabilities;
        var expected = capabilities.SupportsDescriptions
            ? capabilities.IdentifierLengthLimit.StoredText(description)
            : string.Empty;
        var table = schema.Entities.Single();

        DescriptionText.Normalize(table.Description).Should().Be(expected);
        DescriptionText
            .Normalize(
                table
                    .Columns.Single(c => c.Name.Equals("v", StringComparison.OrdinalIgnoreCase))
                    .Description
            )
            .Should()
            .Be(expected);
    }

    /// <summary>表 t (id PK, v) に説明を付けたもの。型は方言の型名で書く（取り込み直した綴りと比べるため）</summary>
    private Entity Table(string description)
    {
        var type = Dialect.TypeName(FuzzTypeKind.Int, 0);
        return new Entity
        {
            TableName = "t",
            Description = description,
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = type,
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "v",
                    DataType = type,
                    IsNullable = true,
                    Description = description,
                },
            },
        };
    }

    private string[] Diff(SchemaImportResult live, Entity target) =>
        [
            .. new SchemaDiffService()
                .Compute(
                    live.Entities,
                    live.Relationships,
                    [target],
                    [],
                    Dialect.Provider.SyncCapabilities
                )
                .Items.Where(i => i.IsSelectable)
                .Select(i => i.Description),
        ];

    private Task<SchemaImportResult> ImportAsync() =>
        Dialect.Provider.SchemaImporter.ImportAsync(
            Dialect.ImportConnectionString,
            DbCommands.DefaultTimeoutSeconds,
            Ct
        );
}

/// <summary>SQLite（実ファイル・Docker 不要。説明を持たない方言）</summary>
public sealed class SqliteDescriptionSupplementaryCharacterIntegrationTests
    : DescriptionSupplementaryCharacterIntegrationTestsBase,
        IDisposable
{
    private readonly SqliteSyncPlanFuzzDialect _dialect = new();

    protected override string? UnavailableReason => null;

    internal override SyncPlanFuzzDialect Dialect => _dialect;

    public void Dispose() => _dialect.Dispose();
}

/// <summary>SQL Server（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerDescriptionSupplementaryCharacterIntegrationTests(
    SqlServerContainerFixture fixture
) : DescriptionSupplementaryCharacterIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new SqlServerSyncPlanFuzzDialect(fixture);
}

/// <summary>PostgreSQL（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(PostgreSqlContainerCollection.Name)]
public sealed class PostgreSqlDescriptionSupplementaryCharacterIntegrationTests(
    PostgreSqlContainerFixture fixture
) : DescriptionSupplementaryCharacterIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } =
        new PostgreSqlSyncPlanFuzzDialect(fixture);
}

/// <summary>MySQL（Testcontainers。説明の BMP の外の文字を ? へ置き換える方言）</summary>
[Trait("RequiresDocker", "true")]
[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlDescriptionSupplementaryCharacterIntegrationTests(
    MySqlContainerFixture fixture
) : DescriptionSupplementaryCharacterIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new MySqlSyncPlanFuzzDialect(fixture);
}

/// <summary>Oracle（Testcontainers）</summary>
[Trait("RequiresDocker", "true")]
[Collection(OracleContainerCollection.Name)]
public sealed class OracleDescriptionSupplementaryCharacterIntegrationTests(
    OracleContainerFixture fixture
) : DescriptionSupplementaryCharacterIntegrationTestsBase
{
    protected override string? UnavailableReason =>
        fixture.IsAvailable ? null : fixture.UnavailableReason;

    internal override SyncPlanFuzzDialect Dialect { get; } = new OracleSyncPlanFuzzDialect(fixture);
}
