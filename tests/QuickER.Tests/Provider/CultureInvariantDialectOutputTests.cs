using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;
using QuickER.Tests.GeneratedFixture;
using QuickER.Tests.GeneratedPortableFixture;
using QuickER.Tests.GeneratedSyncFixture;
using QuickER.Tests.TestSupport;

namespace QuickER.Tests.Provider;

/// <summary>
/// DDL・同期スクリプト・型の変換と正規化の出力が、書式のカルチャ（<see cref="CultureScope.Cultures"/>）に依らず
/// インバリアントで作ったものと一致することを、5 方言の総当たりで確かめる
/// </summary>
/// <remarks>
/// 図は固定フィクスチャの定義（SQL Server の型の全カバレッジ・可搬・同期）に、方言ごとの型の選択肢と負のスケールなどの
/// 表記を載せた図を足したもの。DDL の先頭の生成日時は実行の時刻で変わるので、数字だけを伏せて形を比べ、暦（西暦）は
/// <see cref="DdlHeaderTimestamp_IsGregorianInEveryCulture"/> が別に確かめる。
/// </remarks>
public sealed partial class CultureInvariantDialectOutputTests
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

    [Fact(DisplayName = "DDL は 5 方言とも、書式のカルチャに依らずインバリアントと同じ")]
    public void Ddl_IsCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var provider in Providers)
        {
            foreach (var (name, diagram) in Diagrams(provider))
            {
                differences.AddRange(
                    CultureScope
                        .FindDifferences(() => MaskTimestamp(provider.DdlGenerator.Build(diagram)))
                        .Select(d => $"{provider.Name} {name}: {d}")
                );
            }
        }

        differences.Should().BeEmpty();
    }

    [Fact(DisplayName = "同期スクリプトは 5 方言とも、書式のカルチャに依らずインバリアントと同じ")]
    public void SyncScript_IsCultureInvariant()
    {
        var differences = new List<string>();

        foreach (var provider in Providers)
        {
            foreach (var (name, diagram) in Diagrams(provider))
            {
                differences.AddRange(
                    CultureScope
                        .FindDifferences(() => BuildSyncScripts(provider, diagram))
                        .Select(d => $"{provider.Name} {name}: {d}")
                );
            }
        }

        differences.Should().BeEmpty();
    }

    [Fact(
        DisplayName = "型の解析・方言の間の変換・表記の正規化・型トークンは、書式のカルチャに依らずインバリアントと同じ"
    )]
    public void TypeConversion_IsCultureInvariant()
    {
        CultureScope.FindDifferences(ConvertAllTypes).Should().BeEmpty();
    }

    [Fact(DisplayName = "DDL の先頭の生成日時は、どのカルチャでも西暦の yyyy-MM-dd HH:mm:ss")]
    public void DdlHeaderTimestamp_IsGregorianInEveryCulture()
    {
        var failures = new List<string>();

        foreach (var name in CultureScope.Cultures)
        {
            using var scope = new CultureScope(name);

            foreach (var provider in Providers)
            {
                var ddl = provider.DdlGenerator.Build(GeneratedFixtureDefinition.Build());
                var match = TimestampLine().Match(ddl);

                if (!match.Success)
                {
                    failures.Add(
                        $"{name} {provider.Name}: 生成日時の行が yyyy-MM-dd HH:mm:ss でない"
                    );
                    continue;
                }

                var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);

                if (Math.Abs(year - DateTime.Now.Year) > 1)
                {
                    failures.Add($"{name} {provider.Name}: 年が西暦でない（{year}）");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    /// <summary>方言ごとに流す図</summary>
    private static IEnumerable<(string Name, ErDiagram Diagram)> Diagrams(
        IDatabaseProvider provider
    )
    {
        yield return ("GeneratedFixture", WithTarget(GeneratedFixtureDefinition.Build(), provider));
        yield return ("PortableFixture", WithTarget(PortableFixtureDefinition.Build(), provider));
        yield return ("SyncFixture", WithTarget(SyncFixtureDefinition.Build(), provider));
        yield return ("TypeChoices", TypeChoicesDiagram(provider));
    }

    /// <summary>図の対象 DBMS を方言に合わせる</summary>
    private static ErDiagram WithTarget(ErDiagram diagram, IDatabaseProvider provider)
    {
        diagram.TargetDbms = provider.Name;
        return diagram;
    }

    /// <summary>方言の型の選択肢と追加の表記をすべて列に持つ図（説明・一意制約・外部キーつき）</summary>
    private static ErDiagram TypeChoicesDiagram(IDatabaseProvider provider)
    {
        var parent = new Entity { TableName = "Items", Description = "Items İ ı I i" };
        var id = new Column
        {
            Name = "ID",
            DataType = provider.TypeCatalog.DefaultDataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        parent.Columns.Add(id);

        foreach (
            var (type, index) in provider
                .TypeCatalog.DataTypes.Concat(ExtraTypeNotations)
                .Select((type, index) => (type, index))
        )
        {
            parent.Columns.Add(
                new Column
                {
                    Name = "Column" + index.ToString(CultureInfo.InvariantCulture),
                    DataType = type,
                    IsNullable = index % 2 == 0,
                    Description = "Description " + index.ToString(CultureInfo.InvariantCulture),
                }
            );
        }

        parent.UniqueConstraints.Add(
            new UniqueConstraint { ColumnIds = [parent.Columns[1].Id, parent.Columns[2].Id] }
        );
        var child = new Entity { TableName = "ItemLines" };
        var childId = new Column
        {
            Name = "LineId",
            DataType = provider.TypeCatalog.DefaultDataType,
            IsPrimaryKey = true,
            IsNullable = false,
        };
        var childFk = new Column
        {
            Name = "ItemId",
            DataType = provider.TypeCatalog.DefaultDataType,
            IsNullable = false,
        };
        child.Columns.Add(childId);
        child.Columns.Add(childFk);

        return new ErDiagram
        {
            TargetDbms = provider.Name,
            Entities = [parent, child],
            Relationships =
            [
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    Type = RelationshipType.OneToMany,
                    ColumnPairs = [new(id.Id, childFk.Id)],
                    OnDelete = ForeignKeyReferentialAction.Cascade,
                },
            ],
        };
    }

    /// <summary>空の DB から図を作る同期と、図の全列の型を方言の既定へ変える同期のスクリプト</summary>
    private static string BuildSyncScripts(IDatabaseProvider provider, ErDiagram diagram)
    {
        var builder = new StringBuilder();
        var capabilities = provider.SyncCapabilities;

        // 1. 空の DB から図を作る
        var create = new SchemaDiffService()
            .Compute([], [], diagram.Entities, diagram.Relationships, capabilities)
            .Items;
        builder.AppendLine(Render(provider, create, [], []));

        // 2. 列の型をすべて方言の既定へ変える（図の型を live、既定を target として差分を取る）
        var target = diagram.Entities.Select(e => e.Clone(preserveId: true)).ToList();

        foreach (var column in target.SelectMany(e => e.Columns).Where(c => !c.IsPrimaryKey))
        {
            column.DataType = provider.TypeCatalog.DefaultDataType;
        }

        var alter = new SchemaDiffService()
            .Compute(
                diagram.Entities,
                diagram.Relationships,
                target,
                diagram.Relationships,
                capabilities
            )
            .Items;
        builder.AppendLine(Render(provider, alter, diagram.Entities, diagram.Relationships));

        return builder.ToString();
    }

    /// <summary>差分の全項目を選んで計画を組み、スクリプトにする</summary>
    private static string Render(
        IDatabaseProvider provider,
        IReadOnlyList<SchemaDiffItem> items,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships
    )
    {
        foreach (var item in items)
        {
            item.IsSelected = true;
        }

        var plan = new SyncPlanner().BuildPlan(
            items,
            provider.SyncCapabilities,
            new SyncPlanContext
            {
                LiveEntities = liveEntities,
                LiveRelationships = liveRelationships,
            }
        );

        return provider.SyncScriptBuilder.Build(plan)
            + string.Join("\n", plan.Warnings.Select(w => $"{w.Kind}|{w.TableName}|{w.Detail}"));
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

    /// <summary>DDL の先頭の生成日時の数字を伏せる（形は残す）</summary>
    private static string MaskTimestamp(string ddl) =>
        TimestampLine().Replace(ddl, match => Regex.Replace(match.Value, "[0-9]", "0"));

    [GeneratedRegex(
        @"-- Generated at: (?<year>[0-9]{4})-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex TimestampLine();
}
