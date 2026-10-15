using AwesomeAssertions;
using QuickER.Dialects;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// テーブル名が長く、QuickER が付ける既定の制約名（<c>PK_{表}</c>・<c>FK_{子}_{親}</c>・<c>UQ_{表}_{列}</c>）が方言の上限を超える図で、
/// DDL・同期スクリプト・同期の差分が同じ縮めた名前を使うことを 5 方言で検証するテストクラス
/// </summary>
/// <remarks>
/// <para>
/// 既定名は <see cref="IdentifierLengthLimit.Fit"/> で上限に収める。DDL・同期スクリプト・差分のどれか 1 つだけが
/// 縮めずに名前を作ると、同期が DDL で作った制約を別物とみなして追加と削除を出し続ける（外部キー）か、
/// DDL の適用そのものが上限超過で失敗する。上限の経路（方言の上限を渡す）の正本は <see cref="ConstraintNames"/> の XmlDoc。
/// </para>
/// <para>
/// PostgreSQL は旧版の QuickER が長い既定名で作った外部キー（PostgreSQL が 63 バイトで切り詰めた名前）を
/// 同じ外部キーとみなす（<see cref="SyncDialectCapabilities.TruncatesLongIdentifiers"/>）。実 DB では
/// Integration の <c>LongDefaultConstraintNameIntegrationTests</c> が固定する。
/// </para>
/// </remarks>
public class LongDefaultConstraintNameTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    /// <summary>上限のある 4 方言</summary>
    public static TheoryData<string> LimitedDialects =>
        ["sqlserver", "postgresql", "mysql", "oracle"];

    private static IDatabaseProvider ProviderOf(string dbms) =>
        Providers.Single(provider => provider.Name == dbms);

    /// <summary>図の既定名（主キー 2・一意制約 1・外部キー 2）と、縮める前の名前</summary>
    private static List<(string Name, string Unshortened)> ExpectedNames(
        ErDiagram diagram,
        IdentifierLengthLimit limit
    )
    {
        var parent = diagram.Entities[0];
        var child = diagram.Entities[1];
        var unlimited = IdentifierLengthLimit.Unlimited;
        var names = new List<(string, string)>
        {
            (
                ConstraintNames.PrimaryKey(parent.TableName, limit),
                ConstraintNames.PrimaryKey(parent.TableName, unlimited)
            ),
            (
                ConstraintNames.PrimaryKey(child.TableName, limit),
                ConstraintNames.PrimaryKey(child.TableName, unlimited)
            ),
            (
                UniqueConstraint.SynthesizeName(child.TableName, ["bill_id", "ship_id"], limit),
                UniqueConstraint.SynthesizeName(child.TableName, ["bill_id", "ship_id"], unlimited)
            ),
        };
        var details = ConstraintNames.ResolveForeignKeyNameDetails(
            diagram.Entities,
            diagram.Relationships,
            limit
        );
        names.AddRange(
            diagram.Relationships.Select(r => (details[r].Name, details[r].UnshortenedName))
        );

        return names;
    }

    [Theory(DisplayName = "既定名は上限を超えるときだけ縮み、すべて上限以内で互いに違う（4 方言）")]
    [MemberData(nameof(LimitedDialects))]
    public void DefaultNames_AreShortenedWithinLimitAndDistinct(string dbms)
    {
        var limit = ProviderOf(dbms).SyncCapabilities.IdentifierLengthLimit;
        var names = ExpectedNames(LongNameDiagrams.Build(limit, dbms), limit);

        names.Should().AllSatisfy(pair => limit.Fits(pair.Name).Should().BeTrue(pair.Name));
        names.Should().AllSatisfy(pair => pair.Name.Should().NotBe(pair.Unshortened));
        names.Select(pair => pair.Name).Should().OnlyHaveUniqueItems();
    }

    [Theory(DisplayName = "DDL は縮めた既定名で制約を作る（4 方言）")]
    [MemberData(nameof(LimitedDialects))]
    public void Ddl_UsesShortenedNames(string dbms)
    {
        var provider = ProviderOf(dbms);
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.Build(limit, dbms);

        var ddl = provider.DdlGenerator.Build(diagram);

        foreach (var (name, unshortened) in ExpectedNames(diagram, limit))
        {
            ddl.Should().Contain(name);
            ddl.Should().NotContain(unshortened);
        }
    }

    [Theory(DisplayName = "同期スクリプトは DDL と同じ縮めた既定名で制約を作る（4 方言）")]
    [MemberData(nameof(LimitedDialects))]
    public void Sync_UsesShortenedNames(string dbms)
    {
        var provider = ProviderOf(dbms);
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.Build(limit, dbms);
        var diff = new SchemaDiffService().Compute(
            [],
            [],
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities
        );
        var plan = new SyncPlanner().BuildPlan(
            diff.Items,
            provider.SyncCapabilities,
            new SyncPlanContext()
        );

        var script = provider.SyncScriptBuilder.Build(plan);

        foreach (var (name, unshortened) in ExpectedNames(diagram, limit))
        {
            script.Should().Contain(name);
            script.Should().NotContain(unshortened);
        }
    }

    [Theory(
        DisplayName = "DDL が縮めた名前で作った外部キーを、差分は図の名前の無い外部キーと同じとみなす（4 方言）"
    )]
    [MemberData(nameof(LimitedDialects))]
    public void Diff_MatchesShortenedLiveForeignKeys(string dbms)
    {
        var provider = ProviderOf(dbms);
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.Build(limit, dbms);
        var live = LiveAsImported(diagram, rel => ResolvedName(diagram, rel, limit));

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities
        );

        diff.Items.Should().BeEmpty();
    }

    [Fact(DisplayName = "SQLite は上限が無いので既定名を縮めない")]
    public void Sqlite_DoesNotShorten()
    {
        var provider = ProviderOf("sqlite");
        var diagram = LongNameDiagrams.Build(IdentifierLengthLimit.Unlimited, "sqlite");

        var ddl = provider.DdlGenerator.Build(diagram);

        ddl.Should()
            .Contain(
                ConstraintNames.PrimaryKey(
                    diagram.Entities[1].TableName,
                    IdentifierLengthLimit.Unlimited
                )
            );
        ddl.Should().Contain("PK_" + new string('c', 200) + "\"");
    }

    [Fact(
        DisplayName = "PostgreSQL: 旧版が作った外部キー（縮める前の既定名を 63 バイトで切った名前）は同じ外部キーとみなす"
    )]
    public void PostgreSql_AbsorbsLegacyTruncatedForeignKeyNames()
    {
        var provider = ProviderOf("postgresql");
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.Build(limit, "postgresql");
        var live = LiveAsImported(
            diagram,
            rel => limit.Truncate(UnshortenedName(diagram, rel, limit))
        );

        var absorbed = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities
        );
        var notAbsorbed = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities with
            {
                TruncatesLongIdentifiers = false,
            }
        );

        absorbed.Items.Should().BeEmpty();
        notAbsorbed
            .Items.Select(item => item.Kind)
            .Should()
            .Contain(
                SchemaDiffKind.AddForeignKey,
                "吸収しないと名前が違う別の外部キーに見える（対照）"
            );
    }

    [Fact(DisplayName = "PostgreSQL: 利用者が付けた外部キーの名前は旧版の切り詰めとみなさない")]
    public void PostgreSql_DoesNotAbsorbStoredNames()
    {
        var provider = ProviderOf("postgresql");
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var diagram = LongNameDiagrams.Build(limit, "postgresql");
        var legacy = diagram.Relationships.ToDictionary(
            rel => rel,
            rel => limit.Truncate(UnshortenedName(diagram, rel, limit))
        );
        var live = LiveAsImported(diagram, rel => legacy[rel]);

        // 図の側に、縮めた名前を利用者が付けた名前として保存する（旧版の切り詰めた名前とは違う）
        foreach (var relationship in diagram.Relationships)
        {
            relationship.ConstraintName = ResolvedName(diagram, relationship, limit);
        }

        var diff = new SchemaDiffService().Compute(
            live.Entities,
            live.Relationships,
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities
        );

        diff.Items.Should().NotBeEmpty();
    }

    private static string ResolvedName(
        ErDiagram diagram,
        Relationship rel,
        IdentifierLengthLimit limit
    ) =>
        ConstraintNames
            .ResolveForeignKeyNameDetails(diagram.Entities, diagram.Relationships, limit)[rel]
            .Name;

    private static string UnshortenedName(
        ErDiagram diagram,
        Relationship rel,
        IdentifierLengthLimit limit
    ) =>
        ConstraintNames
            .ResolveForeignKeyNameDetails(diagram.Entities, diagram.Relationships, limit)[rel]
            .UnshortenedName;

    /// <summary>取込が返すのと同じ形の live（同じ表・列、外部キーは DB の名前を持つ）を作る</summary>
    private static ErDiagram LiveAsImported(ErDiagram diagram, Func<Relationship, string> liveName)
    {
        var entities = diagram.Entities.Select(entity => entity.Clone(preserveId: true)).ToList();
        var relationships = diagram
            .Relationships.Select(rel => new Relationship
            {
                SourceEntityId = rel.SourceEntityId,
                TargetEntityId = rel.TargetEntityId,
                Type = rel.Type,
                ColumnPairs = rel.ColumnPairs.ToList(),
                ConstraintName = liveName(rel),
            })
            .ToList();

        // 一意制約は取込で実名を持つ（DDL が付けた合成名）
        foreach (var entity in entities)
        {
            foreach (var constraint in entity.UniqueConstraints)
            {
                var columnNames = constraint.ColumnIds.Select(id =>
                    entity.Columns.Single(column => column.Id == id).Name
                );
                constraint.Name = UniqueConstraint.SynthesizeName(
                    entity.TableName,
                    columnNames,
                    IdentifierLengthLimits.ForDbms(diagram.TargetDbms)
                );
            }
        }

        return new ErDiagram
        {
            TargetDbms = diagram.TargetDbms,
            Entities = entities,
            Relationships = relationships,
        };
    }
}
