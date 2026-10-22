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
