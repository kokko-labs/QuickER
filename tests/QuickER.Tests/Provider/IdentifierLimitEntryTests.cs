using AwesomeAssertions;
using QuickER.Model;
using QuickER.Provider;
using QuickER.Provider.MySql;
using QuickER.Provider.Oracle;
using QuickER.Provider.PostgreSql;
using QuickER.Provider.Sqlite;
using QuickER.Provider.SqlServer;

namespace QuickER.Tests.Provider;

/// <summary>
/// 利用者が付けた名前の種類 × 5 方言の表で、方言の上限を超える名前と方言が表せない文字を含む名前を、
/// DDL 生成と同期スクリプト生成の両方の入口が場所を名指しして断ることを検証するテストクラス（漏れなくのガード 2）
/// </summary>
/// <remarks>
/// <para>
/// 入口の検査（<see cref="SqlNameText"/>）は名前を置き場所ごとに拾い直すので、名前の種類を 1 つ拾い忘れると、その種類の名前だけが
/// 長すぎても DDL・同期スクリプトへ出て、SQL Server・MySQL・Oracle では適用で断られ、PostgreSQL では黙って切り詰められる。
/// 種類ごとに 1 行ずつ、両方の入口で断られることを表明する。上限ちょうどの名前が通ることは
/// <c>OutputIdentifierLimitGuardTests</c>（出力に現れる識別子がすべて上限以内）が確かめる。
/// </para>
/// <para>
/// 期待値は Model の宣言から計算せずに書き下す（方言ごとの上限と表せない文字は、実 DB と照合した P4-1 の値）。
/// </para>
/// </remarks>
public class IdentifierLimitEntryTests
{
    /// <summary>名前を置く場所</summary>
    public enum Kind
    {
        /// <summary>テーブル名</summary>
        Table,

        /// <summary>スキーマ修飾のスキーマの部分</summary>
        SchemaPart,

        /// <summary>列名</summary>
        Column,

        /// <summary>一意制約の名前</summary>
        UniqueConstraint,

        /// <summary>外部キーの名前</summary>
        ForeignKey,

        /// <summary>MySQL の表の名前のファイル名での長さ（51 文字の漢字＝255 バイト）</summary>
        MySqlTableFileName,

        /// <summary>表せない文字（BMP の外の文字＝サロゲート対）を含むテーブル名</summary>
        UnrepresentableTable,

        /// <summary>表せない文字を含む列名</summary>
        UnrepresentableColumn,

        /// <summary>表せない文字を含む一意制約の名前</summary>
        UnrepresentableUniqueConstraint,

        /// <summary>表せない文字を含む外部キーの名前</summary>
        UnrepresentableForeignKey,
    }

    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    private const string Emoji = "😀";

    /// <summary>（方言, 種類, 断られるか）の表</summary>
    public static TheoryData<string, Kind, bool> Rows()
    {
        var rows = new TheoryData<string, Kind, bool>();

        foreach (var dbms in new[] { "sqlserver", "postgresql", "mysql", "oracle", "sqlite" })
        {
            var limited = dbms != "sqlite";
            var isMySql = dbms == "mysql";

            rows.Add(dbms, Kind.Table, limited);
            rows.Add(dbms, Kind.SchemaPart, limited);
            rows.Add(dbms, Kind.Column, limited);
            rows.Add(dbms, Kind.UniqueConstraint, limited);
            rows.Add(dbms, Kind.ForeignKey, limited);
            // 51 文字の漢字は MySQL では 64 文字以内だがファイル名で 255 バイト。PostgreSQL（63 バイト）と Oracle（128 バイト）では
            // バイト数でも超える。SQL Server（128 の UTF-16 コード単位）と SQLite は受け付ける
            rows.Add(dbms, Kind.MySqlTableFileName, dbms is "mysql" or "postgresql" or "oracle");
            rows.Add(dbms, Kind.UnrepresentableTable, isMySql);
            rows.Add(dbms, Kind.UnrepresentableColumn, isMySql);
            rows.Add(dbms, Kind.UnrepresentableUniqueConstraint, isMySql);
            rows.Add(dbms, Kind.UnrepresentableForeignKey, isMySql);
        }

        return rows;
    }

    [Theory(
        DisplayName = "名前の種類ごとに、上限を超える名前・表せない文字を含む名前を DDL と同期の両方の入口が断る"
    )]
    [MemberData(nameof(Rows))]
    public void Entries_RejectExactlyTheDeclaredNames(string dbms, Kind kind, bool rejected)
    {
        var provider = Providers.Single(p => p.Name == dbms);
        var (diagram, name) = Build(provider, kind);
        var located = kind == Kind.SchemaPart ? name + ".c" : name;

        var ddl = () => provider.DdlGenerator.Build(diagram);
        var sync = () => SyncFromEmpty(provider, diagram);

        if (rejected)
        {
            ddl.Should().Throw<InvalidOperationException>().WithMessage($"*'{located}'*");
            sync.Should().Throw<InvalidOperationException>().WithMessage($"*'{located}'*");
        }
        else
        {
            ddl.Should().NotThrow();
            sync.Should().NotThrow();
        }
    }

    /// <summary>空の DB から図を作る同期スクリプト（AddTable・AddForeignKey・一意制約）を組み立てる</summary>
    private static string SyncFromEmpty(IDatabaseProvider provider, ErDiagram diagram)
    {
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

        return provider.SyncScriptBuilder.Build(plan);
    }

    /// <summary>種類の場所に試す名前を置いた図（親 p・子 c・外部キー 1 本・一意制約 1 つ）と、その名前</summary>
    private static (ErDiagram Diagram, string Name) Build(IDatabaseProvider provider, Kind kind)
    {
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var over = new string('a', (limit.IsUnlimited ? 128 : limit.Max) + 1);
        var name = kind switch
        {
            Kind.Table
            or Kind.SchemaPart
            or Kind.Column
            or Kind.UniqueConstraint
            or Kind.ForeignKey => over,
            Kind.MySqlTableFileName => string.Concat(Enumerable.Repeat("表", 51)),
            _ => "x" + Emoji,
        };

        var parent = new Entity
        {
            TableName = "p",
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
            },
        };
        var child = new Entity
        {
            TableName = kind switch
            {
                Kind.Table or Kind.MySqlTableFileName or Kind.UnrepresentableTable => name,
                Kind.SchemaPart => name + ".c",
                _ => "c",
            },
            Columns =
            {
                new Column
                {
                    Name = "id",
                    DataType = "int",
                    IsPrimaryKey = true,
                    IsNullable = false,
                },
                new Column
                {
                    Name = "pid",
                    DataType = "int",
                    IsNullable = false,
                },
                new Column
                {
                    Name = kind is Kind.Column or Kind.UnrepresentableColumn ? name : "code",
                    DataType = "int",
                    IsNullable = false,
                },
            },
        };
        child.UniqueConstraints.Add(
            new UniqueConstraint
            {
                Name = kind is Kind.UniqueConstraint or Kind.UnrepresentableUniqueConstraint
                    ? name
                    : null,
                ColumnIds = { child.Columns[2].Id },
            }
        );

        var diagram = new ErDiagram
        {
            TargetDbms = provider.Name,
            Entities = { parent, child },
            Relationships =
            {
                new Relationship
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ConstraintName = kind is Kind.ForeignKey or Kind.UnrepresentableForeignKey
                        ? name
                        : null,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[1].Id)],
                },
            },
        };

        return (diagram, name);
    }
}
