using System.Text;
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
/// 入口を通った図から作る DDL・同期スクリプトに現れる識別子が、すべて方言の上限以内で表せる文字だけであることを
/// 5 方言で検証するテストクラス（漏れなくのガード 1）
/// </summary>
/// <remarks>
/// <para>
/// 図は、利用者が付ける名前（テーブル・スキーマ修飾の両方の部分・列・一意制約・外部キー）をすべて上限ちょうどにし、QuickER が付ける
/// 既定名（主キー・外部キー・一意化した外部キー・一意制約の合成名）が上限を超える形にする。スクリプトは DDL・空の DB からの同期・
/// 主キーの変更と外部キー・一意制約の追加を含む同期（SQLite は再構築）の 3 つ。
/// </para>
/// <para>
/// 識別子は、方言の区切り（<c>[]</c> / <c>""</c> / バッククォート）で囲まれた部分をスクリプトの全文から取り出す。動的 SQL の文字列
/// リテラルの中も同じように取り出し、リテラルのための <c>''</c> の二重化は解く。名前の種類を決め打ちせずに全部を見るので、
/// QuickER が新しい種類の識別子（索引名・一時名など）を出すようにしたのに上限へ収め忘れると、ここが赤になる。
/// あわせて、種類ごとの期待する識別子が実際に出力に現れたこと（取り出しが見えていること）を表明する。
/// </para>
/// </remarks>
public class OutputIdentifierLimitGuardTests
{
    private static readonly IDatabaseProvider[] Providers =
    [
        new SqlServerProvider(),
        new PostgreSqlProvider(),
        new MySqlProvider(),
        new OracleProvider(),
        new SqliteProvider(),
    ];

    public static TheoryData<string> Dialects =>
        ["sqlserver", "postgresql", "mysql", "oracle", "sqlite"];

    [Theory(
        DisplayName = "DDL・同期スクリプトに現れる識別子は、すべて方言の上限以内で表せる文字だけ（5 方言）"
    )]
    [MemberData(nameof(Dialects))]
    public void OutputIdentifiers_AllFitTheDialect(string dbms)
    {
        var provider = Providers.Single(p => p.Name == dbms);
        var limit = provider.SyncCapabilities.IdentifierLengthLimit;
        var shape = Shape.Build(provider);

        var scripts = new[]
        {
            provider.DdlGenerator.Build(shape.Diagram),
            Sync(provider, [], [], shape.Diagram, new SyncPlanContext()),
            Sync(
                provider,
                shape.Live.Entities,
                shape.Live.Relationships,
                shape.Diagram,
                new SyncPlanContext
                {
                    LiveEntities = shape.Live.Entities,
                    LiveRelationships = shape.Live.Relationships,
                }
            ),
        };
        var identifiers = scripts.SelectMany(script => Extract(script, dbms)).ToList();

        identifiers
            .Where(identifier => !limit.Accepts(identifier))
            .Should()
            .BeEmpty("出力に上限を超える・表せない文字を含む識別子がある");
        identifiers
            .Should()
            .Contain(shape.Expected, "種類ごとの識別子が出力に現れる（取り出しが見えている）");
    }

    /// <summary>差分 → 計画 → 同期スクリプト（差分の項目はすべて選ぶ＝主キーの変更も含める）</summary>
    private static string Sync(
        IDatabaseProvider provider,
        IReadOnlyList<Entity> liveEntities,
        IReadOnlyList<Relationship> liveRelationships,
        ErDiagram diagram,
        SyncPlanContext context
    )
    {
        var diff = new SchemaDiffService().Compute(
            liveEntities,
            liveRelationships,
            diagram.Entities,
            diagram.Relationships,
            provider.SyncCapabilities
        );

        foreach (var item in diff.Items)
        {
            item.IsSelected = true;
        }

        var plan = new SyncPlanner().BuildPlan(diff.Items, provider.SyncCapabilities, context);

        return provider.SyncScriptBuilder.Build(plan);
    }

    /// <summary>スクリプトの全文から、方言の区切りで囲まれた識別子を取り出す</summary>
    /// <remarks>
    /// 文字列リテラル（<c>'…'</c>）の中の識別子も取り出す（動的 SQL が組み立てる文の識別子）。リテラルの中の <c>''</c> は
    /// <c>'</c> へ解く。リテラルの境目をまたぐ区切り（<c>'… `' , 名前, '` …'</c> のように連結で組み立てる部分）は識別子ではないので捨てる。
    /// リテラルの外の行コメント（<c>--</c>）は読み飛ばす（コメントの中の名前は識別子ではない）。MySQL はリテラルの中の <c>\</c> による
    /// エスケープも読む。
    /// </remarks>
    internal static List<string> Extract(string script, string dbms)
    {
        var (open, close) = dbms switch
        {
            "sqlserver" => ('[', ']'),
            "mysql" => ('`', '`'),
            _ => ('"', '"'),
        };
        var identifiers = new List<string>();
        var inLiteral = false;
        var i = 0;

        while (i < script.Length)
        {
            var c = script[i];

            if (!inLiteral && c == '-' && i + 1 < script.Length && script[i + 1] == '-')
            {
                var end = script.IndexOf('\n', i);
                i = end < 0 ? script.Length : end + 1;
                continue;
            }

            if (inLiteral && dbms == "mysql" && c == '\\')
            {
                i += 2;
                continue;
            }

            if (c == '\'')
            {
                if (inLiteral && i + 1 < script.Length && script[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                inLiteral = !inLiteral;
                i++;
                continue;
            }

            if (c != open)
            {
                i++;
                continue;
            }

            var sb = new StringBuilder();
            var j = i + 1;
            var complete = false;

            while (j < script.Length)
            {
                if (script[j] == '\'' && inLiteral)
                {
                    if (j + 1 < script.Length && script[j + 1] == '\'')
                    {
                        sb.Append('\'');
                        j += 2;
                        continue;
                    }

                    // リテラルが識別子の途中で終わる＝連結で組み立てる部分なので捨てる
                    break;
                }

                if (script[j] == close)
                {
                    if (j + 1 < script.Length && script[j + 1] == close)
                    {
                        sb.Append(close);
                        j += 2;
                        continue;
                    }

                    complete = true;
                    j++;
                    break;
                }

                sb.Append(script[j]);
                j++;
            }

            if (complete)
            {
                identifiers.Add(sb.ToString());
                i = j;
            }
            else
            {
                i++;
            }
        }

        return identifiers;
    }

    /// <summary>上限ちょうどの名前と、上限を超える既定名を持つ図・同期の相手の live・期待する識別子</summary>
    private sealed record Shape(ErDiagram Diagram, ErDiagram Live, List<string> Expected)
    {
        public static Shape Build(IDatabaseProvider provider)
        {
            var limit = provider.SyncCapabilities.IdentifierLengthLimit;
            var length = limit.IsUnlimited ? 200 : limit.Max;
            string Name(char c) => new(c, length);

            Column Int(string name, bool primaryKey = false) =>
                new()
                {
                    Name = name,
                    DataType = "int",
                    IsPrimaryKey = primaryKey,
                    IsNullable = false,
                };

            var parent = new Entity { TableName = Name('p'), Columns = { Int("id", true) } };
            var qualified = new Entity
            {
                TableName = Name('s') + "." + Name('t'),
                Columns = { Int("id", true) },
            };
            var child = new Entity
            {
                TableName = Name('c'),
                Columns =
                {
                    Int("id", true),
                    Int(Name('x')),
                    Int("ship_id"),
                    Int("bill_id"),
                    Int(Name('k')),
                },
            };
            child.UniqueConstraints.Add(
                new UniqueConstraint { Name = Name('u'), ColumnIds = { child.Columns[4].Id } }
            );
            child.UniqueConstraints.Add(
                new UniqueConstraint { ColumnIds = { child.Columns[2].Id, child.Columns[3].Id } }
            );

            Relationship Link(int childColumn, string? name) =>
                new()
                {
                    SourceEntityId = parent.Id,
                    TargetEntityId = child.Id,
                    ConstraintName = name,
                    ColumnPairs = [new(parent.Columns[0].Id, child.Columns[childColumn].Id)],
                };

            var diagram = new ErDiagram
            {
                TargetDbms = provider.Name,
                Entities = { parent, qualified, child },
                Relationships = { Link(1, Name('f')), Link(2, null), Link(3, null) },
            };

            // live は同じ表で、子の主キーが別の列・外部キーと一意制約が無い（主キーの変更・外部キーと一意制約の追加になる）
            var liveEntities = diagram
                .Entities.Select(entity => entity.Clone(preserveId: true))
                .ToList();
            var liveChild = liveEntities.Single(entity => entity.Id == child.Id);
            liveChild.UniqueConstraints.Clear();
            liveChild.Columns[0].IsPrimaryKey = false;
            liveChild.Columns[2].IsPrimaryKey = true;
            var live = new ErDiagram { TargetDbms = provider.Name, Entities = liveEntities };

            var fkNames = ConstraintNames.ResolveForeignKeyNames(
                diagram.Entities,
                diagram.Relationships,
                limit
            );
            var expected = new List<string>
            {
                parent.TableName,
                Name('s'),
                Name('t'),
                child.TableName,
                Name('x'),
                Name('k'),
                Name('u'),
                Name('f'),
                ConstraintNames.PrimaryKey(parent.TableName, limit),
                ConstraintNames.PrimaryKey(qualified.TableName, limit),
                ConstraintNames.PrimaryKey(child.TableName, limit),
                UniqueConstraint.SynthesizeName(child.TableName, ["ship_id", "bill_id"], limit),
                fkNames[diagram.Relationships[1]],
                fkNames[diagram.Relationships[2]],
            };

            if (provider.Name == "sqlite")
            {
                // 再構築の一時名（SQLite は上限が無いので、出力に現れることだけを確かめる）
                expected.Add(child.TableName + "_quicker_rebuild");
            }

            return new Shape(diagram, live, expected);
        }
    }
}
